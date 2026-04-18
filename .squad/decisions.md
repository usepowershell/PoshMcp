# Decisions Log

Canonical record of decisions, actions, and outcomes.


## References


## 2026-07-18

### Issue #131: STDIO logging to file — Architecture decision

Stdio transport must prevent console logging from polluting the JSON-RPC stream. Use Serilog file-backed logging with 3-tier resolution: CLI option > env var > config file. See detailed architecture below.

### 2026-07-18: Architecture decision — Issue #131 STDIO logging

**By:** Farnsworth

**What:**

## Problem

When PoshMcp runs in stdio transport mode, `ConfigureServerLogging` unconditionally calls `builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace)` and `ConfigureOpenTelemetry` unconditionally calls `.AddConsoleExporter()`. Both write to stdout/stderr, which pollutes the stdio pipe that MCP clients use for JSON-RPC communication.

Three affected sites in `Program.cs`:
1. `ConfigureServerLogging` — `AddConsole` (used by both stdio and HTTP paths via `RunMcpServerAsync` and indirectly)
2. `ConfigureOpenTelemetry` (stdio path in `ConfigureServerServices`) — `.AddConsoleExporter()`
3. `CreateLoggerFactory` — `AddConsole` (bootstrap / evaluate-tools path)

## Decision

Use **Serilog** for file logging in stdio mode. It is the industry-standard .NET structured logging library, integrates cleanly with `Microsoft.Extensions.Logging` via `UseSerilog()`, and `Serilog.Sinks.File` is battle-tested. No existing Serilog dependency exists in the project — this is a deliberate new dependency. Alternative (custom file logger) rejected: unnecessary maintenance burden when Serilog solves it idiomatically.

## Configuration

### New `appsettings.json` section

```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft.AspNetCore": "Warning",
    "Microsoft.Hosting.Lifetime": "Information"
  },
  "File": {
    "Path": ""
  }
}
```

`Logging.File.Path` is the appsettings key. Empty string or absent = no file logging.

### New environment variable

`POSHMCP_LOG_FILE` — absolute or relative path to the log file.

### New CLI option

`--log-file <path>` — added to the `serve` subcommand only.

### Resolution order (highest wins)

1. `--log-file` CLI option
2. `POSHMCP_LOG_FILE` environment variable
3. `Logging.File.Path` from appsettings.json
4. No file → silent (NullLogger / suppress all logging in stdio mode)

Add a new constant in `Program.cs`:
```csharp
private const string LogFileEnvVar = "POSHMCP_LOG_FILE";
```

## Implementation — Bender's scope (`Program.cs`, C# changes)

### 1. New CLI option

Add to `serve` command in `Main`:
```csharp
var logFileOption = new Option<string?>(
    aliases: new[] { "--log-file" },
    description: "Path to log file for stdio transport (suppresses console logging)");
serveCommand.AddOption(logFileOption);
```

Pass `logFile` into `ResolveCommandSettingsAsync` and `RunMcpServerAsync` (add parameter).

### 2. Log file resolution helper

```csharp
internal static ResolvedSetting ResolveLogFilePath(string? cliValue)
{
    return ResolveArgumentOrEnvironmentWithSource(cliValue, LogFileEnvVar, null);
    // null default = not configured
}
```

For appsettings resolution, read `Logging:File:Path` from the loaded `IConfiguration` after the config file is resolved. Merge: CLI/env wins over appsettings value.

### 3. New Serilog-backed logging configurator for stdio mode

```csharp
private static void ConfigureStdioLogging(HostApplicationBuilder builder, LogLevel? overrideLogLevel, string? logFilePath)
{
    // Remove default console providers — nothing goes to stdout/stderr in stdio mode
    builder.Logging.ClearProviders();

    if (!string.IsNullOrWhiteSpace(logFilePath))
    {
        var logger = new LoggerConfiguration()
            .MinimumLevel.Is(MapToSerilogLevel(overrideLogLevel ?? LogLevel.Information))
            .WriteTo.File(
                logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        builder.Logging.AddSerilog(logger, dispose: true);
    }
    // else: ClearProviders already installed NullLogger behavior — no output anywhere
}
```

Replace `ConfigureServerLogging(builder, overrideLogLevel)` call in `RunMcpServerAsync` with `ConfigureStdioLogging(builder, overrideLogLevel, resolvedLogFilePath)`.

### 4. Update `CreateLoggerFactory` (bootstrap/evaluate-tools)

Pass `logFilePath` parameter. When in stdio context and log file is configured, use Serilog sink. When no file, return a no-op factory. Keep existing `AddConsole` behavior only for HTTP/evaluate-tools paths that explicitly request it.

### 5. Required NuGet packages

Add to `PoshMcp.csproj`:
```xml
<PackageReference Include="Serilog.Extensions.Hosting" Version="9.0.0" />
<PackageReference Include="Serilog.Extensions.Logging" Version="9.0.0" />
<PackageReference Include="Serilog.Sinks.File" Version="6.0.0" />
```

Check NuGet for latest stable versions compatible with .NET 10 before pinning.

### 6. Tests

