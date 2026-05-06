# Farnsworth — Lead/Architect — Work History

## Project Context
**Project:** PoshMcp — Model Context Protocol (MCP) server for PowerShell
**Tech Stack:** .NET 10, C#, PowerShell SDK, OpenTelemetry, ASP.NET Core, xUnit
**Primary User:** Steven Murawski

## Pre-2026-05-02 Summary (archived to history-archive.md on 2026-05-06)
- 2026-04-17: Restructured loose specs (003 prompts, 004 OOP, 005 large-result) into speckit format; FR-035..FR-064, SC-016..SC-030.
- 2026-04-18: Approved PR #130 (MimeType nullable fix) — pattern: model nullable + handler-applied default preserves validator signal.
- 2026-04-20: Filed Spec 006 (Doctor Output Restructure) milestone #3 with 27 issues T001-T027 (#140-#166) split Bender/Fry.
- 2026-07-15: MCP Resources/Prompts spec (002) authored; 4 team skills extracted from PRs #92-#96.
- 2026-07-18: Triaged Issue #131 (stdio logging to file) — Serilog file sink, ClearProviders unconditional in stdio mode, 3-tier resolution (CLI > env > config). Approved PRs #132 (stdio logging), #134 (docker buildx context fix).
- 2026-07-28: Approved PR #167 (Spec 006 Doctor Output Restructure) — DoctorReport records + DoctorTextRenderer architecture.
- See history-archive.md for full entries.

### 2026-05-02: Reviewed PR #184 — Program.cs Refactoring (squad/program-cs-refactor)

**Verdict:** CHANGES REQUESTED

**PR:** https://github.com/usepowershell/PoshMcp/pull/184 — "refactor: extract Program.cs concerns into dedicated service classes (68% reduction)"

**Branch pushed and PR created** from worktree `poshmcp-refactor`. 6 commits, 6 new files.

**Key findings:**

1. **BLOCKING — DescribeConfigurationPath duplicated 5x**: `Program.cs`, `DoctorService.cs`, `CommandHandlers.cs`, `StdioServerHost.cs`, `HttpServerHost.cs` all contain a private copy of the same utility method. Same for `ToToolName`, `GetDiscoveredToolNames`, `GetExpectedToolNames`. Needs a shared `ConfigurationHelpers` utility class.

2. **BLOCKING — DoctorService extraction incomplete**: `BuildDoctorReportFromConfig` and `BuildDoctorJson` (plus all their private helpers) were COPIED into `DoctorService.cs` but NOT removed from `Program.cs`. Tests still call `Program.BuildDoctorReportFromConfig`. Fix: either delete duplicates from Program.cs + update tests, or have Program.cs forward to DoctorService.

3. **CONCERN — CliDefinition nullable static properties**: 70+ options/commands are null until `Build()` is called; mutable static state reset on subsequent `Build()` calls. Suggest returning a value object instead.

4. **CONCERN — CliDefinition/CommandHandlers are `public`**: Should be `internal` (matching DoctorService, McpToolSetupService, etc.).

5. **GOOD — Delegate injection in DoctorService**: Passing `DiscoverToolsForCliAsync` as Func avoids coupling Diagnostics to Server layer.

6. **GOOD — Namespace consistency**: All 6 new classes use `namespace PoshMcp;`.

**Decisions file:** `.squad/decisions/inbox/farnsworth-pr-review.md`
**Key decisions:**
- `McpResources` and `McpPrompts` are top-level `appsettings.json` siblings to `PowerShellConfiguration` — MCP-layer concerns belong at MCP layer, not nested under execution config
- Two source types for both: `"file"` (read at request time, relative to `appsettings.json` dir) and `"command"` (executed in shared runspace, no new runspace)
- URI scheme `poshmcp://resources/{slug}` is recommended but not enforced; doctor warns, does not error
- Prompt argument injection uses pre-assignment (`$argName = value`) before command string executes — not `-ArgumentList` (avoids requiring `param()` blocks)
- File-backed prompt argument substitution deferred to v1+ — file returned verbatim, client does template rendering
- No resource caching in server; operators build caching into PowerShell commands if needed
- Resource subscriptions out of scope — four read-path SDK handlers are sufficient for v1
- SDK registration via `WithListResourcesHandler`, `WithReadResourceHandler`, `WithListPromptsHandler`, `WithGetPromptHandler` in `Program.cs`
- FR numbering starts at FR-018 (after FR-017 from spec 001); SC numbering starts at SC-009 (after SC-008)
- Doctor validation contract fully specified including severity levels and JSON output shape




