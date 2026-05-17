# Cubert — History

## Recent Work Index (2026-05-14 → 2026-05-17)

- **2026-05-17:** Cross-team update: log-forging fix #277 (Bender, PR #278) merged
- **2026-05-16:** PR #276 fact-check (import source tracker) — v1 REJECT (gap identified), v2 APPROVE (all tests pass)
- **2026-05-15:** Spec 011 fully shipped (Phase 1 + 2a/2b all merged)
- **2026-05-15:** Fact-check wave (PR #269, #271, spec 010 baseline) — all APPROVE/VERIFY
- **2026-05-14:** Spec 009 fact-check wave (6 PRs) — all APPROVE

**Note:** File size has reached 15KB threshold (15.6 KB). All entries are recent (within 90-day window); no archival needed yet.

---

## 2026-05-15 — Summarized (full text in history-archive.md, archived by Scribe 2026-05-16)

Five fact-check verdicts (all APPROVE/PROCEED):
- **PR #253 re-verify** (Leela TESTING.md, Fry revised): F1/F2 fixed; verdict ⚠️→✅. Lesson — when a doc cites another file, fetch it at the cited ref before approving.
- **PR #257** (Amy flake-rate workflow): added file 345/0; FR-418 satisfied; TRX parsing via TeamTest 2010 namespace.
- **PR #258** (Hermes TempDirectory helper, spec 009/#219): contract verified; no new FR-403 violations; pattern — `_disposed` guard set BEFORE delete for idempotency.
- **PR #259** (Fry reclassify Unit/OutOfProcess, #213): textbook FR-414 (8 renames, +8/-8); ProgramCli*Tests grep-clean (PR #256 rule applied again).
- **PR #260** (Fry FR-416 Functional sweep, #220): 4 partials in SetupTests promote together — partial-class trait scope is whole-class; reproducibility Functional=107/0 in 4s.

Standing lessons accumulated: (1) grep the file, never infer category from folder; (2) for partial classes, whole class promotes together; (3) when worktree already exists for a PR, USE IT; (4) `gh pr review --approve` rejected for usepowershell-authored PRs — comment-form verdict IS the review.

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

## 2026-05-16 — PR #273 merged (via Scribe)

PR #273 squash-merged to `main`. Polish pass by Leela addressed Farnsworth's 5 non-blocking asks; re-verified clean (`artifacts/cubert-pr273-reverify.md`). No factual drift introduced.

## Learnings

### 2026-05-16T17:46:13.559-05:00 — PR #276 import source tracker
- `BuildDoctorReportForCliAsync` is not the only doctor-report constructor. `BuildDoctorReportFromConfig` is also used by `McpToolSetupService` troubleshooting and `ConfigurationReloadTools.GetConfigurationStatus`; unless tracker data is threaded there too, those surfaces keep `moduleImports.tools[].source = "unknown"`.
- Verification pattern: when a PR enriches `DoctorReport`, grep every `BuildDoctorReportFromConfig(` and `BuildModuleImportsSection(` call site, not just the CLI doctor path.
- Validation split worth remembering: PR-local coverage (`DoctorToolImportSourceTests`, `ToolImportSourceTrackerTests`, `DoctorModuleImportsTests`, `DoctorModuleImportsOopPayloadTests`, `ToolImportParityTests`) can pass clean while full-solution `dotnet test .\PoshMcp.sln --nologo` still fails in unrelated integration smoke tests. Record both facts separately.

### 2026-05-16T18:30:48.077-05:00 — PR #276 re-review
- The runtime parity defect is fixed end-to-end: `DoctorService.BuildDoctorReportFromConfig(...)` now accepts `IToolImportSourceTracker` and both runtime callers thread the live tracker (`ConfigurationReloadTools.GetConfigurationStatus`, `McpToolSetupService.BuildConfigurationTroubleshootingJson`).
- The shared `ToolImportSourceTracker` is reused across setup/reload and explicitly reset at `McpToolFactoryV2.GetToolsListAsync` entry, so discovery cycles do not leak stale attributions.
- `ToolImportParityTests` validates CLI doctor parity across InProcess vs OutOfProcess, and `ToolImportRuntimeParityTests` validates CLI doctor parity against `get-configuration-status` and runtime troubleshooting on the relevant `moduleImports.tools[]` projections.
- Re-review pattern: for doctor/report attribution bugs, verify both constructor signatures and setup-time wiring in `McpToolSetupService`, not just `DoctorService` unit coverage.

## 2026-05-16 — Squad Scribe cross-pollinate (PR #276 multi-agent cycle)

**Fact-check on import tracker fix (issue #272):**
- v1 fact-check: identified tracker design sound but wiring incomplete (CLI path only, runtime surfaces still `unknown`). Recorded decision gate: all doctor builders must thread tracker, not just CLI.
- v2 fact-check: verified tracker now threaded through all report builders (`GetConfigurationStatus`, troubleshooting JSON, CLI doctor). All issue-272 refs updated. Confirmed parity tests cover commitments (CLI doctor vs `get_configuration_status` vs troubleshooting JSON emit consistent `tools[].source` values). Full suite 849/0/0 pass/fail/skip.

**Architectural lesson:** when doctor/report field is promoted from heuristic to authoritative, seam must be threaded through **all** production report builders. Otherwise runtime-facing surfaces degrade silently while CLI surface appears correct, breaking operator parity assumptions and teaching two different truths for same config.

**Process note:** User directive recorded (Steven request) — all squad agents must include their name when posting GitHub comments.

### 2026-05-17T08:12:00-05:00 — PR #278 log-forging fact-check
- Verdict: APPROVE. Verified all 24 open `cs/log-forging` alert locations on `main` are covered by the PR diff: 22 in `PowerShellAssemblyGenerator.cs`, 1 in `AuthenticationServiceExtensions.cs`, and 1 in `LoggerExtensions.cs`.
- `LogSanitizer.Scrub()` is a real CWE-117 mitigation for this codebase's line-oriented logs: it escapes CR/LF and other ASCII control characters into visible sequences, preserves content, and truncates oversized values.
- Right-parameter check: the auth changes scrub genuinely untrusted request/token/config-derived values (path, claims, audiences, issuers, challenge fields), and `LoggerExtensions` now scrubs header-derived `CorrelationId` before it enters the logging scope.
- False-positive note: the 22 `PowerShellAssemblyGenerator` alerts are likely CodeQL false positives because `invocationId` is generated by `OperationContext.BeginOperation(commandName)` in this flow, not supplied by the caller; scrubbing it is harmless and closes the findings conservatively.
- Issue #277's file-count table is off by one: GitHub currently reports 22 `PowerShellAssemblyGenerator` locations, not 21.


## 2026-05-17T13:12:00Z: Cross-team update — Log-forging fix #277

Bender completed remediation of 24 CodeQL cs/log-forging alerts across PowerShellAssemblyGenerator.cs, AuthenticationServiceExtensions.cs, and LoggerExtensions.cs. Pattern: LogSanitizer.Scrub() applied to all untrusted sources (correlation IDs, JWT claims, config values) at structured log call sites. Build + tests pass. PR #278 open.
## 2026-05-16 — v0.14.1 Release (via Scribe)

Release v0.14.1 shipped successfully. Version bump, release notes, and GitHub release creation completed by Amy. Commit a2a89b3, tag v0.14.1 pushed to origin, release published.

