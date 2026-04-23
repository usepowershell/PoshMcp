# Pre-deployment validation script (PowerShell version)
# Checks prerequisites and validates configuration before deployment

[CmdletBinding()]
param()

$ErrorActionPreference = 'SilentlyContinue'
$ScriptDir = $PSScriptRoot
$BicepFile = Join-Path $ScriptDir 'main.bicep'
$ParametersFile = Join-Path $ScriptDir 'parameters.json'

function Write-Check {
    param(
        [string]$Message,
        [ValidateSet('OK', 'FAIL', 'WARNING')]
        [string]$Status,
        [string]$Details = ''
    )
    
    $statusColor = switch ($Status) {
        'OK' { 'Green' }
        'FAIL' { 'Red' }
        'WARNING' { 'Yellow' }
    }
    
    Write-Host "$Message... " -NoNewline
    Write-Host $Status -ForegroundColor $statusColor
    if ($Details) {
        Write-Host "  $Details" -ForegroundColor Gray
    }
}

Write-Host "PoshMcp Azure Deployment Validation" -ForegroundColor Cyan
Write-Host "====================================" -ForegroundColor Cyan
Write-Host ""

$validationPassed = $true

# Check Azure CLI
$azCmd = Get-Command az -ErrorAction SilentlyContinue
if ($azCmd) {
    $azVersion = (az version --query '"azure-cli"' -o tsv 2>$null)
    Write-Check "Checking Azure CLI" "OK" "version $azVersion"
}
else {
    Write-Check "Checking Azure CLI" "FAIL" "Azure CLI not installed"
    $validationPassed = $false
}

# Check Docker
$dockerCmd = Get-Command docker -ErrorAction SilentlyContinue
if ($dockerCmd) {
    $dockerVersion = (docker --version) -replace 'Docker version ([\d.]+).*','$1'
    Write-Check "Checking Docker" "OK" "version $dockerVersion"
    
    # Check Docker daemon
    $dockerPs = docker ps 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Check "Checking Docker daemon" "OK"
    }
    else {
        Write-Check "Checking Docker daemon" "FAIL" "Docker daemon not running"
        $validationPassed = $false
    }
}
else {
    Write-Check "Checking Docker" "FAIL" "Docker not installed"
    $validationPassed = $false
}

# Check Azure login
$accountCheck = az account show 2>&1
if ($LASTEXITCODE -eq 0) {
    $subscription = (az account show --query name -o tsv)
    Write-Check "Checking Azure authentication" "OK" $subscription
}
else {
    Write-Check "Checking Azure authentication" "FAIL" "Not logged in. Run: az login"
    $validationPassed = $false
}

# Check Bicep file
if (Test-Path $BicepFile) {
    Write-Check "Checking Bicep template" "OK"
    
    # Validate Bicep syntax
    $bicepBuild = az bicep build --file $BicepFile --stdout 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Check "Validating Bicep syntax" "OK"
    }
    else {
        Write-Check "Validating Bicep syntax" "FAIL" "Bicep file has syntax errors"
        Write-Host $bicepBuild -ForegroundColor Red
        $validationPassed = $false
    }
}
else {
    Write-Check "Checking Bicep template" "FAIL" "main.bicep not found"
    $validationPassed = $false
}

# Check parameters file
if (Test-Path $ParametersFile) {
    Write-Check "Checking parameters file" "OK"
    
    # Validate JSON
    try {
        $params = Get-Content $ParametersFile | ConvertFrom-Json
        Write-Check "Validating parameters JSON" "OK"
        
        # Check for placeholders
        $paramsText = Get-Content $ParametersFile -Raw
        if ($paramsText -match 'YOUR_REGISTRY') {
            Write-Check "Checking for placeholder values" "WARNING" "Parameters file contains placeholder values (YOUR_REGISTRY)"
        }
        else {
            Write-Check "Checking for placeholder values" "OK"
        }
    }
    catch {
        Write-Check "Validating parameters JSON" "FAIL" "Invalid JSON format"
        $validationPassed = $false
    }
}
else {
    Write-Check "Checking parameters file" "FAIL" "parameters.json not found"
    $validationPassed = $false
}

# Check environment variables
Write-Host ""
Write-Host "Environment Variables:" -ForegroundColor Cyan
if ($env:REGISTRY_NAME) {
    Write-Host "  REGISTRY_NAME: " -NoNewline
    Write-Host $env:REGISTRY_NAME -ForegroundColor Green
}
else {
    Write-Host "  REGISTRY_NAME: " -NoNewline
    Write-Host "Not set (required for deployment)" -ForegroundColor Yellow
}

if ($env:RESOURCE_GROUP) {
    Write-Host "  RESOURCE_GROUP: " -NoNewline
    Write-Host $env:RESOURCE_GROUP -ForegroundColor Green
}
else {
    Write-Host "  RESOURCE_GROUP: " -NoNewline
    Write-Host "Not set (will use default: rg-poshmcp)" -ForegroundColor Yellow
}

# Check location
$location = if ($env:LOCATION) { $env:LOCATION } else { 'eastus' }
$locationCheck = az account list-locations --query "[?name=='$location']" -o tsv 2>&1
if ($LASTEXITCODE -eq 0 -and $locationCheck) {
    Write-Check "Validating Azure location" "OK" $location
}
else {
    Write-Check "Validating Azure location" "WARNING" "location '$location' may be invalid"
}

# Check Container Apps provider
$providerState = az provider show --namespace Microsoft.App --query "registrationState" -o tsv 2>&1
if ($providerState -eq 'Registered') {
    Write-Check "Checking Container Apps availability" "OK"
}
else {
    Write-Check "Checking Container Apps availability" "WARNING" "Provider not registered"
    Write-Host "  Registering Microsoft.App provider..." -ForegroundColor Yellow
    az provider register --namespace Microsoft.App | Out-Null
    Write-Host "  Registration initiated (may take a few minutes)" -ForegroundColor Yellow
}

Write-Host ""
if ($validationPassed) {
    Write-Host "✓ Validation completed successfully" -ForegroundColor Green
    Write-Host ""
    Write-Host "Ready to deploy! Run:" -ForegroundColor Cyan
    Write-Host "  `$env:REGISTRY_NAME='myregistry'" -ForegroundColor White
    Write-Host "  .\deploy.ps1 -RegistryName `$env:REGISTRY_NAME" -ForegroundColor White
    exit 0
}
else {
    Write-Host "✗ Validation failed - fix errors before deploying" -ForegroundColor Red
    exit 1
}
