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
        // Set up command line parsing
        var rootCommand = new RootCommand("PowerShell MCP Server - Provides access to PowerShell commands via Model Context Protocol");

        var evaluateToolsOption = new Option<bool>(
            aliases: new[] { "--evaluate-tools", "-e" },
            description: "Evaluate and list discovered PowerShell tools without starting the MCP server");

        var verboseOption = new Option<bool>(
            aliases: new[] { "--verbose", "-v" },
            description: "Enable verbose logging");

        var debugOption = new Option<bool>(

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
        }, configOption, logLevelOption, runtimeModeOption, formatOption);

        doctorCommand.SetHandler(async (configPath, logLevelText, transport, sessionMode, runtimeMode, mcpPath, format) =>
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
        }, configOption, logLevelOption, transportOption, sessionModeOption, runtimeModeOption, mcpPathOption, formatOption);

        createConfigCommand.SetHandler(async (force, format) =>
        {
            await CommandHandlers.RunCreateConfigAsync(force ? "true" : "false", format);
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

            await CommandHandlers.RunUpdateConfigAsync(
                args,
                configPath,
                logLevelText,
                format,
                addCommands,
                removeCommands,
                addModules,
                removeModules,
                addIncludePatterns,
                removeIncludePatterns,
                addExcludePatterns,
                removeExcludePatterns,
                enableDynamicReloadTools,
                enableConfigurationTroubleshootingTool,
                enableResultCaching,
                useDefaultDisplayProperties,
                setAuthEnabled,
                runtimeMode,
                nonInteractive);
        });

        // Handler for the psmodulepath command
        psModulePathCommand.SetHandler((verbose, debug, trace) =>
        {
            // Determine log level based on options
            LogLevel logLevel = LogLevel.Information;
            if (trace) logLevel = LogLevel.Trace;
            else if (debug) logLevel = LogLevel.Debug;
            else if (verbose) logLevel = LogLevel.Debug; // Verbose maps to Debug level

            CommandHandlers.RunPSModulePathCommand(logLevel);
        }, verboseOption, debugOption, traceOption);

        // Handler for the build command
        buildCommand.SetHandler((InvocationContext context) =>
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

            CommandHandlers.RunBuildCommand(modules, type, tag, dockerFile, sourceImage, sourceTag, generateDockerfile, dockerfileOutput, appSettings);
        });

        // Handler for the run command
        runCommand.SetHandler((mode, port, tag, config, volumes, interactive) =>
        {
            CommandHandlers.RunRunCommand(mode, port, tag, config, volumes, interactive);
        }, runModeOption, runPortOption, runTagOption, runConfigOption, runVolumeOption, runInteractiveOption);

        scaffoldCommand.SetHandler(async (projectPath, force, format) =>
        {
            await CommandHandlers.RunScaffoldCommandAsync(projectPath, force, format);
        }, scaffoldProjectPathOption, forceOption, formatOption);

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

        logger.LogInformation("Using configuration source: {ConfigurationPath}", DescribeConfigurationPath(finalConfigPath));

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

    internal static string DescribeConfigurationPath(string? configurationPath)
    {
        return string.IsNullOrWhiteSpace(configurationPath)
            ? "(environment-only configuration)"
            : configurationPath;
    }




<<<<<<< HEAD
        // Always register set-result-caching (not gated by EnableDynamicReloadTools)
        var setResultCachingTool = CreateSetResultCachingToolInstance(runtimeCachingState);
        tools.Add(setResultCachingTool);
        logger.LogInformation("Registered set-result-caching tool (always enabled)");

        AddConfigurationGuidanceToolToList(tools, config, finalConfigPath, "stdio", config.RuntimeMode.ToString(), null, loggerFactory);
        AddConfigurationTroubleshootingToolToList(tools, config, finalConfigPath, "stdio", null, config.RuntimeMode.ToString(), null, logger);

        return tools;
    }

    internal static void AddConfigurationGuidanceToolToList(
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

    internal static void AddConfigurationTroubleshootingToolToList(
        List<McpServerTool> tools,
        PowerShellConfiguration config,
        string configurationPath,
        string effectiveTransport,
        string? effectiveSessionMode,
        string? effectiveRuntimeMode,
        string? effectiveMcpPath,
        ILogger logger,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        if (!config.EnableConfigurationTroubleshootingTool)
        {
            return;
        }

        Func<System.Security.Claims.ClaimsPrincipal?>? identityProvider =
            httpContextAccessor is null ? null : () => httpContextAccessor.HttpContext?.User;

        tools.Add(CreateConfigurationTroubleshootingToolInstance(
            configurationPath,
            effectiveTransport,
            effectiveSessionMode,
            effectiveRuntimeMode,
            effectiveMcpPath,
            () => tools,
            logger,
            authConfig: null,
            identityProvider: identityProvider));
    }

    private static McpServerTool CreateConfigurationTroubleshootingToolInstance(
        string configurationPath,
        string effectiveTransport,
        string? effectiveSessionMode,
        string? effectiveRuntimeMode,
        string? effectiveMcpPath,
        Func<List<McpServerTool>> registeredToolsProvider,
        ILogger logger,
        AuthenticationConfiguration? authConfig = null,
        Func<System.Security.Claims.ClaimsPrincipal?>? identityProvider = null)
    {
        Func<CancellationToken, Task<string>> troubleshootingDelegate = cancellationToken =>
        {
            try
            {
                logger.LogInformation("Processing configuration troubleshooting request");

                var config = ConfigurationLoader.LoadPowerShellConfiguration(configurationPath, logger, effectiveRuntimeMode);
                var tools = registeredToolsProvider();
                var report = DoctorService.BuildDoctorReportFromConfig(
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
                    tools: tools,
                    authConfig: authConfig,
                    currentIdentity: identityProvider?.Invoke());

                return Task.FromResult(DoctorService.BuildDoctorJson(report));
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
            Description = "Returns doctor-style configuration diagnostics for the running server. Output includes runtime settings, environment variables, PowerShell info, configured functions, MCP definitions, authentication configuration, and caller identity (when available).",
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

    internal static McpToolFactoryV2 CreateToolFactory(PowerShellConfiguration config, ICommandExecutor? commandExecutor, IPowerShellRunspace? runspace = null)
    {
        if (config.RuntimeMode == RuntimeMode.OutOfProcess)
        {
            return commandExecutor is null
                ? throw new InvalidOperationException("Out-of-process runtime mode requires a started command executor.")
                : new McpToolFactoryV2(commandExecutor);
        }

        return runspace is null ? new McpToolFactoryV2() : new McpToolFactoryV2(runspace);
    }

    internal static async Task<OutOfProcessExecutorLease?> StartOutOfProcessExecutorIfNeededAsync(PowerShellConfiguration config, ILoggerFactory loggerFactory, ILogger logger, string? configFilePath = null)
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

    internal sealed class OutOfProcessExecutorLease : IAsyncDisposable
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
=======
>>>>>>> f75842d (refactor: extract MCP tool setup to McpToolSetupService.cs)

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
