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

# Get script directory
$ScriptDir = $PSScriptRoot
$ProjectRoot = Join-Path $ScriptDir '..' '..' | Resolve-Path
$BicepFile = Join-Path $ScriptDir 'main.bicep'
$ParametersFile = Join-Path $ScriptDir 'parameters.json'

# Helper functions
function Write-Info {
    param([string]$Message)
    Write-Host "[INFO] $Message" -ForegroundColor Blue
}

function Write-Success {
    param([string]$Message)
    Write-Host "[SUCCESS] $Message" -ForegroundColor Green
}

function Write-Warning {
    param([string]$Message)
    Write-Host "[WARNING] $Message" -ForegroundColor Yellow
}

function Write-ErrorMessage {
    param([string]$Message)
    Write-Host "[ERROR] $Message" -ForegroundColor Red
}

# Check prerequisites
function Test-Prerequisites {
    Write-Info "Checking prerequisites..."
    
    # Check Azure CLI
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        Write-ErrorMessage "Azure CLI not found. Please install: https://docs.microsoft.com/en-us/cli/azure/install-azure-cli"
        exit 1
    }
    
    # Check Docker
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        Write-ErrorMessage "Docker not found. Please install: https://docs.docker.com/get-docker/"
        exit 1
    }
    
    # Check if logged in to Azure
    $accountCheck = az account show 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-ErrorMessage "Not logged in to Azure. Please run: az login"
        exit 1
    }
    
    Write-Success "All prerequisites met"
}

# Set Azure subscription
function Set-AzureSubscription {
    if ($Subscription) {
        Write-Info "Setting subscription to: $Subscription"
        az account set --subscription $Subscription
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to set subscription"
        }
    }
    
    $currentSub = (az account show --query name -o tsv)
    Write-Info "Using subscription: $currentSub"
}

# Create resource group if it doesn't exist
function New-ResourceGroupIfNeeded {
    Write-Info "Checking resource group: $ResourceGroup"
    
    $rgExists = az group show --name $ResourceGroup 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Info "Creating resource group: $ResourceGroup in $Location"
        az group create --name $ResourceGroup --location $Location
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to create resource group"
        }
        Write-Success "Resource group created"
    }
    else {
        Write-Info "Resource group already exists"
    }
}

# Create or get Azure Container Registry
function Initialize-ContainerRegistry {
    Write-Info "Checking Azure Container Registry: $RegistryName"
    
    $registryExists = az acr show --name $RegistryName --resource-group $ResourceGroup 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Info "Creating Azure Container Registry: $RegistryName"
        az acr create `
            --name $RegistryName `
            --resource-group $ResourceGroup `
            --location $Location `
            --sku Standard `
            --admin-enabled true
        
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to create container registry"
        }
        Write-Success "Container registry created"
    }
    else {
        Write-Info "Container registry already exists"
    }
    
    # Set registry server
    $script:RegistryServer = "$RegistryName.azurecr.io"
    Write-Info "Registry server: $RegistryServer"
}

# Build and push container image
function Build-AndPushImage {
    Write-Info "Building container image..."
    
    Push-Location $ProjectRoot
    try {
        # Build image
        $script:FullImageName = "${RegistryServer}/poshmcp:${ImageTag}"
        $latestImage = "${RegistryServer}/poshmcp:latest"
        
        docker build -t $FullImageName -t $latestImage -f Dockerfile .
        if ($LASTEXITCODE -ne 0) {
            throw "Docker build failed"
        }
        
        Write-Success "Image built: $FullImageName"
        
        # Login to ACR
        Write-Info "Logging in to Azure Container Registry..."
        az acr login --name $RegistryName
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to login to container registry"
        }
        
        # Push image
        Write-Info "Pushing image to registry..."
        docker push $FullImageName
        docker push $latestImage
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to push image"
        }
        
        Write-Success "Image pushed to: $RegistryServer"
    }
    finally {
        Pop-Location
    }
}

# Deploy infrastructure using Bicep
function Deploy-Infrastructure {
    Write-Info "Deploying infrastructure with Bicep..."
    
    # Read and update parameters
    $params = Get-Content $ParametersFile | ConvertFrom-Json
    $params.parameters.containerImage.value = $FullImageName
    $params.parameters.containerRegistryServer.value = $RegistryServer
    $params.parameters.location.value = $Location
    
    # Save temporary parameters file
    $tempParams = New-TemporaryFile
    $params | ConvertTo-Json -Depth 10 | Set-Content $tempParams
    
    try {
        # Deploy using Bicep
        $deploymentName = "poshmcp-deployment-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        
        az deployment group create `
            --name $deploymentName `
            --resource-group $ResourceGroup `
            --template-file $BicepFile `
            --parameters "@$tempParams" `
            --verbose
        
        if ($LASTEXITCODE -ne 0) {
            throw "Deployment failed"
        }
        
        Write-Success "Infrastructure deployed"
    }
    finally {
        Remove-Item $tempParams -ErrorAction SilentlyContinue
    }
}

# Get deployment outputs
function Get-DeploymentInfo {
    Write-Info "Retrieving deployment information..."
    
    $appUrl = az containerapp show `
        --name $ContainerAppName `
        --resource-group $ResourceGroup `
        --query "properties.configuration.ingress.fqdn" `
        -o tsv
    
    if ($appUrl) {
        Write-Success "Application URL: https://$appUrl"
        
        # Test health endpoint
        Write-Info "Testing health endpoint..."
        Start-Sleep -Seconds 10  # Wait for app to start
        
        try {
            $response = Invoke-WebRequest -Uri "https://$appUrl/health/ready" -UseBasicParsing -ErrorAction SilentlyContinue
            if ($response.StatusCode -eq 200) {
                Write-Success "Health check passed"
            }
            else {
                Write-Warning "Health check returned status: $($response.StatusCode)"
            }
        }
        catch {
            Write-Warning "Health check failed - application may still be starting"
        }
        
        Write-Host ""
        Write-Info "Deployment Summary:"
        Write-Host "  Application URL: https://$appUrl" -ForegroundColor Cyan
        Write-Host "  Health Check: https://$appUrl/health" -ForegroundColor Cyan
        Write-Host "  Resource Group: $ResourceGroup" -ForegroundColor Cyan
        Write-Host "  Container App: $ContainerAppName" -ForegroundColor Cyan
    }
    else {
        Write-ErrorMessage "Could not retrieve application URL"
    }
}

# Main deployment workflow
function Invoke-Deployment {
    Write-Info "Starting PoshMcp deployment to Azure Container Apps"
    Write-Host ""
    
    Test-Prerequisites
    Set-AzureSubscription
    New-ResourceGroupIfNeeded
    Initialize-ContainerRegistry
    Build-AndPushImage
    Deploy-Infrastructure
    Get-DeploymentInfo
    
    Write-Host ""
    Write-Success "Deployment completed successfully!"
}

# Run main deployment
try {
    Invoke-Deployment
}
catch {
    Write-ErrorMessage "Deployment failed: $_"
    exit 1
}
