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

---

## 4. Proposal B: Default Property Filtering via Select-Object

### 4.1 Concept

Append `Select-Object -Property <properties>` to the pipeline after the command (and before Tee-Object, if enabled). The property list is determined by:

1. **Explicit per-function configuration** — highest priority
2. **DefaultDisplayPropertySet** of the output type — auto-discovered at runtime
3. **All properties** — fallback when no property set is defined or when caller opts out

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
function ResolveSettings(commandName, globalConfig):
  override = globalConfig.FunctionOverrides[commandName]

  enableCaching = override?.EnableResultCaching
                  ?? globalConfig.Performance.EnableResultCaching
                  ?? false

  useDefaultProps = override?.UseDefaultDisplayProperties
                    ?? globalConfig.Performance.UseDefaultDisplayProperties
                    ?? true

  properties = override?.DefaultProperties
               ?? DiscoverDefaultDisplayPropertySet(commandName)
               ?? null   // null = all properties

  return (enableCaching, useDefaultProps, properties)
```

---

## 6. Implementation Plan

### 6.1 Files Changed

| File | Change |
|------|--------|
| `PoshMcp.Server/PowerShell/PowerShellConfiguration.cs` | Add `PerformanceConfiguration`, `FunctionOverride` classes |
| `PoshMcp.Server/PowerShell/PowerShellAssemblyGenerator.cs` | Conditional Tee-Object; inject Select-Object into pipeline |
| `PoshMcp.Server/PowerShell/PowerShellDynamicAssemblyGenerator.cs` | Pass configuration through static wrappers |
| `PoshMcp.Server/McpToolFactoryV2.cs` | Thread configuration to assembly generator; add `_AllProperties` parameter |
| `PoshMcp.Server/PowerShell/PowerShellSchemaGenerator.cs` | Inject `_AllProperties` into generated tool schemas |
| `PoshMcp.Server/appsettings.json` | Add default `Performance` section |
| `PoshMcp.Tests/` | New test files for property filtering and caching toggle |

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
| **Integration: Caching disabled** | Tool execution works; utility tools return clear error |
| **Integration: Caching enabled** | Tee-Object present; filter/sort/group still work |
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

---

## 7. Open Questions

1. **Should `_AllProperties` also force caching on?** If a caller requests all properties, they probably want replay capability too. Current proposal: no coupling — they're independent settings.

2. **Should we support `-First N` / result limiting?** A `_MaxResults` parameter could cap the number of objects before serialization. This is orthogonal but synergistic. Recommend deferring to a follow-up proposal.

3. **What about `Format-List` vs `Format-Table` property sets?** PowerShell has `DefaultDisplayPropertySet` (table) and `DefaultKeyPropertySet` (list). We use the display set. If users want the key set, they can configure explicit properties.

4. **Should cached results use the filtered or full object?** Current design: cache whatever comes out of the pipeline (filtered if Select-Object is active). This means `get-last-command-output` returns the filtered version. To get full results from cache, caller would need `_AllProperties=true` on the original call.

---

## 8. Recommendation

Implement both proposals together. Phase 1 (configuration) and Phase 2 (optional Tee-Object) are low-risk and deliver immediate memory savings. Phase 3 (Select-Object) delivers the dramatic payload reduction that makes PoshMcp viable for commands producing large result sets.

**Default configuration should be:**
- `EnableResultCaching: false` — opt-in only
- `UseDefaultDisplayProperties: true` — on by default for maximum payload reduction

This gives new users fast, lightweight responses out of the box while allowing power users to enable caching and full property access when needed.
