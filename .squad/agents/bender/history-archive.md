# Bender Work History - Archive

## Archived Snapshot - 2026-04-14T23:57:17Z

Archived because history.md exceeded the 15 KB summarization threshold.

# Bender History

# Bender Work History

## Project Context

**Project:** PoshMcp - Model Context Protocol (MCP) server for PowerShell
**Tech Stack:** .NET 8, C#, PowerShell SDK, OpenTelemetry, ASP.NET Core, xUnit
**Primary User:** Steven Murawski

**Key Files:**
- `PoshMcp.Server/Program.cs` - Main entry point, DI configuration
- `PoshMcp.Server/McpToolFactoryV2.cs` - Tool factory and discovery
- `PoshMcp.Server/PowerShell/` - PowerShell integration layer
- `PoshMcp.Server/Metrics/McpMetrics.cs` - OpenTelemetry metrics

## Learnings

### PR #96 Fix: Caching DiagnoseMissingCommands Results (Double-Execution Pattern)

**Context:** PR #96 (issue #91, doctor command resolution) was rejected by Farnsworth because `DiagnoseMissingCommands` was called twice when doctor outputs JSON — once in `RunDoctorAsync` (lines ~1129) for the text path, then again inside `BuildDoctorJson` (lines ~1252) for the JSON path. Each call spins up an isolated PowerShell runspace (`IsolatedPowerShellRunspace`) and runs `Get-Command`/`Import-Module` per missing command — expensive and redundant.

**Key Files:**
- `PoshMcp.Server/Program.cs` — `RunDoctorAsync` (~line 1108), `BuildDoctorJson` (~line 1224), `DiagnoseMissingCommands` (~line 1356), `ConfiguredFunctionStatus` record (~line 1566)

**Fix Pattern:**
1. Added optional `List<ConfiguredFunctionStatus>? precomputedFunctionStatus = null` parameter to `BuildDoctorJson`
2. Added guard in `BuildDoctorJson`: only calls `DiagnoseMissingCommands` if `ResolutionReason` is not already populated (`configuredFunctionStatus.All(s => s.Found || s.ResolutionReason is null)`)
3. Changed `ConfiguredFunctionStatus` from `private` to `internal` to satisfy C# accessibility rules (method is `internal static`, parameter type must be at least as accessible)
4. In `RunDoctorAsync`, when format is JSON, passes already-computed `configuredFunctionStatus` to `BuildDoctorJson` via `precomputedFunctionStatus:` named argument — skipping the redundant runspace creation entirely

**The Double-Execution Pattern:**
- `RunDoctorAsync` computed diagnostics, merged into `configuredFunctionStatus`
- Then called `BuildDoctorJson` with only `config` and `tools`
- `BuildDoctorJson` rebuilt `configuredFunctionStatus` from scratch and called `DiagnoseMissingCommands` again
- Fix: pass the pre-computed result and guard against re-execution

**Lesson:** When a function computes expensive data and then delegates to a sub-function that recomputes the same data, pass the result as an optional parameter with a guard. This pattern avoids redundant work while maintaining backward compatibility for callers that don't pre-compute.

---

### 2026-03-27: Removed Duplicate Code in Program.cs

**Context:** Farnsworth's Phase 1 review identified duplicate code at lines 157-160 in `PoshMcp.Web/Program.cs`. The duplicate block included `app.MapMcp()` and `app.Run()` calls that were unreachable due to the blocking nature of the first `app.Run()` call.

**Fix Applied:** Removed unreachable duplicate code:
- Lines 157-160: Duplicate `app.MapMcp()` and `app.Run()` calls
- Impact: No functional change since code was unreachable
- Result: Cleaner codebase, follows DRY principle

**Key Insight:** `app.Run()` is a blocking call in ASP.NET Core - any code after it in the same method is unreachable. This is easy to miss during development but caught by code review.

**Files Modified:**
- [PoshMcp.Web/Program.cs](c:\Users\stmuraws\source\usepowershell\poshmcp\PoshMcp.Web\Program.cs)

---

### 2026-03-27: Cross-Team Learnings from Phase 1 Review

**Context:** Phase 1 code review and fixes completed. Multiple agents contributed fixes for issues identified by Farnsworth.

**From Farnsworth's Code Review Process:**
- Architectural review provides structural quality assessment beyond testing
- Scoring rubric (Architecture/Quality/Standards/Integration) gives clear signal
- Separating critical vs. non-blocking issues enables proper prioritization
- Even "minor" issues like duplicate code worth addressing for maintainability
- Specific file/line references with explanation accelerates fix implementation

**From Amy's Critical Fixes:**
- Performance issues not always visible in functional tests (LoggerExtensions scope creation)
- Documentation warnings are legitimate middle ground between breaking changes and API misuse
- Explicit timeout enforcement (Task.WaitAsync) more reliable than relying on framework defaults
- Trade-offs between convenience and performance are design decisions, not just implementation
- XML documentation shows in IDE - effective mechanism for performance guidance

**ASP.NET Core Patterns Reinforced:**
- app.Run() is blocking and starts web server (should be last line in Program.cs)
- Duplicate endpoint mapping indicates possible merge conflict or copy-paste error
- Static analysis could catch unreachable code patterns automatically
- Integration tests passing after dead code removal confirms no functional dependency

**Code Quality Standards:**
- Unreachable code adds confusion even if not functional
- DRY principle applies even to dead code (maintainability matters)
- Code review catches issues that static analysis might miss

---

### 2026-04-11: OOP Phase 1 — Created stub types for out-of-process execution

**Context:** Issue #55. The codebase had references to `RuntimeMode`, `ICommandExecutor`, `RemoteToolSchema`, `OutOfProcessCommandExecutor`, and `OutOfProcessToolAssemblyGenerator` in Program.cs and McpToolFactoryV2.cs, but the types didn't exist yet — build was broken.

**Files Created:**
- `PoshMcp.Server/PowerShell/OutOfProcess/RuntimeMode.cs` — Enum (InProcess, OutOfProcess, Unsupported)
- `PoshMcp.Server/PowerShell/OutOfProcess/ICommandExecutor.cs` — Interface (StartAsync, DiscoverCommandsAsync, InvokeAsync)
- `PoshMcp.Server/PowerShell/OutOfProcess/RemoteToolSchema.cs` — DTOs (RemoteToolSchema + RemoteParameterSchema)
- `PoshMcp.Server/PowerShell/OutOfProcess/OutOfProcessCommandExecutor.cs` — Stub impl, all methods throw NotImplementedException
- `PoshMcp.Server/PowerShell/OutOfProcess/OutOfProcessToolAssemblyGenerator.cs` — Stub impl, throws NotImplementedException (ClearCache is no-op)

**Files Modified:**
- `PoshMcp.Server/PowerShell/PowerShellConfiguration.cs` — Added `using PoshMcp.Server.PowerShell.OutOfProcess;` (needed because ImplicitUsings=disable and RuntimeMode is in child namespace)

**Key Insight:** ImplicitUsings is disabled in this project. Every file needs explicit usings for System, System.Collections.Generic, System.Threading, etc. Child namespaces don't auto-resolve from parent namespace files.

**Result:** Build succeeds (0 errors), all 279 tests pass.
- Quick fixes demonstrate value of thorough review process

**Future Prevention:**
- Consider adding linter rule for unreachable code after blocking calls
- Code review checklist could include "last line is app.Run()" for web projects
- Static analysis integration in CI/CD could catch duplicate code patterns

**Phase 1 Completion:**
- All fixes applied (timeout enforcement, performance warnings, duplicate code)
- Test suite validates changes (13/13 passing)
- Phase 1 fully approved and production-ready
- Team coordination effective through review → fix → validate cycle

---

### 2026-04-08: Serialization migration web-failure batch logged

**Context:** Scribe recorded a new batch focused on PoshMcp.Web failures that appeared after the serialization migration. The spawn manifest assigned Bender to investigate and fix the failing web path.

**Shared Team Update:**
- Keep the web failure investigation anchored to serialization-related regressions in `PoshMcp.Web`
- Preserve enough context in future handoffs to distinguish assignment scope from verified outcomes

---

### 2026-04-11: OOP Phase 2 — Implemented OutOfProcessCommandExecutor subprocess lifecycle

**Context:** Issue #56. Replaced NotImplementedException stubs with full subprocess management in `OutOfProcessCommandExecutor.cs`.

**What was built:**
- `StartAsync()`: Locates `pwsh` via PATH/common locations, launches process with stdin/stdout/stderr redirection, starts background ndjson reader tasks, sends ping health check to confirm subprocess is alive.
- `SendRequestAsync<T>()`: Core ndjson request/response infrastructure using `ConcurrentDictionary<string, TaskCompletionSource<JsonElement>>`, `SemaphoreSlim` for serialized writes, GUID-based request IDs, configurable timeout via `CancellationToken` + `Task`.
- `ReadLoopAsync()`: Background stdout reader that parses ndjson lines, matches `id` field to `_pending` dictionary, completes or faults the `TaskCompletionSource`. Handles error responses from subprocess.
- `StderrLoopAsync()`: Background stderr reader that logs diagnostic output from oop-host.ps1.
- `DisposeAsync()`: Sends shutdown request, waits up to 5s for graceful exit, kills process tree if needed, completes all pending TCS with cancellation, disposes streams.
- `ResolvePwshPath()`: Static helper that searches PATH dirs then common install locations.
- `ResolveHostScriptPath()`: Static helper using `AppContext.BaseDirectory` then `AppDomain.CurrentDomain.BaseDirectory`.
- `DiscoverCommandsAsync` / `InvokeAsync`: Left as stubs throwing `NotImplementedException` with updated Phase 3/Phase 4 messages.

**Files Modified:**
- `PoshMcp.Server/PowerShell/OutOfProcess/OutOfProcessCommandExecutor.cs` — Full implementation
- `PoshMcp.Server/PoshMcp.csproj` — Added `<None Include="oop-host.ps1" CopyToOutputDirectory>` for script deployment
- `PoshMcp.Tests/Unit/OutOfProcess/OutOfProcessCommandExecutorTests.cs` — Updated tests: removed 2 old StartAsync stub tests, added Constructor_WithCustomTimeout, ResolvePwshPath_FindsPwshOnPath, StartAsync_ThenDisposeAsync_FullLifecycle

**Key Patterns:**
- System.Text.Json (not Newtonsoft) for ndjson protocol — `JsonSerializer.Serialize()` and `JsonDocument.Parse()`
- `ConcurrentDictionary` + `TaskCompletionSource` for async request/response matching
- `SemaphoreSlim(1,1)` for serialized stdin writes
- `CancellationTokenSource.CreateLinkedTokenSource` + `CancelAfter` for timeout
- `Process.EnableRaisingEvents` + `Exited` event for crash detection
- CA2024 compliance: using `ReadLineAsync()` null-return instead of `EndOfStream` in async loops

**Result:** Build: 0 errors. All 280 tests pass (52 OOP unit tests, up from 51).
- Team directive now requires `dotnet format` and `dotnet test` after code changes

### 2026-04-11: OOP Phase 3+4 — Wired DiscoverCommandsAsync and InvokeAsync

**Context:** Issues #58 and #59. Replaced the NotImplementedException stubs for `DiscoverCommandsAsync` and `InvokeAsync` with real implementations that communicate with the oop-host.ps1 subprocess.

**DiscoverCommandsAsync implementation:**
- Builds discover params from `PowerShellConfiguration` (modules, functionNames, includePatterns, excludePatterns)
- Calls `SendRequestAsync<JsonElement>("discover", params, ct)`
- Parses `result.commands` array into `List<RemoteToolSchema>` using `System.Text.Json` with `PropertyNameCaseInsensitive = true`
- Caches result in `_cachedSchemas` field — subsequent calls return cache without subprocess roundtrip
- Handles missing `commands` property gracefully (returns empty list with warning log)

**InvokeAsync implementation:**
- Builds invoke params `{ command, parameters }`
- Calls `SendRequestAsync<JsonElement>("invoke", params, ct)`
- Extracts `output` string from result
- Logs warning if `hadErrors` is true but still returns output (some commands produce both)

**Test updates:**
- Changed 4 tests from expecting `NotImplementedException` → `InvalidOperationException` (subprocess not running)

**Files Modified:**
- `PoshMcp.Server/PowerShell/OutOfProcess/OutOfProcessCommandExecutor.cs` — Added `_cachedSchemas` field, implemented both methods
- `PoshMcp.Tests/Unit/OutOfProcess/OutOfProcessCommandExecutorTests.cs` — Updated 4 tests

**Result:** Build: 0 errors. All 280 tests pass.

### 2026-04-08: Implemented dotnet tool packaging for PoshMcp.Server

**Context:** Task requested by Steven Murawski to make PoshMcp installable via `dotnet tool install`.

**Changes Made:**
- Added `<PackAsTool>true</PackAsTool>`, `<ToolCommandName>poshmcp</ToolCommandName>`, `<PackageId>poshmcp</PackageId>`, `<Version>0.1.0</Version>`, and full NuGet metadata properties to `PoshMcp.Server/PoshMcp.csproj`
- Created `.config/dotnet-tools.json` local tool manifest at the repo root (enables `dotnet tool install --local poshmcp`)
- No LICENSE file exists at repo root — noted but not created per task instructions

**Outcomes:**
- Build: `dotnet build PoshMcp.Server/PoshMcp.csproj` — succeeded
- Pack: `dotnet pack PoshMcp.Server/PoshMcp.csproj --no-build` — produced `PoshMcp.Server/bin/Release/poshmcp.0.1.0.nupkg` (~26 MB includes PowerShell SDK)

**Key Insight:** `--no-build` pack still triggers a `publish` step for tool packaging (expected for `PackAsTool=true`). Output appears under `bin/Release/` not `bin/Release/net10.0/`.

---

### 2026-07: MCP pipeline analysis for Get-Process large result set hang

**Context:** Investigated why `Get-Process` and similar cmdlets hang when called via the MCP server.

**Root Cause — three compounding layers:**

