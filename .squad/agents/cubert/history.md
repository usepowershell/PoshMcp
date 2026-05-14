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

### 2026-05-12: Pre-review of spec 010 (tool self-documentation) — APPROVE WITH CHANGES
**Requested by:** Steven Murawski (Brady)
**Artifact:** specs/010-tool-self-documentation/spec.md (Draft, Farnsworth author, Hermes co-author on grounding research)

**Method:** Verified all six cited file:line ranges against current source on disk. Cross-checked format conformance against spec 009. Cross-checked scope discipline against Brady's directive ("don't worry about comment-based vs MAML vs XML — platform normalizes them").

**Verdict:** APPROVE WITH CHANGES — five required changes before promotion to Accepted.

**Citations check (✅ all verified):**
- McpToolFactoryV2.cs#L123-L145, #L442 — match
- PowerShellSchemaGenerator.cs#L98 — match
- oop-host.ps1#L763-L771, oop-host-pool.ps1#L824-L832 — match
- RemoteToolSchema.cs#L17 XML doc — match (and confirmed misleading)

**Required changes (drop file at .squad/decisions/inbox/cubert-spec-010-review.md):**
1. FR-521 parity test strategy is hand-wavy — needs project, naming, equality scope, fixture corpus.
2. FR-550 "no description regression" has no measurement mechanism — unfalsifiable as written.
3. FR-530/531 punt on alias field placement and label it "implementation decision" — not testable; resolve OQ-1 inline.
4. FR-572 baseline capture vague — name the bench-runs/ artifact, require post-change run committed alongside.
5. SC-205/SC-206 byte-identical claim needs culture/host carve-out OR more aggressive normalization in FR-540 — otherwise FR-521 will be flaky on cross-platform CI.

**Recommended revision agent:** Hermes (per strict-lockout rule; Farnsworth is locked out from self-revising his own draft. Hermes has independent grounding in the same code paths from his 2026-05-12 research).

**Top 2 issues (highest blocking weight):**
- (3) Punted FR-530 alias placement — this is the most subtle defect because it reads like a complete FR but isn't testable. Easy to miss on a fast read.
- (5) Cross-mode byte-identical claim without culture/normalization carve-out — will bite at FR-521 implementation time, when the parity test is flaky on Linux CI agents and someone has to retroactively rewrite the SC.

**Patterns worth remembering:**
- When an FR contains the phrase "implementation decision" or "implementation choice", that's a tell — the FR is punting and shouldn't ship as-is. Either resolve the choice in the FR or demote it to an Open Question.
- Byte-identical parity claims across two execution contexts (in-process vs subprocess) almost always need a culture/host-normalization precondition. PowerShell's Get-Help formatting depends on `$Host.UI.RawUI.BufferSize`, which differs between an attached console and a redirected stdin/stdout subprocess. If a spec doesn't disclose that, expect the parity test to flake on the first cross-platform CI run.
- Co-authored specs (Farnsworth-as-author, Hermes-as-grounding) are easy to fact-check on the technical claims because the co-author's history.md is itself a contemporaneous record of the verified evidence. Cross-referencing the two cuts verification time roughly in half.
- Strict-lockout rule means the obvious revision agent is the co-author who provided the technical baseline (Hermes here), not the original drafter. This keeps the lockout meaningful while preserving the research investment.



### 2026-05-12: Wave 1 spec-010 PR fact-check — three PRs verified

**Requested by:** Steven

**PR #235 (Bender — RemoteToolSchema XML doc fix):** VERIFIED. XML doc on `RemoteToolSchema.Description` matches both `oop-host.ps1` L763-771 and `oop-host-pool.ps1` L824-832 exactly: `Get-Help .Synopsis`, trimmed, only when != command name; otherwise empty. Long Description body and parameter-set syntax NOT read. Downstream fallback to bare command name confirmed at `McpToolFactoryV2.cs:442`. Doc correctly scopes itself to "Populated by the OOP host" so the in-process path (different code path) is not contradicted.

