using System;
using System.Collections.Concurrent;
using System.Text.Json;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Holds runtime overrides for result caching, set via the set-result-caching MCP tool.
/// Thread-safe. Ephemeral — does not persist across server restarts.
/// </summary>
public class RuntimeCachingState
{
    private volatile int _globalOverrideValue = -1; // -1 = not set, 0 = false, 1 = true
    private readonly ConcurrentDictionary<string, bool> _functionOverrides = new(StringComparer.OrdinalIgnoreCase);

    // Maps the int sentinel back to bool?
    private bool? GlobalOverride => _globalOverrideValue < 0 ? (bool?)null : _globalOverrideValue > 0;

    /// <summary>
    /// Set or clear the global runtime override.
    /// Pass null to remove the override and fall back to config.
    /// </summary>
    public void SetGlobalOverride(bool? enabled)
        => _globalOverrideValue = enabled.HasValue ? (enabled.Value ? 1 : 0) : -1;

    /// <summary>
    /// Set or clear a per-function runtime override.
    /// Pass null to remove the override and fall back to config.
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
    /// Priority: per-function runtime override > global runtime override.
    /// </summary>
    public bool? Resolve(string functionName)
    {
        if (_functionOverrides.TryGetValue(functionName, out var funcOverride))
            return funcOverride;
        return GlobalOverride;
    }

    /// <summary>
    /// Processes a set-result-caching tool call and returns a JSON response string.
    /// </summary>
    /// <param name="enabled">true/false to set, or null/"reset" to clear the override</param>
    /// <param name="scope">"global" or "function"</param>
    /// <param name="functionName">Required when scope is "function"</param>
    /// <returns>JSON response suitable for MCP tool output</returns>
    public string HandleSetResultCaching(bool? enabled, string scope = "global", string? functionName = null)
    {
        scope = scope?.ToLowerInvariant() ?? "global";

        if (scope == "function")
        {
            if (string.IsNullOrWhiteSpace(functionName))
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "functionName is required when scope is 'function'."
                });
            }

            SetFunctionOverride(functionName, enabled);

            var action = enabled.HasValue
                ? $"set to {(enabled.Value ? "enabled" : "disabled")}"
                : "cleared (falling back to configuration)";

            return JsonSerializer.Serialize(new
            {
                success = true,
                message = $"Result caching for '{functionName}' {action}.",
                scope = "function",
                functionName,
                enabled = (object?)enabled
            });
        }
        else
        {
            SetGlobalOverride(enabled);

            var action = enabled.HasValue
                ? $"set to {(enabled.Value ? "enabled" : "disabled")}"
                : "cleared (falling back to configuration)";

            return JsonSerializer.Serialize(new
            {
                success = true,
                message = $"Global result caching {action}.",
                scope = "global",
                enabled = (object?)enabled
            });
        }
    }
}
