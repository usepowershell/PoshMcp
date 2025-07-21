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
/// Factory class for creating MCP tools from PowerShell commands using dynamically generated assemblies
/// </summary>
public static class McpToolFactoryV2
{
    private static Assembly? _generatedAssembly;
    private static object? _generatedInstance;
    private static Dictionary<string, MethodInfo>? _generatedMethods;

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

            // Get all available PowerShell commands based on configuration
            var commands = GetAvailableCommands(config, logger);

            if (!commands.Any())
            {
                logger.LogWarning("No PowerShell commands found");
                return new List<McpServerTool>();
            }

            logger.LogInformation($"Found {commands.Count} PowerShell commands");

            // Generate the assembly with all command methods
            _generatedAssembly = PowerShellDynamicAssemblyGenerator.GenerateAssembly(commands, logger);
            _generatedInstance = PowerShellDynamicAssemblyGenerator.GetGeneratedInstance(logger);
            _generatedMethods = PowerShellDynamicAssemblyGenerator.GetGeneratedMethods();

            logger.LogInformation($"Generated assembly with {_generatedMethods.Count} methods");

            // Create MCP tools from generated methods
            var tools = new List<McpServerTool>();

            foreach (var kvp in _generatedMethods)
            {
                var methodName = kvp.Key;
                var method = kvp.Value;

                try
                {
                    // Create a delegate from the method
                    var delegateType = GetDelegateTypeForMethod(method);
                    var methodDelegate = Delegate.CreateDelegate(delegateType, _generatedInstance, method);

                    // Create MCP tool options
                    var originalCommandName = DeNormalizeMethodName(methodName);
                    var options = new McpServerToolCreateOptions
                    {
                        Name = methodName.ToLowerInvariant(),
                        Description = $"PowerShell command: {originalCommandName}",
                        Destructive = false,
                        Idempotent = false,
                        OpenWorld = true,
                        ReadOnly = false,
                        Title = originalCommandName,
                        // unless the method's return type is Task<string> or Task<IEnumerable<string>> this should be true
                        UseStructuredContent = true
                    };

                    logger.LogDebug($"Creating MCP tool for method '{methodName}' with delegate type: {delegateType.Name}");

                    // Create the MCP server tool
                    var tool = McpServerTool.Create(methodDelegate, options);
                    tools.Add(tool);

                    logger.LogInformation($"Successfully created MCP tool '{options.Name}' for command '{originalCommandName}'");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"Failed to create MCP tool for method '{methodName}': {ex.Message}");
                }
            }

            logger.LogInformation($"Successfully created {tools.Count} MCP tools from dynamic assembly");
            return tools;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to generate MCP tools using dynamic assembly: {ex.Message}");
            return new List<McpServerTool>();
        }
    }

    /// <summary>
    /// Gets available PowerShell commands to generate methods for based on configuration
    /// </summary>
    private static List<CommandInfo> GetAvailableCommands(PowerShellConfiguration config, ILogger logger)
    {
        var powerShell = PowerShellRunspaceHolder.Instance;
        var commands = new List<CommandInfo>();

        try
        {
            logger.LogInformation("Processing PowerShell configuration...");

            // Always process function names if specified
            if (config.FunctionNames.Any())
            {
                var namedCommands = GetCommandsByName(config.FunctionNames, powerShell, logger);
                commands.AddRange(namedCommands);
                logger.LogInformation($"Added {namedCommands.Count} commands by name");
            }

            // Always process modules if specified
            if (config.Modules.Any())
            {
                var moduleCommands = GetCommandsByModule(config.Modules, powerShell, logger);
                commands.AddRange(moduleCommands.Where(mc => !commands.Any(c => c.Name == mc.Name)));
                logger.LogInformation($"Added {moduleCommands.Count(mc => !commands.Any(c => c.Name == mc.Name))} new commands from modules");
            }

            // Apply include patterns if specified to filter configured commands 
            if (config.IncludePatterns.Any())
            {
                var beforeCount = commands.Count;
                commands = ApplyIncludePatterns(commands, config.IncludePatterns, logger);
                logger.LogInformation($"Included {commands.Count - beforeCount} commands based on include patterns");
            }

            // Apply exclude patterns as final filter
            if (config.ExcludePatterns.Any())
            {
                var beforeCount = commands.Count;
                commands = ApplyExcludePatterns(commands, config.ExcludePatterns, logger);
                logger.LogInformation($"Excluded {beforeCount - commands.Count} commands based on exclude patterns");
            }

            logger.LogInformation($"Final command count after processing: {commands.Count}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error discovering PowerShell commands");
        }

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
