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

        // CS8604: Suppress null-reference warnings from System.CommandLine SetHandler overloads.
        // These warnings are false positives: CliDefinition properties are non-null after Build(),
        // and SetHandler correctly enforces type-safety at runtime.
#pragma warning disable CS8604
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
                await CommandHandlers.RunToolEvaluationAsync(logLevel);
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

                await CommandHandlers.RunListToolsAsync(parsedLogLevel, resolvedSettings.FinalConfigPath, resolvedSettings.RuntimeMode.Value, outputFormat);
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

                await CommandHandlers.RunValidateConfigAsync(parsedLogLevel, resolvedSettings.FinalConfigPath, resolvedSettings.RuntimeMode.Value, outputFormat);
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
            await CommandHandlers.RunCreateConfigAsync(force, format);
        }, CliDefinition.ForceOption, CliDefinition.FormatOption);

        CliDefinition.UpdateConfigCommand!.SetHandler(async (InvocationContext context) =>
        {
            await CommandHandlers.RunUpdateConfigAsync(args, context);
        });

        // Handler for the psmodulepath command
        CliDefinition.PsModulePathCommand!.SetHandler((verbose, debug, trace) =>
        {
            // Determine log level based on options
            LogLevel logLevel = LogLevel.Information;
            if (trace) logLevel = LogLevel.Trace;
            else if (debug) logLevel = LogLevel.Debug;
            else if (verbose) logLevel = LogLevel.Debug; // Verbose maps to Debug level

            CommandHandlers.RunPSModulePathCommand(logLevel);
        }, CliDefinition.VerboseOption, CliDefinition.DebugOption, CliDefinition.TraceOption);

        // Handler for the build command
        // Handler for the build command
        CliDefinition.BuildCommand!.SetHandler((InvocationContext context) =>
        {
            CommandHandlers.RunBuildCommand(context);
        });

        // Handler for the run command
        CliDefinition.RunCommand!.SetHandler((mode, port, tag, config, volumes, interactive) =>
        {
            CommandHandlers.RunRunCommand(mode, port, tag, config, volumes, interactive);
        }, CliDefinition.RunModeOption, CliDefinition.RunPortOption, CliDefinition.RunTagOption, CliDefinition.RunConfigOption, CliDefinition.RunVolumeOption, CliDefinition.RunInteractiveOption);

        CliDefinition.ScaffoldCommand!.SetHandler(async (projectPath, force, format) =>
        {
            await CommandHandlers.RunScaffoldCommand(projectPath, force, format);
        }, CliDefinition.ScaffoldProjectPathOption, CliDefinition.ForceOption, CliDefinition.FormatOption);
#pragma warning restore CS8604

        return await rootCommand.InvokeAsync(args);
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


}
