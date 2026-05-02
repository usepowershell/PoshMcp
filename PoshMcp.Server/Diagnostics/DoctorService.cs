using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PoshMcp.Server.Authentication;
using PoshMcp.Server.PowerShell;

namespace PoshMcp;

/// <summary>
/// Provides doctor command logic for diagnosing PoshMcp configuration and runtime state.
/// </summary>
internal static class DoctorService
{
    /// <summary>
    /// Main entry point for running the doctor command.
    /// </summary>
    internal static async Task RunDoctorAsync(
        ResolvedCommandSettings settings,
        string format,
        Func<PowerShellConfiguration, ILoggerFactory, ILogger, string, Task<List<McpServerTool>>> discoverToolsFunc)
    {
        var parsedLogLevel = SettingsResolver.ParseLogLevel(settings.LogLevel.Value);
        using var loggerFactory = LoggingHelpers.CreateLoggerFactory(parsedLogLevel);
        var logger = loggerFactory.CreateLogger("Doctor");

        var config = ConfigurationLoader.LoadPowerShellConfiguration(settings.FinalConfigPath, logger, settings.RuntimeMode.Value);
        var authRootConfig = ConfigurationLoader.BuildRootConfiguration(settings.FinalConfigPath, reloadOnChange: false);
        var authConfig = authRootConfig.GetSection("Authentication").Get<AuthenticationConfiguration>();
        var environmentVariables = CollectEnvironmentVariables();
        var tools = await discoverToolsFunc(config, loggerFactory, logger, settings.FinalConfigPath);
        var discoveredToolNames = ConfigurationHelpers.GetDiscoveredToolNames(tools);
        var configuredFunctionStatus = BuildConfiguredFunctionStatus(config.GetEffectiveCommandNames(), discoveredToolNames);
        var toolNames = discoveredToolNames.Count > 0
            ? discoveredToolNames
            : ConfigurationHelpers.GetExpectedToolNames(configuredFunctionStatus, s => s.MatchedToolNames, config.EnableDynamicReloadTools);
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

        var (warnings, configurationErrors) = BuildConfigurationWarnings(config, settings.FinalConfigPath);
        var (resourcesDiag, promptsDiag) = ConfigurationLoader.TryValidateResourcesAndPrompts(settings.FinalConfigPath);

        var report = DoctorReport.Build(
            configurationPath: ConfigurationHelpers.DescribeConfigurationPath(settings.FinalConfigPath),
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
            configurationErrors: configurationErrors,
            environmentVariables: environmentVariables,
            authConfig: authConfig);

        if (format == "json")
        {
            Console.WriteLine(BuildDoctorJson(report));
            return;
        }

        Console.WriteLine(DoctorTextRenderer.Render(report));
    }

    /// <summary>
    /// Builds a doctor report from current configuration and runtime state.
    /// </summary>
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
        var discoveredToolNames = ConfigurationHelpers.GetDiscoveredToolNames(tools);
        var configuredFunctionStatus = BuildConfiguredFunctionStatus(config.GetEffectiveCommandNames(), discoveredToolNames);
        var toolNames = discoveredToolNames.Count > 0
            ? discoveredToolNames
            : ConfigurationHelpers.GetExpectedToolNames(configuredFunctionStatus, s => s.MatchedToolNames, config.EnableDynamicReloadTools);
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
            configurationPath: ConfigurationHelpers.DescribeConfigurationPath(configurationPath),
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

    /// <summary>
    /// Builds a JSON-serialized doctor report.
    /// </summary>
    internal static string BuildDoctorJson(DoctorReport report)
    {
        return JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Serializes the effective PowerShell configuration to JSON.
    /// </summary>
    internal static string SerializeEffectivePowerShellConfiguration(PowerShellConfiguration config, bool writeIndented = false)
    {
        return JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = writeIndented,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        });
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
                var expectedToolName = ConfigurationHelpers.ToToolName(functionName);
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

}
