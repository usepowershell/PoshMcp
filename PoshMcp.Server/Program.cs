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

        return await rootCommand.InvokeAsync(args);
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
