---
name: "precomputed-optional-parameter"
description: "Avoid redundant expensive computations by passing pre-computed results as optional parameters to delegation methods, maintaining backward compatibility."
domain: "performance"
confidence: "medium"
source: "earned"
---

## Context
When a public method computes expensive data (e.g., introspection, runspace setup) and then delegates to a private helper that recomputes the same data, the helper should accept the pre-computed result as an optional parameter. This pattern eliminates redundant work while preserving backward compatibility for internal callers that don't pre-compute.

## Patterns
- **Parameter signature:** Accept the pre-computed result as an optional parameter with a default value: `BuildDoctorJson(IConfiguration config, IEnumerable<IMcpTool> tools, List<ConfiguredFunctionStatus>? precomputedFunctionStatus = null)`
- **Guard clause:** Use the null-coalescing operator to compute only if not provided: `precomputedFunctionStatus ?? ComputeExpensiveData()`
- **Type visibility:** When the inner type must appear in the optional parameter signature, promote it from `private` to `internal`. Sealed records are safe for this widening.
- **Applies to:** `IsolatedPowerShellRunspace` operations are the primary use case. Each runspace creation + PS session initialization adds significant startup cost; double-execution doubles this cost. Other expensive introspection scenarios also benefit.
- **Caller responsibility:** The caller that owns the public API layer is responsible for pre-computing and passing the result to avoid double-work.

## Examples
**Signature with optional pre-computed parameter:**
```csharp
// Public method computes; delegates with result
public string GetDoctorStatus(IConfiguration config, IEnumerable<IMcpTool> tools)
{
    var status = _statusBuilder.ComputeStatus(config, tools);  // Expensive
    return _innerBuilder.BuildDoctorJson(config, tools, status);  // Pass pre-computed
}

// Private helper accepts optional; uses null-coalescing
internal string BuildDoctorJson(
    IConfiguration config,
    IEnumerable<IMcpTool> tools,
    List<ConfiguredFunctionStatus>? precomputedFunctionStatus = null)
{
    var functionStatus = precomputedFunctionStatus ?? ComputeExpensiveFunction(config, tools);
    if (functionStatus.All(s => s.Found || s.ResolutionReason is null))
    {
        // Process...
    }
    return json;
}
```

**Type visibility promotion:**
```csharp
// Before: private
private sealed record ConfiguredFunctionStatus(string Name, bool Found, string? ResolutionReason);

// After: internal (safe for sealed records; still hidden from external API)
internal sealed record ConfiguredFunctionStatus(string Name, bool Found, string? ResolutionReason);
```

## Anti-Patterns
- ❌ Always recomputing in the helper even if the caller already computed (wastes resources)
- ❌ Making the optional parameter required or non-null (breaks internal backward compatibility)
- ❌ Passing pre-computed data that may be stale or inconsistent with current state
- ❌ Widening visibility of non-sealed types (exposes implementation details)
