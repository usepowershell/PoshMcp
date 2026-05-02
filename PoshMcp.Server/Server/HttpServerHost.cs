using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using PoshMcp.Server.Authentication;
using PoshMcp.Server.Health;
using PoshMcp.Server.McpPrompts;
using PoshMcp.Server.McpResources;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.Metrics;
using PoshMcp.Server.Observability;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Azure.Monitor.OpenTelemetry.AspNetCore;

namespace PoshMcp;

/// <summary>
/// Handles all HTTP server startup and configuration logic.
/// Manages logging, CORS, health checks, OpenTelemetry, and MCP server initialization for HTTP transport.
/// </summary>
internal static class HttpServerHost
{
    /// <summary>
    /// Main entry point for HTTP MCP server startup.
    /// Configures logging, CORS, health checks, and runs the ASP.NET Core MCP server.
    /// </summary>
    internal static async Task RunHttpTransportServerAsync(
        string[] args,
        LogLevel logLevel,
        string finalConfigPath,
        string configurationPathSource,
        string? runtimeModeOverride,
        string? url,
        string? mcpPath)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Logging.AddConsole(consoleLogOptions =>
        {
            consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
        });
        builder.Logging.SetMinimumLevel(logLevel);

        if (!string.IsNullOrWhiteSpace(url))
        {
            builder.WebHost.UseUrls(url);
        }

        if (!string.IsNullOrWhiteSpace(finalConfigPath) && File.Exists(finalConfigPath))
        {
            builder.Configuration.AddJsonFile(finalConfigPath, optional: false, reloadOnChange: true);
        }
        builder.Configuration.AddEnvironmentVariables();
        builder.Services.Configure<PowerShellConfiguration>(
            builder.Configuration.GetSection("PowerShellConfiguration"));
        builder.Services.Configure<McpPromptsConfiguration>(
            builder.Configuration.GetSection("McpPrompts"));

        // Build auth config from the custom config file directly, bypassing the WebApplicationBuilder's
        // ConfigurationManager which starts with the baked-in appsettings.json (Authentication.Enabled: false).
        // Using the same approach as diagnostic tools (ConfigurationLoader.BuildRootConfiguration) ensures
        // the correct user-configured value is always used for auth decisions and IOptions binding.
        var authRootConfig = ConfigurationLoader.BuildRootConfiguration(finalConfigPath, reloadOnChange: false);

        builder.Services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<PoshMcp.Server.Authentication.AuthenticationConfiguration>,
            PoshMcp.Server.Authentication.AuthenticationConfigurationValidator>();

        ConfigureJsonSerializerOptions(builder);
        ConfigureCorsForMcp(builder, authRootConfig);
        RegisterHealthChecks(builder);

        ConfigureOpenTelemetryForHttp(builder);
        ConfigureApplicationInsights(builder.Services, builder.Configuration, isStdioMode: false);

        using var bootstrapLoggerFactory = LoggingHelpers.CreateLoggerFactory(logLevel);
        var logger = bootstrapLoggerFactory.CreateLogger("PoshMcpHttpLogger");
        var config = ConfigurationLoader.LoadPowerShellConfiguration(finalConfigPath, logger, runtimeModeOverride);
        await using var executorLease = await McpToolSetupService.StartOutOfProcessExecutorIfNeededAsync(config, bootstrapLoggerFactory, logger, finalConfigPath);

        var sharedHttpContextAccessor = new HttpContextAccessor();
        var sharedRunspaceLogger = bootstrapLoggerFactory.CreateLogger<SessionAwarePowerShellRunspace>();
        var sharedSessionRunspace = new SessionAwarePowerShellRunspace(sharedHttpContextAccessor, sharedRunspaceLogger);

        builder.Services.AddSingleton<IHttpContextAccessor>(sharedHttpContextAccessor);
        builder.Services.AddSingleton<IPowerShellRunspace>(sharedSessionRunspace);

        logger.LogInformation("Using configuration source: {ConfigurationPath}", ConfigurationHelpers.DescribeConfigurationPath(finalConfigPath));

