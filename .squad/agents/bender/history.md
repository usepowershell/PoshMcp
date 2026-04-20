# Bender Work History

## Recent Status (2026-04-19)

**Summary:** Backend execution and diagnostic reliability work remains the core focus. Current emphasis is out-of-process lifecycle hardening, tooling diagnostics performance, and minimizing redundant PowerShell execution paths.

## Project Context

**Project:** PoshMcp - Model Context Protocol (MCP) server for PowerShell
**Tech Stack:** .NET 10, C#, PowerShell SDK, OpenTelemetry, ASP.NET Core, xUnit
**Primary User:** Steven Murawski

## Recent Learnings

### 2026-04-19: PR #135 — Copilot review feedback addressed (commit f209175)

**Feedback addressed:**
1. **ExitCode duplication** — Created `PoshMcp.Server/Cli/ExitCodes.cs` with a shared `internal static class ExitCodes` (Success=0, ConfigError=2, StartupError=3, RuntimeError=4). Both `Program.cs` and `DockerRunner.cs` now reference `ExitCodes.*`. Rejected Copilot's reflection-based suggestion as fragile; a dedicated constants file is simpler and correct.
2. **ConfigurationFileManager visibility** — Narrowed 7 helper methods from `internal static` to `private static`: `PromptForAdvancedFunctionConfiguration`, `IsYesAnswer`, `PromptForNullableBoolean`, `GetOrCreateObject`, `GetOrCreateArray`, `AddUniqueValues`, `RemoveValues`. None had external callers (grep confirmed).
3. **SettingsResolver.MergeMissingProperties** — Narrowed from `internal static` to `private static`; only used recursively within the class.
4. **ConfigurationTroubleshootingTools.InferEffectiveLogLevel** — Removed the private duplicate; delegated to `LoggingHelpers.InferEffectiveLogLevel(_logger)`. `using PoshMcp;` was already present.

**Patterns to remember:**
- Always grep ALL files (including tests) before making a method `private` to confirm there are no external callers.
- When Copilot suggests reflection to resolve a constant, a shared constants class is always the better answer.
- `internal` on a helper method is a code smell if it has no external callers — prefer `private` to minimize accidental coupling.

### 2026-07-18: Issue #131 — Stdio logging suppression + file sink

**What changed in Program.cs:**
- Added `private const string LogFileEnvVar = "POSHMCP_LOG_FILE";`
- Added `--log-file <path>` CLI option to `serve` command (converted handler to `InvocationContext` form to accommodate 8th option)
- Added `private static ResolvedSetting ResolveLogFilePath(string? cliValue, IConfiguration? config = null)` — resolves log file path with precedence: CLI > env > `Logging:File:Path` config key > null (silent)
- Added `private static void ConfigureStdioLogging(HostApplicationBuilder builder, LogLevel? overrideLogLevel, string? logFilePath)` — always calls `ClearProviders()`, then wires Serilog file sink if path is configured
- Added `private static LogEventLevel MapToSerilogLevel(LogLevel level)` helper
- Updated `RunMcpServerAsync` signature to accept `string? logFilePath = null` and call `ConfigureStdioLogging` instead of `ConfigureServerLogging`
- Updated `appsettings.json` to include `Logging.File.Path` key (empty by default)

**NuGet packages added:**
- `Serilog.Extensions.Hosting` 10.0.0
- `Serilog.Extensions.Logging` 10.0.0
- `Serilog.Sinks.File` 7.0.0

### 2026-07-28: Program.cs refactor extractions 2-4 (SettingsResolver, ConfigurationFileManager, ConfigurationLoader)

**What was extracted:**
- `SettingsResolver.cs` — All settings resolution logic plus `ResolvedSetting`, `ResolvedCommandSettings`, `TransportMode` types. Previous agent had already done most of this work; just needed commit.
- `ConfigurationFileManager.cs` — All JSON config mutation methods plus `CreateDefaultConfigResult`, `ConfigUpdateRequest`, `ConfigUpdateResult` types. Also includes `NormalizeFormat`, `TryParseRequiredBoolean`, `NormalizeRuntimeMode`.
- `ConfigurationLoader.cs` (in `Configuration/` subdir) — Config loading methods plus `TryValidateResourcesAndPrompts`. `ConfigurationTroubleshootingToolEnvVar` constant moved here. `ConfigurationTroubleshootingTools.cs` also needed updating.

**Surprises / patterns:**
- The PowerShell search-and-replace approach (`-replace` on the whole file) also mangles method definition lines, turning `private static void MethodName(...)` into `private static void ClassName.MethodName(...)`. Need to then surgically remove those mangled definition blocks separately.
- `ConfigurationTroubleshootingTools.cs` (in `PoshMcp.Server.PowerShell` namespace) had a direct `Program.LoadPowerShellConfiguration` call — non-obvious external reference. Always grep ALL files in the project, not just Program.cs, when looking for call sites.
- `ProgramTests.cs` had `Program.LoadPowerShellConfiguration` calls that needed updating to `ConfigurationLoader.LoadPowerShellConfiguration`. C# resolves types in parent namespaces (e.g., `PoshMcp` types from `PoshMcp.Tests.Unit`) without explicit `using` statements.
- The `StdioLoggingTests` were failing intermittently in the full suite but all 319 unit tests pass reliably — pre-existing flaky integration tests.
- Final Program.cs line count: **2395 lines** (still far above 150-line target; most extractions for this PR are pure logic, not the handlers/server-startup which are the bulk).

### 2026-04-14: Doctor diagnostics should not recompute expensive introspection

- Avoid duplicate execution of `DiagnoseMissingCommands` across runtime and JSON builder paths.
- Pass precomputed status data into JSON serialization helpers where available.
- Guard expensive fallback calls behind null/empty checks to preserve standalone correctness.

