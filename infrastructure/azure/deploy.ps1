# Azure Container Apps deployment script for PoshMcp (PowerShell version)
# This script handles the complete deployment workflow including:
# - Prerequisites validation
# - Container image build and push
# - Infrastructure deployment via Bicep
# - Post-deployment verification

<#
.SYNOPSIS
Deploys PoshMcp to Azure Container Apps.

.DESCRIPTION
Builds (or imports) a container image, pushes it to ACR, and deploys infrastructure
using Bicep. Deployment values can come from CLI arguments, environment variables,
or an appsettings-style JSON file.

Precedence for deployment settings is:
1) Explicit CLI parameters
2) Environment variables
3) App settings file values
4) Script defaults

.PARAMETER AppSettingsFile
Optional path to an appsettings JSON file that includes an AzureDeployment section.
You can also set DEPLOY_APPSETTINGS_FILE. CLI path overrides the environment value.

.PARAMETER ServerAppSettingsFile
Optional path to the MCP server's own appsettings.json (e.g. PoshMcp.Server/appsettings.json).
You can also set POSHMCP_APPSETTINGS_FILE. Settings in this file are translated to Container App
environment variables and merged with the hardcoded variables defined in resources.bicep.  If not
provided, the script auto-discovers 'appsettings.json' or 'poshmcp.appsettings.json' in the same
directory as deploy.ps1.

.EXAMPLE
./deploy.ps1 -RegistryName myregistry

.EXAMPLE
./deploy.ps1 -AppSettingsFile ./deploy.appsettings.json -RegistryName myregistry

.EXAMPLE
$env:DEPLOY_APPSETTINGS_FILE = './deploy.appsettings.json'
$env:REGISTRY_NAME = 'myregistry'
./deploy.ps1
#>

[CmdletBinding()]
param(
    [Parameter()]
    [string]$ResourceGroup,
    
    [Parameter()]
    [string]$Location,
    
    [Parameter()]
    [string]$ContainerAppName,
    
    [Parameter()]
    [string]$RegistryName,
    
    [Parameter()]
    [string]$ImageTag,
    
    [Parameter()]
    [string]$Subscription,
    
    [Parameter()]
    [string]$TenantId,

    [Parameter()]
    [string]$SourceImage,

    [Parameter()]
    [switch]$UseRegistryCache,

    [Parameter()]
    [string]$AppSettingsFile,

    [Parameter()]
    [string]$ServerAppSettingsFile
)

$ErrorActionPreference = 'Stop'
# Enable information stream by default for user-facing messages
$InformationPreference = 'Continue'

# Preserve script-level bound parameters so nested helper functions can reliably
# evaluate whether a value was explicitly provided via CLI.
$script:InvocationBoundParameters = @{}
foreach ($entry in $PSBoundParameters.GetEnumerator()) {
    $script:InvocationBoundParameters[$entry.Key] = $entry.Value
}

# Get script directory
$ScriptDir = $PSScriptRoot
$ProjectRoot = Join-Path $ScriptDir '..' '..' | Resolve-Path
$BicepFile = Join-Path $ScriptDir 'main.bicep'
$ParametersFile = Join-Path $ScriptDir 'parameters.json'

function Get-TrimmedString {
    param(
        [Parameter()]
        [object]$Value
    )

    if ($null -eq $Value) {
        return $null
    }

    $stringValue = [string]$Value
    if ([string]::IsNullOrWhiteSpace($stringValue)) {
        return $null
    }

    return $stringValue.Trim()
}

function ConvertTo-NullableBoolean {
    param(
        [Parameter()]
        [object]$Value,

        [Parameter(Mandatory)]
        [string]$Context
    )

    if ($null -eq $Value) {
        return $null
    }

    if ($Value -is [bool]) {
        return [bool]$Value
    }

    if ($Value -is [System.Management.Automation.SwitchParameter]) {
        return [bool]$Value.IsPresent
    }

    if ($Value -is [int] -or $Value -is [long]) {
        if ([int64]$Value -eq 1) { return $true }
        if ([int64]$Value -eq 0) { return $false }
    }

    $normalized = [string]$Value
    if ([string]::IsNullOrWhiteSpace($normalized)) {
        return $null
    }

    switch -Regex ($normalized.Trim().ToLowerInvariant()) {
        '^(true|1|yes|y|on)$' { return $true }
        '^(false|0|no|n|off)$' { return $false }
    }

    Write-Error "Invalid boolean value '$Value' for $Context. Allowed values: true/false, 1/0, yes/no, on/off." -Category InvalidArgument -ErrorAction Stop
}

function Get-AppSettingsDeploymentSection {
    param(
        [Parameter()]
        [string]$ResolvedAppSettingsFile
    )

    if ([string]::IsNullOrWhiteSpace($ResolvedAppSettingsFile)) {
        return @{}
    }

    if (-not (Test-Path -Path $ResolvedAppSettingsFile)) {
        Write-Error "App settings file '$ResolvedAppSettingsFile' was not found." -Category ObjectNotFound -ErrorAction Stop
    }

    Write-Information "Loading deployment settings from appsettings file: $ResolvedAppSettingsFile" -Tags 'Status'

    try {
        $root = Get-Content -Path $ResolvedAppSettingsFile -Raw | ConvertFrom-Json -Depth 50 -AsHashtable
    }
    catch {
        Write-Error "Failed to parse appsettings file '$ResolvedAppSettingsFile': $($_.Exception.Message)" -Category InvalidData -ErrorAction Stop
    }

    if (-not ($root -is [hashtable])) {
        return @{}
    }

    if ($root.ContainsKey('AzureDeployment') -and $root.AzureDeployment -is [hashtable]) {
        return $root.AzureDeployment
    }

    if ($root.ContainsKey('Deployment') -and $root.Deployment -is [hashtable]) {
        $deployment = $root.Deployment
        if ($deployment.ContainsKey('Azure') -and $deployment.Azure -is [hashtable]) {
            return $deployment.Azure
        }
    }

    return @{}
}

