---
name: "unserializable-type-handling"
description: "Handle PowerShell commands with parameters whose types cannot be serialized to JSON by gracefully skipping parameters or parameter sets in MCP tool schema generation."
domain: "api-design"
confidence: "high"
source: "earned"
---

## Context
The MCP protocol requires tool schemas to be JSON-serializable. PowerShell commands may have parameters with types that cannot be represented in JSON Schema (e.g., `PSObject`, `ScriptBlock`, `PowerShell` instances). Rather than failing to expose a command, implement three-tier fallback logic: skip optional parameters, skip parameter sets, or skip the entire command if all sets are affected.

## Patterns
- **Tier 1: Optional unserializable parameter:** If a parameter is optional and unserializable, drop it silently from the MCP schema. The command remains usable without that parameter.
- **Tier 2: Mandatory unserializable parameter (single parameter set):** If all mandatory parameters in a parameter set are unserializable, skip that entire parameter set. Other sets remain available.
- **Tier 3: All parameter sets skipped:** If all parameter sets are skipped (no viable alternative remains), the command receives no MCP tool. Log a warning; the command is inaccessible via MCP but doesn't break the server.
- **Unserializable type set includes:** 
  - `PSObject`, `ScriptBlock`, `System.Object` (too generic)
  - `IntPtr`, `UIntPtr`, and pointer/by-ref types
  - Delegate-derived types
  - Stream-derived types
  - WaitHandle-derived types
  - `System.Reflection.Assembly`
  - `System.Management.Automation.PowerShell`
  - All `Runspaces.*` types
  - Arrays of any unserializable type
- **Check location:** `PowerShellParameterUtils.IsUnserializableType(Type)` performs the check after common parameter exclusion (e.g., `-Verbose`, `-ErrorAction`).
- **Return value semantics:** `GenerateMethodForCommand()` returns `bool` (false = skipped). Do not throw exceptions; emit warnings and continue.

## Examples
**Parameter set handling in schema generation:**
```csharp
// Optional unserializable parameter → skip silently
if (parameter.IsMandatory)
{
    if (PowerShellParameterUtils.IsUnserializableType(parameter.ParameterType))
    {
        skipThisParameterSet = true;
    }
}
else
{
    if (PowerShellParameterUtils.IsUnserializableType(parameter.ParameterType))
    {
        continue;  // Skip to next parameter; don't add to schema
    }
}
```

**Command-level skipping:**
```csharp
public bool GenerateMethodForCommand(string commandName)
{
    var parameterSets = GetParameterSets(commandName);
    var viableParameterSets = new List<IMcpToolParameterSet>();

    foreach (var set in parameterSets)
    {
        if (IsParameterSetViable(set))
        {
            viableParameterSets.Add(set);
        }
    }

    if (viableParameterSets.Count == 0)
    {
        _logger.LogWarning($"Command '{commandName}' has no serializable parameter sets; skipping MCP tool.");
        return false;  // Command not exposed
    }

    // Build schema with only viable parameter sets
    return true;
}
```

**Detecting unserializable types:**
```csharp
private static bool IsUnserializableType(Type type)
{
    var unserializableTypes = new[]
    {
        typeof(PSObject),
        typeof(ScriptBlock),
        typeof(System.Object),
        typeof(IntPtr),
        typeof(UIntPtr),
        // ... more types
    };

    if (unserializableTypes.Contains(type))
        return true;

    // Check arrays
    if (type.IsArray && IsUnserializableType(type.GetElementType()!))
        return true;

    // Check base classes (Stream, WaitHandle, Delegate, etc.)
    if (typeof(Stream).IsAssignableFrom(type) ||
        typeof(WaitHandle).IsAssignableFrom(type) ||
        typeof(Delegate).IsAssignableFrom(type))
        return true;

    return false;
}
```

## Anti-Patterns
- ❌ Throwing an exception when an unserializable type is encountered (crashes server initialization)
- ❌ Attempting to force-serialize unserializable types (results in invalid JSON schemas)
- ❌ Silently skipping entire commands without logging a warning (leaves no trace for debugging)
- ❌ Including mandatory unserializable parameters in the schema with placeholder types
- ❌ Forgetting to check array element types (arrays of unserializable types are also unserializable)
