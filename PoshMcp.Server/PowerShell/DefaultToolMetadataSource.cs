using System;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Default <see cref="IToolMetadataSource"/> implementation. Preserves the pre-spec-010
/// behavior byte-for-byte so wiring this seam into both execution paths does not change any
/// observable tool or parameter description text.
/// </summary>
/// <remarks>
/// <para>
/// Tool descriptions: prefers <c>Synopsis</c> when supplied and non-empty (the OOP path's
/// current behavior); otherwise falls back to the in-process behavior of
/// <c>"{CommandName} {ParameterSetSyntax}"</c>; otherwise returns the bare command name.
/// The <c>LongDescription</c> input is intentionally ignored at this stage — Get-Help
/// long-description sourcing is the responsibility of follow-up issues #226 (in-process)
/// and #227 (out-of-process).
/// </para>
/// <para>
/// Parameter descriptions: always returns the type fallback string. The richer precedence
/// chain (Get-Help parameter, HelpMessage, ValidateSet) is also implemented in #226/#227.
/// </para>
/// <para>
/// Thread-safe: stateless.
/// </para>
/// </remarks>
public sealed class DefaultToolMetadataSource : IToolMetadataSource
{
    /// <inheritdoc />
    public ToolDescriptionResult ResolveToolDescription(in ToolDescriptionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CommandName))
        {
            throw new ArgumentException(
                $"{nameof(ToolDescriptionRequest.CommandName)} must not be null or whitespace.",
                nameof(request));
        }

        // Pre-spec-010 OOP behavior: use Synopsis when present, non-empty, and not equal to
        // the command name. The spec records this comparison rule in FR-500 step 1.
        if (!string.IsNullOrWhiteSpace(request.Synopsis))
        {
            var synopsis = request.Synopsis!.Trim();
            if (!string.Equals(synopsis, request.CommandName, StringComparison.Ordinal))
            {
                return new ToolDescriptionResult(synopsis, ToolDescriptionSource.Synopsis);
            }
        }

        // Pre-spec-010 in-process behavior: "{CommandName} {ParameterSetSyntax}".
        if (!string.IsNullOrWhiteSpace(request.ParameterSetSyntax))
        {
            return new ToolDescriptionResult(
                $"{request.CommandName} {request.ParameterSetSyntax}",
                ToolDescriptionSource.Syntax);
        }

        // Final fallback: bare command name.
        return new ToolDescriptionResult(request.CommandName, ToolDescriptionSource.Name);
    }

    /// <inheritdoc />
    public ParameterDescriptionResult ResolveParameterDescription(in ParameterDescriptionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ParameterName))
        {
            throw new ArgumentException(
                $"{nameof(ParameterDescriptionRequest.ParameterName)} must not be null or whitespace.",
                nameof(request));
        }

        var typeName = string.IsNullOrWhiteSpace(request.ParameterTypeName)
            ? "Object"
            : request.ParameterTypeName;

        // Pre-spec-010 behavior, preserved at every call site: "Parameter of type <Type>".
        // Richer sources (HelpParameter, HelpMessage, ValidateSet) are intentionally NOT
        // consulted here — they land in #226 / #227.
        return new ParameterDescriptionResult(
            $"Parameter of type {typeName}",
            ParameterDescriptionSource.TypeFallback);
    }
}
