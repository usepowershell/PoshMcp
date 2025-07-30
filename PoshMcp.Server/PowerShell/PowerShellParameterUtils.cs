using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Utility class for processing PowerShell parameters
/// </summary>
public static class PowerShellParameterUtils
{
    /// <summary>
    /// Converts a raw parameter value to the expected PowerShell parameter type
    /// </summary>
    /// <param name="rawValue">The raw value from MCP arguments</param>
    /// <param name="targetType">The expected parameter type</param>
    /// <param name="parameterName">Parameter name for logging</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>The converted value</returns>
    public static object ConvertParameterValue(object rawValue, Type targetType, string parameterName, ILogger logger)
    {
        try
        {
            // Handle null values
            if (rawValue == null)
            {
                if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
                {
                    throw new ArgumentException($"Cannot convert null to non-nullable type {targetType.Name} for parameter {parameterName}");
                }
                return null!;
            }

            // If already the correct type, return as-is
            if (targetType.IsAssignableFrom(rawValue.GetType()))
            {
                return rawValue;
            }

            // Handle common type conversions
            var rawString = rawValue.ToString();

            // Handle nullable types
            var underlyingType = Nullable.GetUnderlyingType(targetType);
            if (underlyingType != null)
            {
                targetType = underlyingType;
            }

            // String type
            if (targetType == typeof(string))
            {
                return rawString ?? string.Empty;
            }

            // Boolean type
            if (targetType == typeof(bool))
            {
                if (bool.TryParse(rawString, out bool boolValue))
                    return boolValue;
                // Try common boolean representations
                return rawString?.ToLower() switch
                {
                    "1" or "yes" or "y" or "true" or "on" => true,
                    "0" or "no" or "n" or "false" or "off" => false,
                    _ => throw new ArgumentException($"Cannot convert '{rawString}' to boolean for parameter {parameterName}")
                };
            }

            // Numeric types
            if (targetType == typeof(int))
            {
                if (int.TryParse(rawString, out int intValue))
                    return intValue;
                throw new ArgumentException($"Cannot convert '{rawString}' to integer for parameter {parameterName}");
            }

            if (targetType == typeof(long))
            {
                if (long.TryParse(rawString, out long longValue))
                    return longValue;
                throw new ArgumentException($"Cannot convert '{rawString}' to long for parameter {parameterName}");
            }

            if (targetType == typeof(double))
            {
                if (double.TryParse(rawString, out double doubleValue))
                    return doubleValue;
                throw new ArgumentException($"Cannot convert '{rawString}' to double for parameter {parameterName}");
            }

            if (targetType == typeof(decimal))
            {
                if (decimal.TryParse(rawString, out decimal decimalValue))
                    return decimalValue;
                throw new ArgumentException($"Cannot convert '{rawString}' to decimal for parameter {parameterName}");
            }

            // DateTime type
            if (targetType == typeof(DateTime))
            {
                if (DateTime.TryParse(rawString, out DateTime dateValue))
                    return dateValue;
                throw new ArgumentException($"Cannot convert '{rawString}' to DateTime for parameter {parameterName}");
            }

            // Enum types
            if (targetType.IsEnum)
            {
                if (Enum.TryParse(targetType, rawString, true, out var enumValue))
                    return enumValue;
                throw new ArgumentException($"Cannot convert '{rawString}' to enum {targetType.Name} for parameter {parameterName}");
            }

            // Array types
            if (targetType.IsArray)
            {
                var elementType = targetType.GetElementType()!;
                if (rawValue is System.Collections.IEnumerable enumerable and not string)
                {
                    var list = new List<object>();
                    foreach (var item in enumerable)
                    {
                        list.Add(ConvertParameterValue(item, elementType, $"{parameterName}[{list.Count}]", logger));
                    }
                    var array = Array.CreateInstance(elementType, list.Count);
                    for (int i = 0; i < list.Count; i++)
                    {
                        array.SetValue(list[i], i);
                    }
                    return array;
                }
            }

            // Switch parameter (common in PowerShell)
            if (targetType == typeof(SwitchParameter))
            {
                return new SwitchParameter(ConvertParameterValue(rawValue, typeof(bool), parameterName, logger) as bool? ?? false);
            }

            // Fall back to Convert.ChangeType for other types
            return Convert.ChangeType(rawValue, targetType);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to convert parameter {parameterName} from {rawValue?.GetType()?.Name ?? "null"} to {targetType.Name}");
            throw new ArgumentException($"Cannot convert parameter '{parameterName}' to type {targetType.Name}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Checks if a parameter is a common PowerShell parameter (like Verbose, Debug, etc.)
    /// </summary>
    /// <param name="parameterName">The parameter name</param>
    /// <returns>True if it's a common parameter</returns>
    public static bool IsCommonParameter(string parameterName)
    {
        var commonParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Verbose", "Debug", "ErrorAction", "WarningAction", "InformationAction",
            "ProgressAction", "ErrorVariable", "WarningVariable", "InformationVariable",
            "OutVariable", "OutBuffer", "PipelineVariable", "WhatIf", "Confirm"
        };

        return commonParameters.Contains(parameterName);
    }

    /// <summary>
    /// Gets the default value for a given type
    /// </summary>
    public static object? GetDefaultValueForType(Type type)
    {
        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }
        return null;
    }

