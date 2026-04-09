# Proposal: Large Result Set Performance Improvements

**Author:** Farnsworth (Lead / Architect)
**Date:** 2026-07
**Status:** Proposed
**Requested by:** Steven Murawski

---

## 1. Problem Statement

PoshMcp serializes the complete object graph for every PowerShell command result. When a command like `Get-Process` returns hundreds of objects, each carrying 80+ properties (many of which are nested CLR objects), the resulting JSON payload is enormous, slow to serialize, and wasteful for MCP consumers that only need a handful of display properties.

Two specific concerns:

1. **Tee-Object overhead** — Every pipeline unconditionally pipes through `Tee-Object -Variable LastCommandOutput`, doubling memory pressure by caching the entire result set for replay operations (filter, sort, group). Many callers never use these replay tools.

2. **No property filtering** — The serializer walks every gettable property on every object (up to `MaxDepth=4`). PowerShell's own formatting system uses `DefaultDisplayPropertySet` to show 5–8 properties. MCP callers almost never need the full object graph.

---

## 2. Current Architecture

### 2.1 Pipeline Construction

In `PowerShellAssemblyGenerator.ExecutePowerShellCommandTyped()` (lines 617–680), the pipeline is built as:

```
<CommandName> [parameters] | Tee-Object -Variable LastCommandOutput
```

After `Invoke()`, the `Collection<PSObject>` results are serialized to JSON via `System.Text.Json` using `PowerShellJsonOptions.Options` with the custom `PSObjectJsonConverter` and `PowerShellObjectSerializer`.

### 2.2 Tee-Object Role

`Tee-Object` copies the pipeline output into the `$LastCommandOutput` PowerShell variable. This variable is consumed by four generated utility methods:

| Method | Tool Name | Purpose |
|--------|-----------|---------|
| `GetLastCommandOutput` | `get-last-command-output` | Replay the cached result |
| `SortLastCommandOutput` | `sort-last-command-output` | Sort cached results by property |
| `FilterLastCommandOutput` | `filter-last-command-output` | Filter cached results with `Where-Object` |
| `GroupLastCommandOutput` | `group-last-command-output` | Group cached results by property |

These are convenience tools exposed to MCP clients. They allow post-hoc manipulation of the last command's output without re-executing the command.

### 2.3 Serialization Path

`PowerShellObjectSerializer.FlattenPSObjects()` walks every `PSObject`, recursively normalizing all gettable properties via `PSObject.AsPSObject(value).Properties` up to depth 4. It handles cycles, pointer types, and nested dictionaries/enumerables. The result is a plain `Dictionary<string, object?>` per object, which is then serialized by `System.Text.Json`.

### 2.4 Cost Analysis: Get-Process Example

A typical `Get-Process` call on a development machine returns ~250 processes. Each `System.Diagnostics.Process` object exposes approximately 80 properties. Many of these (e.g., `Modules`, `Threads`, `StartInfo`) are themselves complex nested objects.

| Metric | Current (all properties) | DefaultDisplayPropertySet only |
|--------|------------------------:|------------------------------:|
| Properties per object | ~80 | 5 (`Id`, `ProcessName`, `CPU`, `PM`, `WS`) |
| Estimated JSON size (250 processes) | **2–5 MB** | **~50–100 KB** |
| Serialization time | ~500ms–2s | ~20–50ms |
| Memory (Tee-Object cache) | 2× result size | 0 (if disabled) |

The `DefaultDisplayPropertySet` for `System.Diagnostics.Process` is:
```
Id, Handles, CPU, SI, Name
```

That's a **40–50× payload reduction** for the most common use case.

---

## 3. Proposal A: Optional Tee-Object (Opt-In)

### 3.1 Behavior Change

| Aspect | Current | Proposed |
|--------|---------|----------|
| Tee-Object | Always in pipeline | Off by default; opt-in via config |
| Utility tools (filter/sort/group/get) | Always generated | Only generated when caching enabled |
| Default | N/A | `EnableResultCaching: false` |

