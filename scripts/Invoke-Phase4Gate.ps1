#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Classify a Phase 4 performance-comparison result and enforce the gate per mode.

.DESCRIPTION
    Single source of truth (used by CI, releases, and tests) that distinguishes a
    THRESHOLD_BREACH from an INFRASTRUCTURE/METHODOLOGY_FAILURE on a merged Phase 4
    comparison artifact:

      * INFRA / METHODOLOGY failure  -> always fails (both advisory and release modes).
        Missing/malformed artifact, provenance/SDK-pair violation, missing/partial modes,
        unmeasured scenarios, same-job-paired = false, overallPassed inconsistent with the
        threshold checks, or (when a baseline is supplied) a methodology-parity mismatch.

      * Valid comparison, only red thresholds -> advisory (exit 0, prominent warnings) on PRs,
        RELEASE-RED (exit 1) when -Mode release.

    The step summary and annotations ALWAYS preserve the red status; advisory never rewrites
    overallPassed to true and never discards a breach.

.OUTPUTS
    Exit codes:
      0  PASS (valid comparison, all thresholds within limits) OR advisory threshold breach.
      1  RELEASE gate RED (valid comparison with >=1 threshold breach, -Mode release).
      2  INFRASTRUCTURE / METHODOLOGY failure (validity/provenance/parity).
      3  Missing or unparseable merged artifact.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$MergedArtifactPath,
    [Parameter(Mandatory)][ValidateSet('advisory', 'release')][string]$Mode,
    [string]$BaselineArtifactPath = '',
    [int]$ExpectedBaselineMajor = 1,
    [int]$ExpectedCurrentMajor = 2,
    [string[]]$ExpectedModes = @('Stateless', 'Stateful'),
    [int]$ExpectedThresholdChecksPerMode = 5
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- output helpers ---------------------------------------------------------
$script:SummaryLines = New-Object System.Collections.Generic.List[string]

function Add-Summary([string]$line) {
    $script:SummaryLines.Add($line) | Out-Null
    Write-Host $line
}

function Emit-Annotation([string]$level, [string]$message) {
    # level: error | warning | notice
    Write-Host "::${level}::${message}"
}

function Flush-Summary {
    if ($env:GITHUB_STEP_SUMMARY) {
        $script:SummaryLines | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
    }
}

function Complete([int]$code) {
    Flush-Summary
    exit $code
}

# --- load merged artifact ---------------------------------------------------
Add-Summary "## Phase 4 gate classification (mode=$Mode)"

if (-not (Test-Path -LiteralPath $MergedArtifactPath)) {
    Emit-Annotation 'error' "Phase 4 merged artifact not found: $MergedArtifactPath (INFRASTRUCTURE failure)."
    Add-Summary "- **INFRASTRUCTURE failure**: merged artifact missing (`$MergedArtifactPath`)."
    Complete 3
}

try {
    $raw = Get-Content -LiteralPath $MergedArtifactPath -Raw
    $art = $raw | ConvertFrom-Json
}
catch {
    Emit-Annotation 'error' "Phase 4 merged artifact is not valid JSON: $($_.Exception.Message) (INFRASTRUCTURE failure)."
    Add-Summary "- **INFRASTRUCTURE failure**: merged artifact is not parseable JSON."
    Complete 3
}

# --- validity / methodology enforcement (fail-closed) -----------------------
$violations = New-Object System.Collections.Generic.List[string]

function Has-Prop($obj, [string]$name) {
    return ($null -ne $obj) -and ($obj.PSObject.Properties.Name -contains $name)
}

foreach ($required in @('schemaVersion', 'sdkAssembly', 'baselineProvenance', 'sameJobPaired', 'modes', 'overallPassed')) {
    if (-not (Has-Prop $art $required)) {
        $violations.Add("missing required field '$required'")
    }
}

