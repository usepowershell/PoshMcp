# Farnsworth — Lead/Architect — Work History

### 2026-05-15 — PR #271 review (Hermes, spec 011 Phase 2 / #268)

**Verdict:** APPROVE. Posted via `gh pr comment` (#issuecomment-4463390971) — self-approval blocked because PR author and reviewer share `usepowershell` identity (same pattern as #252, #269). Artifact: `artifacts/farnsworth-pr271-review.md`.

**What I cross-checked:**
- **SC-263-4 backward compat:** `RemoteToolSchema.Source*` are `string?` with no `[JsonRequired]`; `RemoteModuleImportsPayload` is a brand-new top-level property handled by `TryGetProperty` short-circuit. Older hosts deserialize cleanly with nulls. Both executors `try/catch (JsonException)` around payload deserialization → null + LogWarning on parse failure (degrades to older-host fallback, doesn't crash).
- **FR-263-9 priority (`commandName > module > pattern`):** Both PS hosts enforce via execution order + `if (-not $sourceMap.ContainsKey($cmd.Name))` gate. Identical structure in both `oop-host.ps1` and `oop-host-pool.ps1`. Integration test exercises all three sources in one config, asserts `Get-Date` (FunctionNames hit) has both `SourceModule` and `SourcePattern` null — priority verified end-to-end.
- **AsyncLocal capture lifecycle:** `Reset()` at start of `DiscoverToolsAsync`, `Set()` BEFORE `await using var executorLease` exits scope, `Current` read at consumption site. AsyncLocal flows through awaits in same ExecutionContext. CLI invocations are one-shot → cross-flow contamination structurally impossible. The combination is the safe shape; documented in XML.
- **Pool fingerprint race:** All three paths (`SetupAsync` clear, `DiscoverCommandsAsync` write, `LastModuleImports` getter) take `_envLock` → no torn reads. Single-host variant doesn't lock, matching its existing `_cachedSchemas` semantics.
- **Older-host warning gate:** Three-condition AND (OOP mode + module-or-pattern config + null capture) is precise. Doesn't fire for InProcess (capture structurally null) or `CommandNames`-only configs (absence is correct per FR-263-6). Per-doctor-report scope (PR description's "one-time" wording is loose — actual is "one warning per report" — minor doc nit).
- **`tools[]` parity:** New `Source*` fields populated on wire but NOT yet consumed by `BuildModuleImportsSection` for the consolidated `tools[]` array — explicitly deferred. SC-263-3 byte-parity for `tools[]` holds because both runtime modes run the same C# heuristic on the same input. Dead-on-the-wire risk mitigated by explicit XML doc comments.

**Non-blocking follow-ups captured (next-touch):**
1. Pool variant integration test gap — only single-host integration test exists.
2. Older-host warning unit-test gap — fallback contract not unit-tested.
3. Tests use Newtonsoft.Json round-trip but production deserializer is System.Text.Json. PascalCase property names line up so STJ works without `PropertyNameCaseInsensitive`, but companion STJ test would catch future serializer-option drift.
4. Doc nit on "one-time warning" wording.

**Pattern noted for capture:** **AsyncLocal as deliberate alternative to signature-refactor** for cross-disposal capture in CLI-shaped one-shot async flows. When a value is produced on one side of a disposal boundary and consumed on the other within the same flow, AsyncLocal is the smaller change than refactoring a public-ish return shape — provided the lifecycle contract is documented and `Reset()` is called at flow entry to prevent stale leaks. The combination Hermes used (Reset-at-start + Set-before-disposal + Current-at-read, all within one CLI invocation) is the safe shape. Worth replicating for any future Phase-3+ doctor-section enrichment that sits across the same lease-disposal boundary.

**Process note (auth):** `gh auth status` precheck before posting confirmed `usepowershell` (keyring) was active vs. `stmuraws_microsoft` inactive. Mandatory per `.squad/decisions.md` L31-36 — without it, the approval comment would post under the wrong identity.

## Learnings - 2026-05-13 - PR #247 review (issue #234, spec 010 step 11)

### Verdict
APPROVE. Documentation for FR-500/FR-510 description precedence chains in docs/articles/exposing-tools.md plus README cross-link.

### What I cross-checked and what passed
- **Vocabulary parity**: Doc literals (synopsis/description/syntax/name and helpParameter/helpMessage/validateSet/typeFallback) match IToolDescriptionSourceTracker.cs ToString() lines 152-165 verbatim. This is the property we want — doctor JSON, OTel `step` tag, and prose are all one phrase.
- **Resolver behavior**: HelpAwareToolMetadataSource.cs ResolveToolDescription early-returns at each rung; the doc's "tries each step in order, stops at first non-empty" framing is structurally accurate.
- **Step 3 string format**: Code emits `$"{CommandName} {ParameterSetSyntax}"`. Doc shows `"Get-FixtureBare [[-Anything] <string>] [<CommonParameters>]"` — matches.
- **ValidateSet prefixes**: Code line 111 uses `"Each item is one of: "` vs `"One of: "` based on `ValidateSetAppliesToArrayElement`. Doc strings match verbatim, including punctuation/spacing.
- **TypeFallback**: `$"Parameter of type {typeName}"` matches doc's `"Parameter of type System.String"`.
- **Synopsis-equals-name filter**: The StringComparison.Ordinal guard in the code is correctly described as "not equal to the command name".
- **#242 callout**: Honest framing — what works (resolver + doctor), what doesn't (inputSchema plumbing), tracking link, what authors should do today, what changes when fix ships. NO fix date promised.
- **Cross-link**: README anchor `#description-precedence` resolves to `## Description Precedence` heading in both GitHub and DocFX renderers.
- **HelpParityFixture mapping**: Every fixture function the doc references exists in HelpParityFixture.psm1 with the matching shape.

### Doc review patterns worth keeping
- For a multi-step resolver, **table + tiny per-step example** beats prose. Anchoring step names to the literal wire vocabulary makes the doc dual-purpose: human reading + searchability across doctor output and metrics.
- **In-line "Known issue" callout** under the affected section (not at bottom) for partially-shipped features. Format: what works / what doesn't / link / today's guidance / what changes on fix. The "no module change required" line is what makes the callout safe to ship — authors aren't asked to wait.
- **README defers to article**, doesn't restate the chain. Single-link cross-link from a one-line bullet is the right hook.
- For doc PRs touching behavior I've reviewed code-side, cross-checking literal strings (prefixes, format strings, enum-to-wire names) catches drift faster than re-reading the prose.

### Process note
Worktree-local strategy meant I could `gh pr view` and read implementation files in the same session without cross-branch confusion. Resolved everything from c:\Users\stmuraws\source\github\usepowershell\poshmcp-234.
### 2026-05-14 — Reviewed PR #253 (Leela — TESTING.md, branch squad/214-testing-md-commands)

**Verdict:** APPROVE with two non-blocking nits. Posted via gh pr comment (#issuecomment-4453556321). Architectural review only; Cubert handles fact-check.

**Strongest aspect:** Doc is authored against the spec FR contract, not against parallel-PR implementation choices. Default-bucket *name* deferred to PoshMcp.Tests/README.md (set by #212), CI phase order deferred to .github/workflows/ci.yml (set by #215). TESTING.md commits only to "default bucket is not Unit" and "category X reproduces phase X locally" — so it can merge in any order relative to #212/#215 without stranding a false reference. Reviewer notes call out the two re-verification points explicitly. This is the right authoring discipline for parallel work streams.

**Location:** Repo root — correct. Joins README/CONTRIBUTING/SECURITY/DOCKER as a contributor-facing top-level doc. Burying under docs/ would have hidden it behind the DocFX site (which targets end-users, not contributors).

**README integration:** Two-entry-point split (quick-start in README, deep-dive in TESTING.md) — same pattern as PR #210 OOP docs (configuration.md brief + advanced.md deep-dive). No category-table duplication. README diff also flips  + "--filter \"FullyQualifiedName~Unit\"" +  →  + "--filter \"Category=Unit\"" +  aligning the public surface to the trait contract.

**FR coverage:** FR-408 ✓ explicit table. FR-413 ✓ documented in Azure caveats AND repeated in Troubleshooting (justified — Troubleshooting is where operators actually hit the symptom). FR-417 ✓ has its own section, defers bucket name correctly. Bonus coverage of FR-411/412/416/418 in caveats even though not strictly required.

**Two non-blocking nits:** (1) Integration row "Spawns  + "pwsh" + : Yes (in-process runspaces)" — in-process runspaces don't spawn  + "pwsh.exe" + , parenthetical disambiguates but stricter reading is "No". (2) Flake-rate troubleshooting uses cmd  + "or /l" +  syntax — most contributors run PowerShell or bash; either show all three forms or pick bash to match the doc's other code fences.

**Pattern noted:** When a docs PR ships AHEAD of (or in parallel with) the implementation PRs it documents, defer all *named* implementation details (bucket name, phase order, runner config) to the source-of-truth file owned by the implementation PR, and commit only to the *contract* in your doc. This keeps the doc correct under any landing order. Apply this to future docs-first PRs in multi-PR specs.

### 2026-05-14 - Reviewed PR #256 (Fry - Spec 009 Category traits baseline)

**Verdict:** REJECT (request changes). Posted via gh pr comment (#issuecomment-4453570148). Reassigned to **Bender** (NOT Fry - reviewer-rejection lockout).

**Architectural framing:** Class-level [Trait("Category", "Unit")] IS a reclassification, not "honest tagging." It's an active claim that every method on the class satisfies FR-401 (no pwsh) / FR-402 (no port) / FR-403 (no shared temp). The PR's framing - "no test reclassification, deferred to #213" - falls apart for Unit/OutOfProcess/* because tagging those classes Unit is making the false claim NOW, not deferring it.

**Spot-check method (the bit that found the problem):** When PR description says "honest tagging only" but applies traits by folder, audit the FOLDER the spec edge case explicitly names. Spec 009 names Unit/OutOfProcess/* and Unit/ProgramCli* by name. Three minutes of grep confirmed:
- OutOfProcessCancellationTests constructs new OutOfProcessHost(pwshPath, ...) + StartAsync -> spawns pwsh (FR-401).
- OutOfProcessHostConcurrencyTests runs new HttpListener() on 127.0.0.1 ephemeral port (FR-402).
- OutOfProcessCommandExecutorTests has both StartAsync_ThenDisposeAsync_FullLifecycle (spawns pwsh) AND ResolveModulePaths_* using Path.Combine(Path.GetTempPath(), "PoshMcp-ResolveModulePaths") - non-Guid-unique shared temp (FR-403).

**Class-level trait aggregation hazard:** xUnit class-level traits apply to every test method on the class. Even files where SOME methods are pure constructor-validation Unit tests (OutOfProcessHostTests, OutOfProcessSubprocessPoolTests first 2 each) cannot wear Unit if any sibling method violates FR-401/402/403. Class-level category MUST reflect the class's most-resource-intensive method. Fix: tag those classes OutOfProcess. Pure validation classes can stay Unit.

**Downstream harm if shipped as-is:** PR's headline 'Unit 424' count includes the violators. Once that count is canonical, contributors and CI both treat it as the fast tier - but dotnet test --filter Category=Unit against that set will fail FR-404 (< 60s) and FR-405 (zero flakes across 5 runs) because pwsh-spawn / port-bind work is still inside the filter.

**What's right (kept for context):** AssemblyInfo.cs FR-417 default = Integration (never Unit) - correct. Three pre-existing non-canonical class-level traits cleanly replaced (McpPrompts, McpResources, OutOfProcessModules). Method-level non-canonical traits explicitly deferred - good scope discipline. scripts/add-category-traits.ps1 is idempotent, partial-class aware, doc-comment stripping, refuses unmapped classes - right shape for a baseline tool. FR-415 determinism check (counts reproduced 3x) is the right gate for a metadata change. 92 files / +404 / -3 / draft - appropriately scoped.

**Reassignment rationale:** Bender owns the OOP production code (Spec 004 Pool / cancellation work, PRs #200, #207). Bender is structurally immune to the folder-name-as-category trap - knows from writing the code which classes spawn pwsh and which don't. Hermes (Spec 004 PR #201) was the alternative, but Bender's PR #207 cancellation work is closer in time and scope to the audit needed.

**Pattern noted (capture for TESTING.md / #214):** Class-level category traits on mixed-content test classes are a one-bit summary of an N-shape file. When the auditor (script OR human) only sees the folder, the bit defaults to "whatever the folder says." Corrective rule for #213: the class-level category MUST reflect the class's most-resource-intensive method, not the folder. If a class genuinely needs both Unit and OutOfProcess methods, split the class - don't compromise the trait.

### 2026-05-14 — PR #258 review (Hermes, spec 009 / #219)
- ✅ APPROVED. `TempDirectory : IDisposable` helper, prefix `poshmcp-test-` + `Guid:N`, best-effort recursive delete, never-throw-on-dispose contract verified (bare `catch { }` swallows all non-fatal exceptions; failed deletes route to `s_undeleted` for diagnostic `GetUndeletedDirectories()`).
- Composes cleanly with `SubprocessTeardown` from PR #255 — same `Shared/` namespace, same `IDisposable` + best-effort + never-throw shape. Two helpers, one shared-helpers idiom.
- Audit hooks land #216's CI diagnostic seam: `AuditLeftoverDirectories()` sweeps `poshmcp-test-*` from temp path; no further plumbing needed for phased-CI "leaked dirs?" check. Worth referencing in #216's design.
- Coverage scope: 3 real audit hits (`ResolveModulePaths_DeduplicatesCaseInsensitively` from my PR #256 flag + 2 `ResolveConfigurationPath_*` cases writing bare appsettings.json/config.json into temp root) + 3 representative refactors. Whole-suite migration explicitly deferred — appropriate, mechanical churn against now-canonical pattern.
- `OopTestPaths` left alone: defensible under FR-407 (serial execution prevents the fixed `OopE2EProbe` name from racing). Caveat for the record: if any consumer mutates state in that fixed dir and a later test expects clean slate, serial execution stops being sufficient cover. Flagged for whatever follow-up audits `OopTestPaths`.
- Comment posted: https://github.com/usepowershell/PoshMcp/pull/258#issuecomment-4453765946

### 2026-05-14 — PR #257 review (Amy, ci(009): flake-rate workflow)

**Verdict: APPROVE.**

- Separate workflow file (vs. extending ci.yml) is correct — different shape (loop+aggregate vs. single-pass), independent trigger surface (workflow_dispatch+nightly cron), independent runtime budget.
- N=5 default with workflow_dispatch.inputs.runs override resolved via ${{ github.event.inputs.runs || '5' }}. Loop uses set +e + per-phase exit codes captured to exit-codes.txt; loop ends with explicit xit 0 so aggregator owns gating.
- Phasing Unit→Integration→OOP→Http→Functional matches PR #252 one-for-one. Azure correctly excluded (no creds in scheduled runs, would only add noise). FR-407 no-parallelism invariant preserved (single job, sequential bash loop).
- **Measurement vs. gating divergence is deliberate and correct.** Within an iteration, all phases run regardless of earlier failure — production CI halts on first failure, flake measurement wants max signal. A test that's "always green" only because production halts before reaching it is exactly the kind of flake this workflow surfaces.
- Aggregator robustness verified: missing flake-runs/ → stub summary; corrupt TRX → try/catch+continue; missing exit-codes lines → - placeholder; iter sort uses [int] cast so run-10 sorts after run-9; if: always() on aggregator + uploads. XmlNamespaceManager bound to TeamTest 2010 schema (correct — TRX uses default namespace).
- Aggregate flake rate definition (non-pass instances / total invocations) is correct: one test failing 3/5 weighs more than three tests each failing 1/5.
-  1MB cap only at risk in catastrophic worst case (every test flaked every iter), at which point the workflow has bigger problems.
- TRX path collisions: none (per-iter subdir + stable phase filenames).
- Idempotent on rerun (rebuilds summary from scratch each time).
- Minor non-blocker: no concurrency: group set — overlapping manual+nightly would run in parallel. Low probability; flag for next touch.

Reusable lessons:
1. **Measurement vs. gating semantics differ.** Flake-rate workflows should NOT mirror production CI's fail-fast — they want max signal. Halting on first phase failure hides flakes in later phases. set +e + capture-then-decide is the right shape.
2. **TRX parsing requires XmlNamespaceManager.** TRX uses default namespace http://microsoft.com/schemas/VisualStudio/TeamTest/2010; dotted-property access silently returns empty. Select-Xml / SelectNodes with namespace manager is correct.
3. **Numeric directory sort.** 
un-1, run-2, ..., run-10 sorts lexicographically wrong; Sort-Object { [int](.Name -replace '^run-','') } is the fix.
4. **Per-iteration TRX subdirs prevent collision** without needing rename-after-write logic. Stable filenames inside per-iter dirs is cleaner than timestamped filenames.
5. **if: always() on summary + upload steps** is non-negotiable for measurement workflows — you need the partial data when the run blew up, that's often where the signal is.

### 2026-05-14 — PR #258 review (Hermes, spec 009 / #219)
- ✅ APPROVED. `TempDirectory : IDisposable` helper, prefix `poshmcp-test-` + `Guid:N`, best-effort recursive delete, never-throw-on-dispose contract verified (bare `catch { }` swallows all non-fatal exceptions; failed deletes route to `s_undeleted` for diagnostic `GetUndeletedDirectories()`).
- Composes cleanly with `SubprocessTeardown` from PR #255 — same `Shared/` namespace, same `IDisposable` + best-effort + never-throw shape. Two helpers, one shared-helpers idiom.
- Audit hooks land #216's CI diagnostic seam: `AuditLeftoverDirectories()` sweeps `poshmcp-test-*` from temp path; no further plumbing needed for phased-CI "leaked dirs?" check. Worth referencing in #216's design.
- Coverage scope: 3 real audit hits (`ResolveModulePaths_DeduplicatesCaseInsensitively` from my PR #256 flag + 2 `ResolveConfigurationPath_*` cases writing bare appsettings.json/config.json into temp root) + 3 representative refactors. Whole-suite migration explicitly deferred — appropriate, mechanical churn against now-canonical pattern.
- `OopTestPaths` left alone: defensible under FR-407 (serial execution prevents the fixed `OopE2EProbe` name from racing). Caveat for the record: if any consumer mutates state in that fixed dir and a later test expects clean slate, serial execution stops being sufficient cover. Flagged for whatever follow-up audits `OopTestPaths`.
- Comment posted: https://github.com/usepowershell/PoshMcp/pull/258#issuecomment-4453765946

### 2026-05-14 — PR #252 review (Amy — ci(009): category-scoped phases) — already complete

Ralph routed this to me, but the architectural review was already posted as a PR comment (artifacts/farnsworth-pr252-review.md from a prior session). Cubert's fact-check is also already on the PR. Verdict stands: APPROVE. Tried to formalize via gh pr review --approve to flip GitHub UI to a green review state but failed — usepowershell bot account authored the PR so it cannot self-approve. The comment-form approval is the team accepted pattern (matches PR #255 precedent). No new findings; nothing to redo. The PR is unblocked and ready for Amy to flip out of draft and merge.

**Lesson:** Before doing review work, check the PR existing comments/reviews — Ralph queue can re-route work already done in a prior session. One `gh pr view 252 --json reviews,comments` would have caught this in the first 5 seconds. Make this the first move on any "review PR #N" task.

### 2026-05-14 — Reviewed PR #259 (Fry — reclassify misfiled Unit/OutOfProcess tests, Spec 009 FR-414, closes #213)

**Verdict:** APPROVE. Posted via gh pr comment (#issuecomment-4455131167). Artifact: `artifacts/farnsworth-pr259-review.md`.

**Cleanest possible reclassification PR.** 8 files, +1/-1 each, single namespace line edit per file (`PoshMcp.Tests.Unit.OutOfProcess` → `PoshMcp.Tests.OutOfProcess`). `git mv` similarity 98–99%. Mechanically impossible to have changed assertions or test bodies given the +1/-1 shape — the review reduces to confirming the diff really is what the description claims it is.

**Trait-vs-folder check applied (the PR #256 lesson).** Grepped each retained Unit file (`OAuthProxyEndpointsTests`, `WinPsCompatProxyTests`, `ProgramCliBuildCommandTests`, `ServerSessionAwarePowerShellRunspaceTests`) for the actual `[Trait("Category", ...)]` attribute rather than inferring from folder layout. All carry `[Trait("Category", "Unit")]` correctly. None match `Process.Start`, `ProcessStartInfo`, `TcpListener`, `HttpListener`, `"pwsh`, `GetTempPath`, or port-binding patterns. The misfiling really was directory-only — the metadata was already correct.

**Audit table is accurate.** PR description's "files audited and confirmed compliant" section matches actual contents of `PoshMcp.Tests/Unit/` on the PR branch. Justifications hold up: NoOpEndpointRouteBuilder stub for OAuth (no real listener), in-process `Program.Main` invocation with `TemporaryDirectory` + `CurrentDirectoryScope` for ProgramCli (PR #258 isolation helpers), in-process `PowerShell.Create()` for WinPsCompat (runspace ≠ subprocess), pure mocks for ServerSessionAware.

**Acceptance for #213 fully satisfied:** Unit category 0 process-spawning / 0 port-binding / 0 shared-temp; audit table present and accurate; no assertions modified.

**Tier metrics align:** Unit 432/0 in 39s (well under 60s FR-405 budget), OOP 155/0/6 with first-run flake clean on rerun (matches the historical OOP-tier flakiness profile from PR #255 — pwsh subprocess spawn timing, not regression).

**Non-blocking observation:** `PoshMcp.Tests/Unit/OutOfProcess/` directory should be empty post-merge — flagged as a hygiene check, not a gate.

**Pattern to remember (codified in the review for future reclassification PRs):** When a PR carries the FR-414 "metadata-only" guarantee, review reduces to three mechanical checks — (1) `gh pr diff` shows only namespace/folder lines, no `+/-` outside import/namespace blocks; (2) audit table matches diff one-for-one; (3) retained files grep-clean for forbidden patterns under the new boundary, NEVER inferred from folder layout (PR #256 lesson). All three hold → PR is safe to land. This is the third Spec 009 PR (after #256 trait-tagging and #258 isolation helpers) where the "grep the file, never infer from folder" rule has been the deciding mechanic.

### 2026-05-14 — Reviewed PR #260 (Fry — FR-416 sweep of Functional/, spec 009 closing PR, closes #220)

**Verdict:** APPROVE. Posted via gh pr comment (#issuecomment-4455307819). Artifact: `artifacts/farnsworth-pr260-review.md`. This is the closing PR of spec 009.

**Three-check playbook (from #259) applied cleanly:**
1. `gh pr diff 260` — exactly four changes, all metadata or doc: two `[Trait]` flips, one `git mv` (98% similarity) + namespace + trait flip, one TESTING.md additive paragraph. No `[Fact]` bodies, `Assert.*`, ctor/setup, or fixtures touched. FR-414 clean.
2. PR body audit table is one-for-one with the diff: three reclassifications listed, three trait changes in diff, one folder move listed, one `git mv` in diff.
3. Grep against retained `Functional/*.cs` for FR-416 violation patterns (`File.*`, `Path.GetTempFileName`, `Process.Start`, `HttpClient`, `TcpListener`, `InProcessMcpServer`, `ExternalMcpClient`, `StartAsync(`) — every hit resolves to either a file already promoted in this PR or a TODO comment in `McpPromptsTests`/`McpResourcesTests` (placeholder facts).

**Partial-class promotion policy decision codified.** When `[Trait("Category", ...)]` lives on a single declaration of a `partial class` and any partial touches external resources, the **whole class promotes together**. Three options were available: (1) promote the whole partial class, (2) split the partial into two separate classes, (3) per-method `[Trait]` overrides. Within the FR-414 metadata-only constraint, (1) is the only correct move — (2) is structural refactor (out of scope) and (3) relies on flaky xUnit per-method override semantics on partials. The over-classification of the no-IO partials in `SetupTests` (`ShouldFilterCorrectlyWithExcludePatternsTest`, `ShouldHandleNonExistentFunctionGracefullyTest`, `ShouldHaveEmptyDefaultValuesTest`, `ShouldReturnEmptyListWithEmptyConfigurationTest`, `ShouldReturnToolsWithValidConfigurationTest`, `ShouldWorkWithDefaultParameterlessOverloadTest`) is a known cost worth a follow-up issue if the team later wants surgical split, but **not blocking**. Recording as a team policy: see decisions inbox drop.

**Borderline calls confirmed:**
- `ShouldHandleGetChildItemCorrectlyTest` stays Functional. `Path.GetTempPath()` returns a string with zero filesystem access; the test is permanently `[Fact(Skip=...)]`. Skip-gated string ops do not cross the FR-416 boundary.
- `McpPromptsTests`/`McpResourcesTests` stay Functional — the `InProcessMcpServer` references are `// TODO:` comments only; the actual facts are `Assert.True(true)` placeholders. Will need re-audit when the placeholders are filled in (and they likely flip to OutOfProcess at that time).
- `StdioLoggingTests` move to `OutOfProcess/StdioLoggingTests.cs` — correct. Was already mistagged `Integration` on the `Functional/` path (existing folder/trait mismatch); subprocess spawn via `InProcessMcpServer` + `ExternalMcpClient` is textbook OutOfProcess shape, strictly more specific than Integration.

**Patterns to remember:**
- **Partial-class trait scope.** `[Trait]` on a `partial class` declaration applies to the whole class; xUnit will not let you per-method override that reliably across partials. When applying FR-416 to a partial class, the trigger is **any** partial touching external resources, and the result is **the whole class** moving together. Document this trade-off in the PR body so reviewers can verify the audit and the cost is explicit.
- **Grep both raw IO and project-specific helpers.** First-pass grep on `Process.Start|pwsh` would have missed `StdioLoggingTests` because the codebase wraps subprocess spawning in `InProcessMcpServer`/`ExternalMcpClient`/`StartAsync`. Always sweep both raw IO patterns AND test-helper abstractions used in the codebase. Fry called this out independently in her #220 audit — worth elevating to a standing checklist for future Spec-009-style sweeps.
- **TODO-comment grep hits are not violations.** `Select-String -Pattern 'InProcessMcpServer'` will hit comments. Always inspect context (`-Context 1,1`) before treating a hit as an FR-416 violation. McpPrompts/McpResources are the canonical example: real `InProcessMcpServer` use is in TODO blocks for facts that don't yet exist.
- **Spec 009 closeout.** With #260 approved, all eight FR-416 / FR-414 reclassification PRs in the spec 009 wave are landed or under final review. The lessons accumulated across #213 / #253 / #255 / #256 / #259 / #260 — folder name is not the trait, partials promote together, grep both raw and helper IO patterns — should be migrated into a skill (`categorization-trait-review`) for the next wave.

## 2026-05-15: Team update (via Scribe)
**Ralph round 1 — 3 PRs in-flight, may need your review:**
- **PR #266** (Bender, issue #261): Doctor pool display sentinel — EffectiveProcessPoolSize / EffectiveMinHealthyForStartup promoted to `string`, returning `"n/a (<mode> mode)"` when inert. Files: `DoctorService.cs`, `DoctorReport.cs`, `DoctorTextRenderer.cs` + Unit tests.
- **PR #264** (Hermes, issue #262): AAD v2.0 `preferred_username` mapping — added `ClaimsMapping.NameClaim` to `AuthenticationConfiguration`; wires to `JwtBearerOptions.TokenValidationParameters.NameClaimType`. Null preserves default (no behavior change for existing deployments). Files: `AuthenticationConfiguration.cs`, `AuthenticationServiceExtensions.cs`, `docs/entra-id-auth-guide.md`.
- **PR #265 DRAFT** (Farnsworth, issue #263): Spec 011 design-only — `specs/011-doctor-module-imports/spec.md` (13 FRs / 4 SCs / 5 OQs). Implementation split to follow-up issues #267 (Bender) and #268 (Hermes).

### 2026-05-15 — Reviewed PR #269 (Hermes — Phase 1 of #268, in-process ModuleDiscovery helper)

**Verdict:** APPROVE. Posted via gh pr comment (#issuecomment-4462727166). Artifact: artifacts/farnsworth-pr269-review.md. Formal review --approve blocked by GitHub self-approval rule (usepowershell bot authored, same path as PR #252) — comment-form approval is the team accepted pattern.

**Helper interface fits Phase 2 / #267 cleanly.** ModuleProbeResult(Name, Found, Version, Path) is exactly the four FR-263-2 fields Bender's BuildModuleImportsSection needs to populate moduleImports.modules[]. The remaining FR-263-2 fields (contributedToolCount/Names, status, diagnostic) are all CommandInfo.ModuleName-derived or computed at section-build time — correctly NOT in the probe helper's scope.

**Phase split holds.** Phase 2 checklist (RemoteToolSchema additive fields, oop-host*.ps1 source attribution, OutOfProcessCommandExecutor.DiscoverCommandsAsync surface, McpToolFactoryV2 parity, SC-263-3 parity tests, older-host fallback) does not touch ModuleDiscovery or ModuleProbeResult. Phase 1 ships standalone; Phase 2 is independently reviewable. The issue body's explicit allowance for the split is the right call.

**FR-263-10 verified by structure, not just by test.** ProbeModules → ExecuteThreadSafe(ps => foreach name → ProbeOne(ps, name)) → Get-Module -ListAvailable -Name <single name>. One PowerShell call per non-blank input entry. No per-command lookup, no -Module enumeration. The DuplicateNames test confirms the "one call per *configured* name" reading: same name twice → two calls, two results.

**FR-263-11 (no new pwsh process) verified.** Uses IPowerShellRunspace.ExecuteThreadSafe — composes onto the existing tool-discovery runspace. No Runspace.Open(), no RunspaceFactory.Create*, no Process.Start. Compatible with both production runspaces and per-test IsolatedPowerShellRunspace.

**Defensive details worth noting:**
- try { ps.Commands.Clear(); } catch { } in the catch branch prevents a half-built command pipeline from poisoning the next loop iteration. This is the bug shape that doesn't appear in single-module fixtures and bites later.
- TryReadProperty swallows its own exceptions — handles the rare PSObject-with-throwing-property case gracefully.
- ErrorAction SilentlyContinue keeps non-terminating Get-Module errors from triggering the catch — only terminating exceptions get the warning log.

**[Trait("Category", "Unit")] tag honest** under spec 009 / FR-401–403: no pwsh.exe spawn, no port binding, no shared temp. PR #256 / #259 lessons applied — verified by inspecting what the test class actually does, not by inferring from folder name.

**Non-blocking observations passed to #267 wiring (not to this PR):**
1. ModuleProbeResult has no Diagnostic field. When Get-Module terminates, warning is logged but caller doesn't see the message. Bender will likely synthesize generic diagnostics for the dominant Found=false case in BuildModuleImportsSection — fine. If real Get-Module terminating exceptions need operator-visible text, the lowest-friction follow-up is adding optional string? Diagnostic to ModuleProbeResult. Flag for #267.
2. Get-Module -ListAvailable returns highest-precedence match per PSModulePath; shadowed versions not reported. Matches "one call per module" contract; "which other versions exist?" is out of scope for spec 011.

**Patterns to remember:**
- For phase-split PRs, the right gate is "does Phase 2 need to reshape Phase 1's surface?" — not "is Phase 1 complete?" Phase 1 is allowed to be incomplete relative to the spec; it's NOT allowed to be reshaped by Phase 2. ModuleDiscovery passes that gate cleanly.
- Helper interface review: list the consumer's required fields (FR-263-2 modules[] fields here), map each to the helper's surface, and identify which fields the helper can't provide AND which of those the consumer must compute anyway. The unprovidable-but-derivable fields are not gaps; they're correct scope boundaries.
- self-approval block on usepowershell-authored PRs continues to be the constraint. Comment-form approval + artifact file is the established pattern. Don't chase a green review state via gh pr review --approve when the PR author IS the active gh account.
## 2026-05-15 — Spec 011 fully shipped

PRs #269 (Phase 1 ModuleDiscovery), #270 (Phase 2a DoctorService wiring), #271 (Phase 2b OOP wire-format parity) all merged to `main` on 2026-05-15. Issue #263 closed. #272 tracks per-tool source attribution refinement separately.

