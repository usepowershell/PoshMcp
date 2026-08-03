using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;
using PoshMcp;
using PoshMcp.Server.PowerShell;
using Xunit;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// #345: inputSchema audit for SDK v2.
/// Verifies that every tool registration path emits a valid JSON Schema object
/// for <c>inputSchema</c> and that all schemas survive a <c>System.Text.Json</c>
/// round-trip (the serializer used by the MCP SDK v2 wire layer) without a
/// <c>JsonException</c>.
/// </summary>
[Collection("TransportSelectionTests")]
[Trait("Category", "Unit")]
[Trait("Issue", "345")]
public sealed class InputSchemaV2ComplianceTests
{
    // ── SDK v2 default-schema documentation ────────────────────────────────

    /// <summary>
    /// Regression guard: SDK v2 does NOT auto-add <c>additionalProperties:false</c>
    /// for a parameterless <c>Func&lt;CancellationToken, Task&lt;string&gt;&gt;</c>.
    /// This test documents WHY <see cref="McpToolSchema.ApplyStrictEmptyObjectSchema"/>
    /// is still required after the v2 upgrade.  If this assertion starts failing,
    /// re-evaluate whether the manual schema patch is still needed.
    /// </summary>
    [Fact]
    public void SdkV2_ParameterlessFuncTool_DefaultSchema_LacksAdditionalPropertiesFalse()
    {
        var tool = McpServerTool.Create(
            new Func<CancellationToken, Task<string>>(_ => Task.FromResult("ok")),
            new McpServerToolCreateOptions { Name = "v2-probe" });

        var schema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())?.AsObject();
        Assert.NotNull(schema);

        // The SDK generates a valid JSON Schema root but does NOT include the
        // strict additionalProperties:false PoshMcp convention.
        var additionalProperties = schema!["additionalProperties"];
        var hasStrictFalse = additionalProperties is not null
                             && additionalProperties.GetValue<bool>() == false;

