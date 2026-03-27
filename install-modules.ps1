#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Install PowerShell modules with version constraint support.

.DESCRIPTION
    Reusable script for installing PowerShell modules. Supports version constraints
    and can be used in Docker builds, local development, or any PowerShell environment.
    
    Designed to work with Hermes's PowerShell best practices:
    - Proper error handling with ErrorAction Stop
    - Stream-based output (Write-Host for info, Write-Error for errors)
    - Exit codes for CI/CD integration

.PARAMETER Modules
    Space or comma-separated list of module names with optional version constraints.
    Version syntax:
    - ModuleName          - Latest version
    - ModuleName@1.2.3    - Exact version
    - ModuleName@>=1.0.0  - Minimum version
    - ModuleName@<=2.0.0  - Maximum version

.PARAMETER Scope
    Installation scope: 'AllUsers' or 'CurrentUser'. Default: AllUsers

.PARAMETER SkipPublisherCheck
    Skip publisher validation during installation. Default: true

.PARAMETER Force
    Force installation even if module already exists. Default: false

.EXAMPLE
    ./install-modules.ps1 -Modules "Pester PSScriptAnalyzer"
    
.EXAMPLE
    ./install-modules.ps1 -Modules "Az.Accounts@>=2.0.0,Pester@5.5.0" -Scope CurrentUser

.EXAMPLE
    # From environment variable (Docker-friendly)
    $env:INSTALL_PS_MODULES = "Pester PSScriptAnalyzer"
    ./install-modules.ps1
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Modules = $env:INSTALL_PS_MODULES,
    
    [Parameter()]
    [ValidateSet('AllUsers', 'CurrentUser')]
    [string]$Scope = $env:MODULE_INSTALL_SCOPE ?? 'AllUsers',
    
    [Parameter()]
    [bool]$SkipPublisherCheck = [System.Convert]::ToBoolean($env:SKIP_PUBLISHER_CHECK ?? 'true'),
    
    [Parameter()]
    [switch]$Force
)

# Enable strict error handling
$ErrorActionPreference = 'Stop'

# Track installation stats
$script:SuccessCount = 0
$script:FailureCount = 0
$script:SkippedCount = 0

function Write-StatusMessage {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Message,
        
        [Parameter()]
        [ValidateSet('Info', 'Success', 'Warning', 'Error')]
        [string]$Level = 'Info'
    )
    
    $prefix = switch ($Level) {
        'Info'    { '📦' }
        'Success' { '✓' }
        'Warning' { '⚠️' }
        'Error'   { '❌' }
    }
    
    $color = switch ($Level) {
        'Info'    { 'Cyan' }
        'Success' { 'Green' }
        'Warning' { 'Yellow' }
        'Error'   { 'Red' }
    }
    
    Write-Host "$prefix $Message" -ForegroundColor $color
}

function Initialize-PSRepository {
    [CmdletBinding()]
    param()
    
    Write-StatusMessage -Message "Initializing PSGallery repository..." -Level Info
    
    try {
        # Check if PSGallery exists
        $gallery = Get-PSRepository -Name PSGallery -ErrorAction SilentlyContinue
        
        if (-not $gallery) {
            Write-StatusMessage -Message "PSGallery not found, registering..." -Level Warning
            Register-PSRepository -Default -ErrorAction SilentlyContinue
        }
        
        # Set as trusted
        Set-PSRepository -Name PSGallery -InstallationPolicy Trusted -ErrorAction Stop
        Write-StatusMessage -Message "PSGallery is trusted and ready" -Level Success
    }
    catch {
        Write-Error "Failed to initialize PSGallery: $_"
        exit 1
    }
}

