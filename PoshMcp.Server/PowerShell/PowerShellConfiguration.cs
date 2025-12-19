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
    /// Path to a PowerShell initialization script file (.ps1) to run when PowerShell runspaces are created.
    /// Can be an absolute path or relative to the application directory.
    /// If not specified or file doesn't exist, uses the default initialization script.
    /// The script is loaded once at startup and cached for performance.
    /// </summary>
    public string? InitializationScriptPath { get; set; }

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
