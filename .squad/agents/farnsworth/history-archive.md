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