function Resolve-StringSetting {
    param(
        [Parameter(Mandatory)]
        [string]$SettingName,

        [Parameter(Mandatory)]
        [bool]$CliProvided,

        [Parameter()]
        [string]$CliValue,

        [Parameter()]
        [string]$EnvironmentVariableName,

        [Parameter()]
        [hashtable]$AppSettingsSection,

        [Parameter()]
        [string]$AppSettingsKey,

        [Parameter()]
        [string]$DefaultValue,

        [Parameter()]
        [switch]$Required
    )

    $cliResolved = Get-TrimmedString -Value $CliValue
    if ($CliProvided -and $cliResolved) {
        return @{ Value = $cliResolved; Source = 'cli' }
    }

    if ($EnvironmentVariableName) {
        $environmentValue = Get-TrimmedString -Value ([Environment]::GetEnvironmentVariable($EnvironmentVariableName))
        if ($environmentValue) {
            return @{ Value = $environmentValue; Source = "env:$EnvironmentVariableName" }
        }
    }

    if ($AppSettingsSection -and $AppSettingsKey -and $AppSettingsSection.ContainsKey($AppSettingsKey)) {
        $appSettingsValue = Get-TrimmedString -Value $AppSettingsSection[$AppSettingsKey]
        if ($appSettingsValue) {
            return @{ Value = $appSettingsValue; Source = "appsettings:$AppSettingsKey" }
        }
    }

    $defaultResolved = Get-TrimmedString -Value $DefaultValue
    if ($defaultResolved) {
        return @{ Value = $defaultResolved; Source = 'default' }
    }

    if ($Required) {
        Write-Error "Required setting '$SettingName' was not provided. Set it via CLI, environment variable, or appsettings file." -Category InvalidArgument -ErrorAction Stop
    }

    return @{ Value = $null; Source = 'unset' }
}

function Resolve-BooleanSetting {
    param(
        [Parameter(Mandatory)]
        [string]$SettingName,

        [Parameter(Mandatory)]
        [bool]$CliProvided,

        [Parameter()]
        [object]$CliValue,

        [Parameter()]
        [string]$EnvironmentVariableName,

        [Parameter()]
        [hashtable]$AppSettingsSection,

        [Parameter()]
        [string]$AppSettingsKey,

        [Parameter(Mandatory)]
        [bool]$DefaultValue
    )

    if ($CliProvided) {
        $value = ConvertTo-NullableBoolean -Value $CliValue -Context "CLI parameter $SettingName"
        return @{ Value = [bool]$value; Source = 'cli' }
    }

    if ($EnvironmentVariableName) {
        $environmentRaw = Get-TrimmedString -Value ([Environment]::GetEnvironmentVariable($EnvironmentVariableName))
        if ($environmentRaw) {
            $value = ConvertTo-NullableBoolean -Value $environmentRaw -Context "environment variable $EnvironmentVariableName"
            return @{ Value = [bool]$value; Source = "env:$EnvironmentVariableName" }
        }
    }

    if ($AppSettingsSection -and $AppSettingsKey -and $AppSettingsSection.ContainsKey($AppSettingsKey)) {
        $value = ConvertTo-NullableBoolean -Value $AppSettingsSection[$AppSettingsKey] -Context "appsettings key $AppSettingsKey"
        if ($null -ne $value) {
            return @{ Value = [bool]$value; Source = "appsettings:$AppSettingsKey" }
        }
    }

    return @{ Value = $DefaultValue; Source = 'default' }
}

