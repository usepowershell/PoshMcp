using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Custom JSON converter for PowerShell PSObject Type
/// Handles serialization of PSObject instances to maintain properties and values
/// </summary>
public class PSObjectJsonConverter : JsonConverter<PSObject>
{
    public override PSObject? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        throw new NotImplementedException("PSObject deserialization from JSON is not supported");
    }

    public override void Write(Utf8JsonWriter writer, PSObject value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        var normalized = PowerShellObjectSerializer.FlattenPSObject(value);
        JsonSerializer.Serialize(writer, normalized, normalized?.GetType() ?? typeof(object), options);
    }
}

/// <summary>
/// Factory class for creating JsonSerializerOptions configured for PowerShell object serialization
/// </summary>
public static class PowerShellJsonOptions
{
    private static readonly JsonSerializerOptions DefaultOptions = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            MaxDepth = 32
        };

        options.Converters.Add(new PSObjectJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }

    /// <summary>
    /// Gets the default JsonSerializerOptions for PowerShell object serialization
    /// </summary>
    public static JsonSerializerOptions Options => DefaultOptions;
}
