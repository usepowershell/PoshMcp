using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using PoshMcp.Server.Health;
using PoshMcp.Server.Observability;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.Metrics;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PoshMcp;

public class Program
{
    private const int ExitCodeSuccess = 0;
    private const int ExitCodeConfigError = 2;
    private const int ExitCodeStartupError = 3;
    private const int ExitCodeRuntimeError = 4;
    private const string TransportEnvVar = "POSHMCP_TRANSPORT";
    private const string ConfigurationEnvVar = "POSHMCP_CONFIGURATION";
    private const string McpPathEnvVar = "POSHMCP_MCP_PATH";
    private const string SessionModeEnvVar = "POSHMCP_SESSION_MODE";
    private const string LogLevelEnvVar = "POSHMCP_LOG_LEVEL";
    private const string CliSource = "cli";
    private const string EnvSource = "env";
    private const string DefaultSource = "default";
    private const string CwdSource = "cwd";
    private const string UserSource = "user";
    private const string EmbeddedDefaultSource = "embedded-default";

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

        var sessionModeOption = new Option<string?>(
            aliases: new[] { "--session-mode" },
            description: "Session mode hint: stateful|stateless (reserved for hosted transports)");

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
        serveCommand.AddOption(urlOption);
        serveCommand.AddOption(mcpPathOption);

        var listToolsCommand = new Command("list-tools", "Discover and list tools without starting the MCP server");
        listToolsCommand.AddOption(configOption);
        listToolsCommand.AddOption(logLevelOption);
        listToolsCommand.AddOption(formatOption);

        var validateConfigCommand = new Command("validate-config", "Validate configuration and tool discovery");
        validateConfigCommand.AddOption(configOption);
        validateConfigCommand.AddOption(logLevelOption);
        validateConfigCommand.AddOption(formatOption);

        var doctorCommand = new Command("doctor", "Run runtime and configuration diagnostics");
        doctorCommand.AddOption(configOption);
        doctorCommand.AddOption(logLevelOption);
        doctorCommand.AddOption(transportOption);
        doctorCommand.AddOption(sessionModeOption);
        doctorCommand.AddOption(mcpPathOption);
        doctorCommand.AddOption(formatOption);

        var psModulePathCommand = new Command("psmodulepath", "Start a PowerShell runspace and report the value of $env:PSModulePath");
        psModulePathCommand.AddOption(verboseOption);
        psModulePathCommand.AddOption(debugOption);
        psModulePathCommand.AddOption(traceOption);

        rootCommand.AddCommand(serveCommand);
        rootCommand.AddCommand(listToolsCommand);
        rootCommand.AddCommand(validateConfigCommand);
        rootCommand.AddCommand(doctorCommand);
        rootCommand.AddOption(evaluateToolsOption);
        rootCommand.AddOption(verboseOption);
        rootCommand.AddOption(debugOption);
        rootCommand.AddOption(traceOption);
        rootCommand.AddCommand(psModulePathCommand);

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

