using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Integration;

/// <summary>
/// Validates MCP v2 protocol negotiation (Layer 1) independently of HTTP transport statefulness (Layer 2).
///
/// <b>Authoritative wire behavior (observed on SDK v2.0.0, stateful mode <c>Stateless = false</c>):</b>
///
/// <list type="bullet">
///   <item><b>server/discover is blocked at the HTTP transport layer</b> in stateful mode. The ASP.NET
///         Core middleware returns <c>HTTP 400</c> with a well-formed JSON-RPC error before the
///         MCP server-level handler is invoked. SDK error: "A new session can only be created by
///         an initialize request. Include a valid Mcp-Session-Id header for non-initialize requests,
///         or enable stateless mode by setting HttpServerTransportOptions.Stateless = true."</item>
///   <item><b>2026-07-28 protocol is not available via initialize in stateful mode.</b>
///         <c>initialize</c> with <c>2026-07-28</c> returns JSON-RPC error <c>-32022</c>: "Protocol
///         version '2026-07-28' is not available through the initialize handshake." Supported versions
///         for <c>initialize</c> are: <c>2024-11-05</c>, <c>2025-03-26</c>, <c>2025-06-18</c>,
///         <c>2025-11-25</c>. The 2026-07-28 revision requires stateless mode (<c>Stateless = true</c>).</item>
///   <item><b>Layer 1 × Layer 2 independence observed:</b> The HTTP transport layer assigns a
///         <c>Mcp-Session-Id</c> even when the MCP protocol layer returns an initialize error
///         (e.g., for <c>2026-07-28</c>). These are separate concerns.</item>
///   <item><b>Scope mismatch:</b> Issue #340 requests testing <c>server/discover</c> success and
///         <c>2026-07-28</c> initialize success. Both are NOT available in PoshMcp's current stateful
///         HTTP default. The SDK documentation states <c>ConfigureDiscover()</c> "registers the handler
///         unconditionally" at the MCP protocol layer, but the ASP.NET Core HTTP transport middleware
///         (<c>Stateless = false</c>) intercepts pre-session requests before they reach that layer.
///         These tests document the correct compatible behavior and surface the mismatch explicitly.</item>
///   <item>The graceful-degradation path for v2 clients: probe <c>server/discover</c> → receive 400
///         with explicit guidance → fall back to <c>initialize</c> with a supported version → session.</item>
///   <item>The legacy down-level path: <c>initialize</c> with <c>2024-11-05</c> or
///         <c>2025-11-25</c> → established session with <c>tools/list</c> and <c>tools/call</c>.</item>
/// </list>
///
/// <b>Behavior Matrix (Layer 1 × Layer 2 independence, stateful HTTP default):</b>
/// <code>
/// | Negotiation Path                              | Protocol    | tools/list | tools/call | Session ID  |
/// |-----------------------------------------------|-------------|:----------:|:----------:|:-----------:|
/// | server/discover probe (stateful)              | N/A         |     —      |     —      | — (400)     |
/// | initialize with 2026-07-28 (stateful)         | 2026-07-28  |     —      |     —      | * (-32022)  |
/// | server/discover → fallback → init 2025-11-25  | 2025-11-25  |     ✓      |     ✓      |     ✓       |
/// | initialize directly (down-level)              | 2025-11-25  |     ✓      |     ✓      |     ✓       |
/// | initialize directly (down-level)              | 2024-11-05  |     ✓      |     ✓      |     ✓       |
/// </code>
/// * = session ID issued by transport layer but session unusable (MCP protocol error -32022)
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "ProtocolNegotiation")]
[Collection("TransportSelectionTests")]
public class ProtocolNegotiationTests : PowerShellTestBase
{
    public ProtocolNegotiationTests(ITestOutputHelper output) : base(output) { }

