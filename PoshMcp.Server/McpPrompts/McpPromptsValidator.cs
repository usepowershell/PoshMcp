using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PoshMcp.Server.McpPrompts;

/// <summary>
/// Validates an <see cref="McpPromptsConfiguration"/> and returns structured diagnostics.
/// </summary>
public static class McpPromptsValidator
{
    private const string FileSource = "file";
    private const string CommandSource = "command";
    private static readonly string[] ValidSources = [FileSource, CommandSource];

    /// <summary>
    /// Validates all configured prompts and returns a diagnostics summary.
    /// </summary>
    /// <param name="promptsConfig">The prompts configuration to validate.</param>
    /// <param name="configDirectory">
    /// Base directory used to resolve relative file paths (typically the directory containing appsettings.json).
    /// </param>
    public static McpPromptsDiagnostics Validate(McpPromptsConfiguration promptsConfig, string configDirectory)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var prompts = promptsConfig.Prompts;

        // Duplicate Name check
        var nameGroups = prompts
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .GroupBy(p => p.Name!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);
        foreach (var group in nameGroups)
        {
            errors.Add($"Prompt name '{group.Key}' is duplicated across {group.Count()} entries.");
        }

        foreach (var prompt in prompts)
        {
            var label = string.IsNullOrWhiteSpace(prompt.Name)
                ? $"(unnamed prompt at index {prompts.IndexOf(prompt)})"
                : $"'{prompt.Name}'";

            // Source validity
            if (string.IsNullOrWhiteSpace(prompt.Source))
            {
                errors.Add($"Prompt {label}: Source is missing. Must be \"file\" or \"command\".");
            }
            else if (!ValidSources.Contains(prompt.Source, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"Prompt {label}: Source \"{prompt.Source}\" is invalid. Must be \"file\" or \"command\".");
            }
            else if (string.Equals(prompt.Source, FileSource, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(prompt.Path))
                {
                    errors.Add($"Prompt {label}: Source is \"file\" but Path is missing.");
                }
                else
                {
                    var resolvedPath = ResolveFilePath(prompt.Path, configDirectory);
                    if (!File.Exists(resolvedPath))
                    {
                        errors.Add($"Prompt {label}: Path '{prompt.Path}' does not resolve to an existing file (resolved: '{resolvedPath}').");
                    }
                }
            }
            else if (string.Equals(prompt.Source, CommandSource, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(prompt.Command))
                {
                    errors.Add($"Prompt {label}: Source is \"command\" but Command is empty.");
                }
            }

            // Argument validation
            foreach (var arg in prompt.Arguments)
            {
                if (string.IsNullOrWhiteSpace(arg.Name))
                {
                    if (arg.Required)
                    {
                        errors.Add($"Prompt {label}: A required argument has an empty Name — Required arguments must be named.");
                    }
                    else
                    {
                        errors.Add($"Prompt {label}: An argument has an empty Name — all argument Names must be non-empty.");
                    }
                }
            }
        }

        var validCount = prompts.Count - CountAffectedEntries(prompts, errors);

        return new McpPromptsDiagnostics(
            Configured: prompts.Count,
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

    private static int CountAffectedEntries(List<McpPromptConfiguration> prompts, List<string> errors)
    {
        if (errors.Count == 0)
            return 0;

        var affected = 0;
        foreach (var prompt in prompts)
        {
            var label = string.IsNullOrWhiteSpace(prompt.Name)
                ? $"(unnamed prompt at index {prompts.IndexOf(prompt)})"
                : $"'{prompt.Name}'";

            if (errors.Any(e => e.Contains(label, StringComparison.OrdinalIgnoreCase)))
                affected++;
        }

        return affected;
    }
}

/// <summary>
/// Structured diagnostics result for MCP prompt configuration validation.
/// </summary>
public record McpPromptsDiagnostics(
    int Configured,
    int Valid,
    List<string> Errors,
    List<string> Warnings);