# Translate an MCP server appsettings.json into Container App environment variable objects.
# Skips Logging, McpResources, and any keys believed to hold secrets or file paths.
# Returns an array of @{ name = '...'; value = '...' } hashtables.
function ConvertTo-McpServerEnvVars {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath
    )

    $envVars = [System.Collections.Generic.List[hashtable]]::new()

    try {
        $root = Get-Content -Path $FilePath -Raw | ConvertFrom-Json -Depth 50 -AsHashtable
    }
    catch {
        Write-Error "Failed to parse MCP server appsettings file '$FilePath': $($_.Exception.Message)" -Category InvalidData -ErrorAction Stop
    }

    $psConfig = $root['PowerShellConfiguration']
    if ($psConfig -is [hashtable]) {
        # RuntimeMode → canonical POSHMCP_RUNTIME_MODE
        if ($psConfig.ContainsKey('RuntimeMode')) {
            $rawMode = [string]$psConfig['RuntimeMode']
            $normalized = $rawMode.ToLowerInvariant() -replace '[-_]', ''
            $envMode = switch ($normalized) {
                'outofprocess' { 'OutOfProcess' }
                'inprocess'    { 'InProcess' }
                default        { $rawMode }
            }
            $envVars.Add(@{ name = 'POSHMCP_RUNTIME_MODE'; value = $envMode })
        }

        # SessionMode → canonical POSHMCP_SESSION_MODE
        if ($psConfig.ContainsKey('SessionMode')) {
            $envVars.Add(@{ name = 'POSHMCP_SESSION_MODE'; value = [string]$psConfig['SessionMode'] })
        }

        # CommandNames[] → PowerShellConfiguration__CommandNames__0, __1, …
        $cmdNames = $psConfig['CommandNames']
        if ($cmdNames -is [System.Collections.IList]) {
            for ($i = 0; $i -lt $cmdNames.Count; $i++) {
                $envVars.Add(@{ name = "PowerShellConfiguration__CommandNames__$i"; value = [string]$cmdNames[$i] })
            }
        }

        # Modules[] → PowerShellConfiguration__Modules__0, __1, …
        $modules = $psConfig['Modules']
        if ($modules -is [System.Collections.IList]) {
            for ($i = 0; $i -lt $modules.Count; $i++) {
                $envVars.Add(@{ name = "PowerShellConfiguration__Modules__$i"; value = [string]$modules[$i] })
            }
        }

        # IncludePatterns[] → PowerShellConfiguration__IncludePatterns__0, …
        $includePatterns = $psConfig['IncludePatterns']
        if ($includePatterns -is [System.Collections.IList]) {
            for ($i = 0; $i -lt $includePatterns.Count; $i++) {
                $envVars.Add(@{ name = "PowerShellConfiguration__IncludePatterns__$i"; value = [string]$includePatterns[$i] })
            }
        }

        # ExcludePatterns[] → PowerShellConfiguration__ExcludePatterns__0, …
        $excludePatterns = $psConfig['ExcludePatterns']
        if ($excludePatterns -is [System.Collections.IList]) {
            for ($i = 0; $i -lt $excludePatterns.Count; $i++) {
                $envVars.Add(@{ name = "PowerShellConfiguration__ExcludePatterns__$i"; value = [string]$excludePatterns[$i] })
            }
        }

        # EnableDynamicReloadTools (scalar bool)
        if ($psConfig.ContainsKey('EnableDynamicReloadTools')) {
            $envVars.Add(@{ name = 'PowerShellConfiguration__EnableDynamicReloadTools'; value = ([string]$psConfig['EnableDynamicReloadTools']).ToLowerInvariant() })
        }

        # EnableConfigurationTroubleshootingTool (scalar bool)
        if ($psConfig.ContainsKey('EnableConfigurationTroubleshootingTool')) {
            $envVars.Add(@{ name = 'PowerShellConfiguration__EnableConfigurationTroubleshootingTool'; value = ([string]$psConfig['EnableConfigurationTroubleshootingTool']).ToLowerInvariant() })
        }

        # Performance sub-section
        $perf = $psConfig['Performance']
        if ($perf -is [hashtable]) {
            if ($perf.ContainsKey('EnableResultCaching')) {
                $envVars.Add(@{ name = 'PowerShellConfiguration__Performance__EnableResultCaching'; value = ([string]$perf['EnableResultCaching']).ToLowerInvariant() })
            }
            if ($perf.ContainsKey('UseDefaultDisplayProperties')) {
                $envVars.Add(@{ name = 'PowerShellConfiguration__Performance__UseDefaultDisplayProperties'; value = ([string]$perf['UseDefaultDisplayProperties']).ToLowerInvariant() })
            }
        }
    }

    $authConfig = $root['Authentication']
    if ($authConfig -is [hashtable] -and $authConfig.ContainsKey('Enabled')) {
        $envVars.Add(@{ name = 'Authentication__Enabled'; value = ([string]$authConfig['Enabled']).ToLowerInvariant() })
    }

    $loggingConfig = $root['Logging']
    if ($loggingConfig -is [hashtable]) {
        $logLevelConfig = $loggingConfig['LogLevel']
        if ($logLevelConfig -is [hashtable] -and $logLevelConfig.ContainsKey('Default')) {
            $envVars.Add(@{ name = 'Logging__LogLevel__Default'; value = [string]$logLevelConfig['Default'] })
        }
    }

    return $envVars.ToArray()
}

# Resolve the MCP server appsettings file path, performing auto-discovery when not provided.
function Resolve-McpAppSettingsFile {
    param(
        [Parameter()]
        [string]$CliValue
    )

    $cliResolved = Get-TrimmedString -Value $CliValue
    if (-not $cliResolved) {
        $cliResolved = Get-TrimmedString -Value ([Environment]::GetEnvironmentVariable('POSHMCP_APPSETTINGS_FILE'))
    }

    if ($cliResolved) {
        if (-not (Test-Path -Path $cliResolved)) {
            Write-Error "ServerAppSettingsFile '$cliResolved' was not found." -Category ObjectNotFound -ErrorAction Stop
        }
        return (Resolve-Path -Path $cliResolved).Path
    }

    # Auto-discover: look in the script directory for known file names
    foreach ($candidate in @('poshmcp.appsettings.json', 'appsettings.json')) {
        $candidatePath = Join-Path $ScriptDir $candidate
        if (Test-Path -Path $candidatePath) {
            Write-Information "Auto-discovered MCP server appsettings: $candidatePath" -Tags 'Status'
            return $candidatePath
        }
    }

    return $null
}

