using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json.Serialization;
using PoshMcp.Server.Authentication;
using PoshMcp.Server.McpPrompts;
using PoshMcp.Server.McpResources;

namespace PoshMcp;

/// <summary>Structured snapshot of all diagnostic data produced by <c>poshmcp doctor</c>.</summary>
public sealed record DoctorReport
{
    /// <summary>Overall health summary.</summary>
    [JsonPropertyName("summary")]
    public DoctorSummary Summary { get; init; } = new();

    /// <summary>Resolved runtime settings with their resolution sources.</summary>
    [JsonPropertyName("runtimeSettings")]
    public RuntimeSettingsSection RuntimeSettings { get; init; } = new();

    /// <summary>Relevant environment variable values at diagnostic time.</summary>
    [JsonPropertyName("environmentVariables")]
    public Dictionary<string, string?> EnvironmentVariables { get; init; } = [];

    /// <summary>PowerShell runtime diagnostics.</summary>
    [JsonPropertyName("powerShell")]
    public PowerShellSection PowerShell { get; init; } = new();

    /// <summary>Configured function and discovered tool diagnostics.</summary>
    [JsonPropertyName("functionsTools")]
    public FunctionsToolsSection FunctionsTools { get; init; } = new();

    /// <summary>MCP resource and prompt definition diagnostics.</summary>
    [JsonPropertyName("mcpDefinitions")]
    public McpDefinitionsSection McpDefinitions { get; init; } = new();

    /// <summary>Authentication and authorization configuration diagnostics.</summary>
    [JsonPropertyName("authentication")]
    public AuthenticationSection Authentication { get; init; } = new();

    /// <summary>Current caller identity diagnostics.</summary>
    [JsonPropertyName("identity")]
    public IdentitySection Identity { get; init; } = new();

    /// <summary>Configuration errors collected across all sections.</summary>
    [JsonPropertyName("configurationErrors")]
    public List<string> ConfigurationErrors { get; init; } = [];

