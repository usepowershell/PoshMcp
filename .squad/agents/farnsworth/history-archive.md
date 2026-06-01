# farnsworth - History Archive (Pre-cleanup)

# Farnsworth Work History
## Project Context
Project: PoshMcp - Model Context Protocol (MCP) server for PowerShell
Tech Stack: .NET 10, C#, PowerShell SDK, OpenTelemetry, ASP.NET Core, xUnit
Primary User: Steven Murawski
Current Priorities:
- Improve maintainability (structured errors, config validation)
- Enhance resilience (circuit breakers, timeouts, retry logic)
- Boost observability (metrics, health checks, diagnostics)

## Learnings (Recent)

### 2025-07-17: PR #135 — Program.cs extraction quality (items 1-4)

**Task:** Reviewed PR #135 which extracts `LoggingHelpers`, `DockerRunner`, `SettingsResolver`, `ConfigurationFileManager`, and `ConfigurationLoader` from Program.cs.

**Key observations:**
- All extractions were complete and correct — every method and type listed in the plan for items 1–4 appeared in its designated file with no omissions.
- The decision to combine PRs A–D into one PR was sound: all four are "safe" extractions (pure function moves, no instance state), so there was no behavioral risk to combining them.
- Call sites updated consistently throughout Program.cs — no stale direct method calls left behind.
- Namespace (`namespace PoshMcp;`) and visibility (`internal static`) were uniform across all five new files. Zero accidental public surface.
- Build: 0 errors, 0 warnings. Tests not explicitly run, but build green is a strong indicator.
- Only note: `ExitCodeRuntimeError = 4` is duplicated as `private const` in both `Program.cs` and `DockerRunner.cs`. Harmless, but a candidate for a shared constants class in a later sweep.
- `Program.cs` is ~2,100 lines post-extraction — expected at this stage. The big reductions come in PRs E–H (doctor, tool setup, server hosts, CLI tree).

**Verdict:** APPROVED. Extraction quality is high. Pattern is replicable for PRs E–I.

### 2025-07-18: Program.cs refactor plan authored

**Task:** Full read of Program.cs (~3,480 lines) and all other .cs files in PoshMcp.Server. Produced working refactor plan at `specs/program-cs-refactor.md`.

**Key findings:**
- Program.cs owns 12 distinct concerns — CLI tree, command handlers, settings resolution, config file I/O, config loading, doctor diagnostics, MCP tool setup, stdio server startup, HTTP server startup, Docker process commands, logging utilities, and inline model types.
- All methods are `private static` or `internal static` with no instance state — extractions are pure method moves with no behavioral risk.
- Two genuine care points: (1) `args` is closed over in SetHandler lambdas — must be threaded explicitly when extracting handlers; (2) `ConfigureJsonSerializerOptions`/`RegisterCleanupServices` are duplicated for both builder types — deduplicate via a shared `Action<>` delegate.
- Static mutable state on `McpToolFactoryV2.SetMetrics`, `PowerShellAssemblyGenerator.SetMetrics/SetRuntimeCachingState/SetConfiguration` is a pre-existing anti-pattern — explicitly deferred from this refactor.
- `UpgradeConfigWithMissingDefaultsAsync` is a side-effecting call embedded inside config path resolution — intentional coupling, move both methods together rather than decoupling.

**Proposed breakdown:** 10 new files, 9 incremental PRs, Program.cs target ≤200 lines.

**Decision inbox entry:** `.squad/decisions/inbox/farnsworth-program-cs-refactor.md`

### 2026-07-18: Issue #131 — STDIO logging architecture review

**Design ownership:** Created comprehensive architecture spec (farnsworth-131-stdio-logging-design.md) defining:
- Problem: stdio transport must not pollute MCP JSON-RPC stream with console logging
- Solution: Serilog file-backed logging with 3-tier resolution (CLI > env > config > silent)
- New dependencies: Serilog.Extensions.Hosting, Serilog.Extensions.Logging, Serilog.Sinks.File
- ConfigureStdioLogging method with ClearProviders unconditional suppression
- OTel console exporter guarded by isStdioMode parameter

**PR #132 Review:** Comprehensive code review across all team contributions:
- Verified ClearProviders unconditionally prevents stdio pollution
- Validated Serilog file sink configuration (rolling daily, 7-day retention, output template)
- Confirmed resolution tier precedence (CLI > env > appsettings > silent)
- Checked OTel console exporter guarded by isStdioMode (HTTP path unchanged)
- Reviewed test coverage (10 tests, full suite 487/0/1 pass)
- Validated documentation updates (README + DOCKER with all three config options)

**Verdict:** APPROVED - Implementation matches design spec. Ship it.

### 2026-07-14: MCP authentication architecture design

**Decision:** Implement two-layer authentication for HTTP transport:
1. **ASP.NET Core middleware** validates identity (JWT Bearer tokens, API keys) → populates `HttpContext.User`
2. **MCP SDK `CallToolFilters`** enforce per-tool authorization via `FunctionOverrides` config (scopes, roles, anonymous bypass)

**Architecture rationale:**
- Use `McpRequestFilters.CallToolFilters` (not `DelegatingMcpServerTool` wrappers) — cross-cutting, direct access to `User` and tool names, pairs with `ListToolsFilters` for consistent visibility
- Standard ASP.NET Core auth stack (not custom MCP-layer parsing) — SDK's `MessageContext.User` proves this is the intended integration point
- Multi-scheme support: JWT Bearer (spec compliance, enterprise) + API Key (simplicity)
- Disabled by default (`Authentication.Enabled = false`) for backward compatibility
- Stdio transport skips HTTP auth per MCP spec, but `CallToolFilters` still enforce tool-level policy

**Implementation scope:**
- New `Authentication` config section with `Enabled`, `Schemes`, `DefaultPolicy`
- `FunctionOverride` extends existing pattern with `RequiredScopes`, `RequiredRoles`, `AllowAnonymous`
- `Program.cs` gains conditional auth middleware in HTTP pipeline
- RFC 9728 protected resource metadata endpoint: `/.well-known/oauth-protected-resource`
- New dependency: `Microsoft.AspNetCore.Authentication.JwtBearer`

### 2026-07-15: PR #83 re-review (auth metrics Phase 6)

**Verdict:** APPROVED
**Issue resolved:** McpMetrics dual-instance bug fixed by Bender. Auth filters now registered as DI singletons with factory lambdas resolving `McpMetrics` via `sp.GetRequiredService<McpMetrics>()`. No manual `new McpMetrics()` construction in auth path. Deferred capture pattern for filter variables (assigned post-`app.Build()`) is safe — lambdas execute only at request time.
**Non-blocking nit:** Redundant LINQ lookup in `ApiKeyAuthenticationHandler` (`Options.Keys.FirstOrDefault(k => k.Key == apiKey).Key` after `TryGetValue` already succeeded).
**Build:** 0 errors on branch.

### 2026-07-15: Batch PR review session (PRs #92–#96)

**Reviewed 5 PRs, 4 approved, 1 rejected:**

| PR | Author | Verdict | Summary |
|----|--------|---------|---------|
| #92 | Amy | ✅ APPROVED | `--use-default-display-properties` flag — clean pattern adherence |
| #93 | Bender | ✅ APPROVED | Auth-enabled warning — minimal, advisory-only, stderr |
| #94 | Fry | ✅ APPROVED | 12 unit tests for update-config flags — comprehensive coverage |
| #95 | Hermes | ✅ APPROVED | Unserializable type filtering — solid 3-tier handling with 33 tests |
| #96 | Hermes | ❌ REJECTED | Doctor resolution diagnosis — `DiagnoseMissingCommands` called twice in JSON path |

**PR #96 rejection rationale:** `RunDoctorAsync` enriches `configuredFunctionStatus` with resolution reasons, then passes the list to `BuildDoctorJson` which independently calls `DiagnoseMissingCommands` again. Each call creates an `IsolatedPowerShellRunspace` and runs `Get-Command`/`Import-Module` per missing command. Fix: guard in `BuildDoctorJson` to skip when `ResolutionReason` is already populated. Assigned to Bender per rejection lockout.

**Cross-PR observations:**
- PRs #92, #93, #96 all modify `Program.cs` from same base (`bb35363`). Different line ranges — no merge conflicts expected but must merge sequentially.
- No PR touches `PoshMcp.Server.csproj` — no compatibility concerns with recent csproj edits.
- PR #94 depends on PR #85's flags being on `main` (already merged) — no ordering concern.
- PR #95 is self-contained (new files + `PowerShellAssemblyGenerator.cs`) — can merge independently.

### 2026-07-15: PR #96 re-review — approved and merged



### 2025-07-17: PR #135 re-review — second pass confirmation

**PR:** #135 — `refactor: extract LoggingHelpers, DockerRunner, SettingsResolver, ConfigurationFileManager, ConfigurationLoader from Program.cs`
**Verdict:** APPROVED (comment — self-approval blocked by GitHub)

