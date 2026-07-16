using System;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using PoshMcp;
using Xunit;

namespace PoshMcp.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class McpToolSchemaTests
{
    [Fact]
    public void ApplyStrictEmptyObjectSchema_ReplacesSdkParameterlessSchema()
    {
        var tool = McpServerTool.Create(
            new Func<CancellationToken, Task<string>>(_ => Task.FromResult("ok")),
            new McpServerToolCreateOptions { Name = "parameterless-probe" });

        McpToolSchema.ApplyStrictEmptyObjectSchema(tool);

        var schema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())?.AsObject();
        Assert.NotNull(schema);
        Assert.Equal("object", schema!["type"]?.GetValue<string>());
        Assert.Empty(schema["properties"]?.AsObject() ?? new JsonObject());
        Assert.False(schema["additionalProperties"]?.GetValue<bool>() ?? true);
    }

    [Fact]
    public void HasOnlyInfrastructureParameters_IdentifiesParameterlessToolMethods()
    {
        var parameterless = typeof(McpToolSchemaTests).GetMethod(
            nameof(ParameterlessProbe),
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var parameterized = typeof(McpToolSchemaTests).GetMethod(
            nameof(ParameterizedProbe),
            BindingFlags.Static | BindingFlags.NonPublic)!;

        Assert.True(McpToolSchema.HasOnlyInfrastructureParameters(parameterless));
        Assert.False(McpToolSchema.HasOnlyInfrastructureParameters(parameterized));
    }

    private static Task<string> ParameterlessProbe(CancellationToken cancellationToken) =>
        Task.FromResult("ok");

    private static Task<string> ParameterizedProbe(string value, CancellationToken cancellationToken) =>
        Task.FromResult(value);
}
