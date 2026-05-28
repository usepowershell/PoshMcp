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
/// End-to-end integration tests for MCP Prompts (Spec 002, FR-022 through FR-025, FR-032, FR-033).
///
/// These tests start a real server process via InProcessMcpServer and send JSON-RPC requests
/// using ExternalMcpClient. They cover all acceptance scenarios from User Stories 3 and 4.
///
/// To run only these tests:
///   dotnet test --filter "Category=McpPrompts"
/// </summary>
[Trait("Category", "Integration")]
public class McpPromptsIntegrationTests : PowerShellTestBase, IAsyncLifetime
{
    private InProcessMcpServer? _server;
    private ExternalMcpClient? _client;
    private PromptsTestFixture? _fixture;

    public McpPromptsIntegrationTests(ITestOutputHelper output) : base(output) { }

    public async Task InitializeAsync()
    {
        _fixture = new PromptsTestFixture();

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

    // ── prompts/list ─────────────────────────────────────────────────────────

    [Fact]
    public async Task PromptsList_ReturnsFilePrompt_WithCorrectMetadata()
    {
        // User Story 3, Acceptance Scenario 1 (FR-022):
        // prompts/list response includes the file-backed prompt with name, description, arguments.
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response = await client.SendListPromptsAsync();

        Assert.NotNull(response);
        var prompts = response["result"]?["prompts"] as JArray;
        Assert.NotNull(prompts);

        var filePrompt = FindPromptByName(prompts!, PromptsTestFixture.FilePromptName);
        Assert.NotNull(filePrompt);
        Assert.Equal(PromptsTestFixture.FilePromptDescription, filePrompt!["description"]?.ToString());
    }

    [Fact]
    public async Task PromptsList_RequiredArgument_AppearsWithRequiredTrue()
    {
        // User Story 3, Acceptance Scenario 2 (FR-022):
        // The required argument "serviceName" appears in the arguments array with required:true.
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response = await client.SendListPromptsAsync();

        var prompts = response["result"]?["prompts"] as JArray;
        Assert.NotNull(prompts);

        var filePrompt = FindPromptByName(prompts!, PromptsTestFixture.FilePromptName);
        Assert.NotNull(filePrompt);

        var arguments = filePrompt!["arguments"] as JArray;
        Assert.NotNull(arguments);

        var serviceNameArg = FindArgumentByName(arguments!, "serviceName");
        Assert.NotNull(serviceNameArg);
        Assert.True(serviceNameArg!["required"]?.Value<bool>() == true,
            "serviceName argument should have required:true");
    }

    [Fact]
    public async Task PromptsList_ReturnsCommandPrompt_InList()
    {
        // User Story 4: command-backed prompt appears in prompts/list.
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response = await client.SendListPromptsAsync();

        var prompts = response["result"]?["prompts"] as JArray;
        Assert.NotNull(prompts);

        var cmdPrompt = FindPromptByName(prompts!, PromptsTestFixture.CommandPromptName);
        Assert.NotNull(cmdPrompt);
    }

    [Fact]
    public async Task PromptsList_Metadata_RemainsStable_AfterPromptsGet()
    {
        // prompts/list metadata should be stable and unaffected by prompts/get execution.
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var beforeResponse = await client.SendListPromptsAsync();
        var beforePrompts = beforeResponse["result"]?["prompts"] as JArray;
        Assert.NotNull(beforePrompts);

        var getResponse = await client.SendGetPromptAsync(
          PromptsTestFixture.FilePromptName,
          new Dictionary<string, string> { ["serviceName"] = "wuauserv" });
        Assert.Null(getResponse["error"]);

        var afterResponse = await client.SendListPromptsAsync();
        var afterPrompts = afterResponse["result"]?["prompts"] as JArray;
        Assert.NotNull(afterPrompts);

        Assert.True(
          JToken.DeepEquals(beforePrompts, afterPrompts),
          $"Expected prompts/list metadata to remain stable across prompts/get. Before: {beforePrompts}; After: {afterPrompts}");
    }

    // ── prompts/get - file source ─────────────────────────────────────────────

    [Fact]
    public async Task PromptsGet_FileSource_ReturnsFileContentAsUserRoleMessage()
    {
        // User Story 3, Acceptance Scenario 3 (FR-024):
        // prompts/get for a file-backed prompt returns the file content as a user-role message.
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response = await client.SendGetPromptAsync(
          PromptsTestFixture.FilePromptName,
          new Dictionary<string, string> { ["serviceName"] = "wuauserv" });

        Assert.NotNull(response);
        Assert.Null(response["error"]);

        var messages = response["result"]?["messages"] as JArray;
        Assert.NotNull(messages);
        Assert.True(messages!.Count > 0);

        var firstMessage = messages[0]!;
        Assert.Equal("user", firstMessage["role"]?.ToString());

        var textContent = firstMessage["content"]?["text"]?.ToString();
        Assert.False(string.IsNullOrWhiteSpace(textContent));
        Assert.Contains(PromptsTestFixture.FilePromptExpectedContent, textContent, StringComparison.Ordinal);
        Assert.Contains("wuauserv", textContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{{serviceName}}", textContent, StringComparison.Ordinal);
        Assert.DoesNotContain("{serviceName}", textContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PromptsGet_FileSource_MissingRequiredArgument_ReturnsInvalidParamsError()
    {
        // Required prompt arguments are validated at prompts/get time.
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response = await client.SendGetPromptAsync(
            PromptsTestFixture.FilePromptName,
            new { }); // empty arguments

        Assert.NotNull(response);
        var error = response["error"];
        Assert.NotNull(error);

        var errorCode = error!["code"]?.Value<int>();
        Assert.Equal(-32602, errorCode);

        var message = error["message"]?.ToString();
        Assert.NotNull(message);
        Assert.Contains("serviceName", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PromptsGet_FileSource_WhitespaceRequiredArgument_ReturnsInvalidParamsError()
    {
        // Required prompt arguments should reject whitespace-only values.
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response = await client.SendGetPromptAsync(
          PromptsTestFixture.FilePromptName,
          new Dictionary<string, string> { ["serviceName"] = "   " });

        Assert.NotNull(response);
        var error = response["error"];
        Assert.NotNull(error);
        Assert.Equal(-32602, error!["code"]?.Value<int>());

        var message = error["message"]?.ToString();
        Assert.NotNull(message);
        Assert.Contains("serviceName", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PromptsGet_FileSource_NullRequiredArgument_ReturnsInvalidParamsError()
    {
        // Required prompt arguments should reject explicit null values.
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response = await client.SendGetPromptAsync(
          PromptsTestFixture.FilePromptName,
          new Dictionary<string, string?> { ["serviceName"] = null });

        Assert.NotNull(response);
        var error = response["error"];
        Assert.NotNull(error);
        Assert.Equal(-32602, error!["code"]?.Value<int>());

        var message = error["message"]?.ToString();
        Assert.NotNull(message);
        Assert.Contains("serviceName", message, StringComparison.OrdinalIgnoreCase);
    }

    // ── prompts/get - command source ──────────────────────────────────────────

    [Fact]
    public async Task PromptsGet_CommandSource_ExecutesCommandAndReturnsUserRoleMessage()
    {
        // User Story 4, Acceptance Scenario 1 (FR-025):
        // prompts/get for a command-backed prompt executes the command and returns
        // its output as a user-role message.
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response = await client.SendGetPromptAsync(PromptsTestFixture.CommandPromptName);

        Assert.NotNull(response);
        Assert.Null(response["error"]);

        var messages = response["result"]?["messages"] as JArray;
        Assert.NotNull(messages);
        Assert.True(messages!.Count > 0);

        Assert.Equal("user", messages[0]!["role"]?.ToString());

        var textContent = messages[0]!["content"]?["text"]?.ToString();
        Assert.False(string.IsNullOrWhiteSpace(textContent),
            "Command prompt should return non-empty text content");
    }

    [Fact]
    public async Task PromptsGet_CommandSource_InjectsArgumentValues_AsPowerShellVariables()
    {
        // User Story 4, Acceptance Scenario 2 (FR-032):
        // Argument values are injected as $variableName PS variables before the command runs.
        // The command "Write-Output \"Service: $serviceName\"" should produce "Service: wuauserv".
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response = await client.SendGetPromptAsync(
            PromptsTestFixture.ArgCommandPromptName,
            new Dictionary<string, string> { ["serviceName"] = "wuauserv" });

        Assert.NotNull(response);
        Assert.Null(response["error"]);

        var messages = response["result"]?["messages"] as JArray;
        Assert.NotNull(messages);

        var textContent = messages![0]?["content"]?["text"]?.ToString();
        Assert.NotNull(textContent);
        Assert.Contains("wuauserv", textContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PromptsGet_CommandSource_RendersTemplateVariablesInCommandOutput()
    {
        // Command output also supports string-based template variable replacement.
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response = await client.SendGetPromptAsync(
            PromptsTestFixture.TemplateCommandPromptName,
            new Dictionary<string, string> { ["serviceName"] = "wuauserv" });

        Assert.NotNull(response);
        Assert.Null(response["error"]);

        var messages = response["result"]?["messages"] as JArray;
        Assert.NotNull(messages);

        var textContent = messages![0]?["content"]?["text"]?.ToString();
        Assert.NotNull(textContent);
        Assert.Contains("wuauserv", textContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{{serviceName}}", textContent, StringComparison.Ordinal);
        Assert.DoesNotContain("{serviceName}", textContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PromptsGet_CommandSource_TerminatingError_ReturnsMcpError()
    {
        // User Story 4, Acceptance Scenario 3 (FR-033):
        // When the command throws a terminating error, an MCP error response is returned.
        var client = _client ?? throw new InvalidOperationException("Client not initialized");

        var response = await client.SendGetPromptAsync(PromptsTestFixture.ErrorCommandPromptName);

        Assert.NotNull(response);
        var hasTopLevelError = response["error"] != null;
        var hasResultError = response["result"]?["isError"]?.Value<bool>() == true;
        Assert.True(hasTopLevelError || hasResultError,
            $"Expected an error response for failing command prompt. Response: {response}");
    }

    private static JToken? FindPromptByName(JArray prompts, string name)
    {
        foreach (var p in prompts)
        {
            if (string.Equals(p["name"]?.ToString(), name, StringComparison.OrdinalIgnoreCase))
                return p;
        }
        return null;
    }

    private static JToken? FindArgumentByName(JArray arguments, string name)
    {
        foreach (var a in arguments)
        {
            if (string.Equals(a["name"]?.ToString(), name, StringComparison.OrdinalIgnoreCase))
                return a;
        }
        return null;
    }

    /// <summary>
    /// Creates the temporary configuration and content files used by the prompts integration tests.
    /// Config includes a file-backed prompt with a required argument, a command-backed prompt,
    /// an argument-injecting command prompt, and an error-command prompt.
    /// </summary>
    private sealed class PromptsTestFixture : IDisposable
    {
        public const string FilePromptName = "integration-test-file-prompt";
        public const string FilePromptDescription = "A file-backed prompt for integration testing";
        public const string FilePromptExpectedContent = "Analyze the service named";

        public const string CommandPromptName = "integration-test-command-prompt";
        public const string ArgCommandPromptName = "integration-test-arg-prompt";
        public const string TemplateCommandPromptName = "integration-test-template-command-prompt";
        public const string ErrorCommandPromptName = "integration-test-error-prompt";

        public string ConfigPath { get; }

        private readonly string _configDir;

        public PromptsTestFixture()
        {
            _configDir = Path.Combine(Path.GetTempPath(), $"poshmcp-prompts-int-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_configDir);

            // Create the prompt template file
            var promptFilePath = Path.Combine(_configDir, "analyze-service.md");
            File.WriteAllText(promptFilePath, $"# Service Analysis\n{FilePromptExpectedContent} {{{{serviceName}}}} / {{serviceName}}.\nProvide a detailed health report.");

            ConfigPath = Path.Combine(_configDir, "appsettings.json");

            var absolutePromptPath = promptFilePath.Replace("\\", "\\\\");
            var mustachePlaceholder = "{{serviceName}}";
            var bracePlaceholder = "{serviceName}";

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
  "McpPrompts": {
    "Prompts": [
      {
        "Name": "{{FilePromptName}}",
        "Description": "{{FilePromptDescription}}",
        "Source": "file",
        "Path": "{{absolutePromptPath}}",
        "Arguments": [
          { "Name": "serviceName", "Description": "The service to analyze", "Required": true }
        ]
      },
      {
        "Name": "{{CommandPromptName}}",
        "Description": "A command-backed prompt returning the current date",
        "Source": "command",
        "Command": "\"System state captured at: $(Get-Date)\"",
        "Arguments": []
      },
      {
        "Name": "{{ArgCommandPromptName}}",
        "Description": "A command prompt that injects argument values as PS variables",
        "Source": "command",
        "Command": "\"Service: $serviceName\"",
        "Arguments": [
          { "Name": "serviceName", "Description": "The service name to inject", "Required": true }
        ]
      },
      {
        "Name": "{{TemplateCommandPromptName}}",
        "Description": "A command prompt that returns template placeholders for post-command rendering",
        "Source": "command",
        "Command": "\"Template output: {{mustachePlaceholder}} / {{bracePlaceholder}}\"",
        "Arguments": [
          { "Name": "serviceName", "Description": "The service name used for template rendering", "Required": true }
        ]
      },
      {
        "Name": "{{ErrorCommandPromptName}}",
        "Description": "A command prompt that throws a terminating error",
        "Source": "command",
        "Command": "throw 'Deliberate integration test error'",
        "Arguments": []
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
