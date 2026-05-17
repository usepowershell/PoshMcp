# Bender Work History

**Status:** 42.8 KB (checked 2026-05-11: within 90-day retention, no archival required)
**Status:** 37.6 KB (checked 2026-05-03: within 90-day retention, no archival required)

## 2026-05-15: PR #266 - fix(doctor) #261 Pool-mode display

### What I fixed
Doctor report was showing `effectiveProcessPoolSize: 0` and `effectiveMinHealthyForStartup: 0` when `SubprocessHostMode = Pool`. Those knobs are inert in Pool mode (they only apply to ProcessPool), so the value was technically correct but read like a bug to operators. Changed both to render `"n/a (Pool mode)"` outside ProcessPool, mirroring the existing `EffectiveRunspacePoolSize` pattern.

### Files touched
- `PoshMcp.Server/Diagnostics/DoctorReport.cs` - promoted `EffectiveProcessPoolSize` and `EffectiveMinHealthyForStartup` from `int` to `string` (default `string.Empty`).
- `PoshMcp.Server/Diagnostics/DoctorService.cs` - refactored the inline ternaries into an explicit `if (ProcessPool) { compute and ToString } else { "n/a (Pool mode)" }` block. ProcessPool semantics unchanged (clamping + defaults preserved).
- `PoshMcp.Server/Diagnostics/DoctorTextRenderer.cs` - no change. The renderer only emits `process-pool`/`min-healthy` lines when `HostMode == ProcessPool`, so the new strings flow through cleanly.
- `PoshMcp.Tests/Unit/Diagnostics/DoctorOutOfProcessSectionTests.cs` - new, 5 tests: Pool n/a, ProcessPool integer-string, min-healthy clamping, default pool size, not-applicable.

### Test approach
`DoctorService` is internal but the test project has `InternalsVisibleTo`. `OutOfProcessSection` is a public sealed record. Called `DoctorService.BuildOutOfProcessSection` directly with synthesized `PowerShellConfiguration` instances and `NullLoggerFactory.Instance`. No FS, no process spawning - pure Unit tier.

### Gotchas
- `DoctorService` and `DoctorReport` live in the **root `PoshMcp` namespace**, not `PoshMcp.Server.Diagnostics` (despite the folder). My first test file used the folder-shaped namespace and failed to compile. Use `using PoshMcp;` not `using PoshMcp.Server.Diagnostics;`.
- `gh pr create` failed with `Unauthorized: As an Enterprise Managed User` on the `stmuraws_microsoft` account. Had to `gh auth switch -u usepowershell` first. Worth remembering for future PRs to `usepowershell/PoshMcp`.

### Outcome
PR #266 - https://github.com/usepowershell/PoshMcp/pull/266 - marked ready for review, labeled `squad` + `squad:bender`. 54 doctor tests green, full server build clean.
## 2026-05-15: Team update (via Scribe)
**Ralph round 1 — 3 PRs in-flight, may need your review:**
- **PR #266** (Bender, issue #261): Doctor pool display sentinel — EffectiveProcessPoolSize / EffectiveMinHealthyForStartup promoted to `string`, returning `"n/a (<mode> mode)"` when inert. Files: `DoctorService.cs`, `DoctorReport.cs`, `DoctorTextRenderer.cs` + Unit tests.
- **PR #264** (Hermes, issue #262): AAD v2.0 `preferred_username` mapping — added `ClaimsMapping.NameClaim` to `AuthenticationConfiguration`; wires to `JwtBearerOptions.TokenValidationParameters.NameClaimType`. Null preserves default (no behavior change for existing deployments). Files: `AuthenticationConfiguration.cs`, `AuthenticationServiceExtensions.cs`, `docs/entra-id-auth-guide.md`.
- **PR #265 DRAFT** (Farnsworth, issue #263): Spec 011 design-only — `specs/011-doctor-module-imports/spec.md` (13 FRs / 4 SCs / 5 OQs). Implementation split to follow-up issues #267 (Bender) and #268 (Hermes).

## 2026-05-15 — Spec 011 C# wiring (#267) → PR #270 [DRAFT]

