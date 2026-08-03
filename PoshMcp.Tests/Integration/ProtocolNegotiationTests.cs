using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Integration;

/// <summary>
/// Validates MCP v2 protocol negotiation for both transport modes.
///
/// <b>Behavior Matrix:</b>
/// <code>
/// | Path                                               | Protocol    | tools/list | tools/call | Session ID  |
/// |----------------------------------------------------|-------------|:----------:|:----------:|:-----------:|
/// | server/discover (stateless default)                | 2026-07-28  |     ✓      |     —      | —           |
/// | tools/list direct (stateless default)              | any         |     ✓      |     —      | —           |
/// | tools/call direct (stateless default)              | any         |     —      |     ✓      | —           |
/// | initialize 2024-11-05 (stateless, compat)          | 2024-11-05  |     ✓      |     ✓      | —           |
/// | initialize 2024-11-05 (stateful compat mode)       | 2024-11-05  |     ✓      |     ✓      |     ✓       |
/// | initialize 2025-11-25 (stateful compat mode)       | 2025-11-25  |     ✓      |     ✓      |     ✓       |
/// </code>
/// Stateless is the production default; stateful compat tests use an explicit test-host configuration.
/// Protocol negotiation does not mutate the configured transport mode.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "ProtocolNegotiation")]
[Collection("TransportSelectionTests")]
public class ProtocolNegotiationTests : PowerShellTestBase
{
    public ProtocolNegotiationTests(ITestOutputHelper output) : base(output) { }

    // -------------------------------------------------------------------------
    // Stateless default — server/discover and direct tool access
    // Uses InProcessUnifiedHttpServer (production default: Stateless = true).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ServerDiscover_StatelessDefault_Succeeds()
    {
        using var server = new InProcessUnifiedHttpServer();
        await server.StartAsync();

        using var client = CreateMcpClient(server.ServerUrl);
        using var response = await PostDiscoverAsync(client);

        var body = await response.Content.ReadAsStringAsync();
        Output.WriteLine($"server/discover status: {(int)response.StatusCode}");
        Output.WriteLine($"server/discover body: {body}");

        Assert.True(response.IsSuccessStatusCode,
            $"server/discover must succeed in stateless mode (got {(int)response.StatusCode}): {body}");

        var result = ParseJsonOrSse(body, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("2.0", result["jsonrpc"]?.ToString());
        Assert.NotNull(result["result"]);
        Assert.Null(result["error"]);
    }

    [Fact]
    public async Task ServerDiscover_StatelessDefault_ReturnsServerInfo()
    {
        using var server = new InProcessUnifiedHttpServer();
        await server.StartAsync();

        using var client = CreateMcpClient(server.ServerUrl);
        using var response = await PostDiscoverAsync(client);

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"server/discover failed: {body}");

        var result = ParseJsonOrSse(body, response.Content.Headers.ContentType?.MediaType);
        var discoveryResult = result["result"];
        Assert.NotNull(discoveryResult);

        // server/discover result must include at minimum serverInfo or capabilities
        var hasServerInfo = discoveryResult?["serverInfo"] != null;
        var hasCapabilities = discoveryResult?["capabilities"] != null;
        var hasProtocolVersion = discoveryResult?["protocolVersion"] != null;
        Output.WriteLine($"result keys: serverInfo={hasServerInfo}, capabilities={hasCapabilities}, protocolVersion={hasProtocolVersion}");
        Assert.True(hasServerInfo || hasCapabilities || hasProtocolVersion,
            $"server/discover result must include server info, capabilities, or protocolVersion. Got: {discoveryResult}");
    }

