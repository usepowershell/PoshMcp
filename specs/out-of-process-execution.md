# Out-of-Process PowerShell Execution

## Problem Statement

PoshMcp currently executes all PowerShell commands in-process using the `Microsoft.PowerShell.SDK` embedded runtime. This works for simple commands (`Get-Process`, `Get-ChildItem`, etc.) but **crashes or deadlocks** when loading heavy modules like `Az.*` or `Microsoft.Graph.*`.

These modules:
- Load native assemblies that conflict with the .NET host process
- Register type accelerators and format data that corrupt the in-process runspace
- Pull in hundreds of dependent assemblies (the `integration/Modules/` corpus contains ~100 Az modules and ~40 Microsoft.Graph modules)
- Can exhaust memory or hang during `Import-Module` inside the shared PowerShell SDK runspace

The out-of-process (OOP) execution model solves this by running PowerShell commands in a **separate `pwsh` process** that is fully isolated from the MCP server's .NET runtime. If a module crashes, only the subprocess dies — the MCP server stays healthy and can restart it.

## Architecture Overview

```
┌──────────────────────────────┐
│  MCP Server (.NET 8 host)    │
│                              │
│  Program.cs                  │
│    ├─ CreateToolFactory()    │
│    │   (routes on RuntimeMode│
│    │    InProcess vs OOP)    │
│    │                         │
│    └─ OutOfProcessExecutor   │
│        Lease (lifecycle)     │
│                              │
│  McpToolFactoryV2            │
│    ├─ In-process path        │
│    │   (PowerShellAssembly   │
│    │    Generator)           │
│    │                         │
│    └─ OOP path               │
│        ├─ ICommandExecutor   │
│        │   .DiscoverCommands │
│        │   Async()           │
│        └─ OutOfProcess       │
│            ToolAssembly      │
│            Generator         │
│            .GenerateAssembly │
│            (schemas)         │
│                              │
└───────────┬──────────────────┘
            │ stdin/stdout
            │ JSON-RPC over
            │ newline-delimited
            │ JSON (ndjson)
            │
┌───────────▼──────────────────┐
│  pwsh subprocess             │
│                              │
│  Persistent process:         │
│  - Import-Module on demand   │
│  - Execute commands          │
│  - Return JSON results       │
│  - Report available commands │
│    and parameter metadata    │
│                              │
└──────────────────────────────┘
```

### Communication Protocol

**Stdin/stdout newline-delimited JSON (ndjson)** — each message is a single JSON object terminated by `\n`.

We use a simple request/response JSON-RPC-like protocol over the subprocess's stdin/stdout streams. This avoids port allocation, firewall concerns, and platform-specific IPC quirks compared to TCP or named pipes.

> **Decision change from prior history:** The 2026-04-10 history note suggested "localhost TCP" as the lowest-complexity option. After further analysis, **stdin/stdout is lower complexity** — no port conflicts, no firewall, no connection handshake, works identically on Windows/Linux/macOS, and the .NET `Process` API provides redirected streams natively. TCP remains a future option if multi-client scenarios emerge.

**Message format:**

```jsonc
// Request (server → pwsh)
{"id":"<guid>","method":"<method>","params":{...}}

// Response (pwsh → server)
{"id":"<guid>","result":{...}}
// or
{"id":"<guid>","error":{"code":<int>,"message":"<string>"}}
```

**Methods:**

| Method | Direction | Purpose |
|--------|-----------|---------|
| `discover` | server → pwsh | Import configured modules, return command schemas |
| `invoke` | server → pwsh | Execute a command with parameters, return results |
| `ping` | server → pwsh | Health check (subprocess alive?) |
| `shutdown` | server → pwsh | Graceful termination |

## Complete Type Inventory

All types live in namespace `PoshMcp.Server.PowerShell.OutOfProcess` and are created under `PoshMcp.Server/PowerShell/OutOfProcess/`.

### 1. `RuntimeMode` enum

**File:** `PoshMcp.Server/PowerShell/OutOfProcess/RuntimeMode.cs`

