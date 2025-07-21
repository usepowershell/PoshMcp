using System.Collections.Generic;

namespace PoshMcp.PowerShell;

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
}
