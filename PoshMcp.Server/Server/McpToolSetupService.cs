using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PoshMcp.Server.Authentication;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.OutOfProcess;

namespace PoshMcp;

/// <summary>
/// Provides MCP tool setup and configuration for both Stdio and HTTP transports.
/// Manages tool discovery, factory creation, configuration reload tools, guidance, and troubleshooting.
/// </summary>
internal static class McpToolSetupService
{
    /// <summary>
    /// Sets up MCP tools for Stdio transport mode.
    /// Creates tool factory, discovers tools, and sets up configuration management tools.
    /// </summary>
    internal static async Task<List<McpServerTool>> SetupMcpToolsAsync(
        ILoggerFactory loggerFactory,
        PowerShellConfiguration config,
        ILogger logger,
        string finalConfigPath,
        string configurationPathSource,
        ICommandExecutor? commandExecutor)
    {
        // Create RuntimeCachingState singleton and wire into assembly generator static state
        var runtimeCachingState = new RuntimeCachingState();
        PowerShellAssemblyGenerator.SetRuntimeCachingState(runtimeCachingState);
        PowerShellAssemblyGenerator.SetConfiguration(config);
        logger.LogInformation("RuntimeCachingState initialized and wired into PowerShellAssemblyGenerator");

        var toolFactory = CreateToolFactory(config, commandExecutor);
        var tools = await toolFactory.GetToolsListAsync(config, logger);

        if (config.EnableDynamicReloadTools)
        {
            var reloadTools = CreateConfigurationReloadTools(loggerFactory, toolFactory, config, finalConfigPath, configurationPathSource, "stdio", config.RuntimeMode.ToString(), null, () => tools);
            AddConfigurationReloadToolsToList(tools, reloadTools);
            logger.LogInformation($"Added {tools.Count} total tools (including 3 configuration reload tools)");
        }
        else
        {
            logger.LogInformation($"Added {tools.Count} total tools (dynamic reload tools are disabled)");
        }

        // Always register set-result-caching (not gated by EnableDynamicReloadTools)
        var setResultCachingTool = CreateSetResultCachingToolInstance(runtimeCachingState);
        tools.Add(setResultCachingTool);
        logger.LogInformation("Registered set-result-caching tool (always enabled)");

        AddConfigurationGuidanceToolToList(tools, config, finalConfigPath, "stdio", config.RuntimeMode.ToString(), null, loggerFactory);
        AddConfigurationTroubleshootingToolToList(tools, config, finalConfigPath, "stdio", null, config.RuntimeMode.ToString(), null, logger);

        return tools;
    }

    /// <summary>
    /// Sets up MCP tools for HTTP transport mode.
    /// Similar to SetupMcpToolsAsync but with session-aware runspace and HTTP context support.
    /// </summary>
    internal static async Task<List<McpServerTool>> SetupHttpMcpToolsAsync(
        ILoggerFactory loggerFactory,
        PowerShellConfiguration config,
        ILogger logger,
        string finalConfigPath,
        string configurationPathSource,
        IPowerShellRunspace sessionAwareRunspace,
        ICommandExecutor? commandExecutor,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        var runtimeCachingState = new RuntimeCachingState();
        PowerShellAssemblyGenerator.SetRuntimeCachingState(runtimeCachingState);
        PowerShellAssemblyGenerator.SetConfiguration(config);
        logger.LogInformation("RuntimeCachingState initialized and wired into PowerShellAssemblyGenerator");

        var toolFactory = CreateToolFactory(config, commandExecutor, sessionAwareRunspace);
        var tools = await toolFactory.GetToolsListAsync(config, logger);

        if (config.EnableDynamicReloadTools)
        {
            var reloadTools = CreateConfigurationReloadTools(loggerFactory, toolFactory, config, finalConfigPath, configurationPathSource, "http", config.RuntimeMode.ToString(), null, () => tools);
            AddConfigurationReloadToolsToList(tools, reloadTools);
            logger.LogInformation($"Added {tools.Count} total tools (including 3 configuration reload tools)");
        }
        else
        {
            logger.LogInformation($"Added {tools.Count} total tools (dynamic reload tools are disabled)");
        }

        var setResultCachingTool = CreateSetResultCachingToolInstance(runtimeCachingState);
        tools.Add(setResultCachingTool);
        logger.LogInformation("Registered set-result-caching tool (always enabled)");

        AddConfigurationGuidanceToolToList(tools, config, finalConfigPath, "http", config.RuntimeMode.ToString(), null, loggerFactory);
        AddConfigurationTroubleshootingToolToList(tools, config, finalConfigPath, "http", null, config.RuntimeMode.ToString(), null, logger, httpContextAccessor);

        return tools;
    }