**Branch:** `squad/267-doctor-module-imports-csharp` (worktree at `poshmcp-267`), stacked on `squad/268-module-discovery` (#269 — Hermes' `ModuleDiscovery` helper).

### What shipped
- `DoctorReport`: `ModuleImports` property + 4 sealed records (`ModuleImportsSection`, `ModuleImportEntry`, `PatternImportEntry`, `ToolImportEntry`) + extended `ComputeStatus` (module errors → `errors`; pattern/module warnings → `warnings`).
- `DoctorService`: two `BuildModuleImportsSection` overloads — pure-logic (test seam, takes `IReadOnlyList<ModuleProbeResult>`) + runspace-driven (production, calls `ModuleDiscovery.ProbeModules` once). Wired into both `BuildDoctorReportForCliAsync` and `BuildDoctorReportFromConfig`.
- `DoctorTextRenderer`: `RenderModuleImports` + `HasModuleImports` omit guard.
- `DoctorModuleImportsTests`: 12 unit tests (FR-263-12 cases 1-8 + 2 `ComputeStatus` flips + renderer snapshot + empty-section omit). Full Unit suite stays green: 461/461.
- CHANGELOG `## [Unreleased]` with `### Breaking` callout for the `summary.status` flip.

### Key learnings (write these down so future-Bender doesn't re-discover them)

1. **Namespace gotcha (still biting):** `DoctorReport`, `DoctorService`, `DoctorTextRenderer` all live in the ROOT `PoshMcp` namespace, NOT `PoshMcp.Server.Diagnostics`. Tests use `using PoshMcp;` (NOT `using PoshMcp.Server.Diagnostics;`). Look at `DoctorOutOfProcessSectionTests.cs` — it's the canonical pattern.
2. **McpServerTool stubbing:** `McpServerTool` is abstract, but you don't need a custom subclass. Use `McpServerTool.Create(Func<string>, McpServerToolCreateOptions { Name = "snake_case_tool", Title = "PowerShell-CommandName" })`. The `Title` field is what `McpToolFactoryV2` uses to stash the PowerShell command name — that's the field `ExtractToolIdentity` reads to recover `commandName` for FR-263-9 attribution.
3. **Attribution heuristic trade-off (must revisit in Phase 2):** Per-tool `commandName` attribution is exact (we own `config.CommandNames`). For `module`, it's a heuristic: if the config has exactly ONE `Modules` entry, all non-`commandName` tools are attributed to it. With multiple modules, non-`commandName` tools fall back to `source: "unknown"`. The clean fix needs a wire-format extension threading `sourceModule` through `RemoteToolSchema` / `PowerShellCommandMetadata`. Documented in PR body and code comments. Don't lose track of this when planning Phase 2.
4. **Pattern matching:** `PatternMatches` translates `*`/`?` to anchored regex with case-insensitive matching. Wrap in try/catch returning false on regex failure (defensive — the user-supplied pattern could be anything).
5. **Diagnostics MUST be sanitized:** Every diagnostic field that includes a user-supplied module/pattern name flows through `LogSanitizer.Scrub` (FR-263-13, CWE-117 mandate). Don't skip this even when the input "looks safe."
6. **Worktree discipline:** All build/test/commit ops from `poshmcp-267`. All `.squad/*` writes at TEAM_ROOT (`poshmcp`, not the worktree). I keep almost making this mistake.
7. **Renderer recovery:** First multi-edit on `DoctorTextRenderer.cs` accidentally collapsed the `RenderMcpDefinitions` header line into its `foreach` body. Caught by `get_errors` → fixed with one targeted `replace_string_in_file`. Lesson: always run `get_errors` after multi-edit before moving on. Cheap insurance.
8. **PR base:** Stacked on `squad/268-module-discovery` (Hermes' branch), NOT `main`. When that lands first, GitHub will auto-rebase or I'll need to retarget.

## 2026-05-15 — Spec 011 fully shipped

PRs #269 (Phase 1 ModuleDiscovery), #270 (Phase 2a DoctorService wiring), #271 (Phase 2b OOP wire-format parity) all merged to `main` on 2026-05-15. Issue #263 closed. #272 tracks per-tool source attribution refinement separately.

## Learnings

### 2026-05-16T17:02:54.700-05:00 — Issue #272 import source tracker
- `IToolImportSourceTracker` mirrors the spec-010 description tracker shape: per-command, thread-safe, first-writer-wins, and populated during discovery rather than reconstructed later.
- In-process attribution should be recorded directly inside `McpToolFactoryV2` discovery (`GetCommandsByName`, `GetCommandsByModule`, `GetCommandsByPattern`) so no new `Get-Command`/`Get-Module` calls land on the doctor hot path.
- OOP attribution should be captured from `RemoteToolSchema.SourceModule` / `SourcePattern` / `SourceDetail`; when older hosts omit those fields, doctor must report `tools[].source = "unknown"` instead of reviving the old heuristic.
- Doctor consumption point: `PoshMcp.Server/Diagnostics/DoctorService.cs` now takes the tracker and uses it for `moduleImports.tools[]` plus module contribution counts. Wiring entry points are `PoshMcp.Server/McpToolFactoryV2.cs`, `PoshMcp.Server/Server/McpToolSetupService.cs`, and `PoshMcp.Tests/Integration/ToolImportParityTests.cs`.
## 2026-05-16 — Issue #272 assigned to Bender

**Via Farnsworth triage (22:00:11Z):**

Issue #272 "Per-tool import source attribution: introduce IToolImportSourceTracker" assigned to Bender. 

**Scope:** C# interface design task. Directly mirrors Spec 010 pattern (`IToolDescriptionSourceTracker`). No PowerShell discovery work. Builds on Spec 011 Phase 2 payload infrastructure (PR #271).

**Next:** Scope implementation approach for exact per-tool source resolution in doctor report.

### 2026-05-17T08:03:00-05:00 — Issue #277 log forging fixes
- In `PoshMcp.Server/PowerShell/PowerShellAssemblyGenerator.cs`, `OperationContext.CorrelationId` must be treated as untrusted at log sinks; scrub once into `safeInvocationId` and reuse it for every `InvocationId` log argument.
- In `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs`, JWT diagnostics need call-site scrubbing for echoed config values (`Authority`, `ValidAudiences`, `ValidIssuers`) and token-derived data (`AllClaims`, `aud`, `scp`, `roles`, decoded `aud`/`iss`, challenge errors).
- In `PoshMcp.Server/Observability/LoggerExtensions.cs`, structured logging scopes are also log-forging sinks, so `CorrelationId` needs the same `LogSanitizer.Scrub()` treatment as `OperationName`.

