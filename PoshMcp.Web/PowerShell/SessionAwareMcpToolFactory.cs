using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using PoshMcp.Server.PowerShell;

namespace PoshMcp.Web.PowerShell;

/// <summary>
/// Session-aware MCP tool factory that creates tools with session-specific PowerShell runspaces
/// </summary>
public class SessionAwareMcpToolFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly PowerShellConfiguration _config;
    private readonly ILogger<SessionAwareMcpToolFactory> _logger;

    public SessionAwareMcpToolFactory(
        IServiceProvider serviceProvider,
        PowerShellConfiguration config,
        ILogger<SessionAwareMcpToolFactory> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates MCP tools for the current request/session context
    /// </summary>
    /// <returns>List of MCP tools with session-aware runspace</returns>
    public List<McpServerTool> CreateSessionTools()
    {
        try
        {
            _logger.LogDebug("Creating session-aware MCP tools");

            // Get the session-aware runspace from the DI container (scoped to current request)
            var sessionRunspace = _serviceProvider.GetRequiredService<IPowerShellRunspace>();
            
            // Create tool factory with the session-specific runspace
            var toolFactory = new McpToolFactoryV2(sessionRunspace);
            var tools = toolFactory.GetToolsList(_config, _logger);

            // Add configuration reload tools if enabled
            if (_config.EnableDynamicReloadTools)
            {
                var reloadTools = CreateConfigurationReloadTools(sessionRunspace, toolFactory);
                AddConfigurationReloadToolsToList(tools, reloadTools);
                _logger.LogInformation($"Created {tools.Count} session-aware tools (including 3 configuration reload tools)");
            }
            else
            {
                _logger.LogInformation($"Created {tools.Count} session-aware tools (dynamic reload tools are disabled)");
            }

            return tools;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create session-aware MCP tools");
            return new List<McpServerTool>();
        }
    }

    private ConfigurationReloadTools CreateConfigurationReloadTools(IPowerShellRunspace sessionRunspace, McpToolFactoryV2 toolFactory)
    {
        var reloadServiceLogger = _serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<PowerShellConfigurationReloadService>();
        var configPath = "appsettings.json"; // Web apps typically use appsettings.json in the root
        var reloadService = new PowerShellConfigurationReloadService(reloadServiceLogger, toolFactory, _config, configPath);
        var reloadToolsLogger = _serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<ConfigurationReloadTools>();
        return new ConfigurationReloadTools(reloadService, reloadToolsLogger);
    }

    private static void AddConfigurationReloadToolsToList(List<McpServerTool> tools, ConfigurationReloadTools reloadTools)
    {
        var reloadFromFileTool = CreateReloadFromFileToolInstance(reloadTools);
        var updateConfigTool = CreateUpdateConfigurationToolInstance(reloadTools);
        var getConfigStatusTool = CreateGetConfigurationStatusToolInstance(reloadTools);

        tools.Add(reloadFromFileTool);
        tools.Add(updateConfigTool);
        tools.Add(getConfigStatusTool);
    }

    private static McpServerTool CreateReloadFromFileToolInstance(ConfigurationReloadTools reloadTools)
    {
        var reloadConfigFromFileDelegate = new Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<string>>(reloadTools.ReloadConfigurationFromFile);
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

    private static McpServerTool CreateUpdateConfigurationToolInstance(ConfigurationReloadTools reloadTools)
    {
        var updateConfigDelegate = new Func<string, System.Threading.CancellationToken, System.Threading.Tasks.Task<string>>(reloadTools.UpdateConfiguration);
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

    private static McpServerTool CreateGetConfigurationStatusToolInstance(ConfigurationReloadTools reloadTools)
    {
        var getConfigStatusDelegate = new Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<string>>(reloadTools.GetConfigurationStatus);
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
}