    /// <summary>
    /// Processes a single parameter based on its metadata and input arguments
    /// </summary>
    /// <param name="parameterName">Name of the parameter</param>
    /// <param name="parameterMetadata">PowerShell parameter metadata</param>
    /// <param name="arguments">Input arguments from MCP call</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>The processed parameter value or null if not provided/applicable</returns>
    public static object? ProcessParameter(string parameterName, ParameterMetadata parameterMetadata, IDictionary<string, object> arguments, ILogger logger)
    {
        // Check if the parameter was provided in the arguments
        if (!arguments.TryGetValue(parameterName, out var rawValue))
        {
            // Check if the parameter is mandatory
            if (parameterMetadata.Attributes.OfType<ParameterAttribute>().Any(attr => attr.Mandatory))
            {
                logger.LogWarning($"Mandatory parameter {parameterName} not provided");
                throw new ArgumentException($"Mandatory parameter '{parameterName}' is required but was not provided");
            }

            // Parameter is optional and no value provided
            return null;
        }

        // Convert the raw value to the appropriate type
        return ConvertParameterValue(rawValue, parameterMetadata.ParameterType, parameterName, logger);
    }

    /// <summary>
    /// Helper method that creates an object array for method parameters based on a dictionary of values.
    /// Dictionary keys should match parameter names, and values will be used as parameter values.
    /// Parameters not found in the dictionary will be set to their default values or null.
    /// If a parameter expects an array but receives a single value, it will automatically create a single-element array.
    /// </summary>
    /// <param name="methodInfo">The MethodInfo containing parameter information</param>
    /// <param name="parameterValues">Dictionary with parameter names as keys and their values</param>
    /// <returns>Object array suitable for method invocation</returns>
    public static object?[] CreateParameterArray(MethodInfo methodInfo, Dictionary<string, object?> parameterValues)
    {
        var parameters = methodInfo.GetParameters();
        var result = new object?[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];

            if (parameterValues.TryGetValue(param.Name!, out var value))
            {
                // Check if parameter expects an array but we have a single value
                if (param.ParameterType.IsArray && value != null && !value.GetType().IsArray)
                {
                    // Create a single-element array of the correct type
                    var elementType = param.ParameterType.GetElementType()!;
                    var array = Array.CreateInstance(elementType, 1);
                    array.SetValue(value, 0);
                    result[i] = array;
                }
                else
                {
                    // Use the provided value as-is
                    result[i] = value;
                }
            }
            else if (param.HasDefaultValue)
            {
                // Use the default value if available
                result[i] = param.DefaultValue;
            }
            else if (param.ParameterType.IsValueType && Nullable.GetUnderlyingType(param.ParameterType) == null)
            {
                // For non-nullable value types, create default instance
                result[i] = Activator.CreateInstance(param.ParameterType);
            }
            else
            {
                // For reference types or nullable value types, use null
                result[i] = null;
            }
        }

        return result;
    }

    /// <summary>
    /// Helper method that deserializes a JSON string (returned from PowerShell ConvertTo-Json) back to objects.
    /// This is primarily useful for testing and scenarios where you need to work with the actual objects
    /// rather than the JSON representation.
    /// </summary>
    /// <param name="jsonString">JSON string returned from PowerShell execution</param>
    /// <returns>Array of deserialized objects</returns>
    public static object[] DeserializeFromPowerShellJson(string jsonString)
    {
        if (string.IsNullOrWhiteSpace(jsonString))
        {
            return Array.Empty<object>();
        }

        try
        {
            using var document = JsonDocument.Parse(jsonString);

            // Handle both single objects and arrays
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                var results = new List<object>();
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    results.Add(ConvertJsonElementToObject(element));
                }
                return results.ToArray();
            }
            else
            {
                // Single object, wrap in array
                return new[] { ConvertJsonElementToObject(document.RootElement) };
            }
        }
        catch (JsonException)
        {
            // If JSON parsing fails, return the string as-is wrapped in an array
            return new object[] { jsonString };
        }
    }

    /// <summary>
    /// Converts a JsonElement to a .NET object, handling nested structures
    /// </summary>
    private static object ConvertJsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.TryGetInt64(out var longVal) ? longVal : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElementToObject).ToArray(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                prop => prop.Name,
                prop => ConvertJsonElementToObject(prop.Value)),
            _ => element.ToString()
        };
    }
}
