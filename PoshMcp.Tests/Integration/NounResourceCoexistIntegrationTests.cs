using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Integration;

/// <summary>
/// Integration tests for FR-NR-14: Static and noun-derived resources coexist.
///
/// When both McpResources.Resources[] (statically configured) and EnableNounResources = true
/// are configured, resources/list returns the union of both sets and resources/read resolves
/// from the combined set with static taking priority.
///
/// Spec 012, Section 6.2, FR-NR-14.
///
/// To run only these tests:
///   dotnet test --filter "Category=NounCoexist"
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "NounCoexist")]
public class NounResourceCoexistIntegrationTests : PowerShellTestBase, IAsyncLifetime
{
    private InProcessMcpServer? _server;
    private ExternalMcpClient? _client;
    private NounCoexistTestFixture? _fixture;

    public NounResourceCoexistIntegrationTests(ITestOutputHelper output) : base(output) { }

    public async Task InitializeAsync()
    {
        _fixture = new NounCoexistTestFixture();

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

    // ── resources/list — union behavior ─────────────────────────────────────

    /// <summary>
    /// FR-NR-14: resources/list includes the static-only resource (not derived from any noun).
    /// </summary>
    [Fact]
    public async Task ResourcesList_BothSetsConfigured_IncludesStaticOnlyResource()
    {
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response = await client.SendListResourcesAsync();

        Assert.Null(response["error"]);
        var resources = response["result"]?["resources"] as JArray;
        Assert.NotNull(resources);

        var found = FindResourceByUri(resources!, NounCoexistTestFixture.StaticOnlyResourceUri);
        Assert.NotNull(found);
        Assert.Equal(NounCoexistTestFixture.StaticOnlyResourceName, found!["name"]?.ToString());
    }

    /// <summary>
    /// FR-NR-14: resources/list includes the noun-derived resource (Get-Random → random).
    /// </summary>
    [Fact]
    public async Task ResourcesList_BothSetsConfigured_IncludesNounDerivedResource()
    {
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response = await client.SendListResourcesAsync();

        Assert.Null(response["error"]);
        var resources = response["result"]?["resources"] as JArray;
        Assert.NotNull(resources);

        var found = FindResourceByUri(resources!, NounCoexistTestFixture.RandomNounResourceUri);
        Assert.NotNull(found);
    }

    /// <summary>
    /// FR-NR-14: resources/list returns the union — at least one static and one noun resource.
    /// </summary>
    [Fact]
    public async Task ResourcesList_BothSetsConfigured_ReturnsAtLeastStaticAndNounResource()
    {
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response = await client.SendListResourcesAsync();

        Assert.Null(response["error"]);
        var resources = response["result"]?["resources"] as JArray;
        Assert.NotNull(resources);

        // At minimum: the static-only resource + the random noun resource
        Assert.True(resources!.Count >= 2,
            $"Expected at least 2 resources (static + noun), got {resources.Count}. Response: {response}");
    }

    // ── resources/list — duplicate URI conflict resolution ───────────────────

    /// <summary>
    /// FR-NR-14: When static and noun share a URI (poshmcp://resources/date), it appears
    /// exactly once in resources/list (no duplicate).
    /// </summary>
    [Fact]
    public async Task ResourcesList_DuplicateUri_AppearsExactlyOnce()
    {
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response = await client.SendListResourcesAsync();

        Assert.Null(response["error"]);
        var resources = response["result"]?["resources"] as JArray;
        Assert.NotNull(resources);

        var conflictedEntries = resources!
            .Where(r => string.Equals(
                r["uri"]?.ToString(),
                NounCoexistTestFixture.ConflictedResourceUri,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Single(conflictedEntries);
    }

    /// <summary>
    /// FR-NR-14: When static and noun share a URI, the resource appearing in resources/list
    /// is the static one (by name).
    /// </summary>
    [Fact]
    public async Task ResourcesList_DuplicateUri_StaticResourceWins()
    {
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response = await client.SendListResourcesAsync();

        Assert.Null(response["error"]);
        var resources = response["result"]?["resources"] as JArray;
        Assert.NotNull(resources);

        var conflictedEntry = FindResourceByUri(resources!, NounCoexistTestFixture.ConflictedResourceUri);
        Assert.NotNull(conflictedEntry);
        Assert.Equal(NounCoexistTestFixture.ConflictedStaticResourceName, conflictedEntry!["name"]?.ToString());
    }

    // ── resources/read — static and noun resolved correctly ──────────────────

    /// <summary>
    /// FR-NR-14: resources/read resolves a static-only resource from the combined set.
    /// </summary>
    [Fact]
    public async Task ResourcesRead_StaticOnlyResource_ReturnsStaticContent()
    {
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response = await client.SendReadResourceAsync(NounCoexistTestFixture.StaticOnlyResourceUri);

        Assert.Null(response["error"]);
        var contents = response["result"]?["contents"] as JArray;
        Assert.NotNull(contents);
        Assert.True(contents!.Count > 0);

        var text = contents[0]?["text"]?.ToString();
        Assert.False(string.IsNullOrWhiteSpace(text), "Static-only resource should return non-empty content");
        Assert.Contains(NounCoexistTestFixture.StaticOnlyResourceExpectedContent, text!, StringComparison.Ordinal);
    }

    /// <summary>
    /// FR-NR-14: resources/read resolves a noun-derived resource from the combined set.
    /// </summary>
    [Fact]
    public async Task ResourcesRead_NounDerivedResource_ReturnsContent()
    {
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response = await client.SendReadResourceAsync(NounCoexistTestFixture.RandomNounResourceUri);

        Assert.Null(response["error"]);
        var contents = response["result"]?["contents"] as JArray;
        Assert.NotNull(contents);
        Assert.True(contents!.Count > 0);

        var text = contents[0]?["text"]?.ToString();
        Assert.False(string.IsNullOrWhiteSpace(text), "Noun-derived resource (Get-Random) should return non-empty content");
    }

    // ── resources/read — conflict resolution (static wins) ───────────────────

    /// <summary>
    /// FR-NR-14: When static and noun share a URI, resources/read returns the static content,
    /// not the noun-derived content. Static handler is tried first and wins.
    /// </summary>
    [Fact]
    public async Task ResourcesRead_DuplicateUri_StaticContentIsReturned()
    {
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response = await client.SendReadResourceAsync(NounCoexistTestFixture.ConflictedResourceUri);

        Assert.Null(response["error"]);
        var contents = response["result"]?["contents"] as JArray;
        Assert.NotNull(contents);
        Assert.True(contents!.Count > 0);

        var text = contents[0]?["text"]?.ToString();
        Assert.False(string.IsNullOrWhiteSpace(text), "Conflicted URI should resolve to static content");
        Assert.Contains(NounCoexistTestFixture.ConflictedStaticContent, text!, StringComparison.Ordinal);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

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
    /// Creates the temporary configuration used by the noun-coexist integration tests.
    ///
    /// Config layout:
    ///   - CommandNames: ["Get-Date", "Get-Random"], EnableNounResources: true
    ///     → noun resources: poshmcp://resources/date, poshmcp://resources/random
    ///   - Static resources:
    ///       poshmcp://resources/static-coexist-only  (no conflict, unique to static set)
    ///       poshmcp://resources/date                 (same URI as Get-Date noun — static wins)
    /// </summary>
    private sealed class NounCoexistTestFixture : IDisposable
    {
        // Static-only resource (no noun conflict)
        public const string StaticOnlyResourceUri = "poshmcp://resources/static-coexist-only";
        public const string StaticOnlyResourceName = "Static Coexist Only";
        public const string StaticOnlyResourceExpectedContent = "STATIC_COEXIST_CONTENT";

        // Conflicted resource: same URI as the noun resource for Get-Date
        public const string ConflictedResourceUri = "poshmcp://resources/date";
        public const string ConflictedStaticResourceName = "Date (Static Override)";
        public const string ConflictedStaticContent = "STATIC_DATE_OVERRIDE";

        // Noun-derived resource for Get-Random (not conflicted)
        public const string RandomNounResourceUri = "poshmcp://resources/random";

        public string ConfigPath { get; }

        private readonly string _configDir;

        public NounCoexistTestFixture()
        {
            _configDir = Path.Combine(Path.GetTempPath(), $"poshmcp-noun-coexist-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_configDir);

            ConfigPath = Path.Combine(_configDir, "appsettings.json");

            var json = $$"""
{
  "PowerShellConfiguration": {
    "CommandNames": ["Get-Date", "Get-Random"],
    "Modules": [],
    "IncludePatterns": [],
    "ExcludePatterns": [],
    "EnableNounResources": true
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
        "Uri": "{{StaticOnlyResourceUri}}",
        "Name": "{{StaticOnlyResourceName}}",
        "Description": "A static resource with no noun-derived counterpart",
        "MimeType": "text/plain",
        "Source": "command",
        "Command": "'{{StaticOnlyResourceExpectedContent}}'"
      },
      {
        "Uri": "{{ConflictedResourceUri}}",
        "Name": "{{ConflictedStaticResourceName}}",
        "Description": "Static override: same URI as the Get-Date noun resource",
        "MimeType": "text/plain",
        "Source": "command",
        "Command": "'{{ConflictedStaticContent}}'"
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