### 2026-04-14: Authorization overrides must map generated tool names back to base command names


### 2026-04-15: Cross-agent auth handoff with Fry review

- Fry's independent review confirmed docs wording and precedence tests should mirror the implemented override resolver order.
- Keep pairing resolver changes with both code-level precedence tests and docs updates to avoid drift between runtime behavior and operator guidance.

### 2026-04-11 to 2026-04-12: Out-of-process execution patterns

- `OutOfProcessCommandExecutor` should centralize subprocess lifecycle, NDJSON request/response matching, and cancellation timeout behavior.
- Cache discovery schemas after successful discover calls to avoid repeated roundtrips.
- Keep subprocess stdin writes serialized and pair responses via request IDs.

### 2026-04-09 to 2026-04-10: Large-result and harness stability patterns

- Large command results need shaping/limits and asynchronous invocation paths to reduce hang risk.
- Integration harnesses should avoid redundant builds and ensure child process tree cleanup on failures.

### 2026-03-27 baseline pattern still valid

- `app.Run()` is blocking in ASP.NET Core; any logic after it in the same flow is unreachable and should be removed.

### 2026-04-15: MimeType nullable fix (#129)

- Changed `McpResourceConfiguration.MimeType` from `string = "text/plain"` to `string?` (no default).
- The model-level default was shadowing the validator's `IsNullOrWhiteSpace` check — validator warning for absent MimeType never fired.
- `McpResourceHandler` already applied `IsNullOrWhiteSpace(r.MimeType) ? "text/plain" : r.MimeType` in both `HandleListAsync` and `HandleReadAsync` — no handler code changes needed.
- Test stub `McpResourceDefinition` in `PoshMcp.Tests/Models/McpResourceConfig.cs` updated to `string? MimeType` to mirror server type.
- Binding test `McpResourceDefinition_MimeType_DefaultsToTextPlain_WhenOmitted` renamed to `McpResourceDefinition_MimeType_IsNull_WhenOmitted` and asserts `null` — runtime fallback is in handler, not model.
- Pre-existing build warnings (5x CS8602 in `McpToolFactoryV2.cs`) are unrelated and not introduced by this fix.
- **Commit:** `6a93c3d` on `squad/129-fix-mimetype-nullable`

### 2026-04-18: Issue #129 MimeType Fix Completion (PR #130)

- Fix committed `6a93c3d` and rebased by Coordinator into worktree `poshmcp-129`.
- PR #130 opened at https://github.com/usepowershell/PoshMcp/pull/130.
- All 39 backend tests pass; validator warning now fires correctly.
- Handoff to Fry: test verification found no Skip attribute needed; test logic already correct.

## Learnings

### 2026-07-28: PR #139 — Secrets redaction in doctor output (commit c2e8814)

**Problem:** `IConfiguration.GetSection("Authentication")` and `GetSection("Logging")` values were surfaced verbatim into doctor JSON and text output, risking exposure of secrets (API keys, passwords, tokens, etc.).

**Solution:**
- Added `_sensitiveKeyPatterns` array (password, secret, key, token, connectionstring, credential, pwd, apikey, clientsecret)
- `IsSensitiveKey(string key)` — case-insensitive substring check across patterns
- `RedactSensitiveConfigValues(Dictionary<string,string?>)` — returns copy with sensitive-keyed values replaced by `[REDACTED]`
- Applied immediately after `LoadFlatConfigSection` in both `RunDoctorAsync` (before passing to `BuildDoctorJson`) and in the null-coalescing fallback path inside `BuildDoctorJson`
- Result: both text and JSON output paths always get redacted values

**Guard fix:** `TryLoadResourcesAndPromptsDefinitions` was called unconditionally on line 1166 even when `resourceDefinitions` and `promptDefinitions` were already pre-supplied. Wrapped it in `if (resourceDefinitions is null || promptDefinitions is null)` — matching the auth/logging pattern used 3 lines above.

**Tests added (3):**
1. `DoctorJson_SensitiveAuthConfigValues_AreRedacted` — ClientSecret is `[REDACTED]`, ClientId is not
2. `DoctorText_SensitiveAuthConfigValues_AreRedacted` — text output contains `ClientSecret=[REDACTED]`, not the raw value
3. `BuildDoctorJson_WithPreSuppliedResourceAndPromptDefs_UsesPreSupplied` — pre-supplied defs appear in JSON output

**Patterns to remember:**
- Any config section surfaced to diagnostic/observability output should run through redaction before it gets anywhere near serialization.
- `??=` is not enough to avoid a wasted call — need an explicit `if (x is null || y is null)` guard matching the pattern used for peer sections.
- `LoadPowerShellConfiguration(path, ILogger logger, ...)` will throw if `logger` is null — use `NullLogger.Instance` in unit tests.

### 2026-07-28 (continued): PR #135 merged, worktree cleaned up

PR #135 squash-merged to main (commit `5cb6533`). All 4 Copilot inline comments had been replied to in `f209175`. Summary comment posted, squash merge succeeded, remote branch deleted. Worktree `poshmcp-refactor-1-4` removed, local branch `refactor/program-cs-extract-1-4` deleted. Main pulled and fast-forwarded. Next PRs are E-G (remaining Program.cs extractions per `specs/program-cs-refactor.md`).

## Archive Note

Detailed prior history was archived to `history-archive.md` on 2026-04-14 when this file exceeded the 15 KB Scribe threshold.

## Cross-Agent: PR #138 Feedback Resolved (2026-04-20)

- Amy fixed PR #138 orphaned COPY line issue
- PR #138 now approved (worktree poshmcp-136)
- Both PRs approved and integrated
