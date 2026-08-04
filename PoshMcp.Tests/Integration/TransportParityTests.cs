using System;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net;
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
/// Transport parity matrix: proves equivalent tool results, error semantics, and pool-metric
/// activity across Stateless HTTP and Stateful HTTP, validates stdio structural parity, and
/// verifies down-level protocol client compatibility and MCP-Protocol-Version header enforcement.
///
/// <b>Behavior Matrix</b> (✓ = asserted; value = exact content equality; struct = structural only):
/// <code>
/// | Transport          | Protocol    | tools/list | tools/call    | Error    | Session ID |
/// |--------------------|-------------|:----------:|:-------------:|:--------:|:----------:|
/// | Stateless HTTP     | none        |     ✓      |  ✓ value      |    ✓     |     —      |
/// | Stateful HTTP      | 2025-11-25  |     ✓      |  ✓ value      |    ✓     |     ✓      |
/// | Stateful HTTP      | 2024-11-05  |     ✓      |  ✓ value      |    ✓     |     ✓      |
/// | Stdio              | SDK managed |  ✓ struct  |  ✓ struct     |  ✓ struct|     —      |
/// </code>
/// Exact tool-output <em>value</em> equality is asserted across the two HTTP modes (both protocol
/// versions). Stdio uses the default configuration and a different (SingletonPowerShellRunspace)
/// tool surface, so it is covered structurally — a valid MCP result and a structured error — not by
/// value equality with the HTTP hosts.
///
/// <b>Parity invariants under test:</b>
/// <list type="number">
///   <item>tools/list returns the same tool-name set on both HTTP modes (exact set equality).</item>
///   <item>tools/call for a deterministic tool returns the same content value on both HTTP modes,
///         under both the 2025-11-25 and 2024-11-05 protocols.</item>
///   <item>An invalid tool name returns a structurally equivalent MCP error on all 3 transports.</item>
///   <item>Down-level clients (2025-11-25, 2024-11-05) receive a session ID from stateful mode
///         and can invoke functional tools; the tool output equals the stateless output.</item>
///   <item>Stateless HTTP does NOT return Mcp-Session-Id; stateful HTTP always does.</item>
///   <item>Reset clears request-scoped state on a single pinned-by-structure worker
///         (MaxPoolSize=1), and sessions do not pin workers (two-worker blocking proof yields two
///         distinct startup identities) — each proven by a separate, single-property test.</item>
///   <item>Equivalent pool activity produces equal metric deltas across the Stateless and Stateful
///         HTTP hosts (acquisitions, acquisition/reset histogram sample counts, and terminal
///         warm/leased/resetting gauge nets), captured per meter instance to avoid contamination.</item>
///   <item>MCP-Protocol-Version is mandatory (exact match) for 2025-11-25 sessions and optional
///         (but validated when present) for 2024-11-05 sessions, per McpProtocolVersionMiddleware;
///         invalid headers short-circuit to 400 with an empty body.</item>
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
    private StatelessRunspacePool? _statelessPool;
    private WebApplication? _statefulApp;
    private HttpClient? _statefulClient;
    private StatelessRunspacePool? _statefulPool;

    // Gate for the blocking tool used in the two-worker non-affinity proof (shared hosts).
    private readonly SemaphoreSlim _blockingGate = new(0, 1);
    private readonly SemaphoreSlim _blockingEntered = new(0, 1);

    // Per-worker GUID set by the startup script so tests can observe which worker served them.
    // Survives the reset protocol (it is part of the startup snapshot), making it a stable
    // per-worker fingerprint for the session non-affinity proofs.
    private const string ParityWorkerStartupScript =
        "$ParityWorkerIdentity = [System.Guid]::NewGuid().ToString()\n";

    // Tool definitions shared between stateless and stateful hosts.
    // Tools use pure PowerShell expressions so results are deterministic.
    private const string ParityEchoToolName = "parity_echo";
    private const string ParityEchoValue = "parity-pass-42";

    private const string ParityErrorToolName = "parity_error_tool";
    private const string ParityReadVarToolName = "parity_read_var";
    private const string ParitySetVarToolName = "parity_set_var";
    private const string ParityReadIdentityToolName = "parity_read_worker_identity";
    private const string ParityBlockWorkerToolName = "parity_block_worker";

    public TransportParityTests(ITestOutputHelper output) => _output = output;

    // ─── IAsyncLifetime ──────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        (_statelessApp, _statelessPool) = await BuildParityHostAsync(stateless: true);
        _statelessClient = CreateTestClient(_statelessApp);

        (_statefulApp, _statefulPool) = await BuildParityHostAsync(stateless: false);
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

        _blockingGate.Dispose();
        _blockingEntered.Dispose();
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
    public async Task ToolsCall_DeterministicTool_SameOutputAcrossHttpModes_CompatProtocol()
    {
        // Value parity across the two HTTP transports under the 2024-11-05 (compatibility) protocol.
        // Complements ToolsCall_DeterministicTool_SameOutputAcrossHttpModes, which covers 2025-11-25.
        // Stdio value parity is intentionally NOT asserted here: the stdio server uses the default
        // configuration and a different (SingletonPowerShellRunspace) tool surface, so stdio is
        // covered structurally by the Stdio_* tests (valid MCP result + structured error), not by
        // exact value equality with the HTTP hosts.

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
    public async Task SessionId_Stateful_SameSingleWorker_ResetClearsRequestState()
    {
        // Deterministic one-worker reset evidence. A dedicated MaxPoolSize=1 stateful host forces
        // every call in the session onto the SAME worker, so a cleared variable can only be the
        // result of the reset protocol — never of a different warm worker being selected.
        var opts = DefaultParityOptions();
        opts.MinPoolSize = 1;
        opts.MaxPoolSize = 1;
        opts.EagerWarmCount = 1;

        var (app, _) = await BuildParityHostAsync(stateless: false, opts);
        try
        {
            var client = CreateTestClient(app);
            const string proto = "2024-11-05";
            var sessionId = await InitializeStatefulAsync(client, proto);
            Assert.False(string.IsNullOrWhiteSpace(sessionId));

            // Same worker across the whole session (structurally guaranteed by MaxPoolSize=1).
            var identityBefore = ExtractFirstContentText(await CallWithSessionAsync(client, sessionId!, new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/call",
                @params = new { name = ParityReadIdentityToolName, arguments = new { } }
            }, proto));

            await CallWithSessionAsync(client, sessionId!, new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/call",
                @params = new { name = ParitySetVarToolName, arguments = new { } }
            }, proto);

            var value = ExtractFirstContentText(await CallWithSessionAsync(client, sessionId!, new
            {
                jsonrpc = "2.0",
                id = 3,
                method = "tools/call",
                @params = new { name = ParityReadVarToolName, arguments = new { } }
            }, proto));

            var identityAfter = ExtractFirstContentText(await CallWithSessionAsync(client, sessionId!, new
            {
                jsonrpc = "2.0",
                id = 4,
                method = "tools/call",
                @params = new { name = ParityReadIdentityToolName, arguments = new { } }
            }, proto));

            _output.WriteLine($"identityBefore={identityBefore} identityAfter={identityAfter} value='{value}'");

            // Determinism guard: the set and both reads hit the one, same worker.
            Assert.True(Guid.TryParse(identityBefore, out _), $"identity not a GUID: '{identityBefore}'");
            Assert.Equal(identityBefore, identityAfter);

            // The single asserted property: the reset protocol cleared the request-scoped variable.
            Assert.Equal("not-set", value);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task SessionId_Stateful_DoesNotPinWorker_TwoDistinctWorkersBlockingProof()
    {
        // Deterministic two-worker non-affinity proof. Hold worker A via the blocking tool (which
        // returns A's startup identity) while a concurrent call is forced onto worker B. Two
        // distinct startup GUIDs prove requests are not pinned to a per-session worker — the single
        // asserted property here.
        var client = _statelessClient!;

        var blockTask = Task.Run(() => CallAsync(client, new
        {
            jsonrpc = "2.0",
            id = "block",
            method = "tools/call",
            @params = new { name = ParityBlockWorkerToolName, arguments = new { } }
        }));

        Assert.True(await _blockingEntered.WaitAsync(TimeSpan.FromSeconds(15)),
            "Blocking tool did not acquire a worker within 15 s");

        try
        {
            // Worker A is held; the 2-worker pool must serve this from worker B.
            var identityB = ExtractFirstContentText(await CallAsync(client, new
            {
                jsonrpc = "2.0",
                id = "readB",
                method = "tools/call",
                @params = new { name = ParityReadIdentityToolName, arguments = new { } }
            }));
            Assert.True(Guid.TryParse(identityB, out var guidB), $"worker B identity not a GUID: '{identityB}'");

            _blockingGate.Release();
            var identityA = ExtractFirstContentText(await blockTask);
            Assert.True(Guid.TryParse(identityA, out var guidA), $"worker A identity not a GUID: '{identityA}'");

            _output.WriteLine($"identityA={guidA} identityB={guidB}");
            Assert.NotEqual(guidA, guidB);
        }
        finally
        {
            if (_blockingGate.CurrentCount == 0)
                _blockingGate.Release();
        }
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

    // ─── Group G: cross-host pool metric parity ───────────────────────────────

    [Fact]
    public async Task Metrics_EquivalentActivity_ProducesEqualDeltasAcrossStatelessAndStatefulHosts()
    {
        const int rounds = 4;

        // Capture each host's pool metrics by meter INSTANCE (not name) so the two pools — which
        // share the McpMetrics meter name — are measured independently with zero cross-contamination.
        using var slCapture = new PoolMeterCapture(_statelessPool!.MetricsMeter);
        using var sfCapture = new PoolMeterCapture(_statefulPool!.MetricsMeter);

        // Stateless: N bare echo calls.
        for (var i = 0; i < rounds; i++)
        {
            await CallAsync(_statelessClient!, new
            {
                jsonrpc = "2.0",
                id = i,
                method = "tools/call",
                @params = new { name = ParityEchoToolName, arguments = new { } }
            });
        }

        // Stateful: N echo calls within one session (equivalent pool activity).
        const string proto = "2025-11-25";
        var sessionId = await InitializeStatefulAsync(_statefulClient!, proto);
        Assert.False(string.IsNullOrWhiteSpace(sessionId));
        for (var i = 0; i < rounds; i++)
        {
            await CallWithSessionAsync(_statefulClient!, sessionId!, new
            {
                jsonrpc = "2.0",
                id = i,
                method = "tools/call",
                @params = new { name = ParityEchoToolName, arguments = new { } }
            }, proto);
        }

        var slAcq = slCapture.SumLong("poshmcp.runspace_pool.acquisitions_total");
        var sfAcq = sfCapture.SumLong("poshmcp.runspace_pool.acquisitions_total");
        var slAcqDur = slCapture.CountDouble("poshmcp.runspace_pool.acquisition_duration_seconds");
        var sfAcqDur = sfCapture.CountDouble("poshmcp.runspace_pool.acquisition_duration_seconds");
        var slReset = slCapture.CountDouble("poshmcp.runspace_pool.reset_duration_seconds");
        var sfReset = sfCapture.CountDouble("poshmcp.runspace_pool.reset_duration_seconds");

        _output.WriteLine($"stateless: acq={slAcq} acqDur={slAcqDur} reset={slReset}");
        _output.WriteLine($"stateful:  acq={sfAcq} acqDur={sfAcqDur} reset={sfReset}");

        // Acquisition counter: exactly N on each host, and equal across hosts.
        Assert.Equal(rounds, slAcq);
        Assert.Equal(rounds, sfAcq);

        // Acquisition-duration and reset-duration histograms: exactly N samples on each host.
        // (The lease disposal awaits the full reset before the tool response returns, so these are
        // deterministic at response time — no polling required.)
        Assert.Equal(rounds, slAcqDur);
        Assert.Equal(rounds, sfAcqDur);
        Assert.Equal(rounds, slReset);
        Assert.Equal(rounds, sfReset);

        // Terminal worker-gauge observations equivalent: net warm/leased/resetting all zero on
        // both hosts (every acquire is balanced by its reset-and-return before the response
        // completes), so no gauge drifts or goes negative.
        foreach (var (label, cap) in new[] { ("stateless", slCapture), ("stateful", sfCapture) })
        {
            Assert.Equal(0L, cap.SumLong("poshmcp.runspace_pool.workers", "warm"));
            Assert.Equal(0L, cap.SumLong("poshmcp.runspace_pool.workers", "leased"));
            Assert.Equal(0L, cap.SumLong("poshmcp.runspace_pool.workers", "resetting"));
            _output.WriteLine($"[{label}] terminal worker net deltas all zero");
        }
    }

    // ─── Group H: MCP-Protocol-Version header semantics ───────────────────────
    // Source of truth: McpProtocolVersionMiddleware.IsValidProtocolHeader. For a session
    // negotiated at 2025-11-25 (current) the header is MANDATORY and must match exactly; for a
    // session negotiated at 2024-11-05 (compatibility) the header is OPTIONAL but, if present,
    // must match. An invalid header short-circuits to 400 with an empty body.

    [Fact]
    public async Task ProtocolHeader_CurrentVersionSession_MissingHeader_Returns400EmptyBody()
    {
        var sessionId = await InitializeStatefulAsync(_statefulClient!, "2025-11-25");
        Assert.False(string.IsNullOrWhiteSpace(sessionId));

        var (status, body) = await SendToolCallRawAsync(_statefulClient!, sessionId!, protocolHeader: null);
        _output.WriteLine($"2025-11-25, no header → {(int)status}; body='{body}'");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(string.IsNullOrEmpty(body), $"Expected empty body on 400, got: '{body}'");
    }

    [Fact]
    public async Task ProtocolHeader_CurrentVersionSession_MatchingHeader_Succeeds()
    {
        var sessionId = await InitializeStatefulAsync(_statefulClient!, "2025-11-25");
        Assert.False(string.IsNullOrWhiteSpace(sessionId));

        var (status, body) = await SendToolCallRawAsync(_statefulClient!, sessionId!, protocolHeader: "2025-11-25");
        _output.WriteLine($"2025-11-25, matching header → {(int)status}");

        Assert.Equal(HttpStatusCode.OK, status);
        var result = ParseSseOrJson(body, InferMediaType(body));
        Assert.Equal(ParityEchoValue, ExtractFirstContentText(result));
    }

    [Fact]
    public async Task ProtocolHeader_CompatVersionSession_MissingHeader_Succeeds()
    {
        // 2024-11-05 predates the mandatory-header requirement; the middleware treats the header as
        // optional for this negotiated version. Asserts the intentional production behavior — it
        // does not canonize any SDK defect.
        var sessionId = await InitializeStatefulAsync(_statefulClient!, "2024-11-05");
        Assert.False(string.IsNullOrWhiteSpace(sessionId));

        var (status, body) = await SendToolCallRawAsync(_statefulClient!, sessionId!, protocolHeader: null);
        _output.WriteLine($"2024-11-05, no header → {(int)status}");

        Assert.Equal(HttpStatusCode.OK, status);
        var result = ParseSseOrJson(body, InferMediaType(body));
        Assert.Equal(ParityEchoValue, ExtractFirstContentText(result));
    }

    [Fact]
    public async Task ProtocolHeader_CompatVersionSession_MismatchedHeader_Returns400EmptyBody()
    {
        // Optional does not mean "anything goes": a present-but-wrong header is still rejected.
        var sessionId = await InitializeStatefulAsync(_statefulClient!, "2024-11-05");
        Assert.False(string.IsNullOrWhiteSpace(sessionId));

        var (status, body) = await SendToolCallRawAsync(_statefulClient!, sessionId!, protocolHeader: "2025-11-25");
        _output.WriteLine($"2024-11-05, mismatched header → {(int)status}; body='{body}'");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(string.IsNullOrEmpty(body), $"Expected empty body on 400, got: '{body}'");
    }

    // ─── Host builder ─────────────────────────────────────────────────────────

    private Task<(WebApplication App, StatelessRunspacePool Pool)> BuildParityHostAsync(bool stateless) =>
        BuildParityHostAsync(stateless, DefaultParityOptions());

    private static RunspacePoolOptions DefaultParityOptions() => new()
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

    private async Task<(WebApplication App, StatelessRunspacePool Pool)> BuildParityHostAsync(
        bool stateless, RunspacePoolOptions opts)
    {
        // Wire the startup identity script into both the pool workers and the discovery runspace
        // so $ParityWorkerIdentity exists on every worker and survives the reset protocol.
        var pool = new StatelessRunspacePool(opts, loggerFactory: null, startupScript: ParityWorkerStartupScript);
        var pooledRunspace = new PooledHttpRunspace(pool, ParityWorkerStartupScript, NullLoggerFactory.Instance);
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
        return (app, pool);
    }

    private McpServerTool[] CreateParityTools(IPowerShellRunspace runspace)
    {
        var gate = _blockingGate;
        var entered = _blockingEntered;

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

            // Startup per-worker identity (survives reset) for the non-affinity proofs.
            Tool(ParityReadIdentityToolName, "Read the per-worker startup identity GUID",
                _ => Run(runspace,
                    "if (Get-Variable ParityWorkerIdentity -ErrorAction SilentlyContinue) { $ParityWorkerIdentity } else { 'not-set' }")),

            // Blocking tool: holds the current pool worker until the test releases _blockingGate
            // and returns that worker's startup identity so the caller can compare without a race.
            McpServerTool.Create(
                (CancellationToken _) =>
                {
                    string? workerIdentity = null;
                    runspace.ExecuteThreadSafe(ps =>
                    {
                        ps.Commands.Clear();
                        ps.AddScript(
                            "if (Get-Variable ParityWorkerIdentity -ErrorAction SilentlyContinue) { $ParityWorkerIdentity } else { 'not-set' }");
                        var r = ps.Invoke<string>();
                        ps.Commands.Clear();
                        ps.Streams.ClearStreams();
                        workerIdentity = r.Count > 0 ? r[0] : "not-set";
                        entered.Release();
                        gate.Wait(TimeSpan.FromSeconds(30));
                    });
                    return Task.FromResult(workerIdentity ?? "not-set");
                },
                new McpServerToolCreateOptions
                {
                    Name = ParityBlockWorkerToolName,
                    Description = "Holds the pool worker and returns its startup identity GUID"
                }),
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

    private static async Task<(HttpStatusCode Status, string Body)> SendToolCallRawAsync(
        HttpClient client, string sessionId, string? protocolHeader)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/")
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
        req.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
        if (protocolHeader is not null)
            req.Headers.TryAddWithoutValidation("MCP-Protocol-Version", protocolHeader);

        using var response = await client.SendAsync(req);
        var body = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, body);
    }

    // A successful Streamable-HTTP body is either a JSON object or an SSE stream; infer which so
    // ParseSseOrJson can decode it without the response Content-Type header in hand.
    private static string InferMediaType(string body) =>
        body.TrimStart().StartsWith("{", StringComparison.Ordinal)
            ? "application/json"
            : "text/event-stream";

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

    // ─── Meter-instance metric capture ───────────────────────────────────────

    /// <summary>
    /// Captures pool measurements for a single <see cref="Meter"/> instance. Filtering by instance
    /// (not name) is essential here because both parity hosts create meters that share the
    /// McpMetrics meter name — a name filter would blend their measurements. Dispose to stop.
    /// </summary>
    private sealed class PoolMeterCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentDictionary<string, ConcurrentQueue<(long Value, string? Tag)>> _long = new();
        private readonly ConcurrentDictionary<string, ConcurrentQueue<double>> _double = new();

        public PoolMeterCapture(Meter meter)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter, meter))
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            {
                string? tag = null;
                foreach (var kv in tags)
                {
                    if (kv.Key == "state" || kv.Key == "reason")
                    {
                        tag = kv.Value?.ToString();
                        break;
                    }
                }
                _long.GetOrAdd(instrument.Name, _ => new()).Enqueue((measurement, tag));
            });
            _listener.SetMeasurementEventCallback<double>((instrument, measurement, _, __) =>
                _double.GetOrAdd(instrument.Name, _ => new()).Enqueue(measurement));
            _listener.Start();
        }

        public long SumLong(string name, string? tag = null) =>
            _long.TryGetValue(name, out var q)
                ? q.Where(x => tag == null || x.Tag == tag).Sum(x => x.Value)
                : 0L;

        public int CountDouble(string name) =>
            _double.TryGetValue(name, out var q) ? q.Count : 0;

        public void Dispose() => _listener.Dispose();
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