    /// <summary>Configuration warnings collected across all sections.</summary>
    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; init; } = [];

    /// <summary>
    /// Computes the overall health status from the diagnostic data.
    /// Returns <c>"errors"</c>, <c>"warnings"</c>, or <c>"healthy"</c>.
    /// </summary>
    public static string ComputeStatus(DoctorReport report)
    {
        if (report.FunctionsTools.ConfiguredFunctionsMissing > 0
            || report.McpDefinitions.Resources.Errors.Count > 0
            || report.McpDefinitions.Prompts.Errors.Count > 0
            || report.ConfigurationErrors.Count > 0)
            return "errors";
        if (report.Warnings.Count > 0
            || report.McpDefinitions.Resources.Warnings.Count > 0
            || report.McpDefinitions.Prompts.Warnings.Count > 0)
            return "warnings";
        return "healthy";
    }

    /// <summary>
    /// Builds a fully-populated <see cref="DoctorReport"/> from pre-computed diagnostic data.
    /// </summary>
    public static DoctorReport Build(
        string configurationPath,
        string configurationPathSource,
        string? effectiveLogLevel,
        string effectiveLogLevelSource,
        string? effectiveTransport,
        string effectiveTransportSource,
        string? effectiveSessionMode,
        string effectiveSessionModeSource,
        string? effectiveRuntimeMode,
        string effectiveRuntimeModeSource,
        string? effectiveMcpPath,
        string effectiveMcpPathSource,
        List<ConfiguredFunctionStatus> configuredFunctionStatus,
        List<string> toolNames,
        string powerShellVersion,
        int modulePathEntries,
        string[] modulePaths,
        string[] oopModulePaths,
        McpResourcesDiagnostics resourcesDiagnostics,
        McpPromptsDiagnostics promptsDiagnostics,
        List<string> warnings,
        List<string> configurationErrors,
        Dictionary<string, string?> environmentVariables,
        AuthenticationConfiguration? authConfig = null,
        ClaimsPrincipal? currentIdentity = null)
    {
        var foundFunctions = configuredFunctionStatus
            .Where(f => f.Found)
            .Select(f => f.FunctionName)
            .ToList();
        var missingFunctions = configuredFunctionStatus
            .Where(f => !f.Found)
            .Select(f => f.FunctionName)
            .ToList();

        var authentication = BuildAuthenticationSection(authConfig);
        var identity = BuildIdentitySection(currentIdentity);

        var report = new DoctorReport
        {
            RuntimeSettings = new RuntimeSettingsSection
            {
                ConfigurationPath = new ResolvedSetting(configurationPath, configurationPathSource),
                ConfigurationMode = ResolveConfigurationMode(configurationPath, configurationPathSource),
                Transport = new ResolvedSetting(effectiveTransport, effectiveTransportSource),
                LogLevel = new ResolvedSetting(effectiveLogLevel, effectiveLogLevelSource),
                SessionMode = new ResolvedSetting(effectiveSessionMode, effectiveSessionModeSource),
                RuntimeMode = new ResolvedSetting(effectiveRuntimeMode, effectiveRuntimeModeSource),
                McpPath = new ResolvedSetting(effectiveMcpPath, effectiveMcpPathSource),
            },
            EnvironmentVariables = environmentVariables,
            PowerShell = new PowerShellSection
            {
                Version = powerShellVersion,
                ModulePathEntries = modulePathEntries,
                ModulePaths = modulePaths,
                OopModulePathEntries = oopModulePaths.Length,
                OopModulePaths = oopModulePaths,
            },
            FunctionsTools = new FunctionsToolsSection
            {
                ConfiguredFunctionCount = configuredFunctionStatus.Count,
                ConfiguredFunctionsFound = foundFunctions.Count,
                ConfiguredFunctionsMissing = missingFunctions.Count,
                ToolCount = toolNames.Count,
                ToolNames = toolNames,
                ConfiguredFunctionStatus = configuredFunctionStatus,
            },
            McpDefinitions = new McpDefinitionsSection
            {
                Resources = new McpResourcesDiagSummary
                {
                    Configured = resourcesDiagnostics.Configured,
                    Valid = resourcesDiagnostics.Valid,
                    Errors = resourcesDiagnostics.Errors,
                    Warnings = resourcesDiagnostics.Warnings,
                },
                Prompts = new McpPromptsDiagSummary
                {
                    Configured = promptsDiagnostics.Configured,
                    Valid = promptsDiagnostics.Valid,
                    Errors = promptsDiagnostics.Errors,
                    Warnings = promptsDiagnostics.Warnings,
                },
            },
            Authentication = authentication,
            Identity = identity,
            ConfigurationErrors = configurationErrors,
            Warnings = warnings,
        };

        return report with
        {
            Summary = new DoctorSummary
            {
                Status = ComputeStatus(report),
                GeneratedAtUtc = DateTime.UtcNow,
                ConfigurationPath = configurationPath,
                FunctionCount = configuredFunctionStatus.Count,
                FoundCount = foundFunctions.Count,
                WarningCount = warnings.Count,
                Version = GetServerVersion(),
            },
        };
    }

    private static string GetServerVersion()
    {
        var raw = typeof(DoctorReport).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";
        var plusIdx = raw.IndexOf('+');
        return plusIdx >= 0 ? raw[..plusIdx] : raw;
    }

    private static AuthenticationSection BuildAuthenticationSection(AuthenticationConfiguration? authConfig)
    {
        if (authConfig is null)
            return new AuthenticationSection();

        var schemes = authConfig.Schemes
            .Select(kvp => new SchemeInfo
            {
                Name = kvp.Key,
                Type = kvp.Value.Type,
                HasAuthority = !string.IsNullOrWhiteSpace(kvp.Value.Authority),
                HasAudience = !string.IsNullOrWhiteSpace(kvp.Value.Audience),
                RequiresHttps = kvp.Value.RequireHttpsMetadata,
                KeyCount = kvp.Value.Keys.Count,
            })
            .ToList();

        return new AuthenticationSection
        {
            Enabled = authConfig.Enabled,
            DefaultScheme = authConfig.DefaultScheme,
            ConfiguredSchemes = schemes,
            RequireAuthentication = authConfig.DefaultPolicy.RequireAuthentication,
            RequiredScopes = authConfig.DefaultPolicy.RequiredScopes,
            RequiredRoles = authConfig.DefaultPolicy.RequiredRoles,
            ProtectedResourceUri = authConfig.ProtectedResource?.Resource,
            CorsEnabled = authConfig.Cors is not null && authConfig.Cors.AllowedOrigins.Count > 0,
            AllowedOrigins = authConfig.Cors?.AllowedOrigins ?? [],
        };
    }

    private static IdentitySection BuildIdentitySection(ClaimsPrincipal? principal)
    {
        if (principal is null)
            return new IdentitySection { Available = false };

        const string scopeClaim = "scp";
        var scopes = principal.FindAll(scopeClaim).Select(c => c.Value).ToList();
        var roles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

        return new IdentitySection
        {
            Available = true,
            IsAuthenticated = principal.Identity?.IsAuthenticated ?? false,
            AuthenticationScheme = principal.Identity?.AuthenticationType,
            Name = principal.Identity?.Name,
            Scopes = scopes,
            Roles = roles,
        };
    }

    private static ResolvedSetting ResolveConfigurationMode(string configurationPath, string configurationPathSource)
    {
        if (string.Equals(configurationPathSource, SettingsResolver.EnvSource, StringComparison.OrdinalIgnoreCase)
            && string.Equals(configurationPath, "(environment-only configuration)", StringComparison.Ordinal))
        {
            return new ResolvedSetting("environment-only", configurationPathSource);
        }

        return new ResolvedSetting("file-backed", configurationPathSource);
    }
}