    // -------------------------------------------------------------------------
    // Layer 1 — server/discover behavior in stateful mode
    //
    // SCOPE MISMATCH: issue #340 requests testing that server/discover succeeds.
    // Authoritative wire behavior: stateful mode blocks pre-session non-initialize
    // requests at the HTTP transport layer (HTTP 400) before the MCP handler runs.
    // These tests document the actual behavior; tests below prove the correct
    // graceful-fallback path (probe → 400 → initialize → functional session).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ServerDiscover_InStatefulMode_Returns400WithJsonRpcError()
    {
        // In stateful mode (Stateless = false) the ASP.NET Core middleware intercepts
        // pre-session requests before they reach the MCP server/discover handler.
        // The SDK returns a well-formed JSON-RPC error, not an unstructured HTTP error.
        using var server = new InProcessUnifiedHttpServer();
        await server.StartAsync();

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(server.ServerUrl),
            Timeout = TimeSpan.FromSeconds(10)
        };
        httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");

        using var content = new StringContent(
            JsonConvert.SerializeObject(new { jsonrpc = "2.0", id = 1, method = "server/discover" }),
            Encoding.UTF8,
            "application/json");

        using var httpResponse = await httpClient.PostAsync("/", content);
        var body = await httpResponse.Content.ReadAsStringAsync();
        Output.WriteLine($"Status: {(int)httpResponse.StatusCode}");
        Output.WriteLine($"Body: {body}");

        // The HTTP transport layer returns 400 with a structured JSON-RPC error body.
        // Asserting the status and the JSON shape proves graceful rejection (not a crash).
        Assert.Equal(HttpStatusCode.BadRequest, httpResponse.StatusCode);

        var errorObj = JObject.Parse(body);
        Assert.Equal("2.0", errorObj["jsonrpc"]?.ToString());
        Assert.NotNull(errorObj["error"]);

        var errorMessage = errorObj["error"]?["message"]?.ToString() ?? "";
        Output.WriteLine($"Error message: {errorMessage}");

