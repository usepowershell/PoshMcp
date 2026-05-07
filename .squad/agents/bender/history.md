# Bender Work History

**Status:** 37.6 KB (checked 2026-05-03: within 90-day retention, no archival required)

## Recent Work (2026-05-06 — CURRENT SESSION)

### Fix: CWE-117 log forging — `LogSanitizer` + call-site scrubbing
**Date:** 2026-05-06
**Status:** Complete (committed, not pushed — coordinator orchestrates push)
**Branch:** `squad/security-codeql-cleanup`

- Added `PoshMcp.Server/Observability/LogSanitizer.cs` — `Scrub(string?)` static helper.
  - Replaces CR/LF with visible escape sequences (`\\r`, `\\n`); other ASCII C0 controls and DEL → `\\xNN`; TAB → `\\t`.
  - Truncates at 2048 chars with `…(truncated)` suffix.
  - Null → `"<null>"`.
  - Allocation-conscious: returns input unchanged when no escapes needed and within length.
- Applied at call sites only (Farnsworth's call: CodeQL `cs/log-forging` is call-site sink-tracked, so a Serilog enricher would not close the alerts).
  - `LoggerExtensions.BeginCorrelationScope` — scrub `OperationName` before it enters scope.
  - `AuthenticationServiceExtensions` `OnMessageReceived` — scrub `context.Request.Path` (attacker-controlled).
  - `PowerShellAssemblyGenerator`:
    - Introduced `safeCommandName` local once at top of `ExecutePowerShellCommandTyped`; replaced all log/metric sink uses of `commandName` (≈25 sites) with the sanitized form. Raw `commandName` still used at `ps.AddCommand(...)`/`OperationContext.BeginOperation(...)` and in the JSON error responses returned to MCP callers.
    - Scrubbed `paramInfo.Name`, `paramValue`, `convertedValue`, PowerShell error stream messages, exception messages.
    - Generation-time logs (`GenerateAssembly`, `GenerateMethodForCommand`) — scrubbed `command.Name`, `commandInfo.Name`, `parameterSet.Name`, `ex.Message`. Also converted several `$"..."` interpolated log calls to structured templates while wrapping tainted args.
    - `HandlePowerShellErrors`, `InvokePowerShellSafe`, `InvokePowerShellSafeAsync`, `ConvertToJson` — scrubbed `operationName` (interpolates `commandName` at call sites) and PS error messages at every log sink.
- Added 9 focused tests at `PoshMcp.Tests/Unit/Observability/LogSanitizerTests.cs`. All pass.
- Build clean (0 errors; 19 pre-existing warnings — no new warnings introduced).
- Full Unit test slice: 452 passed, 0 failed.

**Files modified:**
- `PoshMcp.Server/Observability/LogSanitizer.cs` (new)
- `PoshMcp.Server/Observability/LoggerExtensions.cs`
- `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs`
- `PoshMcp.Server/PowerShell/PowerShellAssemblyGenerator.cs`
- `PoshMcp.Tests/Unit/Observability/LogSanitizerTests.cs` (new)

## Learnings

- **CWE-117 / `cs/log-forging`** — CodeQL's taint analysis tracks **call-site sinks**, not whether a Serilog enricher exists in the pipeline. Centralized enrichers do not close the alerts; call-site `Scrub()` does. (Farnsworth call.)
- When a log statement uses interpolated `$"..."` strings, prefer converting to structured templates (`"... {Foo} ..."` + arg) when adding scrub wrappers — it's both safer and gives observability platforms structured fields. Keep the change minimal: don't restructure messages that don't need scrubbing.
- For methods with many log calls referencing the same tainted value (here: `commandName` ≈25× in `ExecutePowerShellCommandTyped`), introduce one `safeFoo` local at the method top rather than wrapping `Scrub(...)` 25 times. Easier to read, single allocation, and you can document the sanitization rationale once.
- Distinguish carefully between log sinks and operational uses of the same value. `ps.AddCommand(commandName)` MUST stay raw — escaping the cmdlet name would break invocation. Only sanitize where the value flows into a logger/metric tag/scope.

---

## Recent Work (2026-05-03 — PRIOR SESSION)

### Fix: RequiredRoles OR Semantics
**Date:** 2026-05-03
**Status:** Complete

- Changed `HasRequiredRoles` in `AuthorizationHelpers.cs` from `.All()` to `.Any()`
- Fixes AND/OR mismatch: users need any one role, not every role
- Both `ToolAuthorizationFilter` and `ToolListAuthorizationFilter` inherit the fix automatically
- Build verified clean; committed as `fix(auth): use OR semantics for RequiredRoles checks`

**Files modified:**
- `PoshMcp.Server/Authentication/AuthorizationHelpers.cs`

## Learnings

- Entra app roles are granted one-at-a-time; AND semantics on role lists are unreachable in practice
- ASP.NET Core's `policy.RequireRole(string[])` uses OR — always match that behavior in custom helpers
- Small one-liner fixes can have wide blast radius; always check every caller before changing LINQ predicates

---

### Feature: Claims Mapping Fix + Token Proxy Logging
**Date:** 2026-05-03
**Status:** Complete

- Fixed MapInboundClaims pipeline to correctly transform inbound OAuth claims
- Ensured scope fields properly populated from claim paths
- Fixed RequiredScopes validation for authority/issuer handling
- Updated DoctorReport diagnostic output to reflect fixes
- Enhanced token proxy logging for OAuth flow traceability
- All integration tests passing

**Files modified:**
- OAuth proxy claim transformation logic
- RequiredScopes validation code
- DoctorReport diagnostic output
- Token proxy logging configuration

## Recent Work (2026-05-02 — PRIOR SESSION)

### Feature: Token diagnostics + configurable IdleTimeout (v0.9.12 prep)
**Date:** 2026-05-02
**Status:** Complete

#### 1. Token Diagnostics in `/token` proxy
- Upgraded `OAuthProxyEndpoints.cs` `/token` handler with diagnostic logging
- `LogInformation` on 2xx: logs status code and Content-Type (no token body)
- `LogWarning` on non-2xx: logs status code, Content-Type, and full response body (error JSON)
- `LogDebug` for request field names only (excludes `resource`; field names only, no values)
- Removed old single-line Debug log; replaced with structured conditional logging

#### 2. Configurable `IdleSessionTimeoutSeconds`
- Created `PoshMcp.Server/McpServerConfiguration.cs` with `McpServerConfiguration` class (namespace `PoshMcp`)
- Added `"McpServer": { "IdleSessionTimeoutSeconds": 60 }` to `appsettings.json`
- Updated `HttpServerHost.cs`: reads `McpServer` section via `authRootConfig`, passes `IdleTimeout` via `WithHttpTransport(opts => ...)` delegate overload
- Added `using ModelContextProtocol.AspNetCore;` to `HttpServerHost.cs`

**Key findings:**
- `WithHttpTransport` in `ModelContextProtocol.AspNetCore` 1.2.0 DOES have an overload accepting `Action<HttpServerTransportOptions>` — confirmed via package XML docs
- `HttpServerTransportOptions.IdleTimeout` is a `TimeSpan` property
- Build succeeded: 0 errors, 19 pre-existing warnings (no new warnings introduced)

**Files modified:**
- `PoshMcp.Server/Authentication/OAuthProxyEndpoints.cs` — enhanced /token logging
- `PoshMcp.Server/Server/HttpServerHost.cs` — IdleTimeout wiring + using
- `PoshMcp.Server/appsettings.json` — added McpServer section
- `PoshMcp.Server/McpServerConfiguration.cs` — new file (created)

### Diagnostic: Auth challenge/redirect on no-token MCP connect
**Date:** 2026-05-02
**Status:** In Progress (spawned 15:36:07)
**Focus:** Investigating why unauthenticated MCP clients not receiving auth challenge or redirect
**Session log:** `.squad/log/2026-05-02T15-36-07-auth-challenge-debug.md`

### Bug Fix: Entra v1.0 Authority causing JWT signature validation failure
**Date:** 2026-05-02
**Status:** Complete
**Commits:**
- `fix: use Entra v2.0 authority for JWT Bearer` (AdvocacyBami repo)
- `fix: warn when Entra Authority is v1.0 but ValidIssuers specifies v2.0` (poshmcp repo)

- **Root cause**: `Authority` in AdvocacyBami `appsettings.json` was `https://login.microsoftonline.com/{tenant}` (v1.0). This caused JWT Bearer middleware to fetch the v1.0 OIDC discovery doc and v1.0 JWKS. VS Code obtained tokens via the v2.0 endpoint, which are signed with v2.0 JWKS keys — keys absent from the v1.0 JWKS. Result: `SecurityTokenSignatureKeyNotFoundException`, 401, `DenyAnonymousAuthorizationRequirement` error.
- **Fix 1 (AdvocacyBami)**: Changed `Authority` to `https://login.microsoftonline.com/{tenant}/v2.0` so the v2.0 OIDC discovery doc (and v2.0 JWKS) are fetched.
- **Fix 2 (PoshMcp)**: Added a startup `Console.Error.WriteLine` warning in `AuthenticationServiceExtensions.cs` that fires when Authority is Entra v1.0 but `ValidIssuers` contains a v2.0 issuer — helps operators catch this misconfiguration early.
- **Build note**: `dotnet build --no-incremental` required due to pre-existing MSBuild "Question build" cache issue; build succeeded with 0 CS errors.

**Files modified:**
- `C:\Users\stmuraws\source\emu\gim-home\AdvocacyBami\appsettings.json` — Authority += `/v2.0`
- `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs` — added `using System;` + startup warning block

### Feature: /authorize proxy redirect endpoint (v0.9.11)
**Date:** 2026-05-02
**Status:** Complete
**Commits:** `feat(auth): add /authorize proxy redirect endpoint for VS Code OAuth`


---
*Further trimmed to 100 lines on 2026-05-05 by Scribe (15KB gate). Full record in `history-archive.md`.*

## 2026-05-06: New milestone-tagged issues assigned

Milestone #5 (Spec 004 - Out-of-Process PowerShell Execution) was created. You have issues assigned via squad:* labels:
- Bender: #190 (extract OutOfProcessHost), #192 (Option B - process pool prototype, blocked by #190)
- Fry: #193 (benchmark harness infra), #194 (wire harness to executors, blocked by #191/#192/#193)
- Farnsworth: #196 (adopt the winner, blocked by #195)

Check the issue body for plan reference and dependency chain before starting.
