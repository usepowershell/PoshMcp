using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.Metrics;
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
public class McpToolFactoryV2
{
    private readonly PowerShellAssemblyGenerator _assemblyGenerator;
    private static McpMetrics? _metrics;

    /// <summary>
    /// Sets the metrics instance for OpenTelemetry instrumentation
    /// </summary>
    /// <param name="metrics">McpMetrics instance</param>
    public static void SetMetrics(McpMetrics metrics)
    {
        _metrics = metrics;
    }

    /// <summary>
    /// Initializes a new instance of McpToolFactoryV2 with default runspace
    /// </summary>
    public McpToolFactoryV2()
    {
        _assemblyGenerator = new PowerShellAssemblyGenerator(new SingletonPowerShellRunspace());
    }

    /// <summary>
    /// Initializes a new instance of McpToolFactoryV2 with specified runspace
    /// </summary>
    /// <param name="runspace">PowerShell runspace to use</param>
    public McpToolFactoryV2(IPowerShellRunspace runspace)
    {
        _assemblyGenerator = new PowerShellAssemblyGenerator(runspace);
    }

    /// <summary>
    /// Clears the cached assembly to force regeneration on next GetToolsList call
    /// </summary>
    public void ClearCache()
    {
        _assemblyGenerator.ClearCache();
    }

    /// <summary>
    /// Gets PowerShell command metadata including parameter-set-specific syntax and verb information
    /// </summary>
    /// <param name="commandInfo">CommandInfo to analyze</param>
    /// <param name="parameterSet">Specific parameter set to generate description for</param>
    /// <param name="powerShell">PowerShell instance for executing queries</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Command metadata with parameter-set-specific syntax, verb info, and safety characteristics</returns>
    private PowerShellCommandMetadata GetCommandMetadata(CommandInfo commandInfo, CommandParameterSetInfo parameterSet, PSPowerShell powerShell, ILogger logger)
    {
        var metadata = CreateDefaultCommandMetadata(commandInfo);
        try
        {
            SetParameterSetDescription(metadata, commandInfo, parameterSet, logger);
            AnalyzeCommandVerbSafety(metadata, commandInfo, powerShell, logger);
        }
        catch (Exception ex)
        {
            logger.LogError($"Error getting metadata for command {commandInfo.Name}: {ex.Message}");
        }

        LogCommandMetadata(metadata, parameterSet, logger);
        return metadata;
    }

    internal static PowerShellCommandMetadata CreateDefaultCommandMetadata(CommandInfo commandInfo)
    {
        return new PowerShellCommandMetadata
        {
            CommandName = commandInfo.Name,
            Description = commandInfo.Name,
            IsDestructive = false,
            IsReadOnly = false,
            IsIdempotent = false
        };
    }

