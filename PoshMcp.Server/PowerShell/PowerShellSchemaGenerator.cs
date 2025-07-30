using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Utility class for generating JSON schemas for PowerShell parameters
/// </summary>
public static class PowerShellSchemaGenerator
{
    /// <summary>
    /// Generates a JSON schema object for the command parameters (for documentation purposes)
    /// </summary>
    /// <param name="commandInfo">The PowerShell command information</param>
    /// <returns>A schema object representing the command parameters</returns>
    public static object GenerateParameterSchema(CommandInfo commandInfo)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var parameterKvp in commandInfo.Parameters)
        {
            var parameterName = parameterKvp.Key;
            var parameterMetadata = parameterKvp.Value;

            // Skip common parameters that are handled by PowerShell runtime
            if (PowerShellParameterUtils.IsCommonParameter(parameterName))
                continue;

            var parameterSchema = CreateParameterSchema(parameterMetadata);
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
    /// Creates a JSON schema for a single parameter
    /// </summary>
    /// <param name="parameterMetadata">The parameter metadata</param>
    /// <returns>A schema object for the parameter</returns>
    public static object CreateParameterSchema(ParameterMetadata parameterMetadata)
    {
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

        // Add description from parameter name and type
        schema["description"] = $"Parameter of type {parameterType.Name}";

        return schema;
    }
}