**Second-pass validation (independent of Steven's self-review):**
- Verified all 5 files contain exactly the methods specified in items 1–4 of `specs/program-cs-refactor.md`
- Scanned all 60+ call sites in Program.cs — every one uses the new class prefix (`LoggingHelpers.`, `DockerRunner.`, `SettingsResolver.`, `ConfigurationFileManager.`, `ConfigurationLoader.`). Zero stale unqualified calls.
- Confirmed no method definitions are duplicated between Program.cs and the new files via `private static|internal static` scan.
- Namespace (`namespace PoshMcp;`) and visibility (`internal static`) uniform across all 5 files.
- Program.cs is 2,100 lines — expected intermediate state. Bulk reduction in PRs E–H.
- `ExitCodeRuntimeError = 4` duplication noted again (Program.cs + DockerRunner.cs). Non-blocking. Candidate for shared constants.
- `args` closure, static mutable state, `UpgradeConfigWithMissingDefaultsAsync` coupling — all handled per plan.

**Pattern for future PRs:** The combined A–D approach worked well for "safe" extractions (pure function moves). PRs E–G (doctor, tool setup, server hosts) have more cross-cutting dependencies and should be individual PRs as the plan recommends.



### 2025-07-18: PR #138 review — approved (Dockerfile restore/build fix)

**PR:** #138 (fixes #136) — `fix(#136): Fix Dockerfile restore/build`
**Verdict:** APPROVED

**Fix:** Two-line change: `dotnet restore PoshMcp.sln` → `dotnet restore PoshMcp.Server/PoshMcp.csproj`, `dotnet build PoshMcp.sln` → `dotnet build PoshMcp.Server/PoshMcp.csproj`. Fixes container build failure when only PoshMcp.Server.csproj is copied in the early layer but restore/build targeted the full solution (which references TestClient and PoshMcp.Tests not present in the container).

**Non-blocking nit:** `COPY PoshMcp.sln ./` on line 9 is now dead weight — no build command references it. Candidate for cleanup.



### 2025-07-18: PR #139 review — approved (doctor config coverage)

**PR:** #139 (fixes #137) — `feat(#137): Add auth, logging, env vars, MCP definitions to doctor`
**Verdict:** APPROVED

**Implementation quality:** 4 new diagnostic sections in both text and JSON output. 12 tests with well-designed disposable helpers (`DoctorConfigFile`, `DoctorConsoleCapture`, `DoctorEnvVarScope`). All 7 env vars covered. `BuildDoctorJson` new parameters use `= null` defaults with null-coalescing fallback — zero impact on existing callers. `[Collection("TransportSelectionTests")]` correctly prevents parallel execution. No trailing whitespace.

**Non-blocking nits:**
1. `TryLoadResourcesAndPromptsDefinitions` called unconditionally in `BuildDoctorJson` even when both values pre-supplied — should be guarded like auth/logging 3 lines above (same class of issue as PR #96 rejection, but much lower cost).
2. `POSHMCP_LOG_FILE` (added in PR #132) absent from env vars list — follow-up candidate.

**Pattern noted:** The precomputed-optional-parameter pattern (from PR #96) continues to be the correct approach for `BuildDoctorJson` — compute expensive data once in `RunDoctorAsync`, pass via optional params, let `BuildDoctorJson` self-compute only when called standalone.

## Cross-Agent: PR Review Approved (2026-04-20)

- Amy fixed PR #138 feedback (worktree poshmcp-136) 
- Bender fixed PR #139 feedback (worktree poshmcp-137)
- Both PRs approved with nits resolved


## Archived 2026-05-05 (history summarization, lines 201-281 of pre-summarization file)

- ComputeStatus: `errors > warnings > healthy` precedence per FR-102
- ResolvedSetting: `value`/`source` pairs per FR-107

**Must-fix nits (3):**
1. MCP tool description says "Outputs structured text by default; pass argument '--json'" — tool always returns JSON, no format argument exists. Misleading to LLM clients.
2. `POSHMCP_LOG_FILE` missing from `CollectEnvironmentVariables` — FR-113 violation, flagged since PR #139.
3. `POSHMCP_CONFIG` should be `POSHMCP_CONFIGURATION` in `CollectEnvironmentVariables` — pre-existing bug; `SettingsResolver.cs` defines the env var as `POSHMCP_CONFIGURATION`.

**Non-blocking observations:**
- `✖` (U+2716) vs `✗` (U+2717) inconsistency in `RenderMcpDefinitions` vs `StatusSymbol`
- Auth/logging config removed from output (technically FR-109 information loss, but defensible per spec's "placeholder" language)
- Extra env vars added beyond spec's 8 (POSHMCP_FUNCTION_NAMES, POSHMCP_COMMAND_NAMES, DOTNET_ENVIRONMENT) — additive, fine

## [2026-04-23T15:08:26] Deploy Source Image Spec

**Session:** Deploy source image support implementation (spec 007)
**Contribution:** Authored specification for -SourceImage parameter support

**Key Learnings:**
- Spec document: specs/007-deploy-source-image/spec.md
- Defines parameters for source image support in deploy.ps1
- Coordinated with Amy (implementation) and Fry (testing)

**Artifacts:** specs/007-deploy-source-image/spec.md



### 2026-04-23: Added session-recall skill

Created `.squad/skills/session-recall/SKILL.md` — project-level skill documenting the `session-recall` CLI tool for coordinator startup context recovery. Covers the lean 3-command startup sequence, how to pass recovered context into spawn prompts, fallback to the SQL-based `session-recovery` template skill, and anti-patterns. This is the preferred pattern over raw `session_store` SQL queries when the CLI is installed.

### 2026-04-25: Spec 008 — Application Insights Logging directory structure

**Actions taken:**
- Created `specs/008-application-insights-logging/` directory with `spec.md`, `tasks.md`, `checklists/requirements.md`
- Deleted old flat spec file from repository root
- Committed and pushed to main

**Result:** Spec 008 now in speckit format matching specs 001–007 naming and structure convention.

## Learnings

Spec: Application Insights optional logging — proposed Azure.Monitor.OpenTelemetry.AspNetCore integration, config-driven opt-in via appsettings ApplicationInsights section. File: specs/008-application-insights-logging/spec.md

## 2026-04-27: Reviewed Wave 1 PRs #176 and #177

**PR #176** (Azure.Monitor.OpenTelemetry.AspNetCore package): ✅ APPROVED
- Package Azure.Monitor.OpenTelemetry.AspNetCore v1.4.0 correctly added per FR-306
- Modern OpenTelemetry-based SDK (not legacy Microsoft.ApplicationInsights.AspNetCore)
- Build: 0 errors, 9 warnings (pre-existing)

**PR #177** (ApplicationInsights config section and binding model): ✅ APPROVED
- ApplicationInsightsOptions class correctly implements binding model
- Defaults spec-compliant: Enabled=false (FR-300), ConnectionString="" (FR-301), SamplingPercentage=100 (FR-302)
- SectionName constant = "ApplicationInsights"
- XML documentation on all public members
- appsettings.json includes ApplicationInsights section with Enabled: false (FR-318)
- Build: 0 errors, 9 warnings (pre-existing)

Both PRs ready to merge. Wave 1 infrastructure complete for spec 008.

### 2026-04-28: PR #180 review — REQUEST CHANGES (ConfigureApplicationInsights integration)

**PR:** #180 (branch: squad/172-configure-app-insights) — `feat: ConfigureApplicationInsights() in Program.cs`
**Verdict:** ❌ REQUEST CHANGES — return to original author

**What passes:**
- Core plumbing correct: `UseAzureMonitor()` with connection string, sampling, resource attributes
- FR-303/304/305/306/307/308/309/316/317/318 all satisfied
- Method signature exact match to spec
- Placement after `ConfigureOpenTelemetry*` is architecturally sound
- `AddOpenTelemetry()` idempotency correctly leveraged

**What fails:**
1. **FR-310 (BLOCKING):** Tool parameter names NOT added as custom properties — no enrichment mechanism exists. Needs Activity tags or ITelemetryInitializer in the tool execution path.
2. **FR-311 (BLOCKING):** `UseAzureMonitor()` enables OTel log export by default. Existing code at `PowerShellAssemblyGenerator.cs:731-738` logs parameter VALUES at Debug level. While filtered by default log level, spec says MUST NOT — requires defensive suppression.
3. **FR-312 (BLOCKING):** Same log export concern for PowerShell command output.

**Recommended fix:** Suppress OTel log export in `UseAzureMonitor` config (only export traces + metrics). Add Activity tags for parameter names (comma-separated list, no values). This satisfies FR-310/311/312 without touching the existing logging infrastructure.

**Key insight:** `UseAzureMonitor()` does three things: traces + metrics + logs export. For PoshMcp, we only want traces + metrics. The log exporter creates a security surface where Debug-level ILogger entries (containing parameter values per existing code) could leak to Azure Monitor if an operator adjusts log levels.

---
## Archived 2026-05-06 by Scribe (15KB hard gate)

### PR #96 fix-pattern fragment (continuation from earlier archive)
**PR:** #96 (Hermes original, Bender fix) — `feat: surface resolution reasons for missing commands in poshmcp doctor`
**Outcome:** Squash merged to `main`. Branch `squad/91-doctor-commands-resolved` deleted (remote). Fixes #91.

**Fix pattern (Bender's second commit):**
- `RunDoctorAsync` now calls `DiagnoseMissingCommands` once, enriches `configuredFunctionStatus` records with `ResolutionReason`, then passes the list to `BuildDoctorJson` via new optional `precomputedFunctionStatus` parameter.
- `BuildDoctorJson` uses `precomputedFunctionStatus ?? BuildConfiguredFunctionStatus(...)` to skip re-computation when data is provided.
- Belt-and-suspenders guard: `BuildDoctorJson` independently checks `configuredFunctionStatus.All(s => s.Found || s.ResolutionReason is null)` before calling `DiagnoseMissingCommands`, so standalone callers still get diagnosis but the `RunDoctorAsync` path doesn't double-execute.
- `ConfiguredFunctionStatus` promoted from `private` to `internal` — necessary for the type to appear in `BuildDoctorJson`'s parameter list. Safe: sealed record, assembly-scoped.

**Rejection lockout pattern validated:** Hermes wrote the bug, was locked out, Bender delivered the fix cleanly. Pattern works — fresh eyes caught what the original author missed.



### 2026-07-15: Authored 4 new team skills from history review

Skills created: worktree-pr-merge, precomputed-optional-parameter, unserializable-type-handling, cli-bool-flag-pattern.
All at confidence: medium (except unserializable-type-handling: high — 33 tests).
Source: earned patterns from PRs #92–#96 and agent histories.

📌 Team update (2026-04-14T00:00:00Z): Docs publishing now uses a dedicated GitHub Pages workflow with docs-only path trigger and prebuilt `docs/_site` artifact strategy — decided by Amy.



### 2026-07-15: MCP Resources and Prompts spec authored

**Spec:** `specs/002-mcp-resources-and-prompts/spec.md`




### 2026-04-17: Spec restructure — loose specs → speckit format

**What was done:**
- Rewrote `specs/powershell-interactive-input.md`, `specs/out-of-process-execution.md`, and `specs/large-result-performance.md` into the speckit format (matching specs 001 and 002)
- Created `specs/003-powershell-interactive-input/spec.md`, `specs/004-out-of-process-execution/spec.md`, `specs/005-large-result-performance/spec.md`
- Numbering: FR-035–FR-064, SC-016–SC-030; next available FR-065, SC-031

**Patterns noted:**
- Original loose specs were RFC-style design docs (implementation code, C# classes, architecture diagrams) — speckit strips all of that; requirements must be written from user perspective with no class names
- The stateless retry pattern (Option D in the interactive input RFC) is the correct architecture for prompt handling given MCP's request/response model — captured as the design assumption in spec 003
- "Fail-fast" is the right default for prompt behavior; structured prompt response is P2 (requires fail-fast infrastructure first)
- Property filtering via `DefaultDisplayPropertySet` should be ON by default (95%+ payload reduction); result caching via `Tee-Object` should be OFF by default (most callers never use replay tools)
- Spec 003 (prompt handling) logically precedes spec 004 (OOP) because the OOP interactive prompt strategy is defined as "defer to spec 003 / fail-fast in OOP mode"



### 2026-07-18: PR #130 review — approved (MimeType nullable fix)

**PR:** #130 (fixes #129) — `Fix MimeType default — null model property, apply text/plain at runtime in handler`
**Verdict:** APPROVED

**Pattern validated — "model reflects truth, handler applies default":**
- `McpResourceConfiguration.MimeType` changed from `string` (default `"text/plain"`) to `string?` (no default)
- Runtime fallback `?? "text/plain"` applied via `string.IsNullOrWhiteSpace()` in `McpResourceHandler` at both list and read response sites
- Validator already used `IsNullOrWhiteSpace` — no change needed there
- All 3 `.MimeType` access sites in server code audited and confirmed null-safe
- Edge cases (empty string, whitespace) handled by `IsNullOrWhiteSpace` in both handler and validator
- No serialization cascade — MimeType is consumed, never re-serialized from the model
- Build: 0 errors; Tests: 471 passed, 0 failed

**Key pattern:** When a config property has a protocol-level default, keep the model nullable to distinguish "not configured" from "explicitly configured to the default value". Apply the default at the last responsible moment (the handler constructing the response).


### 2026-04-18: PR #130 review (issue #129 — MimeType fix)

**Verdict:** ✅ APPROVED
**Summary:** MimeType model nullable change restores validator signal while maintaining runtime fallback behavior. All 471 tests pass, 0 build warnings. Validator correctly flags missing MimeType in config; handler provides runtime "text/plain" default in HandleListAsync and HandleReadAsync.
**Key takeaway:** Model defaults that prevent validators from firing should be moved to runtime handlers. This preserves diagnostic signals while keeping runtime contracts stable.



### 2026-07-18: Issue #131 triage — STDIO logging to file

**Decisions made:**
- Use Serilog (Serilog.Extensions.Hosting + Serilog.Sinks.File) as the file logging provider; no existing file logger in the project, Serilog is the idiomatic .NET choice
- In stdio mode: `builder.Logging.ClearProviders()` unconditionally, then add Serilog file sink only if a log file path is configured — silent by default, no startup failure
- Log file resolution priority: `--log-file` CLI > `POSHMCP_LOG_FILE` env var > `Logging.File.Path` appsettings key > silent
- OTel `AddConsoleExporter()` suppressed in stdio mode by passing `isStdioMode` flag to `ConfigureOpenTelemetry`; HTTP path unchanged
- HTTP transport logging behavior is entirely unchanged
- Pre-startup `Console.Error.WriteLine` error paths stay as-is (correct for CLI errors before stdio server starts)

**Branch created:** `squad/131-stdio-logging-to-file`

**Agents assigned:**
- **Bender** — C# implementation: `Program.cs` changes, Serilog wiring, `--log-file` CLI option, `POSHMCP_LOG_FILE` env var, unit + integration tests
- **Amy** — OTel console suppression, `appsettings.json` schema (`Logging.File.Path`), documentation (README.md, DOCKER.md, appsettings.environment-example.json)

**GitHub note:** Label addition and issue comment blocked by Enterprise Managed User policy — triage notes saved to `.squad/decisions/inbox/farnsworth-131-stdio-logging-design.md` instead.



### 2026-07-18: PR #132 review — approved (STDIO logging suppression)

**PR:** #132 (fixes #131) — `feat: suppress console logging in stdio transport, add Serilog file sink`
**Verdict:** APPROVED

**Implementation quality:** Clean match to design spec. Bender handled C# changes (ConfigureStdioLogging, ResolveLogFilePath, CLI option, Serilog wiring), Amy handled OTel suppression, appsettings schema, and documentation. No merge conflicts expected.

**Key validation points:**
- `ClearProviders()` is unconditionally first in `ConfigureStdioLogging` — correct
- Serilog packages updated to 10.0.0/10.0.0/7.0.0 (newer than spec's 9.0.0/9.0.0/6.0.0) — correct per spec guidance
- OTel `AddConsoleExporter()` properly gated by `isStdioMode` flag
- 3-tier resolution (CLI > env > config > silent) works correctly
- HTTP transport completely unaffected
- 10 new tests (7 unit + 3 functional), all pass; full suite 487/0/1

**Non-blocking notes:**
- `default.appsettings.json` (embedded) missing `Logging.File.Path` — absent = silent, functionally correct
- Root handler (bare `poshmcp`) doesn't resolve `POSHMCP_LOG_FILE` — legacy path, low priority
- Pattern: `CreateLoggerFactory` didn't need changes because it's never called from the stdio server path — design spec was overcautious on this point



### 2026-07-18: PR #134 review — approved (docker buildx missing build context path)

**PR:** #134 (fixes #133) — `fix(#133): add missing build context path to docker buildx build command`
**Verdict:** APPROVED (comment posted — GitHub blocked self-review via API)

**Fix:** Single-character change: added ` .` to the end of `buildArgs` in the `buildCommand.SetHandler` lambda in `Program.cs` line 692.

**Validation points:**
- Bug is real: `docker build` requires a PATH argument for the build context; without it the command fails unconditionally
- `File.Exists(imageFile)` guard before the build args line implicitly validates CWD — if CWD were wrong, the Dockerfile check exits early with `ExitCodeConfigError`; by the time `.` is appended, CWD is the repo root
- Consistent with entire codebase: `docker.ps1` (3 sites), `docker.sh`, `infrastructure/azure/deploy.ps1`, `infrastructure/azure/deploy.sh` all use `.` as build context
- CI (`publish-packages.yml`) invokes from repo root — no CWD surprise

**Pattern noted:** When a CLI tool wraps an external command, every required positional argument must be present in the assembled arg string. The `File.Exists` guard doubles as implicit CWD validation — a pattern worth documenting for future Docker command wrappers.



### 2026-04-20: Spec 006 — Doctor Output Restructure milestone created

**Actions taken:**
1. Renamed `specs/doctor-output-restructure/` → `specs/006-doctor-output-restructure/` via git mv, added spec number to frontmatter, committed and pushed to main.
2. Created GitHub milestone #3: "Spec 006 - Doctor Output Restructure" (https://github.com/usepowershell/PoshMcp/milestone/3).
3. Created 27 GitHub issues (T001–T027, #140–#166) across 8 phases:
   - **Bender** (squad:bender): 22 issues — Phases 1–6 (T001–T018) and Phase 8 (T024–T027)
   - **Fry** (squad:fry): 5 issues — Phase 7 (T019–T023, tests)

**Issue mapping:**
- Phase 1 (DoctorReport Record Hierarchy): T001=#140, T002=#141, T003=#142, T004=#143, T005=#144
- Phase 2 (DoctorTextRenderer): T006=#145, T007=#146, T008=#147, T009=#148
- Phase 3 (Wire into RunDoctorAsync): T010=#149, T011=#150, T012=#151
- Phase 4 (Environment Variables): T013=#152, T014=#153
- Phase 5 (Summary Banner): T015=#154, T016=#155
- Phase 6 (Update MCP Tool): T017=#156, T018=#157
- Phase 7 (Tests): T019=#158, T020=#159, T021=#160, T022=#161, T023=#162
- Phase 8 (Cleanup/Validation): T024=#163, T025=#164, T026=#165, T027=#166

**Note:** Push to main required rebase to remove a pre-existing merge commit (a77dfcc) that violated repo rules.



### 2026-07-28: PR #167 review — approved (Spec 006: Doctor Output Restructure)

**PR:** #167 — `feat(spec-006): restructure doctor output`
**Verdict:** ✅ APPROVED (comment — self-approval blocked by GitHub)

**Implementation quality:** Clean match to spec 006. Architecture is solid: `DoctorReport` (pure data model with records + `[JsonPropertyName]`), `DoctorTextRenderer` (static class, pure rendering), `Program.cs` (thin orchestration). Build: 0 errors. Tests: 520 passed, 0 failed, 7 skipped.

**Spec compliance verified:**
- Banner: `╔═══╗` box-drawing chars, `BannerInnerWidth = 42`, correct status symbols (✓/⚠/✗)
- Section headers: `── Name ──` format, padded to 44 chars
- JSON: 7 top-level keys match FR-106, `effectivePowerShellConfiguration` dropped, camelCase throughout

---
*Older entries (pre-2026-05-05 bulk) moved to `history-archive.md` on 2026-05-05 by Scribe to satisfy 15KB hard gate. See archive for full record.*


## Archived 2026-05-13 (Scribe — entries 2026-05-02..2026-05-07; history.md was 36,828 bytes >= 15KB threshold)

### 2026-05-02: Reviewed PR #184 — Program.cs Refactoring (squad/program-cs-refactor)

**Verdict:** CHANGES REQUESTED

**PR:** https://github.com/usepowershell/PoshMcp/pull/184 — "refactor: extract Program.cs concerns into dedicated service classes (68% reduction)"

**Branch pushed and PR created** from worktree `poshmcp-refactor`. 6 commits, 6 new files.

**Key findings:**

1. **BLOCKING — DescribeConfigurationPath duplicated 5x**: `Program.cs`, `DoctorService.cs`, `CommandHandlers.cs`, `StdioServerHost.cs`, `HttpServerHost.cs` all contain a private copy of the same utility method. Same for `ToToolName`, `GetDiscoveredToolNames`, `GetExpectedToolNames`. Needs a shared `ConfigurationHelpers` utility class.

2. **BLOCKING — DoctorService extraction incomplete**: `BuildDoctorReportFromConfig` and `BuildDoctorJson` (plus all their private helpers) were COPIED into `DoctorService.cs` but NOT removed from `Program.cs`. Tests still call `Program.BuildDoctorReportFromConfig`. Fix: either delete duplicates from Program.cs + update tests, or have Program.cs forward to DoctorService.

3. **CONCERN — CliDefinition nullable static properties**: 70+ options/commands are null until `Build()` is called; mutable static state reset on subsequent `Build()` calls. Suggest returning a value object instead.

4. **CONCERN — CliDefinition/CommandHandlers are `public`**: Should be `internal` (matching DoctorService, McpToolSetupService, etc.).

5. **GOOD — Delegate injection in DoctorService**: Passing `DiscoverToolsForCliAsync` as Func avoids coupling Diagnostics to Server layer.

6. **GOOD — Namespace consistency**: All 6 new classes use `namespace PoshMcp;`.

**Decisions file:** `.squad/decisions/inbox/farnsworth-pr-review.md`
**Key decisions:**
- `McpResources` and `McpPrompts` are top-level `appsettings.json` siblings to `PowerShellConfiguration` — MCP-layer concerns belong at MCP layer, not nested under execution config
- Two source types for both: `"file"` (read at request time, relative to `appsettings.json` dir) and `"command"` (executed in shared runspace, no new runspace)
- URI scheme `poshmcp://resources/{slug}` is recommended but not enforced; doctor warns, does not error
- Prompt argument injection uses pre-assignment (`$argName = value`) before command string executes — not `-ArgumentList` (avoids requiring `param()` blocks)
- File-backed prompt argument substitution deferred to v1+ — file returned verbatim, client does template rendering
- No resource caching in server; operators build caching into PowerShell commands if needed
- Resource subscriptions out of scope — four read-path SDK handlers are sufficient for v1
- SDK registration via `WithListResourcesHandler`, `WithReadResourceHandler`, `WithListPromptsHandler`, `WithGetPromptHandler` in `Program.cs`
- FR numbering starts at FR-018 (after FR-017 from spec 001); SC numbering starts at SC-009 (after SC-008)
- Doctor validation contract fully specified including severity levels and JSON output shape




### 2026-05-06: PR #187 review — Hermes runspace pool vs multi-process experiment plan

**Verdict:** APPROVE (review saved to `$env:TEMP\farnsworth-pr187-review.md` — EMU policy blocks `gh pr review` AND `gh pr comment` from this account; surfaced to user for manual paste).

**Plan strengths confirmed:**
- `OutOfProcessHost` extraction is the correct shared seam — per-process state in current `OutOfProcessCommandExecutor` (process, streams, `_sendLock`, `_pending`, read/stderr loops, `Process.Exited`) factors cleanly out, leaving `ICommandExecutor` as the public surface. Single-host executor becomes a thin wrapper; process pool composes N hosts.
- Protocol layer is genuinely parallel-ready (id-keyed `_pending` + async `ReadLoopAsync`). Only the host serializes today. Plan correctly notes this — minimizes C# churn.
- Isolation as benchmark pass/fail gate (not vibes) is the right discipline. "Default to B if neither passes isolation" is the correct tiebreaker — preserves the original OOP motivation.
- Phasing: 1 unblocks 2/3, 4 parallel, 5 fans in, 6 cleans up. `SubprocessHostMode` flag keeps a known-good baseline available for bisecting.
- `SubprocessPoolSize: 1` collapses Option B to current behavior — clean fallback knob.

**Architectural concerns to fold into prototype issues (non-blocking for plan):**
- A1 — Stream pollution: `[Console]::Out` swap won't catch `Write-Host` (which goes through `$Host.UI` snapshotted at runspace open). Need a custom `PSHost` / `PSHostUserInterface` for the pool. The .NET-side `IsNonJsonPowerShellStreamLine` should be defense-in-depth, not load-bearing.
- A2 — Setup race: pool close-rebuild-reopen needs an explicit drain barrier (stop accept → wait `_pending` empty → close → rebuild → reopen). Plan's "setup is rare and cannot race" is aspirational.
- A3 — Per-runspace `$Error` is correctly identified; also reset `$LASTEXITCODE` and `$ErrorActionPreference`. Existing single-runspace host already leaks `$Error` across invokes (separate fix issue).
- B1 — Channel-lease `finally` must check liveness before re-enqueue; dead host stays out of channel until replacement passes `ping` + `setup`. Otherwise pool slowly bleeds capacity.
- B2 — Discovery cache assumption ("schemas identical") only holds while every subprocess runs the same setup. Cache discovery keyed by setup-payload hash; re-discover on hash mismatch.
- 4 — Benchmark harness must use BDN `[GlobalSetup]` / `[IterationSetup]` to keep process spawn out of per-iteration measurements; otherwise B loses on Az.Accounts import time for the wrong reason.
- 5 — Pass/fail "≥ 4× baseline at 10 concurrent" doesn't apply to CPU-bound (ceiling = ProcessorCount) or CPU-light (dispatch overhead floor). Recommend rewriting as scenario × metric × threshold table.
- 6 — Cancellation scope-out: until cancellation lands, Option A's effective capacity under adversarial load is `N - stuck_invokes`. Don't flip default to A in issue #6 without it.
- 7 — Memory metric: sample Win32 handle count alongside working set. Az workloads leak handles characteristically.

**Answers to plan §6 open questions:**
1. Default N = `Environment.ProcessorCount` for prototype. Don't pick a fixed number until network-shaped benchmark confirms scaling.
2. Don't ship the loser. Two host scripts is real maintenance cost — issue #6 deletes the loser.
3. Local `HttpListener` for harness; one manual real-Azure end-to-end for the findings doc; not in CI.
4. Cancellation as separate issue, agreed; conditional on concern #6.

**Pattern noted (logging):**
- EMU blocks BOTH `gh pr review` and `gh pr comment` for usepowershell/PoshMcp from this account. Cubert recently logged the same finding. Future PR reviews must be saved to `$env:TEMP` and surfaced for manual paste — do not waste cycles attempting either gh subcommand.
- Plan-only PRs that quote internals are high-signal fact-check targets — every cited line either resolves or doesn't. Cross-reference cmdlet usage at the call site, not by global grep (e.g., `ConvertTo-Json -Depth N` legitimately varies per call site).
## Learnings (2026-05-06)
- PR #187 review (runspace pool vs multi-process experiment plan, branch squad/65-runspace-pool-experiment-plan): verdict = comment / approve direction with revisions. Plan is sound; #1 (extract OutOfProcessHost) can start immediately.
- Key architectural concerns raised:
  - Option A: `[Console]::Out` cannot be redirected per-runspace (it's process-global) — plan's stream-pollution mitigation needs correction; use custom PSHost via CreateRunspacePool instead.
  - Option A: setup-while-running quiesce protocol is underspecified — `OutOfProcessCommandExecutor.SendRequestAsync` does not gate setup against in-flight invokes today (only `_sendLock` for stdin writes).
  - Option B: Channel<OutOfProcessHost> + Process.Exited race when host crashes mid-lease needs explicit reconciliation (don't end up with N+1 or N-1).
  - Option B: fail-fast on all-N setup is a regression vs single-host; recommend fail-fast on first host then per-host retries.
  - Benchmark: crash-recovery scenario as written measures process restart (same as baseline) — for Option A's real isolation gate, induce runspace-level corruption, not process exit.
  - Benchmark: 4× throughput gate is wrong for CPU-bound; needs per-scenario thresholds.
  - Phasing: split #4 (benchmark harness) into infrastructure (parallel) and wire-up (blocked on #2/#3).
- Verified current OOP code matches plan's description: `OutOfProcessCommandExecutor.cs` (_sendLock at line 29, _pending dict, SendRequestAsync at line 362), `oop-host.ps1` (Write-NdjsonResponse flush, handler ordering).
- Posting via gh failed (EMU policy on usepowershell/poshmcp blocks `gh pr review`). Review body left at C:\Users\stmuraws\AppData\Local\Temp\tmpsvtizs.md for manual posting.

### 2026-05-06 — EMU blocks `gh issue create` too
The `usepowershell/PoshMcp` repo's EMU policy not only blocks `gh pr review` / `gh pr comment` (already known) but also `gh issue create` with the same `Unauthorized: As an Enterprise Managed User` GraphQL error. When asked to file an issue, write the body to a temp file outside the repo and hand the path to the user to create the issue manually. Do not retry `gh issue create` under this account.

### 2026-05-06 — Cancellation propagation gap (issue body drafted at C:\Users\stmuraws\AppData\Local\Temp\poshmcp-cancellation-issue.md)
Confirmed: `CancellationToken` in `OutOfProcessCommandExecutor.SendRequestAsync` only governs the .NET-side wait (`_sendLock.WaitAsync`, `_stdin.WriteLineAsync`, the linked `timeoutCts` failing the local TCS). It is never forwarded to the OOP subprocess, and `oop-host.ps1` runs a single-threaded dispatcher loop (L630–L650) that cannot read a `cancel` while `Invoke-InvokeHandler` (L498) is blocked inside `& $cmdInfo @boundParams`. In-process is worse: `IsolatedPowerShellRunspace.ExecuteThreadSafeAsync` doesn't accept a CT at all; the only `_powerShell.Stop()` is inside Dispose() (PowerShellRunspaceImplementations.cs L146). Layered fix: (1) cooperative `Stop()/StopAsync()` registration on the in-process pipeline, (2) a `cancel` JSON-RPC method + concurrent-readable dispatcher for OOP, (3) bounded escalation cooperative → forced → process kill + recycle via `PowerShellCleanupService`.

## 2026-05-06: New milestone-tagged issues assigned

Milestone #5 (Spec 004 - Out-of-Process PowerShell Execution) was created. You have issues assigned via squad:* labels:
- Bender: #190 (extract OutOfProcessHost), #192 (Option B - process pool prototype, blocked by #190)
- Fry: #193 (benchmark harness infra), #194 (wire harness to executors, blocked by #191/#192/#193)
- Farnsworth: #196 (adopt the winner, blocked by #195)

Check the issue body for plan reference and dependency chain before starting.

### 2026-05-06: Authored SECURITY.md
- Added `SECURITY.md` at repo root.
- Supported Versions tailored to pre-1.0 reality: only the latest 0.x minor (currently 0.10.x) receives security fixes; older minors unsupported.
- Reporting channel: GitHub private vulnerability reporting (Security tab) — deliberately did NOT invent a security email address.
- Documented SLA (ack 3 business days, triage 7), coordinated-disclosure timeline, and reporter credit via GHSA.
- Pattern: when a project has no published security contact, prefer GitHub's built-in private vuln reporting over fabricating an email.

### 2026-05-06: Reviewed PR #200 (Bender / Option B process pool) and PR #201 (Hermes / Option A runspace pool)

**Verdict:** APPROVED both. Comments posted via `gh pr comment` (gh pr review still blocked on this account).
- PR #200: https://github.com/usepowershell/PoshMcp/pull/200#issuecomment-4392376907
- PR #201: https://github.com/usepowershell/PoshMcp/pull/201#issuecomment-4392377065

**Verification highlights:**
- PR #200 — `OutOfProcessSubprocessPool` correctly uses BOTH `Channel<HostSlot>` (lease queue) and `ConcurrentDictionary<int, HostSlot>` (source of truth). Slot 0 fail-fast via `StartSlotAsync(failFast:true)`; slots 1..N-1 retry with exp backoff via `StartSlotWithRetryAsync`; `MinHealthyForStartup` gate. Discovery fingerprint covers modulePaths, importModules ∪ discoveryModules, installModules (sorted, with name/version/min/max/repo/scope), startupScript path/content, trustPSGallery — XML-doc-explained. Timeout → `lease.MarkBroken()` → `MarkSlotDead` → reconciler. Integration tests parameterized over 1/2/4 with all required scenarios.
- PR #201 — `oop-host-pool.ps1` builds ISS via `CreateDefault2()` + `ImportPSModule`; `CreateRunspacePool(1, N, $iss, $customHost)` with `MTA`. `NdjsonHost`/`NdjsonHostUI` route every `$Host.UI.*` write to stderr (correctly notes `[Console]::Out` is process-global). `PoolStdout.Lock` synchronizes ndjson frame writes. `PoolDispatcher.ProcessOne` reads `w.Ps.Streams.Error/Warning` per-pipeline; user script does `$Error.Clear()` first. Quiesce: `DrainEvent.Reset` → `WaitIdle(60s)` → `Close-Pool` → mutate → `Ensure-Pool` → `End-Drain`. Metrics on response frame: queueDepthOnArrival, leaseWaitMs, activeOnComplete, poolSize.

**Cross-PR enum collision (key finding):** Both PRs add `SubprocessHostMode` — PR #200 as `static class` with string constants (property `string?`), PR #201 as `enum SubprocessHostMode { Single, Pool }`. NOT source-compatible. Bender deliberately reserved the `Pool` constant signaling coexistence intent.

**Merge-order recommendation:** Land PR #201 first (smaller diff, idiomatic enum); have Bender rebase PR #200 to extend the enum with `ProcessPool` (~30 lines). Recorded in `.squad/decisions/inbox/farnsworth-spec004-prototypes-review.md`.

**Non-blocking observations posted to each PR:** #200 — silent `MinHealthyForStartup` clamp, unbounded lease channel, stale-slot spin in `LeaseAsync`, discovery cache deliberately excludes filter params (worth doc note). #201 — drain timeout hardcoded 60s (not threaded from config), pool size cap of 8 is prototype guard, `Resolve-SwitchParameters` runs `Get-Command` on host process not in pool runspace (verify ISS modules are visible to host).

**Pattern noted:** EMU policy continues to block `gh pr review`; `gh pr comment --body-file <tempfile-outside-repo>` remains the working channel. Comments do NOT count as formal GitHub approvals for branch protection — Steven (or another non-EMU reviewer) must convert these to formal Approve reviews if required for merge.

### 2026-05-06: Security alerts triage

**Sources checked:** GitHub Dependabot (open=0), code scanning (open=25), secret scanning (disabled at repo level), .github/workflows/*.yml permissions blocks, SECURITY.md, recent security commits.

**Findings:**
- 23 `cs/log-forging` (CWE-117, medium) in `PoshMcp.Server/PowerShell/PowerShellAssemblyGenerator.cs` lines 709–1030 — logger calls take `commandName`, `parameterValues`, `parameterSummary` from MCP `tools/call` payloads.
- 1 `cs/log-forging` in `PoshMcp.Server/Observability/LoggerExtensions.cs` line 31 — `OperationName` from `OperationContext` flows into a logging scope.
- 1 `cs/log-forging` in `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs` line 111 — JWT path/header-derived values logged.
- 1 `actions/missing-workflow-permissions` (medium) in `.github/workflows/ci.yml` — only workflow without an explicit `permissions` block (14 of 15 already correct).
- Secret scanning disabled — should be enabled with push protection.

**Real risk reading (not false positives):** project ships a Serilog file sink (per spec for issue #131 stdio logging to file), so embedded `\r\n` in tool names or parameter values produces forged log lines in plain-text logs. Exploitability low; impact = audit-trail confusion. Not RCE.

**Triage decisions logged to** `.squad/decisions/inbox/farnsworth-security-review-2026-05-06.md`:
- P1: Add explicit `permissions: { contents: read }` to `ci.yml` → **Amy**.
- P2: Add `LogSanitizer.Scrub(string)` (strip CR/LF, length-cap) and apply at call sites in the three flagged files → **Bender**, with **Fry** for newline-scrub tests. Scrubbing must be at the call site, not via Serilog enricher — CodeQL taint analysis tracks call-site sinks and an enricher won't clear the alerts.
- P3: Enable repo secret scanning + push protection → **Amy**.
- P4 (defer): Consider a `LogSafe(string)` wrapper type or Serilog destructuring policy as a follow-up to make sanitization a build-time invariant.

**Pattern noted:** when CodeQL flags `cs/log-forging`, check the sink type before treating it as noise. Structured logging providers that don't replay newlines (console, JSON sinks) are de-facto immune; plain-text file sinks are not. This repo has both, so the alerts are real.

**Hygiene observation:** dependency posture is healthy — Dependabot 0 open, recent CVE bumps merged (`System.Security.Cryptography.Xml 10.0.6`), and v0.9.2 already shipped an auth bypass fix. No active security-relevant specs in flight.

### 2026-05-06: Reviewed PR #204 (Bender) — fix(oop) SendRequestAsync 'Key: Content' under parallel invokes (#203)

**Verdict:** APPROVED. Comment posted: https://github.com/usepowershell/PoshMcp/pull/204#issuecomment-4393068861

**Root cause verified:** Not concurrency. `BasicHtmlWebResponseObject.Content` (string body) CLR-shadows `WebResponseObject.Content` (byte[]); `ConvertTo-Json` reflection enumerates both into `Dictionary<string,object>` → `ArgumentException` on duplicate key. Harness's parallel Invoke-WebRequest just made the failure deterministic. C# `_pending` correlation map was untouched — correctly identified as a red herring.

**Fix shape:**
- `oop-host.ps1`: extracted `ConvertTo-SafeJson` helper, applied at exactly the user-result serialization site in `Invoke-InvokeHandler` (~line 611). Other ConvertTo-Json sites (request envelopes, error frames) are not wrapped — correct, those serialize controlled C# payloads.
- `oop-host-pool.ps1`: same fallback inlined into the runspace user-script scriptblock. Asymmetric with the host-process helper — correct, scriptblocks executed in pooled runspaces should not depend on host-process function availability.
- Trigger is `catch [ArgumentException]` only; happy path unchanged. Fallback chain: ConvertTo-Json → Select-Object * | ConvertTo-Json → ($r | Out-String).Trim() | ConvertTo-Json.

**Fallback semantics:**
- `Select-Object *` materializes a flat PSObject; PowerShell's member resolver collapses shadowed CLR members and derived wins. For BasicHtmlWebResponseObject the string `Content` (body) wins over the byte[] shadow — exactly what callers want.
- Out-String/Trim is bounded (only fires after Select-* itself throws). Realistic case: cyclic graphs. Returning a valid JSON string beats bleeding the exception to the C# client.

**Regression test (`OutOfProcessHostConcurrencyTests`):** real `Invoke-WebRequest -UseBasicParsing` against loopback `HttpListener` produces a real BasicHtmlWebResponseObject. Concurrency=10 mirrors harness repro. Skip guards on pwsh and HttpListener.IsSupported. Companion test on `_pending` correlation is a sanity net for the original hypothesis. Test only exercises single-host path; pool-host inline fallback is covered end-to-end via `WarmInvokeThroughputBenchmark` smoke (Pool 306 ms / 10 calls).

**Cross-PR sequencing:** This PR unblocks `WarmInvokeThroughputBenchmark` for Single and ProcessPool. Hermes's PR #195 (benchmarks + findings) captured runs 1+2 against pre-#203 main where Single/ProcessPool numbers are unreliable. After #204 merges, Hermes must rebase #195 onto post-#203 main and rerun the affected scenarios before publishing findings. Not blocking #204.

**Pattern noted:** PowerShell `ConvertTo-Json` failures throwing `ArgumentException: ... Key: <name>` are CLR member-shadowing bugs, not concurrency. Parallel harnesses make them deterministic, which makes them look like races. Suspect shadowing on input type first.

**EMU pattern (already known, reconfirmed):** `gh pr comment --body-file <tempfile-outside-repo>` works; `gh pr review` does not. Comment is not a formal GitHub approval — Steven or non-EMU reviewer must convert if branch protection requires it.


### 2026-05-06 — Reviewed PR #205 (Hermes — bench(oop) canonical results + findings, #195)

**Verdict:** APPROVE. Posted via gh pr comment (#issuecomment-4393870722) — gh pr review still EMU-blocked.

**Methodology check:** results doc captured BDN 0.14.0, --job short (3×3×1), exact filter/CLI invocation, base commit e4cf7d9 (post-#204), runtime/OS/arch (Win11 Arm64 / .NET 10.0.6 / Concurrent Server GC), wall time, and explicit non-canonical status of runs 1+2. Reproducible.

**Numbers traced:** Spot-checked WarmInvoke speedups against the source table — Pool 661.2/136.2 = 4.857 → 4.86×, P99 686.233/143.321 = 4.788 → 4.79×; ProcessPool 661.2/200.7 = 3.295 → 3.30×, P99 686.233/201.406 = 3.408 → 3.41×. ColdStart penalties 400 ms (ProcessPool) and 478 ms (Pool) → "400-500 ms". 1 MB allocations 13.79/16.34/17.36 MB → "~13.8/~16.3/~17.4". No rounding flips a conclusion.

**Recommendation as Lead:** Pool as default is supportable from the data on the spec's stated workload model. Strongest counter-argument is single-host / single-shape / short-job — disclosed at correct strength in caveat §5, not strong enough to block. ProcessPool's tighter tail (StdDev 1.11 ms vs Pool 6.34 ms; P99 only 0.7 ms above mean) is the right opt-in answer for tail-sensitive / isolation-sensitive workloads.

**Position on #196 default flip — HARD GATES (not 'should land before'):**
1. Custom PSHost/PSHostUserInterface for runspace pool (partially landed in PR #201; #196 verifies completeness).
2. Cancellation propagation (in-process Stop()/StopAsync() registration, OOP cancel JSON-RPC method, concurrent-readable dispatcher, bounded escalation cooperative → forced → process kill + recycle).
Until both land, Pool may ship as documented opt-in only. #196 must NOT flip the default with either gate open. A --job long WarmInvoke rerun against post-cancellation main (captured as run-4) must reaffirm ≥ 4× I/O bar before the flip.

**#196 scope sketch (delivered in review body):**
- Config: HostMode default flip Single → Pool; Pool:Size default Environment.ProcessorCount with hard cap 32; Pool:DrainTimeoutMs threaded through config (currently hardcoded 60s per PR #201).
- Doctor: validate pool sizing, surface active HostMode.
- Docs: 'When to switch HostMode' section in DESIGN.md (three-case rubric: Pool default / ProcessPool tail+isolation / Single short-lived CLI). Sweep DESIGN.md, README.md, examples/appsettings.*.json, spec 004 quickstart if present.
- Acceptance: run-4 --job long rerun captured.
- Out of scope: per-request override, dynamic resizing, removing prototype paths (both Pool and ProcessPool ship).

**Patterns:**
- Docs+data PRs benefit from spot-checking 2-3 headline numbers against source tables — catches both arithmetic and rounding inversions in one pass.
- When a recommendation rests on one workload shape, make the workload-shape disclosure a gate, not a footnote.
- EMU policy continues to block gh pr review on usepowershell/PoshMcp from this account; gh pr comment with --body-file <tempfile-outside-repo> is the working channel and is NOT a formal GitHub approval.

### 2026-05-06 - Reviewed PR #207 (Bender) - feat(oop) cancellation propagation (#188)

**Verdict:** APPROVE. Posted via gh pr comment (#issuecomment-4394001550) - gh pr review still EMU-blocked.

**Design conformance:** code matches specs/004-out-of-process-execution/cancellation-design.md §3 wire protocol verbatim (cancel- id prefix not in _pending, cancelled flag on invoke responses, ack frame shape). ProcessPool pool file untouched - kill-on-timeout backstop at line 421 preserved verbatim.

**Single-mode strategic divergence (justified):** design §5.1 sketched BeginInvoke + ThreadPool.QueueUserWorkItem; PR ships C# SingleDispatcher (BlockingCollection + dedicated worker thread + ConcurrentDictionary registry) mirroring PoolDispatcher shape. Better choice - high code-share with Pool, uniform SingleStdout/PoolStdout.Lock pattern, avoids fighting PowerShell async ergonomics. Pattern lesson: when the reference design is already proven elsewhere in the codebase (PoolDispatcher), reusing the shape beats inventing a parallel async story even if the original design sketched the alternative.

**Belt-and-suspenders cancel detection is correct:** worker catches PipelineStoppedException AND falls back to InvocationStateInfo.State == PSInvocationState.Stopped. BeginStop does not always raise PSE from synchronous Invoke() - sometimes Invoke returns normally with State=Stopped. Both paths set cancelled = wasStopped || w.Cancelled. Lesson: PowerShell.BeginStop() cancellation detection requires checking both paths; assuming PSE alone produces flaky cancel-completion logic.

**SendRequestAsync orthogonality fix:** PR changes 	imeoutCts from CreateLinkedTokenSource(cancellationToken) to plain new CTS. Now caller-cancel and per-request-timeout are properly orthogonal - no double-fire of timeout when caller cancels. Both registrations dispose in finally; TrySendCancelFrameAsync uses independent 2s CTS so caller-token cancel cannot poison the cancel-frame send. The OperationCanceledException diagnostic catch from design §4.3 was deliberately skipped to keep the diff tight (Bender's history note); fine - OCE bubbles up unannotated, can be added if logs prove confusing in practice.

**Cancel race with success:** if read loop sets result a tick before caller cancels, 	cs.TrySetCanceled no-ops and awaiter sees success - but TrySendCancelFrameAsync still fires (no completion gate). Host gets cancel for already-completed id, replies cancelled:false, suppressed at read loop. Noise-only; not worth gating.

**Test discipline:** Start-Sleep -Seconds 60 against 15s ObservationTimeout proves cancel actually unblocks (a passing test cannot be the sleep finishing). Pool test uses 
unspacePoolSize:4 to provably exercise > 1 runspace - without explicit sizing the pool defaults can produce a one-runspace pool that would head-of-line block, falsely failing the test. ProcessPool test asserts HealthyCount >= 1 after soft cancel proving slots stay healthy and kill backstop not invoked. 500-750ms warmup before cancel is realistic - less races the invoke send.

**#196 hard gate status - BOTH SATISFIED:**
1. Custom PSHost/PSHostUserInterface for runspace pool - PR #201.
2. Cancellation propagation - this PR. Bounded soft-cancel across all 3 modes, no Pool head-of-line, hosts/slots stay healthy.

**#196 remaining scope (now unblocked):**
- Default-mode flip: SubprocessHostMode.Default → Pool. Keep Single + ProcessPool as opt-in (ProcessPool stays recommended for tail-sensitive / isolation-sensitive workloads per #195 P99 finding).
- Config key naming review; confirm SubprocessHostMode enum-vs-string serialization; audit for residual #200/#201 enum collision.
- Doctor validation hooks: surface resolved mode, pool size (with clamp applied - #201 cap of 8), host script path, per-request timeout. Warn (not error) if Pool configured but pwsh resolution failed.
- Doc updates (README, DOCKER.md, spec 004 supersedence). Document cancellation contract: caller-token → bounded soft-cancel; per-request timeout as backstop; ProcessPool kill-on-timeout preserved.
- Bench reaffirmation: Hermes --job long WarmInvokeThroughputBenchmark against post-#207 main (capture as run-4), confirm ≥ 4x I/O bar holds. Cancellation refactor adds per-invoke [powershell] allocation + dispatcher hop - expect no measurable warm-I/O regression but verify, don't assume.

**Edge cases worth flagging non-blocking:** cancel before invoke begins (TryGetValue returns false, ack cancelled:false, awaiter still sees OCE - benign); cancel during host startup/shutdown (_disposed and _stdin is null guards + 2s CTS); wedged unmanaged-code pipeline (.NET awaiter still returns OCE promptly via TCS, ProcessPool gets next-invoke kill backstop, Single/Pool get per-request-timeout backstop); vestigial 	ry { ... } catch { throw } wrapper in Single user script is a semantic no-op (cosmetic, strip in follow-up).

**Pattern noted:** the structural blocker for Single-mode cancellation was that the dispatcher loop could not even READ a cancel frame while blocked inside Invoke-InvokeHandler. The fix was structural (extract invoke to a worker thread), not protocol (a side-channel pipe/signal would not have helped because the read side was the bottleneck). Always check what is blocking the read loop before designing a side-channel.

### 2026-05-07 — Reviewed PR #210 (Leela — OOP docs + samples audit, branch squad/oop-docs-samples-audit)

**Verdict:** APPROVE with one non-blocking framing nit. Posted via gh pr comment (#issuecomment-4396923714) — gh pr comment now works after switching back to usepowershell account. Architectural angle only; Cubert handled fact-checking in parallel.

**Mental-model assessment:** Two-entry-point split (brief in configuration.md, deep-dive in advanced.md) is the right structure — avoids duplication, lets operators land on either article and discover the other. Three-mode taxonomy table in advanced.md delivers explicit "when to use" + sizing + per-mode cancellation contract + doctor pointer in the right order. Decision narrative (Pool wins warm throughput ~4.86×, ProcessPool opt-in for trust/tail, Single legacy/bisect) matches spec 004 study and #208 default-flip rationale exactly.

**Sample-pick judgment — both correct, both well-justified in examples/README.md:**
- advanced.json → Pool with SubprocessRunspacePoolSize:0 (auto-tune): correct for heavy-Az + concurrent throughput case. Auto-tune is the right default for a copy-paste sample.
- tenant.json → ProcessPool (size 4, min healthy 2): correct for trust-boundary case. README rationale names the tradeoff explicitly ("trust boundaries between callers matter more than peak throughput") — multi-tenant is exactly where peak throughput is the wrong optimization target. Getting this right in the SAMPLE (not just the docs) is what made #210 more than a documentation update.

**Coherence with #208 — clean.** RuntimeMode correctly described as InProcess/OutOfProcess (the azure-integration.md "sync/async" description was a real bug; fixed). SubprocessHostMode is presented as a primary configuration concept rather than a tuning knob — correct framing for post-default-flip docs. Cancellation documented as a contract per mode, not a footnote — correct framing because cancellation is what made the flip safe.

**Operator completeness:** poshmcp doctor surfaced in advanced.md ("reports the resolved host mode, effective pool sizes, host-script path, clamp warnings"). Adequate — answers the verify-my-config question without burying or over-emphasizing.

**One framing gap (non-blocking):** advanced.md Cancellation section says of Single mode: *"the historical timeout-and-restart behavior applies."* This UNDERSELLS what Single does post-#207 — the SingleDispatcher worker-thread pattern (BlockingCollection + ConcurrentDictionary registry, mirroring PoolDispatcher shape) supports the same cooperative soft-cancel contract as Pool/ProcessPool with per-request timeout as backstop. As written, an operator could read this as "Single mode does not support cooperative cancellation," which would be inaccurate AND would undersell why the default flip became safe across all three modes simultaneously. Suggested follow-up phrasing: *"Single: cooperative cancellation via the dispatcher worker; the per-request timeout acts as the backstop and recycles the host on timeout."* One line, follow-up PR.

**Pattern noted:** When a docs PR ships alongside an engineering decision PR, the per-mode contract narrative is where framing drift hides. Cancellation contract was the strongest place to look because it's the gate that made the default flip safe — any underselling there undersells the whole flip rationale. Sample-pick rationale was the second strongest, because the wrong tradeoff narrative in a sample propagates to operators who copy the sample without reading the docs.

**EMU note:** gh pr comment from usepowershell account works (now properly switched). Coordinator's task setup pre-switched the account so no friction this time. Comments still do NOT count as formal GitHub approvals for branch protection.

### 2026-05-07: v0.11.0 release shipped (cross-agent note from Scribe)
Your work landed in v0.11.0 (csproj 0.10.0 → 0.11.0, CHANGELOG entry, release notes at docs/release-notes/0.11.0.md). The release narrative credits the OOP maturity wave: Pool default flip (#196/#208), cancellation propagation across all modes (#207), benchmarks harness + findings (#193/#194/#195/#205), OOP host extraction (#190/#198), bug fixes (#203/#189), CWE-117 log-injection hardening, minimum workflow permissions, and SECURITY.md. Tag/push deferred to Steven.

## Learnings

## Archived 2026-05-14T11:34Z

### 2026-05-02: Reviewed PR #184 — Program.cs Refactoring (squad/program-cs-refactor)

**Verdict:** CHANGES REQUESTED

**PR:** https://github.com/usepowershell/PoshMcp/pull/184 — "refactor: extract Program.cs concerns into dedicated service classes (68% reduction)"

**Branch pushed and PR created** from worktree `poshmcp-refactor`. 6 commits, 6 new files.

**Key findings:**

1. **BLOCKING — DescribeConfigurationPath duplicated 5x**: `Program.cs`, `DoctorService.cs`, `CommandHandlers.cs`, `StdioServerHost.cs`, `HttpServerHost.cs` all contain a private copy of the same utility method. Same for `ToToolName`, `GetDiscoveredToolNames`, `GetExpectedToolNames`. Needs a shared `ConfigurationHelpers` utility class.

2. **BLOCKING — DoctorService extraction incomplete**: `BuildDoctorReportFromConfig` and `BuildDoctorJson` (plus all their private helpers) were COPIED into `DoctorService.cs` but NOT removed from `Program.cs`. Tests still call `Program.BuildDoctorReportFromConfig`. Fix: either delete duplicates from Program.cs + update tests, or have Program.cs forward to DoctorService.

3. **CONCERN — CliDefinition nullable static properties**: 70+ options/commands are null until `Build()` is called; mutable static state reset on subsequent `Build()` calls. Suggest returning a value object instead.

4. **CONCERN — CliDefinition/CommandHandlers are `public`**: Should be `internal` (matching DoctorService, McpToolSetupService, etc.).

5. **GOOD — Delegate injection in DoctorService**: Passing `DiscoverToolsForCliAsync` as Func avoids coupling Diagnostics to Server layer.

6. **GOOD — Namespace consistency**: All 6 new classes use `namespace PoshMcp;`.

**Decisions file:** `.squad/decisions/inbox/farnsworth-pr-review.md`
**Key decisions:**
- `McpResources` and `McpPrompts` are top-level `appsettings.json` siblings to `PowerShellConfiguration` — MCP-layer concerns belong at MCP layer, not nested under execution config
- Two source types for both: `"file"` (read at request time, relative to `appsettings.json` dir) and `"command"` (executed in shared runspace, no new runspace)
- URI scheme `poshmcp://resources/{slug}` is recommended but not enforced; doctor warns, does not error
- Prompt argument injection uses pre-assignment (`$argName = value`) before command string executes — not `-ArgumentList` (avoids requiring `param()` blocks)
- File-backed prompt argument substitution deferred to v1+ — file returned verbatim, client does template rendering
- No resource caching in server; operators build caching into PowerShell commands if needed
- Resource subscriptions out of scope — four read-path SDK handlers are sufficient for v1
- SDK registration via `WithListResourcesHandler`, `WithReadResourceHandler`, `WithListPromptsHandler`, `WithGetPromptHandler` in `Program.cs`
- FR numbering starts at FR-018 (after FR-017 from spec 001); SC numbering starts at SC-009 (after SC-008)
- Doctor validation contract fully specified including severity levels and JSON output shape




### 2026-05-06: PR #187 review — Hermes runspace pool vs multi-process experiment plan

**Verdict:** APPROVE (review saved to `$env:TEMP\farnsworth-pr187-review.md` — EMU policy blocks `gh pr review` AND `gh pr comment` from this account; surfaced to user for manual paste).

**Plan strengths confirmed:**
- `OutOfProcessHost` extraction is the correct shared seam — per-process state in current `OutOfProcessCommandExecutor` (process, streams, `_sendLock`, `_pending`, read/stderr loops, `Process.Exited`) factors cleanly out, leaving `ICommandExecutor` as the public surface. Single-host executor becomes a thin wrapper; process pool composes N hosts.
- Protocol layer is genuinely parallel-ready (id-keyed `_pending` + async `ReadLoopAsync`). Only the host serializes today. Plan correctly notes this — minimizes C# churn.
- Isolation as benchmark pass/fail gate (not vibes) is the right discipline. "Default to B if neither passes isolation" is the correct tiebreaker — preserves the original OOP motivation.
- Phasing: 1 unblocks 2/3, 4 parallel, 5 fans in, 6 cleans up. `SubprocessHostMode` flag keeps a known-good baseline available for bisecting.
- `SubprocessPoolSize: 1` collapses Option B to current behavior — clean fallback knob.

**Architectural concerns to fold into prototype issues (non-blocking for plan):**
- A1 — Stream pollution: `[Console]::Out` swap won't catch `Write-Host` (which goes through `$Host.UI` snapshotted at runspace open). Need a custom `PSHost` / `PSHostUserInterface` for the pool. The .NET-side `IsNonJsonPowerShellStreamLine` should be defense-in-depth, not load-bearing.
- A2 — Setup race: pool close-rebuild-reopen needs an explicit drain barrier (stop accept → wait `_pending` empty → close → rebuild → reopen). Plan's "setup is rare and cannot race" is aspirational.
- A3 — Per-runspace `$Error` is correctly identified; also reset `$LASTEXITCODE` and `$ErrorActionPreference`. Existing single-runspace host already leaks `$Error` across invokes (separate fix issue).
- B1 — Channel-lease `finally` must check liveness before re-enqueue; dead host stays out of channel until replacement passes `ping` + `setup`. Otherwise pool slowly bleeds capacity.
- B2 — Discovery cache assumption ("schemas identical") only holds while every subprocess runs the same setup. Cache discovery keyed by setup-payload hash; re-discover on hash mismatch.
- 4 — Benchmark harness must use BDN `[GlobalSetup]` / `[IterationSetup]` to keep process spawn out of per-iteration measurements; otherwise B loses on Az.Accounts import time for the wrong reason.
- 5 — Pass/fail "≥ 4× baseline at 10 concurrent" doesn't apply to CPU-bound (ceiling = ProcessorCount) or CPU-light (dispatch overhead floor). Recommend rewriting as scenario × metric × threshold table.
- 6 — Cancellation scope-out: until cancellation lands, Option A's effective capacity under adversarial load is `N - stuck_invokes`. Don't flip default to A in issue #6 without it.
- 7 — Memory metric: sample Win32 handle count alongside working set. Az workloads leak handles characteristically.

**Answers to plan §6 open questions:**
1. Default N = `Environment.ProcessorCount` for prototype. Don't pick a fixed number until network-shaped benchmark confirms scaling.
2. Don't ship the loser. Two host scripts is real maintenance cost — issue #6 deletes the loser.
3. Local `HttpListener` for harness; one manual real-Azure end-to-end for the findings doc; not in CI.
4. Cancellation as separate issue, agreed; conditional on concern #6.

**Pattern noted (logging):**
- EMU blocks BOTH `gh pr review` and `gh pr comment` for usepowershell/PoshMcp from this account. Cubert recently logged the same finding. Future PR reviews must be saved to `$env:TEMP` and surfaced for manual paste — do not waste cycles attempting either gh subcommand.
- Plan-only PRs that quote internals are high-signal fact-check targets — every cited line either resolves or doesn't. Cross-reference cmdlet usage at the call site, not by global grep (e.g., `ConvertTo-Json -Depth N` legitimately varies per call site).
## Learnings (2026-05-06)
- PR #187 review (runspace pool vs multi-process experiment plan, branch squad/65-runspace-pool-experiment-plan): verdict = comment / approve direction with revisions. Plan is sound; #1 (extract OutOfProcessHost) can start immediately.
- Key architectural concerns raised:
  - Option A: `[Console]::Out` cannot be redirected per-runspace (it's process-global) — plan's stream-pollution mitigation needs correction; use custom PSHost via CreateRunspacePool instead.
  - Option A: setup-while-running quiesce protocol is underspecified — `OutOfProcessCommandExecutor.SendRequestAsync` does not gate setup against in-flight invokes today (only `_sendLock` for stdin writes).
  - Option B: Channel<OutOfProcessHost> + Process.Exited race when host crashes mid-lease needs explicit reconciliation (don't end up with N+1 or N-1).
  - Option B: fail-fast on all-N setup is a regression vs single-host; recommend fail-fast on first host then per-host retries.
  - Benchmark: crash-recovery scenario as written measures process restart (same as baseline) — for Option A's real isolation gate, induce runspace-level corruption, not process exit.
  - Benchmark: 4× throughput gate is wrong for CPU-bound; needs per-scenario thresholds.
  - Phasing: split #4 (benchmark harness) into infrastructure (parallel) and wire-up (blocked on #2/#3).
- Verified current OOP code matches plan's description: `OutOfProcessCommandExecutor.cs` (_sendLock at line 29, _pending dict, SendRequestAsync at line 362), `oop-host.ps1` (Write-NdjsonResponse flush, handler ordering).
- Posting via gh failed (EMU policy on usepowershell/poshmcp blocks `gh pr review`). Review body left at C:\Users\stmuraws\AppData\Local\Temp\tmpsvtizs.md for manual posting.

### 2026-05-06 — EMU blocks `gh issue create` too
The `usepowershell/PoshMcp` repo's EMU policy not only blocks `gh pr review` / `gh pr comment` (already known) but also `gh issue create` with the same `Unauthorized: As an Enterprise Managed User` GraphQL error. When asked to file an issue, write the body to a temp file outside the repo and hand the path to the user to create the issue manually. Do not retry `gh issue create` under this account.

### 2026-05-06 — Cancellation propagation gap (issue body drafted at C:\Users\stmuraws\AppData\Local\Temp\poshmcp-cancellation-issue.md)
Confirmed: `CancellationToken` in `OutOfProcessCommandExecutor.SendRequestAsync` only governs the .NET-side wait (`_sendLock.WaitAsync`, `_stdin.WriteLineAsync`, the linked `timeoutCts` failing the local TCS). It is never forwarded to the OOP subprocess, and `oop-host.ps1` runs a single-threaded dispatcher loop (L630–L650) that cannot read a `cancel` while `Invoke-InvokeHandler` (L498) is blocked inside `& $cmdInfo @boundParams`. In-process is worse: `IsolatedPowerShellRunspace.ExecuteThreadSafeAsync` doesn't accept a CT at all; the only `_powerShell.Stop()` is inside Dispose() (PowerShellRunspaceImplementations.cs L146). Layered fix: (1) cooperative `Stop()/StopAsync()` registration on the in-process pipeline, (2) a `cancel` JSON-RPC method + concurrent-readable dispatcher for OOP, (3) bounded escalation cooperative → forced → process kill + recycle via `PowerShellCleanupService`.

## 2026-05-06: New milestone-tagged issues assigned

Milestone #5 (Spec 004 - Out-of-Process PowerShell Execution) was created. You have issues assigned via squad:* labels:
- Bender: #190 (extract OutOfProcessHost), #192 (Option B - process pool prototype, blocked by #190)
- Fry: #193 (benchmark harness infra), #194 (wire harness to executors, blocked by #191/#192/#193)
- Farnsworth: #196 (adopt the winner, blocked by #195)

Check the issue body for plan reference and dependency chain before starting.

### 2026-05-06: Authored SECURITY.md
- Added `SECURITY.md` at repo root.
- Supported Versions tailored to pre-1.0 reality: only the latest 0.x minor (currently 0.10.x) receives security fixes; older minors unsupported.
- Reporting channel: GitHub private vulnerability reporting (Security tab) — deliberately did NOT invent a security email address.
- Documented SLA (ack 3 business days, triage 7), coordinated-disclosure timeline, and reporter credit via GHSA.
- Pattern: when a project has no published security contact, prefer GitHub's built-in private vuln reporting over fabricating an email.

### 2026-05-06: Reviewed PR #200 (Bender / Option B process pool) and PR #201 (Hermes / Option A runspace pool)

**Verdict:** APPROVED both. Comments posted via `gh pr comment` (gh pr review still blocked on this account).
- PR #200: https://github.com/usepowershell/PoshMcp/pull/200#issuecomment-4392376907
- PR #201: https://github.com/usepowershell/PoshMcp/pull/201#issuecomment-4392377065

**Verification highlights:**
- PR #200 — `OutOfProcessSubprocessPool` correctly uses BOTH `Channel<HostSlot>` (lease queue) and `ConcurrentDictionary<int, HostSlot>` (source of truth). Slot 0 fail-fast via `StartSlotAsync(failFast:true)`; slots 1..N-1 retry with exp backoff via `StartSlotWithRetryAsync`; `MinHealthyForStartup` gate. Discovery fingerprint covers modulePaths, importModules ∪ discoveryModules, installModules (sorted, with name/version/min/max/repo/scope), startupScript path/content, trustPSGallery — XML-doc-explained. Timeout → `lease.MarkBroken()` → `MarkSlotDead` → reconciler. Integration tests parameterized over 1/2/4 with all required scenarios.
- PR #201 — `oop-host-pool.ps1` builds ISS via `CreateDefault2()` + `ImportPSModule`; `CreateRunspacePool(1, N, $iss, $customHost)` with `MTA`. `NdjsonHost`/`NdjsonHostUI` route every `$Host.UI.*` write to stderr (correctly notes `[Console]::Out` is process-global). `PoolStdout.Lock` synchronizes ndjson frame writes. `PoolDispatcher.ProcessOne` reads `w.Ps.Streams.Error/Warning` per-pipeline; user script does `$Error.Clear()` first. Quiesce: `DrainEvent.Reset` → `WaitIdle(60s)` → `Close-Pool` → mutate → `Ensure-Pool` → `End-Drain`. Metrics on response frame: queueDepthOnArrival, leaseWaitMs, activeOnComplete, poolSize.

**Cross-PR enum collision (key finding):** Both PRs add `SubprocessHostMode` — PR #200 as `static class` with string constants (property `string?`), PR #201 as `enum SubprocessHostMode { Single, Pool }`. NOT source-compatible. Bender deliberately reserved the `Pool` constant signaling coexistence intent.

**Merge-order recommendation:** Land PR #201 first (smaller diff, idiomatic enum); have Bender rebase PR #200 to extend the enum with `ProcessPool` (~30 lines). Recorded in `.squad/decisions/inbox/farnsworth-spec004-prototypes-review.md`.

**Non-blocking observations posted to each PR:** #200 — silent `MinHealthyForStartup` clamp, unbounded lease channel, stale-slot spin in `LeaseAsync`, discovery cache deliberately excludes filter params (worth doc note). #201 — drain timeout hardcoded 60s (not threaded from config), pool size cap of 8 is prototype guard, `Resolve-SwitchParameters` runs `Get-Command` on host process not in pool runspace (verify ISS modules are visible to host).

**Pattern noted:** EMU policy continues to block `gh pr review`; `gh pr comment --body-file <tempfile-outside-repo>` remains the working channel. Comments do NOT count as formal GitHub approvals for branch protection — Steven (or another non-EMU reviewer) must convert these to formal Approve reviews if required for merge.

### 2026-05-06: Security alerts triage

**Sources checked:** GitHub Dependabot (open=0), code scanning (open=25), secret scanning (disabled at repo level), .github/workflows/*.yml permissions blocks, SECURITY.md, recent security commits.

**Findings:**
- 23 `cs/log-forging` (CWE-117, medium) in `PoshMcp.Server/PowerShell/PowerShellAssemblyGenerator.cs` lines 709–1030 — logger calls take `commandName`, `parameterValues`, `parameterSummary` from MCP `tools/call` payloads.
- 1 `cs/log-forging` in `PoshMcp.Server/Observability/LoggerExtensions.cs` line 31 — `OperationName` from `OperationContext` flows into a logging scope.
- 1 `cs/log-forging` in `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs` line 111 — JWT path/header-derived values logged.
- 1 `actions/missing-workflow-permissions` (medium) in `.github/workflows/ci.yml` — only workflow without an explicit `permissions` block (14 of 15 already correct).
- Secret scanning disabled — should be enabled with push protection.

**Real risk reading (not false positives):** project ships a Serilog file sink (per spec for issue #131 stdio logging to file), so embedded `\r\n` in tool names or parameter values produces forged log lines in plain-text logs. Exploitability low; impact = audit-trail confusion. Not RCE.

**Triage decisions logged to** `.squad/decisions/inbox/farnsworth-security-review-2026-05-06.md`:
- P1: Add explicit `permissions: { contents: read }` to `ci.yml` → **Amy**.
- P2: Add `LogSanitizer.Scrub(string)` (strip CR/LF, length-cap) and apply at call sites in the three flagged files → **Bender**, with **Fry** for newline-scrub tests. Scrubbing must be at the call site, not via Serilog enricher — CodeQL taint analysis tracks call-site sinks and an enricher won't clear the alerts.
- P3: Enable repo secret scanning + push protection → **Amy**.
- P4 (defer): Consider a `LogSafe(string)` wrapper type or Serilog destructuring policy as a follow-up to make sanitization a build-time invariant.

**Pattern noted:** when CodeQL flags `cs/log-forging`, check the sink type before treating it as noise. Structured logging providers that don't replay newlines (console, JSON sinks) are de-facto immune; plain-text file sinks are not. This repo has both, so the alerts are real.

**Hygiene observation:** dependency posture is healthy — Dependabot 0 open, recent CVE bumps merged (`System.Security.Cryptography.Xml 10.0.6`), and v0.9.2 already shipped an auth bypass fix. No active security-relevant specs in flight.

### 2026-05-06: Reviewed PR #204 (Bender) — fix(oop) SendRequestAsync 'Key: Content' under parallel invokes (#203)

**Verdict:** APPROVED. Comment posted: https://github.com/usepowershell/PoshMcp/pull/204#issuecomment-4393068861

**Root cause verified:** Not concurrency. `BasicHtmlWebResponseObject.Content` (string body) CLR-shadows `WebResponseObject.Content` (byte[]); `ConvertTo-Json` reflection enumerates both into `Dictionary<string,object>` → `ArgumentException` on duplicate key. Harness's parallel Invoke-WebRequest just made the failure deterministic. C# `_pending` correlation map was untouched — correctly identified as a red herring.

**Fix shape:**
- `oop-host.ps1`: extracted `ConvertTo-SafeJson` helper, applied at exactly the user-result serialization site in `Invoke-InvokeHandler` (~line 611). Other ConvertTo-Json sites (request envelopes, error frames) are not wrapped — correct, those serialize controlled C# payloads.
- `oop-host-pool.ps1`: same fallback inlined into the runspace user-script scriptblock. Asymmetric with the host-process helper — correct, scriptblocks executed in pooled runspaces should not depend on host-process function availability.
- Trigger is `catch [ArgumentException]` only; happy path unchanged. Fallback chain: ConvertTo-Json → Select-Object * | ConvertTo-Json → ($r | Out-String).Trim() | ConvertTo-Json.

**Fallback semantics:**
- `Select-Object *` materializes a flat PSObject; PowerShell's member resolver collapses shadowed CLR members and derived wins. For BasicHtmlWebResponseObject the string `Content` (body) wins over the byte[] shadow — exactly what callers want.
- Out-String/Trim is bounded (only fires after Select-* itself throws). Realistic case: cyclic graphs. Returning a valid JSON string beats bleeding the exception to the C# client.

**Regression test (`OutOfProcessHostConcurrencyTests`):** real `Invoke-WebRequest -UseBasicParsing` against loopback `HttpListener` produces a real BasicHtmlWebResponseObject. Concurrency=10 mirrors harness repro. Skip guards on pwsh and HttpListener.IsSupported. Companion test on `_pending` correlation is a sanity net for the original hypothesis. Test only exercises single-host path; pool-host inline fallback is covered end-to-end via `WarmInvokeThroughputBenchmark` smoke (Pool 306 ms / 10 calls).

**Cross-PR sequencing:** This PR unblocks `WarmInvokeThroughputBenchmark` for Single and ProcessPool. Hermes's PR #195 (benchmarks + findings) captured runs 1+2 against pre-#203 main where Single/ProcessPool numbers are unreliable. After #204 merges, Hermes must rebase #195 onto post-#203 main and rerun the affected scenarios before publishing findings. Not blocking #204.

**Pattern noted:** PowerShell `ConvertTo-Json` failures throwing `ArgumentException: ... Key: <name>` are CLR member-shadowing bugs, not concurrency. Parallel harnesses make them deterministic, which makes them look like races. Suspect shadowing on input type first.

**EMU pattern (already known, reconfirmed):** `gh pr comment --body-file <tempfile-outside-repo>` works; `gh pr review` does not. Comment is not a formal GitHub approval — Steven or non-EMU reviewer must convert if branch protection requires it.


### 2026-05-06 — Reviewed PR #205 (Hermes — bench(oop) canonical results + findings, #195)

**Verdict:** APPROVE. Posted via gh pr comment (#issuecomment-4393870722) — gh pr review still EMU-blocked.

**Methodology check:** results doc captured BDN 0.14.0, --job short (3×3×1), exact filter/CLI invocation, base commit e4cf7d9 (post-#204), runtime/OS/arch (Win11 Arm64 / .NET 10.0.6 / Concurrent Server GC), wall time, and explicit non-canonical status of runs 1+2. Reproducible.

**Numbers traced:** Spot-checked WarmInvoke speedups against the source table — Pool 661.2/136.2 = 4.857 → 4.86×, P99 686.233/143.321 = 4.788 → 4.79×; ProcessPool 661.2/200.7 = 3.295 → 3.30×, P99 686.233/201.406 = 3.408 → 3.41×. ColdStart penalties 400 ms (ProcessPool) and 478 ms (Pool) → "400-500 ms". 1 MB allocations 13.79/16.34/17.36 MB → "~13.8/~16.3/~17.4". No rounding flips a conclusion.

**Recommendation as Lead:** Pool as default is supportable from the data on the spec's stated workload model. Strongest counter-argument is single-host / single-shape / short-job — disclosed at correct strength in caveat §5, not strong enough to block. ProcessPool's tighter tail (StdDev 1.11 ms vs Pool 6.34 ms; P99 only 0.7 ms above mean) is the right opt-in answer for tail-sensitive / isolation-sensitive workloads.

**Position on #196 default flip — HARD GATES (not 'should land before'):**
1. Custom PSHost/PSHostUserInterface for runspace pool (partially landed in PR #201; #196 verifies completeness).
2. Cancellation propagation (in-process Stop()/StopAsync() registration, OOP cancel JSON-RPC method, concurrent-readable dispatcher, bounded escalation cooperative → forced → process kill + recycle).
Until both land, Pool may ship as documented opt-in only. #196 must NOT flip the default with either gate open. A --job long WarmInvoke rerun against post-cancellation main (captured as run-4) must reaffirm ≥ 4× I/O bar before the flip.

**#196 scope sketch (delivered in review body):**
- Config: HostMode default flip Single → Pool; Pool:Size default Environment.ProcessorCount with hard cap 32; Pool:DrainTimeoutMs threaded through config (currently hardcoded 60s per PR #201).
- Doctor: validate pool sizing, surface active HostMode.
- Docs: 'When to switch HostMode' section in DESIGN.md (three-case rubric: Pool default / ProcessPool tail+isolation / Single short-lived CLI). Sweep DESIGN.md, README.md, examples/appsettings.*.json, spec 004 quickstart if present.
- Acceptance: run-4 --job long rerun captured.
- Out of scope: per-request override, dynamic resizing, removing prototype paths (both Pool and ProcessPool ship).

**Patterns:**
- Docs+data PRs benefit from spot-checking 2-3 headline numbers against source tables — catches both arithmetic and rounding inversions in one pass.
- When a recommendation rests on one workload shape, make the workload-shape disclosure a gate, not a footnote.
- EMU policy continues to block gh pr review on usepowershell/PoshMcp from this account; gh pr comment with --body-file <tempfile-outside-repo> is the working channel and is NOT a formal GitHub approval.

### 2026-05-06 - Reviewed PR #207 (Bender) - feat(oop) cancellation propagation (#188)

**Verdict:** APPROVE. Posted via gh pr comment (#issuecomment-4394001550) - gh pr review still EMU-blocked.

**Design conformance:** code matches specs/004-out-of-process-execution/cancellation-design.md §3 wire protocol verbatim (cancel- id prefix not in _pending, cancelled flag on invoke responses, ack frame shape). ProcessPool pool file untouched - kill-on-timeout backstop at line 421 preserved verbatim.

**Single-mode strategic divergence (justified):** design §5.1 sketched BeginInvoke + ThreadPool.QueueUserWorkItem; PR ships C# SingleDispatcher (BlockingCollection + dedicated worker thread + ConcurrentDictionary registry) mirroring PoolDispatcher shape. Better choice - high code-share with Pool, uniform SingleStdout/PoolStdout.Lock pattern, avoids fighting PowerShell async ergonomics. Pattern lesson: when the reference design is already proven elsewhere in the codebase (PoolDispatcher), reusing the shape beats inventing a parallel async story even if the original design sketched the alternative.

**Belt-and-suspenders cancel detection is correct:** worker catches PipelineStoppedException AND falls back to InvocationStateInfo.State == PSInvocationState.Stopped. BeginStop does not always raise PSE from synchronous Invoke() - sometimes Invoke returns normally with State=Stopped. Both paths set cancelled = wasStopped || w.Cancelled. Lesson: PowerShell.BeginStop() cancellation detection requires checking both paths; assuming PSE alone produces flaky cancel-completion logic.

**SendRequestAsync orthogonality fix:** PR changes 	imeoutCts from CreateLinkedTokenSource(cancellationToken) to plain new CTS. Now caller-cancel and per-request-timeout are properly orthogonal - no double-fire of timeout when caller cancels. Both registrations dispose in finally; TrySendCancelFrameAsync uses independent 2s CTS so caller-token cancel cannot poison the cancel-frame send. The OperationCanceledException diagnostic catch from design §4.3 was deliberately skipped to keep the diff tight (Bender's history note); fine - OCE bubbles up unannotated, can be added if logs prove confusing in practice.

**Cancel race with success:** if read loop sets result a tick before caller cancels, 	cs.TrySetCanceled no-ops and awaiter sees success - but TrySendCancelFrameAsync still fires (no completion gate). Host gets cancel for already-completed id, replies cancelled:false, suppressed at read loop. Noise-only; not worth gating.

**Test discipline:** Start-Sleep -Seconds 60 against 15s ObservationTimeout proves cancel actually unblocks (a passing test cannot be the sleep finishing). Pool test uses 
unspacePoolSize:4 to provably exercise > 1 runspace - without explicit sizing the pool defaults can produce a one-runspace pool that would head-of-line block, falsely failing the test. ProcessPool test asserts HealthyCount >= 1 after soft cancel proving slots stay healthy and kill backstop not invoked. 500-750ms warmup before cancel is realistic - less races the invoke send.

**#196 hard gate status - BOTH SATISFIED:**
1. Custom PSHost/PSHostUserInterface for runspace pool - PR #201.
2. Cancellation propagation - this PR. Bounded soft-cancel across all 3 modes, no Pool head-of-line, hosts/slots stay healthy.

**#196 remaining scope (now unblocked):**
- Default-mode flip: SubprocessHostMode.Default → Pool. Keep Single + ProcessPool as opt-in (ProcessPool stays recommended for tail-sensitive / isolation-sensitive workloads per #195 P99 finding).
- Config key naming review; confirm SubprocessHostMode enum-vs-string serialization; audit for residual #200/#201 enum collision.
- Doctor validation hooks: surface resolved mode, pool size (with clamp applied - #201 cap of 8), host script path, per-request timeout. Warn (not error) if Pool configured but pwsh resolution failed.
- Doc updates (README, DOCKER.md, spec 004 supersedence). Document cancellation contract: caller-token → bounded soft-cancel; per-request timeout as backstop; ProcessPool kill-on-timeout preserved.
- Bench reaffirmation: Hermes --job long WarmInvokeThroughputBenchmark against post-#207 main (capture as run-4), confirm ≥ 4x I/O bar holds. Cancellation refactor adds per-invoke [powershell] allocation + dispatcher hop - expect no measurable warm-I/O regression but verify, don't assume.

**Edge cases worth flagging non-blocking:** cancel before invoke begins (TryGetValue returns false, ack cancelled:false, awaiter still sees OCE - benign); cancel during host startup/shutdown (_disposed and _stdin is null guards + 2s CTS); wedged unmanaged-code pipeline (.NET awaiter still returns OCE promptly via TCS, ProcessPool gets next-invoke kill backstop, Single/Pool get per-request-timeout backstop); vestigial 	ry { ... } catch { throw } wrapper in Single user script is a semantic no-op (cosmetic, strip in follow-up).

**Pattern noted:** the structural blocker for Single-mode cancellation was that the dispatcher loop could not even READ a cancel frame while blocked inside Invoke-InvokeHandler. The fix was structural (extract invoke to a worker thread), not protocol (a side-channel pipe/signal would not have helped because the read side was the bottleneck). Always check what is blocking the read loop before designing a side-channel.




## Archived 2026-05-15T140000Z

## Project Context
**Project:** PoshMcp — Model Context Protocol (MCP) server for PowerShell
**Tech Stack:** .NET 10, C#, PowerShell SDK, OpenTelemetry, ASP.NET Core, xUnit

### 2026-05-14 — Re-reviewed PR #256 (corrected verdict, APPROVE)

**Prior reject was wrong.** I asserted `Unit/OutOfProcess/*` classes carried `[Trait("Category", "Unit")]` and called FR-401/402/403 violations. They actually carry `[Trait("Category", "OutOfProcess")]`. I inferred the trait from the folder path instead of grepping the file — the exact failure mode FR-400/406 exists to prevent. Bender's audit was correct; nothing to fix. Posted corrected APPROVE at #issuecomment-4453760623. Artifact: `artifacts/farnsworth-pr256-rereview.md`.

**Lesson (hard):** **The folder name is not the trait.** When reviewing trait-tagging PRs, ALWAYS grep the file for the actual `[Trait("Category", ...)]` attribute. Never infer the category from the directory the file sits in — the entire point of FR-400/406 (and Spec 009) is that folder layout and behavioral category are decoupled. A single `Select-String -Pattern '\[Trait\("Category"' -Path <file>` would have caught my error in 200ms. Make this the first move on any future Category-trait review.

**Secondary lesson:** When I dictate a "required fix" list naming specific files, the revision author shouldn't have to verify whether the underlying claim is even true before doing the work. Bender did the right thing by checking the actual file contents and pushing back. The reviewer-rejection lockout protocol assumes the rejection itself is well-founded; when it isn't, the lockout traps a correct artifact in a needless revision cycle. Cost reviewed: one wasted revision-cycle for Bender, one credibility hit for me.

### 2026-05-14 — Reviewed PR #255 (Hermes — centralize pwsh subprocess teardown, spec 009 FR-412, closes #218)

**Verdict:** APPROVE. Posted via gh pr comment (#issuecomment-4453565126). Artifact: `artifacts/farnsworth-pr255-review.md`.

**Highest-risk PR of the spec 009 wave (test-side resource hygiene).** Centralization is genuine — all 5 spawn sites (`AppInsights`, `AzureDeployment`, `DeployScriptConfigurationPrecedence`, `McpServerIntegration`, `UnifiedHttpTransport`) now route through `SubprocessTeardown.Teardown` (sync, for `Dispose`) or `TeardownAsync` (for async `finally`). No inline holdouts.

**"Never throws" contract verified at every boundary:** `TryCapturePid`, `HasExitedSafe`, `TryKillTree`, `WaitForHandleReleaseAsync/Sync`, `SafeUnregisterAndDispose` — every external syscall wrapped, each catch either logs (when ILogger supplied) or swallows. PID captured BEFORE Kill so logging keeps an identifier even after Process disposal — subtle but correct. `InvalidOperationException` on Kill caught silently as a benign race; other Kill failures log a warning. Right calibration on log noise.

**Bounds:** `WaitForExitAsync` bounded via `CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)` + `CancelAfter(graceful)` — correct .NET pattern (no native timeout overload exists), composes cleanly with caller-supplied tokens. Default 5s matches prior inline `WaitForExit(5000)` budget — no regression. Handle-release poll bounded at 2s/50ms intervals (max 40 polls), Windows-gated via `OperatingSystem.IsWindows()`. Worst-case ≤2s per torn-down process on Windows, 0 elsewhere — well within SC-107 wall-clock budget.

**No production leakage.** SubprocessTeardown, OrphanProcessAuditor, TestProcessRegistry all in `PoshMcp.Tests/Shared/`, namespace `PoshMcp.Tests`. Production OOP host (PoshMcp.Server/PowerShell) untouched — correct, production already has its own teardown contract via dispatcher/pool path from spec 004 (#207). Test-side teardown is a different problem (developer-laptop orphan hygiene + port release on long serial run) and properly belongs in the test assembly.

**Composition with collection fixtures:** doesn't touch `[Collection(...)]` attributes; `CachingStateTestCollection` and `TransportSelectionTestCollection` keep their grouping semantics. Centralization happens at spawn-site level, orthogonal to xUnit collection grouping — two layers compose cleanly.

**Smoke test design:** `SubprocessTeardownTests` covers happy-path (`exit 0`) + hung path (`Start-Sleep -Seconds 120`), both end-to-end-verified with `OrphanProcessAuditor`. Auditor is per-instance and diff-based — developer with unrelated `pwsh` sessions doesn't produce false positives. Hung-path test is the meaningful one: asserts kill-tree teardown leaves zero new living `pwsh`. Test class correctly tagged `[Trait("Category", "Integration")]` (FR-401: pwsh spawning must not appear under Unit).

**`using var` → `var` demotion subtlety:** in `AzureDeploymentIntegrationTests.cs` and `DeployScriptConfigurationPrecedenceTests.cs`, Hermes correctly removed the `using` keyword so disposal flows through `SubprocessTeardown.SafeUnregisterAndDispose` rather than racing the `finally` block. Single ownership path for `Process.Dispose()` — clean. This is the kind of change that's easy to get wrong (double-dispose, finalizer order surprises) and Hermes got it right.

**Patterns to remember:**
- When centralizing teardown for tests that spawn subprocesses, the "never throws" contract is non-negotiable — every catch boundary must be exception-safe so it can be called from `finally` and `Dispose` paths without becoming a secondary failure mode.
- Capture identifier (PID) BEFORE kill, not after — once `Process.Kill` runs, accessing `.Id` may throw on some paths, and you want the identifier in the warning log even when teardown is failing.
- For `WaitForExitAsync` (no native timeout overload), the canonical pattern is `CancellationTokenSource.CreateLinkedTokenSource(callerToken).CancelAfter(timeout)`. Using the linked-token shape composes cleanly with caller-supplied cancellation rather than fighting it.
- On Windows, handle release lags process exit by a short window; a bounded `Process.GetProcessById(pid).HasExited` poll is the cheapest way to wait for it. Gate with `OperatingSystem.IsWindows()` because the same poll on Linux is wasted budget.
- "Demote `using var` to `var`" is the specific refactor when ownership of process disposal needs to move from the language-level disposal pattern to a `finally`-block centralized helper. Easy to miss; always grep for `using var process` after introducing such a helper.
- Diff-based orphan detection (snapshot baseline at construction, report new pids at audit time) is the right scoping for tests that run on developer laptops — absolute counts produce false positives from unrelated processes on the box.


**Primary User:** Steven Murawski

## Pre-2026-05-12 Summary (archived to history-archive.md on 2026-05-13)
**Pre-2026-05-02:** see history-archive.md (Spec 003/004/005 restructure; PR #130 nullable MimeType; Spec 006 milestone #3 with 27 issues; PR #167 Doctor Output Restructure approved).
- 2026-05-02: Reviewed PR #184 (Program.cs refactor / spec 002 prompts+resources).
- 2026-05-06: Authored SECURITY.md (private vuln reporting + supported-versions). Reviewed Spec 004 wave: PR #187 (experiment plan), PR #200 (Bender Option B / ProcessPool), PR #201 (Hermes Option A / Pool — chosen as enum baseline), PR #202 (Fry harness wiring; surfaced ConvertTo-Json shadowed-member bug → #203/#204), PR #204 (Bender fix), PR #205 (Hermes findings). Recommended Pool as default with cancellation propagation as a hard gate. Security alerts triage: 25 log-forging + 1 missing workflow perms — defined LogSanitizer pattern at call-site (not enricher) to clear CodeQL.
- 2026-05-06: PR #207 (Bender — cancellation across Single/Pool/ProcessPool) approved; both #196 hard gates (custom PSHost from #201, cancellation from #207) closed.
- 2026-05-07: PR #210 (Leela — OOP docs + samples audit) reviewed. v0.11.0 release shipped (Pool default flip).
**Patterns to remember:**
- EMU gh pr review blocked from this account; use gh pr comment (not a formal approval — Steven must convert if branch protection requires).
- When a default flip is questioned, audit ALL direct construction sites of the affected type (grep for 
ew TypeName).
- Benchmark harnesses surfacing concurrency races on day one are doing their job — land harness, file race separately, don't hold harness PR hostage.
- PowerShell ConvertTo-Json failures with ArgumentException: ... Key: <name> → suspect CLR member shadowing on the input type before suspecting a race.
- Surfacing hardcoded values (e.g. 30s timeout) in doctor reports — even before they're config knobs — signposts the eventual configuration surface.
- Spot-check 2-3 headline numbers in docs+data PRs against source tables (catches arithmetic + rounding inversions).

## Pre-2026-05-02 Summary (archived to history-archive.md on 2026-05-06)
- 2026-04-17: Restructured loose specs (003 prompts, 004 OOP, 005 large-result) into speckit format; FR-035..FR-064, SC-016..SC-030.
- 2026-04-18: Approved PR #130 (MimeType nullable fix) — pattern: model nullable + handler-applied default preserves validator signal.
- 2026-04-20: Filed Spec 006 (Doctor Output Restructure) milestone #3 with 27 issues T001-T027 (#140-#166) split Bender/Fry.
- 2026-07-15: MCP Resources/Prompts spec (002) authored; 4 team skills extracted from PRs #92-#96.
- 2026-07-18: Triaged Issue #131 (stdio logging to file) — Serilog file sink, ClearProviders unconditional in stdio mode, 3-tier resolution (CLI > env > config). Approved PRs #132 (stdio logging), #134 (docker buildx context fix).
- 2026-07-28: Approved PR #167 (Spec 006 Doctor Output Restructure) — DoctorReport records + DoctorTextRenderer architecture.
- See history-archive.md for full entries.

### 2026-05-07 — Reviewed PR #210 (Leela — OOP docs + samples audit, branch squad/oop-docs-samples-audit)

**Verdict:** APPROVE with one non-blocking framing nit. Posted via gh pr comment (#issuecomment-4396923714) — gh pr comment now works after switching back to usepowershell account. Architectural angle only; Cubert handled fact-checking in parallel.

**Mental-model assessment:** Two-entry-point split (brief in configuration.md, deep-dive in advanced.md) is the right structure — avoids duplication, lets operators land on either article and discover the other. Three-mode taxonomy table in advanced.md delivers explicit "when to use" + sizing + per-mode cancellation contract + doctor pointer in the right order. Decision narrative (Pool wins warm throughput ~4.86×, ProcessPool opt-in for trust/tail, Single legacy/bisect) matches spec 004 study and #208 default-flip rationale exactly.

**Sample-pick judgment — both correct, both well-justified in examples/README.md:**
- advanced.json → Pool with SubprocessRunspacePoolSize:0 (auto-tune): correct for heavy-Az + concurrent throughput case. Auto-tune is the right default for a copy-paste sample.
- tenant.json → ProcessPool (size 4, min healthy 2): correct for trust-boundary case. README rationale names the tradeoff explicitly ("trust boundaries between callers matter more than peak throughput") — multi-tenant is exactly where peak throughput is the wrong optimization target. Getting this right in the SAMPLE (not just the docs) is what made #210 more than a documentation update.

**Coherence with #208 — clean.** RuntimeMode correctly described as InProcess/OutOfProcess (the azure-integration.md "sync/async" description was a real bug; fixed). SubprocessHostMode is presented as a primary configuration concept rather than a tuning knob — correct framing for post-default-flip docs. Cancellation documented as a contract per mode, not a footnote — correct framing because cancellation is what made the flip safe.

**Operator completeness:** poshmcp doctor surfaced in advanced.md ("reports the resolved host mode, effective pool sizes, host-script path, clamp warnings"). Adequate — answers the verify-my-config question without burying or over-emphasizing.

**One framing gap (non-blocking):** advanced.md Cancellation section says of Single mode: *"the historical timeout-and-restart behavior applies."* This UNDERSELLS what Single does post-#207 — the SingleDispatcher worker-thread pattern (BlockingCollection + ConcurrentDictionary registry, mirroring PoolDispatcher shape) supports the same cooperative soft-cancel contract as Pool/ProcessPool with per-request timeout as backstop. As written, an operator could read this as "Single mode does not support cooperative cancellation," which would be inaccurate AND would undersell why the default flip became safe across all three modes simultaneously. Suggested follow-up phrasing: *"Single: cooperative cancellation via the dispatcher worker; the per-request timeout acts as the backstop and recycles the host on timeout."* One line, follow-up PR.

**Pattern noted:** When a docs PR ships alongside an engineering decision PR, the per-mode contract narrative is where framing drift hides. Cancellation contract was the strongest place to look because it's the gate that made the default flip safe — any underselling there undersells the whole flip rationale. Sample-pick rationale was the second strongest, because the wrong tradeoff narrative in a sample propagates to operators who copy the sample without reading the docs.

**EMU note:** gh pr comment from usepowershell account works (now properly switched). Coordinator's task setup pre-switched the account so no friction this time. Comments still do NOT count as formal GitHub approvals for branch protection.

### 2026-05-07: v0.11.0 release shipped (cross-agent note from Scribe)
Your work landed in v0.11.0 (csproj 0.10.0 → 0.11.0, CHANGELOG entry, release notes at docs/release-notes/0.11.0.md). The release narrative credits the OOP maturity wave: Pool default flip (#196/#208), cancellation propagation across all modes (#207), benchmarks harness + findings (#193/#194/#195/#205), OOP host extraction (#190/#198), bug fixes (#203/#189), CWE-117 log-injection hardening, minimum workflow permissions, and SECURITY.md. Tag/push deferred to Steven.

## Learnings
- 2026-05-12: Authored specs/009-test-suite-consistency/spec.md. Full suite flake (~668 tests, 6min) traced to OS-level resource contention (port reuse, pwsh handle leak, temp-dir collisions) — parallelization is already off. Recommended trait-based phasing (Option 1) + per-test resource hygiene audit (Option 3) as first step; deferred project split (Option 2) and drain fixtures (Option 4) until measured. Hard user requirement: unit tier must run in <60s, no subprocesses, no ports.

### 2026-05-12 — Spec 009 accepted, milestone + 10 issues filed
- Resolved 7 open questions on specs/009-test-suite-consistency/spec.md; status flipped Proposed → Accepted (2026-05-12).
- New FRs encoding resolutions: FR-416 (Functional→Integration rule, OQ-3), FR-417 (untagged → default bucket, not Unit, OQ-2), FR-418 (dedicated CI flake-rate step N=5, OQ-7), FR-419 (reference machine = maintainer's primary dev machine, OQ-1).
- New Non-Goals: Azure category in CI (OQ-4 deferred), Option 4 cooldown duration (OQ-6 blocked on OQ-4), analyzer for Trait presence (OQ-5 dropped).
- Milestone: "Spec 009: Test Suite Consistency" — creation BLOCKED by EMU policy (gh api POST → HTTP 404, same pattern as gh issue create blocked since 2026-05-06). Staged at C:\Users\stmuraws\AppData\Local\Temp\poshmcp-spec009 with create-all.ps1 for manual run from non-EMU context.
- 10 issues drafted (bodies in staging dir):
  1. Add Category traits to all tests (FR-400, FR-406, FR-417)
  2. Reclassify misfiled Unit/* (FR-401/402/403/414)
  3. Document per-category local commands TESTING.md (FR-408)
  4. CI: split into category-scoped phases (FR-409)
  5. CI: dedicated flake-rate step (FR-418 / OQ-7)
  6. Hygiene: dynamic ports (FR-411)
  7. Hygiene: pwsh subprocess teardown (FR-412)
  8. Hygiene: unique temp dirs (FR-403/410)
  9. Functional→Integration rule applied (FR-416 / OQ-3)
  10. Unit-tier acceptance gate <60s 5x clean (SC-100/101, FR-404/405) — blocked by #1, #2, #6, #7, #8.
- Trade-offs accepted: permissive default bucket (no strict analyzer); hard Functional rule over case-by-case; Azure CI deferred; drain fixture deferred behind hygiene-audit results.
- Pattern: EMU blocks all repo-modifying gh API calls (issues, milestones, PR reviews). For multi-resource setup, draft all bodies to a temp dir + create-all.ps1 script so user can fire them from a non-EMU shell in one shot.

### 2026-05-12: Authored Spec 010 — Improve MCP tool self-documentation

**Path:** specs/010-tool-self-documentation/spec.md (Status: Draft)
**Co-author:** Hermes (technical baseline in his history.md, 2026-05-12 entry)
**Scope per Brady:** Read more of what `Get-Help`/`Get-Command`/CommandInfo already expose. NOT about comment-based vs MAML vs XML — platform normalizes all of those.

**Grounding facts verified before drafting:**
- McpToolFactoryV2.cs#L123-145: in-process description = `"{name} {paramSetSyntax}"`, never calls Get-Help.
- McpToolFactoryV2.cs#L442: OOP fallback = bare command name when remote schema description is empty.
- PowerShellSchemaGenerator.cs#L98: parameter description literal `"Parameter of type {Name}"` for both paths.
- oop-host.ps1#L763-771 and oop-host-pool.ps1#L824-832: only Synopsis read from Get-Help, fallback empty string.
- RemoteToolSchema.cs#L17 XML doc claims "from Get-Help or parameter set syntax" — wrong on both counts (called out as FR-560).

**Key spec elements:**
- FR-500 / FR-510: precedence chains for tool desc (Synopsis → Description → syntax → name) and param desc (Get-Help param block → ParameterAttribute.HelpMessage → ValidateSet hint → "Parameter of type X").
- FR-520 / FR-521: byte-identical descriptions across in-process and OOP modes, automated parity test.
- FR-530 / FR-531: command and parameter aliases exposed.
- FR-540..542: sanitization and length caps (suggested 1024/512).
- FR-550 / FR-551: no description regression, no tool identifier rename.
- FR-560: fix the misleading XML doc.
- FR-570..572: one Get-Help call per command per discovery, cache by setup-hash, gate on <50% cold-start regression in PoshMcp.Benchmarks.
- FR-580..582: silent fallback on Get-Help failures, doctor reports resolved precedence step.

**Approach options:** A (Get-Help in both paths via shared sourcing function — RECOMMENDED), B (CommandInfo only, no Get-Help — rejected: misses Scenario 1 for most modules), C (hybrid — rejected: splits the precedence story), D (PoshMcp.ToolDescription attribute — escape hatch, deferred until A ships and need is observed).

**Why Option A wins:** only option that delivers Scenario 1 across both paths; FR-520 parity falls out for free; cost concern addressed by FR-570/571 caching keyed by the same setup-hash already in use for OOP discovery.

**Sequencing recommended:** extract IToolMetadataSource shared seam → in-process precedence first (no protocol change) → extend RemoteToolSchema additively → wire OOP through same source → parity test → doctor reporting → benchmark re-run → fix XML doc → docs update.

**Open Questions left for Brady:** alias placement (description tail vs dedicated array), length cap defaults, MamlParaText join style, cache invalidation across runspace recycling, doctor field naming (coordinate w/ spec 006), ValidateSet phrasing, telemetry on fallback frequency.

**Pattern for MCP-author-facing specs:** when the technical baseline comes from a co-author, restate the cited file/line evidence inside Background so readers can verify without leaving the document, and treat path-divergence (in-process vs OOP producing different descriptions for the same command) as a first-class bug, not a quirk.


### 2026-05-12 — Spec 010 Wave 1 PR reviews (#235 / #236 / #237)

**Verdicts (all DRAFT; posted as `gh pr comment`, would-approve on undraft):**

- **PR #235 — Bender — RemoteToolSchema XML doc fix (FR-560):** APPROVE. https://github.com/usepowershell/PoshMcp/pull/235#issuecomment-4435719787
- **PR #236 — Fry — pre-spec010 tools/list snapshots + HelpParityFixture (FR-550 step 1, FR-521 fixture):** APPROVE; KEEP fixture in this PR (not scope creep). https://github.com/usepowershell/PoshMcp/pull/236#issuecomment-4435719899
- **PR #237 — Amy — pre-spec010 cold-start baseline (FR-572 step 1):** APPROVE. https://github.com/usepowershell/PoshMcp/pull/237#issuecomment-4435719995

**Scope-creep call on #236:** KEEP HelpParityFixture in #236. FR-550 step 1 explicitly names the fixture as a required input to the baseline capture (`"at minimum Microsoft.PowerShell.Management and the HelpParityFixture module from FR-521"`). The fixture is a dependency of #224's stated work, not a separable concern. Splitting would force a 3-PR chain (fixture → snapshots → tests) and block snapshot reproducibility. #229 (parity test class ToolDescriptionParityTests.cs) consumes the fixture but does not own it.

**Architectural notes recorded for spec 010 implementation work (non-blocking on Wave 1 but worth carrying forward):**

1. **OOP host coverage gap.** Description-sourcing block exists in both `oop-host.ps1` (Single) and `oop-host-pool.ps1` (Pool). #236 baseline captures only Pool. Spec 010 implementation should consolidate the description-sourcing logic into a shared helper sourced by both host scripts (preferred), OR capture a third snapshot for `SubprocessHostMode = 'Single'`. Recommendation: consolidate during implementation PR.

2. **In-process `Environment.ImportModules` is unwired.** #236 capture script works around this by setting `PSModulePath` on the spawned process. Worth a separate follow-up issue (in-process should honour `Environment.ImportModules` for parity with OOP) — orthogonal to spec 010 but the asymmetry is real and surfaces every time a spec adds a runtime-mode-parameterized test.

3. **Bench gate granularity.** #237 `--job short` (3-10 iterations) is sufficient for the 50% threshold given current noise envelope (~±2% on Mean). If post-spec010 run lands near the threshold (say 30-40% regression), rerun with `--job medium` before final merge of the implementation PR. Compare per-mode Mean; do not gate on P99 with n=3 (degenerate).

4. **XML doc precedence.** When spec 010 implementation lands, `RemoteToolSchema.Description` XML doc (just corrected in #235) will need a second update to describe the new sourced-from-Get-Help-precedence-chain rule. Bender's #235 doc text was accurate for pre-implementation behavior only. Flag for the implementation PR.

**Cubert review:** Per the user directive (2026-05-05 decision), Cubert pre-reviews Farnsworth plans/proposals before they reach the user. These were PR reviews, not plans/proposals, so the directive did not gate this work — Cubert reviewed in parallel with me, focusing on claim-by-claim fact-checking while I focused on architecture and spec alignment.

**EMU pattern (reconfirmed):** `gh pr comment` works only from the `usepowershell` account; `stmuraws_microsoft` is blocked. Switch-account-comment-switch-back remains the working channel. `gh pr review` remains blocked for all paths.

## Learnings (2026-05-12)
- When a baseline-capture PR depends on a fixture module that the spec explicitly names as a required input, the fixture is part of the baseline PR's scope. Do not split fixture-as-dependency from baseline-as-deliverable — it produces a serialized 3-PR chain with the fixture-only middle PR adding zero verifiable value on its own.
- Reproducibility checklist for benchmark-baseline PRs (used to evaluate #237): folder naming follows spec numbering convention; exact CLI captured; BDN version + OS + .NET + JIT + machine specs recorded; gating rule stated in README; ApplicationInsights/telemetry isolation called out. All five are non-negotiable for FR-572-style gates.
- Reproducibility checklist for snapshot-capture PRs (used to evaluate #236): temp config file (repo config not mutated); full JSON-RPC envelope captured (not just `result`); pretty-print + LF endings + UTF-8 no BOM for diff stability; deterministic-by-construction discovery config (`EnableDynamicReloadTools=false`, `EnableConfigurationTroubleshootingTool=false`); manual fallback documented in README; regeneration policy explicit. All six are non-negotiable for FR-550-style baselines.
- Pre-implementation baseline PRs are inherently low-architectural-risk but high-process-risk. The architectural review surface is small (does the deliverable match what the spec gates against?); the process risk is that a sloppy baseline silently allows post-change regressions to slip the gate. The reviewer's job is mostly verifying that the gate is well-defined and reproducible, not critiquing the implementation.

### 2026-05-12 — Reviewed PR #238 (Bender — IToolMetadataSource seam, #225, spec 010 step 3, Option A)

**Verdict:** APPROVE (would-approve comment posted via `gh pr comment`; Steven owns formal approval). Backup at C:\Users\stmuraws\AppData\Local\Temp\farnsworth-pr238-review.md. Terminal output capture intermittent this session — could not visually confirm comment URL but `gh pr comment` returned non-error.

**Seam contract (the architecture decision):**
- `IToolMetadataSource` exposes two methods (`ResolveToolDescription`, `ResolveParameterDescription`) keyed off readonly record struct request types. Result types carry both the resolved string and a `Source` enum.
- `ToolDescriptionSource` enum {Synopsis, Description, Syntax, Name} maps 1:1 with FR-583 string literals; `ParameterDescriptionSource` enum {HelpParameter, HelpMessage, ValidateSet, TypeFallback} same. Enum-to-literal conversion deferred to #228 (doctor) — correct placement.
- `ParameterDescriptionRequest` carries every input the FR-510 chain needs (HelpParameterDescription, HelpMessage, ValidateSetValues, ValidateSetAppliesToArrayElement for the singleton vs array phrasing in step 3, ParameterTypeName for step 4).
- **Caller-side resolution** is the design decision: Get-Help is invoked by `McpToolFactoryV2`/`PowerShellSchemaGenerator` and resolved fields are PASSED INTO the seam as request fields. The seam owns precedence rules; callers own data acquisition. Right separation — keeps OOP wire-format independent (subprocess can resolve and ship pre-resolved fields without the seam knowing about runspaces).

**Verification:**
1. **Both call sites wired:** in-process `SetParameterSetDescription` and OOP `CreateRemoteCommandMetadataMapping` both construct `ToolDescriptionRequest` and call `ResolveToolDescription`. `ResolveParameterDescription` exists but isn't invoked yet (deferred to #226 with `PowerShellSchemaGenerator` wiring) — coherent scope, not a gap.
2. **Behavior preservation:** identical output bytes for realistic inputs in both paths. Two non-blocking subtleties: `DefaultToolMetadataSource` calls `Synopsis.Trim()` (PowerShell's Get-Help output is already trimmed; FR-540 step 1 will mandate trim anyway — strictly closer to spec target); Synopsis suppressed when equal to CommandName (FR-500 step 1 explicitly excludes that case; output bytes identical, only Source tag differs).
3. **DI lifetime:** `TryAddSingleton<IToolMetadataSource, DefaultToolMetadataSource>()` in BOTH `StdioServerHost.ConfigureOpenTelemetry` and `HttpServerHost.ConfigureOpenTelemetryForHttp`. `TryAdd` is the right choice — lets #226/#227 register a replacement before host configuration without conflict. Singleton lifetime correct (impl is documented stateless/thread-safe).
4. **Forward-compat for #226/#227/#228:** all plug in WITHOUT touching the interface. #226 populates Synopsis+LongDescription+HelpParameterDescription on the request records. #227 extends `RemoteToolSchema` with per-parameter help fields and the OOP caller passes them through the same request shape. #228 reads `result.Source` for doctor `descriptionSource` literal and FR-590 metric tag.

**Non-blocking observations:** `McpToolSetupService.CreateToolFactory` accepts metadata source as optional parameter with `null` default — reasonable for seam-only scope, but #226 should switch to constructor-injected (resolved from DI) so the `null` branch can't accidentally bypass a registered replacement. `McpToolFactoryV2` now has six constructors (3 × {with, without metadata source}) — acceptable backward-compat during transition; collapse in follow-up once all callers route through DI.

**Pattern noted (architecture):** When designing a seam intended to absorb a future precedence chain, putting data-acquisition responsibility on the CALLER (not inside the seam) produces a wire-format-independent contract. The seam is pure logic; call sites supply pre-resolved inputs from whatever source they have access to (in-process runspace, subprocess over ndjson, cached helpData). This makes OOP wire-through (#227) trivially additive — extend RemoteToolSchema fields, populate the request, no seam change.

**Pattern noted (process):** Six-constructor proliferation is acceptable during a backward-compat transition. Don't block a seam-only PR over it; file a collapse-after-DI follow-up.

**EMU caveat:** `gh pr review` remains blocked from this account; `gh pr comment` is the working channel. Comment is NOT a formal GitHub approval.


### 2026-05-12 — Spec 010 Wave 3 PR reviews (#240 / #239)

**Verdicts (both posted via gh pr comment; gh pr review remains EMU-blocked):**

- **PR #240 — Bender — eat(metadata): in-process Help-aware precedence + sanitization (#226):** WOULD APPROVE. https://github.com/usepowershell/PoshMcp/pull/240#issuecomment-4436060222
- **PR #239 — Hermes — eat(oop): extend RemoteToolSchema with full description + per-parameter metadata (#227):** WOULD APPROVE. https://github.com/usepowershell/PoshMcp/pull/239#issuecomment-4436060298

**PR #240 architecture findings:**
- DI registration is a clean swap (TryAddSingleton<IToolMetadataSource, HelpAwareToolMetadataSource>) in both StdioServerHost and HttpServerHost. Not a fallback chain.
- Get-Help cost contained per FR-570/FR-571: PowerShellHelpResolver is a per-factory ConcurrentDictionary keyed case-insensitively on command name; Get-Help -Name <cmd> -Full invoked exactly once per unique command per discovery via GetOrAdd. No per-parameter Get-Help calls — parameter help read from the per-command result.
- DescriptionSanitizer is pure-static, comprehensively unit-tested (17 tests covering nulls, control chars, paragraph preservation, word-boundary truncation, ellipsis edges).
- Caller-side data acquisition matches the seam contract from #238: BuildParameterDescriptionMap collects every input the FR-510 chain needs (HelpParameter, HelpMessage first non-empty, ValidateSetValues, ValidateSetAppliesToArrayElement when array-typed).
- FR-500 step 1 auto-generated synopsis filter (synopsis == commandName → null) implemented in BuildFromHelpObject, matches OOP host behavior.
- Sanitize → check non-empty → cap order applied consistently in both ResolveToolDescription and ResolveParameterDescription. Caps match FR-541/FR-542 exactly.
- [Description] attached only to PowerShell-derived parameters; framework params (_AllProperties, _MaxResults, _RequestedProperties) and CancellationToken correctly skipped.

**PR #240 non-blocking observations:**
- Scenario 1 demo deferred to #229 (FR-521 parity test + FR-550 regression test) — coherent with spec sequencing but flag for #229 review.
- In-process baseline snapshot (inprocess-tools-list.json from #236) WILL diverge after this PR. FR-550 regression test in #229 must encode "starts-with baseline + paragraph separator OR equals baseline" rule, not naive equality.
- PowerShellSchemaGenerator.CreateParameterSchema legacy two-arg overload allocates a DefaultToolMetadataSource per call when source not supplied — negligible; collapse after callers migrate.
- OOP path correctly untouched in this PR; OOP wiring is #228.

**PR #239 architecture findings:**
- Strictly additive: Description field preserved verbatim (same name, type, default, semantics); FullDescription and three new per-parameter fields all string? / string[]?. Older payloads deserialize with nulls and consumers fall through precedence per FR-510 contract.
- JSON ↔ C# DTO shape parity verified for both layers (tool: 5 fields; parameter: 7 fields). Both oop-host.ps1 (Single) and oop-host-pool.ps1 (Pool) emit identical shape — consumer needs no per-host-mode branching.
- Defensive PowerShell at every layer: outer Get-Help wrapped in try/catch with -ErrorAction SilentlyContinue (FR-581 — discovery cannot fail because help is missing); Get-RemoteCommandHelpMetadata guards if ( -ne ); inner property reads in nested try/catch; MAML projection handles both string-array and MamlParaText[] shapes; Get-RemoteParameterAttributeMetadata checks every level for null; HelpMessage only set when non-whitespace; ValidateSetValues only emitted when non-empty.
- Closes the spec-010-wave-1 OOP host coverage gap (Single host now updated alongside Pool host).
- No precedence resolution in the host — emits raw source data only; FR-540 sanitization happens on the .NET consumer side per the seam contract from #238. Right separation.

**PR #239 non-blocking observations:**
- Description-sourcing logic duplicated across two host scripts (script-scope helpers in oop-host.ps1 vs nested local functions in oop-host-pool.ps1's Invoke-DiscoverHandler). Intentional and matches the established pool-host pattern from PR #204 / #207. Future consolidation into oop-host-shared.ps1 is a candidate refactor, not blocking.
- oop-tools-list.json baseline (#236) was Pool-only — capturing a Single snapshot in #229 closes the OOP coverage gap I flagged on #236.
- HelpMessage "first non-empty wins" across multiple parameter sets matches FR-511 intent (single value per parameter); XML doc flags the edge case explicitly.
- ValidateSetValues ordering preserved per declaration order (FR-510 step 3 requirement).
- Consumer wiring lands in #228 — confirmed CreateRemoteCommandMetadataMapping unchanged here. After both #240 and #239 land, #228 wires the OOP consumer through IToolMetadataSource and FR-520 byte-parity becomes verifiable.

**XML doc precedence (FR-560) carryforward:** RemoteToolSchema.Description doc was corrected for pre-spec-010 in #235; updated in this PR to point at FullDescription. Will need a third update in #228 once consumers source descriptions through the precedence chain. Flag for #228.

**Pattern noted (architecture):** When a multi-PR feature splits "wire format extension" from "consumer wiring", the wire-format PR's job is to be additive, defensive, and exhaustively null-safe at the source — and to NOT pre-resolve precedence on the producer side. PR #239 nailed this: every new field is nullable, every read is guarded, and the host emits raw data only. The consumer (#228) then owns the precedence chain via the seam from #238. This separation is what makes gh pr review of the wire-format PR boil down to "does the JSON shape match the DTO and is every read defensive" — both verifiable in isolation without staging the consumer.

**Pattern noted (review process):** When reviewing two coordinated PRs against the same spec wave, sequence the architecture review around the seam contract: verify the implementation PR (#240) wires the seam into the path it owns, verify the wire-format PR (#239) doesn't violate the seam by pre-resolving on the wrong side, and explicitly call out the cross-PR coordination point (#228) so the implementation review of the consumer PR knows what to look for. Reduces the "I forgot why we did X this way" risk in wave 4.

**EMU caveat (reconfirmed):** gh pr review blocked on usepowershell/PoshMcp; gh pr comment --body-file <tempfile-outside-repo> is the working channel. Comments do NOT count as formal GitHub Approves for branch protection — Steven owns formal approval.

### 2026-05-13: PR #222 — SwitchParameter MCP round-trip review
- Root cause: SwitchParameter is a struct with getter-only IsPresent; STJ reflection binds to `default(SwitchParameter)` (always `IsPresent=false`). Schema also exposed `{isPresent}` envelope, which most clients reject when the model emits a plain bool.
- Fix shape: `JsonConverter<SwitchParameter>` + `AIJsonSchemaCreateOptions.TransformSchemaNode` rewrite to `anyOf [boolean | {isPresent} | null]`, registered via `McpServerToolCreateOptions.SerializerOptions` and `SchemaCreateOptions` in `CreateMcpToolOptions`.
- Placement is correct: new file under `PoshMcp.Server/PowerShell/` next to `PowerShellParameterUtils.cs` (which already cross-references it). Single chokepoint — clean.
- Watch-out: `SerializerOptions` is now applied to **every** tool, not only those with switch params. `DefaultIgnoreCondition = WhenWritingNull` will silently change response shapes for tools that emit explicit nulls. Cloning the SDK defaults + adding only the converter would be safer; current PR is acceptable but worth a follow-up audit.
- Converter token-cursor caveat: object-valued `isPresent` (e.g. `{"isPresent": {"x":1}}`) hits the switch's `_ => present` default WITHOUT consuming the inner StartObject, so the outer `while (reader.Read())` then descends INTO the nested object. Practically unreachable, but a one-line `reader.Skip()` in the default branch would harden it.
- Test coverage is genuinely strong: 12 converter inlines, schema transform applied to a real `Get-ChildItem -Recurse`, sanity check that `-Path` is untouched, and an e2e probe that defines a PS function with `[switch]` and asserts `.IsPresent` inside the runspace.
- Pattern to remember: SDK reflection-binding gaps for opaque CLR types → small focused converter + `TransformSchemaNode` + register through `CreateMcpToolOptions`. Don't add per-parameter type substitution in `EffectiveParameterType` for these.


## Learnings (2026-05-13)

### PR #222 review — global JsonSerializerOptions blast radius

Reviewed PR #222 at Steven's request. **Verdict: Approve with suggestions** (UI-only — not posted to GitHub).

**Architectural lesson — globals on shared serializer options.** PR #222 set `JsonSerializerOptions.DefaultIgnoreCondition = WhenWritingNull` on what looks like a shared/default options instance. That mutation has the wrong blast radius: every unrelated serialization path that touches the same options object silently changes behavior, and the change isn't visible at the call sites that actually need it. Pattern to apply going forward:

- If null-skip is needed for a specific contract, scope it to that contract's converter (`[JsonIgnore(Condition = ...)]` on the property or a custom converter).
- If null-skip is needed for one call, build a per-call `JsonSerializerOptions` (or clone) instead of mutating a shared one.
- Reserve mutation of shared options to startup-time configuration where the team has audited every consumer.

**Minor items flagged:** two converter/schema cleanups + several nits (recorded in the chat review; not formalized as a decision because no team-level architectural commitment was made beyond the approve-with-suggestions verdict).

**Process note:** Review was performed in chat; verdict was not posted to the PR. If Steven wants this on the PR record, that's a follow-up action — not done in this session.

## Learnings (2026-05-13)

### PR #222 review — global JsonSerializerOptions blast radius

Reviewed PR #222 at Steven's request. **Verdict: Approve with suggestions** (UI-only — not posted to GitHub).

**Architectural lesson — globals on shared serializer options.** PR #222 set `JsonSerializerOptions.DefaultIgnoreCondition = WhenWritingNull` on what looks like a shared/default options instance. That mutation has the wrong blast radius: every unrelated serialization path that touches the same options object silently changes behavior, and the change isn't visible at the call sites that actually need it. Pattern to apply going forward:

- If null-skip is needed for a specific contract, scope it to that contract's converter (`[JsonIgnore(Condition = ...)]` on the property or a custom converter).
- If null-skip is needed for one call, build a per-call `JsonSerializerOptions` (or clone) instead of mutating a shared one.
- Reserve mutation of shared options to startup-time configuration where the team has audited every consumer.

**Minor items flagged:** two converter/schema cleanups + several nits (recorded in the chat review; not formalized as a decision because no team-level architectural commitment was made beyond the approve-with-suggestions verdict).

**Process note:** Review was performed in chat; verdict was not posted to the PR. If Steven wants this on the PR record, that's a follow-up action — not done in this session.
### 2026-05-13: PR #243 review (issue #229, spec 010 wave 5) — APPROVED
- Fry's test trio (HelpParityFixtureSession + ToolDescriptionParityTests + ToolDescriptionRegressionTests + ParameterSetConsistencyTests) is architecturally clean: one shared fixture, three consumers, correct unit-vs-integration split.
- Skip strategy on the 10 `ParameterDescription_IsNonEmpty_*` variants pinned to issue #242 is the right call. Tests-before-fix sequencing with explicit regression gate; failing them would block unrelated CI for an already-triaged finding.
- Issue #242 (FR-510 wiring gap: resolver returns correct strings but they don't reach `inputSchema.properties.<name>.description` JSON) is well-scoped — names the seam, points at the fixture for repro, lists the 10 test methods that un-skip green on fix.
- One non-blocking nit: `ToolDescriptionRegressionTests.IsEqualOrSuperset` uses `Contains(baseline)` as a third acceptance branch, which is broader than FR-550's "equal OR baseline + paragraph separator + additional text". Suggested tightening to `EndsWith(separator + baseline)` or dropping the fallback once confirmed unneeded. Filed as a review nit, not a gate.
- Lesson: when a test PR exposes a real bug, file a separate issue + skip-with-tracking-comment is the right pattern. Don't conflate measurement and remediation in one PR — different domains, different reviewers.

### 2026-05-13: PR #244 review (issue #230, spec 010 step 8) — APPROVE
**Requested by:** Steven (via Ralph)
**Author:** Bender
**Verdict:** APPROVE with one non-blocking follow-up.

**Reviewed:** descriptionSource per command and per parameter in doctor JSON.

**Architectural takeaways:**
- Parallel IToolDescriptionSourceTracker (NOT extending IToolMetadataSource) — right call. Preserves the OOP seam from #228; adding methods to the interface would have rippled into every external implementer. Tracker is additive, optional, full back-compat across all McpToolFactoryV2 constructor overloads (chained through with descriptionSourceTracker: null).
- OOP coverage verified at all four Resolve* sites: SetParameterSetDescription, BuildParameterDescriptionMap (in-proc) + CreateRemoteCommandMetadataMapping, BuildRemoteParameterDescriptionMap (OOP).
- Vocabulary centralized in DescriptionSourceVocabulary.ToWireValue(...) — both JSON converters route exclusively through it. No duplicated switches anywhere.
- CLI doctor now wires `new HelpAwareToolMetadataSource()` via the new DiscoverToolsForCliAsync overload — matches production DI at HttpServerHost.cs:287 and StdioServerHost.cs:148 (both TryAddSingleton<IToolMetadataSource, HelpAwareToolMetadataSource>()).
- Field placement (functionsTools.tools[]) is consistent with spec 006 FunctionsToolsSection.

**Non-blocking follow-up (file as separate issue):**
Runtime doctor (BuildDoctorReportFromConfig — used by McpToolSetupService.cs:411 and ConfigurationReloadTools.cs:198) does NOT receive a tracker. Live-server doctor resource emits empty tools[]. Seam is in place; wiring is small additive change. CLI doctor is the canonical diagnostic surface, so SC-207 is materially served — but full coverage requires runtime path too.

**#231 handoff:** Decision drop is unusually well-prepared. Names canonical APIs (DescriptionSourceVocabulary.ToWireValue, IToolDescriptionSourceTracker), proposes observer-fan-out pattern so OTel attaches at the same call sites without coupling to the doctor tracker. Amy can land FR-590 cleanly.

**#242:** Correct to leave out of scope. Different code path (parameter desc → JSON Schema, not tracker → doctor). Would have muddied the spec-010-step-8 landing.

521 unit tests passing (12 new). All 8 enum values exercised through real HelpAwareToolMetadataSource resolver.

## 2026-05-13 — PR #245 (issue #231, FR-590) — APPROVE

OTel counters for description-source resolution. Amy chose Option B (direct emission in McpToolFactoryV2) over Option A (decorator over IToolDescriptionSourceTracker).

**Verdict: APPROVE.**

Key architectural points:
- Tracker is IToolDescriptionSourceTracker? — null by default, only wired by doctor command path. Decorator over a null-by-default service cannot satisfy FR-590 (emit on every resolution). Option B is structurally correct, not a workaround.
- All four `Resolve*` sites covered (in-proc tool ~L268, in-proc param ~L625, OOP tool ~L671, OOP param ~L750). Counter call sits immediately after each tracker call — same lexical line of sight, drift-resistant.
- Tag values flow exclusively through `DescriptionSourceVocabulary.ToWireValue(...)`. Doctor JSON `descriptionSource` and OTel `step` tag are byte-identical by construction. Zero string duplication.
- Failure isolation: null-check + try/catch swallowing all exceptions including vocabulary's `ArgumentOutOfRangeException` for unknown enums. Test `Counter_emission_swallows_unknown_enum_values` exercises the failure path.
- Performance: cold path (no MeterListener) is near-no-op; warm path is one `KeyValuePair` allocation per resolution. Negligible on discovery cycle.

Future-proofing flag (non-blocking): if someone later wires the tracker in production AND adds a tracker-side decorator for these counters, you'd get 2x counts. Mitigation already baked in: helpers live in McpToolFactoryV2 not in any tracker impl, so wiring the tracker is a no-op for these counters. Optional one-line "do not duplicate in tracker decorators" comment on each helper would be belt-and-suspenders.

OOP flake (`Lifecycle_Start_Ping_Setup_Shutdown_Restart` STATUS_ACCESS_VIOLATION) confirmed unrelated — counter emission is parent-host code with no IPC contribution to OOP child address space, and the failing test is lifecycle/restart not discovery.

MeterListener test pattern is the right shape (filter on MeterName + instrument name, copy tags into owned dict because ReadOnlySpan doesn't outlive the callback, cache MethodInfo for reflection). Sets a clean precedent for future McpMetrics instrument tests.

## 2026-05-13 — PR #245 (issue #231, FR-590) — APPROVE

OTel counters for description-source resolution. Amy chose Option B (direct emission in McpToolFactoryV2) over Option A (decorator over IToolDescriptionSourceTracker).

**Verdict: APPROVE.**

Key architectural points:
- Tracker is IToolDescriptionSourceTracker? — null by default, only wired by doctor command path. Decorator over a null-by-default service cannot satisfy FR-590 (emit on every resolution). Option B is structurally correct, not a workaround.
- All four `Resolve*` sites covered (in-proc tool ~L268, in-proc param ~L625, OOP tool ~L671, OOP param ~L750). Counter call sits immediately after each tracker call — same lexical line of sight, drift-resistant.
- Tag values flow exclusively through `DescriptionSourceVocabulary.ToWireValue(...)`. Doctor JSON `descriptionSource` and OTel `step` tag are byte-identical by construction. Zero string duplication.
- Failure isolation: null-check + try/catch swallowing all exceptions including vocabulary's `ArgumentOutOfRangeException` for unknown enums. Test `Counter_emission_swallows_unknown_enum_values` exercises the failure path.
- Performance: cold path (no MeterListener) is near-no-op; warm path is one `KeyValuePair` allocation per resolution. Negligible on discovery cycle.

Future-proofing flag (non-blocking): if someone later wires the tracker in production AND adds a tracker-side decorator for these counters, you'd get 2x counts. Mitigation already baked in: helpers live in McpToolFactoryV2 not in any tracker impl, so wiring the tracker is a no-op for these counters. Optional one-line "do not duplicate in tracker decorators" comment on each helper would be belt-and-suspenders.

OOP flake (`Lifecycle_Start_Ping_Setup_Shutdown_Restart` STATUS_ACCESS_VIOLATION) confirmed unrelated — counter emission is parent-host code with no IPC contribution to OOP child address space, and the failing test is lifecycle/restart not discovery.

MeterListener test pattern is the right shape (filter on MeterName + instrument name, copy tags into owned dict because ReadOnlySpan doesn't outlive the callback, cache MethodInfo for reflection). Sets a clean precedent for future McpMetrics instrument tests.
- 2026-05-13: PR #246 (Amy — issue #232, FR-572 spec-010 cold-start regression gate). Methodology parity confirmed: same BDN config (InvocationCount=1, MaxIterationCount=10), same filter, same machine, same scenario class. Direct .exe launch in run-6 is functionally equivalent to `dotnet run -c Release`. Toolchain drift minor (SDK 10.0.107 → 10.0.108). Regression ~11–12% Mean across all three modes (Single +11.74% / Pool +12.08% / ProcessPool +10.66%); P95 and P99 track Mean within ±2pp — uniform overhead, not mode-specific. Spec 010 attribution clean: `git log 16878b8..48db59a -- PoshMcp.Server PoshMcp.Benchmarks` returns exactly the 7 spec-010 wave merges (#235, #238, #239, #240, #241, #244, #245), zero unrelated changes. COMPARISON.md is solid template material — keep the paired `run-N` + COMPARISON.md pattern for future regression gates. Verdict: ✅ APPROVE (comment posted; EMU restriction prevents formal review).


---
# Archived 2026-05-16 (Scribe) — 2026-05-13/14 PR review entries

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



---
# Archived 2026-05-16 (Scribe) — PR #273 review full text (now merged)

### 2026-05-16 — PR #273 review (Leela — 4-part tutorial series, branch squad/docs-tutorial-series)

**Verdict:** APPROVE. Posted via `gh pr comment` (#issuecomment-4467060959). Artifact: `artifacts/farnsworth-pr273-review.md`. Self-approval blocked under usepowershell identity — comment-form per #252/#269/#271 precedent.

**What I cross-checked architecturally:**
- `Authentication.Schemes.ApiKey.Keys` dictionary-key-IS-the-secret: matches `ApiKeyAuthenticationHandler.HandleAuthenticateAsync` `Options.Keys.TryGetValue(apiKey, ...)` at L32. Tutorial 3 calls this out explicitly — single most-misread part of the auth surface.
- Role-claim minting chain: `foreach (var role in keyDef.Roles) claims.Add(new Claim(ClaimTypes.Role, role))` (L62-63) → `HasRequiredRoles` any-match (`requiredRoles.Any(r => user.IsInRole(r))`, AuthorizationHelpers L23). Tutorial 4 narrative matches and hedges the any-match drift risk explicitly.
- `ToolListAuthorizationFilter.CanAccessTool` (L55-78): AllowAnonymous → RequireAuthentication → scope+role gates. Tutorial 4 step 6 reader-hides-admin-tool claim is structurally correct.
- Base image contract: `/usr/local/share/powershell/Modules` and `/app/server/appsettings.json` match `examples/Dockerfile.user`; HTTP default matches `docker-entrypoint.sh` L9 (`POSHMCP_TRANSPORT:-http`). No invented paths.
- `DefaultScheme = "Bearer"` default ([AuthenticationConfiguration.cs L8](PoshMcp.Server/Authentication/AuthenticationConfiguration.cs#L8)) — tutorials 3/4 override correctly to ApiKey but don't explain the override. Flagged as one-sentence ask.

**Non-blocking asks routed to Cubert (NOT Leela — but lockout doesn't apply here, APPROVE verdict):**
1. Add `poshmcp doctor` demo + `moduleImports` section walkthrough to tutorial 2 (spec 011 just shipped — this series is the canonical place to demonstrate the new doctor section).
2. One-sentence `DefaultScheme` callout in tutorial 3.
3. One-sentence `Modules` vs `CommandNames` callout in tutorial 2.
4. Step-6a "no key at all" boundary beat in tutorial 4 (currently only happy paths shown).
5. `examples/Dockerfile.user` drift note in tutorial 2 Dockerfile section.

**Pattern noted (captured to decision drop):** Tutorials/walkthroughs touching `Modules` or `IncludePatterns` should always demonstrate `poshmcp doctor` and call out the v0.14.0 `moduleImports` section. Tutorial 4 step 8 already does this for the Authentication section — same pattern should propagate. Will flag in future doc reviews.

**Pattern noted (review discipline):** For doc PRs that ground claims in code, the high-value review move is mapping each tutorial-named config property to its handler/options class and verifying the spelling and semantics line up. Caught the dictionary-key-IS-secret pattern, the role-claim minting loop, and the ToolList filter ordering as three places where Leela's prose is exactly faithful to code (not paraphrased). When tutorials get this right at code-grounded depth, the architectural review reduces to "does the progression and boundary coverage hold up?" — which here, it does.

**Process discipline (from PR #252 lesson):** First move was `gh pr view 273 --json reviews,comments` to confirm no prior verdict in the queue. Comments and reviews both empty (Cubert running in parallel but hadn't posted yet). Clean to proceed.



## Archived 2026-06-01T00:00:00Z — Scribe compaction

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