# Provenance / SDK migration-pair (mirrors PerformanceComparator.ValidateSdkVersionPair).
$curMajor = $null; $curSha = $null
if ((Has-Prop $art 'sdkAssembly') -and ($null -ne $art.sdkAssembly)) {
    if (Has-Prop $art.sdkAssembly 'majorVersion') { $curMajor = [int]$art.sdkAssembly.majorVersion }
    if (Has-Prop $art.sdkAssembly 'sha256') { $curSha = [string]$art.sdkAssembly.sha256 }
}
$baseMajor = $null; $baseSha = $null
if ((Has-Prop $art 'baselineProvenance') -and $null -ne $art.baselineProvenance -and (Has-Prop $art.baselineProvenance 'sdkAssembly') -and $null -ne $art.baselineProvenance.sdkAssembly) {
    $bsdk = $art.baselineProvenance.sdkAssembly
    if (Has-Prop $bsdk 'majorVersion') { $baseMajor = [int]$bsdk.majorVersion }
    if (Has-Prop $bsdk 'sha256') { $baseSha = [string]$bsdk.sha256 }
}

if ($null -eq $curMajor) { $violations.Add("current sdkAssembly.majorVersion is missing") }
elseif ($curMajor -ne $ExpectedCurrentMajor) { $violations.Add("current SDK major is $curMajor, expected $ExpectedCurrentMajor") }
if ($null -eq $baseMajor) { $violations.Add("baseline sdkAssembly.majorVersion is missing") }
elseif ($baseMajor -ne $ExpectedBaselineMajor) { $violations.Add("baseline SDK major is $baseMajor, expected $ExpectedBaselineMajor") }
if ($null -ne $curMajor -and $null -ne $baseMajor -and $curMajor -eq $baseMajor) {
    $violations.Add("baseline and current SDK majors are identical ($curMajor) — not a v1->v2 migration comparison")
}
if ([string]::IsNullOrWhiteSpace($curSha) -or [string]::IsNullOrWhiteSpace($baseSha)) {
    $violations.Add("baseline/current SDK sha256 missing")
}
elseif ($curSha -eq $baseSha) {
    $violations.Add("baseline and current ModelContextProtocol.dll SHA-256 are identical — same binary")
}

# Same-job paired must be a real true (never hardcoded downstream).
if ((Has-Prop $art 'sameJobPaired') -and ($art.sameJobPaired -ne $true)) {
    $violations.Add("sameJobPaired is '$($art.sameJobPaired)', expected true")
}

# Completeness: exactly the expected transport modes, each fully measured.
$seenModes = @()
if ((Has-Prop $art 'modes') -and $null -ne $art.modes) {
    $seenModes = @($art.modes | ForEach-Object { [string]$_.transportMode })
}
$expectedSorted = ($ExpectedModes | Sort-Object) -join ','
$seenSorted = ($seenModes | Sort-Object) -join ','
if ($expectedSorted -ne $seenSorted) {
    $violations.Add("modes present = [$seenSorted], expected exactly [$expectedSorted]")
}

$breaches = New-Object System.Collections.Generic.List[string]
$allChecksPassed = $true
if ((Has-Prop $art 'modes') -and $null -ne $art.modes) {
    foreach ($m in $art.modes) {
        $tm = [string]$m.transportMode
        $checks = @()
        if ((Has-Prop $m 'thresholdChecks') -and ($null -ne $m.thresholdChecks)) { $checks = @($m.thresholdChecks) }
        if ($checks.Count -ne $ExpectedThresholdChecksPerMode) {
            $violations.Add("mode '$tm' has $($checks.Count) threshold checks, expected $ExpectedThresholdChecksPerMode")
        }
        $scenarios = @()
        if ((Has-Prop $m 'scenarios') -and ($null -ne $m.scenarios)) { $scenarios = @($m.scenarios) }
        if ($scenarios.Count -eq 0) {
            $violations.Add("mode '$tm' has no measured scenarios")
        }
        foreach ($s in $scenarios) {
            $iter = 0; if (Has-Prop $s 'iterations') { $iter = [int]$s.iterations }
            $sc = 0; if ((Has-Prop $s 'stats') -and (Has-Prop $s.stats 'sampleCount')) { $sc = [int]$s.stats.sampleCount }
            if ($iter -le 0 -or $sc -le 0) {
                $violations.Add("mode '$tm' scenario '$($s.scenario)' is unmeasured (iterations=$iter sampleCount=$sc)")
            }
        }
        $idx = 0
        foreach ($c in $checks) {
            $passed = $true; if (Has-Prop $c 'passed') { $passed = [bool]$c.passed }
            if (-not $passed) {
                $allChecksPassed = $false
                $ratio = if (Has-Prop $c 'ratio') { [double]$c.ratio } else { [double]::NaN }
                $breaches.Add(("{0}/check#{1} ratio={2:N3}" -f $tm, $idx, $ratio))
            }
            $idx++
        }
    }
}