        var tools = await McpToolSetupService.SetupHttpMcpToolsAsync(bootstrapLoggerFactory, config, logger, finalConfigPath, configurationPathSource, sharedSessionRunspace, executorLease?.Executor, sharedHttpContextAccessor);
        var resourcesConfig = ConfigurationLoader.LoadMcpResourcesConfiguration(finalConfigPath, logger);
        var resourcesConfigDirectory = Path.GetDirectoryName(finalConfigPath) ?? ".";
        var resourceLogger = bootstrapLoggerFactory.CreateLogger<McpResourceHandler>();
        var resourceHandler = new McpResourceHandler(resourcesConfig, sharedSessionRunspace, resourcesConfigDirectory, resourceLogger);
        var authConfigValue = authRootConfig.GetSection("Authentication").Get<PoshMcp.Server.Authentication.AuthenticationConfiguration>() ?? new();
        var promptsConfig = ConfigurationLoader.LoadPromptsConfiguration(finalConfigPath);
        var httpConfigDirectory = Path.GetDirectoryName(finalConfigPath) ?? Directory.GetCurrentDirectory();
        var httpPromptHandler = new McpPromptHandler(promptsConfig, httpConfigDirectory, bootstrapLoggerFactory.CreateLogger<McpPromptHandler>());
        var mcpBuilder = builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools(tools)
            .WithListResourcesHandler(resourceHandler.HandleListAsync)
            .WithReadResourceHandler(resourceHandler.HandleReadAsync)
            .WithListPromptsHandler(httpPromptHandler.HandleListPromptsAsync)
            .WithGetPromptHandler(httpPromptHandler.HandleGetPromptAsync);

        ToolAuthorizationFilter? callToolFilter = null;
        ToolListAuthorizationFilter? listToolFilter = null;

        if (authConfigValue.Enabled)
        {
            builder.Services.AddSingleton<ToolAuthorizationFilter>(sp =>
                new ToolAuthorizationFilter(
                    authConfigValue,
                    config,
                    sp.GetRequiredService<IHttpContextAccessor>(),
                    sp.GetRequiredService<McpMetrics>(),
                    sp.GetRequiredService<ILogger<ToolAuthorizationFilter>>()));
            builder.Services.AddSingleton<ToolListAuthorizationFilter>(sp =>
                new ToolListAuthorizationFilter(
                    authConfigValue,
                    config,
                    sp.GetRequiredService<IHttpContextAccessor>(),
                    sp.GetRequiredService<ILogger<ToolListAuthorizationFilter>>()));
            mcpBuilder.WithRequestFilters(fb =>
            {
                fb.AddCallToolFilter((next) => async (context, ct) =>
                    await callToolFilter!.AsFilter()(next)(context, ct));
                fb.AddListToolsFilter((next) => async (context, ct) =>
                    await listToolFilter!.AsFilter()(next)(context, ct));
            });
        }

        RegisterCleanupServices(builder);

        builder.Services.AddPoshMcpAuthentication(authRootConfig);

        var app = builder.Build();

        app.Use(async (context, next) =>
        {
            var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
                ?? OperationContext.GenerateCorrelationId();
            OperationContext.CorrelationId = correlationId;
            context.Response.Headers["X-Correlation-ID"] = correlationId;

            await next();
        });

        app.UseCors();