        serveCommand.SetHandler(async (configPath, logLevelText, transport, sessionMode, url, mcpPath) =>
        {
            var resolvedSettings = await ResolveCommandSettingsAsync(args, configPath, logLevelText, transport, sessionMode, mcpPath);
            var parsedLogLevel = ParseLogLevel(resolvedSettings.LogLevel.Value);
            var transportMode = ResolveTransportMode(resolvedSettings.Transport.Value);

            try
            {
                if (ShouldPrintResolvedSettings(parsedLogLevel))
                {
                    PrintResolvedSettings("serve", resolvedSettings);
                }

                if (transportMode == TransportMode.Stdio)
                {
                    await RunMcpServerAsync(args, parsedLogLevel, resolvedSettings.FinalConfigPath);
                    Environment.ExitCode = ExitCodeSuccess;
                    return;
                }

                if (transportMode == TransportMode.Http)
                {
                    await RunHttpTransportServerAsync(args, parsedLogLevel, resolvedSettings.FinalConfigPath, url, resolvedSettings.McpPath.Value);
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
        }, configOption, logLevelOption, transportOption, sessionModeOption, urlOption, mcpPathOption);

        listToolsCommand.SetHandler(async (configPath, logLevelText, format) =>
        {
            var resolvedSettings = await ResolveCommandSettingsAsync(args, configPath, logLevelText, null, null, null);
            var parsedLogLevel = ParseLogLevel(resolvedSettings.LogLevel.Value);
            var outputFormat = NormalizeFormat(format);
            try
            {
                if (ShouldPrintResolvedSettings(parsedLogLevel))
                {
                    PrintResolvedSettings("list-tools", resolvedSettings);
                }

                await RunListToolsAsync(parsedLogLevel, resolvedSettings.FinalConfigPath, outputFormat);
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
        }, configOption, logLevelOption, formatOption);

        validateConfigCommand.SetHandler(async (configPath, logLevelText, format) =>
        {
            var resolvedSettings = await ResolveCommandSettingsAsync(args, configPath, logLevelText, null, null, null);
            var parsedLogLevel = ParseLogLevel(resolvedSettings.LogLevel.Value);
            var outputFormat = NormalizeFormat(format);
            try
            {
                if (ShouldPrintResolvedSettings(parsedLogLevel))
                {
                    PrintResolvedSettings("validate-config", resolvedSettings);
                }

                await RunValidateConfigAsync(parsedLogLevel, resolvedSettings.FinalConfigPath, outputFormat);
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
        }, configOption, logLevelOption, formatOption);

        doctorCommand.SetHandler(async (configPath, logLevelText, transport, sessionMode, mcpPath, format) =>
        {
            var resolvedSettings = await ResolveCommandSettingsAsync(args, configPath, logLevelText, transport, sessionMode, mcpPath);
            var outputFormat = NormalizeFormat(format);
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
        }, configOption, logLevelOption, transportOption, sessionModeOption, mcpPathOption, formatOption);

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

        return await rootCommand.InvokeAsync(args);
    }

    private static LogLevel ParseLogLevel(string? logLevelText, string? environmentVariableName = null)
    {
        var resolvedLogLevelText = string.IsNullOrWhiteSpace(environmentVariableName)
            ? logLevelText
            : ResolveArgumentOrEnvironment(logLevelText, environmentVariableName!);

        if (string.IsNullOrWhiteSpace(resolvedLogLevelText))
        {
            return LogLevel.Information;
        }

        return resolvedLogLevelText.Trim().ToLowerInvariant() switch
        {
            "trace" => LogLevel.Trace,
            "debug" => LogLevel.Debug,
            "info" => LogLevel.Information,
            "information" => LogLevel.Information,
            "warn" => LogLevel.Warning,
            "warning" => LogLevel.Warning,
            "error" => LogLevel.Error,
            _ => LogLevel.Information
        };
    }

    private static string? ResolveArgumentOrEnvironment(string? argumentValue, string environmentVariableName, string? defaultValue = null)
    {
        if (!string.IsNullOrWhiteSpace(argumentValue))
        {
            return argumentValue;
        }

        var environmentValue = Environment.GetEnvironmentVariable(environmentVariableName);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue;
        }

        return defaultValue;
    }

    private static ResolvedSetting ResolveArgumentOrEnvironmentWithSource(string? argumentValue, string environmentVariableName, string? defaultValue = null)
    {
        if (!string.IsNullOrWhiteSpace(argumentValue))
        {
            return new ResolvedSetting(argumentValue, CliSource);
        }

        var environmentValue = Environment.GetEnvironmentVariable(environmentVariableName);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return new ResolvedSetting(environmentValue, EnvSource);
        }

        return new ResolvedSetting(defaultValue, DefaultSource);
    }

    private static string NormalizeFormat(string? format)
    {
        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            return "json";
        }

        return "text";
    }

