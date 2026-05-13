using System;
using System.Collections.Generic;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Spec 010 implementation of <see cref="IToolMetadataSource"/> that applies the full
/// FR-500 / FR-510 precedence chains and FR-540 sanitization. Pure resolver — does NOT
/// invoke PowerShell. Callers (<see cref="PoshMcp.McpToolFactoryV2"/> for the in-process
/// path, the OOP host for the out-of-process path) are responsible for pre-resolving
/// Get-Help inputs and supplying them via the request records.
/// </summary>
/// <remarks>
/// Length caps from FR-541 (1024 for tool descriptions) and FR-542 (512 for parameter
/// descriptions) are applied at this layer so both execution paths produce byte-identical
/// output (FR-520).
/// </remarks>
public sealed class HelpAwareToolMetadataSource : IToolMetadataSource
{
    /// <summary>FR-541 cap applied to tool descriptions.</summary>
    public const int ToolDescriptionMaxLength = 1024;

    /// <summary>FR-542 cap applied to parameter descriptions.</summary>
    public const int ParameterDescriptionMaxLength = 512;

    /// <inheritdoc />
    public ToolDescriptionResult ResolveToolDescription(in ToolDescriptionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CommandName))
        {
            throw new ArgumentException(
                $"{nameof(ToolDescriptionRequest.CommandName)} must not be null or whitespace.",
                nameof(request));
        }

        // FR-500 step 1: Get-Help .Synopsis when present, non-empty, and not equal to the command name.
        if (!string.IsNullOrWhiteSpace(request.Synopsis))
        {
            var synopsis = DescriptionSanitizer.Normalize(request.Synopsis);
            if (synopsis.Length > 0
                && !string.Equals(synopsis, request.CommandName, StringComparison.Ordinal))
            {
                return new ToolDescriptionResult(
                    DescriptionSanitizer.TruncateAtWordBoundary(synopsis, ToolDescriptionMaxLength),
                    ToolDescriptionSource.Synopsis);
            }
        }

        // FR-500 step 2: Get-Help .Description body, paragraph-joined and sanitized.
        if (!string.IsNullOrWhiteSpace(request.LongDescription))
        {
            var longDesc = DescriptionSanitizer.Normalize(request.LongDescription);
            if (longDesc.Length > 0)
            {
                return new ToolDescriptionResult(
                    DescriptionSanitizer.TruncateAtWordBoundary(longDesc, ToolDescriptionMaxLength),
                    ToolDescriptionSource.Description);
            }
        }

        // FR-500 step 3: "{CommandName} {ParameterSetSyntax}".
        if (!string.IsNullOrWhiteSpace(request.ParameterSetSyntax))
        {
            var combined = $"{request.CommandName} {request.ParameterSetSyntax}";
            return new ToolDescriptionResult(
                DescriptionSanitizer.TruncateAtWordBoundary(combined, ToolDescriptionMaxLength),
                ToolDescriptionSource.Syntax);
        }

        // FR-500 step 4: bare command name.
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

        // FR-510 step 1: Get-Help parameter description.
        if (!string.IsNullOrWhiteSpace(request.HelpParameterDescription))
        {
            var helpParam = DescriptionSanitizer.Normalize(request.HelpParameterDescription);
            if (helpParam.Length > 0)
            {
                return new ParameterDescriptionResult(
                    DescriptionSanitizer.TruncateAtWordBoundary(helpParam, ParameterDescriptionMaxLength),
                    ParameterDescriptionSource.HelpParameter);
            }
        }

        // FR-510 step 2: ParameterAttribute.HelpMessage.
        if (!string.IsNullOrWhiteSpace(request.HelpMessage))
        {
            var helpMessage = DescriptionSanitizer.Normalize(request.HelpMessage);
            if (helpMessage.Length > 0)
            {
                return new ParameterDescriptionResult(
                    DescriptionSanitizer.TruncateAtWordBoundary(helpMessage, ParameterDescriptionMaxLength),
                    ParameterDescriptionSource.HelpMessage);
            }
        }

        // FR-510 step 3: ValidateSet phrasing — singleton vs. array element.
        if (request.ValidateSetValues is { Count: > 0 } values)
        {
            var prefix = request.ValidateSetAppliesToArrayElement ? "Each item is one of: " : "One of: ";
            var rendered = prefix + string.Join(", ", values);
            return new ParameterDescriptionResult(
                DescriptionSanitizer.TruncateAtWordBoundary(rendered, ParameterDescriptionMaxLength),
                ParameterDescriptionSource.ValidateSet);
        }

        // FR-510 step 4: type fallback (preserves pre-spec-010 behavior).
        var typeName = string.IsNullOrWhiteSpace(request.ParameterTypeName)
            ? "Object"
            : request.ParameterTypeName;
        return new ParameterDescriptionResult(
            $"Parameter of type {typeName}",
            ParameterDescriptionSource.TypeFallback);
    }
}
