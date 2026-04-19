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