1. **Synchronous `ps.Invoke()` inside `ExecuteThreadSafeAsync` lambda** (primary): The lambda at `PowerShellAssemblyGenerator.cs:602` is not `async` — it calls `InvokePowerShellSafe` which calls `ps.Invoke()` synchronously, blocking the thread pool thread for the full command duration. The `SemaphoreSlim(1,1)` in `PowerShellRunspaceHolder` is held the entire time. An `InvokePowerShellSafeAsync` already exists at line 1048 using `Task.Run(() => ps.Invoke(), cancellationToken)` but is not wired into the main execution path.

2. **`Tee-Object` full-buffer**: The pipeline `Get-Process | Tee-Object -Variable LastCommandOutput` forces full buffering of all `Process` objects (holding live OS handles) before emitting any result.

3. **`GetSafeProperties` enumerates ~50 `Process` properties**: Properties like `Modules`, `MainModule`, `Threads`, and `Handle` call kernel APIs (`EnumProcessModules`) that can block indefinitely on system/protected processes. These are blocking stalls, not exceptions — they are not caught by the `try/catch` in `TryGetShallowPSPropertyValue`.

**No MCP response size limit exists** anywhere in the SDK or our code. The entire serialized JSON (~2 MB for a typical process list) is assembled in memory and written as one stdout line.

**Recommended fix order:**
- Phase 1: Result count cap with truncation hint before serialization step (1 day)
- Phase 2: Use `InvokePowerShellSafeAsync` with `async` lambda (2–3 days)
- Phase 3: Type-specific property shaping registry in `PowerShellObjectSerializer` (2–3 days)
- Phase 4: Per-function config schema in `PowerShellConfiguration` / `appsettings.json`

---

### 2026-04-09: Fixed JSON schema generation crash on unsupported CLR types

**Context:** Integration test `ServerWithExternalClient.ShouldExecutePowerShellCommand` was failing with `System.TimeoutException` because the server crashed during initialization when attempting to create tools for `Get-Process -InputObject <Process[]>` overloads.

**Root Cause:** When `McpServerTool.Create()` tried to build a JSON schema for the function's parameters, it introspected the `Process[]` parameter type and encountered `Encoding.Preamble` (a `ReadOnlySpan<byte>`). The JSON schema generator cannot serialize pointer types or ref structs, triggering an unhandled `InvalidOperationException` that crashed the entire server initialization.

**Fix Applied** (minimal, graceful degradation):
1. Changed `CreateSingleMcpTool()` return type to `McpServerTool?` (nullable)
2. Wrapped `McpServerTool.Create()` call in try-catch for `InvalidOperationException` with condition checking for "pointer type" or "ref struct" in message
3. On catch: log a warning that the method cannot be exposed, return `null`
4. Updated `CreateMcpToolsFromMethods()` to check `tool != null` before adding to list, properly counting failures

**Impact:**
- Problematic overloads skipped: `get_process_input_object`, `get_process_input_object_with_user_name`
- Other Get-Process variants still exposed: `get_process_name`, `get_process_id`, etc.
- Server starts successfully and responds to client within timeout window
- Test now passes (2/2 integration tests passing)

**Key Insight:** Schema generation failures on complex CLR types are not hard errors — graceful degradation (skip the overload, expose other variants) allows the server to bootstrap successfully while maintaining functionality for the majority of command variants.

**Files Modified:**
- `PoshMcp.Server/McpToolFactoryV2.cs` (lines 420–450 and 394–420)

**Testing:**
- `dotnet test --filter "ShouldExecutePowerShellCommand"` → Passed (2/2 tests, 8s duration)
- No changes to tool exposure strategy in `appsettings.json`
- Build: `dotnet build` → succeeded with 0 warnings

**Key config schema additions:**
- `PowerShellConfiguration.DefaultMaxResults` (int, default 50)
- `PowerShellConfiguration.FunctionLimits` (Dictionary<string, FunctionLimitConfiguration>)
- `FunctionLimitConfiguration.MaxResults` and `.SelectProperties`

**Key files for implementation:**
- `PowerShellAssemblyGenerator.cs` lines 602, 691, 772 — execution and serialization path
- `PowerShellRunspaceHolder.cs` — `SemaphoreSlim` gating
- `PowerShellObjectSerializer.cs` — `GetSafeProperties` / `TryGetShallowPSPropertyValue`
- `PowerShellConfiguration.cs` — config DTO to extend

---

### 2026-04-08: Web harness fix recorded for configuration-aligned no-build startup

**Context:** Completed the in-process web harness fix for the serialization migration follow-up.

**Key learnings:**
- The harness should reuse the active test build outputs instead of triggering a second app build during startup
- `dotnet run --no-build --configuration {Debug|Release}` needs to match the test run configuration to avoid Debug/Release drift
- File-lock failures during web integration startup were a harness issue separate from the serializer regression itself

---

### 2026-04-09: Phase 2 + Phase 2.5 implementation — conditional Tee-Object and runtime toggle

**Context:** Crash recovery dispatch. Resuming Phase 2 of Farnsworth's large-result-performance proposal after Phase 1 committed at 9823044.

**User Decisions Captured:**
- **Q2 (_MaxResults parameter):** YES — include result limiting parameter
- **Q4 (cache filtering):** Cache the FILTERED object, not the full object
- **Q5 (reset semantics):** Support null or "reset" to return to previously configured setting
- **Q6 (gating):** Do NOT gate `set-result-caching` behind `EnableDynamicReloadTools`

**Phase 2 Tasks:**
- Conditional Tee-Object implementation
- Per-function and global caching override support
- Runtime cache override state management

**Phase 2.5 Tasks:**
- Runtime toggle DI registration (`RuntimeCachingState`)
- `set-result-caching` MCP tool registration
- Resolution chain: runtime overrides → per-function config → global config

**Key Implementation Details:**
- `RuntimeCachingState.cs` for thread-safe override storage (ConcurrentDictionary + volatile)
- Resolution hierarchy: runtime overrides (global + per-function) > per-function config > global config
- Ephemeral state — no persistence across server restarts
- Immediate effect on next command execution
- Filtered object caching reduces memory footprint vs. full result cache

---

### 2026-04-09: Integration test process cleanup hardening (orphaned child process risk)

**Context:** Investigated long-running tests where integration fixtures launch `dotnet run` child processes for `PoshMcp.Server` and `PoshMcp.Web`.

**Root Cause Identified:**
- Fixture teardown called `Process.Kill()` without `entireProcessTree: true`, which can leave child app processes alive when the parent launcher exits.
- Startup failure paths in test server fixtures could throw after process start without guaranteed cleanup in all failure branches.

**Fix Applied:**
- Added shared `StopServerProcess()` helper in both `InProcessWebServer` and `InProcessMcpServer`.
- Switched to `Kill(entireProcessTree: true)` and always dispose/null the process handle.
- Ensured startup failure paths call `StopServerProcess()` before rethrowing.

**Verification:**
- Focused integration tests passed with no lingering `PoshMcp.Web.csproj` or `PoshMcp.csproj` processes after completion.
- This reduces process leak risk that can accumulate and slow subsequent test runs.

### 2026-04-09: Added CLI configuration management commands

**Context:** Implemented TODO items to add CLI-driven configuration creation and mutation flows in `PoshMcp.Server/Program.cs`.

**What shipped:**
- New `create-config` command creates default `appsettings.json` in current directory with optional `--force` overwrite
- New `update-config` command updates the active configuration file using the same path-resolution chain as `doctor`
- Update command supports function/module/include/exclude edits and `EnableDynamicReloadTools`
- Interactive advanced prompts now run when new functions are added, allowing per-function overrides for:
	- `EnableResultCaching`
	- `UseDefaultDisplayProperties`
	- `DefaultProperties`
- Added `--non-interactive` mode for automation workflows

**Key insight:** Reusing existing configuration resolution logic (`ResolveCommandSettingsAsync`) keeps CLI behavior consistent and avoids drift between diagnostics (`doctor`) and mutation commands.

### 2026-04-10: Recovery learnings for doctor tooling and harness parity

**Key learnings:**
- Shared diagnostics payload builders matter: CLI doctor output and MCP troubleshooting tools should stay on one `BuildDoctorJson(...)` path.
- Special built-in tools belong in the existing `Program.cs` registration seam rather than the PowerShell discovery path.
- Out-of-process recovery fixes were safest when they restored harness parity first: explicit config args, stderr capture, and compile-safe startup expectations.
- Avoid activating tests against a runtime mode the executable and shared harness cannot actually start yet.

### 2026-04-11: Cross-agent update — Out-of-process execution plan filed

**Context:** Farnsworth filed a comprehensive OOP execution plan at `specs/out-of-process-execution.md`.

**Key points for Bender:**
- Communication protocol is ndjson over stdin/stdout (not TCP as previously proposed on 2026-04-10)
- Phase 1 (stub types) is the immediate priority — fixes 13 build errors
- Phase 2 (subprocess lifecycle) and Phase 4 (command invocation) are the main backend implementation phases
- `oop-host.ps1` is the subprocess host script — handles discover/invoke/ping/shutdown via ndjson
- Crash recovery uses exponential backoff (3 retries in 5 min)
- RuntimeMode is server-wide in v1 (InProcess or OutOfProcess), no per-function routing



## Archived 2026-05-05 (history summarization, lines 201-547 of pre-summarization file)


**Learnings:**
- When extracting transport-specific startup paths, create dedicated host classes (e.g., `StdioServerHost`, `HttpServerHost`) instead of splitting across multiple utility files; it's clearer and easier to test
- Wrapper delegators in the original file minimize breaking changes to existing call sites (SetHandler lambdas, etc.)
- Private helper methods (e.g., `ConfigureJsonSerializerOptions`, `ConfigureApplicationInsights`) can live in the host classes without duplication if used by both transports; just make them private static per host

**Next Steps:**
- PR 4: Extract CliDefinition.cs (~250 lines) — all command/option declarations; build the RootCommand tree
- Final: Main() down to ~200 lines — just argument parsing and handler dispatch

---

## Recent Work (2026-04-20)

### Issue #170: Azure.Monitor.OpenTelemetry.AspNetCore Package
**Branch:** squad/170-azure-monitor-otel-package  
**Status:** Complete
**PR:** https://github.com/usepowershell/PoshMcp/pull/176

- **Task**: Add Azure.Monitor.OpenTelemetry.AspNetCore NuGet package reference to PoshMcp.Server
- **Implementation**: 
  - `dotnet add` installed v1.4.0 with full transitive dependency tree (Azure.Core, Azure.Monitor.OpenTelemetry.Exporter, OpenTelemetry.Instrumentation.Http, etc.)
  - Updated `PoshMcp.csproj` with new `<PackageReference>` entry
- **Validation**: `dotnet build Release` succeeded with 10 warnings (9 pre-existing CS8602 nullable, 1 pre-existing NU1510)
- **Outcome**: Committed and pushed; PR #176 opened for Spec 008 optional Application Insights telemetry export

**Files modified:**
- `PoshMcp.Server/PoshMcp.csproj` — added Azure.Monitor.OpenTelemetry.AspNetCore v1.4.0

**NOTE:** The csproj filename is `PoshMcp.csproj` NOT `PoshMcp.Server.csproj`. Manifest resource names use assembly name prefix, not namespace.

### Docker Build Arguments Extraction and Testing
**Branch:** background→sync  
**Status:** Complete

- **Task (Bender)**: Extracted `DockerRunner.BuildDockerBuildArgs` static method from `Program.cs` build handler
- **Implementation**: Created `PoshMcp.Server/Infrastructure/DockerRunner.cs` with reusable `BuildDockerBuildArgs(string projectPath)` method
- **Outcome**: Delegated build handler → DockerRunner; build passes without errors
- **Coordination**: Fry created comprehensive 11-test unit suite in `PoshMcp.Tests/Unit/DockerRunnerTests.cs`; all tests passing

**Files modified:**
- `PoshMcp.Server/Program.cs` — build handler simplified
- `PoshMcp.Server/Infrastructure/DockerRunner.cs` — new extraction
- Both agents coordinate on isolated, testable Docker build logic

## Recent Status (2026-07-30, PR #167 Review Nits — COMPLETE)

**Summary:** Addressed 3 Farnsworth review nits on PR #167. 520 tests pass, 0 failures. Pushed commit e440ab2.

## Spec 006 PR #167 Review Nits — commit e440ab2

**What changed:**
- **Fix 1** (`Program.cs`): Removed misleading `--json` flag mention from `get-configuration-troubleshooting` MCP tool description. The `--json` flag is for the CLI `doctor` command; the MCP tool always returns structured text. New description: `"...Always returns structured text output."`
- **Fix 2** (`Program.cs`): Added `POSHMCP_LOG_FILE` to `CollectEnvironmentVariables()` canonical list, positioned after `POSHMCP_LOG_LEVEL`. No column width change needed in `DoctorTextRenderer` (35-char column is sufficient).
- **Fix 3** (`Program.cs`): Corrected `POSHMCP_CONFIG` → `POSHMCP_CONFIGURATION` to match `SettingsResolver.cs` constant `ConfigurationEnvVar`. Also updated unit test assertion in `ProgramDoctorConfigCoverageTests.cs` (renamed method from `WithSevenExpectedKeys` to `WithExpectedKeys`).

**Key pattern:**
- When renaming env var keys, always grep tests for the old key name — they'll have hard-coded string assertions that need updating too.

---

## Recent Status (2026-07-29, Phase 8 — COMPLETE)

**Summary:** Spec 006 Phase 8 complete — dead code removed, `dotnet format` clean, 520 tests pass, PR #167 opened.

## Spec 006 Phase 8: Cleanup and Finalization (T024–T027) — commit ef27ef1

**What changed:**
- **T024**: Removed 5 dead methods/fields from `Program.cs`: `_sensitiveKeyPatterns`, `IsSensitiveKey`, `RedactSensitiveConfigValues`, `LoadFlatConfigSection`, `TryLoadResourcesAndPromptsDefinitions`. These were superseded by `DoctorReport.Build()` in Phase 3 and had zero call sites. `-31 lines`.
- **T025**: `dotnet format` applied, `--verify-no-changes` exits 0.
- **T026**: `dotnet test -c Release` → **520 passed, 0 failed, 7 skipped**.
- **T027**: PR #167 opened: https://github.com/usepowershell/PoshMcp/pull/167