```csharp
namespace PoshMcp.Server.PowerShell.OutOfProcess;

/// <summary>
/// Controls whether PowerShell commands execute inside the server process
/// or in a separate pwsh subprocess.
/// </summary>
public enum RuntimeMode
{
    /// <summary>
    /// Execute commands using the embedded Microsoft.PowerShell.SDK runtime (default).
    /// </summary>
    InProcess,

    /// <summary>
    /// Execute commands in a persistent external pwsh subprocess.
    /// Required for modules that crash or conflict with the in-process runtime
    /// (e.g., Az.*, Microsoft.Graph.*).
    /// </summary>
    OutOfProcess,

    /// <summary>
    /// The configured runtime mode string was not recognized.
    /// </summary>
    Unsupported
}
```

**Referenced by:** `PowerShellConfiguration.RuntimeMode` property, `Program.NormalizeRuntimeModeValue()`, `Program.ResolveRuntimeMode()`, `McpToolFactoryV2.GetToolsListAsync()`, `Program.CreateToolFactory()`, `Program.StartOutOfProcessExecutorIfNeededAsync()`.

### 2. `ICommandExecutor` interface

**File:** `PoshMcp.Server/PowerShell/OutOfProcess/ICommandExecutor.cs`

```csharp
namespace PoshMcp.Server.PowerShell.OutOfProcess;

/// <summary>
/// Abstraction for executing PowerShell commands, either in-process or
/// via the out-of-process subprocess host.
/// </summary>
public interface ICommandExecutor : IAsyncDisposable
{
    /// <summary>
    /// Start the executor (e.g., launch the pwsh subprocess).
    /// Must be called before DiscoverCommandsAsync or InvokeAsync.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Import configured modules in the remote pwsh process and return
    /// schemas describing all discovered commands and their parameters.
    /// </summary>
    Task<IReadOnlyList<RemoteToolSchema>> DiscoverCommandsAsync(
        PowerShellConfiguration config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute a PowerShell command by name with the given parameters
    /// in the remote process and return the JSON-serialized result.
    /// </summary>
    Task<string> InvokeAsync(
        string commandName,
        IDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default);
}
```

**Referenced by:** `McpToolFactoryV2` constructor overload, `McpToolFactoryV2.GetOutOfProcessToolsListAsync()`, `Program.CreateToolFactory()`, `Program.StartOutOfProcessExecutorIfNeededAsync()`, `OutOfProcessToolAssemblyGenerator` constructor.

### 3. `RemoteToolSchema` class

**File:** `PoshMcp.Server/PowerShell/OutOfProcess/RemoteToolSchema.cs`

```csharp
namespace PoshMcp.Server.PowerShell.OutOfProcess;

/// <summary>
/// Schema describing a single PowerShell command discovered in the remote
/// pwsh subprocess, including its parameters and their types.
/// </summary>
public class RemoteToolSchema
{
    /// <summary>
    /// The full command name (e.g., "Get-AzContext").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable description (from Get-Help or parameter set syntax).
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The parameter set name this schema represents
    /// (null or "__AllParameterSets" for the default set).
    /// </summary>
    public string? ParameterSetName { get; set; }

    /// <summary>
    /// Parameters for this command/parameter-set combination.
    /// </summary>
    public List<RemoteParameterSchema> Parameters { get; set; } = new();
}

/// <summary>
/// Schema for a single parameter of a remote command.
/// </summary>
public class RemoteParameterSchema
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The .NET type name as a string (e.g., "System.String", "System.Int32",
    /// "System.Management.Automation.SwitchParameter").
    /// We use strings because the actual types may not be loadable in the
    /// server process.
    /// </summary>
    public string TypeName { get; set; } = "System.String";

    public bool IsMandatory { get; set; }
    public int Position { get; set; } = int.MaxValue;
}
```

**Referenced by:** `ICommandExecutor.DiscoverCommandsAsync()` return type, `McpToolFactoryV2.GetOutOfProcessToolsListAsync()`, `McpToolFactoryV2.CreateRemoteCommandMetadataMapping()`, `OutOfProcessToolAssemblyGenerator.GenerateAssembly()`.

### 4. `OutOfProcessCommandExecutor` class