function Initialize-DeploymentConfiguration {
    $appSettingsFileFromEnv = Get-TrimmedString -Value ([Environment]::GetEnvironmentVariable('DEPLOY_APPSETTINGS_FILE'))
    $resolvedAppSettingsFile = Get-TrimmedString -Value $AppSettingsFile
    if (-not $resolvedAppSettingsFile) {
        $resolvedAppSettingsFile = $appSettingsFileFromEnv
    }

    if ($resolvedAppSettingsFile) {
        try {
            $resolvedAppSettingsFile = (Resolve-Path -Path $resolvedAppSettingsFile -ErrorAction Stop).Path
        }
        catch {
            Write-Error "App settings file '$resolvedAppSettingsFile' was not found." -Category ObjectNotFound -ErrorAction Stop
        }
    }

    $deploymentSettings = Get-AppSettingsDeploymentSection -ResolvedAppSettingsFile $resolvedAppSettingsFile

    $resourceGroupSetting = Resolve-StringSetting -SettingName 'ResourceGroup' -CliProvided $script:InvocationBoundParameters.ContainsKey('ResourceGroup') -CliValue $ResourceGroup -EnvironmentVariableName 'RESOURCE_GROUP' -AppSettingsSection $deploymentSettings -AppSettingsKey 'ResourceGroup' -DefaultValue 'rg-poshmcp'
    $locationSetting = Resolve-StringSetting -SettingName 'Location' -CliProvided $script:InvocationBoundParameters.ContainsKey('Location') -CliValue $Location -EnvironmentVariableName 'LOCATION' -AppSettingsSection $deploymentSettings -AppSettingsKey 'Location' -DefaultValue 'eastus'
    $containerAppNameSetting = Resolve-StringSetting -SettingName 'ContainerAppName' -CliProvided $script:InvocationBoundParameters.ContainsKey('ContainerAppName') -CliValue $ContainerAppName -EnvironmentVariableName 'CONTAINER_APP_NAME' -AppSettingsSection $deploymentSettings -AppSettingsKey 'ContainerAppName' -DefaultValue 'poshmcp'
    $registryNameSetting = Resolve-StringSetting -SettingName 'RegistryName' -CliProvided $script:InvocationBoundParameters.ContainsKey('RegistryName') -CliValue $RegistryName -EnvironmentVariableName 'REGISTRY_NAME' -AppSettingsSection $deploymentSettings -AppSettingsKey 'RegistryName' -Required
    $imageTagSetting = Resolve-StringSetting -SettingName 'ImageTag' -CliProvided $script:InvocationBoundParameters.ContainsKey('ImageTag') -CliValue $ImageTag -EnvironmentVariableName 'IMAGE_TAG' -AppSettingsSection $deploymentSettings -AppSettingsKey 'ImageTag' -DefaultValue (Get-Date -Format 'yyyyMMdd-HHmmss')
    $subscriptionSetting = Resolve-StringSetting -SettingName 'Subscription' -CliProvided $script:InvocationBoundParameters.ContainsKey('Subscription') -CliValue $Subscription -EnvironmentVariableName 'AZURE_SUBSCRIPTION' -AppSettingsSection $deploymentSettings -AppSettingsKey 'Subscription'
    $tenantIdSetting = Resolve-StringSetting -SettingName 'TenantId' -CliProvided $script:InvocationBoundParameters.ContainsKey('TenantId') -CliValue $TenantId -EnvironmentVariableName 'AZURE_TENANT_ID' -AppSettingsSection $deploymentSettings -AppSettingsKey 'TenantId'
    $sourceImageSetting = Resolve-StringSetting -SettingName 'SourceImage' -CliProvided $script:InvocationBoundParameters.ContainsKey('SourceImage') -CliValue $SourceImage -EnvironmentVariableName 'SOURCE_IMAGE' -AppSettingsSection $deploymentSettings -AppSettingsKey 'SourceImage'
    $useRegistryCacheSetting = Resolve-BooleanSetting -SettingName 'UseRegistryCache' -CliProvided $script:InvocationBoundParameters.ContainsKey('UseRegistryCache') -CliValue $UseRegistryCache -EnvironmentVariableName 'USE_REGISTRY_CACHE' -AppSettingsSection $deploymentSettings -AppSettingsKey 'UseRegistryCache' -DefaultValue $false

    $script:ResourceGroup = $resourceGroupSetting.Value
    $script:Location = $locationSetting.Value
    $script:ContainerAppName = $containerAppNameSetting.Value
    $script:RegistryName = $registryNameSetting.Value
    $script:ImageTag = $imageTagSetting.Value
    $script:Subscription = $subscriptionSetting.Value
    $script:TenantId = $tenantIdSetting.Value
    $script:SourceImage = $sourceImageSetting.Value
    $script:UseRegistryCache = [bool]$useRegistryCacheSetting.Value

    Write-Information "Resolved deployment setting sources:" -Tags 'Status'
    Write-Information "  ResourceGroup: $($resourceGroupSetting.Source)" -Tags 'Status'
    Write-Information "  Location: $($locationSetting.Source)" -Tags 'Status'
    Write-Information "  ContainerAppName: $($containerAppNameSetting.Source)" -Tags 'Status'
    Write-Information "  RegistryName: $($registryNameSetting.Source)" -Tags 'Status'
    Write-Information "  ImageTag: $($imageTagSetting.Source)" -Tags 'Status'
    Write-Information "  Subscription: $($subscriptionSetting.Source)" -Tags 'Status'
    Write-Information "  TenantId: $($tenantIdSetting.Source)" -Tags 'Status'
    Write-Information "  SourceImage: $($sourceImageSetting.Source)" -Tags 'Status'
    Write-Information "  UseRegistryCache: $($useRegistryCacheSetting.Source)" -Tags 'Status'

    # Resolve MCP server appsettings and derive server env vars
    $resolvedMcpAppSettingsFile = Resolve-McpAppSettingsFile -CliValue $ServerAppSettingsFile
    if ($resolvedMcpAppSettingsFile) {
        Write-Information "Loading MCP server settings from: $resolvedMcpAppSettingsFile" -Tags 'Status'
        $script:ServerEnvVars = ConvertTo-McpServerEnvVars -FilePath $resolvedMcpAppSettingsFile
        Write-Information "  MCP server env vars derived: $($script:ServerEnvVars.Count)" -Tags 'Status'
        foreach ($ev in $script:ServerEnvVars) {
            Write-Verbose "    $($ev.name) = $($ev.value)"
        }
    }
    else {
        $script:ServerEnvVars = @()
    }
}

# Retry configuration for transient ACR auth/network issues.
$script:LoginMaxAttempts = 4
$script:PushMaxAttempts = 4
$script:InitialRetryDelaySeconds = 2
$script:MaxRetryDelaySeconds = 20

function Get-RetryDelaySeconds {
    param(
        [Parameter(Mandatory)]
        [int]$Attempt
    )

    $delay = [Math]::Min($script:MaxRetryDelaySeconds, $script:InitialRetryDelaySeconds * [Math]::Pow(2, $Attempt - 1))
    return [int]$delay
}

function Get-CommandOutputSnippet {
    param(
        [Parameter()]
        [string]$Output
    )

    if (-not $Output) {
        return ''
    }

    return (($Output -split "`r?`n") |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -First 12) -join [Environment]::NewLine
}

