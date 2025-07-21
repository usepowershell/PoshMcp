using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using PoshMcp.PowerShell;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp;

/// <summary>
/// Metadata about a PowerShell command for MCP tool creation
/// </summary>
public class PowerShellCommandMetadata
{
    public string CommandName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsDestructive { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsIdempotent { get; set; }
}

/// <summary>
/// Factory class for creating MCP tools from PowerShell commands using dynamically generated assemblies
/// </summary>
public static class McpToolFactoryV2
{
    private static Assembly? _generatedAssembly;
    private static object? _generatedInstance;
    private static Dictionary<string, MethodInfo>? _generatedMethods;

    /// <summary>
    /// Gets PowerShell command metadata including help synopsis and verb information
    /// </summary>
    /// <param name="commandInfo">CommandInfo to analyze</param>
    /// <param name="powerShell">PowerShell instance for executing queries</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Command metadata with synopsis, verb info, and safety characteristics</returns>
    private static PowerShellCommandMetadata GetCommandMetadata(CommandInfo commandInfo, PSPowerShell powerShell, ILogger logger)
    {
        var metadata = new PowerShellCommandMetadata
        {
            CommandName = commandInfo.Name,
            Description = commandInfo.Name, // Default fallback
            IsDestructive = false, // Default to safe
            IsReadOnly = false,    // Default assumption
            IsIdempotent = false   // Default assumption
        };

        try
        {
            // Get help information for the command to extract synopsis
            try
            {
                powerShell.Commands.Clear();
                powerShell.AddCommand("Get-Help")
                         .AddParameter("Name", commandInfo.Name)
                         .AddParameter("ErrorAction", "SilentlyContinue");

                var helpResult = powerShell.Invoke();
                powerShell.Commands.Clear();

                if (helpResult.Count > 0 && helpResult[0] != null)
                {
                    var helpObject = helpResult[0];

                    // Try to get Synopsis property
                    var synopsisProperty = helpObject.Properties["Synopsis"];
                    if (synopsisProperty?.Value != null)
                    {
                        var synopsis = synopsisProperty.Value.ToString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(synopsis) &&
                            !synopsis.Equals(commandInfo.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            metadata.Description = synopsis;
                            logger.LogDebug($"Found synopsis for {commandInfo.Name}: {synopsis}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Could not retrieve help for command {commandInfo.Name}: {ex.Message}");
            }

            // Analyze the command verb using Get-Verb to determine safety characteristics
            try
            {
                var commandName = commandInfo.Name;
                var verbPart = commandName.Contains('-') ? commandName.Split('-')[0] : commandName;

                powerShell.Commands.Clear();
                powerShell.AddCommand("Get-Verb")
                         .AddParameter("Verb", verbPart)
                         .AddParameter("ErrorAction", "SilentlyContinue");

                var verbResult = powerShell.Invoke();
                powerShell.Commands.Clear();

                if (verbResult.Count > 0 && verbResult[0] != null)
                {
                    var verbObject = verbResult[0];

                    // Get the verb group to determine characteristics
                    var groupProperty = verbObject.Properties["Group"];
                    if (groupProperty?.Value != null)
                    {
                        var group = groupProperty.Value.ToString();
                        logger.LogDebug($"Command {commandInfo.Name} has verb group: {group}");

                        // Based on PowerShell verb groups, determine safety characteristics
                        switch (group?.ToUpperInvariant())
                        {
                            case "COMMON":
                                // Get, Find, Search, etc. - typically safe, read-only operations
                                if (verbPart.Equals("Get", StringComparison.OrdinalIgnoreCase) ||
                                    verbPart.Equals("Find", StringComparison.OrdinalIgnoreCase) ||
                                    verbPart.Equals("Search", StringComparison.OrdinalIgnoreCase) ||
                                    verbPart.Equals("Show", StringComparison.OrdinalIgnoreCase))
                                {
                                    metadata.IsReadOnly = true;
                                    metadata.IsIdempotent = true;
                                }
                                break;

                            case "DATA":
                                // Add, Remove, Set, etc. - potentially destructive
                                if (verbPart.Equals("Add", StringComparison.OrdinalIgnoreCase) ||
                                    verbPart.Equals("Remove", StringComparison.OrdinalIgnoreCase) ||
                                    verbPart.Equals("Clear", StringComparison.OrdinalIgnoreCase))
                                {
                                    metadata.IsDestructive = true;
                                }
                                else if (verbPart.Equals("Set", StringComparison.OrdinalIgnoreCase))
                                {
                                    metadata.IsIdempotent = true; // Set operations are often idempotent
                                }
                                break;

                            case "LIFECYCLE":
                                // Start, Stop, Restart, etc. - can be destructive
                                metadata.IsDestructive = true;
                                break;

                            case "SECURITY":
                                // Operations that affect security - generally destructive
                                metadata.IsDestructive = true;
                                break;

                            case "DIAGNOSTIC":
                                // Test, Measure, etc. - typically read-only
                                metadata.IsReadOnly = true;
                                metadata.IsIdempotent = true;
                                break;
                        }
                    }
                }
                else
                {
                    // If Get-Verb doesn't return info, use basic verb analysis
                    logger.LogDebug($"No Get-Verb info found for {verbPart}, using basic analysis");

                    if (verbPart.Equals("Get", StringComparison.OrdinalIgnoreCase) ||
                        verbPart.Equals("Find", StringComparison.OrdinalIgnoreCase) ||
                        verbPart.Equals("Search", StringComparison.OrdinalIgnoreCase) ||
                        verbPart.Equals("Show", StringComparison.OrdinalIgnoreCase) ||
                        verbPart.Equals("Test", StringComparison.OrdinalIgnoreCase) ||
                        verbPart.Equals("Measure", StringComparison.OrdinalIgnoreCase))
                    {
                        metadata.IsReadOnly = true;
                        metadata.IsIdempotent = true;
                    }
                    else if (verbPart.Equals("Remove", StringComparison.OrdinalIgnoreCase) ||
                             verbPart.Equals("Delete", StringComparison.OrdinalIgnoreCase) ||
                             verbPart.Equals("Clear", StringComparison.OrdinalIgnoreCase) ||
                             verbPart.Equals("Stop", StringComparison.OrdinalIgnoreCase) ||
                             verbPart.Equals("Kill", StringComparison.OrdinalIgnoreCase))
                    {
                        metadata.IsDestructive = true;
                    }
                    else if (verbPart.Equals("Set", StringComparison.OrdinalIgnoreCase))
                    {
                        metadata.IsIdempotent = true;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Could not analyze verb for command {commandInfo.Name}: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            logger.LogError($"Error getting metadata for command {commandInfo.Name}: {ex.Message}");
        }

        logger.LogDebug($"Command metadata for {metadata.CommandName}: Description='{metadata.Description}', IsDestructive={metadata.IsDestructive}, IsReadOnly={metadata.IsReadOnly}, IsIdempotent={metadata.IsIdempotent}");

        return metadata;
    }

    /// <summary>
    /// Gets a list of MCP tools from available PowerShell commands using dynamic assembly generation
    /// </summary>
    /// <param name="config">PowerShell configuration</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>List of MCP server tools</returns>
    public static List<McpServerTool> GetToolsList(PowerShellConfiguration config, ILogger logger)
    {
        try
        {
            logger.LogInformation("Starting MCP tools generation using dynamic assembly approach");
            logger.LogTrace("Tool factory configuration:");
            logger.LogTrace($"  Config type: {config.GetType().Name}");

            // Get all available PowerShell commands with their metadata based on configuration
            var (commands, commandsMetadata) = GetAvailableCommandsWithMetadata(config, logger);

            if (!commands.Any())
            {
                logger.LogWarning("No PowerShell commands found - check configuration and PowerShell environment");
                logger.LogDebug("Suggestions:");
                logger.LogDebug("  - Verify function names exist in PowerShell");
                logger.LogDebug("  - Check if specified modules are available");
                logger.LogDebug("  - Review include/exclude patterns");
                return new List<McpServerTool>();
            }

            logger.LogInformation($"Found {commands.Count} PowerShell commands");
            logger.LogTrace($"Commands to process: [{string.Join(", ", commands.Select(c => c.Name))}]");

            // Generate the assembly with all command methods
            logger.LogDebug("Generating dynamic assembly for PowerShell commands...");
            _generatedAssembly = PowerShellDynamicAssemblyGenerator.GenerateAssembly(commands, logger);
            _generatedInstance = PowerShellDynamicAssemblyGenerator.GetGeneratedInstance(logger);
            _generatedMethods = PowerShellDynamicAssemblyGenerator.GetGeneratedMethods();

            logger.LogInformation($"Generated assembly with {_generatedMethods.Count} methods");
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace("Generated methods:");
                foreach (var method in _generatedMethods.OrderBy(m => m.Key))
                {
                    logger.LogTrace($"  {method.Key} -> {method.Value.ReturnType.Name} with {method.Value.GetParameters().Length} parameters");
                }
            }

            // Create MCP tools from generated methods
            var tools = new List<McpServerTool>();
            var successCount = 0;
            var failureCount = 0;

            // Create a mapping from method names to source commands for metadata lookup
            var methodToCommandMap = new Dictionary<string, PowerShellCommandMetadata>();

            foreach (var command in commands)
            {
                // Get the command metadata
                var metadata = commandsMetadata.GetValueOrDefault(command.Name, new PowerShellCommandMetadata
                {
                    CommandName = command.Name,
                    Description = command.Name,
                    IsDestructive = false,
                    IsReadOnly = false,
                    IsIdempotent = false
                });

                // For each parameter set of this command, create method name mapping
                foreach (var parameterSet in command.ParameterSets)
                {
                    var methodName = PowerShellDynamicAssemblyGenerator.SanitizeMethodName(command.Name, parameterSet.Name);
                    methodToCommandMap[methodName] = metadata;
                    logger.LogDebug($"Mapped method '{methodName}' to command '{command.Name}' with metadata");
                }
            }

            foreach (var kvp in _generatedMethods)
            {
                var methodName = kvp.Key;
                var method = kvp.Value;

                try
                {
                    logger.LogTrace($"Processing method '{methodName}' for MCP tool creation...");

                    // Create a delegate from the method
                    var delegateType = GetDelegateTypeForMethod(method);
                    var methodDelegate = Delegate.CreateDelegate(delegateType, _generatedInstance, method);

                    // Get metadata for this method
                    var metadata = methodToCommandMap.GetValueOrDefault(methodName, new PowerShellCommandMetadata
                    {
                        CommandName = methodName.Replace("_", "-"),
                        Description = methodName.Replace("_", "-"),
                        IsDestructive = false,
                        IsReadOnly = false,
                        IsIdempotent = false
                    });

                    var options = new McpServerToolCreateOptions
                    {
                        Name = methodName.ToLowerInvariant(),
                        Description = metadata.Description,
                        Destructive = metadata.IsDestructive,
                        Idempotent = metadata.IsIdempotent,
                        OpenWorld = true,
                        ReadOnly = metadata.IsReadOnly,
                        Title = metadata.CommandName,
                        // unless the method's return type is Task<string> or Task<IEnumerable<string>> this should be true
                        UseStructuredContent = true
                    };

                    logger.LogDebug($"Creating MCP tool for method '{methodName}' with delegate type: {delegateType.Name}, metadata: Destructive={metadata.IsDestructive}, ReadOnly={metadata.IsReadOnly}, Idempotent={metadata.IsIdempotent}");

                    // Create the MCP server tool
                    var tool = McpServerTool.Create(methodDelegate, options);
                    tools.Add(tool);
                    successCount++;

                    logger.LogInformation($"Successfully created MCP tool '{options.Name}' for command '{metadata.CommandName}' - {metadata.Description}");
                }
                catch (Exception ex)
                {
                    failureCount++;
                    logger.LogError(ex, $"Failed to create MCP tool for method '{methodName}': {ex.Message}");
                    if (logger.IsEnabled(LogLevel.Trace))
                    {
                        logger.LogTrace($"Method details - Name: {methodName}, Return Type: {method.ReturnType}, Parameters: {method.GetParameters().Length}");
                    }
                }
            }

            logger.LogInformation($"Successfully created {tools.Count} MCP tools from dynamic assembly");
            logger.LogDebug($"Tool creation summary: {successCount} succeeded, {failureCount} failed");

            if (failureCount > 0 && logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Some tools failed to be created. This may be due to unsupported parameter types or complex PowerShell objects.");
                logger.LogDebug("The MCP server will still work with the successfully created tools.");
            }

            return tools;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to generate MCP tools using dynamic assembly: {ex.Message}");
            logger.LogDebug("This error prevented any tools from being created. Check PowerShell configuration and environment.");
            return new List<McpServerTool>();
        }
    }

    /// <summary>
    /// Gets available PowerShell commands to generate methods for based on configuration
    /// </summary>
    private static (List<CommandInfo> commands, Dictionary<string, PowerShellCommandMetadata> metadata) GetAvailableCommandsWithMetadata(PowerShellConfiguration config, ILogger logger)
    {
        var powerShell = PowerShellRunspaceHolder.Instance;
        var commands = new List<CommandInfo>();
        var metadata = new Dictionary<string, PowerShellCommandMetadata>();

        try
        {
            logger.LogInformation("Processing PowerShell configuration...");
            logger.LogTrace("Configuration details:");
            logger.LogTrace($"  Function Names: [{string.Join(", ", config.FunctionNames)}]");
            logger.LogTrace($"  Modules: [{string.Join(", ", config.Modules)}]");
            logger.LogTrace($"  Include Patterns: [{string.Join(", ", config.IncludePatterns)}]");
            logger.LogTrace($"  Exclude Patterns: [{string.Join(", ", config.ExcludePatterns)}]");

            // Always process function names if specified
            if (config.FunctionNames.Any())
            {
                logger.LogDebug($"Processing {config.FunctionNames.Count} function names...");
                var namedCommands = GetCommandsByName(config.FunctionNames, powerShell, logger);
                commands.AddRange(namedCommands);
                logger.LogInformation($"Added {namedCommands.Count} commands by name");


                foreach (var cmd in namedCommands)
                {
                    if (logger.IsEnabled(LogLevel.Trace))
                    {
                        logger.LogTrace($"  Added command: {cmd.Name} (Type: {cmd.CommandType})");
                    }
                    var cmdMetadata = GetCommandMetadata(cmd, powerShell, logger);
                    metadata[cmd.Name] = cmdMetadata;
                }

            }
            else
            {
                logger.LogDebug("No function names specified in configuration");

            }

            // Always process modules if specified
            if (config.Modules.Any())
            {
                logger.LogDebug($"Processing {config.Modules.Count} modules...");
                var moduleCommands = GetCommandsByModule(config.Modules, powerShell, logger);
                var newModuleCommands = moduleCommands.Where(mc => !commands.Any(c => c.Name == mc.Name)).ToList();
                commands.AddRange(newModuleCommands);
                logger.LogInformation($"Added {newModuleCommands.Count} new commands from modules");


                foreach (var cmd in newModuleCommands)
                {
                    if (logger.IsEnabled(LogLevel.Trace))
                    {
                        logger.LogTrace($"  Added module command: {cmd.Name} from {cmd.Module}");

                    }

                    if (!metadata.ContainsKey(cmd.Name))
                    {
                        var cmdMetadata = GetCommandMetadata(cmd, powerShell, logger);
                        metadata[cmd.Name] = cmdMetadata;
                    }
                }
            }
            else
            {
                logger.LogDebug("No modules specified in configuration");
            }

            // Apply include patterns if specified to filter configured commands 
            if (config.IncludePatterns.Any())
            {
                var beforeCount = commands.Count;
                logger.LogDebug($"Applying {config.IncludePatterns.Count} include patterns to {beforeCount} commands...");
                commands = ApplyIncludePatterns(commands, config.IncludePatterns, logger);
                var addedCount = commands.Count - beforeCount;
                logger.LogInformation($"Include patterns resulted in {commands.Count} commands (net change: {addedCount:+#;-#;0})");

                // Keep only metadata for included commands
                var includedCommandNames = commands.Select(c => c.Name).ToHashSet();
                var filteredMetadata = metadata.Where(kvp => includedCommandNames.Contains(kvp.Key))
                                              .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                metadata = filteredMetadata;
            }
            else
            {
                logger.LogDebug("No include patterns specified in configuration");
            }

            // Apply exclude patterns as final filter
            if (config.ExcludePatterns.Any())
            {
                var beforeCount = commands.Count;
                logger.LogDebug($"Applying {config.ExcludePatterns.Count} exclude patterns to {beforeCount} commands...");
                commands = ApplyExcludePatterns(commands, config.ExcludePatterns, logger);
                logger.LogInformation($"Excluded {beforeCount - commands.Count} commands based on exclude patterns");

                // Keep only metadata for remaining commands
                var remainingCommandNames = commands.Select(c => c.Name).ToHashSet();
                var filteredMetadata = metadata.Where(kvp => remainingCommandNames.Contains(kvp.Key))
                                              .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                metadata = filteredMetadata;
            }
            else
            {
                logger.LogDebug("No exclude patterns specified in configuration");
            }

            logger.LogInformation($"Final command count after processing: {commands.Count}");
            logger.LogInformation($"Extracted metadata for {metadata.Count} commands");

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Final command list:");
                foreach (var cmd in commands.OrderBy(c => c.Name))
                {
                    logger.LogDebug($"  {cmd.Name} ({cmd.CommandType})");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error discovering PowerShell commands");
        }

        return (commands, metadata);
    }

    /// <summary>
    /// Gets available PowerShell commands to generate methods for based on configuration (legacy method)
    /// </summary>
    private static List<CommandInfo> GetAvailableCommands(PowerShellConfiguration config, ILogger logger)
    {
        var (commands, _) = GetAvailableCommandsWithMetadata(config, logger);
        return commands;
    }

    private static List<CommandInfo> GetCommandsByName(List<string> functionNames, PSPowerShell powerShell, ILogger logger)
    {
        var commands = new List<CommandInfo>();

        foreach (var cmdName in functionNames)
        {
            try
            {
                powerShell.Commands.Clear();
                powerShell.AddCommand("Get-Command").AddParameter("Name", cmdName).AddParameter("ErrorAction", "SilentlyContinue");
                var cmdInfo = powerShell.Invoke<CommandInfo>().FirstOrDefault();
                powerShell.Commands.Clear();

                if (cmdInfo != null)
                {
                    commands.Add(cmdInfo);
                    logger.LogDebug($"Found command: {cmdName}");
                }
                else
                {
                    logger.LogWarning($"Command '{cmdName}' not found");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error getting command info for '{cmdName}': {ex.Message}");
            }
        }

        return commands;
    }

    private static List<CommandInfo> GetCommandsByModule(List<string> modules, PSPowerShell powerShell, ILogger logger)
    {
        var commands = new List<CommandInfo>();

        foreach (var module in modules)
        {
            try
            {
                powerShell.Commands.Clear();
                powerShell.AddCommand("Get-Command").AddParameter("Module", module).AddParameter("ErrorAction", "SilentlyContinue");
                var moduleCommands = powerShell.Invoke<CommandInfo>();
                powerShell.Commands.Clear();

                commands.AddRange(moduleCommands);
                logger.LogDebug($"Found {moduleCommands.Count} commands in module '{module}'");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error getting commands from module '{module}': {ex.Message}");
            }
        }

        return commands;
    }

    private static List<CommandInfo> GetCommandsByPattern(List<string> includePatterns, List<string> excludePatterns, PSPowerShell powerShell, ILogger logger)
    {
        var commands = new List<CommandInfo>();

        foreach (var pattern in includePatterns)
        {
            try
            {
                powerShell.Commands.Clear();
                powerShell.AddCommand("Get-Command").AddParameter("Name", pattern).AddParameter("ErrorAction", "SilentlyContinue");
                var patternCommands = powerShell.Invoke<CommandInfo>();
                powerShell.Commands.Clear();

                commands.AddRange(patternCommands);
                logger.LogDebug($"Found {patternCommands.Count} commands matching pattern '{pattern}'");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error getting commands for pattern '{pattern}': {ex.Message}");
            }
        }

        return commands;
    }

    private static List<CommandInfo> ApplyIncludePatterns(List<CommandInfo> commands, List<string> includePatterns, ILogger logger)
    {
        var filteredCommands = new List<CommandInfo>();

        foreach (var command in commands)
        {
            bool shouldInclude = false;

            foreach (var pattern in includePatterns)
            {
                if (IsWildcardMatch(command.Name, pattern))
                {
                    shouldInclude = true;
                    logger.LogDebug($"Including command '{command.Name}' due to pattern '{pattern}'");
                    break;
                }
            }

            if (shouldInclude)
            {
                filteredCommands.Add(command);
            }
        }

        return filteredCommands;
    }

    private static List<CommandInfo> ApplyExcludePatterns(List<CommandInfo> commands, List<string> excludePatterns, ILogger logger)
    {
        var filteredCommands = new List<CommandInfo>();

        foreach (var command in commands)
        {
            bool shouldExclude = false;

            foreach (var pattern in excludePatterns)
            {
                if (IsWildcardMatch(command.Name, pattern))
                {
                    shouldExclude = true;
                    logger.LogDebug($"Excluding command '{command.Name}' due to pattern '{pattern}'");
                    break;
                }
            }

            if (!shouldExclude)
            {
                filteredCommands.Add(command);
            }
        }

        return filteredCommands;
    }

    private static bool IsWildcardMatch(string input, string pattern)
    {
        // Convert wildcard pattern to regex
        var regexPattern = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Gets available PowerShell commands to generate methods for (legacy method for backwards compatibility)
    /// </summary>
    private static List<CommandInfo> GetAvailableCommands(ILogger logger)
    {
        // Create default configuration for backwards compatibility
        var defaultConfig = new PowerShellConfiguration
        {
            FunctionNames = new List<string> { "Get-SomeData", "Get-SomeOtherData" }
        };

        return GetAvailableCommands(defaultConfig, logger);
    }

    /// <summary>
    /// Gets the appropriate delegate type for a generated method
    /// </summary>
    private static Type GetDelegateTypeForMethod(MethodInfo method)
    {
        var parameters = method.GetParameters();
        var parameterTypes = parameters.Select(p => p.ParameterType).ToArray();
        var returnType = method.ReturnType;

        // For methods returning Task<T>, we need to create a Func<> type
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            if (parameterTypes.Length == 0)
            {
                return typeof(Func<>).MakeGenericType(returnType);
            }
            else if (parameterTypes.Length == 1)
            {
                return typeof(Func<,>).MakeGenericType(parameterTypes[0], returnType);
            }
            else if (parameterTypes.Length == 2)
            {
                return typeof(Func<,,>).MakeGenericType(parameterTypes[0], parameterTypes[1], returnType);
            }
            else if (parameterTypes.Length == 3)
            {
                return typeof(Func<,,,>).MakeGenericType(parameterTypes[0], parameterTypes[1], parameterTypes[2], returnType);
            }
            else if (parameterTypes.Length == 4)
            {
                return typeof(Func<,,,,>).MakeGenericType(parameterTypes[0], parameterTypes[1], parameterTypes[2], parameterTypes[3], returnType);
            }
            else if (parameterTypes.Length == 5)
            {
                return typeof(Func<,,,,,>).MakeGenericType(parameterTypes[0], parameterTypes[1], parameterTypes[2], parameterTypes[3], parameterTypes[4], returnType);
            }
            else if (parameterTypes.Length <= 16)
            {
                // Use the generic Func<> type for up to 16 parameters
                var funcType = Type.GetType($"System.Func`{parameterTypes.Length + 1}");
                if (funcType != null)
                {
                    var allTypes = parameterTypes.Concat(new[] { returnType }).ToArray();
                    return funcType.MakeGenericType(allTypes);
                }
            }
        }

        // Fallback: create a generic delegate
        throw new NotSupportedException($"Cannot create delegate type for method with {parameterTypes.Length} parameters and return type {returnType.Name}");
    }

    /// <summary>
    /// Converts a normalized method name back to the original command name
    /// </summary>
    private static string DeNormalizeMethodName(string methodName)
    {
        // This is a simple reverse of the normalization process
        // Replace underscores with hyphens for PowerShell command names
        return methodName.Replace("_", "-");
    }

    /// <summary>
    /// Gets information about the generated assembly for debugging
    /// </summary>
    public static string GetAssemblyInfo(ILogger logger)
    {
        try
        {
            if (_generatedAssembly == null)
            {
                return "No assembly has been generated yet";
            }

            var info = $"Generated Assembly: {_generatedAssembly.FullName}\n";
            info += $"Location: {_generatedAssembly.Location}\n";
            info += $"Dynamic: {_generatedAssembly.IsDynamic}\n";

            if (_generatedMethods != null)
            {
                info += $"Generated Methods ({_generatedMethods.Count}):\n";
                foreach (var kvp in _generatedMethods)
                {
                    var method = kvp.Value;
                    var parameters = method.GetParameters();
                    var paramSignature = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
                    info += $"  {method.ReturnType.Name} {method.Name}({paramSignature})\n";
                }
            }

            return info;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting assembly info");
            return $"Error getting assembly info: {ex.Message}";
        }
    }

    /// <summary>
    /// Tests the generated assembly by invoking a specific method
    /// </summary>
    public static async Task<string> TestGeneratedMethod(string methodName, object[] parameters, ILogger logger)
    {
        try
        {
            if (_generatedMethods == null || _generatedInstance == null)
            {
                return "Assembly has not been generated yet";
            }

            if (!_generatedMethods.TryGetValue(methodName, out var method))
            {
                return $"Method '{methodName}' not found in generated assembly";
            }

            logger.LogInformation($"Testing method '{methodName}' with {parameters.Length} parameters");

            // Add CancellationToken as the last parameter if not provided
            var methodParams = method.GetParameters();
            var allParameters = parameters.ToList();

            if (methodParams.Length > parameters.Length && methodParams.Last().ParameterType == typeof(CancellationToken))
            {
                allParameters.Add(CancellationToken.None);
            }

            // Invoke the method
            var result = method.Invoke(_generatedInstance, allParameters.ToArray());

            if (result is Task<string> taskResult)
            {
                var output = await taskResult;
                logger.LogInformation($"Method '{methodName}' completed successfully");
                return output;
            }

            return result?.ToString() ?? "null";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error testing method '{methodName}'");
            return $"Error testing method '{methodName}': {ex.Message}";
        }
    }

    /// <summary>
    /// Gets a list of MCP tools from available PowerShell commands using dynamic assembly generation (backwards compatible)
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <returns>List of MCP server tools</returns>
    public static List<McpServerTool> GetToolsList(ILogger logger)
    {
        // Create default configuration for backwards compatibility
        var defaultConfig = new PowerShellConfiguration
        {
            FunctionNames = new List<string> { "Get-SomeData", "Get-SomeOtherData" }
        };

        return GetToolsList(defaultConfig, logger);
    }
}