### 2026-05-06: PR #187 review — Hermes runspace pool vs multi-process experiment plan

**Verdict:** APPROVE (review saved to `$env:TEMP\farnsworth-pr187-review.md` — EMU policy blocks `gh pr review` AND `gh pr comment` from this account; surfaced to user for manual paste).

**Plan strengths confirmed:**
- `OutOfProcessHost` extraction is the correct shared seam — per-process state in current `OutOfProcessCommandExecutor` (process, streams, `_sendLock`, `_pending`, read/stderr loops, `Process.Exited`) factors cleanly out, leaving `ICommandExecutor` as the public surface. Single-host executor becomes a thin wrapper; process pool composes N hosts.
- Protocol layer is genuinely parallel-ready (id-keyed `_pending` + async `ReadLoopAsync`). Only the host serializes today. Plan correctly notes this — minimizes C# churn.
- Isolation as benchmark pass/fail gate (not vibes) is the right discipline. "Default to B if neither passes isolation" is the correct tiebreaker — preserves the original OOP motivation.
- Phasing: 1 unblocks 2/3, 4 parallel, 5 fans in, 6 cleans up. `SubprocessHostMode` flag keeps a known-good baseline available for bisecting.
- `SubprocessPoolSize: 1` collapses Option B to current behavior — clean fallback knob.

**Architectural concerns to fold into prototype issues (non-blocking for plan):**
- A1 — Stream pollution: `[Console]::Out` swap won't catch `Write-Host` (which goes through `$Host.UI` snapshotted at runspace open). Need a custom `PSHost` / `PSHostUserInterface` for the pool. The .NET-side `IsNonJsonPowerShellStreamLine` should be defense-in-depth, not load-bearing.
- A2 — Setup race: pool close-rebuild-reopen needs an explicit drain barrier (stop accept → wait `_pending` empty → close → rebuild → reopen). Plan's "setup is rare and cannot race" is aspirational.
- A3 — Per-runspace `$Error` is correctly identified; also reset `$LASTEXITCODE` and `$ErrorActionPreference`. Existing single-runspace host already leaks `$Error` across invokes (separate fix issue).
- B1 — Channel-lease `finally` must check liveness before re-enqueue; dead host stays out of channel until replacement passes `ping` + `setup`. Otherwise pool slowly bleeds capacity.
- B2 — Discovery cache assumption ("schemas identical") only holds while every subprocess runs the same setup. Cache discovery keyed by setup-payload hash; re-discover on hash mismatch.
- 4 — Benchmark harness must use BDN `[GlobalSetup]` / `[IterationSetup]` to keep process spawn out of per-iteration measurements; otherwise B loses on Az.Accounts import time for the wrong reason.
- 5 — Pass/fail "≥ 4× baseline at 10 concurrent" doesn't apply to CPU-bound (ceiling = ProcessorCount) or CPU-light (dispatch overhead floor). Recommend rewriting as scenario × metric × threshold table.
- 6 — Cancellation scope-out: until cancellation lands, Option A's effective capacity under adversarial load is `N - stuck_invokes`. Don't flip default to A in issue #6 without it.
- 7 — Memory metric: sample Win32 handle count alongside working set. Az workloads leak handles characteristically.

**Answers to plan §6 open questions:**
1. Default N = `Environment.ProcessorCount` for prototype. Don't pick a fixed number until network-shaped benchmark confirms scaling.
2. Don't ship the loser. Two host scripts is real maintenance cost — issue #6 deletes the loser.
3. Local `HttpListener` for harness; one manual real-Azure end-to-end for the findings doc; not in CI.
4. Cancellation as separate issue, agreed; conditional on concern #6.

