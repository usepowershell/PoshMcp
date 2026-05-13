using System.Collections.Generic;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Shared sourcing seam for MCP tool and parameter descriptions, applied identically by
/// both the in-process and out-of-process execution paths.
/// </summary>
/// <remarks>
/// <para>
/// This seam exists to satisfy spec 010 (Improve MCP Tool Self-Documentation) Approach
/// Option A: a single precedence implementation consumed by both
/// <c>McpToolFactoryV2.SetParameterSetDescription</c> (in-process) and
/// <c>McpToolFactoryV2.CreateRemoteCommandMetadataMapping</c> (out-of-process). The
/// default implementation supplied with this seam preserves pre-spec-010 behavior
/// exactly — Get-Help-based precedence chains land in follow-up work (issues #226 and
/// #227); this seam is the API surface they will plug into.
/// </para>
/// <para>
/// Implementations MUST be thread-safe: a single instance is shared across all tool
/// discovery on a server.
/// </para>
/// </remarks>
public interface IToolMetadataSource
{
    /// <summary>
    /// Resolves the MCP tool description for a single parameter set of a PowerShell command,
    /// applying the documented precedence chain (synopsis → long description → syntax line →
    /// bare command name).
    /// </summary>
    /// <param name="request">Inputs known to the caller (command name, optional pre-resolved
    /// help fields, optional parameter-set syntax). Members that the caller cannot provide
    /// MUST be left null/empty.</param>
    /// <returns>The resolved description and the precedence step that produced it.</returns>
    ToolDescriptionResult ResolveToolDescription(in ToolDescriptionRequest request);

    /// <summary>
    /// Resolves the MCP parameter description for a single parameter of a PowerShell command,
    /// applying the documented precedence chain (Get-Help parameter → ParameterAttribute
    /// HelpMessage → ValidateSet phrasing → type fallback).
    /// </summary>
    /// <param name="request">Inputs known to the caller. Members that the caller cannot
    /// provide MUST be left null/empty.</param>
    /// <returns>The resolved description and the precedence step that produced it.</returns>
    ParameterDescriptionResult ResolveParameterDescription(in ParameterDescriptionRequest request);
}

/// <summary>
/// Inputs to <see cref="IToolMetadataSource.ResolveToolDescription"/>.
/// </summary>
/// <param name="CommandName">The full PowerShell command name (e.g., "Get-AzContext").
/// Always required.</param>
/// <param name="ParameterSetName">The parameter set name this description applies to, or
/// <c>null</c> for <c>__AllParameterSets</c>. Used for logging/diagnostics only — tool
/// description text is per-command, not per-parameter-set (spec 010 FR-501).</param>
/// <param name="Synopsis">Pre-resolved Get-Help <c>.Synopsis</c>, when the caller has it
/// (the OOP path supplies this from the subprocess; the in-process path will supply it once
/// #226 lands). <c>null</c> or empty means "not available."</param>
/// <param name="LongDescription">Pre-resolved Get-Help <c>.Description</c> body, joined per
/// the sanitization rules in spec 010 FR-540. <c>null</c> or empty means "not available."</param>
/// <param name="ParameterSetSyntax">The <c>CommandParameterSetInfo.ToString()</c> syntax
/// string for the in-process path. <c>null</c> or empty means "not available."</param>
public readonly record struct ToolDescriptionRequest(
    string CommandName,
    string? ParameterSetName,
    string? Synopsis,
    string? LongDescription,
    string? ParameterSetSyntax);

/// <summary>
/// Output of <see cref="IToolMetadataSource.ResolveToolDescription"/>.
/// </summary>
/// <param name="Description">The final, sanitized, length-capped description string to be
/// surfaced to MCP clients. Never <c>null</c>; falls back to the bare command name when no
/// other source has content.</param>
/// <param name="Source">The precedence step that produced <paramref name="Description"/>.
/// Reported by doctor output (spec 010 FR-583) and emitted as a metric tag (FR-590).</param>
public readonly record struct ToolDescriptionResult(
    string Description,
    ToolDescriptionSource Source);

