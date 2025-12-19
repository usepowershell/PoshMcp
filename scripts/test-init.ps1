# Test initialization script for PoshMcp
# This script demonstrates custom initialization

Write-Host "=== Custom PoshMcp Initialization ===" -ForegroundColor Cyan

# Set a global variable
$global:MyCustomValue = "Hello from custom init script!"
$global:InitTimestamp = Get-Date

# Create a custom function
function Get-CustomGreeting {
    param([string]$Name = "World")
    return "Hello, $Name! Initialized at $global:InitTimestamp"
}

# Create another helper function
function Get-InitStatus {
    return @{
        CustomValue             = $global:MyCustomValue
        InitTime                = $global:InitTimestamp
        CustomFunctionAvailable = (Get-Command Get-CustomGreeting -ErrorAction SilentlyContinue) -ne $null
    }
}

Write-Host "Custom initialization complete!" -ForegroundColor Green
Write-Host "Available custom functions: Get-CustomGreeting, Get-InitStatus" -ForegroundColor Yellow
