#!/usr/bin/env pwsh
# oop-host.ps1 — Out-of-process PowerShell host for PoshMcp
# Communicates with the .NET MCP server via stdin/stdout ndjson protocol.
#
# IMPORTANT: stdout is ONLY for ndjson responses. All diagnostic output goes to stderr.
#
# Usage: pwsh -NoProfile -NonInteractive -File oop-host.ps1

$ErrorActionPreference = 'Stop'

# Suppress ANSI escape codes — stdout is for ndjson only.
$env:NO_COLOR = '1'
if ($PSStyle) { $PSStyle.OutputRendering = 'PlainText' }

# Common parameters that should be excluded from discovery schemas.
$script:CommonParameters = @(
    'Verbose', 'Debug', 'ErrorAction', 'WarningAction', 'InformationAction',
    'ErrorVariable', 'WarningVariable', 'InformationVariable',
    'OutVariable', 'OutBuffer', 'PipelineVariable', 'ProgressAction',
    'WhatIf', 'Confirm'
)

function Write-Diag {
    <#
    .SYNOPSIS
        Write diagnostic output to stderr (never stdout).
    #>
    param([string]$Message)
    [Console]::Error.WriteLine("[oop-host] $Message")
}

function Write-NdjsonResponse {
    <#
    .SYNOPSIS
        Write a single ndjson response line to stdout.
    #>
    param(
        [Parameter(Mandatory)][string]$Id,
        [object]$Result,
        [object]$ErrorObj
    )

    $response = [ordered]@{ id = $Id }

    if ($null -ne $ErrorObj) {
        $response['error'] = $ErrorObj
    }
    else {
        $response['result'] = $Result
    }

    $json = $response | ConvertTo-Json -Depth 10 -Compress
    [Console]::Out.WriteLine($json)
    [Console]::Out.Flush()
}

function Invoke-PingHandler {
    <#
    .SYNOPSIS
        Respond to a health-check ping.
    #>
    param([string]$Id)
    Write-NdjsonResponse -Id $Id -Result @{ status = 'ok' }
}

function Invoke-ShutdownHandler {
    <#
    .SYNOPSIS
        Acknowledge shutdown request and exit.
    #>
    param([string]$Id)
    Write-NdjsonResponse -Id $Id -Result @{ status = 'shutting_down' }
    Write-Diag 'Shutdown requested. Exiting.'
    exit 0
}

