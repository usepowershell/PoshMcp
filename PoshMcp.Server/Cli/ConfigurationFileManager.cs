using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace PoshMcp;

internal static class ConfigurationFileManager
{
    internal static string NormalizeFormat(string? format)
    {
        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            return "json";
        }

        return "text";
    }

    internal static bool? TryParseRequiredBoolean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (bool.TryParse(normalized, out var parsed))
        {
            return parsed;
        }

        throw new ArgumentException($"Expected 'true' or 'false' but received '{value}'.");
    }

    internal static string? NormalizeRuntimeMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "in-process" or "inprocess" => "InProcess",
            "out-of-process" or "outofprocess" => "OutOfProcess",
            _ => throw new ArgumentException($"Expected 'in-process' or 'out-of-process' but received '{value}'.")
        };
    }

    internal static async Task<CreateDefaultConfigResult> CreateDefaultConfigInCurrentDirectoryAsync(string targetPath, bool force)
    {
        var alreadyExists = File.Exists(targetPath);
        if (alreadyExists && !force)
        {
            throw new IOException($"Configuration file already exists: {targetPath}. Use --force to overwrite.");
        }

        var defaultConfigJson = SettingsResolver.LoadEmbeddedDefaultConfig();
        await File.WriteAllTextAsync(targetPath, defaultConfigJson + Environment.NewLine);
        return new CreateDefaultConfigResult(alreadyExists);
    }

    internal static async Task<ConfigUpdateResult> UpdateConfigurationFileAsync(string configPath, ConfigUpdateRequest request)
    {
        var existingConfigJson = await File.ReadAllTextAsync(configPath);
        var parseOptions = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        var root = JsonNode.Parse(existingConfigJson, documentOptions: parseOptions)?.AsObject()
            ?? throw new InvalidOperationException($"Configuration file '{configPath}' must be a JSON object.");

        var powerShellConfiguration = GetOrCreateObject(root, "PowerShellConfiguration");

        // CommandNames is the preferred target for --add-command/--remove-command
        var commandNames = GetOrCreateArray(powerShellConfiguration, "CommandNames");
        var addedCommands = AddUniqueValues(commandNames, request.AddCommands, out var addedCommandNames);
        var removedCommands = RemoveValues(commandNames, request.RemoveCommands);

        // FunctionNames is the legacy target for --add-function/--remove-function
        var functionNames = GetOrCreateArray(powerShellConfiguration, "FunctionNames");
        var addedFunctions = AddUniqueValues(functionNames, request.AddFunctions, out var addedFunctionNames);
        var removedFunctions = RemoveValues(functionNames, request.RemoveFunctions);

        // Combine for advanced prompts
        var allAddedNames = addedCommandNames.Concat(addedFunctionNames).ToList();

        var addedModules = AddUniqueValues(GetOrCreateArray(powerShellConfiguration, "Modules"), request.AddModules, out _);
        var removedModules = RemoveValues(GetOrCreateArray(powerShellConfiguration, "Modules"), request.RemoveModules);

        var addedIncludePatterns = AddUniqueValues(GetOrCreateArray(powerShellConfiguration, "IncludePatterns"), request.AddIncludePatterns, out _);
        var removedIncludePatterns = RemoveValues(GetOrCreateArray(powerShellConfiguration, "IncludePatterns"), request.RemoveIncludePatterns);

        var addedExcludePatterns = AddUniqueValues(GetOrCreateArray(powerShellConfiguration, "ExcludePatterns"), request.AddExcludePatterns, out _);
        var removedExcludePatterns = RemoveValues(GetOrCreateArray(powerShellConfiguration, "ExcludePatterns"), request.RemoveExcludePatterns);

        var boolUpdateApplied = 0;
        if (request.EnableDynamicReloadTools.HasValue)
        {
            powerShellConfiguration["EnableDynamicReloadTools"] = request.EnableDynamicReloadTools.Value;
            boolUpdateApplied++;
        }

        if (request.EnableConfigurationTroubleshootingTool.HasValue)
        {
            powerShellConfiguration["EnableConfigurationTroubleshootingTool"] = request.EnableConfigurationTroubleshootingTool.Value;
            boolUpdateApplied++;
        }

        if (request.EnableResultCaching.HasValue)
        {
            var performance = GetOrCreateObject(powerShellConfiguration, "Performance");
            performance["EnableResultCaching"] = request.EnableResultCaching.Value;
            boolUpdateApplied++;
        }

        if (request.UseDefaultDisplayProperties.HasValue)
        {
            var performance = GetOrCreateObject(powerShellConfiguration, "Performance");
            performance["UseDefaultDisplayProperties"] = request.UseDefaultDisplayProperties.Value;
            boolUpdateApplied++;
        }

        if (!string.IsNullOrWhiteSpace(request.SetRuntimeMode))
        {
            powerShellConfiguration["RuntimeMode"] = request.SetRuntimeMode;
            boolUpdateApplied++;
        }

        if (request.SetAuthEnabled.HasValue)
        {
            var authentication = GetOrCreateObject(root, "Authentication");
            authentication["Enabled"] = request.SetAuthEnabled.Value;
            boolUpdateApplied++;

            if (request.SetAuthEnabled.Value)
            {
                var schemes = authentication["Schemes"]?.AsArray();
                if (schemes == null || schemes.Count == 0)
                {
                    Console.Error.WriteLine("WARNING: Authentication.Enabled set to true but Authentication.Schemes is empty. Run 'poshmcp validate-config' to verify your configuration.");
                }
            }
        }

        var advancedPromptedFunctionCount = 0;
        if (!request.NonInteractive && allAddedNames.Count > 0)
        {
            advancedPromptedFunctionCount = PromptForAdvancedFunctionConfiguration(powerShellConfiguration, allAddedNames);
        }

        var changed = addedCommands > 0 || removedCommands > 0 ||
            addedFunctions > 0 || removedFunctions > 0 ||
            addedModules > 0 || removedModules > 0 ||
            addedIncludePatterns > 0 || removedIncludePatterns > 0 ||
            addedExcludePatterns > 0 || removedExcludePatterns > 0 ||
            boolUpdateApplied > 0 || advancedPromptedFunctionCount > 0;

        if (changed)
        {
            var updatedConfigJson = root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(configPath, updatedConfigJson + Environment.NewLine);
        }

        return new ConfigUpdateResult(
            configPath,
            changed,
            addedFunctions,
            removedFunctions,
            addedCommands,
            removedCommands,
            advancedPromptedFunctionCount,
            boolUpdateApplied);
    }

    internal static int PromptForAdvancedFunctionConfiguration(JsonObject powerShellConfiguration, IEnumerable<string> addedFunctionNames)
    {
        var promptedCount = 0;

        foreach (var functionName in addedFunctionNames)
        {
            Console.Write($"Configure advanced settings for '{functionName}'? [y/N]: ");
            if (!IsYesAnswer(Console.ReadLine()))
            {
                continue;
            }

            var functionOverrides = GetOrCreateObject(powerShellConfiguration, "FunctionOverrides");
            var functionOverride = GetOrCreateObject(functionOverrides, functionName);

            var cachingOverride = PromptForNullableBoolean($"Override EnableResultCaching for {functionName} (true/false/skip): ");
            if (cachingOverride.HasValue)
            {
                functionOverride["EnableResultCaching"] = cachingOverride.Value;
            }

            var displayOverride = PromptForNullableBoolean($"Override UseDefaultDisplayProperties for {functionName} (true/false/skip): ");
            if (displayOverride.HasValue)
            {
                functionOverride["UseDefaultDisplayProperties"] = displayOverride.Value;
            }

            Console.Write($"Set DefaultProperties for {functionName} (comma-separated, blank to skip): ");
            var defaultPropertiesText = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(defaultPropertiesText))
            {
                var propertyValues = defaultPropertiesText
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => value.Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (propertyValues.Count > 0)
                {
                    functionOverride["DefaultProperties"] = new JsonArray(propertyValues.Select(value => (JsonNode?)value).ToArray());
                }
            }

            var allowAnonymousOverride = PromptForNullableBoolean($"Set AllowAnonymous for {functionName} (true/false/skip): ");
            if (allowAnonymousOverride.HasValue)
            {
                functionOverride["AllowAnonymous"] = allowAnonymousOverride.Value;
            }

            Console.Write($"Set RequiredScopes for {functionName} (space-separated, blank to skip): ");
            var requiredScopesText = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(requiredScopesText))
            {
                var scopeValues = requiredScopesText
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => value.Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (scopeValues.Count > 0)
                {
                    functionOverride["RequiredScopes"] = new JsonArray(scopeValues.Select(value => (JsonNode?)value).ToArray());
                }
            }

            Console.Write($"Set RequiredRoles for {functionName} (space-separated, blank to skip): ");
            var requiredRolesText = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(requiredRolesText))
            {
                var roleValues = requiredRolesText
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => value.Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (roleValues.Count > 0)
                {
                    functionOverride["RequiredRoles"] = new JsonArray(roleValues.Select(value => (JsonNode?)value).ToArray());
                }
            }

            promptedCount++;
        }

        return promptedCount;
    }

    internal static bool IsYesAnswer(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        return string.Equals(normalized, "y", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool? PromptForNullableBoolean(string prompt)
    {
        while (true)
        {
            Console.Write(prompt);
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input) || string.Equals(input.Trim(), "skip", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (bool.TryParse(input.Trim(), out var parsed))
            {
                return parsed;
            }

            Console.WriteLine("Please enter true, false, or skip.");
        }
    }

    internal static JsonObject GetOrCreateObject(JsonObject parent, string propertyName)
    {
        if (parent[propertyName] is JsonObject existing)
        {
            return existing;
        }

        var created = new JsonObject();
        parent[propertyName] = created;
        return created;
    }

    internal static JsonArray GetOrCreateArray(JsonObject parent, string propertyName)
    {
        if (parent[propertyName] is JsonArray existing)
        {
            return existing;
        }

        var created = new JsonArray();
        parent[propertyName] = created;
        return created;
    }

    internal static int AddUniqueValues(JsonArray array, IEnumerable<string> values, out List<string> addedValues)
    {
        var existing = new HashSet<string>(
            array.Select(v => v?.GetValue<string>())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!.Trim()),
            StringComparer.OrdinalIgnoreCase);

        addedValues = new List<string>();
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var normalized = value.Trim();
            if (!existing.Add(normalized))
            {
                continue;
            }

            array.Add(normalized);
            addedValues.Add(normalized);
        }

        return addedValues.Count;
    }

    internal static int RemoveValues(JsonArray array, IEnumerable<string> values)
    {
        var toRemove = new HashSet<string>(
            values.Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim()),
            StringComparer.OrdinalIgnoreCase);

        if (toRemove.Count == 0)
        {
            return 0;
        }

        var removed = 0;
        for (var i = array.Count - 1; i >= 0; i--)
        {
            var existing = array[i]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(existing))
            {
                continue;
            }

            if (toRemove.Contains(existing.Trim()))
            {
                array.RemoveAt(i);
                removed++;
            }
        }

        return removed;
    }
}

internal sealed record CreateDefaultConfigResult(bool WasOverwritten);

internal sealed record ConfigUpdateRequest(
    IEnumerable<string> AddFunctions,
    IEnumerable<string> RemoveFunctions,
    IEnumerable<string> AddCommands,
    IEnumerable<string> RemoveCommands,
    IEnumerable<string> AddModules,
    IEnumerable<string> RemoveModules,
    IEnumerable<string> AddIncludePatterns,
    IEnumerable<string> RemoveIncludePatterns,
    IEnumerable<string> AddExcludePatterns,
    IEnumerable<string> RemoveExcludePatterns,
    bool? EnableDynamicReloadTools,
    bool? EnableConfigurationTroubleshootingTool,
    bool? EnableResultCaching,
    bool? UseDefaultDisplayProperties,
    bool? SetAuthEnabled,
    string? SetRuntimeMode,
    bool NonInteractive);

internal sealed record ConfigUpdateResult(
    string ConfigurationPath,
    bool Changed,
    int AddedFunctions,
    int RemovedFunctions,
    int AddedCommands,
    int RemovedCommands,
    int AdvancedPromptedFunctionCount,
    int SettingsChanged);
