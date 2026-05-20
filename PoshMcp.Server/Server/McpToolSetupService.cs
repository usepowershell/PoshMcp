using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PoshMcp.Server.Authentication;
using PoshMcp.Server.McpResources;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.OutOfProcess;

namespace PoshMcp;

/// <summary>
/// Provides MCP tool setup and configuration for both Stdio and HTTP transports.
/// Manages tool discovery, factory creation, configuration reload tools, guidance, and troubleshooting.
/// </summary>
internal static class McpToolSetupService
{
    internal sealed record ToolSetupResult(
        List<McpServerTool> Tools,
        EffectiveNounResourceRegistry? EffectiveNounResourceRegistry);

    /// <summary>
    /// Sets up MCP tools for Stdio transport mode.
    /// Creates tool factory, discovers tools, and sets up configuration management tools.
    /// </summary>
    internal static async Task<ToolSetupResult> SetupMcpToolsAsync(
        ILoggerFactory loggerFactory,
        PowerShellConfiguration config,
        ILogger logger,
        string finalConfigPath,
        string configurationPathSource,
        ICommandExecutor? commandExecutor,
        IToolMetadataSource? toolMetadataSource = null)
    {
        // Create RuntimeCachingState singleton and wire into assembly generator static state
        var runtimeCachingState = new RuntimeCachingState();
        PowerShellAssemblyGenerator.SetRuntimeCachingState(runtimeCachingState);
        PowerShellAssemblyGenerator.SetConfiguration(config);
        logger.LogInformation("RuntimeCachingState initialized and wired into PowerShellAssemblyGenerator");

        var importSourceTracker = new ToolImportSourceTracker();
        var toolFactory = CreateToolFactory(config, commandExecutor, runspace: null, toolMetadataSource, importSourceTracker: importSourceTracker);
        var tools = await toolFactory.GetToolsListAsync(config, logger);

        if (config.EnableDynamicReloadTools)
        {
            var reloadTools = CreateConfigurationReloadTools(loggerFactory, toolFactory, config, finalConfigPath, configurationPathSource, "stdio", config.RuntimeMode.ToString(), null, () => tools, importSourceTracker);
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
        AddConfigurationTroubleshootingToolToList(tools, config, finalConfigPath, "stdio", null, config.RuntimeMode.ToString(), null, logger, importSourceTracker: importSourceTracker);

        var effectiveNounRegistry = BuildEffectiveNounResourceRegistry(toolFactory, config, loggerFactory);
        var effectiveCommandOverrides = config.GetEffectiveCommandOverrides();
        if (effectiveNounRegistry is not null || effectiveCommandOverrides.Values.Any(o => !string.IsNullOrWhiteSpace(o.AssociatedResourceUri)))
        {
            var resourcesConfig = ConfigurationLoader.LoadMcpResourcesConfiguration(finalConfigPath, logger);
            tools = ResourceLinkInjector.WrapToolsWithResourceLinks(
                tools,
                effectiveNounRegistry,
                effectiveCommandOverrides,
                resourcesConfig,
                loggerFactory.CreateLogger("ResourceLinkInjector"));
        }

        return new ToolSetupResult(tools, effectiveNounRegistry);
    }

    /// <summary>
    /// Sets up MCP tools for HTTP transport mode.
    /// Similar to SetupMcpToolsAsync but with session-aware runspace and HTTP context support.
    /// </summary>
    internal static async Task<ToolSetupResult> SetupHttpMcpToolsAsync(
        ILoggerFactory loggerFactory,
        PowerShellConfiguration config,
        ILogger logger,
        string finalConfigPath,
        string configurationPathSource,
        IPowerShellRunspace sessionAwareRunspace,
        ICommandExecutor? commandExecutor,
        IHttpContextAccessor? httpContextAccessor = null,
        IToolMetadataSource? toolMetadataSource = null)
    {
        var runtimeCachingState = new RuntimeCachingState();
        PowerShellAssemblyGenerator.SetRuntimeCachingState(runtimeCachingState);
        PowerShellAssemblyGenerator.SetConfiguration(config);
        logger.LogInformation("RuntimeCachingState initialized and wired into PowerShellAssemblyGenerator");

        var importSourceTracker = new ToolImportSourceTracker();
        var toolFactory = CreateToolFactory(config, commandExecutor, sessionAwareRunspace, toolMetadataSource, importSourceTracker: importSourceTracker);
        var tools = await toolFactory.GetToolsListAsync(config, logger);

        if (config.EnableDynamicReloadTools)
        {
            var reloadTools = CreateConfigurationReloadTools(loggerFactory, toolFactory, config, finalConfigPath, configurationPathSource, "http", config.RuntimeMode.ToString(), null, () => tools, importSourceTracker);
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
        AddConfigurationTroubleshootingToolToList(tools, config, finalConfigPath, "http", null, config.RuntimeMode.ToString(), null, logger, httpContextAccessor, importSourceTracker);

        var effectiveNounRegistry = BuildEffectiveNounResourceRegistry(toolFactory, config, loggerFactory);
        var effectiveCommandOverrides = config.GetEffectiveCommandOverrides();
        if (effectiveNounRegistry is not null || effectiveCommandOverrides.Values.Any(o => !string.IsNullOrWhiteSpace(o.AssociatedResourceUri)))
        {
            var resourcesConfig = ConfigurationLoader.LoadMcpResourcesConfiguration(finalConfigPath, logger);
            tools = ResourceLinkInjector.WrapToolsWithResourceLinks(
                tools,
                effectiveNounRegistry,
                effectiveCommandOverrides,
                resourcesConfig,
                loggerFactory.CreateLogger("ResourceLinkInjector"));
        }

        return new ToolSetupResult(tools, effectiveNounRegistry);
    }

    /// <summary>
    /// Discovers available MCP tools from the current configuration.
    /// Used by the --evaluate-tools CLI command and as a helper during tool setup.
    /// </summary>
    internal static async Task<List<McpServerTool>> DiscoverToolsAsync(
        PowerShellConfiguration config,
        ILoggerFactory loggerFactory,
        ILogger logger,
        string configurationPath,
        IToolMetadataSource? toolMetadataSource = null,
        IToolDescriptionSourceTracker? descriptionSourceTracker = null,
        IToolImportSourceTracker? importSourceTracker = null)
    {
        logger.LogInformation("Discovering PowerShell tools...");
        // Spec 011 FR-263-2 / FR-263-10: clear any stale OOP module-imports
        // capture from a prior discovery in this async flow before starting
        // a new lease, so a fresh discovery starts from a clean slate.
        OopModuleImportsCapture.Reset();
        await using var executorLease = await StartOutOfProcessExecutorIfNeededAsync(config, loggerFactory, logger, configurationPath);
        var toolFactory = CreateToolFactory(config, executorLease?.Executor, runspace: null, toolMetadataSource, descriptionSourceTracker, importSourceTracker);
        var tools = await toolFactory.GetToolsListAsync(config, logger);
        // Spec 011 FR-263-2 / FR-263-10: capture the executor's
        // LastModuleImports payload BEFORE the lease disposes (the
        // executor is gone after this method returns). DoctorService
        // reads the capture in BuildDoctorReportForCliAsync.
        if (executorLease?.Executor is not null)
        {
            OopModuleImportsCapture.Set(executorLease.Executor.LastModuleImports);
        }
        AddConfigurationGuidanceToolToList(tools, config, configurationPath, "stdio", config.RuntimeMode.ToString(), null, loggerFactory);
        AddConfigurationTroubleshootingToolToList(tools, config, configurationPath, "stdio", null, config.RuntimeMode.ToString(), null, logger);
        return tools;
    }

    private static EffectiveNounResourceRegistry? BuildEffectiveNounResourceRegistry(
        McpToolFactoryV2 toolFactory,
        PowerShellConfiguration config,
        ILoggerFactory loggerFactory)
    {
        if (!config.EnableNounResources)
        {
            return null;
        }

        var nounRegistry = toolFactory.LastDiscoveredNounRegistry
            ?? NounRegistry.Build(Array.Empty<string>(), loggerFactory.CreateLogger("NounRegistry"));

        return EffectiveNounResourceRegistry.Build(nounRegistry, config.NounResourceOverrides);
    }

    /// <summary>
    /// Creates an appropriate tool factory based on configuration and runtime mode.
    /// Handles both in-process and out-of-process execution modes.
    /// </summary>
    /// <param name="config">Runtime configuration.</param>
    /// <param name="commandExecutor">Out-of-process command executor; required when
    /// <see cref="RuntimeMode"/> is <see cref="RuntimeMode.OutOfProcess"/>.</param>
    /// <param name="runspace">Optional in-process runspace override (HTTP session-aware path).</param>
    /// <param name="toolMetadataSource">Optional spec-010 description source.
    /// When <c>null</c>, <see cref="DefaultToolMetadataSource"/> is selected, which
    /// preserves pre-spec-010 behavior.</param>
    private static McpToolFactoryV2 CreateToolFactory(
        PowerShellConfiguration config,
        ICommandExecutor? commandExecutor,
        IPowerShellRunspace? runspace = null,
        IToolMetadataSource? toolMetadataSource = null,
        IToolDescriptionSourceTracker? descriptionSourceTracker = null,
        IToolImportSourceTracker? importSourceTracker = null)
    {
        if (config.RuntimeMode == RuntimeMode.OutOfProcess)
        {
            return commandExecutor is null
                ? throw new InvalidOperationException("Out-of-process runtime mode requires a started command executor.")
                : new McpToolFactoryV2(commandExecutor, toolMetadataSource, descriptionSourceTracker, importSourceTracker);
        }

        return runspace is null
            ? new McpToolFactoryV2(toolMetadataSource, descriptionSourceTracker, importSourceTracker)
            : new McpToolFactoryV2(runspace, toolMetadataSource, descriptionSourceTracker, importSourceTracker);
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

        var setupTimeout = config.Environment?.SetupTimeoutSeconds is > 0
            ? TimeSpan.FromSeconds(config.Environment.SetupTimeoutSeconds)
            : TimeSpan.FromSeconds(120);
        ICommandExecutor executor;
        if (config.SubprocessHostMode == SubprocessHostMode.ProcessPool)
        {
            executor = await StartProcessPoolExecutorAsync(config, loggerFactory, logger).ConfigureAwait(false);
        }
        else
        {
            var executorLogger = loggerFactory.CreateLogger<OutOfProcessCommandExecutor>();
            var singleExecutor = new OutOfProcessCommandExecutor(
                executorLogger,
                requestTimeout: null,
                hostMode: config.SubprocessHostMode,
                runspacePoolSize: config.SubprocessRunspacePoolSize);
            await singleExecutor.StartAsync();
            executor = singleExecutor;
            logger.LogInformation(
                "Started out-of-process PowerShell executor (HostMode={HostMode}, PoolSize={PoolSize})",
                config.SubprocessHostMode, config.SubprocessRunspacePoolSize);
        }

        if (config.Environment is not null)
        {
            using var setupCts = new CancellationTokenSource(setupTimeout);
            await executor.SetupAsync(config.Environment, configFilePath, setupTimeout, config.Modules, setupCts.Token);
            logger.LogInformation("Applied environment configuration to out-of-process executor");
        }

        return new OutOfProcessExecutorLease(executor);
    }

    /// <summary>
    /// Builds and starts an <see cref="OutOfProcessSubprocessPool"/> using the
    /// per-process configuration knobs from <paramref name="config"/>.
    /// </summary>
    private static async Task<ICommandExecutor> StartProcessPoolExecutorAsync(
        PowerShellConfiguration config,
        ILoggerFactory loggerFactory,
        ILogger logger)
    {
        // Resolve pwsh + host script via the same logic the single executor uses,
        // but without launching its subprocess.
        var executorLogger = loggerFactory.CreateLogger<OutOfProcessCommandExecutor>();
        var resolver = new OutOfProcessCommandExecutor(executorLogger);
        var hostScriptPath = await resolver.ResolveHostScriptPathAsync().ConfigureAwait(false);
        var pwshPath = OutOfProcessCommandExecutor.ResolvePwshPath();
        await resolver.DisposeAsync().ConfigureAwait(false);

        var poolOptions = new OutOfProcessSubprocessPoolOptions
        {
            PoolSize = config.SubprocessPoolSize > 0 ? config.SubprocessPoolSize : 4,
            MinHealthyForStartup = config.SubprocessMinHealthyForStartup > 0
                ? Math.Min(config.SubprocessMinHealthyForStartup, Math.Max(1, config.SubprocessPoolSize))
                : 1,
        };

        var pool = new OutOfProcessSubprocessPool(
            pwshPath, hostScriptPath, poolOptions, loggerFactory);

        await pool.StartAsync().ConfigureAwait(false);

        logger.LogInformation(
            "Started out-of-process PowerShell executor (ProcessPool, size={PoolSize}, minHealthy={MinHealthy}).",
            poolOptions.PoolSize, poolOptions.MinHealthyForStartup);

        return pool;
    }

    /// <summary>
    /// Disposable wrapper for out-of-process executor lifecycle management.
    /// Ensures proper cleanup of PowerShell executor resources.
    /// </summary>
    internal sealed class OutOfProcessExecutorLease : IAsyncDisposable
    {
        public OutOfProcessExecutorLease(ICommandExecutor executor)
        {
            Executor = executor;
        }

        public ICommandExecutor Executor { get; }

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
        Func<List<McpServerTool>> registeredToolsProvider,
        IToolImportSourceTracker? importSourceTracker)
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
            reloadToolsLogger,
            importSourceTracker);
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
    /// Issue #272: builds the runtime troubleshooting payload with the same
    /// authoritative tool-import attribution used by CLI doctor.
    /// </summary>
    internal static string BuildConfigurationTroubleshootingJson(
        string configurationPath,
        string effectiveTransport,
        string? effectiveSessionMode,
        string? effectiveRuntimeMode,
        string? effectiveMcpPath,
        Func<List<McpServerTool>> registeredToolsProvider,
        ILogger logger,
        AuthenticationConfiguration? authConfig = null,
        Func<System.Security.Claims.ClaimsPrincipal?>? identityProvider = null,
        IToolImportSourceTracker? importSourceTracker = null)
    {
        try
        {
            var configurationErrors = new List<string>();
            PowerShellConfiguration config;

            try
            {
                config = ConfigurationLoader.LoadPowerShellConfiguration(configurationPath, logger, effectiveRuntimeMode);
            }
            catch (Exception ex)
            {
                configurationErrors.Add($"Failed to load PowerShell configuration: {ex.Message}");
                config = new PowerShellConfiguration();
            }

            var tools = new List<McpServerTool>();
            try
            {
                logger.LogInformation("Processing configuration troubleshooting request");
                tools = registeredToolsProvider();
            }
            catch (Exception ex)
            {
                configurationErrors.Add($"Tool discovery failed: {ex.Message}");
            }

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
                currentIdentity: identityProvider?.Invoke(),
                allowConfigurationFileAccess: false,
                importSourceTracker: importSourceTracker);

            if (configurationErrors.Count > 0)
            {
                var mergedErrors = report.ConfigurationErrors.Concat(configurationErrors).ToList();
                report = report with
                {
                    ConfigurationErrors = mergedErrors,
                    Summary = report.Summary with
                    {
                        Status = DoctorReport.ComputeStatus(report with { ConfigurationErrors = mergedErrors })
                    }
                };
            }

            return DoctorService.BuildDoctorJson(report);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating configuration troubleshooting output");
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Unexpected error: {ex.Message}"
            });
        }
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
        Func<System.Security.Claims.ClaimsPrincipal?>? identityProvider = null,
        IToolImportSourceTracker? importSourceTracker = null)
    {
        Func<CancellationToken, Task<string>> troubleshootingDelegate = cancellationToken =>
            Task.FromResult(BuildConfigurationTroubleshootingJson(
                configurationPath,
                effectiveTransport,
                effectiveSessionMode,
                effectiveRuntimeMode,
                effectiveMcpPath,
                registeredToolsProvider,
                logger,
                authConfig,
                identityProvider,
                importSourceTracker));

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
        IHttpContextAccessor? httpContextAccessor = null,
        IToolImportSourceTracker? importSourceTracker = null)
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
            identityProvider: identityProvider,
            importSourceTracker: importSourceTracker));
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
    /// Creates the "set-result-caching" MCP tool if runtime caching is enabled.
    /// </summary>
    private static void AddSetResultCachingToolToList(List<McpServerTool> tools, RuntimeCachingState runtimeCachingState)
    {
        tools.Add(CreateSetResultCachingToolInstance(runtimeCachingState));
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
        Console.Error.WriteLine("=== Tool Discovery Results ===");
        Console.Error.WriteLine($"Total tools discovered: {tools.Count}");
        Console.Error.WriteLine();
    }

    /// <summary>
    /// Writes a success message for tool discovery.
    /// </summary>
    private static void PrintSuccessMessage()
    {
        Console.Error.WriteLine("Successfully created MCP tools from discovered PowerShell commands.");
        Console.Error.WriteLine("Tools are ready to be exposed via the MCP server.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("To start the MCP server with these tools, run without the --evaluate-tools flag.");
    }

    /// <summary>
    /// Writes a message explaining that no tools were discovered.
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

    /// <summary>
    /// Spec 010 FR-582 / FR-583 / SC-207: discovery overload that accepts a metadata
    /// source and a description-source tracker so doctor reporting captures the
    /// resolved precedence step per command and per parameter. The CLI doctor wires
    /// <see cref="HelpAwareToolMetadataSource"/> here so the reported sources match
    /// what the production server (which uses the same metadata source via DI) will
    /// surface to MCP clients.
    /// </summary>
    internal static async Task<List<McpServerTool>> DiscoverToolsForCliAsync(
        PowerShellConfiguration config,
        ILoggerFactory loggerFactory,
        ILogger logger,
        string configurationPath,
        IToolMetadataSource? toolMetadataSource,
        IToolDescriptionSourceTracker? descriptionSourceTracker,
        IToolImportSourceTracker? importSourceTracker)
    {
        return await DiscoverToolsAsync(config, loggerFactory, logger, configurationPath, toolMetadataSource, descriptionSourceTracker, importSourceTracker);
    }
}
