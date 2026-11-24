#requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Import-Module PSScriptAnalyzer -RequiredVersion 1.24.0 -ErrorAction Stop
$root = Split-Path -Parent $PSScriptRoot
$diagnostics = @()
foreach ($path in @('PoshMcp.Foundation/worker.ps1', 'fixtures', 'eng')) {
    $resolved = Join-Path $root $path
    $diagnostics += @(Invoke-ScriptAnalyzer -Path $resolved -Recurse -Severity Error, Warning)
    $diagnostics += @(Invoke-ScriptAnalyzer -Path $resolved -Recurse -IncludeRule PSUseCompatibleSyntax -Settings (Join-Path $PSScriptRoot 'PSScriptAnalyzerSettings.psd1'))
}
if ($diagnostics.Count -gt 0) {
    $diagnostics | Format-Table -AutoSize | Out-String | Write-Output
    throw 'PowerShell default lint/5.1 syntax compatibility failed.'
}
$negativeLint = @(Invoke-ScriptAnalyzer -ScriptDefinition 'Write-Host "deliberate lint failure"' -Severity Error, Warning)
$negativeSyntax = @(Invoke-ScriptAnalyzer -ScriptDefinition '$value = $true ? 1 : 0' -IncludeRule PSUseCompatibleSyntax -Settings (Join-Path $PSScriptRoot 'PSScriptAnalyzerSettings.psd1'))
if ($negativeLint.Count -eq 0 -or $negativeSyntax.Count -eq 0) {
    throw 'A negative lint/compatibility fixture was not rejected.'
}
