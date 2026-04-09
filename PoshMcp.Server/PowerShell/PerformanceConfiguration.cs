using System.Collections.Generic;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Performance tuning options that control pipeline construction and payload size.
/// Bound from the "PowerShellConfiguration:Performance" configuration section.
/// </summary>
public class PerformanceConfiguration
{
    /// <summary>
    /// When true, every command pipeline includes Tee-Object to cache results
    /// for replay by filter/sort/group/get-last-command-output tools.
    /// Default: false (caching disabled — saves ~50% memory).
    /// </summary>
    public bool EnableResultCaching { get; set; } = false;

    /// <summary>
    /// When true, inject Select-Object with the output type's DefaultDisplayPropertySet
    /// into each pipeline to reduce JSON payload size dramatically.
    /// Default: true.
    /// </summary>
    public bool UseDefaultDisplayProperties { get; set; } = true;
}

/// <summary>
/// Per-function overrides for performance and display settings.
/// Keyed by PowerShell function name in the FunctionOverrides dictionary.
/// </summary>
public class FunctionOverride
{
    /// <summary>
    /// Explicit list of property names to select. Takes priority over DefaultDisplayPropertySet.
    /// When null, the type's DefaultDisplayPropertySet is used (if UseDefaultDisplayProperties is true).
    /// </summary>
    public List<string>? DefaultProperties { get; set; }

    /// <summary>
    /// Override the global EnableResultCaching setting for this function.
    /// Null means fall through to the global setting.
    /// </summary>
    public bool? EnableResultCaching { get; set; }

    /// <summary>
    /// Override the global UseDefaultDisplayProperties setting for this function.
    /// Null means fall through to the global setting.
    /// </summary>
    public bool? UseDefaultDisplayProperties { get; set; }
}