**PR #236 (Fry — pre-spec010 tools/list snapshots):** VERIFIED. Counted 133 in-process / 144 OOP tools via `ConvertFrom-Json | .result.tools.Count` — exact match. Six fixture tools in each snapshot (snake_case-normalized: `get_fixture_bare`, `get_fixture_full_help`, `get_fixture_help_message_only`, `get_fixture_synopsis_only`, `get_fixture_validate_set_array`, `get_fixture_validate_set_scalar`). Source `HelpParityFixture.psm1` exports exactly six matching `Get-Fixture*` functions. README SHA `16878b84` confirmed as parent of bench commit `6420feb`. Capture script does proper `initialize -> notifications/initialized -> tools/list` handshake with temp appsettings (no live config mutation). Not end-to-end re-executed.

**PR #237 (Amy — cold-start baseline):** VERIFIED with one minor. Mean numbers (Single 5.790s, Pool 5.784s, ProcessPool 6.996s) match across PR body, README table, BDN GitHub markdown, and CSV — all four sources agree on Mean/P95/P99. `ColdStartBenchmark` class exists at `PoshMcp.Benchmarks/Scenarios/ColdStartBenchmark.cs` with `[Params(Single, Pool, ProcessPool)]` and `[InvocationCount(1)]`. SHA `16878b84` confirmed as parent of bench commit `77d535a`. **Minor non-blocker:** README "Artifacts in this folder" lists a `.log` file and `stdout.log` that are NOT in the PR diff — README either over-promises or those files should be added. Cosmetic cleanup, not a factual-accuracy blocker.

**Patterns worth remembering:**
- When fact-checking a doc claim about OOP behavior, the FACT is in the host script, not the C# DTO. Always re-read both `oop-host.ps1` AND `oop-host-pool.ps1` — they have parallel discovery logic in different files; a claim might be true in one and not the other.
- MCP tool name normalization is snake_case. PowerShell function names use PascalCase-with-dashes. When verifying "tool X exists in the snapshot", search BOTH forms (`Get-Fixture*` AND `get_fixture_*`). A negative result on the PowerShell name alone is misleading.
- BDN cold-start benches with `[InvocationCount(1)] + [UnrollFactor(1)] + await using` per iteration correctly report per-iteration cost in the Mean column — the README's interpretation is sound. Worth remembering when reviewing other bench claims that don't have those attributes (those would amortize and the Mean would NOT be per-cold-start).
- When README lists "Artifacts in this folder" and the file list doesn't match the PR diff, it's almost always honest — author intended to ship the .log but `.gitignore` or the diff scope excluded them. Worth flagging non-blocker.



### 2026-05-12: Wave 1 spec-010 PR fact-check — three PRs verified

**Requested by:** Steven

**PR #235 (Bender — RemoteToolSchema XML doc fix):** VERIFIED. XML doc on `RemoteToolSchema.Description` matches both `oop-host.ps1` L763-771 and `oop-host-pool.ps1` L824-832 exactly: `Get-Help .Synopsis`, trimmed, only when != command name; otherwise empty. Long Description body and parameter-set syntax NOT read. Downstream fallback to bare command name confirmed at `McpToolFactoryV2.cs:442`. Doc correctly scopes itself to "Populated by the OOP host" so the in-process path (different code path) is not contradicted.

**PR #236 (Fry — pre-spec010 tools/list snapshots):** VERIFIED. Counted 133 in-process / 144 OOP tools via `ConvertFrom-Json | .result.tools.Count` — exact match. Six fixture tools in each snapshot (snake_case-normalized: `get_fixture_bare`, `get_fixture_full_help`, `get_fixture_help_message_only`, `get_fixture_synopsis_only`, `get_fixture_validate_set_array`, `get_fixture_validate_set_scalar`). Source `HelpParityFixture.psm1` exports exactly six matching `Get-Fixture*` functions. README SHA `16878b84` confirmed as parent of bench commit `6420feb`. Capture script does proper `initialize -> notifications/initialized -> tools/list` handshake with temp appsettings (no live config mutation). Not end-to-end re-executed.

