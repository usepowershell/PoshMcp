# Azure Managed Identity Startup Script for PoshMcp
# This script authenticates to Azure using Managed Identity and sets up the Azure context.
# Designed for use in Azure Container Apps, Azure Container Instances, or Azure VMs.

Write-Host "🔐 Azure Managed Identity Startup Script" -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan

# Import required modules
$ErrorActionPreference = 'Stop'

try {
    # Check if Az.Accounts module is available
    if (-not (Get-Module -ListAvailable -Name Az.Accounts)) {
        Write-Host "❌ Az.Accounts module not found. Cannot authenticate to Azure." -ForegroundColor Red
        Write-Host "   Install it with: Install-Module -Name Az.Accounts -Force" -ForegroundColor Yellow
        exit 1
    }

    Write-Host "✅ Az.Accounts module found" -ForegroundColor Green

    # Check if running in an Azure environment with Managed Identity support
    $isManagedIdentityAvailable = $false
    $identityEndpoint = $env:IDENTITY_ENDPOINT
    $identityHeader = $env:IDENTITY_HEADER
    $msiEndpoint = $env:MSI_ENDPOINT
    $msiSecret = $env:MSI_SECRET

    if ($identityEndpoint -ne $null -or $msiEndpoint -ne $null) {
        $isManagedIdentityAvailable = $true
        Write-Host "✅ Managed Identity environment detected" -ForegroundColor Green
        
        if ($identityEndpoint) {
            Write-Host "   Identity Endpoint: $identityEndpoint" -ForegroundColor DarkGray
        }
        if ($msiEndpoint) {
            Write-Host "   MSI Endpoint: $msiEndpoint" -ForegroundColor DarkGray
        }
    }
    else {
        Write-Host "⚠️  Managed Identity environment not detected" -ForegroundColor Yellow
        Write-Host "   This script is designed for Azure Managed Identity environments." -ForegroundColor Yellow
        Write-Host "   Supported: Container Apps, Container Instances, VMs with Managed Identity" -ForegroundColor Yellow
    }

    # Attempt to connect using Managed Identity
    Write-Host "`n🔄 Connecting to Azure with Managed Identity..." -ForegroundColor Cyan

    $connectParams = @{
        Identity = $true
    }

    # If a specific client ID is provided, use it (for user-assigned managed identities)
    if ($env:AZURE_CLIENT_ID) {
        Write-Host "   Using User-Assigned Managed Identity: $env:AZURE_CLIENT_ID" -ForegroundColor DarkGray
        $connectParams.AccountId = $env:AZURE_CLIENT_ID
    }
    else {
        Write-Host "   Using System-Assigned Managed Identity" -ForegroundColor DarkGray
    }

    # Connect to Azure
    $connection = Connect-AzAccount @connectParams

    if ($connection) {
        Write-Host "✅ Successfully authenticated to Azure!" -ForegroundColor Green
        
        # Display connection details
        $context = Get-AzContext
        Write-Host "`n📋 Azure Context:" -ForegroundColor Cyan
        Write-Host "   Account:      $($context.Account.Id)" -ForegroundColor White
        Write-Host "   Subscription: $($context.Subscription.Name) ($($context.Subscription.Id))" -ForegroundColor White
        Write-Host "   Tenant:       $($context.Tenant.Id)" -ForegroundColor White
        Write-Host "   Environment:  $($context.Environment.Name)" -ForegroundColor White

        # If a specific subscription is requested, switch to it
        if ($env:AZURE_SUBSCRIPTION_ID) {
            Write-Host "`n🔄 Switching to subscription: $env:AZURE_SUBSCRIPTION_ID" -ForegroundColor Cyan
            Set-AzContext -SubscriptionId $env:AZURE_SUBSCRIPTION_ID | Out-Null
            Write-Host "✅ Subscription context set" -ForegroundColor Green
        }

        # Set global variables for easy access
        $Global:AzureContext = Get-AzContext
        $Global:AzureSubscriptionId = $Global:AzureContext.Subscription.Id
        $Global:AzureTenantId = $Global:AzureContext.Tenant.Id

        # Create helper function for getting the current context
        function Get-CurrentAzureContext {
            <#
            .SYNOPSIS
            Get the current Azure context established by Managed Identity
            
            .DESCRIPTION
            Returns information about the current Azure connection, including
            subscription, tenant, and account details.
            
            .EXAMPLE
            Get-CurrentAzureContext
            #>
            
            return [PSCustomObject]@{
                Account = $Global:AzureContext.Account.Id
                Subscription = $Global:AzureContext.Subscription.Name
                SubscriptionId = $Global:AzureContext.Subscription.Id
                Tenant = $Global:AzureContext.Tenant.Id
                Environment = $Global:AzureContext.Environment.Name
                AuthenticationType = "ManagedIdentity"
            }
        }

        Write-Host "`n✅ Azure Managed Identity authentication complete!" -ForegroundColor Green
        Write-Host "   Use Get-CurrentAzureContext to view connection details" -ForegroundColor DarkGray
        Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
    }
    else {
        Write-Host "❌ Failed to authenticate to Azure" -ForegroundColor Red
        exit 1
    }
}
catch {
    Write-Host "❌ Error during Azure authentication: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "   $($_.ScriptStackTrace)" -ForegroundColor DarkGray
    
    # Provide troubleshooting guidance
    Write-Host "`n💡 Troubleshooting:" -ForegroundColor Yellow
    Write-Host "   1. Ensure Managed Identity is enabled on this resource" -ForegroundColor Yellow
    Write-Host "   2. Verify the identity has appropriate Azure RBAC permissions" -ForegroundColor Yellow
    Write-Host "   3. Check that Az.Accounts module version is compatible" -ForegroundColor Yellow
    Write-Host "   4. Review environment variables: IDENTITY_ENDPOINT, MSI_ENDPOINT" -ForegroundColor Yellow
    
    exit 1
}
