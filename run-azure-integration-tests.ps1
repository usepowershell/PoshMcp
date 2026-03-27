#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Run Azure deployment integration tests with automatic setup.

.DESCRIPTION
    This script simplifies running the Azure integration tests by:
    - Validating prerequisites (Docker, Azure CLI, .NET SDK)
    - Checking Azure authentication
    - Setting up environment variables
    - Running the integration tests
    - Optionally cleaning up Azure resources

.PARAMETER TestName
    Specific test to run. Options: Base, Custom, Deploy, All
    Default: All

.PARAMETER SkipPrerequisites
    Skip prerequisite checks (faster, but may fail if tools missing)

.PARAMETER Category
    Run only tests with these trait categories (e.g., Docker, Azure, Integration)
    Can be specified multiple times. Uses AND logic.

.PARAMETER ExcludeCategory
    Exclude tests with these trait categories
    Can be specified multiple times.

.PARAMETER FastOnly
    Run only fast tests (exclude Slow and VerySlow tests)

.PARAMETER ExcludeExpensive
    Exclude tests marked as expensive (Azure costs)

.PARAMETER ResourceGroup
    Azure resource group name. If not specified, creates a timestamped one.

.PARAMETER Location
    Azure region for resources. Default: eastus

.PARAMETER RegistryName
    Azure Container Registry name. If not specified, creates a timestamped one.

.PARAMETER Cleanup
    Automatically delete Azure resources after tests complete

.PARAMETER DryRun
    Show what would happen without actually running tests

.EXAMPLE
    ./run-azure-integration-tests.ps1
    Run all integration tests with default settings

.EXAMPLE
    ./run-azure-integration-tests.ps1 -TestName Base
    Run only the base image build test

.EXAMPLE
    ./run-azure-integration-tests.ps1 -Cleanup
    Run tests and cleanup Azure resources afterwards

.EXAMPLE
    ./run-azure-integration-tests.ps1 -ResourceGroup "rg-existing" -RegistryName "acrexisting"
    Use existing Azure resources

.EXAMPLE
    ./run-azure-integration-tests.ps1 -Category "Docker"
    Run only Docker-related tests

.EXAMPLE
    ./run-azure-integration-tests.ps1 -ExcludeExpensive
    Run all tests except expensive ones (no Azure costs)

.EXAMPLE
    ./run-azure-integration-tests.ps1 -FastOnly
    Run only fast tests (skip slow integration tests)

.EXAMPLE
    ./run-azure-integration-tests.ps1 -Category "Integration" -ExcludeCategory "Azure"
    Run integration tests that don't require Azure deployment
#>

[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('Base', 'Custom', 'Deploy', 'All')]
    [string]$TestName = 'All',
    
    [Parameter()]
    [switch]$SkipPrerequisites,
    
    [Parameter()]
    [string[]]$Category,
    
    [Parameter()]
    [string[]]$ExcludeCategory,
    
    [Parameter()]
    [switch]$FastOnly,
    
    [Parameter()]
    [switch]$ExcludeExpensive,
    
    [Parameter()]
    [string]$ResourceGroup,
    
    [Parameter()]
    [string]$Location = 'eastus2',
    
    [Parameter()]
    [string]$RegistryName,
    
    [Parameter()]
    [switch]$Cleanup,
    
    [Parameter()]
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

# Color output helpers
function Write-Header {
    param([string]$Message)
    Write-Host "`n$Message" -ForegroundColor Cyan
    Write-Host ("=" * $Message.Length) -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "✅ $Message" -ForegroundColor Green
}

function Write-Warning {
    param([string]$Message)
    Write-Host "⚠️  $Message" -ForegroundColor Yellow
}

function Write-Error {
    param([string]$Message)
    Write-Host "❌ $Message" -ForegroundColor Red
}

function Write-Info {
    param([string]$Message)
    Write-Host "ℹ️  $Message" -ForegroundColor Gray
}

# Main script
Write-Header "Azure Deployment Integration Test Runner"

