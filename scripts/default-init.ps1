# Default initialization script for PoshMcp
# This script runs once when a PowerShell runspace is created

# Set up some useful variables
$global:McpServerStartTime = Get-Date
$global:McpServerVersion = '1.0.0'

# Create a function to get session info
function Get-McpSessionInfo {
    <#
    .SYNOPSIS
        Returns information about the MCP PowerShell session
    .DESCRIPTION
        Provides details about the current PowerShell session initialized by MCP
    #>
    return @{
        StartTime = $global:McpServerStartTime
        Version   = $global:McpServerVersion
        Location  = (Get-Location).Path
        Variables = (Get-Variable | Measure-Object).Count
        Functions = (Get-ChildItem Function: | Measure-Object).Count
        Modules   = (Get-Module | Measure-Object).Count
    }
}

# Example function to demonstrate state persistence
function Get-SomeData {
    <#
    .SYNOPSIS
        Returns persistent data from the MCP server
    .PARAMETER test
        Optional test parameter with default value
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $false)]
        [string]$test = 'This is some persistent data from the MCP server.'
    )
    
    return $test
}

Write-Host 'MCP PowerShell session initialized' -ForegroundColor Green
