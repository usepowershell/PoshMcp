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
using PoshMcp.Server.McpPrompts;
using PoshMcp.Server.McpResources;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.OutOfProcess;

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
        Func<PowerShellConfiguration, ILoggerFactory, ILogger, string, IToolMetadataSource?, IToolDescriptionSourceTracker?, Task<List<McpServerTool>>> discoverToolsFunc)
    {
        var report = await BuildDoctorReportForCliAsync(settings, discoverToolsFunc);

        if (format == "json")
        {
            Console.WriteLine(BuildDoctorJson(report));
            return;
        }

        Console.WriteLine(DoctorTextRenderer.Render(report));
    }

    /// <summary>
    /// Builds the doctor report used by the CLI command without throwing on
    /// configuration load or tool discovery failures.
    /// </summary>
    internal static async Task<DoctorReport> BuildDoctorReportForCliAsync(
        ResolvedCommandSettings settings,
        Func<PowerShellConfiguration, ILoggerFactory, ILogger, string, IToolMetadataSource?, IToolDescriptionSourceTracker?, Task<List<McpServerTool>>> discoverToolsFunc)
    {
        var parsedLogLevel = SettingsResolver.ParseLogLevel(settings.LogLevel.Value);
        using var loggerFactory = LoggingHelpers.CreateLoggerFactory(parsedLogLevel);
        var logger = loggerFactory.CreateLogger("Doctor");

        var configurationErrors = new List<string>();

        PowerShellConfiguration config;
        var configurationLoaded = true;
        try
        {
            config = ConfigurationLoader.LoadPowerShellConfiguration(settings.FinalConfigPath, logger, settings.RuntimeMode.Value);
        }
        catch (Exception ex)
        {
            configurationLoaded = false;
            configurationErrors.Add($"Failed to load PowerShell configuration: {ex.Message}");
            config = new PowerShellConfiguration();
        }

        AuthenticationConfiguration? authConfig = null;
        if (configurationLoaded)
        {
            try
            {
                var authRootConfig = ConfigurationLoader.BuildRootConfiguration(settings.FinalConfigPath, reloadOnChange: false);
                authConfig = authRootConfig.GetSection("Authentication").Get<AuthenticationConfiguration>();
            }
            catch (Exception ex)
            {
                configurationErrors.Add($"Failed to load authentication configuration: {ex.Message}");
            }
        }

        var environmentVariables = CollectEnvironmentVariables();

        List<McpServerTool> tools;
        // Spec 010 FR-582 / FR-583 / SC-207: capture the resolved description-source
        // step per command and per parameter using the production HelpAware resolver
        // (the same one the live server wires via DI), so the doctor report shows
        // exactly what an MCP client will see.
        var descriptionSourceTracker = new ToolDescriptionSourceTracker();
        var helpAwareSource = new HelpAwareToolMetadataSource();
        try
        {
            tools = await discoverToolsFunc(config, loggerFactory, logger, settings.FinalConfigPath, helpAwareSource, descriptionSourceTracker);
        }
        catch (Exception ex)
        {
            configurationErrors.Add($"Tool discovery failed: {ex.Message}");
            tools = [];
        }

        var discoveredToolNames = ConfigurationHelpers.GetDiscoveredToolNames(tools);
        var configuredFunctionStatus = BuildConfiguredFunctionStatus(config.GetEffectiveCommandNames(), discoveredToolNames);
        var toolNames = discoveredToolNames.Count > 0
            ? discoveredToolNames
            : ConfigurationHelpers.GetExpectedToolNames(configuredFunctionStatus, s => s.MatchedToolNames, config.EnableDynamicReloadTools);
        var diagnostics = TryCollectPowerShellDiagnostics(configurationErrors);
        var oopModulePaths = ResolveConfiguredModulePathsForOop(config, settings.FinalConfigPath);

        var missingFunctions = configuredFunctionStatus.Where(f => !f.Found).Select(f => f.FunctionName).ToList();
        if (missingFunctions.Count > 0)
        {
            var resolutionReasons = DiagnoseMissingCommands(missingFunctions, config);
            configuredFunctionStatus = configuredFunctionStatus
                .Select(s => s.Found ? s : s with { ResolutionReason = resolutionReasons.GetValueOrDefault(s.FunctionName) })
                .ToList();
        }

        var report = BuildDoctorReportFromConfig(
            configurationPath: settings.FinalConfigPath,
            configurationPathSource: settings.ConfigPath.Source,
            effectiveLogLevel: settings.LogLevel.Value,
            effectiveLogLevelSource: settings.LogLevel.Source,
            effectiveTransport: settings.Transport.Value ?? string.Empty,
            effectiveTransportSource: settings.Transport.Source,
            effectiveSessionMode: settings.SessionMode.Value,
            effectiveSessionModeSource: settings.SessionMode.Source,
            effectiveRuntimeMode: settings.RuntimeMode.Value,
            effectiveRuntimeModeSource: settings.RuntimeMode.Source,
            effectiveMcpPath: settings.McpPath.Value,
            effectiveMcpPathSource: settings.McpPath.Source,
            config: config,
            tools: tools,
            authConfig: authConfig,
            allowConfigurationFileAccess: configurationLoaded);

        report = report with
        {
            FunctionsTools = report.FunctionsTools with
            {
                ConfiguredFunctionStatus = configuredFunctionStatus,
                ToolNames = toolNames,
                ToolCount = toolNames.Count,
                ConfiguredFunctionCount = configuredFunctionStatus.Count,
                ConfiguredFunctionsFound = configuredFunctionStatus.Count(f => f.Found),
                ConfiguredFunctionsMissing = configuredFunctionStatus.Count(f => !f.Found),
                Tools = BuildToolDescriptionEntries(tools, descriptionSourceTracker),
            },
            PowerShell = report.PowerShell with
            {
                Version = diagnostics.PowerShellVersion,
                ModulePathEntries = diagnostics.ModulePathEntries,
                ModulePaths = diagnostics.ModulePaths,
                OopModulePathEntries = oopModulePaths.Length,
                OopModulePaths = oopModulePaths,
            },
            OutOfProcess = BuildOutOfProcessSection(config, settings.FinalConfigPath, loggerFactory),
            ConfigurationErrors = [.. report.ConfigurationErrors, .. configurationErrors],
        };

        report = report with
        {
            Summary = report.Summary with
            {
                Status = DoctorReport.ComputeStatus(report with
                {
                    ConfigurationErrors = report.ConfigurationErrors.Concat(configurationErrors).ToList()
                })
            }
        };

        return report;
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
        System.Security.Claims.ClaimsPrincipal? currentIdentity = null,
        bool allowConfigurationFileAccess = true)
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

        var diagnostics = TryCollectPowerShellDiagnostics(null);
        var oopModulePaths = ResolveConfiguredModulePathsForOop(config, configurationPath);
        var warnings = new List<string>();
        var configurationErrors = new List<string>();
        McpResourcesDiagnostics resourcesDiag = new(0, 0, [], []);
        McpPromptsDiagnostics promptsDiag = new(0, 0, [], []);

        if (allowConfigurationFileAccess)
        {
            var (warningItems, configurationErrorItems) = BuildConfigurationWarnings(config, configurationPath);
            warnings.AddRange(warningItems);
            configurationErrors.AddRange(configurationErrorItems);
            (resourcesDiag, promptsDiag) = ConfigurationLoader.TryValidateResourcesAndPrompts(configurationPath);
        }
        var environmentVariables = CollectEnvironmentVariables();

        if (authConfig is null)
        {
            try
            {
                var rootConfig = ConfigurationLoader.BuildRootConfiguration(configurationPath, reloadOnChange: false);
                authConfig = rootConfig.GetSection("Authentication").Get<AuthenticationConfiguration>();
            }
            catch
            {
                authConfig = null;
            }
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
            currentIdentity: currentIdentity)
            with
        {
            OutOfProcess = BuildOutOfProcessSection(config, configurationPath, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance),
        };
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

    /// <summary>
    /// Builds the out-of-process diagnostics section. Returns a non-applicable
    /// section when <see cref="PowerShellConfiguration.RuntimeMode"/> is not
    /// <see cref="RuntimeMode.OutOfProcess"/>; otherwise resolves the host
    /// script path (without launching a subprocess) and reports the effective
    /// pool sizing.
    /// </summary>
    internal static OutOfProcessSection BuildOutOfProcessSection(
        PowerShellConfiguration config,
        string? configurationPath,
        ILoggerFactory loggerFactory)
    {
        if (config.RuntimeMode != RuntimeMode.OutOfProcess)
        {
            return new OutOfProcessSection { Applicable = false };
        }

        // Detect explicit-vs-default for HostMode by re-reading the raw config.
        var hostModeSource = "config (default)";
        if (!string.IsNullOrWhiteSpace(configurationPath))
        {
            try
            {
                var rootConfig = ConfigurationLoader.BuildRootConfiguration(configurationPath, reloadOnChange: false);
                if (!string.IsNullOrWhiteSpace(rootConfig["PowerShellConfiguration:SubprocessHostMode"]))
                {
                    hostModeSource = "config (explicit)";
                }
            }
            catch
            {
                // Best-effort detection only — fall back to "default".
            }
        }

        // Effective sizing per host-mode.
        var effectiveRunspacePoolSize = config.SubprocessHostMode switch
        {
            SubprocessHostMode.Pool when config.SubprocessRunspacePoolSize <= 0 => "auto (min(ProcessorCount, 8))",
            SubprocessHostMode.Pool => config.SubprocessRunspacePoolSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => "n/a",
        };

        // ProcessPool-only sizing knobs. In other modes (e.g., Pool) these knobs are
        // inert, so render "n/a (Pool mode)" to avoid alarming operators who see
        // configured values of 4/1 next to effective values of 0. (Issue #261)
        string effectiveProcessPoolSize;
        string effectiveMinHealthy;
        if (config.SubprocessHostMode == SubprocessHostMode.ProcessPool)
        {
            var processPoolSize = config.SubprocessPoolSize > 0 ? config.SubprocessPoolSize : 4;
            var minHealthy = Math.Max(1, Math.Min(
                config.SubprocessMinHealthyForStartup > 0 ? config.SubprocessMinHealthyForStartup : 1,
                processPoolSize));
            effectiveProcessPoolSize = processPoolSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
            effectiveMinHealthy = minHealthy.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            effectiveProcessPoolSize = "n/a (Pool mode)";
            effectiveMinHealthy = "n/a (Pool mode)";
        }

        // Best-effort host script resolution. Mirrors the executor's resolution
        // path without starting the subprocess. Failures are reported, not thrown.
        string? hostScriptPath = null;
        var hostScriptResolved = false;
        string? hostScriptError = null;
        try
        {
            var resolverLogger = loggerFactory.CreateLogger<OutOfProcessCommandExecutor>();
            var resolver = new OutOfProcessCommandExecutor(
                resolverLogger,
                requestTimeout: null,
                hostMode: config.SubprocessHostMode,
                runspacePoolSize: config.SubprocessRunspacePoolSize);
            try
            {
                hostScriptPath = resolver.ResolveHostScriptPathAsync().GetAwaiter().GetResult();
                hostScriptResolved = !string.IsNullOrWhiteSpace(hostScriptPath) && File.Exists(hostScriptPath);
                if (!hostScriptResolved)
                {
                    hostScriptError = "Resolved path does not exist on disk.";
                }
            }
            finally
            {
                resolver.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            hostScriptError = ex.Message;
        }

        return new OutOfProcessSection
        {
            Applicable = true,
            HostMode = config.SubprocessHostMode.ToString(),
            HostModeSource = hostModeSource,
            RunspacePoolSize = config.SubprocessRunspacePoolSize,
            EffectiveRunspacePoolSize = effectiveRunspacePoolSize,
            ProcessPoolSize = config.SubprocessPoolSize,
            EffectiveProcessPoolSize = effectiveProcessPoolSize,
            MinHealthyForStartup = config.SubprocessMinHealthyForStartup,
            EffectiveMinHealthyForStartup = effectiveMinHealthy,
            // Per-request timeout is not yet a config knob — OutOfProcessHost
            // hard-codes the 30-second default. Surfacing it here makes the
            // contract explicit for operators and signposts the eventual knob.
            RequestTimeoutSeconds = 30.0,
            HostScriptPath = hostScriptPath,
            HostScriptResolved = hostScriptResolved,
            HostScriptError = hostScriptError,
        };
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
        IConfigurationRoot? configuration = null;
        try
        {
            configuration = ConfigurationLoader.BuildRootConfiguration(configPath, reloadOnChange: false);
        }
        catch (Exception ex)
        {
            errors.Add($"Unable to read configuration for diagnostics: {ex.Message}");
            return (warnings, errors);
        }

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

        // Out-of-process subprocess clamp warnings (only meaningful when OutOfProcess).
        if (config.RuntimeMode == RuntimeMode.OutOfProcess)
        {
            if (config.SubprocessHostMode == SubprocessHostMode.Pool && config.SubprocessRunspacePoolSize < 0)
            {
                warnings.Add($"SubprocessRunspacePoolSize is {config.SubprocessRunspacePoolSize} (negative). The Pool host will treat this as 0 (auto-size to min(ProcessorCount, 8)).");
            }

            if (config.SubprocessHostMode == SubprocessHostMode.ProcessPool)
            {
                if (config.SubprocessPoolSize < 1)
                {
                    warnings.Add($"SubprocessPoolSize is {config.SubprocessPoolSize}. ProcessPool will fall back to the default size of 4.");
                }

                if (config.SubprocessMinHealthyForStartup < 1)
                {
                    warnings.Add($"SubprocessMinHealthyForStartup is {config.SubprocessMinHealthyForStartup}. ProcessPool will clamp this to 1.");
                }
                else if (config.SubprocessPoolSize > 0 && config.SubprocessMinHealthyForStartup > config.SubprocessPoolSize)
                {
                    warnings.Add($"SubprocessMinHealthyForStartup ({config.SubprocessMinHealthyForStartup}) exceeds SubprocessPoolSize ({config.SubprocessPoolSize}); it will be clamped to the pool size.");
                }
            }
        }

        return (warnings, errors);
    }

    /// <summary>
    /// Spec 010 FR-582 / FR-583 / SC-207: builds per-tool description-source entries
    /// for the doctor JSON from the captured tracker. One entry per resolved command
    /// (FR-501: tool description text is per-command, not per-parameter-set), with a
    /// nested entry per parameter (FR-511: same parameter resolves to one source
    /// across parameter sets). Returns an empty list when the tracker is null or
    /// holds no recorded sources.
    /// </summary>
    internal static List<ToolDescriptionDoctorEntry> BuildToolDescriptionEntries(
        IReadOnlyList<McpServerTool> tools,
        IToolDescriptionSourceTracker? tracker)
    {
        if (tracker is null)
        {
            return [];
        }

        var toolSources = tracker.ToolSources;
        var parameterSources = tracker.ParameterSources;
        if (toolSources.Count == 0 && parameterSources.Count == 0)
        {
            return [];
        }

        // Union of command names that appear in either map so we never drop a recorded
        // command that has parameters but no tool source (or vice versa).
        var commandNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in toolSources.Keys)
        {
            commandNames.Add(key);
        }
        foreach (var key in parameterSources.Keys)
        {
            commandNames.Add(key);
        }

        var entries = new List<ToolDescriptionDoctorEntry>(commandNames.Count);
        foreach (var commandName in commandNames)
        {
            ToolDescriptionSource? toolSource = null;
            if (toolSources.TryGetValue(commandName, out var ts))
            {
                toolSource = ts;
            }

            var paramEntries = new List<ParameterDescriptionDoctorEntry>();
            if (parameterSources.TryGetValue(commandName, out var perParam))
            {
                foreach (var kvp in perParam.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                {
                    paramEntries.Add(new ParameterDescriptionDoctorEntry
                    {
                        Name = kvp.Key,
                        DescriptionSource = kvp.Value,
                    });
                }
            }

            entries.Add(new ToolDescriptionDoctorEntry
            {
                Name = commandName,
                CommandName = commandName,
                DescriptionSource = toolSource,
                Parameters = paramEntries,
            });
        }

        return entries;
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

    private static (string PowerShellVersion, int ModulePathEntries, string[] ModulePaths) TryCollectPowerShellDiagnostics(List<string>? configurationErrors)
    {
        try
        {
            return CollectPowerShellDiagnostics();
        }
        catch (Exception ex)
        {
            configurationErrors?.Add($"PowerShell diagnostics unavailable: {ex.Message}");
            return ("unavailable", 0, Array.Empty<string>());
        }
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