### 3.2 Configuration

```jsonc
{
  "PowerShellConfiguration": {
    // Global toggle — default is false (caching disabled)
    "EnableResultCaching": false,

    "FunctionNames": ["Get-Process", "Get-Service"],

    // Per-function override
    "FunctionOverrides": {
      "Get-Service": {
        "EnableResultCaching": true
      }
    }
  }
}
```

Resolution order:
1. Per-function `EnableResultCaching` value (if present in `FunctionOverrides`)
2. Global `EnableResultCaching` value
3. Default: `false`

### 3.3 Implementation Detail

In `PowerShellAssemblyGenerator.ExecutePowerShellCommandTyped()`, the Tee-Object addition becomes conditional:

```csharp
// Only add Tee-Object when caching is enabled for this command
if (enableCaching)
{
    ps.AddCommand("Tee-Object")
      .AddParameter("Variable", "LastCommandOutput");
}
```

When caching is globally disabled, the four utility methods (`get-last-command-output`, etc.) should still be generated but should return a clear error message: `"Result caching is disabled. Enable it in configuration to use this tool."` This avoids breaking MCP clients that enumerate tools.

### 3.4 Trade-offs

**What you lose when caching is disabled:**
- `get-last-command-output` — no replay; caller must re-run the command
- `sort-last-command-output` — no server-side sorting of previous results
- `filter-last-command-output` — no server-side filtering of previous results
- `group-last-command-output` — no server-side grouping of previous results

**What you gain:**
- ~50% memory reduction (no duplicated result set)
- Faster pipeline execution (one less pipeline stage)
- Simpler debugging (no variable-scoping side effects)

### 3.5 Backward Compatibility

The default changes from "caching always on" to "caching off." This is a **breaking change** for any MCP client that relies on `get-last-command-output` without configuration. To mitigate:

- Log a deprecation warning on first release when the feature ships
- Document the change prominently in release notes
- Utility tools return a helpful error message (not a crash) when caching is off

### 3.6 Runtime Toggle via MCP Tool

Static configuration (appsettings.json, per-function overrides) covers deployment-time decisions. But an MCP client may want to enable or disable result caching mid-session — for example, turning caching on before a large exploratory query so the filter/sort/group tools become available, then turning it off again to reduce memory pressure.

#### 3.6.1 New MCP Tool: `set-result-caching`

A new MCP tool allows the client to toggle result caching at runtime without restarting the server.

**Tool Schema:**

```jsonc
{
  "name": "set-result-caching",
  "description": "Enable or disable result caching at runtime. When enabled, command output is cached for replay by filter/sort/group tools. When disabled, memory usage is reduced but replay tools are unavailable. Runtime setting takes highest priority, overriding both global and per-function configuration. Pass scope='global' to toggle for all commands, or scope='function' with functionName to toggle for a specific function only.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "enabled": {
        "type": "boolean",
        "description": "true to enable result caching, false to disable it."
      },
      "scope": {
        "type": "string",
        "enum": ["global", "function"],
        "description": "Whether to apply the toggle globally or to a specific function. Default: 'global'.",
        "default": "global"
      },
      "functionName": {
        "type": "string",
        "description": "Required when scope is 'function'. The PowerShell function name to toggle caching for (e.g., 'Get-Process')."
      }
    },
    "required": ["enabled"]
  }
}
```

**Example calls:**

```jsonc
// Turn on caching globally
{ "enabled": true }

// Turn off caching for Get-Process only
{ "enabled": false, "scope": "function", "functionName": "Get-Process" }

// Reset — remove the runtime override, fall back to config
// (call with enabled=null or use a separate reset tool)
```

**Response:**

```jsonc
{
  "content": [
    {
      "type": "text",
      "text": "Result caching set to enabled (scope: global). Filter, sort, and group tools will now work on subsequent command output."
    }
  ]
}
```

#### 3.6.2 Design Considerations

**Priority in resolution order:**
Runtime overrides take the **highest priority**, above per-function config and global config. The full resolution order becomes:

