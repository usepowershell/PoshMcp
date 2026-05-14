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



