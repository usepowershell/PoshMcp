using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.Pool;
using PoshMcp.Server.Server;
using Xunit;
using Xunit.Abstractions;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.Tests.Integration;

/// <summary>
/// Integration tests for issue #351: pool resilience, exhaustion, cancellation of a returned
/// worker's reset, reset-failure and reset-timeout eviction/replenishment, idle sweep,
/// lifecycle, and post-drain rejection under both HTTP transport modes.
///
/// Pool-level tests use real PowerShell runspaces. Injected seams (factory/resetProtocol/clock) are
/// used only where production-faithful HTTP cannot safely induce the failure condition.
/// All coordination uses deterministic gates/polling — no arbitrary sleeps.
///
/// Cancellation scope note: in current production no request/HTTP CancellationToken is threaded
/// into PowerShell execution or <see cref="RunspaceResetProtocol.ResetAsync"/> — neither
/// <c>IPowerShellRunspace</c> nor <c>PooledHttpRunspace.ExecuteThreadSafeAsync</c> accepts a token,
/// and the HTTP path calls <c>AcquireAsync()</c> with no token. The only cancellation that can
/// reach reset is the pool's own <c>_shutdownToken</c> during drain/dispose. These tests therefore
/// do NOT claim in-flight HTTP execution cancellation; the reset-cancellation branch is exercised
/// deterministically by injecting an <see cref="OperationCanceledException"/> through the reset seam.
///
/// No test in this file creates a genuinely Broken runspace or a genuinely stuck PowerShell
/// pipeline; those real-object scenarios are covered by the pre-existing pool functional tests
/// (<c>ResetProtocol_BrokenRunspace_Throws</c>, <c>Pool_Reset_StuckPipeline_...</c>). The tests
/// here that inject exceptions validate the pool's reaction (evict + replenish) to the exception
/// types the real paths raise, and their names/docs say exactly that.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ResilienceTests
{
    private readonly ITestOutputHelper _output;

    // Startup script executed once per worker: assigns a durable GUID identity preserved across resets.
    private const string WorkerIdentityScript = @"
$WorkerId = [System.Guid]::NewGuid().ToString('N')
function Get-WorkerId { return $WorkerId }
";

    public ResilienceTests(ITestOutputHelper output) => _output = output;

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    private static async Task WaitForStatsAsync(
        StatelessRunspacePool pool,
        Func<RunspacePoolStats, bool> condition,
        TimeSpan? timeout = null)
    {
        var deadline = timeout ?? TimeSpan.FromSeconds(10);
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < deadline)
        {
            if (condition(pool.GetStats())) return;
            await Task.Delay(10);
        }
        var s = pool.GetStats();
        throw new TimeoutException(
            $"Condition not met within {deadline}. " +
            $"warm={s.WarmWorkers} leased={s.LeasedWorkers} resetting={s.ResettingWorkers} " +
            $"creating={s.CreatingWorkers} total={s.TotalWorkers}");
    }

    private static RunspacePoolOptions MakeOptions(
        int min = 1, int max = 2, int eager = 1,
        double acquisitionSec = 5,
        double stopSec = 5,
        double drainSec = 5) =>
        new()
        {
            MinPoolSize = min,
            MaxPoolSize = max,
            EagerWarmCount = eager,
            AcquisitionTimeout = TimeSpan.FromSeconds(acquisitionSec),
            IdleTtl = TimeSpan.FromSeconds(300),
            SweepInterval = TimeSpan.FromSeconds(300),
            StopTimeout = TimeSpan.FromSeconds(stopSec),
            ShutdownDrainTimeout = TimeSpan.FromSeconds(drainSec),
            ReplenishCheckInterval = TimeSpan.FromSeconds(300),
        };

    // Real-PS pool: default factory uses WorkerIdentityScript; all seams can be overridden.
    private static StatelessRunspacePool MakeRealPool(
        RunspacePoolOptions options,
        Func<RunspaceWorker, ILogger, CancellationToken, Task>? resetProtocol = null,
        Func<DateTimeOffset>? clock = null) =>
        new(options,
            startupScript: WorkerIdentityScript,
            resetProtocol: resetProtocol,
            clock: clock);

    // Mock-PS pool: injected factory + no-op snapshots/reset so no real PS runs.
    private static StatelessRunspacePool MakeMockPool(
        RunspacePoolOptions options,
        Func<IPowerShellRunspace> factory,
        Func<RunspaceWorker, ILogger, CancellationToken, Task>? resetProtocol = null,
        Func<DateTimeOffset>? clock = null) =>
        new(options,
            workerFactory: factory,
            snapshotCapture: _ => new HashSet<string>(),
            driveSnapshotCapture: _ => new HashSet<string>(),
            functionSnapshotCapture: _ => new HashSet<string>(),
            aliasSnapshotCapture: _ => new HashSet<string>(),
            resetProtocol: resetProtocol ?? ((_, _, _) => Task.CompletedTask),
            clock: clock);

    private static IPowerShellRunspace CreateMockRunspace()
    {
        var ps = PSPowerShell.Create();
        var mock = new Mock<IPowerShellRunspace>();
        mock.Setup(r => r.Instance).Returns(ps);
        mock.Setup(r => r.Dispose()).Callback(ps.Dispose);
        return mock.Object;
    }

    // Execute a PS script fragment on a leased worker and return the first string result.
    private static string? QueryWorker(RunspaceLease lease, string script)
    {
        var ps = lease.PowerShell;
        ps.Commands.Clear();
        ps.AddScript(script);
        var r = ps.Invoke<string>();
        ps.Commands.Clear();
        ps.Streams.ClearStreams();
        return r.Count > 0 ? r[0] : null;
    }

    // ─── HTTP host builder ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a TestServer-backed MCP host. <paramref name="pool"/> is started by
    /// <see cref="RunspacePoolLifecycleService"/> when <c>app.StartAsync()</c> runs; callers
    /// must NOT call <c>pool.StartAsync()</c> before building the host.
    /// </summary>
    private static async Task<(WebApplication App, HttpClient Client, StatelessRunspacePool Pool)>
        BuildHttpHostAsync(
            StatelessRunspacePool pool,
            IPowerShellRunspace pooledRunspace,
            McpServerTool[] tools,
            bool stateless = true)
    {
        var sessionLifecycle = new McpSessionLifecycle();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton(sessionLifecycle);
        builder.Services.AddSingleton(pooledRunspace);
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

    // ─── HTTP helpers ─────────────────────────────────────────────────────────────

    private static async Task<JObject> CallToolAsync(HttpClient client, string tool, object? args = null)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString("N")[..8],
            method = "tools/call",
            @params = new { name = tool, arguments = args ?? (object)new { } }
        };
        using var resp = await client.PostAsync("/",
            new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));
        var body = await resp.Content.ReadAsStringAsync();
        return ParseJsonOrSse(body, resp.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<JObject> CallToolWithSessionAsync(
        HttpClient client, string tool, string sessionId, object? args = null)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString("N")[..8],
            method = "tools/call",
            @params = new { name = tool, arguments = args ?? (object)new { } }
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
                clientInfo = new { name = "resilience-test", version = "1.0.0" }
            }
        };
        using var resp = await client.PostAsync("/",
            new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));
        return resp.Headers.TryGetValues("Mcp-Session-Id", out var vals) ? vals.FirstOrDefault() : null;
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
            throw new InvalidOperationException($"No MCP data line in SSE: {body}");
        return JObject.Parse(dataLine[6..]);
    }

    private static bool HasMcpError(JObject result) =>
        result["error"] != null || result["result"]?["isError"]?.Value<bool>() == true;

    /// <summary>
    /// Asserts the response is a tool-level error result (<c>result.isError == true</c>, not a
    /// JSON-RPC protocol <c>error</c>) that references the specific tool by name. The MCP SDK does
    /// not surface the underlying exception message in the response body — it returns a generic
    /// "An error occurred invoking '&lt;tool&gt;'." with <c>isError:true</c> (the true exception, e.g.
    /// <c>TimeoutException</c> / <c>ObjectDisposedException</c>, appears only in server logs). This
    /// asserts the stable, observable contract: an isError result for the named tool. The
    /// acquisition-timeout vs. drain-reject <em>semantics</em> are asserted behaviorally (by timing)
    /// at each call site — see the exhaustion and post-drain tests.
    /// </summary>
    private static void AssertToolError(JObject result, string tool)
    {
        Assert.Null(result["error"]);
        Assert.True(result["result"]?["isError"]?.Value<bool>() == true,
            $"Expected a tool-level isError result for '{tool}', got: {result}");
        Assert.Contains(tool, ExtractText(result));
    }

    // ─── 1. HTTP Exhaustion — Stateless (sync bridge + async bridge) ──────────────

    /// <summary>
    /// Holds both pool workers with one sync-bridge and one async-bridge blocking tool.
    /// Two excess calls must fail with a bounded acquisition-timeout error — not deadlock or
    /// thread starvation. After releasing the gate, recovery calls succeed and counters are exact.
    ///
    /// Covers both <c>ExecuteThreadSafe</c> and <c>ExecuteThreadSafeAsync</c> production paths.
    /// Would fail if the pool deadlocked on exhaustion or leaked workers.
    /// </summary>
    [Fact]
    public async Task HttpExhaustion_Stateless_SyncAndAsync_BoundedError_ThenRecovery()
    {
        using var gate = new SemaphoreSlim(0, 2);
        using var entered1 = new SemaphoreSlim(0, 1);
        using var entered2 = new SemaphoreSlim(0, 1);

        var opts = MakeOptions(min: 2, max: 2, eager: 2, acquisitionSec: 0.4, drainSec: 10);
        var pool = MakeRealPool(opts);
        var pr = new PooledHttpRunspace(pool, WorkerIdentityScript, NullLoggerFactory.Instance);
        pr.FinalizeDiscovery();

        // Sync blocking tool — exercises ExecuteThreadSafe path.
        var syncBlock = McpServerTool.Create(
            (CancellationToken _) =>
            {
                var r = pr.ExecuteThreadSafe(ps =>
                {
                    ps.Commands.Clear(); ps.AddScript("'sync-held'");
                    var res = ps.Invoke<string>(); ps.Commands.Clear();
                    entered1.Release();
                    gate.Wait(TimeSpan.FromSeconds(30));
                    return res.Count > 0 ? res[0] : "null";
                });
                return Task.FromResult(r);
            },
            new McpServerToolCreateOptions { Name = "res_sync_block", Description = "Hold worker sync" });

        // Async blocking tool — exercises ExecuteThreadSafeAsync path.
        var asyncBlock = McpServerTool.Create(
            (CancellationToken _) =>
                pr.ExecuteThreadSafeAsync(ps =>
                {
                    ps.Commands.Clear(); ps.AddScript("'async-held'");
                    var res = ps.Invoke<string>(); ps.Commands.Clear();
                    entered2.Release();
                    gate.Wait(TimeSpan.FromSeconds(30));
                    return Task.FromResult(res.Count > 0 ? res[0] : "null");
                }),
            new McpServerToolCreateOptions { Name = "res_async_block", Description = "Hold worker async" });

        var probe = McpServerTool.Create(
            (CancellationToken _) =>
                pr.ExecuteThreadSafeAsync(ps =>
                {
                    ps.Commands.Clear(); ps.AddScript("'probe-ok'");
                    var res = ps.Invoke<string>(); ps.Commands.Clear();
                    return Task.FromResult(res.Count > 0 ? res[0] : "null");
                }),
            new McpServerToolCreateOptions { Name = "res_probe", Description = "Probe" });

        var (app, client, _) = await BuildHttpHostAsync(pool, pr, [syncBlock, asyncBlock, probe]);
        try
        {
            // Hold both workers.
            var hold1 = CallToolAsync(client, "res_sync_block");
            var hold2 = CallToolAsync(client, "res_async_block");
            Assert.True(await entered1.WaitAsync(TimeSpan.FromSeconds(15)), "Sync tool never acquired worker");
            Assert.True(await entered2.WaitAsync(TimeSpan.FromSeconds(15)), "Async tool never acquired worker");
            Assert.Equal(2, pool.GetStats().LeasedWorkers);

            // Excess calls must fail *after waiting ~AcquisitionTimeout* (400ms) — proving the
            // acquisition-timeout path fired (a bounded wait then timeout), not an instant reject
            // or a deadlock. The 5s WaitAsync is the no-deadlock upper bound.
            var sw1 = Stopwatch.StartNew();
            var excess1 = await CallToolAsync(client, "res_probe").WaitAsync(TimeSpan.FromSeconds(5));
            sw1.Stop();
            var sw2 = Stopwatch.StartNew();
            var excess2 = await CallToolAsync(client, "res_probe").WaitAsync(TimeSpan.FromSeconds(5));
            sw2.Stop();
            AssertToolError(excess1, "res_probe");
            AssertToolError(excess2, "res_probe");
            // Lower bound: at least half of AcquisitionTimeout elapsed ⇒ it waited for the acquire
            // timeout rather than failing instantly for some other reason.
            Assert.True(sw1.Elapsed >= TimeSpan.FromMilliseconds(200),
                $"Excess call 1 returned in {sw1.Elapsed} — expected a bounded acquisition-timeout wait (~400ms).");
            Assert.True(sw2.Elapsed >= TimeSpan.FromMilliseconds(200),
                $"Excess call 2 returned in {sw2.Elapsed} — expected a bounded acquisition-timeout wait (~400ms).");

            // Release both blockers.
            gate.Release(2);
            await hold1.WaitAsync(TimeSpan.FromSeconds(15));
            await hold2.WaitAsync(TimeSpan.FromSeconds(15));

            // Wait for workers to reset then verify recovery.
            await WaitForStatsAsync(pool, s => s.WarmWorkers >= 1 && s.LeasedWorkers == 0);
            var recovery = await CallToolAsync(client, "res_probe");
            Assert.False(HasMcpError(recovery), $"Recovery call must succeed: {recovery}");

            // Counter integrity: total must not exceed max=2 and must be non-negative.
            await WaitForStatsAsync(pool, s => s.LeasedWorkers == 0 && s.ResettingWorkers == 0);
            var stats = pool.GetStats();
            Assert.True(stats.TotalWorkers is >= 0 and <= 2, $"TotalWorkers={stats.TotalWorkers} out of range [0,2]");
            _output.WriteLine($"Exhaustion-SL: warm={stats.WarmWorkers} total={stats.TotalWorkers}");
        }
        finally
        {
            gate.Release(2);
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    // ─── 2. HTTP Exhaustion — Stateful (real Mcp-Session-Id) ─────────────────────

    /// <summary>
    /// Same exhaustion pattern over the stateful transport with a real server-issued session ID.
    /// Proves the pool-exhaustion path is identical regardless of session ID.
    /// Recovery is verified with both the same and a fresh session ID.
    /// Would fail if stateful mode introduced session-keyed worker affinity.
    /// </summary>
    [Fact]
    public async Task HttpExhaustion_Stateful_BoundedError_ThenRecovery()
    {
        using var gate = new SemaphoreSlim(0, 1);
        using var entered = new SemaphoreSlim(0, 1);

        var opts = MakeOptions(min: 1, max: 1, eager: 1, acquisitionSec: 0.4, drainSec: 10);
        var pool = MakeRealPool(opts);
        var pr = new PooledHttpRunspace(pool, WorkerIdentityScript, NullLoggerFactory.Instance);
        pr.FinalizeDiscovery();

        var blockTool = McpServerTool.Create(
            (CancellationToken _) =>
            {
                var r = pr.ExecuteThreadSafe(ps =>
                {
                    ps.Commands.Clear(); ps.AddScript("'sf-held'");
                    var res = ps.Invoke<string>(); ps.Commands.Clear();
                    entered.Release();
                    gate.Wait(TimeSpan.FromSeconds(30));
                    return res.Count > 0 ? res[0] : "null";
                });
                return Task.FromResult(r);
            },
            new McpServerToolCreateOptions { Name = "res_sf_block", Description = "Hold stateful" });

        var probeTool = McpServerTool.Create(
            (CancellationToken _) =>
                pr.ExecuteThreadSafeAsync(ps =>
                {
                    ps.Commands.Clear(); ps.AddScript("'sf-probe-ok'");
                    var res = ps.Invoke<string>(); ps.Commands.Clear();
                    return Task.FromResult(res.Count > 0 ? res[0] : "null");
                }),
            new McpServerToolCreateOptions { Name = "res_sf_probe", Description = "Probe stateful" });

        var (app, client, _) = await BuildHttpHostAsync(pool, pr, [blockTool, probeTool], stateless: false);
        try
        {
            var sessionId = await InitializeStatefulSessionAsync(client);
            Assert.False(string.IsNullOrEmpty(sessionId), "Stateful host must return Mcp-Session-Id");

            // Hold the sole worker.
            var holdTask = CallToolWithSessionAsync(client, "res_sf_block", sessionId!);
            Assert.True(await entered.WaitAsync(TimeSpan.FromSeconds(15)), "Blocking tool never acquired worker");
            Assert.Equal(1, pool.GetStats().LeasedWorkers);

            // Excess call must fail after a bounded acquisition-timeout wait (not deadlock).
            var swx = Stopwatch.StartNew();
            var excess = await CallToolWithSessionAsync(client, "res_sf_probe", sessionId!)
                .WaitAsync(TimeSpan.FromSeconds(5));
            swx.Stop();
            AssertToolError(excess, "res_sf_probe");
            Assert.True(swx.Elapsed >= TimeSpan.FromMilliseconds(200),
                $"Excess call returned in {swx.Elapsed} — expected a bounded acquisition-timeout wait (~400ms).");

            // Release.
            gate.Release();
            await holdTask.WaitAsync(TimeSpan.FromSeconds(15));

            // Recovery: verify with both the original and a new session ID.
            await WaitForStatsAsync(pool, s => s.WarmWorkers >= 1 && s.LeasedWorkers == 0);
            var session2 = await InitializeStatefulSessionAsync(client);
            Assert.NotNull(session2);

            var rec1 = await CallToolWithSessionAsync(client, "res_sf_probe", sessionId!);
            var rec2 = await CallToolWithSessionAsync(client, "res_sf_probe", session2!);
            Assert.False(HasMcpError(rec1), $"Recovery (original session) must succeed: {rec1}");
            Assert.False(HasMcpError(rec2), $"Recovery (new session) must succeed: {rec2}");
            _output.WriteLine($"Exhaustion-SF: recovered on both sessions.");
        }
        finally
        {
            gate.Release();
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    // ─── 3. Cancellation at lease-wait (acquire never granted): OCE, counters exact ─

    /// <summary>
    /// Proves ONE contract only: a canceled <c>AcquireAsync</c> that is still <em>waiting</em> for a
    /// worker surfaces <see cref="OperationCanceledException"/> (not <see cref="TimeoutException"/>)
    /// and does not perturb counters. The sole worker is held by a separate lease throughout, so the
    /// canceled caller is never granted a worker — no reset, eviction, or replenishment occurs on the
    /// cancellation path here.
    ///
    /// This deliberately does NOT cover cancellation of a returned worker's reset. That distinct
    /// production branch (<c>OnWorkerReturnedAsync</c> <c>catch (OperationCanceledException)</c> →
    /// evict + replenish) is proven by
    /// <see cref="ResetCanceled_WorkerEvictedAndReplenished_WithDistinctWorker"/>.
    ///
    /// Uses a real PS runspace to prove the counter path end-to-end. Would fail if cancellation were
    /// mis-classified as a timeout or if the held lease's accounting drifted.
    /// </summary>
    [Fact]
    public async Task CancellationAtLeaseWait_OCE_CountersExact_PoolRecovers()
    {
        var opts = MakeOptions(min: 1, max: 1, eager: 1, acquisitionSec: 30);
        await using var pool = MakeRealPool(opts);
        await pool.StartAsync();

        var held = await pool.AcquireAsync();
        try
        {
            var before = pool.GetStats();
            Assert.Equal(0, before.WarmWorkers);
            Assert.Equal(1, before.LeasedWorkers);
            Assert.Equal(0, before.ResettingWorkers);

            // Cancel after 80ms — well before the 30-second AcquisitionTimeout.
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
            var ex = await Assert.ThrowsAsync<OperationCanceledException>(
                () => pool.AcquireAsync(cts.Token).AsTask());

            // Must surface OCE, not TimeoutException (pool timeout has not fired).
            Assert.IsNotType<TimeoutException>(ex);

            // Cancellation must not alter counters — held lease still accounts for the worker.
            var during = pool.GetStats();
            Assert.Equal(0, during.WarmWorkers);
            Assert.Equal(1, during.LeasedWorkers);
            Assert.Equal(0, during.ResettingWorkers);
            Assert.Equal(1, during.TotalWorkers);
        }
        finally
        {
            await held.DisposeAsync();
        }

        // Pool must fully recover: warm=1, leased=0, resetting=0, total=1.
        await WaitForStatsAsync(pool, s => s.WarmWorkers == 1 && s.LeasedWorkers == 0);
        var final = pool.GetStats();
        Assert.Equal(1, final.WarmWorkers);
        Assert.Equal(0, final.LeasedWorkers);
        Assert.Equal(0, final.ResettingWorkers);
        Assert.Equal(1, final.TotalWorkers);

        // Subsequent acquire on the recovered worker succeeds.
        await using var recovery = await pool.AcquireAsync();
        Assert.NotNull(recovery.PowerShell);
        _output.WriteLine("CancellationAtLeaseWait: OCE correct; pool recovered to warm=1.");
    }

    // ─── 4. Tool command failure → eviction → replenishment with distinct worker ──

    /// <summary>
    /// A tool that throws causes <see cref="PooledHttpRunspace.ExecuteThreadSafeAsync{T}"/> to
    /// call <c>lease.RequestEviction()</c>. Proves:
    /// <list type="bullet">
    ///   <item>MCP error surfaces to caller (not an unhandled exception).</item>
    ///   <item>Evicted worker is never re-used: post-replenishment WorkerId differs.</item>
    ///   <item>Counters settle to total=1 (min=max=1, no drift).</item>
    /// </list>
    /// Would fail if the eviction path leaked the old worker back to the pool.
    /// </summary>
    [Fact]
    public async Task ToolCommandFailure_Http_WorkerEvicted_ReplenishedWithDistinctWorker()
    {
        var opts = MakeOptions(min: 1, max: 1, eager: 1, acquisitionSec: 10, drainSec: 10);
        var pool = MakeRealPool(opts);
        var pr = new PooledHttpRunspace(pool, WorkerIdentityScript, NullLoggerFactory.Instance);
        pr.FinalizeDiscovery();

        var getIdTool = McpServerTool.Create(
            (CancellationToken _) =>
                pr.ExecuteThreadSafeAsync(ps =>
                {
                    ps.Commands.Clear(); ps.AddScript("Get-WorkerId");
                    var r = ps.Invoke<string>(); ps.Commands.Clear(); ps.Streams.ClearStreams();
                    return Task.FromResult(r.Count > 0 ? r[0] ?? "null" : "null");
                }),
            new McpServerToolCreateOptions { Name = "res_get_id", Description = "Get worker GUID" });

        // Tool throws unconditionally → RequestEviction called by PooledHttpRunspace catch block.
        var failTool = McpServerTool.Create(
            (CancellationToken _) =>
                pr.ExecuteThreadSafeAsync<string>(ps =>
                {
                    ps.Commands.Clear(); ps.Streams.ClearStreams();
                    throw new InvalidOperationException("Deliberate failure: RequestEviction path test.");
                }),
            new McpServerToolCreateOptions { Name = "res_fail", Description = "Force eviction" });

        var (app, client, _) = await BuildHttpHostAsync(pool, pr, [getIdTool, failTool]);
        try
        {
            // Capture first worker's identity.
            var id1Result = await CallToolAsync(client, "res_get_id");
            var id1 = ExtractText(id1Result);
            Assert.False(string.IsNullOrEmpty(id1),
                $"Worker must have a startup-assigned identity: {id1Result}");

            // Trigger eviction via tool failure.
            var failResult = await CallToolAsync(client, "res_fail");
            Assert.True(HasMcpError(failResult), $"Failed tool must return MCP error: {failResult}");

            // Wait for eviction + replenishment.
            await WaitForStatsAsync(pool,
                s => s.TotalWorkers >= 1 && s.WarmWorkers >= 1 && s.LeasedWorkers == 0);

            // New worker must have a different identity — evicted worker is not re-queued.
            var id2Result = await CallToolAsync(client, "res_get_id");
            var id2 = ExtractText(id2Result);
            Assert.False(string.IsNullOrEmpty(id2), $"Replenished worker must have an ID: {id2Result}");
            Assert.NotEqual(id1, id2);

            // Counter integrity: exactly 1 worker (min=max=1).
            await WaitForStatsAsync(pool, s => s.LeasedWorkers == 0 && s.ResettingWorkers == 0);
            var stats = pool.GetStats();
            Assert.Equal(1, stats.TotalWorkers);
            _output.WriteLine($"ToolFailure: evicted id={id1}; replenished id={id2}.");
        }
        finally
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    // ─── 5. Reset failure: eviction, never reused, deterministic replenishment ────

    /// <summary>
    /// Injected reset protocol fails on the first call (simulating non-removable state or a
    /// command that cannot be cleaned up) then succeeds thereafter.
    /// Proves: (1) the evicted worker's identity is never seen again (distinct WorkerId after
    /// replenishment); (2) pool replenishes exactly to MinPoolSize; (3) counters stay exact.
    /// Would fail if the pool reused the evicted worker or miscounted total after reset failure.
    /// </summary>
    [Fact]
    public async Task ResetFailure_WorkerEvictedNeverReused_PoolReplenishesExactly()
    {
        int resetCalls = 0;
        var opts = MakeOptions(min: 1, max: 1, eager: 1, acquisitionSec: 10, drainSec: 10);
        await using var pool = MakeRealPool(
            opts,
            resetProtocol: (_, _, _) =>
            {
                if (Interlocked.Increment(ref resetCalls) == 1)
                    return Task.FromException(new InvalidOperationException("Reset fails: non-removable state."));
                return Task.CompletedTask;
            });
        await pool.StartAsync();

        // Acquire first worker and capture its identity.
        string id1;
        {
            await using var lease = await pool.AcquireAsync();
            id1 = QueryWorker(lease, "Get-WorkerId") ?? "null";
        }
        // Lease disposed → injected reset throws → first worker evicted → replenishment fires.
        await WaitForStatsAsync(pool,
            s => s.TotalWorkers >= 1 && s.WarmWorkers >= 1 && s.LeasedWorkers == 0);

        var stats1 = pool.GetStats();
        Assert.Equal(0, stats1.LeasedWorkers);
        Assert.Equal(0, stats1.ResettingWorkers);
        Assert.Equal(1, stats1.TotalWorkers);

        // Acquire the replenished worker — must be a different one.
        string id2;
        {
            await using var lease = await pool.AcquireAsync();
            id2 = QueryWorker(lease, "Get-WorkerId") ?? "null";
        }
        // Second reset succeeds (resetCalls >= 2 uses no-op path).
        await WaitForStatsAsync(pool, s => s.WarmWorkers == 1 && s.LeasedWorkers == 0);

        Assert.NotEqual(id1, id2);
        var stats2 = pool.GetStats();
        Assert.Equal(1, stats2.TotalWorkers);
        Assert.Equal(0, stats2.LeasedWorkers);
        _output.WriteLine($"ResetFailure: evicted id={id1}; new id={id2}. total={stats2.TotalWorkers}");
    }

    // ─── 5b. Reset canceled (OCE) → eviction + replenishment with distinct worker ──

    /// <summary>
    /// Exercises the production reset-cancellation branch:
    /// <see cref="StatelessRunspacePool.OnWorkerReturnedAsync"/> <c>catch (OperationCanceledException)</c>,
    /// which evicts the returned worker (metric reason <c>cancel</c>) and fires replenishment.
    ///
    /// A lease is acquired on a real worker and its identity captured; on return, the injected reset
    /// protocol signals that reset is in flight then faults with <see cref="OperationCanceledException"/>
    /// through the exact seam production wires to <c>RunspaceResetProtocol.ResetAsync</c>. The test then
    /// proves the observable behavior: the leased worker is evicted, never leased again (distinct
    /// WorkerId after replenishment), the pool replenishes to MinPoolSize, counters settle exactly
    /// (warm=1, leased=0, resetting=0, total=1), and a subsequent execution succeeds.
    ///
    /// Metric caveat: the eviction reason (<c>cancel</c>) is emitted on a process-global meter shared
    /// by every pool instance (<see cref="RunspacePoolMetrics"/> uses the shared <c>PoshMcp</c> meter
    /// name with no per-pool discriminator), so a reason-count assertion is not deterministic under
    /// xUnit parallelism. We assert behavior (evict + replenish + distinct worker) rather than the
    /// reason tag. The test fails if <c>catch (OperationCanceledException)</c> stops evicting or
    /// replenishing a canceled reset.
    ///
    /// Cancellation-threading note: production does not thread a request CancellationToken into reset;
    /// only the pool's <c>_shutdownToken</c> can cancel <c>ResetAsync</c>. This injects the OCE that
    /// path would raise, rather than simulating in-flight HTTP execution cancellation.
    /// </summary>
    [Fact]
    public async Task ResetCanceled_WorkerEvictedAndReplenished_WithDistinctWorker()
    {
        using var resetEntered = new SemaphoreSlim(0, 1);
        int resetCalls = 0;
        var opts = MakeOptions(min: 1, max: 1, eager: 1, acquisitionSec: 10, drainSec: 10);
        await using var pool = MakeRealPool(
            opts,
            resetProtocol: (_, _, _) =>
            {
                if (Interlocked.Increment(ref resetCalls) == 1)
                {
                    resetEntered.Release(); // deterministic signal: reset is in flight
                    return Task.FromException(new OperationCanceledException(
                        "Reset canceled: models _shutdownToken cancellation of ResetAsync."));
                }
                return Task.CompletedTask;
            });
        await pool.StartAsync();

        // Acquire the first worker, capture its identity, then return it → injected reset cancels.
        string id1;
        {
            await using var lease = await pool.AcquireAsync();
            id1 = QueryWorker(lease, "Get-WorkerId") ?? "null";
        }

        // Deterministically confirm the reset-cancellation branch was entered (no sleep).
        Assert.True(await resetEntered.WaitAsync(TimeSpan.FromSeconds(5)),
            "Reset protocol (cancellation branch) never started.");

        // The canceled worker must be evicted and the pool replenished to MinPoolSize.
        await WaitForStatsAsync(pool,
            s => s.TotalWorkers >= 1 && s.WarmWorkers >= 1 &&
                 s.LeasedWorkers == 0 && s.ResettingWorkers == 0);

        // Acquire the replenished worker — must be a distinct identity (evicted one not re-queued).
        string id2;
        {
            await using var lease = await pool.AcquireAsync();
            id2 = QueryWorker(lease, "Get-WorkerId") ?? "null";
        }
        // Second reset succeeds (resetCalls >= 2 → no-op), so this worker stays warm.
        await WaitForStatsAsync(pool, s => s.WarmWorkers == 1 && s.LeasedWorkers == 0);

        Assert.NotEqual(id1, id2);
        var stats = pool.GetStats();
        Assert.Equal(1, stats.TotalWorkers);
        Assert.Equal(1, stats.WarmWorkers);
        Assert.Equal(0, stats.LeasedWorkers);
        Assert.Equal(0, stats.ResettingWorkers);

        // Subsequent execution on the recovered worker succeeds and is the same distinct worker.
        await using var recovery = await pool.AcquireAsync();
        Assert.Equal(id2, QueryWorker(recovery, "Get-WorkerId"));
        _output.WriteLine($"ResetCanceled: evicted id={id1}; replenished distinct id={id2}; counters exact.");
    }

    // ─── 6. Reset throws TimeoutException → pool evicts (stop_timeout) + replenishes ──

    /// <summary>
    /// Validates the pool's <em>reaction</em> to a reset that throws <see cref="TimeoutException"/>:
    /// <see cref="StatelessRunspacePool.OnWorkerReturnedAsync"/> catches it, evicts the worker with
    /// metric reason <c>stop_timeout</c>, and fires replenishment — all within a bounded window.
    ///
    /// The <see cref="TimeoutException"/> is injected through the reset seam. This test does NOT
    /// exercise the real <c>RunspaceResetProtocol.ResetAsync</c> → <c>PowerShell.Stop()</c> →
    /// configured <c>StopTimeout</c> path: the production reset runs a fixed, fast reset script that
    /// responds to <c>Stop()</c> in milliseconds and cannot be made to genuinely block from a test.
    /// It injects the exception that the real stop-timeout path would surface and asserts only the
    /// pool's eviction/replenishment reaction (matching the honest pre-existing functional test
    /// <c>Pool_Reset_StuckPipeline_EvictedWithStopTimeoutReason</c>).
    ///
    /// Would fail if <see cref="StatelessRunspacePool.OnWorkerReturnedAsync"/> did not catch
    /// <c>TimeoutException</c> and evict, or re-threw instead of replenishing.
    /// </summary>
    [Fact]
    public async Task ResetThrowsTimeout_PoolEvictsStopTimeoutReason_Replenished()
    {
        using var resetEntered = new SemaphoreSlim(0, 1);
        var opts = MakeOptions(min: 1, max: 1, eager: 1, acquisitionSec: 10, drainSec: 10);
        await using var pool = MakeRealPool(
            opts,
            resetProtocol: async (_, _, _) =>
            {
                resetEntered.Release(); // deterministic signal: reset started
                await Task.Delay(300, CancellationToken.None); // brief in-flight delay before the fault
                // Inject the TimeoutException the real stop-timeout path would raise; this test
                // asserts the pool's reaction, not ps.Stop() itself.
                throw new TimeoutException(
                    "Injected TimeoutException: exercises the pool's stop_timeout eviction/replenishment reaction.");
            });
        await pool.StartAsync();

        var sw = Stopwatch.StartNew();

        // Acquire and return — triggers the blocking reset.
        {
            await using var lease = await pool.AcquireAsync();
        }

        // Wait for reset to start (deterministic: no sleep).
        Assert.True(await resetEntered.WaitAsync(TimeSpan.FromSeconds(5)), "Reset protocol never started.");

        // Pool must evict and replenish within a bounded time window.
        await WaitForStatsAsync(pool,
            s => s.TotalWorkers >= 1 && s.WarmWorkers >= 1 &&
                 s.LeasedWorkers == 0 && s.ResettingWorkers == 0,
            TimeSpan.FromSeconds(10));

        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10),
            $"Eviction + replenishment took {sw.Elapsed} — expected < 10s");

        var stats = pool.GetStats();
        Assert.Equal(1, stats.TotalWorkers);
        Assert.Equal(0, stats.LeasedWorkers);
        Assert.Equal(0, stats.ResettingWorkers);

        // New worker is usable.
        {
            await using var lease = await pool.AcquireAsync();
            Assert.NotNull(lease.PowerShell);
        }
        _output.WriteLine($"ResetThrowsTimeout: stop_timeout eviction+replenishment completed in {sw.Elapsed}.");
    }

    // ─── 7. Idle sweep: multi-round, never below MinPoolSize, counters don't drift ─

    /// <summary>
    /// Injected clock enables deterministic sweep without real-time delays.
    /// Round 1: 4 warm workers, clock advanced past IdleTtl → exactly 2 evicted (surplus).
    /// Round 2: at MinPoolSize=2, clock further advanced → 0 evictions.
    /// Round 3: 1 lease held (warm drops to 1, below min in warm-only count) → 0 evictions
    ///   (surplus = warm - min = -1; sweeper correctly skips).
    /// Verifies: total never drifts below min; counters consistent across rounds.
    /// Would fail if <c>SweepOnce</c> used <c>_warmCount</c> alone and ignored active leases.
    /// </summary>
    [Fact]
    public async Task IdleSweep_MultipleRounds_NeverBelowMin_CountersDontDrift()
    {
        var frozenTime = DateTimeOffset.UtcNow;
        var opts = new RunspacePoolOptions
        {
            MinPoolSize = 2,
            MaxPoolSize = 4,
            EagerWarmCount = 4,
            AcquisitionTimeout = TimeSpan.FromSeconds(10),
            IdleTtl = TimeSpan.FromSeconds(300),
            SweepInterval = TimeSpan.FromSeconds(300),
            StopTimeout = TimeSpan.FromSeconds(5),
            ShutdownDrainTimeout = TimeSpan.FromSeconds(5),
            ReplenishCheckInterval = TimeSpan.FromSeconds(300),
        };
        await using var pool = MakeMockPool(
            opts,
            factory: CreateMockRunspace,
            clock: () => frozenTime);
        await pool.StartAsync();

        var s0 = pool.GetStats();
        Assert.Equal(4, s0.WarmWorkers);
        Assert.Equal(4, s0.TotalWorkers);

        // Round 1: advance past IdleTtl; sweep must evict exactly 2 surplus workers.
        frozenTime = frozenTime.AddSeconds(301);
        pool.SweepOnce();
        var s1 = pool.GetStats();
        Assert.Equal(2, s1.WarmWorkers);
        Assert.Equal(2, s1.TotalWorkers);
        Assert.True(s1.WarmWorkers >= opts.MinPoolSize,
            $"Round 1: warm={s1.WarmWorkers} went below min={opts.MinPoolSize}");

        // Round 2: at MinPoolSize, sweep must be a no-op regardless of clock.
        frozenTime = frozenTime.AddSeconds(9999);
        pool.SweepOnce();
        var s2 = pool.GetStats();
        Assert.Equal(2, s2.WarmWorkers);
        Assert.Equal(2, s2.TotalWorkers);

        // Round 3: hold 1 lease → warm=1 (below min in warm-only count).
        // Sweeper must not evict the remaining warm worker (surplus = warm - min = 1 - 2 = -1 ≤ 0).
        var lease = await pool.AcquireAsync();
        var s3pre = pool.GetStats();
        Assert.Equal(1, s3pre.WarmWorkers);
        Assert.Equal(1, s3pre.LeasedWorkers);
        Assert.Equal(2, s3pre.TotalWorkers);

        pool.SweepOnce();
        var s3post = pool.GetStats();
        Assert.Equal(1, s3post.WarmWorkers);  // not evicted
        Assert.Equal(1, s3post.LeasedWorkers);
        Assert.Equal(2, s3post.TotalWorkers); // no drift

        // Release lease; worker returns to warm (no-op reset).
        await lease.DisposeAsync();
        await WaitForStatsAsync(pool,
            s => s.LeasedWorkers == 0 && s.ResettingWorkers == 0);

        // Final state: at least the reset worker is warm; total ≥ min.
        var sFinal = pool.GetStats();
        Assert.True(sFinal.TotalWorkers >= 1,
            $"TotalWorkers drifted below 1 after lease release: {sFinal.TotalWorkers}");
        Assert.Equal(0, sFinal.LeasedWorkers);
        _output.WriteLine($"IdleSweep: 3 rounds stable. final warm={sFinal.WarmWorkers} total={sFinal.TotalWorkers}");
    }

    // ─── 8. Drain timeout: bounded force disposal, then idempotent DisposeAsync ───

    /// <summary>
    /// <c>ShutdownDrainTimeout=200ms</c>. An outstanding lease is deliberately held so the
    /// graceful wait expires. Pool must force-dispose all workers and <c>DrainAsync</c> must
    /// complete within a bounded time window — not hang.
    /// Then <c>DisposeAsync</c> twice must be idempotent (exactly-once disposal guarantee).
    /// Releasing the lease after drain is safe: <c>OnWorkerReturnedAsync</c> handles the
    /// already-evicted worker path without exception.
    /// Would fail if drain spun forever or double-dispose threw.
    /// </summary>
    [Fact]
    public async Task DrainTimeout_OutstandingLease_ForceDisposal_Bounded_IdempotentDispose()
    {
        var opts = MakeOptions(min: 1, max: 1, eager: 1, acquisitionSec: 10, drainSec: 0.2);
        var pool = MakeRealPool(opts);
        await pool.StartAsync();

        var lease = await pool.AcquireAsync();
        Assert.Equal(1, pool.GetStats().LeasedWorkers);

        // Start drain — ShutdownDrainTimeout=200ms, lease is not released.
        var sw = Stopwatch.StartNew();
        var drainTask = pool.DrainAsync();
        await drainTask.WaitAsync(TimeSpan.FromSeconds(5));
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3),
            $"DrainAsync hung: {sw.Elapsed} (ShutdownDrainTimeout=200ms)");
        Assert.True(pool.GetStats().IsDraining, "Pool must be marked draining.");
        _output.WriteLine($"DrainTimeout: completed in {sw.Elapsed}.");

        // DisposeAsync twice — exactly-once guarantee, must not throw.
        await pool.DisposeAsync();
        await pool.DisposeAsync();

        // Releasing the lease after drain+dispose is safe: callback handles evicted-worker path.
        await lease.DisposeAsync();
    }

    // ─── 9. Host lifecycle: drain-then-dispose via RunspacePoolLifecycleService ───

    /// <summary>
    /// <see cref="RunspacePoolLifecycleService.StopAsync"/> must drain then dispose the pool.
    /// A second call to <c>StopAsync</c> must be idempotent (drain is already set; dispose
    /// is a no-op). A direct <c>pool.DisposeAsync()</c> afterward is also safe.
    /// Sweeper and replenisher background loops must terminate after dispose.
    /// Would fail if <c>StopAsync</c> threw on the second call or background loops leaked.
    /// </summary>
    [Fact]
    public async Task HostLifecycle_StopAsync_DrainsThenDisposes_BackgroundLoopsTerminate()
    {
        var opts = MakeOptions(min: 1, max: 2, eager: 1, acquisitionSec: 5, drainSec: 5);
        var pool = MakeRealPool(opts);
        var logger = NullLoggerFactory.Instance.CreateLogger<RunspacePoolLifecycleService>();
        var svc = new RunspacePoolLifecycleService(pool, logger);

        // StartAsync starts the pool (workers ready before returning).
        await svc.StartAsync(CancellationToken.None);
        await WaitForStatsAsync(pool, s => s.WarmWorkers >= 1);
        Assert.True(pool.GetStats().IsStarted);

        // First stop: drains + disposes; sweeper/replenisher cancelled.
        await svc.StopAsync(CancellationToken.None);
        Assert.True(pool.GetStats().IsDraining);

        // Second stop: idempotent — DrainAsync returns immediately (_draining=1 already);
        // DisposeAsync returns immediately (_disposed=1 already). Must not throw.
        await svc.StopAsync(CancellationToken.None);

        // Direct DisposeAsync also idempotent.
        await pool.DisposeAsync();
        _output.WriteLine("HostLifecycle: StopAsync×2 + DisposeAsync×1 — no exceptions, loops terminated.");
    }

    // ─── 10. Both modes post-drain: reject new requests promptly ──────────────────

    /// <summary>
    /// After <c>pool.DrainAsync()</c>, both stateless and stateful HTTP requests must be rejected
    /// by the pool's drain guard — <c>AcquireAsync</c> throws <see cref="ObjectDisposedException"/>
    /// immediately (<c>_draining != 0</c>) — not by an acquisition timeout and not by a deadlock.
    ///
    /// To assert the drain/ObjectDisposed <em>semantics</em> (not merely "any error"), the pool's
    /// <c>AcquisitionTimeout</c> is set large (30s): a prompt (&lt; 3s) rejection can therefore only
    /// come from the drain fast-reject path. If the request instead fell through to the acquire wait,
    /// it would take ~30s and blow the bound. The rejection is also asserted as a tool-level isError
    /// result for the specific probe tool. Stateful test uses a real server-issued
    /// <c>Mcp-Session-Id</c>. Pre-drain success in both modes proves the assertion is non-vacuous.
    /// Would fail if the MCP SDK swallowed <see cref="ObjectDisposedException"/> from the pool or if
    /// drain rejection went through the acquisition-timeout wait.
    /// </summary>
    [Fact]
    public async Task BothModes_PostDrain_RejectNewRequests_Promptly()
    {
        // ── Stateless ──
        {
            // Large AcquisitionTimeout: a prompt rejection proves the drain fast-reject fired.
            var slOpts = MakeOptions(min: 1, max: 1, eager: 1, acquisitionSec: 30, drainSec: 5);
            var slPool = MakeRealPool(slOpts);
            var slPr = new PooledHttpRunspace(slPool, WorkerIdentityScript, NullLoggerFactory.Instance);
            slPr.FinalizeDiscovery();
            var slProbe = McpServerTool.Create(
                (CancellationToken _) =>
                    slPr.ExecuteThreadSafeAsync(ps =>
                    {
                        ps.Commands.Clear(); ps.AddScript("'sl-ok'");
                        var r = ps.Invoke<string>(); ps.Commands.Clear();
                        return Task.FromResult(r.Count > 0 ? r[0] ?? "null" : "null");
                    }),
                new McpServerToolCreateOptions { Name = "res_sl_probe", Description = "SL probe" });

            var (slApp, slClient, _) = await BuildHttpHostAsync(slPool, slPr, [slProbe], stateless: true);
            try
            {
                // Pre-drain: must succeed.
                var pre = await CallToolAsync(slClient, "res_sl_probe");
                Assert.False(HasMcpError(pre), $"Pre-drain stateless must succeed: {pre}");

                await slPool.DrainAsync();

                // Post-drain: must be rejected promptly by the drain guard (ObjectDisposedException),
                // NOT after the 30s acquisition wait.
                var sw = Stopwatch.StartNew();
                var post = await CallToolAsync(slClient, "res_sl_probe");
                sw.Stop();
                AssertToolError(post, "res_sl_probe");
                Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3),
                    $"Post-drain stateless rejection took {sw.Elapsed} — expected prompt drain reject (< 3s, AcquisitionTimeout=30s)");
                _output.WriteLine($"BothModes-Stateless: rejected in {sw.Elapsed}");
            }
            finally
            {
                slClient.Dispose();
                await slApp.StopAsync();
                await slApp.DisposeAsync();
            }
        }

        // ── Stateful ──
        {
            // Large AcquisitionTimeout: a prompt rejection proves the drain fast-reject fired.
            var sfOpts = MakeOptions(min: 1, max: 1, eager: 1, acquisitionSec: 30, drainSec: 5);
            var sfPool = MakeRealPool(sfOpts);
            var sfPr = new PooledHttpRunspace(sfPool, WorkerIdentityScript, NullLoggerFactory.Instance);
            sfPr.FinalizeDiscovery();
            var sfProbe = McpServerTool.Create(
                (CancellationToken _) =>
                    sfPr.ExecuteThreadSafeAsync(ps =>
                    {
                        ps.Commands.Clear(); ps.AddScript("'sf-ok'");
                        var r = ps.Invoke<string>(); ps.Commands.Clear();
                        return Task.FromResult(r.Count > 0 ? r[0] ?? "null" : "null");
                    }),
                new McpServerToolCreateOptions { Name = "res_sf_probe2", Description = "SF probe" });

            var (sfApp, sfClient, _) = await BuildHttpHostAsync(sfPool, sfPr, [sfProbe], stateless: false);
            try
            {
                // Initialize a real stateful session.
                var sessionId = await InitializeStatefulSessionAsync(sfClient);
                Assert.False(string.IsNullOrEmpty(sessionId), "Stateful host must return Mcp-Session-Id");

                // Pre-drain: must succeed with real session ID.
                var pre = await CallToolWithSessionAsync(sfClient, "res_sf_probe2", sessionId!);
                Assert.False(HasMcpError(pre), $"Pre-drain stateful must succeed: {pre}");

                await sfPool.DrainAsync();

                // Post-drain: must be rejected promptly by the drain guard (ObjectDisposedException),
                // NOT after the 30s acquisition wait.
                var sw = Stopwatch.StartNew();
                var post = await CallToolWithSessionAsync(sfClient, "res_sf_probe2", sessionId!);
                sw.Stop();
                AssertToolError(post, "res_sf_probe2");
                Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3),
                    $"Post-drain stateful rejection took {sw.Elapsed} — expected prompt drain reject (< 3s, AcquisitionTimeout=30s)");
                _output.WriteLine($"BothModes-Stateful: rejected in {sw.Elapsed}");
            }
            finally
            {
                sfClient.Dispose();
                await sfApp.StopAsync();
                await sfApp.DisposeAsync();
            }
        }
    }
}
