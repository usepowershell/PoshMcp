using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using PoshMcp.PowerShell;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PoshMcp;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Set up command line parsing
        var rootCommand = new RootCommand("PowerShell MCP Server - Provides access to PowerShell commands via Model Context Protocol");

        var evaluateToolsOption = new Option<bool>(
            aliases: new[] { "--evaluate-tools", "-e" },
            description: "Evaluate and list discovered PowerShell tools without starting the MCP server");

        var verboseOption = new Option<bool>(
            aliases: new[] { "--verbose", "-v" },
            description: "Enable verbose logging");

        var debugOption = new Option<bool>(
            aliases: new[] { "--debug", "-d" },
            description: "Enable debug logging");

        var traceOption = new Option<bool>(
            aliases: new[] { "--trace", "-t" },
            description: "Enable trace logging");

        rootCommand.AddOption(evaluateToolsOption);
        rootCommand.AddOption(verboseOption);
        rootCommand.AddOption(debugOption);
        rootCommand.AddOption(traceOption);

        rootCommand.SetHandler(async (evaluateTools, verbose, debug, trace) =>
        {
            // Determine log level based on options
            LogLevel logLevel = LogLevel.Information;
            if (trace) logLevel = LogLevel.Trace;
            else if (debug) logLevel = LogLevel.Debug;
            else if (verbose) logLevel = LogLevel.Debug; // Verbose maps to Debug level

            if (evaluateTools)
            {
                await RunToolEvaluationAsync(logLevel);
            }
            else
            {
                await RunMcpServerAsync(args, logLevel);
            }
        }, evaluateToolsOption, verboseOption, debugOption, traceOption);

        return await rootCommand.InvokeAsync(args);
    }

    private static async Task RunToolEvaluationAsync(LogLevel logLevel)
    {
        PrintToolEvaluationHeader();
        using var loggerFactory = CreateLoggerFactory(logLevel);
        var logger = loggerFactory.CreateLogger("ToolEvaluation");

        try
        {
            LogEvaluationStart(logger, logLevel);
            var finalConfigPath = await DetermineConfigurationPath(logger);
            var config = LoadPowerShellConfiguration(finalConfigPath, logger);
            var tools = DiscoverTools(config, logger);
            ReportToolDiscoveryResults(tools, logger);
        }
        catch (Exception ex)
        {
            HandleToolEvaluationError(ex, logger);
        }
    }

    private static void PrintToolEvaluationHeader()
    {
        Console.Error.WriteLine("=== PowerShell MCP Server - Tool Evaluation Mode ===");
        Console.Error.WriteLine();
    }

    internal static ILoggerFactory CreateLoggerFactory(LogLevel logLevel)
    {
        return LoggerFactory.Create(builder =>
            builder.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace)
                   .SetMinimumLevel(logLevel));
    }

    private static void LogEvaluationStart(ILogger logger, LogLevel logLevel)
    {
        logger.LogInformation("Starting tool evaluation mode");
        logger.LogDebug($"Log level set to: {logLevel}");
    }

    private static async Task<string> DetermineConfigurationPath(ILogger logger)
    {
        var appDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Directory.GetCurrentDirectory();
        var configPath = Path.Combine(appDirectory, "appsettings.json");

        string finalConfigPath = await ResolveConfigurationPath(configPath);
        logger.LogInformation($"Loading configuration from: {finalConfigPath}");
        return finalConfigPath;
    }

    internal static async Task<string> ResolveConfigurationPath(string configPath)
    {
        if (File.Exists(configPath))
            return configPath;

        if (File.Exists("appsettings.json"))
            return "appsettings.json";

        await CreateDefaultConfigFileAsync(configPath);
        return configPath;
    }

    internal static PowerShellConfiguration LoadPowerShellConfiguration(string configPath, ILogger logger)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(configPath, optional: false, reloadOnChange: false)
            .Build();

        var config = new PowerShellConfiguration();
        configuration.GetSection("PowerShellConfiguration").Bind(config);
        LogConfigurationDetails(config, logger);
        return config;
    }

    private static void LogConfigurationDetails(PowerShellConfiguration config, ILogger logger)
    {
        logger.LogDebug("Configuration loaded successfully");
        logger.LogTrace($"Function names: {string.Join(", ", config.FunctionNames)}");
        logger.LogTrace($"Modules: {string.Join(", ", config.Modules)}");
        logger.LogTrace($"Include patterns: {string.Join(", ", config.IncludePatterns)}");
        logger.LogTrace($"Exclude patterns: {string.Join(", ", config.ExcludePatterns)}");
    }

    private static List<McpServerTool> DiscoverTools(PowerShellConfiguration config, ILogger logger)
    {
        logger.LogInformation("Discovering PowerShell tools...");
        var toolFactory = new McpToolFactoryV2();
        return toolFactory.GetToolsList(config, logger);
    }

    private static void ReportToolDiscoveryResults(List<McpServerTool> tools, ILogger logger)
    {
        PrintToolDiscoveryResults(tools);

        if (tools.Count > 0)
            PrintSuccessMessage();
        else
            PrintNoToolsFoundMessage();

        logger.LogInformation("Tool evaluation completed successfully");
    }

    private static void PrintToolDiscoveryResults(List<McpServerTool> tools)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine($"=== Tool Discovery Results ===");
        Console.Error.WriteLine($"Total tools discovered: {tools.Count}");
        Console.Error.WriteLine();
    }

    private static void PrintSuccessMessage()
    {
        Console.Error.WriteLine("Successfully created MCP tools from discovered PowerShell commands.");
        Console.Error.WriteLine("Tools are ready to be exposed via the MCP server.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("To start the MCP server with these tools, run without the --evaluate-tools flag.");
    }

    private static void PrintNoToolsFoundMessage()
    {
        Console.Error.WriteLine("No tools were discovered. Check your configuration and PowerShell environment.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Ensure that:");
        Console.Error.WriteLine("- PowerShell commands specified in FunctionNames exist");
        Console.Error.WriteLine("- Modules specified in Modules are available");
        Console.Error.WriteLine("- Include/exclude patterns are not filtering out all commands");
    }

    private static void HandleToolEvaluationError(Exception ex, ILogger logger)
    {
        logger.LogError(ex, "Error during tool evaluation: {ErrorMessage}", ex.Message);
        Console.Error.WriteLine($"Error: {ex.Message}");
    }

    private static async Task RunMcpServerAsync(string[] args, LogLevel? overrideLogLevel = null)
    {
        var builder = Host.CreateApplicationBuilder(args);
        ConfigureServerLogging(builder, overrideLogLevel);
        var finalConfigPath = await ConfigureServerConfiguration(builder);
        var serviceProvider = builder.Services.BuildServiceProvider();
        var (logger, config) = ExtractLoggerAndConfiguration(serviceProvider, finalConfigPath);
        var tools = SetupMcpTools(serviceProvider, config, logger, finalConfigPath);
        ConfigureServerServices(builder, tools);
        await builder.Build().RunAsync();
    }

    private static void ConfigureServerLogging(HostApplicationBuilder builder, LogLevel? overrideLogLevel)
    {
        builder.Logging.AddConsole(consoleLogOptions =>
        {
            consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        if (overrideLogLevel.HasValue)
        {
            builder.Logging.SetMinimumLevel(overrideLogLevel.Value);
        }
    }

    private static async Task<string> ConfigureServerConfiguration(HostApplicationBuilder builder)
    {
        var finalConfigPath = await DetermineServerConfigurationPath();
        builder.Configuration.AddJsonFile(finalConfigPath, optional: false, reloadOnChange: true);
        builder.Services.Configure<PowerShellConfiguration>(
            builder.Configuration.GetSection("PowerShellConfiguration"));
        return finalConfigPath;
    }

    private static async Task<string> DetermineServerConfigurationPath()
    {
        var appDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Directory.GetCurrentDirectory();
        var configPath = Path.Combine(appDirectory, "appsettings.json");
        return await ResolveConfigurationPath(configPath);
    }

    private static (ILogger logger, PowerShellConfiguration config) ExtractLoggerAndConfiguration(IServiceProvider serviceProvider, string finalConfigPath)
    {
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PoshMcpLogger");
        var config = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PowerShellConfiguration>>().Value;
        logger.LogInformation($"Using configuration file: {finalConfigPath}");
        return (logger, config);
    }

    private static List<McpServerTool> SetupMcpTools(IServiceProvider serviceProvider, PowerShellConfiguration config, ILogger logger, string finalConfigPath)
    {
        var toolFactory = new McpToolFactoryV2();
        var tools = toolFactory.GetToolsList(config, logger);
        var reloadTools = CreateConfigurationReloadTools(serviceProvider, toolFactory, config, finalConfigPath);
        AddConfigurationReloadToolsToList(tools, reloadTools);
        logger.LogInformation($"Added {tools.Count} total tools (including 3 configuration reload tools)");
        return tools;
    }

    private static ConfigurationReloadTools CreateConfigurationReloadTools(IServiceProvider serviceProvider, McpToolFactoryV2 toolFactory, PowerShellConfiguration config, string finalConfigPath)
    {
        var reloadServiceLogger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<PowerShellConfigurationReloadService>();
        var reloadService = new PowerShellConfigurationReloadService(reloadServiceLogger, toolFactory, config, finalConfigPath);
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

    private static void ConfigureServerServices(HostApplicationBuilder builder, List<McpServerTool> tools)
    {
        ConfigureJsonSerializerOptions(builder);
        RegisterMcpServerServices(builder, tools);
        RegisterCleanupServices(builder);
    }

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

    private static void RegisterMcpServerServices(HostApplicationBuilder builder, List<McpServerTool> tools)
    {
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools(tools);
    }

    private static void RegisterCleanupServices(HostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IHostedService, PowerShellCleanupService>();
    }

    private static async Task CreateDefaultConfigFileAsync(string configPath)
    {
        var defaultConfig = @"{
  ""Logging"": {
    ""LogLevel"": {
      ""Default"": ""Information"",
      ""Microsoft.Hosting.Lifetime"": ""Information""
    }
  },
  ""PowerShellConfiguration"": {
    ""FunctionNames"": [
      ""Get-Process"",
      ""Get-Service"",
      ""Get-ChildItem"",
      ""Get-Content"",
      ""Get-Location"",
      ""Get-Date""
    ],
    ""Modules"": [],
    ""ExcludePatterns"": [],
    ""IncludePatterns"": []
  }
}";

        // Ensure the directory exists
        var directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(configPath, defaultConfig);
    }
}
