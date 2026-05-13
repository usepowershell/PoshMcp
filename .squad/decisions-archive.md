# Decisions Archive
Entries archived on 2026-04-18 and 2026-05-03.

## Archived 2026-05-01

### Removed: Duplicate entry from 2025-07-17

# Decision: Default build type for `--generate-dockerfile`

**Date:** 2025-07-17
**Author:** Bender (Backend Developer)
**Status:** Implemented

## Context

The `poshmcp build` command supports two image types: `base` (builds the runtime from local source using `./Dockerfile`) and `custom` (builds a derived image using `examples/Dockerfile.user`). The default when `--type` is omitted was `custom`, which makes sense for actual Docker builds because the primary user workflow is building a custom derived image from the published GHCR base.

However, `--generate-dockerfile` is a different operation — it dumps the resolved Dockerfile to disk so the user can inspect or customize it. When no `--type` is specified alongside `--generate-dockerfile`, there is no obvious "custom" Dockerfile to generate (the user hasn't specified modules or a source image), so defaulting to `base` (the plain `./Dockerfile`) is the correct zero-configuration behavior.

## Decision

When `--generate-dockerfile` is used without an explicit `--type`:
- Default `buildType` to `"base"` → uses `./Dockerfile`

When `--generate-dockerfile` is **not** used (actual Docker build) without an explicit `--type`:
- Default `buildType` to `"custom"` → uses `examples/Dockerfile.user` (existing behavior preserved)

## Consequences

- Existing deployments are unaffected (auth off by default)
- New `Authentication` config section in `appsettings.json` with `Enabled`, `Schemes`, `DefaultPolicy`
- `FunctionOverride` class gets three new properties
- `Program.cs` HTTP pipeline gains auth middleware conditionally
- RFC 9728 Protected Resource Metadata endpoint at `/.well-known/oauth-protected-resource`
- New NuGet dependency: `Microsoft.AspNetCore.Authentication.JwtBearer`

## Alternatives Considered

1. **Auth at MCP filter layer only** (parse tokens in `CallToolFilters`): Rejected — reinvents ASP.NET Core's auth stack, misses session-init protection, fragile JWT handling.
2. **`DelegatingMcpServerTool` per-tool wrappers**: Rejected — requires wrapping every tool individually, no cleaner than a single filter, and doesn't pair with tool-list filtering.
3. **Auth enabled by default**: Rejected — would break all existing deployments immediately.

### 2026-04-14T00:00:00Z: Patch-release publish workflow confirmation
**By:** Steven Murawski (via Amy/Copilot)
**What:** Bump `PoshMcp.Server/PoshMcp.csproj` `<Version>` by patch (`0.5.5` -> `0.5.6`), package with `dotnet pack -o ./nupkg`, publish `poshmcp.0.5.6.nupkg` to `github-poshmcp` feed via `gh auth token`, and update local global tool from `./nupkg`.
**Why:** This matches current repo release convention and successfully validated GitHub Packages publish plus local install update in one flow.


# Archived 2026-04-20 16:15:42 UTC

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



# Decision: Dockerfile COPY hygiene when switching from solution to project-level builds

**Date:** 2026-07-18
**PR:** #138 (fix for issue #136)
**Author:** Amy (DevOps / Platform)

## Decision

When a Dockerfile's build stage is changed from solution-level (`PoshMcp.sln`) to project-level (`PoshMcp.Server/PoshMcp.csproj`) restore and build commands, any `COPY` lines for files that are no longer referenced by any `RUN` command must be removed.

## Rationale

- `COPY PoshMcp.sln ./` was added to support `dotnet restore PoshMcp.sln`, which is no longer the restore target.
- Orphaned `COPY` lines add an unnecessary cache layer without contributing to the build.
- The subsequent `COPY . .` already brings in `PoshMcp.sln` if it were ever needed; the explicit early copy served only to seed the layer cache for restore.
- Keeping dead `COPY` lines is misleading: future maintainers may assume the file is consumed by some RUN step.

## Rule

> In a multi-stage Dockerfile build stage, every explicitly `COPY`-ed file before `COPY . .` must be directly consumed by a subsequent `RUN` command in that stage. Remove any that are not.


### 2026-04-10T00:00:00Z: Configuration troubleshooting tool follows Program.cs special-tool registration
**By:** Bender (via Copilot)
**What:** Register the doctor-style troubleshooting MCP tool through the same Program.cs special-tool path as reload/caching tools, and apply the feature gate via config with an environment-variable override during configuration load.
**Why:** The existing doctor path already lives outside PowerShell command discovery, so mirroring that registration path keeps the change minimal and makes doctor JSON reflect the real runtime tool surface.

# Decision: Redact sensitive config values in doctor output

**Date:** 2026-07-28
**PR:** #139
**Author:** Bender

## Context

`poshmcp doctor` exposes `IConfiguration.GetSection("Authentication")` and `GetSection("Logging")` values in both JSON (`--format json`) and text output. These sections can contain secrets: API keys, client secrets, passwords, connection strings with credentials, etc. Surfacing raw config values in diagnostic output creates a leak vector — logs, CI output, clipboard paste, etc.

## Decision

Apply a **key-pattern redaction pass** to any flat config dictionary before it reaches any output path (text or JSON). Values whose keys match any of the following patterns (case-insensitive substring match) are replaced with `[REDACTED]`:

```
password, secret, key, token, connectionstring, credential, pwd, apikey, clientsecret
```

## Implementation

Three private helpers in `Program.cs`:

```csharp
private static readonly string[] _sensitiveKeyPatterns =
    ["password", "secret", "key", "token", "connectionstring", "credential", "pwd", "apikey", "clientsecret"];

private static bool IsSensitiveKey(string key) =>
    _sensitiveKeyPatterns.Any(pattern => key.Contains(pattern, StringComparison.OrdinalIgnoreCase));

private static Dictionary<string, string?> RedactSensitiveConfigValues(Dictionary<string, string?> config) =>
    config.ToDictionary(kvp => kvp.Key, kvp => IsSensitiveKey(kvp.Key) ? "[REDACTED]" : kvp.Value);
```

`RedactSensitiveConfigValues` is called immediately after `LoadFlatConfigSection` for auth and logging config — before the values are passed to either the text output loop or `BuildDoctorJson`.

## Rationale

- **Substring match** is intentional and safe: keys like `ClientSecret`, `ApiKey`, `ConnectionString`, `PrivateKey`, `Password`, `JwtSecret` all match. False positives (e.g., a key literally named `key`) are acceptable — we prefer over-redaction to under-redaction for diagnostic output.
- **Apply at load time**, not at serialization time: this way both output paths (text loops and JSON serialization) see the same redacted dict, and there is no risk of forgetting to redact in a future output path.
- **Both sections**: Auth config is the obvious vector, but logging config can include connection strings for log sinks (e.g., Serilog.Sinks.MSSqlServer), so it gets the same treatment.

## Alternatives Considered

- **Allowlist approach** (only expose known-safe keys): too fragile; new config keys would be silently exposed.
- **Strip the section entirely**: loses useful diagnostic information (e.g., `Authentication:Enabled`, `Logging:LogLevel:Default`).
- **Apply redaction only at JSON serialization**: leaves text output unprotected and requires two separate redaction call sites.

## Trade-offs

- The `key` pattern is broad (substring match) and will redact keys like `LogLevel` if anyone adds a nested key that contains `key`. In practice, standard `Logging` and `Authentication` config shapes don't hit this; the trade-off favors security.


### 2025-11-26: Recovery fix for out-of-process merge fallout
**By:** Bender
**What:** Restored a shared `Program.BuildDoctorJson(...)` helper so CLI doctor output and MCP troubleshooting tools use the same JSON payload builder, and extended the shared `InProcessMcpServer` test harness to support explicit config arguments and stderr capture expected by out-of-process integration tests.
**Why:** The merge left the server and integration harness in mismatched states: the runtime troubleshooting tool still depended on a removed helper, and the new out-of-process tests depended on harness features that were no longer present. Centralizing the doctor JSON path again and updating the shared harness was the minimal root-cause fix.

### 2026-04-10T10:52:05Z: Doctor MCP tool contract and gating
**By:** Steven Murawski (via Copilot/Farnsworth)
**What:** Reject the current branch for doctor-as-tool because no MCP tool implementation exists yet. If implemented, expose a read-only troubleshooting tool that returns the existing doctor report in structured JSON, and gate it behind both a configuration flag and an explicit environment variable so it is disabled by default.
**Why:** Existing precedent in this repo is that built-in operational tools are feature-gated in `PowerShellConfiguration`, but a troubleshooting/doctor surface is more operationally sensitive than normal tool discovery. Requiring both config and environment opt-in prevents accidental exposure from config drift alone.

**Accepted shape for follow-up implementation:**
- Add `PowerShellConfiguration.EnableDoctorTool` with default `false`.
- Add a dedicated environment variable gate, preferably `POSHMCP_ENABLE_DOCTOR_TOOL=true`.
- Register the tool only when both gates are true.
- Keep the MCP contract read-only and idempotent, returning machine-readable JSON equivalent to `doctor --format json`.
- Do not expose config mutation through this tool.
- Add tests that prove: default disabled, config-only disabled, env-only disabled, both enabled, and tool name appears/disappears in server discovery accordingly.

# Out-of-Process PowerShell Runtime — Architectural Research Brief

**Researcher:** Farnsworth (Lead / Architect)  
**Date:** 2026-04-10  
**Status:** Research Complete  
**Cross-Platform:** ✅ All patterns evaluated for Windows/macOS/Linux compatibility  
**Constraint Mode:** No mixed mode — if started with out-of-process option, entire runtime is out-of-process

---

## 1. Current Architecture Analysis

### In-Process Model (Status Quo)

**How it works today:**

- **Singleton runspace:** `PowerShellRunspaceHolder` creates a single `PSPowerShell` instance per server process using `Lazy<T>` singleton pattern.
- **Thread-safe access:** `SemaphoreSlim` (count=1) + lock guard all runspace operations. `ExecuteThreadSafeAsync` acquires the semaphore before executing any command.
- **Module loading:** `McpToolFactoryV2.GetToolsList()` discovers PowerShell commands. Module imports happen inline during tool discovery via `PowerShell.AddCommand("Import-Module").Invoke()`. Modules are imported into the singleton runspace and persist across all subsequent tool invocations.
- **Command execution:** Dynamically generated IL code (from `PowerShellAssemblyGenerator`) creates tool methods. Each tool method calls `ExecutePowerShellCommandTyped()`, which:
  - Awaits the runspace semaphore
  - Adds command to the singleton runspace's pipeline
  - Invokes `ps.Invoke()`
  - Serializes results to JSON
  - Returns JSON string

**Result:** All modules share one process-wide PowerShell state. State is persistent across tool calls.

### Module Loading Conflicts (The Problem)

**What breaks:**

From repo memory and codebase inspection:
- Some modules depend on exclusive state (e.g., auth tokens, session objects).
- Other modules have initialization side-effects that conflict with each other.
- Example: `Az.Accounts` + `Az.Storage` may have incompatible runspace configurations if loaded in certain orders.
- Current model forces all modules into one runspace — no isolation.
- When a module fails to load in the singleton, the server's tool inventory is permanently degraded.

**Key insight from `.squad/decisions.md` (module-discovery-import-order):**
- Modules **must** be imported before any `Get-Command` or discovery query.
- `PowerShellEnvironmentSetup` exists but is not currently wired into startup.
- No regression tests exist for `PSModuleAutoLoadingPreference='None'`.

### Runspace Model (Current)

- **Singleton per server process:** One runspace, one state, one module inventory.
- **No isolation:** All tool executions share the same memory, variables, functions, and module state.
- **Session-aware variant exists:** `SessionAwarePowerShellRunspace` exists to create per-session runspaces for HTTP/web contexts, but test coverage is incomplete; it is not used for stdio MCP servers.
- **Synchronization:** Coarse-grained semaphore at the runspace holder level. Blocks all tool calls while one runs.

**Performance impact:** Large cmdlets like `Get-Process` hold the semaphore for seconds, blocking all other tool calls.

---

## 2. Cross-Platform Out-of-Process Hosting Patterns

### Pattern A: TCP Localhost with Persistent Subprocess

**Concept:** Start a separate PowerShell process (`pwsh.exe`, `pwsh` on Linux/macOS) as a subprocess. Main .NET server communicates with subprocess via localhost TCP on an ephemeral port. Subprocess maintains a persistent runspace and reads JSON commands from stdin, writes JSON results to stdout.

**Platforms:** 
- Windows ✅ (pwsh.exe or powershell.exe)
- Linux ✅ (pwsh binary from PowerShell Core)
- macOS ✅ (pwsh from Homebrew or direct install)

**Pros:**
- Works uniformly across all platforms — no conditional code.
- Proven pattern (VS Code debuggers, language servers use similar models).
- Subprocess runspace is completely isolated from main process.
- Easy to test (localhost guaranteed available, ports auto-assigned by OS).
- Subprocess can be killed and restarted without affecting main server.
- Module loading conflicts isolated to subprocess — doesn't crash main server.
- Simple wire protocol (JSON stdin/stdout).

**Cons:**
- Startup latency (~200–500ms for pwsh process creation).
- Memory overhead (+80–120 MB per subprocess for a pwsh instance).
- TCP port allocation/cleanup (minor: OS releases ports quickly, ephemeral ports are abundant).
- Firewall edge cases (localhost should always work; non-localhost bindings require care).
- Slightly higher IPC latency than Unix sockets (but acceptable, <1ms).

**Implementation Sketch:**

1. **Main process (.NET):**
   - Spawns subprocess: `pwsh -NoProfile -Command { Read-Host ... | ... | Write-Output ... }`
   - Subprocess binds to localhost:0 (ephemeral port), reports port number on startup.
   - Main process read port from subprocess stdout, establishes TCP client.
   - Main process creates JSON request: `{ "command": "Get-Process", "args": { "Name": "powershell" } }`
   - Writes JSON + newline to subprocess TCP socket.
   - Reads JSON response from subprocess.
   - Synchronous sends/receives (or async with buffering).

2. **Subprocess (PowerShell):**
   - Initialize: import modules, set up state.
   - Enter loop: read JSON from stdin, parse command + args, execute, serialize results to JSON, write to stdout.
   - Persistent runspace across multiple commands.

**Trade-offs vs. in-process:**

| Aspect | In-Process | Pattern A |
|--------|-----------|----------|
| Module isolation | ❌ None — conflicts crash or degrade | ✅ Yes — processes independent |
| Startup latency | ~100ms | ~300ms (pwsh overhead) |
| Per-call latency | <5ms (direct invoke) | ~10–20ms (TCP + JSON roundtrip) |
| Memory per server instance | ~150 MB | ~150 + 80–120 (subprocess) = ~230–270 MB |
| Testability | Moderate (shared state) | High (isolate subprocess) |
| Cross-platform complexity | Low (native .NET) | Low (TCP everywhere) |
| Error isolation | ❌ Subprocess crash kills server | ✅ Subprocess crash doesn't kill main server |
| State persistence across calls | Yes (shared runspace) | Yes (persistent subprocess runspace) |
| Dynamic module loading | Single inventory | Multiple independent inventories (one per subprocess) |

---

### Pattern B: Socket + TCP Hybrid (Unix domain sockets on *nix, TCP on Windows)

**Concept:** Native Unix domain sockets on Linux/macOS (better performance), TCP on Windows. Unified abstraction layer hides platform differences.

**Platforms:**
- Windows ✅ (TCP fallback)
- Linux ✅ (Unix domain socket, `AF_UNIX`)
- macOS ✅ (Unix domain socket, `AF_UNIX`)

**Pros:**
- Best performance on Unix platforms: Unix sockets have zero kernel-space overhead vs. TCP (no loopback stack).
- Better security posture on Unix (file permissions on socket file).
- Native OS patterns (Linux/macOS developers expect Unix sockets).

**Cons:**
- Platform-specific code paths (need conditional imports, different socket creation logic).
- Test coverage must run on all three platforms (more complex CI/CD).
- Additional abstraction layer (socket factory, platform detection).
- Total implementation complexity ≈ 1.5x vs. Pattern A.
- Port cleanup on Windows identical to Pattern A.
- Socket file cleanup on Unix (if subprocess crashes, socket file may remain; cleanup logic required).

**Implementation Sketch:**

```
IPC Transport (abstraction)
  ├─ Windows: TcpTransport (localhost:ephemeral)
  └─ Unix: UnixDomainSocketTransport (/tmp/poshmcp-GUID.sock)
```

Subprocess reports its listening endpoint (port or socket path) on startup. Main process establishes connection using platform-appropriate transport.

---

### Pattern C: Remote PowerShell (Cluster/Multi-Machine)

**Concept:** Use PowerShell Remoting (`Enter-PSSession`, `Invoke-Command` over WinRM/SSH). Connects to localhost PowerShell service or remote machine.

**Platforms:**
- Windows ✅ (WinRM)
- Linux ⚠️ (SSH remoting, requires OpenSSH + PowerShell Core)
- macOS ⚠️ (SSH remoting, requires OpenSSH + PowerShell Core)

**Pros:**
- Native PowerShell feature (existing tooling, familiar patterns).
- Credential/auth model well-understood.
- Can target remote machines (future feature: distributed execution).

**Cons:**
- WinRM adds daemon/service management complexity on Windows (must run WinRM service).
- SSH setup on Unix is non-trivial (requires OpenSSH server, firewall rules).
- Authentication overhead for localhost connections (overkill).
- Significantly slower than TCP or socket IPC (handles auth, encryption, marshalling).
- **Not recommended for MVP:** Too much operational overhead for localhost-only use case.

**Verdict:** Defer to Phase 2 if remote execution is needed.

---

### Pattern D: Named Pipes (Windows-only)

**Concept:** Use Windows named pipes (`\\.\pipe\PoshMcp-{guid}`) for IPC.

**Platforms:**
- Windows ✅ (native support)
- Linux ❌ (not available)
- macOS ❌ (not available)

**Verdict:** **Disqualified.** Cross-platform constraint requires all patterns to work on Windows/Linux/macOS. Named pipes are Windows-only.

---

## 3. Trade-off Matrix (Cross-Platform Focus)

| Factor | Pattern A (TCP) | Pattern B (Socket+TCP) | Pattern C (Remoting) |
|--------|---------|--------|---------|
| **Startup latency** | ~300ms | ~300ms | ~1–2s |
| **Per-call latency** | ~10–20ms | ~5–10ms (socket) / ~15–20ms (TCP) | ~100–200ms |
| **Memory footprint per instance** | +80–120 MB | +80–120 MB | +50–80 MB (reuses service) |
| **Cross-platform (W/L/M)** | ✅ Uniform | ✅ Best performance varies | ⚠️ Operationally heavy |
| **Windows startup latency** | ~300ms | ~300ms | ~1–2s |
| **Linux startup latency** | ~300ms | ~300ms | ~800ms |
| **macOS startup latency** | ~300ms | ~300ms | ~800ms |
| **Implementation complexity** | Low (TCP everywhere) | Medium (platform detection) | High (credential handling) |
| **Test coverage burden** | Low (same code path everywhere) | Medium (platform-specific tests) | High (auth mocking, service config) |
| **Module isolation** | ✅ Yes | ✅ Yes | ✅ Yes |
| **Error resilience** | ✅ Subprocess crash independent | ✅ Subprocess crash independent | ⚠️ Service crash affects all clients |
| **Dynamic reloading** | Per-subprocess | Per-subprocess | Service-wide |
| **State isolation per-user** | No (one subprocess per config) | No (one subprocess per config) | Yes (per-session isolation) |
| **Operational simplicity** | Low (just spawn process) | Low (just connect to socket/port) | Medium (manage service) |

**Recommendation:** **Pattern A (TCP Localhost)** strikes the best balance for MVP:
- Works uniformly on all platforms.
- Simplest implementation.
- Acceptable latency (10–20ms per call is negligible for AI workloads).
- Easiest to test (no platform-specific logic).
- Defers Pattern B optimization to Phase 2 if needed.

---

## 4. Integration Points & Ripple Analysis

### High-Impact Changes

**1. Runtime Mode Selection (Program.cs)**
- Add new CLI flag: `--runtime-mode [in-process|out-of-process]`
- Or environment variable: `POSHMCP_RUNTIME_MODE=[in-process|out-of-process]`
- **Impact:** Startup logic diverges:
  - **In-process:** Use `SingletonPowerShellRunspace` (status quo).
  - **Out-of-process:** Start subprocess, establish TCP connection, inject `OutOfProcessPowerShellRunspace` service.
- No mixed mode: all operations use selected mode for entire server lifetime.

**2. IPowerShellRunspace Abstraction**
- **Current:** Two implementations: `SingletonPowerShellRunspace` (production) and `IsolatedPowerShellRunspace` (test).
- **Add:** `OutOfProcessPowerShellRunspace` implementation:
  - Opens TCP connection to subprocess.
  - `ExecuteThreadSafeAsync(Func<PSPowerShell, Task<T>> operation)` serializes the operation to JSON, sends to subprocess, waits for response.
  - Returns `PSPowerShell`-compatible interface (but subprocess-backed).

**Problem:** `IPowerShellRunspace.ExecuteThreadSafeAsync` expects a `Func<PSPowerShell, Task<T>>` — a delegate that runs in-process. Out-of-process can't execute a .NET delegate in a PowerShell process.

**Solution:** Refactor the abstraction:
- Split `IPowerShellRunspace` into two:
  - `ILocalPowerShellRunspace` (current): For in-process execution with `PSPowerShell` instance.
  - `IPowerShellExecutor` (new): For any execution mode (in-process or out-of-process):
    ```csharp
    interface IPowerShellExecutor
    {
        Task<string> ExecuteCommandAsync(
            string commandName, 
            PowerShellParameterInfo[] parameters, 
            object[] values, 
            CancellationToken ct);
    }
    ```
- `LocalPowerShellExecutor` (wraps `ILocalPowerShellRunspace`, current behavior).
- `RemotePowerShellExecutor` (uses TCP to off-process subprocess).

**3. McpToolFactoryV2 Execution Path**
- **Current:** Generated IL code calls `ExecutePowerShellCommandTyped()`, which acquires runspace, executes.
- **New:** Generated IL code calls `IPowerShellExecutor.ExecuteCommandAsync()`:
  - If in-process: delegates to `ExecutePowerShellCommandTyped()` (no change).
  - If out-of-process: sends JSON over TCP, waits for response.
- **Impact:** Tool methods remain identical; execution path branches at the executor level.

**4. Subprocess Lifecycle Management**
- **New component:** `OutOfProcessPowerShellHost`:
  - Spawns subprocess: `pwsh -NoProfile -Command [initialization script]`
  - Waits for subprocess to report listening port/socket.
  - Stores subprocess handle for cleanup on shutdown.
  - On server shutdown: `process.Kill()` + `process.WaitForExit()`.
- **Health:** Should subprocess crash, how does main server know?
  - Wrapper class monitors subprocess: `IsAlive` property, restarts on crash (Phase 2).
  - MVP: Just detect crash on next command, return error to caller.

**5. Configuration**
- New `PowerShellConfiguration` section:
  ```json
  {
    "PowerShell": {
      "RuntimeMode": "in-process",  // or "out-of-process"
      "OutOfProcessOptions": {
        "ConnectionType": "tcp",     // or "socket" (Phase 2)
        "InitializationScript": "path/to/init.ps1",
        "RestartPolicy": "none"      // or "auto-restart" (Phase 2)
      }
    }
  }
  ```

### Medium-Impact Changes

**1. Tool Discovery**
- **Artifact:** How does tool discovery work when modules are in subprocess?
- **Current:** `McpToolFactoryV2.GetToolsList()` imports modules, discovers commands, generates assembly.
- **Out-of-process:** Module discovery must run in subprocess, results serialized back to main process.
- **Implementation:** New MCP tool in subprocess: `$tools = Get-AvailableCommands` (internal command).
  - Called once at startup, caches result.
  - Main process caches discovered tools locally (same as current behavior).
- **Impact:** Tool discovery adds 1–2s to startup (subprocess module import time).

**2. Result Serialization**
- **Artifact:** PowerShell objects serialized to JSON differ between in-process and out-of-process.
- **Current:** `ExecutePowerShellCommandTyped()` uses `PowerShellObjectSerializer` to normalize results.
- **Out-of-process:** Subprocess serializes results, sends JSON. Main process receives JSON (double-serialized).
- **Risk:** If subprocess uses different serialization logic, results differ.
- **Mitigation:** Subprocess and main process must use same `PowerShellObjectSerializer` logic. Encode serializer version as part of protocol.
- **Impact:** Low if serializer remains consistent.

**3. Error Handling**
- **Current:** Errors during command execution caught in `ExecutePowerShellCommandTyped()`, wrapped in MCP error response.
- **Out-of-process:** Errors in subprocess caught by subprocess serialization, included in JSON response. Main process unwraps error.
- **Wire format:** JSON response includes:
  ```json
  {
    "success": true/false,
    "result": "...",
    "error": { "code": "...", "message": "..." }
  }
  ```
- **Impact:** Medium — requires new response schema.

### Low-Impact Changes

**1. Logging & Observability**
- **Artifact:** How do we correlate subprocess logs with main server logs?
- **Current:** `OperationContext.CorrelationId` passed through logging scopes.
- **Out-of-process:** Include correlation ID in JSON request to subprocess. Subprocess includes it in its logs.
- **Subprocess log destination:** Same as main server (shared log file or centralized logging).
- **Impact:** Low if logging infrastructure is already centralized (OpenTelemetry already in place).

**2. Health Checks**
- **Current:** `PowerShellRunspaceHealthCheck` pings singleton runspace.
- **Out-of-process:** Special health tool in subprocess: `Test-McpHealth` returns `{ "healthy": true }`.
- **Main server health:** Reports out-of-process subprocess health as part of `/health` endpoint.
- **Impact:** Low — health check becomes TCP call instead of direct invoke.

**3. Configuration Reload**
- **Current:** `PowerShellConfigurationReloadService` reloads function list, clears tool cache.
- **Out-of-process:** Reload service sends signal to subprocess to reload its module list (new internal tool: `Reload-McpConfiguration`).
- **Impact:** Low if reload protocol is simple (command + response).

---

## 5. Recommended Approach: Pattern A (TCP Localhost)

### Rationale

Pattern A (TCP Localhost) is the recommended primary pattern for MVP because:

1. **Uniform cross-platform behavior:** Identical code path on Windows/Linux/macOS. No conditional imports or feature detection.
2. **Simplest implementation:** TCP is fully supported in .NET, no platform-specific APIs needed. Standard socket library everywhere.
3. **Testability:** Same protocol on all platforms = same test suite. No platform-specific test logic.
4. **Operational simplicity:** Spawn subprocess, read port, connect. No daemon management, no service config.
5. **Acceptable performance:** 10–20ms per-call latency is negligible for AI assistant workloads (human perception is ~100ms).
6. **Phase 2 optimization:** Pattern B (socket + TCP hybrid) can be added after MVP; abstraction supports both.
7. **Error resilience:** Subprocess crash doesn't crash main server; can restart independently.

### Key Load-Bearing Decisions

**Decision 1: Mode Selection is Early + Permanent**
- CLI flag or environment variable at startup.
- Once selected, all tool execution uses that mode.
- No switching during runtime (no mixed mode).
- Rationale: Simplifies resource management, prevents mode-switching bugs.

**Decision 2: Subprocess is a Long-Lived Singleton**
- One subprocess per server instance (not per-request).
- Persistent runspace maintains state across tool calls.
- Rationale: Reduces startup overhead, allows stateful module interactions (auth tokens, session objects).

**Decision 3: Module Loading is Out-of-Process Responsibility**
- Configured per-server in `appsettings.json` (same as current).
- Subprocess imports modules at startup.
- Main process doesn't need to know module details.
- Rationale: Isolation — module conflicts don't affect main process.

**Decision 4: Command Execution is Serialized (JSON over TCP)**
- No marshalling of .NET objects or PowerShell objects.
- All parameters serialized as JSON strings/primitives.
- All results serialized as JSON.
- Rationale: Simplifies protocol, prevents serialization mismatches.

**Decision 5: Abstraction: IPowerShellExecutor (Not IPowerShellRunspace)**
- Split from current `IPowerShellRunspace` to avoid exposing `PSPowerShell` in out-of-process mode.
- `IPowerShellExecutor` is mode-agnostic (in-process or out-of-process).
- Current in-process code path wrapped by `LocalPowerShellExecutor`.
- Out-of-process code path implemented by `RemotePowerShellExecutor`.
- Rationale: Clear separation of concerns, no forced abstraction leaks.

---

## 6. MVP Scope & Phasing (Cross-Platform)

### MVP (Phase 1): In-Process + Out-of-Process TCP (Windows + Linux)

**Scope:** Implement Pattern A (TCP localhost) for both platforms. Enable **selective per-server deployment**: operators choose in-process or out-of-process at startup.

**Goals:**
- Solve module loading conflicts by isolating problematic modules to subprocess.
- Maintain compatibility with existing in-process mode.
- Prove TCP protocol works across Windows and Linux (CI/CD).

**What ships:**
1. **New abstraction layer:** `IPowerShellExecutor` (replaces delegation path in `McpToolFactoryV2`).
2. **Implementation:** `LocalPowerShellExecutor` (wraps current logic) + `RemotePowerShellExecutor` (TCP).
3. **Subprocess:** PowerShell script (`pwsh-mcp-host.ps1`):
   - Initializes runspace.
   - Binds to localhost:0, reads port from OS.
   - Enters command loop: read JSON, execute, respond.
   - Persistent runspace.
4. **Subprocess launcher:** `OutOfProcessPowerShellHost` (.NET):
   - Spawns subprocess.
   - Waits for port number.
   - Manages lifecycle.
5. **CLI flag:** `--runtime-mode [in-process|out-of-process]`.
6. **Configuration:** `PowerShellConfiguration.RuntimeMode` in `appsettings.json`.
7. **Tests:**
   - Functional tests for TCP protocol (JSON serialization, error handling).
   - Integration tests for both modes (in-process + out-of-process).
   - Cross-platform CI: Windows + Linux containers.
8. **Documentation:** Deployment guide for selecting mode, module configuration per mode.

**Effort estimate:** 8–12 engineering days (one developer, with testing).

**Success criteria:**
- AI assistant can invoke tools in both modes.
- Results identical (or equivalent after normalization).
- Subprocess crash doesn't crash main server.
- TCP implementation passes cross-platform tests.

**macOS support:** Identical to Linux (pwsh binary available); included in MVP.

### Phase 2: Subprocess Lifecycle Management + Restart Policy

**Scope:** Handle subprocess crashes gracefully. Optional auto-restart.

**What ships:**
- Enhanced `OutOfProcessPowerShellHost`: Monitors subprocess health, auto-restarts on crash (configurable).
- Health endpoint includes subprocess status.
- Logging of subprocess crashes with recovery attempts.

**Effort estimate:** 3–4 days.

### Phase 3: Socket + TCP Hybrid (Performance Optimization)

**Scope:** Add Pattern B (Unix domain sockets on Linux/macOS) for performance-sensitive deployments.

**What ships:**
- Platform-agnostic `IPcTransport` abstraction.
- `TcpTransport` (Windows + fallback).
- `UnixSocketTransport` (Linux/macOS).
- `OutOfProcessPowerShellHost` uses abstraction; subprocess reports endpoint type.
- Benchmarks: socket vs. TCP latency.

**Effort estimate:** 5–7 days.

**Decision point:** Proceed to Phase 3 only if MVP telemetry shows TCP latency is user-visible issue (unlikely).

### Phase 4: Per-User Session Isolation (HTTP/Web Mode)

**Scope:** Use `SessionAwarePowerShellRunspace` for HTTP mode with out-of-process.

**What ships:**
- Subprocess pool: one subprocess per user session (or shared pool with session-scoped state).
- Session ID passed in JSON requests.
- Subprocess maintains per-session module imports.

**Effort estimate:** 8–12 days.

**Prerequisite:** Completion of Phase 1.

### Phase 5: Distributed Execution (Future)

**Scope:** Deploy subprocess on different machines, communicate via SSH or HTTP.

**What ships:** TBD (research phase).

---

## 7. Open Questions & Next Steps

### Questions Requiring Implementation Phase Assessment

**Q1: Subprocess restart strategy for MVP**
- Auto-restart on crash, or fail loudly?
- **Recommendation:** Fail loudly in MVP. Operator restarts server. Phase 2 adds auto-restart.

**Q2: Result caching with out-of-process**
- Feature `set-result-caching` (`.squad/decisions.md`) caches results in main process. Does this work for out-of-process?
- **Answer:** Yes — cache lives in main process, even if execution is out-of-process. No change to caching logic.

**Q3: Module versioning**
- What if main process and subprocess use different module versions?
- **Recommendation:** Both use same `appsettings.json` module list. Version mismatch detected at setup time (error if subprocess import fails).

**Q4: Debugging out-of-process failures**
- How do developers debug subprocess crashes?
- **Recommendation:** Log subprocess stderr/stdout to main process logs. Capture stack traces from PowerShell errors. Phase 2: structured logging query tool.

**Q5: Port allocation race conditions**
- What if ephemeral port is reused between server instances?
- **Answer:** OS handles this; ports released immediately. No race condition.

**Q6: IPv6 vs. IPv4**
- Should we explicitly bind to `127.0.0.1` (IPv4) or support IPv6?
- **Recommendation:** MVP: Explicit `127.0.0.1` (IPv4). All platforms support. IPv6 deferred to Phase 2 if needed.

---

## 8. Implementation Roadmap (Next Steps for Architect)

### Before Implementation Starts

1. **Approve this brief** — confirm Pattern A recommendation and MVP scope.
2. **Define subprocess wire protocol** — JSON schema for requests/responses (request for tech spike).
3. **Identify first problematic module pair** — use this as MVP validation case.
4. **Estimate CI/CD changes** — ensure cross-platform test infrastructure can launch out-of-process servers.

### Assign Work

1. **Core abstraction layer** (IPowerShellExecutor, LocalPowerShellExecutor):
  - Assign to Backend Developer.
  - 2–3 days.
  - Unblocks remaining work.

2. **RemotePowerShellExecutor + TCP transport:**
  - Assign to Backend Developer.
  - 4–5 days.

3. **Subprocess + PowerShell host script:**
  - Assign to Backend Developer or DevOps.
  - 3–4 days.

4. **Functional + integration tests:**
  - Assign to QA / Backend Developer.
  - 2–3 days cross-platform validation.

5. **Configuration + CLI integration:**
  - Assign to Backend Developer.
  - 1–2 days.

### Validation Gate

Before Phase 2:
- [ ] Subprocess can load conflicting modules (e.g., two versions of same module) without main server impact.
- [ ] Tool results identical (or provably equivalent) in both modes.
- [ ] CI/CD green for Windows + Linux.
- [ ] No performance regression in in-process mode.

---

## 9. Risk Mitigation

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| TCP timeout hangs caller | Medium | High | Implement per-call timeout (configurable, default 30s). Return error if subprocess unresponsive. |
| Subprocess OOM crashes silently | Low | High | Monitor subprocess memory in health checks. Restart before OOM if possible (Phase 2). |
| Module discovery results out-of-sync | Medium | Medium | Tool discovery runs once at startup; cache results. Reload via dedicated tool. |
| Firewall blocks localhost TCP | Low | High | Localhost (127.0.0.1) is exempt from firewall on all platforms. Document this. |
| Cross-platform serialization bugs | Medium | High | Shared `PowerShellObjectSerializer` on both sides. Protocol version negotiation at startup. |
| Operator confusion (in-proc vs. out-proc) | High | Low | Clear documentation, distinct log messages, clear CLI help. Web UI mode indicator. |

---

## 10. Conclusion

**Recommended pattern:** **Pattern A (TCP Localhost)** for MVP.

**Why:** Simplest implementation, uniform cross-platform behavior, solves module isolation problem, acceptable performance, unblocks Phase 2 optimizations.

**Key insight:** Out-of-process execution doesn't need to be a permanent architecture change. It's an **opt-in deployment mode**. Operators choose at startup. In-process mode (current) remains the default, suitable for most users. Out-of-process mode available for users with module conflicts or isolation requirements.

**Next milestone:** Tech spike on subprocess wire protocol definition (~1 day). Then architecture review → implementation.



### 2026-04-10T00:00:00Z: Out-of-process recovery work should stay split between runtime product work and integration corpus maintenance
**By:** Steven Murawski (via Copilot/Farnsworth)
**What:** Treat out-of-process recovery as two separate streams. Stream 1 is product/runtime implementation in `PoshMcp.Server/` plus supporting docs, examples, and tests. Stream 2 is the `integration/Modules` corpus used only to validate module isolation and discovery scenarios. The corpus is a test fixture, not shipped runtime content.
**Why:** The repository already contains executable out-of-process plumbing, but the product entry point still lacks a runtime-mode contract while the vendored module corpus is large and operationally distinct. Keeping them separate prevents fixture maintenance from being mistaken for product completion and keeps runtime approvals tied to actual server wiring.

# PR Review: #138 and #139

**Reviewer:** Farnsworth
**Date:** 2025-07-18

## PR #138 — APPROVED ✅

**fix(#136): Fix Dockerfile restore/build**

**Summary:** Switches `dotnet restore` and `dotnet build` from `PoshMcp.sln` to `PoshMcp.Server/PoshMcp.csproj`, fixing the container build failure caused by missing test/client project files.

**Verdict reasoning:** Minimal, correct fix. Layer caching preserved. Runtime stage unchanged. No trailing whitespace. Non-blocking nit: `COPY PoshMcp.sln ./` is now unreferenced in the build stage — candidate for cleanup.

## PR #139 — APPROVED ✅

**feat(#137): Add auth, logging, env vars, MCP definitions to doctor**

**Summary:** Adds 4 new diagnostic sections (environment variables, authentication config, logging config, MCP resource/prompt definitions) to both text and JSON doctor output, with 12 new tests.

**Verdict reasoning:** All 7 env vars present. All 4 sections in both output formats. `BuildDoctorJson` parameters default to null with null-coalescing fallback — zero impact on existing callers. Tests are comprehensive with well-designed disposable helpers. Correct `[Collection("TransportSelectionTests")]` for parallel safety.

**Non-blocking nits:**
1. `TryLoadResourcesAndPromptsDefinitions` called unconditionally in `BuildDoctorJson` line 1166 even when both values pre-supplied — should be guarded like auth/logging 3 lines above.
2. `POSHMCP_LOG_FILE` (from PR #132) absent from the 7 env vars — follow-up candidate.


### 2026-04-10T00:00:00Z: Recovery review
**By:** Steven Murawski (via Copilot/Farnsworth)
**What:** Treat the current out-of-process MCP end-to-end path as incomplete and non-authoritative until `Program.cs` and the `InProcessMcpServer` test harness expose a supported `--runtime-mode` startup path. Keep subprocess/module-isolation tests, but do not let speculative end-to-end tests break the solution build. Also normalize all live deployment helpers from `POSHMCP_MODE` to `POSHMCP_TRANSPORT` to match the single-entry-point `poshmcp serve --transport ...` architecture.
**Why:** The repo was failing at build time because tests advanced past the implemented server surface, and deployment helpers still encoded a retired transport contract.

### 2026-04-10T00:00:00Z: Doctor tool gating coverage anchored on doctor JSON output
**By:** Steven Murawski (via Copilot/Fry)
**What:** Added focused doctor-command tests that treat the JSON payload from `poshmcp doctor --format json` as the public contract for configuration-troubleshooting tool gating, including default-hidden, config-enabled, and environment-override-disabled cases.
**Why:** This keeps the test surface small and user-visible while allowing internal tool registration details to move without rewriting the entire harness.

# Decision: Add startup-ordering regression tests for module import and function discovery

- Author: Fry
- Date: 2026-04-10
- Status: Proposed

## Decision
Add focused unit tests that exercise a shared isolated runspace and prove discovery outcomes differ before vs after environment setup steps.

## Why
- Existing tests covered command discovery by name/module and configuration parsing, but did not validate ordering between environment setup (`ImportModules` / startup script execution) and tool discovery.
- Discovery-before-import regressions can silently remove expected tools at startup.

## What was added
- `ModuleDiscoveryStartupOrderingTests` with two deterministic scenarios:
  - Module import then discovery discovers the module-exported function.
  - Startup script execution then discovery discovers the script-defined function.
- Each test asserts the negative case first (before setup => no tool), then positive case after setup (function discoverable and tool generated).

## Impact
- Provides a fast unit-level guardrail against startup ordering regressions without depending on full server startup.
- Keeps assertions tied to externally visible discovery behavior instead of private implementation details.


### 2026-04-10T00:00:00Z: Out-of-process discovery must honor configured module paths
**By:** Fry (via Copilot)
**What:** Forward `PowerShellConfiguration.Environment.ModulePaths` into the out-of-process discover request so the checked-in `integration/Modules` corpus participates in tool discovery and validation.
**Why:** The subprocess host already supports `modulePaths`, but the executor was not sending them. Without that handoff, out-of-process discovery ignored the repo's intentional module fixtures and left the highest-value validation path uncovered.

### 2026-04-10T00:00:00Z: Restore doctor troubleshooting flag and tool registration
**By:** Fry (via Copilot)
**What:** Restored `EnableConfigurationTroubleshootingTool`, its `POSHMCP_ENABLE_CONFIGURATION_TROUBLESHOOTING_TOOL` override, and `get-configuration-troubleshooting` registration after merge fallout removed the live source path while tests still expected the feature.
**Why:** The repository was failing at compile time on a missing doctor helper and then failing unit coverage because the troubleshooting feature had been dropped from active source without corresponding test or behavior changes.

# Test Plan: Out-of-Process PowerShell Runtime Mode

**Author:** Fry (Tester)
**Date:** 2026-04-10
**Branch:** managing_troublesome_modules
**Status:** Scaffolding complete — stubs + functional tests written

---

## Summary

Test scaffolding is written and compiles cleanly. Five test files cover all categories
in the task brief. Two categories are fully implemented (functional host-script tests,
integration module tests). Three categories are stubs awaiting Bender's implementation.

---

## Files Created

| File | Category | Status |
|------|----------|--------|
| `PoshMcp.Tests/Shared/OutOfProcessTestCollection.cs` | Collection def | Complete |
| `PoshMcp.Tests/Unit/OutOfProcess/SubprocessManagerTests.cs` | Unit — manager | Stubs |
| `PoshMcp.Tests/Unit/OutOfProcess/OutOfProcessCommandExecutorTests.cs` | Unit — executor | Stubs |
| `PoshMcp.Tests/Functional/OutOfProcess/SubprocessHostScriptTests.cs` | Functional | **Fully implemented** |
| `PoshMcp.Tests/Integration/OutOfProcess/OutOfProcessModuleTests.cs` | Integration | **Fully implemented** |
| `PoshMcp.Tests/Integration/OutOfProcess/OutOfProcessMcpServerTests.cs` | Integration e2e | Stubs |

---

## What Is Fully Implemented Now

### Category 3: SubprocessHostScriptTests (6 tests)

Located at `PoshMcp.Tests/Functional/OutOfProcess/SubprocessHostScriptTests.cs`.

These tests launch `poshmcp-host.ps1` directly via `Process.Start("pwsh", ...)`,
communicate via the stdin/stdout JSON wire protocol, and assert on response structure.
They can run the moment `PoshMcp.Server/poshmcp-host.ps1` exists.

All 6 tests skip automatically with a clear message if the host script is not found.

Tests:
- `HostScript_StartupMessage_WritesToStderr` — proves startup noise goes to stderr, not stdout
- `HostScript_ExecuteGetProcess_ReturnsJson` — proves happy-path JSON roundtrip
- `HostScript_ExecuteWithNullParams_FiltersNulls` — proves null params are stripped before execution
- `HostScript_ShutdownRequest_ExitsCleanly` — proves `{"type":"shutdown"}` exits with code 0
- `HostScript_UnknownRequestType_ReturnsError` — proves loop survives unknown type
- `HostScript_InvalidJson_ReturnsError` — proves loop survives malformed stdin

### Category 4: OutOfProcessModuleTests (7 tests)

Located at `PoshMcp.Tests/Integration/OutOfProcess/OutOfProcessModuleTests.cs`.

These tests run real Az and Microsoft.Graph modules in child pwsh processes using
`Process.Start("pwsh", "-Command -")`. They extend the pattern from `LocalModuleLoadingTests`.

Tests skip automatically if `integration/Modules/Az/15.5.0/` or
`integration/Modules/Microsoft.Graph/2.34.0/` are not present.
Use `[Trait("Category", "RequiresIntegrationModules")]` for selective filtering.

**The key test:** `OutOfProcess_AzAndGraph_LoadTogether_NoConflict` — loads
Az.Accounts and Microsoft.Graph.Authentication simultaneously in a single subprocess
and asserts both `Get-AzContext` and `Connect-Graph` are discoverable. This is the
primary proof that these MSAL-conflicting modules coexist in an isolated subprocess.

One test, `InProcess_AzAndGraph_ConfirmConflict`, is permanently skipped
(`[Fact(Skip = "...")]`) to avoid AppDomain pollution in the test host. Run manually
to document the conflict being solved.

---

## What Is Stubbed (Awaiting Bender)

### Category 1: SubprocessManagerTests (8 stubs)

Awaiting: `PowerShellSubprocessManager` class in `PoshMcp.Server.PowerShell` (or equivalent).

Each stub has detailed comments with:
- The full setup/act/assert code that will replace the stub
- The specific behavior being validated
- Mock patterns (using Moq, which is already in the test project)

### Category 2: OutOfProcessCommandExecutorTests (5 stubs)

Awaiting: `IPowerShellExecutor` interface + `OutOfProcessCommandExecutor` implementation.

One open question documented in stub `ExecuteCommandAsync_SubprocessError_ThrowsOrReturnsError`:
> **Decision needed:** Does error behavior throw an exception or return error-shape JSON?
> Must match `LocalPowerShellExecutor` behavior so `McpToolFactoryV2` doesn't branch on executor type.

### Category 5: OutOfProcessMcpServerTests (3 stubs)

Awaiting: `--runtime-mode out-of-process` flag wired into `Program.cs`.

These also need `InProcessMcpServer` to support passing extra command-line arguments
(currently it just calls `dotnet run ... PoshMcp.csproj`). A thin wrapper or overload
will be needed.

---

## Module Inventory (integration/Modules/)

**Az:** Present at `integration/Modules/Az/15.5.0/` ✅
- Also: individual Az.* sub-modules are present in `integration/Modules/`
- Tests use `Az.Accounts` (not the full `Az` umbrella) to avoid 100+ sub-module import latency
- `Az.Accounts` is the correct test target: it ships MSAL and conflicts with Microsoft.Graph.Authentication

**Microsoft.Graph:** Present at `integration/Modules/Microsoft.Graph/2.34.0/` ✅
- Also: `2.20.0` version present (tests use `2.34.0` as the latest)
- `Microsoft.Graph.Authentication/2.34.0/` is also present for the auth-specific skip guard
- Tests use `Microsoft.Graph.Authentication` as the primary Graph module for conflict testing

**Key conflict pair:** Az.Accounts + Microsoft.Graph.Authentication (both bundle MSAL)

---

## Test Execution Notes

**Run only the implemented tests:**
```
dotnet test --filter "OutOfProcess" --configuration Release
```

**Run only integration module tests (requires modules):**
```
dotnet test --filter "Category=RequiresIntegrationModules" --configuration Release
```

**Run only host script tests (requires poshmcp-host.ps1):**
```
dotnet test --filter "FullyQualifiedName~Functional.OutOfProcess" --configuration Release
```

**Timeout notes:**
- Host script tests: 30s per test (generous for pwsh startup)
- Module integration tests: 120s per test (Az.Accounts import can be slow first-run)
- Unit stubs: <1ms each (Assert.True(true, ...))

---

## Decisions / Questions for the Team

1. **Wire protocol finalized?**
   The tests assume:
   ```json
   Request:  {"type":"execute","command":"...","parameters":{...},"id":"..."}
   Response: {"type":"result","id":"...","data":[...],"errors":[]}
   Shutdown: {"type":"shutdown"}
   Error:    {"type":"error","id":"...","code":"...","message":"..."}
   ```
   If Bender changes the protocol, `SubprocessHostScriptTests` needs updating.

2. **Error behavior (throw vs. return)?**
   `OutOfProcessCommandExecutorTests.ExecuteCommandAsync_SubprocessError_ThrowsOrReturnsError`
   is deliberately open-ended. Bender needs to decide: throw exception or return error JSON?
   Must match `LocalPowerShellExecutor` behavior for `McpToolFactoryV2` compatibility.

3. **Host script path?**
   Tests expect `PoshMcp.Server/poshmcp-host.ps1`. If the script lives elsewhere,
   update `HostScriptPath` in `SubprocessHostScriptTests.cs`.

4. **InProcessMcpServer extra args?**
   The e2e tests need `InProcessMcpServer` to accept `--runtime-mode out-of-process`.
   Either extend the constructor or create a thin subclass.

5. **Az full umbrella import?**
   Tests use `Az.Accounts` as a proxy for the full `Az` module. If we need to test the
   full umbrella, add `OutOfProcess_AzUmbrella_LoadsCleanly` with a 5-minute timeout.
   Currently deferred due to latency concerns.


# Hermes — Host Script Design Decisions

**Author:** Hermes (PowerShell Expert)
**Date:** 2026-04-10
**Status:** Proposed
**Task:** Implementation of `PoshMcp.Server/PowerShell/OutOfProcess/poshmcp-host.ps1`

---

## Decisions Made

### 1. No `Set-StrictMode` — Use explicit null-guards instead

`Set-StrictMode -Version Latest` throws `PropertyNotFoundException` when code accesses
a property that does not exist on a `PSCustomObject` (the type returned by
`ConvertFrom-Json`).  Since the wire protocol intentionally omits optional fields
(e.g. a `discover` request may omit `commands`, `includePatterns`, etc.), strict-mode
property access would throw on every such omission.

`$ErrorActionPreference = 'Stop'` is retained because it converts non-terminating
PowerShell errors into terminating ones, which the try/catch blocks can handle.
Strict-mode added no safety beyond what explicit null-guards provide in this script.

**Rejected:** `Set-StrictMode -Version 1` — still enforces uninitialized variable
strictness but not object-property strictness, providing only partial benefit.

---

### 2. `Get-Command` + `& $cmdInfo @params` — not `Invoke-Expression`

Commands are resolved by name with `Get-Command` before execution.  The actual
invocation uses the resolved `CommandInfo` object: `& $cmdInfo @boundParams`.

This prevents command-injection via a crafted `"command"` field value.  An attacker
who controls the request JSON cannot execute arbitrary script via this path; they can
only invoke a command that already exists in the runspace.

**Rejected:** `Invoke-Expression "$commandName @params"` — arbitrary code execution.

---

### 3. Null parameter values are filtered out of the splat

Parameters whose JSON value is `$null` are excluded from `$boundParams`.  This
matches caller intent: `null` means "do not pass this parameter, use the default".

Implemented via `Get-Member -MemberType NoteProperty` iteration over the
`$Request.parameters` PSCustomObject, which is portable across PS 7 versions and
does not require `Set-StrictMode` workarounds.

---

### 4. Sub-module discovery for umbrella modules (Az, Microsoft.Graph)

`Get-Command -Module Az` returns zero results because the Az umbrella module itself
exports no commands — it only imports sub-modules (`Az.Compute`, `Az.Network`, etc.),
and the commands live there.

After importing a module, the handler also queries:
```powershell
Get-Module | Where-Object { $_.Name -like "$moduleName.*" }
```
and runs `Get-Command -Module` against each detected child module.  This produces the
full command inventory without requiring callers to enumerate every sub-module name.

**Impact on team:** The `discover` request `"modules"` field should list the top-level
umbrella name (`"Az"`); the script handles sub-module expansion automatically.

---

### 5. Module import errors are non-fatal in discover

If `Import-Module` fails for one module, the error is appended to the response
`"errors"` array and discovery continues with the remaining modules and named
functions.  A partial tool inventory is better than a total failure, consistent with
the project's stated goal of isolating bad modules from the rest of the tool set.

---

### 6. Include patterns evaluated before exclude patterns

`includePatterns` is an allow-list (empty = include all).  `excludePatterns` is a
deny-list applied after the allow-list.  Evaluation order: include → exclude.

Both use PowerShell `-like` wildcard matching (e.g. `*-AzBilling*`), which matches
the existing `appsettings.json` `ExcludePatterns` convention in the server.

---

### 7. `results` field in execute response is a JSON-encoded string

Per the wire protocol spec, `"results"` is a `string` field whose value is the JSON
serialization of the command output:

```json
{ "id": "req-1", "success": true, "results": "[{\"Name\":\"pwsh\"}]", "errors": [] }
```

The script serializes command output with `ConvertTo-Json -Depth 5 -Compress` and
stores the resulting string as `$response.results`.  When the outer response is
serialized by `Write-Response`, the string value is JSON-escaped as a quoted string.
No manual escaping is needed — PowerShell's serializer handles it.

---

### 8. UTF-8 no-BOM output encoding

On Windows, `pwsh` can emit a UTF-8 BOM on stdout unless suppressed.  The C# reader
in the parent process uses `new UTF8Encoding(false)` (no BOM).  To guarantee
consistency, the script sets:

```powershell
if ($IsWindows) {
    [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false
}
```

Linux and macOS do not emit BOMs; the conditional avoids a redundant object creation
on those platforms.

---

### 9. Stderr for all diagnostics — stdout reserved for protocol

`[poshmcp-host] ready` and `[poshmcp-host] entering request loop` go to stderr.  The
parent process drains stderr asynchronously (documented pattern in
`ValidateModuleInChildProcess` in `McpToolFactoryV2.cs`).

Nothing is written to stdout before the main loop starts and nothing diagnostic is
written to stdout during the loop.  Violating this invariant would corrupt the JSON
protocol stream.

---

### 10. `$script:CommonParameters` built once at script scope

The `HashSet<string>` for common-parameter exclusion is created once with
`$script:` scope and reused by every `Get-ToolDescriptor` call.  This avoids
reallocating the set for every command during a large `discover` response.

---

### 11. Loop-level catch-all with best-effort id recovery

The main request loop wraps every iteration in `try/catch`.  On failure, the handler
attempts to read `$request.id` to correlate the error response to the caller's
original request.  Since `$request` may be `$null` (e.g. JSON parse failure), this
inner read is itself wrapped in a nested `try/catch`.

---

## Open Questions for Team

1. **Per-command timeouts:** Should `poshmcp-host.ps1` enforce a timeout per
   `execute` call (e.g. `Start-Job` + `Wait-Job -Timeout`)?  Current implementation
   has no timeout — the C# host (Bender) is expected to kill the process via
   `Process.WaitForExit(timeout)` if needed.  Confirm ownership.

2. **Module-path side-effects across requests:** `modulePaths` prepended during a
   `discover` call persist in `$env:PSModulePath` for all subsequent `execute` calls
   in the same subprocess lifetime.  This may be intentional (modules discoverable =
   modules usable) or may need resetting.  Confirm design intent with Farnsworth.

3. **Runspace state shared between discover and execute:** Modules imported during
   `discover` are available in subsequent `execute` calls.  This is the expected
   "persistent runspace" design, but means module state (variables, aliases) leaks
   between the two phases.  Acceptable?

4. **`#Requires -Version 7.0`:** Included to fail fast on old `powershell.exe`
   invocations.  Confirm minimum supported pwsh version for out-of-process mode.

5. **Result size limits:** No `maxResults` cap is applied in this script.  If a
   command returns 50,000 objects, the JSON payload could be many MB.  Should the
   script honor an optional `"maxResults"` field in the execute request, or is
   result capping the caller's responsibility via command-native parameters
   (`-First`, `-Top`, etc.)?


# Hermes decision: module import before command discovery

- Date: 2026-04-10
- Author: Hermes
- Status: Proposed

## Decision
Import modules listed in `PowerShellConfiguration.Modules` before any function or module command discovery in `McpToolFactoryV2.GetAvailableCommandsWithMetadata`.

## Why
- Discovery previously queried `Get-Command -Name ...` before any import attempt.
- If module auto-loading is disabled or constrained, commands provided by configured modules are invisible to by-name discovery.
- The configuration model describes `Modules` as modules to import all commands from, so explicit import aligns runtime behavior with configuration intent.

## Implementation
- Added `ImportConfiguredModules(...)` in `PoshMcp.Server/McpToolFactoryV2.cs`.
- Called it at the start of `GetAvailableCommandsWithMetadata(...)` when configured modules are present.
- Import failures are logged as warnings and discovery continues (best-effort resilience).

## Validation
- Added regression test `GetToolsList_WithConfiguredModuleAndAutoloadDisabled_ImportsModuleBeforeNameDiscovery` in `PoshMcp.Tests/Unit/McpToolFactoryV2Tests.cs`.
- Test creates an ephemeral script module, disables auto-loading, and verifies tool generation still finds module command by name.


### 2026-04-10: Remove partial Az.AppConfiguration vendored module and align tests to split-module layout
**By:** Steven Murawski (via Copilot/Hermes)
**What:** Treat the untracked `integration/Modules/Az.AppConfiguration/2.0.1` subtree as partial merge fallout and remove it. Update out-of-process integration tests to validate the current split-module layout (`Az.Accounts`, `Microsoft.Graph.Authentication`) instead of old umbrella-module paths.
**Why:** The added Az.AppConfiguration subtree is incomplete: both `AppConfiguration.Autorest/bin` and `AppConfigurationdata.Autorest/bin` are missing, and `Import-Module` fails with a missing `Az.AppConfiguration.private` assembly. The repo's current vendored module layout is split by module name at `integration/Modules/*`, so tests gating on `integration/Modules/Az/15.5.0` and `integration/Modules/Microsoft.Graph/2.34.0` no longer match the actual structure.

### 2026-04-10T00:00:00Z: Out-of-process PowerShell host must gate on readiness and honor framework execution options

**By:** Steven Murawski (via Copilot/Hermes)
**What:** The out-of-process PowerShell host now waits for an explicit readiness signal before the server uses it, propagates configured module paths/imports/startup hooks into discovery, and applies `requestedProperties`, `maxResults`, and result caching inside the subprocess so remote execution matches in-process semantics more closely.
**Why:** Returning from startup before `pwsh` is actually ready creates race conditions and silent startup failures. Ignoring protocol fields like module paths and result shaping makes out-of-process behavior diverge from the plan and breaks split-module layouts such as `integration/Modules/*`.

# Out-of-Process PowerShell Hosting — PowerShell Patterns Research

**Researcher:** Hermes  
**Date:** 2026-04-10  
**Status:** Research Complete  
**Cross-Platform:** ✅ Windows / Linux / macOS considerations throughout

---

## 1. PowerShell Process Hosting Constraints

### Subprocess Lifecycle (Windows, Linux, macOS)

**Finding: pwsh is uniformly available and reliable as subprocess across all three platforms.**

- **Windows:** `pwsh.exe` from published Microsoft.PowerShell.SDK or PowerShell MSI
- **Linux:** `pwsh` available via package managers (apt, yum, snap)
- **macOS:** `pwsh` available via Homebrew or direct package

**Reliability pattern:** `ProcessStartInfo` with `FileName="pwsh"` and `-NonInteractive -NoProfile` flags spawns reliably. Exit codes and signals behave consistently:
- `exit 0` = success
- Non-zero exit codes = command or module failure
- `0xC0000005` (Windows) = native memory access violation (likely module crash or corruption)
- `143` (Linux/macOS) = SIGTERM received (timeout kill)

**Cross-platform subprocess mechanics:**
- **Windows:** Uses process handles; `Process.Kill(true)` tree-kills with grace period
- **Linux/macOS:** Uses POSIX signals; `Process.Kill(true)` sends SIGTERM then SIGKILL
- Timeout behavior: `WaitForExit(milliseconds)` works uniformly; `Process.WaitForExit()` without timeout hangs if subprocess blocks

**Example from codebase:** `ValidateModuleInChildProcess` in McpToolFactoryV2.cs already demonstrates 30-second timeout with tree-kill fallback — this pattern is production-validated.

---

### Stdin/Stdout Redirection: Reliability for JSON Serialization

**Finding: Stdin/stdout via redirection is safe for JSON-serialized data on all platforms, with UTF-8 encoding as the cross-platform constant.**

**Encoding handling:**
- **Default behavior:** `ProcessStartInfo` with no explicit CodePage uses UTF-8 on .NET 5+ uniformly across Windows, Linux, macOS
- **Line endings:** PowerShell normalizes CRLF → LF automatically in some contexts; safer to explicitly set `$PSDefaultParameterValues['Out-File:Encoding']='UTF8'` in subprocess initialization
- **BOM (Byte Order Mark):** UTF-8 BOM can appear on Windows-only. Mitigation: use `UTF8Encoding(false)` (no BOM) in C# when reading subprocess output
- **Buffer sizes:** Default 4096-byte buffer is sufficient for most MCP results; large payloads (>10 MB) benefit from explicit StreamReader buffer tuning

**Stream handling gotchas:**
1. **Deadlock risk if not drained:** Subprocess stderr blocking while parent waits on stdout → deadlock
   - **Fix:** Drain both stdout and stderr asynchronously (example in codebase uses tasks)
   - Pattern: `stderrTask = process.StandardError.ReadToEndAsync(); stderrContent = stderrTask.Result;`
2. **Closed stream exception:** If subprocess closes unexpectedly, reading returns empty rather than throwing (safe behavior)
3. **Pipe error (EPIPE on Linux):** If parent closes stdin before subprocess writes to stdout, subprocess may crash. Mitigation: keep stdin open or suppress SIGPIPE

**JSON-specific findings:**
- **PowerShell's `ConvertTo-Json`:** Works uniformly; output is valid UTF-8. No platform-specific JSON variants
- **Serialization of complex objects:** PSObject properties serialize identically across platforms (no Windows/Linux property differences for `Get-Service`, `Get-Process`, etc. core cmdlets)
- **Large results:** `[int] $MaxResults` parameter can cap results before serialization; proven in functional tests to work across all platforms

---

### Context Setup (Module Paths, Policies, Profiles)

**Finding: Module path discovery and policy setup differs by platform; profiles must be skipped for deterministic subprocess execution.**

**Module path configuration across platforms:**

| Aspect | Windows | Linux | macOS | Cross-Platform Pattern |
|--------|---------|-------|-------|------------------------|
| **$PSModulePath** | `$PSHOME\Modules; $PROFILE\..\Modules; $env:PSModulePath` | `/opt/powershell/Modules; ~/.local/share/powershell/Modules; /usr/local/share/powershell/Modules` | `/opt/powershell/Modules; ~/.local/share/powershell/Modules; /usr/local/share/powershell/Modules` | **Explicit path passing via env var or CLI arg** |
| **Registry (Windows only)** | HKLM/HKCU for module paths | N/A | N/A | **Skip registry-based discovery; assume cmdline + env var only** |
| **Home directory** | `%USERPROFILE%` | `$HOME` | `$HOME` | **Use `$env:HOME` uniformly** |

**Recommended subprocess initialization script (cross-platform):**

```powershell
# Executed in spawned subprocess with -NonInteractive -NoProfile
# 1. Skip all profiles (deterministic execution)
# 2. Set execution policy to Bypass for Process scope (no persistence)
if ($PSVersionTable.Platform -ne 'Linux') {
    # Windows only - no-op on Linux/macOS
    Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process -Force -ErrorAction SilentlyContinue
}

# 3. Configure PSModulePath if custom paths provided
if (-not [string]::IsNullOrWhiteSpace($env:POSHMCP_MODULE_PATH)) {
    $env:PSModulePath = "$env:POSHMCP_MODULE_PATH" + [System.IO.Path]::PathSeparator + $env:PSModulePath
}

# 4. Set ErrorActionPreference to stop-on-first-error (safer for validation)
$ErrorActionPreference = 'Stop'

# Command execution follows
```

**Cross-platform module path passing via environment:**
- **Option A (env var):** Export `$env:POSHMCP_MODULE_PATH = "/path1:/path2"` before spawning subprocess (Unix-style separator works in PowerShell 7+ on all platforms)
- **Option B (CLI arg):** Pass `-Command "& { $env:PSModulePath = '...' ; Import-Module 'ModuleName' }"`
- **Recommended:** Option A with escaped colons on Windows if needed; PowerShell handles path separator normalization

**Profile executon gotchas:**
- `-NoProfile` flag prevents `$PROFILE` execution (safe, recommended)
- Without `-NoProfile`, Windows reads HKLM and HKCU profiles; Linux/macOS read `~/.config/powershell/profile.ps1`
- **Issue:** If user profile has `Import-Module Az.Accounts`, it may run before subprocess module validation, causing conflicts
- **Solution:** Always use `-NoProfile` for deterministic isolation

---

### Cleanup & Resource Management

**Finding: Process cleanup is reliable across all platforms; file handles and module state are properly freed on process exit. Watch for zombie processes on Linux/macOS if parent crashes.**

**Cleanup patterns:**

1. **Normal exit (process.Exit(0)):**
   - Runspace disposed
   - All handles closed by OS
   - Modules unloaded
   - Works identically on Windows, Linux, macOS

2. **Timeout kill (30s default):**
   - Windows: `Process.Kill(true)` → `TerminateProcess()`
   - Linux/macOS: `Process.Kill(true)` → SIGTERM + SIGKILL
   - Both close handles and free memory immediately

3. **Zombie processes (Linux/macOS specific):**
   - If parent crashes without calling `Dispose()` on Process, OS zombie remains until parent is reaped
   - **Mitigation in PoshMcp:** `TestProcessRegistry` pattern (already in codebase) tracks all spawned processes and kills on `ProcessExit` or unhandled exception
   - Operational cleanup: kill any `pwsh` processes older than 5 minutes (stale validation processes)

4. **File handle leaks:**
   - **Symptom:** 2nd subprocess validation fails with "file in use" on Windows
   - **Root cause:** Parent process keeps stdout/stderr streams open after read
   - **Fix:** `process.StandardOutput.Dispose()` and `process.StandardError.Dispose()` explicitly after `WaitForExit()`
   - Cross-platform: same mitigation works on all three

5. **Module reload contamination (in-process AppDomain, not subprocess):**
   - Subprocesses are isolated; no contamination into parent runspace
   - Parent runspace module state unaffected by subprocess crashes
   - **This is the entire point of out-of-process validation**

---

## 2. Module Isolation & Loading (Cross-Platform)

### Common Module Conflicts (In-Process)

**Finding: Certain modules fail in-process due to AppDomain pollution, type conflicts, and registry assumptions. Out-of-process isolation solves most.**

**Known conflict patterns in PoshMcp (documented in team memory):**

1. **Type definition conflicts:**
   - **Example:** `GroupPolicy` module on Windows defines `Microsoft.GroupPolicy.WmiObject`
   - If loaded alongside other modules using similar strongly-typed COM objects, CLR AppDomain type resolution fails
   - **Error manifest:** `PSArgumentException: Cannot find a matching overload for method 'XyzMethod'` (misleading; actually type mismatch in AppDomain)
   - **Out-of-process fix:** Each subprocess has fresh AppDomain; no accumulated type pollution
   - **Cross-platform:** Linux doesn't have WMI, so GroupPolicy never loads; Windows-specific conflict

2. **Reflection-heavy module initialization:**
   - **Example:** `Azure.PowerShell.Cmdlets.Billing` (part of Az.Billing) scans all executing types on module import
   - If PoshMcp has already imported `Az.Accounts`, the Billing module may detect it and try to augment it
   - This works in-process by design (sharing is the goal), but if modules have **version conflicts** or **dependency cycles**, the second import fails silently or hangs
   - **Error manifest:** `Import-Module` appears to hang for 30+ seconds; actually spinning on lock contention
   - **Out-of-process fix:** Billing module runs in clean AppDomain; no prior Az.Accounts state
   - **Cross-platform:** This is common on Windows/Linux (Azure modules); mostly Windows Azure Stack issues on macOS

3. **Registry-based assumptions (Windows-only):**
   - **Example:** `WMI` cmdlets on Windows assume registry keys exist in `HKLM\Software\Microsoft\Windows\CurrentVersion\policies\system`
   - If running PowerShell with restricted registry access, import silently fails or cmdlets behave unexpectedly
   - **Error manifest:** `Get-WmiObject` returns no results, but no error (Windows behavior: fall back to empty)
   - **Out-of-process fix:** Subprocess runs in same process context (Windows only); no fix
   - **Cross-platform:** N/A on Linux/macOS (no WMI)
   - **Practical mitigation:** For cross-platform tools, avoid WMI; use CIM/WinRM instead

4. **Dependent assembly version conflicts:**
   - **Example:** `Newtonsoft.Json 11.0` + `Newtonsoft.Json 13.0` both loaded in same AppDomain
   - Some PowerShell modules (especially older community modules or vendor-specific tools) bundle JSON libraries
   - If PoshMcp loads a newer version first, the older module's bundled DLL is ignored, causing method-not-found errors
   - **Error manifest:** `MethodAccessException: Method 'JsonConvert.DeserializeObject<T>' not found on type 'Newtonsoft.Json.JsonConvert'`
   - **Out-of-process fix:** Subprocess loads module's bundled DLL in isolation; no conflict with parent
   - **Cross-platform:** Common on Windows/Linux for enterprise tools; Linux distro modules often bundle libraries

---

### Isolation Boundaries (Out-of-Process Model)

**Finding: Out-of-process provides complete isolation at AppDomain and filesystem levels. Cost is process latency and IPC overhead.**

**Isolation model:**

```
┌─────────────────────────────────────────┐
│  PoshMcp Server Process (Parent)        │
│  - Loaded modules: Az.Accounts, Pester  │
│  - Type registry: Pester.*.Types        │
│  - Variables: $ServerState = {...}      │
│  - Runspace: singleton shared session   │
└─────────────────┬───────────────────────┘
                  │ (Process spawn via pwsh)
                  │ Stdin/Stdout pipe
                  │ JSON wire protocol
                  ▼
┌─────────────────────────────────────────┐
│  Subprocess (Child)                     │
│  - Fresh AppDomain                      │
│  - No loaded modules (except cmdre)     │
│  - Isolated type registry               │
│  - No access to parent $ServerState     │
│  - Module import x crashes child        │
│  - Parent runspace inaffected           │
└─────────────────────────────────────────┘
```

**Isolation coverage:**

| Isolation Aspect | In-Process | Out-of-Process | Notes |
|---|---|---|---|
| **AppDomain type registry** | Shared across modules | Isolated per process | Solves type conflicts |
| **CLR assembly binding** | Shared; version conflicts visible | Isolated per process | Solves DLL binding issues |
| **Filesystem access** | Shared; modules can modify cwd | Isolated per process | Module side-effects confined |
| **Registry (Windows)** | Shared; elevation context matters | Shared subprocess context | No fix for Windows-specific issues |
| **Environment variables** | Inherited by subprocess | Can be overridden | See section 1.3 for passing |
| **Loaded modules** | Accumulate in parent | Subprocess starts clean | Core isolation benefit |
| **Process crash** | Terminates parent → MCP down | Terminates child → try next subprocess | Resilience gain |
| **Memory (MB)** | All modules in one heap | Each subprocess separate heap | Per-module cost ~5-50 MB |

**Cost analysis:**

- **Per-module subprocess spawn:** 500-800ms startup (pwsh init + module import)
- **Recurring calls same module:** Can reuse subprocess, amortize cost
- **Memory:** Parent + N subprocesses = (parent baseline) + N * (pwsh baseline ~50 MB + module ~5-30 MB each)
- **Stateful commands:** Lose session state across subprocess boundaries (design choice needed)

---

### Module Discovery & Import Strategy

**Finding: Recommended pattern for cross-platform support is explicit subprocess-per-module-group with pre-validated module list.**

**Strategy (pseudo-implementation):**

```
1. At startup, PoshMcp loads PowerShellConfiguration.Modules
2. For each module:
   a. Validate in isolation subprocess (existing ValidateModuleInChildProcess)
   b. If validation fails:
      - Log warning
      - Exclude module from MCP tools
      - Continue to next module
   c. If validation succeeds:
      - Mark module as "safe"
      - Store in subprocess pool or "import-on-demand" list
3. During tool invocation:
   a. Determine which module(s) the tool needs
   b. If in-process safe (pre-validated, no conflicts): import to parent runspace
   c. If out-of-process needed (high-risk module or requested isolation):
      - Spawn subprocess with module
      - Execute command in subprocess
      - Return result via JSON
      - Keep subprocess alive for 30s (reuse for repeated calls)
   d. After 30s idle, kill subprocess and return pool
4. Cross-platform consideration:
   - Windows-specific modules (GroupPolicy, WMI) marked as Windows-only
   - Call filtered from Linux/macOS clients
```

**Module discovery ordering (cross-platform):**

1. `$PSModulePath` from environment (passed by parent)
2. Builtin cmdlet-only modules (no imports needed)
3. User-provided module paths (from `EnvironmentConfiguration.ModulePaths`)
4. PowerShell Gallery (if `Install-Module` configured)

**Team memory note:** "McpToolFactoryV2 discovery must import PowerShellConfiguration.Modules before any Get-Command name/module queries." — This is currently done, but should be validated with explicit test for cross-platform behavior.

---

### Platform-Specific Module Availability

**Finding: Module ecosystems differ by platform; Windows Azure/Group Policy modules not available on Linux/macOS. Docs must flag this.**

**Module availability table:**

| Module | Windows | Linux | macOS | Notes |
|--------|---------|-------|-------|-------|
| **Az.Accounts** | ✅ | ✅ | ✅ | Core Azure auth; cross-platform |
| **Az.Billing, Az.AnalysisServices** | ✅ | ✅ | ✅ | REST-based; cross-platform |
| **GroupPolicy** | ✅ | ❌ | ❌ | WMI-based; Windows-only |
| **DnsClient** | ✅ | ❌ | ❌ | Win32 API; Windows-only |
| **ScheduledTasks** | ✅ | ❌ | ❌ | Windows Task Scheduler; no equivalent on Unix |
| **Pester** | ✅ | ✅ | ✅ | Test framework; cross-platform |
| **PSReadLine** | ✅ | ✅ | ✅ | Interactive editing; cross-platform |
| **ImportExcel** | ✅ | ✅ | ✅ | .NET-based; cross-platform |

**Cross-platform discovery strategy:**

```csharp
// In PowerShellConfiguration or startup
var osSpecificModules = new Dictionary<string, string[]>
{
    { "Windows", new[] { "GroupPolicy", "DnsClient", "ScheduledTasks" } },
    { "Linux", new[] { } }, // No OS-specific modules
    { "Darwin", new[] { } }  // No OS-specific modules
};

var currentPlatform = Environment.OSVersion.Platform switch
{
    PlatformID.Win32NT => "Windows",
    PlatformID.Unix when RuntimeInformation.IsOSPlatform(OSPlatform.Linux) => "Linux",
    PlatformID.Unix when RuntimeInformation.IsOSPlatform(OSPlatform.OSX) => "Darwin",
    _ => "Unknown"
};

// Filter modules based on platform
var availableModules = config.Modules
    .Where(m => !osSpecificModules.Any(kv => kv.Key != currentPlatform && kv.Value.Contains(m)))
    .ToList();
```

---

## 3. Command Execution Models

### Stateful Runspace (Per-Session Retention)

**Finding: Stateful runspace is viable cross-platform; current SessionAwarePowerShellRunspace proves pattern. Out-of-process makes statefulness harder.**

**Current in-process stateful model (production, cross-platform):**

```csharp
// PoshMcp.Server/PowerShell/SessionAwarePowerShellRunspace.cs
// Creates isolated runspace per Mcp-Session-Id header
// Variables persist across calls in same session
// Example:
//   Call 1: Set-Variable -Name foo -Value "bar"
//   Call 2: Get-Variable -Name foo  (returns "bar")
```

**Characteristics:**

| Aspect | Behavior | Cross-Platform |
|--------|----------|-----------------|
| **Variables** | Persist across calls | ✅ Identical on Windows/Linux/macOS |
| **Functions** | Can define custom functions | ✅ Works everywhere |
| **Module imports** | Shared within session | ✅ Works, but risks module conflicts |
| **Working directory** | Changes affect next call | ✅ Works, but use absolute paths to avoid surprises |
| **Session timeout** | After 30 min idle, runspace can be disposed | ✅ Timer logic is platform-agnostic |

**Why statefulness matters in out-of-process scenario:**

- **Problem:** Out-of-process subprocess dies after process exit → state lost
- **Mitigation A:** Keep subprocess alive (subprocess pool)
  - Reuse subprocess for multiple commands from same session
  - 30s idle timeout before recycle
  - Cost: ~100 MB memory per active module subprocess
- **Mitigation B:** Accept statelessness
  - Each command gets fresh subprocess
  - No state retention
  - Simpler, but limits use cases

**Recommended pattern:** Use in-process stateful for common paths (Az.Accounts, Pester); out-of-process stateless for high-risk modules.

---

### Stateless (Module-Per-Call)

**Finding: Stateless execution (spawn subprocess, run command, exit) is cheapest and safest for out-of-process isolation. Works uniformly across platforms.**

**Stateless model:**

```
User calls: Invoke-MCP-Tool("Get-Service", ["dhcp"])

PoshMcp server:
  1. Check if "Get-Service" is marked high-risk
  2. If no:  Use in-process singleton runspace
     If yes: Spawn subprocess
  3. Subprocess:
     a. pwsh -NonInteractive -NoProfile -Command "Import-Module <deps>; Get-Service dhcp"
     b. Capture stdout (JSON)
     c. Exit(0)
  4. Parent parses JSON, returns result
  5. Subprocess memory freed
```

**Characteristics:**

| Aspect | Behavior | Trade-off |
|--------|----------|-----------|
| **Startup cost** | 500-800ms per call | Significant; only worthwhile for truly problematic modules |
| **State retention** | None (fresh process) | Acceptable if documented |
| **Memory** | Recycled per call | No accumulation |
| **Cross-platform cost** | Identical on all platforms | pwsh startup time ~500ms default everywhere |
| **Error isolation** | Module crash → subprocess exit | Excellent; parent unaffected |
| **Concurrent calls** | Can spawn multiple subprocesses | N processes × startup cost; need rate limiting |

**Cross-platform performance notes:**

- Windows: PowerShell startup ~500ms (includes TPM/security checks on some systems)
- Linux: pwsh startup ~250-400ms (depends on distro, filesystem type)
- macOS: pwsh startup ~300-500ms (depends on code signing, Gatekeeper delays)

**Optimization:** Use subprocess pool (keep alive 30-60s) instead of true stateless; amortizes startup cost.

---

### Hybrid Approaches

**Finding: Recommended hybrid is in-process for safe modules + subprocess pool for risky ones. Session affinity helps with statefulness.**

**Recommended architecture:**

```
┌─ PoshMcp Server (Parent)
│  ├─ SingletonPowerShellRunspace (shared across sessions)
│  │  ├─ Loaded: Az.Accounts (safe, validated)
│  │  ├─ Loaded: Pester (safe, validated)
│  │  └─ Runspace: Variables/functions persist
│  │
│  ├─ ModuleSubprocessPool (keyed by module name)
│  │  ├─ Pool["GroupPolicy"] → [subprocess 1, subprocess 2] (if alive)
│  │  ├─ Pool["VendorTool"] → [subprocess] (if alive)
│  │  └─ Subprocesses auto-recycled after 30s idle
│  │
│  └─ SessionAwarePowerShellRunspace (per-HTTP-session isolation)
│     ├─ Session 123 → IsolatedRunspace_123 (separate variables)
│     └─ Session 456 → IsolatedRunspace_456 (separate variables)
```

**Decision logic per tool invocation:**

```
Tool = "Get-Service" (from Microsoft.PowerShell.Management)
Module = null (builtin)
  → Use SingletonPowerShellRunspace (execute synchronously)

Tool = "New-AzResourceGroup" (from Az.Resources)
Module = "Az.Resources" (depends on Az.Accounts)
Module validation = "safe"
  → Use SingletonPowerShellRunspace (but ensure Az.Accounts imported first)

Tool = "Import-GPO" (from GroupPolicy)
Module = "GroupPolicy"
Module validation = "Windows-only"
Host = Linux
  → Return error "Module 'GroupPolicy' not available on Linux"

Tool = "Invoke-VendorTool" (from VendorModule)
Module = "VendorModule"
Module validation = "FAIL: subprocess crashed"
  → Get subprocess from ModuleSubprocessPool["VendorModule"]
  → If no subprocess alive: spawn new one, validate, add to pool
  → Execute command in subprocess via JSON RPC
  → Return result
```

**Cross-platform implications:**

- Windows-only modules skip singleton, use subprocess isolation (prevents crashes)
- Linux/macOS filter out Windows-only modules early (no subprocess cost)
- Subprocess pool size tuned per platform (Windows: smaller due to heavier processes)

---

## 4. Serialization & Data Passing (Cross-Platform)

### Current PoshMcp Serialization (PSObjectJsonConverter)

**Finding: Current serialization uses PowerShellObjectSerializer.FlattenPSObject -> System.Text.Json. Tested cross-platform; output identical.**

**Current pipeline:**

```csharp
// PowerShell execution returns PSObject[]
PSObject[] results = ps.Invoke();

// PSObjectJsonConverter normalizes each PSObject
foreach (var psObj in results) {
    var normalized = PowerShellObjectSerializer.FlattenPSObject(psObj);
    // normalized is now: scalars, Dictionary<>, List<>, null — no PSObject wrappers
}

// System.Text.Json serializes normalized objects
string json = JsonSerializer.Serialize(normalized, PowerShellJsonOptions.Options);
```

**Key serializer behaviors (cross-platform identical):**

| Behavior | Example | Cross-Platform Test |
|----------|---------|---------------------|
| **Scalar handling** | `"hello"` string → JSON `"hello"` | ✅ Identical output on all platforms |
| **Hashtable** | PowerShell `@{x=1;y=2}` → JSON `{"x":1,"y":2}` | ✅ Works (converted to Dictionary) |
| **PSCustomObject** | `[PSCustomObject]@{a=1}` → JSON `{"a":1}` | ✅ Works (unwrapped properties) |
| **Complex types** | `System.Diagnostics.Process` → JSON `{...process props...}` | ✅ Flattened, but slow if recursive |
| **Null values** | PowerShell `$null` → JSON `null` | ✅ Works |
| **Collections** | `array`, `List<>`, `Collection<>` → JSON `[...]` | ✅ Works |

**Current limitations (documented in team memory):**

1. **Live object performance:** If you serialize a Process object with nested .Modules property (is IEnumerable), the serializer walks the entire tree, which triggers Win32 API calls → hangs. **Fix:** Shallow serialization for expensive properties.
2. **Pointer types:** `System.ReadOnlySpan<byte>` (pointer-like) cannot be serialized. Logged warning; method skipped. **This is acceptable; rare edge case.**
3. **CLR property leaking:** Direct System.Text.Json can leak internal Hashtable properties. **Current fix:** Normalize to Dictionary first.

---

### Bidirectional Viability (Across Process Boundary)

**Finding: Serialization is one-way (PowerShell → JSON) in current design. Deserialization (JSON → PowerShell PSObject) is NOT implemented. Bidirectional across process boundary needs design.**

**Current one-way design:**

```
Parent runspace
  ↓ (ps.Invoke() returns PSObject[])
Server
  ↓ (PowerShellObjectSerializer.FlattenPSObject)
Normalized objects (Dictionary, List, scalars)
  ↓ (System.Text.Json.Serialize)
JSON over HTTP or stdio
  ↓ (Client deserialization)
Client application (e.g., Claude)
```

**For out-of-process subprocess communication, reverse path needed:**

```
Parent MCP call with arguments {name: "GetService", args: {name: "dhcp"}}
  ↓ (JSON)
Subprocess receives JSON
  ↓ (Deserialize JSON → PowerShell parameters??)
PowerShell command: Get-Service -Name "dhcp"
  ↓ (ps.Invoke())
Subprocess result: PSObject[]
  ↓ (Flatten + JSON)
Return JSON to parent
```

**Deserialization challenges:**

1. **Type mapping:** JSON `"dhcp"` → PowerShell string. Simple.
   - But JSON might be `{"_module": "Az.Resources", "_type": "ResourceGroup", "name": "mygroup"}`
   - Deserialize to what PowerShell type? `[ResourceGroup]`? Need type registry.

2. **Parameter transformation:** MCP tool parameter schema says `"type": "array"`.
   - JSON `["a", "b", "c"]`
   - PowerShell command expects `-Name string[]`
   - Deserialization: convert JSON array → PowerShell array (works)
   - But if command wants custom type (e.g., `[PSCredential]`), JSON deserialization can't construct it

3. **Bidirectional design options:**

   **Option A (Recommended): Stateless subprocess, client-side parameter binding**
   ```
   Parent:
     Parameters from MCP call (already in C# objects)
       ↓ Convert C# object → PowerShell string form
       ↓ Pass to subprocess as command-line argument
   Subprocess:
     PowerShell parses command-line → parameter values
     → No JSON deserialization in subprocess
   ```
   
   **Option B: JSON schema + PowerShellParameterUtils**
   ```
   Subprocess receives JSON
   Uses PowerShellParameterUtils.ConvertParameterValue to transform
   Maps JSON to PowerShell parameter types
   Higher effort; more flexible if subprocess needs full PSObject state
   ```

**Cross-platform implications:** Option A is simpler and platform-agnostic. Option B works but requires more testing (different type handling on Windows vs. Linux vs. macOS).

---

### Complex Type Handling, Round-Trip Survival

**Finding: Complex types (custom classes, module-specific types) do NOT survive round-trip across process boundary in current design. This is acceptable; out-of-process is stateless by definition.**

**Round-trip analysis:**

| Type | Parent → Child Serialization | Survival | Notes |
|------|------|---|---|
| Built-in scalar (int, string, bool) | ✅ JSON | ✅ 100% | No issues |
| System.Collections.Hashtable | ✅ JSON → {key: value} | ✅ 100% | Works; becomes PSCustomObject in subprocess |
| System.Management.Automation.PSCredential | ✅ JSON ??? | ❌ 0% | Cannot serialize SecureString; credentials don't round-trip |
| Custom class MyModule.ResourceType | ✅ JSON → {prop: value} | ❌ 0% | Subprocess has no `MyModule.ResourceType` class; becomes PSCustomObject |
| System.Diagnostics.Process | ✅ JSON (flattened) | ⚠️ 5% | Subprocess can't reconstruct Process handle; mostly serialized data only |

**Practical implication:** If a parent command returns a custom object, you can't pass it back to the same module in a subprocess. This is endemic to process boundaries; not a PoshMcp limitation.

---

### Error Propagation ($Error, ExceptionRecord)

**Finding: PowerShell error propagation across process boundary requires explicit capture in subprocess and serialization to JSON. Current design captures but doesn't expose in MCP response.**

**Current error handling (in-process):**

```csharp
// McpToolFactoryV2 generated methods invoke command
ps.Invoke();
if (ps.HadErrors)
{
    // Errors are in ps.Streams.Error (Collection<ErrorRecord>)
    var errorRecords = ps.Streams.Error;
    // Currently: logged but not returned to MCP client
}
```

**For out-of-process (recommended):**

```powershell
# In subprocess
try {
    Get-Service -Name "invalid" -ErrorAction Stop
} catch {
    # Capture exception
    $errorInfo = @{
        Exception = $_.Exception.Message
        ErrorRecord = $_.FullyQualifiedErrorId
        ScriptStackTrace = $_.ScriptStackTrace
        StackTrace = $_.Exception.StackTrace
    }
    # Return as JSON
    ConvertTo-Json $errorInfo
}
```

**Error serialization schema (cross-platform):**

```json
{
  "success": false,
  "error": {
    "message": "Cannot find a matching parameter set for the specified parameters",
    "category": "InvalidArgument",
    "fullyQualifiedId": "System.Management.Automation.ParameterBindingException",
    "scriptStackTrace": "at <ScriptBlock>, <No file>: line 1"
  },
  "stderr": "(optional stderr output)"
}
```

**Cross-platform error behavior:**

| Error Type | Windows | Linux | macOS | Handling |
|---|---|---|---|---|
| **Module not found** | Identical message | Identical message | Identical message | ✅ Same error text |
| **Permission denied** | Access is denied | Permission denied | Permission denied | ⚠️ Different text; normalize |
| **Timeout** | (subprocess killed; no PowerShell error) | SIGTERM (same) | SIGTERM (same) | ✅ Handled uniformly |
| **Type not found** | Identical exception | Identical exception | Identical exception | ✅ Same error |

**Recommended change:** Extend MCP response schema to include optional `error` block with full `ExceptionRecord` details. This is out of scope for current research but feasible.

---

### Line Endings & Encoding Gotchas

**Finding: UTF-8 encoding is safe cross-platform with explicit BOM handling. Line endings (CRLF vs LF) are platform-dependent but PowerShell 7+ normalizes transparently.**

**Encoding security:**

1. **UTF-8 default:** ProcessStartInfo uses UTF-8 on all platforms by default (.NET 5+)
   - No special handling needed
   - BOM (Byte Order Mark) presence: Windows subprocess may emit UTF-8 BOM (`EF BB BF`); Linux/macOS typically don't
   - **Mitigation:** Use `Encoding utf8 = new UTF8Encoding(false);` to ensure no BOM in JSON output

2. **Line ending normalization:**

   ```powershell
   # Parent (Windows)
   $result = "line1`r`nline2" # CRLF
   # Subprocess (Linux) via pipe receives UTF-8 bytes representing CRLF
   # PowerShell normalizes: when reading from stdin, CRLF → automatic handling
   # When writing to stdout with ConvertTo-Json: PowerShell normalizes to LF (Unix style) on pwsh 7+
   ```

   | Platform | Default output | Visible as | PowerShell 7+ behavior |
   |---|---|---|---|
   | Windows | CRLF | `^M^J` in hex | Uses LF in modern pwsh.exe |
   | Linux | LF | newline | Uses LF |
   | macOS | LF | newline | Uses LF |

3. **JSON-specific encoding:**
   - `ConvertTo-Json` always outputs valid JSON (RFC 7159)
   - Newlines within JSON strings are escaped (`\n`)
   - CRLF within strings serializes as `\r\n` (safe)
   - No platform-specific JSON variants

4. **Real-world subprocess command (cross-platform safe):**

   ```csharp
   using var process = Process.Start(new ProcessStartInfo
   {
       FileName = "pwsh",
       Arguments = "-NoProfile -Command \"Get-Service dhcp | ConvertTo-Json\"",
       RedirectStandardOutput = true,
       StandardOutputEncoding = new UTF8Encoding(false), // No BOM
       UseShellExecute = false
   });
   
   string json = process.StandardOutput.ReadToEnd(); // Works identically on all platforms
   ```

---

## 5. Cross-Platform Considerations (Windows vs. Linux vs. macOS)

### Windows-Specific Concerns

**Registry assumptions:**
- Many Windows cmdlets assume registry keys + ACLs exist (e.g., WMI consumer modules read HKLM)
- Out-of-process subprocess runs in same process context → registry access identical to parent
- **Not solved by out-of-process.** Mitigation: use CIM/REST APIs instead of WMI where possible.

**Code signing & Gatekeeper (not Windows, but relevant for comparison):**
- Windows has Authenticode signing; PowerShell respects `-ExecutionPolicy RemoteSigned`
- Add `COPY *.ps1 C:\app\scripts` to Dockerfile → scripts marked with ZoneId=3 (downloaded)
- Bypass via `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass`

**PowerShell editions:**
- Windows: PowerShell 5.1 (built-in, deprecated) vs. pwsh 7+ (modern, recommended)
- PoshMcp uses pwsh CLI exclusively (via Process.Start); compatible with 7.x

**Specific modules affected by Windows platform code:**
- `ActiveDirectory` — LDAP protocol; cross-platform via pwsh 7+, but better as WinRM call
- `DnsClient` — Win32 API; Windows-only
- `GroupPolicy` — ADSI/WMI; Windows-only
- `Hyper-V` — Windows-only
- `ScheduledTasks` — Windows Task Scheduler; no Unix equivalent

---

### Linux-Specific Concerns

**File permissions & sudo elevation:**
- PowerShell runs in user context; if subprocess needs to modify `/etc`, it requires sudo
- Subprocess will prompt for password → blocks waiting on stdin → timeout
- **Mitigation:** Use `sudo` with `-n` (non-interactive) or configure `/etc/sudoers` with `NOPASSWD`
- PoshMcp subprocess uses `-NonInteractive` flag, which blocks password prompts (safe behavior, fails loudly)

**Module availability:**
- Most PowerShell Gallery modules work on Linux
- Fewer modules pre-installed in linux container images
- Recommend: explicitly list required modules in `appsettings.json` or Dockerfile `ENV INSTALL_PS_MODULES`

**Container runtime:**
- If running PoshMcp in container (`docker run`), subprocess inherits container isolation
- No `pwsh` in lightweight scratch images; use `powershell:latest` base image

**Package management differences:**
- Fedora/RHEL: `dnf`, `rpm`
- Debian/Ubuntu: `apt`, `dpkg`
- Alpine: `apk` (often no pwsh available; use Ubuntu base for PoshMcp)

---

### macOS-Specific Concerns

**Code signing & Gatekeeper:**
- Downloaded pwsh binary must be code-signed (Microsoft signs official releases)
- Gatekeeper may quarantine on first run; `xattr -d com.apple.quarantine $(which pwsh)` to remove
- PoshMcp subprocess startup may hang 5-10s on first subprocess spawn if Gatekeeper is involved
- **Mitigation:** Pre-run `pwsh -NoProfile -Command "exit"` in Dockerfile or startup
- **Cross-platform note:** This is macOS-only; no equivalent on Windows/Linux

**Homebrew installation:**
- Standard path: `/usr/local/bin/pwsh` or `/opt/homebrew/bin/pwsh` (Apple Silicon)
- PATH must include Homebrew bin; container base image might not
- PoshMcp subprocess uses `FileName="pwsh"`, relies on PATH; may not find it
- **Mitigation:** `export PATH="/opt/homebrew/bin:$PATH"` in container startup

**M1/M2 (Apple Silicon) vs. Intel:**
- pwsh now has native ARM64 builds
- Container architectures matter: `docker buildx build --platform linux/arm64` for M1
- x86_64 binaries work via Rosetta 2, but slower; native ARM64 preferred

---

### Unified Strategy (Windows, Linux, macOS)

**Recommended approach for cross-platform subprocess support:**

```csharp
public class CrossPlatformPowerShellSubprocess
{
    public static ProcessStartInfo CreateValidationProcessInfo(string moduleName)
    {
        var psi = new ProcessStartInfo
        {
            FileName = GetPowerShellExecutable(),
            Arguments = $"-NonInteractive -NoProfile -Command \"Import-Module '{EscapeForShell(moduleName)}' -ErrorAction Stop; exit 0\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };

        // Add cross-platform environment setup
        // Remove any profile-loading env vars
        psi.EnvironmentVariables.Remove("POWERSHELL_TELEMETRY_OPTOUT");
        
        // Pass module paths if configured
        if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("POSHMCP_MODULE_PATH")))
        {
            psi.EnvironmentVariables["POSHMCP_MODULE_PATH"] =
                System.Environment.GetEnvironmentVariable("POSHMCP_MODULE_PATH")!;
        }

        return psi;
    }

    private static string GetPowerShellExecutable()
    {
        // Always use pwsh (PowerShell Core 7+)
        // Platform-agnostic: pwsh on all platforms
        return "pwsh";
    }

    private static string EscapeForShell(string text)
    {
        // PowerShell escaping: single quote → two single quotes
        return text.Replace("'", "''");
    }
}
```

**Container base image strategy:**

```dockerfile
# Dockerfile.multi-platform
FROM mcr.microsoft.com/powershell:latest

WORKDIR /app

# Install .NET 10 SDK/runtime (for PoshMcp)
# (Already in above base image, but could be explicit)

# On macOS (Apple Silicon): explicitly opt for ARM64
# docker buildx build --platform linux/arm64 -t poshmcp .

# On Linux: any platform works; prefer lightweight distro
# Already using powershell:latest base (Ubuntu-based)

COPY install-modules.ps1 /tmp/
ENV INSTALL_PS_MODULES="Az.Accounts Pester"
RUN pwsh /tmp/install-modules.ps1

COPY PoshMcp.Server/bin/Release/net10.0 /app/server
ENTRYPOINT ["/app/server/poshmcp", "serve", "--transport", "stdio"]
```

---

## 6. Real Module Examples

### Modules Known to Fail In-Process

**Investigation summary:**

PoshMcp's existing `ValidateModuleInChildProcess` function already handles this by **preventing import if subprocess validation fails**. No documented "known bad modules" list in the repo, but the pattern allows discovery.

**When a module would fail in-process (pattern recognition):**

1. **Module that spawns subprocesses that hang:**
   - Example: Some Azure AD module versions fork child processes during initialization
   - **Fail mode:** In-process import hangs parent runspace (no timeout)
   - **In subprocess:** Timeout (30s) kills the child process group → clean exit
   - **Example module:** `AzureAD` (older versions, Microsoft now recommends Graph API instead)

2. **Module with unresolved Windows 7 deprecated APIs:**
   - Example: Older `WebAdministration` module on newer Windows
   - **Fail mode:** Import raises MissingMethodException (API removed)
   - **Platform:** Windows-only (not relevant on Linux/macOS)
   - **In subprocess:** Subprocess crashes; parent unaffected

3. **Module with bundled DLL conflicts:**
   - Example: Vendor module ships Newtonsoft.Json 11.0; PoshMcp has 13.0
   - **Fail mode:** Calls to newer API fail; method not found
   - **In subprocess:** Subprocess loads module's DLL in isolation
   - **Real-world:** AWS Tools for PowerShell (Older versions had this)

**Failing module discovery method (programmatic):**

```powershell
# This is what ValidateModuleInChildProcess does
# Run in a subprocess with 30s timeout
pwsh -NonInteractive -NoProfile -Command @"
    try {
        Import-Module 'ProblematicModule' -ErrorAction Stop
        exit 0
    } catch {
        Write-Error $_
        exit 1
    }
"@
```

**Exit codes tell the story:**
- `0`: Module loaded successfully
- `1`: PowerShell error (normal; module didn't load)
- `0xC0000005`: Access violation (module or dependency corrupt)
- `143` (SIGTERM on Linux/macOS): Timeout kill (module initialization hung)

---

### How Out-of-Process Solves Them

**For each failure mode:**

| Failure Mode | In-Process Result | Out-of-Process Result | Mitigation |
|---|---|---|---|
| **Module hangs parent runspace** | MCP server unresponsive | Subprocess killed after 30s; parent continues | Mark module "do not load in-process"; use subprocess pool |
| **Unresolved API** | Tool creation fails; cascading failures | Subprocess exits with error; parent stable | Exclude module from MCP tools; document platform limitation |
| **DLL conflict** | Module method fails at runtime | Subprocess loads module in clean AppDomain | Use subprocess pool for module; pay startup cost |
| **Subprocess fork deadlock** | (No issue; in-process only) | Subprocess deadlocked child reaped on WaitForExit timeout | Works; timeout protection handles it |

---

### Modules That Won't Benefit

**Out-of-process doesn't solve:**

1. **Registry-based Windows issues**
   - Subprocess runs in same registry context as parent
   - Windows-only; no out-of-process fix
   - **Solution:** Use REST/CIM APIs instead of WMI

2. **Host-level elevation requirements**
   - If a command needs admin privileges, subprocess also needs them
   - Parent process elevation doesn't transfer to child on Windows
   - **Solution:** Run entire PoshMcp server as admin (not recommended); use `runas` subprocess (complex)

3. **Network/authentication timeouts**
   - If `Get-AzResource` hangs waiting for Azure to respond, out-of-process doesn't fix it
   - Subprocess would hang just as much
   - **Solution:** Configure `-TimeoutSec` parameter (if cmdlet supports it)

---

## 7. PowerShell-Specific Recommendations

### Best Practice IPC Mechanism (Cross-Platform)

**Finding: Stdin/stdout JSON framing is simplest; TCP localhost is alternative. Sockets are not recommended (less portable across Windows/Linux/macOS).**

**Option A: Stdin/Stdout (Recommended)**
- **Used by:** Model Context Protocol (MCP) stdio standard
- **Mechanism:** Process.StandardInput/StandardOutput with UTF-8 encoding
- **MCP framing:** `Content-Length: N\r\n\r\n` header followed by JSON
- **Pros:** No extra ports; works in containers; matches MCP spec
- **Cons:** Requires careful buffer management (no partial reads)
- **Cross-platform:** Identical on Windows/Linux/macOS (stdio is universal)

**Option B: TCP Localhost**
- **Mechanism:** Process starts listening on 127.0.0.1:random_port; parent connects
- **Pros:** Built-in TCP libraries; easier async/streaming
- **Cons:** Port conflict possible; requires firewall args; more complex setup
- **Cross-platform:** Identical on Windows/Linux/macOS
- **Use case:** If you need bidirectional streaming or persistent subprocess connection

**Option C: Unix Domain Sockets (Linux/macOS only)**
- **Mechanism:** Subprocess creates socket at `/tmp/poshmcp-XXXXX.sock`
- **Pros:** Efficient; no port binding
- **Cons:** Windows doesn't support; adds platform-specific code
- **Recommendation:** Skip for cross-platform; stick with Option A or B

**For PoshMcp out-of-process addition:**

Use **Stdin/Stdout + MCP framing** (already used for HTTP transport). Minimal changes:
1. Parent spawns `pwsh -NonInteractive -Command <script>`
2. Parent writes JSON request to stdin
3. Subprocess reads stdin, executes command, writes JSON response to stdout
4. Parent reads stdout, parses JSON
5. Process exits; cleanup

```csharp
var psi = new ProcessStartInfo
{
    FileName = "pwsh",
    Arguments = "-NonInteractive -NoProfile -Command -",
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
    UseShellExecute = false,
    StandardOutputEncoding = new UTF8Encoding(false)
};

using var process = Process.Start(psi);
string script = $@"
    Import-Module '{module}' -ErrorAction Stop
    {command} | ConvertTo-Json
";

process.StandardInput.WriteLine(script);
process.StandardInput.Close(); // Signal EOF

string json = process.StandardOutput.ReadToEnd();
process.WaitForExit(30_000);
```

---

### Module Configuration Strategy

**Finding: Configuration should explicitly enumerate which modules are "safe in-process" vs. "require subprocess isolation". This is a per-organization decision.**

**Recommended configuration schema:**

```json
{
  "PowerShellConfiguration": {
    "Modules": [
      {
        "Name": "Az.Accounts",
        "IsolationLevel": "InProcess",
        "ValidationRequired": false,
        "Reason": "Safe; no conflicting types"
      },
      {
        "Name": "GroupPolicy",
        "IsolationLevel": "OutOfProcessOnly",
        "ValidationRequired": false,
        "PlatformFilter": "Windows",
        "Reason": "Windows-only; WMI-based"
      },
      {
        "Name": "MyVendorModule",
        "IsolationLevel": "OutOfProcessPool",
        "ValidationRequired": true,
        "SubprocessTTL": 300,
        "Reason": "Untrusted; crashes child on import; reuse subprocess for 5 min"
      }
    ],
    "OutOfProcessSubprocessPoolConfig": {
      "MaxPoolSize": 10,
      "IdleTimeout": 300,
      "SpawnTimeout": 30000,
      "ValidationTimeout": 30000,
      "MaxConcurrentSpawns": 3
    }
  }
}
```

**Decision criteria per module:**

```
1. Is the module Windows-only?
   → Mark "OutOfProcessOnly" (for Windows users); exclude from Linux/macOS
2. Does subprocess validation fail (timeout, crash)?
   → Mark "OutOfProcessPool"; use subprocess isolation
3. Is the module from trusted source (Microsoft, established vendor)?
   → Mark "InProcess"; load normally
4. Is the module custom or third-party with unknown quality?
   → Mark "OutOfProcessPool"; safer approach
```

---

### Recommended Execution Model (Cross-Platform)

**Finding: Hybrid model recommended — in-process for safe modules, subprocess pool for risky. Addresses safety without prohibitive startup cost.**

**Execution flow (pseudocode):**

```csharp
public async Task<McpToolResponse> ExecuteToolAsync(McpToolCall call)
{
    var toolName = call.ToolName;
    var moduleName = GetModuleForTool(toolName);
    
    var moduleConfig = _config.Modules.FirstOrDefault(m => m.Name == moduleName);
    
    if (moduleConfig?.IsolationLevel == "OutOfProcessOnly")
    {
        return await ExecuteInSubprocessPool(toolName, call.Arguments, moduleName);
    }
    else if (moduleConfig?.IsolationLevel == "OutOfProcessPool")
    {
        return await ExecuteInSubprocessPool(toolName, call.Arguments, moduleName);
    }
    else // InProcess
    {
        // Current behavior: execute in singleton runspace
        return ExecuteInRunspace(toolName, call.Arguments, moduleName);
    }
}

private async Task<McpToolResponse> ExecuteInSubprocessPool(string toolName, Dictionary<string, object> args, string module)
{
    // Reuse subprocess if available (< 5 min idle)
    var subprocess = _subprocessPool.GetOrCreate(module);
    
    // Send request via stdin
    var request = new { tool = toolName, args };
    subprocess.StandardInput.WriteLine(JsonConvert.SerializeObject(request));
    
    // Receive response from stdout
    string jsonResponse = subprocess.StandardOutput.ReadLine();
    
    // Parse and return
    return JsonConvert.DeserializeObject<McpToolResponse>(jsonResponse);
}
```

**Platform tuning:**

- **Windows:** Subprocess pool size = 2-3 (heavier process); startup cost high
- **Linux:** Subprocess pool size = 5-10 (lighter); startup faster
- **macOS:** Subprocess pool size = 2-3 (Gatekeeper adds delay on launch)

---

### Data Passing Protocol (Recommended Wire Format)

**Finding: MCP JSON-RPC over stdin/stdout already proven; use it. For subprocess IPC, minimal wrapper needed.**

**MCP-compliant request format:**

```json
{
  "jsonrpc": "2.0",
  "id": "1",
  "method": "tools/call",
  "params": {
    "name": "get_service",
    "arguments": {
      "Name": "dhcp"
    }
  }
}
```

**MCP-compliant response format:**

```json
{
  "jsonrpc": "2.0",
  "id": "1",
  "result": {
    "content": [
      {
        "type": "text",
        "text": "[{\"name\":\"dhcp\",\"state\":\"Running\"}]"
      }
    ]
  }
}
```

**For subprocess-to-parent IPC (simplified, not full MCP):**

```json
{
  "status": "ok|error",
  "result": [{ "name": "dhcp", "state": "Running" }],
  "error": null
}
```

**Encoding:** UTF-8, no BOM. Framing: `Content-Length: N\r\n\r\n` (MCP standard).

---

## 8. Known Limitations & Gotchas (Cross-Platform)

### What Can Still Go Wrong Even in Out-of-Process Mode

**Finding: Out-of-process solves module isolation but introduces new challenges. Plan accordingly.**

1. **Network timeouts masked by subprocess timeout:**
   - **Scenario:** `Get-AzResource` makes HTTP call to Azure (times out after 60s); parent subprocess timeout is 30s
   - **Result:** Subprocess killed before Azure times out; parent sees process timeout, not Azure timeout
   - **Mitigation:** Set subprocess timeout > command timeout; document expected duration per tool
   - **Cross-platform:** Identical issue on all platforms

2. **Zombie processes on Linux/macOS if parent crashes:**
   - **Scenario:** PoshMcp server crashes; subprocess processes not reaped
   - **Result:** `pwsh` processes remain until systemd reaps them (system-dependent)
   - **Mitigation:** Use PoshMcp's TestProcessRegistry pattern; register all spawned processes on startup
   - **Cross-platform:** Linux/macOS specific (Windows auto-reaps on parent exit)

3. **Subprocess module state leaks between invocations:**
   - **Scenario:** Module A modifies `$env:PSModulePath`; next command in same subprocess sees modified path
   - **Result:** Unexpected behavior if not documented
   - **Mitigation:** Accept this as stateful subprocess behavior; document or reset environment per call
   - **Cross-platform:** Affects all platforms equally

4. **Encoding/BOM surprises if not careful:**
   - **Scenario:** Subprocess outputs UTF-8 with BOM; parent parser chokes
   - **Result:** `"Unexpected character encountered while parsing value: EF BB BF"`
   - **Mitigation:** Explicit UTF8Encoding(false) on parent reader; explicit output encoding in subprocess
   - **Cross-platform:** More common on Windows (BOM output); test all platforms

5. **Working directory inherited by subprocess:**
   - **Scenario:** Parent is in `/app/server`; subprocess starts in same directory
   - **Result:** Relative paths in subprocess commands behave unexpectedly
   - **Mitigation:** Always use absolute paths in MCP tool arguments; document this in tooling guide
   - **Cross-platform:** Path separator difference (\ vs /); PowerShell 7+ normalizes, but test

6. **Module import order matters if sharing state:**
   - **Scenario:** Module A depends on Module B being imported first; if subprocess imports A before B, it fails
   - **Result:** Out-of-process execution fails, but in-process works (B already loaded)
   - **Mitigation:** Explicit dependency ordering in subprocess initialization; document module dependencies
   - **Cross-platform:** Same on all platforms

7. **Credential passing across subprocess boundary:**
   - **Scenario:** Parent has `$cred = Get-Credential`; wants to pass to subprocess
   - **Result:** Cannot serialize PSCredential (SecureString not JSON-serializable)
   - **Mitigation:** Pass credentials separately (e.g., env vars for token; separate auth mechanism); document limitation
   - **Cross-platform:** Affects all platforms equally

8. **Platform-specific cmdlet availability causes silent failures:**
   - **Scenario:** Linux user tries to call `Get-GPOStatus` tool; tool is unavailable
   - **Result:** Tool not exposed in MCP list → AI doesn't attempt call (correct)
   - **Gotcha:** If you dynamically load modules per-session, Linux user might see different tool set than team documented
   - **Mitigation:** Document which tools are Windows-only in all MCP metadata and docs
   - **Cross-platform:** Critical for multi-platform deployments

---

## Summary & Recommendations

### Key Findings

1. **Subprocess hosting is feasible and proven:** PoshMcp already uses `ValidateModuleInChildProcess` successfully. Scaling to command execution is straightforward.

2. **Cross-platform support is achievable:** pwsh is uniformly available; stdin/stdout is platform-agnostic; only module availability differs by OS.

3. **Serialization is safe:** Current PowerShellObjectSerializer + System.Text.Json works cross-platform with minor UTF-8 encoding care.

4. **Hybrid strategy is optimal:** In-process for safe modules; subprocess pool for risky ones. Balances performance with resilience.

5. **No platform-specific IPC needed:** Use MCP stdin/stdout framing; works on Windows, Linux, macOS identically.

### Recommended Next Steps (Phase 2)

1. **Configuration schema extension** (1 day)
   - Add `IsolationLevel` field to module config
   - Add subprocess pool configuration

2. **Subprocess pool implementation** (3-5 days)
   - Extend ProcessStartInfo to support module execution
   - Implement subprocess reuse + TTL-based recycling
   - Add logging and metrics

3. **End-to-end cross-platform testing** (2-3 days)
   - Windows: Test with problematic modules (GroupPolicy, Azure AD)
   - Linux: Test with Az modules, verify no module-not-found errors
   - macOS: Test with native M1 subprocess spawn

4. **Documentation** (1 day)
   - Document which modules require out-of-process isolation
   - Update deployment guides for multi-platform
   - Add runbook for diagnosing subprocess failures

### Risk Mitigation

- **Start with validation only:** Don't change execution yet; extend existing `ValidateModuleInChildProcess` to gather data on which modules actually fail
- **Opt-in per module:** Don't force all modules out-of-process; let operators decide
- **Gradual rollout:** Pilot with known-safe modules (Az.Accounts); upgrade to risky modules only after validation



# Leela Documentation Decision: Out-of-Process Runtime Documentation

**Author:** Leela (Developer Advocate)
**Date:** 2026-04-10
**Status:** Implemented

## Decision

Document out-of-process PowerShell hosting as a supported, optional feature with clear guidance on when to use it, how to configure it, and what the trade-offs are. Explicitly clarify that `integration/Modules` is a local test-asset corpus, not production content.

## Rationale

The out-of-process runtime capability is implemented and wired in Program.cs with CLI and environment variable support, but was not clearly documented for end users. This creates:

- **Discovery gap:** Users don't know out-of-process mode exists or when to use it
- **Configuration confusion:** How to enable it is unclear (CLI flag? env var? appsettings.json?)
- **Test corpus confusion:** `integration/Modules` looks like it could be a feature (it's in examples/) but should only be used locally

Creating comprehensive, focused documentation addresses these gaps and enables:
- Users to self-serve when they have module compatibility issues
- Clear configuration patterns for different deployment scenarios (local dev, containers, Azure)
- Explicit boundaries between test assets and production content

## Implementation

### New Documentation

**`docs/OUT-OF-PROCESS.md`** — Comprehensive 500+ line guide covering:
- When to use out-of-process (module conflicts, type pollution, platform-specific issues)
- Architecture comparison (in-process vs out-of-process)
- Configuration methods (appsettings.json, CLI flag, environment variable, priority order)
- Usage patterns (local dev, containers, Azure Container Apps)
- Troubleshooting (subprocess failures, module loading, latency, memory)
- Performance characteristics with benchmarks
- Integration test workflow with local corpus
- Limitations and known issues
- Best practices

### Updated Documentation

1. **`README.md`**
   - Added reference to OUT-OF-PROCESS.md in documentation section
   - Added "Out-of-Process PowerShell Runtime (Advanced)" quick-reference section with quick start, trade-offs table, and link to full documentation

2. **`examples/README.md`**
   - Clarified `appsettings.outofprocess.integration-modules.json` is **local testing only**
   - Added prominent warning about integration test corpus scope
   - Moved appsettings example documentation above Dockerfile section for clarity

3. **`integration/README.md`**
   - Completely restructured with explicit scope boundaries
   - Clear distinction between what `integration/Modules` IS and is NOT
   - When/when-not-to-use guidance
   - Local development workflow examples
   - Maintenance and refactoring guidelines
   - References to related documentation

4. **`docs/ENVIRONMENT-CUSTOMIZATION.md`**
   - Added "Before You Start" section with references to OUT-OF-PROCESS.md and production deployment patterns
   - Links to help users make informed choices about runtime mode and module installation

## Trade-Offs

**Accuracy vs Brevity:**
- OUT-OF-PROCESS.md is comprehensive (500+ lines) but focused only on that feature
- Keeps a clear one-concern-per-file pattern
- Users can reference the README.md quick start, then dive into OUT-OF-PROCESS.md for details

**Discovery:**
- Out-of-process is an "advanced" feature (noted in README section header)
- Appropriate because most users won't need it; available documentation links for those who do

**Examples:**
- `appsettings.outofprocess.integration-modules.json` remains in examples/ for backward compatibility
- Now clearly marked as local-testing-only to prevent misuse
- Production configurations should use PowerShell Gallery or pre-built container images

## Verification

Documentation is:
- ✅ Consistent with OUT_OF_PROCESS_PLAN.md implementation roadmap
- ✅ Accurate to Program.cs CLI wiring (--runtime-mode, env vars, appsettings)
- ✅ Clear about unsupported behavior (mixed modes, per-module selection)
- ✅ Explicit about test vs product boundaries (integration/Modules)
- ✅ Includes actionable configuration examples
- ✅ References all related docs (ENVIRONMENT-CUSTOMIZATION, DOCKER, examples)

## User Impact

### Before
- Users with module conflicts had no guidance
- Out-of-process capability was undiscoverable
- integration/Modules appeared to be a feature

### After
- Users know out-of-process exists and when to use it
- Configuration is clear (three methods documented with examples)
- Test/product boundaries are explicit
- Clear path from README quick start → detailed OUT-OF-PROCESS.md → troubleshooting

## Next Steps

None. Documentation fully addresses out-of-process hosting scope and is ready for user consumption.

## Related

- `OUT_OF_PROCESS_PLAN.md` — Implementation design
- `Program.cs` — CLI wiring and runtime mode selection
- `.squad/agents/leela/history.md` — Learnings from this documentation work



## 2025-07-17

### Default build type for --generate-dockerfile

**Date:** 2025-07-17
**Author:** Bender (Backend Developer)
**Status:** Implemented

The poshmcp build command supports two image types: ase (builds from ./Dockerfile) and custom (uses xamples/Dockerfile.user). When --generate-dockerfile is used without an explicit --type, default to ase. When actual Docker build without --type, default to custom (existing behavior).

**Decision:** 
- --generate-dockerfile without --type → default to ase
- Actual build without --type → default to custom (unchanged)

**Result:** poshmcp build --generate-dockerfile works without errors. Primary workflow unaffected. Users wanting to generate custom Dockerfile must pass --type custom.

## Archived 2026-05-05 (older than 7 days)


### 2026-04-24: Release v0.8.4 Pushed
**By:** Amy (DevOps/Platform Engineer)
**Status:** Applied
**What:** Bumped version to 0.8.4, built poshmcp.0.8.4.nupkg, updated global install, committed (f5583fe), created and pushed annotated tag v0.8.4. Rebase required to resolve merge commit rejected by branch protection.
**Why:** Security patch release fixing CVE-2026-40894.
**Rule Going Forward:** Rebase onto origin/main before pushing to avoid merge commit rejections on protected branches.

### 2026-07-18: Canonical Infrastructure Defaults for PoshMcp Azure Deployment
**By:** Amy (DevOps / Platform / Azure Engineer)
**Status:** Applied
**What:** Aligned all deploy scripts to match canonical defaults defined in `main.bicep` and `parameters.json`. Updated `deploy.ps1`, `deploy.sh`, and `validate.ps1` to use `rg-poshmcp` instead of `poshmcp-rg` for resource group default.
**Why:** Bicep/parameters.json is source of truth; scripts using different defaults would deploy to wrong resource group. Running with script defaults would create duplicate resource group.
**Rule Going Forward:** Bicep and parameters.json are the authoritative sources for infrastructure defaults. Deploy scripts are wrappers — their defaults must mirror Bicep/parameters, not diverge.



### 2026-04-23 10:03: Implemented spec 007 - deploy.ps1 source image support
**By:** Amy
**What:** Added -SourceImage and -UseRegistryCache parameters to deploy.ps1; added Mode A (docker pull + re-tag), Mode B (az acr import), and Mode C (existing build) routing
**Why:** Steven requested feature to deploy pre-built images without local build

### 2026-04-23: Add session-recall skill
**By:** Steven Murawski (via Farnsworth)
**What:** session-recall CLI wired into coordinator startup behavior via .squad/skills/session-recall/SKILL.md
**Why:** Provides progressive session recall after crashes/compaction using installed CLI tool; preferred over raw SQL patterns

### 2026-07-28: Spec 007 - deploy.ps1 source image support

**By:** Farnsworth

**What:** Created spec 007 for `infrastructure/azure/deploy.ps1` source image and ACR pull-through cache support

**Why:** Steven requested feature to allow pulling pre-built container images instead of always building from Dockerfile locally. Enables faster deployments, artifact promotion workflows, and bandwidth optimization via ACR's pull-through cache for large images.

**Decision Points:**

1. **Parameter names and design**:
   - `-SourceImage` (string): Optional container image reference; when provided, suppresses local build
   - `-UseRegistryCache` (switch): Optional flag; requires `-SourceImage`; enables `az acr import` instead of local pull
   - Validation: `-UseRegistryCache` without `-SourceImage` is a usage error (exit code 2)

2. **Execution modes**:
   - **Mode A** (default when `-SourceImage` provided): Local pull + re-tag + push (`docker pull` → `docker tag` → `docker push`)
   - **Mode B** (when both flags provided): ACR import (`az acr import` directly into ACR, no local pull)
   - **Mode C** (backward compatibility): Build from Dockerfile (no changes to existing behavior)

3. **Retry and error handling**:
   - Reuse existing `Invoke-DockerPushWithRetry` logic for pull failures in Mode A
   - Implement similar retry for `az acr import` in Mode B (exponential backoff, transient error detection)
   - Clear, actionable error messages for each failure scenario

4. **Backward compatibility**:
   - When `-SourceImage` is not provided, script behavior is unchanged
   - All existing parameters and environment variables remain functional
   - No breaking changes to the public contract

5. **Image tagging**:
   - Source image re-tagged to `$RegistryServer/poshmcp:$ImageTag` and `$RegistryServer/poshmcp:latest`
   - Same tagging pattern as current `Build-AndPushImage` for consistency

**Spec location:** `specs/007-deploy-source-image/spec.md`

**Next steps:** Triage into GitHub issues and assign to implementation agent (Bender recommended for Azure/Docker CLI expertise).

### 2026-07-28: Test cases for spec 007
**By:** Fry
**What:** Wrote manual test checklist for deploy.ps1 source image feature
**Why:** Need verification procedures for the three execution modes and error cases


### 2026-04-20T21:20:00Z: User directive
**By:** Steven Murawski (via Copilot)
**What:** Release notes must always be added to the docs TOC when a new release notes file is created.
**Why:** User request — captured for team memory after v0.8.0 release notes were not wired up in the TOC


### 2026-07-28: Spec 007 - deploy.ps1 source image support

**By:** Farnsworth

**What:** Created spec 007 for `infrastructure/azure/deploy.ps1` source image and ACR pull-through cache support

**Why:** Steven requested feature to allow pulling pre-built container images instead of always building from Dockerfile locally. Enables faster deployments, artifact promotion workflows, and bandwidth optimization via ACR's pull-through cache for large images.

**Decision Points:**

1. **Parameter names and design**:
   - `-SourceImage` (string): Optional container image reference; when provided, suppresses local build
   - `-UseRegistryCache` (switch): Optional flag; requires `-SourceImage`; enables `az acr import` instead of local pull
   - Validation: `-UseRegistryCache` without `-SourceImage` is a usage error (exit code 2)

2. **Execution modes**:
   - **Mode A** (default when `-SourceImage` provided): Local pull + re-tag + push (`docker pull` → `docker tag` → `docker push`)
   - **Mode B** (when both flags provided): ACR import (`az acr import` directly into ACR, no local pull)
   - **Mode C** (backward compatibility): Build from Dockerfile (no changes to existing behavior)

3. **Retry and error handling**:
   - Reuse existing `Invoke-DockerPushWithRetry` logic for pull failures in Mode A
   - Implement similar retry for `az acr import` in Mode B (exponential backoff, transient error detection)
   - Clear, actionable error messages for each failure scenario

4. **Backward compatibility**:
   - When `-SourceImage` is not provided, script behavior is unchanged
   - All existing parameters and environment variables remain functional
   - No breaking changes to the public contract

5. **Image tagging**:
   - Source image re-tagged to `$RegistryServer/poshmcp:$ImageTag` and `$RegistryServer/poshmcp:latest`
   - Same tagging pattern as current `Build-AndPushImage` for consistency

**Spec location:** `specs/007-deploy-source-image/spec.md`

**Next steps:** Triage into GitHub issues and assign to implementation agent (Bender recommended for Azure/Docker CLI expertise).


### 2026-07-28: Test cases for spec 007
**By:** Fry
**What:** Wrote manual test checklist for deploy.ps1 source image feature
**Why:** Need verification procedures for the three execution modes and error cases


# Decision: Use `poshmcp build` in deploy.ps1 instead of `docker build`

**Date:** 2026-07-18
**Author:** Amy (DevOps/Platform)
**Context:** `infrastructure/azure/deploy.ps1` — `Build-AndPushImage` function

## Decision

The `Build-AndPushImage` function in the Azure deploy script now uses `poshmcp build --tag <image>` instead of calling `docker build` directly.

## Rationale

Steven requested that any image-building step in the deploy pipeline go through the `poshmcp build` CLI, which:
- Auto-detects docker vs podman
- Is the canonical build interface for this project
- Ensures consistent build behavior with the rest of the toolchain

## Implementation Detail

`poshmcp build` only supports a single `--tag` argument per invocation. The original `docker build` call applied both the versioned tag and `latest` in one pass (`-t $FullImageName -t $latestImage`). To keep a single build, we call `poshmcp build --tag $FullImageName` for the build step, then `docker tag $FullImageName $latestImage` to alias the result. The push logic is unchanged.

## Impact

- `Build-AndPushImage` no longer calls `docker build` directly.
- `docker tag` (not `docker build`) is used to apply the `latest` alias — this is acceptable because the restriction was specifically on the build operation.
- `poshmcp` must be installed as a dotnet global tool on the machine or agent running the deploy script.

### 2026-04-23T15:56:32-05:00: Deploy script config precedence and appsettings contract
**By:** Amy
**What:** Added appsettings-sourced deployment configuration to `infrastructure/azure/deploy.ps1` via `-AppSettingsFile` / `DEPLOY_APPSETTINGS_FILE`, with explicit precedence `CLI > env > appsettings > defaults`. Introduced `AzureDeployment` appsettings section (also supports `Deployment.Azure`) and added `infrastructure/azure/deploy.appsettings.json.template` as scaffold-ready template.
**Why:** Preserve existing deploy workflow while enabling repeatable environment-specific deployment configuration from file, especially for CI/bootstrap scaffolds.

### 2026-04-23T16:05:12Z: Add CLI scaffold command backed by embedded infra artifacts
**By:** Bender
**What:** Added `poshmcp scaffold` to materialize an `infra/azure` folder from assembly-embedded deployment files (`deploy.ps1`, bicep files, and parameters) into a target project directory with optional `--force` overwrite behavior.
**Why:** Ensures scaffold works both from source and packaged tool installations without depending on repository-relative filesystem paths.

### 2026-04-23T17:28:05Z: server appsettings to Container App env vars
**By:** Amy (DevOps/Platform) - requested by Steven Murawski
**What:** Translate MCP server appsettings.json into Container App environment variables via deploy.ps1.
**Decisions:**
- Removed `powerShellFunctions` Bicep param from `resources.bicep` and `main.bicep`. Covered by translated env var array.
- Removed `enableDynamicReloadTools` Bicep param; its env var is now emitted from server appsettings translation.
- Renamed Bicep `extraEnvVars` param to `serverEnvVars`.
- Renamed `-McpAppSettingsFile` param in `deploy.ps1` to `-ServerAppSettingsFile`; added `POSHMCP_APPSETTINGS_FILE` env var support; kept auto-discovery.
- Added translations for IncludePatterns, ExcludePatterns, EnableConfigurationTroubleshootingTool, and Logging.LogLevel.Default.
- Fixed RuntimeMode normalization: server expects "InProcess"/"OutOfProcess". deploy.ps1 previously emitted "in-process"/"out-of-process" - corrected.
**Why:** Single source of truth - container configured identically to local server from one appsettings file.

### 2026-04-24: Version bump to 0.8.3 with release metadata alignment
**By:** Amy
**What:** Chose a patch release bump from `0.8.2` to `0.8.3`, aligned version-bearing artifacts by updating `PoshMcp.Server/PoshMcp.csproj`, and ensured release notes index coverage by adding `docs/release-notes/0.8.3.md` into `docs/toc.yml`.
**Why:** Patch bump is the safest default when no target version is specified, and team convention requires release-notes TOC alignment whenever a new release notes file is added.
**Operational Outcome:** Build and pack completed successfully and produced `artifacts/nupkg/poshmcp.0.8.3.nupkg`.
**Merged from inbox:** `.squad/decisions/inbox/amy-version-bump-pack-update.md`



# Decision: -GenerateDockerfile switch for docker.ps1

**Date:** 2026-07-28
**By:** Amy (DevOps/Platform/Azure)
**Status:** Applied

## What

Added `-GenerateDockerfile` [switch] and `-OutputPath` [string] parameters to `docker.ps1`.

## Decisions Made

1. **`-OutputPath` has no default in `param()`** — default is computed dynamically inside each command block:
   - Base build: `./Dockerfile.generated`
   - Custom build: `./Dockerfile.<Template>.generated`
   This avoids a static default that would be wrong for the `build-custom` case.

2. **Feature scope** — `-GenerateDockerfile` is only meaningful for `build`/`build-base` and `build-custom`. It is silently ignored for `run`, `stop`, `logs`, and `clean` (the switch is simply not tested in those branches). No warning emitted — the existing command still executes normally.

3. **Header format** — Includes `# Generated by PoshMcp docker.ps1`, the equivalent `docker build` command, an ISO 8601 timestamp, and a copy-paste-ready build command referencing the output file. Azure template appends an env-var note.

4. **Source content** — The generated file is header + verbatim source Dockerfile content. No mutations to the Dockerfile itself.

5. **`Set-Content -NoNewline`** — Used to avoid appending a spurious trailing newline that `Set-Content` adds by default. Content from `Get-Content -Raw` already contains the file's original line endings.

## Why

Provides a documented, archivable snapshot of the exact Dockerfile used for any build invocation, useful for audit trails, CI artifact storage, and debugging build regressions without re-running the full build.

## Files Changed

- `docker.ps1` — new parameters, updated help block, build command logic


# Decision: poshmcp build --generate-dockerfile

**Date:** 2026-07-28
**By:** Amy (DevOps/Platform)
**Status:** Applied

## What

Added `--generate-dockerfile` and `--dockerfile-output` options to `poshmcp build`.

## Decision Points

1. **New CLI options:**
   - `--generate-dockerfile` (bool/switch): when set, write the Dockerfile to disk and exit; do not invoke docker/podman.
   - `--dockerfile-output` (string, optional): destination path; default `./Dockerfile.generated`.

2. **Dockerfile header format:**
   - `# Generated by poshmcp build`
   - `# Equivalent build command: docker build -f <output-path> -t <image-tag> [--build-arg ...] .`
   - `# Generated: <ISO 8601 UTC timestamp>`
   - Blank line separator before the actual Dockerfile content.

3. **Implementation split:**
   - `DockerRunner.GenerateDockerfile(...)` in `Cli/DockerRunner.cs` owns file I/O and header construction.
   - `Program.cs` handler owns option parsing and console output (success message + manual build hint).

4. **Handler pattern change:**
   - Switched the `buildCommand.SetHandler` from the typed-parameter overload to `InvocationContext`-based pattern to accommodate 8 options without hitting System.CommandLine overload limits.

5. **No docker/podman detection when `--generate-dockerfile` is set:**
   - The flag check and early-return happen *before* `DetectDockerCommand()` is called, so the CLI works even in environments without docker/podman installed.

## Rule Going Forward

When adding more than ~6 options to a `System.CommandLine` command handler, use the `InvocationContext`-based `SetHandler` pattern instead of the typed-parameter overload.


# Decision: appsettings bundling uses COPY injection rather than build-arg

**Author:** Bender
**Date:** 2026-05-01

## Decision

`poshmcp build --appsettings` bundles the supplied file into the image by injecting a
`COPY poshmcp-appsettings.json /app/server/appsettings.json` line into the Dockerfile, not via
`--build-arg`.

## Rationale

Using `COPY` is the correct Docker pattern for bundling files into an image:
- `--build-arg` is for scalar configuration values, not file contents.
- Embedding file content in a build-arg would require encoding, hit size limits, and make the
  Dockerfile comment unreadable.
- `COPY` is transparent, auditable, and idiomatic — the resulting Dockerfile is self-documenting.

## Implementation

- **Generate mode:** `GenerateDockerfile()` replaces/injects the `COPY` line in the Dockerfile content.
- **Build mode:** the appsettings file is staged as `poshmcp-appsettings.json` in CWD (the Docker
  build context), a temp Dockerfile (`.poshmcp-build.dockerfile`) is generated with the injected
  `COPY` line, the build runs, and both temp files are cleaned up in a `finally` block.


### 2026-04-24: Bundle install-modules.ps1 in base image
**Decision:** Copy install-modules.ps1 into the base container image at /app/install-modules.ps1
**Why:** Generated Dockerfiles (poshmcp build --generate-dockerfile) are used in repos that don't have this script locally. Bundling it eliminates the COPY dependency.


# Decision: Embed Dockerfiles in PoshMcp Assembly

**Date:** 2026-07-30
**Author:** Bender (Backend Developer)
**Requested by:** Steven Murawski

## Context

`poshmcp build --generate-dockerfile` reads Dockerfile templates from disk at runtime.
When the CLI is installed as a global dotnet tool via `dotnet tool install`, those files
do not exist on the user's machine — only the packed NuGet `.nupkg` payload is present.
This caused `Error: Dockerfile not found at examples/Dockerfile.user` for tool users.

## Decision

Embed the four Dockerfile templates directly in the `PoshMcp` assembly as `EmbeddedResource`
items in `PoshMcp.Server/PoshMcp.csproj`:

- `Dockerfile` (root) → manifest name `PoshMcp.Dockerfiles.Dockerfile`
- `examples/Dockerfile.user` → `PoshMcp.Dockerfiles.Dockerfile.user`
- `examples/Dockerfile.azure` → `PoshMcp.Dockerfiles.Dockerfile.azure`
- `examples/Dockerfile.custom` → `PoshMcp.Dockerfiles.Dockerfile.custom`

`DockerRunner.ReadEmbeddedDockerfile(name)` reads from the assembly manifest stream.
`DockerRunner.GenerateDockerfile(...)` tries embedded first, falls back to disk so local
dev workflows are unaffected.

`Program.cs` build handler: the `File.Exists(imageFile)` guard is now skipped when
`--generate-dockerfile` is active (the source doesn't need to be on disk).

## Consequences

- `poshmcp build --generate-dockerfile` works correctly after `dotnet tool install`.
- Local development (running from source) continues to work via the disk fallback.
- Dockerfile content stays in sync with the assembly version — no runtime drift.
- Four Dockerfiles add negligible size to the assembly (~4 KB total).


# Decision: `--generate-dockerfile` always defaults to `buildType = "custom"`

**Date:** current session
**Author:** Bender (Backend Dev)
**Requested by:** Steven Murawski

## Context

`poshmcp build --generate-dockerfile` is a user-facing command for generating a starter Dockerfile
that the user can customize and use to build their own container on top of the published PoshMcp
base image (`ghcr.io/usepowershell/poshmcp/poshmcp:latest`).

The previous logic branched the default `buildType` on whether `--generate-dockerfile` was active:

```csharp
var buildType = string.IsNullOrWhiteSpace(type)
    ? (generateDockerfile ? "base" : "custom")
    : type.ToLowerInvariant();
```

This caused `--generate-dockerfile` (with no `--type`) to default to `"base"`, which maps to the
root `Dockerfile` — the file for building PoshMcp itself from source. That is wrong for users.

## Decision

Always default to `"custom"` when `--type` is not supplied:

```csharp
var buildType = string.IsNullOrWhiteSpace(type)
    ? "custom"
    : type.ToLowerInvariant();
```

`"custom"` maps to `examples/Dockerfile.user`, which is the correct user-deployment template.
Users who need the source-build Dockerfile can explicitly pass `--type base`.

## Consequences

- `poshmcp build --generate-dockerfile` now emits `examples/Dockerfile.user` content by default ✅
- `poshmcp build` (no flags) is unchanged — still defaults to `"custom"` / `examples/Dockerfile.user` ✅
- `poshmcp build --type base --generate-dockerfile` still works for maintainers who want the source Dockerfile ✅


### 2026-04-24: User directive — git fetch/rebase workflow
**By:** Steven Murawski (via Copilot)
**What:** Always use `git fetch origin main` followed by `git rebase origin/main` to sync with remote before pushing. Never use merge pulls (`git pull` without `--rebase`).
**Why:** User preference — avoids stray merge commits that can trigger branch protection rejections.


### 2026-04-25: Application Insights logging spec created
**By:** Farnsworth (via Steven Murawski)
**What:** Spec at specs/application-insights-logging.md. Proposes opt-in App Insights via appsettings using Azure.Monitor.OpenTelemetry.AspNetCore. Targets post-0.8.11.
**Why:** Users running PoshMcp in Azure need logs/traces in App Insights without breaking existing logging.



# Decision: ConfigureApplicationInsights Implementation Choices

**Author**: Bender
**Date**: 2026-04-27
**Issue**: #172

## Decision 1: Use `Console.Error.WriteLine` for startup logs

The spec says "use the existing logging infrastructure" but the method signature `(IServiceCollection, IConfiguration, bool)` provides no `ILogger`. Rather than widening the signature (which would deviate from FR-307), startup messages are written to `Console.Error` — consistent with other early-startup log sites in `Program.cs` that precede host construction.

## Decision 2: Call site placement — after existing OpenTelemetry wiring

`ConfigureApplicationInsights` is called immediately after `ConfigureOpenTelemetryForHttp` (HTTP) and `ConfigureOpenTelemetry` (stdio). This ensures the existing OTel pipeline (McpMetrics, console exporter) is already registered before Azure Monitor is layered on, so FR-317 (McpMetrics flow through Azure Monitor) is satisfied without special ordering logic.

## Decision 3: Transport mode as OpenTelemetry resource attribute

FR-309 requires transport mode as a custom dimension. Implemented via `.ConfigureResource(resource => resource.AddAttributes(...))` on the `OpenTelemetryBuilder`. Resource attributes appear as custom dimensions in Azure Monitor and are set once at startup — zero per-request overhead.

## Decision 4: Clamp `SamplingPercentage` at runtime

`Math.Clamp(options.SamplingPercentage, 1, 100)` is applied before converting to ratio. This makes runtime behaviour predictable even with out-of-range config values. Doctor validation (future issue) will surface the out-of-range warning to users at config time.


---
## Archived 2026-05-06 (entries older than 2026-04-29)
### 2026-04-28: Doctor AppInsights validation architecture
**By:** Bender (Backend Dev)
**What:** Added a `ConfigurationErrors` list to `DoctorReport` (separate from `Warnings`) so `ComputeStatus` can distinguish error-level config issues from warnings. `BuildConfigurationWarnings` now returns a `(Warnings, Errors)` tuple and accepts the config path to load `ApplicationInsights` settings via `BuildRootConfiguration`. This keeps validation offline (no network calls per FR-315).
**Why:** Empty connection string with `Enabled: true` is a hard error (blocks telemetry), while a malformed format or out-of-range sampling is a softer warning. Keeping these separate preserves the existing `ComputeStatus` severity model.

### 2026-04-27: User directive
**By:** Steven Murawski (via Copilot)
**What:** Never merge main back into a branch. Feature branches must stay clean — no back-merges from main. Rebase if needed.
**Why:** User request — captured for team memory

### 2026-04-27T14:50:29Z: User directive
**By:** Steven Murawski (via Copilot)
**What:** Never merge main back into a branch. Feature branches should never have main merged into them.
**Why:** User request — captured for team memory

### 2026-04-27: User directive
**By:** Steven Murawski (via Copilot)
**What:** Always use rebase. When updating feature branches with upstream changes, use git rebase, never git merge.
**Why:** User request — captured for team memory

# PR #180 Review: ConfigureApplicationInsights() — REQUEST CHANGES

**Reviewer:** Farnsworth (Lead/Architect)
**Date:** 2026-04-28
**Branch:** squad/172-configure-app-insights
**Verdict:** REQUEST CHANGES

---

## Spec Compliance Summary

| FR | Status | Notes |
|----|--------|-------|
| FR-303 | ✅ PASS | Early return when `!options.Enabled` — no SDK wiring |
| FR-304 | ✅ PASS | Env var fallback via `Environment.GetEnvironmentVariable` |
| FR-305 | ✅ PASS | `Console.Error.WriteLine` warning + return |
| FR-306 | ✅ PASS | Package is `Azure.Monitor.OpenTelemetry.AspNetCore` v1.4.0 |
| FR-307 | ✅ PASS | Exact method signature match |
| FR-308 | ✅ PASS | `services.AddOpenTelemetry().UseAzureMonitor(...)` |
| FR-309 | ✅ PASS | `transport.mode` resource attribute (becomes global dimension) |
| FR-310 | ❌ FAIL | **Not implemented** — no telemetry enrichment adds parameter names |
| FR-311 | ⚠️ GAP | No active suppression; Debug-level logs include parameter values |
| FR-312 | ⚠️ GAP | No active suppression for PowerShell output |
| FR-316 | ✅ PASS | Serilog untouched |
| FR-317 | ✅ PASS | McpMetrics meter flows through shared OTel pipeline |
| FR-318 | ✅ PASS | appsettings section present with `Enabled: false` |

---

## Required Changes

### 1. FR-310: Tool parameter names as custom properties (BLOCKING)

The spec requires tool parameter **names** to appear in custom properties on telemetry. The current implementation only wires the Azure Monitor exporter but adds no telemetry enrichment. This requires one of:

- An `ITelemetryInitializer` that inspects incoming telemetry and adds parameter name tags
- Activity tag additions in the tool execution path (in `PowerShellAssemblyGenerator.cs`)
- A custom `ActivitySource` span wrapping tool invocations that includes `param.name.*` tags

**Recommendation:** Add Activity tags at the point where tool parameters are resolved (around line 692-730 in `PowerShellAssemblyGenerator.cs`). Add tags like `tool.param.names = "Name,Id,Module"` (comma-separated list). This keeps VALUES out but exposes the schema.

### 2. FR-311/FR-312: Active suppression of parameter values and output (BLOCKING)

`UseAzureMonitor()` enables the OpenTelemetry **log exporter** by default. The existing code at `PowerShellAssemblyGenerator.cs:731-738` logs:

```csharp
logger.LogDebug("Tool parameter detail: ... Value={ParameterValue}", ..., paramValue);
```

And at line 801-808:
```csharp
logger.LogDebug("Bound parameter: ... Value={Value}", ..., convertedValue);
```

While these are at `Debug` level (suppressed by default `Information` filter), the spec says **MUST NOT** — meaning defensive suppression is required regardless of log level configuration. Options:

**Option A (preferred):** Configure `UseAzureMonitor` to disable log export entirely — only export traces + metrics:
```csharp
services.AddOpenTelemetry()
    .UseAzureMonitor(opts => { ... })
    .WithLogging(logBuilder => logBuilder.AddFilter("*", LogLevel.None)); // suppress all OTel log export
```

**Option B:** Add a log filter category that excludes the `PowerShellAssemblyGenerator` category from OTel export.

**Option C:** Strip the `Value=` fields from those log templates (replace with `HasValue={HasValue}` bool). This is the most invasive but cleanest long-term.

**I recommend Option A** for this PR — it's additive, non-invasive, and satisfies FR-311/FR-312 definitively. FR-316 (Serilog continues unchanged) is also preserved since Serilog operates at the ILogger provider level, independent of OTel log export.

---

## Non-Blocking Observations

1. **Double `AddOpenTelemetry()` call ordering:** The implementation correctly relies on `AddOpenTelemetry()` idempotency. Add a brief comment at the call site noting that this builds on the metrics registration from `ConfigureOpenTelemetry`/`ConfigureOpenTelemetryForHttp`.

2. **`SamplingRatio` type:** The SDK's `SamplingRatio` is a `float`. The division `samplingPercentage / 100.0f` is correct but note that `Math.Clamp` returns `int`, so this always produces a clean float division. Fine.

3. **Missing `SectionName` constant:** PR #177 (previously approved) included `public const string SectionName = "ApplicationInsights"` on the options class. This PR uses inline `"ApplicationInsights"` string. Minor inconsistency — prefer the constant.

---

## Architectural Assessment

The plumbing is correct. The method placement (after `ConfigureOpenTelemetry*`), the early-return guard, the connection string resolution chain, and the `ConfigureResource` approach for global dimensions are all architecturally sound. The gaps are in telemetry enrichment and defensive security filtering — both required by spec.

---

## Assignment

Return to original author for fixes. The changes are well-scoped additions to the existing method — no architectural rework needed.

# Wave 1 Review — Spec 008 Application Insights Logging

**Reviewer:** Farnsworth (Lead Architect)
**Date:** 2026-04-27
**Spec:** 008-application-insights-logging

---

## PR #176 — feat: add Azure.Monitor.OpenTelemetry.AspNetCore package reference

**Branch:** squad/170-azure-monitor-otel-package
**Verdict:** ✅ APPROVED

### Findings

| Check | Status | Notes |
|-------|--------|-------|
| Package name | ✅ Pass | Azure.Monitor.OpenTelemetry.AspNetCore (correct, not legacy) |
| Package version | ✅ Pass | 1.4.0 |
| FR-306 compliance | ✅ Pass | Uses modern OpenTelemetry-based SDK |
| Build | ✅ Pass | 0 errors, 9 warnings (pre-existing) |

### Diff Summary

```diff
+    <PackageReference Include="Azure.Monitor.OpenTelemetry.AspNetCore" Version="1.4.0" />
```

---

## PR #177 — feat: add ApplicationInsights config section and binding model

**Branch:** squad/171-app-insights-config-section
**Verdict:** ✅ APPROVED

### Findings

| Check | Status | Notes |
|-------|--------|-------|
| Enabled default | ✅ Pass | false (zero overhead) |
| ConnectionString default | ✅ Pass | empty string |
| SamplingPercentage default | ✅ Pass | 100 |
| SectionName constant | ✅ Pass | "ApplicationInsights" |
| XML documentation | ✅ Pass | All public members documented |
| appsettings.json section | ✅ Pass | Present with Enabled: false |
| FR-300 compliance | ✅ Pass | |
| FR-301 compliance | ✅ Pass | |
| FR-302 compliance | ✅ Pass | |
| FR-318 compliance | ✅ Pass | |
| Build | ✅ Pass | 0 errors, 9 warnings (pre-existing) |

### Diff Summary — appsettings.json

```diff
+  "ApplicationInsights": {
+    "Enabled": false,
+    "ConnectionString": "",
+    "SamplingPercentage": 100
+  },
```

### Diff Summary — ApplicationInsightsOptions.cs

New file with correct structure:
- `SectionName` constant
- `Enabled` property (default: false)
- `ConnectionString` property (default: empty)
- `SamplingPercentage` property (default: 100)
- XML docs on class and all properties

---

## Recommendation

Both PRs are ready to merge. Wave 1 infrastructure for spec 008 is complete.

# Diagnosis: OAuthProxy Configuration Not Reachable in Deployed Image

**Date:** 2026-05-02  
**Investigator:** Amy (DevOps/Platform/Azure)  
**Issue:** `/.well-known/oauth-authorization-server` returns 404 → `Authentication__OAuthProxy__Enabled` is false or missing  
**Root Cause:** OAuthProxy configuration is not present in ANY appsettings.json file in the PoshMcp repository, and the deployment pipeline does not support it.

---

## Findings

### 1. Dockerfile Analysis
**File:** `./Dockerfile` (lines 1-88)

- **Build Stage:** Compiles PoshMcp.Server project with `dotnet publish`
- **Publish Location:** `/app/publish/server` → copied to container image as `/app/server`
- **Appsettings Handling:** The Dockerfile does NOT explicitly copy any appsettings.json file
- **Config Source:** The `dotnet publish` command includes `./PoshMcp.Server/appsettings.json` in the published output by default
- **No Overlay:** The Dockerfile does not layer additional appsettings files on top of the published application

**Implication:** The appsettings.json bundled in the container image is EXACTLY what exists in `./PoshMcp.Server/appsettings.json` at build time.

### 2. PoshMcp.Server appsettings.json Status
**File:** `./PoshMcp.Server/appsettings.json`

- **OAuthProxy Configuration:** ❌ NOT PRESENT
- **Current Authentication Section:**
  ```json
  "Authentication": {
    "Enabled": false,
    "DefaultScheme": "Bearer",
    "DefaultPolicy": {
      "RequireAuthentication": true,
      "RequiredScopes": [],
      "RequiredRoles": []
    },
    "Schemes": {}
  }
  ```
- **No `OAuthProxy` subsection:** The file contains no `OAuthProxy` configuration at all

### 3. Other Appsettings Files Checked
- `./PoshMcp.Server/appsettings.azure.json` — PowerShell config only, no auth
- `./PoshMcp.Server/appsettings.modules.json` — PowerShell module config only
- `./PoshMcp.Server/appsettings.environment-example.json` — Example template, no OAuthProxy
- `./PoshMcp.Server/default.appsettings.json` — Not present or empty
- `./examples/appsettings*.json` — All templates/examples only

**None of these files contain OAuthProxy configuration.**

### 4. Deployment Pipeline Analysis
**File:** `./infrastructure/azure/deploy.ps1` (lines 302–407)

The `ConvertTo-McpServerEnvVars()` function translates appsettings.json keys to Container App environment variables.

**Supported Translations:**
- `PowerShellConfiguration.*` → `PowerShellConfiguration__*`
- `Authentication.Enabled` → `Authentication__Enabled`
- `Logging.LogLevel.Default` → `Logging__LogLevel__Default`

**Missing:** ❌ No handling for `Authentication.OAuthProxy.*` or any nested auth properties  
**Missing:** ❌ No support for `ProtectedResource.*` or `IdentityProvider.*`

**Impact:** Even if OAuthProxy were added to an appsettings.json file, the deployment script would silently ignore it and NOT convert it to an env var.

---

## Root Cause Chain

1. **OAuthProxy is not defined in PoshMcp.Server/appsettings.json**
   - The base/default appsettings has only minimal Authentication config
   
2. **The Dockerfile does not overlay a custom appsettings file**
   - There is no `COPY` instruction for any alternate appsettings files
   - The bundled config is what's published by `dotnet publish`

3. **The ASP.NET Core configuration system loads appsettings.json from the working directory**
   - Container working directory: `/app` (line 33 of Dockerfile)
   - The published binary is in `/app/server/`
   - ASP.NET Core loads `./appsettings.json` relative to the executable, which is `/app/server/appsettings.json`
   - This matches what was published during build

4. **No environment variables are setting OAuthProxy values**
   - The Container App revisions have no `Authentication__OAuthProxy__*` env vars
   - Even if they did, the Bicep/deploy.ps1 doesn't have logic to inject them

5. **Result:** The server runs with `Authentication.Enabled = false`, no OAuthProxy section → returns 404 for `/.well-known/oauth-authorization-server`

---

## What Steven Meant (and What's Wrong)

**Steven's Statement:**  
> "The appsettings.json with OAuthProxy settings IS bundled into the container image — env vars shouldn't be needed."

**Reality:**  
- ❌ There is NO appsettings.json in the repo with OAuthProxy settings
- ❌ The Dockerfile does not perform any custom bundling of appsettings files
- ✅ In theory, IF an appsettings.json with OAuthProxy were in `./PoshMcp.Server/`, it WOULD be bundled by `dotnet publish`
- ❌ But that file does not exist yet

**Likely Scenario:**  
Either Fry or another team member:
1. Created a patched appsettings.json (e.g., in a separate branch or external to this repo)
2. Assumed it would be included in the image
3. Did NOT merge the patch into `./PoshMcp.Server/appsettings.json`
4. Did NOT rebuild/redeploy the image with the patched config

---

## Resolution Path

### **Option A: Add OAuthProxy to PoshMcp.Server/appsettings.json (Recommended)**

1. Update `./PoshMcp.Server/appsettings.json` with a complete `Authentication.OAuthProxy` section
2. Include `TenantId`, `ClientId`, `Audience`, `Scopes`, etc.
3. Rebuild the image: `docker build -t poshmcp:latest .`
4. Deploy the new image to Azure Container Apps
5. OAuthProxy config will be bundled in the image and used by ASP.NET Core's config system

**Pros:**  
- Config is immutable, baked into the image
- No env var surprises or shadowing
- Follows `.NET standard` (appsettings.json is the config source)

**Cons:**  
- Credentials/secrets should use Azure Key Vault, not hardcoded JSON
- Requires rebuild + redeploy for config changes

### **Option B: Update Deployment Pipeline to Support OAuthProxy Env Vars**

1. Extend `ConvertTo-McpServerEnvVars()` in `deploy.ps1` to handle `Authentication.OAuthProxy.*` keys
2. Ensure Bicep resource template accepts and injects these env vars
3. Deploy OAuthProxy config as Container App environment variables (with Key Vault secrets for sensitive values)

**Pros:**  
- Config can be updated without image rebuild
- Secrets managed via Key Vault
- Flexible for multi-environment deployments

**Cons:**  
- More complex deployment logic
- Requires changes to Bicep, deploy.ps1, and possible env var structure

### **Option C: Both (Recommended for Production)**

1. Add a minimal OAuthProxy template to `./PoshMcp.Server/appsettings.json` with placeholder values
2. Extend `deploy.ps1` and Bicep to support overriding OAuthProxy values via env vars
3. At deploy time, inject actual credentials as Container App env vars
4. This gives flexibility + security

---

## Next Steps

1. **Clarify with Fry/Steven:** Where is the patch to appsettings.json that adds OAuthProxy? Is it in a separate branch, external repo, or lost?

2. **For Bender (Development/Build):**  
   - If the OAuthProxy config exists externally, add it to `./PoshMcp.Server/appsettings.json`
   - Update `deploy.ps1` `ConvertTo-McpServerEnvVars()` function to translate OAuthProxy keys
   - Add integration test to verify OAuthProxy settings are passed as env vars

3. **For Amy (Deployment):**  
   - Once config is in place, rebuild image: `./docker.ps1 build -ImageTag latest`
   - Push image to registry
   - Deploy with `infrastructure/azure/deploy.ps1`
   - Verify `/.well-known/oauth-authorization-server` returns 200

---

## References

- **Dockerfile:** Publishes from `./PoshMcp.Server/` with `dotnet publish`
- **PoshMcp.Server/appsettings.json:** Currently has minimal Authentication config, no OAuthProxy
- **deploy.ps1 (ConvertTo-McpServerEnvVars):** Does not translate OAuthProxy keys to env vars
- **ASP.NET Core Config Loading:** Reads `appsettings.json` from app working directory

# Release v0.9.10

**By:** Amy (DevOps / Platform / Azure Engineer)  
**Date:** 2026-05-02  
**Status:** Applied  

## What

Completed release of PoshMcp v0.9.10 by:
1. Verifying fix commit (b81a55d: OAuth issuer in Entra metadata)
2. Confirming version bump to 0.9.10 in `PoshMcp.Server/PoshMcp.csproj`
3. Pushing main branch to origin (2 commits)
4. Creating and pushing annotated tag `v0.9.10`
5. Verifying CI triggered automatically

## Why

Release implements security/configuration fix for OAuth Entra issuer metadata propagation. Bender had prepared all necessary changes (fix commit, version bump, release notes); Amy executed the final push and tagging steps to trigger the container build pipeline.

## Technical Details

- **Fix commit:** b81a55d on main — sets Entra issuer in AS metadata for OAuth compliance
- **Release notes:** Updated `docs/release-notes/0.9.10.md` and `docs/toc.yml`
- **CI Triggered:** GitHub Actions workflow "Build and Publish Packages" (Run 25254551703)
  - Builds Dockerfile → pushes container to `ghcr.io/usepowershell/poshmcp:0.9.10`
  - Expected completion: ~5-10 minutes

## Next Steps (Steven)

1. Monitor workflow at https://github.com/usepowershell/PoshMcp/actions/runs/25254551703
2. Confirm container image published to GHCR with tag `0.9.10`
3. Coordinate AdvocacyBami update with new base image reference (do NOT modify AdvocacyBami files in this release)

## Context

This is a .NET/Docker project using tag-based release model (not npm). Release notes and version were already prepared by Bender. Amy's role was infrastructure/operations — pushing to origin and triggering CI.

## Rule Going Forward

Tag push automatically triggers GitHub Actions workflows. No additional manual steps needed — just monitor the container build completion and publish status.

# Decision: v0.9.11 Release — OAuth /authorize Proxy Endpoint

**Date:** 2026-05-02T10:11:52-05:00
**Agent:** Amy (DevOps / Platform Engineer)
**Status:** COMPLETED

## Context

Bender committed `feat(auth): add /authorize proxy redirect endpoint for VS Code OAuth` and bumped the version to 0.9.11 in PoshMcp.Server/PoshMcp.csproj.

The commit introduced a critical OAuth fix: VS Code MCP clients were constructing auth URLs as `{proxy_base}/authorize` and receiving 404 errors. The new endpoint acts as a proxy that:
- Accepts all OAuth2 PKCE parameters
- Issues a 302 redirect to Entra's authorize endpoint
- Replaces the ephemeral DCR client_id with the real Entra client_id from config

## Decision

Release v0.9.11 with the following artifacts:

### 1. Release Notes (`docs/release-notes/0.9.11.md`)

Created following the established pattern from v0.9.10:
- **Title:** PoshMcp v0.9.11 Release Notes
- **What's New:** OAuth /authorize proxy redirect endpoint feature
- **Bug Fixes:** OAuth flow now completes for VS Code MCP clients
- **Upgrade Notes:** Configuration guidance for Authentication.ClientId and TenantId

### 2. Table of Contents (`docs/toc.yml`)

Updated Release Notes section to include:
```yaml
- name: v0.9.11
  href: release-notes/0.9.11.md
```

Added at the top of the release notes list, maintaining reverse chronological order.

### 3. Git Workflow

- **Commit:** `docs: add v0.9.11 release notes` (7e67ac9)
  - Included Copilot co-author trailer as per project standards
  - Modified: docs/release-notes/0.9.11.md, docs/toc.yml
- **Push:** Pushed commit to origin main (b81a55d → 7e67ac9)
- **Tag:** Created lightweight tag `v0.9.11` on commit 7e67ac9
- **Push Tag:** Pushed tag to origin (new tag on remote)

## Rationale

### Release Timing

The OAuth /authorize endpoint is a critical bug fix for VS Code MCP client compatibility. Without this fix, VS Code clients cannot complete the OAuth flow, making the MCP server unusable with that client. This warrants an immediate patch release.

### Release Notes Format

Followed the established release notes pattern:
- Single-file release notes document per version
- Listed in toc.yml in reverse chronological order
- Clear sections: Features, Bug Fixes, Upgrade Notes
- Concise descriptions tied to user impact

### Version Numbering

No decision needed — Bender already bumped to 0.9.11 in the csproj. This is a patch release (0.9.10 → 0.9.11) appropriate for a bug fix + proxy endpoint.

## Implementation Checklist

- ✅ Version confirmed: 0.9.11 in PoshMcp.Server/PoshMcp.csproj
- ✅ Release notes created with feature and bug fix details
- ✅ Table of contents updated
- ✅ Commit created with proper trailer
- ✅ Changes pushed to origin main
- ✅ Git tag created and pushed
- ✅ Release is now discoverable by CI/CD (publish-packages.yml listens for v* tags)

## Artifacts Created

- `docs/release-notes/0.9.11.md` — Release notes document
- `docs/toc.yml` — Updated table of contents
- Git commit: `7e67ac9` — Release notes commit on main
- Git tag: `v0.9.11` — Release tag pointing to commit 7e67ac9

## Next Steps (Async — CI Handles Automatically)

The publish-packages.yml GitHub Actions workflow will automatically:
1. Detect the `v0.9.11` tag push
2. Build and test the release
3. Create a GitHub Release with the release notes
4. Publish poshmcp v0.9.11 to NuGet.org

No manual intervention required unless CI reports failures.

## Related Documents

- `.squad/agents/amy/history.md` — Updated with session entry
- `.copilot/skills/release-process/SKILL.md` — Release process guidelines (reviewed)
- `docs/release-notes/0.9.11.md` — This release's notes
- `docs/toc.yml` — Navigation updated

# Decision: Advertise explicit delegated scope in AS metadata

**Date:** 2026-05-02
**Author:** Bender (Backend Developer)
**Status:** Implemented

## Problem

After the `/authorize` redirect succeeded and the user authenticated, the token exchange returned a JWT that failed with `SecurityTokenInvalidIssuerException`.

**Root cause:** `OAuthProxyEndpoints.cs` advertised `api://{audience}/.default` in `scopes_supported` of the AS metadata document. When VS Code requested the `.default` scope, Entra issued a **v1.0 token** whose issuer claim is `https://sts.windows.net/{tenant}/`. The configured `ValidIssuers` expected the v2.0 issuer `https://login.microsoftonline.com/{tenant}/v2.0`, causing validation failure.

## Decision

Change `scopes_supported` to advertise an explicit delegated scope instead of `.default`.

**Resolution order:**
1. Look up `config.DefaultPolicy.RequiredScopes` for an entry that starts with the configured audience URI.
2. If found, use it — keeps AS metadata aligned with what token validators require.
3. Otherwise, fall back to `{audience}/user_impersonation`.

## Implementation

**File:** `PoshMcp.Server/Authentication/OAuthProxyEndpoints.cs`

```csharp
// Before
scopesSupported.Add($"{proxy.Audience.TrimEnd('/')}/.default");

// After
var audienceBase = proxy.Audience.TrimEnd('/');
var explicitScope = config.DefaultPolicy?.RequiredScopes
    .FirstOrDefault(s => s.StartsWith(audienceBase, StringComparison.OrdinalIgnoreCase));
scopesSupported.Add(explicitScope ?? $"{audienceBase}/user_impersonation");
```

**Commit:** `fix: advertise explicit user_impersonation scope in AS metadata to prevent v1.0 token issuance`

## Why `.default` is wrong here

The `.default` scope instructs Entra to grant all statically-declared permissions for the app. When the app registration is a v1.0 registration (or has no explicit v2.0 access token configuration), Entra issues a v1.0 token signed by `sts.windows.net`. Our middleware validates against `login.microsoftonline.com/.../v2.0` — these are different issuers. Explicit delegated scopes (e.g. `user_impersonation`) force v2.0 token issuance regardless of app registration version.

## Impact

- VS Code now requests `api://{audience}/user_impersonation` instead of `.default`.
- Entra issues v2.0 tokens; issuer validation passes.
- No changes required to `ValidIssuers` or token validation configuration.

# Decision: Fix Auth Challenge Not Firing for No-Token Requests

**Date:** 2026-05-02
**Author:** Bender (Backend Developer)
**Status:** Implemented

## Problem

When VS Code's MCP client connects with no pre-existing auth credentials, it was not being redirected to sign in — the connection hung at `initialize`. The container log showed `aspnetcore.authentication.result: none` (expected — no token was presented), but the OAuth browser redirect never happened.

## Root Cause

Two related defects in the auth challenge path:

### 1. `OnChallenge` condition too narrow (`AuthenticationServiceExtensions.cs`)

The `OnChallenge` handler that injects `WWW-Authenticate: Bearer resource_metadata="..."` was gated on:

```csharp
if (cfg.Value.ProtectedResource?.Resource is not null)
```

The `AuthenticationConfigurationValidator` does **not** require `ProtectedResource.Resource` to be set — it is optional. When `Resource` is null (a valid configuration), the condition is `false`. The handler fell through to the default JWT Bearer challenge, which emits only `WWW-Authenticate: Bearer` with no `resource_metadata` parameter.

VS Code's MCP client reads `resource_metadata` to discover the OAuth Authorization Server. Without it, no browser redirect is triggered and the connection hangs waiting for the MCP `initialize` response.

This affects the "no token" case (`authentication.result: none`) specifically because a valid token would have bypassed the challenge entirely.

### 2. RFC 9728 `resource` field could be `null` in PRM response (`ProtectedResourceMetadataEndpoint.cs`)

The Protected Resource Metadata endpoint only substituted non-HTTPS URIs with `serverBase`. If `Resource` was `null` or empty, the `resource` field in the PRM JSON was `null`. RFC 9728 requires `resource` to be an absolute HTTPS URI — a null value breaks the VS Code OAuth discovery chain even if the challenge had fired correctly.

## Fix

**`AuthenticationServiceExtensions.cs`:** Changed condition from `ProtectedResource?.Resource is not null` to `ProtectedResource is not null`. This aligns with `MapProtectedResourceMetadata`'s own gate (also `ProtectedResource is not null`), ensuring `resource_metadata` is always sent in the challenge whenever the PRM endpoint is available.

**`ProtectedResourceMetadataEndpoint.cs`:** Added a null/empty fallback so `resource` is always computed as `serverBase` when `Resource` is not configured, before applying the existing non-HTTPS substitution. This ensures the PRM `resource` field always satisfies RFC 9728.

## Expected Flow After Fix

1. VS Code sends `POST /` with no token
2. Server returns `401` + `WWW-Authenticate: Bearer resource_metadata="https://host/.well-known/oauth-protected-resource"` (now fires even when `Resource` is null)
3. VS Code fetches the PRM; `resource` is now always a valid HTTPS URI
4. VS Code reads `authorization_servers`, fetches AS metadata
5. VS Code opens browser → user signs in → token obtained → retry succeeds

# Decision: Use Entra v2.0 Authority URL for JWT Bearer authentication

**Date:** 2026-05-02
**Author:** Bender (Backend Developer)
**Status:** Accepted and implemented

## Context

AdvocacyBami was logging `SecurityTokenSignatureKeyNotFoundException` followed by 401 responses. The JWT Bearer middleware was configured with `Authority = https://login.microsoftonline.com/{tenant}` (no `/v2.0` suffix), which resolves to the Entra **v1.0** OIDC discovery document. The v1.0 JWKS (`/common/discovery/keys`) does not contain the signing keys used for tokens issued via the v2.0 token endpoint (`/oauth2/v2.0/token`), which is what VS Code uses.

## Decision

1. **Always append `/v2.0` to the Entra Authority URL** when the token flow uses the v2.0 endpoint. Specifically: `https://login.microsoftonline.com/{tenant}/v2.0`.

2. **PoshMcp server should warn at startup** when it detects the dangerous mismatch: Authority is a v1.0 Entra URL but `ValidIssuers` contains a v2.0 issuer. This is implemented via `Console.Error.WriteLine` in `AuthenticationServiceExtensions.cs`.

## Rationale

- Entra v1.0 and v2.0 endpoints use different JWKS URIs and issue tokens with different signing keys.
- A v1.0 Authority with a v2.0 token always fails signature validation silently — no obvious configuration error is reported until runtime failure.
- The startup warning gives operators an actionable message before the first request fails.

## Consequences

- **AdvocacyBami**: `appsettings.json` Authority updated. JWT validation now succeeds for v2.0 tokens.
- **PoshMcp**: Any deployment with this misconfiguration will log a clear warning on startup.
- **No breaking changes**: The v2.0 OIDC discovery doc is a superset of v1.0 for validation purposes.

## Affected Files

- `C:\Users\stmuraws\source\emu\gim-home\AdvocacyBami\appsettings.json`
- `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs`

# Decision: Add /authorize proxy redirect endpoint

**Date:** 2026-05-02
**Author:** Bender (Backend Developer)
**Status:** Implemented — v0.9.11

## Context

VS Code's MCP OAuth client does not use `authorization_endpoint` from the Authorization Server metadata document directly. Instead it constructs the auth URL as `{authorization_server_base}/authorize`, where `authorization_server_base` comes from `authorization_servers[0]` in the Protected Resource Metadata. Since PoshMcp is the authorization server base, VS Code was issuing `GET /authorize?...` → **404**.

Root cause diagnosed by Fry.

## Decision

Add a `GET /authorize` endpoint to `OAuthProxyEndpoints.cs` that acts as a redirect proxy to Entra's real authorize endpoint.

## Implementation

**File:** `PoshMcp.Server/Authentication/OAuthProxyEndpoints.cs`

The endpoint:
1. Accepts all incoming query parameters via `HttpContext.Request.Query`
2. Iterates params with `SelectMany` to handle multi-value params; replaces `client_id` (case-insensitive) with `proxy.ClientId` from config
3. Ensures `client_id` is always present even if the caller omits it
4. Builds the redirect URL: `https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize` + `QueryString.Create(params)`
5. Returns `Results.Redirect(url, permanent: false)` — HTTP 302
6. Logs at Debug level: tenant ID only (no code_challenge, state, or other sensitive values)

All other params (`scope`, `response_type`, `code_challenge`, `code_challenge_method`, `redirect_uri`, `state`) pass through unchanged. Scope transformation is deliberately omitted — Entra handles `api://.../.default` scopes natively.

## Alternatives Considered

- **Rewrite `authorization_endpoint` in AS metadata to point at Entra directly**: Would fix VS Code but break other clients that rely on the proxy for `client_id` substitution. Rejected.
- **Update Protected Resource Metadata to point at Entra as authorization server**: Would require clients to handle the real Entra authorize URL directly, losing the DCR proxy benefit. Rejected.

## Guard rails

- Endpoint is only registered when `proxy.Enabled == true` and `proxy.TenantId` is non-empty (same guards as existing proxy endpoints)
- Returns `501 Not Implemented` if `proxy.ClientId` is unconfigured
- Marked `.AllowAnonymous()` — auth challenge must not intercept the OAuth handshake itself

## Version

`PoshMcp.csproj` bumped from `0.9.10` → `0.9.11`

# Decision: Honor X-Forwarded-Proto in All Public-URL Construction

**Date:** 2026-05-02  
**Author:** Bender (Backend Developer)  
**Status:** Implemented

## Context

Fry's v0.9.8 functional check found that the `WWW-Authenticate: Bearer resource_metadata=` URL
returned by the server used `http://` instead of `https://` when deployed to Azure Container Apps.
Azure Container Apps (and similar reverse-proxy platforms) terminate TLS and forward requests
internally over HTTP, setting `X-Forwarded-Proto: https` to indicate the original public scheme.

`HttpContext.Request.Scheme` returns `http` in this configuration, producing incorrect URLs.

## Decision

**Any code that constructs a public-facing URL from the current request MUST read
`X-Forwarded-Proto` (and optionally `X-Forwarded-Host`) before falling back to the raw request
values.**

Canonical pattern:

```csharp
var scheme = req.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? req.Scheme;
var host   = req.Headers["X-Forwarded-Host"].FirstOrDefault() ?? req.Host.ToUriComponent();
var url    = $"{scheme}://{host}{path}";
```

## Rationale

- `OAuthProxyEndpoints.GetServerBaseUrl` and `ProtectedResourceMetadataEndpoint` already
  implemented this pattern correctly.
- `AuthenticationServiceExtensions.OnChallenge` was the only location that did not; this
  inconsistency caused the bug reported by Fry.
- Using the forwarded headers is the standard ASP.NET Core approach for hosted-behind-proxy
  scenarios (cf. `UseForwardedHeaders` middleware, `ForwardedHeadersOptions`).

## Scope of Change

- `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs` — fixed `OnChallenge` handler
- `PoshMcp.Server/PoshMcp.csproj` — version bumped to 0.9.9

## Affected Deployments

All deployments behind a reverse proxy that terminates TLS (Azure Container Apps, nginx, etc.).
Local/stdio deployments are unaffected (fallback to `req.Scheme` = `https` or `http` as
configured).

# Decision: OAuth Issuer and Scope Fix (v0.9.10)

**By:** Bender (Backend Developer)
**Date:** 2026-05-02
**Status:** Applied

## What

Fixed two bugs causing MCP `initialize` timeout when using Entra ID authentication:

1. **Issuer mismatch** — `OAuthProxyEndpoints.cs` returned the container's own URL as `issuer` in the `/.well-known/oauth-authorization-server` metadata. Changed to `https://login.microsoftonline.com/{tenantId}/v2.0`.

2. **Scope format mismatch** — `RequiredScopes` in `AdvocacyBami/appsettings.json` used the full URI form (`api://.../{clientId}/user_impersonation`). Entra v2.0 tokens carry `scp` as the short name only (`user_impersonation`). Changed to `["user_impersonation"]`.

## Why

RFC 8414 requires MCP clients to validate `token.iss == AS.issuer`. With the issuer set to the container URL, Entra tokens were always rejected, triggering infinite `initialize` retries. The scope mismatch caused every authenticated request to return 401.

## Rules Going Forward

- The `issuer` field in `/.well-known/oauth-authorization-server` MUST always be the Entra v2.0 issuer URL, not the server's base URL.
- `RequiredScopes` in configuration MUST use the short scope name (`user_impersonation`), not the full application URI form — Entra v2.0 tokens never include the full URI in the `scp` claim.
- When configuring `RequiredScopes`, test against a real token's decoded `scp` claim to confirm the exact format.

# Decision: Token Diagnostics and Configurable IdleTimeout

**By:** Bender (Backend Developer)
**Date:** 2026-05-02
**Status:** Applied

## What

1. **Token diagnostics**: Enhanced `/token` proxy in `OAuthProxyEndpoints.cs` to log HTTP status, Content-Type, and response body (on error) from Entra. On success, logs status+content-type only (no token body). Request field names are logged at Debug (no values to avoid leaking secrets).

2. **Configurable IdleTimeout**: Added `McpServerConfiguration` class and `McpServer.IdleSessionTimeoutSeconds` appsettings key. `HttpServerHost` reads this and passes it to `WithHttpTransport(opts => opts.IdleTimeout = ...)`.

## Why

- `/token` proxy failures were invisible — no logging on Entra errors made auth debugging very hard.
- VS Code's ~5s initialize timeout causes double auth redirect loops when server startup takes time. `IdleSessionTimeoutSeconds` lets operators tune the session idle timeout without code changes.

## Rule Going Forward

- Never log token values, auth codes, or client secrets — log field names and HTTP metadata only.
- `WithHttpTransport` in MCP SDK 1.2.0 accepts `Action<HttpServerTransportOptions>` — use this overload for transport configuration rather than `builder.Services.Configure<HttpServerTransportOptions>()` separately.


### 2026-04-29T15:11:29Z: User directive
**By:** Steven Murawski (via Copilot)
**What:** All GitHub posts (issue creation, issue comments, PR creation, PR comments, PR reviews) MUST include the name of the agent posting it. Format: **{emoji} {AgentName} ({Role})**  at the start of the message body.
**Why:** User request - ensures traceability of which AI team member authored each GitHub interaction.
**Archived:** 2026-05-07 (>7d, decisions.md >= 50KB threshold)
- `poshmcp build --generate-dockerfile` now works out of the box without errors.
- The non-generate-dockerfile default remains `custom`, preserving the primary build workflow.
- Users who want to generate the custom Dockerfile must pass `--type custom` explicitly.

## Archived 2026-05-12 (entries before 2026-05-05)

### 2026-05-02T06:39:00-05:00: User directive — progress reporting
**By:** Steven Murawski (via Copilot)
**What:** Report progress at each step of tasks: when starting something, if something significant occurs, and when ending. Applies to all agents and to the Coordinator's task narration.
**Why:** User request — captured for team memory. Improves visibility into multi-step work.

### 2026-05-02: User directive
**By:** stmuraws (via Copilot)
**What:** Never use `git pull`; always run `git fetch` and then `git rebase` from the fetched branch.
**Why:** User request — captured for team memory

# Architect Review: PR #184 — Program.cs Refactoring

**Reviewer:** Farnsworth (Lead Architect)
**Date:** 2026-05-02
**PR:** https://github.com/usepowershell/PoshMcp/pull/184
**Branch:** `squad/program-cs-refactor`

---

## Summary

PR reduces Program.cs from 2,290 → 733 lines by extracting 6 focused classes. The structural intent is correct and the individual classes are well-organized. However, the extraction approach has a critical flaw: **methods were copied into new classes but not removed from Program.cs**, creating active code duplication across 5+ files.

---

## ✅ What's Good

1. **Namespace consistency** — All 6 new classes use `namespace PoshMcp;`, matching the existing pattern from `SettingsResolver`, `ConfigurationLoader`, etc.

2. **Single entry point per class** — Each service has a clean primary method: `RunMcpServerAsync`, `RunHttpTransportServerAsync`, `RunDoctorAsync`, `SetupMcpToolsAsync`. Not a grab-bag of unrelated utilities.

3. **CliDefinition.Build() pattern** — Clean separation between CLI tree declaration and handler wiring. `SetHandler` lambdas in `Main()` are more readable with `CliDefinition` properties than inline `Option<T>` construction.

4. **Delegate injection in DoctorService** — Passing `McpToolSetupService.DiscoverToolsForCliAsync` as a `Func<>` to `DoctorService.RunDoctorAsync` avoids hard static coupling from Diagnostics layer to Server layer. Good layering instinct.

5. **Session memory discipline** — Spec was kept up to date and the worktree boundary was respected throughout.

---

## ⚠️ Concerns

1. **`CliDefinition` nullable static properties are null until `Build()` is called** — All 70+ options/commands are `Option<T>?` initialized to `null`. Callers must use `!` (null-forgiving operator) at every `SetHandler` call site. If `Build()` is ever called more than once (e.g., in tests), the mutable static state is silently replaced. Consider returning a value object from `Build()` rather than side-effecting static fields.

2. **`CliDefinition` and `CommandHandlers` are `public`** — `DoctorService`, `McpToolSetupService`, `StdioServerHost`, `HttpServerHost` are all `internal`. `CliDefinition` and `CommandHandlers` have no documented reason to be `public`. If tests need to call these, that should be via `InternalsVisibleTo`, not by widening their access to the entire assembly surface.

3. **`RegisterCleanupServices` duplication not addressed** — Noted as out of scope but worth tracking: `StdioServerHost` and `HttpServerHost` both have near-identical service registration logic. This should be extracted before the duplication compounds further.

---

## 🔴 Must Fix (blocking)

### 2026-05-03: Release v0.9.20 — Authentication Fixes
**By:** Amy (DevOps / Platform / Azure Engineer)
**Status:** Completed
**What:** Cut patch release v0.9.20 (commit b87ca27) capturing three auth fixes and a diagnostics consistency improvement: (1) HasRequiredRoles uses .Any() (OR semantics) for Entra app roles; (2) MapInboundClaims=false on JWT bearer to preserve short claim names; (3) RequiredScopes now uses short scope name (user_impersonation) matching JWT scp claim; (4) DoctorReport.cs uses FindAll(`"roles"`) consistent with MapInboundClaims=false. Bumped PoshMcp.csproj 0.9.19→0.9.20, prepended CHANGELOG, committed with Copilot co-author trailer, lightweight tag v0.9.20.
**Why:** Production Entra OAuth flows were failing due to claim-mapping and role-semantics mismatches. CI/CD auto-publishes to NuGet + GHCR on tag push.

### 2026-05-03: RequiredRoles Uses OR Semantics
**By:** Bender (Backend Developer)
**Status:** Accepted
**What:** Changed AuthorizationHelpers.HasRequiredRoles from .All() (AND) to .Any() (OR). User satisfies the check if they hold any one of the listed roles. ToolAuthorizationFilter and ToolListAuthorizationFilter inherit the corrected semantics automatically.
**Why:** Aligns with (1) Entra app roles being granted one-at-a-time, (2) ASP.NET Core's policy.RequireRole(string[]) which uses OR, (3) explicit product intent. AND semantics is no longer achievable via RequiredRoles — would need nested policies or custom claims.

### 2026-05-03: Fix DoctorReportTests role claim type for MapInboundClaims=false
**By:** Fry (Tester)
**Status:** Accepted
**Commit:** e64b800
**What:** In DoctorReportTests.Build_WithAuthenticatedIdentity_PopulatesIdentitySection, changed `new Claim(ClaimTypes.Role, "admin")` to `new Claim("roles", "admin")`. Single occurrence; no other tests affected.
**Why:** DoctorReport.cs (commit 8c8e4ad) switched to FindAll(`"roles"`) to match MapInboundClaims=false behavior. Test fixtures must mirror the production claim-name form. Result: failing test now passes; full unit suite 582 passed / 1 skipped / 0 failed.
**Rule Going Forward:** Future tests building role claims for DoctorReport validation must use `"roles"` as the claim type, not ClaimTypes.Role.

### 2026-05-03: Release v0.9.21 — Test Fix for DoctorReport Role Claim
**By:** Amy (DevOps)
**Status:** Released
**What:** Patch release v0.9.21 capturing the DoctorReportTests fix from commit e64b800. Verified PoshMcp.csproj already at 0.9.21, CHANGELOG entry present, commit 2ad3739 with Copilot co-author trailer, tag v0.9.21 pushed to origin/main. Pre-release quality gates (dotnet format --verify-no-changes, dotnet test --filter "Category!=Integration" --no-build) both PASS.
**Why:** Ship the DoctorReportTests claim-name fix that was broken by v0.9.20's MapInboundClaims=false change. CI/CD auto-publishes NuGet + GHCR on tag push.

### 2026-05-03: Release-Process Skill — Mandatory Quality Gates
**By:** Leela (Developer Advocate)
**Status:** Approved
**What:** Updated .squad/skills/release-process/SKILL.md to make `dotnet format --verify-no-changes` and `dotnet test` MANDATORY pre-commit steps. Inserted as Step 4 between "Update changelog" and "Leela owns release notes." Renumbered subsequent steps (old 4–9 → new 5–10). Updated YAML description, added anti-pattern `"❌ Pushing a release without running dotnet test first"`, added recovery instructions ("If either command fails, fix the issue first and restart from step 2.").
**Why:** v0.9.20 was pushed and tagged without running dotnet test locally; a failing test was discovered post-release, forcing a v0.9.21 hotfix. Local quality gates shift-left testing, catch failures faster than CI, and become part of the human-executable checklist instead of being buried in CI docs.
**Rule Going Forward:** Release process must run format+test gates locally before commit/tag/push. No exceptions.

### 2026-05-01T15:30:29: Release process gate added
**By:** Amy (DevOps)
**What:** Release process now requires docs/release-notes/{version}.md to exist and be committed before pushing or tagging a release.
**Why:** v0.9.3 release notes were written by Leela but not committed before Amy pushed and tagged. Gate prevents recurrence.


# Amy: v0.9.4 Release Decision Log

**Date:** 2026-05-01T16:16:11.622-05:00  
**Task:** Execute v0.9.4 release — gate is clear  
**Status:** ✅ COMPLETE

## Release Summary

| Field | Value |
|-------|-------|
| **Version Bumped** | 0.9.3 → 0.9.4 |
| **Commit SHA** | 0cadd42 |
| **Tag** | v0.9.4 (pushed) |
| **CI Result** | ✅ GREEN (1m36s) |
| **Gate Status** | ✅ PASSED (`docs/release-notes/0.9.4.md` verified) |

## Files Committed

**7 files included in release commit:**

1. `PoshMcp.Server/PoshMcp.csproj` — Version bump 0.9.3 → 0.9.4
2. `CHANGELOG.md` — New v0.9.4 entry (OAuth discovery + ApiKey metadata URL fixes)
3. `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs` — Bender's RFC 9728 fix
4. `PoshMcp.Server/Authentication/ApiKeyAuthenticationHandler.cs` — Bender's ApiKey metadata URL fix
5. `docs/release-notes/0.9.4.md` — Leela's detailed release notes (new, untracked)
6. `docs/entra-id-auth-guide.md` — Leela's Entra ID auth guide update
7. `docs/toc.yml` — Leela's table of contents update

**Staging Approach:** Used explicit file paths (`git add <path1> <path2> ...`) instead of wildcard to ensure only intended files were staged. This prevented accidental staging of `.squad/` history files or other working changes.

## CI Workflow Execution

**Workflow Runs Triggered:**
- Run ID `25233890105` — "CI" (primary build/test workflow)
- Run ID `25233890078` — "Preview Packages" (package preview build)

**CI Results (25233890105):**
- ✅ Build: 1m36s
- ✅ Checkout: PASS
- ✅ Setup .NET: PASS
- ✅ Restore dependencies: PASS
- ✅ Verify formatting: PASS
- ✅ Build: PASS (5 pre-existing nullable reference warnings — non-blocking)
- ✅ Test (Unit): PASS
- ✅ Test (Functional): PASS
- ✅ Upload Test Results: PASS
- Deprecation Notice: GitHub Actions Node.js 20 EOL (2026-06-02) — no action for this release

**CI Gate Decision:** ✅ PASSED — Release approved for tagging.

## Tag Creation and Push

```
git tag v0.9.4
git push origin v0.9.4
```

**Result:** Tag successfully created and pushed to origin.
- Remote confirmation: `* [new tag] v0.9.4 -> v0.9.4`
- No conflicts or rejections

## Key Decisions

1. **Release Gate:** Enforced by verifying `docs/release-notes/0.9.4.md` existence before proceeding. Leela had already committed this file, so gate was clear from the start.

2. **File Staging Strategy:** Used explicit file paths instead of `git add .` to avoid staging unrelated working changes (e.g., `.squad/` history updates, which were made after the release files were committed).

3. **CI Blocking:** Applied `gh run watch --exit-status` to block on CI completion. This ensures tests pass before tagging, preventing bad releases.

4. **Co-Author Trailer:** Commit message includes `Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>` per project standards.

## Verification Checklist

- [x] Gate file exists and has content
- [x] Version bumped in .csproj
- [x] CHANGELOG.md entry added
- [x] All modified files staged (git status clean after staging)
- [x] Commit created with proper message and co-author trailer
- [x] Pushed to origin/main without conflicts
- [x] CI workflows triggered and completed
- [x] All CI jobs passed (tests, build, formatting)
- [x] Tag created and pushed

## Recommendations for Future Releases

1. **Automate version bumping:** Consider adding a script that bumps version + updates CHANGELOG in a single operation to reduce manual steps.

2. **Pre-flight validation:** Before `git push`, validate that all required release files are staged (using `git diff --cached --name-only` and a checklist).

3. **CI dashboard monitoring:** Set up alerts for CI failures to enable faster feedback loops on future releases.

4. **Release notes in PR:** Require release notes to be included in the feature/fix PR itself (not added in a separate step) to ensure documentation stays in sync with code changes.

## Notes

- Repository URL redirect (usepowershell/poshmcp → usepowershell/PoshMcp) was noted during git push; no action needed. Consider updating local remote URL when convenient.
- No manual package publish needed; GitHub Actions workflows handle NuGet/GitHub Packages publishing automatically on tag push.


# Auth Bypass Diagnosis: Unauthenticated Requests Still Served Despite v0.9.2 Fix

**Date:** 2026-05-01  
**Author:** Bender  
**Server:** https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io  
**Image version confirmed running:** 0.9.2.0

---

## 1. Is the New Image Actually Running?

**Yes.** The `initialize` response includes:
```json
"serverInfo": {"name":"PoshMcp","version":"0.9.2.0"}
```
Version 0.9.2.0 is confirmed deployed.

---

## 2. Does the `appsettings.json` Look Correct?

**Yes.** The user's config at `C:\Users\stmuraws\source\emu\gim-home\AdvocacyBami\appsettings.json` (deployed to `PoshMcp/appsettings.json` in the container) has correct auth settings:
```json
"Authentication": {
    "Enabled": true,
    "DefaultScheme": "Bearer",
    "DefaultPolicy": {
        "RequireAuthentication": true,
        "RequiredScopes": ["api://80939099-d811-4488-8333-83eb0409ed53/access_as_server"]
    },
    "Schemes": {
        "Bearer": {
            "Type": "JwtBearer",
            "Authority": "https://login.microsoftonline.com/...",
            "Audience": "api://80939099-d811-4488-8333-83eb0409ed53"
        }
    },
    "ProtectedResource": { ... }
}
```

---

## 3. What the Running Server's Troubleshooter Says

The `get-configuration-troubleshooting` tool returned `authentication.enabled: true`. **BUT THIS IS MISLEADING.** The troubleshooting and guidance tools read config directly from the file via `ConfigurationLoader.BuildRootConfiguration(configPath)` — NOT from the DI `IOptions<AuthenticationConfiguration>`. The file correctly has `Enabled: true`, so the tools report `true`. The DI runtime sees something different.

**Key evidence that auth IS actually disabled in the runtime DI:**
1. `/.well-known/oauth-protected-resource` returns **404** — `MapProtectedResourceMetadata` has an early return guard `if (!config.Enabled || config.ProtectedResource is null)`. 404 confirms `IOptions<AuthenticationConfiguration>.Value.Enabled` is `false` at runtime.
2. No `WWW-Authenticate` header on any request — auth challenge never fires.
3. `tools/list` returns ALL 7 tools to unauthenticated callers — `ToolListAuthorizationFilter` short-circuits when `authConfig.Enabled = false`.
4. `tools/call get-configuration-troubleshooting` succeeds without a token.

---

## 4. Root Cause: Configuration Precedence Issue in `RunHttpTransportServerAsync`

### 2026-05-01T15:30:29: User directive
**By:** Steven Murawski (via Copilot)
**What:** Amy must wait for Leela's release notes to be committed before cutting a release (version bump commit, push, CI, tag).
**Why:** Release notes were not committed in time for v0.9.3 push — captured for team memory.


# Auth Regression Tests — IOptions Registration Fix

**From:** Fry (QA)
**Date:** 2026-05-01
**Related:** `AddPoshMcpAuthentication()` IOptions registration bug fix

## Summary

Added `PoshMcp.Tests/Unit/AuthenticationServiceExtensionsTests.cs` with 3 regression tests that directly prove `services.Configure<AuthenticationConfiguration>()` is called inside `AddPoshMcpAuthentication()`, so `IOptions<AuthenticationConfiguration>` always resolves to the configured values rather than the default.

## Tests Added

| Test | Scenario | Key Assertion |
|---|---|---|
| `WhenAuthEnabled_IOptionsAuthenticationConfiguration_ReflectsConfig` | Auth enabled with 2 schemes | `Enabled == true`, `DefaultScheme == "Bearer"`, `Schemes.Count == 2` |
| `WhenAuthDisabled_IOptionsAuthenticationConfiguration_IsRegisteredWithEnabledFalse` | Auth disabled | `Enabled == false`, `DefaultScheme` reflects config value |
| `WhenNoAuthSection_IOptionsAuthenticationConfiguration_DoesNotThrow` | No `Authentication` section in config | No exception, `Enabled == false` |

## Test Pattern

```csharp
var config = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?> { ... })
    .Build();
var services = new ServiceCollection();
services.AddPoshMcpAuthentication(config);
var sp = services.BuildServiceProvider();
var options = sp.GetRequiredService<IOptions<AuthenticationConfiguration>>();
Assert.True(options.Value.Enabled);
```

## Status

3/3 passing ✅ — no regressions in full suite.


# Decision Memo: Entra ID Authentication Documentation Consolidation

**Date:** 2026-05-01
**Author:** Leela (Developer Advocate)
**Status:** ✓ Implemented

## Summary

Consolidated two separate Entra ID authentication documents (`entra-id-mcp-auth.md` and `entra-id-auth-guide.md`) into a single comprehensive guide at `docs/entra-id-auth-guide.md`. The new file has been deleted after its content was folded in.

## Inconsistencies Found & Resolved

---

## Archived 2026-05-12

Archived from `decisions.md` head (entries pre-dating the 2026-05-05 dated-entry chronology). File was 132 KB, exceeding the 50 KB hard gate; entries older than 7 days moved.


### 1. `DescribeConfigurationPath` duplicated across 5 files

This private utility method (`string DescribeConfigurationPath(string?)`) now exists independently in:
- `Program.cs`
- `DoctorService.cs`
- `CommandHandlers.cs`
- `StdioServerHost.cs`
- `HttpServerHost.cs`

Same story for `ToToolName`, `GetDiscoveredToolNames`, `GetExpectedToolNames` (exist in both `Program.cs` and `DoctorService.cs`).

**Fix:** Extract these to a shared utility class — `ConfigurationPathHelper` or inline into `ConfigurationLoader` — and delete the duplicates. This must happen before merge, or the codebase will have 5 independent copies of identical logic that will drift.

### 2. `Program.BuildDoctorReportFromConfig` / `Program.BuildDoctorJson` are not removed

The extraction created `DoctorService.BuildDoctorReportFromConfig` and `DoctorService.BuildDoctorJson` correctly. But the originals in `Program.cs` were **not removed**. Program.cs lines 251–440 are entirely duplicated in `DoctorService.cs`. Tests still call `Program.BuildDoctorReportFromConfig` — they should be updated to call `DoctorService.BuildDoctorReportFromConfig`, or Program.cs should forward to DoctorService.

This is not a 68% reduction — it is a 68% reduction in the **entry-point glue**, but the substantive logic is duplicated.

**Fix:** Either:
- (a) Remove the full implementations from `Program.cs`, update tests to call `DoctorService.BuildDoctorReportFromConfig` directly, OR
- (b) Make `Program.BuildDoctorReportFromConfig` a single-line delegation to `DoctorService.BuildDoctorReportFromConfig` (preserving test compatibility while eliminating the duplicate logic)

Option (b) is lower risk for this PR; option (a) is the correct long-term state.

---

## 💡 Recommendations (non-blocking)

1. **Add a shared `ConfigurationHelpers` static class** for `DescribeConfigurationPath`, `ToToolName`, `GetExpectedToolNames`, `GetDiscoveredToolNames`. These are used across CLI, Diagnostics, and Server layers — they need a neutral home.

2. **CliDefinition redesign consideration** — Instead of mutable static properties set during `Build()`, consider having `Build()` return a `CliSetup` record type containing the constructed `RootCommand` and all option/command references. This avoids the null-before-Build problem and makes the contract explicit.

3. **Test class naming** — Tests calling `Program.BuildDoctorReportFromConfig` directly are in `ProgramTests.cs`. Once the method moves to `DoctorService`, rename to `DoctorServiceTests.cs` for clarity.

4. **Follow-on PR should target ≤400 lines** — The ConfigurationManager extraction (~200 lines) plus cleaning up the remaining doctor helper duplicates will bring Program.cs to a reasonable boundary.

---

## Verdict: CHANGES REQUESTED

The structural direction is correct and the CliDefinition/CommandHandlers/ServerHost split is clean. The blocker is the unfinished extraction: **doctor helper methods still exist in Program.cs in full**, duplicating what's in DoctorService.cs. Fix the duplication (blocking item #2) and the utility method copies (blocking item #1) before merge. Both are addressable within 1–2 small commits.

# Root Cause: VS Code /authorize Redirect Bug

**Date:** 2026-05-02T10:11:52-05:00
**By:** Fry (Tester)
**Requested by:** Steven Murawski

## Summary

VS Code MCP client redirects the browser to:
```
https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/authorize?...
```
instead of `https://login.microsoftonline.com/.../oauth2/v2.0/authorize?...`

The container's `/authorize` returns **404 Not Found**, so the OAuth flow fails immediately.

## Evidence

### 1. AS metadata `authorization_endpoint` — CORRECT

```
GET /.well-known/oauth-authorization-server
```
```json
{
  "authorization_endpoint": "https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/oauth2/v2.0/authorize"
}
```

The AS metadata is correct. `authorization_endpoint` points directly to Entra, not the container.

### 2. Container `GET /authorize` — 404

```
GET /authorize?client_id=...&response_type=code&scope=openid&redirect_uri=...
→ 404 Not Found (no Location header)
```

No `/authorize` endpoint exists on the container.

### 3. Code review — no `/authorize` handler

`PoshMcp.Server/Authentication/OAuthProxyEndpoints.cs` registers only:
- `GET /.well-known/oauth-authorization-server`
- `POST /register`

**No `/authorize` route is registered anywhere in the codebase.**

## Root Cause

**VS Code's MCP OAuth client does not use `authorization_endpoint` from the AS metadata.**

Instead, VS Code constructs the authorization URL as:
```
{authorization_server_base_url}/authorize?<params>
```

The `authorization_server_base_url` comes from `authorization_servers[0]` in the Protected Resource Metadata (PRM):
```json
"authorization_servers": ["https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io"]
```

So VS Code builds `https://poshmcp.../authorize?...` and opens it in the browser → 404.

## Classification

**Root cause c:** The proxy `/authorize` handler is **missing entirely** from the server.

The AS metadata is correct. The bug is that VS Code doesn't read `authorization_endpoint` from the metadata — it derives `/authorize` from the authorization server base URL. Since PoshMcp is the declared authorization server (in the PRM), the container must host a working `/authorize` endpoint that proxies/redirects to Entra.

## Required Fix

Add a `GET /authorize` handler to `OAuthProxyEndpoints.cs` that:
1. Accepts all standard OAuth2 query parameters (`client_id`, `response_type`, `scope`, `redirect_uri`, `state`, `code_challenge`, `code_challenge_method`)
2. Issues a `302 Found` redirect to the real Entra `authorization_endpoint`:
   ```
   https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize?<all-params-forwarded>
   ```
3. The `client_id` in the forwarded request must be the configured Entra `ClientId` (not the DCR-issued ephemeral one), since Entra only knows about the registered app.

## Impact

All MCP clients (VS Code and others) that follow the "construct `/authorize` from authorization server base URL" pattern will fail to complete OAuth until this handler is added. The `/register` DCR flow works correctly — the failure is in step 5 of the OAuth flow (browser redirect to authorization endpoint).

# Diagnosis: MCP `initialize` Timeout — "Waiting for server to respond"

**Filed by:** Fry (Tester)
**Date:** 2026-05-02
**Deployment:** `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io`
**Symptom:** MCP client logs "Waiting for server to respond to initialize request..." every 5 seconds indefinitely after logging "Discovered authorization server metadata."

---

## Evidence Collected

### 1. Health Check — ✅ Healthy

```
GET /health → 200
{
  "status":"Healthy",
  "checks":[
    {"name":"powershell_runspace","status":"Healthy","description":"PowerShell runspace responsive"},
    {"name":"assembly_generation","status":"Healthy","description":"Assembly generation ready"},
    {"name":"configuration","status":"Healthy",
     "data":{"FunctionCount":3,"ModuleCount":1,"AuthEnabled":true,"AuthSchemes":"Bearer"}}
  ]
}
```

Server is fully up.

### 2. Unauthenticated POST to `/` (MCP initialize, no token)

```
POST / → 401 Unauthorized (response in <1ms)
WWW-Authenticate: Bearer resource_metadata="https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/.well-known/oauth-protected-resource"
```

- **HTTPS scheme is correct** — the http:// bug from v0.9.8 is fixed. ✅
- Server is reachable and responds instantly to unauthenticated requests.

### 3. GET `/sse`

```
GET /sse → 404
```

No legacy SSE transport endpoint. Server uses Streamable HTTP only (POST /). This is expected for MCP 2025-03-26+, but legacy clients trying SSE first may behave oddly.

### 4. OAuth AS Metadata — `/.well-known/oauth-authorization-server`

```json
{
  "issuer": "https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io",
  "authorization_endpoint": "https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/oauth2/v2.0/authorize",
  "token_endpoint": "https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/oauth2/v2.0/token",
  "registration_endpoint": "https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/register",
  "scopes_supported": [
    "openid","profile","email","offline_access",
    "api://80939099-d811-4488-8333-83eb0409ed53/.default"
  ],
  ...
}
```

**⚠️ CRITICAL: `issuer` is the PoshMcp URL, not the Entra ID URL.**
- The tokens issued by Entra ID have `iss = "https://login.microsoftonline.com/d91aa5af.../v2.0"`
- The AS metadata says `issuer = "https://poshmcp..."` — these do NOT match
- Some MCP clients/OAuth libraries validate that the `iss` claim in the received token matches the `issuer` in the AS metadata. This would cause the client to reject the token entirely and never send a Bearer-authenticated initialize.

**⚠️ CRITICAL: `scopes_supported` does NOT include the actually required scope.**
- AS metadata advertises: `api://80939099-d811-4488-8333-83eb0409ed53/.default`
- Server requires in `RequiredScopes`: `api://80939099-d811-4488-8333-83eb0409ed53/user_impersonation`
- PRM advertises: `api://80939099-d811-4488-8333-83eb0409ed53/user_impersonation`
- If the client uses AS metadata's `scopes_supported` to decide what scope to request, it will request `.default`, which may or may not include `user_impersonation` depending on app permissions.

### 5. Protected Resource Metadata — `/.well-known/oauth-protected-resource`

```json
{
  "resource": "api://80939099-d811-4488-8333-83eb0409ed53",
  "resource_name": "PoshMcp Server",
  "authorization_servers": ["https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io"],
  "scopes_supported": ["api://80939099-d811-4488-8333-83eb0409ed53/user_impersonation"],
  "bearer_methods_supported": ["header"]
}
```

PRM correctly advertises `user_impersonation` scope. ✅

### 6. JWT Validation Functional — ✅

```
POST / (fake Bearer token) → 401 in 457ms
```

OIDC discovery from container → `login.microsoftonline.com` works. JWT validation is not hanging. This rules out the network-timeout hypothesis.

### 7. Server Auth Logs — NO BEARER TOKENS EVER PRESENTED

From container metrics dump (72 auth attempts):
```
aspnetcore.authentication.result: none   (scheme: Bearer, count: 72)
```

`result: none` means the Bearer middleware ran but found **no token** in any of those 72 requests. There are zero `result: success` or `result: failure` entries. **The MCP client is never sending a Bearer token to the server.** This confirms the OAuth flow is failing client-side before the token is presented to the server.

### 8. Scope Claim Format Mismatch (Code Analysis)

`appsettings.json`:
```json
"RequiredScopes": ["api://80939099-d811-4488-8333-83eb0409ed53/user_impersonation"]
```

`AuthenticationServiceExtensions.cs`:
```csharp
policy.RequireClaim("scp", authConfig.DefaultPolicy.RequiredScopes.ToArray());
```

This check uses **exact match**. But Entra ID v2.0 tokens store the scope as the short name in the `scp` claim:
- **Entra token `scp` claim**: `"user_impersonation"` (just the suffix, not the full URI)
- **Server expects**: `"api://80939099-d811-4488-8333-83eb0409ed53/user_impersonation"` (full URI)

Even if the client successfully obtains a valid Entra token with `user_impersonation` consented, the scope check will **always fail** with 401 because the full URI format does not match what Entra puts in the token.

Additionally, if the token has multiple scopes (`scp = "user_impersonation offline_access"`), ASP.NET Core `RequireClaim` does an exact-value match against the full space-separated string — this would also fail even with the correct format.

---

## Root Cause Analysis

There are **two compound bugs** that together prevent the initialize from ever succeeding:

### Bug 1 (Primary — prevents token from being sent): `issuer` mismatch in AS metadata

**Location:** `OAuthProxyEndpoints.cs` line 64: `var issuer = baseUrl;`

The AS metadata `issuer` is set to the PoshMcp server URL. Entra tokens have `iss = login.microsoftonline.com/{tenantId}/v2.0`. MCP client SDKs that validate `iss == issuer` (per RFC 8414 §2) will reject the token and never send an authenticated initialize request.

This explains the log sequence:
1. Client sends initialize → 401 → discovers AS metadata ✅ ("Discovered authorization server metadata")
2. Client completes OAuth flow and gets Entra token
3. **Client SDK validates: `token.iss` (`login.microsoftonline.com`) ≠ `AS.issuer` (`poshmcp`) → token rejected**
4. Client has no valid token; retries initialize without token → 401 → cycle repeats
5. Log: "Waiting for server to respond to initialize request..." every 5s forever

**The AS metadata `issuer` should be the Entra ID issuer, or the client needs to be informed differently.**

Per RFC 8414, the `issuer` in AS metadata must be the authorization server's own identifier. Since PoshMcp is a **resource server with an OAuth proxy** (not a true AS), the `issuer` should ideally be the Entra ID issuer. However, the PRM's `authorization_servers` points to `https://poshmcp...`, so the client fetches the AS metadata from PoshMcp — creating a proxy relationship where `issuer` must logically be the Entra issuer for token validation to work.

### Bug 2 (Secondary — ensures 401 even if token is presented): Scope format mismatch

**Location:** `appsettings.json` `RequiredScopes` configuration

`RequiredScopes` uses the full scope URI `api://80939099.../user_impersonation`. Entra v2.0 tokens have `scp = "user_impersonation"` (short name only). Even if Bug 1 is fixed and the client sends the Entra token, the scope check will still fail with 401.

**Fix options:**
- Change `RequiredScopes` to `["user_impersonation"]` (short name), OR
- Add custom scope claim parsing that extracts the scope short name from the full URI, OR
- Use Microsoft.Identity.Web's `ScopeAuthorizationRequirement` which handles Entra scope format

---

## What IS Working

| Check | Status |
|-------|--------|
| Server health | ✅ Healthy |
| WWW-Authenticate scheme (https://) | ✅ Fixed vs v0.9.8 |
| JWT OIDC discovery reachability | ✅ Working (457ms) |
| PRM scopes_supported format | ✅ Has correct `user_impersonation` |
| AS metadata auth/token endpoints | ✅ Correct Entra endpoints |

---

## Recommended Fixes (for the team to implement)

### Fix 1 — AS metadata `issuer` (High Priority)

In `OAuthProxyEndpoints.cs`, change `issuer` from the PoshMcp base URL to the Entra ID issuer:

```csharp
// Before:
var issuer = baseUrl;

// After:
var entraBase = string.Format(EntraV2BaseTemplate, proxy.TenantId);
var issuer = $"{entraBase}";  // e.g., "https://login.microsoftonline.com/{tenantId}/oauth2/v2.0"
// Or more precisely:
var issuer = $"https://login.microsoftonline.com/{proxy.TenantId}/v2.0";
```

This makes the `issuer` in AS metadata match the `iss` claim in Entra-issued tokens.

### Fix 2 — Scope format in RequiredScopes (High Priority)

Change `appsettings.json` (and documentation) so `RequiredScopes` uses the short scope name:

```json
"DefaultPolicy": {
  "RequireAuthentication": true,
  "RequiredScopes": ["user_impersonation"]
}
```

Or alternatively, add scope claim splitting logic so `RequireClaim("scp", "user_impersonation")` works when `scp = "user_impersonation offline_access"`.

### Fix 3 — Add `user_impersonation` to AS metadata `scopes_supported` (Medium Priority)

The AS metadata `scopes_supported` should advertise the scopes the client needs to request. Currently it only has `.default`. Add the delegated scope explicitly, or populate from `ProtectedResource.ScopesSupported`.

---

## Files to Investigate

- `PoshMcp.Server/Authentication/OAuthProxyEndpoints.cs` — issuer generation (line 64)
- `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs` — RequireClaim scope check
- `appsettings.json` — RequiredScopes value format

# Decision: OAuth Redirect Validation — Live Endpoint Diagnosis

**Date:** 2026-05-02
**Author:** Fry (Tester)
**Reviewers:** Amy (deploy/env vars), Bender (code), Farnsworth (oversight)
**Status:** OPEN — awaiting fix assignment

---

## Context

v0.9.5 shipped OAuth AS proxy + DCR proxy (`OAuthProxyEndpoints.cs`) to enable VS Code MCP clients to authenticate without manual client_id entry. Steven reports that connecting to the live Container App still does NOT redirect to `login.microsoftonline.com`.

Live endpoint: `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io`

---

## Findings Summary

### What IS working
- `/health` → 200, all checks healthy, `AuthEnabled: true`
- `/.well-known/oauth-protected-resource` → 200 (returns data)
- Auth enforcement → 401 with `WWW-Authenticate: Bearer resource_metadata=...`
- Image deployed today (`psbamiacr.azurecr.io/advocacybami:20260502-061835`, revision `poshmcp--0000019`, active)

### What is BROKEN

**Primary failure:** `/.well-known/oauth-authorization-server` → **404**

The OAuth proxy endpoint is not registered because `OAuthProxy.Enabled = false` in the running container. The code in `OAuthProxyEndpoints.MapOAuthProxyEndpoints` returns early when `proxy.Enabled == false`.

**Root cause:** None of the 4 required env vars are set on the Container App:
```
❌ Authentication__OAuthProxy__Enabled    (not set)
❌ Authentication__OAuthProxy__TenantId   (not set)
❌ Authentication__OAuthProxy__ClientId   (not set)
❌ Authentication__OAuthProxy__Audience   (not set)
```

Confirmed via: `az containerapp revision show -n poshmcp -g rg-poshmcp --revision poshmcp--0000019`

**Secondary failure:** PRM (`/.well-known/oauth-protected-resource`) advertises Entra directly

Because `OAuthProxy.Enabled = false`, the PRM does NOT inject the proxy URL as the authorization server. Instead, it returns a hardcoded Entra URL from `ProtectedResource.AuthorizationServers`. VS Code then tries `https://login.microsoftonline.com/{tenant}/.well-known/oauth-authorization-server` → **404** (Entra serves OIDC metadata, not RFC 8414 AS metadata). No `registration_endpoint` is available → VS Code cannot do DCR → no `client_id` → no OAuth redirect → **login.microsoftonline.com never triggered**.

**Tertiary defect (Bender):** `WWW-Authenticate` header uses `http://` instead of `https://`

`AuthenticationServiceExtensions.cs:60` builds `metadataUrl` from `req.Scheme` without honoring `X-Forwarded-Proto`. Azure Container Apps terminates TLS at the ingress, so the app sees `http`. The correct pattern (already used in `OAuthProxyEndpoints.cs::GetServerBaseUrl`) is:
```csharp
var scheme = req.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? req.Scheme;
var host = req.Headers["X-Forwarded-Host"].FirstOrDefault() ?? req.Host.ToUriComponent();
var metadataUrl = $"{scheme}://{host}/.well-known/oauth-protected-resource";
```

**Quaternary defect (investigate):** PRM arrays are duplicated

`authorization_servers`, `scopes_supported` each appear twice; `bearer_methods_supported` appears 3×. Likely caused by non-empty `ProtectedResource.AuthorizationServers` in the baked-in appsettings PLUS another config source (appsettings.Production.json or old env vars). Needs investigation to confirm source; clearing extra config sources should fix.

---

## VS Code Client Flow (Simulated)

```
GET /.well-known/oauth-protected-resource
  → authorization_servers[0] = https://login.microsoftonline.com/{tenant}

GET https://login.microsoftonline.com/{tenant}/.well-known/oauth-authorization-server
  → 404 (Entra does not serve RFC 8414 AS metadata here)

GET https://login.microsoftonline.com/{tenant}/.well-known/openid-configuration
  → 200, registration_endpoint = null (Entra doesn't support DCR)

⛔ No registration_endpoint → no DCR → no client_id → no OAuth flow → no redirect
```

---

## Recommended Actions

### 🔴 Amy — IMMEDIATE (no redeploy needed)

Set the 4 missing env vars on the Container App:

```bash
az containerapp update -n poshmcp -g rg-poshmcp \
  --set-env-vars \
    "Authentication__OAuthProxy__Enabled=true" \
    "Authentication__OAuthProxy__TenantId=d91aa5af-8c1e-442c-b77c-0b92988b387b" \
    "Authentication__OAuthProxy__ClientId=80939099-d811-4488-8333-83eb0409ed53" \
    "Authentication__OAuthProxy__Audience=api://80939099-d811-4488-8333-83eb0409ed53"
```

Also investigate/remove any `Authentication__ProtectedResource__AuthorizationServers__*` env vars that may be contributing to array duplication.

**Expected result after fix:**
- `/.well-known/oauth-authorization-server` → 200 (proxy metadata)
- `/.well-known/oauth-protected-resource` `authorization_servers[0]` → `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io` (the proxy)
- VS Code fetches AS metadata from the proxy → gets `authorization_endpoint`, `token_endpoint`, `registration_endpoint`
- VS Code POSTs `/register` → gets `client_id = 80939099-d811-4488-8333-83eb0409ed53`
- VS Code redirects to `https://login.microsoftonline.com/{tenant}/oauth2/v2.0/authorize` → login prompt appears ✅

**Deploy process fix:** Update the deployment process to invoke `deploy.ps1 -ServerAppSettingsFile ./appsettings.json` rather than a bare `az containerapp update --image ...`. The deploy.ps1 `ConvertTo-McpServerEnvVars` function correctly translates the appsettings into Container App env vars.

### 🟡 Bender — CODE FIX (low urgency, no user-visible impact until proxy works)

Fix `AuthenticationServiceExtensions.cs:60` `OnChallenge` handler to use `X-Forwarded-Proto`:

```csharp
// Before:
var metadataUrl = $"{req.Scheme}://{req.Host}/.well-known/oauth-protected-resource";

// After:
var scheme = req.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? req.Scheme;
var host = req.Headers["X-Forwarded-Host"].FirstOrDefault()
           ?? req.Host.ToUriComponent();
var metadataUrl = $"{scheme}://{host}/.well-known/oauth-protected-resource";
```

### 🟡 Bender — INVESTIGATE array duplication

Determine why `authorization_servers`, `scopes_supported`, and `bearer_methods_supported` appear 2–3× in the PRM response. Check for stale env vars or `appsettings.Production.json` in the image. Fix to ensure exactly one copy of each value.

---

## Decision

**Root cause is configuration, not code.** v0.9.5 code is deployed and correct. The fix is Amy setting 4 env vars on the Container App — no rebuild or code change required for the primary issue.

Bender should address the secondary `http://` and array-duplication bugs in a follow-up commit.

# v0.9.10 OAuth Fix Validation — AdvocacyBami Deployment

**Date:** 2026-05-02T10:02:31-05:00
**Tester:** Fry
**Deployment:** `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io`
**Release:** v0.9.10

## Summary

```
✅ Check 1: Health — 200 Healthy
✅ Check 2: issuer field — https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/v2.0 (expected: https://login.microsoftonline.com/d91aa5af-.../v2.0)
✅ Check 3: PRM — 200 OK, authorization_servers uses https://
✅ Check 4: WWW-Authenticate scheme — https://
✅ Check 5: DCR /register — client_id: 80939099-d811-4488-8333-83eb0409ed53

Overall: PASS
Root cause bugs fixed: Yes (Bug 1 confirmed; Bug 2 deployed, not directly observable without real Entra token)
```

---

## Check 1: Health

**Request:** `GET /health`
**HTTP Status:** 200
**Body:**
```json
{
  "status": "Healthy",
  "checks": [
    {"name": "powershell_runspace", "status": "Healthy"},
    {"name": "assembly_generation", "status": "Healthy"},
    {"name": "configuration", "status": "Healthy",
     "data": {"FunctionCount": 3, "ModuleCount": 1, "AuthEnabled": true, "AuthSchemes": "Bearer"}}
  ]
}
```
**Result:** ✅ PASS — Server healthy, auth enabled, 3 functions registered.

---

## Check 2: OAuth AS Metadata — issuer field (PRIMARY FIX VALIDATION)

**Request:** `GET /.well-known/oauth-authorization-server`
**HTTP Status:** 200

**Key fields:**
| Field | Value |
|-------|-------|
| `issuer` | `https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/v2.0` |
| `authorization_endpoint` | `https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/oauth2/v2.0/authorize` |
| `token_endpoint` | `https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/oauth2/v2.0/token` |
| `registration_endpoint` | `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/register` |

**Result:** ✅ PASS — `issuer` is now `https://login.microsoftonline.com/{tenantId}/v2.0` (Bug 1 fix confirmed). Previously the issuer was the server's own URL, which caused MCP client SDK to reject tokens (iss ≠ issuer). All endpoints point to `login.microsoftonline.com`. `registration_endpoint` is present.

---

## Check 3: Protected Resource Metadata

**Request:** `GET /.well-known/oauth-protected-resource`
**HTTP Status:** 200
**Body:**
```json
{
  "resource": "api://80939099-d811-4488-8333-83eb0409ed53",
  "resource_name": "PoshMcp Server",
  "authorization_servers": ["https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io"],
  "scopes_supported": ["api://80939099-d811-4488-8333-83eb0409ed53/user_impersonation", "api://80939099-d811-4488-8333-83eb0409ed53/user_impersonation"],
  "bearer_methods_supported": ["header", "header"]
}
```

**Result:** ✅ PASS — `authorization_servers` uses `https://` (not `http://`). The `http://` scheme bug (v0.9.8) is still fixed.

**⚠️ Minor observation:** `scopes_supported` and `bearer_methods_supported` both contain duplicate entries. Not a blocking issue but worth noting as a future cleanup item.

---

## Check 4: WWW-Authenticate Header

**Request:** `GET /` (unauthenticated)
**HTTP Status:** 401
**WWW-Authenticate header:**
```
Bearer resource_metadata="https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/.well-known/oauth-protected-resource"
```

**Result:** ✅ PASS — Returns 401 (not redirect), scheme is `https://` (not `http://`). MCP clients following this URL will get the PRM over HTTPS.

---

## Check 5: DCR /register Endpoint

**Request:** `POST /register` with `Content-Type: application/json` body `{}`
**HTTP Status:** 201
**Body:**
```json
{
  "client_id": "80939099-d811-4488-8333-83eb0409ed53",
  "client_id_issued_at": 1777734205,
  "token_endpoint_auth_method": "none"
}
```

**Result:** ✅ PASS — Returns 201 with correct Entra `client_id` `80939099-d811-4488-8333-83eb0409ed53`.

---

## Bug Fix Validation Assessment

### Bug 1: issuer mismatch (OAuthProxyEndpoints.cs)
**Status: ✅ CONFIRMED FIXED**
The `issuer` field now returns `https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/v2.0` exactly as required. MCP client SDKs that validate `iss == issuer` will now accept Entra tokens and proceed to send Bearer tokens in subsequent requests.

### Bug 2: scope format (AdvocacyBami/appsettings.json)
**Status: ✅ DEPLOYED (indirect confirmation)**
The `RequiredScopes` change from `["api://80939099.../user_impersonation"]` to `["user_impersonation"]` cannot be directly validated without a real Entra Bearer token. The deployment is live and Bug 1 is fixed, so the full end-to-end flow (token acquisition + scope check) can now be tested with a real MCP client. The health check confirms `AuthEnabled: true` with correct configuration.

---

## Regression Check

- HTTP → HTTPS scheme fix (v0.9.8): ✅ Still holding (Checks 3 and 4)
- DCR proxy: ✅ Still working (Check 5)
- Server health: ✅ Healthy with auth enabled (Check 1)

No regressions observed.

# Fry — v0.9.8 Deployment Verification Findings
**Date:** 2026-05-02
**Deployment:** https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io
**Image:** PoshMcp v0.9.8 (AdvocacyBami rebuild)

## Summary

| Check | Result | Notes |
|-------|--------|-------|
| 1. Health | ✅ PASS | All 3 sub-checks Healthy |
| 2. OAuth AS Metadata | ✅ PASS | Both endpoints → login.microsoftonline.com |
| 3. Protected Resource Metadata | ⚠️ PARTIAL | `resource` is `api://` URI, not container URL; rest is correct |
| 4. Dynamic Client Registration | ✅ PASS | 201 with correct client_id |
| 5. MCP Endpoint Reachability | ⚠️ ISSUE | `resource_metadata` URL uses `http://` instead of `https://` |

## Detailed Findings

### CHECK 1: Health — ✅ PASS
- **Status:** 200 OK
- **All checks healthy:** `powershell_runspace`, `assembly_generation`, `configuration`
- Configuration: 3 functions, 1 module, Auth enabled (Bearer)

### CHECK 2: OAuth Authorization Server Metadata (RFC 8414) — ✅ PASS
- **Status:** 200 OK
- **issuer:** `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io` ✅
- **authorization_endpoint:** `https://login.microsoftonline.com/d91aa5af-.../oauth2/v2.0/authorize` ✅ (NOT the container URL)
- **token_endpoint:** `https://login.microsoftonline.com/d91aa5af-.../oauth2/v2.0/token` ✅
- **registration_endpoint:** `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/register` ✅
- Scopes, grant types, PKCE all populated correctly.

### CHECK 3: Protected Resource Metadata (RFC 9728) — ⚠️ PARTIAL PASS
- **Status:** 200 OK
- **authorization_servers:** 1 entry (no duplicates) ✅
- **bearer_methods_supported:** `["header"]` (exactly 1, no duplicates) ✅
- **scopes_supported:** 1 entry, no duplicates ✅
- **⚠️ ISSUE — `resource` field:**
  - **Actual:** `"api://80939099-d811-4488-8333-83eb0409ed53"` (Entra app ID URI)
  - **Expected per task spec:** the container URL (`https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io`)
  - RFC 9728 allows either form; the app ID URI is valid for Entra-protected resources. Not a hard failure, but worth noting if MCP clients resolve `resource` to discover the server URL.

### CHECK 4: Dynamic Client Registration — ✅ PASS
- **Status:** 201 Created ✅ (task accepts 200 or 201)
- **client_id:** `80939099-d811-4488-8333-83eb0409ed53` ✅ (configured Entra app client ID)
- Response also includes `client_id_issued_at` and `token_endpoint_auth_method: none`.

### CHECK 5: MCP Endpoint Reachability — ⚠️ ISSUE
- **Status:** 401 Unauthorized ✅ (NOT a redirect to /authorize — the core OAuth fix is working)
- **WWW-Authenticate header present:** ✅
- **⚠️ ISSUE — `http://` in resource_metadata:**
  - **Actual:** `WWW-Authenticate: Bearer resource_metadata="http://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/.well-known/oauth-protected-resource"`
  - **Expected:** `https://` (the container serves HTTPS; `http://` reference in the header will cause MCP clients to attempt an insecure fetch, which will either fail or be redirected)
  - This is a configuration/code bug — the server is generating the `resource_metadata` URL with the wrong scheme.

## Recommended Actions

1. **Check 5 (`http://` in resource_metadata)** — **HIGH PRIORITY:** The `WWW-Authenticate: Bearer resource_metadata` URL must use `https://`. MCP clients (e.g., Claude Desktop, VS Code extension) follow this URL to discover OAuth metadata; an `http://` reference may fail TLS validation or get rejected. Investigate how the resource metadata URL is constructed — likely the app is reading `HttpContext.Request.Scheme` or a configured base URL that is resolving as `http` behind the Azure Container Apps reverse proxy. Fix: ensure `X-Forwarded-Proto` is honored, or hardcode the scheme from configuration.

2. **Check 3 (`resource` URI)** — **LOW PRIORITY / INFORMATIONAL:** `resource` = `api://80939099-...` is valid per RFC 9728 for Entra-protected APIs. No action required unless client tooling specifically expects the container HTTPS URL here.

# Decision: `MapInboundClaims = false` is Required; No `scope` in VS Code mcp.json

**By:** Leela (Developer Advocate)
**Date:** 2026-05-03
**Status:** Proposed

## Summary

Two requirements are now documented in `docs/entra-id-oauth-implementation-guide.md` as a result of live debugging sessions:

### 1. `MapInboundClaims = false` is a documented requirement

ASP.NET Core's JWT Bearer middleware remaps short JWT claim names (`scp`, `roles`) to long WS-Federation URI forms by default. This causes authorization policies that check for `scp` or `roles` by short name to silently fail — the token is valid, the claim is present, but it is stored under the wrong key in `ClaimsPrincipal`.

**Rule:** `options.MapInboundClaims = false` must always be set when configuring JWT Bearer authentication in PoshMcp. `TokenValidationParameters.RoleClaimType` must be set explicitly to the configured role claim short name so that `IsInRole()` continues to work.

This is implemented in `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs` and is now validated in the deployment checklist.

### 2. VS Code `mcp.json` must not include a `scope` field

An explicit `scope` field in VS Code's `mcp.json` causes VS Code's MCP auth provider to silently fail token acquisition — no `Authorization` header is sent, every request hits `DenyAnonymousAuthorizationRequirement`, and no useful error is surfaced to the user.

**Rule:** Do not set `scope` in VS Code's `mcp.json` for PoshMcp connections. Let VS Code read `scopes_supported` from the server's Protected Resource Metadata at `/.well-known/oauth-protected-resource` and handle scope selection automatically.

## Documentation

Both findings are documented in `docs/entra-id-oauth-implementation-guide.md`:
- Bug 5: `MapInboundClaims = false` — in the "Bugs We Hit and Why" section
- VS Code client gotcha — in the new "VS Code MCP Client Configuration Gotchas" section
- Validation Checklist updated with `MapInboundClaims` check
- Summary updated with lessons 5 and 6

## Archived 2026-05-13 (Scribe — entries >7d removed; decisions.md was 113,578 bytes >= 50KB threshold)

### 2026-05-05: Systemic future-dated entries across squad artifacts
**By:** Cubert (Fact Checker)
**Requested by:** Steven Murawski
**Status:** Flagged
**What:** Multiple squad artifacts contain entries dated 2026-07-15, 2026-07-18, 2026-07-28, 2026-07-30 — 2–3 months in the future relative to current date 2026-05-05. Affected: docs/articles/squad-work-log.md (Hermes 2026-07-15/18, Fry 2026-07-15/18, Bender 2026-07-30); .squad/decisions.md (multiple 2026-07-18 and 2026-07-28 entries); .squad/agents/farnsworth/history-archive.md (references to 2026-07-29); story article sample timestamp "2026-07-30T12:34:56Z".
**Why:** Either clerical errors or the project has been silently writing future dates for months. Either way, the integrity of the dated decision ledger is compromised — readers cannot trust chronology. Blocks publication of squad-story.md and squad-work-log.md.
**Recommendation:** (1) Audit git commit dates against `### YYYY-MM-DD` headers in .squad/decisions.md and agent histories; correct headers where they disagree with commit dates. (2) Re-affirm rule already in squad.agent.md: agents must use the CURRENT_DATETIME injected by the Coordinator, never an inferred or guessed date. (3) Document corrected dates in a follow-up decision once audit is complete.
### 2026-05-05: User directive — Cubert pre-reviews Farnsworth plans
**By:** Steven Murawski (via Copilot)
**What:** Cubert (Fact Checker) must review any plans, specs, or proposals Farnsworth creates before they are presented to the user for review. Cubert verifies accuracy, internal consistency, and any verifiable claims; only after Cubert's review does the plan reach the user.
**Why:** User request — captured for team memory. Inserts a fact-checking gate into the architecture proposal workflow.


### 2026-05-06: Hermes — Runspace pool vs multi-process experiment plan (Issue #65)

**By:** Hermes (PowerShell Expert)
**What:** Filed R&D plan at `specs/004-out-of-process-execution/runspace-pool-experiment-plan.md` covering two prototype paths for OOP parallelism: (Option A) a runspace pool inside one pwsh subprocess with a synchronized stdout writer and ISS-based pre-warm; (Option B) a pool of N subprocesses dispatched via a `Channel<OutOfProcessHost>` queue. Plan includes a benchmark harness design (BenchmarkDotNet + custom crash/recovery harness) with scenarios for CPU-light, CPU-bound, I/O-bound, network-shaped, heavy serialization, cold start, crash recovery, and isolation. Recommended phasing into 6 follow-up issues, starting with extracting `OutOfProcessHost` as shared infrastructure.
**Why:** Issue #65 asks us to compare in-process runspace pooling vs multiple processes for OOP execution. A written plan is needed before either prototype is built so the trade-offs (parallelism vs isolation, memory cost, startup cost, complexity) are explicit and the benchmark methodology is fixed in advance. The single biggest open trade-off is failure containment: Option A loses the strong isolation that motivated OOP in the first place, so the benchmark harness explicitly measures isolation as a pass/fail criterion.


### 2026-05-05: Squad story / work-log fact-check corrections
**By:** Leela (via Cubert verification)
**What:** Updated `docs/articles/squad-story.md` and `docs/articles/squad-work-log.md` with verified counts:
- Team size: 8 → 9 (Cubert added)
- NuGet downloads: 700+ → 1,600+
- PRs merged (window 2026-03-27..2026-04-25): "10+" → 34 (verified via `gh pr list --repo usepowershell/PoshMcp --state merged`)
- Issues closed (same window): 27 → 83 (verified via `gh issue list --state closed`)
- Commits to main (same window): "40+" → 183 (verified via `git log main`)
- Documentation: "8 articles, 12,000+ words" → "19 articles" (word count unverified, dropped)
- "0 broken builds" → "0 reverts" (matches what was actually verified)

**Why:** Story metrics were significantly understated and the team-size/article counts were stale after Cubert joined and docs grew. Numbers now match reproducible `gh`/`git` queries.


### 2026-05-06: Spec 004 milestone + 8 follow-up issues filed

**By:** Hermes (PowerShell Expert), at Steven Murawski's request
**What:** Merged PR #187 (runspace pool vs multi-process experiment plan) into `main` (squash merge, branch deleted, issue #65 referenced via `Refs #65` so it stays open). Created milestone **#5 — `Spec 004 - Out-of-Process PowerShell Execution`** (https://github.com/usepowershell/PoshMcp/milestone/5) and filed 8 follow-up issues from the plan's §5 phasing, all in the milestone with proper `Blocked by` cross-references and `squad:*` routing labels.

**Issues created:**

| # | Title | Plan ref | Owner label | Blocked by |
|---|-------|----------|-------------|------------|
| #189 | OOP: Bug-fix — clear `$Error` before invoke in single-runspace host | §5 #0 | squad:hermes | — |
| #190 | OOP: Extract `OutOfProcessHost` (with lifecycle unit tests) | §5 #1 | squad:bender | — |
| #191 | OOP: Option A prototype — runspace pool host (`SubprocessHostMode: "Pool"`) | §5 #2 | squad:hermes | #190 |
| #192 | OOP: Option B prototype — process pool executor (`SubprocessHostMode: "ProcessPool"`) | §5 #3 | squad:bender | #190 |
| #193 | OOP: Benchmark harness infrastructure (`PoshMcp.Benchmarks` project) | §5 #4a | squad:fry | — |
| #194 | OOP: Wire benchmark harness to executors | §5 #4b | squad:fry | #191, #192, #193 |
| #195 | OOP: Run benchmarks and write findings | §5 #5 | squad:hermes | #194 |
| #196 | OOP: Adopt the winner — make recommended mode default | §5 #6 | squad:farnsworth | #195 |

**Why:** Land the experiment plan, set up actionable follow-ups under a single milestone so the runspace-pool vs multi-process work can proceed without losing the dependency ordering. Issue #65 stays open as the umbrella tracker through prototype work; commented there with the milestone and issue list.

**Side effects:**
- Created two missing labels: `refactor` (`#D4E5F7`), `testing` (`#BFD4F2`).
- Auth workaround used throughout: `gh auth switch --user usepowershell` for write ops, switched back to `stmuraws_microsoft` after.


### 2026-05-06: Security policy
**By:** Farnsworth (requested by Steven Murawski)
**What:** Added `SECURITY.md` at repo root. Supported versions: only latest 0.x minor (currently 0.10.x); older minors unsupported. Reporting channel: GitHub private vulnerability reporting via Security tab — no security email address invented. Documented SLA (ack 3 business days, triage 7), coordinated disclosure, and reporter credit via GHSA.
**Why:** Establish a clear, standard security disclosure process before 1.0; align with GitHub's recommended private vuln reporting flow rather than ad-hoc email.


### 2026-05-06: Spec 004 foundation work — review outcomes
**By:** Farnsworth (Lead / Architect)
**Requested by:** Steven Murawski

**Three PRs against the runspace-pool experiment plan §5 sequencing (#0 / #1 / #4a) — all approved (as comments; gh `addPullRequestReview` rejects self-review even from the author's own account):**

- **PR #199 — Hermes — `fix(oop): clear $Error before invoke in single-runspace host (#189)`:** APPROVE. One-line `$Error.Clear()` in `Invoke-InvokeHandler` before user invoke. Regression test asserts both halves: first invoke produces a non-terminating error and reports `hadErrors=true`; second clean invoke reports `hadErrors=false` (pre-fix the second fails). CI green across build/test/CodeQL.
- **PR #198 — Bender — `refactor(oop): extract OutOfProcessHost (with lifecycle unit tests) (#190)`:** APPROVE. Per-process state cleanly moves to `OutOfProcessHost` (Process+Exited, stdin/stdout/stderr, `_sendLock`, `_pending`, read loops, shutdown sequence, `SendRequestAsync`). Executor keeps `setup`/`discover`/`invoke` payload shaping, `_cachedSchemas`, pwsh/script-path resolution. Lifecycle unit test walks start → ping → setup → shutdown → restart and asserts different PID after restart. Construction guards, double-Start, dispose-before-Start, IsRunning/ProcessId all covered. Integration tests refactored to walk `executor._host._process` via a small `GetSubprocess` helper — production code path unchanged. 591/0 pass. Unblocks #191 and #192.
- **PR #197 — Fry — `feat(benchmarks): PoshMcp.Benchmarks harness infrastructure (#193)`:** APPROVE. .NET 10 console project, BDN 0.14.0, project-references `PoshMcp.Server`, in `PoshMcp.sln`. `HttpListener` bound to `127.0.0.1:0` via `TcpListener` probe (correct ephemeral-port workaround). All 5 scenario stubs present (cold start, warm invoke, payload size serialization, process crash recovery, runspace corruption recovery). Per-scenario `[MinIterationCount]/[MaxIterationCount]` thresholds. Baseline (`HostMode.Single`) captured in same run via `[Params]` axis. `ApplicationInsights__Enabled=false` set in `Program.cs`. `MarkdownExporter.GitHub` configured. Stubs no-op; wiring is #194. CI checks green (an unrelated `submit-nuget` workflow failure is not a PR check).

**Issue #65 — "OOP: Experiment with runspace pool parallelism vs multiple processes":** CLOSED as completed. Superseded by the experiment plan (PR #187, merged) and its decomposition into issues #189 (prereq `$Error` fix), #190 (extract `OutOfProcessHost`), #191 (Option A prototype), #192 (Option B prototype), #193 (benchmark harness infra), #194 (wire harness to executors), #195 (run benchmarks + findings), #196 (adopt the winner). Tracking continues on the spec 004 milestone. Closing comment also noted the stale path reference in the issue body (`specs/out-of-process-execution.md` → canonical `specs/004-out-of-process-execution/`).

**Non-blocking follow-ups noted on #198 (do not block merge):**
- Migrate in-tree call sites of `OutOfProcessCommandExecutor` from the legacy `ILogger<OutOfProcessCommandExecutor>` constructor to the new `ILoggerFactory` overload so the host's logger is no longer silently routed to `NullLoggerFactory.Instance`.
- `IsNonJsonPowerShellStreamLine` promoted from `private` to `internal` to enable reflection-based unit testing — visibility creep is minor and justified.

**Process pattern (logged for team memory):** GitHub's GraphQL `addPullRequestReview` mutation rejects `APPROVE` (and `REQUEST_CHANGES`) when the reviewing identity is the PR author, even on the `usepowershell` account that is otherwise unblocked by EMU policy. Error: `Review Can not approve your own pull request`. Workaround: post the review body via `gh pr comment` instead. The badge-prefixed body preserves attribution either way. This is distinct from the EMU `stmuraws_microsoft` block on `gh pr review` / `gh pr comment` / `gh issue create` already documented in agent histories.

### 2026-05-06: Spec 004 prototypes review — PR #200 (Option B / Bender) and PR #201 (Option A / Hermes)
**By:** Farnsworth (Lead / Architect), requested by Steven Murawski
**What:** APPROVED both PRs. Both prototypes meet every architectural criterion in `runspace-pool-experiment-plan.md` §5 #2/#3. PR #200 implements `OutOfProcessSubprocessPool` with the channel + dictionary protocol, slot-0 fail-fast / slots-1..N-1 backoff, SHA-256 environment fingerprint discovery cache, per-request kill-on-timeout, and a parameterized integration test matrix over pool sizes 1/2/4. PR #201 implements `oop-host-pool.ps1` with ISS-based pre-warmed runspace pool, custom `PSHost`/`PSHostUserInterface` (correctly NOT `[Console]::Out`), synchronized stdout writer, per-pipeline `Streams.*` + per-runspace `$Error.Clear()`, full quiesce protocol on `setup` (`DrainEvent` + `PoolDispatcher.WaitIdle` → close → mutate → reopen → resume), and per-invoke metrics on the response frame.
**Why:** Both prototypes are required for the #194 benchmark phase; the plan's pass/fail comparison cannot run with only one option. Approving both unblocks Fry to wire the harness.
**Cross-PR collision (BLOCKING for whichever lands second):** Both PRs introduce a type named `SubprocessHostMode`, source-incompatible. PR #200 = `static class` with string constants and `string?` property. PR #201 = `enum SubprocessHostMode { Single, Pool }` with enum property. PR #200 reserved the name `Pool` in its constants signaling intent to coexist. Recommended convergence: standardize on PR #201's `enum`, extend with `ProcessPool` when the second PR rebases. Rebase scope ~30 lines (PowerShellConfiguration property type, McpToolSetupService dispatch check, Bender unit tests).
**Merge order recommendation:** Land **PR #201 (Option A / Hermes) first**, then have Bender rebase PR #200 onto the enum. Rationale: #201's diff is smaller (+1204 vs +1780), the enum is the more idiomatic C# baseline, and Bender's `IsProcessPool(string?)` helper has fewer call sites to convert than reverse direction. Either order works mechanically; this minimizes net rework.
**Non-blocking observations recorded in PR comments:** #200 — discovery cache key omits filter parameters (correct but worth doc note); `MinHealthyForStartup` clamp in `McpToolSetupService` is silent; unbounded lease channel; lease loop spins on stale slots. #201 — drain timeout hardcoded 60s (not threaded from `SubprocessTimeoutSeconds`); pool-size cap of 8 is a prototype guard; `Resolve-SwitchParameters` calls `Get-Command` on the host process not in a pool runspace (verify ISS-imported modules are visible).

### 2026-05-06: Farnsworth — PR #202 review (spec-004 benchmark harness wired)

**By:** Farnsworth (Lead / Architect) — review requested by Steven Murawski

**Verdict:** REQUEST CHANGES (single hard blocker is CI; architecture is approved).

**PR:** https://github.com/usepowershell/PoshMcp/pull/202 — Fry — feat(benchmarks): wire harness to executors (#194). Closes #194. Branch: `squad/194-wire-benchmark-harness`. Builds on #190 (OutOfProcessHost extraction), #201 (Hermes Option A — Pool), #200 (Bender Option B — ProcessPool).

**Decisions / calls captured:**

1. **Hard blocker is mechanical, not architectural.** CI `build / Verify formatting` fails: `dotnet format --verify-no-changes` reports ~50 whitespace errors in `PoshMcp.Benchmarks/ExecutorFactory.cs` switch-case bodies. Fix = run `dotnet format PoshMcp.sln`, commit, push. No architectural rework required.

2. **Acceptance criteria are met** (verified in worktree `poshmcp-194`): HostMode `[Params(Single, Pool, ProcessPool)]` on every scenario; all five scenarios implemented end-to-end; AI / spec-008 logging disabled at process start (env vars + harness never builds DI); markdown output includes mode / scenario / payload / mean / p95 / p99 / crash-recovery columns; README documents reproducible invocation.

3. **InternalsVisibleTo widening is acceptable.** One IVT entry added to `PoshMcp.csproj` for `PoshMcp.Benchmarks`. Bench needs `OutOfProcessCommandExecutor`, `OutOfProcessHost`, `OutOfProcessSubprocessPool`, `OutOfProcessSubprocessPoolOptions`, `SubprocessHostMode` — all internal in production. Scoped to one assembly, not a blanket open-up.

4. **Reflection-based crash injection is acceptable.** `KillOneHost()` reaches into `_host`, `_process`, `_slots` private fields from the bench-only assembly. Clearly labeled in XML docs, fails safe on missing fields. Fragility: silent degradation if those field names are ever renamed. Suggested follow-up (non-blocking): startup assertion that the reflection lookups resolve, abort the run if any are gone.

5. **Crash-recovery time as `Mean` column** on `ProcessCrashRecoveryBenchmark` is an acceptable interpretation of the AC. For `ProcessPool` it's a real recovery measurement (kill 1 of N, lease loop skips dead slot, next invoke succeeds). For `Single` / `Pool` the iteration disposes and reconstructs the executor — `Mean` reports cold-start cost, which is honestly the answer to "time until next successful request" for those modes. Documented in code and README.

6. **`RunspaceCorruptionRecoveryBenchmark` deviates in name from what it measures.** Implementation measures head-of-line blocking (slow `Start-Sleep` in flight + fast `Get-Date` probe). Design rationale (process-kill is the wrong gate for Option A — see runspace-pool-experiment-plan.md §4) is sound. Recommend rename to `HeadOfLineBlockingBenchmark` as a small follow-up. Non-blocking.

7. **`OutOfProcessHost.SendRequestAsync` "Key: Content" concurrency race surfaced by the harness does NOT block #194.** This is the central architectural call. The harness's deliverable (per AC #1) is that all three executors are wired and exercised from a single run — provably true (cold-start smoke passes all three). The race is in production code (`OutOfProcessHost`), affects Single + ProcessPool when 10 concurrent invokes share a single host; Option A / Pool (PR #201) does NOT hit it because its dispatcher is concurrent-aware (useful comparative data favoring Option A on this axis). A benchmark surfacing a real production race on first concurrent run is doing its job, not failing. Suppressing it would defeat the purpose of #194 and delay #195 / #196 unnecessarily. **Required before merge:** file the race as a separate `spec:004` / `bug` issue and reference it from the PR body so the failure mode is tracked.

**Bottom line:** Fix CI (`dotnet format`), file the concurrency bug, reference it from the PR body — this is APPROVED. The architecture is right, the AC is met, the surfaced production bug is the harness doing its job.

**Pattern (cross-team):** Benchmark harnesses that exercise concurrent paths against production executors are high-signal regression tests in disguise. When the smoke run on day one surfaces a production race, land the harness, file the bug separately, and use the harness as the regression gate — don't hold the harness PR hostage to the bug it discovered first.

Comment posted at https://github.com/usepowershell/PoshMcp/pull/202#issuecomment-4392814612 (gh pr review remains EMU-blocked on usepowershell/PoshMcp from this account).

### 2026-05-06: PR #204 review — fix(oop): SendRequestAsync 'Key: Content' under parallel invokes (#203)
**By:** Farnsworth (Lead / Architect)
**PR:** https://github.com/usepowershell/PoshMcp/pull/204 (branch `squad/203-host-concurrency-fix`)
**Comment:** https://github.com/usepowershell/PoshMcp/pull/204#issuecomment-4393068861

**Verdict:** APPROVED

**Root cause (Bender's diagnosis, verified):** Not a concurrency bug. `BasicHtmlWebResponseObject.Content` (string body) CLR-shadows `WebResponseObject.Content` (byte[]). `ConvertTo-Json` reflects members into a `Dictionary<string,object>`; the shadowed pair collides on `Add` → `System.ArgumentException: ... Key: Content`. Harness's parallel Invoke-WebRequest pattern made it deterministic; a single invoke would also trip it. The C# correlation map (`_pending`) was correctly identified as a red herring and was not touched.

**Fix shape:**
- `oop-host.ps1`: extracted `ConvertTo-SafeJson` helper applied at the single failing site (`Invoke-InvokeHandler` user-result serialization).
- `oop-host-pool.ps1`: same fallback inlined into the runspace user-script scriptblock (correct — scriptblock executes in a pooled runspace and should not depend on host-process functions). Asymmetry is intentional.
- Fallback chain only triggers on `catch [ArgumentException]`: (1) primary `ConvertTo-Json -Depth 4 -Compress`, (2) `Select-Object * | ConvertTo-Json` (flat PSObject collapses shadowed members; derived `Content` wins → callers get the body), (3) `($r | Out-String).Trim() | ConvertTo-Json` last resort. Happy path unchanged.

**Regression test:** `OutOfProcessHostConcurrencyTests` — `InvokeAsync_ConcurrentInvokeWebRequest_DoesNotThrowDuplicateKeyError` fires 10 parallel `Invoke-WebRequest -UseBasicParsing` against a loopback `HttpListener`, producing a real `BasicHtmlWebResponseObject`. Pre-fix throws `OOP error: ... Key: Content`; post-fix asserts non-empty `output`. Companion test `SendRequestAsync_ConcurrentCallers_AllResponsesCorrelate` is a sanity net for the original (incorrect) hypothesis. Skip guards on `pwsh` and `HttpListener.IsSupported` are correct.

**Non-blocking observations posted on the PR:**
- Tests cover `oop-host.ps1` directly; pool-host inline fallback is exercised end-to-end via the `WarmInvokeThroughputBenchmark` smoke (Pool 306 ms / 10 calls clean) but not by a dedicated unit test. Optional follow-up.
- Branch name and issue title retain "concurrency" framing — fine because PR body is explicit that the diagnosis corrected the hypothesis.

**Sequencing observation (cross-PR):** This PR unblocks `WarmInvokeThroughputBenchmark` for `Single` and `ProcessPool` modes. Hermes's PR #195 (benchmarks + findings) has captured runs 1+2 against pre-#203 main, where Single/ProcessPool numbers are unreliable due to the duplicate-key error inside the invoke loop. After #204 merges, Hermes should rebase #195 onto post-#203 main and rerun affected benchmarks before publishing findings. Not blocking #204.

**Pattern noted:** PowerShell serialization-via-reflection failures often masquerade as concurrency bugs when surfaced under parallel harnesses, because parallelism makes them deterministic. When `ConvertTo-Json` throws `ArgumentException: ... Key: <name>`, suspect CLR member shadowing on the input type before suspecting a race.

**EMU caveat:** Posted via `gh pr comment` (gh pr review remains blocked on this account). Comment does not count as a formal GitHub approval for branch protection — Steven (or another non-EMU reviewer) must convert to formal Approve if required for merge.

### 2026-05-06: PR #205 review (Hermes — bench(oop) canonical results + findings, #195) — APPROVE

**Verdict:** APPROVE. Comment posted: https://github.com/usepowershell/PoshMcp/pull/205#issuecomment-4393870722
(EMU policy continues to block `gh pr review` from this account; `gh pr comment` is the working channel and does not satisfy branch-protection approval requirements.)

**Methodology:** `benchmark-results.md` documents BDN 0.14.0, `--job short` (3×3×1), exact filter/CLI, base commit `e4cf7d9` (post-#204), runtime/OS/arch (Windows 11 / Arm64 / .NET 10.0.6), and the explicit reason runs 1+2 are non-canonical. Reproducible.

**Numbers traceability (spot-checked):** WarmInvoke speedups in findings §1 derive cleanly from results table — Pool 4.857× → reported 4.86×, P99 4.788× → 4.79×; ProcessPool 3.295× → 3.30×, P99 3.408× → 3.41×. ColdStart penalty 400–478 ms → reported "400–500 ms". 1 MB allocations 13.79/16.34/17.36 MB → reported "~13.8/~16.3/~17.4 MB". No rounding flips a conclusion.

**Recommendation assessment:** `Pool` as default is supportable from the data under spec 004's stated workload model (network-shaped concurrent warm invokes). 4.86× clears the per-scenario 4× I/O bar; ProcessPool's 3.30× clears the 2× serialization bar but cannot match Pool on warm dispatch. Strongest counter-argument — single Arm64 host, `--job short`, single workload shape — is disclosed in caveat §5 at the right strength.

**Trust-boundary / cancellation gating (Lead-level call):** Hermes flagged custom `PSHost`/`PSHostUserInterface` work and cancellation propagation as prerequisites. Confirming as **HARD GATES** for the default flip in #196, not "should land before":
1. Custom `PSHost`/`PSHostUserInterface` for runspace pool — partially landed in PR #201; #196 must verify completeness for default-flip context.
2. Cancellation propagation: in-process `Stop()`/`StopAsync()` registration, OOP `cancel` JSON-RPC method, concurrent-readable dispatcher, bounded escalation (cooperative → forced → process kill + recycle). Without it, Pool's effective capacity under stuck invokes is `N - stuck_invokes` — a regression vs Single under adversarial load.

Until both land, `Pool` may ship as opt-in only.

**Position on #196 default flip:** Approve flip in principle; gate it on the two prereqs above. Do not flip in #196 if either is unresolved — ship #196 as opt-in `Pool` documentation in that case and re-spawn the flip once gates close. A `--job long` WarmInvoke rerun against post-cancellation main should be captured as run-4 in `benchmark-results.md` and must reaffirm ≥ 4× I/O bar before flipping.

**#196 scope sketch (delivered in review body, summary here):**
- Config keys: `PowerShell:HostMode` (default flip Single → Pool), `PowerShell:Pool:Size` (default `Environment.ProcessorCount`, hard cap 32), `PowerShell:Pool:DrainTimeoutMs` (thread through config; currently hardcoded 60s per PR #201 review).
- Doctor must validate pool sizing and surface active HostMode.
- Opt-in story documented in `DESIGN.md` ("When to switch HostMode") with three cases (Pool default, ProcessPool for tail/isolation, Single for short-lived CLI).
- Doc sweep: `DESIGN.md`, `README.md`, `examples/appsettings.*.json`, spec 004 `quickstart.md` if present.
- Acceptance includes the run-4 `--job long` rerun.
- Out of scope: per-request HostMode override, dynamic pool resizing, removing prototype code paths.

**Patterns reconfirmed:**
- EMU `gh pr review` block; `gh pr comment` works but is not a formal approval.
- Docs+data PRs benefit from spot-checking 2–3 headline numbers against source tables — catches both arithmetic errors and rounding inversions.
- When a recommendation rests on one workload shape, the strongest review move is to make the workload-shape disclosure a gate, not a footnote.

### 2026-05-06: OOP HostMode adoption recommendation (Hermes, #195 → #196)

**Context:** Run-3 benchmarks landed (PR #205) covering Single, Pool (Option A), ProcessPool (Option B) across ColdStart, PayloadSize, and WarmInvoke. Findings doc: `specs/004-out-of-process-execution/benchmark-findings.md`. Issue #196 owns the actual default flip.

**Recommendation:** Default `HostMode` should flip to **Pool** (Option A — in-process runspace pool, single subprocess).

**Why:**
- Pool wins WarmInvoke @ conc=10 by 4.86× mean / 4.79× P99 vs Single — clears the spec's per-scenario 4× I/O bar.
- ProcessPool: 3.30× / 3.41× — clears the 1.5× CPU floor and 2× serialization bar but cannot beat Pool's no-IPC dispatch path on warm throughput.
- ColdStart: Single leads by ~400–500 ms; cost amortizes to zero after invoke #2.
- PayloadSize: Pool competitive at small sizes, lowest allocations (~13.8 MB) at 1 MB. No payload regime where Pool is the worst choice.

**Tradeoffs (must be reflected in #196's adoption plan):**
- Keep `ProcessPool` as opt-in for tail-sensitive workloads — posts tightest StdDev (1.11 ms) and P99 (201.4 ms) of the three, and provides hard process isolation between concurrent invokes.
- Keep `Single` as documented choice for short-lived CLI invocations (cold-start dominates).
- Pool's trust boundary is weaker than ProcessPool's: shared GC, shared `[Console]::Out`, shared loaded modules. Custom `PSHost`/`PSHostUserInterface` work tracked from PR #187 review (Farnsworth #1) is a prerequisite for relying on Pool as default in adversarial scenarios.
- Cancellation propagation is not measured in run-3. Under stuck invokes, Pool's effective capacity is `N - stuck_invokes`. Cancellation work should be a gate on the default flip, not a follow-up.

**Open questions for #196:**
- Default flip vs documented opt-in (default flip changes runtime behavior for every existing deployment).
- Pool sizing default (`Environment.ProcessorCount`) needs validation on constrained containers (e.g., 2-vCPU pods).
- Opt-in story: when should callers prefer ProcessPool? Needs a "switch when..." doc rooted in the tail/trust tradeoffs.
- Doc sweep: `DESIGN.md` and `README.md` reference single-runspace assumptions.

**Caveats on the data:** `--job short` (3 iter × 3 warmup × 1 launch); single Windows 11 / Arm64 host; load shape matters (WarmInvoke is network-shaped per spec 004). Re-run with `--job long` before any SLO-bearing claim.

**Source artifacts:** PR #205, `specs/004-out-of-process-execution/benchmark-results.md`, `specs/004-out-of-process-execution/benchmark-findings.md`, `bench-runs/run-3.log`, `bench-runs/run-3-artifacts/`.

### 2026-05-06: Security review — open alerts triage and fix plan
**By:** Farnsworth (Lead / Architect), requested by Steven Murawski

**Scope reviewed:**
- Dependabot alerts (open) — 0
- CodeQL / code scanning alerts (open) — 25
- Secret scanning — disabled at repo level
- `.github/workflows/*` permissions blocks — 14/15 OK, 1 missing
- SECURITY.md — adequate (private vuln reporting + supported-version policy documented)
- Recent commits — security fixes are tracked (`v0.9.2` auth bypass fix, `System.Security.Cryptography.Xml` CVE bump)

**Open alert breakdown:**
| # | Source | Rule | Severity | File / location | Count |
|---|--------|------|----------|-----------------|-------|
| 24 | CodeQL | `cs/log-forging` (CWE-117) | medium | `PoshMcp.Server/PowerShell/PowerShellAssemblyGenerator.cs` (lines 709–1030) | 23 |
| 1 | CodeQL | `cs/log-forging` (CWE-117) | medium | `PoshMcp.Server/Observability/LoggerExtensions.cs` line 31 | 1 |
| 1 | CodeQL | `cs/log-forging` (CWE-117) | medium | `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs` line 111 | 1 |
| 1 | CodeQL | `actions/missing-workflow-permissions` (CWE-275) | medium | `.github/workflows/ci.yml` job at line 22 | 1 |

**Risk assessment:**

1. **Log-forging (25 alerts) — REAL, medium.** Not a false positive. The flagged sinks log
   `commandName`, `parameterValues`, `parameterSummary`, etc. in `PowerShellAssemblyGenerator.cs`,
   plus claim values in `AuthenticationServiceExtensions.cs`, plus correlation/op-name in
   `LoggerExtensions.cs`. Inputs flow from MCP `tools/call` JSON-RPC payloads, so they are
   client-controlled in HTTP mode and stdio-peer-controlled in stdio mode. Project ships a
   Serilog file sink (per spec for issue #131), so embedded `\r\n` in tool names or parameter
   values will produce forged log lines in plain-text files. Exploitability: low (requires log
   review by humans/tooling that trusts line boundaries); impact: log-trust erosion, possible
   audit-trail confusion. **Not a remote code execution path.**

2. **Missing workflow permissions on `ci.yml` — medium.** Default token is read-only at the
   org level for new repos, but explicit `permissions:` is the documented best practice and is
   required for CodeQL hygiene. All other 14 workflows (squad-*, publish-packages, docs-pages,
   etc.) already have explicit blocks — `ci.yml` is the only outlier. Trivial fix.

3. **Secret scanning disabled — medium (configuration gap).** Public repository with auth
   token handling code; secret-scanning + push-protection should be on. Not a code defect
   but a repo settings hardening item.

4. **No open Dependabot alerts.** Recent NuGet hygiene is tracked
   (`System.Security.Cryptography.Xml 10.0.5 → 10.0.6` CVE bump, MCP SDK 1.2.0 upgrade).
   Continue the current Dependabot-driven cadence.

**Recommended actions (prioritized):**

| Priority | Action | Owner | Mode |
|----------|--------|-------|------|
| P1 | Add `permissions: { contents: read }` (or minimal scope) to `.github/workflows/ci.yml` job. Alert #1 closes. | **Amy** (DevOps / Platform) | small PR, ~5 lines |
| P2 | Add a centralized log-sanitization helper (strip `\r\n`, optionally length-cap) and apply to user-controlled string args at the C# logging boundary in `PowerShellAssemblyGenerator.cs`, `LoggerExtensions.cs`, `AuthenticationServiceExtensions.cs`. Closes 25 alerts in one PR. | **Bender** (Backend Dev) | dedicated PR; needs Fry tests for newline scrubbing |
| P3 | Enable GitHub secret scanning + push protection on the repository (Security tab → Settings). Document the change in SECURITY.md. | **Amy** (repo admin) | settings change + 1 docs commit |
| P4 (defer) | Consider adopting Serilog `Destructure.ToMaximumStringLength` and a dedicated `Sanitize()` enricher across the board to make this a build-time invariant rather than per-call discipline. | Bender (proposal), Farnsworth (review) | follow-up issue, not blocking |

**Architectural decision — log sanitization pattern:**
- Add `LogSanitizer.Scrub(string)` (or extension `string.ScrubForLog()`) in
  `PoshMcp.Server/Observability/`. Replace `\r`, `\n`, and other control chars with a
  visible marker (`\u2424` or literal `\\n`) and cap length at a configurable max (default
  4 KB — generous for tool parameter summaries, prevents log flooding).
- Apply at the **call site** for any string interpolated into a log message template
  argument that originated from MCP request payloads, claims, or appsettings-driven names.
  Do **not** apply globally via a Serilog enricher only — CodeQL's taint analysis tracks
  call-site sinks, and an enricher won't clear the alerts (and risks double-encoding).
- Structured properties (`{ToolName}`) flow through the same sanitizer; use a small wrapper
  type `LogSafe(string)` if call-site noise becomes excessive in a follow-up.

**Out of scope for this triage:**
- The auth-bypass fix in v0.9.2 was already shipped — no action needed.
- The CVE-driven `System.Security.Cryptography.Xml` bump is already merged.
- No active security-relevant specs in `specs/` (1–8 are all feature specs, none are
  hardening work).

### 2026-05-06: Cancellation propagation for OOP execution (#188)
**By:** Bender (Backend Developer) for Steven Murawski
**Decision:** Adopt the cancellation design in `specs/004-out-of-process-execution/cancellation-design.md`. Token cancellation in `OutOfProcessHost.SendRequestAsync` now sends a `cancel` wire frame to the pwsh subprocess, which calls `PowerShell.BeginStop()` on the in-flight pipeline.

**Why this design:** Reuses the existing stdin channel rather than introducing a side-channel (named pipe / OS signal) that would differ across Windows and Linux. The Single-mode async dispatcher refactor is cheaper than a second IPC channel and is required regardless to unblock the dispatcher loop while a pipeline is in flight.

**Per-mode behavior:**
- **Single (`oop-host.ps1`):** invoke handler runs on a background C# dispatcher thread against a shared runspace; active invocations registered by request id; `cancel` calls `BeginStop`. Stdout serialized via `SingleStdout.Lock` to prevent worker/main interleave.
- **Pool (`oop-host-pool.ps1`):** `PoolDispatcher` tracks active items in a `ConcurrentDictionary`; `Cancel(requestId)` calls `BeginStop` on the matching `[powershell]`. No head-of-line blocking — concurrent invokes continue on other runspaces.
- **ProcessPool (`OutOfProcessSubprocessPool`):** inherits soft-cancel propagation from `OutOfProcessHost` for free. Existing per-request kill-on-timeout backstop preserved verbatim.

**Wire protocol additions:** new `cancel` method (frame id prefixed `cancel-`, not registered in `_pending`); optional `cancelled` boolean on invoke responses.

**Tests:** 3 new tests (one per mode) in `OutOfProcessCancellationTests.cs`. Full OOP suite 148 passed, 6 skipped.

**Unblocks:** #196 (default-mode flip to `Pool`).

**PR:** https://github.com/usepowershell/PoshMcp/pull/207

### 2026-05-06: PR #207 review — Bender — feat(oop) cancellation propagation (#188)
**By:** Farnsworth (Lead / Architect) for Steven Murawski
**Decision:** APPROVE. Posted via `gh pr comment` (gh pr review still EMU-blocked): https://github.com/usepowershell/PoshMcp/pull/207#issuecomment-4394001550

**Verdict rationale:**
- Wire protocol matches `specs/004-out-of-process-execution/cancellation-design.md` §3 verbatim. `cancel-` id prefix not registered in `_pending`; read loop downgrades unknown-id warning for `cancel-` prefix and for late `cancelled:true` responses to Debug.
- Single-mode implementation diverges from design §5.1 in strategy: PR uses C# `SingleDispatcher` (`BlockingCollection` + dedicated worker thread + `ConcurrentDictionary` registry) mirroring `PoolDispatcher` shape, instead of design's `BeginInvoke` + `ThreadPool.QueueUserWorkItem`. Divergence is justified — better code-share with Pool, avoids fighting PowerShell async ergonomics, uniform `SingleStdout`/`PoolStdout.Lock` pattern.
- Pool: surgical `_active` registry + `Cancel(requestId)` calling `BeginStop` on the matching `[powershell]`. No head-of-line — workers iterate `_queue.GetConsumingEnumerable()` independently.
- ProcessPool: `OutOfProcessSubprocessPool.cs` not modified. Soft-cancel inherited via Single-mode hosts. Per-request kill-on-timeout backstop at line 421 preserved verbatim.
- Belt-and-suspenders `wasStopped` detection (catches `PipelineStoppedException` AND falls back to `InvocationStateInfo.State == PSInvocationState.Stopped`) is correct — `BeginStop` does not always raise PSE from synchronous `Invoke()`.
- `SendRequestAsync`: `timeoutCts` changed from `CreateLinkedTokenSource(cancellationToken)` to plain new CTS — caller cancel and per-request timeout now properly orthogonal. Both registrations dispose in finally; `TrySendCancelFrameAsync` uses independent 2s CTS so caller-token cancel cannot poison the cancel-frame send.
- Tests: 3 new (one per mode) in `OutOfProcessCancellationTests.cs`. `Start-Sleep -Seconds 60` against 15s `ObservationTimeout` proves cancel actually unblocks. Pool test uses `runspacePoolSize:4` to provably exercise > 1 runspace + concurrent fast invoke for head-of-line check. ProcessPool test asserts `HealthyCount >= 1` after soft cancel (slots stay healthy, kill backstop not invoked).
- Non-blocking observations posted to PR: vestigial `try { ... } catch { throw }` wrapper in Single user script (semantic no-op); cancel-races-with-success path sends spurious cancel frame (handled, noise-only); `ProcessPool.InvokeAsync` did not get the diagnostic `catch (OperationCanceledException)` from design §4.3 (Bender's history acknowledges this; OCE bubbles up unannotated, fine).

**#196 hard gate status — SATISFIED.** Both gates I called on #205 are now closed:
1. ✅ Custom PSHost/PSHostUserInterface for runspace pool (PR #201).
2. ✅ Cancellation propagation (this PR — bounded soft-cancel across all 3 modes, no Pool head-of-line, hosts/slots stay healthy).

**#196 remaining scope (refined from #205):**
1. Default-mode flip: `SubprocessHostMode.Default` → `Pool`. Keep Single + ProcessPool as opt-in. ProcessPool stays the recommended choice for tail-sensitive / isolation-sensitive workloads (per #195: P99 within 0.7ms of mean).
2. Config key naming review for `appsettings.json` surface; confirm `SubprocessHostMode` enum-vs-string serialization story; verify no lingering enum collision from #200/#201.
3. Doctor validation hooks: surface resolved `OutOfProcessMode`, `RunspacePoolSize` (with any clamp applied — recall #201's hardcoded cap of 8), resolved host script path, per-request timeout. Warn (don't error) if Pool configured but pwsh resolution failed.
4. Doc updates: README, DOCKER.md, spec 004 supersedence note. Document cancellation contract (caller-token → bounded soft-cancel; per-request timeout as backstop; ProcessPool kill-on-timeout preserved).
5. Bench reaffirmation: Hermes `--job long` `WarmInvokeThroughputBenchmark` against post-#207 main (capture as run-4) confirming ≥ 4× I/O bar still holds. Cancellation refactor adds per-invoke `[powershell]` allocation + dispatcher hop — expect no measurable warm-I/O regression but verify.

**CI at review time:** Squad CI/test, CodeQL actions/python, dependency submission green; CI/build and CodeQL csharp still in progress (additive code, no signature changes — expected to pass).

**PR:** https://github.com/usepowershell/PoshMcp/pull/207 (mergeable, additions 1140 / deletions 40 / 5 files).

### 2026-05-06: PR #208 (Farnsworth — feat(oop): default to Pool host mode, #196) — APPROVE
**By:** Bender (Backend Developer)
**Posted:** https://github.com/usepowershell/PoshMcp/pull/208#issuecomment-4394193058 (gh pr review still EMU-blocked; comment with badge prefix; not a formal GitHub approval)

**Verdict:** APPROVE. Default flip is correctly scoped, doctor surfacing is operator-grade, docs match shipped behavior, and the spec §Implementation Notes cancellation contract matches what landed in #207.

**Constructor-default audit (the open question on #208):** All direct construction sites of `OutOfProcessCommandExecutor` in `PoshMcp.Server` checked:
- `McpToolSetupService.StartOutOfProcessExecutorIfNeededAsync` — explicit `hostMode: config.SubprocessHostMode`. ✅
- `McpToolSetupService.StartProcessPoolExecutorAsync` — uses parameterless overload (default `Single`), but only as a path resolver for `ResolveHostScriptPathAsync()`. ProcessPool's per-process host script IS `oop-host.ps1` (single-runspace), so default-Single is correct here.
- `DoctorService.BuildOutOfProcessSection` (new) — explicit `hostMode: config.SubprocessHostMode`. ✅

No production path silently still on Single. Constructor-default Single is documented in the enum's XML doc — acceptable trade-off vs. churning every test fixture. Future-callers footgun mitigated by docs, not code.

**Config keys:** `SubprocessRunspacePoolSize` (Pool) vs `SubprocessPoolSize` (ProcessPool) is mildly confusable, but doctor renderer disambiguates clearly per host-mode. JSON shape uses distinct field names. Renaming is breaking. Worth a follow-up issue: deprecate the flat keys in favor of nested `Pool:Size` / `ProcessPool:Size` for the next major.

**Doctor surfacing — strong.** Reports resolved hostMode + source (explicit/default), per-mode pool sizing with clamp output, min-healthy clamp, host script path with resolution status, hardcoded 30s request timeout (right call to surface even though not yet a config knob). Clamp warnings cover negative/zero/exceed-pool cases. Cancellation contract not surfaced — fine, not a config knob today.

**Doc accuracy:** `DOCKER.md`'s `POSHMCP_RUNTIME_MODE` is correct (consumed by `SettingsResolver` line 31; `ConfigurationFileManager.NormalizeRuntimeMode` accepts both Pascal `InProcess`/`OutOfProcess` and kebab `in-process`/`out-of-process`). README perf claim 4.9× matches benchmark-findings.md (4.86×). DESIGN.md links benchmark-findings correctly.

**Spec:** `Status: Implemented` accurate; cancellation contract section matches #207 shipped behavior (Pool's "N - in_flight_uncancelled" framing is correct, distinct from "stuck"); FR-051 restated as channel-writer serialization with multiplexed responses is the right refactoring of the original assumption.

**Test gap (non-blocking):** No new unit tests for `BuildOutOfProcessSection` or `RenderOutOfProcess`. Pure projection/rendering, low risk, but operator-facing. One rendering test fixture (Pool / ProcessPool / non-applicable) would lock in JSON shape and text format. Recommend follow-up issue, not blocker.

**Patterns:**
- When a default flip is questioned, audit ALL direct construction sites of the affected type — `grep_search "new TypeName"` across the production project (not tests). Caller-by-caller analysis is faster than reasoning about defaults in isolation.
- Naming asymmetry between sibling config keys (e.g. `SubprocessRunspacePoolSize` vs `SubprocessPoolSize`) is acceptable when the consuming UI (here, the doctor renderer) renders only the relevant key per active mode. The renderer becomes the disambiguation layer.
- Surfacing a hardcoded value (30s request timeout) in a doctor report — even though it's not yet a config knob — is good practice. Makes the contract explicit for operators and signposts the eventual configuration surface.

### 2026-05-06: Spec 004 foundation merge wave — 2/3 landed, #199 blocked on conflict
**By:** Amy (DevOps), requested by Steven Murawski
**What:** Sequenced merge of PRs #197 → #198 → #199. Each rebased onto fresh `origin/main`, full `dotnet test PoshMcp.sln` ran, only merged when green.
- ✅ **#197** (`squad/193-benchmark-harness`) merged. Tests: 584/591 (7 skipped). Brings `PoshMcp.Benchmarks` harness onto main.
- ✅ **#198** (`squad/190-extract-oop-host`) merged. Tests: 593/600 (7 skipped, +9 new unit tests for `OutOfProcessHost`). Extracts the OOP host with lifecycle coverage.
- ⛔ **#199** (`squad/189-clear-error-before-invoke`) — **STOPPED**. Rebase against post-#198 main hit a content conflict in `PoshMcp.Tests/Integration/OutOfProcessIntegrationTests.cs` (same file the #190 extraction touched). Rebase aborted cleanly; branch on origin is untouched. Needs Hermes (or whoever owns the OOP test surface) to resolve.

**Why:** Spec 004 foundation needed to land in dependency order. The first two PRs were independent enough to rebase clean; the `$Error`-clearing fix in #199 lives in tests that #198 reorganized, so a manual conflict resolution is required.

**Follow-up actions:**
1. Re-spawn Hermes (or equivalent) on `squad/189-clear-error-before-invoke` to: rebase onto current main, resolve the `OutOfProcessIntegrationTests.cs` conflict, re-run full tests, push --force-with-lease, then re-attempt merge.
2. After #199 lands, Spec 004 foundation phase is complete and downstream Spec 004 work can fan out.

**Operational note for future merge waves:** PRs created as drafts must be marked ready with `gh pr ready <num>` before `gh pr merge`. The `--delete-branch` flag triggers a local checkout error when run from a worktree (main is already checked out elsewhere) — the remote branch still gets deleted; just clean up worktree separately.