        // The SDK error message explicitly guides clients to the correct path,
        // enabling clean graceful degradation (the client knows to use initialize).
        Assert.Contains("initialize request", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ServerDiscover_InStatefulMode_ErrorGuidesClientToInitialize()
    {
        // The 400 error from server/discover in stateful mode must contain explicit
        // guidance so that client SDK implementations can implement graceful fallback
        // to the initialize handshake.
        using var server = new InProcessUnifiedHttpServer();
        await server.StartAsync();

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(server.ServerUrl),
            Timeout = TimeSpan.FromSeconds(10)
        };
        httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");

        using var content = new StringContent(
            JsonConvert.SerializeObject(new { jsonrpc = "2.0", id = 1, method = "server/discover" }),
            Encoding.UTF8,
            "application/json");

        using var httpResponse = await httpClient.PostAsync("/", content);
        var body = await httpResponse.Content.ReadAsStringAsync();
        var errorObj = JObject.Parse(body);

        var errorCode = errorObj["error"]?["code"]?.Value<int>();
        var errorMessage = errorObj["error"]?["message"]?.ToString() ?? "";

        Output.WriteLine($"Error code: {errorCode}, message: {errorMessage}");

        // MCP error code -32000 is a generic server error; the message must be actionable.
        Assert.NotNull(errorCode);
        Assert.Contains("initialize", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ServerDiscover_InStatefulMode_Requires_Both_Accept_Headers()
    {
        // The server enforces Accept: application/json AND text/event-stream.
        // A client that omits text/event-stream receives 406 Not Acceptable,
        // which is separate from the session-related 400.
        using var server = new InProcessUnifiedHttpServer();
        await server.StartAsync();

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(server.ServerUrl),
            Timeout = TimeSpan.FromSeconds(10)
        };
        // Intentionally only sending application/json, NOT text/event-stream.
        httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        using var content = new StringContent(
            JsonConvert.SerializeObject(new { jsonrpc = "2.0", id = 1, method = "server/discover" }),
            Encoding.UTF8,
            "application/json");

        using var httpResponse = await httpClient.PostAsync("/", content);
        var body = await httpResponse.Content.ReadAsStringAsync();
        Output.WriteLine($"Status: {(int)httpResponse.StatusCode}, Body: {body}");

        // 406 is the expected response for missing text/event-stream Accept.
        Assert.Equal(HttpStatusCode.NotAcceptable, httpResponse.StatusCode);

        var errorObj = JObject.Parse(body);
        Assert.Equal("2.0", errorObj["jsonrpc"]?.ToString());
        Assert.NotNull(errorObj["error"]);
    }

    // -------------------------------------------------------------------------
    // Layer 1 — initialize handshake (down-level and v2 protocol clients)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("2024-11-05")]
    [InlineData("2025-11-25")]
    public async Task Initialize_ShouldSucceedAndReturnSessionId_ForAllSupportedProtocolVersions(
        string requestedVersion)
    {
        using var server = new InProcessUnifiedHttpServer();
        await server.StartAsync();

        using var client = CreateMcpClient(server.ServerUrl);
        var (response, sessionId) = await SendInitializeAsync(client, requestedVersion);

        Output.WriteLine($"[{requestedVersion}] initialize response: {response.ToString(Formatting.None)}");
        Output.WriteLine($"[{requestedVersion}] sessionId: {sessionId ?? "(none)"}");

        Assert.Equal("2.0", response["jsonrpc"]?.ToString());
        Assert.NotNull(response["result"]);
        Assert.Null(response["error"]);
        Assert.False(string.IsNullOrWhiteSpace(sessionId),
            $"Expected Mcp-Session-Id header for protocol {requestedVersion}");
    }

    [Theory]
    [InlineData("2024-11-05")]
    [InlineData("2025-11-25")]
    public async Task Initialize_ShouldNegotiateCompatibleProtocolVersion(string requestedVersion)
    {
        using var server = new InProcessUnifiedHttpServer();
        await server.StartAsync();

        using var client = CreateMcpClient(server.ServerUrl);
        var (response, _) = await SendInitializeAsync(client, requestedVersion);

        var negotiatedVersion = response["result"]?["protocolVersion"]?.ToString();
        Output.WriteLine($"Requested: {requestedVersion}, Negotiated: {negotiatedVersion}");

        Assert.False(string.IsNullOrWhiteSpace(negotiatedVersion),
            "initialize result must include protocolVersion");

        // SDK doc: "For clients using the initialize handshake, the server returns the requested
        // initialize-capable version when it is supported and otherwise returns 2025-11-25."
        // Both 2024-11-05 and 2025-11-25 are supported, so the server echoes the requested version.
        Assert.Equal(requestedVersion, negotiatedVersion);
    }

    [Fact]
    public async Task Initialize_WithJuly2026Protocol_InStatefulMode_ReturnsStructuredError()
    {
        // Authoritative behavior: 2026-07-28 is NOT available via initialize in stateful mode.
        // The server returns JSON-RPC error -32022 with the list of supported versions,
        // enabling clients to choose a supported version and retry.
        using var server = new InProcessUnifiedHttpServer();
        await server.StartAsync();

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(server.ServerUrl),
            Timeout = TimeSpan.FromSeconds(10)
        };
        httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");

        using var content = new StringContent(
            JsonConvert.SerializeObject(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2026-07-28",
                    capabilities = new { tools = new { } },
                    clientInfo = new { name = "protocol-negotiation-test", version = "1.0.0" }
                }
            }),
            Encoding.UTF8,
            "application/json");

        using var httpResponse = await httpClient.PostAsync("/", content);
        var body = await httpResponse.Content.ReadAsStringAsync();
        Output.WriteLine($"Status: {(int)httpResponse.StatusCode}");
        Output.WriteLine($"Body: {body}");

        var errorObj = ParseJsonOrSse(body, httpResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("2.0", errorObj["jsonrpc"]?.ToString());
        Assert.NotNull(errorObj["error"]);

        var errorCode = errorObj["error"]?["code"]?.Value<int>();
        var errorMessage = errorObj["error"]?["message"]?.ToString() ?? "";
        Output.WriteLine($"Error code: {errorCode}, message: {errorMessage}");

        // Error -32022 is the SDK's UnsupportedProtocolVersion code for initialize.
        Assert.Equal(-32022, errorCode);
        Assert.Contains("2026-07-28", errorMessage, StringComparison.Ordinal);
        Assert.Contains("initialize", errorMessage, StringComparison.OrdinalIgnoreCase);

        // The error data lists supported initialize-compatible versions so clients can retry.
        var supported = errorObj["error"]?["data"]?["supported"] as JArray;
        Assert.NotNull(supported);
        Assert.Contains(supported!, v => string.Equals(v?.ToString(), "2025-11-25", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Initialize_WithJuly2026Protocol_TransportLayerStillAssignsSessionId()
    {
        // Layer 1 × Layer 2 independence: the HTTP transport layer assigns a Mcp-Session-Id
        // regardless of the MCP protocol-level error. This proves the two layers are independent.
        using var server = new InProcessUnifiedHttpServer();
        await server.StartAsync();

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(server.ServerUrl),
            Timeout = TimeSpan.FromSeconds(10)
        };
        httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");

        using var content = new StringContent(
            JsonConvert.SerializeObject(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2026-07-28",
                    capabilities = new { tools = new { } },
                    clientInfo = new { name = "protocol-negotiation-test", version = "1.0.0" }
                }
            }),
            Encoding.UTF8,
            "application/json");

        using var httpResponse = await httpClient.PostAsync("/", content);
        var body = await httpResponse.Content.ReadAsStringAsync();

        var sessionId = httpResponse.Headers.TryGetValues("Mcp-Session-Id", out var vals)
            ? vals.FirstOrDefault()
            : null;

        Output.WriteLine($"Session ID from transport: {sessionId ?? "(none)"}");
        Output.WriteLine($"MCP protocol response: {body}");

        // HTTP transport (Layer 2) assigns a session ID even though the MCP protocol
        // layer (Layer 1) rejects the 2026-07-28 version. Independent layers.
        Assert.False(string.IsNullOrWhiteSpace(sessionId),
            "HTTP transport should assign Mcp-Session-Id independently of MCP protocol-level errors.");
        var errorObj = ParseJsonOrSse(body, httpResponse.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(errorObj["error"]);
    }

    // -------------------------------------------------------------------------
    // Layer 1 — graceful-fallback path:
    //   v2 client probes server/discover → gets 400 → falls back to initialize
    //   This is the authoritative down-level client behavior for stateful servers.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("2025-11-25")]
    public async Task DiscoverProbe_GracefulFallback_ShouldSupportToolsList(string fallbackVersion)
    {
        using var server = new InProcessUnifiedHttpServer();
        await server.StartAsync();

        using var client = CreateMcpClient(server.ServerUrl);

        // Step 1: v2 client probes server/discover — expects 400 in stateful mode.
        using var probeContent = new StringContent(
            JsonConvert.SerializeObject(new { jsonrpc = "2.0", id = 1, method = "server/discover" }),
            Encoding.UTF8,
            "application/json");
        using var probeMsg = new HttpRequestMessage(HttpMethod.Post, "/") { Content = probeContent };
        using var probeResponse = await client.SendAsync(probeMsg);
        Output.WriteLine($"server/discover probe status: {(int)probeResponse.StatusCode}");

        // Graceful fallback: 400 is a clean, structured rejection — not a crash.
        // v2 SDK client implementations fall back to initialize when they see this error.
        Assert.Equal(HttpStatusCode.BadRequest, probeResponse.StatusCode);

        // Step 2: fall back to initialize with the chosen version
        var (_, sessionId) = await SendInitializeAsync(client, fallbackVersion, startId: 2);
        Assert.False(string.IsNullOrWhiteSpace(sessionId),
            "After discovering stateful server, initialize must succeed");
        Output.WriteLine($"sessionId after graceful fallback: {sessionId}");

        // Step 3: tools/list proves the session is functional
        var toolsResponse = await PostJsonRpcAsync(client, new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "tools/list"
        }, sessionId: sessionId, protocolVersion: fallbackVersion);

        var tools = toolsResponse["result"]?["tools"] as JArray;
        Output.WriteLine($"tools count: {tools?.Count ?? 0}");
        Assert.NotNull(tools);
        Assert.NotEmpty(tools!);
    }

    [Theory]
    [InlineData("2025-11-25")]
    public async Task DiscoverProbe_GracefulFallback_ShouldSupportToolsCall(string fallbackVersion)
    {
        using var server = new InProcessUnifiedHttpServer();
        await server.StartAsync();

        using var client = CreateMcpClient(server.ServerUrl);

        // Step 1: probe fails gracefully
        using var probeContent = new StringContent(
            JsonConvert.SerializeObject(new { jsonrpc = "2.0", id = 1, method = "server/discover" }),
            Encoding.UTF8,
            "application/json");
        using var probeMsg = new HttpRequestMessage(HttpMethod.Post, "/") { Content = probeContent };
        using var probeResponse = await client.SendAsync(probeMsg);
        Assert.Equal(HttpStatusCode.BadRequest, probeResponse.StatusCode);

        // Step 2: fall back to initialize
        var (_, sessionId) = await SendInitializeAsync(client, fallbackVersion, startId: 2);
        Assert.False(string.IsNullOrWhiteSpace(sessionId));

        // Step 3: list tools
        var toolsResponse = await PostJsonRpcAsync(client, new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "tools/list"
        }, sessionId: sessionId, protocolVersion: fallbackVersion);

        var tools = toolsResponse["result"]?["tools"] as JArray;
        Assert.NotNull(tools);
        Assert.NotEmpty(tools!);

        var toolName = tools!
            .Select(t => t?["name"]?.ToString())
            .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n) &&
                (n.StartsWith("get_date", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(n, "get_last_command_output", StringComparison.OrdinalIgnoreCase)));
        Assert.False(string.IsNullOrWhiteSpace(toolName), "Expected a callable tool");

        // Step 4: call tool
        var callResponse = await PostJsonRpcAsync(client, new
        {
            jsonrpc = "2.0",
            id = 4,
            method = "tools/call",
            @params = new { name = toolName, arguments = new { } }
        }, sessionId: sessionId, protocolVersion: fallbackVersion);

        Output.WriteLine($"tools/call response: {callResponse.ToString(Formatting.None)}");
        Assert.Equal("2.0", callResponse["jsonrpc"]?.ToString());
        Assert.NotNull(callResponse["result"]);
        Assert.Null(callResponse["error"]);
    }

    // -------------------------------------------------------------------------
    // Layer 1 — direct initialize path (down-level clients, no probe step)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2024-11-05")]
    public async Task DirectInitialize_DownLevelClient_ShouldSupportToolsList(string protocolVersion)
    {
        using var server = new InProcessUnifiedHttpServer();
        await server.StartAsync();

        using var client = CreateMcpClient(server.ServerUrl);

        var (_, sessionId) = await SendInitializeAsync(client, protocolVersion);
        Assert.False(string.IsNullOrWhiteSpace(sessionId));

        var toolsResponse = await PostJsonRpcAsync(client, new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/list"
        }, sessionId: sessionId, protocolVersion: protocolVersion);

        var tools = toolsResponse["result"]?["tools"] as JArray;
        Output.WriteLine($"[{protocolVersion}] tools count: {tools?.Count ?? 0}");
        Assert.NotNull(tools);
        Assert.NotEmpty(tools!);
    }

    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2024-11-05")]
    public async Task DirectInitialize_DownLevelClient_ShouldSupportToolsCall(string protocolVersion)
    {
        using var server = new InProcessUnifiedHttpServer();
        await server.StartAsync();

        using var client = CreateMcpClient(server.ServerUrl);

        var (_, sessionId) = await SendInitializeAsync(client, protocolVersion);
        Assert.False(string.IsNullOrWhiteSpace(sessionId));

        var toolsResponse = await PostJsonRpcAsync(client, new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/list"
        }, sessionId: sessionId, protocolVersion: protocolVersion);

        var tools = toolsResponse["result"]?["tools"] as JArray;
        Assert.NotNull(tools);
        Assert.NotEmpty(tools!);

        var toolName = tools!
            .Select(t => t?["name"]?.ToString())
            .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n) &&
                (n.StartsWith("get_date", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(n, "get_last_command_output", StringComparison.OrdinalIgnoreCase)));
        Assert.False(string.IsNullOrWhiteSpace(toolName));

        var callResponse = await PostJsonRpcAsync(client, new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "tools/call",
            @params = new { name = toolName, arguments = new { } }
        }, sessionId: sessionId, protocolVersion: protocolVersion);

        Output.WriteLine($"[{protocolVersion}] tools/call: {callResponse.ToString(Formatting.None)}");
        Assert.Equal("2.0", callResponse["jsonrpc"]?.ToString());
        Assert.NotNull(callResponse["result"]);
        Assert.Null(callResponse["error"]);
    }

    // -------------------------------------------------------------------------
    // Layer 1 × Layer 2 independence — initialize protocol version must NOT
    // alter HTTP transport mode (session ID always returned)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2024-11-05")]
    public async Task Initialize_ShouldNotAlterHttpTransportMode_SessionIdAlwaysReturned(
        string protocolVersion)
    {
        // Proves that the choice of protocol version in the initialize handshake
        // does not change the server's HTTP transport mode. The server must always
        // return Mcp-Session-Id, confirming stateful mode is independent of
        // the negotiated MCP protocol version.
        using var server = new InProcessUnifiedHttpServer();
        await server.StartAsync();

        using var client = CreateMcpClient(server.ServerUrl);
        var (response, sessionId) = await SendInitializeAsync(client, protocolVersion);

        Output.WriteLine($"[{protocolVersion}] sessionId: {sessionId ?? "(none)"}");

        Assert.False(string.IsNullOrWhiteSpace(sessionId),
            $"Protocol {protocolVersion} must not disable stateful HTTP; Mcp-Session-Id is required.");

        // Verify the session is independently functional (no transport-mode leakage)
        var toolsResponse = await PostJsonRpcAsync(client, new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/list"
        }, sessionId: sessionId, protocolVersion: protocolVersion);

        Assert.NotNull(toolsResponse["result"]?["tools"]);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

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

    private static async Task<(JObject Response, string? SessionId)> SendInitializeAsync(
        HttpClient client,
        string protocolVersion,
        int startId = 1)
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = startId,
            method = "initialize",
            @params = new
            {
                protocolVersion,
                capabilities = new { tools = new { } },
                clientInfo = new
                {
                    name = "protocol-negotiation-test",
                    version = "1.0.0"
                }
            }
        };

        return await PostJsonRpcWithSessionAsync(client, request, sessionId: null, protocolVersion: null);
    }

    private static async Task<JObject> PostJsonRpcAsync(
        HttpClient client,
        object request,
        string? sessionId = null,
        string? protocolVersion = null)
    {
        var (response, _) = await PostJsonRpcWithSessionAsync(client, request, sessionId, protocolVersion);
        return response;
    }

    private static async Task<(JObject Response, string? SessionId)> PostJsonRpcWithSessionAsync(
        HttpClient client,
        object request,
        string? sessionId,
        string? protocolVersion)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var content = new StringContent(
                JsonConvert.SerializeObject(request),
                Encoding.UTF8,
                "application/json");

            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/")
            {
                Content = content
            };

            if (!string.IsNullOrWhiteSpace(sessionId))
                requestMessage.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);

            if (!string.IsNullOrWhiteSpace(protocolVersion))
                requestMessage.Headers.TryAddWithoutValidation("MCP-Protocol-Version", protocolVersion);

            try
            {
                using var httpResponse = await client.SendAsync(requestMessage);
                var body = await httpResponse.Content.ReadAsStringAsync();

                if (!httpResponse.IsSuccessStatusCode)
                    throw new HttpRequestException($"HTTP {(int)httpResponse.StatusCode}: {body}");

                var responseObj = ParseJsonOrSse(body, httpResponse.Content.Headers.ContentType?.MediaType);
                var returnedSessionId = httpResponse.Headers.TryGetValues("Mcp-Session-Id", out var vals)
                    ? vals.FirstOrDefault()
                    : null;

                return (responseObj, returnedSessionId);
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts &&
                (ex.Message.Contains("Only one usage of each socket address", StringComparison.OrdinalIgnoreCase) ||
                 ex.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase)))
            {
                await Task.Delay(100 * attempt);
            }
        }

        throw new InvalidOperationException("MCP request retry loop exhausted.");
    }

    private static JObject ParseJsonOrSse(string body, string? mediaType)
    {
        if (string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            return JObject.Parse(body);

        var dataLine = body
            .Split('\n')
            .FirstOrDefault(l => l.StartsWith("data: ", StringComparison.Ordinal));

        if (string.IsNullOrWhiteSpace(dataLine))
            throw new InvalidOperationException($"No MCP data line in response: {body}");

        return JObject.Parse(dataLine.Substring(6));
    }
}
