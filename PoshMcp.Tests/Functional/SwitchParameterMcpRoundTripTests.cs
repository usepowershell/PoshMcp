// Functional tests for SwitchParameter end-to-end JSON handling in MCP tools.
//
// Verifies the two-layer fix:
//   1. SwitchParameterJsonConverter — accepts boolean / {isPresent} / null on the
//      wire and produces a real SwitchParameter (not default(SwitchParameter)).
//   2. SwitchParameterMcpSupport.SchemaOptions — TransformSchemaNode rewrites the
//      MCP SDK's broken default schema for SwitchParameter (an opaque struct)
//      into a permissive anyOf [boolean | {isPresent} | null] so natural payloads
//      validate client-side.
//
// Coverage tiers (cheapest → most realistic):
//   * Converter unit cases (Theory) and serialization round-trip
//   * Regression guard documenting the silent-false bug if the converter is
//     ever removed
//   * Schema transform applied to a real Get-ChildItem -Recurse tool
//   * End-to-end: define a PowerShell function with a [switch] parameter,
//     generate the MCP method, invoke it with each MCP-wire JSON shape via the
//     existing CreateParameterArray + method.Invoke path, and assert the
//     function actually saw IsPresent=true/false.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.PowerShell;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Functional;

/// <summary>
/// End-to-end checks that PowerShell <see cref="SwitchParameter"/> arguments
/// flow correctly from MCP JSON input to bound CLR values, and that the
/// advertised JSON schema accepts every shape an MCP client might emit.
/// </summary>
public class SwitchParameterMcpRoundTripTests : PowerShellTestBase
{
    public SwitchParameterMcpRoundTripTests(ITestOutputHelper output) : base(output) { }

    // ── JsonConverter behaviour ─────────────────────────────────────────────

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("null", false)]
    [InlineData("\"true\"", true)]
    [InlineData("\"false\"", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("{\"isPresent\": true}", true)]
    [InlineData("{\"isPresent\": false}", false)]
    [InlineData("{\"IsPresent\": true}", true)]   // case-insensitive
    [InlineData("{}", true)]                       // empty object => presence
    [InlineData("{\"unrelated\": 42}", true)]      // ignored property, default presence
    public void Converter_DeserializesAllExpectedShapes(string json, bool expectedIsPresent)
    {
        var result = JsonSerializer.Deserialize<SwitchParameter>(
            json, SwitchParameterMcpSupport.SerializerOptions);

        Assert.Equal(expectedIsPresent, result.IsPresent);
    }

    [Fact]
    public void Converter_SerializesAsPlainBoolean()
    {
        var on = JsonSerializer.Serialize(new SwitchParameter(true), SwitchParameterMcpSupport.SerializerOptions);
        var off = JsonSerializer.Serialize(new SwitchParameter(false), SwitchParameterMcpSupport.SerializerOptions);

        Assert.Equal("true", on);
        Assert.Equal("false", off);
    }

    [Fact]
    public void Converter_RegressionGuard_DefaultStjBindingProducesIsPresentFalse()
    {
        // This is the failure mode the converter exists to prevent: without it,
        // {"isPresent": true} silently deserializes to default(SwitchParameter)
        // because SwitchParameter exposes IsPresent as a getter-only property
        // and System.Text.Json has no way to bind to it.
        var bare = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };

        var result = JsonSerializer.Deserialize<SwitchParameter>("{\"isPresent\": true}", bare);

        // Document the broken behaviour we're working around.
        Assert.False(result.IsPresent);
    }

    // ── Schema transform ────────────────────────────────────────────────────

    [Fact]
    public async Task GeneratedToolSchema_AdvertisesAnyOfForSwitchParameter()
    {
        // Get-ChildItem has the [-Recurse] switch — exercise the full pipeline
        // and verify the schema for that argument is the permissive anyOf, not
        // the SDK's default {"type":["object","null"], properties:{isPresent}}.
        var config = new PowerShellConfiguration
        {
            FunctionNames = new List<string> { "Get-ChildItem" },
        };

        var tools = await ToolFactory.GetToolsListAsync(config, Logger);
        Assert.NotEmpty(tools);

        // At least one Get-ChildItem overload should expose -Recurse.
        var (tool, recurseSchema) = tools
            .Select(t => (t, FindProperty(t.ProtocolTool.InputSchema, "Recurse")))
            .FirstOrDefault(pair => pair.Item2 is not null);

        Assert.NotNull(tool);
        Assert.True(recurseSchema.HasValue,
            "No Get-ChildItem tool exposed a -Recurse parameter; cannot verify schema.");

        var node = recurseSchema!.Value;
        Logger.LogInformation("Recurse schema: {Schema}", node.GetRawText());

        Assert.True(node.TryGetProperty("anyOf", out var anyOf), $"Expected 'anyOf' in schema; got: {node.GetRawText()}");
        Assert.Equal(JsonValueKind.Array, anyOf.ValueKind);

        var variants = anyOf.EnumerateArray().ToList();

        // Must accept plain boolean.
        Assert.Contains(variants, v =>
            v.TryGetProperty("type", out var type) && type.GetString() == "boolean");

        // Must still accept the legacy {isPresent} envelope (so older clients work).
        Assert.Contains(variants, v =>
            v.TryGetProperty("type", out var type) && type.GetString() == "object"
            && v.TryGetProperty("properties", out var props)
            && props.TryGetProperty("isPresent", out _));

        // Must accept null (parameter omitted).
        Assert.Contains(variants, v =>
            v.TryGetProperty("type", out var type) && type.GetString() == "null");
    }

    [Fact]
    public async Task GeneratedToolSchema_LeavesNonSwitchParametersUntouched()
    {
        // Sanity: non-switch parameters keep their normal SDK-generated schema.
        var config = new PowerShellConfiguration
        {
            FunctionNames = new List<string> { "Get-ChildItem" },
        };

        var tools = await ToolFactory.GetToolsListAsync(config, Logger);
        Assert.NotEmpty(tools);

        var (tool, pathSchema) = tools
            .Select(t => (t, FindProperty(t.ProtocolTool.InputSchema, "Path")))
            .FirstOrDefault(pair => pair.Item2 is not null);

        Assert.NotNull(tool);
        Assert.True(pathSchema.HasValue, "No Get-ChildItem tool exposed -Path.");

        // Path is string[] in PowerShell — our transform must not have rewritten it.
        Assert.False(pathSchema!.Value.TryGetProperty("anyOf", out _),
            $"Non-switch parameter 'Path' was unexpectedly rewritten: {pathSchema.Value.GetRawText()}");
    }

    // ── End-to-end: real PowerShell invocation through generated method ─────

    /// <summary>
    /// PowerShell function used as the e2e probe. <c>-MarkPresent</c> is a real
    /// <see cref="SwitchParameter"/>; the function emits a single string of the
    /// form <c>"present:True"</c> / <c>"present:False"</c> so the test can
    /// assert what the runtime actually saw — not just what the converter
    /// returned in isolation.
    /// </summary>
    private const string SwitchProbeFunction = @"