    /// <summary>
    /// Discovers available MCP tools from the current configuration.
    /// Used by the --evaluate-tools CLI command and as a helper during tool setup.
    /// </summary>
    internal static async Task<List<McpServerTool>> DiscoverToolsAsync(
        PowerShellConfiguration config,
        ILoggerFactory loggerFactory,
        ILogger logger,
        string configurationPath)
    {
        logger.LogInformation("Discovering PowerShell tools...");
        await using var executorLease = await StartOutOfProcessExecutorIfNeededAsync(config, loggerFactory, logger, configurationPath);
        var toolFactory = CreateToolFactory(config, executorLease?.Executor);
        var tools = await toolFactory.GetToolsListAsync(config, logger);
        AddConfigurationGuidanceToolToList(tools, config, configurationPath, "stdio", config.RuntimeMode.ToString(), null, loggerFactory);
        AddConfigurationTroubleshootingToolToList(tools, config, configurationPath, "stdio", null, config.RuntimeMode.ToString(), null, logger);
        return tools;
    }

    /// <summary>
    /// Creates an appropriate tool factory based on configuration and runtime mode.
    /// Handles both in-process and out-of-process execution modes.
    /// </summary>
    private static McpToolFactoryV2 CreateToolFactory(
        PowerShellConfiguration config,
        ICommandExecutor? commandExecutor,
        IPowerShellRunspace? runspace = null)
    {
        if (config.RuntimeMode == RuntimeMode.OutOfProcess)
        {
            return commandExecutor is null
                ? throw new InvalidOperationException("Out-of-process runtime mode requires a started command executor.")
                : new McpToolFactoryV2(commandExecutor);
        }

        return runspace is null ? new McpToolFactoryV2() : new McpToolFactoryV2(runspace);
    }

    /// <summary>
    /// Starts an out-of-process PowerShell executor if the runtime mode requires it.
    /// Returns an OutOfProcessExecutorLease that ensures cleanup on disposal.
    /// </summary>
    internal static async Task<OutOfProcessExecutorLease?> StartOutOfProcessExecutorIfNeededAsync(
        PowerShellConfiguration config,
        ILoggerFactory loggerFactory,
        ILogger logger,
        string? configFilePath = null)
    {
        if (config.RuntimeMode != RuntimeMode.OutOfProcess)
        {
            return null;
        }

        var executorLogger = loggerFactory.CreateLogger<OutOfProcessCommandExecutor>();
        var setupTimeout = config.Environment?.SetupTimeoutSeconds is > 0
            ? TimeSpan.FromSeconds(config.Environment.SetupTimeoutSeconds)
            : TimeSpan.FromSeconds(120);
        var executor = new OutOfProcessCommandExecutor(executorLogger);
        await executor.StartAsync();
        logger.LogInformation("Started out-of-process PowerShell executor");

        if (config.Environment is not null)
        {
            using var setupCts = new CancellationTokenSource(setupTimeout);
            await executor.SetupAsync(config.Environment, configFilePath, setupTimeout, config.Modules, setupCts.Token);
            logger.LogInformation("Applied environment configuration to out-of-process executor");
        }

        return new OutOfProcessExecutorLease(executor);
    }

    /// <summary>
    /// Disposable wrapper for out-of-process executor lifecycle management.
    /// Ensures proper cleanup of PowerShell executor resources.
    /// </summary>
    internal sealed class OutOfProcessExecutorLease : IAsyncDisposable
    {
        public OutOfProcessExecutorLease(OutOfProcessCommandExecutor executor)
        {
            Executor = executor;
        }

        public OutOfProcessCommandExecutor Executor { get; }

        public async ValueTask DisposeAsync()
        {
            await Executor.DisposeAsync();
        }
    }

