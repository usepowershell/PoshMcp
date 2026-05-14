
- Created `integration/spec-002-mcp-resources-and-prompts` from `main` and merged all 4 feature branches in order.
- `feature/002-resources` merged clean. `feature/002-prompts` conflicted on `Program.cs` — resolved by merging `ConfigureServerServices`/`RegisterMcpServerServices` signatures to accept both handlers, and chaining all 4 `With*Handler` calls in HTTP and stdio paths.
- `feature/002-doctor` had add/add conflicts on all 5 config model files (it defined its own nullable-property versions). Kept HEAD (implementation branch) non-nullable versions; validator `IsNullOrWhiteSpace` checks are compatible with both.
- `feature/002-tests` merged clean.
- Build: `dotnet build PoshMcp.sln --no-incremental` → **succeeded**, 5 pre-existing warnings in `McpToolFactoryV2.cs` (unrelated to Spec 002).
- Branch pushed to `origin`.
- Key lesson: when 3+ branches all modify `Program.cs` service registration, the standard pattern is to merge signatures by adding parameters for each feature's handler/config, then chain all handlers together.

### 2026-05-07 - v0.11.0 minor release cut
- Bumped PoshMcp.Server/PoshMcp.csproj Version 0.10.0 -> 0.11.0.
- Added [0.11.0] CHANGELOG entry above 0.10.0. Marquee: out-of-process subprocess pool (Pool now default SubprocessHostMode, #196). Also: ProcessPool mode, OutOfProcessHost extraction, OOP cancellation propagation (#188), PoshMcp.Benchmarks harness, ConvertTo-Json wrap (#203), $Error clear (#189), CWE-117 log scrubbing in OOP host, CI minimum permissions + SECURITY.md, docs (#210).
- Did NOT tag/push - Steven runs the tag after Cubert reviews release notes.
- Did NOT touch SECURITY.md or docs/release-notes/ - Leela has those in flight.
- Build verified: dotnet build PoshMcp.sln -c Debug -> 0 errors, 19 pre-existing nullable warnings.

### 2026-05-07 - Lockout-revision: fix config key in 0.11.0 release notes (Cubert rejection)
- Took over from Leela (locked out per Reviewer Rejection Protocol).
- Cubert flagged: both jsonc snippets in 'Upgrade Notes' used "PowerShell" as top-level key; correct binding section is "PowerShellConfiguration" (verified in PoshMcp.Server/appsettings.json).
- Replaced "PowerShell" -> "PowerShellConfiguration" in both snippets (opt-out Single example + opt-in ProcessPool example). No other changes.

### 2026-05-11 - v0.12.0 release: Doctor resilience + proxy cmdlet support
- **Version bump:** 0.11.0 → 0.12.0 (minor). Doctor resilience + WinPSCompat proxy support both qualify as feature-level additions; no breaking changes.
- **Release notes:** Created `docs/release-notes/0.12.0.md` covering (1) resilient Doctor command with error handling, (2) proxy cmdlet discovery via cached-delegate generation for >16-parameter methods, (3) integration test coverage, (4) no upgrade notes required.
- **Release workflow:** (1) Created release notes file, committed before tag (`git commit -m "docs: Release notes for v0.12.0"`), (2) bumped version in PoshMcp.Server/PoshMcp.csproj `<Version>` tag, committed (`git commit -am "chore: Bump version to 0.12.0"`), (3) created annotated tag (`git tag -a v0.12.0 -m "Release v0.12.0"`), (4) pushed main branch then tag separately (`git push origin main` → `git push origin v0.12.0`). **Critical:** Release notes must be committed BEFORE tag creation per charter constraint.
- **Key learnings:** (a) Release notes commit is a gate; tag creation depends on it. (b) Version bumps and release notes are independent commits (enables cherry-pick/revert granularity). (c) Git push of branch and tag are separate operations. (d) PR #211 (proxy support) + doctor branch (resilience) merged cleanly; no conflicts. (e) Semantic versioning decision: feature-level work → minor bump (vs. patch).
- **Commits shipped:** Release notes (ff8997f), version bump (40e4a56); tag v0.12.0 created and pushed.

### 2026-05-11 - v0.12.1 patch release: Code formatting cleanup
- **Hotfix patch:** 0.12.0 → 0.12.1 (formatting/maintenance only).
- **What happened:** dotnet format discovered and fixed trailing whitespace and spacing inconsistencies (single change: collection expression spacing in DoctorService.cs).
- **Release notes:** Created lightweight docs/release-notes/0.12.1.md documenting patch as 'maintenance' with no functional changes.
- **Release workflow:** (1) Amended HEAD commit message to 'chore: Code formatting cleanup', (2) bumped version in PoshMcp.Server/PoshMcp.csproj 0.12.0 → 0.12.1, committed ('chore: Bump version to 0.12.1'), (3) created release notes, committed ('docs: Release notes for v0.12.1'), (4) created annotated tag (git tag -a v0.12.1 -m 'Release v0.12.1 — patch release'), (5) pushed main and tag.
- **Key learnings:** (a) Formatting-only releases are valid patch cycles — tooling (dotnet format) finds regressions/inconsistencies that are worth capturing in version history. (b) Amend HEAD if formatting commit landed with incorrect message (no need to rebase or force-push if only local). (c) Patch releases don't require detailed upgrade notes — single-line 'maintenance/cleanup' suffices. (d) Hotfix workflow is fast: one or two functional commits + lightweight release notes + tag + push.
- **Commits shipped:** Formatting cleanup (c9e67b2), version bump (ef50162), release notes (554d9ce); tag v0.12.1 created and pushed.

## Learnings

- 2026-05-12: Wrote v0.12.3 release notes (docs/release-notes/0.12.3.md). Hotfix release covering two OOP executor fixes: (1) error-with-partial-output now throws InvalidOperationException with 'OOP error:' prefix instead of returning intermediate pipeline objects as success, and (2) defensive useLocalScope=$true on Invoke-Command in oop-host.ps1/oop-host-pool.ps1. Real-world repro was Assert-BamiTenantRoleMember returning tenant-context object as if the role check passed. Followed 0.12.2.md style: Summary / Fixes / Behavior notes / Tests / Upgrade path / Affected files. Called out the behavior change explicitly so callers know error-vs-success semantics shifted. Added entry to docs/toc.yml at top of Release Notes section.

## Learnings — 2026-05-12 (issue #223, PR #237)

- Cold-start baseline scenario: `PoshMcp.Benchmarks.Scenarios.ColdStartBenchmark` (Single / Pool / ProcessPool host modes).
- Exact command: `dotnet run -c Release --project PoshMcp.Benchmarks -- --filter "*ColdStart*"`
- BDN artifacts land in `BenchmarkDotNet.Artifacts/results/` (md/csv/html) + `BenchmarkDotNet.Artifacts/*.log`.
- Bench-runs convention: copy results/ contents + run log into `bench-runs/run-N-{tag}/` and write a README.md capturing date, SHA, machine, command, gating rule.
- Gotcha: `.gitignore` excludes `*.log` so BDN run logs do NOT get committed even if copied. The .md/.csv/.html reports are the durable record. Either rename the log to `.log.txt` or accept that local-only.
- ApplicationInsights export is already disabled in `PoshMcp.Benchmarks/Program.cs` via env vars so latency reflects executor cost only.
- ColdStartBenchmark uses `[InvocationCount(1)]` so Mean column = per-cold-start cost (not amortized). Don't divide.
- Run 5 numbers on SJMDEVBOX (32 lcores, ~128 GiB, .NET SDK 10.0.107): Single 5.79 s, Pool 5.78 s, ProcessPool 7.00 s.

## Learnings — 2026-05-12 (issue #223, PR #237)

- Cold-start scenario: PoshMcp.Benchmarks.Scenarios.ColdStartBenchmark (Single/Pool/ProcessPool).
- Command: dotnet run -c Release --project PoshMcp.Benchmarks -- --filter "*ColdStart*"
- BDN artifacts in BenchmarkDotNet.Artifacts/results/ (md/csv/html) + *.log.
- bench-runs convention: copy results into bench-runs/run-N-tag/ + README.md (date, SHA, machine, command, gating rule).
- Gotcha: .gitignore excludes *.log so BDN run logs do NOT commit; .md/.csv/.html are the durable record.
- AppInsights export already disabled in Program.cs via env vars; latency reflects executor cost only.
- ColdStartBenchmark uses [InvocationCount(1)] so Mean = per-cold-start cost.
- Run 5 on SJMDEVBOX (32 lcores, ~128 GiB, .NET 10.0.107): Single 5.79 s, Pool 5.78 s, ProcessPool 7.00 s.

## Learnings — 2026-05-13 (#231 / PR #245)

### Tracker is per-discovery-cycle, not DI singleton
- `IToolDescriptionSourceTracker` is OPTIONAL (`IToolDescriptionSourceTracker?`) and is only wired by the doctor command path. In normal HTTP/stdio operation the tracker is null.
- Decorator-over-tracker would silently miss every production resolution. For metrics that must fire on EVERY resolution, decoration is the wrong pattern.
- Lesson: when the brief says "decorator preferred", verify the decorated thing is actually present at every call site. If it can be null, decoration is brittle.

### McpToolFactoryV2 metrics pattern
- Existing code uses `private static McpMetrics? _metrics` injected via `SetMetrics(...)` from a hosted service in `HttpServerHost` and `StdioServerHost`. Static field, not constructor injection.
- New counters added to `McpMetrics` follow the same pattern — declare as `Counter<long>` properties, instantiate in constructor with `_meter.CreateCounter<long>(name, description: ...)`.
- For new metric emission, add a `private static` helper inside the consuming class (`McpToolFactoryV2`) that null-checks `_metrics` once and wraps the `.Add(...)` in try/catch. Mirrors the surrounding `_metrics?.ToolRegistrationTotal.Add(...)` style but adds the failure isolation the charter requires.

### DescriptionSourceVocabulary is the single source of truth
- `DescriptionSourceVocabulary.ToWireValue(ToolDescriptionSource)` and `ToWireValue(ParameterDescriptionSource)` produce the FR-583 string literals. Doctor JSON serializer AND OTel tag emission both call it. Never hardcode the strings anywhere else.
- Side benefit: the vocabulary throws `ArgumentOutOfRangeException` on unknown enum values, which is exactly what failure-isolation try/catch absorbs.

### MeterListener test pattern (no existing precedent in repo)
- Use `MeterListener.InstrumentPublished` to filter on `instrument.Meter.Name == McpMetrics.MeterName` AND specific instrument names; call `listener.EnableMeasurementEvents(instrument)`.
- Set `SetMeasurementEventCallback<long>` to capture `(instrument, measurement, tags, state)` — copy tags into a dict because the `ReadOnlySpan<KeyValuePair<...>>` doesn't outlive the callback.
- `listener.Start()` AFTER both setup steps. Dispose in test `Dispose()`.
- For testing private static helpers, reflection (`BindingFlags.NonPublic | BindingFlags.Static`) is acceptable — exercises the exact code path the production sites use. Cache the `MethodInfo` in a static field.

### Relevance for #232 (re-bench)
- Counter emission adds: 1 null-check + 1 try/catch + 1 `Counter<long>.Add` per resolution. `Add` on an unobserved counter is essentially free (no `MeterListener`-attached callbacks = no work). When OTel pipeline IS attached, overhead is one allocation for the `KeyValuePair<string, object?>` tag.
- Expected impact on description-resolution hot paths: negligible (sub-microsecond per call). Re-bench should still confirm.

### Worktree workflow note
- Worktree at `poshmcp-231` shared the same commit as main, so reads from main checkout were fine for context. Writes MUST go to the worktree path.
- `gh auth switch --user usepowershell` before `gh pr create` to satisfy the EMU policy — `stmuraws_microsoft` cannot push to `usepowershell/PoshMcp`.


### 2026-05-13 - v0.13.0 minor release cut (PR #251, draft)

**Bump:** 0.12.3 -> 0.13.0 (minor). Spec 010 (tool self-documentation) end-to-end + #242 wire-path fix + #222 SwitchParameter fix. New observable behavior in tools/list and doctor warrants a minor.

**Files touched (the canonical version-bump set for this repo):**
- `PoshMcp.Server/PoshMcp.csproj` `<Version>` element (single source of truth)
- `CHANGELOG.md` (prepend new section)
- `docs/release-notes/0.13.0.md` (new file)
- `docs/toc.yml` (new entry above the previous version)

To find them quickly next time: `git grep -n "<old-version>" -- ':!docs/release-notes/' ':!.squad/' ':!bench-runs/' ':!artifacts/'`. PowerShell `Select-String -Recurse` is wrong (no `-Recurse` switch on Select-String) - use `Get-ChildItem -Recurse | Select-String`, or just `git grep` (much faster on a tracked repo).

**Workflow:**
1. `git worktree add ../poshmcp-release-0.13.0 -b release/0.13.0 main` (kept main checkout clean while it had unrelated dirty files from a prior session).
2. Edit the four files. Build + unit tests in the worktree (532/532 passed under Release).
3. `git add` only the four release files (don't pick up worktree-local stragglers).
4. Commit with title `release: 0.13.0`. Push branch.
5. Open draft PR (`gh pr create ... --draft`) so Steven can review before tag/publish.

**Holding line:** Did NOT push tag, did NOT publish GitHub release, did NOT push containers. Steven gives the green light per process.

**PR:** #251 (draft, `release/0.13.0` -> `main`).

**EMU gotchas hit this run:**
- `Select-String -Recurse` is not a thing - had to switch to `git grep`.
- `gh pr create --body-file` works fine for UTF-8 content with code fences and bullets; no need for `--body` heredoc gymnastics.
- The repo on github.com has been renamed to `PoshMcp` (mixed case); `git push` warns about the rename but the operation still works against the old URL.

## Learnings — 2026-05-14T11:34Z — v0.13.0 release

- Executed two-phase autonomous release for v0.13.0 (Steven away).
- **Housekeeping commit:** `5847efb` — bundled stale agent history.md updates (amy, bender, cubert, farnsworth, hermes, leela) and docker.ps1 changes that had accumulated on main since v0.12.3.
- **Release commit:** `a2b9c3e` — bumped `PoshMcp.Server/PoshMcp.csproj` Version 0.12.3 → 0.13.0, prepended 0.13.0 entry to CHANGELOG.md, staged Leela's pre-drafted `docs/release-notes/0.13.0.md`.
- **Quality gates (both passed):**
  - `dotnet format --verify-no-changes`: passed (warnings only — workspace-load NU1510, no failure exit).
  - `dotnet test --nologo`: 777 passed, 0 failed, 7 skipped, 784 total. Duration 11m15s. The earlier run was self-cancelled at 143s — probably triggered by my polling `get_terminal_output` on a long-running terminal. Lesson: for ~10min test runs, set generous `timeout` and DO NOT poll mid-flight; let the sync run finish.
- **Push:** `git push origin main` succeeded (83a3703..a2b9c3e). Both housekeeping and release commits landed together. Note: GitHub responded with a "repository moved" hint (poshmcp → PoshMcp casing), redirect handled silently.
- **HARD STOP held:** No tag created or pushed. Release process requires CI green before tagging; Steven (or whoever returns) will run `git tag -a v0.13.0 -m "v0.13.0"` followed by `git push origin v0.13.0` once CI on a2b9c3e passes.
- Marquee theme for v0.13.0 (per CHANGELOG): Help-aware tool descriptions (spec 010) — in-process and OOP byte-identical schemas, FR-500/FR-510/FR-540, `IToolMetadataSource` seam, doctor `descriptionSource` reporting, OTel counters, parity tests, cold-start gates. Includes fixes for SwitchParameter round-trip (#222), parameter descriptions on inputSchema (#248), and `HelpAwareToolMetadataSource` as default (#250). No breaking API changes.
- Process pattern reaffirmed: explicit-path `git add` only, no `-A` / no `--force`, push to main only, tag only after CI green.

## Learnings — 2026-05-14 (#216 / spec 009 FR-418)

- Authored `.github/workflows/flake-rate.yml`: separate workflow (cleaner than bolting onto `ci.yml`), triggers `workflow_dispatch` (with `runs` input, default 5) + nightly `schedule` (cron `0 7 * * *`).
- Mirrors PR #252 phasing exactly (Unit/Integration/OutOfProcess/Http/Functional). Azure phase intentionally excluded — scheduled runs have no creds, would skip silently.
- Loop pattern: bash `for i in $(seq 1 RUN_COUNT)` with `set +e` so a flaky phase in iteration 3 does NOT short-circuit iterations 4 and 5. Per-phase exit codes captured into `flake-runs/run-{i}/exit-codes.txt`; loop step always exits 0. The aggregator owns the verdict.
- Aggregator in PowerShell (matches repo idiom). Walks TRX via `Select-Xml` + `XmlNamespaceManager` (TRX uses default namespace `http://microsoft.com/schemas/VisualStudio/TeamTest/2010` — dotted access silently returns nothing). Counts non-Passed outcomes, breaks out Failed / NotExecuted / Other separately so the table tells you what kind of flake it was.
- Aggregate rate = `total non-pass instances / total test invocations` (intentional: same test failing 3/5 should weigh more than three different tests each failing once).
- Output is markdown (`flake-rate-summary.md`) — easier human read than JSON. Uploaded as `flake-rate-summary` artifact AND mirrored into `$GITHUB_STEP_SUMMARY` so reviewers see it on the run page without downloading. Raw TRX uploaded as `flake-runs-raw` for drill-down.
- TESTING.md (PR #253) is owned by another agent — did NOT touch it. PR body calls out exactly where the pointer to `flake-rate-summary` artifact should land in TESTING.md so the next person merging #253 can add it without coordination overhead.
- Workflow co-exists with `ci.yml` from PR #252; if #252 lands first the phasing already matches, if it lands after this still works (separate workflow file).
- Gotcha: PowerShell expanded `$(seq 1 ...)` inside an unquoted here-string when appending these notes. Use single-quoted here-strings (or write a file then append) when notes contain literal shell syntax.
## 2026-05-14T17:20Z — PR #251 cleanup (v0.13.0)
- v0.13.0 was already shipped via direct-to-main (5847efb + a2b9c3e); PR #251 was redundant and CONFLICTING.
- Cherry-picked the only unique content (`docs/toc.yml` v0.13.0 entry, +2 lines) from `origin/release/0.13.0` to main as commit `cbf5d7d`.
- Pushed main (a2b9c3e..cbf5d7d, also carried prior unpushed scribe log commit cd7c9f5).
- Deleted `origin/release/0.13.0` (matches convention — no other release/* branches preserved on origin).
- PR #251 auto-closed when source branch was deleted. `gh pr comment` blocked by EMU policy, so closure rationale lives only in this history entry + commit message.
- Note: repo URL casing changed remote-side (poshmcp -> PoshMcp); `gh` calls now require `--repo usepowershell/PoshMcp` explicitly.

## 2026-05-14: Spec 009 closed via this session

Spec 009 (Test Suite Consistency and Fast Unit Tier) is functionally complete. Five PRs merged in the closeout wave (#252, #253, #257, #259, #260) and six issues closed (#213, #214, #215, #216, #220, #221). Issue #221 acceptance gate (Fry) measured the Unit tier at 432 passed / 0 failed / 0 skipped across 5 consecutive runs, mean 20.45s wall-clock — well under the <60s FR-419 budget. Your contribution: see your own history entries for this session.