/// <summary>Overall health summary for the doctor report.</summary>
public sealed record DoctorSummary
{
    /// <summary>Computed health status: <c>"healthy"</c>, <c>"warnings"</c>, or <c>"errors"</c>.</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    /// <summary>UTC timestamp when the report was generated.</summary>
    [JsonPropertyName("generatedAtUtc")]
    public DateTime GeneratedAtUtc { get; init; }

    /// <summary>Resolved path to the active configuration file.</summary>
    [JsonPropertyName("configurationPath")]
    public string ConfigurationPath { get; init; } = string.Empty;

    /// <summary>Total number of configured functions.</summary>
    [JsonPropertyName("functionCount")]
    public int FunctionCount { get; init; }

    /// <summary>Number of configured functions that were found.</summary>
    [JsonPropertyName("foundCount")]
    public int FoundCount { get; init; }

    /// <summary>Number of warnings collected across all sections.</summary>
    [JsonPropertyName("warningCount")]
    public int WarningCount { get; init; }

    /// <summary>Server version string.</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;
}

/// <summary>Resolved runtime settings with source annotations.</summary>
public sealed record RuntimeSettingsSection
{
    private static readonly ResolvedSetting Empty = new(null, string.Empty);

    /// <summary>Resolved configuration file path.</summary>
    [JsonPropertyName("configurationPath")]
    public ResolvedSetting ConfigurationPath { get; init; } = Empty;

    /// <summary>Resolved configuration mode (<c>file-backed</c> or <c>environment-only</c>).</summary>
    [JsonPropertyName("configurationMode")]
    public ResolvedSetting ConfigurationMode { get; init; } = Empty;

    /// <summary>Resolved transport mode.</summary>
    [JsonPropertyName("transport")]
    public ResolvedSetting Transport { get; init; } = Empty;

    /// <summary>Resolved log level.</summary>
    [JsonPropertyName("logLevel")]
    public ResolvedSetting LogLevel { get; init; } = Empty;

    /// <summary>Resolved session mode.</summary>
    [JsonPropertyName("sessionMode")]
    public ResolvedSetting SessionMode { get; init; } = Empty;

    /// <summary>Resolved runtime mode.</summary>
    [JsonPropertyName("runtimeMode")]
    public ResolvedSetting RuntimeMode { get; init; } = Empty;

    /// <summary>Resolved MCP path.</summary>
    [JsonPropertyName("mcpPath")]
    public ResolvedSetting McpPath { get; init; } = Empty;
}

/// <summary>PowerShell runtime diagnostics.</summary>
public sealed record PowerShellSection
{
    /// <summary>PowerShell engine version string.</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    /// <summary>Number of entries in the in-process <c>PSModulePath</c>.</summary>
    [JsonPropertyName("modulePathEntries")]
    public int ModulePathEntries { get; init; }

    /// <summary>In-process <c>PSModulePath</c> entries.</summary>
    [JsonPropertyName("modulePaths")]
    public string[] ModulePaths { get; init; } = [];

    /// <summary>Number of entries in the out-of-process <c>PSModulePath</c>.</summary>
    [JsonPropertyName("oopModulePathEntries")]
    public int OopModulePathEntries { get; init; }

    /// <summary>Out-of-process <c>PSModulePath</c> entries.</summary>
    [JsonPropertyName("oopModulePaths")]
    public string[] OopModulePaths { get; init; } = [];
}

/// <summary>Configured function and discovered tool diagnostics.</summary>
public sealed record FunctionsToolsSection
{
    /// <summary>Number of functions listed in configuration.</summary>
    [JsonPropertyName("configuredFunctionCount")]
    public int ConfiguredFunctionCount { get; init; }

    /// <summary>Number of configured functions that were found in the PowerShell session.</summary>
    [JsonPropertyName("configuredFunctionsFound")]
    public int ConfiguredFunctionsFound { get; init; }

