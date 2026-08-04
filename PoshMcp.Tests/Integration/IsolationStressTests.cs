using System;
using System.Collections.Generic;
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
/// Isolation stress and resilience tests for issue #351.
/// Covers 100+ concurrent real MCP/HTTP requests with contamination probes (stateless and
/// stateful), sequential 50-round all-fields contamination scenarios, and graceful drain
/// semantics through the pool API while the HTTP host continues running.
///
/// All tests use real PowerShell runspaces through the full TestServer → PooledHttpRunspace
/// → StatelessRunspacePool → IsolatedPowerShellRunspace stack. No mocks in the execution
/// path. No arbitrary sleeps; all coordination uses barriers, gates, and polling loops.
/// </summary>
[Trait("Category", "Integration")]
public sealed class IsolationStressTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;

    // Shared stateless host for concurrent and sequential tests.
    private WebApplication? _statelessApp;
    private HttpClient? _statelessClient;

    // Shared stateful host for concurrent stateful test.
    private WebApplication? _statefulApp;
    private HttpClient? _statefulClient;

    // Per-worker identity script so tests can observe startup isolation.
    private const string StressStartupScript = @"
$WorkerToken = [System.Guid]::NewGuid().ToString()
function Get-StressWorkerToken { return $WorkerToken }
";

    // Expected clean read after reset: all 7 PS state fields at baseline.
    private const string CleanStatePattern = "var=not-set;err=0;pref=Continue;drive=False;func=not-defined;alias=not-set";

    public IsolationStressTests(ITestOutputHelper output) => _output = output;

    // ─── IAsyncLifetime ──────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        (_statelessApp, _statelessClient, _) = await BuildStressHostAsync(stateless: true);
        (_statefulApp, _statefulClient, _) = await BuildStressHostAsync(stateless: false);
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

    // ─── 1. 100 concurrent stateless requests — zero cross-request variable leakage ────

    /// <summary>
    /// 100 concurrent HTTP/MCP tool calls each contaminate a unique GUID into
    /// <c>$IsoStressVar</c> and assert the pre-contamination value is always "not-set".
    /// Because the pool resets the worker after every request, the next caller on the same
    /// worker must always find an empty variable — even under full concurrent load.
    ///
    /// This test would fail deterministically if the reset protocol stopped clearing variables,
    /// or if any worker were re-used without reset.
    /// </summary>
    [Fact]
    public async Task Concurrent_100Requests_ZeroCrossLeakage_Stateless()
    {
        const int requestCount = 100;
        _output.WriteLine($"Launching {requestCount} concurrent stateless requests.");

        var markers = Enumerable.Range(0, requestCount)
            .Select(_ => Guid.NewGuid().ToString("N"))
            .ToArray();

        // All tasks start their HTTP calls in parallel (no barrier needed — Task.WhenAll
        // schedules them simultaneously; TestServer queues excess beyond MaxPoolSize).
        var tasks = markers.Select((marker, index) => Task.Run(async () =>
        {
            var result = await CallToolAsync(_statelessClient!, "stress_check_and_set",
                new { marker });
            var found = ExtractText(result);
            return (index, marker, found);
        })).ToArray();

        var results = await Task.WhenAll(tasks);

        // Every call must have found "not-set" — no prior request's marker should survive reset.
        var leaks = results.Where(r => r.found != "not-set").ToArray();
        Assert.True(leaks.Length == 0,
            $"{leaks.Length} request(s) observed cross-request variable leakage:\n" +
            string.Join("\n", leaks.Select(r =>
                $"  request[{r.index}] expected 'not-set', found '{r.found}'")));

        _output.WriteLine($"Stateless: all {requestCount} requests found clean state.");
    }

    // ─── 2. 100 concurrent stateful requests — session IDs never pin PS workers ────────

    /// <summary>
    /// 100 concurrent HTTP/MCP tool calls through a single stateful session.
    /// All calls share the same <c>Mcp-Session-Id</c>, which the production code must NOT
    /// use to select or retain workers. Each call must still find clean PS state.
    ///
    /// This test fails if any session-keyed worker selection or state retention is introduced.
    /// </summary>
    [Fact]
    public async Task Concurrent_100Requests_ZeroCrossLeakage_Stateful()
    {
        const int requestCount = 100;
        var sessionId = await InitializeStatefulSessionAsync(_statefulClient!);
        Assert.False(string.IsNullOrEmpty(sessionId),
            "Stateful mode must return Mcp-Session-Id on initialize.");
        _output.WriteLine($"Stateful session: {sessionId}. Launching {requestCount} concurrent requests.");

        var markers = Enumerable.Range(0, requestCount)
            .Select(_ => Guid.NewGuid().ToString("N"))
            .ToArray();

        var tasks = markers.Select((marker, index) => Task.Run(async () =>
        {
            var result = await CallToolWithSessionAsync(_statefulClient!, "stress_check_and_set",
                sessionId!, new { marker });
            var found = ExtractText(result);
            return (index, marker, found);
        })).ToArray();

        var results = await Task.WhenAll(tasks);

        var leaks = results.Where(r => r.found != "not-set").ToArray();
        Assert.True(leaks.Length == 0,
            $"{leaks.Length} stateful request(s) observed cross-request variable leakage " +
            $"(session={sessionId}):\n" +
            string.Join("\n", leaks.Select(r =>
                $"  request[{r.index}] expected 'not-set', found '{r.found}'")));

        _output.WriteLine($"Stateful: all {requestCount} requests found clean state.");
    }

    // ─── 3. Sequential 50-round all-fields contamination — every field reset correctly ──

    /// <summary>
    /// 50 sequential rounds on a single-worker host. Each round contaminates all 7 PS state
    /// fields (variable, $Error, preference, PSDrive, function, alias, and the variable set by
    /// <c>stress_check_and_set</c>) and then asserts the subsequent call reads exactly the
    /// baseline clean state.
    ///
    /// MaxPoolSize=1 ensures every call goes through the same worker, making the contaminate →
    /// reset → read assertion deterministic and unambiguous.
    ///
    /// This test would fail if any reset field is omitted or the reset script has a bug.
    /// </summary>
    [Fact]
    public async Task Sequential_50Rounds_AllFields_ZeroLeak_Stateless()
    {
        var (app, client) = await BuildSingleWorkerHostAsync();
        try
        {
            const int rounds = 50;
            _output.WriteLine($"Running {rounds} sequential all-fields contamination rounds.");

            for (int r = 0; r < rounds; r++)
            {
                var marker = $"r{r}_{Guid.NewGuid():N}";

                // Round N: contaminate all fields with a unique marker.
                var contamResult = await CallToolAsync(client, "stress_contaminate_all",
                    new { marker });
                Assert.Null(contamResult["error"]);

                // Round N+1: read all fields — reset ran after contaminate, so state must be clean.
                // On a MaxPoolSize=1 host, this call goes through the same worker after reset.
                var readResult = await CallToolAsync(client, "stress_read_all_clean");
                Assert.Null(readResult["error"]);
                var readText = ExtractText(readResult);

                Assert.True(readText == CleanStatePattern,
                    $"Round {r}: expected '{CleanStatePattern}' after reset, got '{readText}'. " +
                    $"Contamination marker was '{marker}'.");
            }

            _output.WriteLine($"All {rounds} rounds: every field clean after reset.");
        }
        finally
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    // ─── 4. Graceful drain — in-flight completes, new acquisitions rejected ─────────────

    /// <summary>
    /// Holds one pool worker via a blocking HTTP tool call, then calls
    /// <see cref="IRunspacePool.DrainAsync"/> directly. Proves three things:
    /// <list type="bullet">
    ///   <item>Drain does not complete while a lease is outstanding.</item>
    ///   <item>New acquisition attempts after drain starts get a bounded error (not a deadlock).</item>
    ///   <item>Releasing the held lease allows drain to complete and the pool to reach quiescence.</item>
    /// </list>
    /// Uses deterministic gate coordination — no arbitrary sleeps.
    /// </summary>
    [Fact]
    public async Task GracefulDrain_InFlightCompletes_NewAcquisitionsRejected()
    {
        using var gate = new SemaphoreSlim(0, 1);
        using var entered = new SemaphoreSlim(0, 1);

        var (app, client, pool) = await BuildDrainTestHostAsync(gate, entered);
        try
        {
            // Start blocking tool: acquires a worker, signals entered, waits on gate.
            var holdingTask = CallToolAsync(client, "drain_block_worker");

            Assert.True(await entered.WaitAsync(TimeSpan.FromSeconds(15)),
                "Blocking tool never signaled worker acquisition.");
            Assert.Equal(1, pool.GetStats().LeasedWorkers);

            // Drain: stops new acquisitions; cannot complete until the held lease is returned.
            var drainTask = pool.DrainAsync();
            Assert.False(drainTask.IsCompleted,
                "Drain should not complete while a lease is outstanding.");

            // New HTTP request must fail — pool is draining, AcquireAsync throws ObjectDisposedException.
            var newResult = await CallToolAsync(client, "drain_noop");
            _output.WriteLine($"New request during drain: {newResult}");
            var hasError = newResult["error"] != null ||
                           newResult["result"]?["isError"]?.Value<bool>() == true;
            Assert.True(hasError,
                $"Expected error when pool is draining; got: {newResult}");

            // Release blocking gate — in-flight tool returns its lease — drain unblocks.
            gate.Release();
            await holdingTask.WaitAsync(TimeSpan.FromSeconds(15));
            await drainTask.WaitAsync(TimeSpan.FromSeconds(15));

            var stats = pool.GetStats();
            Assert.Equal(0, stats.LeasedWorkers);
            Assert.Equal(0, stats.ResettingWorkers);
            _output.WriteLine(
                $"Drain complete: warm={stats.WarmWorkers} leased={stats.LeasedWorkers} total={stats.TotalWorkers}");
        }
        finally
        {
            // Ensure gate is released even on assertion failure so the blocking tool can exit.
            gate.Release();
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    // ─── Host builders ───────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a stress host with 8 workers (min=4, max=8) and stress tools.
    /// Used by concurrent tests 1 and 2.
    /// </summary>
    private async Task<(WebApplication App, HttpClient Client, StatelessRunspacePool Pool)>
        BuildStressHostAsync(bool stateless)
    {
        var opts = new RunspacePoolOptions
        {
            MinPoolSize = 4,
            MaxPoolSize = 8,
            EagerWarmCount = 4,
            AcquisitionTimeout = TimeSpan.FromSeconds(30),
            IdleTtl = TimeSpan.FromSeconds(300),
            SweepInterval = TimeSpan.FromSeconds(300),
            StopTimeout = TimeSpan.FromSeconds(5),
            ShutdownDrainTimeout = TimeSpan.FromSeconds(30),
            ReplenishCheckInterval = TimeSpan.FromSeconds(300),
        };

        var pool = new StatelessRunspacePool(opts, loggerFactory: null,
            startupScript: StressStartupScript);
        var pooledRunspace = new PooledHttpRunspace(pool, StressStartupScript,
            NullLoggerFactory.Instance);
        pooledRunspace.FinalizeDiscovery();

        var sessionLifecycle = new McpSessionLifecycle();
        var tools = CreateStressTools(pooledRunspace);

        return await BuildAndStartHostAsync(pool, pooledRunspace, sessionLifecycle, tools,
            stateless);
    }

    /// <summary>
    /// Builds a single-worker stateless host for the sequential all-fields test.
    /// MaxPoolSize=1 ensures all calls go through the same worker, making contaminate→reset→read
    /// assertions deterministic.
    /// </summary>
    private async Task<(WebApplication App, HttpClient Client)> BuildSingleWorkerHostAsync()
    {
        var opts = new RunspacePoolOptions
        {
            MinPoolSize = 1,
            MaxPoolSize = 1,
            EagerWarmCount = 1,
            AcquisitionTimeout = TimeSpan.FromSeconds(10),
            IdleTtl = TimeSpan.FromSeconds(300),
            SweepInterval = TimeSpan.FromSeconds(300),
            StopTimeout = TimeSpan.FromSeconds(5),
            ShutdownDrainTimeout = TimeSpan.FromSeconds(10),
            ReplenishCheckInterval = TimeSpan.FromSeconds(300),
        };

        var pool = new StatelessRunspacePool(opts, loggerFactory: null,
            startupScript: StressStartupScript);
        var pooledRunspace = new PooledHttpRunspace(pool, StressStartupScript,
            NullLoggerFactory.Instance);
        pooledRunspace.FinalizeDiscovery();

        var sessionLifecycle = new McpSessionLifecycle();
        var tools = CreateStressTools(pooledRunspace);

        var (app, client, _) = await BuildAndStartHostAsync(pool, pooledRunspace, sessionLifecycle,
            tools, stateless: true);
        return (app, client);
    }

    /// <summary>
    /// Builds a drain-test host with a blocking tool (gated by <paramref name="gate"/>) and a
    /// no-op tool used to verify acquisition rejection during drain.
    /// MinPoolSize=1, MaxPoolSize=2 so one lease can be held while the pool remains operational.
    /// </summary>
    private async Task<(WebApplication App, HttpClient Client, StatelessRunspacePool Pool)>
        BuildDrainTestHostAsync(SemaphoreSlim gate, SemaphoreSlim entered)
    {
        var opts = new RunspacePoolOptions
        {
            MinPoolSize = 1,
            MaxPoolSize = 2,
            EagerWarmCount = 1,
            AcquisitionTimeout = TimeSpan.FromSeconds(3),
            IdleTtl = TimeSpan.FromSeconds(300),
            SweepInterval = TimeSpan.FromSeconds(300),
            StopTimeout = TimeSpan.FromSeconds(5),
            ShutdownDrainTimeout = TimeSpan.FromSeconds(10),
            ReplenishCheckInterval = TimeSpan.FromSeconds(300),
        };

        var pool = new StatelessRunspacePool(opts, loggerFactory: null, startupScript: null);
        var pooledRunspace = new PooledHttpRunspace(pool, (string?)null, NullLoggerFactory.Instance);
        pooledRunspace.FinalizeDiscovery();

        var sessionLifecycle = new McpSessionLifecycle();
        var tools = CreateDrainTools(pooledRunspace, gate, entered);

        return await BuildAndStartHostAsync(pool, pooledRunspace, sessionLifecycle, tools,
            stateless: true);
    }

    private static async Task<(WebApplication App, HttpClient Client, StatelessRunspacePool Pool)>
        BuildAndStartHostAsync(
            StatelessRunspacePool pool,
            PooledHttpRunspace pooledRunspace,
            McpSessionLifecycle sessionLifecycle,
            McpServerTool[] tools,
            bool stateless)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddSingleton(sessionLifecycle);
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
                    o.RunSessionHandler = sessionLifecycle.RunSessionAsync;
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

    // ─── Tool factories ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the stress tools used by concurrent and sequential tests.
    /// All tools exercise the full production PS execution path through
    /// <see cref="PooledHttpRunspace.ExecuteThreadSafeAsync{T}"/>.
    /// </summary>
    private static McpServerTool[] CreateStressTools(IPowerShellRunspace runspace)
    {
        static Task<string> RunScript(IPowerShellRunspace rs, string script) =>
            rs.ExecuteThreadSafeAsync(ps =>
            {
                ps.Commands.Clear();
                ps.AddScript(script);
                var results = ps.Invoke<string>();
                ps.Commands.Clear();
                ps.Streams.ClearStreams();
                return Task.FromResult(results.Count > 0 ? results[0] ?? "null" : "null");
            });

        return
        [
            // Reads IsoStressVar (should always be "not-set" after reset), then sets it to Marker.
            // Returns the pre-contamination value — if it's anything other than "not-set", reset failed.
            McpServerTool.Create(
                (string marker, CancellationToken _) =>
                    runspace.ExecuteThreadSafeAsync(ps =>
                    {
                        ps.Commands.Clear();
                        ps.AddScript(
                            "param([string]$Marker) " +
                            "$found = if (Get-Variable IsoStressVar -ErrorAction SilentlyContinue) " +
                            "{ $IsoStressVar } else { 'not-set' }; " +
                            "$IsoStressVar = $Marker; " +
                            "$found");
                        ps.AddParameter("Marker", marker);
                        var results = ps.Invoke<string>();
                        ps.Commands.Clear();
                        ps.Streams.ClearStreams();
                        return Task.FromResult(results.Count > 0 ? results[0] ?? "null" : "null");
                    }),
                new McpServerToolCreateOptions
                {
                    Name = "stress_check_and_set",
                    Description = "Read IsoStressVar (should be not-set), then set it to Marker"
                }),

            // Contaminates all 7 resettable PS state fields: variable, $Error, preference,
            // PSDrive, function, alias.  The IsoStressVar contamination is implicitly part of the
            // same worker's state and will also be covered by the read-all tool.
            McpServerTool.Create(
                (string marker, CancellationToken _) =>
                    runspace.ExecuteThreadSafeAsync(ps =>
                    {
                        ps.Commands.Clear();
                        ps.AddScript(
                            "param([string]$Marker) " +
                            "$IsoStressVar = $Marker; " +
                            "Write-Error $Marker -ErrorAction SilentlyContinue; " +
                            "$ErrorActionPreference = 'Stop'; " +
                            "New-PSDrive -Name IsoStressDrive -PSProvider FileSystem " +
                            "  -Root ([System.IO.Path]::GetTempPath()) -ErrorAction SilentlyContinue; " +
                            "function IsoStressFunc { $Marker }; " +
                            "Set-Alias -Name IsoStressAlias -Value Get-Date; " +
                            "'ok'");
                        ps.AddParameter("Marker", marker);
                        var results = ps.Invoke<string>();
                        ps.Commands.Clear();
                        ps.Streams.ClearStreams();
                        return Task.FromResult(results.Count > 0 ? results[0] ?? "error" : "error");
                    }),
                new McpServerToolCreateOptions
                {
                    Name = "stress_contaminate_all",
                    Description = "Set all 7 resettable PS state fields to Marker value"
                }),

            // Reads all 7 fields and returns a combined key=value string.
            // Expected baseline: "var=not-set;err=0;pref=Continue;drive=False;func=not-defined;alias=not-set"
            McpServerTool.Create(
                (CancellationToken _) => RunScript(runspace, @"
$v = if (Get-Variable IsoStressVar -ErrorAction Ignore) { $IsoStressVar } else { 'not-set' }
$e = $Error.Count.ToString()
$p = $ErrorActionPreference
$d = ((Get-PSDrive IsoStressDrive -ErrorAction Ignore) -ne $null).ToString()
$f = if (Get-Command IsoStressFunc -ErrorAction Ignore) { 'exists' } else { 'not-defined' }
$a = if (Get-Alias IsoStressAlias -ErrorAction SilentlyContinue) { 'exists' } else { 'not-set' }
""var=$v;err=$e;pref=$p;drive=$d;func=$f;alias=$a"""),
                new McpServerToolCreateOptions
                {
                    Name = "stress_read_all_clean",
                    Description = "Read all 7 fields; must equal baseline after reset"
                }),
        ];
    }

    /// <summary>
    /// Creates the blocking and no-op tools used by the drain test.
    /// </summary>
    private static McpServerTool[] CreateDrainTools(
        IPowerShellRunspace runspace, SemaphoreSlim gate, SemaphoreSlim entered)
    {
        return
        [
            McpServerTool.Create(
                (CancellationToken _) =>
                {
                    var result = runspace.ExecuteThreadSafe(ps =>
                    {
                        ps.Commands.Clear();
                        ps.AddScript("'drain-blocking-acquired'");
                        var r = ps.Invoke<string>();
                        ps.Commands.Clear();
                        entered.Release();          // signal: worker is acquired
                        gate.Wait(TimeSpan.FromSeconds(30)); // hold until released
                        return r.Count > 0 ? r[0] : "null";
                    });
                    return Task.FromResult(result);
                },
                new McpServerToolCreateOptions
                {
                    Name = "drain_block_worker",
                    Description = "Holds pool worker until gate is released (drain test)"
                }),

            McpServerTool.Create(
                (CancellationToken _) =>
                    runspace.ExecuteThreadSafeAsync(ps =>
                    {
                        ps.Commands.Clear();
                        ps.AddScript("'drain-noop-ok'");
                        var r = ps.Invoke<string>();
                        ps.Commands.Clear();
                        ps.Streams.ClearStreams();
                        return Task.FromResult(r.Count > 0 ? r[0] ?? "null" : "null");
                    }),
                new McpServerToolCreateOptions
                {
                    Name = "drain_noop",
                    Description = "No-op tool; used to verify acquisition rejection during drain"
                }),
        ];
    }

    // ─── HTTP helpers ─────────────────────────────────────────────────────────────

    private static async Task<JObject> CallToolAsync(HttpClient client, string toolName,
        object? args = null)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString("N")[..8],
            method = "tools/call",
            @params = new { name = toolName, arguments = args ?? (object)new { } }
        };
        using var resp = await client.PostAsync("/",
            new StringContent(
                JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));
        var body = await resp.Content.ReadAsStringAsync();
        return ParseJsonOrSse(body, resp.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<JObject> CallToolWithSessionAsync(
        HttpClient client, string toolName, string sessionId, object? args = null)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString("N")[..8],
            method = "tools/call",
            @params = new { name = toolName, arguments = args ?? (object)new { } }
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Content = new StringContent(
                JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
        req.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2024-11-05");
        using var resp = await client.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        return ParseJsonOrSse(body, resp.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<string?> InitializeStatefulSessionAsync(HttpClient client)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { tools = new { } },
                clientInfo = new { name = "stress-test", version = "1.0.0" }
            }
        };
        using var resp = await client.PostAsync("/",
            new StringContent(
                JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));
        return resp.Headers.TryGetValues("Mcp-Session-Id", out var vals)
            ? vals.FirstOrDefault() : null;
    }

    private static string ExtractText(JObject result)
    {
        var content = result["result"]?["content"] as JArray;
        if (content is null || content.Count == 0) return string.Empty;
        return content[0]?["text"]?.ToString()?.Trim() ?? string.Empty;
    }

    private static JObject ParseJsonOrSse(string body, string? mediaType)
    {
        if (string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            return JObject.Parse(body);
        var dataLine = body.Split('\n')
            .FirstOrDefault(l => l.StartsWith("data: ", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(dataLine))
            throw new InvalidOperationException($"No MCP data line in SSE response: {body}");
        return JObject.Parse(dataLine[6..]);
    }
}
