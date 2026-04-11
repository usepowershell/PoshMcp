using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Per-function overrides for performance and display settings,
    /// keyed by PowerShell function name (e.g. "Get-Process").
    /// </summary>
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
    /// Gets all function names from all configuration sources (deprecated — use GetEffectiveCommandNames())
    /// </summary>
    public List<string> GetAllFunctionNames() => GetEffectiveCommandNames();
}