**Key pattern:**
- After refactoring to a new model (e.g., `DoctorReport.Build()`), always grep ALL call sites for helper methods from the old path. Private helpers with zero external references are safe to delete.

## Spec 006 Phase 6: MCP Tool Schema Update (T017–T018) — commit 2ed1546

**What changed:**

### `Program.cs` — `CreateConfigurationTroubleshootingToolInstance` (T017)
- Updated `Description` for the `get-configuration-troubleshooting` MCP tool:
  - Old: `"Returns doctor-style configuration diagnostics for the running server"`
  - New: `"Returns doctor-style configuration diagnostics for the running server. Output includes runtime settings, environment variables, PowerShell info, configured functions, and MCP definitions. Outputs structured text by default; pass argument '--json' for machine-readable JSON."`

### `DoctorReport.cs` — `FunctionsToolsSection` (T018)
- Changed `ConfiguredFunctionsFound` from `List<string>` to `int` to match spec JSON shape (`"configuredFunctionsFound": 5`)
- Changed `ConfiguredFunctionsMissing` from `List<string>` to `int` to match spec JSON shape (`"configuredFunctionsMissing": 0`)
- Updated `ComputeStatus`: `ConfiguredFunctionsMissing.Count > 0` → `ConfiguredFunctionsMissing > 0`
- Updated `DoctorReport.Build`: `ConfiguredFunctionsFound = foundFunctions` → `ConfiguredFunctionsFound = foundFunctions.Count` and same for Missing

### `DoctorTextRenderer.cs`
- Updated `RenderFunctionsTools`: `ConfiguredFunctionsMissing.Count == 0` → `ConfiguredFunctionsMissing == 0`
- Updated count display: `ConfiguredFunctionsFound.Count` → `ConfiguredFunctionsFound`

**Why the schema fix:** The spec.md JSON Output Design shows `configuredFunctionsFound` and `configuredFunctionsMissing` as integer counts (e.g., `5` and `0`), not arrays of names. The full name details are already available in `configuredFunctionStatus` entries. Changed to integers to match the spec contract.

**Build:** 0 errors. Pre-existing warnings (NU1903, CS8602 in McpToolFactoryV2.cs) unchanged.

---

## Recent Status (2026-07-29, Phase 4)

**Summary:** Spec 006 Phase 4 complete — canonical env var list and renderer column width aligned to spec.

## Spec 006 Phase 4: Env Vars Section Population (T013–T014) — commit 2fc1b55

**What changed:**

### `Program.cs` — `CollectEnvironmentVariables()`
- Added 3 missing keys: `POSHMCP_FUNCTION_NAMES`, `POSHMCP_COMMAND_NAMES`, `DOTNET_ENVIRONMENT`
- Reordered to match canonical spec order: TRANSPORT → LOG_LEVEL → SESSION_MODE → RUNTIME_MODE → MCP_PATH → CONFIG → FUNCTION_NAMES → COMMAND_NAMES → ASPNETCORE_ENVIRONMENT → DOTNET_ENVIRONMENT
- All values resolved via `Environment.GetEnvironmentVariable(key)` (null if unset)

### `DoctorTextRenderer.cs` — `RenderEnvironmentVariables()`
- Changed key column width from `{key,-30}` to `{key,-35}` to match spec format

**Build:** 0 errors. All pre-existing warnings (NU1903, CS8602) unchanged.

**Canonical env var list (10 keys):**
```
POSHMCP_TRANSPORT
POSHMCP_LOG_LEVEL
POSHMCP_SESSION_MODE
POSHMCP_RUNTIME_MODE
POSHMCP_MCP_PATH
POSHMCP_CONFIG
POSHMCP_FUNCTION_NAMES
POSHMCP_COMMAND_NAMES
ASPNETCORE_ENVIRONMENT
DOTNET_ENVIRONMENT
```

---

**[Earlier history before 2026-04-21 archived to history-archive.md per Scribe threshold policy. Preserving last 90 days in main history.]**

## Recent Work (2026-04-23)

### CLI infra scaffolding with embedded deployment assets
**Status:** Complete

- Added a new `scaffold` CLI command in `Program.cs` with `--project-path|--path|-p` (default current directory), `--force`, and `--format text|json`.
- Implemented `InfrastructureScaffolder.ScaffoldAzureInfrastructureAsync` to extract embedded infrastructure assets into `infra/azure` under the target project.
- Embedded Azure deployment artifacts in `PoshMcp.csproj` (`deploy.ps1`, `validate.ps1`, `main.bicep`, `resources.bicep`, `parameters.json`, `parameters.local.json.template`) so scaffolding works from packaged tool output.
- Added `ProgramCliScaffoldCommandTests` covering successful scaffold and existing-file behavior without force.

**Key pattern:**
- For tool packaging scenarios, embed source artifacts in the server assembly and resolve resource names by suffix to avoid brittle fully-qualified manifest names.

## 2026-04-23 17:21 — appsettings → env var mapping (with Amy)

- Added \ConvertTo-McpServerEnvVars\ to deploy.ps1: walks known PowerShellConfiguration/Authentication keys,
  applies canonical POSHMCP_* names for RuntimeMode/SessionMode, falls through to __-separated names for the rest.
- Added \Resolve-McpAppSettingsFile\: CLI override first, then auto-discovers poshmcp.appsettings.json / appsettings.json in script dir.
- Added \-McpAppSettingsFile\ parameter to deploy.ps1 param block; distinct from \-AppSettingsFile\ (deploy-level settings).
- Skips: Logging, McpResources, secrets, file paths.
- Injects \xtraEnvVars\ into Bicep parameters JSON at deploy time.
- Key file: infrastructure/azure/deploy.ps1

## 2026-04-24 — Build flow defaults to remote GHCR base image

- Changed `poshmcp build` default behavior from local source-image build assumptions to custom-image layering with published base image.
- Added `--source-image` and `--source-tag` build options and defaulted source resolution to `ghcr.io/usepowershell/poshmcp/poshmcp:latest`.
- Updated default Dockerfile selection to `examples/Dockerfile.user` for `--type custom` (now default), while preserving `--type base` for local `Dockerfile` source builds.
- Updated `examples/Dockerfile.user` to support `BASE_IMAGE` and `INSTALL_PS_MODULES` build args so `--modules` remains effective in the new default flow.
- Added/updated tests in `PoshMcp.Tests/Unit/DockerRunnerTests.cs` and `PoshMcp.Tests/Unit/ProgramCliBuildCommandTests.cs` for build arg construction and option/help coverage.

## 2026-04-24 — Issue #169: update-config adds obsolete FunctionNames block

- Reproduced issue locally: running `update-config --runtime-mode out-of-process` against config with only `CommandNames` added an empty legacy `FunctionNames` array.
- Root cause in `ConfigurationFileManager.UpdateConfigurationFileAsync`: legacy function array was always created via `GetOrCreateArray(powerShellConfiguration, "FunctionNames")` even when no `--add-function/--remove-function` flags were used.
- Fix: only create/update `FunctionNames` when legacy function updates are explicitly requested or the property already exists.
- Added regression test in `ProgramCliConfigCommandsTests` to ensure runtime-mode updates do not introduce `FunctionNames` when absent.
- Validation:
  - `dotnet test PoshMcp.Tests/PoshMcp.Tests.csproj --filter "FullyQualifiedName~ProgramCliConfigCommandsTests"` => 16 passed.
  - `dotnet build PoshMcp.Server/PoshMcp.csproj` => build succeeded (existing warnings unchanged).

## 2026-04-24 — CommandOverrides rename with FunctionOverrides compatibility

- Updated configuration nomenclature from `FunctionOverrides` to `CommandOverrides` across runtime access, update-config advanced prompt writes, appsettings templates/examples, and user-facing docs.
- Added compatibility path in `PowerShellConfiguration`: legacy `FunctionOverrides` still binds and is merged via `GetEffectiveCommandOverrides()` while `CommandOverrides` takes precedence.
- Updated runtime consumers (`AuthorizationHelpers`, `PowerShellAssemblyGenerator`, `ConfigurationHealthCheck`) to resolve overrides through command-first helpers.
- Enhanced `update-config` advanced prompts to write `CommandOverrides` and migrate existing `FunctionOverrides` in-place when the command touches overrides.
- Added/updated focused tests:
  - `ProgramCliConfigCommandsTests`: assert `CommandOverrides` output and migration from legacy key.
  - `PerformanceConfigurationTests`: binding compatibility coverage for legacy and precedence behavior.
  - `ProgramTests` + `AuthorizationHelpersTests`: primary usage now points to `CommandOverrides`.
- Validation:
  - `dotnet build PoshMcp.Server/PoshMcp.csproj -p:UseSharedCompilation=false` => succeeded.
  - Targeted unit tests (`ProgramCliConfigCommandsTests`, `PerformanceConfigurationTests`, `AuthorizationHelpersTests`, `ProgramTests`) => 73 passed.

## Learnings

### MCP OAuth + Entra ID proxy (2026-05-01)

**Root cause pattern for "client opens /authorize on container app":**
When `ProtectedResource.AuthorizationServers` is empty or points to the container app itself,
and the container app has no `/.well-known/oauth-authorization-server`, MCP clients fall back
to treating the container app as the AS and derive `{server}/authorize` as the auth endpoint.

**Fix:** Implement an OAuth AS proxy on PoshMcp:
- `/.well-known/oauth-authorization-server` — RFC 8414 metadata wrapping Entra endpoints +
  adding `registration_endpoint = {server}/register`
- `POST /register` — DCR proxy returning the statically-configured `ClientId`
- PRM `authorization_servers` auto-populated to server base URL when OAuthProxy.Enabled=true
  and no servers are explicitly listed

**Entra limitations:**
- Entra does NOT support RFC 7591 DCR for public clients.
- VS Code has a hardcoded client_id `aebc6443-996d-45c2-90f0-388ff96faa56` — works without DCR.
- Other MCP clients (Claude Desktop, Cline, etc.) need DCR to avoid prompting the user.
- Pre-authorize any client_id in Entra under **Expose an API → Authorized client applications**.

**Config env vars for Container Apps (for Amy):**
```
Authentication__OAuthProxy__Enabled=true
Authentication__OAuthProxy__TenantId={tenant-guid}
Authentication__OAuthProxy__ClientId={client-id}
Authentication__OAuthProxy__Audience=api://poshmcp-prod
```

**Files:**
- `PoshMcp.Server/Authentication/AuthenticationConfiguration.cs` — `OAuthProxyConfiguration`
- `PoshMcp.Server/Authentication/OAuthProxyEndpoints.cs` — new endpoints
- `PoshMcp.Server/Authentication/ProtectedResourceMetadataEndpoint.cs` — dynamic AS URL
- `PoshMcp.Server/Program.cs` — `MapOAuthProxyEndpoints`
- `PoshMcp.Tests/Unit/OAuthProxyEndpointsTests.cs` — 9 unit tests

**X-Forwarded-* headers:** Azure Container Apps sets `X-Forwarded-Proto=https` and
`X-Forwarded-Host={fqdn}`. Always honor these when constructing absolute URLs in endpoints.

**StringValues gotcha:** `Request.Headers["X-Forwarded-Proto"]` returns `StringValues`, not
`string`. Use `(string?)req.Headers["X-Forwarded-Proto"]` or `using System.Linq` for
`.FirstOrDefault()`.



- `AssemblyInformationalVersionAttribute` preserves the full semver string (including `+{commit-hash}` suffix added by the .NET SDK). Strip the suffix with `raw[..raw.IndexOf('+')]` to expose a clean `0.9.2` string.
- `.NET SDK` sets `InformationalVersion` from `<Version>` in the csproj — no manual attribute needed.
- `GetEntryAssembly()` can return null in test contexts; `typeof(DoctorReport).Assembly` is safer and always resolves the correct assembly.
- `DoctorSummary.Version` defaults to `string.Empty` — tests that build minimal reports without setting `Version` still pass; banner renders `PoshMcp v  ✓ healthy` in test but the substring checks (`✓ healthy` etc.) still match.
- **Files modified:** `PoshMcp.Server/Diagnostics/DoctorReport.cs`, `PoshMcp.Server/Diagnostics/DoctorTextRenderer.cs`

### Authentication IOptions bypass fix (2026-05-01)

- **Root cause pattern:** Calling `.Get<T>()` on a config section for local decision-making does NOT register `IOptions<T>` in DI. These are two independent operations. Always pair with `services.Configure<T>(section)` when any downstream consumer uses `IOptions<T>`.
- **Security implication:** If an early-return guard sits before `services.Configure<>()`, the DI options object always resolves to the default value — in this case `Enabled = false` — regardless of appsettings. Middleware and authorization policy gates that read `IOptions<AuthenticationConfiguration>.Value.Enabled` will always see `false`.
- **Rule:** Register `services.Configure<T>()` unconditionally (before any feature-enabled guard) so the real configured value is always available to downstream consumers via DI.
- **Files modified:** `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs`

- `install-modules.ps1` is now bundled in the base image at `/app/install-modules.ps1`; `examples/Dockerfile.user` updated to use it directly.
- Added PSModule path documentation to examples/Dockerfile.user — AllUsers=/usr/local/share/powershell/Modules, built-in=/opt/microsoft/powershell/7/Modules, CurrentUser(runtime)=/home/appuser/.local/share/powershell/Modules
- Added commented COPY directive examples to examples/Dockerfile.user for local module installation (single module + bulk copy patterns)

### ConfigureApplicationInsights pattern (2026-04-27)

- `ApplicationInsightsOptions` must be in `PoshMcp.Server` namespace; Program.cs is in `PoshMcp` namespace — use fully-qualified `PoshMcp.Server.ApplicationInsightsOptions` in the method or add a using.
- `ConfigureApplicationInsights(IServiceCollection, IConfiguration, bool)` must be called AFTER `ConfigureOpenTelemetry` / `ConfigureOpenTelemetryForHttp` in both paths (stdio and HTTP), so OpenTelemetry is already wired before Azure Monitor enriches it.
- `UseAzureMonitor` chaining with `.ConfigureResource(...)` works cleanly on the same `OpenTelemetryBuilder` returned by `services.AddOpenTelemetry()`.
- `SamplingRatio` is a float 0–1; divide `SamplingPercentage` by 100.0f — don't forget the float suffix.
- `Math.Clamp(value, 1, 100)` guards the percentage before converting to ratio, preventing 0% or >100% from reaching Azure Monitor SDK.
- When `Enabled: false` (the default), zero code runs — the guard at the top of the method is all that's needed for zero overhead.


