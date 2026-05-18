using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json.Serialization;
using PoshMcp.Server.Authentication;
using PoshMcp.Server.McpPrompts;
using PoshMcp.Server.McpResources;
using PoshMcp.Server.PowerShell;

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
    /// Out-of-process execution diagnostics. Only meaningful when
    /// <see cref="RuntimeSettingsSection.RuntimeMode"/> resolves to
    /// <c>OutOfProcess</c>; otherwise <see cref="OutOfProcessSection.Applicable"/>
    /// is <c>false</c> and the remaining fields hold defaults.
    /// </summary>
    [JsonPropertyName("outOfProcess")]
    public OutOfProcessSection OutOfProcess { get; init; } = new();

    /// <summary>
    /// Spec 011 (FR-263-1): per-module / per-pattern / per-tool import diagnostics.
    /// Surfaces validation results for tools brought in via
    /// <see cref="PowerShellConfiguration.Modules"/> and
    /// <see cref="PowerShellConfiguration.IncludePatterns"/> /
    /// <see cref="PowerShellConfiguration.ExcludePatterns"/>, which were
    /// previously invisible to the doctor report. Empty for configurations
    /// that use only <see cref="PowerShellConfiguration.CommandNames"/>.
    /// </summary>
    [JsonPropertyName("moduleImports")]
    public ModuleImportsSection ModuleImports { get; init; } = new();

    /// <summary>
    /// Spec 012 (OQ-4): noun-derived MCP resource diagnostics.
    /// Present when <see cref="PowerShellConfiguration.EnableNounResources"/> is <c>true</c>;
    /// reports registered noun resources, resource name conflicts, and suppressed nouns.
    /// When the feature is disabled, <see cref="NounResourcesSection.Enabled"/> is <c>false</c>
    /// and the remaining fields are empty defaults.
    /// </summary>
    [JsonPropertyName("nounResources")]
    public NounResourcesSection NounResources { get; init; } = new();

    /// <summary>
    /// Computes the overall health status from the diagnostic data.
    /// Returns <c>"errors"</c>, <c>"warnings"</c>, or <c>"healthy"</c>.
    /// </summary>
    public static string ComputeStatus(DoctorReport report)
    {
        // Spec 011 FR-263-5: any module-import error escalates to errors;
        // module/pattern warnings escalate to warnings. Existing rules preserved.
        var moduleImports = report.ModuleImports;
        var hasModuleErrors = false;
        var hasModuleWarnings = false;
        var hasPatternWarnings = false;
        for (var i = 0; i < moduleImports.Modules.Count; i++)
        {
            var status = moduleImports.Modules[i].Status;
            if (string.Equals(status, "error", StringComparison.Ordinal))
                hasModuleErrors = true;
            else if (string.Equals(status, "warning", StringComparison.Ordinal))
                hasModuleWarnings = true;
        }
        for (var i = 0; i < moduleImports.Patterns.Count; i++)
        {
            if (string.Equals(moduleImports.Patterns[i].Status, "warning", StringComparison.Ordinal))
                hasPatternWarnings = true;
        }

        if (report.FunctionsTools.ConfiguredFunctionsMissing > 0
            || report.McpDefinitions.Resources.Errors.Count > 0
            || report.McpDefinitions.Prompts.Errors.Count > 0
            || report.ConfigurationErrors.Count > 0
            || hasModuleErrors)
            return "errors";
        if (report.Warnings.Count > 0
            || report.McpDefinitions.Resources.Warnings.Count > 0
            || report.McpDefinitions.Prompts.Warnings.Count > 0
            || hasModuleWarnings
            || hasPatternWarnings)
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
        var roles = principal.FindAll("roles").Select(c => c.Value).ToList();

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

    /// <summary>
    /// Per-tool description-source diagnostics. Spec 010 FR-582 / FR-583 / SC-207:
    /// each entry reports the resolved precedence step that produced the MCP tool
    /// description and each parameter description, using the wire vocabulary
    /// <c>synopsis | description | syntax | name</c> for tools and
    /// <c>helpParameter | helpMessage | validateSet | typeFallback</c> for parameters.
    /// Empty when no <see cref="IToolDescriptionSourceTracker"/> was wired into
    /// discovery (e.g., legacy callers).
    /// </summary>
    [JsonPropertyName("tools")]
    public List<ToolDescriptionDoctorEntry> Tools { get; init; } = [];
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

/// <summary>
/// Diagnostics for the out-of-process PowerShell execution layer, populated
/// when the resolved <c>RuntimeMode</c> is <c>OutOfProcess</c>.
/// </summary>
public sealed record OutOfProcessSection
{
    /// <summary>
    /// True when the active runtime mode is <c>OutOfProcess</c> and the rest
    /// of the section is meaningful. False otherwise (defaults will be present
    /// but should not be displayed to the operator).
    /// </summary>
    [JsonPropertyName("applicable")]
    public bool Applicable { get; init; }

    /// <summary>Resolved subprocess host mode (<c>Single</c>, <c>Pool</c>, or <c>ProcessPool</c>).</summary>
    [JsonPropertyName("hostMode")]
    public string HostMode { get; init; } = string.Empty;

    /// <summary>How <see cref="HostMode"/> was resolved (<c>config (explicit)</c> or <c>config (default)</c>).</summary>
    [JsonPropertyName("hostModeSource")]
    public string HostModeSource { get; init; } = string.Empty;

    /// <summary>
    /// Configured runspace pool size for <c>Pool</c> mode (0 means
    /// auto-size to <c>min(ProcessorCount, 8)</c> at the host).
    /// </summary>
    [JsonPropertyName("runspacePoolSize")]
    public int RunspacePoolSize { get; init; }

    /// <summary>
    /// Effective runspace pool size including any clamping (e.g. negative or
    /// out-of-range inputs are normalized to 1; 0 is treated as auto).
    /// </summary>
    [JsonPropertyName("effectiveRunspacePoolSize")]
    public string EffectiveRunspacePoolSize { get; init; } = string.Empty;

    /// <summary>Configured process pool size for <c>ProcessPool</c> mode (number of subprocess hosts).</summary>
    [JsonPropertyName("processPoolSize")]
    public int ProcessPoolSize { get; init; }

    /// <summary>
    /// Effective process pool size after clamping (string for display parity with
    /// <see cref="EffectiveRunspacePoolSize"/>). Renders as <c>"n/a (Pool mode)"</c>
    /// when <see cref="HostMode"/> is not <c>ProcessPool</c>, since this knob is
    /// inert outside ProcessPool mode.
    /// </summary>
    [JsonPropertyName("effectiveProcessPoolSize")]
    public string EffectiveProcessPoolSize { get; init; } = string.Empty;

    /// <summary>Minimum healthy hosts required at startup for <c>ProcessPool</c> mode (configured value).</summary>
    [JsonPropertyName("minHealthyForStartup")]
    public int MinHealthyForStartup { get; init; }

    /// <summary>
    /// Minimum healthy hosts after clamping (capped at the effective pool size).
    /// Renders as <c>"n/a (Pool mode)"</c> when <see cref="HostMode"/> is not
    /// <c>ProcessPool</c>, since this knob is inert outside ProcessPool mode.
    /// </summary>
    [JsonPropertyName("effectiveMinHealthyForStartup")]
    public string EffectiveMinHealthyForStartup { get; init; } = string.Empty;

    /// <summary>Per-request timeout enforced by the host for outbound invokes (defaults to 30s).</summary>
    [JsonPropertyName("requestTimeoutSeconds")]
    public double RequestTimeoutSeconds { get; init; }

    /// <summary>Resolved on-disk path to the host script for the active mode.</summary>
    [JsonPropertyName("hostScriptPath")]
    public string? HostScriptPath { get; init; }

    /// <summary>True when the host script for the active mode resolved successfully.</summary>
    [JsonPropertyName("hostScriptResolved")]
    public bool HostScriptResolved { get; init; }

    /// <summary>If <see cref="HostScriptResolved"/> is false, a short error description.</summary>
    [JsonPropertyName("hostScriptError")]
    public string? HostScriptError { get; init; }
}

/// <summary>
/// Spec 011 (FR-263-1, FR-263-6): per-module / per-pattern / per-tool import
/// diagnostics. Renders as the <c>moduleImports</c> JSON property on
/// <see cref="DoctorReport"/>. The text renderer omits the section entirely
/// when all three arrays are empty.
/// </summary>
public sealed record ModuleImportsSection
{
    /// <summary>
    /// One entry per <see cref="PowerShellConfiguration.Modules"/> entry,
    /// reporting whether the module resolved on disk and how many tools it
    /// contributed to the discovered set.
    /// </summary>
    [JsonPropertyName("modules")]
    public List<ModuleImportEntry> Modules { get; init; } = [];

    /// <summary>
    /// One entry per pattern across
    /// <see cref="PowerShellConfiguration.IncludePatterns"/> and
    /// <see cref="PowerShellConfiguration.ExcludePatterns"/>, reporting which
    /// branch (filter / discovery / exclude) executed and how many commands
    /// it affected.
    /// </summary>
    [JsonPropertyName("patterns")]
    public List<PatternImportEntry> Patterns { get; init; } = [];

    /// <summary>
    /// Per-discovered-tool attribution back to the configuration source that
    /// produced it (FR-263-4). Empty when no tools were discovered.
    /// </summary>
    [JsonPropertyName("tools")]
    public List<ToolImportEntry> Tools { get; init; } = [];
}

/// <summary>
/// Spec 011 (FR-263-2): per-module diagnostic. <c>found / version / path</c>
/// come from a single <c>Get-Module -ListAvailable</c> probe per module
/// (<see cref="PoshMcp.Server.PowerShell.ModuleDiscovery.ProbeModules"/>);
/// <c>contributedToolCount / contributedToolNames</c> are derived from the
/// already-discovered tool set.
/// </summary>
public sealed record ModuleImportEntry
{
    /// <summary>The configured module name, preserved verbatim.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary><c>true</c> when <c>Get-Module -ListAvailable</c> resolved at least one record.</summary>
    [JsonPropertyName("found")]
    public bool Found { get; init; }

    /// <summary>Module version string, or <c>null</c> when not found.</summary>
    [JsonPropertyName("version")]
    public string? Version { get; init; }

    /// <summary><c>ModuleBase</c> directory, or <c>null</c> when not found.</summary>
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    /// <summary>Count of tools attributed to this module after dedup and pattern filtering.</summary>
    [JsonPropertyName("contributedToolCount")]
    public int ContributedToolCount { get; init; }

    /// <summary>MCP tool names attributed to this module.</summary>
    [JsonPropertyName("contributedToolNames")]
    public List<string> ContributedToolNames { get; init; } = [];

    /// <summary><c>"ok"</c>, <c>"warning"</c>, or <c>"error"</c>.</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = "ok";

    /// <summary>Sanitized diagnostic message when <see cref="Status"/> is not <c>"ok"</c>.</summary>
    [JsonPropertyName("diagnostic")]
    public string? Diagnostic { get; init; }
}

/// <summary>
/// Spec 011 (FR-263-3): per-pattern diagnostic. <c>role</c> records which
/// branch the pattern executed in (<c>filter</c> when other sources populated
/// the command set; <c>discovery</c> when the pattern itself drove discovery;
/// <c>exclude</c> for entries from <see cref="PowerShellConfiguration.ExcludePatterns"/>).
/// </summary>
public sealed record PatternImportEntry
{
    /// <summary>The configured pattern, preserved verbatim.</summary>
    [JsonPropertyName("pattern")]
    public string Pattern { get; init; } = string.Empty;

    /// <summary><c>"include"</c> or <c>"exclude"</c>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    /// <summary><c>"filter"</c>, <c>"discovery"</c>, or <c>"exclude"</c>.</summary>
    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    /// <summary>
    /// Count of commands the pattern affected. Meaning depends on <see cref="Role"/>:
    /// for <c>filter</c> and <c>discovery</c>, the number retained; for <c>exclude</c>,
    /// the number dropped.
    /// </summary>
    [JsonPropertyName("matchedCount")]
    public int MatchedCount { get; init; }

    /// <summary><c>"ok"</c> or <c>"warning"</c>; <c>"warning"</c> for dead patterns.</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = "ok";

    /// <summary>Diagnostic message when <see cref="Status"/> is <c>"warning"</c>.</summary>
    [JsonPropertyName("diagnostic")]
    public string? Diagnostic { get; init; }
}

/// <summary>
/// Spec 011 (FR-263-4): per-tool attribution back to its configuration source.
/// Source priority is <c>commandName</c> &gt; <c>module</c> &gt; <c>pattern</c>
/// (FR-263-9); <c>unknown</c> appears when the OOP wire format omits attribution
/// fields (FR-263-11) or when in-process attribution cannot be resolved.
/// </summary>
public sealed record ToolImportEntry
{
    /// <summary>The MCP tool name (lowercase / snake_case).</summary>
    [JsonPropertyName("toolName")]
    public string ToolName { get; init; } = string.Empty;

    /// <summary>The PowerShell command name (e.g. <c>Get-AzContext</c>).</summary>
    [JsonPropertyName("commandName")]
    public string CommandName { get; init; } = string.Empty;

    /// <summary><c>"commandName"</c>, <c>"module"</c>, <c>"pattern"</c>, or <c>"unknown"</c>.</summary>
    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// The configured string that produced this tool: the command name for
    /// <c>commandName</c>, the module name for <c>module</c>, the pattern
    /// string for <c>pattern</c>, or empty for <c>unknown</c>.
    /// </summary>
    [JsonPropertyName("sourceDetail")]
    public string SourceDetail { get; init; } = string.Empty;

    /// <summary><c>"exposed"</c>, <c>"filteredOut"</c>, or <c>"discoveryFailed"</c>.</summary>
    [JsonPropertyName("disposition")]
    public string Disposition { get; init; } = "exposed";

    /// <summary>Diagnostic message when <see cref="Disposition"/> is not <c>"exposed"</c>.</summary>
    [JsonPropertyName("diagnostic")]
    public string? Diagnostic { get; init; }
}

/// <summary>
/// Spec 012 (OQ-4): noun-derived MCP resource diagnostics. Rendered as the
/// <c>nounResources</c> JSON property on <see cref="DoctorReport"/>.
/// The text renderer omits the section when <see cref="Enabled"/> is <c>false</c>.
/// </summary>
public sealed record NounResourcesSection
{
    /// <summary>
    /// <c>true</c> when <see cref="PowerShellConfiguration.EnableNounResources"/> is
    /// <c>true</c>; <c>false</c> when the feature is disabled.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    /// <summary>
    /// Non-conflicted noun resources registered in the <see cref="NounRegistry"/>,
    /// excluding any suppressed via <see cref="PowerShellConfiguration.NounResourceOverrides"/>.
    /// </summary>
    [JsonPropertyName("registeredResources")]
    public List<NounResourceEntry> RegisteredResources { get; init; } = [];

    /// <summary>
    /// Resource name conflicts where two <c>Get-*</c> commands derive the same
    /// snake_case resource name. The winner claimed the resource; the loser is
    /// listed here with a diagnostic.
    /// </summary>
    [JsonPropertyName("conflicts")]
    public List<NounResourceConflictEntry> Conflicts { get; init; } = [];

    /// <summary>
    /// Nouns that would have produced a resource but were suppressed via
    /// <see cref="NounResourceOverride.Disabled"/> in
    /// <see cref="PowerShellConfiguration.NounResourceOverrides"/>.
    /// </summary>
    [JsonPropertyName("suppressedNouns")]
    public List<string> SuppressedNouns { get; init; } = [];
}

/// <summary>A single registered noun-derived MCP resource.</summary>
public sealed record NounResourceEntry
{
    /// <summary>PascalCase PowerShell noun (e.g. <c>BamiTenantUser</c>).</summary>
    [JsonPropertyName("noun")]
    public string Noun { get; init; } = string.Empty;

    /// <summary>Derived snake_case resource name (e.g. <c>bami_tenant_user</c>).</summary>
    [JsonPropertyName("resourceName")]
    public string ResourceName { get; init; } = string.Empty;

    /// <summary>Full MCP resource URI (e.g. <c>poshmcp://resources/bami_tenant_user</c>).</summary>
    [JsonPropertyName("uri")]
    public string Uri { get; init; } = string.Empty;

    /// <summary>Backing PowerShell command (e.g. <c>Get-BamiTenantUser</c>).</summary>
    [JsonPropertyName("canonicalGetCommand")]
    public string CanonicalGetCommand { get; init; } = string.Empty;
}

/// <summary>
/// A resource name conflict: two <c>Get-*</c> commands derived the same
/// snake_case resource name; first-writer won.
/// </summary>
public sealed record NounResourceConflictEntry
{
    /// <summary>The conflicted snake_case resource name.</summary>
    [JsonPropertyName("resourceName")]
    public string ResourceName { get; init; } = string.Empty;

    /// <summary>The command that won the resource name and produced the registered resource.</summary>
    [JsonPropertyName("winnerCommand")]
    public string WinnerCommand { get; init; } = string.Empty;

    /// <summary>The command that lost the conflict and does not produce a resource.</summary>
    [JsonPropertyName("loserCommand")]
    public string LoserCommand { get; init; } = string.Empty;
}
