using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using PoshMcp.Server.PowerShell;

namespace PoshMcp;

/// <summary>
/// Per-tool diagnostic entry surfaced under
/// <c>functionsTools.tools[]</c> in the doctor JSON, populated from the
/// <see cref="IToolDescriptionSourceTracker"/> recorded during tool discovery.
/// Spec 010 FR-583 defines the JSON path
/// <c>tools[].descriptionSource</c> and
/// <c>tools[].parameters[].descriptionSource</c>.
/// </summary>
public sealed record ToolDescriptionDoctorEntry
{
    /// <summary>The MCP tool name (sanitized command + parameter set).</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>The PowerShell command name backing this tool.</summary>
    [JsonPropertyName("commandName")]
    public string CommandName { get; init; } = string.Empty;

    /// <summary>
    /// FR-583 string literal identifying which step of the FR-500 precedence chain
    /// produced the tool description. Null when no source was recorded for this
    /// command (e.g., tool was registered outside the discovery path).
    /// </summary>
    [JsonPropertyName("descriptionSource")]
    [JsonConverter(typeof(NullableToolDescriptionSourceJsonConverter))]
    public ToolDescriptionSource? DescriptionSource { get; init; }

    /// <summary>Per-parameter description source entries for this tool.</summary>
    [JsonPropertyName("parameters")]
    public List<ParameterDescriptionDoctorEntry> Parameters { get; init; } = [];
}

/// <summary>
/// Per-parameter diagnostic entry under
/// <c>functionsTools.tools[].parameters[]</c>. Spec 010 FR-583 defines the
/// <c>descriptionSource</c> field with values
/// <c>helpParameter | helpMessage | validateSet | typeFallback</c>.
/// </summary>
public sealed record ParameterDescriptionDoctorEntry
{
    /// <summary>The parameter name without the leading dash.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// FR-583 string literal identifying which step of the FR-510 precedence chain
    /// produced the parameter description. Null when no source was recorded.
    /// </summary>
    [JsonPropertyName("descriptionSource")]
    [JsonConverter(typeof(NullableParameterDescriptionSourceJsonConverter))]
    public ParameterDescriptionSource? DescriptionSource { get; init; }
}

/// <summary>
/// Serializes a nullable <see cref="ToolDescriptionSource"/> as its FR-583 wire-format
/// literal (<c>synopsis | description | syntax | name</c>) or <c>null</c>.
/// </summary>
internal sealed class NullableToolDescriptionSourceJsonConverter : JsonConverter<ToolDescriptionSource?>
{
    public override ToolDescriptionSource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }
        var s = reader.GetString();
        return s switch
        {
            "synopsis" => ToolDescriptionSource.Synopsis,
            "description" => ToolDescriptionSource.Description,
            "syntax" => ToolDescriptionSource.Syntax,
            "name" => ToolDescriptionSource.Name,
            _ => null,
        };
    }

    public override void Write(Utf8JsonWriter writer, ToolDescriptionSource? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }
        writer.WriteStringValue(DescriptionSourceVocabulary.ToWireValue(value.Value));
    }
}

/// <summary>
/// Serializes a nullable <see cref="ParameterDescriptionSource"/> as its FR-583
/// wire-format literal (<c>helpParameter | helpMessage | validateSet | typeFallback</c>)
/// or <c>null</c>.
/// </summary>
internal sealed class NullableParameterDescriptionSourceJsonConverter : JsonConverter<ParameterDescriptionSource?>
{
    public override ParameterDescriptionSource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }
        var s = reader.GetString();
        return s switch
        {
            "helpParameter" => ParameterDescriptionSource.HelpParameter,
            "helpMessage" => ParameterDescriptionSource.HelpMessage,
            "validateSet" => ParameterDescriptionSource.ValidateSet,
            "typeFallback" => ParameterDescriptionSource.TypeFallback,
            _ => null,
        };
    }

    public override void Write(Utf8JsonWriter writer, ParameterDescriptionSource? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }
        writer.WriteStringValue(DescriptionSourceVocabulary.ToWireValue(value.Value));
    }
}