**File:** `PoshMcp.Server/PowerShell/OutOfProcess/OutOfProcessCommandExecutor.cs`

```csharp
namespace PoshMcp.Server.PowerShell.OutOfProcess;

/// <summary>
/// Manages a persistent pwsh subprocess and communicates with it via
/// stdin/stdout ndjson to discover and invoke PowerShell commands.
/// </summary>
public class OutOfProcessCommandExecutor : ICommandExecutor
{
    // Constructor: OutOfProcessCommandExecutor(ILogger<OutOfProcessCommandExecutor> logger)
    // Fields: Process _process, StreamWriter _stdin, StreamReader _stdout,
    //         SemaphoreSlim _sendLock, ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending

    // ICommandExecutor implementation:
    // Task StartAsync(CancellationToken)
    // Task<IReadOnlyList<RemoteToolSchema>> DiscoverCommandsAsync(PowerShellConfiguration, CancellationToken)
    // Task<string> InvokeAsync(string, IDictionary<string, object?>, CancellationToken)
    // ValueTask DisposeAsync()
}
```

**Referenced by:** `Program.StartOutOfProcessExecutorIfNeededAsync()`, `OutOfProcessExecutorLease`.

### 5. `OutOfProcessToolAssemblyGenerator` class

**File:** `PoshMcp.Server/PowerShell/OutOfProcess/OutOfProcessToolAssemblyGenerator.cs`

```csharp
namespace PoshMcp.Server.PowerShell.OutOfProcess;

/// <summary>
/// Generates a dynamic assembly of MCP tool methods that delegate execution
/// to the out-of-process command executor instead of the in-process runspace.
///
/// Mirrors PowerShellAssemblyGenerator but generates IL that calls
/// ICommandExecutor.InvokeAsync() instead of the in-process runspace.
/// </summary>
public class OutOfProcessToolAssemblyGenerator
{
    // Constructor: OutOfProcessToolAssemblyGenerator(ICommandExecutor commandExecutor)
    // Methods:
    //   void GenerateAssembly(IReadOnlyList<RemoteToolSchema> schemas, ILogger logger)
    //   object GetGeneratedInstance(ILogger logger)
    //   Dictionary<string, MethodInfo> GetGeneratedMethods()
    //   void ClearCache()
}
```

**Referenced by:** `McpToolFactoryV2` constructor (OOP overload), `McpToolFactoryV2.GetOutOfProcessToolsListAsync()`, `McpToolFactoryV2.ClearCache()`.

### 6. Helper Script: `oop-host.ps1`

**File:** `PoshMcp.Server/PowerShell/OutOfProcess/oop-host.ps1`

A PowerShell script that runs inside the subprocess. It:
- Reads ndjson requests from stdin
- Handles `discover`, `invoke`, `ping`, `shutdown` methods
- Imports modules specified in `discover` params
- Uses `Get-Command` to introspect parameters and build `RemoteToolSchema` responses
- Executes commands and serializes results via `ConvertTo-Json`
- Writes ndjson responses to stdout, errors/diagnostics to stderr

This script is embedded as a resource or shipped alongside the server.

## Implementation Phases

### Phase 1: Stub Types (Fix Build Errors)

**Goal:** Restore `dotnet build PoshMcp.sln` to green by creating minimal stub implementations.

**Files to create:**

| File | Contents |
|------|----------|
| `PoshMcp.Server/PowerShell/OutOfProcess/RuntimeMode.cs` | Enum with `InProcess`, `OutOfProcess`, `Unsupported` |
| `PoshMcp.Server/PowerShell/OutOfProcess/ICommandExecutor.cs` | Interface with `StartAsync`, `DiscoverCommandsAsync`, `InvokeAsync` |
| `PoshMcp.Server/PowerShell/OutOfProcess/RemoteToolSchema.cs` | DTO classes `RemoteToolSchema` + `RemoteParameterSchema` |
| `PoshMcp.Server/PowerShell/OutOfProcess/OutOfProcessCommandExecutor.cs` | Class implementing `ICommandExecutor` — all methods throw `NotImplementedException` |
| `PoshMcp.Server/PowerShell/OutOfProcess/OutOfProcessToolAssemblyGenerator.cs` | Class with constructor + stub methods that throw `NotImplementedException` |