function Install-ModuleWithVersion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$ModuleSpec,
        
        [Parameter(Mandatory)]
        [string]$InstallScope,
        
        [Parameter()]
        [bool]$SkipCheck,
        
        [Parameter()]
        [bool]$ForceInstall
    )
    
    # Parse module name and version constraint
    if ($ModuleSpec -match '^([^@]+)@(.+)$') {
        $moduleName = $matches[1]
        $versionSpec = $matches[2]
    }
    else {
        $moduleName = $ModuleSpec
        $versionSpec = $null
    }
    
    Write-StatusMessage -Message "Processing module: $moduleName" -Level Info
    
    # Check if module already exists (unless Force is specified)
    if (-not $ForceInstall) {
        $existingModule = Get-Module -ListAvailable -Name $moduleName -ErrorAction SilentlyContinue | 
                          Select-Object -First 1
        
        if ($existingModule) {
            Write-StatusMessage -Message "Module $moduleName (v$($existingModule.Version)) already installed, skipping" -Level Info
            $script:SkippedCount++
            return
        }
    }
    
    try {
        # Build Install-Module parameters
        $installParams = @{
            Name               = $moduleName
            Scope              = $InstallScope
            Force              = $true
            SkipPublisherCheck = $SkipCheck
            ErrorAction        = 'Stop'
        }
        
        # Add version constraint if specified
        if ($versionSpec) {
            Write-StatusMessage -Message "Installing $moduleName with version constraint: $versionSpec" -Level Info
            
            if ($versionSpec -match '^>=(.+)$') {
                $minVersion = $matches[1]
                $installParams['MinimumVersion'] = $minVersion
            }
            elseif ($versionSpec -match '^<=(.+)$') {
                $maxVersion = $matches[1]
                $installParams['MaximumVersion'] = $maxVersion
            }
            elseif ($versionSpec -match '^>(.+)$') {
                # Greater than - use minimum version and filter manually
                $minVersion = $matches[1]
                Write-StatusMessage -Message "Version constraint >$minVersion not directly supported, using >=$minVersion" -Level Warning
                $installParams['MinimumVersion'] = $minVersion
            }
            elseif ($versionSpec -match '^<(.+)$') {
                # Less than - use maximum version
                $maxVersion = $matches[1]
                $installParams['MaximumVersion'] = $maxVersion
            }
            else {
                # Exact version
                $installParams['RequiredVersion'] = $versionSpec
            }
        }
        else {
            Write-StatusMessage -Message "Installing $moduleName (latest version)" -Level Info
        }
        
        # Install the module
        Install-Module @installParams
        
        # Verify installation
        $installed = Get-Module -ListAvailable -Name $moduleName -ErrorAction SilentlyContinue | 
                     Select-Object -First 1
        
        if ($installed) {
            Write-StatusMessage -Message "Successfully installed: $moduleName v$($installed.Version)" -Level Success
            $script:SuccessCount++
        }
        else {
            throw "Module installation completed but module not found"
        }
    }
    catch {
        Write-StatusMessage -Message "Failed to install module $moduleName" -Level Error
        Write-Error ("Module installation failed for {0} - {1}" -f $moduleName, $_.Exception.Message)
        $script:FailureCount++
        
        # Exit immediately on failure for fail-fast behavior
        exit 1
    }
}

function Show-InstallationSummary {
    [CmdletBinding()]
    param()
    
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "  PowerShell Module Installation Summary" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  ✓ Successful:  $script:SuccessCount" -ForegroundColor Green
    Write-Host "  ⊘ Skipped:     $script:SkippedCount" -ForegroundColor Yellow
    Write-Host "  ❌ Failed:      $script:FailureCount" -ForegroundColor Red
    Write-Host ""
    
    if ($script:SuccessCount -gt 0) {
        Write-Host "Installed modules:" -ForegroundColor Cyan
        Get-Module -ListAvailable | 
            Sort-Object Name, Version -Descending | 
            Group-Object Name | 
            ForEach-Object { $_.Group | Select-Object -First 1 } |
            Format-Table Name, Version, Path -AutoSize |
            Out-String |
            Write-Host
    }
    
    Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan
}

# Main execution
try {
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "  PowerShell Module Installation Script" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""
    
    # Validate input
    if ([string]::IsNullOrWhiteSpace($Modules)) {
        Write-StatusMessage -Message "No modules specified. Set -Modules parameter or INSTALL_PS_MODULES environment variable." -Level Warning
        Write-Host ""
        Write-Host "Usage examples:" -ForegroundColor Yellow
        Write-Host "  ./install-modules.ps1 -Modules 'Pester PSScriptAnalyzer'" -ForegroundColor White
        Write-Host "  ./install-modules.ps1 -Modules 'Az.Accounts@>=2.0.0,Pester@5.5.0'" -ForegroundColor White
        Write-Host "  env INSTALL_PS_MODULES='Pester' ./install-modules.ps1" -ForegroundColor White
        Write-Host ""
        exit 0
    }
    
    Write-StatusMessage -Message "Configuration:" -Level Info
    Write-Host "  Modules:             $Modules" -ForegroundColor White
    Write-Host "  Scope:               $Scope" -ForegroundColor White
    Write-Host "  Skip Publisher Check: $SkipPublisherCheck" -ForegroundColor White
    Write-Host "  Force Reinstall:     $Force" -ForegroundColor White
    Write-Host ""
    
    # Initialize PSGallery
    Initialize-PSRepository
    Write-Host ""
    
    # Parse module list (support both space and comma separation)
    $moduleList = $Modules -replace '[,;]', ' ' -split '\s+' | Where-Object { $_ }
    
    Write-StatusMessage -Message "Found $($moduleList.Count) module(s) to process" -Level Info
    Write-Host ""
    
    # Install each module
    foreach ($moduleSpec in $moduleList) {
        Install-ModuleWithVersion -ModuleSpec $moduleSpec `
                                  -InstallScope $Scope `
                                  -SkipCheck $SkipPublisherCheck `
                                  -ForceInstall $Force
        Write-Host ""
    }
    
    # Show summary
    Show-InstallationSummary
    
    # Exit with appropriate code
    if ($script:FailureCount -gt 0) {
        exit 1
    }
    else {
        exit 0
    }
}
catch {
    Write-StatusMessage -Message "Unexpected error occurred" -Level Error
    Write-Error $_.Exception.Message
    exit 1
}