function Invoke-SetupHandler {
    <#
    .SYNOPSIS
        Apply environment customization: module paths, PSGallery trust,
        module installation, module import, and startup scripts.
        Mirrors the ordering from PowerShellEnvironmentSetup.ApplyEnvironmentConfiguration().
    #>
    param(
        [string]$Id,
        [object]$Params
    )

    $errors = [System.Collections.ArrayList]::new()
    $warnings = [System.Collections.ArrayList]::new()
    $installedModules = [System.Collections.ArrayList]::new()
    $importedModules = [System.Collections.ArrayList]::new()
    $configuredPaths = [System.Collections.ArrayList]::new()
    $startupScriptExecuted = $false
    $inlineScriptExecuted = $false

    Write-Diag 'Starting environment setup'

    # Step 1: Configure PSModulePath with additional paths
    $modulePaths = @()
    if ($null -ne $Params.modulePaths) {
        $modulePaths = @($Params.modulePaths) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    }
    if ($modulePaths.Count -gt 0) {
        Write-Diag "Configuring PSModulePath with $($modulePaths.Count) additional path(s)"
        $validPaths = [System.Collections.ArrayList]::new()
        foreach ($p in $modulePaths) {
            $expanded = [System.Environment]::ExpandEnvironmentVariables($p)
            if (Test-Path -Path $expanded -PathType Container) {
                $null = $validPaths.Add($expanded)
                Write-Diag "  Added module path: $expanded"
            }
            else {
                $msg = "Module path does not exist: $expanded"
                Write-Diag "  WARNING: $msg"
                $null = $warnings.Add($msg)
            }
        }
        if ($validPaths.Count -gt 0) {
            $separator = [System.IO.Path]::PathSeparator
            $env:PSModulePath = ($validPaths -join $separator) + $separator + $env:PSModulePath
            $null = $configuredPaths.AddRange($validPaths)
        }
    }

    # Step 2: Trust PSGallery if configured — only needed when modules will be installed
    $trustPSGallery = $false
    if ($null -ne $Params.trustPSGallery) {
        $trustPSGallery = [bool]$Params.trustPSGallery
    }
    $hasModulesToInstall = $null -ne $Params.installModules -and @($Params.installModules).Count -gt 0
    if ($trustPSGallery -and $hasModulesToInstall) {
        Write-Diag 'Configuring PSGallery as trusted repository'
        try {
            if (-not (Get-PSRepository -Name PSGallery -ErrorAction SilentlyContinue)) {
                Register-PSRepository -Default -ErrorAction SilentlyContinue
            }
            Set-PSRepository -Name PSGallery -InstallationPolicy Trusted -ErrorAction Stop
            Write-Diag '  PSGallery configured as trusted'
        }
        catch {
            $msg = "Failed to trust PSGallery: $_"
            Write-Diag "  WARNING: $msg"
            $null = $warnings.Add($msg)
        }
    }

    # Step 3: Install modules from PSGallery or other repositories
    $installModules = @()
    if ($null -ne $Params.installModules) {
        $installModules = @($Params.installModules)
    }
    $skipPublisherCheck = $true
    if ($null -ne $Params.skipPublisherCheck) {
        $skipPublisherCheck = [bool]$Params.skipPublisherCheck
    }
    $installTimeoutSeconds = 300
    if ($null -ne $Params.installTimeoutSeconds) {
        $installTimeoutSeconds = [int]$Params.installTimeoutSeconds
    }
    foreach ($mod in $installModules) {
        $modName = $mod.name
        if ([string]::IsNullOrWhiteSpace($modName)) { continue }

        Write-Diag "Installing module: $modName"
        try {
            # Check if already installed (skip unless force)
            $forceInstall = $false
            if ($null -ne $mod.force) {
                $forceInstall = [bool]$mod.force
            }
            if (-not $forceInstall) {
                $existing = Get-Module -ListAvailable -Name $modName -ErrorAction SilentlyContinue
                if ($existing) {
                    Write-Diag "  Module $modName already installed. Skipping."
                    continue
                }
            }

            $installParams = @{
                Name        = $modName
                ErrorAction = 'Stop'
                Force       = $true
            }

            # Repository
            if (-not [string]::IsNullOrWhiteSpace($mod.repository)) {
                $installParams['Repository'] = $mod.repository
            }
            else {
                $installParams['Repository'] = 'PSGallery'
            }

            # Scope
            if (-not [string]::IsNullOrWhiteSpace($mod.scope)) {
                $installParams['Scope'] = $mod.scope
            }
            else {
                $installParams['Scope'] = 'CurrentUser'
            }

            # Version constraints
            if (-not [string]::IsNullOrWhiteSpace($mod.version)) {
                $installParams['RequiredVersion'] = $mod.version
            }
            elseif (-not [string]::IsNullOrWhiteSpace($mod.minimumVersion)) {
                $installParams['MinimumVersion'] = $mod.minimumVersion
                if (-not [string]::IsNullOrWhiteSpace($mod.maximumVersion)) {
                    $installParams['MaximumVersion'] = $mod.maximumVersion
                }
            }

            # SkipPublisherCheck — per-module setting overrides global
            $modSkipPublisher = $skipPublisherCheck
            if ($null -ne $mod.skipPublisherCheck) {
                $modSkipPublisher = [bool]$mod.skipPublisherCheck
            }
            if ($modSkipPublisher) {
                $installParams['SkipPublisherCheck'] = $true
            }

            # AllowPrerelease
            if ($null -ne $mod.allowPrerelease -and [bool]$mod.allowPrerelease) {
                $installParams['AllowPrerelease'] = $true
            }

            Install-Module @installParams -WarningAction SilentlyContinue -WarningVariable installWarnings
            foreach ($w in $installWarnings) { Write-Diag "  Module install warning: $w" }
            $null = $installedModules.Add($modName)
            Write-Diag "  Successfully installed module: $modName"
        }
        catch {
            $msg = "Error installing module $modName`: $_"
            Write-Diag "  ERROR: $msg"
            $null = $errors.Add($msg)
        }
    }

    # Step 4: Import pre-installed modules
    $importModulesList = @()
    if ($null -ne $Params.importModules) {
        $importModulesList = @($Params.importModules) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    }
    $allowClobber = $false
    if ($null -ne $Params.allowClobber) {
        $allowClobber = [bool]$Params.allowClobber
    }
    foreach ($modName in $importModulesList) {
        Write-Diag "Importing module: $modName"
        try {
            $importParams = @{
                Name            = $modName
                ErrorAction     = 'Stop'
                PassThru        = $true
                WarningAction   = 'SilentlyContinue'
                WarningVariable = 'importWarnings'
            }
            if ($allowClobber) {
                $importParams['Force'] = $true
            }
            Import-Module @importParams
            foreach ($w in $importWarnings) { Write-Diag "  Module warning: $w" }
            $null = $importedModules.Add($modName)
            Write-Diag "  Successfully imported module: $modName"
        }
        catch {
            $msg = "Error importing module $modName`: $_"
            Write-Diag "  ERROR: $msg"
            $null = $errors.Add($msg)
        }
    }

    # Step 5: Execute startup script from file
    if (-not [string]::IsNullOrWhiteSpace($Params.startupScriptPath)) {
        $scriptPath = [System.Environment]::ExpandEnvironmentVariables($Params.startupScriptPath)
        Write-Diag "Executing startup script file: $scriptPath"
        if (Test-Path -Path $scriptPath -PathType Leaf) {
            try {
                $scriptContent = Get-Content -Path $scriptPath -Raw
                Invoke-Expression $scriptContent
                $startupScriptExecuted = $true
                Write-Diag '  Successfully executed startup script file'
            }
            catch {
                $msg = "Error executing startup script file: $_"
                Write-Diag "  ERROR: $msg"
                $null = $errors.Add($msg)
            }
        }
        else {
            $msg = "Startup script file not found: $scriptPath"
            Write-Diag "  ERROR: $msg"
            $null = $errors.Add($msg)
        }
    }

    # Step 6: Execute inline startup script
    if (-not [string]::IsNullOrWhiteSpace($Params.startupScript)) {
        Write-Diag "Executing inline startup script ($($Params.startupScript.Length) characters)"
        try {
            Invoke-Expression $Params.startupScript
            $inlineScriptExecuted = $true
            Write-Diag '  Successfully executed inline startup script'
        }
        catch {
            $msg = "Error executing inline startup script: $_"
            Write-Diag "  ERROR: $msg"
            $null = $errors.Add($msg)
        }
    }

    $success = $errors.Count -eq 0
    Write-Diag "Environment setup completed. Success=$success, Installed=$($installedModules.Count), Imported=$($importedModules.Count), Errors=$($errors.Count)"

    Write-NdjsonResponse -Id $Id -Result @{
        success                = $success
        installedModules       = @($installedModules)
        importedModules        = @($importedModules)
        configuredModulePaths  = @($configuredPaths)
        startupScriptExecuted  = $startupScriptExecuted
        inlineScriptExecuted   = $inlineScriptExecuted
        errors                 = @($errors)
        warnings               = @($warnings)
    }
}