function Test-IsTransientNetworkError {
    param(
        [Parameter()]
        [string]$Message
    )

    if (-not $Message) {
        return $false
    }

    $patterns = @(
        'eof',
        'i/o timeout',
        'timed out',
        'timeout',
        'connection reset',
        'connection aborted',
        'temporary failure',
        'temporarily unavailable',
        'tls handshake timeout',
        'context deadline exceeded',
        '503 service unavailable',
        '429 too many requests'
    )

    foreach ($pattern in $patterns) {
        if ($Message -match $pattern) {
            return $true
        }
    }

    return $false
}

function Test-IsAuthenticationError {
    param(
        [Parameter()]
        [string]$Message
    )

    if (-not $Message) {
        return $false
    }

    $patterns = @(
        'unauthorized',
        'authentication required',
        'invalid username',
        'invalid password',
        'denied',
        'insufficient scope',
        'forbidden',
        '401'
    )

    foreach ($pattern in $patterns) {
        if ($Message -match $pattern) {
            return $true
        }
    }

    return $false
}

function Test-AcrReachability {
    Write-Information "Checking ACR reachability: https://$RegistryServer/v2/" -Tags 'Status'

    try {
        $null = Invoke-WebRequest -Uri "https://$RegistryServer/v2/" -Method Head -TimeoutSec 15 -UseBasicParsing -ErrorAction Stop
        Write-Information "✓ ACR endpoint reachable" -Tags 'Success'
    }
    catch {
        $statusCode = $null
        if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
            $statusCode = [int]$_.Exception.Response.StatusCode
        }

        if ($statusCode -eq 401 -or $statusCode -eq 403) {
            Write-Information "✓ ACR endpoint reachable (authentication challenge received: $statusCode)" -Tags 'Success'
            return
        }

        $message = $_.Exception.Message
        if (Test-IsTransientNetworkError -Message $message) {
            Write-Warning "Transient network issue detected while probing ACR endpoint: $message"
        }
        else {
            Write-Warning "ACR reachability probe failed: $message"
        }
    }
}

function Invoke-AcrLoginWithRetry {
    for ($attempt = 1; $attempt -le $script:LoginMaxAttempts; $attempt++) {
        Write-Information "Logging in to Azure Container Registry (attempt $attempt/$($script:LoginMaxAttempts))..." -Tags 'Status'
        Write-Verbose "Executing: az acr login --name $RegistryName"

        $commandOutput = (az acr login --name $RegistryName 2>&1 | Out-String)
        if ($LASTEXITCODE -eq 0) {
            Write-Information "✓ ACR login succeeded" -Tags 'Success'
            return
        }

        $snippet = Get-CommandOutputSnippet -Output $commandOutput
        $isTransient = Test-IsTransientNetworkError -Message $commandOutput
        $isAuthError = Test-IsAuthenticationError -Message $commandOutput

        if ($isTransient -and $attempt -lt $script:LoginMaxAttempts) {
            $delay = Get-RetryDelaySeconds -Attempt $attempt
            Write-Warning "ACR login failed with a transient network error. Retrying in $delay seconds..."
            if ($snippet) {
                Write-Warning "ACR login output: $snippet"
            }
            Start-Sleep -Seconds $delay
            continue
        }

        if ($commandOutput -match 'oauth2/token' -and $commandOutput -match 'eof') {
            Write-Error "ACR login failed while requesting OAuth token endpoint (likely transient network interruption). Output: $snippet" -Category ResourceUnavailable -ErrorAction Stop
        }

        if ($isAuthError -and -not $isTransient) {
            Write-Error "ACR login failed due to authentication/authorization. Verify Azure login, tenant/subscription context, and ACR permissions. Output: $snippet" -Category AuthenticationError -ErrorAction Stop
        }

        Write-Error "ACR login failed after $attempt attempt(s). Output: $snippet" -Category InvalidOperation -ErrorAction Stop
    }
}

function Invoke-DockerPullWithRetry {
    param(
        [Parameter(Mandatory)]
        [string]$ImageName
    )

    for ($attempt = 1; $attempt -le $script:PushMaxAttempts; $attempt++) {
        Write-Information "Pulling image (attempt $attempt/$($script:PushMaxAttempts)): $ImageName" -Tags 'Status'
        Write-Verbose "Executing: docker pull $ImageName"

        $commandOutput = (docker pull $ImageName 2>&1 | Out-String)
        if ($LASTEXITCODE -eq 0) {
            Write-Information "✓ Pull succeeded: $ImageName" -Tags 'Success'
            return
        }

        $snippet = Get-CommandOutputSnippet -Output $commandOutput
        $isTransient = Test-IsTransientNetworkError -Message $commandOutput
        $isAuthError = Test-IsAuthenticationError -Message $commandOutput

        if ($isTransient -and $attempt -lt $script:PushMaxAttempts) {
            $delay = Get-RetryDelaySeconds -Attempt $attempt
            Write-Warning "Docker pull failed with a transient network error for $ImageName. Retrying in $delay seconds..."
            if ($snippet) {
                Write-Warning "Docker pull output: $snippet"
            }
            Start-Sleep -Seconds $delay
            continue
        }

        if ($isAuthError -and -not $isTransient) {
            Write-Error "Docker pull failed due to authentication/authorization for $ImageName. Verify you are logged in to the source registry. Run 'docker login <registry>' if needed. Output: $snippet" -Category AuthenticationError -ErrorAction Stop
        }

        Write-Error "Docker pull failed for '$ImageName' after $attempt attempt(s). Verify the image exists and is accessible. Output: $snippet" -Category InvalidOperation -ErrorAction Stop
    }
}

