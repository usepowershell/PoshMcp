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
