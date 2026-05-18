using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using PoshMcp.Server.McpResources;
using PoshMcp.Server.PowerShell.OutOfProcess;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Configuration options for PowerShell command importing
/// </summary>
public class PowerShellConfiguration
{
    /// <summary>
    /// Selects whether commands execute in-process or via the persistent PowerShell subprocess host.
    /// </summary>
    public RuntimeMode RuntimeMode { get; set; } = RuntimeMode.InProcess;

    /// <summary>
    /// Selects which OOP host topology is launched when <see cref="RuntimeMode"/> is
    /// <see cref="RuntimeMode.OutOfProcess"/>. <see cref="SubprocessHostMode.Pool"/> (default)
    /// launches a runspace-pool host (<c>oop-host-pool.ps1</c>) that runs invokes
    /// concurrently in one subprocess and is the recommended setting for typical
    /// concurrent MCP workloads (see <c>specs/004-out-of-process-execution/benchmark-findings.md</c>).
    /// <see cref="SubprocessHostMode.Single"/> launches the legacy single-runspace
    /// host (<c>oop-host.ps1</c>) and remains supported for callers that need the
    /// historical serialized behavior or are bisecting a regression.
    /// <see cref="SubprocessHostMode.ProcessPool"/> launches a pool of N independent
    /// single-runspace subprocess hosts, leased per request, for trust-boundary or
    /// tail-latency-sensitive workloads.
    /// </summary>
    public SubprocessHostMode SubprocessHostMode { get; set; } = SubprocessHostMode.Pool;

    /// <summary>
    /// Maximum number of runspaces in the pool when <see cref="SubprocessHostMode"/> is
    /// <see cref="SubprocessHostMode.Pool"/>. When unset or non-positive, the pool host
    /// defaults to <see cref="System.Environment.ProcessorCount"/> capped at 8.
    /// Ignored in <see cref="SubprocessHostMode.Single"/> and
    /// <see cref="SubprocessHostMode.ProcessPool"/> modes.
    /// </summary>
    public int SubprocessRunspacePoolSize { get; set; } = 0;

    /// <summary>
    /// When <see cref="SubprocessHostMode"/> is <see cref="SubprocessHostMode.ProcessPool"/>,
    /// the number of independent pwsh subprocess hosts to launch and lease from. Default: 4.
    /// </summary>
    public int SubprocessPoolSize { get; set; } = 4;

    /// <summary>
    /// When <see cref="SubprocessHostMode"/> is <see cref="SubprocessHostMode.ProcessPool"/>,
    /// the minimum number of healthy hosts required for the pool to start successfully.
    /// The first host always fails fast; hosts 2..N retry with backoff and the pool tolerates
    /// degraded startup as long as at least this many hosts come up healthy. Default: 1.
    /// </summary>
    public int SubprocessMinHealthyForStartup { get; set; } = 1;

    /// <summary>
    /// Specific command names to expose as MCP tools.
    /// This is the preferred property; use instead of FunctionNames.
    /// </summary>
    public List<string> CommandNames { get; set; } = new();

    /// <summary>
    /// Specific function names to import (deprecated — use CommandNames instead)
    /// </summary>
    public List<string> FunctionNames { get; set; } = new();

    /// <summary>
    /// Additional commands to import (alternative to CommandNames)
    /// </summary>
    public List<string> Commands { get; set; } = new();

    /// <summary>
    /// Modules to import all commands from
    /// </summary>
    public List<string> Modules { get; set; } = new();

    /// <summary>
    /// Patterns to exclude from import (supports wildcards)
    /// </summary>
    public List<string> ExcludePatterns { get; set; } = new();

    /// <summary>
    /// Patterns to include in import (supports wildcards)
    /// </summary>
    public List<string> IncludePatterns { get; set; } = new();

    /// <summary>
    /// Whether to enable dynamic reload tools (reload-configuration-from-file, update-configuration, get-configuration-status)
    /// </summary>
    public bool EnableDynamicReloadTools { get; set; } = false;

    /// <summary>
    /// Whether to expose the configuration troubleshooting tool.
    /// </summary>
    public bool EnableConfigurationTroubleshootingTool { get; set; } = false;

    /// <summary>
    /// Environment customization settings (startup scripts, module installation, etc.)
    /// </summary>
    public EnvironmentConfiguration Environment { get; set; } = new();

    /// <summary>
    /// Performance tuning (result caching, property filtering).
    /// </summary>
    public PerformanceConfiguration Performance { get; set; } = new();

    /// <summary>
    /// Per-command overrides for performance and display settings,
    /// keyed by PowerShell command name (e.g. "Get-Process").
    /// </summary>
    public Dictionary<string, FunctionOverride> CommandOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Legacy per-function overrides key, bound from FunctionOverrides.
    /// Use CommandOverrides for new configuration.
    /// </summary>
    [ConfigurationKeyName("FunctionOverrides")]
    [JsonIgnore]
    public Dictionary<string, FunctionOverride> FunctionOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the effective command names from all configuration sources.
    /// Prefers CommandNames over FunctionNames when both are present.
    /// </summary>
    public List<string> GetEffectiveCommandNames()
    {
        // If CommandNames is populated, it takes precedence over FunctionNames
        var primarySource = CommandNames.Count > 0 ? CommandNames : FunctionNames;
        var allNames = new List<string>(primarySource);
        allNames.AddRange(Commands);
        return allNames.Distinct().ToList();
    }

    /// <summary>
    /// Whether the deprecated FunctionNames property has values.
    /// </summary>
    public bool HasLegacyFunctionNames => FunctionNames.Count > 0;

    /// <summary>
    /// Whether both CommandNames and FunctionNames have values (config conflict).
    /// </summary>
    public bool HasBothCommandAndFunctionNames => CommandNames.Count > 0 && FunctionNames.Count > 0;

    /// <summary>
    /// When true, automatically derives MCP resources from the nouns in configured command names
    /// and augments tool results with a resourceLinkBlock. Default: false.
    /// </summary>
    public bool EnableNounResources { get; set; } = false;

    /// <summary>
    /// Per-noun overrides for noun-derived resource configuration. Keyed by default resource name
    /// (snake_case derived from the noun). Any noun not present uses default derivation.
    /// </summary>
    public Dictionary<string, NounResourceOverride> NounResourceOverrides { get; set; } = new();

    /// <summary>
    /// Gets all function names from all configuration sources (deprecated — use GetEffectiveCommandNames())
    /// </summary>
    public List<string> GetAllFunctionNames() => GetEffectiveCommandNames();

    /// <summary>
    /// Gets effective command overrides, merging legacy FunctionOverrides and CommandOverrides.
    /// CommandOverrides entries take precedence when both keys define the same command.
    /// </summary>
    public Dictionary<string, FunctionOverride> GetEffectiveCommandOverrides()
    {
        var merged = new Dictionary<string, FunctionOverride>(FunctionOverrides, StringComparer.OrdinalIgnoreCase);
        foreach (var item in CommandOverrides)
        {
            merged[item.Key] = item.Value;
        }

        return merged;
    }

    /// <summary>
    /// Resolve a command override by name from CommandOverrides first, then legacy FunctionOverrides.
    /// </summary>
    public bool TryGetCommandOverride(string commandName, out FunctionOverride? commandOverride)
    {
        if (CommandOverrides.TryGetValue(commandName, out var overrideFromCommand))
        {
            commandOverride = overrideFromCommand;
            return true;
        }

        if (FunctionOverrides.TryGetValue(commandName, out var overrideFromLegacy))
        {
            commandOverride = overrideFromLegacy;
            return true;
        }

        commandOverride = null;
        return false;
    }
}
