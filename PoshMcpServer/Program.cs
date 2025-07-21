using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using PoshMcp.PowerShell;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace PoshMcp;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Configure logging first
        builder.Logging.AddConsole(consoleLogOptions =>
        {
            // Configure all logs to go to stderr
            consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
        });

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
        var tools = McpToolFactoryV2.GetToolsList(config, logger);

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