1. Runtime per-function override (set via `set-result-caching` with `scope=function`)
2. Runtime global override (set via `set-result-caching` with `scope=global`)
3. Static per-function `EnableResultCaching` value (from `FunctionOverrides` in config)
4. Static global `EnableResultCaching` value (from `Performance` section in config)
5. Default: `false`

**Thread safety:**
The runtime state must be safe for concurrent access. The MCP server processes tool calls that may overlap in multi-turn conversations. Use:
- A `ConcurrentDictionary<string, bool>` for per-function runtime overrides (keyed by function name)
- A `volatile bool?` (or `Interlocked`-guarded nullable) for the global runtime override
- These are simple atomic reads/writes — no locks needed for the global flag; the `ConcurrentDictionary` handles its own synchronization

**Scope:**
The toggle supports two scopes:
- **Global** — applies to all commands. Good for "I'm about to do a bunch of exploratory work, cache everything."
- **Per-function** — applies to a single command. Good for "Cache `Get-Process` output but not `Get-Service`."
- **Session-scoped** — the runtime state is tied to the MCP server process lifetime. There is no session ID or multi-tenant isolation. This is appropriate because PoshMcp runs as a single-client stdio server.

**Persistence:**
Runtime overrides are **ephemeral** — they do not survive server restarts. This is intentional:
- Runtime = developer intent for the current session
- Config = operational defaults that persist
- If a user consistently wants caching on, they should set it in `appsettings.json`
- Mixing runtime state with disk persistence adds complexity (dirty writes, stale state) for no clear benefit

**Immediate effect:**
When toggled on at runtime, the change takes effect on the **next** tool call. It does not retroactively cache output from previous calls. Specifically:
- Toggling caching **on** means the next command execution will include `Tee-Object` in the pipeline, and subsequent filter/sort/group calls will work on that output
- Toggling caching **off** means the next command execution will skip `Tee-Object`. The `$LastCommandOutput` variable from any prior cached run remains in the PowerShell runspace until overwritten or the session ends

#### 3.6.3 Implementation Approach

**Runtime state container:**

```csharp
/// <summary>
/// Holds runtime overrides for result caching, set via the set-result-caching MCP tool.
/// Thread-safe. Ephemeral — does not persist across server restarts.
/// </summary>
public class RuntimeCachingState
{
    private volatile bool? _globalOverride;
    private readonly ConcurrentDictionary<string, bool> _functionOverrides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Set or clear the global runtime override.
    /// Pass null to remove the override and fall back to config.
    /// </summary>
    public void SetGlobalOverride(bool? enabled) => _globalOverride = enabled;

    /// <summary>
    /// Set or clear a per-function runtime override.
    /// </summary>
    public void SetFunctionOverride(string functionName, bool? enabled)
    {
        if (enabled.HasValue)
            _functionOverrides[functionName] = enabled.Value;
        else
            _functionOverrides.TryRemove(functionName, out _);
    }

    /// <summary>
    /// Resolve the runtime override for a given function.
    /// Returns null if no runtime override is active (fall through to config).
    /// </summary>
    public bool? Resolve(string functionName)
    {
        if (_functionOverrides.TryGetValue(functionName, out var funcOverride))
            return funcOverride;
        return _globalOverride;
    }
}
```

**Registration:** Register `RuntimeCachingState` as a singleton in DI (`Program.cs`). Inject it into `McpToolFactoryV2` and the generated assembly's static wrapper methods.

**Integration with `ExecutePowerShellCommandTyped`:**

The existing caching check becomes:

```csharp
bool enableCaching = ResolveCachingSetting(commandName, runtimeState, config);

// ... existing pipeline construction ...
if (enableCaching)
{
    ps.AddCommand("Tee-Object")
      .AddParameter("Variable", "LastCommandOutput");
}
```

Where `ResolveCachingSetting` implements the five-layer resolution order from section 3.6.2.

