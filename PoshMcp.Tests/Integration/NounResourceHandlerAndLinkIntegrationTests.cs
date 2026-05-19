using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Integration;

/// <summary>
/// Integration coverage for noun-derived resource handling and resource-link injection.
///
/// Scope:
/// - #287 (McpNounResourceHandler): FR-NR-05/06/07
/// - #285 (ResourceLinkInjectorWrapper): FR-NR-08/08A/09/10
///
/// To run only these tests:
///   dotnet test --filter "Category=NounResourceCoverage"
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "NounResourceCoverage")]
public class NounResourceHandlerAndLinkIntegrationTests : PowerShellTestBase, IAsyncLifetime
{
    private const string FixtureResourceUri = "poshmcp://resources/noun_resource_fixture";
    private const string OverrideFixtureResourceName = "fixture_override";
    private const string OverrideFixtureResourceUri = "poshmcp://resources/fixture_override";
    private const string MissingResourceUri = "poshmcp://resources/does_not_exist";

    private InProcessMcpServer? _server;
    private ExternalMcpClient? _client;
    private NounResourceFixtureConfig? _fixture;

    public NounResourceHandlerAndLinkIntegrationTests(ITestOutputHelper output) : base(output) { }

    public async Task InitializeAsync()
    {
        _fixture = new NounResourceFixtureConfig();

        _server = new InProcessMcpServer(Logger, explicitConfigPath: _fixture.ConfigPath);
        await _server.StartAsync();

        _client = new ExternalMcpClient(Logger, _server);
        await _client.StartAsync();
    }

    public Task DisposeAsync()
    {
        _client?.Dispose();
        _server?.Dispose();
        _fixture?.Dispose();
        return Task.CompletedTask;
    }

    // #287 — McpNounResourceHandler integration coverage

    [Fact]
    public async Task ResourcesList_EnableNounResources_ContainsDerivedFixtureResourceWithJsonMimeType()
    {
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response = await client.SendListResourcesAsync();

        Assert.Null(response["error"]);
        var resources = response["result"]?["resources"] as JArray;
        Assert.NotNull(resources);

        var fixtureResource = FindResourceByUri(resources!, FixtureResourceUri);
        Assert.NotNull(fixtureResource);
        Assert.Equal("application/json", fixtureResource!["mimeType"]?.ToString());

        // FR-NR-10 precondition: non-resourceable noun should not be listed.
        var nonResourceable = FindResourceByUri(resources!, "poshmcp://resources/no_get_fixture");
        Assert.Null(nonResourceable);
    }

    [Fact]
    public async Task ResourcesRead_DerivedFixtureResource_ExecutesGetCommandAndReturnsSerializedPayload()
    {
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response = await client.SendReadResourceAsync(FixtureResourceUri);

        Assert.Null(response["error"]);
        var contents = response["result"]?["contents"] as JArray;
        Assert.NotNull(contents);
        Assert.True(contents!.Count > 0);

        var payload = contents[0]?["text"]?.ToString();
        Assert.False(string.IsNullOrWhiteSpace(payload));

        var json = JObject.Parse(payload!);
        Assert.Equal("NounResourceFixture", json["Name"]?.ToString());
        Assert.Equal(42, json["Value"]?.Value<int>());
    }

