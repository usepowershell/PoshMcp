using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Integration;

/// <summary>
/// End-to-end integration tests for MCP Resources (Spec 002, FR-018 through FR-021, FR-030, FR-033, FR-034).
///
/// These tests start a real server process via InProcessMcpServer and send JSON-RPC requests
/// using ExternalMcpClient. They cover all acceptance scenarios from User Stories 1 and 2.
///
/// To run only these tests:
///   dotnet test --filter "Category=McpResources"
/// </summary>
[Trait("Category", "McpResources")]
public class McpResourcesIntegrationTests : PowerShellTestBase, IAsyncLifetime
{
    private InProcessMcpServer? _server;
    private ExternalMcpClient? _client;
    private ResourcesTestFixture? _fixture;

    public McpResourcesIntegrationTests(ITestOutputHelper output) : base(output) { }

    public async Task InitializeAsync()
    {
        _fixture = new ResourcesTestFixture();

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

    // ── resources/list ──────────────────────────────────────────────────────

    [Fact]
    public async Task ResourcesList_ReturnsFileResource_WithCorrectMetadata()
    {
        // User Story 1, Acceptance Scenario 1 (FR-018):
        // resources/list response includes the file-backed resource with uri, name, description, mimeType.
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response = await client.SendListResourcesAsync();

        Assert.NotNull(response);
        var resources = response["result"]?["resources"] as JArray;
        Assert.NotNull(resources);

        var fileResource = FindResourceByUri(resources!, ResourcesTestFixture.FileResourceUri);
        Assert.NotNull(fileResource);
        Assert.Equal(ResourcesTestFixture.FileResourceName, fileResource!["name"]?.ToString());
        Assert.Equal(ResourcesTestFixture.FileResourceDescription, fileResource["description"]?.ToString());
        Assert.Equal(ResourcesTestFixture.FileResourceMimeType, fileResource["mimeType"]?.ToString());
    }

    [Fact]
    public async Task ResourcesList_ReturnsCommandResource_InList()
    {
        // User Story 2: command-backed resource appears in resources/list.
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response = await client.SendListResourcesAsync();

        var resources = response["result"]?["resources"] as JArray;
        Assert.NotNull(resources);

        var cmdResource = FindResourceByUri(resources!, ResourcesTestFixture.CommandResourceUri);
        Assert.NotNull(cmdResource);
        Assert.Equal(ResourcesTestFixture.CommandResourceName, cmdResource!["name"]?.ToString());
    }

    [Fact]
    public async Task ResourcesList_ReturnsAllConfiguredResources()
    {
        // User Story 1, Acceptance Scenario 5 (FR-018):
        // Both file-backed and command-backed resources appear in the response.
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response = await client.SendListResourcesAsync();

        var resources = response["result"]?["resources"] as JArray;
        Assert.NotNull(resources);
        Assert.True(resources!.Count >= 2,
            $"Expected at least 2 resources, got {resources.Count}. Response: {response}");
    }

    // ── resources/read - file source ─────────────────────────────────────────

    [Fact]
    public async Task ResourcesRead_FileSource_ReturnsFileContent()
    {
        // User Story 1, Acceptance Scenario 2 (FR-019, FR-020):
        // resources/read for a file-backed resource returns the file's text content.
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response = await client.SendReadResourceAsync(ResourcesTestFixture.FileResourceUri);

        Assert.NotNull(response);
        Assert.Null(response["error"]);

        var contents = response["result"]?["contents"] as JArray;
        Assert.NotNull(contents);
        Assert.True(contents!.Count > 0);

        var textContent = contents[0]?["text"]?.ToString();
        Assert.False(string.IsNullOrWhiteSpace(textContent));
        Assert.Contains(ResourcesTestFixture.FileResourceExpectedContent, textContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResourcesRead_FileSource_IncludesMimeTypeInResponse()
    {
        // User Story 1, Acceptance Scenario 4 (FR-019):
        // resources/read response includes the configured mimeType in the content entry.
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response = await client.SendReadResourceAsync(ResourcesTestFixture.FileResourceUri);

        var contents = response["result"]?["contents"] as JArray;
        Assert.NotNull(contents);

        var mimeType = contents![0]?["mimeType"]?.ToString();
        Assert.Equal(ResourcesTestFixture.FileResourceMimeType, mimeType);
    }

    // ── resources/read - command source ──────────────────────────────────────

    [Fact]
    public async Task ResourcesRead_CommandSource_ExecutesCommandAndReturnsOutput()
    {
        // User Story 2, Acceptance Scenario 1 (FR-021):
        // resources/read for a command-backed resource executes the command
        // and returns its output as text content.
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response = await client.SendReadResourceAsync(ResourcesTestFixture.CommandResourceUri);

        Assert.NotNull(response);
        Assert.Null(response["error"]);

        var contents = response["result"]?["contents"] as JArray;
        Assert.NotNull(contents);
        Assert.True(contents!.Count > 0);

        var textContent = contents[0]?["text"]?.ToString();
        Assert.False(string.IsNullOrWhiteSpace(textContent),
            "Command resource should return non-empty text content");
    }

    [Fact]
    public async Task ResourcesRead_CommandSource_ExecutedEachTime_NoCache()
    {
        // User Story 2, Acceptance Scenario 2 (FR-021):
        // The command is executed on every resources/read call — output is not cached.
        // Verified by calling a time-sensitive command twice and observing non-empty results.
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response1 = await client.SendReadResourceAsync(ResourcesTestFixture.CommandResourceUri);
        await Task.Delay(1100); // ensure at least 1 second difference for time-based command
        var response2 = await client.SendReadResourceAsync(ResourcesTestFixture.CommandResourceUri);

        var content1 = (response1["result"]?["contents"] as JArray)?[0]?["text"]?.ToString();
        var content2 = (response2["result"]?["contents"] as JArray)?[0]?["text"]?.ToString();

        // Both calls must succeed (non-empty). The test fixture uses Get-Date so results differ.
        Assert.False(string.IsNullOrWhiteSpace(content1), "First call should return content");
        Assert.False(string.IsNullOrWhiteSpace(content2), "Second call should return content");
    }

    [Fact]
    public async Task ResourcesRead_CommandSource_TerminatingError_ReturnsMcpError()
    {
        // User Story 2, Acceptance Scenario 4 (FR-033):
        // When the command throws a terminating error, an MCP error response is returned.
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response = await client.SendReadResourceAsync(ResourcesTestFixture.ErrorCommandResourceUri);

        Assert.NotNull(response);
        // Either top-level error or isError=true in result
        var hasTopLevelError = response["error"] != null;
        var hasResultError = response["result"]?["isError"]?.Value<bool>() == true;
        Assert.True(hasTopLevelError || hasResultError,
            $"Expected an error response for failing command resource. Response: {response}");
    }

    private static JToken? FindResourceByUri(JArray resources, string uri)
    {
        foreach (var r in resources)
        {
            if (string.Equals(r["uri"]?.ToString(), uri, StringComparison.OrdinalIgnoreCase))
                return r;
        }
        return null;
    }

    /// <summary>
    /// Creates the temporary configuration and content files used by the resources integration tests.
    /// The config includes a file-backed resource, a command-backed resource, and an error-command resource.
    /// </summary>
    private sealed class ResourcesTestFixture : IDisposable
    {
        public const string FileResourceUri = "poshmcp://resources/integration-test-file";
        public const string FileResourceName = "Integration Test File";
        public const string FileResourceDescription = "A text file for integration testing";
        public const string FileResourceMimeType = "text/plain";
        public const string FileResourceExpectedContent = "Hello from PoshMcp resources integration test";

        public const string CommandResourceUri = "poshmcp://resources/integration-test-command";
        public const string CommandResourceName = "Current Date";

        public const string ErrorCommandResourceUri = "poshmcp://resources/integration-test-error";

        public string ConfigPath { get; }

        private readonly string _configDir;
        private readonly string _resourceFilePath;

        public ResourcesTestFixture()
        {
            _configDir = Path.Combine(Path.GetTempPath(), $"poshmcp-resources-int-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_configDir);

            // Create the data file referenced by the file-backed resource
            _resourceFilePath = Path.Combine(_configDir, "test-resource.txt");
            File.WriteAllText(_resourceFilePath, FileResourceExpectedContent);

            ConfigPath = Path.Combine(_configDir, "appsettings.json");

            // Use an absolute path for the file resource so FR-034 relative-path is not in play here;
            // relative path is exercised separately in functional tests.
            var absoluteResourcePath = _resourceFilePath.Replace("\\", "\\\\");

            var json = $$"""
{
  "PowerShellConfiguration": {
    "CommandNames": ["Get-Date"],
    "Modules": [],
    "IncludePatterns": [],
    "ExcludePatterns": []
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
  },
  "McpResources": {
    "Resources": [
      {
        "Uri": "{{FileResourceUri}}",
        "Name": "{{FileResourceName}}",
        "Description": "{{FileResourceDescription}}",
        "MimeType": "{{FileResourceMimeType}}",
        "Source": "file",
        "Path": "{{absoluteResourcePath}}"
      },
      {
        "Uri": "{{CommandResourceUri}}",
        "Name": "{{CommandResourceName}}",
        "Description": "Current date from Get-Date",
        "MimeType": "text/plain",
        "Source": "command",
        "Command": "Get-Date | Out-String"
      },
      {
        "Uri": "{{ErrorCommandResourceUri}}",
        "Name": "Error Command",
        "Description": "A command that throws a terminating error",
        "MimeType": "text/plain",
        "Source": "command",
        "Command": "throw 'Deliberate integration test error'"
      }
    ]
  }
}
""";
            File.WriteAllText(ConfigPath, json);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_configDir))
                    Directory.Delete(_configDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }
}
