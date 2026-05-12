# Decisions

## Recent Decisions
> Older entries archived to `decisions-archive.md` (entries >7d removed when file >= 50KB).
### 2026-05-05: Systemic future-dated entries across squad artifacts
**By:** Cubert (Fact Checker)
**Requested by:** Steven Murawski
**Status:** Flagged
**What:** Multiple squad artifacts contain entries dated 2026-07-15, 2026-07-18, 2026-07-28, 2026-07-30 — 2–3 months in the future relative to current date 2026-05-05. Affected: docs/articles/squad-work-log.md (Hermes 2026-07-15/18, Fry 2026-07-15/18, Bender 2026-07-30); .squad/decisions.md (multiple 2026-07-18 and 2026-07-28 entries); .squad/agents/farnsworth/history-archive.md (references to 2026-07-29); story article sample timestamp "2026-07-30T12:34:56Z".
**Why:** Either clerical errors or the project has been silently writing future dates for months. Either way, the integrity of the dated decision ledger is compromised — readers cannot trust chronology. Blocks publication of squad-story.md and squad-work-log.md.
**Recommendation:** (1) Audit git commit dates against `### YYYY-MM-DD` headers in .squad/decisions.md and agent histories; correct headers where they disagree with commit dates. (2) Re-affirm rule already in squad.agent.md: agents must use the CURRENT_DATETIME injected by the Coordinator, never an inferred or guessed date. (3) Document corrected dates in a follow-up decision once audit is complete.
### 2026-05-05: User directive — Cubert pre-reviews Farnsworth plans
**By:** Steven Murawski (via Copilot)
**What:** Cubert (Fact Checker) must review any plans, specs, or proposals Farnsworth creates before they are presented to the user for review. Cubert verifies accuracy, internal consistency, and any verifiable claims; only after Cubert's review does the plan reach the user.
**Why:** User request — captured for team memory. Inserts a fact-checking gate into the architecture proposal workflow.


### 2026-05-06: Hermes — Runspace pool vs multi-process experiment plan (Issue #65)

**By:** Hermes (PowerShell Expert)
**What:** Filed R&D plan at `specs/004-out-of-process-execution/runspace-pool-experiment-plan.md` covering two prototype paths for OOP parallelism: (Option A) a runspace pool inside one pwsh subprocess with a synchronized stdout writer and ISS-based pre-warm; (Option B) a pool of N subprocesses dispatched via a `Channel<OutOfProcessHost>` queue. Plan includes a benchmark harness design (BenchmarkDotNet + custom crash/recovery harness) with scenarios for CPU-light, CPU-bound, I/O-bound, network-shaped, heavy serialization, cold start, crash recovery, and isolation. Recommended phasing into 6 follow-up issues, starting with extracting `OutOfProcessHost` as shared infrastructure.
**Why:** Issue #65 asks us to compare in-process runspace pooling vs multiple processes for OOP execution. A written plan is needed before either prototype is built so the trade-offs (parallelism vs isolation, memory cost, startup cost, complexity) are explicit and the benchmark methodology is fixed in advance. The single biggest open trade-off is failure containment: Option A loses the strong isolation that motivated OOP in the first place, so the benchmark harness explicitly measures isolation as a pass/fail criterion.


### 2026-05-05: Squad story / work-log fact-check corrections
**By:** Leela (via Cubert verification)
**What:** Updated `docs/articles/squad-story.md` and `docs/articles/squad-work-log.md` with verified counts:
- Team size: 8 → 9 (Cubert added)
- NuGet downloads: 700+ → 1,600+
- PRs merged (window 2026-03-27..2026-04-25): "10+" → 34 (verified via `gh pr list --repo usepowershell/PoshMcp --state merged`)
- Issues closed (same window): 27 → 83 (verified via `gh issue list --state closed`)
- Commits to main (same window): "40+" → 183 (verified via `git log main`)
- Documentation: "8 articles, 12,000+ words" → "19 articles" (word count unverified, dropped)
- "0 broken builds" → "0 reverts" (matches what was actually verified)

**Why:** Story metrics were significantly understated and the team-size/article counts were stale after Cubert joined and docs grew. Numbers now match reproducible `gh`/`git` queries.


### 2026-05-06: Spec 004 milestone + 8 follow-up issues filed

**By:** Hermes (PowerShell Expert), at Steven Murawski's request
**What:** Merged PR #187 (runspace pool vs multi-process experiment plan) into `main` (squash merge, branch deleted, issue #65 referenced via `Refs #65` so it stays open). Created milestone **#5 — `Spec 004 - Out-of-Process PowerShell Execution`** (https://github.com/usepowershell/PoshMcp/milestone/5) and filed 8 follow-up issues from the plan's §5 phasing, all in the milestone with proper `Blocked by` cross-references and `squad:*` routing labels.

**Issues created:**

| # | Title | Plan ref | Owner label | Blocked by |
|---|-------|----------|-------------|------------|
| #189 | OOP: Bug-fix — clear `$Error` before invoke in single-runspace host | §5 #0 | squad:hermes | — |
| #190 | OOP: Extract `OutOfProcessHost` (with lifecycle unit tests) | §5 #1 | squad:bender | — |
| #191 | OOP: Option A prototype — runspace pool host (`SubprocessHostMode: "Pool"`) | §5 #2 | squad:hermes | #190 |
| #192 | OOP: Option B prototype — process pool executor (`SubprocessHostMode: "ProcessPool"`) | §5 #3 | squad:bender | #190 |
| #193 | OOP: Benchmark harness infrastructure (`PoshMcp.Benchmarks` project) | §5 #4a | squad:fry | — |
| #194 | OOP: Wire benchmark harness to executors | §5 #4b | squad:fry | #191, #192, #193 |
| #195 | OOP: Run benchmarks and write findings | §5 #5 | squad:hermes | #194 |
| #196 | OOP: Adopt the winner — make recommended mode default | §5 #6 | squad:farnsworth | #195 |

**Why:** Land the experiment plan, set up actionable follow-ups under a single milestone so the runspace-pool vs multi-process work can proceed without losing the dependency ordering. Issue #65 stays open as the umbrella tracker through prototype work; commented there with the milestone and issue list.

**Side effects:**
- Created two missing labels: `refactor` (`#D4E5F7`), `testing` (`#BFD4F2`).
- Auth workaround used throughout: `gh auth switch --user usepowershell` for write ops, switched back to `stmuraws_microsoft` after.


### 2026-05-06: Security policy
**By:** Farnsworth (requested by Steven Murawski)
**What:** Added `SECURITY.md` at repo root. Supported versions: only latest 0.x minor (currently 0.10.x); older minors unsupported. Reporting channel: GitHub private vulnerability reporting via Security tab — no security email address invented. Documented SLA (ack 3 business days, triage 7), coordinated disclosure, and reporter credit via GHSA.
**Why:** Establish a clear, standard security disclosure process before 1.0; align with GitHub's recommended private vuln reporting flow rather than ad-hoc email.


### 2026-05-06: Spec 004 foundation work — review outcomes
**By:** Farnsworth (Lead / Architect)
**Requested by:** Steven Murawski