**PR #237 (Amy — cold-start baseline):** VERIFIED with one minor. Mean numbers (Single 5.790s, Pool 5.784s, ProcessPool 6.996s) match across PR body, README table, BDN GitHub markdown, and CSV — all four sources agree on Mean/P95/P99. `ColdStartBenchmark` class exists at `PoshMcp.Benchmarks/Scenarios/ColdStartBenchmark.cs` with `[Params(Single, Pool, ProcessPool)]` and `[InvocationCount(1)]`. SHA `16878b84` confirmed as parent of bench commit `77d535a`. **Minor non-blocker:** README "Artifacts in this folder" lists a `.log` file and `stdout.log` that are NOT in the PR diff — README either over-promises or those files should be added. Cosmetic cleanup, not a factual-accuracy blocker.

**Patterns worth remembering:**
- When fact-checking a doc claim about OOP behavior, the FACT is in the host script, not the C# DTO. Always re-read both `oop-host.ps1` AND `oop-host-pool.ps1` — they have parallel discovery logic in different files; a claim might be true in one and not the other.
- MCP tool name normalization is snake_case. PowerShell function names use PascalCase-with-dashes. When verifying "tool X exists in the snapshot", search BOTH forms (`Get-Fixture*` AND `get_fixture_*`). A negative result on the PowerShell name alone is misleading.
- BDN cold-start benches with `[InvocationCount(1)] + [UnrollFactor(1)] + await using` per iteration correctly report per-iteration cost in the Mean column — the README's interpretation is sound. Worth remembering when reviewing other bench claims that don't have those attributes (those would amortize and the Mean would NOT be per-cold-start).
- When README lists "Artifacts in this folder" and the file list doesn't match the PR diff, it's almost always honest — author intended to ship the .log but `.gitignore` or the diff scope excluded them. Worth flagging non-blocker.


### 2026-05-12: PR #238 verification (Bender — IToolMetadataSource seam, issue #225)
**Verdict:** VERIFIED.
- Re-ran capture-snapshots.ps1 against PR HEAD e94349f from worktree poshmcp-225. Both snapshots byte-identical to committed wave-1 baselines (InProcess 133 tools / 386829 bytes; OOP 144 tools / 287869 bytes). git status --short clean after capture — empirical proof of byte-for-byte preservation.
- DI confirmed in BOTH transport hosts: StdioServerHost.cs L141 and HttpServerHost.cs L280, both use TryAddSingleton<IToolMetadataSource, DefaultToolMetadataSource>(). TryAddSingleton is the right choice — lets #226/#227 swap implementations.
- Build clean: dotnet build PoshMcp.sln -c Release reports 0 errors / 20 warnings, all pre-existing (CS8602 in McpToolFactoryV2.cs/PowerShellAssemblyGenerator.cs, CS8604 in CommandHandlers.cs, NU1510 transitive). None inside the new files.
- Test count 661 not locally re-run (worktree cd was stripped by terminal simplification; main-checkout test would not exercise PR code). gh pr checks 238 reports 7/7 green including CI/build and Squad CI/test.
- Spec consistency: Option A explicitly named in spec.md; ToolDescriptionSource enum {Synopsis, Description, Syntax, Name} maps 1:1 to FR-583 tool literals; ParameterDescriptionSource enum maps 1:1 to FR-583 parameter literals. LongDescription field on the request record is present-but-unused per the deferred-to-#226 note.
- Subtle: DefaultToolMetadataSource adds .Trim() and synopsis-equals-CommandName guard on the OOP path. Pre-spec-010 code at L442 was a literal pass-through. The OOP host already trims and applies the equals guard at oop-host.ps1 L763-771 / oop-host-pool.ps1 L824-832 BEFORE populating schema.Description, so the seam's added guard is idempotent on every shipping host. Snapshot equality empirically confirms no drift on Microsoft.PowerShell.Management + HelpParityFixture corpus. Worth noting because a non-shipping host that sent an untrimmed Description would now produce different output.
- Posted neutral, evidence-first verdict via gh pr comment (formal review submission still self-blocks).

