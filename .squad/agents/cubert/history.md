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

### 2026-05-07: Fact-check — PR #210 (Leela: OOP docs + samples audit) — REQUEST CHANGES
- Verified property names, casing, defaults, enum values against PowerShellConfiguration.cs and SubprocessHostMode.cs. All correct.
- Verified 4.86x and 4x I/O bar claims against benchmark-findings.md table. Match.
- Verified ProcessPool min-healthy clamp against McpToolSetupService.cs and DoctorService.cs. Match.
- Verified examples/appsettings.advanced.json + tenant.json parse + use correct per-mode keys (Pool->RunspacePoolSize, ProcessPool->PoolSize+MinHealthy). Clean.
- Verified examples/README.md rationale aligns with benchmark-findings.md S4 recommendations. Clean.
- Three errors in advanced.md blocked approval:
  1. "Unrecognized values fall back to InProcess with a logged error" -- code throws InvalidOperationException (ConfigurationLoader.cs:50). Server fails to start.
  2. ProcessPool cancellation bullet describes BACKSTOP teardown as the normal path. Per PR #207 + cancellation-design.md S2.3, normal cancel is soft via BeginStop; slot stays healthy.
  3. Single cancellation bullet describes pre-#207 behavior. PR #207 explicitly refactored oop-host.ps1 so host stays healthy after cancel.
- Lockout: Leela cannot self-revise. Recommended Bender (owner of PR #207) for revision.
- Posted REQUEST CHANGES verdict via gh pr comment (formal review blocked on this repo for usepowershell account).
- Learning: when fact-checking docs that describe a recently-shipped contract, ALWAYS read the merging PR description as well as the design spec. Design specs are "Status: Proposed" snapshots; the PR description is the authoritative record of what shipped. In this case the design and shipped behavior matched, but confirming both was what caught the fact that the docs described pre-PR behavior.

### 2026-05-07 - Revised PR #210 docs/articles/advanced.md on strict lockout
- Cubert REQUEST_CHANGES'd PR #210 (Leela's OOP docs/samples audit) with three blocking factual errors in advanced.md. Strict lockout: Leela cannot self-revise; Cubert recommended me as #207 owner.
- Verified ground truth before editing:
  * RuntimeMode error path: ConfigurationLoader.cs:46-50 throws InvalidOperationException on Unsupported mode. There is no fallback to InProcess. Cubert's claim confirmed.
  * Cancellation contract: re-read cancellation-design.md sections 2.1 (Single via background dispatcher + BeginStop), 2.3 (ProcessPool inherits OutOfProcessHost cancel frame; teardown is backstop only). Cubert's claims confirmed.
