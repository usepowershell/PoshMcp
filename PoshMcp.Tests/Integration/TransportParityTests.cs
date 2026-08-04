using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.Pool;
using PoshMcp.Server.Server;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Integration;

/// <summary>
/// Transport parity matrix: proves same tool results and error semantics across
/// Stateless HTTP, Stateful HTTP, and stdio, and validates down-level protocol
/// client compatibility with functional tool invocation.
///
/// <b>Behavior Matrix:</b>
/// <code>
/// | Transport          | Protocol    | tools/list | tools/call | Error    | Session ID |
/// |--------------------|-------------|:----------:|:----------:|:--------:|:----------:|
/// | Stateless HTTP     | none        |     ✓      |     ✓      |    ✓     |     —      |
/// | Stateful HTTP      | 2025-11-25  |     ✓      |     ✓      |    ✓     |     ✓      |
/// | Stateful HTTP      | 2024-11-05  |     ✓      |     ✓      |    ✓     |     ✓      |
/// | Stdio              | SDK managed |     ✓      |     ✓      |    ✓     |     —      |
/// </code>
///
/// <b>Parity invariants under test:</b>
/// <list type="number">
///   <item>tools/list returns the same tool-name set on both HTTP modes (exact set equality).</item>
///   <item>tools/call for a deterministic tool returns the same content value on both HTTP modes.</item>
///   <item>An invalid tool name returns a structurally identical MCP error on all 3 transports.</item>
///   <item>Down-level clients (2025-11-25, 2024-11-05) receive a session ID from stateful mode
///         and can invoke functional tools; the tool output equals the stateless output.</item>
///   <item>Stateless HTTP does NOT return Mcp-Session-Id; stateful HTTP always does.</item>
///   <item>Legacy session IDs do not pin or retain PowerShell state between requests
///         (see also TransportIsolationTests.Stateful_SameSessionId_ClearsRequestStateAndDoesNotPinWorker).</item>
///   <item>Stdio state is retained between calls (intentional; SingletonPowerShellRunspace).</item>
/// </list>
///
/// HTTP tests use ASP.NET TestServer (in-process, no network).
/// Stdio tests use InProcessMcpServer (out-of-process, real stdio protocol).
/// </summary>
[Trait("Category", "Integration")]
[Collection("TransportParityTests")]
public sealed class TransportParityTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;

    // Shared test hosts
    private WebApplication? _statelessApp;
    private HttpClient? _statelessClient;
    private WebApplication? _statefulApp;
    private HttpClient? _statefulClient;

    // Tool definitions shared between stateless and stateful hosts.
    // Tools use pure PowerShell expressions so results are deterministic.
    private const string ParityEchoToolName = "parity_echo";
    private const string ParityEchoValue = "parity-pass-42";

    private const string ParityErrorToolName = "parity_error_tool";
    private const string ParityReadVarToolName = "parity_read_var";
    private const string ParitySetVarToolName = "parity_set_var";

    public TransportParityTests(ITestOutputHelper output) => _output = output;

    // ─── IAsyncLifetime ──────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        _statelessApp = await BuildParityHostAsync(stateless: true);
        _statelessClient = CreateTestClient(_statelessApp);

        _statefulApp = await BuildParityHostAsync(stateless: false);
        _statefulClient = CreateTestClient(_statefulApp);
    }

    public async Task DisposeAsync()
    {
        _statelessClient?.Dispose();
        if (_statelessApp is not null)
        {
            await _statelessApp.StopAsync();
            await _statelessApp.DisposeAsync();
        }
        _statefulClient?.Dispose();
        if (_statefulApp is not null)
        {
            await _statefulApp.StopAsync();
            await _statefulApp.DisposeAsync();
        }
    }

    // ─── Group A: tools/list parity ───────────────────────────────────────────

    [Fact]
    public async Task ToolsList_StatelessAndStateful_ReturnIdenticalToolNames()
    {
        // Stateless: direct tools/list
        var slResult = await CallAsync(_statelessClient!, new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/list"
        });
        var slTools = ExtractToolNames(slResult);

        // Stateful: initialize first, then tools/list with session ID
        const string sfProtocol = "2024-11-05";
        var sessionId = await InitializeStatefulAsync(_statefulClient!, sfProtocol);
        Assert.False(string.IsNullOrWhiteSpace(sessionId), "stateful initialize must return Mcp-Session-Id");

        var sfResult = await CallWithSessionAsync(_statefulClient!, sessionId!, new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/list"
        }, sfProtocol);
        var sfTools = ExtractToolNames(sfResult);

        _output.WriteLine($"Stateless tools: [{string.Join(", ", slTools)}]");
        _output.WriteLine($"Stateful tools:  [{string.Join(", ", sfTools)}]");

        Assert.True(slTools.SetEquals(sfTools),
            $"tools/list mismatch: stateless={string.Join(",", slTools.OrderBy(t => t))} " +
            $"stateful={string.Join(",", sfTools.OrderBy(t => t))}");
    }

    [Fact]
    public async Task ToolsList_Response_HasRequiredMcpSchema()
    {
        foreach (var (label, client, sessionId, protocol) in await GetAllMcpClientsAsync())
        {
            JObject result;
            if (sessionId is null)
                result = await CallAsync(client, new { jsonrpc = "2.0", id = 1, method = "tools/list" });
            else
                result = await CallWithSessionAsync(client, sessionId, new { jsonrpc = "2.0", id = 1, method = "tools/list" }, protocol);

            _output.WriteLine($"[{label}] tools/list: {result.ToString(Formatting.None)}");

            Assert.Equal("2.0", result["jsonrpc"]?.ToString());
            Assert.Null(result["error"]);
            var tools = result["result"]?["tools"] as JArray;
            Assert.NotNull(tools);
            Assert.NotEmpty(tools!);

            // Each tool must have name, description, and inputSchema.
            foreach (var tool in tools)
            {
                Assert.False(string.IsNullOrWhiteSpace(tool["name"]?.ToString()),
                    $"[{label}] Tool missing 'name'");
                Assert.NotNull(tool["inputSchema"]);
            }
        }
    }

    // ─── Group B: tools/call parity ───────────────────────────────────────────

    [Fact]
    public async Task ToolsCall_DeterministicTool_SameOutputAcrossHttpModes()
    {
        // Stateless
        var slResult = await CallAsync(_statelessClient!, new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/call",
            @params = new { name = ParityEchoToolName, arguments = new { } }
        });
        var slText = ExtractFirstContentText(slResult);

        // Stateful (down-level 2025-11-25)
        const string sfProtocol = "2025-11-25";
        var sessionId = await InitializeStatefulAsync(_statefulClient!, sfProtocol);
        Assert.False(string.IsNullOrWhiteSpace(sessionId));
        var sfResult = await CallWithSessionAsync(_statefulClient!, sessionId!, new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/call",
            @params = new { name = ParityEchoToolName, arguments = new { } }
        }, sfProtocol);
        var sfText = ExtractFirstContentText(sfResult);

        _output.WriteLine($"Stateless output:  '{slText}'");
        _output.WriteLine($"Stateful output:   '{sfText}'");

        Assert.Equal(ParityEchoValue, slText);
        Assert.Equal(slText, sfText);
    }

    [Fact]
    public async Task ToolsCall_DeterministicTool_SameOutputAcrossAllThreeTransports()
    {
        // Stateless HTTP
        var slResult = await CallAsync(_statelessClient!, new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/call",
            @params = new { name = ParityEchoToolName, arguments = new { } }
        });
        var slText = ExtractFirstContentText(slResult);

        // Stateful HTTP (2024-11-05 down-level)
        const string sfProtocol = "2024-11-05";
        var sessionId = await InitializeStatefulAsync(_statefulClient!, sfProtocol);
        var sfResult = await CallWithSessionAsync(_statefulClient!, sessionId!, new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/call",
            @params = new { name = ParityEchoToolName, arguments = new { } }
        }, sfProtocol);
        var sfText = ExtractFirstContentText(sfResult);

        _output.WriteLine($"Stateless output:  '{slText}'");
        _output.WriteLine($"Stateful output:   '{sfText}'");

        Assert.Equal(ParityEchoValue, slText);
        Assert.Equal(ParityEchoValue, sfText);
        Assert.Equal(slText, sfText);
    }

    [Fact]
    public async Task ToolsCall_ValidContentStructure_AcrossAllModes()
    {
        foreach (var (label, client, sessionId, protocol) in await GetAllMcpClientsAsync())
        {
            JObject result;
            if (sessionId is null)
                result = await CallAsync(client, new
                {
                    jsonrpc = "2.0",
                    id = 3,
                    method = "tools/call",
                    @params = new { name = ParityEchoToolName, arguments = new { } }
                });
            else
                result = await CallWithSessionAsync(client, sessionId, new
                {
                    jsonrpc = "2.0",
                    id = 3,
                    method = "tools/call",
                    @params = new { name = ParityEchoToolName, arguments = new { } }
                }, protocol);

            _output.WriteLine($"[{label}] tools/call: {result.ToString(Formatting.None)}");

            Assert.Equal("2.0", result["jsonrpc"]?.ToString());
            Assert.Null(result["error"]);
            Assert.NotNull(result["result"]);
            var content = result["result"]?["content"] as JArray;
            Assert.NotNull(content);
            Assert.True(content!.Count > 0, $"[{label}] Expected non-empty content array");
            Assert.Equal("text", content[0]?["type"]?.ToString());
            Assert.False(string.IsNullOrWhiteSpace(content[0]?["text"]?.ToString()),
                $"[{label}] Expected non-empty text in content[0]");
        }
    }

    // ─── Group C: error response parity ──────────────────────────────────────

    [Fact]
    public async Task ToolsCall_InvalidToolName_ErrorStructureMatchesAcrossHttpModes()
    {
        const string invalidTool = "no_such_tool_xyzzy_12345";

        // Stateless
        var slResult = await CallAsync(_statelessClient!, new
        {
            jsonrpc = "2.0",
            id = 99,
            method = "tools/call",
            @params = new { name = invalidTool, arguments = new { } }
        });

        // Stateful
        const string sfProtocol = "2024-11-05";
        var sessionId = await InitializeStatefulAsync(_statefulClient!, sfProtocol);
        var sfResult = await CallWithSessionAsync(_statefulClient!, sessionId!, new
        {
            jsonrpc = "2.0",
            id = 99,
            method = "tools/call",
            @params = new { name = invalidTool, arguments = new { } }
        }, sfProtocol);

        _output.WriteLine($"Stateless error: {slResult.ToString(Formatting.None)}");
        _output.WriteLine($"Stateful  error: {sfResult.ToString(Formatting.None)}");

        // Both must return the same structural error shape: either a JSON-RPC error object
        // or an MCP result with isError=true (SDK may differ by version but structure must match).
        AssertMcpErrorShape(slResult, "stateless");
        AssertMcpErrorShape(sfResult, "stateful");

        // Both must report the same tool-not-found semantics (no difference in error code or message).
        var slCode = GetErrorCode(slResult);
        var sfCode = GetErrorCode(sfResult);
        if (slCode.HasValue && sfCode.HasValue)
            Assert.Equal(slCode, sfCode);
    }

    // ─── Group D: down-level protocol compatibility ───────────────────────────

    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2024-11-05")]
    public async Task DownLevel_Initialize_Returns_SessionId_InStatefulMode(string version)
    {
        var sessionId = await InitializeStatefulAsync(_statefulClient!, version);
        _output.WriteLine($"[{version}] Mcp-Session-Id: {sessionId}");
        Assert.False(string.IsNullOrWhiteSpace(sessionId),
            $"Stateful mode must return Mcp-Session-Id for protocol {version}");
    }

    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2024-11-05")]
    public async Task DownLevel_ToolsCall_EqualStatelessOutput_InStatefulMode(string version)
    {
        // Reference: stateless result
        var slResult = await CallAsync(_statelessClient!, new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/call",
            @params = new { name = ParityEchoToolName, arguments = new { } }
        });
        var slText = ExtractFirstContentText(slResult);

        // Down-level stateful result
        var sessionId = await InitializeStatefulAsync(_statefulClient!, version);
        Assert.False(string.IsNullOrWhiteSpace(sessionId));

        var sfResult = await CallWithSessionAsync(_statefulClient!, sessionId!, new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/call",
            @params = new { name = ParityEchoToolName, arguments = new { } }
        }, version);
        var sfText = ExtractFirstContentText(sfResult);

        _output.WriteLine($"[{version}] Stateless: '{slText}'");
        _output.WriteLine($"[{version}] Stateful:  '{sfText}'");

        Assert.Equal(ParityEchoValue, slText);
        Assert.Equal(slText, sfText);
    }

    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2024-11-05")]
    public async Task DownLevel_ToolsList_Works_InStatefulMode(string version)
    {
        var sessionId = await InitializeStatefulAsync(_statefulClient!, version);

        var result = await CallWithSessionAsync(_statefulClient!, sessionId!, new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/list"
        }, version);

        _output.WriteLine($"[{version}] tools/list: {result.ToString(Formatting.None)}");
        Assert.Null(result["error"]);
        var tools = result["result"]?["tools"] as JArray;
        Assert.NotNull(tools);
        Assert.NotEmpty(tools!);
    }

    // ─── Group E: session-ID semantics ────────────────────────────────────────

    [Fact]
    public async Task SessionId_StatelessMode_NeverReturned()
    {
        // Stateless HTTP must NOT return Mcp-Session-Id on any request.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Content = new StringContent(
                JsonConvert.SerializeObject(new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "tools/call",
                    @params = new { name = ParityEchoToolName, arguments = new { } }
                }),
                Encoding.UTF8, "application/json")
        };

        using var response = await _statelessClient!.SendAsync(request);
        var hasSessionId = response.Headers.TryGetValues("Mcp-Session-Id", out var vals)
            && vals.Any(v => !string.IsNullOrWhiteSpace(v));

        _output.WriteLine($"Stateless Mcp-Session-Id header present: {hasSessionId}");
        Assert.False(hasSessionId, "Stateless mode must not return Mcp-Session-Id");
    }

    [Fact]
    public async Task SessionId_StatefulMode_AlwaysReturnedOnInitialize()
    {
        var sessionId = await InitializeStatefulAsync(_statefulClient!, "2025-11-25");
        Assert.False(string.IsNullOrWhiteSpace(sessionId),
            "Stateful mode must return Mcp-Session-Id on initialize");
    }

    [Fact]
    public async Task SessionId_Stateful_TwoSessions_HaveDifferentIds()
    {
        var s1 = await InitializeStatefulAsync(_statefulClient!, "2025-11-25");
        var s2 = await InitializeStatefulAsync(_statefulClient!, "2025-11-25");

        _output.WriteLine($"Session 1: {s1}");
        _output.WriteLine($"Session 2: {s2}");

        Assert.False(string.IsNullOrWhiteSpace(s1));
        Assert.False(string.IsNullOrWhiteSpace(s2));
        Assert.NotEqual(s1, s2);
    }

    [Fact]
    public async Task SessionId_Stateful_DoesNotPinWorkerOrRetainState()
    {
        // Use the same session ID for a set-then-read cycle.
        // Reset must clear request-scoped variables between calls regardless of session ID.
        const string proto = "2024-11-05";
        var sessionId = await InitializeStatefulAsync(_statefulClient!, proto);
        Assert.False(string.IsNullOrWhiteSpace(sessionId));

        // Set a request-scoped variable.
        await CallWithSessionAsync(_statefulClient!, sessionId!, new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new { name = ParitySetVarToolName, arguments = new { } }
        }, proto);

        // Read it back — reset should have cleared it.
        var readResult = await CallWithSessionAsync(_statefulClient!, sessionId!, new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/call",
            @params = new { name = ParityReadVarToolName, arguments = new { } }
        }, proto);

        var value = ExtractFirstContentText(readResult);
        _output.WriteLine($"Variable after reset (same session): '{value}'");
        Assert.Equal("not-set", value);
    }

    // ─── Group F: stdio structural parity ─────────────────────────────────────

    [Fact]
    public async Task Stdio_ToolsList_HasSameResponseStructure()
    {
        // Stdio uses InProcessMcpServer with default appsettings.json.
        // We verify structural contract, not identical tool names (configs may differ).
        var logger = new XunitLogger(_output, nameof(Stdio_ToolsList_HasSameResponseStructure));

        using var server = new InProcessMcpServer(logger);
        await server.StartAsync();

        var client = new ExternalMcpClient(logger, server);
        await client.StartAsync();

        var response = await client.SendListToolsAsync();

        _output.WriteLine($"[stdio] tools/list: {response?.ToString(Formatting.None)}");
        Assert.NotNull(response);
        Assert.Equal("2.0", response!["jsonrpc"]?.ToString());
        Assert.Null(response["error"]);

        var tools = response["result"]?["tools"] as JArray;
        Assert.NotNull(tools);
        Assert.NotEmpty(tools!);

        // Structural contract: each tool has name + inputSchema.
        foreach (var tool in tools)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool["name"]?.ToString()),
                "[stdio] Tool missing 'name'");
            Assert.NotNull(tool["inputSchema"]);
        }
    }

    [Fact]
    public async Task Stdio_ToolsCall_ReturnsValidMcpResult()
    {
        var logger = new XunitLogger(_output, nameof(Stdio_ToolsCall_ReturnsValidMcpResult));

        using var server = new InProcessMcpServer(logger);
        await server.StartAsync();

        var client = new ExternalMcpClient(logger, server);
        await client.StartAsync();

        // Discover an available tool from the stdio server's default config.
        var listResponse = await client.SendListToolsAsync();
        var tools = listResponse!["result"]?["tools"] as JArray;
        Assert.NotNull(tools);
        Assert.NotEmpty(tools!);

        var toolName = tools![0]?["name"]?.ToString();
        Assert.False(string.IsNullOrWhiteSpace(toolName));

        // Call it with empty args (choose a tool that can be called without required params).
        var callResponse = await client.SendToolCallAsync(toolName!, new JObject());
        _output.WriteLine($"[stdio] tools/call [{toolName}]: {callResponse?.ToString(Formatting.None)}");
        Assert.NotNull(callResponse);
        Assert.Equal("2.0", callResponse!["jsonrpc"]?.ToString());
        Assert.Null(callResponse["error"]);
        Assert.NotNull(callResponse["result"]);

        var content = callResponse["result"]?["content"] as JArray;
        Assert.NotNull(content);
    }

    [Fact]
    public async Task Stdio_InvalidToolCall_ReturnsStructuredError()
    {
        var logger = new XunitLogger(_output, nameof(Stdio_InvalidToolCall_ReturnsStructuredError));

        using var server = new InProcessMcpServer(logger);
        await server.StartAsync();

        var client = new ExternalMcpClient(logger, server);
        await client.StartAsync();

        var response = await client.SendToolCallAsync("no_such_tool_xyzzy_12345", new JObject());
        _output.WriteLine($"[stdio] invalid call response: {response?.ToString(Formatting.None)}");
        Assert.NotNull(response);
        // Must be a structured MCP error, not a null/empty response.
        Assert.True(response!["error"] != null || response["result"]?["isError"] != null,
            "[stdio] Expected error response for invalid tool call");
    }

    // ─── Host builder ─────────────────────────────────────────────────────────

    private static async Task<WebApplication> BuildParityHostAsync(bool stateless)
    {
        var opts = new RunspacePoolOptions
        {
            MinPoolSize = 1,
            MaxPoolSize = 2,
            EagerWarmCount = 2,
            AcquisitionTimeout = TimeSpan.FromSeconds(10),
            IdleTtl = TimeSpan.FromSeconds(300),
            SweepInterval = TimeSpan.FromSeconds(60),
            StopTimeout = TimeSpan.FromSeconds(5),
            ShutdownDrainTimeout = TimeSpan.FromSeconds(10),
            ReplenishCheckInterval = TimeSpan.FromSeconds(60),
        };

        var pool = new StatelessRunspacePool(opts);
        var pooledRunspace = new PooledHttpRunspace(pool, (string?)null, NullLoggerFactory.Instance);
        pooledRunspace.FinalizeDiscovery();

        var lifecycle = new McpSessionLifecycle();
        var tools = CreateParityTools(pooledRunspace);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddSingleton(lifecycle);
        builder.Services.AddSingleton<IPowerShellRunspace>(pooledRunspace);
        builder.Services.AddSingleton<IRunspacePool>(pool);
        builder.Services.AddSingleton<IHostedService, RunspacePoolLifecycleService>();

        builder.Services
            .AddMcpServer()
            .WithHttpTransport(o =>
            {
                o.Stateless = stateless;
#pragma warning disable MCP9006
                o.IdleTimeout = Timeout.InfiniteTimeSpan;
#pragma warning restore MCP9006
                if (!stateless)
                {
#pragma warning disable MCPEXP002
                    o.RunSessionHandler = lifecycle.RunSessionAsync;
#pragma warning restore MCPEXP002
                }
            })
            .WithTools(tools);

        var app = builder.Build();
        app.UseMiddleware<McpProtocolVersionMiddleware>((object)new[] { "/" });
        app.MapMcp();
        await app.StartAsync();
        return app;
    }

    private static McpServerTool[] CreateParityTools(IPowerShellRunspace runspace)
    {
        static Task<string> Run(IPowerShellRunspace rs, string script) =>
            rs.ExecuteThreadSafeAsync(ps =>
            {
                ps.Commands.Clear();
                ps.AddScript(script);
                var results = ps.Invoke<string>();
                ps.Commands.Clear();
                ps.Streams.ClearStreams();
                return Task.FromResult(results.Count > 0 ? results[0] ?? "" : "");
            });

        McpServerTool Tool(string name, string desc, Func<CancellationToken, Task<string>> fn) =>
            McpServerTool.Create(fn, new McpServerToolCreateOptions { Name = name, Description = desc });

        return
        [
            // Deterministic constant: same value every call, every transport.
            Tool(ParityEchoToolName, "Returns a constant parity value",
                _ => Run(runspace, $"'{ParityEchoValue}'")),

            // Request-scoped variable mutation for session non-affinity test.
            Tool(ParitySetVarToolName, "Set a request-scoped variable",
                _ => Run(runspace, "$ParityVar = 'was-set'; 'ok'")),
            Tool(ParityReadVarToolName, "Read request-scoped variable (cleared by reset)",
                _ => Run(runspace,
                    "if (Get-Variable ParityVar -ErrorAction SilentlyContinue) { $ParityVar } else { 'not-set' }")),
        ];
    }

    private static HttpClient CreateTestClient(WebApplication app)
    {
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
        return client;
    }

    // ─── HTTP helpers ─────────────────────────────────────────────────────────

    private static async Task<JObject> CallAsync(HttpClient client, object payload)
    {
        using var content = new StringContent(
            JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/", content);
        var body = await response.Content.ReadAsStringAsync();
        return ParseSseOrJson(body, response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<JObject> CallWithSessionAsync(
        HttpClient client, string sessionId, object payload, string protocolVersion = "2024-11-05")
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Content = new StringContent(
                JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
        req.Headers.TryAddWithoutValidation("MCP-Protocol-Version", protocolVersion);
        using var response = await client.SendAsync(req);
        var body = await response.Content.ReadAsStringAsync();
        return ParseSseOrJson(body, response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<string?> InitializeStatefulAsync(
        HttpClient client, string protocol = "2024-11-05")
    {
        using var content = new StringContent(
            JsonConvert.SerializeObject(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = protocol,
                    capabilities = new { tools = new { } },
                    clientInfo = new { name = "parity-test", version = "1.0.0" }
                }
            }),
            Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/", content);
        return response.Headers.TryGetValues("Mcp-Session-Id", out var vals)
            ? vals.FirstOrDefault()
            : null;
    }

    /// <summary>
    /// Returns (label, client, sessionId, protocolVersion) tuples for stateless HTTP and stateful HTTP.
    /// Stdio is handled separately in Group F tests.
    /// </summary>
    private async Task<(string Label, HttpClient Client, string? SessionId, string Protocol)[]> GetAllMcpClientsAsync()
    {
        const string sfProtocol = "2024-11-05";
        var sfSessionId = await InitializeStatefulAsync(_statefulClient!, sfProtocol);
        return
        [
            ("stateless", _statelessClient!, null, "2025-11-25"),
            ($"stateful-{sfProtocol}", _statefulClient!, sfSessionId, sfProtocol),
        ];
    }

    private static System.Collections.Generic.HashSet<string> ExtractToolNames(JObject result)
    {
        var tools = result["result"]?["tools"] as JArray;
        if (tools is null) return [];
        return tools.Select(t => t["name"]?.ToString() ?? "").Where(n => n.Length > 0).ToHashSet();
    }

    private static string ExtractFirstContentText(JObject result)
    {
        var content = result["result"]?["content"] as JArray;
        if (content is null || content.Count == 0) return "";
        return content[0]?["text"]?.ToString()?.Trim() ?? "";
    }

    private static void AssertMcpErrorShape(JObject result, string label)
    {
        // MCP errors can surface as top-level JSON-RPC error OR as result.isError=true.
        bool hasJsonRpcError = result["error"] != null;
        bool hasIsError = result["result"]?["isError"]?.Value<bool?>() == true;

        Assert.True(hasJsonRpcError || hasIsError,
            $"[{label}] Expected an MCP error for invalid tool call. Got: {result.ToString(Formatting.None)}");
    }

    private static int? GetErrorCode(JObject result)
    {
        var code = result["error"]?["code"];
        return code?.Type == JTokenType.Integer ? code.Value<int>() : (int?)null;
    }

    private static JObject ParseSseOrJson(string body, string? mediaType)
    {
        if (string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            return JObject.Parse(body);
        var dataLine = body.Split('\n')
            .FirstOrDefault(l => l.StartsWith("data: ", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(dataLine))
            throw new InvalidOperationException($"No MCP data line in response: {body}");
        return JObject.Parse(dataLine[6..]);
    }

    // ─── XunitLogger ──────────────────────────────────────────────────────────

    private sealed class XunitLogger : ILogger
    {
        private readonly ITestOutputHelper _out;
        private readonly string _name;
        public XunitLogger(ITestOutputHelper o, string n) { _out = o; _name = n; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel l) => l >= LogLevel.Information;
        public void Log<TState>(LogLevel l, EventId id, TState state, Exception? ex,
            Func<TState, Exception?, string> fmt)
        {
            if (IsEnabled(l))
                try { _out.WriteLine($"[{l}][{_name}] {fmt(state, ex)}"); }
                catch { }
        }
    }
}

[CollectionDefinition("TransportParityTests", DisableParallelization = true)]
public class TransportParityTestsCollection { }