**Patterns worth remembering:**
- For a refactor PR that claims byte-for-byte preservation, the strongest possible verification is to RE-RUN the capture script that produced the baseline against the new code path and binary-diff the output files. -eq on Get-Content -Raw + identical lengths is the simplest sufficient check; git status clean after capture is independent confirmation. This took ~9s vs. trying to mentally trace 300+ lines of diff.
- When a worktree-bound dotnet test command can't be launched (terminal cwd simplification strips the cd in this VSCode terminal), CI green status is acceptable corroboration for a test-count claim, BUT the byte-for-byte snapshot is the higher-signal check — it verifies BEHAVIOR, not just compilation/test-pass. For a seam PR, snapshot identity > test count.
- When a refactor adds normalization (.Trim() here) at a new layer, always check whether the SOURCE layer already does the same normalization. If it does, the new layer is idempotent and snapshot identity is expected. If it doesn't, you have a real behavior change and need test coverage for the malformed-input case.


### 2026-05-12 — Wave 3 review of PRs #240 and #239 (spec 010, requested by Steven)

**PR #240 (squad/226-inprocess-precedence) — APPROVE.** Built worktree, ran DescriptionSanitizerTests (23/23 in 161 ms), and re-captured the in-process tools/list snapshot. **124 of 133 tools** got upgraded from syntax lines to real Get-Help synopses. The 9 unchanged tools are exactly the ones the FR-500 chain says should fall through to syntax (no synopsis, no description body) — including the bare/HelpMessage-only/ValidateSet fixtures (HelpMessage and ValidateSet are parameter-level signals, not tool-level). DI confirmed in both `HttpServerHost.cs:287` and `StdioServerHost.cs:148`. Posted via `gh pr comment` (EMU self-review block — does NOT count as formal approval).

**Non-blocking observation on #240:** `BuildParameterDescriptionMap` correctly resolves per-parameter help text and the assembly generator emits `[System.ComponentModel.DescriptionAttribute]` (`PowerShellAssemblyGenerator.cs:613-626`), but the captured `inputSchema.properties.<name>.description` field is still empty for fixture parameters with known help. The data IS reaching the map (probed PowerShell directly: Get-Help returns text). The remaining gap is between `[Description]` on dynamically-emitted method parameters and the MCP SDK's auto-schema serializer. Worth a follow-up issue but does not block #240's stated FR-500 scope.

**PR #239 (squad/227-oop-remoteschema) — APPROVE.** Schema is purely additive: `RemoteToolSchema.Description` unchanged, all new fields nullable with null defaults. Both PS hosts (`oop-host.ps1` top-level helpers, `oop-host-pool.ps1` inline helpers) emit identical PascalCase keys matching C# DTO properties. `OutOfProcessCommandExecutor.cs:124` uses `PropertyNameCaseInsensitive=true`. **Empirical proof:** re-ran the OOP capture and diffed — **0 description diffs across all 144 tools**, byte-identical Description fields. Both PRs CI green, mergeable.