    [Fact]
    public async Task ToolsList_StatelessDefault_WorksWithoutSession()
    {
        using var server = new InProcessUnifiedHttpServer();
        await server.StartAsync();

        using var client = CreateMcpClient(server.ServerUrl);
        using var response = await PostRawAsync(client,
            new { jsonrpc = "2.0", id = 1, method = "tools/list" });

        var body = await response.Content.ReadAsStringAsync();
        Output.WriteLine($"tools/list: {body}");
        Assert.True(response.IsSuccessStatusCode, $"tools/list in stateless mode failed: {body}");

        var result = ParseJsonOrSse(body, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("2.0", result["jsonrpc"]?.ToString());
        Assert.NotNull(result["result"]?["tools"]);
        var tools = result["result"]!["tools"] as JArray;
        Assert.NotEmpty(tools!);
    }

    [Fact]
    public async Task ToolsCall_StatelessDefault_Succeeds()
    {
        using var server = new InProcessUnifiedHttpServer();
        await server.StartAsync();

        using var client = CreateMcpClient(server.ServerUrl);

        // Get a callable tool name
        using var listResponse = await PostRawAsync(client,
            new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        var listBody = await listResponse.Content.ReadAsStringAsync();
        Assert.True(listResponse.IsSuccessStatusCode, $"tools/list failed: {listBody}");
        var listResult = ParseJsonOrSse(listBody, listResponse.Content.Headers.ContentType?.MediaType);
        var tools = listResult["result"]!["tools"] as JArray;
        Assert.NotNull(tools);

        var toolName = tools!
            .Select(t => t?["name"]?.ToString())
            .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n) &&
                (n.StartsWith("get_date", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(n, "get_last_command_output", StringComparison.OrdinalIgnoreCase)));
        Assert.False(string.IsNullOrWhiteSpace(toolName), "Expected a callable tool in stateless mode");

        using var callResponse = await PostRawAsync(client, new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/call",
            @params = new { name = toolName, arguments = new { } }
        });
        var callBody = await callResponse.Content.ReadAsStringAsync();
        Output.WriteLine($"tools/call [{toolName}]: {callBody}");
        Assert.True(callResponse.IsSuccessStatusCode, $"tools/call in stateless mode failed: {callBody}");

        var callResult = ParseJsonOrSse(callBody, callResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("2.0", callResult["jsonrpc"]?.ToString());
        Assert.NotNull(callResult["result"]);
        Assert.Null(callResult["error"]);
    }

    [Fact]
    public async Task BackwardCompat_Initialize_DownLevel_202411_InStatelessMode_Succeeds()
    {
        // Proves down-level clients using initialize with 2024-11-05 still work in stateless mode.
        // No Mcp-Session-Id is returned (stateless transport), but the MCP protocol succeeds.
        using var server = new InProcessUnifiedHttpServer();
        await server.StartAsync();

        using var client = CreateMcpClient(server.ServerUrl);
        using var response = await PostRawAsync(client, new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { tools = new { } },
                clientInfo = new { name = "compat-test", version = "1.0.0" }
            }
        });

        var body = await response.Content.ReadAsStringAsync();
        Output.WriteLine($"initialize 2024-11-05 (stateless): {body}");
        Assert.True(response.IsSuccessStatusCode, $"initialize 2024-11-05 failed in stateless mode: {body}");

        var result = ParseJsonOrSse(body, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("2.0", result["jsonrpc"]?.ToString());
        Assert.NotNull(result["result"]);
        Assert.Null(result["error"]);

        // Stateless mode must NOT return Mcp-Session-Id
        var hasSessionId = response.Headers.TryGetValues("Mcp-Session-Id", out var vals)
            && vals.Any(v => !string.IsNullOrWhiteSpace(v));
        Assert.False(hasSessionId, "Stateless mode must not return Mcp-Session-Id on initialize");

        // tools/list still works after initialize in stateless mode
        using var toolsResponse = await PostRawAsync(client,
            new { jsonrpc = "2.0", id = 2, method = "tools/list" });
        var toolsBody = await toolsResponse.Content.ReadAsStringAsync();
        Assert.True(toolsResponse.IsSuccessStatusCode, $"tools/list after stateless initialize failed: {toolsBody}");
        var toolsResult = ParseJsonOrSse(toolsBody, toolsResponse.Content.Headers.ContentType?.MediaType);
        var tools = toolsResult["result"]?["tools"] as JArray;
        Assert.NotNull(tools);
        Assert.NotEmpty(tools!);
    }

