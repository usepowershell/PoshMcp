using System;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using ModelContextProtocol.Server;

namespace PoshMcp;

internal static class McpToolSchema
{
    private const string StrictEmptyObjectSchema = """
        {
          "type": "object",
          "properties": {},
          "additionalProperties": false
        }
        """;

    public static bool HasOnlyInfrastructureParameters(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);

        foreach (var parameter in method.GetParameters())
        {
            if (parameter.ParameterType != typeof(CancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    public static McpServerTool ApplyStrictEmptyObjectSchema(McpServerTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        tool.ProtocolTool.InputSchema = JsonDocument.Parse(StrictEmptyObjectSchema).RootElement.Clone();
        return tool;
    }
}
