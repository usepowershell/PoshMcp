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
    /// long <c>Description</c> body is exposed separately via
    /// <see cref="FullDescription"/>; parameter set syntax is NOT read here.
    /// When empty, downstream tool schema generation falls back to the bare
    /// command name.
    /// </summary>
    /// <remarks>
    /// Preserved verbatim for backward compatibility with consumers that
    /// only know about this field. Spec 010 introduces <see cref="FullDescription"/>
    /// alongside it; the precedence chain (FR-500) is applied on the .NET
    /// consumer side, not by the host script.
    /// </remarks>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Optional long-form description body sourced from
    /// <c>Get-Help &lt;command&gt;.Description</c>, an array of
    /// <c>MamlParaText</c> objects whose <c>.Text</c> values are joined with
    /// the paragraph separator <c>"\n\n"</c>. Raw text — no sanitization or
    /// length capping is applied by the host; the .NET consumer applies
    /// FR-540 sanitization and FR-541 length caps.
    /// </summary>
    /// <remarks>
    /// <c>null</c> when <c>Get-Help</c> returned no help record, threw, or
    /// returned a record whose <c>.Description</c> property was absent or
    /// empty. The empty string is also possible when <c>Description</c>
    /// existed but contained only whitespace after the join.
    /// Consumers MUST treat <c>null</c> and empty-or-whitespace as
    /// "no value" and fall through to the next precedence step.
    /// </remarks>
    public string? FullDescription { get; set; }

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

    /// <summary>
    /// Optional per-parameter description text sourced from
    /// <c>Get-Help &lt;command&gt;.Parameters.parameter[name=&lt;Name&gt;].description</c>,
    /// joined paragraph-by-paragraph with <c>"\n\n"</c>. Raw text — the
    /// .NET consumer applies FR-540 sanitization and FR-542 length caps.
    /// </summary>
    /// <remarks>
    /// <c>null</c> when the help record had no entry for this parameter, the
    /// entry's <c>description</c> array was absent, or <c>Get-Help</c>
    /// itself was unavailable. Consumers treat <c>null</c> or
    /// empty-or-whitespace as "no value" and fall through to the next
    /// precedence step (HelpMessage → ValidateSet → type fallback) per
    /// FR-510.
    /// </remarks>
    public string? HelpDescription { get; set; }

    /// <summary>
    /// Optional <c>HelpMessage</c> value from <c>[Parameter(HelpMessage="...")]</c>
    /// on this parameter, read from <c>CommandInfo.Parameters[Name].Attributes</c>
    /// of type <c>System.Management.Automation.ParameterAttribute</c>.
    /// </summary>
    /// <remarks>
    /// <c>null</c> when no <c>ParameterAttribute</c> sets <c>HelpMessage</c>,
    /// or when the attribute was unavailable. If multiple parameter sets
    /// declare the parameter with different <c>HelpMessage</c> values, the
    /// first non-empty value encountered is emitted; the host does not
    /// attempt to reconcile divergent values across attributes.
    /// </remarks>
    public string? HelpMessage { get; set; }

    /// <summary>
    /// Optional list of allowed values from <c>[ValidateSet(...)]</c> on this
    /// parameter, read from the parameter's
    /// <c>System.Management.Automation.ValidateSetAttribute.ValidValues</c>
    /// collection.
    /// </summary>
    /// <remarks>
    /// <c>null</c> when the parameter has no <c>ValidateSetAttribute</c>.
    /// An empty array is theoretically possible but treated equivalently to
    /// <c>null</c> by FR-510 step 3. Order is preserved as declared in the
    /// attribute.
    /// </remarks>
    public string[]? ValidateSetValues { get; set; }
}