        var authConfigForMiddleware = app.Services.GetRequiredService<IOptions<AuthenticationConfiguration>>();
        if (authConfigForMiddleware.Value.Enabled)
        {
            callToolFilter = app.Services.GetRequiredService<ToolAuthorizationFilter>();
            listToolFilter = app.Services.GetRequiredService<ToolListAuthorizationFilter>();
            app.UseAuthentication();
            app.UseAuthorization();
        }

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = WriteHealthCheckResponseAsync
        }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = _ => true,
            ResponseWriter = WriteHealthCheckResponseAsync,
            ResultStatusCodes =
            {
                [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy] = StatusCodes.Status200OK,
                [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
                [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
            }
        }).AllowAnonymous();

        var normalizedMcpPath = SettingsResolver.NormalizeMcpPath(mcpPath);
        IEndpointConventionBuilder mcpEndpoint;
        if (string.IsNullOrWhiteSpace(normalizedMcpPath))
        {
            mcpEndpoint = app.MapMcp();
        }
        else
        {
            mcpEndpoint = app.MapMcp(normalizedMcpPath);
        }
        if (authConfigForMiddleware.Value.Enabled)
        {
            mcpEndpoint.RequireAuthorization("McpAccess");
        }

        // RFC 9728 Protected Resource Metadata
        var authConfigForEndpoints = app.Services
            .GetRequiredService<IOptions<AuthenticationConfiguration>>();
        app.MapProtectedResourceMetadata(authConfigForEndpoints.Value);
        // OAuth proxy: /.well-known/oauth-authorization-server + /register (DCR)
        app.MapOAuthProxyEndpoints(authConfigForEndpoints.Value);

        await app.RunAsync();
    }

    /// <summary>
    /// Configures CORS (Cross-Origin Resource Sharing) policy for MCP endpoint.
    /// Respects authentication configuration to determine appropriate origin policies.
    /// </summary>
    private static void ConfigureCorsForMcp(WebApplicationBuilder builder, IConfigurationRoot authRootConfig)
    {
        var authConfig = authRootConfig.GetSection("Authentication").Get<AuthenticationConfiguration>()
            ?? new AuthenticationConfiguration();

        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                if (authConfig.Enabled && authConfig.Cors?.AllowedOrigins.Count > 0)
                {
                    policy.WithOrigins(authConfig.Cors.AllowedOrigins.ToArray());
                    if (authConfig.Cors.AllowCredentials)
                        policy.AllowCredentials();
                    else
                        policy.DisallowCredentials();
                }
                else if (authConfig.Enabled)
                {
                    // Auth enabled but no origins configured — same-origin only (no wildcard)
                    // ASP.NET Core doesn't support "same-origin only" via CORS policy directly,
                    // so we just don't add AllowAnyOrigin — this effectively blocks cross-origin
                }
                else
                {
                    // Auth disabled — keep wide-open for dev/stdio use
                    policy.AllowAnyOrigin();
                }
                policy.AllowAnyMethod().AllowAnyHeader().WithExposedHeaders("Mcp-Session-Id");
            });
        });
    }

    /// <summary>
    /// Registers health checks for PowerShell runspace, assembly generation, and configuration.
    /// </summary>
    private static void RegisterHealthChecks(WebApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            .AddCheck<PowerShellRunspaceHealthCheck>("powershell_runspace")
            .AddCheck<AssemblyGenerationHealthCheck>("assembly_generation")
            .AddCheck<ConfigurationHealthCheck>("configuration");
    }

    /// <summary>
    /// Configures OpenTelemetry metrics and tracing for HTTP server.
    /// Includes ASP.NET Core instrumentation and console exporter.
    /// </summary>
    private static void ConfigureOpenTelemetryForHttp(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<McpMetrics>();

        builder.Services.AddOpenTelemetry()
            .WithTracing(tracingBuilder =>
            {
                tracingBuilder.AddSource(PowerShellAssemblyGenerator.ToolActivitySource.Name);
            })
            .WithMetrics(metricsBuilder =>
            {
                metricsBuilder
                    .AddMeter(McpMetrics.MeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddConsoleExporter();
            });

        builder.Services.AddSingleton<IHostedService>(serviceProvider =>
        {
            var metrics = serviceProvider.GetRequiredService<McpMetrics>();
            McpToolFactoryV2.SetMetrics(metrics);
            PowerShellAssemblyGenerator.SetMetrics(metrics);
            return new MetricsConfigurationService();
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
    /// Writes health check response as JSON.
    /// Includes status summary, individual check details, and total duration.
    /// </summary>
    private static async Task WriteHealthCheckResponseAsync(HttpContext context, Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var result = JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds,
                data = e.Value.Data
            }),
            totalDuration = report.TotalDuration.TotalMilliseconds
        });
        await context.Response.WriteAsync(result);
    }

    /// <summary>
    /// Configures JSON serialization options for the HTTP server.
    /// Sets up reference handling, depth limits, and null value handling.
    /// </summary>
    private static void ConfigureJsonSerializerOptions(WebApplicationBuilder builder)
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
    /// Registers cleanup services for HTTP transport.
    /// Ensures PowerShell runspaces are properly cleaned up on server shutdown.
    /// </summary>
    private static void RegisterCleanupServices(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IHostedService, PowerShellCleanupService>();
    }

}
