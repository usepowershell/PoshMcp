# Azure Container Apps deployment script for PoshMcp (PowerShell version)
# This script handles the complete deployment workflow including:
# - Prerequisites validation
# - Container image build and push
# - Infrastructure deployment via Bicep
# - Post-deployment verification

[CmdletBinding()]
param(
    [Parameter()]
    [string]$ResourceGroup = $env:RESOURCE_GROUP ?? 'poshmcp-rg',
    
    [Parameter()]
    [string]$Location = $env:LOCATION ?? 'eastus',
    
    [Parameter()]
    [string]$ContainerAppName = $env:CONTAINER_APP_NAME ?? 'poshmcp',
    
    [Parameter(Mandatory)]
    [string]$RegistryName,
    
    [Parameter()]
    [string]$ImageTag = (Get-Date -Format 'yyyyMMdd-HHmmss'),
    
    [Parameter()]
    [string]$Subscription = $env:AZURE_SUBSCRIPTION,
    
    [Parameter()]
    [string]$TenantId = $env:AZURE_TENANT_ID
)

$ErrorActionPreference = 'Stop'
# Enable information stream by default for user-facing messages
$InformationPreference = 'Continue'

# Get script directory
$ScriptDir = $PSScriptRoot
$ProjectRoot = Join-Path $ScriptDir '..' '..' | Resolve-Path
$BicepFile = Join-Path $ScriptDir 'main.bicep'
$ParametersFile = Join-Path $ScriptDir 'parameters.json'

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
        docker build -t $FullImageName -t $latestImage -f Dockerfile .
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Docker build failed" -Category InvalidOperation -ErrorAction Stop
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
        Write-Verbose "Waiting 10 seconds for application to start"
        Start-Sleep -Seconds 10  # Wait for app to start
        
        try {
            Write-Verbose "Testing: https://$appUrl/health/ready"
            $response = Invoke-WebRequest -Uri "https://$appUrl/health/ready" -UseBasicParsing -ErrorAction SilentlyContinue
            if ($response.StatusCode -eq 200) {
                Write-Information "✓ Health check passed" -Tags 'Success'
            }
            else {
                Write-Warning "Health check returned status: $($response.StatusCode)"
            }
        }
        catch {
            Write-Warning "Health check failed - application may still be starting: $_"
        }
        
        Write-Host ""
        Write-Information "Deployment Summary:" -Tags 'Status'
        Write-Host "  Application URL: https://$appUrl" -ForegroundColor Cyan
        Write-Host "  Health Check: https://$appUrl/health" -ForegroundColor Cyan
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
    
    Test-Prerequisites
    Set-AzureTenant
    Set-AzureSubscription
    New-ResourceGroupIfNeeded
    Initialize-ContainerRegistry
    Build-AndPushImage
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
