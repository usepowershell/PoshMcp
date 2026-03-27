# Company-specific PowerShell environment setup script
# This script runs at container startup to initialize the PowerShell session

Write-Host "🚀 Initializing Company PowerShell Environment..." -ForegroundColor Cyan

# Set global variables
$Global:CompanyName = $env:COMPANY_NAME ?? "Acme Corporation"
$Global:Environment = $env:ENVIRONMENT ?? "Production"
$Global:InitializedAt = Get-Date

# Configure PowerShell preferences for production
$ErrorActionPreference = "Continue"  # Continue on errors but log them
$ProgressPreference = "SilentlyContinue"  # Hide progress bars in logs
$VerbosePreference = "SilentlyContinue"  # Hide verbose output unless explicitly requested

# Define utility functions available to all commands
function Get-EnvironmentInfo {
    <#
    .SYNOPSIS
    Get information about the current PowerShell environment
    
    .DESCRIPTION
    Returns details about the MCP PowerShell environment including
    company info, environment type, loaded modules, and session state
    
    .EXAMPLE
    Get-EnvironmentInfo
    #>
    
    return [PSCustomObject]@{
        Company = $Global:CompanyName
        Environment = $Global:Environment
        InitializedAt = $Global:InitializedAt
        UptimeMinutes = ((Get-Date) - $Global:InitializedAt).TotalMinutes
        LoadedModules = (Get-Module).Name
        PSVersion = $PSVersionTable.PSVersion.ToString()
        OS = $PSVersionTable.OS
        MachineName = $env:COMPUTERNAME ?? $env:HOSTNAME
    }
}

function Write-CompanyLog {
    <#
    .SYNOPSIS
    Write a formatted log message
    
    .PARAMETER Message
    The message to log
    
    .PARAMETER Level
    Log level (Info, Warning, Error)
    
    .EXAMPLE
    Write-CompanyLog -Message "Operation completed" -Level Info
    #>
    param(
        [Parameter(Mandatory)]
        [string]$Message,
        
        [ValidateSet('Info', 'Warning', 'Error')]
        [string]$Level = 'Info'
    )
    
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $color = switch ($Level) {
        'Info' { 'Green' }
        'Warning' { 'Yellow' }
        'Error' { 'Red' }
    }
    
    Write-Host "[$timestamp] [$Level] $Message" -ForegroundColor $color
}

# Azure-specific setup if running in Azure
if ($env:AZURE_CLIENT_ID) {
    Write-CompanyLog "Detected Azure Managed Identity environment" -Level Info
    
    # Import Azure modules if available
    $azModules = @('Az.Accounts', 'Az.Resources', 'Az.Storage')
    foreach ($module in $azModules) {
        if (Get-Module -ListAvailable -Name $module) {
            Import-Module $module -ErrorAction SilentlyContinue
            Write-CompanyLog "Imported module: $module" -Level Info
        }
    }
    
    # Connect using Managed Identity
    try {
        $null = Connect-AzAccount -Identity -ErrorAction Stop
        $context = Get-AzContext
        Write-CompanyLog "Connected to Azure subscription: $($context.Subscription.Name)" -Level Info
    }
    catch {
        Write-CompanyLog "Failed to connect to Azure: $_" -Level Warning
    }
}

# Set up custom module paths if specified
if ($env:CUSTOM_MODULE_PATH) {
    $customPath = $env:CUSTOM_MODULE_PATH
    if (Test-Path $customPath) {
        $env:PSModulePath = "$customPath$([IO.Path]::PathSeparator)$env:PSModulePath"
        Write-CompanyLog "Added custom module path: $customPath" -Level Info
    }
}

# Define company-specific data access functions
function Get-CompanyData {
    <#
    .SYNOPSIS
    Retrieve company-specific data
    
    .PARAMETER DataType
    Type of data to retrieve
    
    .EXAMPLE
    Get-CompanyData -DataType "customers"
    #>
    param(
        [Parameter(Mandatory)]
        [string]$DataType
    )
    
    # This is a placeholder - replace with actual data access logic
    Write-CompanyLog "Retrieving $DataType data from company systems" -Level Info
    
    return @{
        DataType = $DataType
        RetrievedAt = Get-Date
        Source = "Company Data Lake"
        Status = "Success"
    }
}

# Create common aliases
Set-Alias -Name company-info -Value Get-EnvironmentInfo
Set-Alias -Name log -Value Write-CompanyLog

# Display initialization summary
Write-Host ""
Write-Host "✓ Environment: $Global:Environment" -ForegroundColor Green
Write-Host "✓ Company: $Global:CompanyName" -ForegroundColor Green
Write-Host "✓ PowerShell: $($PSVersionTable.PSVersion)" -ForegroundColor Green
Write-Host "✓ Loaded Modules: $((Get-Module).Count)" -ForegroundColor Green
Write-Host "✓ Custom Functions: Get-EnvironmentInfo, Write-CompanyLog, Get-CompanyData" -ForegroundColor Green
Write-Host ""
Write-Host "🎉 Company PowerShell environment ready!" -ForegroundColor Cyan
Write-Host ""
