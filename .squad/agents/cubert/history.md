# Cubert — History

## 2026-05-14 — PR #253 re-verify (TESTING.md)

- Fry pushed c12f26d addressing both findings.
- F1 fixed: dead pointer to PoshMcp.Tests/README.md replaced with inline 'default bucket = Integration' citing PoshMcp.Tests/AssemblyInfo.cs. Verified against squad/212-category-traits-baseline — AssemblyInfo policy block matches verbatim.
- F2 fixed: 'fast-fail logic' framing replaced with neutral 'Unit runs first ... ordering of remaining phases owned by .github/workflows/ci.yml'. Matches PR #252 actual order.
- Verdict flipped ⚠️ → ✅. Lockout protocol respected (Leela locked, Fry revised — correct since he owns the AssemblyInfo policy in #256).
- Lesson: when a doc cites another file, fetch that file at the cited ref before approving. AssemblyInfo on the right branch was the only way to confirm F1 was a real fix and not just a different incorrect citation.

### 2026-05-14: Fact-check — PR #257 (Amy: ci(009) flake-rate workflow) — APPROVE
- Pulled raw flake-rate.yml via gh api at ref squad/216-ci-flake-rate-measurement; re-validated with python yaml.safe_load (PARSED OK).
- Verified workflow_dispatch input runs default '5' string, schedule cron '0 7 * * *' (nightly UTC), 5 phases (Unit/Integration/OutOfProcess/Http/Functional, no Azure).
- Cross-checked phase filter syntax against PR #252's ci.yml diff: 'Category=X' strings match one-for-one.
- Aggregator confirmed: TRX walked via SelectNodes('//t:UnitTestResult', ) under TeamTest 2010 namespace; Failed/NotExecuted/Other split per test; sorted by total non-pass desc.
- Both artifacts present (flake-rate-summary, flake-runs-raw, both if: always()), GITHUB_STEP_SUMMARY mirror present. set +e + per-phase exit-codes.txt prevents iteration short-circuit.
- Workflow is ADDED file (345/0); ci.yml not touched. FR-418 satisfied. Comment posted: #issuecomment-4453761687.

### 2026-05-14 — PR #258 verification (Hermes, spec 009 / #219, TempDirectory helper)
- ✅ PROCEED (with one wait condition). All substantive claims verified against `gh pr diff 258`.
- Three real audit hits confirmed pre/post: `ResolveModulePaths_DeduplicatesCaseInsensitively` (Farnsworth's PR #256 flag — fixed `PoshMcp-ResolveModulePaths` name) + two `ProgramTests.ResolveConfigurationPath_*` cases (bare `Path.GetTempPath()` writes of `appsettings.json` / `config.json`).
- Three representative refactors confirmed: `ModuleDiscoveryStartupOrderingTests` (field `_tempDirectory` preserved as `=> _tempDir.Path` view to keep diff small), `OutOfProcessIntegrationTests` (`_testTempDirHolder` companion), `OutOfProcessPoolHostIntegrationTests` (3 inline pool tests).
- `OopTestPaths.cs` confirmed NOT in the 8-file diff. Intentional; documented in PR body.
- `TempDirectory.cs` (+115) and `TempDirectoryTests.cs` (+97, 8 `[Fact]` methods, `[Trait("Category","Unit")]`) verified. Helper contract: `Prefix = "poshmcp-test-"` + `Guid:N`, optional label, `_disposed` guard set BEFORE delete attempt (covers the throwing-delete idempotency case), `s_undeleted` audit bag, `AuditLeftoverDirectories()` cross-run sweep.
- Independent `Path.GetTempPath` audit across `PoshMcp.Tests/**`: NO newly discovered FR-403 violations. Remaining sites are already-unique GUID paths (eligible for the explicit follow-up sweep), `GetTempFileName()` (OS-unique), or read-only/synthesized paths.
- Wait condition: `CI / build` job (run 25878951951) and CodeQL `Analyze (csharp)` were still IN_PROGRESS at review time. `Squad CI / test` was already SUCCESS — the strongest signal for a test-only change.
- Build / test counts (`0 errors`, `64+29 pass`) marked ⚠️ author-attested — not independently re-run; consistent with green `Squad CI / test` but not granular enough to cite per-filter counts.
- Verdict: `artifacts/cubert-pr258-verdict.md` · Comment: https://github.com/usepowershell/PoshMcp/pull/258#issuecomment-4453769035

### 2026-05-14: Fact-check — PR #259 (Fry: reclassify misfiled Unit tests, Spec 009/#213) — APPROVE
- Diff is textbook FR-414: 8 file renames (similarity 98–99%), each one line edit (namespace PoshMcp.Tests.Unit.OutOfProcess → PoshMcp.Tests.OutOfProcess). Zero touches to `[Trait]`, `[Fact]`, asserts, setup, fixtures. Aggregate +8/-8 confirms scope.
- Verified all 8 listed files match the audit table 1:1. `git diff main --stat` on the worktree shows exactly the 8 `{Unit => }/OutOfProcess/` renames, nothing else.
- Directory hygiene: `PoshMcp.Tests/Unit/OutOfProcess/` is GONE on the PR branch (Test-Path returns false). Farnsworth's hygiene flag clears.
- Independently grepped retained Unit files for `Process.Start`, `ProcessStartInfo`, `TcpListener`, `HttpListener`, `"pwsh`, `BindAsync`, `Listen(`, `GetTempPath`: `OAuthProxyEndpointsTests`, `WinPsCompatProxyTests`, `ProgramCliBuildCommandTests`, `ServerSessionAwarePowerShellRunspaceTests` are all clean. `ProgramCliConfigCommandsTests` and `ProgramCliScaffoldCommandTests` (NOT named in PR body) hit `GetTempPath` — but inspected context: both wrap it in a nested `TemporaryDirectory` helper with `poshmcp-cli-tests-{Guid.NewGuid():N}` suffix. Matches the "safe pattern" Hermes documented in PR #258. Not a violation; candidate for the follow-up `TempDirectory` migration sweep.
- Reproduced the Unit tier metric: `dotnet test --filter "Category=Unit"` ran 432/0 in 20s test-only (29.7s wall). Author claimed 432/0 in 39s — count exact, timing well under FR-405 budget (60s).
- OOP tier metric (155/0/6 skipped) NOT independently re-run — too expensive in this verification window. CI `Squad CI / test` green on head SHA corroborates. Marked ⚠️ in verdict, not a blocker.
- Verdict: APPROVE, ready to merge. Posted to PR #259.
- Process learning: PR #256's "grep the file, never infer from folder" rule WORKED again here — Fry's PR body listed `ProgramCli*Tests.cs` collectively as compliant, but only named `ProgramCliBuildCommandTests`. The other two (Config, Scaffold) DO touch `GetTempPath`, just safely. Folder/PR-body claims are not a substitute for grep evidence on each file.

### 2026-05-14: Fact-check — PR #260 (Fry: FR-416 sweep of Functional folder, closes #220) — APPROVE

- All 6 Fry claims verified on branch `squad/220-functional-reclassify` @ `b430a9d` via the existing worktree at `C:\Users\stmuraws\source\github\usepowershell\poshmcp-220`.
- C1 metadata-only diff: confirmed via `gh pr diff 260` — 2 single-line Trait flips, 1 git mv + namespace flip on the moved file (StdioLoggingTests), +5 lines in TESTING.md. Zero body/assert/setup/data edits.
- C2 StdioLoggingTests OutOfProcess shape: confirmed lines 31, 35, 62, 67, 69, 70 use `InProcessMcpServer`/`ExternalMcpClient`/`StartAsync` — the OutOfProcess subprocess shape, strictly more specific than Integration.
- C3 ConfigurationReloadTests real I/O: confirmed `Path.GetTempFileName()` (line 68), `File.WriteAllTextAsync` (line 82), `File.Delete` (line 110).
- C4 SetupTests partial-class promotion: confirmed FOUR partials touch FS (ShouldParseJsonFileCorrectlyTest, ShouldThrowExceptionWithInvalidJsonTest, ShouldThrowExceptionWithMissingFileTest, ShouldWorkWithConfigurationToToolsListIntegrationTest), and the `[Trait]` lives on the shared declaration in `SetupTestsShared.cs`. Whole-partial promotion is correct per FR-416. Same pattern Fry used in #259.
- C5 borderline `ShouldHandleGetChildItemCorrectly`: confirmed `[Fact(Skip = "...")]` at line 20 and `Path.GetTempPath()` at line 52 is string-only (variable assignment, no file ops). Leaving Functional is correct.
- C6 TESTING.md: +5 lines accurately enumerate the 3 reclassifications and the in-process groups that remain Functional.
- C7 reproducibility: `dotnet test --filter "Category=Functional"` locally returned `Passed: 107, Skipped: 1, Total: 108, Duration: 4 s`. The 1 skip is the borderline. Cheap (~4s) and proves Functional tier stays green after the sweep.
- Did NOT re-run Unit tier (432/432) or the targeted 18/18 — Fry's PR description records them and they were visible in Steven's recent session terminal output. The clean Functional run + metadata-only diff give high confidence.
- Lesson — partial-class promotion is the right shape for FR-416 even when it over-classifies a few clean partials. Auditing every partial individually would re-introduce case-by-case judgment, which is exactly what FR-416 forbids.
- Lesson — when a worktree already exists for the PR, USE IT. Avoids `git checkout` on the main checkout, isolates the build artifacts, and keeps Steven's main checkout on `main`.
- Posted neutral verdict via `gh pr comment 260 --body-file artifacts/cubert-pr260-verdict.md`. Strict lockout NOT triggered (verdict is APPROVE).

## 2026-05-15: Team update (via Scribe)
**Ralph round 1 — 3 PRs in-flight, may need your review:**
- **PR #266** (Bender, issue #261): Doctor pool display sentinel — EffectiveProcessPoolSize / EffectiveMinHealthyForStartup promoted to `string`, returning `"n/a (<mode> mode)"` when inert. Files: `DoctorService.cs`, `DoctorReport.cs`, `DoctorTextRenderer.cs` + Unit tests.
- **PR #264** (Hermes, issue #262): AAD v2.0 `preferred_username` mapping — added `ClaimsMapping.NameClaim` to `AuthenticationConfiguration`; wires to `JwtBearerOptions.TokenValidationParameters.NameClaimType`. Null preserves default (no behavior change for existing deployments). Files: `AuthenticationConfiguration.cs`, `AuthenticationServiceExtensions.cs`, `docs/entra-id-auth-guide.md`.
- **PR #265 DRAFT** (Farnsworth, issue #263): Spec 011 design-only — `specs/011-doctor-module-imports/spec.md` (13 FRs / 4 SCs / 5 OQs). Implementation split to follow-up issues #267 (Bender) and #268 (Hermes).

### 2026-05-15: Fact-check — PR #269 (Hermes, Phase 1 of #268, ModuleDiscovery helper) — APPROVE

- All 8 claims verified on 25ee095 via the existing worktree at C:\Users\stmuraws\source\github\usepowershell\poshmcp-268.
- Reproduced: `dotnet test --filter "FullyQualifiedName~ModuleDiscoveryTests"` → 10/10 pass in 1.7947s. Per-test wall 13–442ms (cold cost concentrated in NonExistentModule first-runspace use).
- Build: `dotnet build PoshMcp.Server -c Debug --nologo /p:UseSharedCompilation=false` → 0 Error(s), 19 Warning(s). All warnings pre-existing in McpToolFactoryV2 / CommandHandlers / PowerShellAssemblyGenerator — none in new ModuleDiscovery.cs.
- FR-263-10 verified in source vs spec wording: helper at `ModuleDiscovery.cs:99-128` is `foreach name in moduleNames -> ps.AddCommand("Get-Module").AddParameter("Name", name).AddParameter("ListAvailable")`. Exactly one call per module, never per command. Spec at `specs/011-doctor-module-imports/spec.md` FR-263-10 reads `"a single Get-Module -ListAvailable -Name <name> call per module"` — matches verbatim.
- Runspace claim verified at `ModuleDiscovery.cs:90`: `runspace.ExecuteThreadSafe(ps => ...)`. Helper does NOT call `PSPowerShell.Create()`, `Process.Start`, or import. Interface at `IPowerShellRunspace.cs:27` confirms shape. The "same runspace as discovery" property is structurally enforced by accepting an interface (not constructing one) — actual wire-up to the discovery runspace happens in Phase 2 / #267.
- Record shape `ModuleProbeResult(Name, Found, Version, Path)` confirmed at `ModuleDiscovery.cs:36-40`. Path is documented as `ModuleBase` (the manifest directory).
- Branch base sanity: HEAD is one commit above `b04c07d (tag: v0.13.1, origin/main) release: v0.13.1`. ⚠ Annotated-tag trap: `git rev-parse v0.13.1` returns the tag-object SHA (15a5dc22), NOT the commit SHA (b04c07d). Read the log line to confirm, not raw rev-parse. Logging this as a methodology lesson — almost mis-flagged a clean lineage as divergent.
- Mergeable: `MERGEABLE` / mergeStateStatus `UNSTABLE` (build pending). CodeQL csharp/python/actions PASS, `test` 4s PASS. PR is DRAFT — flip to ready-for-review before merge.
- Approval: gh review --approve REJECTED with `"Can not approve your own pull request"` — PR owned by `usepowershell` identity. Verdict comment posted via `gh pr comment` is the substantive review record. Pattern: when PR is opened under the same shared identity Cubert uses, `gh pr review --approve` won't work; the verdict comment IS the review.
- Verdict: `artifacts/cubert-pr269-verdict.md` · Comment: https://github.com/usepowershell/PoshMcp/pull/269#issuecomment-4462728326
- Lesson: when PR claims "branch is on top of vX.Y.Z", verify via `git log --oneline` and look for the tag annotation in the log output. `git rev-parse <tag>` alone is misleading for annotated tags — use `git rev-parse <tag>^{commit}` for the actual commit SHA.
- Lesson: when reviewing a Phase-1-of-N split, separately validate (a) the helper's local contract, and (b) that the Phase 2 checklist truly operates on disjoint surfaces. Here Phase 2 touches RemoteToolSchema, oop-host*.ps1, OutOfProcessCommandExecutor, McpToolFactoryV2 in-process — none of those are touched in Phase 1's diff. Clean split.

## 2026-05-15 — PR #271 fact-check (PROCEED)

**Artifact:** `artifacts/cubert-pr271-verdict.md`  •  **Posted:** https://github.com/usepowershell/PoshMcp/pull/271#issuecomment-4463475205

Verified all 16 substantive claims for Hermes' Phase 2 OOP wire-format parity. Reproduction in worktree `poshmcp-268-p2`: build 0/19, Phase-2 sweep 10/10 pass, regression sweep 79/79 pass.

### Lessons

- **Spec docs may live only in the design PR commit, not on main.** `specs/011-doctor-module-imports/spec.md` is absent from `squad/268-oop-wire-format` and `origin/main` HEAD. Last touched in commit `bf4d763` (PR #263). Recovered FR text via `git show bf4d763:specs/011-doctor-module-imports/spec.md`. **Pattern:** when a spec is referenced but not on disk, `git log --all --oneline -- 'specs/<id>/**'` finds the design commit; `git show <sha>:<path>` recovers the blob without checkout.
- **AsyncLocal Reset → Set → Read trio is the safe shape for cross-disposal capture.** Spec 011 places the capture call (`OopModuleImportsCapture.Set`) inside the `await using` lease scope but after `DiscoverCommandsAsync` returns. The CLI flow then reads `OopModuleImportsCapture.Current` after the lease has disposed — works because AsyncLocal binds to the call context, not the disposed object. Pattern worth remembering: explicit `Reset()` at flow entry is what makes "current capture" semantically meaningful in long-running hosts.
- **PowerShell source priority is grep-verifiable.** Three loops + `ContainsKey` gate, in fixed execution order, encode the FR-263-9 priority (commandName > module > pattern) cheaply. No need to reason about flow — line numbers from `Select-String` prove it. Same pattern in both `oop-host.ps1` (lines 793/816/841) and `oop-host-pool.ps1` (lines 839/872/907). Use this approach for any "execution-order-encodes-policy" PowerShell.
- **Author count claims may be subsets of actual.** Hermes claimed "59 tests pass" in regression sweep; broader filter showed 79/79. Not a defect — author counted the suites they explicitly named, broader name-prefix match caught more. When verifying test-count claims, the question is "are any failing?" not "is the number exact?".


## 2026-05-15 — Spec 011 fully shipped

PRs #269 (Phase 1 ModuleDiscovery), #270 (Phase 2a DoctorService wiring), #271 (Phase 2b OOP wire-format parity) all merged to `main` on 2026-05-15. Issue #263 closed. #272 tracks per-tool source attribution refinement separately.


## 2026-05-16 — PR #273 (tutorial series, Leela)

- Verdict: proceed. 8 substantive code/contract claims + 8 surface/structural claims, all verified against source on squad/docs-tutorial-series. Comment posted (issuecomment-4467062499). Full verdict at artifacts/cubert-pr273-verdict.md.
- Key fact dropped to decision inbox: `RequiredRoles` is **any-match** (`AuthorizationHelpers.cs:25`: `requiredRoles.Any(r => user.IsInRole(r))`), while `RequiredScopes` is **all-match** (line 16). Asymmetric and intentional. Tutorial 4 documents this correctly with the right workaround.
- Verified Docker contract end-to-end: base `ghcr.io/usepowershell/poshmcp/poshmcp:latest` → runtime publishes to `/app/server` (Dockerfile:21,42) → entrypoint runs `/app/server/PoshMcp.dll` (docker-entrypoint.sh:8) → user images COPY to `/app/server/appsettings.json` (examples/Dockerfile.user:42). AllUsers PS module path `/usr/local/share/powershell/Modules` is the standard PowerShell-on-Linux location. `USER root` → COPY → `USER appuser` pattern is the canonical safe-COPY shape for the base image (appuser = UID 1001, sgid nologin).
- `CommandOverrides` is a `Dictionary<string, FunctionOverride>` with `StringComparer.OrdinalIgnoreCase` (PowerShellConfiguration.cs:113) — case-insensitive but NOT format-translating at config-binding time. The runtime `AuthorizationHelpers.GetToolOverride` does snake_case→PSName normalization, so auth checks tolerate either form, BUT `PowerShellAssemblyGenerator` (display properties) only queries by PSName. So the universally-correct canonical form for `CommandOverrides` keys is the PowerShell command name. Tutorials get this right.
- `FunctionOverride.{AllowAnonymous, RequiredScopes, RequiredRoles}` are all nullable (`bool?` / `List<string>?`). Null means "fall through to DefaultPolicy" (ToolAuthorizationFilter.cs:62-63, ToolListAuthorizationFilter.cs:71-72 use `override?.X ?? defaultPolicy.X`). Worth remembering: setting an override to `[]` (empty list, not null) means "policy says no roles required" — that's an explicit "allow anyone authenticated" override, NOT a fall-through. Subtle but important.
- Lesson: `CamelCaseToSnakeCase` lives in `PoshMcp.Server/PowerShell/PowerShellAssemblyGenerator.cs:74`. Use this file if a future tutorial needs to demonstrate non-obvious tool-naming (e.g., parameter set suffixes — line 109 appends `_` + snake_case(parameterSetName) for non-default param sets).
- Lesson: Doctor section headers are owned by `DoctorTextRenderer.cs` (not the JSON DoctorReport). If a tutorial cites a heading exactly, verify against the renderer, not the data shape.

## 2026-05-16 — PR #273 (tutorial series, Leela)

- Verdict: proceed. 8 substantive code/contract claims + 8 surface/structural claims, all verified against source on squad/docs-tutorial-series. Comment posted (issuecomment-4467062499). Full verdict at artifacts/cubert-pr273-verdict.md.
- Key fact dropped to decision inbox: `RequiredRoles` is **any-match** (`AuthorizationHelpers.cs:25`: `requiredRoles.Any(r => user.IsInRole(r))`), while `RequiredScopes` is **all-match** (line 16). Asymmetric and intentional. Tutorial 4 documents this correctly with the right workaround.
- Verified Docker contract end-to-end: base `ghcr.io/usepowershell/poshmcp/poshmcp:latest` → runtime publishes to `/app/server` (Dockerfile:21,42) → entrypoint runs `/app/server/PoshMcp.dll` (docker-entrypoint.sh:8) → user images COPY to `/app/server/appsettings.json` (examples/Dockerfile.user:42). AllUsers PS module path `/usr/local/share/powershell/Modules` is the standard PowerShell-on-Linux location. `USER root` → COPY → `USER appuser` pattern is the canonical safe-COPY shape for the base image (appuser = UID 1001, nologin shell).
- `CommandOverrides` is a `Dictionary<string, FunctionOverride>` with `StringComparer.OrdinalIgnoreCase` (PowerShellConfiguration.cs:113) — case-insensitive but NOT format-translating at config-binding time. The runtime `AuthorizationHelpers.GetToolOverride` does snake_case→PSName normalization, so auth checks tolerate either form, BUT `PowerShellAssemblyGenerator` (display properties) only queries by PSName. Universally-correct canonical form for `CommandOverrides` keys is the PowerShell command name. Tutorials get this right.
- `FunctionOverride.{AllowAnonymous, RequiredScopes, RequiredRoles}` are all nullable (`bool?` / `List<string>?`). Null means "fall through to DefaultPolicy" (ToolAuthorizationFilter.cs:62-63, ToolListAuthorizationFilter.cs:71-72 use `override?.X ?? defaultPolicy.X`). Worth remembering: setting an override to `[]` (empty list, not null) means "policy says no roles required" — an explicit "allow anyone authenticated" override, NOT a fall-through. Subtle but important.
- Lesson: `CamelCaseToSnakeCase` lives in `PoshMcp.Server/PowerShell/PowerShellAssemblyGenerator.cs:74`. If a future tutorial needs to demonstrate non-obvious tool-naming (parameter set suffixes — line 109 appends `_` + snake_case(parameterSetName) for non-default param sets), this is the file.
- Lesson: Doctor section headers are owned by `DoctorTextRenderer.cs` (not the JSON `DoctorReport`). When a tutorial cites a heading exactly, verify against the renderer, not the data shape.

