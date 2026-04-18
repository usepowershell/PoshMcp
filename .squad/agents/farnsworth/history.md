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
