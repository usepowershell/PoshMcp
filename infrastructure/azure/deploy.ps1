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
    [string]$Subscription = $env:SUBSCRIPTION
)

$ErrorActionPreference = 'Stop'
# Enable information stream by default for user-facing messages
$InformationPreference = 'Continue'

# Get script directory
$ScriptDir = $PSScriptRoot
$ProjectRoot = Join-Path $ScriptDir '..' '..' | Resolve-Path
$BicepFile = Join-Path $ScriptDir 'main.bicep'
$ParametersFile = Join-Path $ScriptDir 'parameters.json'

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

# Set Azure subscription
function Set-AzureSubscription {
    if ($Subscription) {
        Write-Information "Setting subscription to: $Subscription" -Tags 'Status'
        Write-Verbose "Executing: az account set --subscription $Subscription"
        az account set --subscription $Subscription
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to set subscription" -Category InvalidOperation -ErrorAction Stop
        }
    }
    
    $currentSub = (az account show --query name -o tsv)
    Write-Information "Using subscription: $currentSub" -Tags 'Status'
}

# Create resource group if it doesn't exist
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
        
        # Login to ACR
        Write-Information "Logging in to Azure Container Registry..." -Tags 'Status'
        Write-Verbose "Executing: az acr login --name $RegistryName"
        az acr login --name $RegistryName
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to login to container registry" -Category AuthenticationError -ErrorAction Stop
        }
        
        # Push image
        Write-Information "Pushing image to registry..." -Tags 'Status'
        Write-Verbose "Pushing: $FullImageName"
        Write-Verbose "Pushing: $latestImage"
        docker push $FullImageName
        docker push $latestImage
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to push image" -Category InvalidOperation -ErrorAction Stop
        }
        
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
        # Deploy using Bicep
        $deploymentName = "poshmcp-deployment-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        Write-Verbose "Deployment name: $deploymentName"
        
        Write-Verbose "Executing: az deployment group create"
        az deployment group create `
            --name $deploymentName `
            --resource-group $ResourceGroup `
            --template-file $BicepFile `
            --parameters "@$tempParams" `
            --verbose
        
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Deployment failed" -Category InvalidOperation -ErrorAction Stop
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