function Get-SwitchProbeResult {
    [CmdletBinding()]
    param(
        [Parameter()]
        [string]$Name = 'probe',

        [Parameter()]
        [switch]$MarkPresent
    )

    ""$Name|present=$($MarkPresent.IsPresent)""
}";

    [Theory]
    // Each case represents how an MCP client might encode the switch on the wire.
    // The driver below feeds these straight into the generated method's argument
    // array via SwitchParameterMcpSupport.SerializerOptions, mirroring exactly
    // what the MCP SDK does at tools/call time.
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("null", false)]
    [InlineData("{\"isPresent\": true}", true)]
    [InlineData("{\"isPresent\": false}", false)]
    public async Task EndToEnd_SwitchParameter_PowerShellSeesExpectedIsPresent(
        string switchJson, bool expectedIsPresent)
    {
        Logger.LogInformation("=== Probe with switchJson='{Json}', expected={Expected} ===",
            switchJson, expectedIsPresent);

        // Define the probe function in the test runspace.
        var ps = PowerShellRunspace.Instance;
        ps.Commands.Clear();
        ps.AddScript(SwitchProbeFunction);
        SafeInvokePowerShell(ps, "defining Get-SwitchProbeResult");
        ps.Commands.Clear();

        // Discover it as a CommandInfo and run it through the same generation
        // path the MCP server uses.
        ps.AddCommand("Get-Command").AddParameter("Name", "Get-SwitchProbeResult");
        var cmds = SafeInvokePowerShell(ps, "Get-Command Get-SwitchProbeResult")
            .Select(p => p.BaseObject).OfType<CommandInfo>().ToList();
        ps.Commands.Clear();
        Assert.Single(cmds);

        AssemblyGenerator.GenerateAssembly(cmds, Logger);
        var instance = AssemblyGenerator.GetGeneratedInstance(Logger);
        var methods = AssemblyGenerator.GetGeneratedMethods();
        Assert.Contains("get_switch_probe_result", methods.Keys);

        var method = methods["get_switch_probe_result"];

        // Deserialize the wire JSON into the CLR type the generated method
        // expects (SwitchParameter). This is the exact code path the MCP SDK
        // takes for tool arguments — JsonSerializer.Deserialize against the
        // SerializerOptions we hand it via McpServerToolCreateOptions.
        var markPresent = JsonSerializer.Deserialize<SwitchParameter>(
            switchJson, SwitchParameterMcpSupport.SerializerOptions);
        Logger.LogInformation("Bound MarkPresent.IsPresent = {Bound}", markPresent.IsPresent);

        var args = PowerShellParameterUtils.CreateParameterArray(
            method,
            new Dictionary<string, object?>
            {
                ["Name"] = "probe",
                ["MarkPresent"] = markPresent,
                ["cancellationToken"] = CancellationToken.None,
            });

        var taskResult = (Task<string>)method.Invoke(instance, args)!;
        var output = await taskResult;
        Logger.LogInformation("Function output: {Out}", output);

        // The generated method returns a JSON-encoded array of pipeline output.
        var rows = ConvertJsonToObjects(output);
        Assert.Single(rows);

        var line = Assert.IsType<string>(rows[0]);
        var expectedLine = $"probe|present={expectedIsPresent}";
        Assert.Equal(expectedLine, line);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Walk an MCP tool's <c>inputSchema</c> and return the schema node for the
    /// named property if it exists. The schema is always shaped
    /// <c>{"type":"object","properties":{...}}</c>.
    /// </summary>
    private static JsonElement? FindProperty(JsonElement schema, string name)
    {
        if (schema.ValueKind != JsonValueKind.Object) return null;
        if (!schema.TryGetProperty("properties", out var props)) return null;
        if (props.ValueKind != JsonValueKind.Object) return null;

        foreach (var p in props.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return p.Value;
        }
        return null;
    }
}