function Invoke-DiscoverHandler {
    <#
    .SYNOPSIS
        Import modules, discover commands, and return RemoteToolSchema objects.
    #>
    param(
        [string]$Id,
        [object]$Params
    )

    $commands = [System.Collections.ArrayList]::new()

    # Import requested modules
    $modules = @()
    if ($null -ne $Params.modules) {
        $modules = @($Params.modules)
    }
    foreach ($moduleName in $modules) {
        try {
            Write-Diag "Importing module: $moduleName"
            Import-Module -Name $moduleName -ErrorAction Stop -WarningAction SilentlyContinue -WarningVariable discoverImportWarnings
            foreach ($w in $discoverImportWarnings) { Write-Diag "  Module warning: $w" }
            Write-Diag "Imported module: $moduleName"
        }
        catch {
            Write-Diag "Failed to import module '$moduleName': $_"
            Write-NdjsonResponse -Id $Id -ErrorObj @{
                code    = -1
                message = "Failed to import module '$moduleName': $_"
            }
            return
        }
    }

    # Build Get-Command parameters
    $getCommandParams = @{}

    # Explicit function names
    $functionNames = @()
    if ($null -ne $Params.functionNames) {
        $functionNames = @($Params.functionNames) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    }

    # Include patterns
    $includePatterns = @()
    if ($null -ne $Params.includePatterns) {
        $includePatterns = @($Params.includePatterns) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    }

    # Exclude patterns
    $excludePatterns = @()
    if ($null -ne $Params.excludePatterns) {
        $excludePatterns = @($Params.excludePatterns)
    }

    # Discover commands from explicit names
    foreach ($name in $functionNames) {
        try {
            $cmds = @(Get-Command -Name $name -ErrorAction SilentlyContinue)
            foreach ($cmd in $cmds) {
                $null = $commands.Add($cmd)
            }
        }
        catch {
            Write-Diag "Warning: Could not resolve command '$name': $_"
        }
    }

    # Discover commands from modules using include patterns
    foreach ($moduleName in $modules) {
        foreach ($pattern in $includePatterns) {
            try {
                $cmds = @(Get-Command -Module $moduleName -Name $pattern -ErrorAction SilentlyContinue)
                foreach ($cmd in $cmds) {
                    # Apply exclude patterns
                    $excluded = $false
                    foreach ($ep in $excludePatterns) {
                        if ($cmd.Name -like $ep) {
                            $excluded = $true
                            break
                        }
                    }
                    if (-not $excluded) {
                        $null = $commands.Add($cmd)
                    }
                }
            }
            catch {
                Write-Diag "Warning: Get-Command failed for module '$moduleName' pattern '$pattern': $_"
            }
        }
    }

    # When no modules are specified, discover include patterns globally (same behaviour as in-process)
    if ($modules.Count -eq 0 -and $commands.Count -eq 0 -and $includePatterns.Count -gt 0) {
        foreach ($pattern in $includePatterns) {
            try {
                $cmds = @(Get-Command -Name $pattern -ErrorAction SilentlyContinue)
                foreach ($cmd in $cmds) {
                    $excluded = $false
                    foreach ($ep in $excludePatterns) {
                        if ($cmd.Name -like $ep) { $excluded = $true; break }
                    }
                    if (-not $excluded) { $null = $commands.Add($cmd) }
                }
            }
            catch {
                Write-Diag "Warning: Get-Command failed for global pattern '$pattern': $_"
            }
        }
    }

    # Deduplicate by name (same command may appear from explicit + pattern)
    $seen = @{}
    $uniqueCommands = [System.Collections.ArrayList]::new()
    foreach ($cmd in $commands) {
        if (-not $seen.ContainsKey($cmd.Name)) {
            $seen[$cmd.Name] = $true
            $null = $uniqueCommands.Add($cmd)
        }
    }

    Write-Diag "Discovered $($uniqueCommands.Count) unique command(s)"

    # Build RemoteToolSchema for each command and parameter set
    $schemas = [System.Collections.ArrayList]::new()

    foreach ($cmd in $uniqueCommands) {
        $description = ''
        try {
            $helpInfo = Get-Help -Name $cmd.Name -ErrorAction SilentlyContinue
            if ($null -ne $helpInfo -and $null -ne $helpInfo.Synopsis) {
                $synopsis = "$($helpInfo.Synopsis)".Trim()
                if ($synopsis -and $synopsis -ne $cmd.Name) {
                    $description = $synopsis
                }
            }
        }
        catch {
            # Best effort — description stays empty
        }

        foreach ($paramSet in $cmd.ParameterSets) {
            $parameters = [System.Collections.ArrayList]::new()

            foreach ($param in $paramSet.Parameters) {
                # Skip common parameters
                if ($script:CommonParameters -contains $param.Name) {
                    continue
                }

                $null = $parameters.Add([ordered]@{
                    Name        = $param.Name
                    TypeName    = $param.ParameterType.FullName
                    IsMandatory = [bool]$param.IsMandatory
                    Position    = $param.Position
                })
            }

            $null = $schemas.Add([ordered]@{
                Name             = $cmd.Name
                Description      = $description
                ParameterSetName = $paramSet.Name
                Parameters       = @($parameters)
            })
        }
    }

    Write-NdjsonResponse -Id $Id -Result @{ commands = @($schemas) }
}

