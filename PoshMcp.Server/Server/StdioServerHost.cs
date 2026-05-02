using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using PoshMcp.Server.McpPrompts;
using PoshMcp.Server.McpResources;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.Metrics;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PoshMcp;

/// <summary>
/// Handles all Stdio server startup and configuration logic.
/// Manages logging, configuration loading, service setup, and MCP server initialization for stdio transport.
/// </summary>
internal static class StdioServerHost
{
    /// <summary>
    /// Main entry point for Stdio MCP server startup.
    /// Configures logging, loads configuration, sets up services, and runs the server.
    /// </summary>
    internal static async Task RunMcpServerAsync(
        string[] args,
        LogLevel? overrideLogLevel = null,
        string? explicitConfigPath = null,
        string? configurationPathSource = null,
        string? runtimeModeOverride = null,
        string? logFilePath = null)
    {
        var builder = Host.CreateApplicationBuilder(args);
        ConfigureStdioLogging(builder, overrideLogLevel, logFilePath);
        var finalConfigPath = await ConfigureServerConfiguration(builder, explicitConfigPath);
        var serviceProvider = builder.Services.BuildServiceProvider();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("PoshMcpLogger");
        logger.LogInformation("Using configuration source: {ConfigurationPath}", DescribeConfigurationPath(finalConfigPath));
        var config = ConfigurationLoader.LoadPowerShellConfiguration(finalConfigPath, logger, runtimeModeOverride);
        var resourcesConfig = ConfigurationLoader.LoadMcpResourcesConfiguration(finalConfigPath, logger);
        await using var executorLease = await McpToolSetupService.StartOutOfProcessExecutorIfNeededAsync(config, loggerFactory, logger, finalConfigPath);
        var tools = await McpToolSetupService.SetupMcpToolsAsync(loggerFactory, config, logger, finalConfigPath, configurationPathSource ?? McpToolSetupService.InferConfigurationPathSource(finalConfigPath), executorLease?.Executor);
        var promptsConfig = ConfigurationLoader.LoadPromptsConfiguration(finalConfigPath);
        var configDirectory = Path.GetDirectoryName(finalConfigPath) ?? Directory.GetCurrentDirectory();
        var promptHandler = new McpPromptHandler(promptsConfig, configDirectory, loggerFactory.CreateLogger<McpPromptHandler>());
        ConfigureServerServices(builder, tools, resourcesConfig, finalConfigPath, loggerFactory, promptHandler);
        await builder.Build().RunAsync();
    }

