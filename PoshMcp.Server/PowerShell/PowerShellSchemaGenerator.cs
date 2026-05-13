using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Utility class for generating JSON schemas for PowerShell parameters.
/// </summary>
/// <remarks>
/// Spec 010: parameter descriptions emitted by this generator are now resolved through an
/// <see cref="IToolMetadataSource"/> so they apply the same FR-510 precedence chain
/// (Get-Help parameter → ParameterAttribute.HelpMessage → ValidateSet phrasing → typed
/// fallback) used by the in-process and out-of-process tool factory paths. Callers that
/// don't supply a source receive the type-fallback string for every parameter (preserves
/// pre-spec-010 output).
/// </remarks>
public static class PowerShellSchemaGenerator
{
    /// <summary>
    /// Generates a JSON schema object for the command parameters (for documentation purposes).
    /// </summary>
    /// <param name="commandInfo">The PowerShell command information.</param>
    /// <param name="metadataSource">Optional metadata source used to resolve per-parameter
    /// descriptions per spec 010 FR-510. Defaults to <see cref="DefaultToolMetadataSource"/>
    /// when omitted, which preserves pre-spec-010 output.</param>
    /// <returns>A schema object representing the command parameters.</returns>
    public static object GenerateParameterSchema(CommandInfo commandInfo, IToolMetadataSource? metadataSource = null)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();
        var source = metadataSource ?? new DefaultToolMetadataSource();

        foreach (var parameterKvp in commandInfo.Parameters)
        {
            var parameterName = parameterKvp.Key;
            var parameterMetadata = parameterKvp.Value;

            // Skip common parameters that are handled by PowerShell runtime
            if (PowerShellParameterUtils.IsCommonParameter(parameterName))
                continue;

            var parameterSchema = CreateParameterSchema(parameterMetadata, commandInfo.Name, parameterName, source);
            properties[parameterName] = parameterSchema;

            // Check if parameter is mandatory
            if (parameterMetadata.Attributes.OfType<ParameterAttribute>().Any(attr => attr.Mandatory))
            {
                required.Add(parameterName);
            }
        }

        return new
        {
            type = "object",
            properties = properties,
            required = required.ToArray()
        };
    }

    /// <summary>
    /// Creates a JSON schema for a single parameter.
    /// </summary>
    /// <param name="parameterMetadata">The parameter metadata.</param>
    /// <returns>A schema object for the parameter.</returns>
    public static object CreateParameterSchema(ParameterMetadata parameterMetadata)
        => CreateParameterSchema(parameterMetadata, commandName: string.Empty, parameterName: parameterMetadata?.Name ?? string.Empty, metadataSource: null);

    /// <summary>
    /// Creates a JSON schema for a single parameter, applying the spec 010 FR-510 precedence
    /// chain to the parameter description when <paramref name="metadataSource"/> is supplied.
    /// </summary>
    /// <param name="parameterMetadata">The parameter metadata.</param>
    /// <param name="commandName">Owning command name (used for diagnostics in resolver
    /// implementations); empty string is acceptable when not available.</param>
    /// <param name="parameterName">Parameter name without leading dash.</param>
    /// <param name="metadataSource">Description resolver. <c>null</c> selects the type fallback.</param>
    public static object CreateParameterSchema(
        ParameterMetadata parameterMetadata,
        string commandName,
        string parameterName,
        IToolMetadataSource? metadataSource)
    {
        if (parameterMetadata == null) throw new ArgumentNullException(nameof(parameterMetadata));

        var schema = new Dictionary<string, object>();
        var parameterType = parameterMetadata.ParameterType;

        // Handle nullable types
        var underlyingType = Nullable.GetUnderlyingType(parameterType) ?? parameterType;

        // Map .NET types to JSON schema types
        if (underlyingType == typeof(string))
        {
            schema["type"] = "string";
        }
        else if (underlyingType == typeof(bool) || underlyingType == typeof(SwitchParameter))
        {
            schema["type"] = "boolean";
        }
        else if (underlyingType == typeof(int) || underlyingType == typeof(long))
        {
            schema["type"] = "integer";
        }
        else if (underlyingType == typeof(double) || underlyingType == typeof(decimal) || underlyingType == typeof(float))
        {
            schema["type"] = "number";
        }
        else if (underlyingType.IsArray)
        {
            schema["type"] = "array";
            var elementType = underlyingType.GetElementType()!;
            // Create a simplified schema for array elements
            schema["items"] = new { type = "string" }; // Simplified for now
        }
        else if (underlyingType.IsEnum)
        {
            schema["type"] = "string";
            schema["enum"] = Enum.GetNames(underlyingType);
        }
        else
        {
            schema["type"] = "string"; // Default to string for complex types
        }

        // Resolve description through the spec 010 precedence chain. Pull the same inputs
        // the in-process call site collects (Get-Help text isn't available here, so the
        // chain naturally degrades to HelpMessage / ValidateSet / typed fallback).
        var source = metadataSource ?? new DefaultToolMetadataSource();
        var helpMessage = parameterMetadata.Attributes.OfType<ParameterAttribute>()
            .Select(a => a.HelpMessage)
            .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));
        var validateSet = parameterMetadata.Attributes.OfType<ValidateSetAttribute>().FirstOrDefault();
        IReadOnlyList<string>? validateValues = validateSet?.ValidValues != null
            ? validateSet.ValidValues.ToArray()
            : null;
        var appliesToArrayElement = validateValues != null && parameterType.IsArray;

        var request = new ParameterDescriptionRequest(
            CommandName: string.IsNullOrEmpty(commandName) ? parameterName : commandName,
            ParameterName: string.IsNullOrEmpty(parameterName) ? "_param" : parameterName,
            ParameterTypeName: parameterType.Name,
            HelpParameterDescription: null,
            HelpMessage: helpMessage,
            ValidateSetValues: validateValues,
            ValidateSetAppliesToArrayElement: appliesToArrayElement);

        schema["description"] = source.ResolveParameterDescription(in request).Description;

        return schema;
    }
}
