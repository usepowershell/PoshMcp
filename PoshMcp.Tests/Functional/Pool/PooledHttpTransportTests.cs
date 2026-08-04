using System;
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

namespace PoshMcp.Tests.Functional.Pool;

/// <summary>
/// Production-faithful tests that exercise real MCP HTTP transport with real
/// <see cref="StatelessRunspacePool"/> and <see cref="PooledHttpRunspace"/>.
/// These tests cover gaps identified in the sync bridge, pool exhaustion under
/// HTTP, and stateful-compat session-ID isolation through the actual transport.
/// </summary>
[Trait("Category", "Functional")]
public sealed class PooledHttpTransportTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;

    // Shared gate that the blocking tool waits on. Registered as a singleton in the
    // test host DI so the MCP tool closure can resolve it.
    private readonly SemaphoreSlim _toolGate = new(0, 1);
    private readonly SemaphoreSlim _toolEntered = new(0, 1);

    // Stateless host (production default)
    private WebApplication? _statelessApp;
    private HttpClient? _statelessClient;
    private StatelessRunspacePool? _pool;

    public PooledHttpTransportTests(ITestOutputHelper output) => _output = output;

    // ─────────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        (_statelessApp, _statelessClient, _pool) = await BuildPooledTestHostAsync(stateless: true);
    }

    public async Task DisposeAsync()
    {
        _statelessClient?.Dispose();
        if (_statelessApp is not null)
        {
            await _statelessApp.StopAsync();
            await _statelessApp.DisposeAsync();
        }
        _toolGate.Dispose();
        _toolEntered.Dispose();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. Sync bridge — production callers (health checks, resource handlers)
    //    exercise PooledHttpRunspace.ExecuteThreadSafe through real HTTP/MCP.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SyncBridgeTool_ThroughHttp_ReturnsResult()
    {
        var result = await CallToolAsync(_statelessClient!, "pool_sync_bridge");
        _output.WriteLine($"sync bridge result: {result}");

        Assert.NotNull(result["result"]);
        Assert.Null(result["error"]);
        var content = result["result"]?["content"]?[0]?["text"]?.ToString();
        Assert.Contains("sync-bridge-ok", content ?? "");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. Async bridge — production tools/call path.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AsyncBridgeTool_ThroughHttp_ReturnsResult()
    {
        var result = await CallToolAsync(_statelessClient!, "pool_async_bridge");
        _output.WriteLine($"async bridge result: {result}");

        Assert.NotNull(result["result"]);
        Assert.Null(result["error"]);
        var content = result["result"]?["content"]?[0]?["text"]?.ToString();
        Assert.Contains("async-bridge-ok", content ?? "");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. Pool exhaustion under HTTP — MaxPoolSize=1, hold the sole worker,
    //    concurrent call gets bounded MCP/HTTP error (not deadlock), then
    //    recovery after release.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PoolExhaustion_ConcurrentCall_ReturnsBoundedError_ThenRecovery()
    {
        // Call the blocking tool — it will hold the sole worker until we release _toolGate.
        var holdingTask = CallToolRawAsync(_statelessClient!, "pool_blocking_tool");

        // Wait for the tool to actually acquire the worker.
        var entered = await _toolEntered.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(entered, "Blocking tool never signaled that it acquired the worker");

        // Pool is exhausted (MaxPoolSize=1). A concurrent call must fail with a bounded
        // timeout error, not deadlock. The pool's AcquisitionTimeout is 2 seconds.
        var concurrentResult = await CallToolAsync(_statelessClient!, "pool_sync_bridge");
        _output.WriteLine($"concurrent call during exhaustion: {concurrentResult}");

        // The MCP SDK wraps internal exceptions as MCP error responses. We accept either:
        // - An MCP error in the JSON-RPC response, or
        // - An isError flag in the result content.
        var hasError = concurrentResult["error"] != null
            || concurrentResult["result"]?["isError"]?.Value<bool>() == true;
        Assert.True(hasError,
            $"Expected a bounded MCP error during pool exhaustion, got: {concurrentResult}");

        // Release the held worker.
        _toolGate.Release();
        var holdingResponse = await holdingTask;
        var holdBody = await holdingResponse.Content.ReadAsStringAsync();
        _output.WriteLine($"blocking tool completed: {holdBody}");
        holdingResponse.Dispose();

        // After release, a new call must succeed.
        var recoveryResult = await CallToolAsync(_statelessClient!, "pool_async_bridge");
        _output.WriteLine($"recovery call: {recoveryResult}");
        Assert.NotNull(recoveryResult["result"]);
        Assert.Null(recoveryResult["error"]);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. Stateful-compat: same & different Mcp-Session-Id values do not pin
    //    or retain PowerShell workers, and PS state does not persist across
    //    requests on the same session ID.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StatefulCompat_SessionIds_DoNotPinWorkers_OrPreserveState()
    {
        // Build a separate stateful-compat host with pool.
        await using var ctx = await BuildPooledStatefulCompatHostAsync();
        var client = ctx.Client;

        // Initialize two sessions (different session IDs).
        var (sessionA, protoA) = await InitializeStatefulSessionAsync(client, "2024-11-05");
        _output.WriteLine($"Session A: {sessionA}");
        Assert.False(string.IsNullOrWhiteSpace(sessionA), "Stateful mode must return Mcp-Session-Id");

        var (sessionB, protoB) = await InitializeStatefulSessionAsync(client, "2024-11-05");
        _output.WriteLine($"Session B: {sessionB}");
        Assert.False(string.IsNullOrWhiteSpace(sessionB), "Stateful mode must return Mcp-Session-Id");
        Assert.NotEqual(sessionA, sessionB);

        // Call A sets a PS variable via the sync bridge tool (production resource-handler path).
        var setResult = await CallToolWithSessionAsync(client, "pool_set_variable", sessionA!);
        _output.WriteLine($"set variable (session A): {setResult}");
        Assert.Null(setResult["error"]);

        // Call B reads the variable — must NOT see session A's state (workers are anonymous).
        var getResultB = await CallToolWithSessionAsync(client, "pool_get_variable", sessionB!);
        _output.WriteLine($"get variable (session B): {getResultB}");
        Assert.Null(getResultB["error"]);
        var valB = getResultB["result"]?["content"]?[0]?["text"]?.ToString();
        Assert.True(string.IsNullOrEmpty(valB) || valB == "not-set",
            $"Session B must not see session A's PS state, got: '{valB}'");

        // Same session A reads the variable — must ALSO not see it (pool reset protocol).
        var getResultA = await CallToolWithSessionAsync(client, "pool_get_variable", sessionA!);
        _output.WriteLine($"get variable (session A): {getResultA}");
        Assert.Null(getResultA["error"]);
        var valA = getResultA["result"]?["content"]?[0]?["text"]?.ToString();
        Assert.True(string.IsNullOrEmpty(valA) || valA == "not-set",
            $"Same session A must not retain PS state across requests, got: '{valA}'");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. Stateful-compat: sync bridge through real HTTP/MCP transport with
    //    session headers.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StatefulCompat_SyncBridge_ThroughHttpWithSessionId_ReturnsResult()
    {
        await using var ctx = await BuildPooledStatefulCompatHostAsync();
        var client = ctx.Client;

        var (sessionId, _) = await InitializeStatefulSessionAsync(client, "2024-11-05");
        Assert.False(string.IsNullOrWhiteSpace(sessionId));

        var result = await CallToolWithSessionAsync(client, "pool_sync_bridge", sessionId!);
        _output.WriteLine($"stateful sync bridge result: {result}");
        Assert.NotNull(result["result"]);
        Assert.Null(result["error"]);
        var content = result["result"]?["content"]?[0]?["text"]?.ToString();
        Assert.Contains("sync-bridge-ok", content ?? "");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Host builders — wire real pool + PooledHttpRunspace into TestServer
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<(WebApplication App, HttpClient Client, StatelessRunspacePool Pool)> BuildPooledTestHostAsync(bool stateless)
    {
        var poolOptions = new RunspacePoolOptions
        {
            MinPoolSize = 1,
            MaxPoolSize = 1,
            EagerWarmCount = 1,
            AcquisitionTimeout = TimeSpan.FromSeconds(2),
            IdleTtl = TimeSpan.FromSeconds(300),
            SweepInterval = TimeSpan.FromSeconds(60),
            StopTimeout = TimeSpan.FromSeconds(5),
            ShutdownDrainTimeout = TimeSpan.FromSeconds(5),
            ReplenishCheckInterval = TimeSpan.FromSeconds(60),
        };

        var pool = new StatelessRunspacePool(
            poolOptions,
            loggerFactory: null,
            startupScript: null);
        await pool.StartAsync();

        var pooledRunspace = new PooledHttpRunspace(pool, (string?)null, NullLoggerFactory.Instance);
        pooledRunspace.FinalizeDiscovery();

        var sessionLifecycle = new McpSessionLifecycle(_ => { });
        var tools = CreateTestTools(pooledRunspace);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddSingleton(sessionLifecycle);
        builder.Services.AddSingleton<IPowerShellRunspace>(pooledRunspace);
        builder.Services.AddSingleton<IRunspacePool>(pool);
        builder.Services.AddSingleton<IHostedService, RunspacePoolLifecycleService>();

        var mcpBuilder = builder.Services
            .AddMcpServer()
            .WithHttpTransport(opts =>
            {
                opts.Stateless = stateless;
#pragma warning disable MCP9006
                opts.IdleTimeout = Timeout.InfiniteTimeSpan;
#pragma warning restore MCP9006
                if (!stateless)
                {
#pragma warning disable MCPEXP002
                    opts.RunSessionHandler = sessionLifecycle.RunSessionAsync;
#pragma warning restore MCPEXP002
                }
            })
            .WithTools(tools);

        var app = builder.Build();
        app.UseMiddleware<McpProtocolVersionMiddleware>((object)new[] { "/" });
        app.MapMcp();

        await app.StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");

        return (app, client, pool);
    }

    private async Task<StatefulCompatContext> BuildPooledStatefulCompatHostAsync()
    {
        var (app, client, pool) = await BuildPooledTestHostAsync(stateless: false);
        return new StatefulCompatContext(app, client, pool);
    }

    /// <summary>
    /// Creates MCP tools that exercise both sync and async bridges of
    /// <see cref="PooledHttpRunspace"/> through real PowerShell execution.
    /// </summary>
    private McpServerTool[] CreateTestTools(IPowerShellRunspace runspace)
    {
        // Tool 1: Sync bridge — mirrors production health-check / resource-handler pattern.
        var syncBridge = McpServerTool.Create(
            (CancellationToken ct) =>
            {
                var output = runspace.ExecuteThreadSafe(ps =>
                {
                    ps.Commands.Clear();
                    ps.AddScript("'sync-bridge-ok'");
                    var results = ps.Invoke<string>();
                    ps.Commands.Clear();
                    return results.Count > 0 ? results[0] : "no-output";
                });
                return Task.FromResult(output ?? "null");
            },
            new McpServerToolCreateOptions
            {
                Name = "pool_sync_bridge",
                Description = "Exercises PooledHttpRunspace.ExecuteThreadSafe (sync bridge)"
            });

        // Tool 2: Async bridge — mirrors production tools/call pattern.
        var asyncBridge = McpServerTool.Create(
            async (CancellationToken ct) =>
            {
                var output = await runspace.ExecuteThreadSafeAsync(ps =>
                {
                    ps.Commands.Clear();
                    ps.AddScript("'async-bridge-ok'");
                    var results = ps.Invoke<string>();
                    ps.Commands.Clear();
                    return Task.FromResult(results.Count > 0 ? results[0] : "no-output");
                });
                return output ?? "null";
            },
            new McpServerToolCreateOptions
            {
                Name = "pool_async_bridge",
                Description = "Exercises PooledHttpRunspace.ExecuteThreadSafeAsync (async bridge)"
            });

        // Tool 3: Blocking tool — holds the worker until _toolGate is released.
        // Uses the sync bridge like production resource handlers.
        var gate = _toolGate;
        var entered = _toolEntered;
        var blockingTool = McpServerTool.Create(
            (CancellationToken ct) =>
            {
                var output = runspace.ExecuteThreadSafe(ps =>
                {
                    ps.Commands.Clear();
                    ps.AddScript("'blocking-tool-acquired'");
                    var results = ps.Invoke<string>();
                    ps.Commands.Clear();
                    // Signal that we have acquired the worker.
                    entered.Release();
                    // Hold the worker until the test releases the gate.
                    gate.Wait(TimeSpan.FromSeconds(30));
                    return results.Count > 0 ? results[0] : "no-output";
                });
                return Task.FromResult(output ?? "null");
            },
            new McpServerToolCreateOptions
            {
                Name = "pool_blocking_tool",
                Description = "Holds the sole pool worker for exhaustion testing"
            });

        // Tool 4: Set PS variable — for session-ID isolation testing (sync bridge).
        var setVariable = McpServerTool.Create(
            (CancellationToken ct) =>
            {
                runspace.ExecuteThreadSafe(ps =>
                {
                    ps.Commands.Clear();
                    ps.AddScript("$script:PoolTestSentinel = 'session-pinned'");
                    ps.Invoke();
                    ps.Commands.Clear();
                });
                return Task.FromResult("variable-set");
            },
            new McpServerToolCreateOptions
            {
                Name = "pool_set_variable",
                Description = "Sets a PS variable to test session isolation"
            });

        // Tool 5: Get PS variable — reads back the sentinel (sync bridge).
        var getVariable = McpServerTool.Create(
            (CancellationToken ct) =>
            {
                var value = runspace.ExecuteThreadSafe(ps =>
                {
                    ps.Commands.Clear();
                    ps.AddScript(
                        "if (Get-Variable -Name 'PoolTestSentinel' -ErrorAction SilentlyContinue) " +
                        "{ $PoolTestSentinel } else { 'not-set' }");
                    var results = ps.Invoke<string>();
                    ps.Commands.Clear();
                    return results.Count > 0 ? results[0] : "not-set";
                });
                return Task.FromResult(value ?? "not-set");
            },
            new McpServerToolCreateOptions
            {
                Name = "pool_get_variable",
                Description = "Reads a PS variable to test session isolation"
            });

        return [syncBridge, asyncBridge, blockingTool, setVariable, getVariable];
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MCP HTTP helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<JObject> CallToolAsync(HttpClient client, string toolName)
    {
        using var response = await CallToolRawAsync(client, toolName);
        var body = await response.Content.ReadAsStringAsync();
        return ParseJsonOrSse(body, response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<HttpResponseMessage> CallToolRawAsync(HttpClient client, string toolName)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString("N")[..8],
            method = "tools/call",
            @params = new { name = toolName, arguments = new { } }
        };
        var content = new StringContent(
            JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
        return await client.PostAsync("/", content);
    }

    private static async Task<JObject> CallToolWithSessionAsync(
        HttpClient client, string toolName, string sessionId)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString("N")[..8],
            method = "tools/call",
            @params = new { name = toolName, arguments = new { } }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Content = new StringContent(
                JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2024-11-05");
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return ParseJsonOrSse(body, response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<(string? SessionId, string ProtocolVersion)> InitializeStatefulSessionAsync(
        HttpClient client, string protocolVersion)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion,
                capabilities = new { tools = new { } },
                clientInfo = new { name = "pool-isolation-test", version = "1.0.0" }
            }
        };
        using var response = await client.PostAsync("/",
            new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));
        var sessionId = response.Headers.TryGetValues("Mcp-Session-Id", out var vals)
            ? vals.FirstOrDefault() : null;
        return (sessionId, protocolVersion);
    }

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

    // ─────────────────────────────────────────────────────────────────────────
    // Disposable context for stateful-compat hosts
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class StatefulCompatContext : IAsyncDisposable
    {
        public WebApplication App { get; }
        public HttpClient Client { get; }
        public StatelessRunspacePool Pool { get; }

        public StatefulCompatContext(WebApplication app, HttpClient client, StatelessRunspacePool pool)
        {
            App = app;
            Client = client;
            Pool = pool;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }
}