    /// <summary>
    /// Configures logging for stdio transport mode. Always clears default console providers to prevent
    /// stdout/stderr pollution of the MCP JSON-RPC pipe. Optionally wires up a Serilog file sink.
    /// </summary>
    private static void ConfigureStdioLogging(HostApplicationBuilder builder, LogLevel? overrideLogLevel, string? logFilePath)
    {
        builder.Logging.ClearProviders();

        if (!string.IsNullOrWhiteSpace(logFilePath))
        {
            var serilogLogger = new Serilog.LoggerConfiguration()
                .MinimumLevel.Is(LoggingHelpers.MapToSerilogLevel(overrideLogLevel ?? LogLevel.Information))
                .WriteTo.File(
                    logFilePath,
                    rollingInterval: Serilog.RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            builder.Logging.AddSerilog(serilogLogger, dispose: true);
        }
    }

    /// <summary>
    /// Configures server configuration from file and environment variables.
    /// Loads PowerShell and MCP prompts configuration, sets up authentication options.
    /// </summary>
    private static async Task<string> ConfigureServerConfiguration(HostApplicationBuilder builder, string? explicitConfigPath)
    {
        var finalConfigPath = await ConfigurationLoader.ResolveExplicitOrDefaultConfigPath(explicitConfigPath);
        if (!string.IsNullOrWhiteSpace(finalConfigPath) && File.Exists(finalConfigPath))
        {
            builder.Configuration.AddJsonFile(finalConfigPath, optional: false, reloadOnChange: true);
        }
        builder.Configuration.AddEnvironmentVariables();
        builder.Services.Configure<PowerShellConfiguration>(
            builder.Configuration.GetSection("PowerShellConfiguration"));
        builder.Services.Configure<McpPromptsConfiguration>(
            builder.Configuration.GetSection("McpPrompts"));

        builder.Services
            .AddOptions<PoshMcp.Server.Authentication.AuthenticationConfiguration>()
            .BindConfiguration("Authentication")
            .ValidateOnStart();

        builder.Services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<PoshMcp.Server.Authentication.AuthenticationConfiguration>,
            PoshMcp.Server.Authentication.AuthenticationConfigurationValidator>();

        return finalConfigPath;
    }

    /// <summary>
    /// Configures all server services including JSON serialization, OpenTelemetry, and MCP services.
    /// </summary>
    private static void ConfigureServerServices(
        HostApplicationBuilder builder,
        List<McpServerTool> tools,
        McpResourcesConfiguration resourcesConfig,
        string configFilePath,
        ILoggerFactory loggerFactory,
        McpPromptHandler promptHandler)
    {
        ConfigureJsonSerializerOptions(builder);
        ConfigureOpenTelemetry(builder, isStdioMode: true);
        ConfigureApplicationInsights(builder.Services, builder.Configuration, isStdioMode: true);
        RegisterMcpServerServices(builder, tools, resourcesConfig, configFilePath, loggerFactory, promptHandler);
        RegisterCleanupServices(builder);
    }

    /// <summary>
    /// Configures OpenTelemetry metrics and tracing for stdio server.
    /// Conditionally includes console exporter based on transport mode.
    /// </summary>
    private static void ConfigureOpenTelemetry(HostApplicationBuilder builder, bool isStdioMode = false)
    {
        // Register and configure OpenTelemetry metrics
        builder.Services.AddSingleton<McpMetrics>();

        builder.Services.AddOpenTelemetry()
            .WithTracing(tracingBuilder =>
            {
                tracingBuilder.AddSource(PowerShellAssemblyGenerator.ToolActivitySource.Name);
            })
            .WithMetrics(metricsBuilder =>
            {
                metricsBuilder.AddMeter(McpMetrics.MeterName);
                if (!isStdioMode)
                {
                    metricsBuilder.AddConsoleExporter();
                }
            });

        // Configure metrics in the factories after building the service provider
        builder.Services.AddSingleton<IHostedService>(serviceProvider =>
        {
            var metrics = serviceProvider.GetRequiredService<McpMetrics>();
            McpToolFactoryV2.SetMetrics(metrics);
            PowerShellAssemblyGenerator.SetMetrics(metrics);
            return new MetricsConfigurationService();
        });
    }

    /// <summary>
    /// Configures JSON serialization options for the stdio server.
    /// Sets up reference handling, depth limits, and null value handling.
    /// </summary>
    private static void ConfigureJsonSerializerOptions(HostApplicationBuilder builder)
    {
        builder.Services.Configure<JsonSerializerOptions>(options =>
        {
            options.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            options.MaxDepth = 128;
            options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.WriteIndented = false;
        });
    }

    /// <summary>
    /// Configures Application Insights for observability of the server.
    /// Handles connection string setup, sampling configuration, and log filtering.
    /// </summary>
    private static void ConfigureApplicationInsights(
        IServiceCollection services,
        IConfiguration configuration,
        bool isStdioMode)
    {
        var options = configuration.GetSection(PoshMcp.Server.ApplicationInsightsOptions.SectionName).Get<PoshMcp.Server.ApplicationInsightsOptions>()
                      ?? new PoshMcp.Server.ApplicationInsightsOptions();

        if (!options.Enabled)
            return;

        var connectionString = options.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("[WARN] Application Insights is enabled but no connection string was found. " +
                                    "Set ApplicationInsights.ConnectionString in appsettings.json or the " +
                                    "APPLICATIONINSIGHTS_CONNECTION_STRING environment variable.");
            return;
        }

        var samplingPercentage = Math.Clamp(options.SamplingPercentage, 1, 100);
        var transportMode = isStdioMode ? "stdio" : "http";

        services.AddOpenTelemetry()
            .UseAzureMonitor(azureMonitorOptions =>
            {
                azureMonitorOptions.ConnectionString = connectionString;
                azureMonitorOptions.SamplingRatio = samplingPercentage / 100.0f;
            })
            .ConfigureResource(resource =>
                resource.AddAttributes(new[]
                {
                    new KeyValuePair<string, object>("transport.mode", (object)transportMode)
                }));

        // FR-311/FR-312: Suppress OTel log export to Azure Monitor.
        // UseAzureMonitor() registers an OpenTelemetryLoggerProvider that would export
        // all ILogger output (including parameter values logged at Debug level) to App Insights.
        // We only want traces and metrics exported — not logs.
        services.Configure<LoggerFilterOptions>(opts =>
        {
            opts.Rules.Add(new LoggerFilterRule(
                providerName: "OpenTelemetry",
                categoryName: null,
                logLevel: LogLevel.None,
                filter: null));
        });

        Console.Error.WriteLine($"[INFO] Application Insights enabled. Sampling: {samplingPercentage}%");
    }

    /// <summary>
    /// Registers MCP server services with tools, resources, and prompts handlers.
    /// Sets up the MCP server with stdio transport and initializes handlers.
    /// </summary>
    private static void RegisterMcpServerServices(
        HostApplicationBuilder builder,
        List<McpServerTool> tools,
        McpResourcesConfiguration resourcesConfig,
        string configFilePath,
        ILoggerFactory loggerFactory,
        McpPromptHandler promptHandler)
    {
        var runspace = new SingletonPowerShellRunspace();
        var configDirectory = Path.GetDirectoryName(configFilePath) ?? ".";
        var resourceLogger = loggerFactory.CreateLogger<McpResourceHandler>();
        var resourceHandler = new McpResourceHandler(resourcesConfig, runspace, configDirectory, resourceLogger);

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools(tools)
            .WithListResourcesHandler(resourceHandler.HandleListAsync)
            .WithReadResourceHandler(resourceHandler.HandleReadAsync)
            .WithListPromptsHandler(promptHandler.HandleListPromptsAsync)
            .WithGetPromptHandler(promptHandler.HandleGetPromptAsync);
    }

    /// <summary>
    /// Registers cleanup services for stdio transport.
    /// Ensures PowerShell runspaces are properly cleaned up on server shutdown.
    /// </summary>
    private static void RegisterCleanupServices(HostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IHostedService, PowerShellCleanupService>();
    }

    /// <summary>
    /// Describes the configuration path for logging purposes.
    /// Returns a human-readable description of the configuration source.
    /// </summary>
    private static string DescribeConfigurationPath(string? configurationPath)
    {
        return string.IsNullOrWhiteSpace(configurationPath)
            ? "(environment-only configuration)"
            : configurationPath;
    }
}