    /// <summary>
    /// Creates configuration reload tools bundle with service and logging dependencies.
    /// </summary>
    private static ConfigurationReloadTools CreateConfigurationReloadTools(
        ILoggerFactory loggerFactory,
        McpToolFactoryV2 toolFactory,
        PowerShellConfiguration config,
        string finalConfigPath,
        string configurationPathSource,
        string effectiveTransport,
        string? effectiveRuntimeMode,
        string? effectiveMcpPath,
        Func<List<McpServerTool>> registeredToolsProvider)
    {
        var reloadServiceLogger = loggerFactory.CreateLogger<PowerShellConfigurationReloadService>();
        var reloadService = new PowerShellConfigurationReloadService(reloadServiceLogger, toolFactory, config, finalConfigPath);
        var reloadToolsLogger = loggerFactory.CreateLogger<ConfigurationReloadTools>();
        return new ConfigurationReloadTools(
            reloadService,
            finalConfigPath,
            configurationPathSource,
            effectiveTransport,
            null,
            effectiveRuntimeMode,
            effectiveMcpPath,
            registeredToolsProvider,
            reloadToolsLogger);
    }

    /// <summary>
    /// Adds three configuration reload tools to the tools list:
    /// - reload-configuration-from-file
    /// - update-configuration
    /// - get-configuration-status
    /// </summary>
    private static void AddConfigurationReloadToolsToList(
        List<McpServerTool> tools,
        ConfigurationReloadTools reloadTools)
    {
        var reloadFromFileTool = CreateReloadFromFileToolInstance(reloadTools);
        var updateConfigTool = CreateUpdateConfigurationToolInstance(reloadTools);
        var getConfigStatusTool = CreateGetConfigurationStatusToolInstance(reloadTools);

        tools.Add(reloadFromFileTool);
        tools.Add(updateConfigTool);
        tools.Add(getConfigStatusTool);
    }

    /// <summary>
    /// Creates the "reload-configuration-from-file" MCP tool.
    /// </summary>
    private static McpServerTool CreateReloadFromFileToolInstance(ConfigurationReloadTools reloadTools)
    {
        var reloadConfigFromFileDelegate = new Func<CancellationToken, Task<string>>(reloadTools.ReloadConfigurationFromFile);
        return McpServerTool.Create(reloadConfigFromFileDelegate, new McpServerToolCreateOptions
        {
            Name = "reload-configuration-from-file",
            Description = "Reloads PowerShell configuration from the configuration file and regenerates available tools",
            Title = "Reload Configuration from File",
            ReadOnly = false,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false,
            UseStructuredContent = true
        });
    }

    /// <summary>
    /// Creates the "update-configuration" MCP tool.
    /// </summary>
    private static McpServerTool CreateUpdateConfigurationToolInstance(ConfigurationReloadTools reloadTools)
    {
        var updateConfigDelegate = new Func<string, CancellationToken, Task<string>>(reloadTools.UpdateConfiguration);
        return McpServerTool.Create(updateConfigDelegate, new McpServerToolCreateOptions
        {
            Name = "update-configuration",
            Description = "Updates PowerShell configuration with new settings and regenerates available tools",
            Title = "Update Configuration",
            ReadOnly = false,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false,
            UseStructuredContent = true
        });
    }

    /// <summary>
    /// Creates the "get-configuration-status" MCP tool.
    /// </summary>
    private static McpServerTool CreateGetConfigurationStatusToolInstance(ConfigurationReloadTools reloadTools)
    {
        var getConfigStatusDelegate = new Func<CancellationToken, Task<string>>(reloadTools.GetConfigurationStatus);
        return McpServerTool.Create(getConfigStatusDelegate, new McpServerToolCreateOptions
        {
            Name = "get-configuration-status",
            Description = "Gets current PowerShell configuration status and tool information",
            Title = "Get Configuration Status",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false,
            UseStructuredContent = true
        });
    }

