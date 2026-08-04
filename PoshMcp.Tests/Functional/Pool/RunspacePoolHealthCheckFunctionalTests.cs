using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PoshMcp.Server.Health;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.Pool;
using PoshMcp.Server.Server;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Functional.Pool;

/// <summary>
/// Functional tests for <see cref="RunspacePoolHealthCheck"/>.
/// G2: gated factory — observe Degraded then Healthy.
/// G3: drain in progress — Degraded with draining marker.
/// G4: TestServer endpoints — HTTP status and JSON "runspace_pool" entry.
/// G7: disposed/failure boundaries.
/// G8: EagerWarmCount > MinPoolSize — prove thresholds are not conflated.
/// </summary>
[Trait("Category", "Functional")]
public sealed class RunspacePoolHealthCheckFunctionalTests
{
    private readonly ITestOutputHelper _output;

    public RunspacePoolHealthCheckFunctionalTests(ITestOutputHelper output) =>
        _output = output;

    // ─── Helpers ────────────────────────────────────────────────────────────────

    private static RunspacePoolOptions OptionsFor(
        int min = 1, int max = 4, int eager = 1,
        TimeSpan? replenishInterval = null) => new()
        {
            MinPoolSize = min,
            MaxPoolSize = max,
            EagerWarmCount = eager,
            AcquisitionTimeout = TimeSpan.FromSeconds(10),
            IdleTtl = TimeSpan.FromSeconds(300),
            SweepInterval = TimeSpan.FromSeconds(60),
            StopTimeout = TimeSpan.FromSeconds(5),
            ShutdownDrainTimeout = TimeSpan.FromSeconds(10),
            ReplenishCheckInterval = replenishInterval ?? TimeSpan.FromSeconds(60),
        };

    private static RunspacePoolHealthCheck MakeCheck(IRunspacePool pool) =>
        new(pool, NullLogger<RunspacePoolHealthCheck>.Instance);