    [Fact]
    public async Task ResourcesRead_UnknownResourceUri_ReturnsResourceNotFound()
    {
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response = await client.SendReadResourceAsync(MissingResourceUri);

        var error = response["error"] as JObject;
        Assert.NotNull(error);
        Assert.Contains("Resource not found", error!["message"]?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    // #285 — ResourceLinkInjectorWrapper integration coverage

    [Fact]
    public async Task ToolCall_AssertCommandWithResourceableNoun_AppendsResourceLinkBlock()
    {
        var client = _client ?? throw new InvalidOperationException("Client not initialized");
        var toolName = await ResolveToolNameAsync(client, "Assert-NounResourceFixture");

        var response = await client.SendToolCallAsync(toolName, new { });

        Assert.Null(response["error"]);
        Assert.NotEqual(true, response["result"]?["isError"]?.Value<bool>());

        var content = response["result"]?["content"] as JArray;
        Assert.NotNull(content);
        AssertHasExactResourceLinkBlock(content!, FixtureResourceUri, "noun_resource_fixture");
    }

    [Fact]
    public async Task ToolCall_GetCommandWithResourceableNoun_AppendsResourceLinkBlock()
    {
        var client = _client ?? throw new InvalidOperationException("Client not initialized");
        var toolName = await ResolveToolNameAsync(client, "Get-NounResourceFixture");

        var response = await client.SendToolCallAsync(toolName, new { });

        Assert.Null(response["error"]);
        Assert.NotEqual(true, response["result"]?["isError"]?.Value<bool>());

        var content = response["result"]?["content"] as JArray;
        Assert.NotNull(content);
        AssertHasExactResourceLinkBlock(content!, FixtureResourceUri, "noun_resource_fixture");
    }

    [Fact]
    public async Task ToolCall_CommandThrowPath_UsesIsErrorFlagForResourceLinkInjection()
    {
        var client = _client ?? throw new InvalidOperationException("Client not initialized");
        var toolName = await ResolveToolNameAsync(client, "Get-NounResourceFixtureError");

        var response = await client.SendToolCallAsync(toolName, new { });

        Assert.Null(response["error"]);
        var resultIsError = response["result"]?["isError"]?.Value<bool>() == true;

        var content = response["result"]?["content"] as JArray;
        Assert.NotNull(content);

        if (resultIsError)
        {
            Assert.False(HasAnyResourceLinkBlock(content!),
                $"isError=true result should not include resource-link block. Response: {response}");
            return;
        }

        // Current implementation for this fixture path reports a non-error result
        // containing error text; injector behavior follows IsError strictly.
        AssertHasExactResourceLinkBlock(
            content!,
            "poshmcp://resources/noun_resource_fixture_error",
            "noun_resource_fixture_error");
    }

    [Fact]
    public async Task ToolCall_NonResourceableNoun_DoesNotAppendResourceLinkBlock()
    {
        var client = _client ?? throw new InvalidOperationException("Client not initialized");
        var toolName = await ResolveToolNameAsync(client, "Assert-NoGetFixture");

        var response = await client.SendToolCallAsync(toolName, new { });

        Assert.Null(response["error"]);
        Assert.NotEqual(true, response["result"]?["isError"]?.Value<bool>());

        var content = response["result"]?["content"] as JArray;
        Assert.NotNull(content);
        Assert.False(HasAnyResourceLinkBlock(content!),
            $"Non-resourceable noun should not include resource-link block. Response: {response}");
    }

    [Fact]
    public async Task FeatureDisabled_DoesNotListOrReadDerivedResources_AndDoesNotInjectResourceLinks()
    {
        using var fixture = new NounResourceFixtureConfig(enableNounResources: false);

        await ExecuteWithFixtureAsync(fixture, async client =>
        {
            var listResponse = await client.SendListResourcesAsync();

            Assert.Null(listResponse["error"]);
            var resources = listResponse["result"]?["resources"] as JArray;
            Assert.NotNull(resources);
            Assert.Null(FindResourceByUri(resources!, FixtureResourceUri));

            var readResponse = await client.SendReadResourceAsync(FixtureResourceUri);
            var readError = readResponse["error"] as JObject;
            Assert.NotNull(readError);
            Assert.Contains("Resource not found", readError!["message"]?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);

            var toolName = await ResolveToolNameAsync(client, "Assert-NounResourceFixture");
            var toolResponse = await client.SendToolCallAsync(toolName, new { });

            Assert.Null(toolResponse["error"]);
            var content = toolResponse["result"]?["content"] as JArray;
            Assert.NotNull(content);
            Assert.False(HasAnyResourceLinkBlock(content!),
                            $"Feature-disabled tool result should not include resource-link block. Response: {toolResponse}");
        });
    }

    [Fact]
    public async Task DisabledOverride_SuppressesListedAndReadableResource_AndSkipsResourceLinkInjection()
    {
        const string disabledOverrideJson = """
{
    "noun_resource_fixture": {
        "Disabled": true
    }
}
""";

        using var fixture = new NounResourceFixtureConfig(nounResourceOverridesJson: disabledOverrideJson);

        await ExecuteWithFixtureAsync(fixture, async client =>
        {
            var listResponse = await client.SendListResourcesAsync();

            Assert.Null(listResponse["error"]);
            var resources = listResponse["result"]?["resources"] as JArray;
            Assert.NotNull(resources);
            Assert.Null(FindResourceByUri(resources!, FixtureResourceUri));

            var readResponse = await client.SendReadResourceAsync(FixtureResourceUri);
            var readError = readResponse["error"] as JObject;
            Assert.NotNull(readError);
            Assert.Contains("Resource not found", readError!["message"]?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);

            var toolName = await ResolveToolNameAsync(client, "Assert-NounResourceFixture");
            var toolResponse = await client.SendToolCallAsync(toolName, new { });

            Assert.Null(toolResponse["error"]);
            var content = toolResponse["result"]?["content"] as JArray;
            Assert.NotNull(content);
            Assert.False(HasAnyResourceLinkBlock(content!),
                            $"Disabled noun override should suppress resource-link block. Response: {toolResponse}");
        });
    }

    [Fact]
    public async Task OverrideResourceNameAndUri_AreExposedThroughListReadAndResourceLinkInjection()
    {
        const string overrideJson = """
{
    "noun_resource_fixture": {
        "ResourceName": "fixture_override",
        "Uri": "poshmcp://resources/fixture_override"
    }
}
""";

        using var fixture = new NounResourceFixtureConfig(nounResourceOverridesJson: overrideJson);

        await ExecuteWithFixtureAsync(fixture, async client =>
        {
            var listResponse = await client.SendListResourcesAsync();

            Assert.Null(listResponse["error"]);
            var resources = listResponse["result"]?["resources"] as JArray;
            Assert.NotNull(resources);

            var overriddenResource = FindResourceByUri(resources!, OverrideFixtureResourceUri);
            Assert.NotNull(overriddenResource);
            Assert.Equal(OverrideFixtureResourceName, overriddenResource!["name"]?.ToString());
            Assert.Null(FindResourceByUri(resources!, FixtureResourceUri));

            var readResponse = await client.SendReadResourceAsync(OverrideFixtureResourceUri);
            Assert.Null(readResponse["error"]);
            var contents = readResponse["result"]?["contents"] as JArray;
            Assert.NotNull(contents);
            Assert.True(contents!.Count > 0);

            var payload = contents[0]?["text"]?.ToString();
            Assert.False(string.IsNullOrWhiteSpace(payload));
            var json = JObject.Parse(payload!);
            Assert.Equal("NounResourceFixture", json["Name"]?.ToString());
            Assert.Equal(42, json["Value"]?.Value<int>());

            var toolName = await ResolveToolNameAsync(client, "Assert-NounResourceFixture");
            var toolResponse = await client.SendToolCallAsync(toolName, new { });

            Assert.Null(toolResponse["error"]);
            var content = toolResponse["result"]?["content"] as JArray;
            Assert.NotNull(content);
            AssertHasExactResourceLinkBlock(content!, OverrideFixtureResourceUri, OverrideFixtureResourceName);
        });
    }

    private static JToken? FindResourceByUri(JArray resources, string uri)
    {
        return resources.FirstOrDefault(r => string.Equals(r["uri"]?.ToString(), uri, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasAnyResourceLinkBlock(JArray content)
    {
        foreach (var block in content.OfType<JObject>())
        {
            if (!string.Equals(block["type"]?.ToString(), "resource", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var resource = block["resource"] as JObject;
            if (resource is null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(resource["uri"]?.ToString()))
            {
                return true;
            }
        }

        return false;
    }

    private static void AssertHasExactResourceLinkBlock(JArray content, string expectedUri, string expectedResourceName)
    {
        Assert.True(content.Count > 0, "Expected tool result to contain at least one content block.");

        var lastBlock = content[^1] as JObject;
        Assert.NotNull(lastBlock);
        Assert.Equal("resource", lastBlock!["type"]?.ToString());

        var resource = lastBlock["resource"] as JObject;
        Assert.NotNull(resource);
        Assert.Equal(expectedUri, resource!["uri"]?.ToString());
        Assert.Equal("application/json+mcp-resource-link", resource["mimeType"]?.ToString());

        var text = resource["text"]?.ToString();
        Assert.False(string.IsNullOrWhiteSpace(text), "resource.text should contain the serialized resourceLink payload.");

        var parsed = JObject.Parse(text!);
        var resourceLink = parsed["resourceLink"] as JObject;
        Assert.NotNull(resourceLink);
        Assert.Equal(expectedUri, resourceLink!["uri"]?.ToString());
        Assert.Equal(expectedResourceName, resourceLink["resourceName"]?.ToString());
        Assert.False(string.IsNullOrWhiteSpace(resourceLink["noun"]?.ToString()));
        Assert.Equal("subject", resourceLink["relationship"]?.ToString());
        Assert.False(string.IsNullOrWhiteSpace(resourceLink["description"]?.ToString()));
    }

    private async Task ExecuteWithFixtureAsync(NounResourceFixtureConfig fixture, Func<ExternalMcpClient, Task> assertion)
    {
        InProcessMcpServer? server = null;
        ExternalMcpClient? client = null;

        try
        {
            server = new InProcessMcpServer(Logger, explicitConfigPath: fixture.ConfigPath);
            await server.StartAsync();

            client = new ExternalMcpClient(Logger, server);
            await client.StartAsync();

            await assertion(client);
        }
        finally
        {
            client?.Dispose();
            server?.Dispose();
        }
    }

    private static async Task<string> ResolveToolNameAsync(ExternalMcpClient client, string commandTitle)
    {
        var toolsResponse = await client.SendListToolsAsync();
        Assert.Null(toolsResponse["error"]);

        var tools = toolsResponse["result"]?["tools"] as JArray;
        Assert.NotNull(tools);

        foreach (var tool in tools!.OfType<JObject>())
        {
            var title = tool["title"]?.ToString();
            if (!string.Equals(title, commandTitle, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = tool["name"]?.ToString();
            Assert.False(string.IsNullOrWhiteSpace(name), $"Tool '{commandTitle}' has no MCP name.");
            return name!;
        }

        throw new InvalidOperationException($"Tool title '{commandTitle}' not found in tools/list response: {toolsResponse}");
    }

    private sealed class NounResourceFixtureConfig : IDisposable
    {
        public string ConfigPath { get; }

        private readonly string _configDir;
        private readonly string? _previousModulePath;

        public NounResourceFixtureConfig(bool enableNounResources = true, string? nounResourceOverridesJson = null)
        {
            var repoRoot = ResolveWorkspaceRoot();
            var moduleRoot = Path.Combine(repoRoot, "PoshMcp.Tests", "Fixtures", "Modules");
            var moduleManifest = Path.Combine(moduleRoot, "NounResourceFixture", "NounResourceFixture.psd1");
            if (!File.Exists(moduleManifest))
            {
                throw new FileNotFoundException($"NounResourceFixture module manifest not found: {moduleManifest}");
            }

            _configDir = Path.Combine(Path.GetTempPath(), $"poshmcp-noun-resource-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_configDir);
            ConfigPath = Path.Combine(_configDir, "appsettings.json");

            _previousModulePath = Environment.GetEnvironmentVariable("PSModulePath");
            var updatedModulePath = string.IsNullOrWhiteSpace(_previousModulePath)
                ? moduleRoot
                : moduleRoot + Path.PathSeparator + _previousModulePath;
            Environment.SetEnvironmentVariable("PSModulePath", updatedModulePath);

            var nounResourceOverridesBlock = string.IsNullOrWhiteSpace(nounResourceOverridesJson)
                ? string.Empty
                : $",\n    \"NounResourceOverrides\": {nounResourceOverridesJson}";

            var json = $$"""
{
  "PowerShellConfiguration": {
    "CommandNames": [
      "Get-NounResourceFixture",
      "Assert-NounResourceFixture",
      "Get-NounResourceFixtureError",
      "Assert-NoGetFixture"
    ],
    "Modules": ["NounResourceFixture"],
    "IncludePatterns": [],
    "ExcludePatterns": [],
        "EnableNounResources": {{enableNounResources.ToString().ToLowerInvariant()}}{{nounResourceOverridesBlock}}
  },
  "Authentication": {
    "Enabled": false,
    "DefaultScheme": "Bearer",
    "DefaultPolicy": {
      "RequireAuthentication": true,
      "RequiredScopes": [],
      "RequiredRoles": []
    },
    "Schemes": {}
  }
}
""";

            File.WriteAllText(ConfigPath, json);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("PSModulePath", _previousModulePath);

            try
            {
                if (Directory.Exists(_configDir))
                {
                    Directory.Delete(_configDir, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        private static string ResolveWorkspaceRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null)
            {
                var solutionPath = Path.Combine(current.FullName, "PoshMcp.sln");
                if (File.Exists(solutionPath))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new InvalidOperationException("Unable to locate workspace root containing PoshMcp.sln.");
        }
    }
}
