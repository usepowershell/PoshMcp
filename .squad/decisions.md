# Decisions

## Recent Decisions
> Older entries archived to `decisions-archive.md` (entries >7d removed when file >= 50KB).

### 2026-05-16: Tutorial series location (PR #273)
**By:** Leela (Developer Advocate), requested by Steven
**What:** New `docs/articles/tutorials/` subdirectory hosts the 4-part progressive tutorial series. Series indexed at `tutorials/index.md`; navigation added under a new "Tutorials" section in `docs/toc.yml` between Getting Started and the User Guide. No `docfx.json` change needed — `articles/**/*.md` glob auto-picks-up the subdirectory.
**Why:** Keeps tutorials grouped and easy to expand later (next series, e.g. OAuth/Entra ID end-to-end, can drop into the same folder). Sentence-case heading "Tutorials" matches the rest of the toc.

---

### 2026-05-16: Doc tutorials should demonstrate `poshmcp doctor` after setup steps
**By:** Farnsworth (Lead/Architect) — captured during PR #273 review
**What:** Tutorials, getting-started guides, and any prose docs that walk a reader through a working-config setup should include a `poshmcp doctor` (or `docker exec <container> poshmcp doctor`) verification step. Specifically: any tutorial that exercises `PowerShellConfiguration.Modules` or `IncludePatterns` should call out the `moduleImports` section of the doctor report (shipped in v0.14.0 / spec 011) as the canonical "did this actually work?" inspection.
**Why:** Doctor is the only operator-facing surface that resolves config-as-written → effective behavior. Without it, tutorial readers learn the config shape but not the verification idiom — and when their config silently misbehaves (typo in module name, pattern that matches zero commands), they have no muscle memory for where to look. The new `moduleImports` section was built precisely to close this gap; doc series should reflect it.
**Scope:** Applies to future doc PRs touching tutorials, getting-started, configuration-walkthrough articles. Reviewers (Farnsworth, Cubert) should flag missing doctor-verification steps in such PRs. Does NOT apply to API reference docs or pure conceptual articles.
**Pair pattern:** Tutorial 4 in PR #273 already follows this pattern for the Authentication doctor section (`docker exec poshmcp-roles poshmcp doctor` → inspect Authentication section). Tutorials 2 and 3 should adopt the same pattern for the new `moduleImports` section.

---

### 2026-05-16: `RequiredRoles` is any-match; `RequiredScopes` is all-match (asymmetric)
**By:** Cubert — verifying PR #273 tutorial 4
**What:** `AuthorizationHelpers.HasRequiredRoles` uses `requiredRoles.Any(r => user.IsInRole(r))` — a caller satisfies the policy if they hold **any one** of the required roles. In contrast, `HasRequiredScopes` uses `requiredScopes.All(...)` — caller must hold **all** required scopes.
**Why:** This asymmetry is intentional but easy to miss. Tutorial 4 documents the any-match behavior and recommends "split into multiple overrides" if all-of-N role semantics are needed. Future tutorials, examples, and reviewers should preserve this distinction explicitly when explaining authorization config.
**Citation:** `PoshMcp.Server/Authentication/AuthorizationHelpers.cs:11-25`
**Scope:** Team-wide. Affects any docs, examples, or features that talk about `RequiredRoles`/`RequiredScopes` semantics.

---

### 2026-05-15: Spec 011 Phase 2 — OOP wire-format parity for moduleImports (PR #271, closes #268)
**By:** Hermes (PowerShell Expert) — requested by Steven via Squad Coordinator
**What:** Phase 2 extends the OOP discover wire format so the C# server can build the doctor `moduleImports` section from data the OOP host produced — achieving SC-263-3 (byte-identical JSON across `InProcess` and `OutOfProcess` modes) without re-running `Get-Module -ListAvailable` in the parent process. Additive wire fields: `RemoteToolSchema.SourceModule` / `SourcePattern` / `SourceDetail` (nullable strings, FR-263-9), plus a new top-level optional `RemoteModuleImportsPayload` on the discover response carrying per-module probe data and per-pattern match data. `oop-host.ps1` and `oop-host-pool.ps1` populate both. The pool wraps script-block return as `PSCustomObject {Schemas, ModuleImports}` with a defensive bare-array fallback.
**C# consumer chain:** `ICommandExecutor.LastModuleImports` (default null), `OutOfProcessCommandExecutor` + `OutOfProcessSubprocessPool` parse + stash + expose (pool variant under `_envLock`, cleared on fingerprint mismatch). `McpToolSetupService.DiscoverToolsAsync` captures via new `OopModuleImportsCapture` `AsyncLocal<RemoteModuleImportsPayload?>` — Reset before discovery, Set after, both BEFORE the executor lease disposes. `DoctorService.BuildModuleImportsSection` gains a 4-arg payload-aware overload (skips `IsolatedPowerShellRunspace` + `ModuleDiscovery.ProbeModules` when payload is non-null); 3-arg overload delegates with `payload: null`. `DoctorService.BuildDoctorReportForCliAsync` emits a one-time `DoctorReport.Warnings` entry when `RuntimeMode == OutOfProcess && hasModuleOrPatternConfig && OopModuleImportsCapture.Current is null` — older-host fallback contract per FR-263-2 / FR-263-10.
**Backward compatibility:** Older OOP hosts → C# parses without error, `LastModuleImports` stays null, schemas have null `Source*` fields, doctor falls back to in-process probe + heuristic with one-time warning. Newer OOP host with older C# → unknown `moduleImports` field ignored by Newtonsoft.
**Per-tool attribution:** Consolidated `ModuleImportsSection.Tools[]` still uses Bender's single-module heuristic from #270; full per-tool attribution (analogous to spec 010's `IToolDescriptionSourceTracker`) is deferred to a follow-up issue.
**Tests:** `DoctorModuleImportsOopPayloadTests` (4), `RemoteToolSchemaSourceFieldsTests` (5), `OutOfProcessIntegrationTests.DiscoverCommandsAsync_PopulatesLastModuleImports_FromOopHostPayload` (real OOP host, all three FR-263-9 sources). 59 local tests pass; build clean.

---