- Unit test: `ResolveLogFilePath` resolution priority (CLI > env > null)
- Unit test: `ConfigureStdioLogging` with file path → Serilog file sink registered; without path → `ClearProviders` only
- Integration test: start server in stdio mode with no log file, verify no output on stderr before first MCP message
- Integration test: start server in stdio mode with `--log-file`, verify log file created and messages appear there

## Implementation — Amy's scope (OTel + config schema + docs)

### 1. Suppress OTel console exporter in stdio mode

In `ConfigureOpenTelemetry` (stdio path, called from `ConfigureServerServices`):

```csharp
private static void ConfigureOpenTelemetry(HostApplicationBuilder builder, bool isStdioMode = false)
{
    builder.Services.AddSingleton<McpMetrics>();

    builder.Services.AddOpenTelemetry()
        .WithMetrics(metricsBuilder =>
        {
            metricsBuilder.AddMeter(McpMetrics.MeterName);
            if (!isStdioMode)
            {
                metricsBuilder.AddConsoleExporter();
            }
        });

    // ... rest unchanged
}
```

Pass `isStdioMode: true` from `ConfigureServerServices` when building for stdio transport. `ConfigureOpenTelemetryForHttp` is already HTTP-only and stays unchanged.

### 2. appsettings.json schema

Add `Logging.File.Path` to `appsettings.json`, `default.appsettings.json`, and `appsettings.environment-example.json`:

```json
"Logging": {
  "LogLevel": { ... },
  "File": {
    "Path": ""
  }
}
```

### 3. Documentation updates

**README.md** — Add to the configuration/environment variables section:

| Variable | Description | Default |
|----------|-------------|---------|
| `POSHMCP_LOG_FILE` | Path to log file when running in stdio transport mode. When set, all log output is redirected to this file. When unset in stdio mode, logging is suppressed entirely. | (none) |

Add `--log-file <path>` to the CLI reference for the `serve` subcommand.

Add a note under stdio transport usage: "Logging to console is disabled in stdio mode to prevent interference with the MCP JSON-RPC stream. Use `--log-file` or `POSHMCP_LOG_FILE` to capture logs."

**DOCKER.md** — Add `POSHMCP_LOG_FILE` to the environment variables table. Note that in container deployments, the log file path should point to a volume-mounted directory for persistence (e.g., `/data/poshmcp.log`).

**`appsettings.environment-example.json`** — Update example to include `Logging.File.Path`.

## Default behavior in stdio mode with no log file

**Silent** — `builder.Logging.ClearProviders()` removes all providers. No output to stdout, stderr, or any file. This is correct: the process is intentionally silent to avoid polluting the MCP pipe. If an operator needs diagnostics they must configure a log file path.

Do NOT fail startup or warn to stderr when no log file is configured in stdio mode — that would also pollute the pipe.

## What does NOT change

- HTTP transport logging: `AddConsole` stays, OTel `AddConsoleExporter` stays
- `doctor` command: writes to stdout intentionally (structured output, not logs)
- `list-tools` command: writes to stdout intentionally
- All `Console.Error.WriteLine` calls in pre-startup error handling (before transport is determined) stay — they're correct for CLI error reporting before stdio server starts

**Why:** Issue #131 — STDIO transport must not write logs to stdio. MCP clients (Claude Desktop, VS Code, etc.) communicate with the server exclusively over stdio and any non-JSON-RPC output corrupts the stream.


### 2026-07-18: PR #132 Review — Issue #131
**By:** Farnsworth
**Verdict:** Approved
**What was checked:**
- Logging suppression (ClearProviders first in ConfigureStdioLogging)
- Serilog file sink (rolling daily, 7-day retention, output template)
- Resolution order (CLI > env > appsettings > silent)
- OTel console exporter guarded by isStdioMode
- HTTP path unchanged (AddConsole + AddConsoleExporter still present)
- Null safety (IsNullOrWhiteSpace guards)
- Tests (7 unit + 2+ functional, 10/10 pass, full suite 487/0/1)
- Documentation (README + DOCKER updated with all three config options)
- Build warnings (0 new, 5 pre-existing unrelated)

**Issues found:**
- Non-blocking: `default.appsettings.json` (embedded) missing `Logging.File.Path` — functionally harmless
- Non-blocking: Root handler (bare `poshmcp`) skips `POSHMCP_LOG_FILE` resolution — legacy path, low priority

**Conclusion:** Implementation matches the design spec across all critical areas. ClearProviders unconditionally prevents stdio pollution, Serilog file sink is properly configured, 3-tier resolution works as specified, OTel suppressed in stdio mode, HTTP unaffected. Ship it.


- MCP Spec Authorization: https://modelcontextprotocol.io/specification/2025-06-18/basic/authorization
- C# MCP SDK v1.2.0 API: https://csharp.sdk.modelcontextprotocol.io/
- Full implementation plan: Session workspace `plan.md`

## 2026-04-14

### Deploy docs to GitHub Pages from prebuilt `docs/_site`

**Author:** Amy
**Date:** 2026-04-14
**Status:** Implemented

Deploy documentation to GitHub Pages from the prebuilt `docs/_site` directory using a dedicated workflow at `.github/workflows/docs-pages.yml`.

**Rationale:**
- Keeps CI simple and low risk by avoiding DocFX installation/build in workflow runtime.
- Matches the current repository state where `docs/_site` is already available.
- Uses official GitHub Pages actions with least-required permissions.
- Restricts deployments to documentation changes with `paths: docs/**`.