function Invoke-AcrImportWithRetry {
    param(
        [Parameter(Mandatory)]
        [string]$SourceImageRef,

        [Parameter(Mandatory)]
        [string]$TargetTag
    )

    for ($attempt = 1; $attempt -le $script:PushMaxAttempts; $attempt++) {
        Write-Information "Importing image into ACR (attempt $attempt/$($script:PushMaxAttempts)): $SourceImageRef -> $TargetTag" -Tags 'Status'
        Write-Verbose "Executing: az acr import --registry $RegistryName --source $SourceImageRef --image $TargetTag"

        $commandOutput = (az acr import --registry $RegistryName --source $SourceImageRef --image $TargetTag 2>&1 | Out-String)
        if ($LASTEXITCODE -eq 0) {
            Write-Information "✓ ACR import succeeded: $TargetTag" -Tags 'Success'
            return
        }

        $snippet = Get-CommandOutputSnippet -Output $commandOutput
        $isTransient = Test-IsTransientNetworkError -Message $commandOutput

        if ($isTransient -and $attempt -lt $script:PushMaxAttempts) {
            $delay = Get-RetryDelaySeconds -Attempt $attempt
            Write-Warning "ACR import failed with a transient network error. Retrying in $delay seconds..."
            if ($snippet) {
                Write-Warning "ACR import output: $snippet"
            }
            Start-Sleep -Seconds $delay
            continue
        }

        Write-Error "ACR import failed for source image '$SourceImageRef' after $attempt attempt(s). Verify the source registry is accessible and the image exists. Output: $snippet" -Category InvalidOperation -ErrorAction Stop
    }
}

function Invoke-DockerPushWithRetry {
    param(
        [Parameter(Mandatory)]
        [string]$ImageName
    )

    for ($attempt = 1; $attempt -le $script:PushMaxAttempts; $attempt++) {
        Write-Information "Pushing image (attempt $attempt/$($script:PushMaxAttempts)): $ImageName" -Tags 'Status'
        Write-Verbose "Executing: docker push $ImageName"

        $commandOutput = (docker push $ImageName 2>&1 | Out-String)
        if ($LASTEXITCODE -eq 0) {
            Write-Information "✓ Push succeeded: $ImageName" -Tags 'Success'
            return
        }

        $snippet = Get-CommandOutputSnippet -Output $commandOutput
        $isTransient = Test-IsTransientNetworkError -Message $commandOutput
        $isAuthError = Test-IsAuthenticationError -Message $commandOutput

        if ($isTransient -and $attempt -lt $script:PushMaxAttempts) {
            $delay = Get-RetryDelaySeconds -Attempt $attempt
            Write-Warning "Docker push failed with a transient network error for $ImageName. Retrying in $delay seconds..."
            if ($snippet) {
                Write-Warning "Docker push output: $snippet"
            }
            Start-Sleep -Seconds $delay
            continue
        }

        if ($isAuthError -and -not $isTransient) {
            Write-Error "Docker push failed due to authentication/authorization for $ImageName. Verify ACR login and repository permissions. Output: $snippet" -Category AuthenticationError -ErrorAction Stop
        }

        Write-Error "Docker push failed for $ImageName after $attempt attempt(s). Output: $snippet" -Category InvalidOperation -ErrorAction Stop
    }
}

# Check prerequisites
function Test-Prerequisites {
    Write-Information "Checking prerequisites..." -Tags 'Status'
    Write-Verbose "Validating Azure CLI, Docker, and Azure authentication"
    
    # Check Azure CLI
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        Write-Error "Azure CLI not found. Please install: https://docs.microsoft.com/en-us/cli/azure/install-azure-cli" -Category NotInstalled -ErrorAction Stop
    }
    
    # Check Docker
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        Write-Error "Docker not found. Please install: https://docs.docker.com/get-docker/" -Category NotInstalled -ErrorAction Stop
    }
    
    # Check if logged in to Azure
    Write-Verbose "Verifying Azure authentication status"
    $null = az account show 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Not logged in to Azure. Please run: az login" -Category AuthenticationError -ErrorAction Stop
    }
    
    Write-Information "✓ All prerequisites met" -Tags 'Success'
}

# Validate and set Azure tenant
function Set-AzureTenant {
    Write-Verbose "Validating Azure tenant access"
    
    # Get current tenant
    $currentTenantId = az account show --query tenantId -o tsv
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to get current tenant" -Category InvalidOperation -ErrorAction Stop
    }
    
    Write-Information "Current tenant: $currentTenantId" -Tags 'Status'
    
    # If TenantId specified, validate and switch if needed
    if ($TenantId) {
        Write-Information "Target tenant: $TenantId" -Tags 'Status'
        
        if ($currentTenantId -ne $TenantId) {
            Write-Information "Switching to tenant: $TenantId" -Tags 'Status'
            Write-Verbose "Executing: az login --tenant $TenantId"
            
            # Attempt login to specified tenant
            $null = az login --tenant $TenantId 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-Error "Failed to login to tenant $TenantId. Verify you have access to this tenant." -Category AuthenticationError -ErrorAction Stop
            }
            
            # Verify we're now in the correct tenant
            $newTenantId = az account show --query tenantId -o tsv
            if ($newTenantId -ne $TenantId) {
                Write-Error "Failed to switch to tenant $TenantId (current: $newTenantId)" -Category InvalidOperation -ErrorAction Stop
            }
            
            Write-Information "✓ Successfully switched to tenant: $TenantId" -Tags 'Success'
        }
        else {
            Write-Information "Already in target tenant" -Tags 'Status'
        }
    }
    
    # Store the active tenant ID for use in commands
    $script:ActiveTenantId = az account show --query tenantId -o tsv
    Write-Information "Using tenant: $script:ActiveTenantId" -Tags 'Status'
}

