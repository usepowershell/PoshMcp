#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Deterministic checks for Invoke-Phase4Gate.ps1 (advisory vs release semantics and
    infrastructure/methodology enforcement). Exits non-zero if any case fails.

.DESCRIPTION
    Proves, against synthetic merged artifacts:
      * advisory mode succeeds (exit 0) ONLY for a valid comparison (pass or threshold breach);
      * release mode fails (exit 1) for a valid threshold breach;
      * BOTH modes fail (exit 2/3) for missing/malformed/partial/schema/provenance/
        methodology/SDK-pair/parity errors;
      * the step summary always preserves the red status on a breach.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$gate = Join-Path $PSScriptRoot 'Invoke-Phase4Gate.ps1'
$work = Join-Path $PSScriptRoot '.gatetest'
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force -Path $work | Out-Null

$script:failures = 0
$script:count = 0

function New-Mode([string]$name, [bool]$allPassed = $true) {
    $scen = @()
    foreach ($s in @('cold_a', 'cold_b', 'warm', 'throughput', 'memory')) {
        $scen += [ordered]@{
            scenario   = "${s}_$($name.ToLower())"
            description = "d"
            unit        = "milliseconds"
            iterations  = 5
            stats       = [ordered]@{ mean = 1.0; p50 = 1.0; p95 = 1.0; p99 = 1.0; min = 1.0; max = 1.0; stdDev = 0.0; sampleCount = 5 }
            rawSamples  = @(1.0, 1.0, 1.0, 1.0, 1.0)
        }
    }
    $checks = @()
    for ($i = 0; $i -lt 5; $i++) {
        $checks += [ordered]@{ ratio = 1.0; passed = $true; baselineValue = 1.0 }
    }
    return [ordered]@{ transportMode = $name; scenarios = $scen; thresholdChecks = $checks; allPassed = $allPassed }
}

function New-BaseArtifact {
    return [ordered]@{
        schemaVersion      = "poshmcp/phase4-comparison/1.0"
        capturedAt         = "2026-08-04T00:00:00Z"
        sdkPackageVersion  = "ModelContextProtocol 2.0.0"
        sdkAssembly        = [ordered]@{ name = "ModelContextProtocol"; informationalVersion = "2.0.0"; fileVersion = "2.0.0"; majorVersion = 2; path = "/x/ModelContextProtocol.dll"; sha256 = "current-sha-2222"; packageDisplay = "ModelContextProtocol 2.0.0" }
        commitSha          = "headsha"
        runtimeInfo        = [ordered]@{ dotNetVersion = "10.0.0"; os = "Linux"; logicalProcessors = 4; machineName = "runnerA"; processorModel = "CPU-X"; totalMemoryKb = 16000000 }
        baselineProvenance = [ordered]@{ sdkAssembly = [ordered]@{ name = "ModelContextProtocol"; informationalVersion = "1.4.1"; majorVersion = 1; sha256 = "baseline-sha-1111" } }
        sameJobPaired      = $true
        modes              = @((New-Mode "Stateless"), (New-Mode "Stateful"))
        warnings           = @()
        overallPassed      = $true
        exitCode           = 0
    }
}

function New-BaselineArtifact {
    # Parity reference: same runtimeInfo as current (same runner/host).
    return [ordered]@{
        schemaVersion = "poshmcp/v1-characterization/1.0"
        capturedAt    = "2026-08-03T00:00:00Z"
        commitSha     = "phase0sha"
        sdkAssembly   = [ordered]@{ majorVersion = 1; sha256 = "baseline-sha-1111" }
        runtimeInfo   = [ordered]@{ dotNetVersion = "10.0.0"; os = "Linux"; logicalProcessors = 4; machineName = "runnerA"; processorModel = "CPU-X"; totalMemoryKb = 16000000 }
        scenarios     = @()
    }
}

function Write-Json($obj, [string]$path) {
    ($obj | ConvertTo-Json -Depth 20) | Out-File -LiteralPath $path -Encoding utf8
}

function Invoke-Gate([string]$artifact, [string]$mode, [string]$baseline = '', [string]$summary = '') {
    $a = @('-NoProfile', '-File', $gate, '-MergedArtifactPath', $artifact, '-Mode', $mode)
    if ($baseline) { $a += @('-BaselineArtifactPath', $baseline) }
    if ($summary) { $env:GITHUB_STEP_SUMMARY = $summary } else { Remove-Item Env:\GITHUB_STEP_SUMMARY -ErrorAction SilentlyContinue }
    & pwsh @a *> $null
    $code = $LASTEXITCODE
    Remove-Item Env:\GITHUB_STEP_SUMMARY -ErrorAction SilentlyContinue
    return $code
}