    /// <summary>Number of configured functions that were not found in the PowerShell session.</summary>
    [JsonPropertyName("configuredFunctionsMissing")]
    public int ConfiguredFunctionsMissing { get; init; }

    /// <summary>Total number of discovered MCP tools.</summary>
    [JsonPropertyName("toolCount")]
    public int ToolCount { get; init; }

    /// <summary>Discovered MCP tool names.</summary>
    [JsonPropertyName("toolNames")]
    public List<string> ToolNames { get; init; } = [];

    /// <summary>Per-function resolution status details.</summary>
    [JsonPropertyName("configuredFunctionStatus")]
    public List<ConfiguredFunctionStatus> ConfiguredFunctionStatus { get; init; } = [];
}

/// <summary>MCP resource and prompt definition diagnostics.</summary>
public sealed record McpDefinitionsSection
{
    /// <summary>Resource definition diagnostics summary.</summary>
    [JsonPropertyName("resources")]
    public McpResourcesDiagSummary Resources { get; init; } = new();

    /// <summary>Prompt definition diagnostics summary.</summary>
    [JsonPropertyName("prompts")]
    public McpPromptsDiagSummary Prompts { get; init; } = new();
}

/// <summary>Validation summary for MCP resource definitions.</summary>
public sealed record McpResourcesDiagSummary
{
    /// <summary>Number of configured resources.</summary>
    [JsonPropertyName("configured")]
    public int Configured { get; init; }

    /// <summary>Number of valid resources.</summary>
    [JsonPropertyName("valid")]
    public int Valid { get; init; }

    /// <summary>Validation errors.</summary>
    [JsonPropertyName("errors")]
    public List<string> Errors { get; init; } = [];

    /// <summary>Validation warnings.</summary>
    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; init; } = [];
}

/// <summary>Validation summary for MCP prompt definitions.</summary>
public sealed record McpPromptsDiagSummary
{
    /// <summary>Number of configured prompts.</summary>
    [JsonPropertyName("configured")]
    public int Configured { get; init; }

    /// <summary>Number of valid prompts.</summary>
    [JsonPropertyName("valid")]
    public int Valid { get; init; }

    /// <summary>Validation errors.</summary>
    [JsonPropertyName("errors")]
    public List<string> Errors { get; init; } = [];

    /// <summary>Validation warnings.</summary>
    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; init; } = [];
}

/// <summary>Authentication and authorization configuration diagnostics.</summary>
public sealed record AuthenticationSection
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("defaultScheme")]
    public string DefaultScheme { get; init; } = string.Empty;

    [JsonPropertyName("configuredSchemes")]
    public List<SchemeInfo> ConfiguredSchemes { get; init; } = [];

    [JsonPropertyName("requireAuthentication")]
    public bool RequireAuthentication { get; init; }

    [JsonPropertyName("requiredScopes")]
    public List<string> RequiredScopes { get; init; } = [];

    [JsonPropertyName("requiredRoles")]
    public List<string> RequiredRoles { get; init; } = [];

    [JsonPropertyName("protectedResourceUri")]
    public string? ProtectedResourceUri { get; init; }

    [JsonPropertyName("corsEnabled")]
    public bool CorsEnabled { get; init; }

    [JsonPropertyName("allowedOrigins")]
    public List<string> AllowedOrigins { get; init; } = [];
}

/// <summary>Metadata about a configured authentication scheme (no secrets exposed).</summary>
public sealed record SchemeInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("hasAuthority")]
    public bool HasAuthority { get; init; }

    [JsonPropertyName("hasAudience")]
    public bool HasAudience { get; init; }

    [JsonPropertyName("requiresHttps")]
    public bool RequiresHttps { get; init; }

    /// <summary>Number of API keys configured (ApiKey scheme only). Never exposes actual key values.</summary>
    [JsonPropertyName("keyCount")]
    public int KeyCount { get; init; }
}

/// <summary>Current caller identity diagnostics (populated in MCP tool context; unavailable in CLI doctor).</summary>
public sealed record IdentitySection
{
    /// <summary>True when identity info was available (HTTP context present). False for CLI or stdio with no HTTP context.</summary>
    [JsonPropertyName("available")]
    public bool Available { get; init; }

    [JsonPropertyName("isAuthenticated")]
    public bool IsAuthenticated { get; init; }

    [JsonPropertyName("authenticationScheme")]
    public string? AuthenticationScheme { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("scopes")]
    public List<string> Scopes { get; init; } = [];

    [JsonPropertyName("roles")]
    public List<string> Roles { get; init; } = [];
}