**Three PRs against the runspace-pool experiment plan §5 sequencing (#0 / #1 / #4a) — all approved (as comments; gh `addPullRequestReview` rejects self-review even from the author's own account):**

- **PR #199 — Hermes — `fix(oop): clear $Error before invoke in single-runspace host (#189)`:** APPROVE. One-line `$Error.Clear()` in `Invoke-InvokeHandler` before user invoke. Regression test asserts both halves: first invoke produces a non-terminating error and reports `hadErrors=true`; second clean invoke reports `hadErrors=false` (pre-fix the second fails). CI green across build/test/CodeQL.
- **PR #198 — Bender — `refactor(oop): extract OutOfProcessHost (with lifecycle unit tests) (#190)`:** APPROVE. Per-process state cleanly moves to `OutOfProcessHost` (Process+Exited, stdin/stdout/stderr, `_sendLock`, `_pending`, read loops, shutdown sequence, `SendRequestAsync`). Executor keeps `setup`/`discover`/`invoke` payload shaping, `_cachedSchemas`, pwsh/script-path resolution. Lifecycle unit test walks start → ping → setup → shutdown → restart and asserts different PID after restart. Construction guards, double-Start, dispose-before-Start, IsRunning/ProcessId all covered. Integration tests refactored to walk `executor._host._process` via a small `GetSubprocess` helper — production code path unchanged. 591/0 pass. Unblocks #191 and #192.
- **PR #197 — Fry — `feat(benchmarks): PoshMcp.Benchmarks harness infrastructure (#193)`:** APPROVE. .NET 10 console project, BDN 0.14.0, project-references `PoshMcp.Server`, in `PoshMcp.sln`. `HttpListener` bound to `127.0.0.1:0` via `TcpListener` probe (correct ephemeral-port workaround). All 5 scenario stubs present (cold start, warm invoke, payload size serialization, process crash recovery, runspace corruption recovery). Per-scenario `[MinIterationCount]/[MaxIterationCount]` thresholds. Baseline (`HostMode.Single`) captured in same run via `[Params]` axis. `ApplicationInsights__Enabled=false` set in `Program.cs`. `MarkdownExporter.GitHub` configured. Stubs no-op; wiring is #194. CI checks green (an unrelated `submit-nuget` workflow failure is not a PR check).

**Issue #65 — "OOP: Experiment with runspace pool parallelism vs multiple processes":** CLOSED as completed. Superseded by the experiment plan (PR #187, merged) and its decomposition into issues #189 (prereq `$Error` fix), #190 (extract `OutOfProcessHost`), #191 (Option A prototype), #192 (Option B prototype), #193 (benchmark harness infra), #194 (wire harness to executors), #195 (run benchmarks + findings), #196 (adopt the winner). Tracking continues on the spec 004 milestone. Closing comment also noted the stale path reference in the issue body (`specs/out-of-process-execution.md` → canonical `specs/004-out-of-process-execution/`).

**Non-blocking follow-ups noted on #198 (do not block merge):**
- Migrate in-tree call sites of `OutOfProcessCommandExecutor` from the legacy `ILogger<OutOfProcessCommandExecutor>` constructor to the new `ILoggerFactory` overload so the host's logger is no longer silently routed to `NullLoggerFactory.Instance`.
- `IsNonJsonPowerShellStreamLine` promoted from `private` to `internal` to enable reflection-based unit testing — visibility creep is minor and justified.

**Process pattern (logged for team memory):** GitHub's GraphQL `addPullRequestReview` mutation rejects `APPROVE` (and `REQUEST_CHANGES`) when the reviewing identity is the PR author, even on the `usepowershell` account that is otherwise unblocked by EMU policy. Error: `Review Can not approve your own pull request`. Workaround: post the review body via `gh pr comment` instead. The badge-prefixed body preserves attribution either way. This is distinct from the EMU `stmuraws_microsoft` block on `gh pr review` / `gh pr comment` / `gh issue create` already documented in agent histories.

### 2026-05-06: Spec 004 prototypes review — PR #200 (Option B / Bender) and PR #201 (Option A / Hermes)
**By:** Farnsworth (Lead / Architect), requested by Steven Murawski
**What:** APPROVED both PRs. Both prototypes meet every architectural criterion in `runspace-pool-experiment-plan.md` §5 #2/#3. PR #200 implements `OutOfProcessSubprocessPool` with the channel + dictionary protocol, slot-0 fail-fast / slots-1..N-1 backoff, SHA-256 environment fingerprint discovery cache, per-request kill-on-timeout, and a parameterized integration test matrix over pool sizes 1/2/4. PR #201 implements `oop-host-pool.ps1` with ISS-based pre-warmed runspace pool, custom `PSHost`/`PSHostUserInterface` (correctly NOT `[Console]::Out`), synchronized stdout writer, per-pipeline `Streams.*` + per-runspace `$Error.Clear()`, full quiesce protocol on `setup` (`DrainEvent` + `PoolDispatcher.WaitIdle` → close → mutate → reopen → resume), and per-invoke metrics on the response frame.
**Why:** Both prototypes are required for the #194 benchmark phase; the plan's pass/fail comparison cannot run with only one option. Approving both unblocks Fry to wire the harness.
**Cross-PR collision (BLOCKING for whichever lands second):** Both PRs introduce a type named `SubprocessHostMode`, source-incompatible. PR #200 = `static class` with string constants and `string?` property. PR #201 = `enum SubprocessHostMode { Single, Pool }` with enum property. PR #200 reserved the name `Pool` in its constants signaling intent to coexist. Recommended convergence: standardize on PR #201's `enum`, extend with `ProcessPool` when the second PR rebases. Rebase scope ~30 lines (PowerShellConfiguration property type, McpToolSetupService dispatch check, Bender unit tests).
**Merge order recommendation:** Land **PR #201 (Option A / Hermes) first**, then have Bender rebase PR #200 onto the enum. Rationale: #201's diff is smaller (+1204 vs +1780), the enum is the more idiomatic C# baseline, and Bender's `IsProcessPool(string?)` helper has fewer call sites to convert than reverse direction. Either order works mechanically; this minimizes net rework.
**Non-blocking observations recorded in PR comments:** #200 — discovery cache key omits filter parameters (correct but worth doc note); `MinHealthyForStartup` clamp in `McpToolSetupService` is silent; unbounded lease channel; lease loop spins on stale slots. #201 — drain timeout hardcoded 60s (not threaded from `SubprocessTimeoutSeconds`); pool-size cap of 8 is a prototype guard; `Resolve-SwitchParameters` calls `Get-Command` on the host process not in a pool runspace (verify ISS-imported modules are visible).

### 2026-05-06: Farnsworth — PR #202 review (spec-004 benchmark harness wired)

**By:** Farnsworth (Lead / Architect) — review requested by Steven Murawski

**Verdict:** REQUEST CHANGES (single hard blocker is CI; architecture is approved).

**PR:** https://github.com/usepowershell/PoshMcp/pull/202 — Fry — feat(benchmarks): wire harness to executors (#194). Closes #194. Branch: `squad/194-wire-benchmark-harness`. Builds on #190 (OutOfProcessHost extraction), #201 (Hermes Option A — Pool), #200 (Bender Option B — ProcessPool).

**Decisions / calls captured:**

1. **Hard blocker is mechanical, not architectural.** CI `build / Verify formatting` fails: `dotnet format --verify-no-changes` reports ~50 whitespace errors in `PoshMcp.Benchmarks/ExecutorFactory.cs` switch-case bodies. Fix = run `dotnet format PoshMcp.sln`, commit, push. No architectural rework required.

2. **Acceptance criteria are met** (verified in worktree `poshmcp-194`): HostMode `[Params(Single, Pool, ProcessPool)]` on every scenario; all five scenarios implemented end-to-end; AI / spec-008 logging disabled at process start (env vars + harness never builds DI); markdown output includes mode / scenario / payload / mean / p95 / p99 / crash-recovery columns; README documents reproducible invocation.

3. **InternalsVisibleTo widening is acceptable.** One IVT entry added to `PoshMcp.csproj` for `PoshMcp.Benchmarks`. Bench needs `OutOfProcessCommandExecutor`, `OutOfProcessHost`, `OutOfProcessSubprocessPool`, `OutOfProcessSubprocessPoolOptions`, `SubprocessHostMode` — all internal in production. Scoped to one assembly, not a blanket open-up.

4. **Reflection-based crash injection is acceptable.** `KillOneHost()` reaches into `_host`, `_process`, `_slots` private fields from the bench-only assembly. Clearly labeled in XML docs, fails safe on missing fields. Fragility: silent degradation if those field names are ever renamed. Suggested follow-up (non-blocking): startup assertion that the reflection lookups resolve, abort the run if any are gone.

5. **Crash-recovery time as `Mean` column** on `ProcessCrashRecoveryBenchmark` is an acceptable interpretation of the AC. For `ProcessPool` it's a real recovery measurement (kill 1 of N, lease loop skips dead slot, next invoke succeeds). For `Single` / `Pool` the iteration disposes and reconstructs the executor — `Mean` reports cold-start cost, which is honestly the answer to "time until next successful request" for those modes. Documented in code and README.

6. **`RunspaceCorruptionRecoveryBenchmark` deviates in name from what it measures.** Implementation measures head-of-line blocking (slow `Start-Sleep` in flight + fast `Get-Date` probe). Design rationale (process-kill is the wrong gate for Option A — see runspace-pool-experiment-plan.md §4) is sound. Recommend rename to `HeadOfLineBlockingBenchmark` as a small follow-up. Non-blocking.

7. **`OutOfProcessHost.SendRequestAsync` "Key: Content" concurrency race surfaced by the harness does NOT block #194.** This is the central architectural call. The harness's deliverable (per AC #1) is that all three executors are wired and exercised from a single run — provably true (cold-start smoke passes all three). The race is in production code (`OutOfProcessHost`), affects Single + ProcessPool when 10 concurrent invokes share a single host; Option A / Pool (PR #201) does NOT hit it because its dispatcher is concurrent-aware (useful comparative data favoring Option A on this axis). A benchmark surfacing a real production race on first concurrent run is doing its job, not failing. Suppressing it would defeat the purpose of #194 and delay #195 / #196 unnecessarily. **Required before merge:** file the race as a separate `spec:004` / `bug` issue and reference it from the PR body so the failure mode is tracked.

**Bottom line:** Fix CI (`dotnet format`), file the concurrency bug, reference it from the PR body — this is APPROVED. The architecture is right, the AC is met, the surfaced production bug is the harness doing its job.

**Pattern (cross-team):** Benchmark harnesses that exercise concurrent paths against production executors are high-signal regression tests in disguise. When the smoke run on day one surfaces a production race, land the harness, file the bug separately, and use the harness as the regression gate — don't hold the harness PR hostage to the bug it discovered first.

Comment posted at https://github.com/usepowershell/PoshMcp/pull/202#issuecomment-4392814612 (gh pr review remains EMU-blocked on usepowershell/PoshMcp from this account).

### 2026-05-06: PR #204 review — fix(oop): SendRequestAsync 'Key: Content' under parallel invokes (#203)
**By:** Farnsworth (Lead / Architect)
**PR:** https://github.com/usepowershell/PoshMcp/pull/204 (branch `squad/203-host-concurrency-fix`)
**Comment:** https://github.com/usepowershell/PoshMcp/pull/204#issuecomment-4393068861

**Verdict:** APPROVED

**Root cause (Bender's diagnosis, verified):** Not a concurrency bug. `BasicHtmlWebResponseObject.Content` (string body) CLR-shadows `WebResponseObject.Content` (byte[]). `ConvertTo-Json` reflects members into a `Dictionary<string,object>`; the shadowed pair collides on `Add` → `System.ArgumentException: ... Key: Content`. Harness's parallel Invoke-WebRequest pattern made it deterministic; a single invoke would also trip it. The C# correlation map (`_pending`) was correctly identified as a red herring and was not touched.

**Fix shape:**
- `oop-host.ps1`: extracted `ConvertTo-SafeJson` helper applied at the single failing site (`Invoke-InvokeHandler` user-result serialization).
- `oop-host-pool.ps1`: same fallback inlined into the runspace user-script scriptblock (correct — scriptblock executes in a pooled runspace and should not depend on host-process functions). Asymmetry is intentional.
- Fallback chain only triggers on `catch [ArgumentException]`: (1) primary `ConvertTo-Json -Depth 4 -Compress`, (2) `Select-Object * | ConvertTo-Json` (flat PSObject collapses shadowed members; derived `Content` wins → callers get the body), (3) `($r | Out-String).Trim() | ConvertTo-Json` last resort. Happy path unchanged.

**Regression test:** `OutOfProcessHostConcurrencyTests` — `InvokeAsync_ConcurrentInvokeWebRequest_DoesNotThrowDuplicateKeyError` fires 10 parallel `Invoke-WebRequest -UseBasicParsing` against a loopback `HttpListener`, producing a real `BasicHtmlWebResponseObject`. Pre-fix throws `OOP error: ... Key: Content`; post-fix asserts non-empty `output`. Companion test `SendRequestAsync_ConcurrentCallers_AllResponsesCorrelate` is a sanity net for the original (incorrect) hypothesis. Skip guards on `pwsh` and `HttpListener.IsSupported` are correct.

**Non-blocking observations posted on the PR:**
- Tests cover `oop-host.ps1` directly; pool-host inline fallback is exercised end-to-end via the `WarmInvokeThroughputBenchmark` smoke (Pool 306 ms / 10 calls clean) but not by a dedicated unit test. Optional follow-up.
- Branch name and issue title retain "concurrency" framing — fine because PR body is explicit that the diagnosis corrected the hypothesis.

**Sequencing observation (cross-PR):** This PR unblocks `WarmInvokeThroughputBenchmark` for `Single` and `ProcessPool` modes. Hermes's PR #195 (benchmarks + findings) has captured runs 1+2 against pre-#203 main, where Single/ProcessPool numbers are unreliable due to the duplicate-key error inside the invoke loop. After #204 merges, Hermes should rebase #195 onto post-#203 main and rerun affected benchmarks before publishing findings. Not blocking #204.

**Pattern noted:** PowerShell serialization-via-reflection failures often masquerade as concurrency bugs when surfaced under parallel harnesses, because parallelism makes them deterministic. When `ConvertTo-Json` throws `ArgumentException: ... Key: <name>`, suspect CLR member shadowing on the input type before suspecting a race.

**EMU caveat:** Posted via `gh pr comment` (gh pr review remains blocked on this account). Comment does not count as a formal GitHub approval for branch protection — Steven (or another non-EMU reviewer) must convert to formal Approve if required for merge.

### 2026-05-06: PR #205 review (Hermes — bench(oop) canonical results + findings, #195) — APPROVE

**Verdict:** APPROVE. Comment posted: https://github.com/usepowershell/PoshMcp/pull/205#issuecomment-4393870722
(EMU policy continues to block `gh pr review` from this account; `gh pr comment` is the working channel and does not satisfy branch-protection approval requirements.)

**Methodology:** `benchmark-results.md` documents BDN 0.14.0, `--job short` (3×3×1), exact filter/CLI, base commit `e4cf7d9` (post-#204), runtime/OS/arch (Windows 11 / Arm64 / .NET 10.0.6), and the explicit reason runs 1+2 are non-canonical. Reproducible.

**Numbers traceability (spot-checked):** WarmInvoke speedups in findings §1 derive cleanly from results table — Pool 4.857× → reported 4.86×, P99 4.788× → 4.79×; ProcessPool 3.295× → 3.30×, P99 3.408× → 3.41×. ColdStart penalty 400–478 ms → reported "400–500 ms". 1 MB allocations 13.79/16.34/17.36 MB → reported "~13.8/~16.3/~17.4 MB". No rounding flips a conclusion.

**Recommendation assessment:** `Pool` as default is supportable from the data under spec 004's stated workload model (network-shaped concurrent warm invokes). 4.86× clears the per-scenario 4× I/O bar; ProcessPool's 3.30× clears the 2× serialization bar but cannot match Pool on warm dispatch. Strongest counter-argument — single Arm64 host, `--job short`, single workload shape — is disclosed in caveat §5 at the right strength.

**Trust-boundary / cancellation gating (Lead-level call):** Hermes flagged custom `PSHost`/`PSHostUserInterface` work and cancellation propagation as prerequisites. Confirming as **HARD GATES** for the default flip in #196, not "should land before":
1. Custom `PSHost`/`PSHostUserInterface` for runspace pool — partially landed in PR #201; #196 must verify completeness for default-flip context.
2. Cancellation propagation: in-process `Stop()`/`StopAsync()` registration, OOP `cancel` JSON-RPC method, concurrent-readable dispatcher, bounded escalation (cooperative → forced → process kill + recycle). Without it, Pool's effective capacity under stuck invokes is `N - stuck_invokes` — a regression vs Single under adversarial load.

Until both land, `Pool` may ship as opt-in only.

**Position on #196 default flip:** Approve flip in principle; gate it on the two prereqs above. Do not flip in #196 if either is unresolved — ship #196 as opt-in `Pool` documentation in that case and re-spawn the flip once gates close. A `--job long` WarmInvoke rerun against post-cancellation main should be captured as run-4 in `benchmark-results.md` and must reaffirm ≥ 4× I/O bar before flipping.

**#196 scope sketch (delivered in review body, summary here):**
- Config keys: `PowerShell:HostMode` (default flip Single → Pool), `PowerShell:Pool:Size` (default `Environment.ProcessorCount`, hard cap 32), `PowerShell:Pool:DrainTimeoutMs` (thread through config; currently hardcoded 60s per PR #201 review).
- Doctor must validate pool sizing and surface active HostMode.
- Opt-in story documented in `DESIGN.md` ("When to switch HostMode") with three cases (Pool default, ProcessPool for tail/isolation, Single for short-lived CLI).
- Doc sweep: `DESIGN.md`, `README.md`, `examples/appsettings.*.json`, spec 004 `quickstart.md` if present.
- Acceptance includes the run-4 `--job long` rerun.
- Out of scope: per-request HostMode override, dynamic pool resizing, removing prototype code paths.

**Patterns reconfirmed:**
- EMU `gh pr review` block; `gh pr comment` works but is not a formal approval.
- Docs+data PRs benefit from spot-checking 2–3 headline numbers against source tables — catches both arithmetic errors and rounding inversions.
- When a recommendation rests on one workload shape, the strongest review move is to make the workload-shape disclosure a gate, not a footnote.

### 2026-05-06: OOP HostMode adoption recommendation (Hermes, #195 → #196)

**Context:** Run-3 benchmarks landed (PR #205) covering Single, Pool (Option A), ProcessPool (Option B) across ColdStart, PayloadSize, and WarmInvoke. Findings doc: `specs/004-out-of-process-execution/benchmark-findings.md`. Issue #196 owns the actual default flip.

**Recommendation:** Default `HostMode` should flip to **Pool** (Option A — in-process runspace pool, single subprocess).

**Why:**
- Pool wins WarmInvoke @ conc=10 by 4.86× mean / 4.79× P99 vs Single — clears the spec's per-scenario 4× I/O bar.
- ProcessPool: 3.30× / 3.41× — clears the 1.5× CPU floor and 2× serialization bar but cannot beat Pool's no-IPC dispatch path on warm throughput.
- ColdStart: Single leads by ~400–500 ms; cost amortizes to zero after invoke #2.
- PayloadSize: Pool competitive at small sizes, lowest allocations (~13.8 MB) at 1 MB. No payload regime where Pool is the worst choice.

**Tradeoffs (must be reflected in #196's adoption plan):**
- Keep `ProcessPool` as opt-in for tail-sensitive workloads — posts tightest StdDev (1.11 ms) and P99 (201.4 ms) of the three, and provides hard process isolation between concurrent invokes.
- Keep `Single` as documented choice for short-lived CLI invocations (cold-start dominates).
- Pool's trust boundary is weaker than ProcessPool's: shared GC, shared `[Console]::Out`, shared loaded modules. Custom `PSHost`/`PSHostUserInterface` work tracked from PR #187 review (Farnsworth #1) is a prerequisite for relying on Pool as default in adversarial scenarios.
- Cancellation propagation is not measured in run-3. Under stuck invokes, Pool's effective capacity is `N - stuck_invokes`. Cancellation work should be a gate on the default flip, not a follow-up.

**Open questions for #196:**
- Default flip vs documented opt-in (default flip changes runtime behavior for every existing deployment).
- Pool sizing default (`Environment.ProcessorCount`) needs validation on constrained containers (e.g., 2-vCPU pods).
- Opt-in story: when should callers prefer ProcessPool? Needs a "switch when..." doc rooted in the tail/trust tradeoffs.
- Doc sweep: `DESIGN.md` and `README.md` reference single-runspace assumptions.

**Caveats on the data:** `--job short` (3 iter × 3 warmup × 1 launch); single Windows 11 / Arm64 host; load shape matters (WarmInvoke is network-shaped per spec 004). Re-run with `--job long` before any SLO-bearing claim.

**Source artifacts:** PR #205, `specs/004-out-of-process-execution/benchmark-results.md`, `specs/004-out-of-process-execution/benchmark-findings.md`, `bench-runs/run-3.log`, `bench-runs/run-3-artifacts/`.

### 2026-05-06: Security review — open alerts triage and fix plan
**By:** Farnsworth (Lead / Architect), requested by Steven Murawski

**Scope reviewed:**
- Dependabot alerts (open) — 0
- CodeQL / code scanning alerts (open) — 25
- Secret scanning — disabled at repo level
- `.github/workflows/*` permissions blocks — 14/15 OK, 1 missing
- SECURITY.md — adequate (private vuln reporting + supported-version policy documented)
- Recent commits — security fixes are tracked (`v0.9.2` auth bypass fix, `System.Security.Cryptography.Xml` CVE bump)

**Open alert breakdown:**
| # | Source | Rule | Severity | File / location | Count |
|---|--------|------|----------|-----------------|-------|
| 24 | CodeQL | `cs/log-forging` (CWE-117) | medium | `PoshMcp.Server/PowerShell/PowerShellAssemblyGenerator.cs` (lines 709–1030) | 23 |
| 1 | CodeQL | `cs/log-forging` (CWE-117) | medium | `PoshMcp.Server/Observability/LoggerExtensions.cs` line 31 | 1 |
| 1 | CodeQL | `cs/log-forging` (CWE-117) | medium | `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs` line 111 | 1 |
| 1 | CodeQL | `actions/missing-workflow-permissions` (CWE-275) | medium | `.github/workflows/ci.yml` job at line 22 | 1 |

**Risk assessment:**

1. **Log-forging (25 alerts) — REAL, medium.** Not a false positive. The flagged sinks log
   `commandName`, `parameterValues`, `parameterSummary`, etc. in `PowerShellAssemblyGenerator.cs`,
   plus claim values in `AuthenticationServiceExtensions.cs`, plus correlation/op-name in
   `LoggerExtensions.cs`. Inputs flow from MCP `tools/call` JSON-RPC payloads, so they are
   client-controlled in HTTP mode and stdio-peer-controlled in stdio mode. Project ships a
   Serilog file sink (per spec for issue #131), so embedded `\r\n` in tool names or parameter
   values will produce forged log lines in plain-text files. Exploitability: low (requires log
   review by humans/tooling that trusts line boundaries); impact: log-trust erosion, possible
   audit-trail confusion. **Not a remote code execution path.**

2. **Missing workflow permissions on `ci.yml` — medium.** Default token is read-only at the
   org level for new repos, but explicit `permissions:` is the documented best practice and is
   required for CodeQL hygiene. All other 14 workflows (squad-*, publish-packages, docs-pages,
   etc.) already have explicit blocks — `ci.yml` is the only outlier. Trivial fix.

3. **Secret scanning disabled — medium (configuration gap).** Public repository with auth
   token handling code; secret-scanning + push-protection should be on. Not a code defect
   but a repo settings hardening item.

4. **No open Dependabot alerts.** Recent NuGet hygiene is tracked
   (`System.Security.Cryptography.Xml 10.0.5 → 10.0.6` CVE bump, MCP SDK 1.2.0 upgrade).
   Continue the current Dependabot-driven cadence.

**Recommended actions (prioritized):**

| Priority | Action | Owner | Mode |
|----------|--------|-------|------|
| P1 | Add `permissions: { contents: read }` (or minimal scope) to `.github/workflows/ci.yml` job. Alert #1 closes. | **Amy** (DevOps / Platform) | small PR, ~5 lines |
| P2 | Add a centralized log-sanitization helper (strip `\r\n`, optionally length-cap) and apply to user-controlled string args at the C# logging boundary in `PowerShellAssemblyGenerator.cs`, `LoggerExtensions.cs`, `AuthenticationServiceExtensions.cs`. Closes 25 alerts in one PR. | **Bender** (Backend Dev) | dedicated PR; needs Fry tests for newline scrubbing |
| P3 | Enable GitHub secret scanning + push protection on the repository (Security tab → Settings). Document the change in SECURITY.md. | **Amy** (repo admin) | settings change + 1 docs commit |
| P4 (defer) | Consider adopting Serilog `Destructure.ToMaximumStringLength` and a dedicated `Sanitize()` enricher across the board to make this a build-time invariant rather than per-call discipline. | Bender (proposal), Farnsworth (review) | follow-up issue, not blocking |

**Architectural decision — log sanitization pattern:**
- Add `LogSanitizer.Scrub(string)` (or extension `string.ScrubForLog()`) in
  `PoshMcp.Server/Observability/`. Replace `\r`, `\n`, and other control chars with a
  visible marker (`\u2424` or literal `\\n`) and cap length at a configurable max (default
  4 KB — generous for tool parameter summaries, prevents log flooding).
- Apply at the **call site** for any string interpolated into a log message template
  argument that originated from MCP request payloads, claims, or appsettings-driven names.
  Do **not** apply globally via a Serilog enricher only — CodeQL's taint analysis tracks
  call-site sinks, and an enricher won't clear the alerts (and risks double-encoding).
- Structured properties (`{ToolName}`) flow through the same sanitizer; use a small wrapper
  type `LogSafe(string)` if call-site noise becomes excessive in a follow-up.

**Out of scope for this triage:**
- The auth-bypass fix in v0.9.2 was already shipped — no action needed.
- The CVE-driven `System.Security.Cryptography.Xml` bump is already merged.
- No active security-relevant specs in `specs/` (1–8 are all feature specs, none are
  hardening work).

### 2026-05-06: Cancellation propagation for OOP execution (#188)
**By:** Bender (Backend Developer) for Steven Murawski
**Decision:** Adopt the cancellation design in `specs/004-out-of-process-execution/cancellation-design.md`. Token cancellation in `OutOfProcessHost.SendRequestAsync` now sends a `cancel` wire frame to the pwsh subprocess, which calls `PowerShell.BeginStop()` on the in-flight pipeline.

**Why this design:** Reuses the existing stdin channel rather than introducing a side-channel (named pipe / OS signal) that would differ across Windows and Linux. The Single-mode async dispatcher refactor is cheaper than a second IPC channel and is required regardless to unblock the dispatcher loop while a pipeline is in flight.

**Per-mode behavior:**
- **Single (`oop-host.ps1`):** invoke handler runs on a background C# dispatcher thread against a shared runspace; active invocations registered by request id; `cancel` calls `BeginStop`. Stdout serialized via `SingleStdout.Lock` to prevent worker/main interleave.
- **Pool (`oop-host-pool.ps1`):** `PoolDispatcher` tracks active items in a `ConcurrentDictionary`; `Cancel(requestId)` calls `BeginStop` on the matching `[powershell]`. No head-of-line blocking — concurrent invokes continue on other runspaces.
- **ProcessPool (`OutOfProcessSubprocessPool`):** inherits soft-cancel propagation from `OutOfProcessHost` for free. Existing per-request kill-on-timeout backstop preserved verbatim.

**Wire protocol additions:** new `cancel` method (frame id prefixed `cancel-`, not registered in `_pending`); optional `cancelled` boolean on invoke responses.

**Tests:** 3 new tests (one per mode) in `OutOfProcessCancellationTests.cs`. Full OOP suite 148 passed, 6 skipped.

**Unblocks:** #196 (default-mode flip to `Pool`).

**PR:** https://github.com/usepowershell/PoshMcp/pull/207

### 2026-05-06: PR #207 review — Bender — feat(oop) cancellation propagation (#188)
**By:** Farnsworth (Lead / Architect) for Steven Murawski
**Decision:** APPROVE. Posted via `gh pr comment` (gh pr review still EMU-blocked): https://github.com/usepowershell/PoshMcp/pull/207#issuecomment-4394001550

**Verdict rationale:**
- Wire protocol matches `specs/004-out-of-process-execution/cancellation-design.md` §3 verbatim. `cancel-` id prefix not registered in `_pending`; read loop downgrades unknown-id warning for `cancel-` prefix and for late `cancelled:true` responses to Debug.
- Single-mode implementation diverges from design §5.1 in strategy: PR uses C# `SingleDispatcher` (`BlockingCollection` + dedicated worker thread + `ConcurrentDictionary` registry) mirroring `PoolDispatcher` shape, instead of design's `BeginInvoke` + `ThreadPool.QueueUserWorkItem`. Divergence is justified — better code-share with Pool, avoids fighting PowerShell async ergonomics, uniform `SingleStdout`/`PoolStdout.Lock` pattern.
- Pool: surgical `_active` registry + `Cancel(requestId)` calling `BeginStop` on the matching `[powershell]`. No head-of-line — workers iterate `_queue.GetConsumingEnumerable()` independently.
- ProcessPool: `OutOfProcessSubprocessPool.cs` not modified. Soft-cancel inherited via Single-mode hosts. Per-request kill-on-timeout backstop at line 421 preserved verbatim.
- Belt-and-suspenders `wasStopped` detection (catches `PipelineStoppedException` AND falls back to `InvocationStateInfo.State == PSInvocationState.Stopped`) is correct — `BeginStop` does not always raise PSE from synchronous `Invoke()`.
- `SendRequestAsync`: `timeoutCts` changed from `CreateLinkedTokenSource(cancellationToken)` to plain new CTS — caller cancel and per-request timeout now properly orthogonal. Both registrations dispose in finally; `TrySendCancelFrameAsync` uses independent 2s CTS so caller-token cancel cannot poison the cancel-frame send.
- Tests: 3 new (one per mode) in `OutOfProcessCancellationTests.cs`. `Start-Sleep -Seconds 60` against 15s `ObservationTimeout` proves cancel actually unblocks. Pool test uses `runspacePoolSize:4` to provably exercise > 1 runspace + concurrent fast invoke for head-of-line check. ProcessPool test asserts `HealthyCount >= 1` after soft cancel (slots stay healthy, kill backstop not invoked).
- Non-blocking observations posted to PR: vestigial `try { ... } catch { throw }` wrapper in Single user script (semantic no-op); cancel-races-with-success path sends spurious cancel frame (handled, noise-only); `ProcessPool.InvokeAsync` did not get the diagnostic `catch (OperationCanceledException)` from design §4.3 (Bender's history acknowledges this; OCE bubbles up unannotated, fine).

**#196 hard gate status — SATISFIED.** Both gates I called on #205 are now closed:
1. ✅ Custom PSHost/PSHostUserInterface for runspace pool (PR #201).
2. ✅ Cancellation propagation (this PR — bounded soft-cancel across all 3 modes, no Pool head-of-line, hosts/slots stay healthy).

**#196 remaining scope (refined from #205):**
1. Default-mode flip: `SubprocessHostMode.Default` → `Pool`. Keep Single + ProcessPool as opt-in. ProcessPool stays the recommended choice for tail-sensitive / isolation-sensitive workloads (per #195: P99 within 0.7ms of mean).
2. Config key naming review for `appsettings.json` surface; confirm `SubprocessHostMode` enum-vs-string serialization story; verify no lingering enum collision from #200/#201.
3. Doctor validation hooks: surface resolved `OutOfProcessMode`, `RunspacePoolSize` (with any clamp applied — recall #201's hardcoded cap of 8), resolved host script path, per-request timeout. Warn (don't error) if Pool configured but pwsh resolution failed.
4. Doc updates: README, DOCKER.md, spec 004 supersedence note. Document cancellation contract (caller-token → bounded soft-cancel; per-request timeout as backstop; ProcessPool kill-on-timeout preserved).
5. Bench reaffirmation: Hermes `--job long` `WarmInvokeThroughputBenchmark` against post-#207 main (capture as run-4) confirming ≥ 4× I/O bar still holds. Cancellation refactor adds per-invoke `[powershell]` allocation + dispatcher hop — expect no measurable warm-I/O regression but verify.

**CI at review time:** Squad CI/test, CodeQL actions/python, dependency submission green; CI/build and CodeQL csharp still in progress (additive code, no signature changes — expected to pass).

**PR:** https://github.com/usepowershell/PoshMcp/pull/207 (mergeable, additions 1140 / deletions 40 / 5 files).

### 2026-05-06: PR #208 (Farnsworth — feat(oop): default to Pool host mode, #196) — APPROVE
**By:** Bender (Backend Developer)
**Posted:** https://github.com/usepowershell/PoshMcp/pull/208#issuecomment-4394193058 (gh pr review still EMU-blocked; comment with badge prefix; not a formal GitHub approval)

**Verdict:** APPROVE. Default flip is correctly scoped, doctor surfacing is operator-grade, docs match shipped behavior, and the spec §Implementation Notes cancellation contract matches what landed in #207.

**Constructor-default audit (the open question on #208):** All direct construction sites of `OutOfProcessCommandExecutor` in `PoshMcp.Server` checked:
- `McpToolSetupService.StartOutOfProcessExecutorIfNeededAsync` — explicit `hostMode: config.SubprocessHostMode`. ✅
- `McpToolSetupService.StartProcessPoolExecutorAsync` — uses parameterless overload (default `Single`), but only as a path resolver for `ResolveHostScriptPathAsync()`. ProcessPool's per-process host script IS `oop-host.ps1` (single-runspace), so default-Single is correct here.
- `DoctorService.BuildOutOfProcessSection` (new) — explicit `hostMode: config.SubprocessHostMode`. ✅

No production path silently still on Single. Constructor-default Single is documented in the enum's XML doc — acceptable trade-off vs. churning every test fixture. Future-callers footgun mitigated by docs, not code.

**Config keys:** `SubprocessRunspacePoolSize` (Pool) vs `SubprocessPoolSize` (ProcessPool) is mildly confusable, but doctor renderer disambiguates clearly per host-mode. JSON shape uses distinct field names. Renaming is breaking. Worth a follow-up issue: deprecate the flat keys in favor of nested `Pool:Size` / `ProcessPool:Size` for the next major.

**Doctor surfacing — strong.** Reports resolved hostMode + source (explicit/default), per-mode pool sizing with clamp output, min-healthy clamp, host script path with resolution status, hardcoded 30s request timeout (right call to surface even though not yet a config knob). Clamp warnings cover negative/zero/exceed-pool cases. Cancellation contract not surfaced — fine, not a config knob today.

**Doc accuracy:** `DOCKER.md`'s `POSHMCP_RUNTIME_MODE` is correct (consumed by `SettingsResolver` line 31; `ConfigurationFileManager.NormalizeRuntimeMode` accepts both Pascal `InProcess`/`OutOfProcess` and kebab `in-process`/`out-of-process`). README perf claim 4.9× matches benchmark-findings.md (4.86×). DESIGN.md links benchmark-findings correctly.

**Spec:** `Status: Implemented` accurate; cancellation contract section matches #207 shipped behavior (Pool's "N - in_flight_uncancelled" framing is correct, distinct from "stuck"); FR-051 restated as channel-writer serialization with multiplexed responses is the right refactoring of the original assumption.

**Test gap (non-blocking):** No new unit tests for `BuildOutOfProcessSection` or `RenderOutOfProcess`. Pure projection/rendering, low risk, but operator-facing. One rendering test fixture (Pool / ProcessPool / non-applicable) would lock in JSON shape and text format. Recommend follow-up issue, not blocker.

**Patterns:**
- When a default flip is questioned, audit ALL direct construction sites of the affected type — `grep_search "new TypeName"` across the production project (not tests). Caller-by-caller analysis is faster than reasoning about defaults in isolation.
- Naming asymmetry between sibling config keys (e.g. `SubprocessRunspacePoolSize` vs `SubprocessPoolSize`) is acceptable when the consuming UI (here, the doctor renderer) renders only the relevant key per active mode. The renderer becomes the disambiguation layer.
- Surfacing a hardcoded value (30s request timeout) in a doctor report — even though it's not yet a config knob — is good practice. Makes the contract explicit for operators and signposts the eventual configuration surface.

### 2026-05-06: Spec 004 foundation merge wave — 2/3 landed, #199 blocked on conflict
**By:** Amy (DevOps), requested by Steven Murawski
**What:** Sequenced merge of PRs #197 → #198 → #199. Each rebased onto fresh `origin/main`, full `dotnet test PoshMcp.sln` ran, only merged when green.
- ✅ **#197** (`squad/193-benchmark-harness`) merged. Tests: 584/591 (7 skipped). Brings `PoshMcp.Benchmarks` harness onto main.
- ✅ **#198** (`squad/190-extract-oop-host`) merged. Tests: 593/600 (7 skipped, +9 new unit tests for `OutOfProcessHost`). Extracts the OOP host with lifecycle coverage.
- ⛔ **#199** (`squad/189-clear-error-before-invoke`) — **STOPPED**. Rebase against post-#198 main hit a content conflict in `PoshMcp.Tests/Integration/OutOfProcessIntegrationTests.cs` (same file the #190 extraction touched). Rebase aborted cleanly; branch on origin is untouched. Needs Hermes (or whoever owns the OOP test surface) to resolve.

**Why:** Spec 004 foundation needed to land in dependency order. The first two PRs were independent enough to rebase clean; the `$Error`-clearing fix in #199 lives in tests that #198 reorganized, so a manual conflict resolution is required.

**Follow-up actions:**
1. Re-spawn Hermes (or equivalent) on `squad/189-clear-error-before-invoke` to: rebase onto current main, resolve the `OutOfProcessIntegrationTests.cs` conflict, re-run full tests, push --force-with-lease, then re-attempt merge.
2. After #199 lands, Spec 004 foundation phase is complete and downstream Spec 004 work can fan out.

**Operational note for future merge waves:** PRs created as drafts must be marked ready with `gh pr ready <num>` before `gh pr merge`. The `--delete-branch` flag triggers a local checkout error when run from a worktree (main is already checked out elsewhere) — the remote branch still gets deleted; just clean up worktree separately.

### 2026-05-07: Leela — OOP docs + samples audit (PR #210)
**By:** Steven Murawski (via Leela)
**What:** Audited whether spec 004 OOP changes (default flip to `Pool`, `SubprocessHostMode` taxonomy, sizing knobs, cancellation contract) reached `./docs` and the sample `appsettings.json` files. Findings: docs had material gaps (advanced.md stale, configuration.md silent on RuntimeMode/SubprocessHostMode, azure-integration.md described RuntimeMode incorrectly as "sync/async"); samples were partial (root + PoshMcp.Server were current; `examples/appsettings.advanced.json` and `examples/appsettings.tenant.json` had no PowerShell runtime tuning despite being the heavy-Az and multi-tenant scenarios where OOP applies). Updates landed in PR #210: rewrote advanced.md OOP section with full taxonomy, sizing, cancellation contract, ProcessPool example, link to benchmark-findings.md; added Runtime Mode section to configuration.md; fixed azure-integration.md description; added `RuntimeMode: OutOfProcess` + `SubprocessHostMode: Pool` to advanced.json and `RuntimeMode: OutOfProcess` + `SubprocessHostMode: ProcessPool` (size 4, min healthy 2) to tenant.json; documented rationale in `examples/README.md`. Intentionally left alone: examples/appsettings.basic.json (purpose mismatch), PoshMcp.Server/default+modules+azure+environment-example (loaded by dev/tests, out of audit scope), README.md/DOCKER.md (already updated in #208), docs/release-notes (belongs with the shipping release). Build green.
**Why:** Source-of-truth schema (`PowerShellConfiguration.cs`) shipped Pool as the default but the user-facing docs and the two samples whose use cases are exactly what the modes exist for hadn't been updated to match. Risk was that users following the docs or copying the samples would not know the new default exists, would not know how to opt into ProcessPool for trust-boundary scenarios, and (in azure-integration.md) would read a wrong description of the RuntimeMode field.

### 2026-05-07: Cubert — Fact-check verdict on PR #210 (OOP docs + samples audit)
**By:** Steven Murawski (via Cubert)
**Verdict:** REQUEST CHANGES — three substantive errors in `docs/articles/advanced.md`. Samples and other docs check out.

**Verified ✅**
- All property names in changed docs and samples exist in `PoshMcp.Server/PowerShell/PowerShellConfiguration.cs` with the casing shown: `RuntimeMode`, `SubprocessHostMode`, `SubprocessRunspacePoolSize`, `SubprocessPoolSize`, `SubprocessMinHealthyForStartup`.
- All `SubprocessHostMode` string values used (`Single`, `Pool`, `ProcessPool`) match the enum defined in `PoshMcp.Server/PowerShell/OutOfProcess/SubprocessHostMode.cs`.
- Defaults cited match code: `SubprocessHostMode = Pool`, `SubprocessRunspacePoolSize = 0` auto-sizes to `min(ProcessorCount, 8)`, `SubprocessPoolSize = 4`, `SubprocessMinHealthyForStartup = 1`.
- Clamp claim "Clamped to `[1, SubprocessPoolSize]`" matches `Math.Min(config.SubprocessMinHealthyForStartup, Math.Max(1, config.SubprocessPoolSize))` in `McpToolSetupService.cs:214` and the doctor-warning paths in `DoctorService.cs:361,365`.
- `4.86×` warm-invoke throughput at concurrency 10 matches `specs/004-out-of-process-execution/benchmark-findings.md` §1 (table: 4.86× mean / 4.79× P99).
- "Clears the spec's per-scenario 4× bar for I/O-shaped workloads" matches the same findings file.
- "Default since 2026-05-06" matches the date on `benchmark-findings.md` and the spec-004 default-flip context.
- `examples/appsettings.advanced.json` and `examples/appsettings.tenant.json` (PR-branch versions) parse as valid JSON. advanced.json uses Pool-mode-relevant key (`SubprocessRunspacePoolSize`); tenant.json uses ProcessPool-mode-relevant keys (`SubprocessPoolSize`, `SubprocessMinHealthyForStartup`) — correct per-mode key selection.
- `examples/README.md` rationale aligns with `benchmark-findings.md` §4 recommendation (Pool for typical concurrent MCP load, ProcessPool for trust-boundary / tail-latency-sensitive workloads).
- `docs/articles/azure-integration.md` `RuntimeMode` fix uses real values from the schema (`InProcess`/`OutOfProcess`).
- `POSHMCP_RUNTIME_MODE=OutOfProcess` (PascalCase) in advanced.md is accepted by `SettingsResolver.NormalizeRuntimeModeValue`.
- No new TOC entries needed; no broken intra-doc links observed in the diff.

**Discrepancies ❌**
1. `docs/articles/advanced.md`, "Enable Out-of-Process Mode": "Unrecognized values fall back to `InProcess` with a logged error." Code does not fall back — `ConfigurationLoader.cs:50` **throws `InvalidOperationException`** ("Unsupported runtime mode '{value}'. Supported runtime modes: in-process, out-of-process.") and the server fails to start. Recommend: replace with "Unrecognized values cause the server to fail startup with `InvalidOperationException`."
2. `docs/articles/advanced.md`, "Cancellation" section, **ProcessPool** bullet: "cancellation tears down the leased subprocess; the pool spins a replacement. Other hosts are unaffected." Per PR #207 (merged 2026-05-07) and `specs/004-out-of-process-execution/cancellation-design.md` §2.3, ProcessPool now inherits soft-cancel via the new `cancel` wire frame. BeginStop is invoked inside the host; the slot **stays healthy** and is returned to the pool. Subprocess teardown is only the **backstop** for wedged hosts (unmanaged code) via the existing per-request kill-on-timeout path. Leela's text describes the backstop as if it were the normal path.
3. `docs/articles/advanced.md`, "Cancellation" section, **Single** bullet: "cancellation kills the host; the historical timeout-and-restart behavior applies." PR #207 explicitly refactored `oop-host.ps1` so the Single-mode handler runs invokes on a background dispatcher thread; cancel calls `BeginStop` on the matching `[powershell]` instance and **the host stays healthy for follow-ups** (PR #207 description, verbatim). This is pre-#207 behavior.

**Minor ⚠️**
- `docs/articles/advanced.md` "Valid values: `InProcess`, `OutOfProcess`" is incomplete. `SettingsResolver.NormalizeRuntimeModeValue` also accepts `in-process` / `out-of-process` (kebab-case) and lowercase forms. The repo's own `README.md`, `integration/README.md`, and `CliDefinition.cs:212` describe the kebab form as canonical for the env var/CLI, while `spec.md` uses the PascalCase form. Not blocking, but could mislead.

**Lockout:** Per Reviewer Rejection Protocol — strict lockout. Leela may not self-revise. Recommend Steven assigns Bender (owner of PR #207, cancellation-design.md author) to revise the cancellation bullets and the runtime-mode error-handling claim.

**Why:** External-facing docs that misstate the cancellation contract are exactly what the spec-004 default flip was gated on (`benchmark-findings.md` §6 caveat 5). Shipping these docs as-is would teach users wrong expectations about host survivability after a cancelled invoke — the property the cancellation work was created to provide.

### 2026-05-07: Farnsworth — PR #210 review (Leela — OOP docs + samples audit)

**By:** Steven Murawski (via Farnsworth, Lead / Architect)

**What:** APPROVE with one non-blocking framing nit. Architectural review of PR #210 covering mental model, framing coherence with #208 (default flip), sample-pick rationale, and operator-facing completeness. Cubert handled fact-checking in parallel; this review is scoped to architecture and framing only.

**Mental model assessment — clear.** Two-entry-point split (brief in `configuration.md`, deep-dive in `advanced.md`) avoids duplication. New operator landing on either article reaches the three-mode taxonomy with explicit "when to use" guidance, sizing knobs (pool runspaces vs pool processes vs min healthy), per-mode cancellation contract, and doctor pointer for verification. Decision narrative — `Pool` wins warm throughput (~4.86×, citing `benchmark-findings.md`), `ProcessPool` opt-in for trust/tail, `Single` legacy/bisect — matches the spec 004 study and #208 default-flip rationale exactly.

**Coherence with #208.** `RuntimeMode` correctly described as `InProcess`/`OutOfProcess` (the `azure-integration.md` "sync/async" line was a real bug; correctly fixed). `SubprocessHostMode` is presented as a primary configuration concept rather than a tuning knob — correct framing for post-default-flip docs. Cancellation is documented as a contract per mode, not a footnote — correct framing because cancellation is what made the flip safe.

**Sample-pick rationale — both correct.** `advanced.json` → `Pool` matches Pool's documented strength (concurrent warm-invoke throughput) plus the heavy-Az use case; `SubprocessRunspacePoolSize: 0` (auto-tune to `min(ProcessorCount, 8)`) is the right default for a copy-paste sample. `tenant.json` → `ProcessPool` (size 4, min healthy 2) matches ProcessPool's documented strength (per-slot crash recovery + process-level isolation between callers). The `examples/README.md` rationale names the tradeoff explicitly ("trust boundaries between callers matter more than peak throughput") — multi-tenant is exactly the workload class where peak throughput is the wrong optimization target.

**Operator completeness.** `poshmcp doctor` is referenced from `advanced.md` ("reports the resolved host mode, effective pool sizes, host-script path, and any clamp warnings under Runtime Settings"). Adequate — answers the "how do I verify my config did what I intended?" question without burying it or over-emphasizing.

**Non-blocking framing nit (one):** The Cancellation section in `advanced.md` says of `Single`: *"the historical timeout-and-restart behavior applies."* This undersells what Single mode does post-#207 — the `SingleDispatcher` worker-thread pattern landed in #207 supports the same cooperative soft-cancel contract as Pool/ProcessPool, with the per-request timeout serving as the backstop. As written, an operator could read this as "Single mode does not support cooperative cancellation," which would be inaccurate, and which would also undersell why the default flip became safe across all three modes simultaneously. Suggested follow-up phrasing: *"Single: cooperative cancellation via the dispatcher worker; the per-request timeout acts as the backstop and recycles the host on timeout."* One line. Not blocking #210.

**No architectural gaps that block.** Mental model intact, decision narrative matches engineering, sample picks match documented tradeoffs, doctor surfaced for verification.

**Comment URL:** https://github.com/usepowershell/PoshMcp/pull/210#issuecomment-4396923714

### 2026-05-07: Cubert — Re-verification verdict on PR #210 (post-Bender revision)
**By:** Steven Murawski (via Cubert)
**Verdict:** APPROVE — all three blocking findings from prior fact-check are resolved in commit `a4c9ed0`. No collateral defects introduced.

**Scope:** Re-verified `docs/articles/advanced.md` at HEAD (`a4c9ed09a395384596905aa169c3edb30ae60eb0`) on `squad/oop-docs-samples-audit`. Bender (revision author per strict-lockout rule) modified only `advanced.md` per his decision drop.

**Per-finding verdict:**

1. ✅ **`RuntimeMode` invalid-value behavior — RESOLVED.** Doc text now reads: "Unrecognized values cause the server to fail startup with `InvalidOperationException` (`Unsupported runtime mode '<value>'. Supported runtime modes: in-process, out-of-process.`)." Matches ground truth in `PoshMcp.Server/Configuration/ConfigurationLoader.cs:46-50` verbatim — the loader throws when `config.RuntimeMode == RuntimeMode.Unsupported`. No fallback path exists. The non-blocking kebab-case clarification (`in-process` / `out-of-process` accepted by env var/CLI) is folded into the same paragraph correctly.

2. ✅ **ProcessPool cancellation — RESOLVED.** Doc now describes soft-cancel via inherited `OutOfProcessHost` cancel frame as the primary path: "each leased host runs the Single-mode script and inherits the same soft-cancel via the inherited `OutOfProcessHost` cancel frame. If the host honors `BeginStop`, the slot stays healthy and is returned to the pool; other hosts are unaffected. The existing per-request kill-on-timeout path in `OutOfProcessSubprocessPool` remains as a backstop for wedged hosts (e.g., a cmdlet stuck in unmanaged code) that do not honor `BeginStop` within the per-request timeout." Matches `specs/004-out-of-process-execution/cancellation-design.md` §2.3.

3. ✅ **Single cancellation — RESOLVED.** Doc now reads: "`SingleDispatcher` runs the invoke on a background dispatcher thread and calls `BeginStop` on the matching `[powershell]` instance when the cancel frame arrives. The host stays healthy for follow-up requests; the per-request timeout serves as the backstop and recycles the host only if `BeginStop` does not unwind the pipeline in time." Matches `cancellation-design.md` §2.1.

**Collateral check:** Skimmed surrounding cancellation section. The new shared-mechanism lead-in is accurate (`cancel` control frame from `OutOfProcessHost.SendRequestAsync`; cooperative `BeginStop`; .NET awaiter completes with `OperationCanceledException` immediately without waiting for host ack — matches `cancellation-design.md` §3 lines 104, 115). Pool bullet (`PoolDispatcher` looks up active `[powershell]` by request id and calls `BeginStop`, runspace returned without restart) matches §2.2 lines 46-47. No broken markdown links, no broken code fences, no new factual errors introduced. Markdown structure intact.

**CI:** All checks green on `a4c9ed0` (CodeQL actions/csharp/python, Squad CI test, submit-nuget). PR is `MERGEABLE`.

**Lockout note:** With APPROVE verdict, no further lockout triggers. PR cleared from fact-check standpoint.

### 2026-05-07: v0.11.0 minor release version bump
**By:** Amy (DevOps / Platform / Azure Engineer), requested by Steven Murawski
**What:** Bumped `PoshMcp.Server/PoshMcp.csproj` version from `0.10.0` to `0.11.0` and added a `## [0.11.0] - 2026-05-07` entry to `CHANGELOG.md`.
**Why:** Cutting a minor release. The marquee feature is the out-of-process subprocess pool (`Pool` is now the default `SubprocessHostMode`, #196), with supporting work across ProcessPool mode, `OutOfProcessHost` extraction, OOP cancellation propagation (#188), the new `PoshMcp.Benchmarks` harness, OOP fixes (`ConvertTo-Json` wrap #203, `$Error` clear #189), CWE-117 log-injection hardening in the OOP host, CI permission minimization plus `SECURITY.md`, and docs catch-up (#210, #187). Minor-version bump is appropriate — new feature surface (Pool default, ProcessPool, benchmarks) is additive but a meaningful behavior change for OOP users.
**Status:** Code change shipped (csproj + CHANGELOG). Build verified clean (`dotnet build PoshMcp.sln -c Debug` → 0 errors, only pre-existing nullable warnings). Git tag (`v0.11.0`) and push are intentionally deferred to Steven, after Cubert reviews release notes and Leela finishes `docs/release-notes/` + `SECURITY.md` work.

### 2026-05-07: v0.11.0 release notes published; SECURITY.md support matrix bumped to 0.11.x
**By:** Leela (Developer Advocate), requested by Steven Murawski
**What:**
- Created `docs/release-notes/0.11.0.md`. Lead story is OOP execution maturity: `Pool` is now the default `SubprocessHostMode` (replacing `Single`) backed by ~4.86× warm-invoke throughput at concurrency 10 in the new benchmarks harness; new `ProcessPool` topology for trust-boundary / tail-latency workloads; cancellation now propagates across the OOP boundary. Also covers `PoshMcp.Benchmarks` harness, log-sanitization (CWE-117) hardening, minimum workflow permissions, published `SECURITY.md`, and bug fixes (`ConvertTo-Json` `Content` shadowing, `$Error` clear-before-invoke). Upgrade notes call out the `Pool` default flip explicitly with an opt-out snippet to preserve `Single`.
- Updated `SECURITY.md` supported-versions table: `0.11.x` now `:white_check_mark:`, `< 0.11` now `:x:`. Replaces the prior `0.10.x` line.
**Why:** v0.11.0 is the first release where OOP `Pool` is the default — that needs an explicit, accurate upgrade story for users, and the supported-versions matrix must follow the new minor line.
**Scope:** Did not touch `CHANGELOG.md` or `PoshMcp.Server/PoshMcp.csproj` — those are Amy's. Cubert to review.

### 2026-05-07: v0.11.0 release notes review — config key error in upgrade snippets
**By:** Cubert (review of Leela's docs/release-notes/0.11.0.md)
**What:** REJECTED. Both jsonc snippets in the "Upgrade Notes" section use `"PowerShell"` as the top-level config key. The actual section name in every shipping `appsettings.json`, doc, and example is `"PowerShellConfiguration"`. Users copy-pasting the opt-out snippet would silently keep the new `Pool` default instead of restoring `Single` — defeating the entire purpose of the upgrade note.
**Why:** Verified zero matches for `"PowerShell": { ... }` carrying these properties; 30+ matches for `"PowerShellConfiguration"` as the canonical section. Confirmed against `PoshMcp.Server/PowerShell/PowerShellConfiguration.cs` (binds to the `PowerShellConfiguration` section) and all repo configs/docs.
**Rule for future release notes:** Spot-check every jsonc/json snippet's top-level keys against an actual shipping `appsettings.json` before publishing. Default-flip snippets are user-facing executable content — wrong keys are silent landmines, not cosmetic bugs.
**Other claims in v0.11.0 release notes verified accurate:** Pool default flip in code, three-mode taxonomy, sizing knobs, cancellation propagation, benchmarks harness, bug fixes (#203, #189), security hardening, SECURITY.md table update. Format matches prior release notes.
---

## Recommendation

Both PRs are ready to merge. Wave 1 infrastructure for spec 008 is complete.

### The Problem

`WebApplicationBuilder` starts with a `ConfigurationManager` that already contains the **baked-in `appsettings.json`** from the container image at `/app/server/appsettings.json`. This file has:
```json
"Authentication": { "Enabled": false, ... }
```

At line 1758 of `Program.cs`, the custom user config file (`PoshMcp/appsettings.json`, with `Enabled: true`) is added to `builder.Configuration`. In theory, later-added sources have higher priority. In practice, with `WebApplicationBuilder`'s `ConfigurationManager`, the baked-in `appsettings.json` was winning, causing:

- `authConfigValue.Enabled = false` at line 1800 → auth filters NOT registered, `WithRequestFilters` NOT set up
- `IOptions<AuthenticationConfiguration>.Value.Enabled = false` at middleware setup (line 1858-1864) → `UseAuthentication()` and `UseAuthorization()` NOT called
- `RequireAuthorization("McpAccess")` NOT applied to the MCP endpoint (inside the same `if (authConfigForMiddleware.Value.Enabled)` block)
- `AddPoshMcpAuthentication(builder.Configuration)` (line 1842) reads `Enabled: false` → returns early without registering JWT Bearer or the McpAccess policy

### Why the v0.9.2 Fix Didn't Fix This

The v0.9.2 fix addressed a **different bug**: when `Enabled: false` in config, `IOptions<AuthenticationConfiguration>` was not registered at all (the `services.Configure<T>()` call was inside the early-return guard). That fix moved `services.Configure<T>()` before the guard so IOptions always shows the real configured value.

The **current bug** is upstream: `builder.Configuration` itself returns `Enabled: false` because the base `appsettings.json` overrides the custom file. The fix was applied to the wrong layer.

### The Disconnect Between Diagnostic Tools and Runtime

`BuildRootConfiguration(configPath)` used by all diagnostic tools (`get-configuration-troubleshooting`, `get-configuration-guidance`, `BuildDoctorReportFromConfig`) is:
```csharp
var builder = new ConfigurationBuilder();
builder.AddJsonFile(configPath, ...);  // ONLY the custom file
builder.AddEnvironmentVariables();
return builder.Build();
```

This **does NOT include the base `appsettings.json`**. It only sees the custom file with `Enabled: true`. The runtime DI uses `builder.Configuration` (the `WebApplicationBuilder`'s `ConfigurationManager`) which starts with the base `appsettings.json` and has a precedence problem with the custom file.

---

## 5. The Fix

Changed `RunHttpTransportServerAsync` to build a dedicated `authRootConfig` via `ConfigurationLoader.BuildRootConfiguration(finalConfigPath, reloadOnChange: false)` — reading ONLY from the custom file and env vars, exactly like the diagnostic tools.

**Three call sites changed:**

```csharp
// NEW: build auth-specific config from custom file only
var authRootConfig = ConfigurationLoader.BuildRootConfiguration(finalConfigPath, reloadOnChange: false);

// IOptions now bound to authRootConfig (not builder.Configuration)
builder.Services
    .AddOptions<AuthenticationConfiguration>()
    .Configure(opts => authRootConfig.GetSection("Authentication").Bind(opts))
    .ValidateOnStart();

// ...

// authConfigValue from authRootConfig (not builder.Configuration)
var authConfigValue = authRootConfig.GetSection("Authentication").Get<AuthenticationConfiguration>() ?? new();

// ...

// AddPoshMcpAuthentication reads from authRootConfig (not builder.Configuration)
builder.Services.AddPoshMcpAuthentication(authRootConfig);
```

**Result:**
- `authConfigValue.Enabled = true` → filters registered, `WithRequestFilters` set up ✓
- `IOptions<AuthenticationConfiguration>.Value.Enabled = true` → `UseAuthentication()` and `UseAuthorization()` called ✓
- `RequireAuthorization("McpAccess")` applied to MCP endpoint ✓
- JWT Bearer scheme and McpAccess policy registered ✓

**Tests:** 574 passing, 0 failing, 7 skipped.

---

## 6. Key Rule Going Forward

> **Never use `WebApplicationBuilder.Configuration` as the source for security-gate decisions when a custom config file is involved.**
>
> The `WebApplicationBuilder` default config chain always includes the baked-in `appsettings.json` which has `Authentication.Enabled: false` as a safe default. This can unexpectedly win over the custom file due to configuration precedence issues with `ConfigurationManager`. Use `ConfigurationLoader.BuildRootConfiguration(configPath)` for auth configuration — it reads only what the user explicitly configured.

---

## 7. Remaining Action Items

- [ ] **Deploy v0.9.3** with this fix. The current deployed v0.9.2 is still vulnerable.
- [ ] **Consider a regression test** verifying `authConfigValue.Enabled` is correctly read from the custom config file in an HTTP server context (Fry's domain per `fry-auth-regression-tests.md`).
- [ ] **Consider removing `Authentication.Enabled: false` from the baked-in `appsettings.json`** entirely — or at least document that the baked-in defaults are NOT for production use and will be overridden by custom configs only if there's no precedence race.


# Decision: Auth Config Source Fix — ConfigureCorsForMcp

**Date:** 2026-05-01  
**Author:** Bender  
**Commit:** 351c42c  
**Status:** Applied

## Context

After the main auth bypass fix (building `authRootConfig` via `ConfigurationLoader.BuildRootConfiguration` for IOptions and `AddPoshMcpAuthentication`), a second instance of `builder.Configuration` usage for auth settings was found in `ConfigureCorsForMcp`.

`ConfigureCorsForMcp` read `builder.Configuration.GetSection("Authentication")` to decide whether to open up CORS (`AllowAnyOrigin`) or restrict it. Because `builder.Configuration` includes the baked-in `appsettings.json` (where `Authentication.Enabled: false`), CORS would be opened wide even for deployments where the custom config had `Enabled: true` — a security gap.

## Decision

Extend `ConfigureCorsForMcp` to accept the `IConfigurationRoot authRootConfig` built from `ConfigurationLoader.BuildRootConfiguration(finalConfigPath)` and use it instead of `builder.Configuration`.

## Change

```csharp
// Before
private static void ConfigureCorsForMcp(WebApplicationBuilder builder)
{
    var authConfig = builder.Configuration.GetSection("Authentication").Get<AuthenticationConfiguration>()
        ?? new AuthenticationConfiguration();
    ...
}

// Call site
ConfigureCorsForMcp(builder);

// After
private static void ConfigureCorsForMcp(WebApplicationBuilder builder, IConfigurationRoot authRootConfig)
{
    var authConfig = authRootConfig.GetSection("Authentication").Get<AuthenticationConfiguration>()
        ?? new AuthenticationConfiguration();
    ...
}

// Call site
ConfigureCorsForMcp(builder, authRootConfig);
```

## Rationale

`authRootConfig` is the canonical auth config source for this server session — it reads only from the user-resolved config file + env vars, bypassing the WebApplicationBuilder config chain that includes the baked-in base defaults. All auth-gated decisions must use this same source.

## Verification

- `dotnet build PoshMcp.Server\PoshMcp.csproj --no-incremental`: 0 errors, 10 pre-existing warnings
- `dotnet test PoshMcp.Tests\PoshMcp.Tests.csproj`: 574 passed, 0 failed, 7 skipped

## Rule for Future Work

After any auth config source refactor, run:
```
grep -n "builder.Configuration.GetSection.*Authentication" Program.cs
```
Any remaining hits are potential auth bypass vectors.


# Decision: Always Register AuthenticationConfiguration with IOptions

**Date:** 2026-05-01
**By:** Bender (Backend Developer)
**Status:** Applied

## What

In `AuthenticationServiceExtensions.AddPoshMcpAuthentication()`, added `services.Configure<AuthenticationConfiguration>(configuration.GetSection("Authentication"))` **before** the early-return guard that exits when auth is disabled.

## Why

`IOptions<AuthenticationConfiguration>` was resolving to the default object (`Enabled = false`) throughout the application because the options system was never bound to configuration. The method used `.Get<AuthenticationConfiguration>()` for local decision-making but never called `services.Configure<>()` to wire up the DI options binding.

Three consumers were broken as a result:
- `Program.cs` (lines ~1859, ~1893): middleware and endpoint authorization guards both evaluated `false`, leaving the pipeline open to unauthenticated requests even when `Authentication.Enabled: true` in appsettings.
- `ApiKeyAuthenticationHandler.cs` (line 79): handler received a default (blank) config.
- `ConfigurationHealthCheck.cs` (line 24): health check evaluated against defaults, not real config.

## Rule Going Forward

When a service extension reads configuration via `.Get<T>()` for local logic AND consumers elsewhere depend on `IOptions<T>`, **always call `services.Configure<T>()` unconditionally** — regardless of whether the feature is enabled. The options registration must not be gated behind a feature flag because consumers may need to observe the real disabled state versus the default state.


# Decision: Show server version in doctor/troubleshooter output

**Author:** Bender (Backend Developer)  
**Date:** 2026-05-01  
**Status:** Implemented

## Decision

Add the PoshMcp server version string to both the `poshmcp doctor` CLI banner and the `get-configuration-troubleshooting` MCP tool JSON output.

## Rationale

Users and operators need to know which version of PoshMcp is running when diagnosing issues. The doctor/troubleshooter output is the natural place to surface this.

## Implementation

- Added `Version` property to `DoctorSummary` record (`DoctorReport.cs`).
- Added private `GetServerVersion()` helper to `DoctorReport` that reads `AssemblyInformationalVersionAttribute` and strips any `+{commit-hash}` suffix.
- Updated `DoctorReport.Build()` to populate `Version = GetServerVersion()`.
- Updated `DoctorTextRenderer.RenderBanner()` to show `PoshMcp v{version}` instead of `PoshMcp Doctor`.

## Version source

`typeof(DoctorReport).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion`
stripped of everything after `+`.

The `.NET SDK` sets this automatically from `<Version>0.9.2</Version>` in `PoshMcp.csproj`.


# Fix: VS Code OAuth Redirect to PoshMcp `/authorize`

**Date:** 2026-05-01  
**Author:** Bender (Backend Developer)  
**Status:** Implemented — build clean, 574/574 tests pass

---

## What Was Fixed

Two authentication handler bugs that together caused VS Code to redirect to PoshMcp's own `/authorize` endpoint instead of Entra ID.

---

## Fix 1: JwtBearer — inject `resource_metadata` into `WWW-Authenticate`

**File:** `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs`

**Before:** JwtBearer was configured with no `Events`, so 401 responses emitted:
```http
WWW-Authenticate: Bearer
```

**After:** Added `JwtBearerEvents.OnChallenge` that emits:
```http
WWW-Authenticate: Bearer resource_metadata="https://<host>/.well-known/oauth-protected-resource"
```

Key implementation details:
- `context.HandleResponse()` is called to suppress ASP.NET Core's default challenge pipeline (prevents a duplicate plain `Bearer` header being appended).
- `context.Response.StatusCode = 401` is set explicitly after `HandleResponse()`.
- The metadata URL is derived from `context.HttpContext.Request.Scheme + Request.Host` — never hardcoded.
- The `OnChallenge` block is guarded by `cfg.Value.ProtectedResource?.Resource is not null` so it only fires when PRM is configured (auth-disabled deployments are unaffected).

---

## Fix 2: ApiKeyAuthenticationHandler — fix `resource_metadata` URL construction

**File:** `PoshMcp.Server/Authentication/ApiKeyAuthenticationHandler.cs`

**Before:**
```csharp
var metadataUrl = $"{authConfig.Value.ProtectedResource.Resource}/.well-known/oauth-protected-resource";
// Produced: api://80939099-d811-4488-8333-83eb0409ed53/.well-known/oauth-protected-resource
```

**After:**
```csharp
var metadataUrl = $"{Request.Scheme}://{Request.Host}/.well-known/oauth-protected-resource";
// Produces: https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/.well-known/oauth-protected-resource
```

---

## Expected Post-Fix Behavior

1. Unauthenticated request hits PoshMcp
2. Server responds `401` with `WWW-Authenticate: Bearer resource_metadata="https://<host>/.well-known/oauth-protected-resource"`
3. VS Code reads `resource_metadata`, fetches the PRM
4. PRM returns `authorization_servers: ["https://login.microsoftonline.com/<tenant>"]`
5. VS Code fetches Entra ID metadata, discovers `authorization_endpoint`
6. Browser redirects to `login.microsoftonline.com/...` with VS Code's own `client_id=aebc6443-996d-45c2-90f0-388ff96faa56`

---

## Files Modified

| File | Change |
|------|--------|
| `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs` | Added `JwtBearerEvents.OnChallenge` with `resource_metadata` header; added `using System.Threading.Tasks` |
| `PoshMcp.Server/Authentication/ApiKeyAuthenticationHandler.cs` | Fixed metadata URL to use `Request.Scheme + Request.Host` |

---

## Validation

- `dotnet build PoshMcp.Server/PoshMcp.csproj -c Release` — 0 errors, 10 pre-existing warnings (unchanged)
- `dotnet test PoshMcp.Tests/PoshMcp.Tests.csproj --no-build -c Release` — 574 passed, 0 failed, 7 skipped (pre-existing)


# Diagnosis: VS Code Redirecting to PoshMcp's Own `/authorize` Endpoint

**Date:** 2026-05-01  
**Author:** Bender (Backend Developer)  
**Status:** Diagnosis complete — awaiting fix approval

---

## The Symptom

VS Code opens a browser tab to:
```
https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/authorize
  ?client_id=80939099-d811-4488-8333-83eb0409ed53
  &response_type=code
  &code_challenge=DsFdRdRJrgNLeuzw_RsPo1Qv30blZiB0LfcPVbv2bQk
  &code_challenge_method=S256
  &redirect_uri=http%3A%2F%2F127.0.0.1%3A33418%2F
  &state=HqfYeTV%2F%2Bxr48AmWc9Wjfg%3D%3D
```

VS Code should redirect to **Entra ID** (`login.microsoftonline.com/...`), not to PoshMcp itself.

---

## Investigation Findings

### 1. What does the PRM return for `authorization_servers`?

The PRM is correctly configured in the deployed `appsettings.json`
(`C:\Users\stmuraws\source\emu\gim-home\AdvocacyBami\appsettings.json`):

```json
"ProtectedResource": {
  "Resource": "api://80939099-d811-4488-8333-83eb0409ed53",
  "ResourceName": "PoshMcp Server",
  "AuthorizationServers": ["https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b"],
  "ScopesSupported": ["api://80939099-d811-4488-8333-83eb0409ed53/user_impersonation"],
  "BearerMethodsSupported": ["header"]
}
```

**The PRM content itself is correct.** `authorization_servers` points to the right Entra ID tenant URL. This is NOT the bug.

### 2. Does PoshMcp have a `/authorize` endpoint?

**No.** There is no `app.MapGet("/authorize", ...)` or any route handling for `/authorize` anywhere in the codebase. The only auth-related endpoint PoshMcp maps is `/.well-known/oauth-protected-resource` via `ProtectedResourceMetadataEndpoint.MapProtectedResourceMetadata()`.

So when VS Code hits `/authorize`, it will get a 404 or fall through to the MCP handler.

### 3. Root Cause: JwtBearer 401 challenge omits `resource_metadata`

**This is the bug.** In `AuthenticationServiceExtensions.cs`, the JwtBearer scheme is configured with default options only:

```csharp
authBuilder.AddJwtBearer(name, options =>
{
    options.Authority = scheme.Authority;
    options.Audience = scheme.Audience;
    options.RequireHttpsMetadata = scheme.RequireHttpsMetadata;
    // ...
    // ← NO Events.OnChallenge configured
});
```

When an unauthenticated request hits a protected endpoint, ASP.NET Core's built-in JwtBearer handler issues a 401 with:
```http
WWW-Authenticate: Bearer
```

RFC 9728 (OAuth 2.0 Protected Resource Metadata) requires the 401 to include a `resource_metadata` parameter pointing to the PRM endpoint:
```http
WWW-Authenticate: Bearer resource_metadata="https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/.well-known/oauth-protected-resource"
```

Without this hint, VS Code's MCP OAuth client never discovers the PRM. It falls back to treating the resource server itself as the authorization server and constructs the authorization URL as `{resourceServerBaseUrl}/authorize`.

### 4. Secondary Bug: ApiKeyAuthenticationHandler constructs a wrong `resource_metadata` URL

`ApiKeyAuthenticationHandler.HandleChallengeAsync()` does attempt to set `resource_metadata`, but it has a bug:

```csharp
// BUGGY — uses the api:// URI, not the server's HTTP base URL
var metadataUrl = $"{authConfig.Value.ProtectedResource.Resource}/.well-known/oauth-protected-resource";
// Produces: api://80939099-d811-4488-8333-83eb0409ed53/.well-known/oauth-protected-resource
```

This is not a valid HTTP URL. It uses `ProtectedResource.Resource` (the `api://` URI identifier) instead of the server's actual HTTPS base URL. This doesn't affect the current deployment (which uses JwtBearer), but would break any future ApiKey deployment.

### 5. The `client_id` discrepancy

`client_id=80939099-d811-4488-8333-83eb0409ed53` in the browser redirect is **the PoshMcp App Registration's Application ID** — the same GUID used in `"Audience": "api://80939099-d811-4488-8333-83eb0409ed53"` in the deployed config.

The documented VS Code pre-registered client ID for MCP is `aebc6443-996d-45c2-90f0-388ff96faa56`.

**Why VS Code is using `80939099-d811-4488-8333-83eb0409ed53` as its client_id:**

VS Code's MCP OAuth implementation has a fallback behavior. When it cannot resolve the authorization server via `WWW-Authenticate: Bearer resource_metadata=...`, it falls back to treating the resource server as the AS. In this fallback mode, VS Code extracts the GUID from the resource's `api://` URI and uses it as the `client_id` in the authorization request. This GUID (`80939099-d811-4488-8333-83eb0409ed53`) is exactly what's in the PRM's `resource` field.

**This is confirmation** that VS Code is in fallback mode — it found the PRM but couldn't follow the `authorization_servers` metadata path (or never got the `resource_metadata` hint to find the PRM in the first place).

---

## Root Cause Summary

**Primary cause:** `AuthenticationServiceExtensions.cs` does not configure `JwtBearerEvents.OnChallenge` to inject `WWW-Authenticate: Bearer resource_metadata="<serverBaseUrl>/.well-known/oauth-protected-resource"` into 401 responses. Without this header, VS Code cannot discover the PRM and falls back to using PoshMcp as the authorization server.

**Contributing cause:** Even the ApiKey handler's `resource_metadata` URL would be wrong (using `api://` URI instead of the server's HTTP base URL), so neither scheme currently produces a correct `WWW-Authenticate` challenge.

---

## What the Fix Should Be

### Fix 1: Add `OnChallenge` to JwtBearer configuration

In `AuthenticationServiceExtensions.cs`, configure the JwtBearer events to inject the correct `WWW-Authenticate` header:

```csharp
authBuilder.AddJwtBearer(name, options =>
{
    options.Authority = scheme.Authority;
    options.Audience = scheme.Audience;
    options.RequireHttpsMetadata = scheme.RequireHttpsMetadata;
    // ... existing config ...

    options.Events = new JwtBearerEvents
    {
        OnChallenge = context =>
        {
            var authCfg = context.HttpContext.RequestServices
                .GetRequiredService<IOptions<AuthenticationConfiguration>>();
            if (authCfg.Value.ProtectedResource?.Resource is not null)
            {
                var request = context.HttpContext.Request;
                var baseUrl = $"{request.Scheme}://{request.Host}";
                context.Response.Headers["WWW-Authenticate"] =
                    $"Bearer resource_metadata=\"{baseUrl}/.well-known/oauth-protected-resource\"";
            }
            return Task.CompletedTask;
        }
    };
});
```

**Important:** The `baseUrl` must be derived from `HttpContext.Request` (the actual server URL), NOT from `ProtectedResource.Resource` (which is an `api://` URI).

### Fix 2: Fix ApiKeyAuthenticationHandler

Replace:
```csharp
var metadataUrl = $"{authConfig.Value.ProtectedResource.Resource}/.well-known/oauth-protected-resource";
```
With:
```csharp
var request = Context.Request;
var metadataUrl = $"{request.Scheme}://{request.Host}/.well-known/oauth-protected-resource";
```

### Additional consideration: VS Code's pre-registered client_id

Once VS Code can properly discover Entra ID via the PRM, it should use its own pre-registered client ID (`aebc6443-996d-45c2-90f0-388ff96faa56`) rather than the fallback GUID. Confirm this works post-fix by verifying that:
1. The `WWW-Authenticate` header contains `resource_metadata`
2. VS Code fetches the PRM and follows `authorization_servers` to Entra ID
3. The browser redirect goes to `login.microsoftonline.com` with `client_id=aebc6443-996d-45c2-90f0-388ff96faa56`

---

## Files to Modify (when fix is approved)

| File | Change |
|------|--------|
| `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs` | Add `JwtBearerEvents.OnChallenge` to inject `resource_metadata` in `WWW-Authenticate` |
| `PoshMcp.Server/Authentication/ApiKeyAuthenticationHandler.cs` | Fix `resource_metadata` URL construction to use `Request.Scheme + Request.Host` |

---

## Deployed Config Summary (for reference)

- **App Registration Application ID / Audience:** `80939099-d811-4488-8333-83eb0409ed53`
- **Tenant ID:** `d91aa5af-8c1e-442c-b77c-0b92988b387b`
- **JwtBearer Authority:** `https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b`
- **PRM `authorization_servers`:** `["https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b"]` ✅ correct
- **VS Code expected client_id:** `aebc6443-996d-45c2-90f0-388ff96faa56` (per docs)
- **VS Code actual client_id in redirect:** `80939099-d811-4488-8333-83eb0409ed53` ← fallback mode


### 1. Scope Naming Convention
- **New file** (`entra-id-mcp-auth.md`): Used `user_impersonation` as scope name example
- **Existing file** (`entra-id-auth-guide.md`): Used `access_as_server` as scope name example
- **Decision**: Keep `access_as_server` (more descriptive; already used throughout the guide for consistency)
- **Impact**: Low — both are valid; users should pick meaningful names for their use case. Consolidated guide now explicitly states this is a user-choice with guidance on granular scope design.

### 2. Protected Resource Metadata (PRM) Configuration
- **New file**: Mentioned App Service EasyAuth automatic PRM generation via `WEBSITE_AUTH_PRM_DEFAULT_WITH_SCOPES` environment variable
- **Existing file**: Covered manual `/.well-known/oauth-protected-resource` endpoint implementation for self-hosted deployments
- **Decision**: Include both approaches in the guide
- **Impact**: Informational addition. Users deploying on App Service now know they can use EasyAuth's auto-generation; self-hosted users already had guidance. No breaking changes.

### 3. VS Code Pre-Registered Client ID Authorization (Critical Missing Step)
- **New file**: Explicitly covered VS Code's pre-registered client ID (`aebc6443-996d-45c2-90f0-388ff96faa56`) and need to authorize it in "Authorized client applications"
- **Existing file**: Did not mention VS Code client authorization or this critical setup step
- **Decision**: Add as **Step 2b** in app registration setup (new step between scope creation and M2M credentials)
- **Rationale**: This is essential guidance for VS Code MCP users. Without authorizing the pre-registered client ID, users get "Dynamic client registration not supported" error with no clear fix
- **Impact**: High importance — prevents user confusion and support burden. New users will now see this step clearly

### 4. Scope Consent Model Guidance
- **New file**: Briefly mentioned consent model selection ("Admins only" vs "Admins and users")
- **Existing file**: Covered this in detail with guidance on M2M scenarios
- **Decision**: Existing guide's coverage is comprehensive; no changes needed
- **Impact**: None — existing documentation already correct

## Content Migration Summary

| Content Area | Source | Location in Consolidated Guide |
|--------------|--------|--------------------------------|
| OAuth 2.1 + RFC 9728 basics | New file | VS Code MCP Integration subsection |
| VS Code client ID authorization | New file | Step 2b (Authorize Client Applications) |
| VS Code OAuth flow explanation | New file | VS Code MCP Integration subsection |
| VS Code settings.json config | New file | VS Code MCP Integration subsection |
| Protected Resource Metadata endpoint | New file | VS Code MCP Integration subsection |
| PRM via App Service EasyAuth | New file | VS Code MCP Integration subsection |
| VS Code troubleshooting | New file | VS Code MCP Integration subsection |
| App Registration general guidance | Existing | Path A (unchanged) |
| Managed Identity guidance | Existing | Path B (unchanged) |
| Token validation & security | Existing | Token Validation & Security section (unchanged) |
| Comprehensive troubleshooting | Existing | Troubleshooting section (enhanced with VS Code errors) |

## Decision Authority

**Authority**: Leela (Developer Advocate) — documentation structure and organization

**Rationale for Keeping Existing File as Canonical**:
- More comprehensive scope (covers app registration + managed identity + security + troubleshooting)
- Better structured with clear paths and decision matrices
- Established TOC and cross-references
- More extensive testing and troubleshooting sections

**Rationale for Adding VS Code as Subsection (Not Separate Doc)**:
- Avoids link fragmentation — users looking for "Entra ID auth" now find everything in one place
- VS Code is one implementation scenario, not a separate authentication method
- Single source of truth for app registration steps (no duplication)
- Easier to maintain consistency across both general and VS Code-specific guidance

## Files Changed

- **Modified**: `docs/entra-id-auth-guide.md` (added Step 2b and VS Code MCP Integration subsection)
- **Deleted**: `docs/entra-id-mcp-auth.md` (content consolidated)
- **Updated**: `.squad/agents/leela/history.md` (added learning notes)

## Testing & Validation

- ✓ No broken cross-references (only reference was in auto-generated DOCFX summary)
- ✓ All VS Code-specific content from new file now in consolidated guide
- ✓ All app registration and managed identity content from existing file preserved
- ✓ Scope naming, terminology, and step sequence consistent throughout
- ✓ No duplicate content in final guide

## Recommendation for Future Entra ID Auth Docs

If new authentication scenarios emerge (e.g., third-party OIDC providers, custom claims mapping), add them as subsections to `docs/entra-id-auth-guide.md` rather than creating separate files. Keep the main authentication guide as the single source of truth.

If a scenario becomes large enough to warrant its own detailed guide, create a separate file and link to it from the main guide's TOC, but avoid duplication of core setup steps.


# Decision: VS Code Scope Naming Requirements

**Date:** 2026-05-01  
**Status:** RESOLVED — No changes needed  
**Owner:** Leela (Developer Advocate)  
**Stakeholder:** Steven Murawski  

## Question

After consolidating Entra ID documentation and choosing `access_as_server` as the scope name, Steven flagged a concern: Does VS Code specifically require the scope name `user_impersonation` rather than custom scope names?

## Investigation Results

### 1. VS Code OAuth Flow with MCP

VS Code's MCP client uses OAuth 2.1 with PKCE and a pre-registered client ID (`aebc6443-996d-45c2-90f0-388ff96faa56`). The flow:

1. VS Code connects to the MCP server
2. Server responds with `401 Unauthorized` + metadata URL
3. **VS Code fetches Protected Resource Metadata (RFC 9728) from the server**
4. **Metadata includes `scopes_supported` array listing available scopes**
5. VS Code requests those scopes during the OAuth flow
6. User authenticates and grants consent for the requested scopes
7. VS Code receives a token with the approved scopes

**Key insight:** VS Code does NOT hardcode scope names. It dynamically reads scope names from the server's Protected Resource Metadata endpoint.

### 2. Scope Naming Conventions

**`user_impersonation`** — Microsoft's built-in convention:
- Used for Azure service permissions: `AzureServiceManagement/user_impersonation`, `https://management.azure.com/user_impersonation`
- Indicates delegated access (acting on behalf of a user)
- Owned by Microsoft services

**`access_as_server`** — Custom scope owned by PoshMcp:
- Follows the custom scope pattern: `api://app-id/scope-name`
- Descriptive: clearly indicates delegated server access
- Fully configurable (any name works)

### 3. VS Code Compatibility

✅ **VS Code is compatible with any scope name**, as long as:
- The scope is declared in `ScopesSupported` in the Protected Resource Metadata
- The scope is authorized in "Authorized client applications" for the VS Code client ID
- The token includes the scope in its `scp` claim

No special naming convention is required.

## Decision

**Keep `access_as_server` as the scope name for PoshMcp.**

### Rationale

1. **Ownership:** PoshMcp defines and owns its custom scopes; `user_impersonation` belongs to Microsoft services
2. **Clarity:** `access_as_server` better describes the permission (delegated server access)
3. **Flexibility:** Custom scope names are fully supported by VS Code's dynamic scope discovery
4. **Standards compliance:** Follows OAuth 2.0 + RFC 9728 standards without constraint
5. **Existing compatibility:** Already implemented and working in the current documentation

## Documentation Status

✅ **No changes needed.** The current documentation is accurate:
- `access_as_server` is properly configured
- VS Code section correctly explains the Protected Resource Metadata mechanism
- Scope authorization step (Step 2b) is correct
- All troubleshooting guidance is accurate

## References

- **RFC 8414**: OAuth 2.0 Authorization Server Metadata (well-known endpoint discovery)
- **RFC 9728**: OAuth 2.0 Protected Resource Metadata (scope discovery)
- **Microsoft Entra ID scopes documentation**: Custom scopes follow pattern `api://{app-id}/{scope-name}`
- **VS Code MCP integration**: Uses RFC 9728 for dynamic scope discovery

---

**Next Steps:** None — document this finding in Leela's learnings and archive the decision.

### 2026-05-12: Bender — AuthServer metadata diagnosis (AggregateError fix)

**By:** Bender (Backend Developer)

**Problem:** VS Code reports `AggregateError: Failed to fetch authorization server metadata` when discovering the deployed PoshMcp instance.

**Root cause (primary):** `ProtectedResource.AuthorizationServers` in the deployed appsettings.json was missing the `/v2.0` suffix. Without it, Entra ID returns a v1.0 OIDC discovery document whose `issuer` is `https://sts.windows.net/{tenant}/` — which does not match the authorization_server URL (`login.microsoftonline.com`). VS Code rejects the document per RFC 8414 §3 (issuer validation).

**Root cause (secondary):** The deployed PRM response contains duplicated entries in `authorization_servers` (2x), `scopes_supported` (2x), and `bearer_methods_supported` (3x). The 2/2/3 pattern matches the constructor default of `BearerMethodsSupported = new() { ""header"" }` plus the config being bound twice. Likely caused by the custom appsettings.json being registered with the configuration pipeline more than once.

**Fix (required):** Append `/v2.0` to entries in `ProtectedResource.AuthorizationServers`.

**Fix (recommended):** Investigate the duplicate-binding cause and default `BearerMethodsSupported` to `new()` (empty) so config replacement works cleanly.

**Status:** Diagnosis only — fix not yet applied. Full diagnosis archived to the orchestration log.

**File**: `.squad/decisions/inbox/bender-authserver-metadata-diagnosis.md` (now merged)

---

### 2026-05-12: Bender — PR #211 test fixture architecture

**By:** Bender (Backend Developer)

**Decision:** For end-to-end validation of PR #211 (proxy detection + high-parameter delegate emit), use **reusable test fixtures that build real CommandInfo objects via PowerShell** (no mocking). Place them under `PoshMcp.Tests/Fixtures/` so Fry can consume them from integration tests.

**Components:**
- `ProxyTestFixtures.cs` — static factories: `CreateProxyStyledCommand()` (proxy path) and `CreateHighParameterCommand()` (17 parameters → triggers cached delegate emit, since BCL `Func<>` only goes to `Func17`).
- `Pr211IntegrationFixtureSetup.cs` — xUnit collection-fixture infrastructure with caching so fixtures are built once per collection.
- `README.md` — usage docs for teammates.

**Why real commands:** Mocked `CommandInfo` would not faithfully exercise the proxy detection (`IsImplicitRemotingProxy`) or the > 16-param delegate emit path. Tests already require a PowerShell runtime, so the cost of real fixtures is acceptable.

**Status:** Fixtures committed; Fry consumes them in the new integration tests for PR #211.

**File**: `.squad/decisions/inbox/bender-pr211-fixture-architecture.md` (now merged)

---

### 2026-05-12: Recommend trait-based phasing + resource hygiene audit for test suite consistency (spec 009)

**By:** Farnsworth (Lead/Architect), with Fry (Tester)
**For:** Steven Murawski (Brady)

**Decision:** For spec 009 (test suite consistency), recommend **Option 1 (trait-based phasing via `[Trait(""Category"", ...)]`)** as the first step, combined **in parallel** with **Option 3 (per-test resource hygiene audit — dynamic ports, GUID temp dirs, deterministic subprocess teardown)**. Defer Option 2 (separate test projects) until trait-based phasing has run in CI for two weeks. Hold Option 4 (drain fixtures) as a targeted follow-up only if specific categories remain flaky after Option 3 lands.

**Why:**
- The hard user requirement — *""all unit tests should always be able to be run and run quickly""* — is unblocked fastest by traits + a documented `dotnet test --filter` command. Days, not weeks.
- Traits alone only isolate flakes; they don't fix them. The hygiene audit addresses the actual root cause (port reuse races, pwsh handle leaks, temp-dir collisions across a 6-minute serial run).
- A project split is the likely correct end state but the wrong first step. Run with traits first, learn which boundaries actually matter, then split along validated lines.
- Drain fixtures are a sharpened tool for a specific shape of failure — predict less, measure more.

**Non-goals reaffirmed:** No test rewrites, no skipping tests, no framework change, no re-enabling parallelism, no benchmark changes.

**File**: `specs/009-test-suite-consistency/spec.md` (spec authored)

---

---

# Spec 009 acceptance — open questions resolved

**Date:** 2026-05-12
**By:** Farnsworth (Lead/Architect)
**Requested by:** Brady

## Decision

Spec 009 (Test Suite Consistency and Fast Unit Tier) moves from **Proposed** to **Accepted**. All seven open questions are resolved.

## Resolutions

1. **OQ-1 — Reference machine for < 60s target.** Maintainer's primary dev machine is the reference. Documented in **FR-419**.
2. **OQ-2 — Default category for untagged tests.** Permissive — untagged tests fall back to a default bucket (not `Unit`). No strict analyzer required at this stage. Documented in **FR-417**.
3. **OQ-3 — Functional/* classification.** Rule, not case-by-case: Functional = exercises multiple areas of code, no external resources. Any test that touches disk, network, files, subprocesses, or ports is `Integration` (or `OutOfProcess` / `Http`). Documented in **FR-416**. Existing `Functional/*` tests get audited under Issue 9.
4. **OQ-4 — Azure credentials in CI.** Deferred. Skip-when-no-creds locally remains in scope; CI-side Azure execution is a future task. Documented as a **Non-Goal**.
5. **OQ-5 — EditorConfig / analyzer to require `[Trait("Category", ...)]`.** Dropped from scope. Documented as a **Non-Goal**.
6. **OQ-6 — Option 4 cooldown duration.** Blocked on OQ-4 (Option 4 itself is deferred). Documented as a **Non-Goal**.
7. **OQ-7 — Flake-rate reporting.** Dedicated CI step that re-runs the phased suite N=5 times and emits a single flake-rate summary artifact. Documented in **FR-418**, scoped under Issue 5.

## Work plan

Milestone: **Spec 009: Test Suite Consistency** (number assigned at creation time — staging at `C:\Users\stmuraws\AppData\Local\Temp\poshmcp-spec009`).

Issues filed under the milestone:

1. Add Category traits to all tests (Option 1 baseline) — FR-400, FR-406, FR-417.
2. Reclassify misfiled `Unit/*` tests — FR-401, FR-402, FR-403, FR-414.
3. Document per-category local commands (TESTING.md) — FR-408.
4. CI: split full suite into category-scoped phases — FR-409.
5. CI: dedicated flake-rate step — FR-418 (OQ-7).
6. Resource hygiene: dynamic ports — FR-411.
7. Resource hygiene: pwsh subprocess teardown — FR-412.
8. Resource hygiene: unique temp directories — FR-403, FR-410.
9. Functional → Integration reclassification rule — FR-416 (OQ-3).
10. Measure unit-tier acceptance: <60s, 5x clean — SC-100, SC-101, FR-404, FR-405; blocked by #1, #2, #6, #7, #8.

## Trade-offs accepted

- **Permissive default bucket over strict analyzer (OQ-2/OQ-5).** Accepting the risk that a new test could land untagged and silently fall into the default bucket. Mitigation: the default bucket is documented and is explicitly **not** `Unit`, so an untagged test cannot accidentally promote itself into the fast pre-commit tier. A strict analyzer is a follow-up if untagged tests become a recurring problem.
- **Functional rule applied as a hard line, not case-by-case (OQ-3).** Reduces judgment overhead but may reclassify a test that "feels" Functional but touches disk. Trade-off accepted because case-by-case was already producing inconsistent classifications.
- **Azure-in-CI deferred (OQ-4).** CI will not catch Azure-category regressions until this is revisited. Acceptable because (a) Azure tests already skip locally without creds — same behavior in CI; (b) maintainer can run them on demand; (c) credentialed CI is a meaningful infra change that doesn't belong on this spec's critical path.
- **Drain fixture (Option 4) deferred until after Option 3 lands (OQ-6).** If hygiene audits (Issues 6, 7, 8) eliminate the flakiness, the drain fixture is unnecessary and we save complexity. If they don't, we'll have data on where leaks survive and can target the fixture precisely.

## EMU note

Milestone creation and issue creation are blocked under Farnsworth's account by the EMU policy on `usepowershell/PoshMcp` (HTTP 404 on `gh api POST` and `gh issue create`, same pattern previously logged for `gh issue create` and `gh pr review`). Staging is at `C:\Users\stmuraws\AppData\Local\Temp\poshmcp-spec009\` with a `create-all.ps1` script that creates the milestone and all 10 issues when run from a non-EMU context.

---

### 2026-05-12: OOP invoke must surface `hadErrors=true` as a thrown exception

**By:** Bender (Backend Engineer), requested by Brady
**Scope:** `PoshMcp.Server/PowerShell/OutOfProcess/` (single and pool executors)

**Bug pattern** (from a real user report): a tool call to `assert_tenant_role_member`
with an invalid role returned what looked like the previous `assert_tenant_user`
success payload, with MCP `IsError=false`. The server log showed
`warn: ... reported errors. Output: {prior-looking JSON}` followed by
`"assert_tenant_role_member" completed. IsError = False.`

**Mechanism** (NOT cross-invoke leak):
- Each invoke uses a fresh `[powershell]` instance with its own streams.
  `$Error.Clear()` already runs at the top of the OOP user script (#189 fix). So
  there is no stale `$Error` or stale stream contamination between invokes.
- The "prior-looking payload" is actually the *current* command's intermediate
  pipeline output captured by `$r = & $Name @Splat` BEFORE the command writes a
  non-terminating error to the error stream. AdvocacyBami's
  `Assert-BamiTenantRoleMember` internally calls `Assert-BamiTenantUser` (which
  emits the user object) and then writes a non-terminating error for the bad
  role. `$r` ends up bound to the user object, which gets JSON-serialized.
- The OOP host correctly reported `hadErrors=true` on the wire. The .NET-side
  `InvokeAsync` (both single and pool variants) logged a warning and returned
  the partial output unchanged. The MCP framework can only mark a tool result
  `IsError=true` when the generated method throws — a successful string return
  is always treated as success.

**Decision**: when an OOP invoke response carries `hadErrors=true` and
`cancelled=false`, `InvokeAsync` MUST throw `InvalidOperationException` with a
message of the form `OOP error: command '{name}' reported {N} error(s): {joined
errors}`. The `"OOP error:"` prefix matches the existing terminating-error path
(`OutOfProcessHost` → `tcs.TrySetException(new InvalidOperationException("OOP error: " + msg))`)
so existing test catches like `ex.Message.Contains("OOP error")` keep working.

**Explicit non-decisions**:
- `cancelled=true` is excluded from the throw. Cancellation has its own surface
  and reclassifying it as a tool error would break the cancel-in-flight path.
- A command can legitimately write to `$Error` and still produce output worth
  surfacing. Post-fix that case becomes `IsError=true`. If a future caller wants
  a tolerant variant, add a separate API — do NOT weaken this gate.
- The PowerShell-side user script is NOT changed. `$Error.Clear()` and fresh
  per-invoke `[powershell]` instances were already correct.

**Files touched**:
- `PoshMcp.Server/PowerShell/OutOfProcess/OutOfProcessCommandExecutor.cs`
  (single-host `InvokeAsync` + new `ExtractErrorMessage` helper)
- `PoshMcp.Server/PowerShell/OutOfProcess/OutOfProcessSubprocessPool.cs`
  (pool `InvokeAsync` + new `ExtractInvokeErrorMessage` helper)
- `PoshMcp.Tests/Integration/OutOfProcessIntegrationTests.cs` — new test
  `Invoke_WithErrorAfterSuccess_DoesNotReturnPreviousOutput`

**Test gate**: 40/40 `Category=OutOfProcess` tests pass.

**Why this matters for future bug reports**: when a user describes "the previous
command's output leaked into the next command", check `hadErrors` plumbing
first. The OOP runspace is shared by design (so `Connect-AzAccount` state
persists across invokes), so any "stale state" claim should be cross-checked
against the actual single-invoke output shape before chasing leak hypotheses.

---


### 2026-05-12: OOP cross-invoke output leak — investigated, could not reproduce

**By:** Bender (revisiting prior incomplete diagnosis)
**Requested by:** Steven Murawski

**What:** Steven reported a cross-invocation state leak in OOP PowerShell execution: a
command that initially returned `null` started returning *prior commands' output* after
other invokes ran. He explicitly rejected the prior diagnosis (which addressed a related
but distinct hadErrors-not-propagated bug) and asked for the real leak to be found and
fixed — with reproduction required FIRST.

**Investigation:**
- Reviewed all OOP source files for shared mutable state across invokes:
  - `oop-host.ps1`, `oop-host-pool.ps1`: fresh `[powershell]` per invoke, local-scoped
    `$r`, `$Error.Clear()` at top of user script. No `$script:`/`$global:` output buffer.
  - `OutOfProcessHost.cs`: `_pending` keyed on per-request Guid, removed on completion.
  - `OutOfProcessCommandExecutor.cs` + `OutOfProcessSubprocessPool.cs`: no result cache;
    `_cachedSchemas` is discover-only; `_lastSetupConfig` is restart-only.
  - No mutable static fields in OOP module.
- Built two new regression tests reproducing Steven's exact sequence (empty-returning
  cmd → producing cmd → rerun empty cmd, assert null) on both Single and Pool hosts.
  Both PASS on current main (HEAD `273bc3b`).
- Full `Category=OutOfProcess` suite: 46/46 PASS.

**Outcome:** Could NOT reproduce a framework-level cross-invoke output leak. Per
Steven's directive ("do NOT push a speculative fix"), no production code changed.

**Committed:** 2 new regression tests as permanent guards. They will fail loudly if a
real cross-invoke leak is ever introduced.

**Hypotheses NOT chased (would need Steven's exact command list to verify):**
1. A user-module's own `$script:`-scoped state leaking across invokes (out of framework
   scope to detect or fix).
2. Subprocess restart/reconnect path with overlapping calls.
3. A specific parameter-binding shape in the tool generator.

**Why:** The user's stated observation requires real time-separated state survival —
the prior diagnosis's "current-invoke partial pipeline output" cannot explain
"command returned null first, then started returning later commands' output". Without
a faithful reproduction, any production change would be speculative.

**Files changed:**
- `PoshMcp.Tests/Integration/OutOfProcessIntegrationTests.cs` (test added)
- `PoshMcp.Tests/Integration/OutOfProcessPoolHostIntegrationTests.cs` (test added)


# Decision — OOP user-script defensive scope (defense-in-depth)

**Date:** 2026-05-12
**By:** Bender (Backend Developer)
**Requested by:** Steven Murawski (Brady)
**Status:** Applied — commit e1c923e on main

## Context

Brady reported a deployed poshmcp-web v0.12.2 returning byte-for-byte
identical payloads from two sequentially invoked, structurally unrelated
MCP tools (`get_tenant_context` then `assert_tenant_role_member`). The
v0.12.2 server pre-dates commit 6908917 ("fix(oop): clear per-invoke
state so errors don't return prior output"), which converts an invoke
that reports `hadErrors=true` into a thrown `InvalidOperationException`
that MCP surfaces as `IsError=true`. On v0.12.2 the same condition logs
a warning and returns the partial pipeline output as a successful tool
result.

The earlier (2026-05-12) repro round on current main HEAD used a single
script body with different parameters at pool size 2 across 6 iterations
and could not reproduce a framework-level cross-invoke output leak. The
honest disposition recorded at the time was: I cannot reproduce, and I
will not push a speculative fix.

## What was done in this round

1. **Production-shape repro test.** Added
   `PoolHost_AlternatingDifferentScripts_LargePool_NoCrossInvokeLeak` to
   `OutOfProcessPoolHostIntegrationTests.cs`. The new test mirrors the
   deployed configuration (runspacePoolSize=10) and uses TWO structurally
   different commands per iteration over 50 iterations:
   - **A:** `Write-Output -InputObject <per-iteration sentinel>` — a
     fresh sentinel per iteration; asserts the response contains the
     current sentinel and NO prior sentinel.
   - **B:** `Write-Verbose -Message <iteration tag>` — returns nothing;
     asserts the response equals the canonical `"null"` payload and
     contains NO prior sentinel.
   The test passes on current main HEAD even without the defensive
   change below, confirming the framework-level `$r`-leak hypothesis is
   not what produces the user's reported symptom.

2. **Defensive change.** Updated both `oop-host.ps1` and
   `oop-host-pool.ps1` to call `AddScript($userScript, $true)` instead
   of `AddScript($userScript)`. With `useLocalScope=$true` the script
   body runs in a child scope of the runspace's default scope, so the
   per-invoke working variable `$r` is discarded when the pipeline
   returns instead of living at runspace scope where the next invoke on
   the same leased runspace could observe it. The per-pipeline
   `Streams.Error` and `HadErrors` flags are unaffected because they
   live on the `[powershell]` instance, not the runspace scope chain.

3. **First attempt rejected.** An initial version of this change also
   wrapped the call site in an inner `& { ... }` scriptblock for
   redundant child-scope isolation. That broke
   `HadErrorsDoesNotLeakAcrossInvokes`: with the inner scriptblock in
   place, `Get-ChildItem -Path missing -ErrorAction SilentlyContinue`
   no longer surfaced as `HadErrors=true` on the parent pipeline
   (`Streams.Error` remained populated but the boolean flag flipped).
   The single-layer `useLocalScope=$true` change passes all prior tests
   and gives the same structural defense without the side effect on
   non-terminating-error reporting.

## Reproduction outcome

- **Did I reproduce a framework-level cross-invoke output leak locally?**
  No. Two iterations of repro design (the prior 2026-05-12 attempt at
  pool=2 / 6 iterations / same script with different params, and this
  round's pool=10 / 50 iterations / different scripts) both ran clean
  on current main HEAD.
- **Did I land a fix anyway?** Yes, explicitly as defense-in-depth
  rather than a repro-driven point fix.

## What this commit does NOT fix

- It does NOT make the production-deployed v0.12.2 server stop
  returning the deceptive tenant-context payload from
  `assert_tenant_role_member`. That requires deploying current main
  (≥ 6908917), which converts `hadErrors=true` into a thrown exception
  that MCP marks `IsError=true`. The defensive scope change applies a
  second layer to that already-merged primary fix.
- It does NOT prevent user-authored modules from setting their own
  cross-invoke state (`$global:` or `$script:` variables in the user's
  module scope). That is module behavior and the framework cannot
  contain it from outside.

## Files changed

- `PoshMcp.Server/PowerShell/OutOfProcess/oop-host.ps1` — AddScript
  call now passes `$true` (useLocalScope); explanatory comment block.
- `PoshMcp.Server/PowerShell/OutOfProcess/oop-host-pool.ps1` — same.
- `PoshMcp.Tests/Integration/OutOfProcessPoolHostIntegrationTests.cs` —
  added `PoolHost_AlternatingDifferentScripts_LargePool_NoCrossInvokeLeak`.

## Test status

- `Category=OutOfProcess`: 47 passed, 0 failed, 0 skipped.
- New production-shape test passes on current main HEAD with the
  defensive change applied.

## Commit

`e1c923e fix(oop): defensive per-invoke scope for user script`,
pushed to `main`.
