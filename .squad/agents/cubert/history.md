# Cubert — History

## Project Context

- **Project:** poshmcp
- **Description:** Model Context Protocol (MCP) server that dynamically transforms PowerShell scripts, cmdlets, and modules into secure, discoverable AI-consumable tools
- **Tech Stack:** .NET 10, C#, PowerShell SDK, OpenTelemetry, ASP.NET Core, xUnit
- **Primary User:** Steven Murawski
- **Joined:** 2026-05-05

## Learnings

### 2026-05-05: Fact-check of squad-story.md and squad-work-log.md
**Requested by:** Steven Murawski

**Method:** Verified each technical claim by reading the actual repo (file_search, grep_search, read_file). Counted `[Fact]/[Theory]` attributes for test counts. Confirmed file paths against current directory layout.

**Key findings:**
- **Systemic future-dating bug.** Both docs (and `.squad/decisions.md`, several agent histories) carry entries dated July 2026 while the current date is 2026-05-05. Filed `cubert-future-dated-entries.md` to the decisions inbox.
- **Story fabricates the `/health` endpoint JSON shape.** The codebase has no `runspacePoolSize`, `activeCommandCount`, or `lastCommandCompletedAt` fields. `PoshMcp.Server/Health/` contains only standard `IHealthCheck` implementations that return Microsoft's `HealthCheckResult`.
- **Wrong file paths in work-log.** `DockerRunner.cs` lives at `PoshMcp.Server/Cli/`, not `Infrastructure/`. `DiagnoseMissingCommands` and `ConfiguredFunctionStatus` live in `PoshMcp.Server/Diagnostics/DoctorService.cs`, not `Program.cs` — work was refactored after the entry was written.
- **Stale counts.** "18 decision entries" is significantly low; actual count is much higher (30+ top-level date headers). "478 integration tests" is implausible for this codebase. "11 DockerRunner tests" vs 16 `[Fact]/[Theory]` attributes — recount before publishing.
- **Story roster missing Cubert.** The article's roster table omits the Fact Checker even though I'm on the active squad in `.squad/team.md`.
- **Unverifiable external claims:** "700+ NuGet downloads" and "CVE-2026-40894" cannot be checked without web access. Both should be sourced or removed.

**Patterns worth remembering:**
- Always grep for symbol locations rather than trust path claims in narrative docs — refactors break path attributions.
- Test counts in prose are a frequent source of staleness; recount via `[Fact]/[Theory]` attribute count or `dotnet test` output before publishing.
- When sample JSON appears in docs, search the source for the field names. Fabricated samples almost always have field names that don't appear anywhere in the codebase.
- Future-dated entries usually indicate either an agent ignoring `CURRENT_DATETIME` or a clock-skew bug — check across multiple files to distinguish a one-off typo from a systemic issue.

**Recommended next agent:** Leela (owns docs).

### 2026-05-06: Fact-check of PR #187 — runspace pool experiment plan
**Requested by:** Steven Murawski

**Method:** Verified each technical claim in `specs/004-out-of-process-execution/runspace-pool-experiment-plan.md` against source files on the current branch. Confirmed API names, type signatures, file paths, behavioral assertions in `OutOfProcessCommandExecutor.cs` and `oop-host.ps1`. Cross-checked phasing claims against `spec.md` and the spec directory contents. Cross-checked plan summary against decisions.md ledger entry.

**Key findings:**
- **Internals all check out.** Every cited type and field in `OutOfProcessCommandExecutor.cs` (`_sendLock` SemaphoreSlim(1,1), `_pending` ConcurrentDictionary<string,TaskCompletionSource<JsonElement>>, GUID id via `Guid.NewGuid().ToString("N")`, `ReadLoopAsync`, `IsNonJsonPowerShellStreamLine`, `Process.Exited`, `Kill(entireProcessTree:true)`) verified at the cited line ranges. All five ndjson handlers (`Invoke-PingHandler`, `Invoke-SetupHandler`, `Invoke-DiscoverHandler`, `Invoke-InvokeHandler`, `Invoke-ShutdownHandler`) exist in `oop-host.ps1`.
- **`-Depth 4` for invoke output is correct** — initially looked like a contradiction with `Send-Response` which uses `-Depth 10`, but those are different code paths: the wrapper envelope uses 10, the user-facing payload from `Invoke-InvokeHandler` uses 4. Always read the specific function, not the first depth value found.
- **"Phases 1–4 complete" is unanchored.** The phrase originates from Issue #65's Dependencies section. The spec at `specs/004-out-of-process-execution/spec.md` is `Status: Draft` and contains no phase manifest; there is no `plan.md` or `tasks.md` in that spec directory. The substantive claim (subprocess lifecycle, ndjson protocol, discovery, invoke, setup, shutdown all shipping) is verifiably true, but the label has no formal anchor.
- **Pre-existing `$Error` bug.** Plan flags per-runspace `$Error` handling as an Option-A hazard. Verified at `oop-host.ps1:558-565`: the current single-runspace `Invoke-InvokeHandler` does NOT clear `$Error` before `& $commandName`, so `hadErrors` already leaks errors across invocations in shipping code. The plan should note this is a pre-existing bug, not just a pool-mode concern, and a separate fix issue should be filed.
- **Path drift in upstream issue.** Issue #65 references `specs/out-of-process-execution.md` (no `004-` prefix). Plan correctly uses the canonical directory path.
- **No `OutOfProcessHost.cs` and no `PoshMcp.Benchmarks` project exist yet.** Consistent with the plan's phasing — both are listed as future work, not as existing artifacts.

**Patterns worth remembering:**
- When a plan cites `ConvertTo-Json -Depth N`, search for the specific cmdlet usage in the named function rather than grep for "Depth" — multiple call sites can use different depths legitimately.
- "Phases X–Y complete" is a recurring fact-check trap when the spec has no phase manifest. Always check whether the spec directory actually contains `plan.md`/`tasks.md` before accepting the label.
- Plan-only PRs that re-quote internals are a great fact-check target because every cited line either resolves cleanly or doesn't — high signal, low ambiguity.
- EMU policy blocks `gh pr review` and `gh pr comment` against `usepowershell/PoshMcp` from this account. Future PR reviews need to be saved to a temp file and surfaced to the user for manual posting; do not assume `gh` write access.

**Recommendation:** Proceed with the plan. Filed inbox note for the two follow-ups (anchor the phase label; promote the pre-existing `$Error` clearing bug to its own issue). Full report at `C:\Users\stmuraws\AppData\Local\Temp\cubert-pr187-review.md`.
