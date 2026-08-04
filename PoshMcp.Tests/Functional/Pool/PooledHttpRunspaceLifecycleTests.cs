using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.Pool;
using PoshMcp.Server.Server;
using Xunit;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.Tests.Functional.Pool;

/// <summary>
/// Functional lifecycle tests for <see cref="RunspacePoolLifecycleService"/> and
/// <see cref="PooledHttpRunspace"/> using a real <see cref="StatelessRunspacePool"/>
/// and real PowerShell runspaces to verify production-faithful behaviour.
/// </summary>
[Trait("Category", "Functional")]
public sealed class PooledHttpRunspaceLifecycleTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────────

    private static RunspacePoolOptions FastOptions(int eager = 1, int max = 4) => new()
    {
        MinPoolSize = 1,
        MaxPoolSize = max,
        EagerWarmCount = eager,
        AcquisitionTimeout = TimeSpan.FromSeconds(10),
        IdleTtl = TimeSpan.FromSeconds(300),
        SweepInterval = TimeSpan.FromSeconds(60),
        StopTimeout = TimeSpan.FromSeconds(5),
        ShutdownDrainTimeout = TimeSpan.FromSeconds(5),
        ReplenishCheckInterval = TimeSpan.FromSeconds(60),
    };

    private static StatelessRunspacePool CreatePool(int eager = 1, int max = 4,
        Func<IPowerShellRunspace>? factory = null) =>
        new StatelessRunspacePool(
            FastOptions(eager, max),
            loggerFactory: null,
            startupScript: null,
            workerFactory: factory);

    // ─── StartAsync: gated factory blocks until eager workers are warm ───────

    [Fact]
    public async Task StartAsync_GatedFactory_BlocksUntilEagerWorkerReady()
    {
        // Gate delays worker creation so we can confirm StartAsync blocks until
        // at least one worker is ready. The factory signals `factoryEntered` when
        // it begins, then waits for `factoryGate` before creating the runspace.
        var factoryGate = new SemaphoreSlim(0, 1);
        var factoryEntered = new SemaphoreSlim(0, 1);

        await using var pool = new StatelessRunspacePool(
            FastOptions(eager: 1),
            loggerFactory: null,
            startupScript: null,
            workerFactory: () =>
            {
                factoryEntered.Release();  // signal: factory started
                factoryGate.Wait();        // hold until test releases
                return new IsolatedPowerShellRunspace();
            });

        var startTask = pool.StartAsync();

        // Wait for the factory to actually start (runs on a thread-pool thread via Task.Run).
        await factoryEntered.WaitAsync(TimeSpan.FromSeconds(10));

        // StartAsync should still be running — factory has not finished yet.
        Assert.False(startTask.IsCompleted);

        // Release the factory gate — StartAsync should now complete.
        factoryGate.Release();
        await startTask.WaitAsync(TimeSpan.FromSeconds(10));

        // Pool is ready: at least one warm worker is available.
        var stats = pool.GetStats();
        Assert.True(stats.WarmWorkers >= 1,
            $"Expected warm workers >= 1 after StartAsync. Stats: {stats}");
    }

    // ─── StartAsync: all factories fail → exception propagates ──────────────

    [Fact]
    public async Task StartAsync_AllWorkerFactoriesFail_ThrowsInvalidOperationException()
    {
        await using var pool = new StatelessRunspacePool(
            FastOptions(eager: 2),
            loggerFactory: null,
            startupScript: null,
            workerFactory: () => throw new InvalidOperationException("factory boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pool.StartAsync());
    }

    // ─── Lifecycle service: StartAsync blocks until eager workers warm ───────

    [Fact]
    public async Task LifecycleService_StartAsync_BlocksUntilEagerWorkerReady()
    {
        await using var pool = CreatePool(eager: 1);
        var svc = new RunspacePoolLifecycleService(pool, NullLogger<RunspacePoolLifecycleService>.Instance);

        await svc.StartAsync(CancellationToken.None);

        var stats = pool.GetStats();
        Assert.True(stats.WarmWorkers >= 1,
            $"Expected warm workers >= 1 after service StartAsync. Stats: {stats}");
    }

    // ─── StopAsync: drain-before-dispose, counters reach zero ───────────────

    [Fact]
    public async Task LifecycleService_StopAsync_AllCountersReachZero()
    {
        await using var pool = CreatePool(eager: 1);
        var svc = new RunspacePoolLifecycleService(pool, NullLogger<RunspacePoolLifecycleService>.Instance);

        await svc.StartAsync(CancellationToken.None);
        await svc.StopAsync(CancellationToken.None);

        var stats = pool.GetStats();
        Assert.Equal(0, stats.TotalWorkers);
        Assert.Equal(0, stats.WarmWorkers);
        Assert.Equal(0, stats.LeasedWorkers);
        Assert.Equal(0, stats.ResettingWorkers);
    }

    // ─── Session completion: pool is not drained/disposed ───────────────────

    [Fact]
    public void SessionCompletion_Callback_DoesNotDrainOrDisposePool()
    {
        var pool = new Mock<IRunspacePool>();
        pool.Setup(p => p.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        pool.Setup(p => p.GetStats()).Returns(new RunspacePoolStats(1, 4, 1, 0, 0, 1));

        // No-op callback as wired in HttpServerHost: never touches the pool.
        var lifecycle = new McpSessionLifecycle(_ => { });

        lifecycle.CompleteSession("session-xyz");

        pool.Verify(p => p.DrainAsync(It.IsAny<CancellationToken>()), Times.Never);
        pool.Verify(p => p.DisposeAsync(), Times.Never);
    }

    // ─── State isolation: variable from one call is not visible in next ──────

    [Fact]
    public async Task StatelessExecution_VariableFromOneCall_IsNotVisibleInSubsequentCall()
    {
        // Use real PowerShell runspaces and the real reset protocol so that the
        // worker state ($sentinel) is cleaned between calls.
        await using var pool = new StatelessRunspacePool(
            FastOptions(eager: 1),
            loggerFactory: null,
            startupScript: null);
        await pool.StartAsync();

        using var adapter = new PooledHttpRunspace(pool, (string?)null, NullLoggerFactory.Instance);
        adapter.FinalizeDiscovery(); // discovery not needed here

        // Call A: set $script:sentinel = "callA"
        await adapter.ExecuteThreadSafeAsync<bool>(ps =>
        {
            ps.Commands.Clear();
            ps.AddScript("$script:sentinel = 'callA'");
            ps.Invoke();
            ps.Commands.Clear();
            return Task.FromResult(true);
        });

        // Call B: $script:sentinel must not be "callA" — reset protocol cleared it.
        var sentinelInCallB = await adapter.ExecuteThreadSafeAsync<string?>(ps =>
        {
            ps.Commands.Clear();
            ps.AddScript("$script:sentinel");
            var results = ps.Invoke<string?>();
            ps.Commands.Clear();
            return Task.FromResult(results.Count > 0 ? results[0] : null);
        });

        // After reset the variable must be absent (null or empty).
        Assert.True(string.IsNullOrEmpty(sentinelInCallB),
            $"Expected $sentinel to be cleared by reset, but got: '{sentinelInCallB}'");
    }

    // ─── Discovery finalisation ──────────────────────────────────────────────

    [Fact]
    public async Task FinalizeDiscovery_AfterToolIntrospection_BlocksSubsequentInstanceAccess()
    {
        await using var pool = CreatePool(eager: 0);
        await pool.StartAsync();

        var discovery = new IsolatedPowerShellRunspace();
        using var adapter = new PooledHttpRunspace(pool, discovery, NullLoggerFactory.Instance);

        // Access Instance (simulates McpToolFactoryV2 introspection).
        var ps = adapter.Instance;
        Assert.NotNull(ps);

        // Finalize discovery — simulates the call in HttpServerHost.
        adapter.FinalizeDiscovery();

        // Any subsequent Instance access must throw, not silently recreate the runspace.
        Assert.Throws<InvalidOperationException>(() => _ = adapter.Instance);
    }
}
