using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
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

    /// <summary>Configuration warnings collected across all sections.</summary>
    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; init; } = [];

    /// <summary>
    /// Computes the overall health status from the diagnostic data.
    /// Returns <c>"errors"</c>, <c>"warnings"</c>, or <c>"healthy"</c>.
    /// </summary>
    public static string ComputeStatus(DoctorReport report)
    {
        if (report.FunctionsTools.ConfiguredFunctionsMissing.Count > 0
            || report.McpDefinitions.Resources.Errors.Count > 0
            || report.McpDefinitions.Prompts.Errors.Count > 0)
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
        List<Program.ConfiguredFunctionStatus> configuredFunctionStatus,
        List<string> toolNames,
        string powerShellVersion,
        int modulePathEntries,
        string[] modulePaths,
        string[] oopModulePaths,
        McpResourcesDiagnostics resourcesDiagnostics,
        McpPromptsDiagnostics promptsDiagnostics,
        List<string> warnings,
        Dictionary<string, string?> environmentVariables)
    {
        var foundFunctions = configuredFunctionStatus
            .Where(f => f.Found)
            .Select(f => f.FunctionName)
            .ToList();
        var missingFunctions = configuredFunctionStatus
            .Where(f => !f.Found)
            .Select(f => f.FunctionName)
            .ToList();

        var report = new DoctorReport
        {
            RuntimeSettings = new RuntimeSettingsSection
            {
                ConfigurationPath = new ResolvedSetting(configurationPath, configurationPathSource),
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
                ConfiguredFunctionsFound = foundFunctions,
                ConfiguredFunctionsMissing = missingFunctions,
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
            },
        };
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
}

/// <summary>Resolved runtime settings with source annotations.</summary>
public sealed record RuntimeSettingsSection
{
    private static readonly ResolvedSetting Empty = new(null, string.Empty);

    /// <summary>Resolved configuration file path.</summary>
    [JsonPropertyName("configurationPath")]
    public ResolvedSetting ConfigurationPath { get; init; } = Empty;

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

    /// <summary>Names of configured functions that were found in the PowerShell session.</summary>
    [JsonPropertyName("configuredFunctionsFound")]
    public List<string> ConfiguredFunctionsFound { get; init; } = [];

    /// <summary>Names of configured functions that were not found in the PowerShell session.</summary>
    [JsonPropertyName("configuredFunctionsMissing")]
    public List<string> ConfiguredFunctionsMissing { get; init; } = [];

    /// <summary>Total number of discovered MCP tools.</summary>
    [JsonPropertyName("toolCount")]
    public int ToolCount { get; init; }

    /// <summary>Discovered MCP tool names.</summary>
    [JsonPropertyName("toolNames")]
    public List<string> ToolNames { get; init; } = [];

    /// <summary>Per-function resolution status details.</summary>
    [JsonPropertyName("configuredFunctionStatus")]
    public List<Program.ConfiguredFunctionStatus> ConfiguredFunctionStatus { get; init; } = [];
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
