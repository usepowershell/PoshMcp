# Spec 010 — Pre-change `tools/list` Baseline Snapshots

This directory holds the **pre-implementation** `tools/list` snapshots required
by FR-550 of [spec 010 — Improve MCP Tool Self-Documentation](../spec.md). They
freeze the existing PoshMcp behavior for both runtime modes so that the
post-implementation regression test (`ToolDescriptionRegressionTests`) can
verify the FR-550 backward-compatibility guarantee: every command whose
pre-change description was a non-empty `Get-Help` `.Synopsis` MUST surface
that exact synopsis or a strict superset post-change.

## Files

| File | Contents |
|------|----------|
| `inprocess-tools-list.json` | Full JSON-RPC `tools/list` response from the in-process runtime path. |
| `oop-tools-list.json` | Full JSON-RPC `tools/list` response from the out-of-process runtime path (Pool host mode). |
| `capture-snapshots.ps1` | Reproducible capture script. Re-run to regenerate. |

Both JSON files are the **complete** JSON-RPC envelope (`{ jsonrpc, id, result }`)
pretty-printed with 2-space indent and LF line endings for diff-friendly review.
Tests should project to `result.tools[]` when asserting.

## Reference module set

Per FR-550 step 1 — at minimum `Microsoft.PowerShell.Management` plus the
`HelpParityFixture` module:

- `Microsoft.PowerShell.Management` — ships with PowerShell; supplies the
  general-purpose cmdlet corpus (Get-Process, Get-Item, etc.).
- `HelpParityFixture` — fixture module at
  [`PoshMcp.Tests/Fixtures/Modules/HelpParityFixture/`](../../../PoshMcp.Tests/Fixtures/Modules/HelpParityFixture/),
  introduced alongside this baseline to exercise the FR-500 / FR-510
  precedence chains. It exports six deterministic functions:
  - `Get-FixtureSynopsisOnly` (FR-500 step 1)
  - `Get-FixtureFullHelp` (FR-500 step 2 + FR-510 step 1)
  - `Get-FixtureHelpMessageOnly` (FR-510 step 2)
  - `Get-FixtureValidateSetScalar` (FR-510 step 3 singleton)
  - `Get-FixtureValidateSetArray` (FR-510 step 3 array)
  - `Get-FixtureBare` (FR-500 step 3/4 + FR-510 step 4 fallbacks)

## Capture metadata

| | |
|---|---|
| Capture date | 2026-05-12 |
| Server SHA | `16878b84` (`squad/224-toolslist-snapshots` HEAD before this commit) |
| OS | Windows |
| .NET | 10 (Release build) |
| PowerShell | pwsh 7.x (host default) |
| In-process tool count | 133 |
| Out-of-process tool count | 144 |
| Fixture tools present in both modes | 6 |

The tool-count delta (133 vs 144) is itself a pre-change parity artifact:
the two runtime paths today expose slightly different command surfaces for
the same configured module set. Spec 010 does not undertake to close that
gap (FR-551 explicitly keeps tool **names** stable; tool **descriptions**
are what spec 010 normalizes). The regression assertion is per-tool — it
matches tools by name across the two snapshots and ignores tools that
exist in only one side.

## How the snapshots were captured

```powershell
# From the repository root, after building PoshMcp.Server in Release:
pwsh -NoProfile -File specs/010-tool-self-documentation/baseline/capture-snapshots.ps1
```

The script:

1. Builds `PoshMcp.Server` in `Release` configuration.
2. For each runtime mode (`InProcess`, then `OutOfProcess`):
   - Writes a temporary `appsettings.json` that loads the reference
     module set and lists the fixture functions in `CommandNames`.
   - Sets `PSModulePath` for the spawned process so the in-process
     runspace auto-loads `HelpParityFixture`.
   - Launches `dotnet PoshMcp.dll serve --transport stdio` and speaks
     MCP JSON-RPC 2.0 over stdio: `initialize` →
     `notifications/initialized` → `tools/list`.
   - Persists the complete `tools/list` JSON-RPC envelope (pretty-printed)
     to the corresponding `*-tools-list.json` file.

The script uses no test framework and depends only on `pwsh 7+` and the
`dotnet` CLI; running it is the canonical way to regenerate the baseline.

## When to regenerate

The baseline is **not** intended to be regenerated after spec 010 lands —
that would defeat the purpose of pinning pre-change behavior. Regenerate
**only** if:

1. The fixture module (`HelpParityFixture`) changes in a way that affects
   discovered command count or synopsis text (e.g., adding/removing
   exported functions). In that case, regenerate before any spec-010
   description-source work begins and update the regression test to match.
2. The reference module set is intentionally extended to cover additional
   precedence cases not represented by the current fixture.

After spec 010 implementation lands, snapshots in this directory are
read-only inputs to the regression test.

## Manual capture (out-of-script reference)

For debugging the capture flow, the equivalent manual sequence is:

```powershell
$config = @{
    Logging = @{ LogLevel = @{ Default = 'Warning' } }
    PowerShellConfiguration = @{
        RuntimeMode  = 'InProcess'   # or 'OutOfProcess'
        CommandNames = @(
            'Get-FixtureSynopsisOnly','Get-FixtureFullHelp',
            'Get-FixtureHelpMessageOnly','Get-FixtureValidateSetScalar',
            'Get-FixtureValidateSetArray','Get-FixtureBare')
        Modules         = @('Microsoft.PowerShell.Management','HelpParityFixture')
        IncludePatterns = @('*')
        ExcludePatterns = @()
        Environment     = @{
            ModulePaths   = @('<repo>/PoshMcp.Tests/Fixtures/Modules')
            ImportModules = @('HelpParityFixture')
        }
    }
} | ConvertTo-Json -Depth 10
$config | Set-Content tmp.json -Encoding utf8

$env:PSModulePath = "<repo>/PoshMcp.Tests/Fixtures/Modules;$env:PSModulePath"
$psi = [System.Diagnostics.ProcessStartInfo]@{
    FileName = 'dotnet'
    Arguments = '<repo>/PoshMcp.Server/bin/Release/net10.0/PoshMcp.dll serve --config tmp.json --transport stdio --log-level Warning'
    RedirectStandardInput = $true; RedirectStandardOutput = $true
    UseShellExecute = $false
}
# ... then write initialize/notifications/initialized/tools/list JSON-RPC
# frames to stdin and read responses from stdout. See capture-snapshots.ps1
# for the working implementation.
```