### Doctor AppInsights validation (2026-04-28)

- `BuildConfigurationWarnings` now returns `(List<string> Warnings, List<string> Errors)` tuple and takes `string configPath` to load `ApplicationInsights` settings offline.
- Added `ConfigurationErrors` property to `DoctorReport` at the top level — errors are separate from warnings so `ComputeStatus` can return `"errors"` when config problems are hard blockers (e.g., missing connection string).
- Connection string validation: must start with `InstrumentationKey=` or `https://` — matches the patterns accepted by Azure Monitor SDK.
- `SamplingPercentage` outside 1–100 is a warning (not error) because the runtime already `Math.Clamp`s it.
- When `Enabled: false`, ALL App Insights validation is skipped entirely — no warnings, no errors.
- `DoctorTextRenderer` renders `ConfigurationErrors` with `✖` prefix (same as MCP definition errors).
- Key files: `Program.cs` (`BuildConfigurationWarnings`), `DoctorReport.cs` (`ConfigurationErrors`, `ComputeStatus`), `DoctorTextRenderer.cs`.

### Embedding Dockerfiles in the assembly (2026-07-30)

**Pattern:** To ship static files (Dockerfiles, templates) inside a dotnet global tool so they work without disk presence:

1. Add `<EmbeddedResource>` entries in `.csproj` with `Link` paths using backslash separators to control the manifest resource name:
   ```xml
   <EmbeddedResource Include="..\Dockerfile" Link="Dockerfiles\Dockerfile" />
   ```

2. The manifest name is: `{AssemblyName}.{Link path with backslashes replaced by dots}`.  
   **Important:** The prefix is the *assembly name* (`<AssemblyName>` or project name), not the namespace. For this project, the assembly is `PoshMcp`, so the resource is `PoshMcp.Dockerfiles.Dockerfile` — NOT `PoshMcp.Server.Dockerfiles.Dockerfile`.

3. Read via `Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)`.

4. When the resource isn't found (e.g., file wasn't embedded, or path was custom), fall back to `File.ReadAllText()` so local dev still works.

5. Skip disk-existence checks (`File.Exists`) for paths that are satisfied by embedded resources — in this case the `--generate-dockerfile` flow.

### `--generate-dockerfile` default corrected to "custom" (fixed current session)

**What was wrong:** The `build` command handler had:

```csharp
var buildType = string.IsNullOrWhiteSpace(type)
    ? (generateDockerfile ? "base" : "custom")
    : type.ToLowerInvariant();
```

This meant `poshmcp build --generate-dockerfile` defaulted to `buildType = "base"`, which maps
to the repo root `Dockerfile` — the file for building PoshMcp from source. That is the wrong
template for users; they want `examples/Dockerfile.user`, which extends the published base image.

**How it was fixed:** Both paths (with and without `--generate-dockerfile`) now default to `"custom"`:

```csharp
var buildType = string.IsNullOrWhiteSpace(type)
    ? "custom"
    : type.ToLowerInvariant();
```

Users who explicitly want the source-build Dockerfile can still pass `--type base`.

**Also updated:** `examples/Dockerfile.user` — clarified that `install-modules.ps1` must be
downloaded from the repo, and that the `COPY appsettings.json` line is a placeholder the user
should update to their own path (removed the repo-internal `examples/appsettings.basic.json` path).

- Added --appsettings to poshmcp build: injects COPY line into generated Dockerfile; for build mode stages file to CWD as poshmcp-appsettings.json, uses temp Dockerfile (.poshmcp-build.dockerfile), cleans up both temp files after build
- Fixed poshmcp build 'Dockerfile not found' — embedded resources bypass the disk check; always generate temp dockerfile from embedded resource so build works outside the poshmcp repo

## 2026-05-01: Team OAuth Authentication Architecture Session

### OAuth Proxy Implementation (Joint Effort)
**Bender + Amy coordinated on comprehensive OAuth fix for deployment:**

- **Bender Role:** Implemented OAuth AS proxy + DCR proxy server-side (RFC 8414 + RFC 7591)
  - Added /.well-known/oauth-authorization-server endpoint
  - Added /register DCR proxy (returns configured ClientId)
  - Dynamic ProtectedResource.AuthorizationServers population
  - PR #135 (items 1-4) merged: LoggingHelpers, DockerRunner, SettingsResolver, ConfigurationFileManager, ConfigurationLoader extracted
  - 32 tests passing

- **Amy Role:** Fixed deployment-side configuration (Container Apps + Bicep)
  - Audited deployed Container App (found OAuth proxy disabled)
  - Located real deployment repo (AdvocacyBami, separate from poshmcp)
  - Patched ppsettings.json with OAuthProxy config (TenantId, ClientId, Audience)
  - Updated deploy.ps1 to translate OAuthProxy env vars
  - Cleared duplicate ProtectedResource.AuthorizationServers entries
  - Changes applied; awaiting redeploy

**Coordination outcome:** Server-side OAuth metadata now advertises Entra endpoints; deployment config now passes OAuth settings to Container App via env vars. MCP clients should complete OAuth 2.0 code grant flow without redirect loops after redeploy.

**Decision files:** bender-mcp-oauth-metadata.md, amy-container-apps-auth-config.md (both merged to decisions.md)


## Archived 2026-05-05 (second-pass summarization)

- **Root cause (diagnosed by Fry)**: VS Code builds auth URL as `{authorization_server_base}/authorize` instead of using `authorization_endpoint` from AS metadata, resulting in 404.
- **Fix**: Added `GET /authorize` endpoint to `OAuthProxyEndpoints.cs` that:
  - Captures all incoming OAuth2/PKCE query params from `HttpContext.Request.Query`
  - Replaces the ephemeral DCR `client_id` with the real Entra `client_id` from `proxy.ClientId`
  - Issues a `302 Found` redirect to `https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize`
  - Logs at Debug level (sanitized — only tenant ID, no challenge/state values)
- **Pattern**: Injects `ILoggerFactory` into the minimal API delegate (same pattern used elsewhere)
- **Scope handling**: All params including `scope` pass through unchanged; Entra handles them
- **Validation**: `dotnet build` succeeded (0 errors, 66 pre-existing warnings)

**Files modified:**
- `PoshMcp.Server/Authentication/OAuthProxyEndpoints.cs` — added `/authorize` endpoint + `using Microsoft.Extensions.Logging`
- `PoshMcp.Server/PoshMcp.csproj` — bumped version 0.9.10 → 0.9.11

### Bug Fix: X-Forwarded-Proto in WWW-Authenticate header (v0.9.9)
**Date:** 2026-05-02  
**Status:** Complete  
**Commits:** `fix(auth): honor X-Forwarded-Proto in WWW-Authenticate resource_metadata URL`, `chore: release v0.9.9`  
**Tag:** v0.9.9

- **Bug**: `OnChallenge` JWT event handler built `resource_metadata` URL using `req.Scheme` which returns `http` behind Azure Container Apps' reverse proxy
- **Fix**: Read `X-Forwarded-Proto` and `X-Forwarded-Host` headers (falling back to raw request values) — same pattern already used by `OAuthProxyEndpoints.GetServerBaseUrl` and `ProtectedResourceMetadataEndpoint`
- **Scope**: Only `AuthenticationServiceExtensions.cs` needed fixing; the other two auth files were already correct
- **Validation**: `dotnet build PoshMcp.Server` succeeded; all 24 auth tests passed

**Files modified:**
- `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs` — fixed scheme/host resolution in `OnChallenge`
- `PoshMcp.Server/PoshMcp.csproj` — bumped version 0.9.8 → 0.9.9

### Bug Fix: OnChallenge not firing for no-token (result=none) requests
**Date:** 2026-05-02
**Status:** Complete

- **Symptom**: VS Code connected with no credentials, server logged `authentication.result: none`, but no browser redirect appeared. Connection hung at `initialize`.
- **Root cause 1 (`AuthenticationServiceExtensions.cs`)**: `OnChallenge` was gated on `cfg.Value.ProtectedResource?.Resource is not null`. The validator does NOT require `Resource` to be set. When null, the default JWT Bearer challenge fired with `WWW-Authenticate: Bearer` only — no `resource_metadata`. VS Code never started the RFC 9728 discovery chain.
- **Root cause 2 (`ProtectedResourceMetadataEndpoint.cs`)**: The `resource` field in the PRM JSON could be `null` when `ProtectedResource.Resource` is not configured. RFC 9728 requires `resource` to be an absolute HTTPS URI; a null value would break VS Code's PRM validation even if the challenge had fired correctly.
- **Fix 1**: Changed condition to `ProtectedResource is not null` — aligns with the PRM endpoint's own gate and fires for ALL challenge scenarios (result=none and result=failure) whenever PRM is available.
- **Fix 2**: Added null/empty fallback in PRM endpoint — `resource` now always resolves to `serverBase` when not explicitly configured.

**Files modified:**
- `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs` — broader `OnChallenge` condition
- `PoshMcp.Server/Authentication/ProtectedResourceMetadataEndpoint.cs` — RFC 9728 `resource` null fallback

## Learnings

- **`OnChallenge` fires for result=none**: In ASP.NET Core JWT Bearer, `HandleChallengeAsync` is called by `AuthorizationMiddleware` whenever the user fails policy requirements — both when no token was presented (result=none) and when the token is invalid (result=failure). The handler does NOT skip OnChallenge for result=none.
- **Challenge condition must match endpoint registration**: The `OnChallenge` condition that gates `resource_metadata` injection should always match the condition used by `MapProtectedResourceMetadata` — both now use `ProtectedResource is not null`. If they diverge, the challenge points VS Code to a PRM URL that may not exist.
- **RFC 9728 `resource` is REQUIRED**: The `resource` field in the Protected Resource Metadata MUST be a valid absolute HTTPS URI. Returning `null` is invalid even if all other fields are correct, and will silently break the VS Code OAuth flow.
- **Reverse proxy scheme detection**: Always use `req.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? req.Scheme` in code that builds public-facing URLs. Azure Container Apps (and other proxies) terminate TLS and forward `http` internally. The `UseForwardedHeaders` middleware can be used for app-wide forwarding, but targeted header reads are fine for isolated handlers.
- **Consistency check**: When fixing a header-reading bug, search all auth files for the same pattern. `OAuthProxyEndpoints` and `ProtectedResourceMetadataEndpoint` already had the correct pattern via a `GetServerBaseUrl` helper — the fix brought `AuthenticationServiceExtensions` in line.
- **Prefer `req.Host.ToUriComponent()` over `req.Host.ToString()`** when building URLs — `ToUriComponent()` includes the port only when non-default, which is the correct behaviour.
- **AS metadata must advertise explicit scopes, not `.default`**: Advertising `api://{audience}/.default` in `scopes_supported` causes Entra to issue v1.0 tokens (issuer: `https://sts.windows.net/{tenant}/`) when the app registration targets v1.0 endpoints. v2.0 `ValidIssuers` validation then fails with `SecurityTokenInvalidIssuerException`. Always advertise an explicit delegated scope (e.g. `api://{audience}/user_impersonation`) so Entra issues v2.0 tokens with the expected issuer.
- **Use `DefaultPolicy.RequiredScopes` for dynamic scope resolution**: `AuthenticationConfiguration.DefaultPolicy.RequiredScopes` holds the configured explicit scopes. Prefer the first entry matching the audience over hardcoding `user_impersonation` — this keeps AS metadata in sync with what token validators actually require.

## Previous Work (2026-04-20)
### Spec 009 Phase 3 PR 3: StdioServerHost and HttpServerHost Extraction
**Branch:** squad/program-cs-refactor  
**Status:** Complete ✅
**Commit:** e4b6309 — "refactor: extract server host initialization to StdioServerHost and HttpServerHost"

- **Task**: Extract server startup logic from Program.cs into transport-specific host classes
- **Implementation**:
  - Created `Server/StdioServerHost.cs` (~240 lines):
    - `RunMcpServerAsync()` — main entry point for stdio transport
    - `ConfigureStdioLogging()` — clears console providers, optional Serilog file sink
    - `ConfigureServerConfiguration()` — loads config, wires IOptions validation
    - `ConfigureServerServices()` — chains JSON options, OpenTelemetry, Application Insights, MCP services
    - `RegisterMcpServerServices()` — wires MCP server with stdio transport and handlers
    - `RegisterCleanupServices()` (stdio variant) — PowerShell cleanup service
    - Private helpers: `ConfigureJsonSerializerOptions()`, `ConfigureOpenTelemetry()`, `ConfigureApplicationInsights()`, `DescribeConfigurationPath()`
  
  - Created `Server/HttpServerHost.cs` (~340 lines):
    - `RunHttpTransportServerAsync()` — main entry point for HTTP transport
    - `ConfigureCorsForMcp()` — auth-aware CORS policy setup
    - `RegisterHealthChecks()` — PowerShell, assembly generation, configuration checks
    - `ConfigureOpenTelemetryForHttp()` — includes ASP.NET Core instrumentation + console exporter
    - `WriteHealthCheckResponseAsync()` — JSON health report serialization
    - `RegisterCleanupServices()` (HTTP variant) — PowerShell cleanup service
    - Private helpers: `ConfigureJsonSerializerOptions()`, `ConfigureApplicationInsights()`, `DescribeConfigurationPath()`

- **Program.cs Updates**:
  - Wrapper methods delegate to extracted hosts: `RunMcpServerAsync()` → `StdioServerHost.RunMcpServerAsync()`
  - Removed ~700 lines of configuration code; kept ~50 lines of wrapper delegators
  - SetHandler lambdas now call `StdioServerHost.RunMcpServerAsync()` and `HttpServerHost.RunHttpTransportServerAsync()`
  - Kept McpToolSetupService call sites (tool wiring already extracted in PR 2)
  - Kept DescribeConfigurationPath() — used by BuildDoctorReportFromConfig() and other diagnostic methods

- **Validation**: All 3 files (Program.cs, StdioServerHost.cs, HttpServerHost.cs) compile without errors

