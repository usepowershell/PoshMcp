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

