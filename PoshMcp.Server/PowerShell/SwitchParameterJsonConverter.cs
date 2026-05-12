using System;
using System.Management.Automation;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// JSON converter for PowerShell <see cref="SwitchParameter"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SwitchParameter"/> is a struct whose only mutable state is set via
/// constructor (<c>IsPresent</c> is read-only). Default
/// <see cref="System.Text.Json"/> deserialization cannot bind to it, so
/// <c>{"isPresent": true}</c> silently produced <c>default(SwitchParameter)</c>
/// (i.e. <c>IsPresent = false</c>), which prevented any switch parameter from
/// ever being activated through MCP.
/// </para>
/// <para>
/// This converter accepts every JSON shape an MCP client might plausibly send
/// for a switch:
/// <list type="bullet">
///   <item><description><c>true</c> / <c>false</c> — natural boolean.</description></item>
///   <item><description><c>null</c> — treated as <c>false</c> (PowerShell switch absence).</description></item>
///   <item><description><c>{"isPresent": true}</c> / <c>{"IsPresent": true}</c> — the
///     legacy struct envelope still advertised by the MCP SDK's reflection-based
///     schema generator for <see cref="SwitchParameter"/>.</description></item>
///   <item><description><c>{}</c> — an empty object is treated as presence (the
///     LLM bothered to mention the switch).</description></item>
/// </list>
/// On write, the converter emits a plain boolean.
/// </para>
/// </remarks>
public sealed class SwitchParameterJsonConverter : JsonConverter<SwitchParameter>
{
    public override SwitchParameter Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True:
                return new SwitchParameter(true);

            case JsonTokenType.False:
            case JsonTokenType.Null:
                return new SwitchParameter(false);

            case JsonTokenType.String:
                var s = reader.GetString();
                return new SwitchParameter(bool.TryParse(s, out var b) && b);

            case JsonTokenType.Number:
                return new SwitchParameter(reader.TryGetInt64(out var n) && n != 0);

            case JsonTokenType.StartObject:
                bool present = true; // empty object => presence
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                        return new SwitchParameter(present);

                    if (reader.TokenType != JsonTokenType.PropertyName)
                        continue;

                    var propName = reader.GetString();
                    reader.Read();
                    if (string.Equals(propName, "isPresent", StringComparison.OrdinalIgnoreCase))
                    {
                        present = reader.TokenType switch
                        {
                            JsonTokenType.True => true,
                            JsonTokenType.False => false,
                            JsonTokenType.String => bool.TryParse(reader.GetString(), out var parsed) && parsed,
                            JsonTokenType.Number => reader.TryGetInt64(out var num) && num != 0,
                            _ => present,
                        };
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
                return new SwitchParameter(present);

            default:
                return new SwitchParameter(false);
        }
    }

    public override void Write(
        Utf8JsonWriter writer,
        SwitchParameter value,
        JsonSerializerOptions options)
    {
        writer.WriteBooleanValue(value.IsPresent);
    }
}

/// <summary>
/// Centralizes all <see cref="SwitchParameter"/>-related plumbing for MCP tool
/// creation: a <see cref="JsonSerializerOptions"/> instance carrying
/// <see cref="SwitchParameterJsonConverter"/> for runtime binding, and an
/// <see cref="AIJsonSchemaCreateOptions"/> instance whose
/// <see cref="AIJsonSchemaCreateOptions.TransformSchemaNode"/> rewrites the
/// schema node for switch parameters into a permissive
/// <c>anyOf</c> (boolean | <c>{isPresent}</c> | null).
/// </summary>
public static class SwitchParameterMcpSupport
{
    /// <summary>
    /// Shared serializer options carrying <see cref="SwitchParameterJsonConverter"/>.
    /// </summary>
    public static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    /// <summary>
    /// Shared schema-creation options rewriting the SwitchParameter node.
    /// </summary>
    public static readonly AIJsonSchemaCreateOptions SchemaOptions = CreateSchemaOptions();

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            // Without a TypeInfoResolver, MakeReadOnly() throws on .NET 10
            // (the MCP SDK freezes the options before first use).
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.Converters.Add(new SwitchParameterJsonConverter());
        return options;
    }

    private static AIJsonSchemaCreateOptions CreateSchemaOptions()
    {
        return new AIJsonSchemaCreateOptions
        {
            TransformSchemaNode = (context, node) =>
            {
                var t = context.TypeInfo?.Type;
                if (t == typeof(SwitchParameter)
                    || (t is not null && Nullable.GetUnderlyingType(t) == typeof(SwitchParameter)))
                {
                    return new JsonObject
                    {
                        ["anyOf"] = new JsonArray(
                            new JsonObject { ["type"] = "boolean" },
                            new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject
                                {
                                    ["isPresent"] = new JsonObject { ["type"] = "boolean" }
                                }
                            },
                            new JsonObject { ["type"] = "null" }),
                        ["default"] = null,
                    };
                }
                return node;
            },
        };
    }
}