**Key Patterns:**
1. **Transport-Specific Extraction**: Each transport (stdio vs HTTP) has distinct setup paths; isolating them clarifies dependencies and reduces Main() clutter
2. **Wrapper Delegators**: Lightweight Program.cs methods delegate to extracted hosts; call sites unchanged
3. **Consolidation of Configuration Methods**: Both hosts needed some duplicate config (JSON options, Application Insights); kept them close to their usage rather than in Program.cs
4. **Health Check Isolation**: HTTP-only feature (health checks) is now in HttpServerHost, not scattered in Program.cs

**Metrics**:
- Lines removed from Program.cs: 694
- Program.cs post-PR3: ~1,140 lines
- **Cumulative reduction**: 38% complete (was 1,834 after PR 2 + DoctorService + McpToolSetupService)
- **Remaining target**: ~200 lines (estimated 2 more PRs: CLI extraction + final cleanup)

---
*Older entries (pre-2026-05-05 bulk) moved to `history-archive.md` on 2026-05-05 by Scribe to satisfy 15KB hard gate. See archive for full record.*

## Archived 2026-05-15T140000Z

## Learnings

### 2026-05-14: Issue #217 — Resource hygiene audit, dynamic ports (PR draft)

**Requested by:** Steven (Ralph started everyone on milestone 6 / spec 009).
Spec 009 FR-411: tests that bind a TCP port must use port 0 + read back the
actual port, not a hard-coded port from a small range.

**Audit results.** Scanned `PoshMcp.Tests/` for `HttpListener|TcpListener|`
`UseUrls|ListenLocalhost|UseKestrel|ConfigureKestrel|WebApplication.Create|`
`UseHttpSys|Kestrel|ListenAnyIP|builder.WebHost`, plus port-literal patterns
`:5000`, `:8080`, `:5001`, `:8081`, `localhost:N`, `127.0.0.1:N`, range
allocators (`Random.Shared.Next`, `new Random()`).

| File | Pattern found | Action |
| --- | --- | --- |
| `Unit/OutOfProcess/OutOfProcessHostConcurrencyTests.cs` (`LoopbackHttpServer`) | HttpListener bound via TcpListener probe on port 0 → URL `http://127.0.0.1:{port}/` | None — already canonical |
| `Integration/ApplicationInsightsIntegrationTests.cs` (`AppInsightsTestHttpServer`) | `_port = port == 0 ? Random.Shared.Next(6100, 6900) : port;` | Replaced with `DynamicPort.Allocate()` |
| `Integration/UnifiedHttpTransportIntegrationTests.cs` (`InProcessUnifiedHttpServer`) | Same `Random.Shared.Next(6100, 6900)` pattern | Replaced with `DynamicPort.Allocate()` |
| `Fixtures/ProxyTestFixtures.cs` `:5985` and `Unit/WinPsCompatProxyTests.cs` `:1234` | Port literals in WSMan URL strings used as test description data — NOT bind sites | False positive — left alone |

**Helper extracted.** Added `PoshMcp.Tests/Shared/DynamicPort.cs` — a single
`Allocate()` method that binds `TcpListener(IPAddress.Loopback, 0)`, reads
`((IPEndPoint)LocalEndpoint).Port`, then `Stop()`s the probe so the caller's
child process can bind it. XML doc explains the small race window and why it's
still vastly better than the 800-port range-picker we replaced (which
collided across concurrent CI runs).

**Why a probe-and-release helper instead of binding port 0 in-place.** The
two affected fixtures spawn a child `dotnet run -- serve --transport http
--url http://localhost:N` process and pass the URL by argument — the parent
needs the numeric port BEFORE the child has bound. `LoopbackHttpServer`
already used this exact pattern; I lifted it into shared code instead of a
third copy.

**Consumer changes.** `ServerUrl` properties were already
`$"http://localhost:{_port}"` in both fixtures, so consumers didn't need any
changes — the field they read is now populated by the OS instead of by a
range-picker.

**Validation.**
- Build: `dotnet build PoshMcp.Tests` — 0 errors, 20 warnings, all
  pre-existing (NU1510 + CS8602/CS8604 in untouched files).
- Tests passed (background-spawned pwsh-style child processes; durations are
  whole-second granularity from VSTest):
  - `UnifiedHttpTransportIntegrationTests` — 4/4 (covers `InProcessUnifiedHttpServer`)
  - `ApplicationInsightsIntegrationTests` — 4/4 (covers `AppInsightsTestHttpServer`)
  - `MultiUserIsolationTests` — 1/1 (also uses `InProcessUnifiedHttpServer`)
  - Total: 9/9 impacted tests pass.
- `OutOfProcessHostConcurrencyTests` (the unchanged `LoopbackHttpServer`)
  was attempted but the platform terminal kept cancelling the run mid-flight;
  the fixture itself was not modified, so its existing pre-spec-009 baseline
  applies.

**Don't regress.**
- `DynamicPort.Allocate()` is the canonical pattern for fixtures that hand
  a port to a child process by argument. For fixtures that own the listener
  in-process (HttpListener, TcpListener, Kestrel inside the test process),
  prefer to bind to port 0 directly and read the bound endpoint —
  `Allocate()` is only required when the bind happens elsewhere.
- The probe-and-release race window is real but rare. If a future change
  starts seeing collisions in CI, the fix is retry-on-bind-failure inside
  the helper, NOT going back to a hand-picked range.
- The two fixtures still expose an optional `port` parameter (default 0).
  Keep that — callers can override for diagnosis. The auto-allocation only
  kicks in when `port == 0`.

---



### 2026-05-13: Issue #249 — PowerShellSchemaGenerator default swap (PR #250, draft)

**Requested by:** Steven. Cold-path twin of the #242/#248 wire fix. Same class of bug,
different file.

**The bug:** `PowerShellSchemaGenerator.cs` had `metadataSource ?? new DefaultToolMetadataSource()`
in two spots (`GenerateParameterSchema` line ~33 and `CreateParameterSchema` overload line ~131).
DefaultToolMetadataSource is the no-op resolver — always returns the typed-fallback string and
bypasses the FR-510 precedence chain. Identical mistake to the three McpToolFactoryV2 ctor
defaults that #248 just fixed.

**The fix:** Swap both defaults to `new HelpAwareToolMetadataSource()`. That class is a
**parameterless pure resolver** — no DI plumbing, no runspace, no help cache required. Without
pre-resolved Get-Help text the chain degrades naturally to HelpMessage → ValidateSet → typed
fallback, which matches what the cold path can actually supply. XML doc comments updated to
describe the new default.

**Caller analysis (key finding):** `grep_search PowerShellSchemaGenerator|GenerateParameterSchema|CreateParameterSchema`
returns ONLY matches inside the file itself. Zero production callers, zero test callers. This
class is currently dead code on the doc-emission path, so the user-visible blast radius is
nil — but leaving the wrong default armed is exactly what bit us in #242 when a future caller
finally landed on the live MCP wire. Fix it now while it's easy.

**Validation:**
- `dotnet build PoshMcp.Server\PoshMcp.csproj`: 0 errors, 19 pre-existing warnings.
- `ParameterDescription_IsNonEmpty` gate: **10/10 passed** (5 in-process + 5 OOP).
- Unit suite (`FullyQualifiedName~PoshMcp.Tests.Unit`): **532/532 passed**.

**Commit:** `8807a73 fix(schema): wire HelpAwareToolMetadataSource as default in PowerShellSchemaGenerator (#249)`
**PR:** https://github.com/usepowershell/PoshMcp/pull/250 — draft, base main, head `squad/249-schemagen-helpaware`.

**Don't regress:**
- The HelpAwareToolMetadataSource parameterless ctor is now load-bearing. If a future change
  forces it to require dependencies (a runspace, a help resolver), the cold-path callers in
  PowerShellSchemaGenerator will need a different fallback strategy. Don't switch back to
  DefaultToolMetadataSource as the easy out — that's the bug we just fixed twice.
- DefaultToolMetadataSource is still the right shape for tests that want to lock down the
  pre-spec-010 byte-for-byte output. Keep it; just don't use it as a production default.

---



### 2026-05-12: Issue #225 — IToolMetadataSource seam extraction (PR #238, draft)

**Requested by:** Steven. Spec 010 sequencing step 3. Wave-2 foundational issue;
wave-3 (#226 in-process Get-Help, #227 OOP Get-Help) and wave-4 (#228 doctor +
metrics) all depend on this interface shape.