    private static ResolvedSetting ResolveEffectiveLogLevel(string[] args, string? logLevelText)
    {
        if (HasOption(args, "--trace", "-t"))
        {
            return new ResolvedSetting(LogLevel.Trace.ToString(), CliSource);
        }

        if (HasOption(args, "--debug", "-d") || HasOption(args, "--verbose", "-v"))
        {
            return new ResolvedSetting(LogLevel.Debug.ToString(), CliSource);
        }

        var resolvedLogLevel = ResolveArgumentOrEnvironmentWithSource(logLevelText, LogLevelEnvVar);
        var parsedLogLevel = ParseLogLevel(resolvedLogLevel.Value);
        return new ResolvedSetting(parsedLogLevel.ToString(), resolvedLogLevel.Source);
    }

    private static bool HasOption(string[] args, string longName, string shortName)
    {
        return args.Any(arg =>
            string.Equals(arg, longName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, shortName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldPrintResolvedSettings(LogLevel logLevel)
    {
        return logLevel == LogLevel.Debug || logLevel == LogLevel.Trace;
    }

    private static async Task<ResolvedCommandSettings> ResolveCommandSettingsAsync(
        string[] args,
        string? configPath,
        string? logLevelText,
        string? transport,
        string? sessionMode,
        string? mcpPath)
    {
        var preferredConfigPath = ResolveArgumentOrEnvironmentWithSource(configPath, ConfigurationEnvVar);
        var resolvedConfigPath = await ResolveConfigurationPathWithSourceAsync(preferredConfigPath);
        var resolvedLogLevel = ResolveEffectiveLogLevel(args, logLevelText);
        var resolvedTransport = ResolveArgumentOrEnvironmentWithSource(transport, TransportEnvVar, "stdio");
        var normalizedTransport = new ResolvedSetting(NormalizeTransportValue(resolvedTransport.Value), resolvedTransport.Source);
        var resolvedSessionMode = ResolveArgumentOrEnvironmentWithSource(sessionMode, SessionModeEnvVar);
        var resolvedMcpPath = ResolveArgumentOrEnvironmentWithSource(mcpPath, McpPathEnvVar);

        return new ResolvedCommandSettings(
            resolvedConfigPath,
            resolvedConfigPath.Value ?? string.Empty,
            resolvedLogLevel,
            normalizedTransport,
            resolvedSessionMode,
            resolvedMcpPath);
    }

    internal static string NormalizeTransportValue(string? transport)
    {
        if (string.IsNullOrWhiteSpace(transport))
        {
            return "stdio";
        }

        return transport.Trim().ToLowerInvariant();
    }

    internal static TransportMode ResolveTransportMode(string? transport)
    {
        return NormalizeTransportValue(transport) switch
        {
            "stdio" => TransportMode.Stdio,
            "http" => TransportMode.Http,
            _ => TransportMode.Unsupported
        };
    }

    internal static string? NormalizeMcpPath(string? mcpPath)
    {
        if (string.IsNullOrWhiteSpace(mcpPath))
        {
            return null;
        }

        var normalized = mcpPath.Trim();
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        return normalized;
    }

    private static void PrintResolvedSettings(string commandName, ResolvedCommandSettings settings)
    {
        Console.Error.WriteLine($"{commandName} resolved settings:");
        Console.Error.WriteLine($"Configuration: {settings.FinalConfigPath} (source: {settings.ConfigPath.Source})");
        Console.Error.WriteLine($"Effective log level: {settings.LogLevel.Value} (source: {settings.LogLevel.Source})");
        Console.Error.WriteLine($"Effective transport: {settings.Transport.Value} (source: {settings.Transport.Source})");
        Console.Error.WriteLine($"Effective session mode: {settings.SessionMode.Value ?? "(not set)"} (source: {settings.SessionMode.Source})");
        Console.Error.WriteLine($"Effective MCP path: {settings.McpPath.Value ?? "(not set)"} (source: {settings.McpPath.Source})");
        Console.Error.WriteLine();
    }

    private static async Task RunListToolsAsync(LogLevel logLevel, string finalConfigPath, string format)
    {
        using var loggerFactory = CreateLoggerFactory(logLevel);
        var logger = loggerFactory.CreateLogger("ListTools");

        var config = LoadPowerShellConfiguration(finalConfigPath, logger);
        var tools = DiscoverTools(config, logger);

        if (format == "json")
        {
            var payload = new
            {
                configurationPath = finalConfigPath,
                toolCount = tools.Count,
                functionNames = config.FunctionNames,
                generatedAtUtc = DateTime.UtcNow
            };
            Console.WriteLine(JsonSerializer.Serialize(payload));
            return;
        }

        Console.WriteLine($"Configuration: {finalConfigPath}");
        Console.WriteLine($"Discovered tools: {tools.Count}");
        Console.WriteLine("Configured function names:");
        foreach (var functionName in config.FunctionNames)
        {
            Console.WriteLine($"- {functionName}");
        }
    }

    private static async Task RunValidateConfigAsync(LogLevel logLevel, string finalConfigPath, string format)
    {
        using var loggerFactory = CreateLoggerFactory(logLevel);
        var logger = loggerFactory.CreateLogger("ValidateConfig");

        var config = LoadPowerShellConfiguration(finalConfigPath, logger);
        var tools = DiscoverTools(config, logger);

        if (format == "json")
        {
            var payload = new
            {
                valid = true,
                configurationPath = finalConfigPath,
                functionCount = config.FunctionNames.Count,
                moduleCount = config.Modules.Count,
                toolCount = tools.Count
            };
            Console.WriteLine(JsonSerializer.Serialize(payload));
            return;
        }

        Console.WriteLine("Configuration validation succeeded.");
        Console.WriteLine($"Configuration: {finalConfigPath}");
        Console.WriteLine($"Functions: {config.FunctionNames.Count} | Modules: {config.Modules.Count} | Tools: {tools.Count}");
    }

    private static async Task RunDoctorAsync(ResolvedCommandSettings settings, string format)
    {
        var parsedLogLevel = ParseLogLevel(settings.LogLevel.Value);
        using var loggerFactory = CreateLoggerFactory(parsedLogLevel);
        var logger = loggerFactory.CreateLogger("Doctor");

        var config = LoadPowerShellConfiguration(settings.FinalConfigPath, logger);
        var tools = DiscoverTools(config, logger);
        var discoveredToolNames = GetDiscoveredToolNames(tools);
        var configuredFunctionStatus = BuildConfiguredFunctionStatus(config.FunctionNames, discoveredToolNames);
        var toolNames = discoveredToolNames.Count > 0
            ? discoveredToolNames
            : GetExpectedToolNames(configuredFunctionStatus, config.EnableDynamicReloadTools);
        var diagnostics = CollectPowerShellDiagnostics();
        var effectivePowerShellConfiguration = JsonNode.Parse(SerializeEffectivePowerShellConfiguration(config));

        var foundFunctions = configuredFunctionStatus.Where(f => f.Found).Select(f => f.FunctionName).ToList();
        var missingFunctions = configuredFunctionStatus.Where(f => !f.Found).Select(f => f.FunctionName).ToList();

        if (format == "json")
        {
            var payload = new
            {
                configurationPath = settings.FinalConfigPath,
                configurationPathSource = settings.ConfigPath.Source,
                effectiveLogLevel = settings.LogLevel.Value,
                effectiveLogLevelSource = settings.LogLevel.Source,
                effectiveTransport = settings.Transport.Value,
                effectiveTransportSource = settings.Transport.Source,
                effectiveSessionMode = settings.SessionMode.Value,
                effectiveSessionModeSource = settings.SessionMode.Source,
                effectiveMcpPath = settings.McpPath.Value,
                effectiveMcpPathSource = settings.McpPath.Source,
                effectivePowerShellConfiguration,
                toolCount = tools.Count,
                toolNames,
                configuredFunctionCount = configuredFunctionStatus.Count,
                configuredFunctionsFound = foundFunctions,
                configuredFunctionsMissing = missingFunctions,
                configuredFunctionStatus,
                powershellVersion = diagnostics.PowerShellVersion,
                modulePathEntries = diagnostics.ModulePathEntries,
                modulePaths = diagnostics.ModulePaths,
                generatedAtUtc = DateTime.UtcNow
            };
            Console.WriteLine(JsonSerializer.Serialize(payload));
            return;
        }

        Console.WriteLine("PoshMcp doctor");
        Console.WriteLine($"Configuration: {settings.FinalConfigPath} (source: {settings.ConfigPath.Source})");
        Console.WriteLine($"Effective log level: {settings.LogLevel.Value} (source: {settings.LogLevel.Source})");
        Console.WriteLine($"Effective transport: {settings.Transport.Value} (source: {settings.Transport.Source})");
        Console.WriteLine($"Effective session mode: {settings.SessionMode.Value ?? "(not set)"} (source: {settings.SessionMode.Source})");
        Console.WriteLine($"Effective MCP path: {settings.McpPath.Value ?? "(not set)"} (source: {settings.McpPath.Source})");
        Console.WriteLine("Effective PowerShell configuration:");
        Console.WriteLine(SerializeEffectivePowerShellConfiguration(config, writeIndented: true));
        Console.WriteLine($"Tools discovered: {tools.Count}");
        Console.WriteLine($"Configured functions found: {foundFunctions.Count}/{configuredFunctionStatus.Count}");
        if (configuredFunctionStatus.Count > 0)
        {
            Console.WriteLine("Configured function status:");
            foreach (var functionStatus in configuredFunctionStatus)
            {
                var statusText = functionStatus.Found ? "FOUND" : "MISSING";
                var detail = functionStatus.Found
                    ? $"matched tools: {string.Join(", ", functionStatus.MatchedToolNames)}"
                    : "configured, but no generated tool matched";
                Console.WriteLine($"- {functionStatus.FunctionName} -> {functionStatus.ExpectedToolName} [{statusText}] {detail}");
            }
        }
        if (missingFunctions.Count > 0)
        {
            Console.WriteLine("Missing configured functions:");
            foreach (var missing in missingFunctions)
            {
                Console.WriteLine($"- {missing}");
            }
        }
        if (toolNames.Count > 0)
        {
            Console.WriteLine("Tool names:");
            foreach (var toolName in toolNames)
            {
                Console.WriteLine($"- {toolName}");
            }
        }
        Console.WriteLine($"PowerShell version: {diagnostics.PowerShellVersion}");
        Console.WriteLine($"PSModulePath entries: {diagnostics.ModulePathEntries}");
        if (diagnostics.ModulePaths.Length > 0)
        {
            Console.WriteLine("PSModulePath values:");
            foreach (var modulePath in diagnostics.ModulePaths)
            {
                Console.WriteLine($"- {modulePath}");
            }
        }
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

    private sealed record ConfiguredFunctionStatus(
        string FunctionName,
        string ExpectedToolName,
        bool Found,
        List<string> MatchedToolNames);

    private sealed record ResolvedSetting(string? Value, string Source);

    private sealed record ResolvedCommandSettings(
        ResolvedSetting ConfigPath,
        string FinalConfigPath,
        ResolvedSetting LogLevel,
        ResolvedSetting Transport,
        ResolvedSetting SessionMode,
        ResolvedSetting McpPath);

    internal enum TransportMode
    {
        Stdio,
        Http,
        Unsupported
    }

    private static async Task<string> ResolveExplicitOrDefaultConfigPath(string? explicitConfigPath)
    {
        var preferredConfigPath = string.IsNullOrWhiteSpace(explicitConfigPath)
            ? new ResolvedSetting(null, DefaultSource)
            : new ResolvedSetting(explicitConfigPath, CliSource);

        var resolvedConfigPath = await ResolveConfigurationPathWithSourceAsync(preferredConfigPath);
        return resolvedConfigPath.Value ?? throw new InvalidOperationException("Resolved configuration path was empty.");
    }

    private static async Task<ResolvedSetting> ResolveConfigurationPathWithSourceAsync(ResolvedSetting preferredConfigPath)
    {
        if (!string.IsNullOrWhiteSpace(preferredConfigPath.Value))
        {
            var absoluteConfigPath = Path.GetFullPath(preferredConfigPath.Value);
            if (!File.Exists(absoluteConfigPath))
            {
                throw new FileNotFoundException($"Configuration file not found: {absoluteConfigPath}");
            }

            await UpgradeConfigWithMissingDefaultsAsync(absoluteConfigPath);
            return new ResolvedSetting(absoluteConfigPath, preferredConfigPath.Source);
        }

        var currentDirectoryConfigPath = Path.GetFullPath("appsettings.json");
        if (File.Exists(currentDirectoryConfigPath))
        {
            await UpgradeConfigWithMissingDefaultsAsync(currentDirectoryConfigPath);
            return new ResolvedSetting(currentDirectoryConfigPath, CwdSource);
        }

        var userConfigPath = GetUserConfigPath();
        if (File.Exists(userConfigPath))
        {
            await UpgradeConfigWithMissingDefaultsAsync(userConfigPath);
            return new ResolvedSetting(userConfigPath, UserSource);
        }

        await InstallEmbeddedDefaultConfigToUserLocationAsync(userConfigPath);
        return new ResolvedSetting(userConfigPath, EmbeddedDefaultSource);
    }

    private static string GetUserConfigPath()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appDataPath, "PoshMcp", "appsettings.json");
    }

    private static async Task InstallEmbeddedDefaultConfigToUserLocationAsync(string userConfigPath)
    {
        var directory = Path.GetDirectoryName(userConfigPath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var defaultConfigJson = LoadEmbeddedDefaultConfig();
        await File.WriteAllTextAsync(userConfigPath, defaultConfigJson);
    }

    private static async Task UpgradeConfigWithMissingDefaultsAsync(string configPath)
    {
        var defaultConfigJson = LoadEmbeddedDefaultConfig();
        var existingConfigJson = await File.ReadAllTextAsync(configPath);

        var parseOptions = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        var defaultRoot = JsonNode.Parse(defaultConfigJson, documentOptions: parseOptions)?.AsObject()
            ?? throw new InvalidOperationException("Embedded default configuration must be a JSON object.");
        var existingRoot = JsonNode.Parse(existingConfigJson, documentOptions: parseOptions)?.AsObject()
            ?? throw new InvalidOperationException($"Configuration file '{configPath}' must be a JSON object.");

        var changed = MergeMissingProperties(defaultRoot, existingRoot);
        if (!changed)
        {
            return;
        }

        var updatedConfigJson = existingRoot.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(configPath, updatedConfigJson + Environment.NewLine);
    }

    private static bool MergeMissingProperties(JsonObject defaultObject, JsonObject targetObject)
    {
        var changed = false;

        foreach (var defaultProperty in defaultObject)
        {
            if (!targetObject.TryGetPropertyValue(defaultProperty.Key, out var existingValue))
            {
                targetObject[defaultProperty.Key] = defaultProperty.Value?.DeepClone();
                changed = true;
                continue;
            }

            if (defaultProperty.Value is JsonObject defaultChildObject && existingValue is JsonObject existingChildObject)
            {
                changed |= MergeMissingProperties(defaultChildObject, existingChildObject);
            }
        }

        return changed;
    }

    private static string LoadEmbeddedDefaultConfig()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("default.appsettings.json", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            throw new InvalidOperationException("Embedded default configuration resource was not found.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Unable to open embedded configuration resource '{resourceName}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void RunPSModulePathCommand(LogLevel logLevel)
    {
        Console.Error.WriteLine("=== PowerShell MCP Server - PSModulePath Report ===");
        Console.Error.WriteLine();

        using var loggerFactory = CreateLoggerFactory(logLevel);
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
        var preferredConfigPath = File.Exists(configPath)
            ? new ResolvedSetting(configPath, CliSource)
            : new ResolvedSetting(null, DefaultSource);
        var resolvedConfigPath = await ResolveConfigurationPathWithSourceAsync(preferredConfigPath);
        return resolvedConfigPath.Value ?? throw new InvalidOperationException("Resolved configuration path was empty.");
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

    private static async Task RunMcpServerAsync(string[] args, LogLevel? overrideLogLevel = null, string? explicitConfigPath = null)
    {
        var builder = Host.CreateApplicationBuilder(args);
        ConfigureServerLogging(builder, overrideLogLevel);
        var finalConfigPath = await ConfigureServerConfiguration(builder, explicitConfigPath);
        var serviceProvider = builder.Services.BuildServiceProvider();
        var (logger, config) = ExtractLoggerAndConfiguration(serviceProvider, finalConfigPath);
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var tools = SetupMcpTools(loggerFactory, config, logger, finalConfigPath);
        ConfigureServerServices(builder, tools);
        await builder.Build().RunAsync();
    }

    private static async Task RunHttpTransportServerAsync(
        string[] args,
        LogLevel logLevel,
        string finalConfigPath,
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

        builder.Configuration.AddJsonFile(finalConfigPath, optional: false, reloadOnChange: true);
        builder.Services.Configure<PowerShellConfiguration>(
            builder.Configuration.GetSection("PowerShellConfiguration"));

        ConfigureJsonSerializerOptions(builder);
        ConfigureCorsForMcp(builder);
        RegisterHealthChecks(builder);

        ConfigureOpenTelemetryForHttp(builder);

        using var bootstrapLoggerFactory = CreateLoggerFactory(logLevel);
        var logger = bootstrapLoggerFactory.CreateLogger("PoshMcpHttpLogger");
        var config = LoadPowerShellConfiguration(finalConfigPath, logger);

        var sharedHttpContextAccessor = new HttpContextAccessor();
        var sharedRunspaceLogger = bootstrapLoggerFactory.CreateLogger<SessionAwarePowerShellRunspace>();
        var sharedSessionRunspace = new SessionAwarePowerShellRunspace(sharedHttpContextAccessor, sharedRunspaceLogger);

        builder.Services.AddSingleton<IHttpContextAccessor>(sharedHttpContextAccessor);
        builder.Services.AddSingleton<IPowerShellRunspace>(sharedSessionRunspace);

        logger.LogInformation("Using configuration file: {ConfigurationPath}", finalConfigPath);

        var tools = SetupHttpMcpTools(bootstrapLoggerFactory, config, logger, finalConfigPath, sharedSessionRunspace);
        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools(tools);

        RegisterCleanupServices(builder);

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

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = WriteHealthCheckResponseAsync
        });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = _ => true
        });

        var normalizedMcpPath = NormalizeMcpPath(mcpPath);
        if (string.IsNullOrWhiteSpace(normalizedMcpPath))
        {
            app.MapMcp();
        }
        else
        {
            app.MapMcp(normalizedMcpPath);
        }

        await app.RunAsync();
    }

    private static void ConfigureCorsForMcp(WebApplicationBuilder builder)
    {
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

    private static List<McpServerTool> SetupHttpMcpTools(
        ILoggerFactory loggerFactory,
        PowerShellConfiguration config,
        ILogger logger,
        string finalConfigPath,
        IPowerShellRunspace sessionAwareRunspace)
    {
        var runtimeCachingState = new RuntimeCachingState();
        PowerShellAssemblyGenerator.SetRuntimeCachingState(runtimeCachingState);
        PowerShellAssemblyGenerator.SetConfiguration(config);
        logger.LogInformation("RuntimeCachingState initialized and wired into PowerShellAssemblyGenerator");

        var toolFactory = new McpToolFactoryV2(sessionAwareRunspace);
        var tools = toolFactory.GetToolsList(config, logger);

        if (config.EnableDynamicReloadTools)
        {
            var reloadTools = CreateConfigurationReloadTools(loggerFactory, toolFactory, config, finalConfigPath);
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

    private static async Task<string> ConfigureServerConfiguration(HostApplicationBuilder builder, string? explicitConfigPath)
    {
        var finalConfigPath = await ResolveExplicitOrDefaultConfigPath(explicitConfigPath);
        builder.Configuration.AddJsonFile(finalConfigPath, optional: false, reloadOnChange: true);
        builder.Services.Configure<PowerShellConfiguration>(
            builder.Configuration.GetSection("PowerShellConfiguration"));
        return finalConfigPath;
    }

    private static (ILogger logger, PowerShellConfiguration config) ExtractLoggerAndConfiguration(IServiceProvider serviceProvider, string finalConfigPath)
    {
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PoshMcpLogger");
        var config = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PowerShellConfiguration>>().Value;
        logger.LogInformation($"Using configuration file: {finalConfigPath}");
        return (logger, config);
    }

    private static List<McpServerTool> SetupMcpTools(ILoggerFactory loggerFactory, PowerShellConfiguration config, ILogger logger, string finalConfigPath)
    {
        // Create RuntimeCachingState singleton and wire into assembly generator static state
        var runtimeCachingState = new RuntimeCachingState();
        PowerShellAssemblyGenerator.SetRuntimeCachingState(runtimeCachingState);
        PowerShellAssemblyGenerator.SetConfiguration(config);
        logger.LogInformation("RuntimeCachingState initialized and wired into PowerShellAssemblyGenerator");

        var toolFactory = new McpToolFactoryV2();
        var tools = toolFactory.GetToolsList(config, logger);

        if (config.EnableDynamicReloadTools)
        {
            var reloadTools = CreateConfigurationReloadTools(loggerFactory, toolFactory, config, finalConfigPath);
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

        return tools;
    }

    private static ConfigurationReloadTools CreateConfigurationReloadTools(ILoggerFactory loggerFactory, McpToolFactoryV2 toolFactory, PowerShellConfiguration config, string finalConfigPath)
    {
        var reloadServiceLogger = loggerFactory.CreateLogger<PowerShellConfigurationReloadService>();
        var reloadService = new PowerShellConfigurationReloadService(reloadServiceLogger, toolFactory, config, finalConfigPath);
        var reloadToolsLogger = loggerFactory.CreateLogger<ConfigurationReloadTools>();
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

    private static void ConfigureServerServices(HostApplicationBuilder builder, List<McpServerTool> tools)
    {
        ConfigureJsonSerializerOptions(builder);
        ConfigureOpenTelemetry(builder);
        RegisterMcpServerServices(builder, tools);
        RegisterCleanupServices(builder);
    }

    private static void ConfigureOpenTelemetry(HostApplicationBuilder builder)
    {
        // Register and configure OpenTelemetry metrics
        builder.Services.AddSingleton<McpMetrics>();

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metricsBuilder =>
            {
                metricsBuilder
                    .AddMeter(McpMetrics.MeterName)
                    .AddConsoleExporter();
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

    private static void RegisterCleanupServices(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IHostedService, PowerShellCleanupService>();
    }

}
