# Hermes Work History
- **20260403T135630Z**: ✓ Docker fixes & scripts reviews compiled and merged into decision ledger.
- **20260408T000000Z**: ✓ Reviewed/recorded deploy.ps1 hardening for transient ACR OAuth EOF failures: bounded retry loops, transient error classification, and improved failure diagnostics.
- **20260418T000000Z**: ✓ Rebased feature/002-tests onto main; resolved 5 add/add conflicts (McpResources + McpPrompts config classes, kept main implementation); removed Skip attrs from 16 integration tests (8 McpResources + 8 McpPrompts); all 16 passed; force-pushed.
# Hermes Work History
## Project Context
**Project:** PoshMcp - Model Context Protocol (MCP) server for PowerShell
**Tech Stack:** .NET 10, C#, PowerShell SDK, OpenTelemetry, ASP.NET Core, xUnit
**Primary User:** Steven Murawski
**Key Files:**
- `PoshMcp.Server/PowerShell/PowerShellRunspaceHolder.cs` - Singleton runspace management
- `PoshMcp.Server/PowerShell/PowerShellRunspaceImplementations.cs` - Runspace implementations
- `PoshMcp.Server/PowerShell/PowerShellAssemblyGenerator.cs` - Dynamic assembly generation
- `PoshMcp.Server/PowerShell/PowerShellCleanupService.cs` - Cleanup lifecycle
- `PoshMcp.Server/PowerShell/PowerShellConfiguration.cs` - Configuration model
### 2026-04-03: Session Summary
**Status:** 2026-03-27 work (PowerShell streams refactoring, multi-tenant review, deployment script patterns) complete.
**Review Results:** Amy's multi-tenant implementation APPROVED (9/10 PowerShell quality).

### 2026-04-08: Serialization normalization fixes recorded

**Context:** Closed out the serializer migration fixes for string and nested object handling.

**Key learnings:**
- Scalar `PSObject.BaseObject` values need an early leaf-value path before property enumeration
- Nested PowerShell and CLR objects should be normalized into JSON-safe scalars, dictionaries, and arrays before `System.Text.Json` runs
- Serialization fixes need paired coverage so live execution and cached outputs preserve the same shape

### 2026-07: Large result set hang analysis (Get-Process)

**Context:** Diagnosed why `Get-Process` and similar cmdlets hang when called via MCP.

## Learnings

**Execution pipeline flow (key facts):**
- `ExecutePowerShellCommandTyped` (`PowerShellAssemblyGenerator.cs:534`) is the single entry point for all tool invocations.
- It calls `runspace.ExecuteThreadSafeAsync<string>(ps => { ... return Task.FromResult(...) })` — the lambda is synchronous; it never awaits anything.
- `InvokePowerShellSafe` (line 1008) calls `ps.Invoke()` — fully synchronous, no CancellationToken support.
- The singleton `PowerShellRunspaceHolder` guards the runspace with `SemaphoreSlim(1,1)`, so a hung invocation blocks all subsequent tool calls.
- A `TimeoutException` catch path exists in the outer try/catch, but nothing in the code ever raises it — there is effectively zero timeout enforcement.

**Serialization depth hazard for CLR objects:**
- `PowerShellObjectSerializer.GetSafeProperties` wraps any CLR object in a `PSObject` and enumerates ALL reflected properties.
- `System.Diagnostics.Process` has ~50 properties. Several (`Modules`, `MainModule`, `Threads`, `Handle`) make Win32 API calls that can block indefinitely on protected or system processes — these stalls are not caught by the surrounding `try/catch` because they don't throw, they block.
- 200-300 processes × 50 properties each = ~10,000–15,000 property accesses per `Get-Process` call.

**`Tee-Object` in the pipeline amplifies memory pressure:**
- Every tool invocation pipes through `Tee-Object -Variable LastCommandOutput` to cache results.
- For `Get-Process`, this keeps all 200-300 live `Process` objects (with OS handles) in memory simultaneously through the serialization pass.

**Recommended fix order:**
1. **Result count cap** (Approach B, quick win): truncate to ~50 results before serialization; include totalCount in response.
2. **Property shaping for known CLR types** (Approach A, medium effort): type-specific shapersfor `Process`, `Service`, `FileInfo` that emit only AI-useful properties.
3. **Async invocation with CancellationToken** (Approach C, high effort): use `InvokePowerShellSafeAsync` in the main execution path and thread the CancellationToken through.

### 2026-07: PropertySetDiscovery and serializer refinement

**Context:** Phase 3 crash recovery — implemented DefaultDisplayPropertySet discovery and refined serializer.