- Surgical edits to advanced.md only (5 insertions, 5 deletions, commit a4c9ed0):
  1. RuntimeMode invalid-value: replaced "fall back to InProcess with logged error" with the actual InvalidOperationException + exact message; added kebab-case acceptance note (Cubert's non-blocking minor).
  2. Cancellation lead-in: added one sentence identifying the shared cancel control frame mechanism so per-mode bullets read as variations on one contract.
  3. Cancellation Pool bullet: clarified PoolDispatcher looks up [powershell] by id, runspace returns to pool without restart.
  4. Cancellation ProcessPool bullet: rewrote to describe inherited soft-cancel; framed kill-on-timeout as backstop for wedged hosts only.
  5. Cancellation Single bullet: rewrote to describe SingleDispatcher background thread + BeginStop + host stays healthy; per-request timeout is backstop. Resolves Farnsworth's framing nit.
- Cross-check: configuration.md and azure-integration.md (other files in PR diff) do not contain matching claims; no further edits needed.
- Push: a4c9ed0 -> origin/squad/oop-docs-samples-audit (note: remote returns "repository moved" warning to usepowershell/PoshMcp.git but push succeeds via redirect).
- PR reply: posted via gh pr comment (formal review submission self-blocks on this repo); asked Cubert to re-verify.

## Learnings
- When a doc PR makes claims about runtime behavior I implemented, the fix path is: re-read my own design spec FIRST (cancellation-design.md), then re-grep the code path to confirm spec matches reality, THEN write doc text. Skipping the spec re-read risks writing doc that matches my mental model but not the shipped behavior.
- Doc bullets that describe per-mode behavior of a shared mechanism read better with a one-sentence lead-in naming the shared mechanism. Otherwise each bullet looks like an independent design and readers can't tell what's mode-specific vs. shared. Used this pattern for the Cancellation section in advanced.md.
- ConfigurationLoader.cs:46-50 is the source of truth for unrecognized RuntimeMode handling: throws InvalidOperationException with a kebab-case-supported-modes message. SettingsResolver.NormalizeRuntimeModeValue accepts both PascalCase and kebab-case at the env-var/CLI surface, but the ConfigurationLoader gate is strict.
- gh pr review --approve/--request-changes self-blocks on this repo because all squad personas push under the same usepowershell account (EMU note). For agent verdicts, post via 'gh pr comment <num> --body-file <tempfile>' with the agent badge prefix. This does NOT count as a formal GitHub approval for branch-protection.
- Push to origin returns a "This repository moved. Please use the new location: usepowershell/PoshMcp.git" warning but completes successfully via redirect. The local remote URL still points to smurawski/poshmcp; not a problem for pushes today, but worth noting if a future operation needs the canonical URL.

### 2026-05-07 - PR #210 re-verification (advanced.md, commit a4c9ed0)
- Re-verified all three prior REQUEST CHANGES findings against current HEAD of squad/oop-docs-samples-audit. Verdict: APPROVE.
- F1 (RuntimeMode invalid-value): fixed text matches ConfigurationLoader.cs:46-50 throw verbatim.
- F2 (ProcessPool cancel): fixed text correctly frames soft-cancel via inherited OutOfProcessHost cancel frame as primary; OutOfProcessSubprocessPool kill-on-timeout as backstop. Matches cancellation-design.md section 2.3.
- F3 (Single cancel): fixed text correctly describes SingleDispatcher background-thread + BeginStop + host-stays-healthy contract. Matches section 2.1.
- Collateral: shared-mechanism lead-in accurate; Pool bullet matches section 2.2; no broken markdown/code blocks. CI green.
- Lesson: when re-verifying, read the FILE AT HEAD via "gh api .../contents/path?ref=<oid>" and Base64 decode -- gh pr diff shows only changed hunks and can mask context. Cross-check claims against named source files at the line numbers cited in the revision drop, not just the diff.
- Lesson: the usepowershell account can post review comments via gh pr comment but NOT formal gh pr review --approve (self-review block applies even though Cubert and Bender are different personas). Verdict comment must carry the badge prefix.

### 2026-05-07 - PR #210 re-verification (advanced.md, commit a4c9ed0)
- Re-verified all three prior REQUEST CHANGES findings against current HEAD of squad/oop-docs-samples-audit. Verdict: APPROVE.
- F1 (RuntimeMode invalid-value): fixed text matches ConfigurationLoader.cs:46-50 throw verbatim.
- F2 (ProcessPool cancel): fixed text correctly frames soft-cancel via inherited OutOfProcessHost cancel frame as primary; OutOfProcessSubprocessPool kill-on-timeout as backstop. Matches cancellation-design.md section 2.3.
- F3 (Single cancel): fixed text correctly describes SingleDispatcher background-thread + BeginStop + host-stays-healthy contract. Matches section 2.1.
- Collateral: shared-mechanism lead-in accurate; Pool bullet matches section 2.2; no broken markdown/code blocks. CI green.
- Lesson: when re-verifying, read the FILE AT HEAD via "gh api .../contents/path?ref=<oid>" and Base64 decode -- gh pr diff shows only changed hunks and can mask context. Cross-check claims against named source files at the line numbers cited in the revision drop, not just the diff.
- Lesson: the usepowershell account can post review comments via gh pr comment but NOT formal gh pr review --approve (self-review block applies even though Cubert and Bender are different personas). Verdict comment must carry the badge prefix.

### 2026-05-07: Review of v0.11.0 release notes + SECURITY.md (Leela)

**Verdict: ❌ REJECTED**

**Verification report:**

Claims that check out (✅):
- `Pool` is the new default `SubprocessHostMode` — confirmed at PoshMcp.Server/PowerShell/PowerShellConfiguration.cs:33 (`= SubprocessHostMode.Pool`) and PoshMcp.Server/appsettings.json:64.
- Three-mode taxonomy (Single / Pool / ProcessPool) — matches `enum SubprocessHostMode` in PoshMcp.Server/PowerShell/OutOfProcess/SubprocessHostMode.cs.
- Pool sizing knobs (`SubprocessRunspacePoolSize`, `SubprocessPoolSize`, `SubprocessMinHealthyForStartup`) — match doctor service references and configuration.md.
- Cancellation propagation across OOP boundary — backed by commit 17b11f8 (#207, #188).
- New `PoshMcp.Benchmarks` harness — backed by commit b2b80be (#193, #197).
- Bug fixes: `ConvertTo-Json` Content shadowing (#203/#204) and `` clear-before-invoke (#189/#199) — match git log v0.10.0..HEAD.
- Security: LogSanitizer.Scrub() in OOP host call sites (commits d14b70e/4f4e962), workflow permissions (b69b6f4), SECURITY.md publication.
- SECURITY.md table now shows 0.11.x ✅ and < 0.11 ❌; rest of file untouched.
- Format matches established release-notes style (H1, "What's New" / "Bug Fixes" / "Upgrade Notes" sections, fenced jsonc).
- ~4.86x warm-invoke throughput at concurrency 10 — consistent with bench-runs/run-2-artifacts and the spec 004 narrative; not independently re-run but matches the cited findings file.

Claims that FAIL (❌):
- **Both JSON snippets in "Upgrade Notes" use the wrong top-level config key.** They show `"PowerShell": { ... }` but every shipping `appsettings.json`, every doc under `docs/articles/`, every `examples/appsettings.*.json`, and the README all use `"PowerShellConfiguration": { ... }`. Verified: 30+ matches for `"PowerShellConfiguration"` across the repo, zero matches for `"PowerShell"` as a top-level config key for these properties. A user copy-pasting the opt-out snippet would silently keep the new Pool default — directly contradicting the upgrade-notes intent. The opt-in ProcessPool snippet has the same defect.

**Required revision:**
Replace `"PowerShell"` with `"PowerShellConfiguration"` as the top-level key in both jsonc blocks under "Upgrade Notes" in docs/release-notes/0.11.0.md.

**Per Reviewer Rejection Protocol:** Leela cannot self-revise. Route the fix to Amy (release-notes co-owner per charter) or any other agent.

**Cubert.**
