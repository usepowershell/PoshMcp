using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using ModelContextProtocol.Server;
using PoshMcp.Server.Health;
using PoshMcp.Server.Observability;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.OutOfProcess;
using PoshMcp.Server.Metrics;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using PoshMcp.Server.Authentication;
using PoshMcp.Server.McpPrompts;
using PoshMcp.Server.McpResources;
using ModelContextProtocol.Protocol;
namespace PoshMcp;

public class Program
{
    private const int ExitCodeSuccess = ExitCodes.Success;
    private const int ExitCodeConfigError = ExitCodes.ConfigError;
    private const int ExitCodeStartupError = ExitCodes.StartupError;
    private const int ExitCodeRuntimeError = ExitCodes.RuntimeError;

    public static async Task<int> Main(string[] args)
    {
        // Build CLI structure
        var rootCommand = CliDefinition.Build();

        // Handler for the main command (default MCP server behavior)
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
                await StdioServerHost.RunMcpServerAsync(args, logLevel, null);
            }
        }, CliDefinition.EvaluateToolsOption, CliDefinition.VerboseOption, CliDefinition.DebugOption, CliDefinition.TraceOption);

        CliDefinition.ServeCommand!.SetHandler(async (InvocationContext context) =>
        {
            var configPath = context.ParseResult.GetValueForOption(CliDefinition.ConfigOption);
            var logLevelText = context.ParseResult.GetValueForOption(CliDefinition.LogLevelOption);
            var transport = context.ParseResult.GetValueForOption(CliDefinition.TransportOption);
            var sessionMode = context.ParseResult.GetValueForOption(CliDefinition.SessionModeOption);
            var runtimeMode = context.ParseResult.GetValueForOption(CliDefinition.RuntimeModeOption);
            var url = context.ParseResult.GetValueForOption(CliDefinition.UrlOption);
            var mcpPath = context.ParseResult.GetValueForOption(CliDefinition.McpPathOption);
            var logFile = context.ParseResult.GetValueForOption(CliDefinition.LogFileOption);

            var resolvedSettings = await SettingsResolver.ResolveCommandSettingsAsync(args, configPath, logLevelText, transport, sessionMode, runtimeMode, mcpPath);
            var parsedLogLevel = SettingsResolver.ParseLogLevel(resolvedSettings.LogLevel.Value);
            var transportMode = SettingsResolver.ResolveTransportMode(resolvedSettings.Transport.Value);

            var fileConfigBuilder = new ConfigurationBuilder();
            if (!string.IsNullOrWhiteSpace(resolvedSettings.FinalConfigPath) && File.Exists(resolvedSettings.FinalConfigPath))
            {
                fileConfigBuilder.AddJsonFile(resolvedSettings.FinalConfigPath, optional: true, reloadOnChange: false);
            }
            fileConfigBuilder.AddEnvironmentVariables();
            IConfiguration fileConfig = fileConfigBuilder.Build();
            var resolvedLogFile = SettingsResolver.ResolveLogFilePath(logFile, fileConfig);

            try
            {
                if (SettingsResolver.ShouldPrintResolvedSettings(parsedLogLevel))
                {
                    SettingsResolver.PrintResolvedSettings("serve", resolvedSettings);
                }

                if (transportMode == TransportMode.Stdio)
                {
                    await StdioServerHost.RunMcpServerAsync(args, parsedLogLevel, resolvedSettings.FinalConfigPath, resolvedSettings.ConfigPath.Source, resolvedSettings.RuntimeMode.Value, resolvedLogFile.Value);
                    Environment.ExitCode = ExitCodeSuccess;
                    return;
                }

                if (transportMode == TransportMode.Http)
                {
                    await HttpServerHost.RunHttpTransportServerAsync(args, parsedLogLevel, resolvedSettings.FinalConfigPath, resolvedSettings.ConfigPath.Source, resolvedSettings.RuntimeMode.Value, url, resolvedSettings.McpPath.Value);
                    Environment.ExitCode = ExitCodeSuccess;
                    return;
                }

                Console.Error.WriteLine($"Unsupported transport '{resolvedSettings.Transport.Value}' in this executable.");
                Console.Error.WriteLine("Supported transport modes in this executable: stdio, http.");
                Environment.ExitCode = ExitCodeStartupError;
            }
            catch (FileNotFoundException ex)
            {
                Console.Error.WriteLine($"Configuration error: {ex.Message}");
                Environment.ExitCode = ExitCodeConfigError;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Startup error: {ex.Message}");
                Environment.ExitCode = ExitCodeStartupError;
            }
        });

        CliDefinition.ListToolsCommand!.SetHandler(async (configPath, logLevelText, runtimeMode, format) =>
        {
            var resolvedSettings = await SettingsResolver.ResolveCommandSettingsAsync(args, configPath, logLevelText, null, null, runtimeMode, null);
            var parsedLogLevel = SettingsResolver.ParseLogLevel(resolvedSettings.LogLevel.Value);
            var outputFormat = ConfigurationFileManager.NormalizeFormat(format);
            try
            {
                if (SettingsResolver.ShouldPrintResolvedSettings(parsedLogLevel))
                {
                    SettingsResolver.PrintResolvedSettings("list-tools", resolvedSettings);
                }

                await RunListToolsAsync(parsedLogLevel, resolvedSettings.FinalConfigPath, resolvedSettings.RuntimeMode.Value, outputFormat);
                Environment.ExitCode = ExitCodeSuccess;
            }
            catch (FileNotFoundException ex)
            {
                Console.Error.WriteLine($"Configuration error: {ex.Message}");
                Environment.ExitCode = ExitCodeConfigError;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Runtime error: {ex.Message}");
                Environment.ExitCode = ExitCodeRuntimeError;
            }
        }, CliDefinition.ConfigOption, CliDefinition.LogLevelOption, CliDefinition.RuntimeModeOption, CliDefinition.FormatOption);

        CliDefinition.ValidateConfigCommand!.SetHandler(async (configPath, logLevelText, runtimeMode, format) =>
        {
            var resolvedSettings = await SettingsResolver.ResolveCommandSettingsAsync(args, configPath, logLevelText, null, null, runtimeMode, null);
            var parsedLogLevel = SettingsResolver.ParseLogLevel(resolvedSettings.LogLevel.Value);
            var outputFormat = ConfigurationFileManager.NormalizeFormat(format);
            try
            {
                if (SettingsResolver.ShouldPrintResolvedSettings(parsedLogLevel))
                {
                    SettingsResolver.PrintResolvedSettings("validate-config", resolvedSettings);
                }

                await RunValidateConfigAsync(parsedLogLevel, resolvedSettings.FinalConfigPath, resolvedSettings.RuntimeMode.Value, outputFormat);
                Environment.ExitCode = ExitCodeSuccess;
            }
            catch (FileNotFoundException ex)
            {
                Console.Error.WriteLine($"Configuration error: {ex.Message}");
                Environment.ExitCode = ExitCodeConfigError;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Configuration validation failed: {ex.Message}");
                Environment.ExitCode = ExitCodeConfigError;
            }
        }, CliDefinition.ConfigOption, CliDefinition.LogLevelOption, CliDefinition.RuntimeModeOption, CliDefinition.FormatOption);

        CliDefinition.DoctorCommand!.SetHandler(async (configPath, logLevelText, transport, sessionMode, runtimeMode, mcpPath, format) =>
        {
            var resolvedSettings = await SettingsResolver.ResolveCommandSettingsAsync(args, configPath, logLevelText, transport, sessionMode, runtimeMode, mcpPath);
            var outputFormat = ConfigurationFileManager.NormalizeFormat(format);
            try
            {
                await DoctorService.RunDoctorAsync(resolvedSettings, outputFormat, McpToolSetupService.DiscoverToolsForCliAsync);
                Environment.ExitCode = ExitCodeSuccess;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Doctor failed: {ex.Message}");
                Environment.ExitCode = ExitCodeRuntimeError;
            }
        }, CliDefinition.ConfigOption, CliDefinition.LogLevelOption, CliDefinition.TransportOption, CliDefinition.SessionModeOption, CliDefinition.RuntimeModeOption, CliDefinition.McpPathOption, CliDefinition.FormatOption);

        CliDefinition.CreateConfigCommand!.SetHandler(async (force, format) =>
        {
            var outputFormat = ConfigurationFileManager.NormalizeFormat(format);
            var targetPath = Path.GetFullPath("appsettings.json");

            try
            {
                var created = await ConfigurationFileManager.CreateDefaultConfigInCurrentDirectoryAsync(targetPath, force);

                if (outputFormat == "json")
                {
                    var payload = new
                    {
                        success = true,
                        configurationPath = targetPath,
                        overwritten = created.WasOverwritten
                    };
                    Console.WriteLine(JsonSerializer.Serialize(payload));
                }
                else
                {
                    Console.WriteLine(created.WasOverwritten
                        ? $"Overwrote default configuration: {targetPath}"
                        : $"Created default configuration: {targetPath}");
                }

                Environment.ExitCode = ExitCodeSuccess;
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"Configuration error: {ex.Message}");
                Environment.ExitCode = ExitCodeConfigError;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Runtime error: {ex.Message}");
                Environment.ExitCode = ExitCodeRuntimeError;
            }
        }, CliDefinition.ForceOption, CliDefinition.FormatOption);

        CliDefinition.UpdateConfigCommand!.SetHandler(async (InvocationContext context) =>
        {
            var configPath = context.ParseResult.GetValueForOption(CliDefinition.ConfigOption);
            var logLevelText = context.ParseResult.GetValueForOption(CliDefinition.LogLevelOption);
            var format = context.ParseResult.GetValueForOption(CliDefinition.FormatOption);
            var addCommands = context.ParseResult.GetValueForOption(CliDefinition.AddCommandOption);
            var removeCommands = context.ParseResult.GetValueForOption(CliDefinition.RemoveCommandOption);
            var addModules = context.ParseResult.GetValueForOption(CliDefinition.AddModuleOption);
            var removeModules = context.ParseResult.GetValueForOption(CliDefinition.RemoveModuleOption);
            var addIncludePatterns = context.ParseResult.GetValueForOption(CliDefinition.AddIncludePatternOption);
            var removeIncludePatterns = context.ParseResult.GetValueForOption(CliDefinition.RemoveIncludePatternOption);
            var addExcludePatterns = context.ParseResult.GetValueForOption(CliDefinition.AddExcludePatternOption);
            var removeExcludePatterns = context.ParseResult.GetValueForOption(CliDefinition.RemoveExcludePatternOption);
            var enableDynamicReloadTools = context.ParseResult.GetValueForOption(CliDefinition.EnableDynamicReloadToolsOption);
            var enableConfigurationTroubleshootingTool = context.ParseResult.GetValueForOption(CliDefinition.EnableConfigurationTroubleshootingToolOption);
            var enableResultCaching = context.ParseResult.GetValueForOption(CliDefinition.EnableResultCachingOption);
            var useDefaultDisplayProperties = context.ParseResult.GetValueForOption(CliDefinition.UseDefaultDisplayPropertiesOption);
            var setAuthEnabled = context.ParseResult.GetValueForOption(CliDefinition.SetAuthEnabledOption);
            var runtimeMode = context.ParseResult.GetValueForOption(CliDefinition.RuntimeModeOption);
            var nonInteractive = context.ParseResult.GetValueForOption(CliDefinition.NonInteractiveOption);

            var resolvedSettings = await SettingsResolver.ResolveCommandSettingsAsync(args, configPath, logLevelText, null, null, null, null);
            var parsedLogLevel = SettingsResolver.ParseLogLevel(resolvedSettings.LogLevel.Value);
            var outputFormat = ConfigurationFileManager.NormalizeFormat(format);

            try
            {
                if (SettingsResolver.ShouldPrintResolvedSettings(parsedLogLevel))
                {
                    SettingsResolver.PrintResolvedSettings("update-config", resolvedSettings);
                }

                var updateRequest = new ConfigUpdateRequest(
                    addCommands ?? Array.Empty<string>(),
                    removeCommands ?? Array.Empty<string>(),
                    addCommands ?? Array.Empty<string>(),
                    removeCommands ?? Array.Empty<string>(),
                    addModules ?? Array.Empty<string>(),
                    removeModules ?? Array.Empty<string>(),
                    addIncludePatterns ?? Array.Empty<string>(),
                    removeIncludePatterns ?? Array.Empty<string>(),
                    addExcludePatterns ?? Array.Empty<string>(),
                    removeExcludePatterns ?? Array.Empty<string>(),
                    ConfigurationFileManager.TryParseRequiredBoolean(enableDynamicReloadTools),
                    ConfigurationFileManager.TryParseRequiredBoolean(enableConfigurationTroubleshootingTool),
                    ConfigurationFileManager.TryParseRequiredBoolean(enableResultCaching),
                    ConfigurationFileManager.TryParseRequiredBoolean(useDefaultDisplayProperties),
                    ConfigurationFileManager.TryParseRequiredBoolean(setAuthEnabled),
                    ConfigurationFileManager.NormalizeRuntimeMode(runtimeMode),
                    nonInteractive);

                var result = await ConfigurationFileManager.UpdateConfigurationFileAsync(resolvedSettings.FinalConfigPath, updateRequest);

                if (outputFormat == "json")
                {
                    var payload = new
                    {
                        success = true,
                        configurationPath = result.ConfigurationPath,
                        changed = result.Changed,
                        addedFunctions = result.AddedFunctions,
                        removedFunctions = result.RemovedFunctions,
                        addedCommands = result.AddedCommands,
                        removedCommands = result.RemovedCommands,
                        advancedPromptedCommandCount = result.AdvancedPromptedFunctionCount,
                        advancedPromptedFunctionCount = result.AdvancedPromptedFunctionCount,
                        settingsChanged = result.SettingsChanged
                    };
                    Console.WriteLine(JsonSerializer.Serialize(payload));
                }
                else
                {
                    Console.WriteLine(result.Changed
                        ? $"Updated configuration: {result.ConfigurationPath}"
                        : $"No changes applied to configuration: {result.ConfigurationPath}");
                    Console.WriteLine($"Added commands: {result.AddedCommands} | Removed commands: {result.RemovedCommands}");
                    if (result.SettingsChanged > 0)
                    {
                        Console.WriteLine($"Settings changed: {result.SettingsChanged}");
                    }
                    if (result.AdvancedPromptedFunctionCount > 0)
                    {
                        Console.WriteLine($"Advanced prompts completed for {result.AdvancedPromptedFunctionCount} command(s).");
                    }
                }

                Environment.ExitCode = ExitCodeSuccess;
            }
            catch (FileNotFoundException ex)
            {
                Console.Error.WriteLine($"Configuration error: {ex.Message}");
                Environment.ExitCode = ExitCodeConfigError;
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"Configuration error: {ex.Message}");
                Environment.ExitCode = ExitCodeConfigError;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Runtime error: {ex.Message}");
                Environment.ExitCode = ExitCodeRuntimeError;
            }
        });

        // Handler for the psmodulepath command
        CliDefinition.PsModulePathCommand!.SetHandler((verbose, debug, trace) =>
        {
            // Determine log level based on options
            LogLevel logLevel = LogLevel.Information;
            if (trace) logLevel = LogLevel.Trace;
            else if (debug) logLevel = LogLevel.Debug;
            else if (verbose) logLevel = LogLevel.Debug; // Verbose maps to Debug level

            RunPSModulePathCommand(logLevel);
        }, CliDefinition.VerboseOption, CliDefinition.DebugOption, CliDefinition.TraceOption);

        // Handler for the build command
        CliDefinition.BuildCommand!.SetHandler((InvocationContext context) =>
        {
            try
            {
                var modules = context.ParseResult.GetValueForOption(CliDefinition.BuildModulesOption);
                var type = context.ParseResult.GetValueForOption(CliDefinition.BuildTypeOption);
                var tag = context.ParseResult.GetValueForOption(CliDefinition.BuildTagOption);
                var dockerFile = context.ParseResult.GetValueForOption(CliDefinition.BuildDockerFileOption);
                var sourceImage = context.ParseResult.GetValueForOption(CliDefinition.BuildSourceImageOption);
                var sourceTag = context.ParseResult.GetValueForOption(CliDefinition.BuildSourceTagOption);
                var generateDockerfile = context.ParseResult.GetValueForOption(CliDefinition.BuildGenerateDockerfileOption);
                var dockerfileOutput = context.ParseResult.GetValueForOption(CliDefinition.BuildDockerfileOutputOption);
                var appSettings = context.ParseResult.GetValueForOption(CliDefinition.BuildAppSettingsOption);

                var buildType = string.IsNullOrWhiteSpace(type)
                    ? "custom"
                    : type.ToLowerInvariant();
                var imageTag = string.IsNullOrWhiteSpace(tag) ? "poshmcp:latest" : tag;

                if (buildType != "base" && buildType != "custom")
                {
                    Console.Error.WriteLine("Error: --type must be 'custom' or 'base'");
                    Environment.ExitCode = ExitCodeConfigError;
                    return;
                }

                var imageFile = string.IsNullOrWhiteSpace(dockerFile)
                    ? (buildType == "base" ? "Dockerfile" : "examples/Dockerfile.user")
                    : dockerFile;

                // When the imageFile maps to an embedded resource or we're generating a Dockerfile,
                // the file does not need to exist on disk. For custom Dockerfiles it must.
                var hasEmbeddedDockerfile = DockerRunner.GetEmbeddedDockerfileName(imageFile) != null;
                if (!generateDockerfile && !hasEmbeddedDockerfile && !File.Exists(imageFile))
                {
                    Console.Error.WriteLine($"Error: Dockerfile not found at {imageFile}");
                    Environment.ExitCode = ExitCodeConfigError;
                    return;
                }

                if (!string.IsNullOrWhiteSpace(appSettings) && !File.Exists(appSettings))
                {
                    Console.Error.WriteLine($"Error: appsettings file not found: {appSettings}");
                    Environment.ExitCode = ExitCodeConfigError;
                    return;
                }

                var resolvedSourceImage = DockerRunner.ResolveSourceImageReference(sourceImage, sourceTag);

                if (generateDockerfile)
                {
                    var outputPath = string.IsNullOrWhiteSpace(dockerfileOutput)
                        ? "./Dockerfile.generated"
                        : dockerfileOutput;
                    var writtenPath = DockerRunner.GenerateDockerfile(
                        imageFile,
                        outputPath,
                        imageTag,
                        modules,
                        buildType == "custom" ? resolvedSourceImage : null,
                        appSettings);
                    Console.WriteLine($"Dockerfile written to: {writtenPath}");
                    if (!string.IsNullOrWhiteSpace(appSettings))
                    {
                        Console.WriteLine($"Note: Copy {appSettings} to poshmcp-appsettings.json in your build context before building.");
                    }
                    Console.WriteLine("To build manually, run:");
                    Console.WriteLine($"  docker build -f {writtenPath} -t {imageTag} .");
                    Environment.ExitCode = ExitCodeSuccess;
                    return;
                }

                var dockerPath = DockerRunner.DetectDockerCommand();

                if (dockerPath == null)
                {
                    Console.Error.WriteLine("Error: Docker or Podman is not installed or not available in PATH.");
                    Console.Error.WriteLine("Please install Docker (https://www.docker.com) or Podman (https://podman.io)");
                    Environment.ExitCode = ExitCodeStartupError;
                    return;
                }

                Console.WriteLine($"Building {buildType} PoshMcp image: {imageTag}");

                if (buildType == "custom")
                {
                    Console.WriteLine($"Using source image: {resolvedSourceImage}");
                }

                string? stagedAppSettings = null;
                string? tempDockerfilePath = null;
                var effectiveImageFile = imageFile;

                if (!string.IsNullOrWhiteSpace(appSettings) || hasEmbeddedDockerfile)
                {
                    stagedAppSettings = !string.IsNullOrWhiteSpace(appSettings)
                        ? DockerRunner.PrepareAppSettingsForBuild(appSettings, ".")
                        : null;
                    tempDockerfilePath = ".poshmcp-build.dockerfile";
                    DockerRunner.GenerateDockerfile(
                        imageFile,
                        tempDockerfilePath,
                        imageTag,
                        modules,
                        buildType == "custom" ? resolvedSourceImage : null,
                        appSettings);
                    effectiveImageFile = tempDockerfilePath;
                }

                try
                {
                    var buildArgs = DockerRunner.BuildDockerBuildArgs(
                        effectiveImageFile,
                        imageTag,
                        modules,
                        buildType == "custom" ? resolvedSourceImage : null);

                    var result = DockerRunner.ExecuteDockerCommand(dockerPath, buildArgs);
                    if (result != ExitCodeSuccess)
                    {
                        Environment.ExitCode = result;
                        return;
                    }

                    Console.WriteLine($"Successfully built image: {imageTag}");
                    Environment.ExitCode = ExitCodeSuccess;
                }
                finally
                {
                    DockerRunner.CleanupTempFile(tempDockerfilePath);
                    DockerRunner.CleanupTempFile(stagedAppSettings);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Build error: {ex.Message}");
                Environment.ExitCode = ExitCodeRuntimeError;
            }
        });

        // Handler for the run command
        CliDefinition.RunCommand!.SetHandler((mode, port, tag, config, volumes, interactive) =>
        {
            try
            {
                var transportMode = string.IsNullOrWhiteSpace(mode) ? "http" : mode.ToLowerInvariant();
                var portNumber = port ?? 8080;
                var imageTag = string.IsNullOrWhiteSpace(tag) ? "poshmcp:latest" : tag;
                var dockerPath = DockerRunner.DetectDockerCommand();

                if (dockerPath == null)
                {
                    Console.Error.WriteLine("Error: Docker or Podman is not installed or not available in PATH.");
                    Console.Error.WriteLine("Please install Docker (https://www.docker.com) or Podman (https://podman.io)");
                    Environment.ExitCode = ExitCodeStartupError;
                    return;
                }

                if (transportMode != "http" && transportMode != "stdio")
                {
                    Console.Error.WriteLine("Error: --mode must be 'http' or 'stdio'");
                    Environment.ExitCode = ExitCodeConfigError;
                    return;
                }

                var runArgs = "run -d";

                if (interactive)
                {
                    runArgs = "run -it";
                }

                // Set transport mode environment variable
                var envVar = transportMode == "http" ? "http" : "stdio";
                runArgs += $" -e POSHMCP_TRANSPORT={envVar}";

                // Expose port for HTTP mode
                if (transportMode == "http")
                {
                    runArgs += $" -p {portNumber}:8080";
                }

                // Mount config file if provided
                if (!string.IsNullOrWhiteSpace(config))
                {
                    var configPath = Path.GetFullPath(config);
                    if (!File.Exists(configPath))
                    {
                        Console.Error.WriteLine($"Error: Config file not found: {configPath}");
                        Environment.ExitCode = ExitCodeConfigError;
                        return;
                    }

                    runArgs += $" -v {configPath}:/app/appsettings.json:ro";
                }

                // Add volume mounts if provided
                if (volumes != null && volumes.Length > 0)
                {
                    foreach (var volume in volumes)
                    {
                        runArgs += $" -v {volume}";
                    }
                }

                // Add image tag
                runArgs += $" {imageTag}";

                if (interactive)
                {
                    Console.WriteLine($"Starting PoshMcp container in interactive mode: {imageTag}");
                    var result = DockerRunner.ExecuteDockerCommand(dockerPath, runArgs, interactive: true);
                    Environment.ExitCode = result;
                }
                else
                {
                    Console.WriteLine($"Starting PoshMcp container in {transportMode} mode on port {portNumber}...");
                    var result = DockerRunner.ExecuteDockerCommand(dockerPath, runArgs);
                    if (result == ExitCodeSuccess)
                    {
                        Console.WriteLine($"Container started successfully ({transportMode} mode on port {portNumber})");
                    }
                    Environment.ExitCode = result;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Run error: {ex.Message}");
                Environment.ExitCode = ExitCodeRuntimeError;
            }
        }, CliDefinition.RunModeOption, CliDefinition.RunPortOption, CliDefinition.RunTagOption, CliDefinition.RunConfigOption, CliDefinition.RunVolumeOption, CliDefinition.RunInteractiveOption);

        CliDefinition.ScaffoldCommand!.SetHandler(async (projectPath, force, format) =>
        {
            var outputFormat = ConfigurationFileManager.NormalizeFormat(format);

            try
            {
                var targetProjectPath = string.IsNullOrWhiteSpace(projectPath)
                    ? Directory.GetCurrentDirectory()
                    : projectPath;

                var result = await InfrastructureScaffolder.ScaffoldAzureInfrastructureAsync(targetProjectPath, force);

                if (outputFormat == "json")
                {
                    var payload = new
                    {
                        success = true,
                        projectPath = result.ProjectPath,
                        relativePath = result.RelativeInfraPath,
                        filesWritten = result.FilesWritten,
                        filesOverwritten = result.FilesOverwritten,
                        force = result.Force
                    };
                    Console.WriteLine(JsonSerializer.Serialize(payload));
                }
                else
                {
                    Console.WriteLine($"Scaffolded {result.FilesWritten} infra file(s) to {Path.Combine(result.ProjectPath, result.RelativeInfraPath.Replace('/', Path.DirectorySeparatorChar))}");
                    if (result.FilesOverwritten > 0)
                    {
                        Console.WriteLine($"Overwritten files: {result.FilesOverwritten}");
                    }
                }

                Environment.ExitCode = ExitCodeSuccess;
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"Scaffold error: {ex.Message}");
                Environment.ExitCode = ExitCodeConfigError;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Scaffold error: {ex.Message}");
                Environment.ExitCode = ExitCodeRuntimeError;
            }
        }, CliDefinition.ScaffoldProjectPathOption, CliDefinition.ForceOption, CliDefinition.FormatOption);

        return await rootCommand.InvokeAsync(args);
    }


    private static async Task RunListToolsAsync(LogLevel logLevel, string finalConfigPath, string? runtimeModeOverride, string format)
    {
        using var loggerFactory = LoggingHelpers.CreateLoggerFactory(logLevel);
        var logger = loggerFactory.CreateLogger("ListTools");

        var config = ConfigurationLoader.LoadPowerShellConfiguration(finalConfigPath, logger, runtimeModeOverride);
        var tools = await McpToolSetupService.DiscoverToolsAsync(config, loggerFactory, logger, finalConfigPath);

        if (format == "json")
        {
            var payload = new
            {
                configurationPath = DescribeConfigurationPath(finalConfigPath),
                runtimeMode = config.RuntimeMode.ToString(),
                toolCount = tools.Count,
                commandNames = config.GetEffectiveCommandNames(),
                generatedAtUtc = DateTime.UtcNow
            };
            Console.WriteLine(JsonSerializer.Serialize(payload));
            return;
        }

        Console.WriteLine($"Configuration: {DescribeConfigurationPath(finalConfigPath)}");
        Console.WriteLine($"Runtime mode: {config.RuntimeMode}");
        Console.WriteLine($"Discovered tools: {tools.Count}");
        Console.WriteLine("Configured command names:");
        foreach (var commandName in config.GetEffectiveCommandNames())
        {
            Console.WriteLine($"- {commandName}");
        }
    }

    private static async Task RunValidateConfigAsync(LogLevel logLevel, string finalConfigPath, string? runtimeModeOverride, string format)
    {
        using var loggerFactory = LoggingHelpers.CreateLoggerFactory(logLevel);
        var logger = loggerFactory.CreateLogger("ValidateConfig");

        var config = ConfigurationLoader.LoadPowerShellConfiguration(finalConfigPath, logger, runtimeModeOverride);
        var tools = await McpToolSetupService.DiscoverToolsAsync(config, loggerFactory, logger, finalConfigPath);
        var (resourcesDiag, promptsDiag) = ConfigurationLoader.TryValidateResourcesAndPrompts(finalConfigPath);

        var hasErrors = resourcesDiag.Errors.Count > 0 || promptsDiag.Errors.Count > 0;

        if (format == "json")
        {
            var payload = new
            {
                valid = !hasErrors,
                configurationPath = DescribeConfigurationPath(finalConfigPath),
                runtimeMode = config.RuntimeMode.ToString(),
                commandCount = config.GetEffectiveCommandNames().Count,
                moduleCount = config.Modules.Count,
                toolCount = tools.Count,
                resources = new
                {
                    configured = resourcesDiag.Configured,
                    valid = resourcesDiag.Valid,
                    errors = resourcesDiag.Errors,
                    warnings = resourcesDiag.Warnings
                },
                prompts = new
                {
                    configured = promptsDiag.Configured,
                    valid = promptsDiag.Valid,
                    errors = promptsDiag.Errors,
                    warnings = promptsDiag.Warnings
                }
            };
            Console.WriteLine(JsonSerializer.Serialize(payload));
            return;
        }

        if (hasErrors)
        {
            Console.WriteLine("Configuration validation failed.");
        }
        else
        {
            Console.WriteLine("Configuration validation succeeded.");
        }
        Console.WriteLine($"Configuration: {DescribeConfigurationPath(finalConfigPath)}");
        Console.WriteLine($"Runtime mode: {config.RuntimeMode}");
        Console.WriteLine($"Commands: {config.GetEffectiveCommandNames().Count} | Modules: {config.Modules.Count} | Tools: {tools.Count}");
        Console.WriteLine($"Resources configured: {resourcesDiag.Configured} | valid: {resourcesDiag.Valid}");
        foreach (var error in resourcesDiag.Errors)
            Console.WriteLine($"  ✖ {error}");
        foreach (var warning in resourcesDiag.Warnings)
            Console.WriteLine($"  ⚠ {warning}");
        Console.WriteLine($"Prompts configured: {promptsDiag.Configured} | valid: {promptsDiag.Valid}");
        foreach (var error in promptsDiag.Errors)
            Console.WriteLine($"  ✖ {error}");
        foreach (var warning in promptsDiag.Warnings)
            Console.WriteLine($"  ⚠ {warning}");
    }

    internal static string BuildDoctorJson(DoctorReport report)
    {
        return JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    }

    internal static DoctorReport BuildDoctorReportFromConfig(
        string configurationPath,
        string configurationPathSource,
        string? effectiveLogLevel,
        string effectiveLogLevelSource,
        string effectiveTransport,
        string effectiveTransportSource,
        string? effectiveSessionMode,
        string effectiveSessionModeSource,
        string? effectiveRuntimeMode,
        string effectiveRuntimeModeSource,
        string? effectiveMcpPath,
        string effectiveMcpPathSource,
        PowerShellConfiguration config,
        List<McpServerTool> tools,
        AuthenticationConfiguration? authConfig = null,
        System.Security.Claims.ClaimsPrincipal? currentIdentity = null)
    {
        var discoveredToolNames = GetDiscoveredToolNames(tools);
        var configuredFunctionStatus = BuildConfiguredFunctionStatus(config.GetEffectiveCommandNames(), discoveredToolNames);
        var toolNames = discoveredToolNames.Count > 0
            ? discoveredToolNames
            : GetExpectedToolNames(configuredFunctionStatus, config.EnableDynamicReloadTools);
        var missingFunctions = configuredFunctionStatus.Where(f => !f.Found).Select(f => f.FunctionName).ToList();
        if (missingFunctions.Count > 0)
        {
            var resolutionReasons = DiagnoseMissingCommands(missingFunctions, config);
            configuredFunctionStatus = configuredFunctionStatus
                .Select(s => s.Found ? s : s with { ResolutionReason = resolutionReasons.GetValueOrDefault(s.FunctionName) })
                .ToList();
        }

        var diagnostics = CollectPowerShellDiagnostics();
        var oopModulePaths = ResolveConfiguredModulePathsForOop(config, configurationPath);
        var (warnings, configurationErrors) = BuildConfigurationWarnings(config, configurationPath);
        var (resourcesDiag, promptsDiag) = ConfigurationLoader.TryValidateResourcesAndPrompts(configurationPath);
        var environmentVariables = CollectEnvironmentVariables();

        if (authConfig is null)
        {
            var rootConfig = ConfigurationLoader.BuildRootConfiguration(configurationPath, reloadOnChange: false);
            authConfig = rootConfig.GetSection("Authentication").Get<AuthenticationConfiguration>();
        }

        return DoctorReport.Build(
            configurationPath: DescribeConfigurationPath(configurationPath),
            configurationPathSource: configurationPathSource,
            effectiveLogLevel: effectiveLogLevel,
            effectiveLogLevelSource: effectiveLogLevelSource,
            effectiveTransport: effectiveTransport,
            effectiveTransportSource: effectiveTransportSource,
            effectiveSessionMode: effectiveSessionMode,
            effectiveSessionModeSource: effectiveSessionModeSource,
            effectiveRuntimeMode: effectiveRuntimeMode,
            effectiveRuntimeModeSource: effectiveRuntimeModeSource,
            effectiveMcpPath: effectiveMcpPath,
            effectiveMcpPathSource: effectiveMcpPathSource,
            configuredFunctionStatus: configuredFunctionStatus,
            toolNames: toolNames,
            powerShellVersion: diagnostics.PowerShellVersion,
            modulePathEntries: diagnostics.ModulePathEntries,
            modulePaths: diagnostics.ModulePaths,
            oopModulePaths: oopModulePaths,
            resourcesDiagnostics: resourcesDiag,
            promptsDiagnostics: promptsDiag,
            warnings: warnings,
            configurationErrors: configurationErrors,
            environmentVariables: environmentVariables,
            authConfig: authConfig,
            currentIdentity: currentIdentity);
    }

    private static (List<string> Warnings, List<string> Errors) BuildConfigurationWarnings(PowerShellConfiguration config, string configPath)
    {
        var warnings = new List<string>();
        var errors = new List<string>();

        if (config.HasBothCommandAndFunctionNames)
        {
            warnings.Add("Both CommandNames and FunctionNames are configured. CommandNames takes precedence; FunctionNames entries are ignored.");
        }
        else if (config.HasLegacyFunctionNames)
        {
            warnings.Add("FunctionNames is deprecated. Migrate to CommandNames in your appsettings.json (rename the \"FunctionNames\" array to \"CommandNames\").");
        }

        // Validate ApplicationInsights configuration (FR-313, FR-314, FR-315 — no network calls)
        var configuration = ConfigurationLoader.BuildRootConfiguration(configPath, reloadOnChange: false);
        var appInsightsOptions = configuration.GetSection(PoshMcp.Server.ApplicationInsightsOptions.SectionName).Get<PoshMcp.Server.ApplicationInsightsOptions>()
                                 ?? new PoshMcp.Server.ApplicationInsightsOptions();

        if (appInsightsOptions.Enabled)
        {
            var connectionString = appInsightsOptions.ConnectionString;
            if (string.IsNullOrWhiteSpace(connectionString))
                connectionString = Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                errors.Add("ApplicationInsights is enabled but no connection string is configured. Set ApplicationInsights.ConnectionString in appsettings.json or the APPLICATIONINSIGHTS_CONNECTION_STRING environment variable.");
            }
            else if (!connectionString.StartsWith("InstrumentationKey=", StringComparison.OrdinalIgnoreCase)
                     && !connectionString.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("ApplicationInsights connection string format may be invalid. Expected format starting with 'InstrumentationKey=' or 'https://'.");
            }

            if (appInsightsOptions.SamplingPercentage < 1 || appInsightsOptions.SamplingPercentage > 100)
            {
                warnings.Add($"ApplicationInsights SamplingPercentage is {appInsightsOptions.SamplingPercentage}, which is outside the valid range of 1-100. It will be clamped at runtime.");
            }
        }

        return (warnings, errors);
    }

    private static Dictionary<string, string?> CollectEnvironmentVariables()
    {
        return new Dictionary<string, string?>
        {
            ["POSHMCP_TRANSPORT"] = Environment.GetEnvironmentVariable("POSHMCP_TRANSPORT"),
            ["POSHMCP_LOG_LEVEL"] = Environment.GetEnvironmentVariable("POSHMCP_LOG_LEVEL"),
            ["POSHMCP_LOG_FILE"] = Environment.GetEnvironmentVariable("POSHMCP_LOG_FILE"),
            ["POSHMCP_SESSION_MODE"] = Environment.GetEnvironmentVariable("POSHMCP_SESSION_MODE"),
            ["POSHMCP_RUNTIME_MODE"] = Environment.GetEnvironmentVariable("POSHMCP_RUNTIME_MODE"),
            ["POSHMCP_MCP_PATH"] = Environment.GetEnvironmentVariable("POSHMCP_MCP_PATH"),
            ["POSHMCP_CONFIGURATION"] = Environment.GetEnvironmentVariable("POSHMCP_CONFIGURATION"),
            ["POSHMCP_FUNCTION_NAMES"] = Environment.GetEnvironmentVariable("POSHMCP_FUNCTION_NAMES"),
            ["POSHMCP_COMMAND_NAMES"] = Environment.GetEnvironmentVariable("POSHMCP_COMMAND_NAMES"),
            ["ASPNETCORE_ENVIRONMENT"] = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            ["DOTNET_ENVIRONMENT"] = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
        };
    }

    internal static string SerializeEffectivePowerShellConfiguration(PowerShellConfiguration config, bool writeIndented = false)
    {
        return JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = writeIndented,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        });
    }

    private static (string PowerShellVersion, int ModulePathEntries, string[] ModulePaths) CollectPowerShellDiagnostics()
    {
        using var runspace = new IsolatedPowerShellRunspace();
        var result = runspace.ExecuteThreadSafe(ps =>
        {
            ps.Commands.Clear();
            ps.AddScript("$PSVersionTable.PSVersion.ToString();$env:PSModulePath");
            var results = ps.Invoke();
            if (ps.HadErrors)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error));
            }

            var version = results.Count > 0 ? results[0]?.ToString() ?? "unknown" : "unknown";
            var modulePath = results.Count > 1 ? results[1]?.ToString() ?? string.Empty : string.Empty;
            var modulePaths = modulePath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            return (version, modulePaths.Length, modulePaths);
        });

        return result;
    }

    private static string[] ResolveConfiguredModulePathsForOop(PowerShellConfiguration config, string? configurationPath)
    {
        var configuredModulePaths = config.Environment?.ModulePaths;
        if (configuredModulePaths is null || configuredModulePaths.Count == 0)
        {
            return Array.Empty<string>();
        }

        var baseDir = !string.IsNullOrWhiteSpace(configurationPath)
            ? Path.GetDirectoryName(Path.GetFullPath(configurationPath))
            : null;
        baseDir ??= Directory.GetCurrentDirectory();

        return configuredModulePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.IsPathRooted(path)
                ? path
                : Path.GetFullPath(Path.Combine(baseDir, path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static List<ConfiguredFunctionStatus> BuildConfiguredFunctionStatus(List<string> functionNames, List<string> discoveredToolNames)
    {
        return functionNames
            .Select(functionName =>
            {
                var expectedToolName = ToToolName(functionName);
                var matchedToolNames = discoveredToolNames
                    .Where(toolName =>
                        string.Equals(toolName, expectedToolName, StringComparison.OrdinalIgnoreCase) ||
                        toolName.StartsWith(expectedToolName + "_", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                return new ConfiguredFunctionStatus(functionName, expectedToolName, matchedToolNames.Count > 0, matchedToolNames);
            })
            .ToList();
    }

    /// <summary>
    /// For each missing command, runs PowerShell introspection to explain why it wasn't resolved.
    /// </summary>
    private static Dictionary<string, string> DiagnoseMissingCommands(
        IReadOnlyList<string> missingCommandNames,
        PowerShellConfiguration config)
    {
        var reasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (missingCommandNames.Count == 0)
            return reasons;

        try
        {
            using var runspace = new IsolatedPowerShellRunspace();
            runspace.ExecuteThreadSafe(ps =>
            {
                foreach (var commandName in missingCommandNames)
                {
                    try
                    {
                        reasons[commandName] = DiagnoseOneCommand(commandName);
                    }
                    catch (Exception ex)
                    {
                        reasons[commandName] = $"Diagnostic introspection failed: {ex.Message}";
                    }
                }

                string DiagnoseOneCommand(string name)
                {
                    var safeName = EscapeForPowerShell(name);

                    // Step 1: Is the command visible in the current session at all?
                    ps.Commands.Clear();
                    ps.AddScript($"Get-Command -Name {safeName} -ErrorAction SilentlyContinue | Select-Object -First 1");
                    var cmdResults = ps.Invoke();
                    ps.Commands.Clear();

                    if (cmdResults.Count > 0)
                    {
                        // The command exists but no tool was generated — all parameter sets were likely skipped.
                        return "Command found in PowerShell session but no tool was generated — " +
                               "all parameter sets may have been skipped due to unserializable parameter types";
                    }

                    // Step 2: For each configured module, check availability then command membership.
                    foreach (var moduleName in config.Modules)
                    {
                        var safeModuleName = EscapeForPowerShell(moduleName);

                        ps.Commands.Clear();
                        ps.AddScript($"Get-Module -Name {safeModuleName} -ListAvailable -ErrorAction SilentlyContinue | Select-Object -First 1");
                        var moduleAvailableResults = ps.Invoke();
                        ps.Commands.Clear();

                        if (moduleAvailableResults.Count == 0)
                        {
                            return $"Module '{moduleName}' not found in PSModulePath — " +
                                   "ensure the module is installed or its path is added to PSModulePath";
                        }

                        // Module is available — check whether it exports the command.
                        ps.Commands.Clear();
                        ps.AddScript(
                            $"Import-Module -Name {safeModuleName} -ErrorAction SilentlyContinue; " +
                            $"Get-Command -Module {safeModuleName} -Name {safeName} -ErrorAction SilentlyContinue | Select-Object -First 1");
                        var cmdInModuleResults = ps.Invoke();
                        ps.Commands.Clear();

                        if (cmdInModuleResults.Count == 0)
                        {
                            return $"Module '{moduleName}' is available but does not export command '{name}'";
                        }

                        return $"Command '{name}' found in module '{moduleName}' but was not loaded during tool discovery — " +
                               "check module import order or environment setup";
                    }

                    // No modules configured — bare command not found.
                    return $"Command '{name}' not found in PowerShell session — " +
                           "ensure the command exists and its module is installed and available in PSModulePath";
                }
            });
        }
        catch (Exception ex)
        {
            foreach (var name in missingCommandNames)
            {
                if (!reasons.ContainsKey(name))
                    reasons[name] = $"Diagnostic introspection failed: {ex.Message}";
            }
        }

        return reasons;
    }

    private static string EscapeForPowerShell(string value) => "'" + value.Replace("'", "''") + "'";

    private static List<string> GetDiscoveredToolNames(List<McpServerTool> tools)
    {
        var names = new List<string>();

        foreach (var tool in tools)
        {
            var name = TryGetNameFromObject(tool, 0);
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }

        }

        return names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? TryGetNameFromObject(object? value, int depth)
    {
        if (value is null || depth > 3)
        {
            return null;
        }

        var type = value.GetType();
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // First, look for a direct Name property.
        var directNameProperty = type.GetProperty("Name", flags);
        if (directNameProperty is not null && directNameProperty.PropertyType == typeof(string))
        {
            var name = directNameProperty.GetValue(value) as string;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        // Then recurse into nested objects to find name-bearing metadata.
        foreach (var property in type.GetProperties(flags))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            object? nestedValue;
            try
            {
                nestedValue = property.GetValue(value);
            }
            catch
            {
                continue;
            }

            if (nestedValue is null)
            {
                continue;
            }

            if (nestedValue is string)
            {
                continue;
            }

            var nestedName = TryGetNameFromObject(nestedValue, depth + 1);
            if (!string.IsNullOrWhiteSpace(nestedName))
            {
                return nestedName;
            }
        }

        return null;
    }

    private static List<string> GetExpectedToolNames(List<ConfiguredFunctionStatus> configuredFunctionStatus, bool enableDynamicReloadTools)
    {
        var names = new List<string>();

        // Include generated tools matched to configured functions (handles parameter-set specific names).
        names.AddRange(configuredFunctionStatus.SelectMany(functionStatus => functionStatus.MatchedToolNames));

        // Built-in utility tools are always generated.
        names.Add("get_last_command_output");
        names.Add("sort_last_command_output");
        names.Add("filter_last_command_output");
        names.Add("group_last_command_output");

        // Dynamic configuration tools are conditional.
        if (enableDynamicReloadTools)
        {
            names.Add("reload_configuration_from_file");
            names.Add("update_configuration");
            names.Add("get_configuration_status");
        }

        return names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ToToolName(string functionName)
    {
        var normalized = functionName.Replace('-', '_');
        normalized = Regex.Replace(normalized, "([a-z0-9])([A-Z])", "$1_$2");
        normalized = Regex.Replace(normalized, "_+", "_");
        return normalized.ToLowerInvariant();
    }

    public sealed record ConfiguredFunctionStatus(
        string FunctionName,
        string ExpectedToolName,
        bool Found,
        List<string> MatchedToolNames,
        string? ResolutionReason = null);



    private static void RunPSModulePathCommand(LogLevel logLevel)
    {
        Console.Error.WriteLine("=== PowerShell MCP Server - PSModulePath Report ===");
        Console.Error.WriteLine();

        using var loggerFactory = LoggingHelpers.CreateLoggerFactory(logLevel);
        var logger = loggerFactory.CreateLogger("PSModulePath");

        try
        {
            logger.LogInformation("Starting PowerShell runspace to check PSModulePath");

            using var runspace = new IsolatedPowerShellRunspace();

            var psModulePath = runspace.ExecuteThreadSafe(ps =>
            {
                ps.Commands.Clear();
                ps.AddScript("$env:PSModulePath");

                var results = ps.Invoke();

                if (ps.HadErrors)
                {
                    var errors = string.Join(Environment.NewLine, ps.Streams.Error);
                    throw new InvalidOperationException($"PowerShell execution failed: {errors}");
                }

                return results.Count > 0 ? results[0]?.ToString() ?? string.Empty : string.Empty;
            });

            Console.WriteLine("PSModulePath:");
            Console.WriteLine(new string('=', 50));

            if (!string.IsNullOrEmpty(psModulePath))
            {
                // Split the path and display each entry on a separate line for better readability
                var paths = psModulePath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < paths.Length; i++)
                {
                    Console.WriteLine($"{i + 1:D2}. {paths[i]}");
                }

                Console.WriteLine();
                Console.WriteLine($"Total module paths: {paths.Length}");
            }
            else
            {
                Console.WriteLine("(empty or undefined)");
            }

            Console.WriteLine(new string('=', 50));
            logger.LogInformation("PSModulePath report completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while checking PSModulePath: {ErrorMessage}", ex.Message);
            Console.Error.WriteLine($"Error: {ex.Message}");
        }
    }

    private static async Task RunToolEvaluationAsync(LogLevel logLevel)
    {
        PrintToolEvaluationHeader();
        using var loggerFactory = LoggingHelpers.CreateLoggerFactory(logLevel);
        var logger = loggerFactory.CreateLogger("ToolEvaluation");

        try
        {
            LogEvaluationStart(logger, logLevel);
            var finalConfigPath = await DetermineConfigurationPath(logger);
            var config = ConfigurationLoader.LoadPowerShellConfiguration(finalConfigPath, logger);
            var tools = await McpToolSetupService.DiscoverToolsForCliAsync(config, loggerFactory, logger, finalConfigPath);
            McpToolSetupService.ReportToolDiscoveryResults(tools, logger);
        }
        catch (Exception ex)
        {
            McpToolSetupService.HandleToolEvaluationError(ex, logger);
        }
    }

    private static void PrintToolEvaluationHeader()
    {
        Console.Error.WriteLine("=== PowerShell MCP Server - Tool Evaluation Mode ===");
        Console.Error.WriteLine();
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
        logger.LogInformation("Loading configuration from: {ConfigurationPath}", DescribeConfigurationPath(finalConfigPath));
        return finalConfigPath;
    }

    internal static async Task<string> ResolveConfigurationPath(string configPath)
    {
        var preferredConfigPath = File.Exists(configPath)
            ? new ResolvedSetting(configPath, SettingsResolver.CliSource)
            : new ResolvedSetting(null, SettingsResolver.DefaultSource);
        var resolvedConfigPath = await SettingsResolver.ResolveConfigurationPathWithSourceAsync(preferredConfigPath);
        return resolvedConfigPath.Value ?? string.Empty;
    }



    private static async Task RunMcpServerAsync(string[] args, LogLevel? overrideLogLevel = null, string? explicitConfigPath = null, string? configurationPathSource = null, string? runtimeModeOverride = null, string? logFilePath = null)
    {
        await StdioServerHost.RunMcpServerAsync(args, overrideLogLevel, explicitConfigPath, configurationPathSource, runtimeModeOverride, logFilePath);
    }

    private static async Task RunHttpTransportServerAsync(
        string[] args,
        LogLevel logLevel,
        string finalConfigPath,
        string configurationPathSource,
        string? runtimeModeOverride,
        string? url,
        string? mcpPath)
    {
        await HttpServerHost.RunHttpTransportServerAsync(args, logLevel, finalConfigPath, configurationPathSource, runtimeModeOverride, url, mcpPath);
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

    private static string DescribeConfigurationPath(string? configurationPath)
    {
        return string.IsNullOrWhiteSpace(configurationPath)
            ? "(environment-only configuration)"
            : configurationPath;
    }















}