**Utility tool behavior:**
When caching is toggled **on** at runtime, filter/sort/group tools start working immediately on the next command's output. If called before any cached output exists, they return the same "no cached output available" error they would return normally.

---

## 4. Proposal B: Default Property Filtering via Select-Object

### 4.1 Concept

Append `Select-Object -Property <properties>` to the pipeline after the command (and before Tee-Object, if enabled). The property list is determined by:

1. **Explicit per-function configuration** — highest priority
2. **RequestedProperties parameter added to all tool calls** for the caller to request specific property filtering - provided at runtime (see 4.4 for example)
3. **DefaultDisplayPropertySet** of the output type — auto-discovered at runtime
4. **All properties** — fallback when no property set is defined or when caller opts out

### 4.2 How DefaultDisplayPropertySet Works

PowerShell types define a `DefaultDisplayPropertySet` in their type data. This is what `Format-Table` uses when you don't specify `-Property`. It can be queried at runtime:

```powershell
$obj = Get-Process | Select-Object -First 1
$obj.PSStandardMembers.DefaultDisplayPropertySet.ReferencedPropertyNames
# Returns: Id, Handles, CPU, SI, Name
```

In C#, this is accessible via:
```csharp
var memberSet = psObject.Members["PSStandardMembers"] as PSMemberSet;
var displaySet = memberSet?.Members["DefaultDisplayPropertySet"] as PSPropertySet;
var propertyNames = displaySet?.ReferencedPropertyNames; // string[]
```

### 4.3 Configuration

```jsonc
{
  "PowerShellConfiguration": {
    // Global toggle — default is true (use DefaultDisplayPropertySet when available)
    "UseDefaultDisplayProperties": true,

    "FunctionNames": ["Get-Process", "Get-Service"],

    "FunctionOverrides": {
      // Explicit property list — overrides DefaultDisplayPropertySet
      "Get-Process": {
        "DefaultProperties": ["Id", "ProcessName", "CPU", "WorkingSet64", "StartTime"]
      },
      // Disable property filtering for this command (get everything)
      "Get-Service": {
        "UseDefaultDisplayProperties": false
      }
    }
  }
}
```

### 4.4 Caller Override via Tool Parameter

Every generated tool should accept an optional parameter that lets the MCP caller request the full object:

```jsonc
// MCP tool schema addition (auto-generated for every tool)
{
  "name": "get_process_name",
  "inputSchema": {
    "properties": {
      "Name": { "type": "string" },
      "_AllProperties": {
        "type": "boolean",
        "description": "When true, returns all properties instead of the default display set. Use sparingly — large payloads.",
        "default": false
      }
    }
  }
}
```
The underscore prefix (`_AllProperties`) signals it's a PoshMcp framework parameter, not a PowerShell parameter.

Or the caller requests specific properties

```jsonc
// MCP tool schema addition (auto-generated for every tool)
{
  "name": "get_process_name",
  "inputSchema": {
    "properties": {
      "Name": { "type": "string" },
      "_RequestedProperties": {
        "type": "array",
        "description": "List of property names to return.",
        "default": null
      }
    }
  }
}
```

The underscore prefix (`_RequestedProperties`) signals it's a PoshMcp framework parameter, not a PowerShell parameter.


### 4.5 Pipeline Construction (Before/After)

**Current pipeline:**
```
Get-Process -Name "dotnet" | Tee-Object -Variable LastCommandOutput
```

**Proposed pipeline (with both features):**
```
Get-Process -Name "dotnet" | Select-Object -Property Id,ProcessName,CPU,WorkingSet64,StartTime
```

Or with caching enabled:
```
Get-Process -Name "dotnet" | Select-Object -Property Id,ProcessName,CPU,WorkingSet64,StartTime | Tee-Object -Variable LastCommandOutput
```

Or when `_AllProperties=true`:
```
Get-Process -Name "dotnet" | Tee-Object -Variable LastCommandOutput
```
(no Select-Object, caching re-enabled for full result replay)

### 4.6 Property Discovery Strategy

At assembly generation time (when tools are being built), discover the output type's `DefaultDisplayPropertySet`:

