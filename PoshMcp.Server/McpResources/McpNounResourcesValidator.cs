using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.PowerShell;

namespace PoshMcp.Server.McpResources;

/// <summary>
/// Validates <see cref="PowerShellConfiguration.NounResourceOverrides"/> and returns structured diagnostics.
/// </summary>
public static class McpNounResourcesValidator
{
    /// <summary>
    /// Validates the <see cref="PowerShellConfiguration.NounResourceOverrides"/> dictionary for conflicts.
    /// </summary>
    public static McpNounResourcesDiagnostics Validate(PowerShellConfiguration config, ILogger logger)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var overrides = config.NounResourceOverrides;

        var resourceNameGroups = overrides
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value.ResourceName))
            .GroupBy(kvp => kvp.Value.ResourceName!, System.StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var group in resourceNameGroups)
        {
            var nouns = group.Select(kvp => kvp.Key).ToList();
            var noun1 = nouns[0];
            var noun2 = nouns[1];
            var message = $"NounResourceOverrides conflict: resource name '{group.Key}' assigned to both '{noun1}' and '{noun2}'.";
            logger.LogError("{Message}", message);
            errors.Add(message);
        }

        return new McpNounResourcesDiagnostics(
            Configured: overrides.Count,
            Conflicts: errors.Count,
            Errors: errors,
            Warnings: warnings);
    }
}

/// <summary>
/// Structured diagnostics result for noun resource override configuration validation.
/// </summary>
public record McpNounResourcesDiagnostics(
    int Configured,
    int Conflicts,
    List<string> Errors,
    List<string> Warnings);