**Acceptance criteria:**
- `dotnet build PoshMcp.sln` succeeds with 0 errors
- All 13 current build errors resolved
- Existing in-process tests (`dotnet test`) still pass (OOP path is not exercised)

**Estimated scope:** ~5 small files, ~150 lines total.

### Phase 2: Core Subprocess Lifecycle

**Goal:** `OutOfProcessCommandExecutor` can launch, health-check, and gracefully shut down a `pwsh` subprocess.

**Implementation details:**

1. **`OutOfProcessCommandExecutor.StartAsync()`:**
   - Locate `pwsh` executable (check `PATH`, common install locations, configurable override)
   - Launch `Process` with `RedirectStandardInput = true`, `RedirectStandardOutput = true`, `RedirectStandardError = true`
   - Start a background reader task on stdout that parses ndjson responses, matches by `id`, and completes `TaskCompletionSource` entries in `_pending`
   - Start a background reader task on stderr that logs diagnostics
   - Send a `ping` request and await response to confirm the subprocess is alive

2. **`oop-host.ps1` bootstrap script:**
   - Stdin read loop: `while ($line = [Console]::ReadLine()) { ... }`
   - Parse JSON, dispatch by `method`
   - `ping` → respond `{"id":"...","result":{"status":"ok"}}`
   - `shutdown` → respond, then `exit 0`
   - Write responses to stdout as single-line JSON

3. **`DisposeAsync()`:**
   - Send `shutdown` request
   - Wait up to 5 seconds for process exit
   - Kill if not exited
   - Dispose streams

4. **Error handling:**
   - Process crash detection (monitor `Process.Exited` event)
   - Automatic restart with exponential backoff (configurable max retries)
   - Timeout on individual requests (configurable, default 30 seconds)
   - `CancellationToken` propagation

**Key design decisions:**
- One `pwsh` process per `OutOfProcessCommandExecutor` instance (1:1)
- Serialized access via `SemaphoreSlim` — one request in flight at a time initially (can be relaxed later with request multiplexing)
- The `oop-host.ps1` script is loaded via `-File` parameter to avoid PowerShell profile interference

**Files:**

| File | Action |
|------|--------|
| `OutOfProcessCommandExecutor.cs` | Implement `StartAsync`, `DisposeAsync`, ndjson send/receive, process management |
| `OutOfProcess/oop-host.ps1` | Create the subprocess host script (ping + shutdown handlers) |

### Phase 3: Command Discovery

**Goal:** `DiscoverCommandsAsync` imports modules in the remote `pwsh` and returns `RemoteToolSchema` objects.

**Implementation details:**

1. **`discover` method in `oop-host.ps1`:**
   - Receives: `{"method":"discover","params":{"modules":["Az.Accounts","Az.Compute"],"functionNames":["Get-Process"],"includePatterns":["*"],"excludePatterns":[]}}`
   - Imports specified modules via `Import-Module -Name <name> -ErrorAction Stop`
   - Runs `Get-Command` with configured filters
   - For each command, iterates `ParameterSets` and builds `RemoteToolSchema`:
     ```powershell
     foreach ($cmd in Get-Command ...) {
         foreach ($ps in $cmd.ParameterSets) {
             # Build parameter list excluding common params
             # Map ParameterType.FullName as TypeName string
             # Include IsMandatory, Position from ParameterAttribute
         }
     }
     ```
   - Returns array of schema objects as JSON