function Assert-Exit([string]$desc, [int]$actual, [int]$expected) {
    $script:count++
    if ($actual -eq $expected) {
        Write-Host "  PASS  $desc (exit=$actual)"
    }
    else {
        Write-Host "  FAIL  $desc (exit=$actual expected=$expected)"
        $script:failures++
    }
}

# --- Case 1: valid PASS -> advisory 0, release 0 ----------------------------
$p = Join-Path $work 'pass.json'; Write-Json (New-BaseArtifact) $p
Assert-Exit "valid pass / advisory" (Invoke-Gate $p 'advisory') 0
Assert-Exit "valid pass / release"  (Invoke-Gate $p 'release')  0

# --- Case 2: valid threshold BREACH -> advisory 0, release 1 ----------------
$b = New-BaseArtifact
$b.modes[0].thresholdChecks[2].passed = $false
$b.modes[0].thresholdChecks[2].ratio = 1.9
$b.modes[0].allPassed = $false
$b.overallPassed = $false
$b.exitCode = 1
$pb = Join-Path $work 'breach.json'; Write-Json $b $pb
Assert-Exit "valid breach / advisory (non-blocking)" (Invoke-Gate $pb 'advisory') 0
Assert-Exit "valid breach / release (RED)"           (Invoke-Gate $pb 'release')  1

# --- Case 3: missing file -> 3 both -----------------------------------------
$missing = Join-Path $work 'nope.json'
Assert-Exit "missing / advisory" (Invoke-Gate $missing 'advisory') 3
Assert-Exit "missing / release"  (Invoke-Gate $missing 'release')  3

# --- Case 4: malformed JSON -> 3 both ---------------------------------------
$mal = Join-Path $work 'malformed.json'; "{ not json" | Out-File -LiteralPath $mal -Encoding utf8
Assert-Exit "malformed / advisory" (Invoke-Gate $mal 'advisory') 3
Assert-Exit "malformed / release"  (Invoke-Gate $mal 'release')  3

# --- Case 5: partial modes -> 2 both ----------------------------------------
$part = New-BaseArtifact; $part.modes = @((New-Mode "Stateless"))
$pp = Join-Path $work 'partial.json'; Write-Json $part $pp
Assert-Exit "partial modes / advisory" (Invoke-Gate $pp 'advisory') 2
Assert-Exit "partial modes / release"  (Invoke-Gate $pp 'release')  2

# --- Case 6: sameJobPaired false -> 2 both ----------------------------------
$sj = New-BaseArtifact; $sj.sameJobPaired = $false
$ps = Join-Path $work 'samejob.json'; Write-Json $sj $ps
Assert-Exit "sameJobPaired=false / advisory" (Invoke-Gate $ps 'advisory') 2
Assert-Exit "sameJobPaired=false / release"  (Invoke-Gate $ps 'release')  2

# --- Case 7: current SDK major wrong (v1) -> 2 both -------------------------
$c1 = New-BaseArtifact; $c1.sdkAssembly.majorVersion = 1; $c1.sdkAssembly.sha256 = "x1"
$pc = Join-Path $work 'curv1.json'; Write-Json $c1 $pc
Assert-Exit "current v1 / advisory" (Invoke-Gate $pc 'advisory') 2
Assert-Exit "current v1 / release"  (Invoke-Gate $pc 'release')  2

# --- Case 8: baseline SDK major wrong (v2) -> 2 both ------------------------
$b2 = New-BaseArtifact; $b2.baselineProvenance.sdkAssembly.majorVersion = 2
$pb2 = Join-Path $work 'basev2.json'; Write-Json $b2 $pb2
Assert-Exit "baseline v2 / advisory" (Invoke-Gate $pb2 'advisory') 2
Assert-Exit "baseline v2 / release"  (Invoke-Gate $pb2 'release')  2

# --- Case 9: identical SDK sha -> 2 both ------------------------------------
$id = New-BaseArtifact; $id.baselineProvenance.sdkAssembly.sha256 = $id.sdkAssembly.sha256
$pidsha = Join-Path $work 'idsha.json'; Write-Json $id $pidsha
Assert-Exit "identical sha / advisory" (Invoke-Gate $pidsha 'advisory') 2
Assert-Exit "identical sha / release"  (Invoke-Gate $pidsha 'release')  2

# --- Case 10: unmeasured scenario -> 2 both ---------------------------------
$um = New-BaseArtifact; $um.modes[1].scenarios[0].iterations = 0; $um.modes[1].scenarios[0].stats.sampleCount = 0
$pum = Join-Path $work 'unmeasured.json'; Write-Json $um $pum
Assert-Exit "unmeasured scenario / advisory" (Invoke-Gate $pum 'advisory') 2
Assert-Exit "unmeasured scenario / release"  (Invoke-Gate $pum 'release')  2