# Step 1: Check prerequisites
if (-not $SkipPrerequisites) {
    Write-Header "Step 1: Checking Prerequisites"
    
    # Check Docker
    try {
        $dockerVersion = docker --version
        Write-Success "Docker found: $dockerVersion"
        
        # Check if Docker is running
        docker ps 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Docker is installed but not running. Start Docker Desktop and try again."
            exit 1
        }
    }
    catch {
        Write-Error "Docker not found. Install from https://www.docker.com/products/docker-desktop"
        exit 1
    }
    
    # Check Azure CLI
    try {
        $azVersion = az --version | Select-Object -First 1
        Write-Success "Azure CLI found: $azVersion"
    }
    catch {
        Write-Error "Azure CLI not found. Install from https://aka.ms/InstallTheAzureCLI"
        exit 1
    }
    
    # Check .NET SDK
    try {
        $dotnetVersion = dotnet --version
        Write-Success ".NET SDK found: $dotnetVersion"
    }
    catch {
        Write-Error ".NET SDK not found. Install from https://dotnet.microsoft.com/download"
        exit 1
    }
    
    # Check Azure authentication
    try {
        $account = az account show --query '[name,id]' -o tsv 2>$null
        if ($LASTEXITCODE -eq 0) {
            $accountInfo = $account -split "`t"
            Write-Success "Azure authenticated: $($accountInfo[0])"
            Write-Info "Subscription ID: $($accountInfo[1])"
            $subscriptionId = $accountInfo[1]
        }
        else {
            Write-Error "Not authenticated to Azure. Run: az login"
            exit 1
        }
    }
    catch {
        Write-Error "Azure authentication check failed. Run: az login"
        exit 1
    }
}
else {
    Write-Info "Skipping prerequisite checks"
    
    # Still need subscription ID
    try {
        $subscriptionId = (az account show --query 'id' -o tsv)
    }
    catch {
        Write-Error "Could not get Azure subscription ID. Run: az login"
        exit 1
    }
}

# Step 2: Configure environment
Write-Header "Step 2: Configuring Environment"

# Set subscription ID
$env:AZURE_SUBSCRIPTION_ID = $subscriptionId
Write-Success "AZURE_SUBSCRIPTION_ID: $subscriptionId"

# Set resource group
if (-not $ResourceGroup) {
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $ResourceGroup = "rg-poshmcp-test-$timestamp"
    Write-Info "Using generated resource group: $ResourceGroup"
}
else {
    Write-Info "Using specified resource group: $ResourceGroup"
}
$env:AZURE_RESOURCE_GROUP = $ResourceGroup
Write-Success "AZURE_RESOURCE_GROUP: $ResourceGroup"

# Set location
$env:AZURE_LOCATION = $Location
Write-Success "AZURE_LOCATION: $Location"

# Set registry name
if (-not $RegistryName) {
    $timestamp = Get-Date -Format "yyyyMMddHHmmss"
    $RegistryName = "acr$timestamp".Substring(0, [Math]::Min(50, "acr$timestamp".Length)).ToLower()
    Write-Info "Using generated registry name: $RegistryName"
}
else {
    Write-Info "Using specified registry name: $RegistryName"
}
$env:AZURE_CONTAINER_REGISTRY = $RegistryName
Write-Success "AZURE_CONTAINER_REGISTRY: $RegistryName"

# Step 3: Determine test filter
Write-Header "Step 3: Preparing Test Execution"

# Build test filter based on parameters
$filterParts = @()

# Add test name filter
$testNameFilter = switch ($TestName) {
    'Base'   { 'BuildBaseImage_ShouldSucceed' }
    'Custom' { 'BuildCustomAzureImage_FromBaseImage_ShouldSucceed' }
    'Deploy' { 'DeployToAzure_CompleteFlow_ShouldSucceed' }
    'All'    { 'FullyQualifiedName~AzureDeploymentIntegrationTests' }
}
$filterParts += $testNameFilter