# Set Azure subscription
function Set-AzureSubscription {
    if ($Subscription) {
        Write-Information "Setting subscription to: $Subscription" -Tags 'Status'
        Write-Verbose "Executing: az account set --subscription $Subscription"
        az account set --subscription $Subscription
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to set subscription" -Category InvalidOperation -ErrorAction Stop
        }
        
        # Validate subscription belongs to current tenant
        $subTenantId = az account show --query tenantId -o tsv
        if ($subTenantId -ne $script:ActiveTenantId) {
            Write-Error "Subscription '$Subscription' belongs to tenant $subTenantId, but currently logged into tenant $script:ActiveTenantId. Tenant mismatch detected." -Category InvalidOperation -ErrorAction Stop
        }
    }
    
    $currentSub = (az account show --query name -o tsv)
    $currentSubId = (az account show --query id -o tsv)
    Write-Information "Using subscription: $currentSub ($currentSubId)" -Tags 'Status'
}

# Ensure the resource group exists before creating resources that depend on it (e.g., ACR).
# The Bicep template also declares the resource group at subscription scope, which is
# idempotent — creating a resource group that already exists is a safe no-op in Azure.
function New-ResourceGroupIfNeeded {
    Write-Information "Checking resource group: $ResourceGroup" -Tags 'Status'
    Write-Verbose "Executing: az group show --name $ResourceGroup"

    $null = az group show --name $ResourceGroup 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Information "Creating resource group: $ResourceGroup in $Location" -Tags 'Status'
        Write-Verbose "Executing: az group create --name $ResourceGroup --location $Location"
        az group create --name $ResourceGroup --location $Location
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to create resource group" -Category InvalidOperation -ErrorAction Stop
        }
        Write-Information "✓ Resource group created" -Tags 'Success'
    }
    else {
        Write-Information "Resource group already exists" -Tags 'Status'
    }
}

# Create or get Azure Container Registry
function Initialize-ContainerRegistry {
    Write-Information "Checking Azure Container Registry: $RegistryName" -Tags 'Status'
    Write-Verbose "Executing: az acr show --name $RegistryName --resource-group $ResourceGroup"
    
    $null = az acr show --name $RegistryName --resource-group $ResourceGroup 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Information "Creating Azure Container Registry: $RegistryName" -Tags 'Status'
        Write-Verbose "Executing: az acr create with Standard SKU and admin enabled"
        az acr create `
            --name $RegistryName `
            --resource-group $ResourceGroup `
            --location $Location `
            --sku Standard `
            --admin-enabled true
        
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to create container registry" -Category InvalidOperation -ErrorAction Stop
        }
        Write-Information "✓ Container registry created" -Tags 'Success'
    }
    else {
        Write-Information "Container registry already exists" -Tags 'Status'
    }
    
    # Set registry server
    $script:RegistryServer = "$RegistryName.azurecr.io"
    Write-Information "Registry server: $RegistryServer" -Tags 'Status'
}

# Pull a pre-built source image, re-tag it for ACR, and push (Mode A)
function Invoke-SourceImagePull {
    Write-Information "Using source image (no local build): $SourceImage" -Tags 'Status'

    Test-AcrReachability
    Invoke-AcrLoginWithRetry

    Invoke-DockerPullWithRetry -ImageName $SourceImage

    $script:FullImageName = "${RegistryServer}/poshmcp:${ImageTag}"
    $latestImage = "${RegistryServer}/poshmcp:latest"

    Write-Information "Re-tagging $SourceImage for ACR..." -Tags 'Status'
    Write-Verbose "Executing: docker tag $SourceImage $script:FullImageName"
    docker tag $SourceImage $script:FullImageName
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to re-tag source image '$SourceImage' as '$script:FullImageName'" -Category InvalidOperation -ErrorAction Stop
    }

    Write-Verbose "Executing: docker tag $SourceImage $latestImage"
    docker tag $SourceImage $latestImage
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to re-tag source image '$SourceImage' as '$latestImage'" -Category InvalidOperation -ErrorAction Stop
    }

    Write-Information "Pushing re-tagged images to registry..." -Tags 'Status'
    Invoke-DockerPushWithRetry -ImageName $script:FullImageName
    Invoke-DockerPushWithRetry -ImageName $latestImage

    Write-Information "✓ Source image pulled, re-tagged, and pushed to: $script:RegistryServer" -Tags 'Success'
}

# Import a pre-built source image directly into ACR via az acr import (Mode B)
function Invoke-SourceImageCache {
    Write-Information "Using ACR import (pull-through cache) for source image: $SourceImage" -Tags 'Status'

    Invoke-AcrLoginWithRetry

    $script:FullImageName = "${RegistryServer}/poshmcp:${ImageTag}"

    Invoke-AcrImportWithRetry -SourceImageRef $SourceImage -TargetTag "poshmcp:${ImageTag}"
    Invoke-AcrImportWithRetry -SourceImageRef $SourceImage -TargetTag "poshmcp:latest"

    Write-Information "✓ Source image imported into ACR: $script:FullImageName" -Tags 'Success'
}

# Build and push container image
function Build-AndPushImage {
    Write-Information "Building container image..." -Tags 'Status'
    
    Push-Location $ProjectRoot
    try {
        # Build image
        $script:FullImageName = "${RegistryServer}/poshmcp:${ImageTag}"
        $latestImage = "${RegistryServer}/poshmcp:latest"
        
        Write-Verbose "Building Docker image: $FullImageName"
        Write-Verbose "Tagging as latest: $latestImage"
        poshmcp build --type base --tag $FullImageName
        if ($LASTEXITCODE -ne 0) {
            Write-Error "poshmcp build failed" -Category InvalidOperation -ErrorAction Stop
        }
        docker tag $FullImageName $latestImage
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Docker tag failed" -Category InvalidOperation -ErrorAction Stop
        }
        
        Write-Information "✓ Image built: $FullImageName" -Tags 'Success'
        
        Test-AcrReachability
        Invoke-AcrLoginWithRetry
        
        # Push image
        Write-Information "Pushing image to registry..." -Tags 'Status'
        Write-Verbose "Pushing: $FullImageName"
        Write-Verbose "Pushing: $latestImage"
        Invoke-DockerPushWithRetry -ImageName $FullImageName
        Invoke-DockerPushWithRetry -ImageName $latestImage
        
        Write-Information "✓ Image pushed to: $RegistryServer" -Tags 'Success'
    }
    finally {
        Pop-Location
    }
}

# Deploy infrastructure using Bicep
function Deploy-Infrastructure {
    Write-Information "Deploying infrastructure with Bicep..." -Tags 'Status'
    
    # Read and update parameters
    Write-Verbose "Reading parameters from: $ParametersFile"
    $params = Get-Content $ParametersFile | ConvertFrom-Json
    $params.parameters.containerImage.value = $FullImageName
    $params.parameters.containerRegistryServer.value = $RegistryServer
    $params.parameters.location.value = $Location

    # Inject server env vars from MCP server appsettings
    $params.parameters | Add-Member -NotePropertyName 'serverEnvVars' -NotePropertyValue ([pscustomobject]@{ value = $script:ServerEnvVars }) -Force
    
    # Save temporary parameters file
    $tempParams = New-TemporaryFile
    Write-Verbose "Writing temporary parameters to: $tempParams"
    $params | ConvertTo-Json -Depth 10 | Set-Content $tempParams
    
    try {
        # Deploy using Bicep at subscription scope
        $deploymentName = "poshmcp-deployment-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        Write-Verbose "Deployment name: $deploymentName"
        
        Write-Verbose "Executing: az deployment sub create"
        $deploymentOutput = az deployment sub create `
            --name $deploymentName `
            --location $Location `
            --template-file $BicepFile `
            --parameters "@$tempParams" `
            --verbose 2>&1
        
        if ($LASTEXITCODE -ne 0) {
            $deploymentErrorDetails = ($deploymentOutput | Out-String).Trim()
            if (-not $deploymentErrorDetails) {
                $deploymentErrorDetails = 'No additional error output was captured.'
            }
            Write-Error "Deployment failed (exit code: $LASTEXITCODE). Details: $deploymentErrorDetails" -Category InvalidOperation -ErrorAction Stop
        }
        
        Write-Information "✓ Infrastructure deployed" -Tags 'Success'
    }
    finally {
        Remove-Item $tempParams -ErrorAction SilentlyContinue
    }
}