```
Phase 1: GenerateAssembly() — for each command:
  1. Check FunctionOverrides for explicit DefaultProperties
  2. If UseDefaultDisplayProperties is true and no override:
     a. Execute: <Command> | Select-Object -First 1 (dry-run to get type info)
     b. Query PSStandardMembers.DefaultDisplayPropertySet
     c. Cache the property list
  3. Store property list metadata alongside the generated method
```

**Important:** Some commands have expensive side effects or require parameters. Discovery should be best-effort — if it fails, fall back to no filtering. The dry-run approach works for commands like `Get-Process` and `Get-Service` but should be skipped for destructive commands (detected via verb analysis already in `GetCommandMetadata`).

A safer alternative: look up type metadata from PowerShell's type system without executing the command:

```powershell
$typeData = Get-TypeData -TypeName "System.Diagnostics.Process"
$typeData.DefaultDisplayPropertySet.ReferencedProperties
```

This requires knowing the output type. PowerShell's `[OutputType()]` attribute on functions or the `OutputType` from `Get-Command` can provide this without execution.

### 4.7 Payload Size Impact

| Command | All Properties | DefaultDisplayPropertySet | Reduction |
|---------|---------------|--------------------------|-----------|
| `Get-Process` (250 objects) | ~3 MB | ~75 KB | **97%** |
| `Get-Service` (200 objects) | ~800 KB | ~40 KB | **95%** |
| `Get-ChildItem` (100 files) | ~500 KB | ~25 KB | **95%** |
| `Get-Date` (1 object) | ~2 KB | ~200 bytes | **90%** |

---

## 5. Combined Configuration Schema

```jsonc
{
  "PowerShellConfiguration": {
    "FunctionNames": ["Get-Process", "Get-Service", "Get-ChildItem"],
    "Modules": [],
    "ExcludePatterns": [],
    "IncludePatterns": [],
    "EnableDynamicReloadTools": false,

    // NEW: Performance configuration
    "Performance": {
      // Cache previous command output for filter/sort/group replay
      // Default: false (opt-in)
      "EnableResultCaching": false,

      // Use DefaultDisplayPropertySet to limit output properties
      // Default: true (on by default — major payload reduction)
      "UseDefaultDisplayProperties": true
    },

    // NEW: Per-function overrides
    "FunctionOverrides": {
      "Get-Process": {
        // Explicit property list — takes priority over DefaultDisplayPropertySet
        "DefaultProperties": ["Id", "ProcessName", "CPU", "WorkingSet64", "StartTime"],
        // Override global caching setting for this function
        "EnableResultCaching": true
      },
      "Get-Service": {
        // Disable property filtering for this function
        "UseDefaultDisplayProperties": false
      }
    }
  }
}
```

### 5.1 C# Configuration Classes

```csharp
public class PerformanceConfiguration
{
    public bool EnableResultCaching { get; set; } = false;
    public bool UseDefaultDisplayProperties { get; set; } = true;
}

public class FunctionOverride
{
    public List<string>? DefaultProperties { get; set; }
    public bool? EnableResultCaching { get; set; }
    public bool? UseDefaultDisplayProperties { get; set; }
}

// Added to PowerShellConfiguration
public class PowerShellConfiguration
{
    // ... existing properties ...
    public PerformanceConfiguration Performance { get; set; } = new();
    public Dictionary<string, FunctionOverride> FunctionOverrides { get; set; } = new();
}
```

### 5.2 Resolution Logic (Pseudocode)

```
function ResolveSettings(commandName, globalConfig, runtimeState):
  override = globalConfig.FunctionOverrides[commandName]

  // EnableResultCaching — 5-layer resolution (runtime highest priority)
  enableCaching = runtimeState.Resolve(commandName)           // Layer 1+2: runtime per-function, then runtime global
                  ?? override?.EnableResultCaching             // Layer 3: static per-function config
                  ?? globalConfig.Performance.EnableResultCaching  // Layer 4: static global config
                  ?? false                                     // Layer 5: default

  useDefaultProps = override?.UseDefaultDisplayProperties
                    ?? globalConfig.Performance.UseDefaultDisplayProperties
                    ?? true

  properties = override?.DefaultProperties
               ?? DiscoverDefaultDisplayPropertySet(commandName)
               ?? null   // null = all properties

  return (enableCaching, useDefaultProps, properties)
```

