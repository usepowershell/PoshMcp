# Farnsworth — Lead/Architect — Work History

## Recent Work Index (2026-05-16)

- **2026-05-16:** PR #276 re-review (import source tracker wiring across all doctor builders) — APPROVE
- **2026-05-16:** PR #273 review + merge (Leela — tutorials series) — merged
- **2026-05-15–16:** Cross-agent PR #276 cycle (Hermes execution, Cubert verification) — track parity achieved

## Prior Work (2026-05-13 to 2026-05-15)

Detailed entries archived to history-archive.md: Spec 009 review wave (6 PRs, closed), PR #269–#271 (Hermes spec 011 work), architectural learnings from import-tracker-gap discovery.

---

### 2026-05-16 — PR #276 re-review (issue #272)

**Verdict:** APPROVE. The revised chain now threads IToolImportSourceTracker through both runtime report entry points: ConfigurationReloadTools.GetConfigurationStatus() and McpToolSetupService.BuildConfigurationTroubleshootingJson() pass the shared discovery tracker into DoctorService.BuildDoctorReportFromConfig(...), which forwards it into BuildModuleImportsSection(...). That closes the prior runtime 	ools[].source = "unknown" gap.

**Architectural lesson:** for provenance/report seams, the clean shape is a shared per-discovery snapshot owned by discovery (McpToolFactoryV2) and injected into read-only report builders. This keeps DoctorService pure, avoids re-running command discovery for attribution, and lets runtime + CLI surfaces share one authoritative contract.

**Lifecycle note:** McpToolFactoryV2.GetToolsListAsync() now resets the tracker at discovery start, so reload-driven rediscovery can safely reuse the same tracker instance without stale attribution leaking across cycles.

### 2026-05-16 — Squad Scribe cross-pollinate (PR #276 multi-agent cycle)

