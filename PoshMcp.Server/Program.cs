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

        var configOption = new Option<string?>(
            aliases: new[] { "--config", "-c" },
            description: "Path to configuration file (defaults to appsettings.json resolution)");

        var logLevelOption = new Option<string?>(
            aliases: new[] { "--log-level" },
            description: "Log level: trace|debug|info|warn|error");

        var transportOption = new Option<string?>(
            aliases: new[] { "--transport" },
            description: "Server transport: stdio|sse|http (currently stdio only for this executable)");

        var formatOption = new Option<string?>(
            aliases: new[] { "--format" },
            description: "Output format: text|json");

        var forceOption = new Option<bool>(
            aliases: new[] { "--force", "-f" },
            description: "Overwrite an existing appsettings.json when creating defaults");

        var nonInteractiveOption = new Option<bool>(
            aliases: new[] { "--non-interactive" },
            description: "Skip interactive advanced-configuration prompts during updates");



        var addCommandOption = new Option<string[]>(
            aliases: new[] { "--add-command" },
            description: "Add one or more command names to PowerShellConfiguration.CommandNames")
        {
            Arity = ArgumentArity.ZeroOrMore
        };

        var removeCommandOption = new Option<string[]>(
            aliases: new[] { "--remove-command" },
            description: "Remove one or more command names from PowerShellConfiguration.CommandNames")
        {
            Arity = ArgumentArity.ZeroOrMore
        };

        var addModuleOption = new Option<string[]>(
            aliases: new[] { "--add-module" },
            description: "Add one or more module names to PowerShellConfiguration.Modules")
        {
            Arity = ArgumentArity.ZeroOrMore
        };

        var removeModuleOption = new Option<string[]>(
            aliases: new[] { "--remove-module" },
            description: "Remove one or more module names from PowerShellConfiguration.Modules")
        {
            Arity = ArgumentArity.ZeroOrMore
        };

        var addIncludePatternOption = new Option<string[]>(
            aliases: new[] { "--add-include-pattern" },
            description: "Add one or more patterns to PowerShellConfiguration.IncludePatterns")
        {
            Arity = ArgumentArity.ZeroOrMore
        };

        var removeIncludePatternOption = new Option<string[]>(
            aliases: new[] { "--remove-include-pattern" },
            description: "Remove one or more patterns from PowerShellConfiguration.IncludePatterns")
        {
            Arity = ArgumentArity.ZeroOrMore
        };

        var addExcludePatternOption = new Option<string[]>(
            aliases: new[] { "--add-exclude-pattern" },
            description: "Add one or more patterns to PowerShellConfiguration.ExcludePatterns")
        {
            Arity = ArgumentArity.ZeroOrMore
        };

        var removeExcludePatternOption = new Option<string[]>(
            aliases: new[] { "--remove-exclude-pattern" },
            description: "Remove one or more patterns from PowerShellConfiguration.ExcludePatterns")
        {
            Arity = ArgumentArity.ZeroOrMore
        };

        var enableDynamicReloadToolsOption = new Option<string?>(
            aliases: new[] { "--enable-dynamic-reload-tools" },
            description: "Set PowerShellConfiguration.EnableDynamicReloadTools to true or false");

        var enableConfigurationTroubleshootingToolOption = new Option<string?>(
            aliases: new[] { "--enable-configuration-troubleshooting-tool" },
            description: "Set PowerShellConfiguration.EnableConfigurationTroubleshootingTool to true or false");

        var enableResultCachingOption = new Option<string?>(
            aliases: new[] { "--enable-result-caching" },
            description: "Set PowerShellConfiguration.Performance.EnableResultCaching to true or false");

        var useDefaultDisplayPropertiesOption = new Option<string?>(
            aliases: new[] { "--use-default-display-properties" },
            description: "Set PowerShellConfiguration.Performance.UseDefaultDisplayProperties to true or false");

        var setAuthEnabledOption = new Option<string?>(
            aliases: new[] { "--set-auth-enabled" },
            description: "Set Authentication.Enabled to true or false");

        var sessionModeOption = new Option<string?>(
            aliases: new[] { "--session-mode" },
            description: "Session mode hint: stateful|stateless (reserved for hosted transports)");

        var runtimeModeOption = new Option<string?>(
            aliases: new[] { "--runtime-mode" },
            description: "PowerShell runtime mode: in-process|out-of-process");

        var urlOption = new Option<string?>(
            aliases: new[] { "--url" },
            description: "URL bind hint for hosted transports (reserved)");

        var mcpPathOption = new Option<string?>(
            aliases: new[] { "--mcp-path" },
            description: "MCP endpoint path hint for hosted transports (reserved)");

        // Add subcommands
        var serveCommand = new Command("serve", "Run the MCP server (stdio transport by default)");
        serveCommand.AddOption(configOption);
        serveCommand.AddOption(logLevelOption);
        serveCommand.AddOption(transportOption);
        serveCommand.AddOption(sessionModeOption);
        serveCommand.AddOption(runtimeModeOption);
        serveCommand.AddOption(urlOption);
        serveCommand.AddOption(mcpPathOption);

        var logFileOption = new Option<string?>(
            aliases: new[] { "--log-file" },
            description: "Path to log file for stdio transport (suppresses console logging)");
        serveCommand.AddOption(logFileOption);

        var listToolsCommand = new Command("list-tools", "Discover and list tools without starting the MCP server");
        listToolsCommand.AddOption(configOption);
        listToolsCommand.AddOption(logLevelOption);
        listToolsCommand.AddOption(runtimeModeOption);
        listToolsCommand.AddOption(formatOption);

        var validateConfigCommand = new Command("validate-config", "Validate configuration and tool discovery");
        validateConfigCommand.AddOption(configOption);
        validateConfigCommand.AddOption(logLevelOption);
        validateConfigCommand.AddOption(runtimeModeOption);
        validateConfigCommand.AddOption(formatOption);

        var doctorCommand = new Command("doctor", "Run runtime and configuration diagnostics");
        doctorCommand.AddOption(configOption);
        doctorCommand.AddOption(logLevelOption);
        doctorCommand.AddOption(transportOption);
        doctorCommand.AddOption(sessionModeOption);
        doctorCommand.AddOption(runtimeModeOption);
        doctorCommand.AddOption(mcpPathOption);
        doctorCommand.AddOption(formatOption);

        var createConfigCommand = new Command("create-config", "Create a default appsettings.json in the current directory");
        createConfigCommand.AddOption(forceOption);
        createConfigCommand.AddOption(formatOption);

        var updateConfigCommand = new Command("update-config", "Update settings in the active configuration file (same resolution rules as doctor)");
        updateConfigCommand.AddOption(configOption);
        updateConfigCommand.AddOption(logLevelOption);
        updateConfigCommand.AddOption(formatOption);
        updateConfigCommand.AddOption(addCommandOption);
        updateConfigCommand.AddOption(removeCommandOption);
        updateConfigCommand.AddOption(addModuleOption);
        updateConfigCommand.AddOption(removeModuleOption);
        updateConfigCommand.AddOption(addIncludePatternOption);
        updateConfigCommand.AddOption(removeIncludePatternOption);
        updateConfigCommand.AddOption(addExcludePatternOption);
        updateConfigCommand.AddOption(removeExcludePatternOption);
        updateConfigCommand.AddOption(enableDynamicReloadToolsOption);
        updateConfigCommand.AddOption(enableConfigurationTroubleshootingToolOption);
        updateConfigCommand.AddOption(enableResultCachingOption);
        updateConfigCommand.AddOption(useDefaultDisplayPropertiesOption);
        updateConfigCommand.AddOption(setAuthEnabledOption);
        updateConfigCommand.AddOption(runtimeModeOption);
        updateConfigCommand.AddOption(nonInteractiveOption);

        var psModulePathCommand = new Command("psmodulepath", "Start a PowerShell runspace and report the value of $env:PSModulePath");
        psModulePathCommand.AddOption(verboseOption);
        psModulePathCommand.AddOption(debugOption);
        psModulePathCommand.AddOption(traceOption);

        // Build command for Docker image creation
        var buildModulesOption = new Option<string?>(
            aliases: new[] { "--modules" },
            description: "Space-separated module names to pre-install in custom images (e.g., 'Pester Az.Accounts')");

        var buildTypeOption = new Option<string?>(
            aliases: new[] { "--type", "--base" },
            description: "Image type: 'custom' (derived from a source/base image) or 'base' (build local runtime source image). Default: custom");

        var buildTagOption = new Option<string?>(
            aliases: new[] { "--tag" },
            description: "Image tag name (default: poshmcp:latest)");

        var buildDockerFileOption = new Option<string?>(
            aliases: new[] { "--docker-file" },
            description: "Custom Dockerfile path for advanced users");

        var buildSourceImageOption = new Option<string?>(
            aliases: new[] { "--source-image" },
            description: "Source/base image repository or full reference for custom builds (default: ghcr.io/usepowershell/poshmcp/poshmcp:latest)");

        var buildSourceTagOption = new Option<string?>(
            aliases: new[] { "--source-tag" },
            description: "Tag for --source-image when a repository is provided (default: latest)");

        var buildGenerateDockerfileOption = new Option<bool>(
            aliases: new[] { "--generate-dockerfile" },
            description: "Write the generated Dockerfile to disk instead of building");

        var buildDockerfileOutputOption = new Option<string?>(
            aliases: new[] { "--dockerfile-output" },
            description: "Path for the generated Dockerfile (default: ./Dockerfile.generated)");

        var buildAppSettingsOption = new Option<string?>(
            aliases: new[] { "--appsettings" },
            description: "Path to a local appsettings.json to bundle into the image at /app/server/appsettings.json");

        var buildCommand = new Command("build", "Build a Docker image; defaults to creating a custom image from the published GHCR base image");
        buildCommand.AddOption(buildModulesOption);
        buildCommand.AddOption(buildTypeOption);
        buildCommand.AddOption(buildTagOption);
        buildCommand.AddOption(buildDockerFileOption);
        buildCommand.AddOption(buildSourceImageOption);
        buildCommand.AddOption(buildSourceTagOption);
        buildCommand.AddOption(buildGenerateDockerfileOption);
        buildCommand.AddOption(buildDockerfileOutputOption);
        buildCommand.AddOption(buildAppSettingsOption);

        // Run command for Docker container execution
        var runModeOption = new Option<string?>(
            aliases: new[] { "--mode" },
            description: "Transport mode: 'http' or 'stdio' (default: http)");

        var runPortOption = new Option<int?>(
            aliases: new[] { "--port" },
            description: "Port number for HTTP mode (default: 8080)");

        var runTagOption = new Option<string?>(
            aliases: new[] { "--tag" },
            description: "Image tag to run (default: poshmcp:latest)");

        var runConfigOption = new Option<string?>(
            aliases: new[] { "--config" },
            description: "Config file path to mount into container");

        var runVolumeOption = new Option<string[]>(
            aliases: new[] { "--volume" },
            description: "Volume mount in format 'source:destination' (repeatable)")
        {
            Arity = ArgumentArity.ZeroOrMore
        };

        var runInteractiveOption = new Option<bool>(
            aliases: new[] { "--interactive", "-it" },
            description: "Run in interactive mode with terminal");

        var runCommand = new Command("run", "Run PoshMcp in a Docker container");
        runCommand.AddOption(runModeOption);
        runCommand.AddOption(runPortOption);
        runCommand.AddOption(runTagOption);
        runCommand.AddOption(runConfigOption);
        runCommand.AddOption(runVolumeOption);
        runCommand.AddOption(runInteractiveOption);

        var scaffoldProjectPathOption = new Option<string?>(
            aliases: new[] { "--project-path", "--path", "-p" },
            description: "Target project directory where infra/azure files will be scaffolded (default: current directory)");

        var scaffoldCommand = new Command("scaffold", "Scaffold embedded infrastructure artifacts into a target project");
        scaffoldCommand.AddOption(scaffoldProjectPathOption);
        scaffoldCommand.AddOption(forceOption);
        scaffoldCommand.AddOption(formatOption);

        rootCommand.AddCommand(serveCommand);
        rootCommand.AddCommand(listToolsCommand);
        rootCommand.AddCommand(validateConfigCommand);
        rootCommand.AddCommand(doctorCommand);
        rootCommand.AddCommand(createConfigCommand);
        rootCommand.AddCommand(updateConfigCommand);
        rootCommand.AddOption(evaluateToolsOption);
        rootCommand.AddOption(verboseOption);
        rootCommand.AddOption(debugOption);
        rootCommand.AddOption(traceOption);
        rootCommand.AddCommand(psModulePathCommand);
        rootCommand.AddCommand(buildCommand);
        rootCommand.AddCommand(runCommand);
        rootCommand.AddCommand(scaffoldCommand);

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
                await RunMcpServerAsync(args, logLevel, null);
            }
        }, evaluateToolsOption, verboseOption, debugOption, traceOption);

        serveCommand.SetHandler(async (InvocationContext context) =>
        {
            var configPath = context.ParseResult.GetValueForOption(configOption);
            var logLevelText = context.ParseResult.GetValueForOption(logLevelOption);
            var transport = context.ParseResult.GetValueForOption(transportOption);
            var sessionMode = context.ParseResult.GetValueForOption(sessionModeOption);
            var runtimeMode = context.ParseResult.GetValueForOption(runtimeModeOption);
            var url = context.ParseResult.GetValueForOption(urlOption);
            var mcpPath = context.ParseResult.GetValueForOption(mcpPathOption);
            var logFile = context.ParseResult.GetValueForOption(logFileOption);

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
                    await RunMcpServerAsync(args, parsedLogLevel, resolvedSettings.FinalConfigPath, resolvedSettings.ConfigPath.Source, resolvedSettings.RuntimeMode.Value, resolvedLogFile.Value);
                    Environment.ExitCode = ExitCodeSuccess;
                    return;
                }

                if (transportMode == TransportMode.Http)
                {
                    await RunHttpTransportServerAsync(args, parsedLogLevel, resolvedSettings.FinalConfigPath, resolvedSettings.ConfigPath.Source, resolvedSettings.RuntimeMode.Value, url, resolvedSettings.McpPath.Value);
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

        listToolsCommand.SetHandler(async (configPath, logLevelText, runtimeMode, format) =>
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
        }, configOption, logLevelOption, runtimeModeOption, formatOption);

        validateConfigCommand.SetHandler(async (configPath, logLevelText, runtimeMode, format) =>
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
        }, configOption, logLevelOption, runtimeModeOption, formatOption);

        doctorCommand.SetHandler(async (configPath, logLevelText, transport, sessionMode, runtimeMode, mcpPath, format) =>
        {
            var resolvedSettings = await SettingsResolver.ResolveCommandSettingsAsync(args, configPath, logLevelText, transport, sessionMode, runtimeMode, mcpPath);
            var outputFormat = ConfigurationFileManager.NormalizeFormat(format);
            try
            {
                await RunDoctorAsync(resolvedSettings, outputFormat);
                Environment.ExitCode = ExitCodeSuccess;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Doctor failed: {ex.Message}");
                Environment.ExitCode = ExitCodeRuntimeError;
            }
        }, configOption, logLevelOption, transportOption, sessionModeOption, runtimeModeOption, mcpPathOption, formatOption);

        createConfigCommand.SetHandler(async (force, format) =>
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
        }, forceOption, formatOption);

        updateConfigCommand.SetHandler(async (InvocationContext context) =>
        {
            var configPath = context.ParseResult.GetValueForOption(configOption);
            var logLevelText = context.ParseResult.GetValueForOption(logLevelOption);
            var format = context.ParseResult.GetValueForOption(formatOption);
            var addCommands = context.ParseResult.GetValueForOption(addCommandOption);
            var removeCommands = context.ParseResult.GetValueForOption(removeCommandOption);
            var addModules = context.ParseResult.GetValueForOption(addModuleOption);
            var removeModules = context.ParseResult.GetValueForOption(removeModuleOption);
            var addIncludePatterns = context.ParseResult.GetValueForOption(addIncludePatternOption);
            var removeIncludePatterns = context.ParseResult.GetValueForOption(removeIncludePatternOption);
            var addExcludePatterns = context.ParseResult.GetValueForOption(addExcludePatternOption);
            var removeExcludePatterns = context.ParseResult.GetValueForOption(removeExcludePatternOption);
            var enableDynamicReloadTools = context.ParseResult.GetValueForOption(enableDynamicReloadToolsOption);
            var enableConfigurationTroubleshootingTool = context.ParseResult.GetValueForOption(enableConfigurationTroubleshootingToolOption);
            var enableResultCaching = context.ParseResult.GetValueForOption(enableResultCachingOption);
            var useDefaultDisplayProperties = context.ParseResult.GetValueForOption(useDefaultDisplayPropertiesOption);
            var setAuthEnabled = context.ParseResult.GetValueForOption(setAuthEnabledOption);
            var runtimeMode = context.ParseResult.GetValueForOption(runtimeModeOption);
            var nonInteractive = context.ParseResult.GetValueForOption(nonInteractiveOption);

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
        psModulePathCommand.SetHandler((verbose, debug, trace) =>
        {
            // Determine log level based on options
            LogLevel logLevel = LogLevel.Information;
            if (trace) logLevel = LogLevel.Trace;
            else if (debug) logLevel = LogLevel.Debug;
            else if (verbose) logLevel = LogLevel.Debug; // Verbose maps to Debug level

            RunPSModulePathCommand(logLevel);
        }, verboseOption, debugOption, traceOption);

        // Handler for the build command
        buildCommand.SetHandler((InvocationContext context) =>
        {
            try
            {
                var modules = context.ParseResult.GetValueForOption(buildModulesOption);
                var type = context.ParseResult.GetValueForOption(buildTypeOption);
                var tag = context.ParseResult.GetValueForOption(buildTagOption);
                var dockerFile = context.ParseResult.GetValueForOption(buildDockerFileOption);
                var sourceImage = context.ParseResult.GetValueForOption(buildSourceImageOption);
                var sourceTag = context.ParseResult.GetValueForOption(buildSourceTagOption);
                var generateDockerfile = context.ParseResult.GetValueForOption(buildGenerateDockerfileOption);
                var dockerfileOutput = context.ParseResult.GetValueForOption(buildDockerfileOutputOption);
                var appSettings = context.ParseResult.GetValueForOption(buildAppSettingsOption);

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
        runCommand.SetHandler((mode, port, tag, config, volumes, interactive) =>
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
        }, runModeOption, runPortOption, runTagOption, runConfigOption, runVolumeOption, runInteractiveOption);

        scaffoldCommand.SetHandler(async (projectPath, force, format) =>
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
        }, scaffoldProjectPathOption, forceOption, formatOption);

        return await rootCommand.InvokeAsync(args);
    }


    private static async Task RunListToolsAsync(LogLevel logLevel, string finalConfigPath, string? runtimeModeOverride, string format)
    {
        using var loggerFactory = LoggingHelpers.CreateLoggerFactory(logLevel);
        var logger = loggerFactory.CreateLogger("ListTools");

        var config = ConfigurationLoader.LoadPowerShellConfiguration(finalConfigPath, logger, runtimeModeOverride);
        var tools = await DiscoverToolsAsync(config, loggerFactory, logger, finalConfigPath);

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
        var tools = await DiscoverToolsAsync(config, loggerFactory, logger, finalConfigPath);
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

    private static async Task RunDoctorAsync(ResolvedCommandSettings settings, string format)
    {
        var parsedLogLevel = SettingsResolver.ParseLogLevel(settings.LogLevel.Value);
        using var loggerFactory = LoggingHelpers.CreateLoggerFactory(parsedLogLevel);
        var logger = loggerFactory.CreateLogger("Doctor");

        var config = ConfigurationLoader.LoadPowerShellConfiguration(settings.FinalConfigPath, logger, settings.RuntimeMode.Value);
        var environmentVariables = CollectEnvironmentVariables();
        var tools = await DiscoverToolsAsync(config, loggerFactory, logger, settings.FinalConfigPath);
        var discoveredToolNames = GetDiscoveredToolNames(tools);
        var configuredFunctionStatus = BuildConfiguredFunctionStatus(config.GetEffectiveCommandNames(), discoveredToolNames);
        var toolNames = discoveredToolNames.Count > 0
            ? discoveredToolNames
            : GetExpectedToolNames(configuredFunctionStatus, config.EnableDynamicReloadTools);
        var diagnostics = CollectPowerShellDiagnostics();
        var oopModulePaths = ResolveConfiguredModulePathsForOop(config, settings.FinalConfigPath);

        var missingFunctions = configuredFunctionStatus.Where(f => !f.Found).Select(f => f.FunctionName).ToList();
        if (missingFunctions.Count > 0)
        {
            var resolutionReasons = DiagnoseMissingCommands(missingFunctions, config);
            configuredFunctionStatus = configuredFunctionStatus
                .Select(s => s.Found ? s : s with { ResolutionReason = resolutionReasons.GetValueOrDefault(s.FunctionName) })
                .ToList();
        }

        var warnings = BuildConfigurationWarnings(config);
        var (resourcesDiag, promptsDiag) = ConfigurationLoader.TryValidateResourcesAndPrompts(settings.FinalConfigPath);

        var report = DoctorReport.Build(
            configurationPath: DescribeConfigurationPath(settings.FinalConfigPath),
            configurationPathSource: settings.ConfigPath.Source,
            effectiveLogLevel: settings.LogLevel.Value,
            effectiveLogLevelSource: settings.LogLevel.Source,
            effectiveTransport: settings.Transport.Value,
            effectiveTransportSource: settings.Transport.Source,
            effectiveSessionMode: settings.SessionMode.Value,
            effectiveSessionModeSource: settings.SessionMode.Source,
            effectiveRuntimeMode: settings.RuntimeMode.Value,
            effectiveRuntimeModeSource: settings.RuntimeMode.Source,
            effectiveMcpPath: settings.McpPath.Value,
            effectiveMcpPathSource: settings.McpPath.Source,
            configuredFunctionStatus: configuredFunctionStatus,
            toolNames: toolNames,
            powerShellVersion: diagnostics.PowerShellVersion,
            modulePathEntries: diagnostics.ModulePathEntries,
            modulePaths: diagnostics.ModulePaths,
            oopModulePaths: oopModulePaths,
            resourcesDiagnostics: resourcesDiag,
            promptsDiagnostics: promptsDiag,
            warnings: warnings,
            environmentVariables: environmentVariables);

        if (format == "json")
        {
            Console.WriteLine(BuildDoctorJson(report));
            return;
        }

        Console.WriteLine(DoctorTextRenderer.Render(report));
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
        List<McpServerTool> tools)
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
        var warnings = BuildConfigurationWarnings(config);
        var (resourcesDiag, promptsDiag) = ConfigurationLoader.TryValidateResourcesAndPrompts(configurationPath);
        var environmentVariables = CollectEnvironmentVariables();

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
            environmentVariables: environmentVariables);
    }

    private static List<string> BuildConfigurationWarnings(PowerShellConfiguration config)
    {
        var warnings = new List<string>();
        if (config.HasBothCommandAndFunctionNames)
        {
            warnings.Add("Both CommandNames and FunctionNames are configured. CommandNames takes precedence; FunctionNames entries are ignored.");
        }
        else if (config.HasLegacyFunctionNames)
        {
            warnings.Add("FunctionNames is deprecated. Migrate to CommandNames in your appsettings.json (rename the \"FunctionNames\" array to \"CommandNames\").");
        }
        return warnings;
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
            var tools = await DiscoverToolsAsync(config, loggerFactory, logger, finalConfigPath);
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

    private static async Task<List<McpServerTool>> DiscoverToolsAsync(PowerShellConfiguration config, ILoggerFactory loggerFactory, ILogger logger, string configurationPath)
    {
        logger.LogInformation("Discovering PowerShell tools...");
        await using var executorLease = await StartOutOfProcessExecutorIfNeededAsync(config, loggerFactory, logger, configurationPath);
        var toolFactory = CreateToolFactory(config, executorLease?.Executor);
        var tools = await toolFactory.GetToolsListAsync(config, logger);
        AddConfigurationGuidanceToolToList(tools, config, configurationPath, "stdio", config.RuntimeMode.ToString(), null, loggerFactory);
        AddConfigurationTroubleshootingToolToList(tools, config, configurationPath, "stdio", null, config.RuntimeMode.ToString(), null, logger);
        return tools;
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

    private static async Task RunMcpServerAsync(string[] args, LogLevel? overrideLogLevel = null, string? explicitConfigPath = null, string? configurationPathSource = null, string? runtimeModeOverride = null, string? logFilePath = null)
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
        await using var executorLease = await StartOutOfProcessExecutorIfNeededAsync(config, loggerFactory, logger, finalConfigPath);
        var tools = await SetupMcpToolsAsync(loggerFactory, config, logger, finalConfigPath, configurationPathSource ?? InferConfigurationPathSource(finalConfigPath), executorLease?.Executor);
        var promptsConfig = ConfigurationLoader.LoadPromptsConfiguration(finalConfigPath);
        var configDirectory = Path.GetDirectoryName(finalConfigPath) ?? Directory.GetCurrentDirectory();
        var promptHandler = new McpPromptHandler(promptsConfig, configDirectory, loggerFactory.CreateLogger<McpPromptHandler>());
        ConfigureServerServices(builder, tools, resourcesConfig, finalConfigPath, loggerFactory, promptHandler);
        await builder.Build().RunAsync();
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

        builder.Services
            .AddOptions<PoshMcp.Server.Authentication.AuthenticationConfiguration>()
            .BindConfiguration("Authentication")
            .ValidateOnStart();

        builder.Services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<PoshMcp.Server.Authentication.AuthenticationConfiguration>,
            PoshMcp.Server.Authentication.AuthenticationConfigurationValidator>();

        ConfigureJsonSerializerOptions(builder);
        ConfigureCorsForMcp(builder);
        RegisterHealthChecks(builder);

        ConfigureOpenTelemetryForHttp(builder);
        ConfigureApplicationInsights(builder.Services, builder.Configuration, isStdioMode: false);

        using var bootstrapLoggerFactory = LoggingHelpers.CreateLoggerFactory(logLevel);
        var logger = bootstrapLoggerFactory.CreateLogger("PoshMcpHttpLogger");
        var config = ConfigurationLoader.LoadPowerShellConfiguration(finalConfigPath, logger, runtimeModeOverride);
        await using var executorLease = await StartOutOfProcessExecutorIfNeededAsync(config, bootstrapLoggerFactory, logger, finalConfigPath);

        var sharedHttpContextAccessor = new HttpContextAccessor();
        var sharedRunspaceLogger = bootstrapLoggerFactory.CreateLogger<SessionAwarePowerShellRunspace>();
        var sharedSessionRunspace = new SessionAwarePowerShellRunspace(sharedHttpContextAccessor, sharedRunspaceLogger);

        builder.Services.AddSingleton<IHttpContextAccessor>(sharedHttpContextAccessor);
        builder.Services.AddSingleton<IPowerShellRunspace>(sharedSessionRunspace);

        logger.LogInformation("Using configuration source: {ConfigurationPath}", DescribeConfigurationPath(finalConfigPath));

        var tools = await SetupHttpMcpToolsAsync(bootstrapLoggerFactory, config, logger, finalConfigPath, configurationPathSource, sharedSessionRunspace, executorLease?.Executor);
        var resourcesConfig = ConfigurationLoader.LoadMcpResourcesConfiguration(finalConfigPath, logger);
        var resourcesConfigDirectory = Path.GetDirectoryName(finalConfigPath) ?? ".";
        var resourceLogger = bootstrapLoggerFactory.CreateLogger<McpResourceHandler>();
        var resourceHandler = new McpResourceHandler(resourcesConfig, sharedSessionRunspace, resourcesConfigDirectory, resourceLogger);
        var authConfigValue = builder.Configuration.GetSection("Authentication").Get<PoshMcp.Server.Authentication.AuthenticationConfiguration>() ?? new();
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

        builder.Services.AddPoshMcpAuthentication(builder.Configuration);

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

        await app.RunAsync();
    }

    private static void ConfigureCorsForMcp(WebApplicationBuilder builder)
    {
        var authConfig = builder.Configuration.GetSection("Authentication").Get<AuthenticationConfiguration>()
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

    private static void RegisterHealthChecks(WebApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            .AddCheck<PowerShellRunspaceHealthCheck>("powershell_runspace")
            .AddCheck<AssemblyGenerationHealthCheck>("assembly_generation")
            .AddCheck<ConfigurationHealthCheck>("configuration");
    }

    private static void ConfigureOpenTelemetryForHttp(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<McpMetrics>();

        builder.Services.AddOpenTelemetry()
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

    private static async Task<List<McpServerTool>> SetupHttpMcpToolsAsync(
        ILoggerFactory loggerFactory,
        PowerShellConfiguration config,
        ILogger logger,
        string finalConfigPath,
        string configurationPathSource,
        IPowerShellRunspace sessionAwareRunspace,
        ICommandExecutor? commandExecutor)
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
        AddConfigurationTroubleshootingToolToList(tools, config, finalConfigPath, "http", null, config.RuntimeMode.ToString(), null, logger);

        return tools;
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

    private static (ILogger logger, PowerShellConfiguration config) ExtractLoggerAndConfiguration(IServiceProvider serviceProvider, string finalConfigPath)
    {
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PoshMcpLogger");
        var config = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PowerShellConfiguration>>().Value;
        logger.LogInformation("Using configuration source: {ConfigurationPath}", DescribeConfigurationPath(finalConfigPath));
        return (logger, config);
    }

    private static string DescribeConfigurationPath(string? configurationPath)
    {
        return string.IsNullOrWhiteSpace(configurationPath)
            ? "(environment-only configuration)"
            : configurationPath;
    }

    private static async Task<List<McpServerTool>> SetupMcpToolsAsync(ILoggerFactory loggerFactory, PowerShellConfiguration config, ILogger logger, string finalConfigPath, string configurationPathSource, ICommandExecutor? commandExecutor)
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

    private static void AddConfigurationTroubleshootingToolToList(
        List<McpServerTool> tools,
        PowerShellConfiguration config,
        string configurationPath,
        string effectiveTransport,
        string? effectiveSessionMode,
        string? effectiveRuntimeMode,
        string? effectiveMcpPath,
        ILogger logger)
    {
        if (!config.EnableConfigurationTroubleshootingTool)
        {
            return;
        }

        tools.Add(CreateConfigurationTroubleshootingToolInstance(
            configurationPath,
            effectiveTransport,
            effectiveSessionMode,
            effectiveRuntimeMode,
            effectiveMcpPath,
            () => tools,
            logger));
    }

    private static McpServerTool CreateConfigurationTroubleshootingToolInstance(
        string configurationPath,
        string effectiveTransport,
        string? effectiveSessionMode,
        string? effectiveRuntimeMode,
        string? effectiveMcpPath,
        Func<List<McpServerTool>> registeredToolsProvider,
        ILogger logger)
    {
        Func<CancellationToken, Task<string>> troubleshootingDelegate = cancellationToken =>
        {
            try
            {
                logger.LogInformation("Processing configuration troubleshooting request");

                var config = ConfigurationLoader.LoadPowerShellConfiguration(configurationPath, logger, effectiveRuntimeMode);
                var tools = registeredToolsProvider();
                var report = BuildDoctorReportFromConfig(
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
                    tools: tools);

                return Task.FromResult(BuildDoctorJson(report));
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
            Description = "Returns doctor-style configuration diagnostics for the running server. Output includes runtime settings, environment variables, PowerShell info, configured functions, and MCP definitions. Always returns structured text output.",
            Title = "Get Configuration Troubleshooting",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false,
            UseStructuredContent = true
        });
    }

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

    private static McpToolFactoryV2 CreateToolFactory(PowerShellConfiguration config, ICommandExecutor? commandExecutor, IPowerShellRunspace? runspace = null)
    {
        if (config.RuntimeMode == RuntimeMode.OutOfProcess)
        {
            return commandExecutor is null
                ? throw new InvalidOperationException("Out-of-process runtime mode requires a started command executor.")
                : new McpToolFactoryV2(commandExecutor);
        }

        return runspace is null ? new McpToolFactoryV2() : new McpToolFactoryV2(runspace);
    }

    private static async Task<OutOfProcessExecutorLease?> StartOutOfProcessExecutorIfNeededAsync(PowerShellConfiguration config, ILoggerFactory loggerFactory, ILogger logger, string? configFilePath = null)
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

    private sealed class OutOfProcessExecutorLease : IAsyncDisposable
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

    private static string InferConfigurationPathSource(string? configurationPath)
    {
        return string.IsNullOrWhiteSpace(configurationPath) ? SettingsResolver.EnvSource : "runtime";
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

    private static void ConfigureOpenTelemetry(HostApplicationBuilder builder, bool isStdioMode = false)
    {
        // Register and configure OpenTelemetry metrics
        builder.Services.AddSingleton<McpMetrics>();

        builder.Services.AddOpenTelemetry()
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

    private static void RegisterCleanupServices(HostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IHostedService, PowerShellCleanupService>();
    }

    private static void RegisterCleanupServices(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IHostedService, PowerShellCleanupService>();
    }

}