    private static void SetParameterSetDescription(PowerShellCommandMetadata metadata, CommandInfo commandInfo, CommandParameterSetInfo parameterSet, ILogger logger)
    {
        try
        {
            var parameterSetSyntax = parameterSet.ToString();
            if (!string.IsNullOrWhiteSpace(parameterSetSyntax))
            {
                metadata.Description = $"{commandInfo.Name} {parameterSetSyntax}";
                logger.LogDebug($"Generated parameter-set-specific description for {commandInfo.Name} ({parameterSet.Name}): {metadata.Description}");
            }
            else
            {
                metadata.Description = commandInfo.Name;
                logger.LogDebug($"No parameter set syntax available for {commandInfo.Name} ({parameterSet.Name}), using command name as fallback");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning($"Could not generate parameter-set syntax for command {commandInfo.Name} ({parameterSet.Name}): {ex.Message}");
            metadata.Description = commandInfo.Name;
        }
    }

    private static void AnalyzeCommandVerbSafety(PowerShellCommandMetadata metadata, CommandInfo commandInfo, PSPowerShell powerShell, ILogger logger)
    {
        try
        {
            var verbPart = ExtractVerbFromCommandName(commandInfo.Name);
            var verbResult = ExecuteGetVerbCommand(verbPart, powerShell);

            if (verbResult.Count > 0 && verbResult[0] != null)
            {
                AnalyzeVerbGroupSafety(metadata, verbPart, verbResult[0], logger);
            }
            else
            {
                AnalyzeVerbWithBasicRules(metadata, verbPart, logger);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning($"Could not analyze verb for command {commandInfo.Name}: {ex.Message}");
        }
    }

    internal static string ExtractVerbFromCommandName(string commandName)
    {
        return commandName.Contains('-') ? commandName.Split('-')[0] : commandName;
    }

    private static Collection<PSObject> ExecuteGetVerbCommand(string verbPart, PSPowerShell powerShell)
    {
        powerShell.Commands.Clear();
        powerShell.AddCommand("Get-Verb")
                 .AddParameter("Verb", verbPart)
                 .AddParameter("ErrorAction", "SilentlyContinue");

        var verbResult = powerShell.Invoke();
        powerShell.Commands.Clear();
        return verbResult;
    }

    private static void AnalyzeVerbGroupSafety(PowerShellCommandMetadata metadata, string verbPart, PSObject verbObject, ILogger logger)
    {
        var groupProperty = verbObject.Properties["Group"];
        if (groupProperty?.Value != null)
        {
            var group = groupProperty.Value.ToString();
            if (!string.IsNullOrEmpty(group))
            {
                logger.LogDebug($"Command {metadata.CommandName} has verb group: {group}");
                SetSafetyBasedOnVerbGroup(metadata, verbPart, group);
            }
        }
    }

    private static void SetSafetyBasedOnVerbGroup(PowerShellCommandMetadata metadata, string verbPart, string group)
    {
        switch (group?.ToUpperInvariant())
        {
            case "COMMON":
                SetCommonVerbSafety(metadata, verbPart);
                break;
            case "DATA":
                SetDataVerbSafety(metadata, verbPart);
                break;
            case "LIFECYCLE":
                metadata.IsDestructive = true;
                break;
            case "SECURITY":
                metadata.IsDestructive = true;
                break;
            case "DIAGNOSTIC":
                metadata.IsReadOnly = true;
                metadata.IsIdempotent = true;
                break;
        }
    }

    private static void SetCommonVerbSafety(PowerShellCommandMetadata metadata, string verbPart)
    {
        if (IsReadOnlyVerb(verbPart))
        {
            metadata.IsReadOnly = true;
            metadata.IsIdempotent = true;
        }
        else if (verbPart.Equals("Set", StringComparison.OrdinalIgnoreCase))
        {
            metadata.IsIdempotent = true;
        }
        else if (IsDestructiveVerb(verbPart))
        {
            metadata.IsDestructive = true;
        }
    }

    private static void SetDataVerbSafety(PowerShellCommandMetadata metadata, string verbPart)
    {
        if (IsDestructiveVerb(verbPart))
        {
            metadata.IsDestructive = true;
        }
        else if (verbPart.Equals("Set", StringComparison.OrdinalIgnoreCase))
        {
            metadata.IsIdempotent = true;
        }
    }

    private static void AnalyzeVerbWithBasicRules(PowerShellCommandMetadata metadata, string verbPart, ILogger logger)
    {
        logger.LogDebug($"No Get-Verb info found for {verbPart}, using basic analysis");

        if (IsReadOnlyVerb(verbPart))
        {
            metadata.IsReadOnly = true;
            metadata.IsIdempotent = true;
        }
        else if (IsDestructiveVerb(verbPart))
        {
            metadata.IsDestructive = true;
        }
        else if (verbPart.Equals("Set", StringComparison.OrdinalIgnoreCase))
        {
            metadata.IsIdempotent = true;
        }
    }

    internal static bool IsReadOnlyVerb(string verbPart)
    {
        return verbPart.Equals("Get", StringComparison.OrdinalIgnoreCase) ||
               verbPart.Equals("Find", StringComparison.OrdinalIgnoreCase) ||
               verbPart.Equals("Search", StringComparison.OrdinalIgnoreCase) ||
               verbPart.Equals("Show", StringComparison.OrdinalIgnoreCase) ||
               verbPart.Equals("Test", StringComparison.OrdinalIgnoreCase) ||
               verbPart.Equals("Measure", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsDestructiveVerb(string verbPart)
    {
        return verbPart.Equals("Remove", StringComparison.OrdinalIgnoreCase) ||
               verbPart.Equals("Delete", StringComparison.OrdinalIgnoreCase) ||
               verbPart.Equals("Clear", StringComparison.OrdinalIgnoreCase) ||
               verbPart.Equals("Stop", StringComparison.OrdinalIgnoreCase) ||
               verbPart.Equals("Kill", StringComparison.OrdinalIgnoreCase) ||
               verbPart.Equals("Start", StringComparison.OrdinalIgnoreCase) ||
               verbPart.Equals("Add", StringComparison.OrdinalIgnoreCase);
    }

    private static void LogCommandMetadata(PowerShellCommandMetadata metadata, CommandParameterSetInfo parameterSet, ILogger logger)
    {
        logger.LogDebug($"Command metadata for {metadata.CommandName} ({parameterSet.Name}): Description='{metadata.Description}', IsDestructive={metadata.IsDestructive}, IsReadOnly={metadata.IsReadOnly}, IsIdempotent={metadata.IsIdempotent}");
    }

    /// <summary>
    /// Gets a list of MCP tools from available PowerShell commands using dynamic assembly generation
    /// </summary>
    /// <param name="config">PowerShell configuration</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>List of MCP server tools</returns>
    public List<McpServerTool> GetToolsList(PowerShellConfiguration config, ILogger logger)
    {
        try
        {
            LogToolGenerationStart(logger, config);
            var commands = ValidateAndGetCommands(config, logger);
            if (!commands.Any()) return new List<McpServerTool>();

            var (generatedAssembly, generatedInstance, generatedMethods) = GenerateAssemblyAndMethods(commands, logger);
            var methodToCommandMap = CreateCommandMetadataMapping(commands, logger);
            var tools = CreateMcpToolsFromMethods(generatedMethods, generatedInstance, methodToCommandMap, logger);
            LogToolGenerationResults(tools, logger);
            return tools;
        }
        catch (Exception ex)
        {
            return HandleToolGenerationError(ex, logger);
        }
    }

    private static void LogToolGenerationStart(ILogger logger, PowerShellConfiguration config)
    {
        logger.LogInformation("Starting MCP tools generation using dynamic assembly approach");
        logger.LogTrace("Tool factory configuration:");
        logger.LogTrace($"  Config type: {config.GetType().Name}");
    }

    private List<CommandInfo> ValidateAndGetCommands(PowerShellConfiguration config, ILogger logger)
    {
        var commands = GetAvailableCommands(config, logger);
        if (!commands.Any())
        {
            LogNoCommandsFound(logger);
        }
        return commands;
    }

    private static void LogNoCommandsFound(ILogger logger)
    {
        logger.LogWarning("No PowerShell commands found - check configuration and PowerShell environment");
        logger.LogDebug("Suggestions:");
        logger.LogDebug("  - Verify function names exist in PowerShell");
        logger.LogDebug("  - Check if specified modules are available");
        logger.LogDebug("  - Review include/exclude patterns");
    }

    private (Assembly assembly, object instance, Dictionary<string, MethodInfo> methods) GenerateAssemblyAndMethods(List<CommandInfo> commands, ILogger logger)
    {
        logger.LogInformation($"Found {commands.Count} PowerShell commands");
        logger.LogTrace($"Commands to process: [{string.Join(", ", commands.Select(c => c.Name))}]");

        logger.LogDebug("Generating dynamic assembly for PowerShell commands...");
        var generatedAssembly = _assemblyGenerator.GenerateAssembly(commands, logger);
        var generatedInstance = _assemblyGenerator.GetGeneratedInstance(logger);
        var generatedMethods = _assemblyGenerator.GetGeneratedMethods();

        LogGeneratedAssemblyDetails(generatedMethods, logger);
        return (generatedAssembly, generatedInstance, generatedMethods);
    }

    private static void LogGeneratedAssemblyDetails(Dictionary<string, MethodInfo> generatedMethods, ILogger logger)
    {
        logger.LogInformation($"Generated assembly with {generatedMethods.Count} methods");
        if (logger.IsEnabled(LogLevel.Trace))
        {
            LogGeneratedMethodDetails(generatedMethods, logger);
        }
    }

    private static void LogGeneratedMethodDetails(Dictionary<string, MethodInfo> generatedMethods, ILogger logger)
    {
        logger.LogTrace("Generated methods:");
        foreach (var method in generatedMethods.OrderBy(m => m.Key))
        {
            logger.LogTrace($"  {method.Key} -> {method.Value.ReturnType.Name} with {method.Value.GetParameters().Length} parameters");
        }
    }

    private Dictionary<string, PowerShellCommandMetadata> CreateCommandMetadataMapping(List<CommandInfo> commands, ILogger logger)
    {
        var methodToCommandMap = new Dictionary<string, PowerShellCommandMetadata>();
        var powerShell = PowerShellRunspaceHolder.Instance;

        foreach (var command in commands)
        {
            MapParameterSetsToMetadata(command, powerShell, methodToCommandMap, logger);
        }
        return methodToCommandMap;
    }

    private void MapParameterSetsToMetadata(CommandInfo command, PSPowerShell powerShell, Dictionary<string, PowerShellCommandMetadata> methodToCommandMap, ILogger logger)
    {
        foreach (var parameterSet in command.ParameterSets)
        {
            var methodName = PowerShellAssemblyGenerator.SanitizeMethodName(command.Name, parameterSet.Name);
            var metadata = GetCommandMetadata(command, parameterSet, powerShell, logger);
            methodToCommandMap[methodName] = metadata;
            logger.LogDebug($"Created parameter-set-specific metadata for method '{methodName}' from command '{command.Name}' parameter set '{parameterSet.Name}'");
        }
    }

    private List<McpServerTool> CreateMcpToolsFromMethods(Dictionary<string, MethodInfo> generatedMethods, object generatedInstance, Dictionary<string, PowerShellCommandMetadata> methodToCommandMap, ILogger logger)
    {
        var tools = new List<McpServerTool>();
        var successCount = 0;
        var failureCount = 0;

        foreach (var kvp in generatedMethods)
        {
            try
            {
                var tool = CreateSingleMcpTool(kvp, generatedInstance, methodToCommandMap, logger);
                tools.Add(tool);
                successCount++;
            }
            catch (Exception ex)
            {
                failureCount++;
                LogToolCreationFailure(kvp.Key, kvp.Value, ex, logger);
            }
        }

        LogToolCreationSummary(tools.Count, successCount, failureCount, logger);
        return tools;
    }

    private McpServerTool CreateSingleMcpTool(KeyValuePair<string, MethodInfo> methodKvp, object generatedInstance, Dictionary<string, PowerShellCommandMetadata> methodToCommandMap, ILogger logger)
    {
        var methodName = methodKvp.Key;
        var method = methodKvp.Value;

        logger.LogTrace($"Processing method '{methodName}' for MCP tool creation...");

        var delegateType = GetDelegateTypeForMethod(method);
        var methodDelegate = Delegate.CreateDelegate(delegateType, generatedInstance, method);
        var metadata = GetOrCreateMethodMetadata(methodName, methodToCommandMap);
        var options = CreateMcpToolOptions(methodName, metadata);

        LogToolCreationSuccess(methodName, delegateType, metadata, logger);
        return McpServerTool.Create(methodDelegate, options);
    }

    private static PowerShellCommandMetadata GetOrCreateMethodMetadata(string methodName, Dictionary<string, PowerShellCommandMetadata> methodToCommandMap)
    {
        return methodToCommandMap.GetValueOrDefault(methodName, new PowerShellCommandMetadata
        {
            CommandName = methodName.Replace("_", "-"),
            Description = methodName.Replace("_", "-"),
            IsDestructive = false,
            IsReadOnly = false,
            IsIdempotent = false
        });
    }

    private static McpServerToolCreateOptions CreateMcpToolOptions(string methodName, PowerShellCommandMetadata metadata)
    {
        return new McpServerToolCreateOptions
        {
            Name = methodName.ToLowerInvariant(),
            Description = metadata.Description,
            Destructive = metadata.IsDestructive,
            Idempotent = metadata.IsIdempotent,
            OpenWorld = true,
            ReadOnly = metadata.IsReadOnly,
            Title = metadata.CommandName,
            UseStructuredContent = true
        };
    }

    private static void LogToolCreationSuccess(string methodName, Type delegateType, PowerShellCommandMetadata metadata, ILogger logger)
    {
        logger.LogDebug($"Creating MCP tool for method '{methodName}' with delegate type: {delegateType.Name}, metadata: Destructive={metadata.IsDestructive}, ReadOnly={metadata.IsReadOnly}, Idempotent={metadata.IsIdempotent}");
        logger.LogInformation($"Successfully created MCP tool '{methodName.ToLowerInvariant()}' for command '{metadata.CommandName}' - {metadata.Description}");
    }

    private static void LogToolCreationFailure(string methodName, MethodInfo method, Exception ex, ILogger logger)
    {
        logger.LogError(ex, $"Failed to create MCP tool for method '{methodName}': {ex.Message}");
        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace($"Method details - Name: {methodName}, Return Type: {method.ReturnType}, Parameters: {method.GetParameters().Length}");
        }
    }

    private static void LogToolCreationSummary(int toolCount, int successCount, int failureCount, ILogger logger)
    {
        logger.LogInformation($"Successfully created {toolCount} MCP tools from dynamic assembly");
        logger.LogDebug($"Tool creation summary: {successCount} succeeded, {failureCount} failed");

        if (failureCount > 0 && logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Some tools failed to be created. This may be due to unsupported parameter types or complex PowerShell objects.");
            logger.LogDebug("The MCP server will still work with the successfully created tools.");
        }
    }

    private static void LogToolGenerationResults(List<McpServerTool> tools, ILogger logger)
    {
        logger.LogInformation($"Tool generation completed with {tools.Count} tools");

        // Record tool registration metrics
        _metrics?.ToolRegistrationTotal.Add(tools.Count,
            new TagList { { "source", "auto-discovered" } });
    }

    private static List<McpServerTool> HandleToolGenerationError(Exception ex, ILogger logger)
    {
        logger.LogError(ex, $"Failed to generate MCP tools using dynamic assembly: {ex.Message}");
        logger.LogDebug("This error prevented any tools from being created. Check PowerShell configuration and environment.");
        return new List<McpServerTool>();
    }

    /// <summary>
    /// Gets available PowerShell commands to generate methods for based on configuration
    /// </summary>
    private (List<CommandInfo> commands, Dictionary<string, PowerShellCommandMetadata> metadata) GetAvailableCommandsWithMetadata(PowerShellConfiguration config, ILogger logger)
    {
        var powerShell = PowerShellRunspaceHolder.Instance;
        var commands = new List<CommandInfo>();
        var metadata = new Dictionary<string, PowerShellCommandMetadata>();

        try
        {
            logger.LogInformation("Processing PowerShell configuration...");
            
            var allFunctionNames = config.GetAllFunctionNames();
            logger.LogTrace("Configuration details:");
            logger.LogTrace($"  Function Names (legacy): [{string.Join(", ", config.FunctionNames)}]");
            logger.LogTrace($"  All Function Names: [{string.Join(", ", allFunctionNames)}]");
            logger.LogTrace($"  Modules: [{string.Join(", ", config.Modules)}]");
            logger.LogTrace($"  Include Patterns: [{string.Join(", ", config.IncludePatterns)}]");
            logger.LogTrace($"  Exclude Patterns: [{string.Join(", ", config.ExcludePatterns)}]");

            // Always process function names if specified
            if (allFunctionNames.Any())
            {
                logger.LogDebug($"Processing {allFunctionNames.Count} function names...");
                var namedCommands = GetCommandsByName(allFunctionNames, powerShell, logger);
                commands.AddRange(namedCommands);
                logger.LogInformation($"Added {namedCommands.Count} commands by name");

                // Extract metadata for named commands
                // (metadata extraction moved to per-parameter-set in GetToolsList)
            }

            // Always process modules if specified
            if (config.Modules.Any())
            {
                logger.LogDebug($"Processing {config.Modules.Count} modules...");
                var moduleCommands = GetCommandsByModule(config.Modules, powerShell, logger);
                var newModuleCommands = moduleCommands.Where(mc => !commands.Any(c => c.Name == mc.Name)).ToList();
                commands.AddRange(newModuleCommands);
                logger.LogInformation($"Added {newModuleCommands.Count} new commands from modules");

                // Extract metadata for module commands
                // (metadata extraction moved to per-parameter-set in GetToolsList)
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
                logger.LogInformation($"Included {commands.Count - beforeCount} commands based on include patterns");
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
                logger.LogInformation($"Excluded {beforeCount - commands.Count} commands based on exclude patterns");
            }
            else
            {
                logger.LogDebug("No exclude patterns specified in configuration");
            }

            logger.LogInformation($"Final command count after processing: {commands.Count}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error discovering PowerShell commands");
        }

        return (commands, new Dictionary<string, PowerShellCommandMetadata>()); // Empty metadata dictionary since it's now generated per-parameter-set
    }

    /// <summary>
    /// Gets available PowerShell commands to generate methods for based on configuration (legacy method)
    /// </summary>
    private List<CommandInfo> GetAvailableCommands(PowerShellConfiguration config, ILogger logger)
    {
        var (commands, _) = GetAvailableCommandsWithMetadata(config, logger);
        return commands;
    }

    private List<CommandInfo> GetCommandsByName(List<string> functionNames, PSPowerShell powerShell, ILogger logger)
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

    private List<CommandInfo> GetCommandsByModule(List<string> modules, PSPowerShell powerShell, ILogger logger)
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

    private List<CommandInfo> GetCommandsByPattern(List<string> includePatterns, List<string> excludePatterns, PSPowerShell powerShell, ILogger logger)
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

    private List<CommandInfo> ApplyIncludePatterns(List<CommandInfo> commands, List<string> includePatterns, ILogger logger)
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

    private List<CommandInfo> ApplyExcludePatterns(List<CommandInfo> commands, List<string> excludePatterns, ILogger logger)
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

    private bool IsWildcardMatch(string input, string pattern)
    {
        // Convert wildcard pattern to regex
        var regexPattern = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Gets available PowerShell commands to generate methods for (legacy method for backwards compatibility)
    /// </summary>
    private List<CommandInfo> GetAvailableCommands(ILogger logger)
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
    private Type GetDelegateTypeForMethod(MethodInfo method)
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
    private string DeNormalizeMethodName(string methodName)
    {
        // This is a simple reverse of the normalization process
        // Replace underscores with hyphens for PowerShell command names
        return methodName.Replace("_", "-");
    }

    /// <summary>
    /// Gets information about the generated assembly for debugging
    /// </summary>
    public string GetAssemblyInfo(ILogger logger)
    {
        try
        {
            var generatedAssembly = _assemblyGenerator.GeneratedAssembly;
            if (generatedAssembly == null)
            {
                return "No assembly has been generated yet";
            }

            var info = $"Generated Assembly: {generatedAssembly.FullName}\n";
            info += $"Location: {generatedAssembly.Location}\n";
            info += $"Dynamic: {generatedAssembly.IsDynamic}\n";

            var generatedMethods = _assemblyGenerator.GetGeneratedMethods();
            if (generatedMethods != null)
            {
                info += $"Generated Methods ({generatedMethods.Count}):\n";
                foreach (var kvp in generatedMethods)
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
    public async Task<string> TestGeneratedMethod(string methodName, object[] parameters, ILogger logger)
    {
        try
        {

            var generatedMethods = _assemblyGenerator.GetGeneratedMethods();
            var generatedInstance = _assemblyGenerator.GetGeneratedInstance(logger);
            if (generatedMethods == null || generatedInstance == null)
            {
                return "Assembly has not been generated yet";
            }

            if (!generatedMethods.TryGetValue(methodName, out var method))
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
            var result = method.Invoke(generatedInstance, allParameters.ToArray());

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
    public List<McpServerTool> GetToolsList(ILogger logger)
    {
        // Create default configuration for backwards compatibility
        var defaultConfig = new PowerShellConfiguration
        {
            FunctionNames = new List<string> { "Get-SomeData", "Get-SomeOtherData" }
        };

        return GetToolsList(defaultConfig, logger);
    }
}
