using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Utility class for safely serializing PowerShell objects to prevent cycles and deep nesting issues
/// </summary>
public static class PowerShellObjectSerializer
{
    private const int MaxDepth = 4;

    private static readonly IEqualityComparer<object> ReferenceComparer = new ObjectReferenceEqualityComparer();

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

        return psObjects.Select(psObject => FlattenPSObject(psObject)).ToArray();
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

        var visited = new HashSet<object>(ReferenceComparer);

        try
        {
            return NormalizeValue(psObject, 0, visited)!;
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

    public static object? NormalizeForJson(object? value)
    {
        var visited = new HashSet<object>(ReferenceComparer);
        return NormalizeValue(value, 0, visited);
    }

    private static object? NormalizeValue(object? value, int currentDepth, HashSet<object> visited)
    {
        if (value == null)
        {
            return null;
        }

        if (currentDepth >= MaxDepth)
        {
            return value.ToString();
        }

        if (value is PSObject psObject)
        {
            return NormalizePSObject(psObject, currentDepth, visited);
        }

        if (value is IntPtr intptr)
        {
            return intptr.ToInt64();
        }

        if (value is UIntPtr uintptr)
        {
            return uintptr.ToUInt64();
        }

        if (IsSimpleType(value))
        {
            return value;
        }

        if (!ShouldTrackReference(value))
        {
            return NormalizeNonScalarValue(value, currentDepth, visited);
        }

        if (!visited.Add(value))
        {
            return value.ToString();
        }

        try
        {
            return NormalizeNonScalarValue(value, currentDepth, visited);
        }
        finally
        {
            visited.Remove(value);
        }
    }

    private static object? NormalizeNonScalarValue(object value, int currentDepth, HashSet<object> visited)
    {
        if (value is IDictionary dictionary)
        {
            var result = new Dictionary<string, object?>();

            foreach (DictionaryEntry entry in dictionary)
            {
                var key = entry.Key?.ToString();
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                result[key] = NormalizeValue(entry.Value, currentDepth + 1, visited);
            }

            return result;
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            var items = new List<object?>();
            foreach (var item in enumerable)
            {
                items.Add(NormalizeValue(item, currentDepth + 1, visited));
            }

            return items;
        }

        var properties = GetSafeProperties(value);
        if (properties.Count == 0)
        {
            return value.ToString();
        }

        var propertyMap = new Dictionary<string, object?>();
        foreach (var property in properties)
        {
            if (TryGetNormalizedPropertyValue(property, currentDepth, visited, out var normalizedValue))
            {
                propertyMap[property.Name] = normalizedValue;
            }
        }

        return propertyMap.Count > 0 ? propertyMap : value.ToString();
    }

    private static object? NormalizePSObject(PSObject psObject, int currentDepth, HashSet<object> visited)
    {
        var baseObject = psObject.BaseObject;
        if (baseObject == null)
        {
            return null;
        }

        if (IsSimpleType(baseObject))
        {
            return baseObject;
        }

        if (!ReferenceEquals(baseObject, psObject) && ShouldTrackReference(baseObject) && visited.Contains(baseObject))
        {
            return psObject.ToString();
        }

        if (baseObject is IDictionary or IEnumerable and not string)
        {
            return NormalizeValue(baseObject, currentDepth, visited);
        }

        var properties = GetSafeProperties(psObject);
        if (properties.Count == 0 && !ReferenceEquals(baseObject, psObject))
        {
            return NormalizeValue(baseObject, currentDepth, visited);
        }

        var result = new Dictionary<string, object?>();
        foreach (var property in properties)
        {
            if (TryGetShallowPSPropertyValue(property, currentDepth, visited, out var normalizedValue))
            {
                result[property.Name] = normalizedValue;
            }
        }

        return result.Count > 0 ? result : psObject.ToString();
    }

    private static List<PSPropertyInfo> GetSafeProperties(object value)
    {
        var properties = new List<PSPropertyInfo>();

        foreach (var property in PSObject.AsPSObject(value).Properties)
        {
            if (!property.IsGettable)
            {
                continue;
            }

            properties.Add(property);
        }

        return properties;
    }

    private static bool TryGetShallowPSPropertyValue(PSPropertyInfo property, int currentDepth, HashSet<object> visited, out object? normalizedValue)
    {
        try
        {
            var propertyValue = property.Value;
            normalizedValue = NormalizePSPropertyValue(propertyValue, currentDepth, visited);
            return true;
        }
        catch
        {
            normalizedValue = null;
            return false;
        }
    }

    private static object? NormalizePSPropertyValue(object? value, int currentDepth, HashSet<object> visited)
    {
        if (value == null)
        {
            return null;
        }

        if (value is IntPtr intptr)
        {
            return intptr.ToInt64();
        }

        if (value is UIntPtr uintptr)
        {
            return uintptr.ToUInt64();
        }

        if (IsSimpleType(value))
        {
            return value;
        }

        if (value is IDictionary or IEnumerable and not string)
        {
            return value.ToString();
        }

        if (value is PSObject nestedPsObject)
        {
            return NormalizeValue(nestedPsObject, currentDepth + 1, visited);
        }

        return value.ToString();
    }

    private static bool TryGetNormalizedPropertyValue(PSPropertyInfo property, int currentDepth, HashSet<object> visited, out object? normalizedValue)
    {
        try
        {
            normalizedValue = NormalizeValue(property.Value, currentDepth + 1, visited);
            return true;
        }
        catch
        {
            normalizedValue = null;
            return false;
        }
    }

    private static bool ShouldTrackReference(object value)
    {
        var type = value.GetType();
        return !(type.IsValueType || value is string);
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
               type == typeof(Uri) ||
               type == typeof(Version) ||
               type == typeof(decimal) ||
               type == typeof(DateTime) ||
               type == typeof(DateTimeOffset) ||
               type == typeof(TimeSpan) ||
               type == typeof(Guid) ||
               type.IsEnum ||
               (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>) &&
                IsSimpleType(Nullable.GetUnderlyingType(type)));
    }

    private sealed class ObjectReferenceEqualityComparer : IEqualityComparer<object>
    {
        bool IEqualityComparer<object>.Equals(object? x, object? y)
        {
            return ReferenceEquals(x, y);
        }

        int IEqualityComparer<object>.GetHashCode(object obj)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