# --- Case 11: overallPassed inconsistent -> 2 both --------------------------
$inc = New-BaseArtifact; $inc.modes[0].thresholdChecks[0].passed = $false; $inc.modes[0].allPassed = $false
# overallPassed left true => inconsistent
$pinc = Join-Path $work 'inconsistent.json'; Write-Json $inc $pinc
Assert-Exit "inconsistent overallPassed / advisory" (Invoke-Gate $pinc 'advisory') 2
Assert-Exit "inconsistent overallPassed / release"  (Invoke-Gate $pinc 'release')  2

# --- Case 12: wrong threshold-check count -> 2 both -------------------------
$wc = New-BaseArtifact; $wc.modes[0].thresholdChecks = @($wc.modes[0].thresholdChecks[0..3])
$pwc = Join-Path $work 'wrongcount.json'; Write-Json $wc $pwc
Assert-Exit "wrong check count / advisory" (Invoke-Gate $pwc 'advisory') 2
Assert-Exit "wrong check count / release"  (Invoke-Gate $pwc 'release')  2

# --- Case 13: methodology parity mismatch -> 2 both -------------------------
$basel = New-BaselineArtifact; $basel.runtimeInfo.os = "Windows"  # differs from current "Linux"
$pbl = Join-Path $work 'baseline-mismatch.json'; Write-Json $basel $pbl
Assert-Exit "parity mismatch / advisory" (Invoke-Gate $p 'advisory' $pbl) 2
Assert-Exit "parity mismatch / release"  (Invoke-Gate $p 'release'  $pbl) 2

# --- Case 14: methodology parity match + breach -> advisory 0, release 1 ----
$baselOk = New-BaselineArtifact
$pblok = Join-Path $work 'baseline-ok.json'; Write-Json $baselOk $pblok
Assert-Exit "parity ok + breach / advisory" (Invoke-Gate $pb 'advisory' $pblok) 0
Assert-Exit "parity ok + breach / release"  (Invoke-Gate $pb 'release'  $pblok) 1

# --- Case 15: summary preserves RED on advisory breach ----------------------
$sumFile = Join-Path $work 'summary.md'
if (Test-Path $sumFile) { Remove-Item $sumFile }
$null = Invoke-Gate $pb 'advisory' '' $sumFile
$script:count++
if ((Test-Path $sumFile) -and ((Get-Content -Raw $sumFile) -match 'RED') -and ((Get-Content -Raw $sumFile) -match 'ADVISORY')) {
    Write-Host "  PASS  advisory summary preserves RED status"
}
else {
    Write-Host "  FAIL  advisory summary does not preserve RED status"
    $script:failures++
}

# --- Case 16: env-derived parity CAPTURE GAP is non-blocking ----------------
# Reproduces the real CI artifact: the current (Phase 4) run leaves processorModel empty and
# totalMemoryKb 0 while the baseline captured them. A one-sided capture gap on ENV-derived
# fields must NOT be a methodology violation — advisory stays 0, release stays 1 (breach), never 2.
$bGap = New-BaseArtifact
$bGap.modes[0].thresholdChecks[2].passed = $false
$bGap.modes[0].thresholdChecks[2].ratio = 1.9
$bGap.modes[0].allPassed = $false
$bGap.overallPassed = $false
$bGap.exitCode = 1
$bGap.runtimeInfo.processorModel = ''
$bGap.runtimeInfo.totalMemoryKb = 0
$pgap = Join-Path $work 'breach-paritygap.json'; Write-Json $bGap $pgap
$baselGap = New-BaselineArtifact   # baseline has processorModel/totalMemoryKb populated
$pblgap = Join-Path $work 'baseline-paritygap.json'; Write-Json $baselGap $pblgap
Assert-Exit "env-parity capture gap / advisory (non-blocking)" (Invoke-Gate $pgap 'advisory' $pblgap) 0
Assert-Exit "env-parity capture gap / release (RED breach, not infra)" (Invoke-Gate $pgap 'release' $pblgap) 1

# --- Case 17: env-derived parity present-on-both but DIFFERENT -> 2 both -----
$bDiff = New-BaseArtifact
$bDiff.runtimeInfo.processorModel = 'CPU-Y'   # baseline is CPU-X -> genuine contamination
$pdiff = Join-Path $work 'parity-procdiff.json'; Write-Json $bDiff $pdiff
$baselDiff = New-BaselineArtifact
$pbldiff = Join-Path $work 'baseline-procdiff.json'; Write-Json $baselDiff $pbldiff
Assert-Exit "env-parity present+differ / advisory" (Invoke-Gate $pdiff 'advisory' $pbldiff) 2
Assert-Exit "env-parity present+differ / release"  (Invoke-Gate $pdiff 'release'  $pbldiff) 2

Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Phase 4 gate checks: $($script:count) run, $($script:failures) failed."
if ($script:failures -gt 0) { exit 1 } else { exit 0 }