    // -------------------------------------------------------------------------
    // Stateful compatibility mode — explicit Stateless = false on test host
    // Proves down-level initialize still works with session tracking when
    // operators configure the backward-compatibility transport mode.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("2024-11-05")]
    [InlineData("2025-11-25")]
    public async Task StatefulCompat_Initialize_DownLevel_ReturnsSessionId_And_FunctionalTools(
        string protocolVersion)
    {
        await using var app = BuildStatefulCompatHost(protocolVersion);
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");

        // initialize returns a session ID in stateful mode
        using var initResponse = await client.PostAsync("/",
            JsonContent(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion,
                    capabilities = new { tools = new { } },
                    clientInfo = new { name = "stateful-compat-test", version = "1.0.0" }
                }
            }));
        var initBody = await initResponse.Content.ReadAsStringAsync();
        Output.WriteLine($"[{protocolVersion}] initialize (stateful): {initBody}");
        Assert.Equal(HttpStatusCode.OK, initResponse.StatusCode);

        var sessionId = initResponse.Headers.TryGetValues("Mcp-Session-Id", out var vals)
            ? vals.FirstOrDefault() : null;
        Assert.False(string.IsNullOrWhiteSpace(sessionId),
            $"Stateful compat mode must return Mcp-Session-Id for protocol {protocolVersion}");

        // tools/list works with session ID
        using var toolsRequest = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Content = JsonContent(new { jsonrpc = "2.0", id = 2, method = "tools/list" })
        };
        toolsRequest.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
        toolsRequest.Headers.TryAddWithoutValidation("MCP-Protocol-Version", protocolVersion);
        using var toolsResponse = await client.SendAsync(toolsRequest);
        var toolsBody = await toolsResponse.Content.ReadAsStringAsync();
        Output.WriteLine($"[{protocolVersion}] tools/list (stateful): {toolsBody}");
        Assert.Equal(HttpStatusCode.OK, toolsResponse.StatusCode);
        var toolsResult = ParseJsonOrSse(toolsBody, toolsResponse.Content.Headers.ContentType?.MediaType);
        var tools = toolsResult["result"]?["tools"] as JArray;
        Assert.NotNull(tools);
        Assert.NotEmpty(tools!);

        // tools/call proves tools are functional end-to-end in stateful compat mode
        using var callRequest = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Content = JsonContent(new
            {
                jsonrpc = "2.0",
                id = 3,
                method = "tools/call",
                @params = new { name = "compat_echo", arguments = new { } }
            })
        };
        callRequest.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
        callRequest.Headers.TryAddWithoutValidation("MCP-Protocol-Version", protocolVersion);
        using var callResponse = await client.SendAsync(callRequest);
        var callBody = await callResponse.Content.ReadAsStringAsync();
        Output.WriteLine($"[{protocolVersion}] tools/call (stateful): {callBody}");
        Assert.Equal(HttpStatusCode.OK, callResponse.StatusCode);
        var callResult = ParseJsonOrSse(callBody, callResponse.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(callResult["result"]);
        Assert.Null(callResult["error"]);
    }

    // -------------------------------------------------------------------------
    // Mode independence — protocol negotiation does not mutate transport mode
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("2024-11-05")]
    [InlineData("2025-11-25")]
    public async Task ProtocolVersion_InHeader_DoesNotMutateStatelessTransportMode(string version)
    {
        // Prove that sending MCP-Protocol-Version headers on stateless requests
        // does not cause the server to switch to stateful mode (no Mcp-Session-Id returned).
        using var server = new InProcessUnifiedHttpServer();
        await server.StartAsync();

        using var client = CreateMcpClient(server.ServerUrl);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Content = JsonContent(new { jsonrpc = "2.0", id = 1, method = "tools/list" })
        };
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", version);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Output.WriteLine($"[{version}] tools/list with version header: {body}");
        Assert.True(response.IsSuccessStatusCode, $"tools/list with version header failed: {body}");

        // Transport mode is stateless; no Mcp-Session-Id should be returned
        var hasSessionId = response.Headers.TryGetValues("Mcp-Session-Id", out var vals)
            && vals.Any(v => !string.IsNullOrWhiteSpace(v));
        Assert.False(hasSessionId,
            $"Protocol version header {version} must not trigger stateful transport (Mcp-Session-Id returned)");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static WebApplication BuildStatefulCompatHost(string negotiatedProtocolVersion)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        var lifecycle = new McpSessionLifecycle(_ => { });
        builder.Services.AddSingleton(lifecycle);

        var compatTool = McpServerTool.Create(
            static (CancellationToken _) => Task.FromResult("compatibility-test-pass"),
            new McpServerToolCreateOptions { Name = "compat_echo", Description = "Stateful compatibility echo" });

        builder.Services
            .AddMcpServer()
            .WithHttpTransport(options =>
            {
                options.Stateless = false; // Explicit operator compatibility mode.
#pragma warning disable MCP9006 // Intentional: stateful-mode option in explicit compat test.
                options.IdleTimeout = Timeout.InfiniteTimeSpan;
#pragma warning restore MCP9006
#pragma warning disable MCPEXP002 // Lifecycle callback for stateful compat mode.
                options.RunSessionHandler = lifecycle.RunSessionAsync;
#pragma warning restore MCPEXP002
            })
            .WithTools([compatTool]);

        var app = builder.Build();
        app.UseMiddleware<McpProtocolVersionMiddleware>((object)new[] { "/" });
        app.MapMcp();
        return app;
    }

    private static HttpClient CreateMcpClient(string serverUrl)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(serverUrl),
            Timeout = TimeSpan.FromSeconds(15)
        };
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
        return client;
    }

    private static async Task<HttpResponseMessage> PostRawAsync(HttpClient client, object payload)
    {
        using var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
        return await client.PostAsync("/", content);
    }

    private static async Task<HttpResponseMessage> PostDiscoverAsync(HttpClient client)
    {
        // MCP 2026-07-28 server/discover requires per-request metadata:
        //   HTTP headers: MCP-Protocol-Version: 2026-07-28, Mcp-Method: server/discover
        //   Body params._meta: protocolVersion + clientCapabilities (keys contain '/', use JObject)
        var payload = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = "server/discover",
            ["params"] = new JObject
            {
                ["_meta"] = new JObject
                {
                    ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
                    ["io.modelcontextprotocol/clientCapabilities"] = new JObject()
                }
            }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Content = new StringContent(payload.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2026-07-28");
        request.Headers.TryAddWithoutValidation("Mcp-Method", "server/discover");
        return await client.SendAsync(request);
    }

    private static StringContent JsonContent(object payload) =>
        new(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

    private static JObject ParseJsonOrSse(string body, string? mediaType)
    {
        if (string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            return JObject.Parse(body);

        var dataLine = body.Split('\n')
            .FirstOrDefault(l => l.StartsWith("data: ", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(dataLine))
            throw new InvalidOperationException($"No MCP data line in response: {body}");
        return JObject.Parse(dataLine.Substring(6));
    }
}