2. **`OutOfProcessCommandExecutor.DiscoverCommandsAsync()`:**
   - Build discover request from `PowerShellConfiguration` (modules, functionNames, includePatterns, excludePatterns)
   - Send request, await response
   - Deserialize response into `List<RemoteToolSchema>`
   - Cache schemas for the lifetime of the executor (modules don't change without restart)

3. **Type mapping strategy:**
   - Remote side reports `TypeName` as `System.String`, `System.Int32`, etc.
   - Server side maps recognized type names to CLR types for IL generation
   - Unrecognized types fall back to `System.String` (the MCP client sends JSON strings anyway)
   - `SwitchParameter` → `bool?` on the server side

**Files:**

| File | Action |
|------|--------|
| `OutOfProcessCommandExecutor.cs` | Implement `DiscoverCommandsAsync` |
| `oop-host.ps1` | Add `discover` handler |

### Phase 4: Command Invocation

**Goal:** `InvokeAsync` executes a PowerShell command in the remote process and returns JSON results.

**Implementation details:**

1. **`invoke` method in `oop-host.ps1`:**
   - Receives: `{"method":"invoke","params":{"command":"Get-AzContext","parameters":{"SubscriptionId":"..."}}}`
   - Builds a PowerShell pipeline: `& $command @parameters`
   - Handles `SwitchParameter` (present/absent based on boolean value)
   - Serializes results with `ConvertTo-Json -Depth 4 -Compress`
   - Returns: `{"id":"...","result":{"output":"<json-array>","hadErrors":false}}`
   - On error: `{"id":"...","error":{"code":-1,"message":"<error-message>"}}`

2. **`OutOfProcessCommandExecutor.InvokeAsync()`:**
   - Build invoke request with command name and parameters dict
   - Send, await response with timeout
   - Return the `output` JSON string (compatible with existing serialization pipeline)

3. **Framework parameter support:**
   - `_AllProperties`, `_MaxResults`, `_RequestedProperties` are intercepted server-side (same as in-process path)
   - `Select-Object` pipeline stage added remotely only if needed (or handled server-side on the JSON result)
   - Decision: handle property selection **server-side** on the returned JSON to keep the remote script simple

**Files:**

| File | Action |
|------|--------|
| `OutOfProcessCommandExecutor.cs` | Implement `InvokeAsync` |
| `oop-host.ps1` | Add `invoke` handler |

### Phase 5: Assembly Generation for Remote Tools

**Goal:** `OutOfProcessToolAssemblyGenerator` generates IL methods that delegate to `ICommandExecutor.InvokeAsync()` instead of in-process runspace execution.

**Implementation details:**

1. **Mirror `PowerShellAssemblyGenerator` structure** but with different IL:
   - Generated class holds `ICommandExecutor` + `ILogger` fields (not `IPowerShellRunspace`)
   - Each method:
     - Builds `Dictionary<string, object?>` from its typed parameters
     - Calls `ICommandExecutor.InvokeAsync(commandName, parameters, cancellationToken)`
     - Returns the JSON string result

2. **Parameter type mapping from `RemoteToolSchema`:**
   - `System.String` → `string`
   - `System.Int32` → `int?` (non-mandatory)
   - `System.Boolean` → `bool?`
   - `System.Management.Automation.SwitchParameter` → `bool?`
   - Others → `string` (server-side conversion)
   - Mandatory parameters keep non-nullable type

3. **Method name generation:**
   - Reuse `PowerShellAssemblyGenerator.SanitizeMethodName(schema.Name, schema.ParameterSetName)`
   - Already called in `CreateRemoteCommandMetadataMapping` (McpToolFactoryV2.cs line 430+)

4. **Framework parameters:**
   - Add `_AllProperties`, `_MaxResults`, `_RequestedProperties` and `CancellationToken` parameters (same as in-process)
   - The IL routes framework parameters to server-side post-processing, not to the remote pwsh

**Files:**

| File | Action |
|------|--------|
| `OutOfProcessToolAssemblyGenerator.cs` | Full IL generation implementation |

### Phase 6: Integration Testing

**Goal:** End-to-end tests proving that Az and Microsoft.Graph modules load and execute via OOP without crashing the server.

**Test categories:**

1. **Unit tests** (`PoshMcp.Tests/Unit/OutOfProcess/`):
   - `RuntimeModeTests.cs` — enum parsing round-trips
   - `RemoteToolSchemaTests.cs` — serialization/deserialization
   - `OutOfProcessCommandExecutorTests.cs` — mock subprocess, test ndjson protocol
   - `OutOfProcessToolAssemblyGeneratorTests.cs` — verify generated IL delegates to executor

2. **Integration tests** (`PoshMcp.Tests/Integration/`):
   - `OutOfProcessIntegrationTests.cs`:
     - Requires `pwsh` on PATH (trait-gated: `[Trait("Category", "OutOfProcess")]`)
     - Launch real subprocess, discover built-in commands (`Get-Process`, `Get-ChildItem`)
     - Invoke a command, verify JSON output shape
   - `OutOfProcessModuleTests.cs`:
     - Trait-gated: `[Trait("Category", "OutOfProcessModules")]`
     - Uses vendored modules from `integration/Modules/`
     - Test: import Az.Accounts, discover commands, invoke `Get-AzContext` (expected to fail auth but proves module loads)
     - Test: import Microsoft.Graph, discover commands, verify schema count

3. **MCP server integration tests**:
   - Launch `InProcessMcpServer` with `--runtime-mode OutOfProcess`
   - Verify `tools/list` returns OOP-discovered tools
   - Verify `tools/call` round-trips through the OOP executor

**Test infrastructure:**
- Extend `PowerShellTestBase` for OOP test fixtures
- Add `PwshAvailableFactAttribute` that skips tests when `pwsh` is not on PATH
- Use `TestProcessRegistry` to track and clean up spawned pwsh processes

## Configuration

### appsettings.json

```jsonc
{
  "PowerShellConfiguration": {
    "RuntimeMode": "OutOfProcess",   // or "InProcess" (default)
    "Modules": ["Az.Accounts", "Az.Compute"],
    "FunctionNames": [],
    "ExcludePatterns": [],
    "IncludePatterns": []
  }
}
```

### Environment variable

```
POSHMCP_RUNTIME_MODE=OutOfProcess
```

### CLI override

```
poshmcp serve --runtime-mode OutOfProcess
```

**Resolution order** (already implemented in `Program.ResolveEffectiveRuntimeMode`):
1. CLI `--runtime-mode` argument
2. `POSHMCP_RUNTIME_MODE` environment variable
3. `appsettings.json` → `PowerShellConfiguration.RuntimeMode`
4. Default: `InProcess`

## Error Handling and Resilience

### Subprocess Crash Recovery

| Scenario | Detection | Response |
|----------|-----------|----------|
| `pwsh` process exits unexpectedly | `Process.Exited` event | Log error, restart subprocess, re-run discovery |
| Subprocess hangs (no response) | Request timeout (30s default) | Kill process, restart, retry request |
| `pwsh` not found on PATH | `StartAsync` fails | Throw `InvalidOperationException` with guidance to install pwsh |
| Module import fails | `discover` returns error | Log warning, skip module, return partial schema set |
| Command execution fails | `invoke` returns error response | Return MCP error response with PowerShell error details |

### Restart Strategy

- **Max restarts:** 3 within 5 minutes (configurable)
- **Backoff:** 1s, 2s, 4s between restarts
- **After max restarts exhausted:** Mark executor as faulted, return errors for all subsequent requests until manual intervention
- **On restart:** Re-run `DiscoverCommandsAsync` to rebuild schemas (module state is lost)

### Timeout Configuration

| Operation | Default | Configurable via |
|-----------|---------|------------------|
| `StartAsync` (process launch + first ping) | 15 seconds | Future: appsettings |
| `DiscoverCommandsAsync` (module import + introspection) | 120 seconds | Future: appsettings |
| `InvokeAsync` (per-command execution) | 30 seconds | Future: appsettings |
| `DisposeAsync` (graceful shutdown) | 5 seconds | Hardcoded |

## Risks and Open Questions

### Risks

1. **Performance overhead:** Each command invocation crosses a process boundary with JSON serialization. For high-frequency tool calls, this adds latency. Mitigation: the in-process path remains the default; OOP is opt-in for modules that require it.

2. **State isolation:** The remote `pwsh` process has its own variable scope. Variables set by one command are visible to the next (within the same subprocess), but this differs from the in-process model where the runspace is shared more broadly. This is actually a feature — it prevents cross-command pollution.

3. **`pwsh` availability:** OOP mode requires `pwsh` (PowerShell 7+) to be installed separately. The in-process mode uses the embedded SDK and requires nothing extra. Documentation and `poshmcp doctor` should validate this.

4. **Large result serialization:** `ConvertTo-Json` in the subprocess and then parsing in the server means large result sets are serialized twice. The performance spec's `_MaxResults` and property filtering can mitigate this.

5. **Module version conflicts:** If the user's `pwsh` has different module versions than expected, behavior may differ. The subprocess uses the user's standard `$env:PSModulePath`.

### Open Questions

1. **Should we support running multiple `pwsh` subprocesses for parallelism?** Current design is 1:1 (one executor = one process), serialized access. Parallel invoke would require either multiple processes or request multiplexing in the host script.

2. **Should `oop-host.ps1` be an embedded resource or a file on disk?** Embedded resource is more self-contained but harder to debug. File on disk allows users to customize. Recommendation: **embedded resource** extracted to a temp file at startup, with an override option for development.

3. **Should we support mixed mode (some commands in-process, some OOP)?** The current `RuntimeMode` is server-wide. Mixed mode would require per-function config. Defer to a later iteration — it adds significant complexity to routing.

4. **How should `oop-host.ps1` handle environment customization?** The existing `EnvironmentConfiguration` (startup scripts, module installation from `appsettings.json`) needs an equivalent flow for the subprocess. The subprocess should run the same startup scripts and module installation logic before discovery.

## File Inventory Summary

| File Path | Phase | Description |
|-----------|-------|-------------|
| `PoshMcp.Server/PowerShell/OutOfProcess/RuntimeMode.cs` | 1 | Enum: InProcess, OutOfProcess, Unsupported |
| `PoshMcp.Server/PowerShell/OutOfProcess/ICommandExecutor.cs` | 1 | Interface for command execution abstraction |
| `PoshMcp.Server/PowerShell/OutOfProcess/RemoteToolSchema.cs` | 1 | DTOs for remote command schemas |
| `PoshMcp.Server/PowerShell/OutOfProcess/OutOfProcessCommandExecutor.cs` | 1 (stubs), 2-4 (impl) | Subprocess lifecycle + ndjson protocol |
| `PoshMcp.Server/PowerShell/OutOfProcess/OutOfProcessToolAssemblyGenerator.cs` | 1 (stubs), 5 (impl) | IL generation for OOP tool methods |
| `PoshMcp.Server/PowerShell/OutOfProcess/oop-host.ps1` | 2-4 | PowerShell subprocess host script |
| `PoshMcp.Tests/Unit/OutOfProcess/RuntimeModeTests.cs` | 1 | Unit tests for enum |
| `PoshMcp.Tests/Unit/OutOfProcess/RemoteToolSchemaTests.cs` | 3 | Schema serialization tests |
| `PoshMcp.Tests/Unit/OutOfProcess/OutOfProcessCommandExecutorTests.cs` | 2-4 | Protocol + lifecycle tests |
| `PoshMcp.Tests/Unit/OutOfProcess/OutOfProcessToolAssemblyGeneratorTests.cs` | 5 | IL generation tests |
| `PoshMcp.Tests/Integration/OutOfProcessIntegrationTests.cs` | 6 | End-to-end with real pwsh |
| `PoshMcp.Tests/Integration/OutOfProcessModuleTests.cs` | 6 | Az/Graph module loading tests |

## Dependency Graph

```
Phase 1 (stubs) ─── unblocks build
    │
    ├── Phase 2 (subprocess lifecycle) ─── requires Phase 1
    │       │
    │       ├── Phase 3 (discovery) ─── requires Phase 2
    │       │       │
    │       │       └── Phase 4 (invocation) ─── requires Phase 2, uses Phase 3 schemas
    │       │               │
    │       │               └── Phase 5 (assembly gen) ─── requires Phase 3 + 4
    │       │                       │
    │       │                       └── Phase 6 (integration tests) ─── requires all above
    │       │
    │       └── Phase 6 tests for lifecycle can start after Phase 2
    │
    └── Phase 6 unit tests for stubs can start after Phase 1
```

Phase 1 is the critical path — it unblocks the entire build and lets other work proceed in parallel.