/// <summary>
/// Identifies which step of the tool-description precedence chain produced a result.
/// Values map one-to-one with the <c>descriptionSource</c> string literals in spec 010
/// FR-583 (<c>synopsis | description | syntax | name</c>).
/// </summary>
public enum ToolDescriptionSource
{
    /// <summary>Get-Help <c>.Synopsis</c> (FR-500 step 1).</summary>
    Synopsis,

    /// <summary>Get-Help <c>.Description</c> body (FR-500 step 2).</summary>
    Description,

    /// <summary><c>CommandParameterSetInfo.ToString()</c> syntax line (FR-500 step 3).</summary>
    Syntax,

    /// <summary>Bare command name fallback (FR-500 step 4).</summary>
    Name,
}

/// <summary>
/// Inputs to <see cref="IToolMetadataSource.ResolveParameterDescription"/>.
/// </summary>
/// <param name="CommandName">The owning command name (e.g., "Get-AzContext"). Always
/// required.</param>
/// <param name="ParameterName">The parameter name without the leading dash (e.g., "Name").
/// Always required.</param>
/// <param name="ParameterTypeName">The .NET type name of the parameter (e.g.,
/// "System.String"). Always required — used by the type-fallback step
/// (<see cref="ParameterDescriptionSource.TypeFallback"/>).</param>
/// <param name="HelpParameterDescription">Pre-resolved Get-Help parameter description text,
/// joined and sanitized per spec 010 FR-540. <c>null</c> or empty means "not available."</param>
/// <param name="HelpMessage">Value of <c>[Parameter(HelpMessage="...")]</c>, when present.
/// <c>null</c> or empty means "not available."</param>
/// <param name="ValidateSetValues">Allowed values from a <c>[ValidateSet(...)]</c>
/// attribute, in declaration order. <c>null</c> or empty means "not available."</param>
/// <param name="ValidateSetAppliesToArrayElement">When <paramref name="ValidateSetValues"/>
/// is supplied, indicates whether the validated set constrains array elements (true) or the
/// scalar parameter itself (false). Drives the FR-510 step 3 phrasing
/// ("Each item is one of: ..." vs. "One of: ...").</param>
public readonly record struct ParameterDescriptionRequest(
    string CommandName,
    string ParameterName,
    string ParameterTypeName,
    string? HelpParameterDescription,
    string? HelpMessage,
    IReadOnlyList<string>? ValidateSetValues,
    bool ValidateSetAppliesToArrayElement);

/// <summary>
/// Output of <see cref="IToolMetadataSource.ResolveParameterDescription"/>.
/// </summary>
/// <param name="Description">The final, sanitized, length-capped description string. Never
/// <c>null</c>; falls back to <c>"Parameter of type &lt;TypeName&gt;"</c> when no other
/// source has content.</param>
/// <param name="Source">The precedence step that produced <paramref name="Description"/>.</param>
public readonly record struct ParameterDescriptionResult(
    string Description,
    ParameterDescriptionSource Source);

/// <summary>
/// Identifies which step of the parameter-description precedence chain produced a result.
/// Values map one-to-one with the <c>descriptionSource</c> string literals in spec 010
/// FR-583 (<c>helpParameter | helpMessage | validateSet | typeFallback</c>).
/// </summary>
public enum ParameterDescriptionSource
{
    /// <summary>Get-Help <c>.Parameters.parameter[].description</c> (FR-510 step 1).</summary>
    HelpParameter,

    /// <summary><c>[Parameter(HelpMessage="...")]</c> (FR-510 step 2).</summary>
    HelpMessage,

    /// <summary><c>[ValidateSet(...)]</c> allowed-values phrasing (FR-510 step 3).</summary>
    ValidateSet,

    /// <summary><c>Parameter of type &lt;TypeName&gt;</c> fallback (FR-510 step 4).</summary>
    TypeFallback,
}