function Invoke-InvokeHandler {
    <#
    .SYNOPSIS
        Execute a PowerShell command and return the result.
    #>
    param(
        [string]$Id,
        [object]$Params
    )

    $commandName = $Params.command
    if ([string]::IsNullOrWhiteSpace($commandName)) {
        Write-NdjsonResponse -Id $Id -ErrorObj @{
            code    = -1
            message = 'Missing required parameter: command'
        }
        return
    }

    # Build parameters hashtable for splatting
    $splatParams = @{}
    if ($null -ne $Params.parameters) {
        # Convert the PSCustomObject from ConvertFrom-Json into a hashtable
        $Params.parameters.PSObject.Properties | ForEach-Object {
            $splatParams[$_.Name] = $_.Value
        }
    }

    # Handle SwitchParameter: if value is boolean true, include as [switch];
    # if false, omit from splatting entirely.
    try {
        $cmdInfo = Get-Command -Name $commandName -ErrorAction Stop
        $switchParams = @()
        foreach ($ps in $cmdInfo.ParameterSets) {
            foreach ($p in $ps.Parameters) {
                if ($p.ParameterType.FullName -eq 'System.Management.Automation.SwitchParameter') {
                    $switchParams += $p.Name
                }
            }
        }

        foreach ($switchName in ($switchParams | Select-Object -Unique)) {
            if ($splatParams.ContainsKey($switchName)) {
                $val = $splatParams[$switchName]
                if ($val -eq $true -or $val -eq 'true' -or $val -eq 'True') {
                    $splatParams[$switchName] = [switch]$true
                }
                else {
                    # Remove false switch parameters so they aren't passed
                    $splatParams.Remove($switchName)
                }
            }
        }
    }
    catch {
        Write-Diag "Warning: Could not resolve command info for '$commandName': $_"
        # Proceed anyway — splatting may still work
    }

    Write-Diag "Invoking: $commandName with $($splatParams.Count) parameter(s)"

    try {
        $result = & $commandName @splatParams
        $hadErrors = $false

        # Check if there were non-terminating errors
        if ($Error.Count -gt 0) {
            $hadErrors = $true
        }

        $jsonOutput = $result | ConvertTo-Json -Depth 4 -Compress -WarningAction SilentlyContinue 3>$null
        if ($null -eq $jsonOutput) {
            $jsonOutput = 'null'
        }

        Write-NdjsonResponse -Id $Id -Result @{
            output    = $jsonOutput
            hadErrors = $hadErrors
        }
    }
    catch {
        Write-Diag "Error invoking '$commandName': $_"
        Write-NdjsonResponse -Id $Id -ErrorObj @{
            code    = -1
            message = "$_"
        }
    }
}

