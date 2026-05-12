#requires -Version 7.0
<#
.SYNOPSIS
    Captures pre-spec-010 baseline `tools/list` snapshots for both runtime modes.

.DESCRIPTION
    Implements FR-550 step 1: for each of `InProcess` and `OutOfProcess` runtime
    modes, this script launches the PoshMcp server over stdio, completes the MCP
    initialization handshake, sends `tools/list`, and persists the pretty-printed
    response under `specs/010-tool-self-documentation/baseline/{mode}-tools-list.json`.

    The reference module set is:
      - `Microsoft.PowerShell.Management` (ships with PowerShell)
      - `HelpParityFixture` (PoshMcp.Tests/Fixtures/Modules/HelpParityFixture)

.NOTES
    Run from the repository root. The server is built once in Release before
    capture. Each capture run uses a temporary appsettings.json so the live
    repo configuration is not modified.
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))),
    [int]$ResponseTimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'

$serverProject = Join-Path $RepoRoot 'PoshMcp.Server' 'PoshMcp.csproj'
$serverAssembly = Join-Path $RepoRoot 'PoshMcp.Server' 'bin' 'Release' 'net10.0' 'PoshMcp.dll'
$baselineDir = Join-Path $RepoRoot 'specs' '010-tool-self-documentation' 'baseline'
$fixtureModule = Join-Path $RepoRoot 'PoshMcp.Tests' 'Fixtures' 'Modules' 'HelpParityFixture' 'HelpParityFixture.psd1'

if (-not (Test-Path -Path $fixtureModule -PathType Leaf)) {
    throw "Fixture module not found at $fixtureModule"
}

Write-Host "Building PoshMcp.Server (Release)..." -ForegroundColor Cyan
& dotnet build $serverProject -c Release --nologo -v:quiet | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Server build failed with exit code $LASTEXITCODE"
}
if (-not (Test-Path -Path $serverAssembly -PathType Leaf)) {
    throw "Server assembly not produced at $serverAssembly"
}

if (-not (Test-Path -Path $baselineDir)) {
    New-Item -Path $baselineDir -ItemType Directory -Force | Out-Null
}

function New-CaptureAppSettings {
    param(
        [Parameter(Mandatory)][string]$RuntimeMode,
        [Parameter(Mandatory)][string]$FixturePath
    )

    $fixtureParent = Split-Path -Parent (Split-Path -Parent $FixturePath)

    $config = [ordered]@{
        Logging = [ordered]@{
            LogLevel = [ordered]@{
                Default = 'Warning'
            }
        }
        PowerShellConfiguration = [ordered]@{
            RuntimeMode = $RuntimeMode
            # CommandNames lists the fixture functions explicitly so the
            # in-process discovery path triggers PowerShell command auto-
            # loading from PSModulePath (the fixture root is appended to
            # PSModulePath in the spawn environment, see Invoke-Capture).
            # The OOP path also honours functionNames, so listing them here
            # makes both modes see the same fixture command set.
            CommandNames = @(
                'Get-FixtureSynopsisOnly',
                'Get-FixtureFullHelp',
                'Get-FixtureHelpMessageOnly',
                'Get-FixtureValidateSetScalar',
                'Get-FixtureValidateSetArray',
                'Get-FixtureBare'
            )
            Modules = @(
                'Microsoft.PowerShell.Management',
                'HelpParityFixture'
            )
            ExcludePatterns = @()
            # `*` is required by the OOP discovery path (see oop-host.ps1
            # Invoke-DiscoverHandler) to enumerate all commands in the
            # imported modules. The in-process path treats the same value
            # as the no-filter default, so the two modes see the same
            # effective command set.
            IncludePatterns = @('*')
            EnableDynamicReloadTools = $false
            EnableConfigurationTroubleshootingTool = $false
            Performance = [ordered]@{
                EnableResultCaching = $false
                UseDefaultDisplayProperties = $true
            }
            SubprocessHostMode = 'Pool'
            Environment = [ordered]@{
                # ModulePaths is consumed by the OOP setup handler and pushes
                # the fixture root onto the subprocess PSModulePath. The
                # in-process path picks up the same path via the spawn-time
                # PSModulePath environment variable set in Invoke-Capture.
                ModulePaths = @($fixtureParent)
                ImportModules = @('HelpParityFixture')
            }
        }
        McpServer = [ordered]@{
            IdleSessionTimeoutSeconds = 60
        }
        Authentication = [ordered]@{
            Enabled = $false
        }
    }

    $temp = [System.IO.Path]::GetTempFileName()
    $jsonPath = [System.IO.Path]::ChangeExtension($temp, '.json')
    Remove-Item -Path $temp -Force -ErrorAction SilentlyContinue
    $config | ConvertTo-Json -Depth 10 | Set-Content -Path $jsonPath -Encoding utf8
    return $jsonPath
}

