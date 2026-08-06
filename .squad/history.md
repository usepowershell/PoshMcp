# Project Context

- **Owner:** {user name}
- **Project:** {project description}
- **Stack:** {languages, frameworks, tools}
- **Created:** {timestamp}

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2026-08-06: Farnsworth Warm-Call Gap & Handle Growth Architecture Diagnosis
**Issues:** #380, #385
**Finding:** Warm p95 gap (~1.45–1.76× vs ephemeral baseline) is structurally caused by RunspaceResetProtocol's defensive full-table enumeration of ~1700 session-state entries on every call — even clean ones (Get-Date creates zero request-scoped state). FullMix handle drift isolated to tools/call path; primary suspect is unlistened Activity object accumulation from ToolActivitySource.StartActivity() allocating OS handles faster than GC reclaims them.
**Plan:** 5 ordered fixes — (1) skip-enumeration fast path for clean workers (Hermes), (2) count-guard before table enum (Hermes), (3) suppress unsampled Activity creation (Bender), (4) scope disposal audit (Bender), (5) validation run. No threshold relaxation.

### 2026-08-06: Hermes Warm-Call & Handle-Floor Fixes Shipped
**Issues:** #380, #385
**Branch:** `squad/warm-handle-ps-fix`
**PR:** #392 (draft, base=main, closingIssuesReferences=[])
**Fixes:** (1) `TryGetTables()` hoisted to top of `ResetCore` so preference reset + $Error clear share the fast path; (2) `ResetPreferenceVariablesFast` via direct `PSVariable.Value` assignment bypassing proxy lock/event for 8 preference vars; (3) `ResetWorkingLocation` skips `SetLocation` when runspace already at drive root; (4) `InvokePowerShellSafe` switched from `ps.Invoke()` to `BeginInvoke/EndInvoke` with `using var output` and explicit `asyncResult.AsyncWaitHandle?.Dispose()` to eliminate per-call OS handle backlog. All 356 pool tests pass.
