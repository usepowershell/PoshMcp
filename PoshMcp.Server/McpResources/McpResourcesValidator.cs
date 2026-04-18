using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PoshMcp.Server.McpPrompts;

namespace PoshMcp.Server.McpResources;

/// <summary>
/// Validates an <see cref="McpResourcesConfiguration"/> and returns structured diagnostics.
/// </summary>
public static class McpResourcesValidator
{
    private const string FileSource = "file";
    private const string CommandSource = "command";
    private static readonly string[] ValidSources = [FileSource, CommandSource];
    private const string RecommendedUriPrefix = "poshmcp://resources/";

    /// <summary>
    /// Validates all configured resources and returns a diagnostics summary.
    /// </summary>
    /// <param name="resourcesConfig">The resources configuration to validate.</param>
    /// <param name="configDirectory">
    /// Base directory used to resolve relative file paths (typically the directory containing appsettings.json).
    /// </param>
    public static McpResourcesDiagnostics Validate(McpResourcesConfiguration resourcesConfig, string configDirectory)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var resources = resourcesConfig.Resources;

        // Duplicate URI check
        var uriGroups = resources
            .Where(r => !string.IsNullOrWhiteSpace(r.Uri))
            .GroupBy(r => r.Uri!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);
        foreach (var group in uriGroups)
        {
            errors.Add($"Resource URI '{group.Key}' is duplicated across {group.Count()} entries.");
        }

        foreach (var resource in resources)
        {
            var label = string.IsNullOrWhiteSpace(resource.Uri)
                ? $"(unnamed resource at index {resources.IndexOf(resource)})"
                : $"'{resource.Uri}'";

            // Source validity
            if (string.IsNullOrWhiteSpace(resource.Source))
            {
                errors.Add($"Resource {label}: Source is missing. Must be \"file\" or \"command\".");
            }
            else if (!ValidSources.Contains(resource.Source, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"Resource {label}: Source \"{resource.Source}\" is invalid. Must be \"file\" or \"command\".");
            }
            else if (string.Equals(resource.Source, FileSource, StringComparison.OrdinalIgnoreCase))
            {
                // File source: path must resolve to an existing file
                if (string.IsNullOrWhiteSpace(resource.Path))
                {
                    errors.Add($"Resource {label}: Source is \"file\" but Path is missing.");
                }
                else
                {
                    var resolvedPath = ResolveFilePath(resource.Path, configDirectory);
                    if (!File.Exists(resolvedPath))
                    {
                        errors.Add($"Resource {label}: Path '{resource.Path}' does not resolve to an existing file (resolved: '{resolvedPath}').");
                    }
                }
            }
            else if (string.Equals(resource.Source, CommandSource, StringComparison.OrdinalIgnoreCase))
            {
                // Command source: command must be non-empty
                if (string.IsNullOrWhiteSpace(resource.Command))
                {
                    errors.Add($"Resource {label}: Source is \"command\" but Command is empty.");
                }
            }

            // URI format recommendation
            if (!string.IsNullOrWhiteSpace(resource.Uri) &&
                !resource.Uri.StartsWith(RecommendedUriPrefix, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Resource {label}: Uri does not follow the recommended '{RecommendedUriPrefix}{{slug}}' scheme.");
            }

            // MimeType presence recommendation
            if (string.IsNullOrWhiteSpace(resource.MimeType))
            {
                warnings.Add($"Resource {label}: MimeType is not specified; will default to \"text/plain\".");
            }
        }

        var validCount = resources.Count - CountAffectedEntries(resources, errors);

        return new McpResourcesDiagnostics(
            Configured: resources.Count,
            Valid: Math.Max(0, validCount),
            Errors: errors,
            Warnings: warnings);
    }

    private static string ResolveFilePath(string path, string baseDirectory)
    {
        if (Path.IsPathRooted(path))
            return path;
        return Path.GetFullPath(Path.Combine(baseDirectory, path));
    }

    /// <summary>
    /// Counts the number of resource entries that have at least one error mentioning their label.
    /// Uses a simple heuristic: count distinct entries referenced in error messages.
    /// </summary>
    private static int CountAffectedEntries(List<McpResourceConfiguration> resources, List<string> errors)
    {
        if (errors.Count == 0)
            return 0;

        var affected = 0;
        foreach (var resource in resources)
        {
            var label = string.IsNullOrWhiteSpace(resource.Uri)
                ? $"(unnamed resource at index {resources.IndexOf(resource)})"
                : $"'{resource.Uri}'";

            if (errors.Any(e => e.Contains(label, StringComparison.OrdinalIgnoreCase)))
                affected++;
        }

        // Duplicate URI errors affect all resources with that URI
        var duplicateUriErrors = errors.Where(e => e.Contains("is duplicated", StringComparison.OrdinalIgnoreCase));
        foreach (var dupError in duplicateUriErrors)
        {
            // Already counted above through the URI label logic
        }

        return affected;
    }
}

/// <summary>
/// Structured diagnostics result for MCP resource configuration validation.
/// </summary>
public record McpResourcesDiagnostics(
    int Configured,
    int Valid,
    List<string> Errors,
    List<string> Warnings);
