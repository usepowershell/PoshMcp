using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Utility class for safely serializing PowerShell objects to prevent cycles and deep nesting issues
/// </summary>
public static class PowerShellObjectSerializer
{
    /// <summary>
    /// Converts a PSObject array to a serializable format that avoids cycles and deep nesting
    /// </summary>
    /// <param name="psObjects">Array of PowerShell objects</param>
    /// <returns>Serializable object array</returns>
    public static object[] FlattenPSObjects(PSObject[] psObjects)
    {
        if (psObjects == null || psObjects.Length == 0)
        {
            return Array.Empty<object>();
        }

        return psObjects.Select(FlattenPSObject).ToArray();
    }

    /// <summary>
    /// Converts a single PSObject to a serializable format
    /// </summary>
    /// <param name="psObject">PowerShell object to flatten</param>
    /// <returns>Serializable object</returns>
    public static object FlattenPSObject(PSObject psObject)
    {
        if (psObject == null)
        {
            return null!;
        }

        try
        {
            // If the base object is a simple type, return it directly
            var baseObject = psObject.BaseObject;
            if (IsSimpleType(baseObject))
            {
                return baseObject;
            }

            // For complex objects, create a flattened dictionary of properties
            var result = new Dictionary<string, object?>();

            // Add type information
            result["_PSTypeName"] = psObject.TypeNames.FirstOrDefault() ?? baseObject.GetType().Name;

            // Add properties with safe traversal
            foreach (var property in psObject.Properties)
            {
                try
                {
                    if (property.Value == null)
                    {
                        result[property.Name] = null;
                    }
                    else if (IsSimpleType(property.Value))
                    {
                        result[property.Name] = property.Value;
                    }
                    else if (property.Value is PSObject nestedPSObject)
                    {
                        // Recursively flatten nested PSObjects, but limit depth
                        result[property.Name] = FlattenPSObjectSafe(nestedPSObject, 1, 3);
                    }
                    else
                    {
                        // Convert complex objects to string representation
                        result[property.Name] = property.Value.ToString();
                    }
                }
                catch (Exception ex)
                {
                    // If we can't access a property, include the error info
                    result[property.Name] = $"[Error accessing property: {ex.Message}]";
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            // Fallback to string representation if flattening fails
            return new Dictionary<string, object?>
            {
                ["_PSTypeName"] = "PSObject",
                ["_Value"] = psObject.ToString(),
                ["_Error"] = $"Serialization error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Safely flattens a PSObject with depth limiting to prevent infinite recursion
    /// </summary>
    private static object FlattenPSObjectSafe(PSObject psObject, int currentDepth, int maxDepth)
    {
        if (currentDepth >= maxDepth || psObject == null)
        {
            return psObject?.ToString() ?? "null";
        }

        var baseObject = psObject.BaseObject;
        if (IsSimpleType(baseObject))
        {
            return baseObject;
        }

        var result = new Dictionary<string, object?>();
        result["_PSTypeName"] = psObject.TypeNames.FirstOrDefault() ?? baseObject.GetType().Name;

        // Only include a limited set of properties at deeper levels
        var properties = psObject.Properties.Take(10); // Limit properties to prevent huge objects
        foreach (var property in properties)
        {
            try
            {
                if (property.Value == null)
                {
                    result[property.Name] = null;
                }
                else if (IsSimpleType(property.Value))
                {
                    result[property.Name] = property.Value;
                }
                else if (property.Value is PSObject nestedPSObject)
                {
                    result[property.Name] = FlattenPSObjectSafe(nestedPSObject, currentDepth + 1, maxDepth);
                }
                else
                {
                    result[property.Name] = property.Value.ToString();
                }
            }
            catch
            {
                result[property.Name] = "[Property access error]";
            }
        }

        return result;
    }

    /// <summary>
    /// Determines if a type is simple enough to serialize directly
    /// </summary>
    private static bool IsSimpleType(object? obj)
    {
        if (obj == null) return true;

        var type = obj.GetType();
        return type.IsPrimitive ||
               type == typeof(string) ||
               type == typeof(decimal) ||
               type == typeof(DateTime) ||
               type == typeof(DateTimeOffset) ||
               type == typeof(TimeSpan) ||
               type == typeof(Guid) ||
               type.IsEnum ||
               (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>) &&
                IsSimpleType(Nullable.GetUnderlyingType(type)));
    }
}