# overallPassed must be consistent with the checks (no silent disagreement).
$overall = $false; if (Has-Prop $art 'overallPassed') { $overall = [bool]$art.overallPassed }
if ($overall -ne $allChecksPassed) {
    $violations.Add("overallPassed=$overall disagrees with aggregate threshold checks (allPassed=$allChecksPassed)")
}

# --- optional methodology parity vs baseline (fail-closed) ------------------
if (-not [string]::IsNullOrWhiteSpace($BaselineArtifactPath)) {
    if (-not (Test-Path -LiteralPath $BaselineArtifactPath)) {
        $violations.Add("baseline artifact for parity not found: $BaselineArtifactPath")
    }
    else {
        try {
            $base = Get-Content -LiteralPath $BaselineArtifactPath -Raw | ConvertFrom-Json
        }
        catch {
            $base = $null
            $violations.Add("baseline artifact for parity is not parseable JSON")
        }
        if ($null -ne $base -and (Has-Prop $base 'runtimeInfo') -and (Has-Prop $art 'runtimeInfo')) {
            # These fields are recorded by the SAME test host on the SAME runner for both
            # phases (only the measured server DLL differs), so they MUST match. A mismatch
            # means cross-runner/toolchain contamination. SDK version and commit SHA are the
            # only intentional migration differences and are NOT compared here.
            foreach ($f in @('dotNetVersion', 'os', 'logicalProcessors', 'processorModel', 'totalMemoryKb', 'machineName')) {
                $bv = if (Has-Prop $base.runtimeInfo $f) { [string]$base.runtimeInfo.$f } else { '<missing>' }
                $cv = if (Has-Prop $art.runtimeInfo $f) { [string]$art.runtimeInfo.$f } else { '<missing>' }
                if ($bv -ne $cv) {
                    $violations.Add("methodology parity mismatch on runtimeInfo.$f (baseline='$bv' current='$cv')")
                }
            }
        }
        elseif ($null -ne $base) {
            $violations.Add("methodology parity: runtimeInfo missing on baseline or current")
        }
    }
}

if ($violations.Count -gt 0) {
    Emit-Annotation 'error' "Phase 4 INFRASTRUCTURE/METHODOLOGY failure: $($violations.Count) violation(s). This fails BOTH advisory and release."
    Add-Summary "- **INFRASTRUCTURE / METHODOLOGY failure** ($($violations.Count) violation(s)):"
    foreach ($v in $violations) {
        Emit-Annotation 'error' "  - $v"
        Add-Summary "  - $v"
    }
    Complete 2
}

# --- valid comparison: report thresholds (red status always preserved) ------
Add-Summary "- Valid comparison: SDK v$baseMajor (baseline) vs v$curMajor (current), distinct binaries, sameJobPaired=true, all scenarios measured."
Add-Summary "- overallPassed=$overall"

if ($overall) {
    Emit-Annotation 'notice' "Phase 4 gate PASSED: valid v$baseMajor->v$curMajor comparison, all thresholds within limits."
    Add-Summary "- Result: **GREEN** (all thresholds within limits)."
    Complete 0
}

# Valid comparison with >=1 threshold breach.
Add-Summary "- Result: **RED** — $($breaches.Count) threshold breach(es):"
foreach ($b in $breaches) {
    Add-Summary "  - $b"
}

$annLevel = if ($Mode -eq 'release') { 'error' } else { 'warning' }
foreach ($b in $breaches) {
    Emit-Annotation $annLevel "Phase 4 threshold breach: $b"
}

if ($Mode -eq 'release') {
    Emit-Annotation 'error' "RELEASE GATE RED: valid v$baseMajor->v$curMajor comparison with $($breaches.Count) threshold breach(es). Exiting non-zero."
    Add-Summary "- **RELEASE gate: FAIL (exit 1).**"
    Complete 1
}
else {
    Emit-Annotation 'warning' "ADVISORY: valid v$baseMajor->v$curMajor comparison with $($breaches.Count) threshold breach(es). Non-blocking on PR; the RELEASE gate would FAIL. Status remains RED."
    Add-Summary "- **ADVISORY (PR): non-blocking (exit 0).** The release gate would FAIL — red status is preserved, not cleared."
    Complete 0
}
