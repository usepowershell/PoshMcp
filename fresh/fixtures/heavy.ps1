# A real, pinned module import, not a sleep masquerading as heavy startup.
Import-Module PSScriptAnalyzer -RequiredVersion 1.24.0 -ErrorAction Stop
$null = Get-Command -Module PSScriptAnalyzer -ErrorAction Stop