# Add category filters if specified
if ($Category) {
    foreach ($cat in $Category) {
        $filterParts += "Category=$cat"
    }
    Write-Info "Including categories: $($Category -join ', ')"
}

# Add exclude category filters if specified
if ($ExcludeCategory) {
    foreach ($cat in $ExcludeCategory) {
        $filterParts += "Category!=$cat"
    }
    Write-Info "Excluding categories: $($ExcludeCategory -join ', ')"
}

# Add speed filters
if ($FastOnly) {
    $filterParts += "Speed!=Slow"
    $filterParts += "Speed!=VerySlow"
    Write-Info "Fast tests only (excluding Slow and VerySlow)"
}

# Add cost filters
if ($ExcludeExpensive) {
    $filterParts += "Cost!=Expensive"
    Write-Info "Excluding expensive tests"
}

# Combine filters with & (AND)
$testFilter = $filterParts -join '&'

Write-Info "Test filter: $testFilter"

# Estimate cost and time
$estimatedCost = "~`$0.10"
$estimatedTime = "2-5 minutes"

if ($TestName -eq 'Deploy' -or $TestName -eq 'All') {
    $estimatedCost = "~`$0.50-1.00"
    $estimatedTime = "5-10 minutes"
}

Write-Warning "Estimated cost: $estimatedCost"
Write-Warning "Estimated time: $estimatedTime"

if ($DryRun) {
    Write-Header "Dry Run Complete"
    Write-Info "Would run: dotnet test --filter `"$testFilter`""
    Write-Info "Environment configured correctly"
    exit 0
}

# Step 4: Run tests
Write-Header "Step 4: Running Integration Tests"

try {
    # Navigate to test directory
    $repoRoot = $PSScriptRoot
    $testDir = Join-Path $repoRoot "PoshMcp.Tests"
    
    if (-not (Test-Path $testDir)) {
        Write-Error "Test directory not found: $testDir"
        exit 1
    }
    
    Push-Location $testDir
    
    Write-Info "Running tests from: $testDir"
    Write-Info "Command: dotnet test --filter `"$testFilter`" --logger `"console;verbosity=detailed`""
    
    dotnet test --filter $testFilter --logger "console;verbosity=detailed"
    
    $testExitCode = $LASTEXITCODE
    
    Pop-Location
    
    if ($testExitCode -eq 0) {
        Write-Header "Test Results"
        Write-Success "All integration tests passed!"
    }
    else {
        Write-Header "Test Results"
        Write-Error "Integration tests failed with exit code: $testExitCode"
    }
}
catch {
    Write-Error "Error running tests: $($_.Exception.Message)"
    $testExitCode = 1
}

# Step 5: Cleanup (if requested)
if ($Cleanup) {
    Write-Header "Step 5: Cleaning Up Azure Resources"
    
    Write-Warning "This will delete resource group: $ResourceGroup"
    $confirmation = Read-Host "Are you sure? (yes/no)"
    
    if ($confirmation -eq 'yes') {
        Write-Info "Deleting resource group asynchronously..."
        az group delete --name $ResourceGroup --yes --no-wait
        
        if ($LASTEXITCODE -eq 0) {
            Write-Success "Cleanup initiated. Resource group will be deleted in the background."
            Write-Info "Check status with: az group show --name $ResourceGroup"
        }
        else {
            Write-Warning "Cleanup failed or resource group doesn't exist"
        }
    }
    else {
        Write-Info "Cleanup cancelled"
        Write-Info "To cleanup manually, run: az group delete --name $ResourceGroup --yes"
    }
}
else {
    Write-Header "Cleanup"
    Write-Info "Azure resources were NOT automatically cleaned up"
    Write-Info "To cleanup manually, run: az group delete --name $ResourceGroup --yes"
}

# Final summary
Write-Header "Summary"
Write-Info "Test execution complete"
Write-Info "Exit code: $testExitCode"

if ($testExitCode -ne 0) {
    Write-Info "For troubleshooting, see: PoshMcp.Tests/Integration/README.azure-integration.md"
}

exit $testExitCode