    /// <summary>
    /// Creates the "get-configuration-troubleshooting" MCP tool.
    /// Calls DoctorService to build diagnostics and render as JSON.
    /// </summary>
    private static McpServerTool CreateConfigurationTroubleshootingToolInstance(
        string configurationPath,
        string effectiveTransport,
        string? effectiveSessionMode,
        string? effectiveRuntimeMode,
        string? effectiveMcpPath,
        Func<List<McpServerTool>> registeredToolsProvider,
        ILogger logger,
        AuthenticationConfiguration? authConfig = null,
        Func<System.Security.Claims.ClaimsPrincipal?>? identityProvider = null)
    {
        Func<CancellationToken, Task<string>> troubleshootingDelegate = cancellationToken =>
        {
            try
            {
                logger.LogInformation("Processing configuration troubleshooting request");

                var config = ConfigurationLoader.LoadPowerShellConfiguration(configurationPath, logger, effectiveRuntimeMode);
                var tools = registeredToolsProvider();
                var report = DoctorService.BuildDoctorReportFromConfig(
                    configurationPath: configurationPath,
                    configurationPathSource: "runtime",
                    effectiveLogLevel: LoggingHelpers.InferEffectiveLogLevel(logger),
                    effectiveLogLevelSource: "runtime",
                    effectiveTransport: effectiveTransport,
                    effectiveTransportSource: "runtime",
                    effectiveSessionMode: effectiveSessionMode,
                    effectiveSessionModeSource: "runtime",
                    effectiveRuntimeMode: effectiveRuntimeMode,
                    effectiveRuntimeModeSource: "runtime",
                    effectiveMcpPath: effectiveMcpPath,
                    effectiveMcpPathSource: "runtime",
                    config: config,
                    tools: tools,
                    authConfig: authConfig,
                    currentIdentity: identityProvider?.Invoke());

                return Task.FromResult(DoctorService.BuildDoctorJson(report));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error generating configuration troubleshooting output");
                return Task.FromResult(JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"Unexpected error: {ex.Message}"
                }));
            }
        };

        return McpServerTool.Create(troubleshootingDelegate, new McpServerToolCreateOptions
        {
            Name = "get-configuration-troubleshooting",
            Description = "Returns doctor-style configuration diagnostics for the running server. Output includes runtime settings, environment variables, PowerShell info, configured functions, MCP definitions, authentication configuration, and caller identity (when available).",
            Title = "Get Configuration Troubleshooting",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false,
            UseStructuredContent = true
        });
    }

    /// <summary>
    /// Adds the "get-configuration-troubleshooting" tool to the tools list if enabled.
    /// </summary>
    private static void AddConfigurationTroubleshootingToolToList(
        List<McpServerTool> tools,
        PowerShellConfiguration config,
        string configurationPath,
        string effectiveTransport,
        string? effectiveSessionMode,
        string? effectiveRuntimeMode,
        string? effectiveMcpPath,
        ILogger logger,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        if (!config.EnableConfigurationTroubleshootingTool)
        {
            return;
        }

        Func<System.Security.Claims.ClaimsPrincipal?>? identityProvider =
            httpContextAccessor is null ? null : () => httpContextAccessor.HttpContext?.User;

        tools.Add(CreateConfigurationTroubleshootingToolInstance(
            configurationPath,
            effectiveTransport,
            effectiveSessionMode,
            effectiveRuntimeMode,
            effectiveMcpPath,
            () => tools,
            logger,
            authConfig: null,
            identityProvider: identityProvider));
    }

    /// <summary>
    /// Creates the "get-configuration-guidance" MCP tool.
    /// Provides guidance for configuring appsettings.json and environment settings.
    /// </summary>
    private static McpServerTool CreateConfigurationGuidanceToolInstance(
        string configurationPath,
        string effectiveTransport,
        string? effectiveRuntimeMode,
        string? effectiveMcpPath,
        ILoggerFactory loggerFactory)
    {
        var guidanceLogger = loggerFactory.CreateLogger<ConfigurationGuidanceTools>();
        var guidanceTools = new ConfigurationGuidanceTools(
            configurationPath,
            effectiveTransport,
            effectiveRuntimeMode,
            effectiveMcpPath,
            guidanceLogger);

        return McpServerTool.Create(guidanceTools.GetConfigurationGuidance, new McpServerToolCreateOptions
        {
            Name = "get-configuration-guidance",
            Description = "Returns configuration guidance for creating and updating appsettings.json, including environment customization and authentication recommendations based on the current runtime transport.",
            Title = "Get Configuration Guidance",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false,
            UseStructuredContent = true
        });
    }

    /// <summary>
    /// Adds the "get-configuration-guidance" tool to the tools list if enabled.
    /// </summary>
    private static void AddConfigurationGuidanceToolToList(
        List<McpServerTool> tools,
        PowerShellConfiguration config,
        string configurationPath,
        string effectiveTransport,
        string? effectiveRuntimeMode,
        string? effectiveMcpPath,
        ILoggerFactory loggerFactory)
    {
        if (!config.EnableConfigurationTroubleshootingTool)
        {
            return;
        }

        tools.Add(CreateConfigurationGuidanceToolInstance(
            configurationPath,
            effectiveTransport,
            effectiveRuntimeMode,
            effectiveMcpPath,
            loggerFactory));
    }

    /// <summary>
    /// Creates the "set-result-caching" MCP tool.
    /// Controls runtime result caching behavior for filter/sort/group operations.
    /// </summary>
    private static McpServerTool CreateSetResultCachingToolInstance(RuntimeCachingState runtimeCachingState)
    {
        Func<string?, string?, string?, CancellationToken, Task<string>> setResultCachingDelegate =
            (enabled, scope, functionName, cancellationToken) =>
            {
                bool? enabledBool = ParseEnabledParameter(enabled);
                var result = runtimeCachingState.HandleSetResultCaching(enabledBool, scope ?? "global", functionName);
                return Task.FromResult(result);
            };

        return McpServerTool.Create(setResultCachingDelegate, new McpServerToolCreateOptions
        {
            Name = "set-result-caching",
            Description = "Enable or disable result caching at runtime. When enabled, command output is cached for replay by filter/sort/group tools. Pass enabled=null or enabled='reset' to clear the runtime override and fall back to configuration. Runtime settings are ephemeral and do not persist across server restarts.",
            Title = "Set Result Caching",
            ReadOnly = false,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false,
            UseStructuredContent = true
        });
    }

    /// <summary>
    /// Parses the "enabled" parameter for result caching tool.
    /// Supports "true", "false", "reset", or null.
    /// </summary>
    private static bool? ParseEnabledParameter(string? enabled)
    {
        if (string.IsNullOrEmpty(enabled) || string.Equals(enabled, "reset", StringComparison.OrdinalIgnoreCase))
            return null;
        if (string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(enabled, "false", StringComparison.OrdinalIgnoreCase))
            return false;
        return null;
    }

    /// <summary>
    /// Infers the configuration path source based on whether a path was explicitly provided.
    /// </summary>
    internal static string InferConfigurationPathSource(string? configurationPath)
    {
        return string.IsNullOrWhiteSpace(configurationPath) ? SettingsResolver.EnvSource : "runtime";
    }

    /// <summary>
    /// Reports tool discovery results to logger and console.
    /// Used by --evaluate-tools CLI command.
    /// </summary>
    internal static void ReportToolDiscoveryResults(List<McpServerTool> tools, ILogger logger)
    {
        PrintToolDiscoveryResults(tools);

        if (tools.Count > 0)
            PrintSuccessMessage();
        else
            PrintNoToolsFoundMessage();

        logger.LogInformation("Tool evaluation completed successfully");
    }

    /// <summary>
    /// Prints tool discovery header and count to stderr.
    /// </summary>
    private static void PrintToolDiscoveryResults(List<McpServerTool> tools)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine($"=== Tool Discovery Results ===");
        Console.Error.WriteLine($"Total tools discovered: {tools.Count}");
        Console.Error.WriteLine();
    }

    /// <summary>
    /// Prints success message when tools are discovered.
    /// </summary>
    private static void PrintSuccessMessage()
    {
        Console.Error.WriteLine("Successfully created MCP tools from discovered PowerShell commands.");
        Console.Error.WriteLine("Tools are ready to be exposed via the MCP server.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("To start the MCP server with these tools, run without the --evaluate-tools flag.");
    }

    /// <summary>
    /// Prints message when no tools are discovered.
    /// Provides diagnostic hints for troubleshooting.
    /// </summary>
    private static void PrintNoToolsFoundMessage()
    {
        Console.Error.WriteLine("No tools were discovered. Check your configuration and PowerShell environment.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Ensure that:");
        Console.Error.WriteLine("- PowerShell commands specified in FunctionNames exist");
        Console.Error.WriteLine("- Modules specified in Modules are available");
        Console.Error.WriteLine("- Include/exclude patterns are not filtering out all commands");
    }

    /// <summary>
    /// Handles and reports tool evaluation errors.
    /// Logs error and prints message to stderr.
    /// </summary>
    internal static void HandleToolEvaluationError(Exception ex, ILogger logger)
    {
        logger.LogError(ex, "Error during tool evaluation: {ErrorMessage}", ex.Message);
        Console.Error.WriteLine($"Error: {ex.Message}");
    }

    /// <summary>
    /// Public wrapper for DiscoverToolsAsync for use by CLI commands.
    /// </summary>
    internal static async Task<List<McpServerTool>> DiscoverToolsForCliAsync(
        PowerShellConfiguration config,
        ILoggerFactory loggerFactory,
        ILogger logger,
        string configurationPath)
    {
        return await DiscoverToolsAsync(config, loggerFactory, logger, configurationPath);
    }
}