**Key files:**
- `PoshMcp.Server/PowerShell/PropertySetDiscovery.cs` — Discovery of DefaultDisplayPropertySet via Get-Command OutputType + Get-TypeData. Uses temporary runspace, ConcurrentDictionary cache, best-effort (returns null on failure).
- `PoshMcp.Server/PowerShell/PowerShellObjectSerializer.cs` — Refined `NormalizePSPropertyValue`: IDictionary now recursively normalized instead of `.ToString()` (dictionaries are bounded key-value maps). IEnumerable kept as `.ToString()` (expensive to enumerate, e.g., ProcessModuleCollection).

**Design decisions:**
- PropertySetDiscovery uses temporary runspace, NOT the singleton — runs at assembly generation time before the server is fully initialized.
- Two-step lookup: Get-Command → OutputType names → Get-TypeData → DefaultDisplayPropertySet.ReferencedProperties.
- DiscoverAll() shares a single runspace across all commands for startup efficiency.
- IDictionary vs IEnumerable split in shallow path: dictionaries are safe JSON maps; enumerables may trigger OS calls.

### 2026-04-10: Recovery learnings for module layout and host-script safety

**Key learnings:**
- The split `integration/Modules/*` layout is the canonical integration-module shape; umbrella-module path assumptions are stale.
- Partial vendored trees like `integration/Modules/Az.AppConfiguration/2.0.1` are likely merge fallout and should be removed rather than patched around.
- Module discovery needs explicit import-before-discovery ordering when autoloading cannot be trusted.
- If the host script work resumes, keep stdout protocol-only, route diagnostics to stderr, and resolve commands through `Get-Command` plus `CommandInfo` invocation instead of string evaluation.

### 2026-04-11: Cross-agent update — Out-of-process execution plan filed

**Context:** Farnsworth filed a comprehensive OOP execution plan at `specs/out-of-process-execution.md`.

**Key points for Hermes:**
- Communication protocol is ndjson over stdin/stdout (supersedes the localhost TCP direction from 2026-04-10)
- Phase 3 (command discovery) involves the subprocess discovering commands via `Get-Command` and reporting back — similar to `PropertySetDiscovery` patterns Hermes already implemented
- `oop-host.ps1` uses the host-script safety rules Hermes helped define: stdout protocol-only, stderr for diagnostics, `Get-Command` + `CommandInfo` invocation
- Phase 6 (integration testing) will use modules from `integration/Modules/` — the canonical split layout Hermes helped establish
- Crash recovery with automatic subprocess restart and exponential backoff

### 2026-04-11: Created oop-host.ps1 — OOP subprocess host script (Issue #57, Phases 2-4)

**File:** `PoshMcp.Server/PowerShell/OutOfProcess/oop-host.ps1`

**What was built:**
- Full ndjson protocol host script implementing all 4 methods: `ping`, `shutdown`, `discover`, `invoke`
- Strict stdout/stderr separation: only ndjson on stdout, diagnostics on stderr with `[oop-host]` prefix
- `[Console]::ReadLine()` for stdin (not Read-Host), `[Console]::Out.WriteLine()` + Flush for stdout