### 2026-05-15: AsyncLocal as a cross-disposal capture pattern
**By:** Farnsworth (Lead/Architect) — captured during PR #271 review
**What:** When a value is produced on one side of a `using`/`await using` disposal boundary and consumed on the other side within the same one-shot async flow (CLI-shaped code paths), prefer a static `AsyncLocal<T?>` capture over refactoring a public-ish return shape — provided three things hold: (1) the capture is `Reset()` at the start of the producer flow so stale-from-prior-flow values cannot leak; (2) the `Set()` runs BEFORE the disposal boundary closes (producer is still alive at capture time); (3) the lifecycle contract — Reset-at-start + Set-before-disposal + Current-at-read, single async flow — is documented in XML on the capture class.
**Why:** AsyncLocal flows through `await` boundaries within an ExecutionContext, so the consumer reads the captured value correctly without changing the public surface. CLI invocations are one-shot per process invocation, so cross-flow contamination is structurally impossible. The alternative (refactoring `DiscoverToolsAsync` return shape) has a larger blast radius across stdio + HTTP entry points.
**Reference implementation:** `PoshMcp.Server/PowerShell/OutOfProcess/OopModuleImportsCapture.cs` (PR #271). The combination Hermes used is the safe shape.
**When NOT to use:** Long-running multi-flow scenarios (web servers handling concurrent requests where producer and consumer are not in the same async flow), or when the value MUST cross a process boundary (use the response payload).
**Apply to:** Future Phase-3+ doctor-section enrichment that sits across the same lease-disposal boundary. Any other CLI-flow-shaped code where a producer is disposed before the consumer needs its output.

---

### 2026-05-15: Spec 011 implementation split — Phase 1 ships standalone (PR #269), Phase 2 follows (PR #271)
**By:** Hermes (PowerShell Engineer), per Steven's directive in issue #268 body
**What:** Issue #268 implementation split into two PRs per the explicit allowance in the issue body. Phase 1 (PR #269 — MERGED) shipped only the in-process `ModuleDiscovery` helper + 10 unit tests with `ModuleProbeResult(Name, Found, Version, Path)` shape. Phase 2 (PR #271 — MERGED) shipped the OOP wire-format parity work as a single focused change. Bender's #270 (single-module attribution heuristic in `BuildModuleImportsSection`) consumed Phase 1 directly.
**Why:** Phase 1 was small, self-contained, immediately useful — unblocked Bender's #267 doctor `moduleImports` section. Phase 2 touched `RemoteToolSchema.cs`, both OOP host scripts, `OutOfProcessCommandExecutor`, `McpToolFactoryV2` parity, parity tests, and older-host fallback test in lockstep — better as a focused review.
**Outcome:** All 13 FRs from spec 011 delivered across #269 (Phase 1), #270 (Phase 2a — C# wiring), #271 (Phase 2b — OOP wire-format parity). Issue #263 closed; per-tool source attribution refinement deferred to issue #272.

---

### 2026-05-15: PR #269 (Phase 1 ModuleDiscovery helper) — APPROVED
**By:** Farnsworth (architectural review) + Cubert (verification) — both 2026-05-15
**Verdict:** ✅ APPROVED. `ModuleProbeResult(string Name, bool Found, string? Version, string? Path)` accepted as the in-process probe contract for spec 011.
**Verifications (Cubert):** 10/10 ModuleDiscoveryTests pass in 1.79s; build clean (0 errors, 19 pre-existing warnings); FR-263-10 ("one Get-Module per module, never per command") matches implementation verbatim — single `AddCommand("Get-Module")` per name in foreach inside `runspace.ExecuteThreadSafe`; helper accepts `IPowerShellRunspace` interface, never spawns `pwsh` (FR-263-11 satisfied); branch on top of `b04c07d` = `v0.13.1`.
**Architectural sign-off (Farnsworth):** Four-field surface maps exactly to FR-263-2's `name / found / version / path`. Tool attribution and source-priority resolution (FR-263-8/9) are `CommandInfo`-driven and live correctly in `BuildModuleImportsSection`, not in the probe helper. Phase split confirmed sound — Phase 2 work operates on surfaces NOT touched by this PR.
**Non-blocking flag for #267 wiring:** `ModuleProbeResult` exposes no `Diagnostic` field — terminating `Get-Module` exceptions are logged but not surfaced. Lowest-friction follow-up if needed: optional `string? Diagnostic`. Not a Phase 1 blocker.
**Self-approval block:** `gh pr review --approve` rejected for both reviewers (PR opened under same `usepowershell` identity). Comment-form approval is the team accepted pattern; verdicts stand. Cubert: https://github.com/usepowershell/PoshMcp/pull/269#issuecomment-4462728326. Farnsworth: https://github.com/usepowershell/PoshMcp/pull/269#issuecomment-4462727166.

---

### 2026-05-15: Spec 011 single-module attribution heuristic in DoctorService (PR #270, #267)
**By:** Bender (Backend Developer)
**What:** Per-tool module attribution in `DoctorService.BuildModuleImportsSection` uses a heuristic, not a precise mapping. (1) **Exact:** `commandName` source — match against `config.CommandNames`. (2) **Heuristic — single module:** if `config.Modules.Count == 1`, attribute all non-`commandName` tools to that module. (3) **Heuristic — multi module:** non-`commandName` tools fall back to `source: "unknown"` with no `sourceDetail`. (4) **Fallback:** pattern source — first `IncludePatterns` entry that matches the tool's `Title` (PowerShell command name).
**Why:** Issue #267 explicitly permits this per FR-263-11. Clean precise fix is a wire-format extension threading `sourceModule` (and ideally `sourceCommandName`) through `RemoteToolSchema` and `PowerShellCommandMetadata` — that landed as Phase 2 (PR #271), but per-tool exact attribution is deferred to a follow-up issue (#272).
**Trade-off:** Ships today with 80% diagnostic value. Single-module configs (Az.* / Microsoft.Graph.* common case) get exact attribution. Multi-module configs see `unknown` for tools from `Modules` rather than `CommandNames` — operators can't tell which module a given tool came from at a glance. Documented in PR body, in-code XML comments, and this decision drop.
**Affects:** `DoctorService.BuildModuleImportsSection` (pure overload, multi-module branch); `ToolImportEntry.Source` / `ToolImportEntry.SourceDetail` semantics.

---

### 2026-05-15: Doctor "effective" sizing fields use string sentinels for inert knobs
**By:** Bender (PR #266, issue #261) — requested by Steven
**What:** When a doctor-report "effective sizing" field corresponds to a knob that is **inert in the current host mode**, render it as a sentinel string (`"n/a (Pool mode)"`) rather than `0` or another integer. Apply this consistently across `OutOfProcessSection` effective fields. The field type on the report record is `string`, not `int` or a discriminated union.
**Why:** `0` for inert knobs reads as a bug to operators (configured-vs-effective drift looks broken). The pattern was already established by `EffectiveRunspacePoolSize` (string, returns `"n/a"` outside Pool mode); promoting `EffectiveProcessPoolSize` and `EffectiveMinHealthyForStartup` from `int` to `string` aligns the surface. Keeps the doctor JSON self-explanatory without needing a separate "applicable" boolean per knob.
**Scope:** Applies to "effective" fields on `OutOfProcessSection` and any future host-mode-conditional knobs surfaced in the doctor report. Does **not** apply to "configured" fields — those remain typed (`int`, `bool`) since they reflect the literal config value.
**Rendering:** Active mode → `"<integer>"` (e.g., `"4"`, `"1"`). Inert mode → `"n/a (<active mode> mode)"` (e.g., `"n/a (Pool mode)"`).
**Out of scope:** Not refactoring the report record into a discriminated union. String sentinels are sufficient and keep the JSON contract flat.
**Files:** `PoshMcp.Server/Diagnostics/DoctorReport.cs`, `PoshMcp.Server/Diagnostics/DoctorService.cs`, `PoshMcp.Tests/Unit/Diagnostics/DoctorOutOfProcessSectionTests.cs`.

---

### 2026-05-15T14:00:00Z: ClaimsMapping.NameClaim drives Identity.Name
**By:** Hermes (for Steven, issue #262, PR #264)
**What:** Added per-scheme `ClaimsMapping.NameClaim` (nullable string) to `AuthenticationConfiguration`. When set, `AuthenticationServiceExtensions` writes it to `JwtBearerOptions.TokenValidationParameters.NameClaimType`. Null/empty preserves the JwtBearer default (`http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name`) — no behavior change for existing deployments.
**Why:** AAD v2.0 access tokens carry `preferred_username` rather than `name`. Without this knob, `Identity.Name` and the doctor report's `identity.name` were silently null in v2.0 deployments. Operators now set `"NameClaim": "preferred_username"` (documented in `entra-id-auth-guide.md`) and `principal.Identity?.Name` resolves correctly. `DoctorReport.cs` needed no change — it reads through the framework primitive.

---

### 2026-05-15: Spec 011 ships design-only; module-imports validation deferred to #267/#268
**By:** Farnsworth (for Steven)
**What:** Issue #263 (doctor: module-imported tools have no validation/visibility section) is being addressed via design-only spec 011 (`specs/011-doctor-module-imports/spec.md`, PR #265). Implementation split into two follow-up issues: #267 (Bender — C#/JSON wiring + tests) and #268 (Hermes — PowerShell module discovery + OOP wire-format parity). The design intentionally flips `summary.status` from `healthy` to `errors` for configurations with broken `Modules` (e.g., misnamed module names) — this is the bug fix, requires CHANGELOG entry under "Breaking — diagnostics" when implementation lands.
**Why:** Discovery surface spans three sources (CommandNames/Modules/IncludePatterns) with three filter modes plus OOP byte-parity (inherited from spec 010); a single implementation PR would be too wide. Splitting along agent expertise (C# vs PowerShell) keeps each follow-up reviewable. The `IncludePatterns: ["*"]` filter-vs-discovery surprise (McpToolFactoryV2.cs L1022) is deliberately out of scope — spec surfaces current behavior, separate ergonomics issue can change it.

---

### 2026-05-14: User directive — gh CLI auth check before use
**By:** Steven (via Copilot)
**What:** Before starting to use the `gh` CLI in this project, ALWAYS check authentication status first. This project requires the `usepowershell` identity. Use `gh auth status` to verify, and `gh auth switch` if a different identity is currently active.
**Why:** This is a multi-account environment; the wrong identity can cause silent failures (e.g., EMU policy blocks like the `gh pr comment` Unauthorized error during the v0.13.0 release) or, worse, write to the wrong account. Captured for team memory.
**Applies to:** All agents (Amy, Bender, Hermes, Farnsworth, Fry, Leela, Cubert, Ralph) and the Coordinator before any `gh` invocation.

---

### 2026-05-14: Spec 009 unit-tier acceptance gate (Issue #221) — PASSED
**By:** Fry (Tester) — requested by Steven via Ralph
**What:** Measured 5 consecutive `dotnet test --filter Category=Unit --no-build` runs on FR-419 reference machine (commit 629486a, post-#216). All 5 clean: 432 passed / 0 failed / 0 skipped, wall-clock 20.07–21.08s (mean 20.45s), 0 flake re-runs. SC-100, SC-101, FR-404, FR-405, FR-419 satisfied. Issue #221 closed as completed.
**Why:** Closing acceptance gate for Spec 009 (Test Suite Consistency) — all blockers (#213/#214/#215/#216/#217/#218/#219) merged; gate run validates the Unit tier meets the <60s, 0-flake budget on the maintainer reference machine.

---

### 2026-05-14: Amy — Spec 009 / #216 flake-rate measurement workflow
**By:** Amy (DevOps / Platform / Azure)
**What:** Spec 009 FR-418 (and supporting FR-405 / SC-105) implemented as a separate workflow at `.github/workflows/flake-rate.yml` (NOT additional steps in `ci.yml`). Re-runs the phased suite N times (default 5, configurable via `workflow_dispatch` input `runs`), aggregates per-test failure counts, and emits a single markdown summary (`flake-rate-summary.md`) uploaded as artifact and mirrored to `$GITHUB_STEP_SUMMARY`. Triggers: `workflow_dispatch` + `schedule '0 7 * * *'` (nightly UTC). Phasing mirrors PR #252 one-for-one (Unit → Integration → OutOfProcess → Http → Functional). Azure phase intentionally excluded (no creds in CI per spec 009 Non-Goals).
**Why:** FR-418 demands flake measurement that does not gate normal CI. A separate workflow keeps `ci.yml` lean and lets maintainers crank N up to 20+ on demand. Aggregator is PowerShell + `Select-Xml` with explicit `XmlNamespaceManager` (TRX has a default namespace; dotted access silently returns nothing). Loop uses `set +e` and per-iteration exit-code text files so a phase failure in iteration 3 does NOT skip 4 and 5.
**Aggregate flake-rate definition:** `total non-pass instances / total test invocations across all iterations` — repeated failure of the same test in different iterations counts as separate flake instances.
**Artifacts:** `flake-rate-summary` (the headline `.md`) + `flake-runs-raw` (full TRX tree). Both `if: always()`.
**Cross-PR:** TESTING.md (PR #253) should add a "Flake-rate measurement" pointer linking to the workflow run page → Artifacts → `flake-rate-summary`. Whoever merges #253 (or follow-up) owns that pointer.

---

### 2026-05-14: Cubert REQUEST CHANGES on PR #253 — broken cross-reference
**By:** Cubert (Reviewer/Fact-checker) — review of PR #253 (Leela, `docs(009): add TESTING.md`)
**Verdict:** ⚠️ REQUEST CHANGES (one blocking F1, one non-blocking F2).
**F1 BLOCKER:** TESTING.md punted on naming the default bucket: "set by issue #212 and documented in `PoshMcp.Tests/README.md`". Both halves stale — (a) bucket IS already named `Integration` per Fry's `AssemblyInfo.cs:8-25` policy block on `squad/212-category-traits-baseline`; (b) `PoshMcp.Tests/README.md` at HEAD only documents legacy folder convention, no trait policy. For external-facing docs that's a broken cross-reference, not a stylistic choice. Fix: inline the answer (quote `AssemblyInfo.cs:8-25`).
**F2 NON-BLOCKER:** "Phase order in CI follows fast-fail logic: cheaper, more deterministic phases run first" overstates. Actual #252 order is Unit → Integration → OutOfProcess → Http → Functional → Azure; Http (1-2 min) and Functional (<1 min) run AFTER OutOfProcess (3-6 min). Hedge two sentences later softens this; rewrite recommended not required.
**Lockout:** Leela cannot self-revise. Recommended Fry — owns the AssemblyInfo policy block.
**Patterns to remember:**
- For external-facing contributor docs, a "see X for the answer" pointer where X doesn't yet contain the answer is a defect, not a stub. When the answer IS already known and committed elsewhere, inline it.
- Wave-of-PRs reviews need cross-PR fact-checking. PR #253 referenced bucket-naming work in PR #256 and CI work in PR #252; verifying required pulling all three.
- xUnit `--filter "Category=X"` matches `[Trait("Category", "X")]` exactly, no shell quoting subtleties on Windows pwsh or bash.
- Always pass `-R usepowershell/PoshMcp` to `gh pr view` in this repo (the smurawski/poshmcp redirect causes silent ID mismatches without it).

---

### 2026-05-14: Reject PR #256 — reassign to Bender (NOT Fry)
**By:** Farnsworth (requested by Steven)
**What:** PR #256 (Fry — class-level Category traits, branch `squad/212-category-traits-baseline`) rejected. Class-level `[Trait("Category", "Unit")]` applied to `Unit/OutOfProcess/OutOfProcessCancellationTests`, `OutOfProcessHostConcurrencyTests`, `OutOfProcessCommandExecutorTests` — these classes spawn `pwsh` (FR-401), bind `HttpListener` on 127.0.0.1 (FR-402), and write to shared temp dir `Path.Combine(Path.GetTempPath(), "PoshMcp-ResolveModulePaths")` (FR-403). Spec 009 edge-case section explicitly names `Unit/OutOfProcess/*` as the case to NOT mislabel. Class-level traits aggregate — even classes with some pure validation methods cannot wear `Unit` if any sibling method violates FR-401/402/403.
**Why:** Honest-tagging promise; SC-103 (zero pwsh / zero ports / zero shared temp in unit tier) cannot be satisfied at filter time if these classes carry the `Unit` tag.
**Reassign to:** Bender (owns OOP wave from Spec 004 — Pool / cancellation production code; immune to the folder-name-as-category trap that produced this miss). NOT Fry (reviewer-rejection lockout).
**Scope of revision:** Re-tag the three confirmed classes (and any other `Unit/OutOfProcess/*` that fails the audit) as `OutOfProcess`. Spot-check `Unit/ProgramCli*`. Update `scripts/add-category-traits.ps1` category map. Re-run FR-415 deterministic count check. Metadata-only — no test logic changes.

---

### 2026-05-14: Partial-class trait promotion under FR-416
**By:** Farnsworth (Lead/Architect) — surfaced during PR #260 review (closes #220, spec 009 closing PR).
**What:** When `[Trait("Category", ...)]` lives on a single declaration of a `partial class` and any partial in that class touches external resources (disk, network, subprocess, port), the **entire partial class promotes together** to the more specific category (Integration, OutOfProcess, or Http). Trait flips on the shared declaration; per-partial trait overrides are not used.
**Rationale:** FR-414 requires reclassification PRs to be metadata-only — no `[Fact]` body, `Assert.*`, ctor/setup, or fixture changes. The only metadata-only way to apply FR-416 to a `partial class` is to flip the trait on the shared declaration (which xUnit applies class-wide). Alternatives both violate FR-414: (a) splitting the partial class into two separate classes is a structural refactor; (b) per-method `[Trait]` overrides on partials rely on flaky xUnit semantics and produce mixed-category test files that defeat the point of FR-416 being a class-level rule.
**Known cost:** Some partials that do not themselves touch external resources will be over-classified (e.g. in `SetupTests`, six of the ten partials are pure-functional but inherit Integration). Acceptable. If surgical separation is wanted later, that's a follow-up structural refactor, not part of an FR-416 metadata sweep.
**Applies to:** Any future FR-416 sweep encountering a `partial class` test fixture. Do not split partials in a metadata-only PR. Promote the whole class together and call out over-classified partials in the PR body so reviewers can confirm the trade-off was deliberate.

---

### 2026-05-13: Accept JsonConverter + TransformSchemaNode pattern as the standard workaround for MCP SDK reflection-binding gaps
**By:** Farnsworth (review of PR #222 by youyuanwu, requested by Steven)
**What:** When a CLR type cannot be bound by the MCP SDK's default System.Text.Json reflection (e.g. `SwitchParameter` — struct with getter-only `IsPresent`), the accepted fix is:
1. A dedicated `JsonConverter<T>` in `PoshMcp.Server/PowerShell/`.
2. A small static support class exposing shared, frozen `JsonSerializerOptions` and `AIJsonSchemaCreateOptions` (with `TransformSchemaNode` rewriting the bad node to a permissive `anyOf`).
3. Wire both through `McpServerToolCreateOptions.SerializerOptions` / `SchemaCreateOptions` in `McpToolFactoryV2.CreateMcpToolOptions`.
**Why:** Single chokepoint, no per-parameter detection needed, schema stays honest about what the converter actually accepts. PR #222 establishes the template.
**Follow-up:** Track whether globally replacing the SDK's default `SerializerOptions` (instead of cloning + extending) introduces response-serialization regressions for tools that emit explicit nulls — `DefaultIgnoreCondition = WhenWritingNull` now applies to every tool.

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

### 2026-05-12: Farnsworth — Spec 010 drafted

**By:** Farnsworth (Lead / Architect)
**Requested by:** Steven Murawski

**What:** Authored `specs/010-tool-self-documentation/spec.md` — "Improve MCP Tool Self-Documentation from PowerShell Help/Metadata." Status: Draft, awaiting Brady's review (Cubert pre-review per the 2026-05-05 directive applies).

**Co-authored with Hermes** — his 2026-05-12 research entry in `.squad/agents/hermes/history.md` established the technical baseline (two-path divergence: in-process never calls Get-Help, OOP reads only Synopsis; both paths use literal `"Parameter of type X"` for every parameter description; misleading XML doc on `RemoteToolSchema.Description`).

**Scope (per Brady's clarification):** What `Get-Help`/`Get-Command`/`CommandInfo`/`ParameterMetadata` already expose, since the platform has normalized whatever help mechanism the author chose. NOT about comment-based vs MAML vs XML authoring conventions.

**Headline recommendation:** Option A — implement a shared sourcing function that reads `Get-Help` in both paths, with documented precedence chains for tool descriptions (Synopsis → Description body → syntax line → command name) and parameter descriptions (Get-Help param description → `ParameterAttribute.HelpMessage` → `ValidateSet` hint → `"Parameter of type X"` fallback). Mandates byte-identical output across in-process and OOP modes (FR-520) verified by automated test (FR-521). Includes alias exposure (FR-530/531), sanitization + length caps (FR-540..542), FR-571 caching keyed by the same setup-hash already used for OOP discovery, and FR-572 cold-start regression gate via `PoshMcp.Benchmarks`. Option D (`[PoshMcp.ToolDescription]` attribute) explicitly deferred as an opt-in follow-up.

**Open Questions** left for Brady to resolve before Accepted: alias placement, length cap defaults, MamlParaText join style, cache invalidation across runspace recycling (coordinates with spec 004), doctor field shape (coordinates with spec 006), ValidateSet description phrasing, fallback-frequency telemetry.

**Why:** Authors write PowerShell help and reasonably expect MCP clients to see it. Today's two-path divergence means the same command exposes structurally different descriptions depending on a flag (`RuntimeMode`) the author cannot see. The headline gap is real and the platform-normalized data sources to close it already exist.



### 2026-05-12: Cubert pre-review of spec 010 — APPROVE WITH CHANGES
**By:** Cubert (Fact Checker)
**Requested by:** Steven Murawski (Brady)
**Artifact:** specs/010-tool-self-documentation/spec.md (Status: Draft)
**Author:** Farnsworth (locked out from self-revision per strict-lockout rule)

**Verdict:** APPROVE WITH CHANGES. Five required changes; spec cannot promote to Accepted until they land. Recommended revision agent: Hermes (technical co-author with the original grounding research, not the original drafter).

**Verification report:**

Citations all check out (✅):
- `PoshMcp.Server/McpToolFactoryV2.cs#L123-L145` — `SetParameterSetDescription` body matches spec quote verbatim. Get-Help is never called.
- `PoshMcp.Server/McpToolFactoryV2.cs#L442` — `Description = string.IsNullOrWhiteSpace(schema.Description) ? schema.Name : schema.Description` confirmed.
- `PoshMcp.Server/PowerShell/PowerShellSchemaGenerator.cs#L98` — `schema["description"] = $"Parameter of type {parameterType.Name}";` confirmed.
- `PoshMcp.Server/PowerShell/OutOfProcess/oop-host.ps1#L763-L771` — Synopsis-only read with `-ne $cmd.Name` guard confirmed.
- `PoshMcp.Server/PowerShell/OutOfProcess/oop-host-pool.ps1#L824-L832` — same logic, inline form, confirmed.
- `PoshMcp.Server/PowerShell/OutOfProcess/RemoteToolSchema.cs#L17` — XML doc text matches spec quote ("from Get-Help or parameter set syntax") and is genuinely misleading (long Description never used; OOP fallback is empty string, not syntax).

Scope discipline (✅): No FR drifts into authoring formats. Non-Goals explicitly disclaims comment-based vs MAML vs XML, matches Brady's scoping directive.

Format conformance (✅): Layout matches spec 009 exactly — Title block, Background, User Scenarios with P1/P2/P3, Edge Cases, Functional Requirements (grouped sub-sections), Approach Options, Recommendation, Open Questions, Non-Goals, Success Criteria.

**Required changes (must fix before Accepted):**

1. **FR-521 (parity test) is hand-wavy.** "Verified by an automated test that runs the same configured command through both paths and asserts equality of the resulting MCP `tools/list` responses for description fields." Doesn't say which test project (PoshMcp.Tests, presumably), doesn't name the test class or pattern, doesn't say what the equality primitive is (string-equal per field? full JSON tree? scoped to `description` only?), doesn't say what command(s) constitute the parity corpus, doesn't say how flaky-test risk is bounded if Get-Help cold-loads MAML mid-test. A reviewer can't tell if the test is implementable in 50 lines or 500. Specify: test project, naming pattern, equality scope, fixture command set (suggested: a small in-tree test module with deterministic help), and whether the test runs in both InProcess and OOP modes within a single test session.

2. **FR-550 (no description regression) has no measurement strategy.** "No tool currently producing a useful description MUST regress to a less useful one" — but "useful" is undefined and "regression" has no detection mechanism. Snapshot test against a reference module? Manual operator opt-in? Diff against baseline captured pre-change? Without a mechanism this FR cannot be verified, and "no regression" claims at release time will be unsupported. Either: (a) add a snapshot test that captures pre-change descriptions for a fixed module set and asserts post-change descriptions are equal-or-longer for non-empty originals, OR (b) tighten the FR to a verifiable property (e.g., "every command whose pre-change description is a non-empty Synopsis MUST surface that exact synopsis or a strict superset post-change"). Status quo is unfalsifiable.

3. **FR-530 punts on field placement and labels it "implementation decision".** Functional requirements must be testable. As written, FR-530 says "command aliases MUST be exposed in the MCP tool metadata" then immediately disclaims where. A test cannot assert on "exposed somewhere"; it must assert on a concrete shape. This is identical content to OQ-1 — the FR should either resolve OQ-1 inline (pick one or both options and commit) or be downgraded to a sub-bullet of OQ-1 until OQ-1 is resolved. Same critique applies to FR-531 by reference. Recommendation: resolve OQ-1 to "dedicated `aliases` array on the tool/parameter object, AND tail-of-description for clients that ignore custom fields" (covers both machine and human consumers) and rewrite FR-530/531 to cite the chosen shape. See Open Question recommendations below.

4. **FR-572 (performance gating) is concrete on threshold but vague on baseline capture.** "Regression of more than 50% on cold start triggers a redesign" — threshold is concrete ✅ and the benchmark is named (`PoshMcp.Benchmarks` cold-start scenario) ✅. But "re-run pre/post change" doesn't say where the baseline lives. Run-4 of the benchmark runs (per Hermes's findings) is the natural pre-change baseline; the FR should name the baseline artifact (e.g., `bench-runs/run-4-artifacts/` or a new captured `bench-runs/run-5-pre-spec010/`) and require the post-change run to be committed alongside as `run-N-post-spec010/` so the regression delta is reproducible from the repo, not from a developer laptop.

5. **SC-205 / SC-206 byte-identical claim needs a carve-out.** "Byte-identical between in-process and OOP modes, given identical PowerShell source loaded identically." Two paths read help in different process contexts; `Get-Help` output can include culture-dependent formatting (paragraph wrapping varies by `$Host.UI.RawUI.BufferSize`) and the OOP host runs in a fresh subprocess with potentially different `$PSDefaultParameterValues` or culture. Either: (a) add an explicit precondition "given identical culture, identical loaded modules, and identical `$PSDefaultParameterValues`" to SC-205/SC-206, OR (b) acknowledge in FR-540 (sanitization) that normalization MUST be aggressive enough to absorb host-specific formatting differences (e.g., collapse all runs of whitespace to single space, not just `\r\n`). Without one of these, the parity test in FR-521 will be flaky on Windows-vs-Linux CI agents and the spec is making an undeliverable promise.

**Suggested improvements (non-blocking, nice-to-have):**

- **Background "What authors expect" table** is excellent — concrete side-by-side of in-process vs OOP for `Get-AzContext`. Consider adding a third column showing what the spec delivers post-change so the win is unambiguous.
- **Edge case "parameter present in multiple parameter sets"** correctly mandates per-parameter (not per-set) descriptions in FR-511. Worth adding an SC for this case so the property is testable and not just declarative.
- **Sequencing step 9** says "Update `docs/articles/exposing-tools.md` (or a new `authoring-tools.md`)". Pick one — leaving the choice in the sequencing list creates a follow-up question at implementation time. Suggest committing to `docs/articles/exposing-tools.md` as the existing surface most authors will look at first.
- **Recommendation section** is strong on rationale. The "Sequencing" sub-list reads like a tasks.md preview; consider moving it to `tasks.md` when this spec promotes, leaving Recommendation focused on the architectural choice.

**Open Question recommendations (where Cubert has an opinion):**

- **OQ-1 (alias placement):** Resolve to **both** — dedicated `aliases` array on the tool/parameter object AND a `(aliases: x, y)` tail on the description. Machine readers get structure; human-only readers (clients that render only `description`) still see them. Closes FR-530/531 testability gap (Required Change 3).
- **OQ-3 (Description body assembly):** Join `MamlParaText[]` with single space, not `"\n\n"`. FR-540 already mandates collapsing embedded newlines; preserving paragraph breaks just to strip them again two FRs later is a contradiction. Single space, then sanitize, then truncate.
- **OQ-6 (ValidateSet phrasing):** Use `"One of: A, B, C"` for ≤5 values, `"One of N values: A, B, C, ..."` for >5. Including the parameter type alongside is redundant — the schema already advertises the type. Don't repeat it in description.
- **OQ-2 (length caps configurability):** Don't make configurable in v1. 1024/512 are sane defaults; configurability adds a config surface to test for marginal value. Defer until an operator asks.
- **OQ-4 (cache invalidation across runspace recycling):** Cache lives in the executor layer (above the runspace), keyed by setup-hash. Recycling a slot does NOT invalidate — the setup-hash is stable across recycles. This matches the existing OOP discovery cache pattern (per #200 review).
- **OQ-5 (doctor field naming):** Coordinate with spec 006 as the spec already says. No opinion until spec 006's doctor schema lands.
- **OQ-7 (telemetry):** Defer to a follow-up spec. Adding a metric layer is a separate concern from making the data correct in the first place.

**Per Reviewer Rejection Protocol (strict lockout):** Farnsworth drafted this spec and is locked out from revising it. Recommended revision agent: **Hermes** (provided the technical baseline research per his 2026-05-12 history entry; has independent grounding in the same code paths Farnsworth cited). Alternate: any squad member other than Farnsworth.

**Cubert.**



### 2026-05-12: Spec 010 revised — ready for re-review or promotion to Accepted
**By:** Hermes (PowerShell Expert)
**Requested by:** Brady
**Artifact:** specs/010-tool-self-documentation/spec.md (Status remains Draft — Brady promotes)
**Original author:** Farnsworth (locked out from self-revision per Reviewer Rejection Protocol strict-lockout rule)
**Reviewer:** Cubert (pre-review verdict: APPROVE WITH CHANGES, 5 required)

**What:** Revised spec 010 to address Cubert's 5 required changes and bake in all 7 of Brady's Open Question resolutions. Status stays Draft per task instructions; Brady makes the final promotion call.

**Cubert's 5 required changes — addressed:**
1. **FR-521 parity test** is now concrete: test class `PoshMcp.Tests/Integration/ToolDescriptionParityTests.cs`, fixture corpus at `PoshMcp.Tests/Fixtures/Modules/HelpParityFixture/HelpParityFixture.psm1` (5 named functions covering each precedence step), equality scope narrowed to MCP `description` + `inputSchema.properties.<name>.description`, both modes run within a single test session, pre-warm Get-Help to bound MAML lazy-load flake.
2. **FR-550 regression** rewritten as a verifiable property + snapshot mechanism. Baseline lives at `specs/010-tool-self-documentation/baseline/{mode}-tools-list.json`. Post-change assertion: any non-empty Synopsis-sourced description must equal-or-prefix-then-`\n\n` post-change.
3. **FR-530/FR-531 removed** entirely per Brady's OQ-1 directive (skip aliases). Added Non-Goal entry. Pruned alias references from Edge Cases, SC list (SC-208/209/210 removed), Approach Options, Recommendation rationale #5, and the Sequencing list.
4. **FR-572 baseline artifact** named explicitly: `bench-runs/run-N-pre-spec010/` captured before implementation, `bench-runs/run-N-post-spec010/` committed with the implementation PR. Regression delta computed against the pre-spec010 baseline specifically.
5. **SC-205/206 byte-identical claim** carve-out resolved via Cubert's option (b) — strengthened FR-540 sanitization to collapse all whitespace runs within paragraphs to a single space while preserving `\n\n` separators, plus stripping non-printable control chars. Spec states explicitly that this normalization is what makes the byte-identical guarantee deliverable across the in-process console host and the OOP subprocess with redirected stdin/stdout.

**Brady's 7 OQ resolutions baked in (now in "Resolved Questions" section):**
- **OQ-1 aliases:** out of scope (FR-530/531 removed, Non-Goal added)
- **OQ-2 length caps:** 1024 tools / 512 params, not configurable in v1 (left a clarifying note in Resolved Questions in case Brady meant 512 for both)
- **OQ-3 description body assembly:** join `MamlParaText[]` with `\n\n`, FR-540 preserves separators
- **OQ-4 cache invalidation:** per-path resolution in FR-571 — in-process cache lives for the runspace lifetime; OOP in-subprocess cache lives until process recycle; optional .NET-side cache invalidates on setup-hash change
- **OQ-5 doctor field:** Hermes-proposed name `descriptionSource` with 4+4 string literals (FR-583)
- **OQ-6 ValidateSet phrasing:** singleton `"One of: A, B, C"` / array `"Each item is one of: A, B, C"` (FR-510 step 3)
- **OQ-7 telemetry:** FR-590 added — two OpenTelemetry counters (`poshmcp.tool_description.source`, `poshmcp.parameter_description.source`) with `step` tag matching the FR-583 vocabulary exactly

**Non-blocking suggestions also applied:**
- "What authors expect" table now has a third row showing what both paths deliver post-spec 010
- Added Scenario 3 (P3) + SC-208 covering FR-511 multi-parameter-set consistency
- Sequencing step 11 commits to `docs/articles/exposing-tools.md` (no "or new file" choice)
- Sequencing list re-headed to note detailed step-by-step belongs in `tasks.md` when promoted; numbered 1-11 with pre-change baseline captures (FR-572 bench + FR-550 snapshots) explicitly first

**Status / next:**
- Spec is Draft. Brady makes the call to promote to Accepted.
- Re-review by Cubert is optional but recommended (the 5 required changes were substantive and the structural changes — new Scenario 3, FR-583, FR-590, Resolved Questions section — warrant a second look).
- Per strict-lockout, if a re-review surfaces further required changes, Hermes is now also locked out from any subsequent revision; a third squad member would own the next pass.

**One open question for Brady (non-blocking, recorded inline in Resolved Questions OQ-2):** Brady's note "512 is reasonable" was interpreted as the parameter cap (512) with tool description cap kept at the draft's proposed 1024. If Brady intended 512 for both, flag and I'll re-revise FR-541 + Resolved Questions OQ-2.

**Hermes.**



### 2026-05-12: Bender — IToolMetadataSource seam shape (PR #238, spec 010 step 3)
# Decision: IToolMetadataSource seam shape

**Date:** 2026-05-12
**By:** Bender (#225)
**Status:** Implemented in seam; precedence implementations land in #226, #227

## Decision

Spec 010 Option A's shared sourcing seam is `IToolMetadataSource` with two
methods: `ResolveToolDescription(in ToolDescriptionRequest)` and
`ResolveParameterDescription(in ParameterDescriptionRequest)`. Both return a
result record carrying the resolved string + an enum identifying which
precedence step produced it.

## Contract

```
IToolMetadataSource
├── ToolDescriptionResult ResolveToolDescription(in ToolDescriptionRequest)
└── ParameterDescriptionResult ResolveParameterDescription(in ParameterDescriptionRequest)

ToolDescriptionRequest        ToolDescriptionResult       ToolDescriptionSource
  CommandName : string          Description : string        Synopsis
  ParameterSetName : string?    Source : enum               Description
  Synopsis : string?                                        Syntax
  LongDescription : string?                                 Name
  ParameterSetSyntax : string?

ParameterDescriptionRequest             ParameterDescriptionSource
  CommandName : string                    HelpParameter
  ParameterName : string                  HelpMessage
  ParameterTypeName : string              ValidateSet
  HelpParameterDescription : string?      TypeFallback
  HelpMessage : string?
  ValidateSetValues : IReadOnlyList<string>?
  ValidateSetAppliesToArrayElement : bool
```

Enum values map 1:1 to the FR-583 `descriptionSource` string literals so
doctor output (#228) and metrics tags (FR-590) can serialize the enum
directly (camelCase JSON convention).

## Rationale

- **Pre-resolved fields, not callbacks.** The seam never calls `Get-Help`
  itself. Each caller (in-process #226, OOP #227) populates the help fields
  from its own source and passes them in. This keeps the seam thread-safe and
  side-effect-free; both call sites can be unit-tested without a PowerShell
  runspace.
- **Request records are `readonly record struct`.** No allocation per call,
  pattern-match-friendly, immutable.
- **`in` parameters.** Avoid struct copies at call sites.
- **Two interface methods, not one.** Tool-level and parameter-level
  precedence are independent chains with different inputs (parameter has no
  syntax line, tool has no `ValidateSet`). Splitting them is clearer than a
  union request type with mode discriminators.
- **Default implementation preserves pre-spec-010 behavior byte-for-byte.**
  In-process falls through Synopsis (null) → Syntax → identical to old
  `"{name} {parameterSet.ToString()}"`. OOP path's Synopsis-when-non-empty
  rule is reproduced exactly.

## DI Wiring

`StdioServerHost` and `HttpServerHost` register
`TryAddSingleton<IToolMetadataSource, DefaultToolMetadataSource>()`. The
`TryAddSingleton` choice lets #226/#227 register their replacement
implementation earlier (or via a layered registration) without conflict.

`McpToolFactoryV2` ctors accept an optional `IToolMetadataSource?` parameter
that defaults to a fresh `DefaultToolMetadataSource` instance. This keeps the
factory usable in test contexts that don't construct a `HostApplicationBuilder`.

## Reviewer-open question

`ToolDescriptionRequest.LongDescription` is part of the contract but the
default impl ignores it. The spec assigns Get-Help long-description
*sourcing* to the caller side in #226, not the seam's behavior selection
ladder. If Farnsworth/Cubert prefer the seam itself to consume
`LongDescription` (i.e., precedence step 2 logic centralized in the seam
rather than each caller deciding what to populate), the change is a
~3-line edit to `DefaultToolMetadataSource.ResolveToolDescription`. Posed
in PR #238 body for explicit reviewer call.



### 2026-05-12: Farnsworth — Spec 010 IToolMetadataSource seam architecture verdict (PR #238)
# Farnsworth — Spec 010 IToolMetadataSource seam architecture verdict (PR #238)

**By:** Farnsworth (Lead/Architect)
**Requested by:** Steven Murawski
**Date:** 2026-05-12
**Status:** Approve (formal approval owned by Steven)

## What

Approved the architectural shape of `IToolMetadataSource` introduced in PR #238 (Bender, branch `squad/225-tool-metadata-source`, closes #225, spec 010 step 3, Option A). This is the foundational seam that wave 3 (#226 in-process Get-Help precedence, #227 OOP `RemoteToolSchema` extension) and wave 4 (#228 OOP wire-through + doctor + FR-590 metrics) plug into.

## Architectural decisions ratified

1. **Caller-side data acquisition.** Get-Help is invoked by `McpToolFactoryV2`/`PowerShellSchemaGenerator` — NOT inside the seam. The seam owns precedence rules; callers own data acquisition. This separation keeps the OOP wire-format independent: the subprocess can resolve and ship pre-resolved fields over ndjson without the seam needing to know about runspaces or processes.

2. **Two-method interface, request/result records.** `ResolveToolDescription` and `ResolveParameterDescription`, each keyed off `readonly record struct` request types. Result types carry both the resolved string and a `Source` enum.

3. **Source enums map 1:1 with FR-583 literals.** `ToolDescriptionSource` {Synopsis, Description, Syntax, Name} and `ParameterDescriptionSource` {HelpParameter, HelpMessage, ValidateSet, TypeFallback}. Enum-to-literal conversion is deferred to #228 (doctor) — correct placement.

4. **DI registration is `TryAddSingleton` in both hosts.** Stdio and HTTP host configurations both register `DefaultToolMetadataSource` via `TryAddSingleton`. `TryAdd` is the right choice — lets #226/#227 register a replacement before host configuration without conflict. Singleton lifetime is correct (default impl is stateless and thread-safe by documentation).

## Verdict

**Approve.** Both call sites (`SetParameterSetDescription` for in-process, `CreateRemoteCommandMetadataMapping` for OOP) are wired through the seam. Behavior is preserved byte-for-byte for realistic inputs in both paths. The interface is shaped so #226/#227/#228 plug in without touching it.

## Forward-compat notes for the wave 3/4 implementers

- **#226 (in-process Get-Help):** populate `Synopsis`, `LongDescription`, `HelpParameterDescription` on the request records. Resolve Get-Help once per command per discovery (FR-570). Switch `McpToolSetupService.CreateToolFactory` to constructor-injected metadata source so the `null` branch can't accidentally bypass a registered replacement.
- **#227 (OOP `RemoteToolSchema` extension):** extend the schema additively with per-parameter help fields. The OOP caller (`CreateRemoteCommandMetadataMapping`) passes them through the same request shape as in-process — no seam change needed.
- **#228 (doctor + metrics):** read `result.Source`, format as the FR-583 literal, emit on the `descriptionSource` field and as a tag on the `poshmcp.tool_description.source` / `poshmcp.parameter_description.source` counters.

## Non-blocking observations (filed for follow-up, not blocking PR #238)

- `McpToolFactoryV2` now has six constructors (3 × {with, without metadata source}). Acceptable for backward-compat during transition. Collapse in a follow-up once all callers route through DI.
- `DefaultToolMetadataSource` calls `Synopsis.Trim()`. PowerShell's Get-Help output is already trimmed for realistic inputs, and FR-540 step 1 will mandate trim anyway. Strictly closer to the spec target than the pre-change OOP behavior.

## Cubert's role

Per the 2026-05-05 user directive, Cubert pre-reviews Farnsworth plans/proposals before they reach the user. This was a PR review (not a plan/proposal), so the directive did not gate this work — Cubert fact-checked in parallel.



### 2026-05-12: Spec 010 baseline capture mechanics

**By:** Fry (issue #224, requested by Steven)

**What:**
- Pre-spec-010 `tools/list` snapshots live under `specs/010-tool-self-documentation/baseline/`. The full JSON-RPC envelope is persisted (pretty 2-space, LF), not just `result.tools`.
- The fixture module `HelpParityFixture` (FR-521) was authored as part of this baseline (PR #236) because the snapshot is meaningless without it. It exports six deterministic functions, one per FR-500/FR-510 precedence-chain rung.
- `capture-snapshots.ps1` is the canonical regen mechanism. Do NOT regenerate after spec 010 lands — the snapshots must remain pre-change to anchor the FR-550 regression test.

**Why (pre-change parity artifacts surfaced during capture; documented in baseline/README.md):**
- In-process tool count (133) ≠ OOP tool count (144) for the same configured module set. Out of scope for spec 010 (FR-551 keeps tool names stable).
- The in-process discovery path does not auto-load modules from `PSModulePath` via `Get-Command -Module`; explicit `CommandNames` are required to trigger auto-load.
- `PowerShellConfiguration.Environment` (ImportModules / ModulePaths) is wired only for the OOP path; the `PowerShellEnvironmentSetup` class exists but is not instantiated for in-process. Captured but worth a separate bug if not intentional.
- `IncludePatterns = ["*"]` is required for OOP discovery to enumerate commands from imported modules; in-process treats it as the no-filter default. Same setting in both modes produces semantically equivalent discovery.

# Decision: v0.13.0 is a MINOR bump

**Date:** 2026-05-13
**By:** Amy (DevOps), at Steven's direction
**Affects:** Versioning policy for spec-completion releases

## Decision

v0.13.0 is a **minor** bump (0.12.3 → 0.13.0), not a patch.

## Why

Spec 010 (tool self-documentation) lands end-to-end in this release plus the #242 wire-path fix. Both materially change what MCP clients see in `tools/list` results — descriptions go from often-empty/fallback in v0.12.x to richer, sanitized text resolved via the FR-500/FR-510 precedence chain. New observable surfaces also ship: doctor's `descriptionSource` field per command/parameter, and OTel counters for description-source resolution. The `SwitchParameter` round-trip fix (#222) is a correctness fix, but the spec 010 surfaces and the wire-path change are the gating reasons.

Patch (0.12.4) would have understated the user-facing change. Major (1.0.0) is reserved for the API-stability commitment we haven't made yet. Minor is the right call.

## Convention going forward

For PoshMcp under 1.x:

- **Patch (0.X.Y → 0.X.Y+1):** server-internal correctness fixes that don't change `tools/list`, doctor output shape, or configuration surface. Hotfixes for OOP, executor, or auth bugs that don't add a new observable surface.
- **Minor (0.X.Y → 0.X+1.0):** spec completions; new observable surfaces (new MCP fields, new doctor sections, new metrics); behavior changes visible to MCP clients on the wire (even when they're "more correct"); new configuration keys.
- **Major:** reserved for the eventual 1.0 API stability commitment.

The headline question for "patch vs minor" is: **does an MCP client see something different in `tools/list` or in tool responses?** If yes, minor.

### 2026-05-14T11:34Z: v0.13.0 release prepped and pushed; tag deferred until CI green
**By:** Amy (executed autonomously while Steven away)
**What:** Cut v0.13.0 in two commits and pushed to `origin/main`:
- Housekeeping commit `5847efb` — stale agent history.md updates (amy, bender, cubert, farnsworth, hermes, leela) and docker.ps1.
- Release commit `a2b9c3e` — version bump (0.12.3 → 0.13.0) in `PoshMcp.Server/PoshMcp.csproj`, CHANGELOG entry for 0.13.0, and `docs/release-notes/0.13.0.md`.

**Quality gates:** `dotnet format --verify-no-changes` passed (warnings only). `dotnet test --nologo` passed: 777/0/7 (passed/failed/skipped of 784, 11m15s).

**Why:** Steven asked Amy to run the autonomous release while away. Marquee for 0.13.0 is spec 010 — Help-aware tool descriptions (in-process + OOP byte-identical schemas, FR-500/510/540, `IToolMetadataSource` seam, doctor `descriptionSource` reporting, OTel counters, parity tests, cold-start gates). Includes fixes for SwitchParameter round-trip (#222), parameter descriptions on inputSchema (#248), and `HelpAwareToolMetadataSource` as default (#250). No breaking API changes.

**Tag NOT created.** Per the team's release process, the v0.13.0 tag must wait for CI green on `a2b9c3e`. When CI is green, tag with:
```
git tag -a v0.13.0 -m "v0.13.0"
git push origin v0.13.0
```

**Process notes for the team:**
- Continued explicit-path-only staging: only `PoshMcp.Server/PoshMcp.csproj`, `CHANGELOG.md`, and `docs/release-notes/0.13.0.md` were staged for the release commit; `.squad/tmp/` left untracked deliberately.
- During the first test run, polling `get_terminal_output` on the long-running `dotnet test` terminal coincided with a build-cancel event at 143s. The clean re-run (no mid-flight polling, generous timeout, single sync wait) completed in 11m15s with full pass. Future autonomous releases on this repo: budget ~12 minutes for the test gate and don't poll.

### 2026-05-13: Test-PR + tracking-issue pattern for found bugs is the team norm

**By:** Farnsworth (via PR #243 review)
**What:** When a test-addition PR exposes a real bug in the code under test, the right architectural choice is: (1) ship the tests pinned to a tracking issue via `[Theory(Skip="Tracking issue #N — ...")]`, (2) file the bug as a separate issue with the skipped test method names listed under acceptance criteria, (3) keep the test-addition PR mergeable on its own. Do NOT conflate measurement and remediation in one PR.
**Why:** Test addition (#229) and resolver→schema wiring fix (#242) belong to different files, different review domains, and different agents. Failing the new tests instead of skipping would block unrelated CI work for a finding that's already triaged and assigned. The skip-with-tracking-issue pattern preserves the regression gate (un-skip when the fix lands → tests turn green) without holding the test PR hostage to the fix PR. Confirmed by PR #243 (Fry's spec 010 wave 5 trio) where 10 `ParameterDescription_IsNonEmpty_*` variants pin to #242.

### 2026-05-13: PR #250 review — PowerShellSchemaGenerator default swap → APPROVE

**PR:** https://github.com/usepowershell/PoshMcp/pull/250 (cold-path twin of #248, closes #249)
**Verdict:** ✅ APPROVE — comment posted https://github.com/usepowershell/PoshMcp/pull/250#issuecomment-4443824935

**What I verified:**
- Diff is exactly the two default swaps Bender described (lines ~33 and ~131): `DefaultToolMetadataSource()` → `HelpAwareToolMetadataSource()` in `GenerateParameterSchema` and the four-arg `CreateParameterSchema` overload, plus matching XML doc updates on class remarks and the `metadataSource` parameter doc.
- `HelpAwareToolMetadataSource` is `sealed` with no explicit ctor → implicit parameterless ctor available. Confirmed by reading the file: it's a pure resolver, never invokes PowerShell, degrades naturally to HelpMessage / ValidateSet / typed fallback when Synopsis/HelpParameterDescription are null. Cold path can't crash.
- Zero-caller claim verifies independently: `grep_search PowerShellSchemaGenerator\.(Generate|Create)` returns no matches in `PoshMcp.Server/**/*.cs` outside the file itself. Cubert's history.md L234 also independently confirms `McpToolFactoryV2` doesn't call `CreateParameterSchema` — the actual inputSchema comes from MCP SDK reflecting on the dynamically-generated assembly. Latent-bug fix, no production impact.
- CI: 2 successful, 1 skipped, 4 pending (CodeQL + CI/build), zero failures at review time. Bender's local validation: ParameterDescription_IsNonEmpty 10/10 + unit suite 532/532.

**Non-blocking notes I left on the PR:**
1. `HelpAwareToolMetadataSource`'s parameterless ctor is now load-bearing for this file. If it ever grows DI, the two default-construction sites need a different strategy (don't revert to `DefaultToolMetadataSource`).
2. Two-arg `CreateParameterSchema(ParameterMetadata)` overload (L67-68) passes `metadataSource: null`, which now lands on the new default — correct, but the comment could clarify it inherits HelpAware rather than implying no-op.

**Architectural takeaway:** The cold doc-emission path and the live MCP wire path now share the same FR-510 precedence default. Two-spec contract (010 FR-500/510/520/540) is uniformly applied across both code paths — no more "fix one, leave the twin" trap. This is the pattern I want to see propagated: when a default fallback embodies a spec contract, both call sites must share the same impl, and the impl should be a pure resolver that degrades safely on missing inputs.

### 2026-05-13: External PR merge protocol — squash via `gh pr merge`, never force-push contributor branches
**By:** Hermes (PowerShell Engineer) on behalf of Steven
**What:** When merging an external contributor's PR (no write access to their fork branch), the protocol is:
1. Fetch the PR head into a local worktree (`git fetch origin pull/N/head:pr-N` + `git worktree add`).
2. Locally rebase onto current `main` ONLY to verify build + tests pass against latest. Do NOT push the rebased commits anywhere.
3. Run full build (`dotnet build PoshMcp.sln`) and the test suites that touch the changed code (Unit + Functional + any feature-targeted filter).
4. Merge via `gh pr merge <N> --squash --delete-branch`. Squash collapses everything to one commit on `main`; the local rebase was just for confidence. GitHub handles the merge atomically.
5. Never use `--rebase` on `gh pr merge` for external PRs unless we've coordinated with the contributor — it can fail mid-merge if their branch has drift we didn't account for.
**Why:** External contributors don't grant push access to their fork branches, so we can't `git push --force-with-lease` to update their PR. Squash-merge sidesteps the entire rewrite-history problem and keeps `main` history linear.