> **Note:** The `runtimeState.Resolve(commandName)` call checks per-function runtime overrides first, then the global runtime override. If neither is set, it returns `null` and resolution falls through to static configuration. See section 3.6.2 for the full priority order.

---

## 6. Implementation Plan

### 6.1 Files Changed

| File | Change |
|------|--------|
| `PoshMcp.Server/PowerShell/PowerShellConfiguration.cs` | Add `PerformanceConfiguration`, `FunctionOverride` classes |
| `PoshMcp.Server/PowerShell/RuntimeCachingState.cs` | **New file.** Thread-safe runtime caching state container (`ConcurrentDictionary` + volatile global flag) |
| `PoshMcp.Server/PowerShell/PowerShellAssemblyGenerator.cs` | Conditional Tee-Object; inject Select-Object into pipeline; resolve caching via `RuntimeCachingState` |
| `PoshMcp.Server/PowerShell/PowerShellDynamicAssemblyGenerator.cs` | Pass configuration and `RuntimeCachingState` through static wrappers |
| `PoshMcp.Server/McpToolFactoryV2.cs` | Thread configuration to assembly generator; add `_AllProperties` parameter; register `set-result-caching` tool |
| `PoshMcp.Server/PowerShell/PowerShellSchemaGenerator.cs` | Inject `_AllProperties` into generated tool schemas |
| `PoshMcp.Server/Program.cs` | Register `RuntimeCachingState` as singleton in DI |
| `PoshMcp.Server/appsettings.json` | Add default `Performance` section |
| `PoshMcp.Tests/` | New test files for property filtering, caching toggle, and runtime toggle scenarios |

### 6.2 Work Ordering

```
Phase 1: Configuration (low risk, no behavior change)
  1. Add PerformanceConfiguration and FunctionOverride classes
  2. Bind from appsettings.json
  3. Add configuration validation
  4. Unit tests for config resolution logic

Phase 2: Optional Tee-Object (medium risk)
  1. Thread EnableResultCaching to ExecutePowerShellCommandTyped
  2. Make Tee-Object conditional
  3. Update utility methods to check caching state
  4. Integration tests: verify caching on/off behavior

Phase 2.5: Runtime Caching Toggle (low-medium risk, depends on Phase 2)
  1. Implement RuntimeCachingState class (ConcurrentDictionary + volatile flag)
  2. Register as singleton in DI (Program.cs)
  3. Wire into McpToolFactoryV2 — register `set-result-caching` as a built-in MCP tool
  4. Update ResolveCachingSetting to check runtime state first
  5. Unit tests: thread safety, resolution priority, scope behavior
  6. Integration tests: toggle on/off mid-session, verify filter/sort/group availability

Phase 3: Select-Object property filtering (medium risk)
  1. Implement DefaultDisplayPropertySet discovery
  2. Inject Select-Object into pipeline construction
  3. Add _AllProperties parameter to schema generation
  4. Integration tests with Get-Process, Get-Service
  5. Test fallback when no display property set exists

Phase 4: Documentation and defaults
  1. Update appsettings.json with defaults
  2. Update README and DESIGN.md
  3. Add migration notes for breaking change
```

### 6.3 Testing Strategy