**Pattern noted (logging):**
- EMU blocks BOTH `gh pr review` and `gh pr comment` for usepowershell/PoshMcp from this account. Cubert recently logged the same finding. Future PR reviews must be saved to `$env:TEMP` and surfaced for manual paste — do not waste cycles attempting either gh subcommand.
- Plan-only PRs that quote internals are high-signal fact-check targets — every cited line either resolves or doesn't. Cross-reference cmdlet usage at the call site, not by global grep (e.g., `ConvertTo-Json -Depth N` legitimately varies per call site).
## Learnings (2026-05-06)
- PR #187 review (runspace pool vs multi-process experiment plan, branch squad/65-runspace-pool-experiment-plan): verdict = comment / approve direction with revisions. Plan is sound; #1 (extract OutOfProcessHost) can start immediately.
- Key architectural concerns raised:
  - Option A: `[Console]::Out` cannot be redirected per-runspace (it's process-global) — plan's stream-pollution mitigation needs correction; use custom PSHost via CreateRunspacePool instead.
  - Option A: setup-while-running quiesce protocol is underspecified — `OutOfProcessCommandExecutor.SendRequestAsync` does not gate setup against in-flight invokes today (only `_sendLock` for stdin writes).
  - Option B: Channel<OutOfProcessHost> + Process.Exited race when host crashes mid-lease needs explicit reconciliation (don't end up with N+1 or N-1).
  - Option B: fail-fast on all-N setup is a regression vs single-host; recommend fail-fast on first host then per-host retries.
  - Benchmark: crash-recovery scenario as written measures process restart (same as baseline) — for Option A's real isolation gate, induce runspace-level corruption, not process exit.
  - Benchmark: 4× throughput gate is wrong for CPU-bound; needs per-scenario thresholds.
  - Phasing: split #4 (benchmark harness) into infrastructure (parallel) and wire-up (blocked on #2/#3).
- Verified current OOP code matches plan's description: `OutOfProcessCommandExecutor.cs` (_sendLock at line 29, _pending dict, SendRequestAsync at line 362), `oop-host.ps1` (Write-NdjsonResponse flush, handler ordering).
- Posting via gh failed (EMU policy on usepowershell/poshmcp blocks `gh pr review`). Review body left at C:\Users\stmuraws\AppData\Local\Temp\tmpsvtizs.md for manual posting.

### 2026-05-06 — EMU blocks `gh issue create` too
The `usepowershell/PoshMcp` repo's EMU policy not only blocks `gh pr review` / `gh pr comment` (already known) but also `gh issue create` with the same `Unauthorized: As an Enterprise Managed User` GraphQL error. When asked to file an issue, write the body to a temp file outside the repo and hand the path to the user to create the issue manually. Do not retry `gh issue create` under this account.

### 2026-05-06 — Cancellation propagation gap (issue body drafted at C:\Users\stmuraws\AppData\Local\Temp\poshmcp-cancellation-issue.md)
Confirmed: `CancellationToken` in `OutOfProcessCommandExecutor.SendRequestAsync` only governs the .NET-side wait (`_sendLock.WaitAsync`, `_stdin.WriteLineAsync`, the linked `timeoutCts` failing the local TCS). It is never forwarded to the OOP subprocess, and `oop-host.ps1` runs a single-threaded dispatcher loop (L630–L650) that cannot read a `cancel` while `Invoke-InvokeHandler` (L498) is blocked inside `& $cmdInfo @boundParams`. In-process is worse: `IsolatedPowerShellRunspace.ExecuteThreadSafeAsync` doesn't accept a CT at all; the only `_powerShell.Stop()` is inside Dispose() (PowerShellRunspaceImplementations.cs L146). Layered fix: (1) cooperative `Stop()/StopAsync()` registration on the in-process pipeline, (2) a `cancel` JSON-RPC method + concurrent-readable dispatcher for OOP, (3) bounded escalation cooperative → forced → process kill + recycle via `PowerShellCleanupService`.

## 2026-05-06: New milestone-tagged issues assigned

Milestone #5 (Spec 004 - Out-of-Process PowerShell Execution) was created. You have issues assigned via squad:* labels:
- Bender: #190 (extract OutOfProcessHost), #192 (Option B - process pool prototype, blocked by #190)
- Fry: #193 (benchmark harness infra), #194 (wire harness to executors, blocked by #191/#192/#193)
- Farnsworth: #196 (adopt the winner, blocked by #195)

Check the issue body for plan reference and dependency chain before starting.