        Assert.False(
            hasStrictFalse,
            "SDK v2 unexpectedly generates additionalProperties:false — "
            + "verify whether McpToolSchema.ApplyStrictEmptyObjectSchema is still needed.");
    }

    /// <summary>
    /// SDK v2 auto-generates <c>type:object</c> at the root of the schema —
    /// the minimum required for a valid MCP tool schema.
    /// </summary>
    [Fact]
    public void SdkV2_ParameterlessFuncTool_DefaultSchema_HasTypeObject()
    {
        var tool = McpServerTool.Create(
            new Func<CancellationToken, Task<string>>(_ => Task.FromResult("ok")),
            new McpServerToolCreateOptions { Name = "v2-probe" });

        var schema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())?.AsObject();
        Assert.NotNull(schema);
        Assert.Equal("object", schema!["type"]?.GetValue<string>());
    }

    // ── ApplyStrictEmptyObjectSchema correctness ───────────────────────────

    [Fact]
    public void ApplyStrictEmptyObjectSchema_ProducesRequiredTriple()
    {
        var tool = McpServerTool.Create(
            new Func<CancellationToken, Task<string>>(_ => Task.FromResult("ok")),
            new McpServerToolCreateOptions { Name = "v2-probe" });

        McpToolSchema.ApplyStrictEmptyObjectSchema(tool);

        var schema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())?.AsObject();
        Assert.NotNull(schema);
        Assert.Equal("object", schema!["type"]?.GetValue<string>());
        Assert.Empty(schema["properties"]?.AsObject() ?? new JsonObject());
        Assert.False(schema["additionalProperties"]?.GetValue<bool>() ?? true);
    }

    // ── System.Text.Json round-trip (SDK v2 wire layer) ────────────────────

    [Fact]
    public void StrictEmptyObjectSchema_RoundTrips_SystemTextJson_WithoutException()
    {
        var tool = McpServerTool.Create(
            new Func<CancellationToken, Task<string>>(_ => Task.FromResult("ok")),
            new McpServerToolCreateOptions { Name = "v2-probe" });
        McpToolSchema.ApplyStrictEmptyObjectSchema(tool);

        var ex = Record.Exception(() =>
        {
            var json = JsonSerializer.Serialize(tool.ProtocolTool.InputSchema);
            var roundTripped = JsonSerializer.Deserialize<JsonElement>(json);
            Assert.Equal(JsonValueKind.Object, roundTripped.ValueKind);
        });
        Assert.Null(ex);
    }

    [Fact]
    public void ToolWithUserParameters_Schema_RoundTrips_SystemTextJson_WithoutException()
    {
        var tool = McpServerTool.Create(
            new Func<string, CancellationToken, Task<string>>((s, _) => Task.FromResult(s)),
            new McpServerToolCreateOptions { Name = "v2-probe-with-param" });

        var ex = Record.Exception(() =>
        {
            var json = JsonSerializer.Serialize(tool.ProtocolTool.InputSchema);
            var roundTripped = JsonSerializer.Deserialize<JsonElement>(json);
            Assert.Equal(JsonValueKind.Object, roundTripped.ValueKind);
        });
        Assert.Null(ex);
    }

    [Fact]
    public void ToolWithNullableParameters_Schema_RoundTrips_SystemTextJson_WithoutException()
    {
        var tool = McpServerTool.Create(
            new Func<string?, int?, string[]?, CancellationToken, Task<string>>(
                (s, n, arr, _) => Task.FromResult(s ?? string.Empty)),
            new McpServerToolCreateOptions { Name = "v2-probe-nullable" });

        var ex = Record.Exception(() =>
        {
            var json = JsonSerializer.Serialize(tool.ProtocolTool.InputSchema);
            var roundTripped = JsonSerializer.Deserialize<JsonElement>(json);
            Assert.Equal(JsonValueKind.Object, roundTripped.ValueKind);
        });
        Assert.Null(ex);
    }

    // ── SetupMcpToolsAsync: all paths produce type:object schemas ──────────

    [PwshAvailableFact]
    public async Task SetupMcpToolsAsync_AllTools_HaveTypeObjectInputSchema()
    {
        using var configFile = new V2ComplianceConfigFile();
        var config = CreateFullConfig();

        var result = await McpToolSetupService.SetupMcpToolsAsync(
            NullLoggerFactory.Instance,
            config,
            NullLogger.Instance,
            configFile.Path,
            "runtime",
            commandExecutor: null);

        foreach (var tool in result.Tools)
        {
            var schema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())?.AsObject();
            Assert.NotNull(schema);
            var actualType = schema!["type"]?.GetValue<string>();
            Assert.True(
                actualType == "object",
                $"Tool '{tool.ProtocolTool.Name}' inputSchema must have type:object. " +
                $"Got type='{actualType}'. Schema: {tool.ProtocolTool.InputSchema.GetRawText()}");
        }
    }

    [PwshAvailableFact]
    public async Task SetupMcpToolsAsync_AllTools_SchemasRoundTrip_WithoutJsonException()
    {
        using var configFile = new V2ComplianceConfigFile();
        var config = CreateFullConfig();

        var result = await McpToolSetupService.SetupMcpToolsAsync(
            NullLoggerFactory.Instance,
            config,
            NullLogger.Instance,
            configFile.Path,
            "runtime",
            commandExecutor: null);

        foreach (var tool in result.Tools)
        {
            var ex = Record.Exception(() =>
            {
                var json = JsonSerializer.Serialize(tool.ProtocolTool.InputSchema);
                var roundTripped = JsonSerializer.Deserialize<JsonElement>(json);
                Assert.Equal(JsonValueKind.Object, roundTripped.ValueKind);
            });
            Assert.Null(ex);
        }
    }

    // ── Parameterless config-tool schemas are strict ─────────────────────

    [PwshAvailableFact]
    public async Task ReloadConfigurationFromFile_Tool_HasStrictEmptyInputSchema()
    {
        using var configFile = new V2ComplianceConfigFile();
        var config = CreateFullConfig();
        config.EnableDynamicReloadTools = true;

        var result = await McpToolSetupService.SetupMcpToolsAsync(
            NullLoggerFactory.Instance,
            config,
            NullLogger.Instance,
            configFile.Path,
            "runtime",
            commandExecutor: null);

        AssertStrictEmptyInputSchema(result.Tools, "reload-configuration-from-file");
    }

    [PwshAvailableFact]
    public async Task GetConfigurationStatus_Tool_HasStrictEmptyInputSchema()
    {
        using var configFile = new V2ComplianceConfigFile();
        var config = CreateFullConfig();
        config.EnableDynamicReloadTools = true;

        var result = await McpToolSetupService.SetupMcpToolsAsync(
            NullLoggerFactory.Instance,
            config,
            NullLogger.Instance,
            configFile.Path,
            "runtime",
            commandExecutor: null);

        AssertStrictEmptyInputSchema(result.Tools, "get-configuration-status");
    }

    [PwshAvailableFact]
    public async Task GetConfigurationGuidance_Tool_HasStrictEmptyInputSchema()
    {
        using var configFile = new V2ComplianceConfigFile();
        var config = CreateFullConfig();
        config.EnableConfigurationTroubleshootingTool = true;

        var result = await McpToolSetupService.SetupMcpToolsAsync(
            NullLoggerFactory.Instance,
            config,
            NullLogger.Instance,
            configFile.Path,
            "runtime",
            commandExecutor: null);

        AssertStrictEmptyInputSchema(result.Tools, "get-configuration-guidance");
    }

    [PwshAvailableFact]
    public async Task GetConfigurationTroubleshooting_Tool_HasStrictEmptyInputSchema()
    {
        using var configFile = new V2ComplianceConfigFile();
        var config = CreateFullConfig();
        config.EnableConfigurationTroubleshootingTool = true;

        var result = await McpToolSetupService.SetupMcpToolsAsync(
            NullLoggerFactory.Instance,
            config,
            NullLogger.Instance,
            configFile.Path,
            "runtime",
            commandExecutor: null);

        AssertStrictEmptyInputSchema(result.Tools, "get-configuration-troubleshooting");
    }

    // ── Tools with user parameters still have valid schemas ────────────────

    [PwshAvailableFact]
    public async Task UpdateConfiguration_Tool_HasTypeObjectWithStringProperty()
    {
        using var configFile = new V2ComplianceConfigFile();
        var config = CreateFullConfig();
        config.EnableDynamicReloadTools = true;

        var result = await McpToolSetupService.SetupMcpToolsAsync(
            NullLoggerFactory.Instance,
            config,
            NullLogger.Instance,
            configFile.Path,
            "runtime",
            commandExecutor: null);

        var tool = Assert.Single(result.Tools, t =>
            string.Equals(t.ProtocolTool.Name, "update-configuration", StringComparison.OrdinalIgnoreCase));

        var schema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())?.AsObject();
        Assert.NotNull(schema);
        Assert.Equal("object", schema!["type"]?.GetValue<string>());

        var properties = schema["properties"]?.AsObject();
        Assert.NotNull(properties);
        Assert.True(properties!.Count > 0, "update-configuration must expose at least one parameter in inputSchema");
    }

    [PwshAvailableFact]
    public async Task SetResultCaching_Tool_HasTypeObjectWithUserParameters()
    {
        using var configFile = new V2ComplianceConfigFile();
        var config = CreateFullConfig();

        var result = await McpToolSetupService.SetupMcpToolsAsync(
            NullLoggerFactory.Instance,
            config,
            NullLogger.Instance,
            configFile.Path,
            "runtime",
            commandExecutor: null);

        var tool = Assert.Single(result.Tools, t =>
            string.Equals(t.ProtocolTool.Name, "set-result-caching", StringComparison.OrdinalIgnoreCase));

        var schema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())?.AsObject();
        Assert.NotNull(schema);
        Assert.Equal("object", schema!["type"]?.GetValue<string>());

        var properties = schema["properties"]?.AsObject();
        Assert.NotNull(properties);
        Assert.True(properties!.Count > 0, "set-result-caching must expose at least one parameter in inputSchema");
    }

    // ── ParameterType mapping survives SDK v2 (schema generator smoke tests) ─

    [Fact]
    public void StringParameter_GeneratesStringType_InSdkV2Schema()
    {
        var tool = McpServerTool.Create(
            new Func<string, CancellationToken, Task<string>>((s, _) => Task.FromResult(s)),
            new McpServerToolCreateOptions { Name = "v2-type-probe" });

        AssertPropertyType(tool, 0, "string");
    }

    [Fact]
    public void IntParameter_GeneratesIntegerType_InSdkV2Schema()
    {
        var tool = McpServerTool.Create(
            new Func<int, CancellationToken, Task<string>>((n, _) => Task.FromResult(n.ToString())),
            new McpServerToolCreateOptions { Name = "v2-type-probe" });

        AssertPropertyType(tool, 0, "integer");
    }

    [Fact]
    public void BoolParameter_GeneratesBooleanType_InSdkV2Schema()
    {
        var tool = McpServerTool.Create(
            new Func<bool, CancellationToken, Task<string>>((b, _) => Task.FromResult(b.ToString())),
            new McpServerToolCreateOptions { Name = "v2-type-probe" });

        AssertPropertyType(tool, 0, "boolean");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static void AssertStrictEmptyInputSchema(IEnumerable<McpServerTool> tools, string toolName)
    {
        var tool = Assert.Single(tools, t =>
            string.Equals(t.ProtocolTool.Name, toolName, StringComparison.OrdinalIgnoreCase));
        var schema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())?.AsObject();

        Assert.NotNull(schema);
        Assert.Equal("object", schema!["type"]?.GetValue<string>());
        Assert.Empty(schema["properties"]?.AsObject() ?? new JsonObject());
        Assert.False(schema["additionalProperties"]?.GetValue<bool>() ?? true);
    }

    private static void AssertPropertyType(McpServerTool tool, int parameterIndex, string expectedJsonType)
    {
        var schema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())?.AsObject();
        Assert.NotNull(schema);

        var properties = schema!["properties"]?.AsObject();
        Assert.NotNull(properties);

        var propNode = properties!.ElementAt(parameterIndex).Value?.AsObject();
        Assert.NotNull(propNode);

        var actualType = propNode!["type"]?.GetValue<string>();
        Assert.Equal(expectedJsonType, actualType);
    }

    private static PowerShellConfiguration CreateFullConfig() => new()
    {
        CommandNames = new List<string> { "Get-Date" },
        Modules = new List<string>(),
        IncludePatterns = new List<string>(),
        ExcludePatterns = new List<string>(),
        EnableDynamicReloadTools = false,
        EnableConfigurationTroubleshootingTool = false
    };

    private sealed class V2ComplianceConfigFile : IDisposable
    {
        public string Path { get; }

        public V2ComplianceConfigFile()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"poshmcp-v2-compliance-{Guid.NewGuid():N}.json");
            File.WriteAllText(Path, JsonSerializer.Serialize(new
            {
                PowerShellConfiguration = new
                {
                    CommandNames = new[] { "Get-Date" },
                    Modules = Array.Empty<string>(),
                    IncludePatterns = Array.Empty<string>(),
                    ExcludePatterns = Array.Empty<string>(),
                    EnableDynamicReloadTools = false,
                    EnableConfigurationTroubleshootingTool = false
                },
                Authentication = new { Enabled = false }
            }));
        }

        public void Dispose()
        {
            if (File.Exists(Path)) File.Delete(Path);
        }
    }
}
