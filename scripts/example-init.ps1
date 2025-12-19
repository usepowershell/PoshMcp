# Example PowerShell Initialization Script for PoshMcp
# This script runs once when a PowerShell runspace is created
# Use it to set up your custom environment, functions, modules, and variables

# Set up custom variables
$global:McpInitializationTime = Get-Date
Write-Host "Custom MCP initialization at $McpInitializationTime" -ForegroundColor Cyan

# Example: Import commonly used modules
# Import-Module Microsoft.PowerShell.Management -ErrorAction SilentlyContinue
# Import-Module Microsoft.PowerShell.Utility -ErrorAction SilentlyContinue

# Example: Set up custom paths
# $env:CUSTOM_PATH = "C:\MyCustomPath"
# $env:PSModulePath += ";C:\MyModules"

# Example: Create helper functions available to all MCP tools
function Get-McpInfo {
    <#
    .SYNOPSIS
        Returns information about the MCP session
    .DESCRIPTION
        Provides details about the current PowerShell session initialized by MCP
    #>
    return @{
        InitTime         = $global:McpInitializationTime
        PSVersion        = $PSVersionTable.PSVersion.ToString()
        WorkingDirectory = (Get-Location).Path
        ModuleCount      = (Get-Module).Count
        FunctionCount    = (Get-ChildItem Function:).Count
        VariableCount    = (Get-Variable).Count
    }
}

function Write-McpLog {
    <#
    .SYNOPSIS
        Writes a formatted log message
    .PARAMETER Message
        The message to log
    .PARAMETER Level
        Log level (Info, Warning, Error)
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,

        [Parameter(Mandatory = $false)]
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

# Example: Set default error action preference
$ErrorActionPreference = 'Stop'

# Example: Configure output formatting
# $FormatEnumerationLimit = 10

# Example: Load custom configuration
# $customConfig = Get-Content -Path "custom-config.json" -Raw | ConvertFrom-Json
# $global:McpConfig = $customConfig

# Example: Initialize logging
# Write-McpLog "MCP PowerShell session initialized successfully" -Level Info

# Example: Custom data persistence
$global:McpSessionData = @{
    StartTime    = $global:McpInitializationTime
    RequestCount = 0
    CustomData   = @{}
}

function Get-McpSessionData {
    <#
    .SYNOPSIS
        Retrieves session-scoped data
    #>
    return $global:McpSessionData
}

function Set-McpSessionData {
    <#
    .SYNOPSIS
        Sets session-scoped data
    .PARAMETER Key
        The key for the data
    .PARAMETER Value
        The value to store
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Key,

        [Parameter(Mandatory = $true)]
        $Value
    )

    $global:McpSessionData.CustomData[$Key] = $Value
}

# Example: Network or API configuration
# $global:ApiBaseUrl = "https://api.example.com"
# $global:ApiHeaders = @{
#     "Authorization" = "Bearer $env:API_TOKEN"
#     "Content-Type" = "application/json"
# }

# Example: Database connection setup
# $global:DbConnectionString = "Server=localhost;Database=MyDb;Integrated Security=true;"

Write-Host "Example initialization script completed" -ForegroundColor Green
Write-Host "Custom functions available: Get-McpInfo, Write-McpLog, Get-McpSessionData, Set-McpSessionData" -ForegroundColor Cyan