function Invoke-Capture {
    param(
        [Parameter(Mandatory)][string]$RuntimeMode,
        [Parameter(Mandatory)][string]$ConfigPath,
        [Parameter(Mandatory)][string]$OutputPath,
        [Parameter(Mandatory)][string]$FixtureModuleRoot
    )

    Write-Host "Capturing tools/list for runtime mode '$RuntimeMode'..." -ForegroundColor Cyan

    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = 'dotnet'
    $psi.ArgumentList.Add($serverAssembly)
    $psi.ArgumentList.Add('serve')
    $psi.ArgumentList.Add('--config')
    $psi.ArgumentList.Add($ConfigPath)
    $psi.ArgumentList.Add('--transport')
    $psi.ArgumentList.Add('stdio')
    $psi.ArgumentList.Add('--log-level')
    $psi.ArgumentList.Add('Warning')
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.WorkingDirectory = $RepoRoot

    # Prepend the fixture module root to PSModulePath so the in-process
    # PowerShell runspace auto-loads HelpParityFixture by simple name.
    $existingPSModulePath = $env:PSModulePath
    $sep = [System.IO.Path]::PathSeparator
    $effectivePSModulePath = if ([string]::IsNullOrEmpty($existingPSModulePath)) {
        $FixtureModuleRoot
    } else {
        "$FixtureModuleRoot$sep$existingPSModulePath"
    }
    $psi.EnvironmentVariables['PSModulePath'] = $effectivePSModulePath

    $proc = [System.Diagnostics.Process]::Start($psi)

    $stderrBuf = [System.Text.StringBuilder]::new()
    $errReader = Register-ObjectEvent -InputObject $proc -EventName ErrorDataReceived -Action {
        if ($null -ne $EventArgs.Data) {
            [void]$Event.MessageData.AppendLine($EventArgs.Data)
        }
    } -MessageData $stderrBuf
    $proc.BeginErrorReadLine()

    try {
        $send = {
            param([object]$Payload)
            $line = ($Payload | ConvertTo-Json -Depth 20 -Compress)
            $proc.StandardInput.WriteLine($line)
            $proc.StandardInput.Flush()
        }

        & $send @{
            jsonrpc = '2.0'
            id = 1
            method = 'initialize'
            params = @{
                protocolVersion = '2024-11-05'
                capabilities = @{ tools = @{} }
                clientInfo = @{ name = 'spec010-baseline-capture'; version = '1.0.0' }
            }
        }

        $initResp = $null
        $deadline = [DateTime]::UtcNow.AddSeconds($ResponseTimeoutSeconds)
        while ([DateTime]::UtcNow -lt $deadline) {
            $line = $proc.StandardOutput.ReadLine()
            if ($null -eq $line) { break }
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            try {
                $parsed = $line | ConvertFrom-Json -Depth 50
                if ($parsed.id -eq 1 -and $parsed.result) {
                    $initResp = $parsed
                    break
                }
            } catch {
                # Ignore non-JSON lines
            }
        }
        if ($null -eq $initResp) {
            throw "Did not receive initialize response within $ResponseTimeoutSeconds seconds"
        }

        & $send @{
            jsonrpc = '2.0'
            method = 'notifications/initialized'
        }

        & $send @{
            jsonrpc = '2.0'
            id = 2
            method = 'tools/list'
        }

        $listResp = $null
        $deadline = [DateTime]::UtcNow.AddSeconds($ResponseTimeoutSeconds)
        while ([DateTime]::UtcNow -lt $deadline) {
            $line = $proc.StandardOutput.ReadLine()
            if ($null -eq $line) { break }
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            try {
                $parsed = $line | ConvertFrom-Json -Depth 50
                if ($parsed.id -eq 2 -and $parsed.result) {
                    $listResp = $parsed
                    break
                }
            } catch {
                # Ignore non-JSON lines (e.g. log output that slipped through)
            }
        }
        if ($null -eq $listResp) {
            throw "Did not receive tools/list response within $ResponseTimeoutSeconds seconds"
        }

        # Persist the full JSON-RPC envelope (jsonrpc + id + result) for diff-friendly
        # snapshotting; consumers can project to .result.tools if needed.
        $pretty = $listResp | ConvertTo-Json -Depth 50
        # Force LF line endings + 2-space indent so the file is stable across hosts.
        $pretty = $pretty -replace "`r`n", "`n"
        Set-Content -Path $OutputPath -Value $pretty -Encoding utf8NoBOM -NoNewline
        Write-Host "  → wrote $OutputPath ($(($listResp.result.tools).Count) tools)" -ForegroundColor Green
    }
    finally {
        try {
            if (-not $proc.HasExited) {
                $proc.StandardInput.Close()
                if (-not $proc.WaitForExit(5000)) {
                    $proc.Kill($true)
                }
            }
        } catch {}
        Unregister-Event -SourceIdentifier $errReader.Name -ErrorAction SilentlyContinue
        Remove-Job -Job $errReader -Force -ErrorAction SilentlyContinue
        $stderrText = $stderrBuf.ToString().Trim()
        if ($stderrText) {
            Write-Verbose ("Server stderr for {0}: {1}" -f $RuntimeMode, $stderrText)
        }
    }
}

$modes = @(
    @{ Mode = 'InProcess'; Out = Join-Path $baselineDir 'inprocess-tools-list.json' }
    @{ Mode = 'OutOfProcess'; Out = Join-Path $baselineDir 'oop-tools-list.json' }
)

foreach ($entry in $modes) {
    $configPath = New-CaptureAppSettings -RuntimeMode $entry.Mode -FixturePath $fixtureModule
    try {
        $fixtureModuleRoot = Split-Path -Parent (Split-Path -Parent $fixtureModule)
        Invoke-Capture -RuntimeMode $entry.Mode -ConfigPath $configPath -OutputPath $entry.Out -FixtureModuleRoot $fixtureModuleRoot
    }
    finally {
        Remove-Item -Path $configPath -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "Baseline capture complete." -ForegroundColor Green