    private static async Task WaitForConditionAsync(
        Func<bool> condition,
        string description,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Timed out waiting for: {description}");
            await Task.Delay(20);
        }
    }

    // ─── G2: Degraded while factory is in flight, Healthy after release ────────

    [Fact]
    public async Task G2_GatedFactory_DegradedWhileCreating_ThenHealthy()
    {
        // EagerWarmCount=1 so StartAsync creates only 1 worker immediately.
        // MinPoolSize=2 so the pool is BELOW min after startup → replenisher creates more.
        // ReplenishCheckInterval=100ms so we don't wait long.
        var factoryGate = new SemaphoreSlim(0, 1);
        var factoryEntered = new SemaphoreSlim(0, 1);
        var firstCreation = true;

        await using var pool = new StatelessRunspacePool(
            OptionsFor(min: 2, max: 2, eager: 1, replenishInterval: TimeSpan.FromMilliseconds(100)),
            loggerFactory: null,
            startupScript: null,
            workerFactory: () =>
            {
                // First creation (EagerWarm): pass through immediately.
                if (firstCreation)
                {
                    firstCreation = false;
                    return new IsolatedPowerShellRunspace();
                }
                // Subsequent creations (replenisher): gate.
                factoryEntered.Release();
                factoryGate.Wait(TimeSpan.FromSeconds(30));
                return new IsolatedPowerShellRunspace();
            });

        await pool.StartAsync();

        // After StartAsync: IsStarted=true, WarmWorkers=1 < MinPoolSize=2.
        var statsAfterStart = pool.GetStats();
        Assert.True(statsAfterStart.IsStarted, "Pool must be started after StartAsync.");
        Assert.Equal(1, statsAfterStart.WarmWorkers);

        // Wait for replenisher to detect below-min and start creating.
        await factoryEntered.WaitAsync(TimeSpan.FromSeconds(5));

        // While factory is blocked: WarmWorkers=1, CreatingWorkers=1 → Degraded.
        var check = MakeCheck(pool);
        var degradedResult = await check.CheckHealthAsync(new HealthCheckContext());
        _output.WriteLine($"G2 during creation: {degradedResult.Status} — {degradedResult.Description}");

        Assert.Equal(HealthStatus.Degraded, degradedResult.Status);
        Assert.True((int)degradedResult.Data["creating"] > 0,
            "Expected CreatingWorkers > 0 while factory is blocked.");

        // Release factory gate → second worker created → WarmWorkers=2 ≥ min=2.
        factoryGate.Release();

        await WaitForConditionAsync(
            () => pool.GetStats().WarmWorkers >= 2,
            "WarmWorkers >= 2 (Healthy)");

        var healthyResult = await check.CheckHealthAsync(new HealthCheckContext());
        _output.WriteLine($"G2 after release: {healthyResult.Status} — {healthyResult.Description}");
        Assert.Equal(HealthStatus.Healthy, healthyResult.Status);
    }

    // ─── G3: Drain in progress → Degraded with draining marker ───────────────

    [Fact]
    public async Task G3_DrainInProgress_ReturnsDegradedWithDrainingMarker()
    {
        await using var pool = new StatelessRunspacePool(
            OptionsFor(min: 1, max: 4, eager: 1),
            loggerFactory: null,
            startupScript: null);

        await pool.StartAsync();

        // Acquire a lease so the drain blocks until we return it.
        var lease = await pool.AcquireAsync();
        _output.WriteLine($"Lease acquired. Stats: {pool.GetStats()}");

        // Start drain in background — it blocks because there's an outstanding lease.
        var drainTask = pool.DrainAsync(CancellationToken.None);

        // Drain is in progress (IsDraining=true) while lease is held.
        // Pool classifies this as Degraded (draining takes precedence).
        var check = MakeCheck(pool);
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        _output.WriteLine($"G3 during drain: {result.Status} — {result.Description}");

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("draining", result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(true, result.Data["is_draining"]);

        // Return lease so drain can complete.
        await lease.DisposeAsync();
        await drainTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    // ─── G4: TestServer endpoints — Healthy pool → 200, below min → 503 ────────

    [Fact]
    public async Task G4_HealthyPool_ReturnsReadiness200_WithRunspacePoolEntry()
    {
        await using var ctx = await BuildHealthCheckHostAsync(min: 1, max: 2, eager: 1);
        var client = ctx.Client;

        var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"G4 healthy /health/ready: {response.StatusCode} — {body}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = JsonDocument.Parse(body);
        AssertHasCheck(json, "runspace_pool", "Healthy");
    }

    [Fact]
    public async Task G4_HealthEndpoint_IncludesRunspacePoolCheck()
    {
        await using var ctx = await BuildHealthCheckHostAsync(min: 1, max: 2, eager: 1);

        var response = await ctx.Client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"G4 /health: {response.StatusCode} — {body}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(body);
        var checks = json.RootElement.GetProperty("checks");
        Assert.True(
            checks.EnumerateArray().Any(c =>
                c.GetProperty("name").GetString() == "runspace_pool"),
            "Expected 'runspace_pool' check in /health response.");
    }

    [Fact]
    public async Task G4_PoolBelowMinPoolSize_ReturnsReadiness503()
    {
        // MaxPoolSize=MinPoolSize=1 so acquiring the sole worker drops warm below min.
        await using var ctx = await BuildHealthCheckHostAsync(min: 1, max: 1, eager: 1);

        // Acquire the only worker to make WarmWorkers=0 < MinPoolSize=1.
        var lease = await ctx.Pool.AcquireAsync();

        var response = await ctx.Client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"G4 below-min /health/ready: {response.StatusCode} — {body}");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var json = JsonDocument.Parse(body);
        AssertHasCheck(json, "runspace_pool");  // present, regardless of status

        // Return the lease and verify recovery.
        await lease.DisposeAsync();
        await WaitForConditionAsync(
            () => ctx.Pool.GetStats().WarmWorkers >= 1,
            "WarmWorkers >= 1 after lease return");

        var recoveryResponse = await ctx.Client.GetAsync("/health/ready");
        _output.WriteLine($"G4 recovery /health/ready: {recoveryResponse.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, recoveryResponse.StatusCode);
    }

    [Fact]
    public async Task G4_PoolDraining_ReturnsReadiness503()
    {
        await using var ctx = await BuildHealthCheckHostAsync(min: 1, max: 1, eager: 1);

        // Hold a lease so DrainAsync blocks.
        var lease = await ctx.Pool.AcquireAsync();
        var drainTask = ctx.Pool.DrainAsync();

        var response = await ctx.Client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"G4 draining /health/ready: {response.StatusCode} — {body}");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        await lease.DisposeAsync();
        await drainTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    // ─── G7: Disposed pool — health check surfaces cleanly ───────────────────

    [Fact]
    public async Task G7_DisposedPool_HealthCheck_DoesNotThrow()
    {
        // A disposed pool has _draining=1 (DisposeAsync calls DrainAsync internally)
        // and all workers evicted. The health check must not throw; it returns a non-Healthy
        // status (Degraded because IsDraining=true after dispose).
        var pool = new StatelessRunspacePool(
            OptionsFor(min: 1, max: 4, eager: 1),
            loggerFactory: null,
            startupScript: null);
        await pool.StartAsync();
        await pool.DisposeAsync();

        var check = MakeCheck(pool);

        // Must not throw — health check catches all exceptions.
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        _output.WriteLine($"G7 disposed: {result.Status} — {result.Description}");
        // IsDraining=true after dispose (DrainAsync is called during DisposeAsync) → Degraded.
        Assert.NotEqual(HealthStatus.Healthy, result.Status);
    }

    // ─── G8: EagerWarmCount > MinPoolSize — thresholds are not conflated ──────

    [Fact]
    public async Task G8_EagerWarmCountExceedsMinPoolSize_SteadyStateUsesMinPoolSize()
    {
        // EagerWarmCount=3, MinPoolSize=1. After start, warm=3. Steady-state classification
        // uses MinPoolSize, not EagerWarmCount.
        await using var pool = new StatelessRunspacePool(
            OptionsFor(min: 1, max: 3, eager: 3),
            loggerFactory: null,
            startupScript: null);

        await pool.StartAsync();
        var check = MakeCheck(pool);

        // warm=3 ≥ min=1 → Healthy (not Degraded — EagerWarmCount is not a steady-state threshold).
        var initialResult = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, initialResult.Status);

        // Acquire 2 leases: warm=1 (still ≥ min=1), but below EagerWarmCount=3.
        var lease1 = await pool.AcquireAsync();
        var lease2 = await pool.AcquireAsync();

        var withLeasesResult = await check.CheckHealthAsync(new HealthCheckContext());
        _output.WriteLine(
            $"G8 warm={pool.GetStats().WarmWorkers}, min={pool.GetStats().MinPoolSize}: " +
            $"{withLeasesResult.Status}");

        // Still Healthy: warm=1 ≥ min=1. EagerWarmCount=3 does NOT affect this.
        Assert.Equal(HealthStatus.Healthy, withLeasesResult.Status);

        // Acquire the last lease: warm=0 < min=1 → Unhealthy (all leased, none creating).
        var lease3 = await pool.AcquireAsync();
        var exhaustedResult = await check.CheckHealthAsync(new HealthCheckContext());
        _output.WriteLine(
            $"G8 exhausted warm={pool.GetStats().WarmWorkers}: {exhaustedResult.Status}");

        Assert.NotEqual(HealthStatus.Healthy, exhaustedResult.Status);

        await lease1.DisposeAsync();
        await lease2.DisposeAsync();
        await lease3.DisposeAsync();
    }

    // ─── TestServer builder for G4 ────────────────────────────────────────────

    private async Task<HealthCheckTestContext> BuildHealthCheckHostAsync(
        int min, int max, int eager)
    {
        var poolOptions = OptionsFor(min, max, eager);
        var pool = new StatelessRunspacePool(poolOptions, loggerFactory: null, startupScript: null);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddSingleton<IRunspacePool>(pool);
        builder.Services.AddSingleton<IHostedService, RunspacePoolLifecycleService>();
        builder.Services.AddHealthChecks()
            .AddCheck<RunspacePoolHealthCheck>("runspace_pool", tags: new[] { "ready" });

        var app = builder.Build();

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = WriteHealthJsonAsync,
        }).AllowAnonymous();

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = _ => true,
            ResponseWriter = WriteHealthJsonAsync,
            ResultStatusCodes =
            {
                [HealthStatus.Healthy]   = StatusCodes.Status200OK,
                [HealthStatus.Degraded]  = StatusCodes.Status503ServiceUnavailable,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
            },
        }).AllowAnonymous();

        // RunspacePoolLifecycleService.StartAsync owns pool startup.
        await app.StartAsync();

        var client = app.GetTestClient();
        return new HealthCheckTestContext(app, client, pool);
    }

    private static async Task WriteHealthJsonAsync(HttpContext ctx, HealthReport report)
    {
        ctx.Response.ContentType = "application/json";
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                data = e.Value.Data,
            }),
        });
        await ctx.Response.WriteAsync(json);
    }

    private static void AssertHasCheck(JsonDocument doc, string checkName, string? expectedStatus = null)
    {
        var checks = doc.RootElement.GetProperty("checks");
        var found = checks.EnumerateArray()
            .FirstOrDefault(c => c.GetProperty("name").GetString() == checkName);
        Assert.True(found.ValueKind != System.Text.Json.JsonValueKind.Undefined,
            $"Expected check '{checkName}' not found in health response.");
        if (expectedStatus is not null)
        {
            var actualStatus = found.GetProperty("status").GetString();
            Assert.Equal(expectedStatus, actualStatus);
        }
    }

    private sealed class HealthCheckTestContext : IAsyncDisposable
    {
        public WebApplication App { get; }
        public HttpClient Client { get; }
        public StatelessRunspacePool Pool { get; }

        public HealthCheckTestContext(WebApplication app, HttpClient client, StatelessRunspacePool pool)
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
