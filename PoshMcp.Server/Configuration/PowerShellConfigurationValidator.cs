using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using PoshMcp.Server.PowerShell;

namespace PoshMcp.Server.Configuration;

/// <summary>
/// Validates PowerShell configuration options
/// </summary>
public class PowerShellConfigurationValidator : IValidateOptions<PowerShellConfiguration>
{
    public ValidateOptionsResult Validate(string? name, PowerShellConfiguration options)
    {
        var failures = new List<string>();

        // Validate that at least one source is configured
        ValidateAtLeastOneSourceConfigured(options, failures);

        // Validate function names
        ValidateFunctionNames(options.FunctionNames, failures);

        // Validate module names
        ValidateModuleNames(options.Modules, failures);

        // Validate patterns
        ValidatePatterns("IncludePatterns", options.IncludePatterns, failures);
        ValidatePatterns("ExcludePatterns", options.ExcludePatterns, failures);

        // Validate logical consistency
        ValidateLogicalConsistency(options, failures);

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static void ValidateAtLeastOneSourceConfigured(PowerShellConfiguration options, List<string> failures)
    {
        bool hasFunctionNames = options.FunctionNames?.Count > 0;
        bool hasModules = options.Modules?.Count > 0;
        bool hasIncludePatterns = options.IncludePatterns?.Count > 0;

        if (!hasFunctionNames && !hasModules && !hasIncludePatterns)
        {
            failures.Add("At least one of FunctionNames, Modules, or IncludePatterns must be specified.");
        }
    }

    private static void ValidateFunctionNames(List<string> functionNames, List<string> failures)
    {
        if (functionNames == null) return;

        foreach (var functionName in functionNames)
        {
            if (string.IsNullOrWhiteSpace(functionName))
            {
                failures.Add("FunctionNames cannot contain null, empty, or whitespace-only values.");
                continue;
            }

            // Validate PowerShell function name format
            if (!IsValidPowerShellCommandName(functionName))
            {
                failures.Add($"Invalid function name '{functionName}'. PowerShell function names should follow Verb-Noun pattern or be valid PowerShell identifiers.");
            }
        }

        // Check for duplicates
        var duplicates = functionNames
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        foreach (var duplicate in duplicates)
        {
            failures.Add($"Duplicate function name found: '{duplicate}'.");
        }
    }

    private static void ValidateModuleNames(List<string> modules, List<string> failures)
    {
        if (modules == null) return;

        foreach (var module in modules)
        {
            if (string.IsNullOrWhiteSpace(module))
            {
                failures.Add("Modules cannot contain null, empty, or whitespace-only values.");
                continue;
            }

            // Basic validation - module names should not contain invalid characters
            if (module.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
            {
                failures.Add($"Invalid module name '{module}'. Module names cannot contain path-invalid characters.");
            }
        }

        // Check for duplicates
        var duplicates = modules
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        foreach (var duplicate in duplicates)
        {
            failures.Add($"Duplicate module name found: '{duplicate}'.");
        }
    }

    private static void ValidatePatterns(string propertyName, List<string> patterns, List<string> failures)
    {
        if (patterns == null) return;

        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                failures.Add($"{propertyName} cannot contain null, empty, or whitespace-only values.");
                continue;
            }

            // Validate that the pattern is a valid wildcard pattern
            if (!IsValidWildcardPattern(pattern))
            {
                failures.Add($"Invalid wildcard pattern in {propertyName}: '{pattern}'. Patterns should use * and ? wildcards.");
            }
        }

        // Check for duplicates
        var duplicates = patterns
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        foreach (var duplicate in duplicates)
        {
            failures.Add($"Duplicate pattern found in {propertyName}: '{duplicate}'.");
        }
    }

    private static void ValidateLogicalConsistency(PowerShellConfiguration options, List<string> failures)
    {
        // Warn if both include and exclude patterns are specified - this might lead to confusion
        if (options.IncludePatterns?.Count > 0 && options.ExcludePatterns?.Count > 0)
        {
            // Check for overlapping patterns
            foreach (var includePattern in options.IncludePatterns)
            {
                foreach (var excludePattern in options.ExcludePatterns)
                {
                    if (PatternsOverlap(includePattern, excludePattern))
                    {
                        failures.Add($"Include pattern '{includePattern}' and exclude pattern '{excludePattern}' may overlap, which could lead to unexpected behavior.");
                    }
                }
            }
        }

        // Check for overly broad patterns that might include dangerous commands
        var dangerousPatterns = new[] { "*", "Remove-*", "Delete-*", "Clear-*", "Stop-*" };
        
        if (options.IncludePatterns != null)
        {
            foreach (var pattern in options.IncludePatterns)
            {
                if (dangerousPatterns.Contains(pattern, StringComparer.OrdinalIgnoreCase))
                {
                    failures.Add($"Include pattern '{pattern}' is overly broad and may include dangerous commands. Consider more specific patterns.");
                }
            }
        }
    }

    private static bool IsValidPowerShellCommandName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        // PowerShell commands typically follow Verb-Noun pattern or are valid identifiers
        // Allow alphanumeric, hyphens, underscores, and dots
        var validPattern = @"^[a-zA-Z][a-zA-Z0-9._-]*$";
        return Regex.IsMatch(name, validPattern);
    }

    private static bool IsValidWildcardPattern(string pattern)
    {
        try
        {
            // Try to convert wildcard pattern to regex to validate it
            var regexPattern = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            _ = new Regex(regexPattern, RegexOptions.IgnoreCase);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool PatternsOverlap(string includePattern, string excludePattern)
    {
        // Simple heuristic: if patterns are identical or one is contained in the other
        if (string.Equals(includePattern, excludePattern, StringComparison.OrdinalIgnoreCase))
            return true;

        // More sophisticated overlap detection could be implemented here
        // For now, just check for obvious cases
        var includeNormalized = includePattern.Replace("*", "").Replace("?", "");
        var excludeNormalized = excludePattern.Replace("*", "").Replace("?", "");

        return includeNormalized.Contains(excludeNormalized, StringComparison.OrdinalIgnoreCase) ||
               excludeNormalized.Contains(includeNormalized, StringComparison.OrdinalIgnoreCase);
    }
}