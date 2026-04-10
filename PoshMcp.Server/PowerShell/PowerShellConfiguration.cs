using System;
using System.Collections.Generic;
using System.Linq;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Configuration options for PowerShell command importing
/// </summary>
public class PowerShellConfiguration
{
    /// <summary>
    /// Specific function names to import
    /// </summary>
    public List<string> FunctionNames { get; set; } = new();

    /// <summary>
    /// Additional commands to import (alternative to FunctionNames)
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
    /// Whether to expose the doctor-style configuration troubleshooting MCP tool.
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
    /// Gets all function names from all configuration sources
    /// </summary>
    public List<string> GetAllFunctionNames()
    {
        var allNames = new List<string>(FunctionNames);

        allNames.AddRange(Commands);

        return allNames.Distinct().ToList();
    }
}