# --- Main loop ---
# Read ndjson from stdin, dispatch to the appropriate handler.

Write-Diag 'oop-host.ps1 started. Waiting for requests on stdin.'

while ($true) {
    $line = [Console]::ReadLine()

    # stdin closed (EOF) — exit cleanly
    if ($null -eq $line) {
        Write-Diag 'stdin closed (EOF). Exiting.'
        break
    }

    # Skip blank lines
    if ([string]::IsNullOrWhiteSpace($line)) {
        continue
    }

    try {
        $request = $line | ConvertFrom-Json
    }
    catch {
        Write-Diag "Malformed JSON received, skipping: $line"
        continue
    }

    $id = $request.id
    $method = $request.method
    $params = $request.params

    if ([string]::IsNullOrWhiteSpace($id)) {
        Write-Diag "Request missing 'id' field, skipping: $line"
        continue
    }

    if ([string]::IsNullOrWhiteSpace($method)) {
        Write-NdjsonResponse -Id $id -ErrorObj @{
            code    = -1
            message = "Missing required field: method"
        }
        continue
    }

    try {
        switch ($method) {
            'ping' {
                Invoke-PingHandler -Id $id
            }
            'setup' {
                Invoke-SetupHandler -Id $id -Params $params
            }
            'shutdown' {
                Invoke-ShutdownHandler -Id $id
            }
            'discover' {
                Invoke-DiscoverHandler -Id $id -Params $params
            }
            'invoke' {
                Invoke-InvokeHandler -Id $id -Params $params
            }
            default {
                Write-NdjsonResponse -Id $id -ErrorObj @{
                    code    = -1
                    message = "Unknown method: $method"
                }
            }
        }
    }
    catch {
        Write-Diag "Unhandled error processing method '$method': $_"
        Write-NdjsonResponse -Id $id -ErrorObj @{
            code    = -1
            message = "Internal error: $_"
        }
    }
}