**Agent collaboration on import tracker fix (issue #272):**
- **Hermes (executor):** Wired tracker through all runtime doctor paths (ConfigurationReloadTools, McpToolSetupService → DoctorService), reset tracker per discovery cycle, added parity tests. 849 tests green.
- **Farnsworth (architect):** Identified architectural gap v1 (CLI-only wiring), recorded decision (all doctor builders must share provenance seam), approved revised wiring.
- **Cubert (fact-check):** Verified tracker design, caught wiring gap v1, confirmed all claims in v2 (parity tests cover commitments, no stale refs).

**Architectural lesson captured in decisions.md:** doctor provenance upgrades must thread through all report builders (BuildDoctorReportForCliAsync AND BuildDoctorReportFromConfig), not just CLI path. Shared per-discovery tracker owned by discovery layer, injected into report builders as read-only snapshot. Reset-at-cycle-start prevents stale attribution on reloads.

**Process note:** User directive recorded (Steven request) — all squad agents must include their name when posting GitHub comments (e.g., "— Farnsworth" or "[Bender]").

## Learnings

### 2026-05-18 — Issue #284: Configuration schema for EnableNounResources and NounResourceOverrides

**Deliverable:** Four files changed/created for the Spec 012 §7 config layer:
- `PoshMcp.Server/McpResources/NounResourceOverride.cs` — new POCO in the `McpResources` namespace (not `PowerShell`), so it lives adjacent to its consumer components rather than in the configuration class hierarchy.
- `PoshMcp.Server/PowerShell/PowerShellConfiguration.cs` — `EnableNounResources` (bool, default false) and `NounResourceOverrides` (Dictionary<string, NounResourceOverride>) added. Dictionary key is the **default** snake_case resource name, not the noun itself — consistent with how `CommandOverrides` is keyed by command name.
- `PoshMcp.Server/McpResources/McpNounResourcesValidator.cs` — static validator + `McpNounResourcesDiagnostics` record following the `McpResourcesValidator` / `McpResourcesDiagnostics` pattern exactly.
- `PoshMcp.Server/Configuration/ConfigurationLoader.cs` — validator called in `LoadPowerShellConfiguration` after binding. This differs from where `McpResourcesValidator` is called (`ValidateResourcesAndPrompts`) because the noun validator takes an `ILogger` which is only available in `LoadPowerShellConfiguration`. The logger-taking variant is therefore wired at load time, not at the separate validate step.

**Design choice recorded:** NounResourceOverride in McpResources namespace (not PowerShell namespace) because it is a resource-layer concern. The PowerShellConfiguration just holds the dictionary; the resource subsystem owns the type.

### 2026-05-18T11:02:22-05:00 — Spec 012: milestone and issues created

**Deliverable:** GitHub milestone #8 ("Spec 012: Noun-Derived MCP Resource Mapping") and 11 tracking issues created.

**Issue numbers:**
- #279 — NounRegistry: Build noun-to-command map at startup (squad:bender)
- #280 — McpNounResourceHandler: Register and serve noun-derived resources (squad:bender)
- #281 — ResourceLinkInjectorWrapper: Augment tool results with resourceLinkBlock (squad:bender)
- #284 — Configuration: EnableNounResources and NounResourceOverrides in appsettings (squad:bender)
- #282 — OOP mode support for NounRegistry and McpNounResourceHandler (squad:bender)
- #283 — Static and noun-derived resources coexist in resources/list and resources/read (squad:bender)
- #286 — Tests: NounRegistry unit tests (squad:fry)
- #287 — Tests: McpNounResourceHandler integration tests (squad:fry)
- #285 — Tests: ResourceLinkInjectorWrapper integration tests (squad:fry)
- #289 — Investigate McpServerTool wrapping surface (OQ-1) (squad:hermes)
- #288 — Doctor report: nounResources section (squad:bender)

### 2026-05-18T10:50:47-05:00 — Spec 012: restructure and OQ resolutions

**Deliverable:** `specs/noun-resource-mapping.md` restructured to `specs/012-noun-resource-mapping/spec.md`. Old file deleted. Decision file: `.squad/decisions/inbox/farnsworth-spec-012-oq-resolutions.md`.

**Open questions resolved:**
- **OQ-3:** Inject resourceLinkBlock for ALL commands with a resourceable noun, including Get-* verbs. No verb-based suppression. §5.2 and §7.2 updated; FR-NR-08A added.
- **OQ-4:** Doctor integration planned — `poshmcp doctor` will include a `nounResources` section (follow-up spec). §8.3 updated.
- **OQ-5:** `EmbeddedResource` content type (MCP spec 2024-11-05 `type: "resource"`) is the canonical approach. `TextContentBlock` has no `mimeType` field; the original draft was non-standard. Wire shape is `EmbeddedResource` wrapping `TextResourceContents` with `uri`, `mimeType = "application/json+mcp-resource-link"`, and `text` = JSON resourceLink. SDK v1.2.0 type name to verify at implementation time.

**Spec 011 forward reference:** `specs/011-doctor-module-imports/` folder does not exist in the repo (was shipped as PRs #269–#271, no spec folder created). §8.3 reference kept as a forward reference note.

### 2026-05-18T10:32:05-05:00 — Spec: noun-resource-mapping.md

**Deliverable:** `specs/noun-resource-mapping.md` — design spec for dynamically derived MCP resources from PowerShell verb-noun naming. Decision file: `.squad/decisions/inbox/farnsworth-noun-resource-mapping.md`.

**Key architectural decisions captured:**
1. **Noun → resource name**: PascalCase noun from `Verb-Noun` → snake_case via upper-boundary insertion (`BamiTenantUser` → `bami_tenant_user`). Mechanical, no normalization dictionary.
2. **Resourceable = has Get-{Noun}**: Only nouns with a `Get-*` backing command get a resource and `resourceLinkBlock`. Nouns with only mutating commands produce nothing — a resource without a read surface is misleading.
3. **resourceLinkBlock = separate TextContent item**: `mimeType = "application/json+mcp-resource-link"`, appended last in `CallToolResult.Content`. Works for all result shapes (scalar, object, array). NOT embedded into primary JSON payload.
4. **Opt-in**: `EnableNounResources` defaults `false`. Existing deployments are unaffected.
5. **Parameterless URIs only**: `poshmcp://resources/{resource_name}` — no `/{id}` segment in this iteration. Parameterized read is OQ-2 (deferred).

**Implementation shape**: `NounRegistry` (immutable, built post-discovery) + `McpNounResourceHandler` (composite with existing `McpResourceHandler`) + `ResourceLinkInjectorWrapper` (wraps `McpServerTool` per the SDK surface, OQ-1 is the key blocker to confirm). All wired in `McpToolSetupService` and `StdioServerHost`/`HttpServerHost`.

**Open questions for team**: OQ-1 (McpServerTool wrapping), OQ-3 (should Get commands receive a resourceLinkBlock), OQ-5 (separate content item vs. embedded JSON — spec defaults to separate pending team confirmation).

### 2026-05-17T08:12:00-05:00 — PR #278 review (issue #277)

**Verdict:** REJECT.

**What I reviewed:** `main...squad/277-log-forging-fixes` for `AuthenticationServiceExtensions.cs`, `PowerShellAssemblyGenerator.cs`, and `LoggerExtensions.cs`, focusing on whether `LogSanitizer.Scrub()` was applied at user-controlled log sinks without needless spread into internal-only values.

**Key observations:** The new scrubbing added in JWT diagnostics and correlation scope handling is directionally correct, and the call-site pattern matches the `LogSanitizer` contract. But `PowerShellAssemblyGenerator.cs` still leaves user-controlled values unsanitized at several log sinks, including `_MaxResults` validation (`commandName`), cache-output helpers (`property`, `filterScript`), and generation-time command-name/error logging. That means the fix is not yet a complete or sustainable logging-hardening pass for the touched file.

**Review comment posted:** REJECT on PR #278 with the specific missed sinks called out for follow-up.

### 2026-05-17T08:20:00-05:00 — PR #278 re-review after Hermes revision

**Verdict:** REJECT.

**What changed since prior review:** Hermes fixed the five originally-called-out sinks in `PowerShellAssemblyGenerator.cs`: generation-time command/error logging, `_MaxResults` validation, sort helper property logging, filter helper filter-script/exception logging, and group helper property logging. The broader pass also introduced a sustainable pattern for hot paths by scrubbing once into `safeCommandName` and `safeInvocationId` and reusing those values across the invocation lifecycle logs.

**Remaining issue:** the claimed comprehensive scan is not yet complete. `PowerShellAssemblyGenerator.cs` still logs a dynamic string type name raw in the bound-parameter debug sink (`ValueType={ValueType}` uses `convertedValue?.GetType().Name` unsanitized while adjacent `ParameterName` and `Value` are scrubbed). I kept the verdict at REJECT because the architectural rule for this hardening pass should remain: every untrusted or runtime-derived string reaches the log sink only through `LogSanitizer.Scrub()` or a pre-scrubbed local.

### 2026-05-17T08:30:00-05:00 — PR #278 final re-review (third revision)

**Verdict:** APPROVE.

**What I reviewed:** the full `main...squad/277-log-forging-fixes` diff, with focused verification of the `PowerShellAssemblyGenerator.cs` bound-parameter sink and a quick scan of remaining logger calls in the touched server files.

**Key observations:** The prior blocker is resolved: `ValueType` now flows through `LogSanitizer.Scrub(convertedValue?.GetType().Name)` before reaching the bound-parameter debug log. The touched files now consistently scrub attacker-controlled or runtime-derived string fields at the sink (or via pre-scrubbed locals), including correlation scope values, JWT diagnostic payloads, command names, invocation IDs, `_MaxResults` warnings, and cached-output helper inputs (`property`, `filterScript`). I did not find any remaining unsanitized dynamic string values in logger calls during this re-review.

## 2026-05-17T13:12:00Z: Cross-team update — Log-forging fix #277

Bender completed remediation of 24 CodeQL cs/log-forging alerts across PowerShellAssemblyGenerator.cs, AuthenticationServiceExtensions.cs, and LoggerExtensions.cs. Pattern: LogSanitizer.Scrub() applied to all untrusted sources (correlation IDs, JWT claims, config values) at structured log call sites. Build + tests pass. PR #278 open.
## 2026-05-16 — v0.14.1 Release (via Scribe)

Release v0.14.1 shipped successfully. Version bump, release notes, and GitHub release creation completed by Amy. Commit a2a89b3, tag v0.14.1 pushed to origin, release published.
## 2026-05-13 / 2026-05-14 — Summarized (full text in history-archive.md, archived by Scribe 2026-05-16)

Spec 009 review wave + #247 docs:
- **PR #247** (Hermes, FR-500/510 description precedence docs) — APPROVE. Doc literals verified verbatim against `IToolDescriptionSourceTracker` (synopsis/description/syntax/name + helpParameter/helpMessage/validateSet/typeFallback). Lesson: for multi-step resolver docs, **table + tiny per-step example + literal wire vocabulary** beats prose.
- **PR #253** (Leela, TESTING.md) — APPROVE with 2 non-blocking nits. Lesson: when a docs PR ships ahead of impl PRs, defer all *named* details to the source-of-truth file; commit only to the *contract*.
- **PR #256** (Fry, class-level Category traits baseline) — REJECT (reassigned to Bender). Found Unit/OutOfProcess/* classes that spawn pwsh / bind ports / share temp. Lesson: **grep the file for forbidden patterns, never infer from folder.** Class-level trait MUST reflect the class''s most-resource-intensive method.
- **PR #257** (Amy, flake-rate workflow) — APPROVE. Lesson: measurement vs gating semantics differ — flake measurement wants `set +e` + capture-then-decide, NOT production fail-fast.
- **PR #258** (Hermes, TempDirectory helper) — APPROVE. Composes with PR #255 SubprocessTeardown (same `Shared/` IDisposable + best-effort + never-throw idiom). Audit hooks land #216 CI diagnostic seam.
- **PR #252** (Amy, category-scoped phases) — APPROVE. Already complete from prior session — Ralph re-routed; lesson: **before doing review work, `gh pr view N --json reviews,comments` to check existing verdicts.**
- **PR #259** (Fry, reclassify misfiled Unit/OutOfProcess, FR-414) — APPROVE. Cleanest possible reclassification (8 namespace flips, +1/-1 each, 98–99% similarity). The "grep, never infer from folder" rule applied for the third time.
- **PR #260** (Fry, FR-416 Functional sweep, spec 009 closing PR) — APPROVE. Codified partial-class promotion policy: **any partial touching external resources → whole class promotes together.** Spec 009 closed.

Standing patterns codified: (1) grep the file, never infer from folder; (2) partial classes promote whole-class; (3) always grep both raw IO AND test-helper abstractions (`InProcessMcpServer`/`ExternalMcpClient`); (4) check existing reviews before doing review work; (5) self-approval blocked under `usepowershell` identity — comment-form verdict + artifact file is the team accepted pattern.
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


## 2026-05-16 — PR #273 review + merge (Leela — 4-part tutorial series)

**Verdict:** APPROVE → MERGED. Comment posted (#issuecomment-4467060959); full review text archived 2026-05-16.

Code-grounded review passed: ApiKey dictionary-key-is-secret (`ApiKeyAuthenticationHandler.cs:32`), role-claim minting chain through `AuthorizationHelpers.HasRequiredRoles` any-match (L23), `ToolListAuthorizationFilter.CanAccessTool` ordering (L55-78), Docker base image contract (paths + `appuser` UID 1001), `DefaultScheme="Bearer"` default ([AuthenticationConfiguration.cs:8](PoshMcp.Server/Authentication/AuthenticationConfiguration.cs#L8)). All 5 non-blocking polish asks (doctor demo + DefaultScheme callout + Modules-vs-CommandNames callout + no-key boundary beat + Dockerfile.user drift note) landed by Leela in polish pass; Cubert re-verified clean. Squash-merged to ``main``.

Patterns codified: (1) tutorials touching `Modules`/`IncludePatterns` should always demonstrate `poshmcp doctor` + v0.14.0 `moduleImports` section; (2) for code-grounded doc PRs, map each named config property to its handler/options class and verify spelling + semantics; (3) check `gh pr view N --json reviews,comments` first to avoid double-verdict races.

## 2026-05-22T05:40:02 — Unit Test Gap-Fill Plan Development

**Session:** Unit test review and remediation roadmap (background mode, coordinated with Fry)

**Task:** Develop prioritized remediation plan based on Fry's confidence review and gap analysis.

**Approach:** Phased roadmap prioritized for risk/effort trade-off:
- **Phase 1 (8 quick wins):** No refactoring required — immediate coverage wins on straightforward code paths
- **Phase 2 (6 items):** Minor mock/seam additions — leverage existing test patterns with minimal setup
- **Phase 3 (2 items):** Interface extraction investments — well-scoped subsystems worthy of seam patterns

**Input from Fry:** 412 tests across 51 files, Medium confidence, 6 critical gaps (ConvertParameterValue, HealthCheck, Auth filters, ObjectSerializer, SchemaGenerator, ToolFactory).

**Outcome:** Roadmap ready for execution. Phase 1 establishes solid foundation with no architectural risk.

**Note:** Decisions.md updated with Hermes log-forging revision (2026-05-17T08:15:00).

### 2026-06-01T00:00:00Z — App Insights suppresses HTTP metrics console exporter

**Change:** HTTP OpenTelemetry now treats console metrics as a local/default fallback only. When `ApplicationInsights.Enabled` is true and a connection string is available from config or `APPLICATIONINSIGHTS_CONNECTION_STRING`, the HTTP host skips `AddConsoleExporter()` so metric output does not clutter console logs while Azure Monitor export is active.

**Architectural lesson:** exporter routing should share the same App Insights readiness predicate as Azure Monitor wiring. A small shared helper (`ApplicationInsightsConfiguration`) prevents drift between "should we add console exporter?" and "can we configure App Insights?" decisions across HTTP and stdio hosts.
## 2026-05-13 / 2026-05-14 — Summarized (full text in history-archive.md, archived by Scribe 2026-05-16)

Spec 009 review wave + #247 docs:
- **PR #247** (Hermes, FR-500/510 description precedence docs) — APPROVE. Doc literals verified verbatim against `IToolDescriptionSourceTracker` (synopsis/description/syntax/name + helpParameter/helpMessage/validateSet/typeFallback). Lesson: for multi-step resolver docs, **table + tiny per-step example + literal wire vocabulary** beats prose.
- **PR #253** (Leela, TESTING.md) — APPROVE with 2 non-blocking nits. Lesson: when a docs PR ships ahead of impl PRs, defer all *named* details to the source-of-truth file; commit only to the *contract*.
- **PR #256** (Fry, class-level Category traits baseline) — REJECT (reassigned to Bender). Found Unit/OutOfProcess/* classes that spawn pwsh / bind ports / share temp. Lesson: **grep the file for forbidden patterns, never infer from folder.** Class-level trait MUST reflect the class''s most-resource-intensive method.
- **PR #257** (Amy, flake-rate workflow) — APPROVE. Lesson: measurement vs gating semantics differ — flake measurement wants `set +e` + capture-then-decide, NOT production fail-fast.
- **PR #258** (Hermes, TempDirectory helper) — APPROVE. Composes with PR #255 SubprocessTeardown (same `Shared/` IDisposable + best-effort + never-throw idiom). Audit hooks land #216 CI diagnostic seam.
- **PR #252** (Amy, category-scoped phases) — APPROVE. Already complete from prior session — Ralph re-routed; lesson: **before doing review work, `gh pr view N --json reviews,comments` to check existing verdicts.**
- **PR #259** (Fry, reclassify misfiled Unit/OutOfProcess, FR-414) — APPROVE. Cleanest possible reclassification (8 namespace flips, +1/-1 each, 98–99% similarity). The "grep, never infer from folder" rule applied for the third time.
- **PR #260** (Fry, FR-416 Functional sweep, spec 009 closing PR) — APPROVE. Codified partial-class promotion policy: **any partial touching external resources → whole class promotes together.** Spec 009 closed.

Standing patterns codified: (1) grep the file, never infer from folder; (2) partial classes promote whole-class; (3) always grep both raw IO AND test-helper abstractions (`InProcessMcpServer`/`ExternalMcpClient`); (4) check existing reviews before doing review work; (5) self-approval blocked under `usepowershell` identity — comment-form verdict + artifact file is the team accepted pattern.
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


## 2026-05-16 — PR #273 review + merge (Leela — 4-part tutorial series)

**Verdict:** APPROVE → MERGED. Comment posted (#issuecomment-4467060959); full review text archived 2026-05-16.

Code-grounded review passed: ApiKey dictionary-key-is-secret (`ApiKeyAuthenticationHandler.cs:32`), role-claim minting chain through `AuthorizationHelpers.HasRequiredRoles` any-match (L23), `ToolListAuthorizationFilter.CanAccessTool` ordering (L55-78), Docker base image contract (paths + `appuser` UID 1001), `DefaultScheme="Bearer"` default ([AuthenticationConfiguration.cs:8](PoshMcp.Server/Authentication/AuthenticationConfiguration.cs#L8)). All 5 non-blocking polish asks (doctor demo + DefaultScheme callout + Modules-vs-CommandNames callout + no-key boundary beat + Dockerfile.user drift note) landed by Leela in polish pass; Cubert re-verified clean. Squash-merged to ``main``.

Patterns codified: (1) tutorials touching `Modules`/`IncludePatterns` should always demonstrate `poshmcp doctor` + v0.14.0 `moduleImports` section; (2) for code-grounded doc PRs, map each named config property to its handler/options class and verify spelling + semantics; (3) check `gh pr view N --json reviews,comments` first to avoid double-verdict races.

## 2026-05-22T05:40:02 — Unit Test Gap-Fill Plan Development

**Session:** Unit test review and remediation roadmap (background mode, coordinated with Fry)

**Task:** Develop prioritized remediation plan based on Fry's confidence review and gap analysis.

**Approach:** Phased roadmap prioritized for risk/effort trade-off:
- **Phase 1 (8 quick wins):** No refactoring required — immediate coverage wins on straightforward code paths
- **Phase 2 (6 items):** Minor mock/seam additions — leverage existing test patterns with minimal setup
- **Phase 3 (2 items):** Interface extraction investments — well-scoped subsystems worthy of seam patterns

**Input from Fry:** 412 tests across 51 files, Medium confidence, 6 critical gaps (ConvertParameterValue, HealthCheck, Auth filters, ObjectSerializer, SchemaGenerator, ToolFactory).

**Outcome:** Roadmap ready for execution. Phase 1 establishes solid foundation with no architectural risk.

**Note:** Decisions.md updated with Hermes log-forging revision (2026-05-17T08:15:00).