**Contract chosen.** Single interface `IToolMetadataSource` with two methods:
`ResolveToolDescription(in ToolDescriptionRequest)` and
`ResolveParameterDescription(in ParameterDescriptionRequest)`. Both return a
result record carrying the resolved string + the precedence-step enum that
produced it (`ToolDescriptionSource` / `ParameterDescriptionSource`). Enum
values map 1:1 to the FR-583 string literals (`synopsis|description|syntax|name`
for tools; `helpParameter|helpMessage|validateSet|typeFallback` for parameters)
so doctor output (#228) just `.ToString()`-es them with camelCase.

**Request records carry pre-resolved help fields, not callbacks.** The
in-process caller (#226) will populate `Synopsis` / `LongDescription` /
`HelpParameterDescription` / `HelpMessage` / `ValidateSetValues` from its own
`Get-Help` invocation; the OOP caller (#227) will populate them from extended
`RemoteToolSchema` fields shipped over ndjson. The seam itself never calls
`Get-Help` — it's pure precedence selection + sanitization (sanitization lands
in #226/#227 with the FR-540 implementation).

**Files touched:**
- NEW `PoshMcp.Server/PowerShell/IToolMetadataSource.cs` — interface + records + enums.
- NEW `PoshMcp.Server/PowerShell/DefaultToolMetadataSource.cs` — preserves pre-spec-010 output byte-for-byte.
- `PoshMcp.Server/McpToolFactoryV2.cs` — new field `_toolMetadataSource`; new ctor overloads accepting `IToolMetadataSource?`; `SetParameterSetDescription` and `CreateRemoteCommandMetadataMapping` route through the seam.
- `PoshMcp.Server/Server/StdioServerHost.cs` + `HttpServerHost.cs` — `TryAddSingleton<IToolMetadataSource, DefaultToolMetadataSource>()`.
- `PoshMcp.Server/Server/McpToolSetupService.cs` — optional `IToolMetadataSource?` param threaded through `SetupMcpToolsAsync` / `SetupHttpMcpToolsAsync` / `CreateToolFactory`.

**Default impl precedence (preserves today's behavior):**
1. `Synopsis` non-empty AND != `CommandName` → Synopsis (preserves OOP).
2. Else `ParameterSetSyntax` non-empty → `"{name} {syntax}"` (preserves in-process).
3. Else bare command name.
The default impl deliberately IGNORES `LongDescription` and all parameter-help
fields — those land in #226/#227. Parameter resolver always returns the type
fallback for now.

**Verified equivalence:**
- In-process path: never had Synopsis populated. Falls straight to Syntax →
  identical to old `"{name} {parameterSet.ToString()}"`.
- OOP path: `oop-host.ps1` only writes Synopsis when `≠ CommandName`, so the
  `Synopsis != CommandName` guard in the default impl is effectively pre-checked
  upstream — still wired safely. Empty schema.Description → fallthrough → Name.

**Spec gap surfaced (PR body called this out for reviewers):**
`ToolDescriptionRequest.LongDescription` is on the interface but the default
impl ignores it. The spec assigns long-description sourcing to the caller-side
Get-Help integration in #226, not the seam itself. If Farnsworth/Cubert prefer
the seam to consume LongDescription as part of its precedence ladder, that's a
trivial follow-up edit in `DefaultToolMetadataSource` and an enum entry already
exists (`ToolDescriptionSource.Description`). Posed as a reviewer choice.

**Validation:**
- `dotnet build PoshMcp.sln -c Release`: 0 errors. 20 warnings — all pre-existing
  (NU1510 + CS8602/CS8604 in `PowerShellAssemblyGenerator.cs`,
  `Cli/CommandHandlers.cs`, untouched lines in `McpToolFactoryV2.cs`,
  `WinPsCompatProxyMethodGenerationTests.cs`). No new warnings introduced.
- `dotnet test --filter "Category!=Integration"`: 661 passed, 7 skipped, 0 failed.

**Commit:** `df5b9bd feat(metadata): extract IToolMetadataSource seam (#225)`.
**PR:** https://github.com/usepowershell/PoshMcp/pull/238 — draft, base main, head `squad/225-tool-metadata-source`.

**Don't regress:**
- `PowerShellSchemaGenerator.cs` still hard-codes `"Parameter of type X"`. That's
  the parameter-description call site #226 will need to thread `IToolMetadataSource`
  into. The interface method `ResolveParameterDescription` exists and the default
  returns TypeFallback verbatim — wire it through, then implement Get-Help
  parameter-block sourcing in the same PR.
- The OOP cross-invoke defensive fix landed in v0.12.3 (`AddScript($s, $true)`).
  Do NOT touch that when wiring #227's `RemoteToolSchema` extension — preserve
  `useLocalScope=$true` and don't add an inner `& { ... }` wrapper (it breaks
  `HadErrors` propagation; the round-3 history entry below records the trap).
- `gh pr create` from `stmuraws_microsoft` account fails with EMU GraphQL
  Unauthorized on the `usepowershell/PoshMcp` org. Switch with
  `gh auth switch --user usepowershell` before creating PRs, then switch back.

---

### 2026-05-12: Issue #233 — RemoteToolSchema XML doc fix (PR #235, draft)

**Requested by:** Steven. Spec 010 step 10 / FR-560. Doc-only, no runtime change.

**Current behavior of `RemoteToolSchema.Description` (verified by reading source, not speculated):**
- Populated exclusively in `oop-host-pool.ps1` ~L824-829 during `discover`. The in-process path does not use this type at all.
- Source: `Get-Help -Name $cmd.Name -ErrorAction SilentlyContinue`; if `.Synopsis` is non-null, it is `Trim()`-ed and assigned only when it differs from `$cmd.Name`. Otherwise the field stays as the initial value `''` (empty string).
- There is NO fallback to parameter set syntax. The prior XML doc claim ("from Get-Help or parameter set syntax") was wrong on both counts.
- Downstream (tool schema generation) treats an empty description as "use the bare command name as the description" — matches the spec 010 scenario table.

**No other stale property docs found in `RemoteToolSchema.cs`:**
- `Name`: accurate.
- `ParameterSetName`: accurate (mentions `__AllParameterSets` sentinel).
- `Parameters`: accurate.
- `RemoteParameterSchema.TypeName`: accurate, already explains the string-not-`Type` rationale.
- `IsMandatory` / `Position`: no doc comments — absent, not stale. Out of scope for #233.

**PR:** https://github.com/usepowershell/PoshMcp/pull/235 — draft, base `main`, head `squad/233-remotetoolschema-doc`.

**Build:** `dotnet build PoshMcp.Server -c Release` succeeds. Only warning is the pre-existing NU1510 about `System.Security.Cryptography.Xml` package pruning — unrelated.

**Don't regress:** When spec 010's parameter-description sourcing rule lands (FR-510 et al — parameter description from `Get-Help` `.Parameters.parameter.description`), the same XML doc will need updating again to describe the new precedence. The current corrected text matches today's behavior, not the post-spec-010 behavior.

---


### 2026-05-12 (round 3): OOP cross-invoke — production v0.12.2 evidence; defensive scope landed without local repro

**Requested by:** Steven Murawski (Brady). Brady returned with hard
production evidence: two sequential MCP calls against the deployed
poshmcp-web server returned byte-for-byte identical payloads even though
the tools were unrelated (`get_tenant_context` then
`assert_tenant_role_member`, both responses showing tenant-context
JSON). Brady's directive: re-read the pool host with fresh eyes, run an
aggressive repro that actually mirrors production (runspacePoolSize=10,
DIFFERENT scripts in each step, 50+ iterations), and apply
defense-in-depth even if I still can't reproduce locally.

**Production config gap I missed in round 2:** my round-2 repro ran at
pool=2 / 6 iterations / SAME script with different params. The deployed
server runs at pool=10 / process pool=4 / AdvocacyBami workload. The
"same script with different params" gap is the one that mattered — the
production scenario invokes structurally different tools back-to-back
on the same leased runspace.

**What I did:**

1. **Production-shape repro test.**
   `PoolHost_AlternatingDifferentScripts_LargePool_NoCrossInvokeLeak`
   in `OutOfProcessPoolHostIntegrationTests.cs` runs `Write-Output`
   (returns structured per-iteration sentinel) alternating with
   `Write-Verbose` (returns nothing) for 50 iterations at
   runspacePoolSize=10. After A: response must contain the current
   sentinel and NO prior sentinel. After B: response must equal "null"
   and contain NO prior sentinel.
2. **Result:** test PASSES on current main HEAD without any production
   code change. Cross-invoke `$r`-leak hypothesis is not what the user
   is observing.
3. **Defensive change applied anyway.** Both `oop-host.ps1` and
   `oop-host-pool.ps1` now call `AddScript($userScript, $true)`. With
   `useLocalScope=$true` the script body runs in a child scope of the
   runspace's default scope, so the per-invoke working variable `$r` is
   discarded on return instead of living at runspace scope where the
   next invoke on the same leased runspace could (in some future-edit
   exception path) observe it.

**First defensive attempt I had to back out:** wrapping the call site in
an inner `& { ... }` scriptblock as well broke
`HadErrorsDoesNotLeakAcrossInvokes`. With the inner scriptblock, a
`Get-ChildItem -Path missing -ErrorAction SilentlyContinue` no longer
surfaced `HadErrors=true` on the parent pipeline (`Streams.Error`
populated, boolean flipped). The single-layer `useLocalScope=$true`
change has none of that side effect.

**Honest disposition for the production symptom:** v0.12.2 lacks commit
6908917 ("fix(oop): clear per-invoke state so errors don't return prior
output"). That fix converts `hadErrors=true` into a thrown
`InvalidOperationException` that MCP marks `IsError=true` instead of
returning the partial pipeline output as a deceptive success. The
mechanism in production for the user-visible "same tenant payload from
a role-member tool" is the same partial-pipeline-output-before-error
pattern documented in round 1: `Assert-BamiTenantRoleMember` emits
tenant-shaped output internally (via its embedded `Assert-BamiTenantUser`
/ `Get-BamiTenantContext` call chain) before writing the role
non-terminating error; v0.12.2 returns that pipeline output as success.
**The user-facing fix is the existing 6908917 commit; deploying a
0.12.3 (or later) build that includes it is what resolves the report.**
The defensive scope change landed in this round is the structural
belt-and-suspenders against the adjacent `$r`-leak class.

**What this change does NOT fix:**

- User-authored modules setting their own cross-invoke state via
  `$global:` or `$script:` in their own module scope. The OOP framework
  cannot contain that from outside.
- The deployed v0.12.2 server's behavior. That requires a deployment of
  current main.

**Files changed (this round):**

- `PoshMcp.Server/PowerShell/OutOfProcess/oop-host.ps1` —
  `AddScript($userScript, $true)`; comment block.
- `PoshMcp.Server/PowerShell/OutOfProcess/oop-host-pool.ps1` — same.
- `PoshMcp.Tests/Integration/OutOfProcessPoolHostIntegrationTests.cs` —
  new test `PoolHost_AlternatingDifferentScripts_LargePool_NoCrossInvokeLeak`.

**Test status:** Category=OutOfProcess: 47 passed, 0 failed, 0 skipped.

**Commit:** `e1c923e fix(oop): defensive per-invoke scope for user
script`. Pushed to main.

**Don't regress:**

- Do NOT also wrap the user script body in an inner `& { ... }`
  scriptblock. That breaks non-terminating-error propagation to the
  parent pipeline's `HadErrors` flag. The single
  `useLocalScope=$true` is the right shape.
- Do NOT use `Get-Variable -Name r -ErrorAction Ignore` as a peek
  primitive in tests. `Get-Variable` on a missing name still flips
  `HadErrors=true` and (post-6908917) the C# layer surfaces that as an
  exception. The 2026-05-12 round-2 history already captured this trap;
  I hit it again in round 3 while writing a variable-peek test and
  removed that test in favor of the alternating-scripts production-
  shape repro.

---

### 2026-05-12 (revisit): OOP cross-invoke leak — could NOT reproduce; prior diagnosis acknowledged as incomplete

**Requested by:** Steven Murawski. User explicitly rejected the prior diagnosis below
("YOU GOT THE PRIOR DIAGNOSIS WRONG... The actual observed behavior is: 'When the command
was run the first time, it returned null. After other commands were run, it started
returning their output when being rerun.' That is definitively cross-invocation state.")
and asked me to find and fix the real leak — reproduce FIRST, no speculative fix.

**Acknowledgment of prior misdiagnosis:** The 2026-05-12 entry below correctly identified
*one* real bug (`hadErrors=true` was being logged but the partial output returned anyway),
but it conflated that with the user's observation. The user's report describes a *time-
separated* state leak: first call returned null, *then after other calls* the same command
started returning their output. That pattern is not explained by "current invoke's
pre-error pipeline output" — it requires actual state surviving between separate invokes.

**Reproduction attempts (all PASS on current main HEAD 273bc3b — no leak observed):**
1. `Invoke_TwoDifferentSuccessfulCommands_SecondDoesNotReturnFirstOutput` (existed) — Single host
2. `Invoke_ErrorInDifferentCommandAfterSuccess_DoesNotReturnFirstOutput` (existed) — Single host
3. `PoolHost_TwoDifferentSuccessfulCommands_SecondDoesNotReturnFirstOutput` (existed) — Pool host
4. `PoolHost_ErrorInDifferentCommandAfterSuccess_DoesNotReturnFirstOutput` (existed) — Pool host
5. **NEW** `Invoke_EmptyCommand_AfterPriorOutput_DoesNotReturnPriorOutput` — exactly matches
   user's sequence: empty-returning cmd (Write-Verbose) → producing cmd (Get-Item) → rerun
   empty cmd, assert "null". Single host. PASS.
6. **NEW** `PoolHost_EmptyCommand_AfterPriorOutput_DoesNotReturnPriorOutput` — same pattern
   on Pool host (pool size 2, 6 iterations to exercise every runspace). PASS.

False starts during repro design (lessons):
- Custom function via startup script does NOT land in `$script:SharedRunspace` (startup
  scripts execute in the OOP host's own runspace) — separate gap worth filing as its own
  issue, not the leak being investigated.
- `Get-Variable -ValueOnly -ErrorAction Ignore` on a nonexistent name STILL sets
  `HadErrors=true`, which (correctly) trips the 2026-05-12 fix and throws "OOP error:
  ... (discarded 4-char output)". The 4 chars are the literal string `"null"` returned
  by the user script. The hadErrors→throw path is working as designed.
- Settled on `Write-Verbose -Message 'x'` as the clean empty-returning vehicle: built-in,
  writes to verbose stream (suppressed), returns nothing, does NOT set HadErrors.

**Architectural review (no leak surface found):**
- `oop-host.ps1` `$script:` vars: only `Dispatcher`, `SharedRunspace`, `CommonParameters`,
  `Cancellations`. No output accumulator. User script uses a *local* `$r` overwritten
  every invoke; `$Error.Clear()` runs at the top of every invoke.
- `oop-host-pool.ps1` mirrors the pattern. `$script:Pool`, `$script:Dispatcher`, host UI
  routes to stderr. Same per-invoke fresh `[powershell]` with `RunspacePool`.
- `OutOfProcessHost.cs`: `_pending` is `ConcurrentDictionary<string, TCS>` keyed on
  `Guid.NewGuid()` per request, removed on completion. No keying on command name.
- `OutOfProcessCommandExecutor.cs`: `_cachedSchemas` is for `discover` output only.
  `_lastSetupConfig` is for restart replay, never returned to callers.
- `OutOfProcessSubprocessPool.cs`: same per-request-ID pattern.
- `OutOfProcessToolAssemblyGenerator.cs`: no per-tool result cache.
- No mutable static fields in the OOP module (grep verified).

**Disposition:** I cannot reproduce a framework-level cross-invocation output leak on
current main. All 46 `Category=OutOfProcess` tests pass, including the 2 new regression
guards. Per Steven's directive ("do NOT push a speculative fix"), I am NOT modifying
production code. The 2 new tests are committed as permanent regression guards — they will
fail loudly if a real cross-invoke leak is ever introduced.

**Remaining hypotheses I did NOT chase** (any of these could explain Steven's observation
without a framework bug; would require Steven's exact command list to verify):
- The reported sequence used an AdvocacyBami module command whose own internal state
  (module-scoped `$script:` vars in user-authored modules) leaked across calls. The OOP
  framework cannot detect or prevent that.
- The reported sequence involved restart/reconnect of the OOP subprocess in between
  calls, where `_lastSetupConfig` replay or a stale pending response could matter. I
  did not exercise the subprocess-death+restart path with overlapping calls.
- A bug in a specific tool-generation code path for a specific parameter shape (e.g.,
  PSCredential, complex pipeline-bound parameters) that I didn't exercise.

**Files changed (this session):**
- `PoshMcp.Tests/Integration/OutOfProcessIntegrationTests.cs` — added regression test
- `PoshMcp.Tests/Integration/OutOfProcessPoolHostIntegrationTests.cs` — added regression test
- NO production code modified.

---

### 2026-05-12: OOP invoke — hadErrors was logged but not propagated to MCP

**Bug**: A user invoked `assert_tenant_role_member` with a bad role and got back what looked
like the *prior* `assert_tenant_user` payload, with MCP `IsError=false`. Server log showed
`warn: ... reported errors. Output: {prior-looking JSON}` and `IsError = False`.

**Root cause** (NOT actual cross-invoke leak):
- Each invoke uses a fresh `[powershell]` instance, so streams are not shared across invokes
  and `$Error.Clear()` already runs at the top of the user script (#189 was a prior fix).
- The real bug was in `OutOfProcessCommandExecutor.InvokeAsync` (and the pool mirror in
  `OutOfProcessSubprocessPool.InvokeAsync`): when the response carried `hadErrors=true`,
  the executor **logged a warning and returned the partial output anyway**. The MCP
  framework can only mark a tool call `IsError=true` if the generated method throws —
  returning a normal string is always treated as success.
- The "prior payload" the user saw was actually the *current* command's partial pipeline
  output before its own non-terminating error. AdvocacyBami's `Assert-BamiTenantRoleMember`
  internally calls `Assert-BamiTenantUser` (which emits the user object) and then writes
  a non-terminating error for the bad role. With `$r = & $Name @Splat`, `$r` ends up
  holding the user assertion object, which then got JSON-serialized as "success".

**Fix location**:
- `PoshMcp.Server/PowerShell/OutOfProcess/OutOfProcessCommandExecutor.cs` — `InvokeAsync`
  now throws `InvalidOperationException` with message `OOP error: command '{X}' reported
  {N} error(s): {joined errors}` whenever `hadErrors=true && cancelled=false`. Added
  private helper `ExtractErrorMessage`.
- `PoshMcp.Server/PowerShell/OutOfProcess/OutOfProcessSubprocessPool.cs` — same change
  in the pool's `InvokeAsync`, with helper `ExtractInvokeErrorMessage`. Pool mode and
  single mode now behave identically on hadErrors.
- `cancelled=true` is intentionally excluded so cooperative cancellation does not get
  reclassified as a tool failure.

**Message format preserves the existing "OOP error:" prefix** used by `OutOfProcessHost`
for terminating errors. That means existing test catches like
`ex.Message.Contains("OOP error")` (e.g. the `Get-AzContext` path in
`OutOfProcessModuleTests`) keep working without modification.

**Regression test**:
`PoshMcp.Tests/Integration/OutOfProcessIntegrationTests.cs::Invoke_WithErrorAfterSuccess_DoesNotReturnPreviousOutput`
runs a successful `Get-Item` against a marker directory, then a failing `Get-Item`
against a non-existent path with `ErrorAction=Continue`, and asserts the second invoke:
(1) throws `InvalidOperationException`, (2) message contains `"OOP error"` and the
failing command name, (3) message does NOT contain the unique marker token from the
prior successful output.

**Test status**: 18/18 in `OutOfProcessIntegrationTests`, 40/40 with `Category=OutOfProcess`.

**Don't regress**:
- A command can legitimately write to `$Error` non-terminally and still produce output
  the user might care about. Post-fix, that case becomes `IsError=true`. This is the
  intended contract: MCP clients must see error state instead of silently-success
  output. If a future caller wants a tolerant variant, add a separate API rather than
  weakening this gate.
- Do NOT throw when `cancelled=true`. Cancellation already has its own surface and
  reclassifying it as an error would break the cancel-in-flight path.

## Recent Work (2026-05-01 — CURRENT SESSION)

### Diagnosis: AggregateError — Failed to Fetch Authorization Server Metadata
**Date:** 2026-05-01  
**Status:** Diagnosis complete — fix NOT yet applied  
**Report:** `.squad/decisions/inbox/bender-authserver-metadata-diagnosis.md`

- **Task**: Diagnose why VS Code reports `AggregateError: Failed to fetch authorization server metadata from all attempted URLs` after the v0.9.4 fix for `WWW-Authenticate`
- **Findings**:
  - Root cause: `authorization_servers` in the PRM contains `https://login.microsoftonline.com/{tenant}` (missing `/v2.0`). VS Code fetches the discovery doc at `{url}/.well-known/openid-configuration`, gets back a v1.0 document with `issuer: https://sts.windows.net/{tenant}/`. Per RFC 8414 §3, issuer MUST match the URL used to discover it — `sts.windows.net` ≠ `login.microsoftonline.com` → VS Code rejects the document → AggregateError.
  - Secondary: PRM response contains duplicated entries (2× `authorization_servers`, 2× `scopes_supported`, 3× `bearer_methods_supported`). Consistent with config being applied twice; the count of 3 for BearerMethods (which has a model-level default of `["header"]`) vs 2 for others (empty default) confirms this.
  - The config file (`appsettings.json`) already has the correct `/v2.0` form in `ValidIssuers` — just missing it in `AuthorizationServers`.
- **Fix required**:
  1. Change `AuthorizationServers` in the AdvocacyBami `appsettings.json` from `login.microsoftonline.com/{tenant}` to `login.microsoftonline.com/{tenant}/v2.0`
  2. Investigate why configuration arrays are being accumulated (duplicated) — likely double-registration of the config JSON file in the provider pipeline

---

### Diagnosis: VS Code Auth Redirect to PoshMcp `/authorize`
**Date:** 2026-05-01  
**Status:** Diagnosis complete — awaiting fix approval  
**Report:** `.squad/decisions/inbox/bender-vscode-auth-redirect-diagnosis.md`

- **Task**: Diagnose why VS Code redirects authentication to PoshMcp's own `/authorize` endpoint instead of Entra ID
- **Findings**:
  - Root cause: `AuthenticationServiceExtensions.cs` does not configure `JwtBearerEvents.OnChallenge`, so JwtBearer 401 responses emit `WWW-Authenticate: Bearer` without the RFC 9728 `resource_metadata` parameter. VS Code can't discover the PRM and falls back to treating PoshMcp as the auth server → constructs `{serverBaseUrl}/authorize`.
  - Secondary bug: `ApiKeyAuthenticationHandler.HandleChallengeAsync` constructs the `resource_metadata` URL from `ProtectedResource.Resource` (an `api://` URI) instead of the server's actual HTTP base URL. Produces an invalid non-HTTP URL.
  - The `client_id=80939099-d811-4488-8333-83eb0409ed53` in the redirect is the PoshMcp App Registration's Application ID — confirms VS Code is in fallback mode (extracted GUID from PRM's `resource` field).
  - The PRM content and `authorization_servers` configuration are correct; only the 401 challenge header is missing.
- **Fix required**:
  1. Add `JwtBearerEvents.OnChallenge` in `AuthenticationServiceExtensions.cs` to emit `WWW-Authenticate: Bearer resource_metadata="{request.Scheme}://{request.Host}/.well-known/oauth-protected-resource"`
  2. Fix `ApiKeyAuthenticationHandler.HandleChallengeAsync` to use `Request.Scheme + Request.Host` for the metadata URL

## Learnings

- **RFC 9728 `resource_metadata` is required in `WWW-Authenticate`** — Without `resource_metadata="{url}"` in the 401 `WWW-Authenticate` header, VS Code's MCP OAuth client cannot discover the PRM. It falls back to treating the resource server as the authorization server and appends `/authorize` to the base URL.
- **`ProtectedResource.Resource` is an `api://` URI, not an HTTP URL** — Never use it to construct HTTP endpoint URLs (like the PRM metadata URL). Always derive the server base URL from `HttpContext.Request.Scheme + Request.Host`.
- **VS Code fallback `client_id` behavior** — When VS Code can't resolve the real auth server, it extracts the GUID from the PRM's `resource` field (e.g., `api://80939099-...`) and uses it as the OAuth `client_id` in the fallback authorization request. This GUID is the App Registration's Application ID, NOT VS Code's own client_id (`aebc6443-996d-45c2-90f0-388ff96faa56`).
- **ApiKey scheme ≠ JwtBearer scheme for challenge handling** — Adding `WWW-Authenticate` logic to `ApiKeyAuthenticationHandler` does NOT cover the JwtBearer scheme. Each scheme must independently configure its challenge response.
- **`context.HandleResponse()` is required when overriding JwtBearer challenge** — Calling `context.HandleResponse()` in `OnChallenge` suppresses the default JwtBearer challenge pipeline so you can set your own `StatusCode` and `WWW-Authenticate` header. Without it, ASP.NET Core writes a second `WWW-Authenticate: Bearer` header after your custom one, producing a malformed multi-value header.
- **Entra ID `authorization_servers` must include `/v2.0`** — The PRM `authorization_servers` value must be `https://login.microsoftonline.com/{tenant}/v2.0`, NOT the bare tenant URL. Without `/v2.0`, VS Code discovers the v1.0 OIDC endpoint, which returns `issuer: https://sts.windows.net/{tenant}/`. That issuer does not match the authorization_server URL, so VS Code rejects the discovery document per RFC 8414 §3 → AggregateError. With `/v2.0`, the issuer is `https://login.microsoftonline.com/{tenant}/v2.0` which matches exactly.
- **ASP.NET Core config array duplication** — If the same JSON config file is registered as a configuration provider more than once (e.g., once by the default `WebApplication.CreateBuilder()` pipeline and again by a custom config loader), array values are accumulated (not replaced). Properties with C# model-level defaults (e.g., `new() { "header" }`) accumulate one extra copy. Check for double `AddJsonFile(path)` calls in the configuration pipeline when PRM arrays contain unexpected duplicates.

---

## Recent Work (2026-04-20)

## Recent Work (2026-05-11 — CURRENT SESSION)

### PR #211: Test Fixtures for Proxy & High-Parameter Method Schema Validation
**Date:** 2026-05-11
**Status:** Complete (committed, awaiting Fry for integration test implementation)
**Branch:** `fix/winpscompat-proxy-parameters`

- Created new `PoshMcp.Tests/Fixtures/` folder with three files:
  - `ProxyTestFixtures.cs` — Static factory methods for synthetic commands:
    - `CreateProxyStyledCommand()` → CommandInfo with ImplicitRemoting marker, object params
    - `CreateHighParameterCommand()` → CommandInfo with 17 params (triggers cached delegate path in McpToolFactoryV2)
    - `CreateObjectParameterCommand()` → CommandInfo with [object] params on proxy module
  - `Pr211IntegrationFixtureSetup.cs` — Test infrastructure class:
    - `GetFixtureCommands()` → Creates and caches all three fixture commands
    - `ValidateFixtureSchemas()` → Helper to validate generated MCP tool schemas
    - Collection fixture definition for Xunit shared setup
  - `README.md` — Documentation of fixture usage for Fry (test specialist)

- Fixtures address Farnsworth's finding: unit tests validated helper behavior, but NOT end-to-end schema generation.
  - Fixtures are ready to pass directly to McpToolFactoryV2 for schema generation
  - No mocking/stubbing — real CommandInfo objects created via PowerShell
  - Designed for integration test to validate schema parameter types are correct (object→string for proxies, etc.)

- Build validation:
  - Fixtures compile clean (0 errors, 0 warnings in fixture code)
  - Committed: `test(#211): Add fixtures for proxy & >16-param method-generation tests`

**Files added:**
- `PoshMcp.Tests/Fixtures/ProxyTestFixtures.cs` (289 lines)
- `PoshMcp.Tests/Fixtures/Pr211IntegrationFixtureSetup.cs` (167 lines)
- `PoshMcp.Tests/Fixtures/README.md` (125 lines)

## Learnings (2026-05-11)

- **PowerShell fixture creation pattern**: Use `New-Module -ScriptBlock { ... } | Export-ModuleMember` to create synthetic PSModuleInfo objects. The `Invoke()` result wraps output in PSObjects — use `.BaseObject` to unwrap and `OfType<T>()` to filter by actual type (not PSObject).
- **Proxy module structure**: Export-PSSession creates modules with:
  - `PrivateData["ImplicitRemoting"] = true` (primary signal)
  - `Description` starting with "Implicit remoting for ..."
  - `RootModule` matching pattern `remoteIpMoProxy_*_*.psm1`
  - All parameters typed as `[object]` with no Mandatory flag
- **Read-only PSModuleInfo properties**: Properties like `RootModule` and `ModuleType` are not publicly settable. Access backing fields via reflection (`_propertyName` or `_lowercaseFirstLetter` pattern) to mutate in test fixtures.
- **Test infrastructure layering**: Separate static factories (`ProxyTestFixtures`) from test runner infrastructure (`Pr211IntegrationFixtureSetup`). Factories create objects, runner handles caching, validation, and Xunit collection fixture protocol.
- **End-to-end validation path**: Unit tests verify individual helper behavior; integration tests validate that helpers compose correctly through the full MCP tool schema generation pipeline. Fixtures bridge the gap by providing realistic CommandInfo inputs that exercise both code paths (proxy detection + high-parameter delegate emit).
- **File structure**: New test fixtures go in `PoshMcp.Tests/Fixtures/` (parallel to `Unit/`, `Integration/`, `Functional/`). Include README documenting usage for teammates who will consume the fixtures.
- **Xunit analyzer**: Prefer `Assert.Contains(item, collection)` or `Assert.NotEmpty(collection)` with LINQ filters rather than `Assert.True(collection.Any(...))` — the analyzer catches verbose assertion patterns and suggests idiomatic xUnit.

---

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
### Auth enforcement bypass despite Enabled: true (2026-05-01)

- **Root cause:** `WebApplicationBuilder`'s `ConfigurationManager` starts with the container's baked-in `appsettings.json` (`Authentication.Enabled: false`). Even though the user's custom `PoshMcp/appsettings.json` (with `Enabled: true`) is added later via `builder.Configuration.AddJsonFile(...)`, the default `appsettings.json` was winning over the custom file, causing `authConfigValue.Enabled = false` at line 1800 and `IOptions<AuthenticationConfiguration>.Value.Enabled = false` at middleware setup time.
- **Evidence of the bug:** `/.well-known/oauth-protected-resource` returned 404 (endpoint not mapped because `config.Enabled = false`). No `WWW-Authenticate` header on unauthenticated requests. `ToolListAuthorizationFilter` returned ALL tools to unauthenticated user (filter short-circuits when `authConfig.Enabled = false`).
- **Misleading diagnostic:** The `get-configuration-troubleshooting` and `get-configuration-guidance` tools showed `enabled: true` — but they read from the config FILE directly via `ConfigurationLoader.BuildRootConfiguration(configurationPath)`, not from `IOptions`. The DI runtime had `Enabled: false` while the diagnostic tools showed `true`.
- **Why the v0.9.2 IOptions fix didn't fix this:** That fix addressed a different case: `Enabled: false` → IOptions always showed the default `false` even when no guard was hit. The *current* bug is: even when `Enabled: true` in the custom file, `builder.Configuration` returns `false` due to the baked-in base `appsettings.json` winning the precedence battle.
- **Fix (this session):** Changed `RunHttpTransportServerAsync` to build a dedicated `authRootConfig` via `ConfigurationLoader.BuildRootConfiguration(finalConfigPath, reloadOnChange: false)` — reading ONLY from the custom file + env vars, same as diagnostic tools. Three call sites updated:
  - `authConfigValue` (line ~1806): now reads from `authRootConfig` instead of `builder.Configuration`
  - `AddOptions<T>().Configure(opts => authRootConfig...)`: binds IOptions directly from `authRootConfig`
  - `AddPoshMcpAuthentication(authRootConfig)`: JWT Bearer and McpAccess policy now configured from correct source
- **Key rule:** Never use `WebApplicationBuilder.Configuration` as the source for security-gate decisions when a custom config file is involved. The `WebApplicationBuilder` default config chain always includes the baked-in `appsettings.json` which may have different (and unsafe) defaults. Use `ConfigurationLoader.BuildRootConfiguration(configPath)` for auth configuration — it reads only what the user explicitly configured.
- **Files modified:** `PoshMcp.Server/Program.cs`

### ConfigureCorsForMcp also used builder.Configuration (2026-05-01)

- **Discovery:** After applying the main auth fix (authRootConfig for IOptions/AddPoshMcpAuthentication/authConfigValue), `ConfigureCorsForMcp` still read from `builder.Configuration`. This would cause CORS to silently open up (`AllowAnyOrigin`) even when auth is enabled, because `authConfig.Enabled` resolved to `false` from the baked-in base appsettings.
- **Fix:** Changed method signature from `ConfigureCorsForMcp(WebApplicationBuilder builder)` to `ConfigureCorsForMcp(WebApplicationBuilder builder, IConfigurationRoot authRootConfig)`, replacing `builder.Configuration.GetSection("Authentication")` with `authRootConfig.GetSection("Authentication")`. Updated the call site at line ~1781 to pass `authRootConfig`.
- **Pattern:** After applying an auth config source fix, grep ALL call sites for `builder.Configuration.GetSection("Authentication")` — any remaining uses are potential auth bypasses. The `authRootConfig` should be the single source of truth for all auth-gated decisions in the server setup method.
- **Commit:** 351c42c
- **Files modified:** `PoshMcp.Server/Program.cs`


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

### 2026-05-07: v0.11.0 release shipped (cross-agent note from Scribe)
Your work landed in v0.11.0 (csproj 0.10.0 → 0.11.0, CHANGELOG entry, release notes at docs/release-notes/0.11.0.md). The release narrative credits the OOP maturity wave: Pool default flip (#196/#208), cancellation propagation across all modes (#207), benchmarks harness + findings (#193/#194/#195/#205), OOP host extraction (#190/#198), bug fixes (#203/#189), CWE-117 log-injection hardening, minimum workflow permissions, and SECURITY.md. Tag/push deferred to Steven.
1. Add `<EmbeddedResource>` entries in `.csproj` with `Link` paths using backslash separators to control the manifest resource name:
   ```xml
   <EmbeddedResource Include="..\Dockerfile" Link="Dockerfiles\Dockerfile" />
   ```

2. The manifest name is: `{AssemblyName}.{Link path with backslashes replaced by dots}`.  
   **Important:** The prefix is the *assembly name* (`<AssemblyName>` or project name), not the namespace. For this project, the assembly is `PoshMcp`, so the resource is `PoshMcp.Dockerfiles.Dockerfile` — NOT `PoshMcp.Server.Dockerfiles.Dockerfile`.

3. Read via `Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)`.

4. When the resource isn't found (e.g., file wasn't embedded, or path was custom), fall back to `File.ReadAllText()` so local dev still works.

5. Skip disk-existence checks (`File.Exists`) for paths that are satisfied by embedded resources — in this case the `--generate-dockerfile` flow.

### `--generate-dockerfile` default corrected to "custom" (fixed current session)

**What was wrong:** The `build` command handler had:

```csharp
var buildType = string.IsNullOrWhiteSpace(type)
    ? (generateDockerfile ? "base" : "custom")
    : type.ToLowerInvariant();
```

This meant `poshmcp build --generate-dockerfile` defaulted to `buildType = "base"`, which maps
to the repo root `Dockerfile` — the file for building PoshMcp from source. That is the wrong
template for users; they want `examples/Dockerfile.user`, which extends the published base image.

**How it was fixed:** Both paths (with and without `--generate-dockerfile`) now default to `"custom"`:

```csharp
var buildType = string.IsNullOrWhiteSpace(type)
    ? "custom"
    : type.ToLowerInvariant();
```

Users who explicitly want the source-build Dockerfile can still pass `--type base`.

**Also updated:** `examples/Dockerfile.user` — clarified that `install-modules.ps1` must be
downloaded from the repo, and that the `COPY appsettings.json` line is a placeholder the user
should update to their own path (removed the repo-internal `examples/appsettings.basic.json` path).

- Added --appsettings to poshmcp build: injects COPY line into generated Dockerfile; for build mode stages file to CWD as poshmcp-appsettings.json, uses temp Dockerfile (.poshmcp-build.dockerfile), cleans up both temp files after build
- Fixed poshmcp build 'Dockerfile not found' — embedded resources bypass the disk check; always generate temp dockerfile from embedded resource so build works outside the poshmcp repo

### 2026-05-01T16:16:11Z - VS Code OAuth Redirect Fix - Release v0.9.4 (Bender contribution)

- Diagnosed VS Code OAuth redirect root cause: missing resource_metadata in WWW-Authenticate header
- Implemented Fix 1: JwtBearerEvents.OnChallenge in AuthenticationServiceExtensions.cs
- Implemented Fix 2: ApiKeyAuthenticationHandler metadata URL configuration
- All 574 tests passing (green build)
- Coordination: Worked with Amy (release engineering), Leela (docs), Fry (regression tests)
### 2026-05-12: Issue #233 — RemoteToolSchema XML doc fix (PR #235, draft)

**Requested by:** Steven. Spec 010 step 10 / FR-560. Doc-only.

**Current behavior of `RemoteToolSchema.Description` (verified, not speculated):**
- Populated exclusively in `oop-host-pool.ps1` ~L824-829 (the in-process path does NOT use this type at all).
- Source: `Get-Help -Name $cmd.Name -ErrorAction SilentlyContinue`; if `.Synopsis` is non-null, `Trim()` it; assign to `description` only if it differs from `cmd.Name`. Otherwise the field stays as initial value `''` (empty string).
- There is NO fallback to parameter set syntax. The prior XML doc claim was wrong on both counts.
- Downstream (`RemoteToolSchemaToMcpToolConverter` / `OutOfProcessToolAssemblyGenerator`) treats empty description as "use the bare command name as the description" — confirmed by the spec scenario table line "(Synopsis only) | ... | (raw syntax)".

**No other stale property docs found in `RemoteToolSchema.cs`:**
- `Name`: accurate ("full command name").
- `ParameterSetName`: accurate ("__AllParameterSets" sentinel).
- `Parameters`: accurate.
- `RemoteParameterSchema.TypeName`: accurate (already explains string-not-Type rationale).
- `IsMandatory` / `Position`: no doc comments, not stale (just absent — separate concern, not in scope of #233).

**PR:** https://github.com/usepowershell/PoshMcp/pull/235 (draft, base `main`, head `squad/233-remotetoolschema-doc`).

**Build:** `dotnet build PoshMcp.Server -c Release` succeeds; only warning is the pre-existing NU1510 about `System.Security.Cryptography.Xml` package pruning — unrelated to this change.

**Don't regress:** When spec 010's sourcing rule lands (FR-510 et al, parameter description from `Get-Help` `.Parameters.parameter.description`), this XML doc will need an update *again* to describe the new precedence. The current text is correct for today's behavior, not the post-spec-010 behavior.

---


## Learnings (2026-05-13) — issue #230 doctor descriptionSource

**What landed:** Spec 010 sequencing step 8 — added descriptionSource to doctor JSON output identifying the resolved precedence step per command (FR-500 chain) and per parameter (FR-510 chain). FR-582 + FR-583 + SC-207 all addressed in one PR.

**Vocabulary location is single source of truth.** DescriptionSourceVocabulary.ToWireValue(...) (in PoshMcp.Server.PowerShell) is the only place that maps the ToolDescriptionSource/ParameterDescriptionSource enums to wire literals (`synopsis|description|syntax|name` and `helpParameter|helpMessage|validateSet|typeFallback`). Issue #231 (OTel counters by description source) MUST reuse this — already documented in the decisions inbox for Amy's review.

**Tracker design — parallel, not extension.** Did NOT extend `IToolMetadataSource` (which would have rippled into every implementer and broken the OOP seam landed in #228). Instead introduced `IToolDescriptionSourceTracker` as a separate optional dependency the factory accepts via constructor overloads. All existing constructors chain through with `descriptionSourceTracker: null` so no caller breaks. The tracker is recorded at the existing `Resolve*` call sites in `McpToolFactoryV2` (in-proc) AND in `CreateRemoteCommandMetadataMapping` / `BuildRemoteParameterDescriptionMap` (out-of-process — full OOP coverage).

**Aggregation rule (from FR-501/FR-511):** `ToolDescriptionSourceTracker` uses first-recorded-wins per (command) and (command, parameter) pair. This matches the spec invariant that one command produces one tool description across all parameter sets, and a given parameter resolves to one source regardless of which set it appears in.

**Doctor entry shape — by command, not by tool.** Initially built `BuildToolDescriptionEntries` to iterate `McpServerTool` and reverse-map sanitized names back to PowerShell command names. Aborted: `SanitizeMethodName` (in `PowerShellAssemblyGenerator`) does `CamelCaseToSnakeCase` + dash-to-underscore + lowercase + parameter-set suffix — lossy and impossible to reliably reverse (e.g., `Get-AzContext` → `get_az_context`). Switched to iterating the tracker directly and emitting one entry per recorded command. Same data, cleaner semantics, matches FR-501 (per-command granularity).

**`HelpAwareToolMetadataSource` for CLI doctor.** Production wires HelpAware via DI in StdioServerHost/HttpServerHost. CLI doctor was previously using `DefaultToolMetadataSource` (the pre-spec fallback) — would have under-reported precedence steps. `BuildDoctorReportForCliAsync` now explicitly instantiates `new HelpAwareToolMetadataSource()` so reported sources match production behavior.

**Func signature change rippled cleanly.** `BuildDoctorReportForCliAsync` Func type changed from 4-arg to 6-arg (added `IToolMetadataSource?, IToolDescriptionSourceTracker?`). Only one external caller in tests (`ProgramTests.BuildDoctorReportForCliAsync_WhenStartupAndDiscoveryFail_StillReturnsReportWithErrors`) — updated the lambda discards. Method group conversion in Program.cs picked up the new overload automatically.

**Coordinate with spec 006:** doctor restructure (#239) put `functionsTools` in its own section with `toolNames`, `namedToolCount`, etc. Added `tools` field as a sibling — not nested under any existing field — so future spec additions to `functionsTools` stay independent.

**#242 observation (FR-510 parameter descriptions not reaching MCP `inputSchema`):** Looked but did not deeply audit. The PR will note this is a separate concern. Hypothesis: `BuildParameterDescriptionMap` does record into the description map, but the description map may not be flowing into the JSON Schema property definition emitted to MCP. Worth its own investigation — different code path from doctor output (which now reads the tracker, not the schema).

**Test coverage (12 unit tests):** all 4 tool sources + all 4 parameter sources via real `HelpAwareToolMetadataSource` resolution + tracker first-wins semantics + JSON round-trip + vocabulary mapping (both enums) + `BuildToolDescriptionEntries` empty/populated paths. 521 unit tests now passing.

## Learnings — 2026-05-13 (Issue #242)

**PowerShell SDK: PSObject wrapper vs BaseObject for Get-Help.parameters**

Get-Help returns .parameters as a PSObject whose BaseObject is a marker PSCustomObject with no public members. The synthesized .parameter[] collection only exists on the **wrapper**, NOT on BaseObject. Calling .BaseObject first dereferences to the marker and silently drops the array.

Rule: when working with PowerShell adapted/synthesized members (especially Get-Help output, format data, custom property sets), access `Properties["name"]` on the PSObject wrapper directly. Only fall back to BaseObject when the wrapper does not expose the member you need. Never reflexively unwrap.

This bug shipped silently because the resolver returned the right strings for parameters it could find, but the array itself was empty — so callers got "no parameters in help" rather than an exception.

## Learnings — 2026-05-13 (Issue #242)

**PowerShell SDK: PSObject wrapper vs BaseObject for Get-Help.parameters**

Get-Help returns .parameters as a PSObject whose BaseObject is a marker PSCustomObject with no public members. The synthesized .parameter[] collection only exists on the **wrapper**, NOT on BaseObject. Calling .BaseObject first dereferences to the marker and silently drops the array.

Rule: when working with PowerShell adapted/synthesized members (especially Get-Help output, format data, custom property sets), access `Properties["name"]` on the PSObject wrapper directly. Only fall back to BaseObject when the wrapper does not expose the member you need. Never reflexively unwrap.

This bug shipped silently because the resolver returned the right strings for parameters it could find, but the array itself was empty — so callers got "no parameters in help" rather than an exception.

### 2026-05-14: v0.13.0 released from main (tag pending CI)
**By:** Scribe (cross-agent note from coordinator)
**What:** v0.13.0 commits landed on origin/main: housekeeping `5847efb` + release `a2b9c3e` (csproj 0.12.3 → 0.13.0, CHANGELOG, docs/release-notes/0.13.0.md). Tests 777/0/7. Tag NOT yet created — pending CI green on `a2b9c3e`.
**Marquee:** Spec 010 — Help-aware tool descriptions. In-process + OOP byte-identical schemas, `IToolMetadataSource` seam, FR-500/510/540 precedence, `HelpAwareToolMetadataSource` as default, doctor `descriptionSource` reporting, OTel counters, parity tests. Includes #222 (SwitchParameter round-trip) and #248 (parameter descriptions on inputSchema).

## 2026-05-15: PR #266 — fix(doctor) #261 Pool-mode display

### What I fixed
Doctor report was showing `effectiveProcessPoolSize: 0` and `effectiveMinHealthyForStartup: 0` when `SubprocessHostMode = Pool`. Those knobs are inert in Pool mode (they only apply to ProcessPool), so the value was technically correct but read like a bug to operators. Changed both to render `"n/a (Pool mode)"` outside ProcessPool, mirroring the existing `EffectiveRunspacePoolSize` pattern.

### Files touched
- `PoshMcp.Server/Diagnostics/DoctorReport.cs` — promoted `EffectiveProcessPoolSize` and `EffectiveMinHealthyForStartup` from `int` to `string` (default `string.Empty`).
- `PoshMcp.Server/Diagnostics/DoctorService.cs` — refactored the inline ternaries into an explicit `if (ProcessPool) { compute and ToString } else { "n/a (Pool mode)" }` block. ProcessPool semantics unchanged (clamping + defaults preserved).
- `PoshMcp.Server/Diagnostics/DoctorTextRenderer.cs` — no change. The renderer only emits `process-pool`/`min-healthy` lines when `HostMode == ProcessPool`, so the new strings flow through cleanly.
- `PoshMcp.Tests/Unit/Diagnostics/DoctorOutOfProcessSectionTests.cs` — new, 5 tests: Pool n/a, ProcessPool integer-string, min-healthy clamping, default pool size, not-applicable.

### Test approach
`DoctorService` is internal but the test project has `InternalsVisibleTo`. `OutOfProcessSection` is a public sealed record. So I called `DoctorService.BuildOutOfProcessSection` directly with synthesized `PowerShellConfiguration` instances and `NullLoggerFactory.Instance`. No FS, no process spawning — pure Unit tier.

### Gotchas
- `DoctorService` and `DoctorReport` live in the **root `PoshMcp` namespace**, not `PoshMcp.Server.Diagnostics` (despite the folder). My first test file used the folder-shaped namespace and failed to compile. Use `using PoshMcp;` not `using PoshMcp.Server.Diagnostics;`.
- `gh pr create` failed with `Unauthorized: As an Enterprise Managed User` on the `stmuraws_microsoft` account. Had to `gh auth switch -u usepowershell` first. Worth remembering for future PRs to `usepowershell/PoshMcp`.

### Outcome
PR #266 — https://github.com/usepowershell/PoshMcp/pull/266 — marked ready for review, labeled `squad` + `squad:bender`. 54 doctor tests green, full server build clean.