**Implementation notes:**
- Trigger: `push` on `main` with `paths: docs/**`, plus `workflow_dispatch`.
- Permissions: `contents: read`, `pages: write`, `id-token: write`.
- Concurrency: `group: pages`, `cancel-in-progress: true`.
- Actions: `actions/configure-pages@v5`, `actions/upload-pages-artifact@v3`, `actions/deploy-pages@v4`.

**Follow-up:** If docs source changes are committed without regenerating `docs/_site`, deployment can publish stale output. Consider adding DocFX build-in-CI later if this occurs.

### Build DocFX in CI before GitHub Pages deploy

**Author:** Amy
**Date:** 2026-04-14
**Status:** Implemented

Update docs deployment workflow (`.github/workflows/docs-pages.yml`) to run a DocFX build in CI before uploading and deploying Pages artifacts.

**Rationale:**
- Ensures deployed docs always match committed source content under `docs/`.
- Removes dependence on prebuilt `docs/_site` being manually regenerated.
- Keeps existing trigger scope, Pages permissions, concurrency, and deploy target unchanged.

**Implementation notes:**
- Keep trigger behavior: `push` on `main` with `paths: docs/**`, plus `workflow_dispatch`.
- Install DocFX via dotnet global tool in workflow runtime.
- Run `docfx build docs/docfx.json` from repository root.
- Upload generated `docs/_site` and deploy via existing GitHub Pages actions.

**Impact:**
- Slightly longer workflow runtime due to tool install/build.
- Lower risk of stale docs publication.

### Fix docs index API links to published API landing URL

**Author:** Leela (via Scribe)
**Date:** 2026-04-14
**Status:** Implemented

Use the published API landing URL `https://usepowershell.github.io/PoshMcp/api/PoshMcp.html` for API reference links in `docs/index.md` instead of `api/index.md`.

**Rationale:**
- Local DocFX builds report `InvalidFileLink` for `api/index.md` because there is no source-side `docs/api/index.md`.
- Published API URL keeps the homepage API link functional for readers.
- Scope stays limited to source docs content and avoids generated output or pipeline changes.

**Verification:**
- `docfx build .\\docs\\docfx.json` no longer reports `docs/index.md` invalid link warnings for the previous API link locations.
- Any remaining build warnings are unrelated to this API link change.

### Team intro framing for conference audiences

**Author:** Leela
**Requested by:** Steven Murawski
**Date:** 2026-04-14
**Status:** Implemented

Use a concise role-to-achievement mapping for team introductions, with 1-2 audience-friendly sentences per team member in `docs/articles/talk-team-introductions.md`.

**Rationale:**
- Keeps live delivery short and clear.
- Anchors each intro to verifiable project contributions.

**Impact:** Team-intro content for talk prep is now concise, consistent, and externally legible.

# Merge Session Decisions — PRs #92–#95

**Author:** Amy (DevOps/Platform)
**Date:** 2026-04-12
**Status:** Informational

## Summary

Sequential squash-merge of four approved PRs into main. All passed tests before merging.

| PR | Branch | Description | Tests Before | Tests After |
|----|--------|-------------|-------------|-------------|
| #92 | squad/86-use-default-display-properties-flag | `--use-default-display-properties` CLI flag | 343 passed | 343 passed |
| #93 | squad/87-warn-set-auth-enabled-no-schemes | Advisory warning when auth enabled with no schemes | 343 passed | 343 passed |
| #94 | squad/88-unit-tests-update-config-flags | 12 new unit tests for update-config CLI flags | 348 passed | 355 passed |
| #95 | squad/89-unserializable-parameter-types | Skip unserializable param types in MCP schema gen | 381 passed | 388 passed |

## Notable Operational Decisions

### `gh pr merge --delete-branch` exit code in worktrees
The `--delete-branch` flag on `gh pr merge` exits non-zero in a worktree environment because the local branch-delete step fails (`fatal: 'main' is already used by worktree`). The GitHub-side squash merge **succeeds**. This is expected behavior in a git worktree setup — the remote branch is deleted by GitHub; the local worktree ref cleanup fails harmlessly. No action needed; treat exit code 1 as a false failure when the merge confirmation line is present in stdout.

### `dotnet restore` required for cold worktrees
Worktrees that have not been previously built do not have `project.assets.json` present. `dotnet test --no-restore` fails with `NETSDK1004`. Always run `dotnet restore` first when testing a worktree that hasn't been built in the current session.

### Force-push requires explicit remote branch when upstream is not configured
`git push --force-with-lease` fails without an upstream tracking ref. Use `git push --force-with-lease origin <branch-name>` explicitly in worktrees.

# Decision: --use-default-display-properties CLI flag pattern

**Date:** 2026-04-14
**Author:** Amy
**Issue:** #86
**PR:** #92 (https://github.com/usepowershell/PoshMcp/pull/92)

## Decision