**Discovery handler design decisions:**
- Module import uses `Import-Module -Name -ErrorAction Stop` — fails fast on bad modules with error response (doesn't crash host)
- Commands discovered via explicit `functionNames` list AND module+pattern matching, then deduplicated by name
- Common parameters excluded via hardcoded allowlist (14 params)
- Description sourced from `Get-Help` synopsis, best-effort (empty on failure)
- Each ParameterSet gets its own RemoteToolSchema entry with `Name`, `Description`, `ParameterSetName`, `Parameters`
- Parameter fields: `Name`, `TypeName` (ParameterType.FullName), `IsMandatory`, `Position`

**Invoke handler design decisions:**
- PSCustomObject from `ConvertFrom-Json` converted to hashtable via `.PSObject.Properties` enumeration for splatting
- SwitchParameter detection: inspects `CommandInfo.ParameterSets` for SwitchParameter types, converts true→`[switch]$true`, removes false entries
- Results serialized with `ConvertTo-Json -Depth 4 -Compress`
- Non-terminating errors tracked via `$Error.Count` → `hadErrors` field
- Terminating errors caught and returned as error response

**Error handling patterns:**
- Malformed JSON: logged to stderr, skipped (no response — no id to respond to)
- Missing `id`: logged to stderr, skipped
- Missing `method`: error response with code -1
- Unknown method: error response with code -1
- Unhandled exceptions in handlers: caught by outer try/catch, error response returned
- EOF on stdin: clean exit

### 2026-04-11: OOP environment customization (issue #67)

**Context:** Added `setup` protocol method to oop-host.ps1 so OOP subprocess gets the same environment customization as in-process host.

**Key design decisions:**
- `setup` method called after `ping`, before `discover` — mirrors PowerShellEnvironmentSetup.ApplyEnvironmentConfiguration() ordering
- Setup is optional: only sent when EnvironmentConfiguration has content (module paths, install/import modules, startup scripts, or PSGallery trust)
- Setup errors throw InvalidOperationException, failing server startup — fail-fast is correct for environment misconfiguration
- `SetupAsync()` is a concrete method on OutOfProcessCommandExecutor (not on ICommandExecutor interface) since it's OOP-specific

**Key files modified:**
- `PoshMcp.Server/PowerShell/OutOfProcess/oop-host.ps1` — Added Invoke-SetupHandler function and `setup` dispatch
- `PoshMcp.Server/PowerShell/OutOfProcess/OutOfProcessCommandExecutor.cs` — Added SetupAsync() method
- `PoshMcp.Server/Program.cs` — Updated StartOutOfProcessExecutorIfNeededAsync() to call SetupAsync
- `specs/out-of-process-execution.md` — Updated protocol docs with setup method

**PowerShell patterns used:**
- `[System.Environment]::ExpandEnvironmentVariables()` for path expansion
- `[System.IO.Path]::PathSeparator` for cross-platform PSModulePath construction
- `Install-Module` with version constraint params (RequiredVersion, MinimumVersion, MaximumVersion)
- `Get-Module -ListAvailable` to skip already-installed modules
- `Invoke-Expression` for startup scripts (consistent with in-process host behavior)

### 2026-07: Unserializable parameter type filtering (Issue #89)

**Context:** Commands with parameters whose types can't be serialized to JSON need to be handled gracefully.

**Key files modified:**
- `PoshMcp.Server/PowerShell/PowerShellParameterUtils.cs` — Added `IsUnserializableType(Type)` static method
- `PoshMcp.Server/PowerShell/PowerShellAssemblyGenerator.cs` — `GenerateMethodForCommand` changed from `void` to `bool`; filtering logic added
- `PoshMcp.Tests/Unit/UnserializableTypeTests.cs` — 33 unit tests for the new method

**Filtering rules implemented:**
1. Optional parameter with unserializable type → drop the parameter silently from the schema
2. Mandatory parameter with unserializable type in a parameter set → skip the entire parameter set (return false)
3. All parameter sets skipped for a command → command gets no MCP tool (emergent), logged as warning

**Unserializable type set:**
- `PSObject`, `ScriptBlock`, `System.Object`
- `IntPtr`, `UIntPtr`, pointer and by-ref types
- `Delegate` and all derived types (Action, Func<>, …)
- `Stream` and derived (FileStream, MemoryStream, …)
- `WaitHandle` and derived
- `System.Reflection.Assembly`
- `System.Management.Automation.PowerShell` (the automation class, not the language)
- All `System.Management.Automation.Runspaces.*` types (Runspace, RunspacePool, …)
- Arrays whose element type is unserializable

**Design decisions:**
- `IsUnserializableType` lives in `PowerShellParameterUtils` alongside the other parameter helpers
- `GenerateMethodForCommand` returns bool (false = skipped) rather than throwing, to preserve clean caller control flow
- Common parameter exclusion (`IsCommonParameter`) still runs first; unserializable check runs second on the already-filtered list
- Arrays of unserializable types are also unserializable (recursive check on element type)

### 2026-07: doctor command resolution diagnostics (Issue #91)

**Context:** `poshmcp doctor` showed [MISSING] for configured commands with no explanation of why.

**Changes made (Program.cs):**
- `ConfiguredFunctionStatus` record: added nullable `ResolutionReason` field (default `null`)
- New `DiagnoseMissingCommands(IReadOnlyList<string>, PowerShellConfiguration)` method: runs PS introspection via `IsolatedPowerShellRunspace` for each missing command
- New `EscapeForPowerShell(string)` helper: single-quote escaping for safe PS script injection
- `RunDoctorAsync`: enriches status list with reasons before text/JSON output
- `BuildDoctorJson`: same enrichment so JSON payload includes `resolutionReason` per `configuredFunctionStatus` entry
- Text output: adds indented `reason:` line under each [MISSING] entry

**Diagnostic logic (DiagnoseMissingCommands):**
1. `Get-Command -Name <name>` in isolated runspace → if found, report "all parameter sets skipped due to unserializable types"
2. For each configured module: `Get-Module -Name <module> -ListAvailable` → if missing, report "module not in PSModulePath"
3. If module available: `Import-Module; Get-Command -Module <module> -Name <name>` → if not found, report "module does not export command"
4. If found in module → report "command in module but not loaded at discovery time"
5. No modules configured and command not found → report "command not found in PS session"

**Design decisions:**
- Uses `IsolatedPowerShellRunspace` (not singleton) to avoid interfering with server state
- All diagnostics for a doctor call share ONE isolated runspace for efficiency
- Local function `DiagnoseOneCommand` inside the `ExecuteThreadSafe` lambda avoids explicit `System.Management.Automation.PowerShell` type reference in Program.cs
- For JSON format, diagnostics run in both `RunDoctorAsync` (wasted) and `BuildDoctorJson` (used) — acceptable for a non-hot diagnostic command path
- `ConfiguredFunctionStatus` uses positional record syntax with `ResolutionReason = null` default for backwards compatibility
