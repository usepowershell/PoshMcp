using System.Collections.Generic;

namespace PoshMcp.Server.PowerShell.OutOfProcess;

/// <summary>
/// Schema describing a single PowerShell command discovered in the remote
/// pwsh subprocess, including its parameters and their types.
/// </summary>
public class RemoteToolSchema
{
    /// <summary>
    /// The full command name (e.g., "Get-AzContext").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable description for the command. Populated by the OOP host
    /// from <c>Get-Help</c>'s <c>.Synopsis</c> field (trimmed, and only when
    /// it differs from the command name); otherwise the empty string. The
    /// long <c>Description</c> body and parameter set syntax are NOT read.
    /// When empty, downstream tool schema generation falls back to the bare
    /// command name.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The parameter set name this schema represents
    /// (null or "__AllParameterSets" for the default set).
    /// </summary>
    public string? ParameterSetName { get; set; }

    /// <summary>
    /// Parameters for this command/parameter-set combination.
    /// </summary>
    public List<RemoteParameterSchema> Parameters { get; set; } = new();
}

/// <summary>
/// Schema for a single parameter of a remote command.
/// </summary>
public class RemoteParameterSchema
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The .NET type name as a string (e.g., "System.String", "System.Int32",
    /// "System.Management.Automation.SwitchParameter").
    /// We use strings because the actual types may not be loadable in the
    /// server process.
    /// </summary>
    public string TypeName { get; set; } = "System.String";

    public bool IsMandatory { get; set; }
    public int Position { get; set; } = int.MaxValue;
}