| Test Category | What to Verify |
|---------------|---------------|
| **Unit: Config resolution** | Override precedence, default values, null handling |
| **Unit: Pipeline construction** | Select-Object injected with correct properties; Tee-Object present/absent |
| **Unit: RuntimeCachingState** | Thread-safe concurrent reads/writes; per-function vs global priority; null returns when no override set |
| **Unit: Resolution with runtime** | 5-layer resolution order: runtime per-function > runtime global > config per-function > config global > default |
| **Integration: Caching disabled** | Tool execution works; utility tools return clear error |
| **Integration: Caching enabled** | Tee-Object present; filter/sort/group still work |
| **Integration: Runtime toggle on** | Call `set-result-caching` with `enabled=true`; next command output is cached; filter/sort/group work |
| **Integration: Runtime toggle off** | Call `set-result-caching` with `enabled=false`; next command skips Tee-Object; utility tools return error |
| **Integration: Runtime per-function** | Toggle caching on for `Get-Process` only; verify `Get-Service` remains uncached |
| **Integration: Runtime overrides config** | Config says `EnableResultCaching: true`; runtime toggle sets `false`; verify caching is off |
| **Integration: Property filtering** | JSON output contains only selected properties |
| **Integration: _AllProperties=true** | Full object graph returned |
| **Integration: No DisplayPropertySet** | Commands without property sets return all properties gracefully |
| **Regression: Existing tests** | All existing tests pass with new defaults |

### 6.4 Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Breaking MCP clients that use `get-last-command-output` | Medium | High | Return helpful error when disabled; document prominently |
| DefaultDisplayPropertySet discovery fails for some types | Medium | Low | Fall back to all properties; log warning |
| Select-Object changes output type (loses type identity) | Low | Medium | Select-Object preserves `PSTypeName`; test explicitly |
| Performance regression in property discovery | Low | Low | Cache at assembly generation time, not per-invocation |
| Commands with no output type metadata | Medium | Low | Skip filtering for unresolvable types |
| Runtime toggle race condition with concurrent tool calls | Low | Medium | `ConcurrentDictionary` and `volatile` provide safe concurrent access; no locks needed for simple flag reads |
| Runtime toggle confuses MCP clients expecting stable behavior | Low | Low | Tool response clearly states what changed; state is ephemeral (resets on restart) |

---

## 7. Open Questions — Resolved

1. **Should `_AllProperties` also force caching on?** No coupling — they're independent settings. ✅ *Kept default (not addressed — no coupling).*

2. **Should we support `-First N` / result limiting?** ✅ **YES** — Add a `_MaxResults` framework parameter (int?) to every generated tool. When provided, inject `Select-Object -First N` into the pipeline before serialization. Caps result count to reduce payload and serialization time. *Decision by: Steven Murawski, 2026-04-09.*

3. **What about `Format-List` vs `Format-Table` property sets?** Use the display set (`DefaultDisplayPropertySet`). ✅ *Kept default (not addressed).*

4. **Should cached results use the filtered or full object?** ✅ **Cache the FILTERED object.** Cache whatever comes out of the pipeline (after Select-Object if active). `get-last-command-output` returns the filtered version. To get full results from cache, caller needs `_AllProperties=true` on the original call. *Decision by: Steven Murawski, 2026-04-09.*

5. **Should the runtime toggle support a "reset" operation?** ✅ **YES** — Support `null` OR the string value `"reset"` on the `enabled` parameter. Both clear the runtime override and fall back to the previously configured setting. No separate reset tool needed. *Decision by: Steven Murawski, 2026-04-09.*

6. **Should `set-result-caching` be gated behind `EnableDynamicReloadTools`?** ✅ **NO** — Do NOT gate it. `set-result-caching` is always registered as an MCP tool regardless of the `EnableDynamicReloadTools` setting. *Decision by: Steven Murawski, 2026-04-09.*

---

## 8. Recommendation

Implement both proposals together. Phase 1 (configuration) and Phase 2 (optional Tee-Object) are low-risk and deliver immediate memory savings. Phase 3 (Select-Object) delivers the dramatic payload reduction that makes PoshMcp viable for commands producing large result sets.

**Default configuration should be:**
- `EnableResultCaching: false` — opt-in only
- `UseDefaultDisplayProperties: true` — on by default for maximum payload reduction

This gives new users fast, lightweight responses out of the box while allowing power users to enable caching and full property access when needed.
