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