**Patterns worth remembering:**
- For PR self-review blocked by EMU on usepowershell/* — always use `gh pr comment` with a `Posting via gh pr comment instead of gh pr review --approve` disclaimer; never claim formal approval.
- The capture-snapshots.ps1 script overwrites both baseline files. To validate without disturbing main: copy baselines to `C:\Users\stmuraws\AppData\Local\Temp` first, run capture, diff in PowerShell, then `Copy-Item -Force` back and `git checkout --` the others.
- `PowerShellSchemaGenerator.CreateParameterSchema` exists but is **not called from McpToolFactoryV2** in the in-process path — the actual inputSchema comes from the MCP SDK reflecting on the dynamically-generated assembly. So passing `HelpParameterDescription: null` in the schema generator's request is fine because that code path isn't on the hot path; the real wiring is the `[Description]` attribute emission in the assembly generator.
- For OOP additive DTO changes, `System.Text.Json` with `PropertyNameCaseInsensitive=true` makes both directions safe (extra fields ignored on old client, missing fields default to null on new client).


### 2026-05-12: PR #241 fact-check (Hermes — wire OOP through IToolMetadataSource seam, #228) — VERIFIED

**Verdict:** ✅ VERIFIED. Comment: https://github.com/usepowershell/PoshMcp/pull/241#issuecomment-4436357123

**Method:** Captured both `tools/list` snapshots from worktree `poshmcp-228` @ `7c19106` (backed up committed baselines to `C:\Users\stmuraws\AppData\Local\Temp`, restored after capture — committed corpus unchanged). Ran `dotnet build PoshMcp.sln -c Release` and `dotnet test ... --filter "Category!=Integration" --no-build`. Traced the new OOP entry points (`CreateRemoteCommandMetadataMapping` + `BuildRemoteParameterDescriptionMap`) through `_toolMetadataSource` → `HelpAwareToolMetadataSource` (DI-registered in `HttpServerHost.cs:287` and `StdioServerHost.cs:148`) → `DescriptionSanitizer.Normalize` to confirm FR-540 parity.

**Findings:**
- Build: 3 warnings, all pre-existing (2× NU1510, 1× CS8604 in `WinPsCompatProxyMethodGenerationTests.cs` which is not in PR diff). Zero new warnings — claim verified.
- Tests: `Failed: 0, Passed: 684, Skipped: 7` — exact match to PR body.
- Mode parity for all 6 HelpParityFixture commands: tool-level descriptions byte-identical across in-process and OOP. Parameter-level descriptions also byte-identical (both empty for fixtures — see observations).
- Sanitizer: every new OOP description path routes through `HelpAwareToolMetadataSource.ResolveToolDescription` and `ResolveParameterDescription`, both of which call `DescriptionSanitizer.Normalize` before `TruncateAtWordBoundary`. Same sanitizer pipeline as in-process.
- Backward compat: all new `RemoteToolSchema` fields nullable; PR diff guards every consumer with `IsNullOrWhiteSpace` or explicit null/length checks; legacy `GenerateAssembly(schemas, logger)` overload preserved; IL emit only when `parameterDescriptions != null && TryGetValue` succeeds.

**Non-blocking observations posted on PR:**
- OOP snapshot vs pre-spec010 OOP baseline: 0/144 tool-description diffs. Fixture parity with in-process was coincidentally already present before this PR (because the in-process precedence chain post-#240 happens to land on the same syntax strings the pre-spec010 OOP host emitted). The PR's "now match in-process output" framing is technically true post-PR but no visible snapshot delta is produced by this commit alone. The architectural value (uniform seam-based precedence so future changes affect both modes) is real — worth one line in PR body so reviewers don't expect a visible delta.
- Parameter-level `inputSchema.properties.<name>.description` is empty for every parameter of every tool in both modes (in-process and OOP). FR-520 parity is satisfied (both modes byte-identical empty). Whether FR-510 precedence is actually emitting parameter descriptions for non-fixture commands is a question outside this PR's scope.

**Patterns worth remembering:**
- When a PR's claim is "X now matches Y," verify both (a) X and Y match post-PR and (b) the pre-PR state. If pre-PR already matched, the PR is wiring/architectural rather than behavior-changing — both can be legitimate but should be framed correctly to set reviewer expectations.
- Backward-compat checks on extended DTOs reduce to three things: nullable type signatures, defensive guards on every read site, and a preserved overload signature for any method whose surface changed. The PR diff did all three. Worth keeping as a checklist for future DTO-extension PRs.
- The `IToolMetadataSource` seam is the single best place to gate sanitization in this codebase — if a new code path routes through `ResolveToolDescription` / `ResolveParameterDescription`, `DescriptionSanitizer.Normalize` is guaranteed via `HelpAwareToolMetadataSource`. Verifying sanitizer coverage reduces to "does the path call the seam?" which is one `grep` per entry point.
- `capture-snapshots.ps1` overwrites the committed baseline files in-place. ALWAYS back them up to `\C:\Users\stmuraws\AppData\Local\Temp` before running and restore after. This makes the script safe to run for verification without polluting the working tree.


### 2026-05-13: PR #243 fact-check (issue #229, spec 010 wave 5) - APPROVED
- Verified PR body numbers by running tests in worktree: 23 passed + 2 skipped on new test trio (1m 36s), 501/501 unit suite green (1m 11s). Both match the PR body exactly.
- Spot-checked 4 test methods (parity count, parity per-param, resolver determinism, regression equal-or-superset) - all behave as their names declare. Param parity test correctly uses union-of-keys (not intersection) so a missing parameter on one side surfaces as a parity failure, not a silent skip.
- Issue #242 is factually accurate: names the seam (resolver returns correct strings per ParameterSetConsistencyTests, gap is in inputSchema JSON output), points at HelpParityFixture for repro, lists the 10 specific [Theory(Skip)] methods as the regression gate, gives crisp acceptance criteria.
- Skip messages on the 10 ParameterDescription_IsNonEmpty_* variants reference issue #242 verbatim and accurately describe the gap. Reviewers landing on a skipped test can navigate straight to the tracking issue.
- Baseline files exist (specs/010-tool-self-documentation/baseline/{inprocess,oop}-tools-list.json) and ToolDescriptionRegressionTests.LoadBaseline reads them via workspace-root walk-up to PoshMcp.sln, then unwraps esult.tools JArray. Parsing matches captured snapshot shape.
- The gap I raised in PR #241 is unchanged: parity test passing while the non-empty test would fail is only consistent if descriptions are uniformly empty in both modes - exactly what PR #241 reported.
- Concurred with Farnsworth's non-blocking nit on IsEqualOrSuperset.Contains(baseline) being broader than FR-550 wording. Not a gate.
- Lesson: when verifying a PR that confirms a previous finding I raised, run the tests myself in the worktree rather than trusting the body. Two test commands took ~3 minutes total and turned "the PR says 23/2 and 501/501" into verified ground truth.

## 2026-05-13: Fact-checked PR #244 (issue #230, spec 010 step 8) — APPROVE
**Requested by:** Steven (via Ralph)
**PR:** Bender — descriptionSource doctor reporting (FR-582/FR-583/SC-207)

### Verified facts
- Vocabulary literals in DescriptionSourceVocabulary.ToWireValue match spec.md FR-583 line 137 exactly: tools synopsis|description|syntax|name, params helpParameter|helpMessage|validateSet|typeFallback. Case-sensitive match.
- 521 unit tests pass (12 new) — ran `dotnet test --filter FullyQualifiedName~Unit` in poshmcp-230 worktree.
- Spot-checked 3 of 12 new tests in `DoctorDescriptionSourceTests.cs`; each test name accurately describes what is asserted (enum value + JSON wire string).
- Confirmed Farnsworth's finding: `BuildDoctorReportFromConfig` (DoctorService.cs:180) does not instantiate or accept a tracker. Only `BuildDoctorReportForCliAsync` (line 92) wires it. Runtime MCP doctor returns empty `tools[]` until follow-up.
- Confirmed #242 still present: PR diff touches no schema-emission files (no PowerShellAssemblyGenerator.cs changes). Tracker reads resolver result, not inputSchema.
- Decision drop `bender-description-source-vocabulary.md` correctly pins vocabulary + tracker reuse pattern for Amy's #231 work.

### Minor observation (non-blocking)
PR includes `.squad/scribe-session-report.txt` — stray Scribe artifact. Worth a .gitignore entry next pass.

### Pattern noted
For PRs that ship vocabulary-as-API: always character-by-character diff the wire literals against the spec FR text, both case and ordering. Implementation correctness alone isn't enough — the strings ARE the contract.

## 2026-05-13: Fact-checked PR #244 (issue #230, spec 010 step 8) — APPROVE
**Requested by:** Steven (via Ralph)
**PR:** Bender — descriptionSource doctor reporting (FR-582/FR-583/SC-207)

### Verified facts
- Vocabulary literals in DescriptionSourceVocabulary.ToWireValue match spec.md FR-583 line 137 exactly: tools synopsis|description|syntax|name, params helpParameter|helpMessage|validateSet|typeFallback. Case-sensitive match.
- 521 unit tests pass (12 new) — ran `dotnet test --filter FullyQualifiedName~Unit` in poshmcp-230 worktree.
- Spot-checked 3 of 12 new tests in `DoctorDescriptionSourceTests.cs`; each test name accurately describes what is asserted (enum value + JSON wire string).
- Confirmed Farnsworth's finding: `BuildDoctorReportFromConfig` (DoctorService.cs:180) does not instantiate or accept a tracker. Only `BuildDoctorReportForCliAsync` (line 92) wires it. Runtime MCP doctor returns empty `tools[]` until follow-up.
- Confirmed #242 still present: PR diff touches no schema-emission files (no PowerShellAssemblyGenerator.cs changes). Tracker reads resolver result, not inputSchema.
- Decision drop `bender-description-source-vocabulary.md` correctly pins vocabulary + tracker reuse pattern for Amy's #231 work.

### Minor observation (non-blocking)
PR includes `.squad/scribe-session-report.txt` — stray Scribe artifact. Worth a .gitignore entry next pass.

### Pattern noted
For PRs that ship vocabulary-as-API: always character-by-character diff the wire literals against the spec FR text, both case and ordering. Implementation correctness alone isn't enough — the strings ARE the contract.

## 2026-05-13 — PR #245 Fact-Check (FR-590 OTel description-source counters)

**Verdict:** ✅ APPROVE. Posted comment-id 4441777768.

**What I verified (Steven, via Ralph):**
- Counter names exact: `poshmcp.tool_description.source` + `poshmcp.parameter_description.source` declared in `McpMetrics.cs` (~L144, ~L148).
- Tag literals exact via `[InlineData]` rows + `MeterListener` capture: tool {synopsis,description,syntax,name}; parameter {helpParameter,helpMessage,validateSet,typeFallback}.
- 11 new tests in `DescriptionSourceMetricsTests` (4+4+1+1+1). My rerun: 532/532 passed (Amy reported 531/532; OOP flake passed on rerun = transient confirmed).
- 4/4 Resolve* sites paired (lines 268-269, 625-626, 671-672, 750-751 in McpToolFactoryV2.cs).
- Failure isolation: bare `catch` swallows all incl. `ArgumentOutOfRangeException` from vocabulary on unknown enums; null-meter early-return; both paths exercised by tests.
- Vocabulary single-source: grep for FR-583 wire literals across both files = zero matches; tags flow exclusively via `DescriptionSourceVocabulary.ToWireValue`.
- #242 untouched: diff is exactly 3 files (McpToolFactoryV2.cs, Metrics/McpMetrics.cs, new test file).

**Operational lesson — gh CLI on Windows mangles UTF-8 in --body-file and --input:**
`gh.exe` reads file content using the OEM code page (CP437/CP850 on en-US Windows), not UTF-8. Em-dash bytes `E2 80 94` get re-encoded as `ΓÇö`. Affected both `gh pr comment --body-file` and `gh api --input`. Workaround: bypass gh and POST/PATCH directly via `urllib.request` from Python with `Content-Type: application/json; charset=utf-8` and the auth token from `gh auth token`. Confirmed clean storage afterward (6 em-dashes, 8 checks counted in the API response). Worth recording as a team skill if it bites again.

No decision drop — no architectural gap found.

## 2026-05-13 — PR #246 fact-check (issue #232, FR-572)

Reviewed Amy's bench artifacts PR. Verified:
- Numbers in COMPARISON.md and PR body match raw BDN .md/.csv exactly.
- Run-5 baseline (ench-runs/run-5-pre-spec010/) untouched (last commit a4440e6, parent of PR).
- Recomputed Mean deltas: Single +11.74% ✓, Pool +12.0851% (reported 12.08%, 1bp display-rounding noise — not blocking), ProcessPool +10.66% ✓. All << 50% gate.
- FR-572 wording: spec.md L240 (> 50% triggers redesign) and L387 (< 50%) both consistent. PR uses correct baseline + formula.
- Diff scope: exactly 5 files under bench-runs/run-6-post-spec010/. Zero source changes.

**Verdict: APPROVE.** Posted to PR.

**Lesson:** When BDN reports show 3-decimal seconds, recomputed % deltas can differ ~0.01% from the report's % column because the underlying ns means have higher precision. Treat sub-basis-point discrepancies as rounding artifacts, not math errors. The CSV doesn't expose the higher-precision values either — only the displayed seconds.

## 2026-05-13 — PR #246 fact-check (issue #232, FR-572)

Reviewed Amy's bench artifacts PR. Verified:
- Numbers in COMPARISON.md and PR body match raw BDN .md/.csv exactly.
- Run-5 baseline (bench-runs/run-5-pre-spec010/) untouched (last commit a4440e6, parent of PR).
- Recomputed Mean deltas: Single +11.74%, Pool +12.0851% (reported 12.08%, 1bp display-rounding noise, not blocking), ProcessPool +10.66%. All well under 50% gate.
- FR-572 wording: spec.md L240 ("> 50% triggers redesign") and L387 ("< 50%") both consistent. PR uses correct baseline and formula.
- Diff scope: exactly 5 files under bench-runs/run-6-post-spec010/. Zero source changes.

Verdict: APPROVE. Posted to PR (comment 4442013557).

Lesson: When BDN reports show 3-decimal seconds, recomputed % deltas can differ ~0.01% from the report's own % column because the underlying ns means have higher precision than the display. Treat sub-basis-point discrepancies as rounding artifacts, not math errors. The CSV does not expose the higher-precision values either - only displayed seconds.

## 2026-05-13 — PR #247 fact-check (docs precedence chains, issue #234)

**Verdict: APPROVE.** All facts clean.

- Vocabulary literals in doc tables match DescriptionSourceVocabulary.ToWireValue (IToolDescriptionSourceTracker.cs:150-168) byte-for-byte: synopsis|description|syntax|name and helpParameter|helpMessage|validateSet|typeFallback.
- Counter names exact: poshmcp.tool_description.source + poshmcp.parameter_description.source declared at McpMetrics.cs:143/147.
- #242 callout accurately describes current state I verified in PRs #243-#245: resolver returns right value with right Source, doctor reports correct source, but inputSchema.properties.<name>.description plumbing not wired into tools/list payload.
- All FR numbers (500, 510, 540, 541, 542, 582, 583, 590) verified present in specs/010-tool-self-documentation/spec.md. No invented FRs.
- Length caps numeric: FR-541 = 1024 chars (line 216), FR-542 = 512 chars (line 217). Code: ToolDescriptionMaxLength = 1024 at HelpAwareToolMetadataSource.cs:21. Match.
- Doctor JSON paths in doc (tools[].descriptionSource, tools[].parameters[].descriptionSource) match spec FR-583 line 250 verbatim.
- Worked example Get-WidgetReport synthesized (not a fixture) but predicted resolutions follow chain rules correctly. Fixture functions referenced (FixtureSynopsisOnly, FullHelp, HelpMessageOnly, ValidateSetScalar, Bare) all exist in HelpParityFixture.psm1 lines 11/21/56/68/88.
- Diff scope: README.md +1/-1 (single bullet edit) and docs/articles/exposing-tools.md +186 appended. Zero code changes.
- README cross-link docs/articles/exposing-tools.md#description-precedence resolves to ## Description Precedence H2 — slug correct for both GitHub and DocFX.

**Pattern:** docs PRs that anchor every chain step to a code symbol are fast to fact-check — grep the wire-vocabulary class once, walk the table top to bottom. Leela's table format (Step | Source | Notes) made each row a one-shot grep target. Recommend adopting this format for future precedence-chain docs.

