using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.Metrics;
using PoshMcp.Web.PowerShell;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PoshMcp.Web;

public class Program
{
    // disable the warning for a second calling of BuildServiceProvider
    // which is needed to pass logging in to the dynamically generated 
    // assembly with the wrappers for the powershell commands.
#pragma warning disable ASP0000
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Configure PowerShell configuration binding
        builder.Services.Configure<PowerShellConfiguration>(
            builder.Configuration.GetSection("PowerShellConfiguration"));

        // Configure JSON serializer options
        builder.Services.Configure<JsonSerializerOptions>(options =>
        {
            options.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            options.MaxDepth = 128;
            options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.WriteIndented = false;
        });

        // Add CORS for MCP session header support
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .WithExposedHeaders("Mcp-Session-Id");
            });
        });

        // Register HTTP context accessor for MCP session header access
        builder.Services.AddHttpContextAccessor();

        // Register session-aware PowerShell runspace as singleton
        // This proxy will create session-specific runspaces internally based on Mcp-Session-Id header
        builder.Services.AddSingleton<IPowerShellRunspace, SessionAwarePowerShellRunspace>();

        // Build service provider to get configuration and logger
        var tempServiceProvider = builder.Services.BuildServiceProvider();
        var logger = tempServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PoshMcpWebLogger");
        var config = tempServiceProvider.GetRequiredService<IOptions<PowerShellConfiguration>>().Value;

        // Setup MCP tools with session-aware approach using Mcp-Session-Id header
        var tools = SetupSessionAwareMcpTools(tempServiceProvider, config, logger);

        // Configure OpenTelemetry metrics
        builder.Services.AddSingleton<McpMetrics>();

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metricsBuilder =>
            {
                metricsBuilder
                    .AddMeter(McpMetrics.MeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddConsoleExporter();
            });

        // Configure metrics in the factories
        var metrics = new McpMetrics();
        McpToolFactoryV2.SetMetrics(metrics);
        PowerShellAssemblyGenerator.SetMetrics(metrics);

        // Configure MCP server with HTTP transport and discovered tools
        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools(tools);

        // Register cleanup services
        builder.Services.AddSingleton<IHostedService, PowerShellCleanupService>();

        var app = builder.Build();

        // Enable CORS
        app.UseCors();

        // Map MCP endpoints
        app.MapMcp();

        app.Run();
    }
#pragma warning restore ASP0000

    private static List<McpServerTool> SetupSessionAwareMcpTools(IServiceProvider serviceProvider, PowerShellConfiguration config, ILogger logger)
    {
        // Initialize PowerShellRunspaceHolder with configuration for consistency
        PowerShellRunspaceHolder.Initialize(config, logger);

        // Get the session-aware runspace proxy that will be used by all generated tools
        var sessionAwareRunspace = serviceProvider.GetRequiredService<IPowerShellRunspace>();

        // Create tool factory with the session-aware runspace proxy
        var toolFactory = new McpToolFactoryV2(sessionAwareRunspace);
        var tools = toolFactory.GetToolsList(config, logger);

        if (config.EnableDynamicReloadTools)
        {
            var reloadTools = CreateConfigurationReloadTools(serviceProvider, toolFactory, config);
            AddConfigurationReloadToolsToList(tools, reloadTools);
            logger.LogInformation($"Added {tools.Count} total tools (including 3 configuration reload tools) with session-aware runspaces using Mcp-Session-Id header");
        }
        else
        {
            logger.LogInformation($"Added {tools.Count} total tools with session-aware runspaces using Mcp-Session-Id header (dynamic reload tools are disabled)");
        }

        return tools;
    }

    private static ConfigurationReloadTools CreateConfigurationReloadTools(IServiceProvider serviceProvider, McpToolFactoryV2 toolFactory, PowerShellConfiguration config)
    {
        var reloadServiceLogger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<PowerShellConfigurationReloadService>();
        var configPath = "appsettings.json"; // Web apps typically use appsettings.json in the root
        var reloadService = new PowerShellConfigurationReloadService(reloadServiceLogger, toolFactory, config, configPath);
        var reloadToolsLogger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<ConfigurationReloadTools>();
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
}