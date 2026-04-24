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
/// Per-command overrides for performance and display settings.
/// Keyed by PowerShell command name in the CommandOverrides dictionary.
/// </summary>
public class FunctionOverride
{
    /// <summary>
    /// Explicit list of property names to select. Takes priority over DefaultDisplayPropertySet.
    /// When null, the type's DefaultDisplayPropertySet is used (if UseDefaultDisplayProperties is true).
    /// </summary>
    public List<string>? DefaultProperties { get; set; }

    /// <summary>
    /// Override the global EnableResultCaching setting for this command.
    /// Null means fall through to the global setting.
    /// </summary>
    public bool? EnableResultCaching { get; set; }

    /// <summary>
    /// Override the global UseDefaultDisplayProperties setting for this command.
    /// Null means fall through to the global setting.
    /// </summary>
    public bool? UseDefaultDisplayProperties { get; set; }

    /// <summary>
    /// Scopes required to invoke this command. Overrides the default policy when set.
    /// </summary>
    public List<string>? RequiredScopes { get; set; }

    /// <summary>
    /// Roles required to invoke this command. Overrides the default policy when set.
    /// </summary>
    public List<string>? RequiredRoles { get; set; }

    /// <summary>
    /// When true, this command can be invoked without authentication even if the global
    /// policy requires it.
    /// </summary>
    public bool? AllowAnonymous { get; set; }
}