Added `--use-default-display-properties <true|false>` to `update-config`, following the exact same pattern as `--enable-result-caching` (PR #85). No new patterns were introduced.

## Rationale

Consistency: every scalar `Performance.*` setting in `PowerShellConfiguration` should be directly settable as a top-level CLI flag without requiring interactive prompts. `UseDefaultDisplayProperties` was the only one missing this treatment.

## Pattern Confirmed

All scalar boolean flags in `update-config` follow this four-step pattern in `Program.cs`:
1. `Option<string?>` declaration near line 180
2. `updateConfigCommand.AddOption(...)` near line 255
3. `GetValueForOption` + `TryParseRequiredBoolean` in handler, passed positionally to `ConfigUpdateRequest`
4. `if (request.X.HasValue)` block in `UpdateConfigurationFileAsync`, using `GetOrCreateObject` for the correct parent object and incrementing `boolUpdateApplied`

## Scope

Single file change: `PoshMcp.Server/Program.cs`, 15 lines added, 0 deleted.

# Decision: Advisory warnings in CLI commands go to stderr

**Date:** 2026-04-14
**Author:** Bender
**Issue:** #87

## Context

When `--set-auth-enabled true` is passed to `update-config` without any `Authentication.Schemes` configured, the server would fail at startup with `AuthenticationConfigurationValidator` but the user received no signal at config-write time.

## Decision

CLI advisory warnings that do not block an operation should be written to `Console.Error` (stderr), **not** stdout. This keeps stdout clean for structured output (e.g., `--output json`) while still surfacing important information to interactive users and CI pipelines that capture stderr separately.

## Pattern

```csharp
Console.Error.WriteLine("WARNING: <message>. Run 'poshmcp validate-config' to verify your configuration.");
```

Always prefix with `WARNING:` for easy grepping/filtering.

## Rationale

- Stdout may be parsed programmatically (`--output json`); mixing warnings there breaks parsers.
- Stderr is the conventional channel for diagnostic/advisory output in CLI tools.
- The write must not be blocked — the advisory is informational only.

## 2026-04-15

### README consistency source and link policy

**Author:** Leela
**Date:** 2026-04-15
**Status:** Proposed

For user-facing guidance in the root `README.md`, treat `docs/articles/*` as canonical. Keep archived materials in `docs/archive/*` explicitly marked as archived, and avoid links to removed root-level `docs/*.md` pages.

**Rationale:**
- Current docs IA centers on `docs/articles/*` for active guides.
- Root README had stale links (`docs/OUT-OF-PROCESS.md`, `docs/ENVIRONMENT-CUSTOMIZATION.md`, `docs/IMPLEMENTATION-GUIDE.md`) that no longer exist.
- Mixed command patterns in README caused drift from current docs (`poshmcp` CLI vs legacy `dotnet run` examples for common workflows).

**Consequences:**
- README remains stable as an onboarding surface while docs evolve.
- Reduced broken-link risk by preferring active docs paths and explicit archive links.
- Fewer support issues caused by outdated command examples.

**Scope:**
- Root `README.md` link and command examples.
- No behavior or code changes.
- Build succeeds with no new warnings
- PR #96 re-reviewed and ready for Farnsworth's approval

### 2026-04-13T08:50:30Z: User directive
**By:** Steven Murawski (via Copilot)
**What:** Whenever an agent creates a comment, issue, or PR on GitHub, sign it at the end with the agent's name (e.g., — Bender, — Farnsworth).
**Why:** Without signatures, GitHub activity looks like the repo owner talking to themselves. Agent attribution makes conversations legible.

# Decision: Guard against duplicate DiagnoseMissingCommands calls

**Author:** Farnsworth
**Date:** 2026-07-15
**Status:** Required (PR #96 rejection condition)

## Context

PR #96 adds `DiagnoseMissingCommands` for doctor command resolution diagnosis. The method creates an `IsolatedPowerShellRunspace` and runs `Get-Command`/`Import-Module` for each missing command — expensive operations.

## Problem

Both `RunDoctorAsync` and `BuildDoctorJson` independently call `DiagnoseMissingCommands`. When doctor runs in JSON format, introspection executes twice per missing command.

## Decision

`BuildDoctorJson` must guard the call: only invoke `DiagnoseMissingCommands` when `configuredFunctionStatus` entries with `Found=false` have `ResolutionReason is null`. This preserves standalone correctness (tests calling `BuildDoctorJson` directly) while avoiding double work from `RunDoctorAsync`.

## Impact

- PR #96 must be revised before merge
- Assigned to Bender (rejection lockout on Hermes)
- Pattern applies to any future expensive diagnostic that appears in both runtime and builder paths

# PR #84 Action Required — Rebase onto main

**Date:** 2026-07-15
**Author:** Farnsworth
**PR:** [#84 — fix: handle warning stream content during OOP server startup](https://github.com/usepowershell/PoshMcp/pull/84)

---

## Status

GitHub reports `mergeable: false / dirty`. **This is almost certainly a transient compute-lag, not a real conflict.**

`git merge-tree origin/main origin/squad/78-fix-oop-warning-stream` exits 0 with a clean tree — no conflicts.

---

## Files Changed in PR #84

| File | What PR #84 Does |
|---|---|
| `PoshMcp.Server/PowerShell/OutOfProcess/oop-host.ps1` | Adds `-WarningAction SilentlyContinue -WarningVariable` to all `Install-Module` and `Import-Module` calls; forwards captured warnings to `Write-Diag` (stderr) |
| `PoshMcp.Server/PowerShell/OutOfProcess/OutOfProcessCommandExecutor.cs` | Adds `IsNonJsonPowerShellStreamLine()` fast-path helper; skips non-JSON PowerShell stream lines at Debug level; demotes `catch(JsonException)` from LogWarning to LogDebug |
| `.squad/agents/farnsworth/history.md` | Appends PR #83 review note |

---

## Overlap With Already-Merged Work

All three files were also touched by commit `728b108` (#90 "Fixing tests") which landed on main after the PR branch was last synced.

| File | What #90 Changed | Conflict? |
|---|---|---|
| `OutOfProcessCommandExecutor.cs` | Line 62: added `-ExecutionPolicy Bypass` to `ProcessStartInfo.Arguments` | **None** — PR #84 edits lines 424-550 (ReadLoopAsync + helper method) |
| `oop-host.ps1` | Lines ~411+: added global include-pattern discovery block inside `Invoke-DiscoverHandler` | **None** — PR #84 edits lines 223, 247-264, 339-345 (Install/Import-Module params) |
| `history.md` | Appended PR #85 merge note | **None** — PR #84 appends different entry (PR #83 review) |

The PR's `PassThru = $true` (ImportModules success detection, already on main) is correctly reflected in the PR diff context — no duplication issue.

---

## Required Action

1. **Author** (`usepowershell` / Steven Murawski): update the PR branch to include main's latest commits:
   ```bash
   git checkout squad/78-fix-oop-warning-stream
   git merge origin/main   # or: git rebase origin/main
   git push origin squad/78-fix-oop-warning-stream
   ```
2. GitHub will recompute mergeability — it should flip to `true`.
3. **No code changes are needed** — the PR changes are correct, non-overlapping, and CI passes.
4. **Safe to merge immediately after the branch update.**

---

## Review Assessment

The fix is sound and the approach is appropriate for the current scope:
- Primary fix is at the source (oop-host.ps1 suppresses warnings before they hit stdout).
- Defensive C# fix (`IsNonJsonPowerShellStreamLine`) is a cheap fast-path guard against third-party modules that bypass WarningAction.
- Demoting `JsonException` catch from LogWarning to LogDebug eliminates alarm fatigue without hiding real errors.
- CLIXML and in-process-runspace alternatives acknowledged and deferred appropriately (tracked issue open if needed).

**Verdict: Approve and merge after rebase.**

# Decision: Approve and merge PR #85 — extend update-config all settings

**Date:** 2026-04-13
**Decision maker:** Farnsworth (Lead / Architect)
**PR:** https://github.com/usepowershell/PoshMcp/pull/85
**Author:** Amy
**Fixes:** Issue #76

## Approval Decision

**APPROVED and MERGED** (squash merge to `main`).

## Summary of Changes

PR #85 extends the `poshmcp update-config` CLI command to expose all remaining scalar configuration settings as top-level flags:

| Flag | Config Path |
|------|-------------|
| `--runtime-mode <in-process\|out-of-process>` | `PowerShellConfiguration.RuntimeMode` |
| `--enable-result-caching <true\|false>` | `PowerShellConfiguration.Performance.EnableResultCaching` |
| `--enable-configuration-troubleshooting-tool <true\|false>` | `PowerShellConfiguration.EnableConfigurationTroubleshootingTool` |
| `--set-auth-enabled <true\|false>` | `Authentication.Enabled` |

Additionally:
- Interactive per-function prompts extended with `AllowAnonymous`, `RequiredScopes`, `RequiredRoles`
- Interactive prompts now correctly cover `--add-command` entries (was functions-only bug)
- `boolUpdateApplied` counter upgraded bool → int; `SettingsChanged` exposed in text and JSON output

## Notable Patterns

### Correct JSON nesting
`Performance.EnableResultCaching` is nested under `powerShellConfiguration` (correct), while `Authentication.Enabled` is at the config root (correct). The `GetOrCreateObject` helper handles both levels cleanly.

### `NormalizeRuntimeMode` validation
New helper follows the same defensive pattern as `NormalizeFormat` and `TryParseRequiredBoolean` — normalizes casing variants (`in-process`, `inprocess` → `InProcess`) and throws `ArgumentException` for invalid input. Good pattern to continue.

### Complex auth config stays as direct JSON editing
JWT authorities, API keys, CORS — these deeply nested settings are intentionally NOT exposed as CLI flags. Direct JSON editing via `--config-path` is the right call. This is the correct long-term design: CLI flags for scalar toggles, direct JSON for structured config.

### Counter vs bool for settings-changed tracking
Upgrading `boolUpdateApplied` from `bool` to `int` is a strictly better design — it allows `settingsChanged: 3` in JSON output rather than a boolean, which is more informative and composable with future audit/logging.

## Non-blocking Observations (filed as issues)

- **#86** — Add `--use-default-display-properties` global flag for `Performance.UseDefaultDisplayProperties` (consistency)
- **#87** — Warn when `--set-auth-enabled true` used with empty `Authentication.Schemes` (UX improvement, not blocking)
- **#88** — Add unit tests for all 4 new flags in `ProgramCliConfigCommandsTests` (test coverage gap, Fry's queue)

# Decision: update-config flag test patterns (Issue #88)

**Author:** Fry  
**Date:** 2026-04-14  
**PR:** #94

## Summary

Closed the test coverage gap for the four CLI flags and interactive prompt extensions added in PR #85.

## Decisions Made

### 1. Structural assertions over raw file comparison
When asserting that a config file was NOT modified after an error, parse it as JSON and check specific keys rather than comparing raw strings. `UpgradeConfigWithMissingDefaultsAsync` normalizes line endings (`\n` → `\r\n`) as a side effect of config resolution on Windows, making raw string comparison brittle.

### 2. Assert stderr content for error paths
For `--runtime-mode invalid-value`, assert that `capture.StandardError` contains the invalid value string. This is more direct than checking `Environment.ExitCode` vs the `InvokeAsync` return value (which always returns 0 for Task handlers).

### 3. Authentication.Enabled placement assertion
The `--set-auth-enabled` test explicitly asserts both that `Authentication.Enabled` is set at the JSON root AND that `PowerShellConfiguration["Authentication"]` is null. This prevents accidental wrong-level placement by future refactors.

### 4. Existing interactive test extended, not duplicated
Rather than a separate test for AllowAnonymous/RequiredScopes/RequiredRoles, the new test `UpdateConfigCommand_WhenAddingFunction_InteractivePromptsCanSetAllowAnonymousRequiredScopesAndRoles` uses `Get-Service` (different function) with a full stdin sequence. The original `Get-Process` test was updated to supply blank-skip lines for the new prompts to avoid hanging on the extra `Console.ReadLine()` calls.

### 5. settingsChanged = boolUpdateApplied
The `settingsChanged` JSON field increments once per flag that writes a value (`boolUpdateApplied` in `UpdateConfigurationFileAsync`). It does NOT count function add/remove operations — those appear in separate fields (`addedFunctions`, `removedFunctions`).

# Decision: Doctor command resolution diagnostics pattern

**Author:** Hermes  
**Issue:** #91  
**PR:** https://github.com/usepowershell/PoshMcp/pull/96  
**Date:** 2026-07

## Decision

When `poshmcp doctor` reports a configured command as [MISSING], it now runs PowerShell introspection via `IsolatedPowerShellRunspace` and surfaces a human-readable reason explaining why the command was not resolved.

## Rationale

The doctor command exists for troubleshooting. Reporting [MISSING] with no context forces users to manually investigate PSModulePath, module exports, and parameter type issues. The fix surfaces actionable diagnostics directly.

## Pattern established

- Use `IsolatedPowerShellRunspace` (never the singleton) for any diagnostic introspection that runs outside the normal tool execution path
- Share ONE isolated runspace across all diagnostics in a single doctor call
- Use local functions inside `ExecuteThreadSafe` lambdas to avoid needing `System.Management.Automation.PowerShell` type references in Program.cs
- Diagnostic enrichment is additive: the `ConfiguredFunctionStatus` record gets a nullable `ResolutionReason` field, null when found or not diagnosed

## Diagnostic resolution order

1. `Get-Command <name>` in isolated session → found = unserializable param types skipped tool generation
2. Per configured module: `Get-Module -ListAvailable` → missing = not in PSModulePath
3. Per configured module: `Import-Module; Get-Command -Module <module> -Name <name>` → missing = module doesn't export command
4. Command in module → import order / discovery timing issue
5. No modules + not found → command not installed

## Scope

This pattern applies to any future doctor/diagnostic subcommands that need to explain why something is missing. Keep introspection in `IsolatedPowerShellRunspace`, keep it best-effort (catch and report errors), and surface reasons in both text and JSON output.

# Decision: Unserializable Parameter Type Filtering

**Author:** Hermes
**Date:** 2026-07
**Issue:** #89
**Status:** Implemented — PR #95

## Decision

When a PowerShell parameter type cannot be meaningfully represented as a JSON schema value, the MCP tool schema generator should filter it out rather than exposing a broken or misleading parameter entry.

### Rules

| Scenario | Action |
|---|---|
| Optional parameter with unserializable type | Drop from schema silently |
| Mandatory parameter with unserializable type (in a specific parameter set) | Skip that entire parameter set |
| All parameter sets skipped for a command | No MCP tool emitted; warning logged |

### Unserializable Type Criteria

A type is considered unserializable if it belongs to any of these categories:

- **Pointer/by-ref** — `IntPtr`, `UIntPtr`, `T*`, `T&`
- **Opaque PS types** — `PSObject`, `ScriptBlock`
- **Too generic** — `System.Object`
- **Delegate-derived** — `Delegate`, `Action`, `Func<>`, …
- **Binary streams** — `Stream` and any derived type
- **OS sync primitives** — `WaitHandle` and derived
- **Reflection handles** — `System.Reflection.Assembly`
- **PS runtime handles** — `System.Management.Automation.PowerShell`
- **Runspace types** — any type in `System.Management.Automation.Runspaces.*`
- **Arrays** — when the element type is itself unserializable

## Rationale

- JSON has no representation for OS handles, streams, callbacks, or opaque object wrappers.
- Including such parameters in the MCP schema would mislead callers about what values are acceptable.
- Skipping only the affected parameter sets (rather than the whole command) preserves reachability of overloads that use only serializable types.

## Implementation Location

- `PowerShellParameterUtils.IsUnserializableType(Type)` — predicate, can be reused anywhere parameter types are evaluated
- `PowerShellAssemblyGenerator.GenerateMethodForCommand` — filtering applied before IL generation
- `PowerShellAssemblyGenerator.GenerateAssembly` — per-command tracking + warning log when all parameter sets are skipped

### 2026-04-14: DocFX docs branding and Mermaid template baseline (consolidated)
**By:** Leela, Amy
**Status:** Accepted

**What:**
- Set DocFX global metadata `_appLogoPath` to `poshmcp.svg`.
- Ensure `poshmcp.svg` is explicitly included in `build.resource.files` so it is copied to `docs/_site`.
- Enable DocFX Mermaid rendering by using `build.template: ["default", "modern"]`.

**Why:**
- Keeps branding and navbar logo behavior source-driven in `docs/docfx.json` instead of patching generated files.
- Guarantees consistent logo asset availability in generated output for both root and nested docs pages.
- Enables Mermaid diagram rendering without introducing Node.js or `mermaid-cli` dependencies in CI.

**Validation:**
- `docfx docs/docfx.json` completed successfully.
- Generated docs output uses `poshmcp.svg` for navbar branding.

### 2026-04-14: Standardize DocFX navbar logo path to logo.svg
**By:** Steven Murawski (via Leela/Scribe)
**Status:** Implemented

**Decision:**
Use `logo.svg` as the canonical DocFX navbar logo path in source configuration.

**Rationale:**
- Align source configuration with published navbar contract (`<img id="logo" class="svg" src="logo.svg" alt="">`).
- Remove ambiguity between `poshmcp.svg` and `logo.svg` naming.
- Keep fixes targeted to docs source/config rather than generated output edits.

**Impact:**
- `docs/docfx.json` should use `build.globalMetadata._appLogoPath = "logo.svg"`.
- `docs/docfx.json` should include `logo.svg` under `build.resource.files`.
- `docs/logo.svg` is the canonical source asset for navbar branding.

**Verification:**
- `docfx build .\\docs\\docfx.json` succeeds.
- Generated `docs/_site/index.html` contains `<img id="logo" class="svg" src="logo.svg" alt="">`.
- Generated article pages contain `<img id="logo" class="svg" src="../logo.svg" alt="">`.

### 2026-04-14: Resolve DocFX environment link warnings within content boundaries
**By:** Steven Murawski (via Leela/Scribe)
**Status:** Implemented

**Decision:**
When a markdown page is intentionally included as a singleton from a larger folder, links to files outside the DocFX content graph should be converted to either in-scope docs links or stable external repository URLs.

**Rationale:**
- Keeps markdown valid under the current `docs/docfx.json` content graph.
- Minimizes edits while preserving reader intent for cross-references.
- Avoids widening DocFX content boundaries to solve warning-only issues.

**Impact:**
- In `docs/archive/ENVIRONMENT-CUSTOMIZATION.md`, out-of-scope local links should be replaced by in-scope docs links when equivalents exist.
- Repository-root/archive references without in-scope equivalents should use stable GitHub URLs.
- In `docs/articles/environment.md`, relative links should point to `../archive/ENVIRONMENT-CUSTOMIZATION.md`.

**Verification:**
- The six originally reported `InvalidFileLink` warnings are resolved.
- A follow-up pass resolved two remaining warnings.
- Final `docfx build .\\docs\\docfx.json` result is 0 warnings / 0 errors.
- `docs/_site/poshmcp.svg` exists after build.

### 2026-04-14: Route logo.svg through docs/public/ for DocFX build output
**By:** Steven Murawski (via Leela/Scribe)
**Status:** Implemented

**Decision:**
Move the canonical logo source to `docs/public/logo.svg` and route it through DocFX's `build.resource` mechanism so that `logo.svg` is emitted to `docs/_site/public/` during every build.

**Changes:**
- Created `docs/public/logo.svg` (canonical logo source location).
- `docs/docfx.json` `build.resource.files`: added `"public/logo.svg"`.
- `docs/docfx.json` `globalMetadata._appLogoPath`: changed from `"logo.svg"` to `"public/logo.svg"`.
- `docs/logo.svg` retained at root for backward compatibility.

**Rationale:**
- Deployment tooling expects the logo at `public/logo.svg` relative to the site root.
- All other static template assets (JS, CSS) land in `_site/public/` via the modern DocFX template; the logo should follow the same path.
- Template mechanism (`templates/poshmcp/public/logo.svg`) rejected to avoid conflating content asset with template asset.
- Post-build copy script rejected per task constraints.

**Verification:**
- `docfx build` completed with 0 warnings, 0 errors.
- `Test-Path docs/_site/public/logo.svg` returns `True`.

## 2026-04-15

### Authorization override matching for generated tool names
**By:** Steven Murawski (via Copilot/Bender)
**Status:** Implemented

**Decision:**
Resolve per-tool authorization overrides by command-name candidates derived from generated MCP tool names, preferring configured `CommandNames`/`FunctionNames` matches.

**Rationale:**
- Previous lookup behavior checked exact tool names and simple normalization but could miss command-name override keys when generated tool names included parameter-set suffixes.
- Matching generated tool names back to command names keeps per-command `FunctionOverrides` authorization policies effective.

**Impact:**
- Command-level authorization overrides now apply consistently to tools generated from parameter-set-specific method names.
- Existing command-name override configuration remains valid and predictable.

### Align auth docs with real FunctionOverrides matching behavior
**By:** Steven Murawski (via Fry/Copilot)
**Status:** Implemented

**Decision:**
Update docs to reflect actual `FunctionOverrides` resolver order: exact tool-name match first, then normalized command-name candidates.

**Rationale:**
- Prior docs implied generated MCP tool names were not valid override keys, which contradicted runtime behavior.
- Accurate docs reduce operator confusion and align guidance with implementation and tests.

**Impact:**
- Documentation now recommends command-name keys for durable configuration while acknowledging that generated tool-name keys are currently honored.
- Regression coverage includes precedence behavior so docs and implementation remain aligned.

# Decision Proposal: Keep DESIGN.md aligned with implementation boundaries

## Date
2026-04-15

## Proposed By
Farnsworth (Lead/Architect)

## Status
Proposed

## Decision
Adopt a lightweight architecture-doc consistency rule for DESIGN.md:
- Describe AI intent mapping as MCP client responsibility, not server responsibility.
- Describe PoshMcp server responsibilities as tool discovery, schema generation, execution, and transport hosting.
- Keep runtime and transport statements synchronized with implemented modes (`in-process`, `out-of-process`, `stdio`, `http`).
- Use active documentation paths in local links; avoid archived paths unless explicitly labeled archive material.

## Context
The architecture consistency pass found drift in boundary language and at least one stale local link. The implementation and docs now clearly expose dual transport and runtime modes, while intent mapping remains external to the server.

## Rationale
These guardrails preserve architectural clarity for contributors and reviewers, reduce onboarding confusion, and prevent design docs from becoming aspirational in areas that are already concretely implemented.

## Expected Impact
- Fewer architecture misunderstandings in PR reviews.
- Better consistency across DESIGN.md, README, and docs/articles.
- Reduced broken-link churn in design documentation.

## Suggested Follow-up
Add a periodic docs consistency check in release readiness (manual checklist item to start).

# Docker docs consistency guardrails (proposal)

**Author:** Leela (Developer Advocate)  
**Date:** 2026-04-15  
**Status:** Proposed

## Decision
Treat `DOCKER.md` as the canonical root-level container operations guide, and constrain consistency edits to factual, high-confidence alignment with current CLI + container behavior.

## Scope
- Keep `DOCKER.md` CLI-first (`poshmcp build`, `poshmcp run`), while including Docker-native equivalents for parity with docs/articles.
- Use container-accurate paths and entrypoint terminology (`/app/server/poshmcp`, `/app/server/appsettings.json`, `POSHMCP_TRANSPORT`).
- Ensure root-level links only target files that currently exist in-repo.

## Rationale
- Existing docs have mixed generations of guidance (CLI-first and Docker-native); parity examples reduce confusion without broad rewrites.
- Path and entrypoint precision prevents copy/paste failures in derived images and compose usage.
- Small, factual edits lower risk and preserve voice/tone in docs that users already reference.

## Follow-up (non-blocking)
- In a future docs sweep, align `docs/articles/docker.md` examples with the same canonical path and compose environment-variable pattern used in `DOCKER.md`.
# Decision: MimeType default belongs in the handler, not the model

**Date:** 2026-04-15
**Issue:** #129
**Author:** Bender

## Decision

`McpResourceConfiguration.MimeType` is now `string?` with no C# default.
The `"text/plain"` fallback is applied at runtime inside `McpResourceHandler`
(both `HandleListAsync` and `HandleReadAsync`) using `IsNullOrWhiteSpace` coalescing.

## Rationale

A model-level default of `"text/plain"` silenced the validator's null/whitespace check,
so operators who omitted MimeType from config received no warning — violating FR-027.
Moving the default to the handler preserves the runtime contract (FR-030) while
restoring the diagnostic signal.

## Impact

- `McpResourceConfiguration.MimeType` is nullable; callers must handle null.
- `McpResourceHandler` already used null-coalescing — no logic change needed there.
- Test stub `McpResourceDefinition` updated to `string?` to stay in sync.
- Binding tests updated: assert `null` from config binding, not `"text/plain"`.

# Decision: MimeType test was failing, not skipped

**Date:** 2026-04-18
**Author:** Fry
**Issue:** #129

## Finding

`Validate_ResourceWithNoMimeType_ReportsMimeTypeWarning` never had a `[Skip]` attribute. It was simply *failing* because `McpResourceConfiguration.MimeType` had a hardcoded `"text/plain"` default at the model level, preventing `IsNullOrWhiteSpace` from ever being true.

## Resolution

Once Bender made `MimeType` a nullable `string?` with no default (commit `78de3c7`), the validator's existing `IsNullOrWhiteSpace` guard fired correctly and the test passed without any change to test logic.

Fry updated only the inline comment to reference nullable behavior and committed `1419a20`.

## Implication for future

When a test appears to need "unskipping", check first whether it was actually skipped vs failing. A failing `[Fact]` with no Skip attribute just needs the underlying code fixed — no test-attribute surgery needed.

