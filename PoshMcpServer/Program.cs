using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using PoshMcp.PowerShell;
using System;
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
        Console.Error.WriteLine("=== PowerShell MCP Server - Tool Evaluation Mode ===");
        Console.Error.WriteLine();

        // Create a simple logger factory for evaluation mode
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace)
                   .SetMinimumLevel(logLevel));

        var logger = loggerFactory.CreateLogger("ToolEvaluation");

        try
        {
            logger.LogInformation("Starting tool evaluation mode");
            logger.LogDebug($"Log level set to: {logLevel}");

            // Load configuration similar to main server
            var appDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Directory.GetCurrentDirectory();
            var configPath = Path.Combine(appDirectory, "appsettings.json");

            string finalConfigPath;
            if (File.Exists(configPath))
            {
                finalConfigPath = configPath;
            }
            else if (File.Exists("appsettings.json"))
            {
                finalConfigPath = "appsettings.json";
            }
            else
            {
                finalConfigPath = configPath;
                await CreateDefaultConfigFileAsync(configPath);
            }

            logger.LogInformation($"Loading configuration from: {finalConfigPath}");

            // Build configuration
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(finalConfigPath, optional: false, reloadOnChange: false)
                .Build();

            var config = new PowerShellConfiguration();
            configuration.GetSection("PowerShellConfiguration").Bind(config);

            logger.LogDebug("Configuration loaded successfully");
            logger.LogTrace($"Function names: {string.Join(", ", config.FunctionNames)}");
            logger.LogTrace($"Modules: {string.Join(", ", config.Modules)}");
            logger.LogTrace($"Include patterns: {string.Join(", ", config.IncludePatterns)}");
            logger.LogTrace($"Exclude patterns: {string.Join(", ", config.ExcludePatterns)}");

            // Discover tools using the same logic as the main server
            logger.LogInformation("Discovering PowerShell tools...");
            var toolFactory = new McpToolFactoryV2();
            var tools = toolFactory.GetToolsList(config, logger);

            // Report results
            Console.Error.WriteLine();
            Console.Error.WriteLine($"=== Tool Discovery Results ===");
            Console.Error.WriteLine($"Total tools discovered: {tools.Count}");
            Console.Error.WriteLine();

            if (tools.Count > 0)
            {
                Console.Error.WriteLine("Successfully created MCP tools from discovered PowerShell commands.");
                Console.Error.WriteLine("Tools are ready to be exposed via the MCP server.");
                Console.Error.WriteLine();
                Console.Error.WriteLine("To start the MCP server with these tools, run without the --evaluate-tools flag.");
            }
            else
            {
                Console.Error.WriteLine("No tools were discovered. Check your configuration and PowerShell environment.");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Ensure that:");
                Console.Error.WriteLine("- PowerShell commands specified in FunctionNames exist");
                Console.Error.WriteLine("- Modules specified in Modules are available");
                Console.Error.WriteLine("- Include/exclude patterns are not filtering out all commands");
            }

            logger.LogInformation("Tool evaluation completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during tool evaluation: {ErrorMessage}", ex.Message);
            Console.Error.WriteLine($"Error: {ex.Message}");
        }
    }

    private static async Task RunMcpServerAsync(string[] args, LogLevel? overrideLogLevel = null)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Configure logging
        builder.Logging.AddConsole(consoleLogOptions =>
        {
            // Configure all logs to go to stderr
            consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        if (overrideLogLevel.HasValue)
        {
            builder.Logging.SetMinimumLevel(overrideLogLevel.Value);
        }

        // Configure settings - look for appsettings.json in the application directory
        var appDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Directory.GetCurrentDirectory();
        var configPath = Path.Combine(appDirectory, "appsettings.json");

        // Try multiple locations for the config file
        string finalConfigPath;
        if (File.Exists(configPath))
        {
            finalConfigPath = configPath;
        }
        else if (File.Exists("appsettings.json"))
        {
            finalConfigPath = "appsettings.json";
        }
        else
        {
            // Create a default config file in the app directory if none exists
            finalConfigPath = configPath;
            await CreateDefaultConfigFileAsync(configPath);
        }

        builder.Configuration.AddJsonFile(finalConfigPath, optional: false, reloadOnChange: true);
        builder.Services.Configure<PowerShellConfiguration>(
            builder.Configuration.GetSection("PowerShellConfiguration"));

        // Build service provider to get logger and configuration
        var serviceProvider = builder.Services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PoshMcpLogger");
        var config = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PowerShellConfiguration>>().Value;

        logger.LogInformation($"Using configuration file: {finalConfigPath}");

        // Use the new dynamic assembly-based tool factory with configuration
        var toolFactory = new McpToolFactoryV2();
        var tools = toolFactory.GetToolsList(config, logger);

        // Create configuration reload service
        var reloadServiceLogger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<PowerShellConfigurationReloadService>();
        var reloadService = new PowerShellConfigurationReloadService(reloadServiceLogger, toolFactory, config, finalConfigPath);

        // Create configuration reload tools
        var reloadToolsLogger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<ConfigurationReloadTools>();
        var reloadTools = new ConfigurationReloadTools(reloadService, reloadToolsLogger);

        // Create MCP tools for configuration reload functionality
        var reloadConfigFromFileDelegate = new Func<CancellationToken, Task<string>>(reloadTools.ReloadConfigurationFromFile);
        var updateConfigDelegate = new Func<string, CancellationToken, Task<string>>(reloadTools.UpdateConfiguration);
        var getConfigStatusDelegate = new Func<CancellationToken, Task<string>>(reloadTools.GetConfigurationStatus);

        var reloadFromFileTool = McpServerTool.Create(reloadConfigFromFileDelegate, new McpServerToolCreateOptions
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

        var updateConfigTool = McpServerTool.Create(updateConfigDelegate, new McpServerToolCreateOptions
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

        var getConfigStatusTool = McpServerTool.Create(getConfigStatusDelegate, new McpServerToolCreateOptions
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

        // Add reload tools to the main tools list
        tools.Add(reloadFromFileTool);
        tools.Add(updateConfigTool);
        tools.Add(getConfigStatusTool);

        logger.LogInformation($"Added {tools.Count} total tools (including 3 configuration reload tools)");

        // Configure JSON serializer options to handle cycles and deep object graphs
        builder.Services.Configure<JsonSerializerOptions>(options =>
        {
            options.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            options.MaxDepth = 128; // Increase from default 64 to handle deeper PowerShell object graphs
            options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.WriteIndented = false; // Compact output for MCP protocol
        });

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools(tools);

        // Register cleanup for the PowerShell runspace
        builder.Services.AddSingleton<IHostedService, PowerShellCleanupService>();

        await builder.Build().RunAsync();
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