# Get deployment outputs
function Get-DeploymentInfo {
    Write-Information "Retrieving deployment information..." -Tags 'Status'
    
    Write-Verbose "Executing: az containerapp show --name $ContainerAppName --resource-group $ResourceGroup"
    $appUrl = az containerapp show `
        --name $ContainerAppName `
        --resource-group $ResourceGroup `
        --query "properties.configuration.ingress.fqdn" `
        -o tsv
    
    if ($appUrl) {
        Write-Information "✓ Application URL: https://$appUrl" -Tags 'Success'
        
        # Test health endpoint
        Write-Information "Testing health endpoint..." -Tags 'Status'
        $healthUrl = "https://$appUrl/health/ready"
        $healthCheckPassed = $false
        $healthCheckAttempts = 6
        $healthCheckDelaySeconds = 10

        for ($attempt = 1; $attempt -le $healthCheckAttempts; $attempt++) {
            try {
                Write-Verbose "Testing ($attempt/$healthCheckAttempts): $healthUrl"
                $response = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 15 -ErrorAction Stop
                if ($response.StatusCode -eq 200) {
                    Write-Information "✓ Health check passed" -Tags 'Success'
                    $healthCheckPassed = $true
                    break
                }

                Write-Warning "Health check attempt $attempt returned status: $($response.StatusCode)"
            }
            catch {
                Write-Warning "Health check attempt $attempt failed: $($_.Exception.Message)"
            }

            if ($attempt -lt $healthCheckAttempts) {
                Start-Sleep -Seconds $healthCheckDelaySeconds
            }
        }

        if (-not $healthCheckPassed) {
            Write-Warning "Health check failed after $healthCheckAttempts attempts. The app may still be starting or not ready."
        }
        
        Write-Host ""
        Write-Information "Deployment Summary:" -Tags 'Status'
        Write-Host "  Application URL: https://$appUrl" -ForegroundColor Cyan
        Write-Host "  Health Check: $healthUrl" -ForegroundColor Cyan
        Write-Host "  Resource Group: $ResourceGroup" -ForegroundColor Cyan
        Write-Host "  Container App: $ContainerAppName" -ForegroundColor Cyan
    }
    else {
        Write-Error "Could not retrieve application URL" -Category ObjectNotFound -ErrorAction Stop
    }
}

# Main deployment workflow
function Invoke-Deployment {
    Write-Information "Starting PoshMcp deployment to Azure Container Apps" -Tags 'Status'
    Write-Host ""

    Initialize-DeploymentConfiguration

    if ($UseRegistryCache -and -not $SourceImage) {
        Write-Error "Parameter -UseRegistryCache requires -SourceImage to be provided" -Category InvalidArgument -ErrorAction Stop
        exit 2
    }

    Test-Prerequisites
    Set-AzureTenant
    Set-AzureSubscription
    New-ResourceGroupIfNeeded
    Initialize-ContainerRegistry

    if ($SourceImage) {
        if ($UseRegistryCache) {
            Invoke-SourceImageCache
        }
        else {
            Invoke-SourceImagePull
        }
    }
    else {
        Build-AndPushImage
    }

    Deploy-Infrastructure
    Get-DeploymentInfo

    Write-Host ""
    Write-Information "✓ Deployment completed successfully!" -Tags 'Success'
}

# Run main deployment
try {
    Invoke-Deployment
}
catch {
    Write-Error "Deployment failed: $_" -Category InvalidOperation
    exit 1
}
