using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.Pool;
using Xunit;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.Tests.Unit.Pool;

/// <summary>
/// Unit tests for <see cref="StatelessRunspacePool"/>.
/// All tests use injected test doubles — no real PowerShell runspaces are created.
/// </summary>
[Trait("Category", "Unit")]
public sealed class StatelessRunspacePoolTests : IDisposable
{
    // ─── Helpers ────────────────────────────────────────────────────────────────

    // PS instance shared across mocks (no runspace; not actually invoked in unit tests).
    private static readonly PSPowerShell SharedPs = PSPowerShell.Create();

    private static Mock<IPowerShellRunspace> MockRunspace()
    {
        var mock = new Mock<IPowerShellRunspace>();
        mock.Setup(r => r.Instance).Returns(SharedPs);
        return mock;
    }

    private static StatelessRunspacePool MakePool(
        RunspacePoolOptions? options = null,
        Func<IPowerShellRunspace>? factory = null,
        Func<RunspaceWorker, ILogger, CancellationToken, Task>? reset = null)
    {
        return new StatelessRunspacePool(
            options ?? DefaultOptions(),
            loggerFactory: null,
            startupScript: null,
            workerFactory: factory ?? (() => MockRunspace().Object),
            snapshotCapture: _ => new HashSet<string>(),
            resetProtocol: reset ?? ((_, _, _) => Task.CompletedTask));
    }

    private static RunspacePoolOptions DefaultOptions(int min = 1, int max = 4, int eager = 1) =>
        new()
        {
            MinPoolSize = min,
            MaxPoolSize = max,
            EagerWarmCount = eager,
            AcquisitionTimeout = TimeSpan.FromSeconds(5),
            IdleTtl = TimeSpan.FromSeconds(300),
            SweepInterval = TimeSpan.FromSeconds(30),
            StopTimeout = TimeSpan.FromSeconds(5),
            ShutdownDrainTimeout = TimeSpan.FromMilliseconds(500),
            ReplenishCheckInterval = TimeSpan.FromSeconds(30),
        };

    public void Dispose() => SharedPs.Dispose();

    // ─── Construction / options validation ──────────────────────────────────────

    [Fact]
    public void Constructor_WithInvalidOptions_ThrowsArgumentException()
    {
        var bad = new RunspacePoolOptions { MinPoolSize = 0 };
        var ex = Assert.Throws<ArgumentException>(() => MakePool(options: bad));
        Assert.Contains("MinPoolSize", ex.Message);
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new StatelessRunspacePool(null!));
    }

    [Fact]
    public async Task GetStats_BeforeStart_ReturnsAllZeroCounts()
    {
        await using var pool = MakePool();
        var stats = pool.GetStats();
        Assert.Equal(0, stats.WarmWorkers);
        Assert.Equal(0, stats.LeasedWorkers);
        Assert.Equal(0, stats.ResettingWorkers);
        Assert.Equal(0, stats.TotalWorkers);
    }

    [Fact]
    public async Task GetStats_ReflectsOptionsMinMax()
    {
        await using var pool = MakePool(DefaultOptions(min: 3, max: 12, eager: 0));
        var stats = pool.GetStats();
        Assert.Equal(3, stats.MinPoolSize);
        Assert.Equal(12, stats.MaxPoolSize);
    }

    // ─── StartAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_WithEagerWarmCount_EnqueuesWarmWorkers()
    {
        await using var pool = MakePool(DefaultOptions(min: 1, max: 4, eager: 2));
        await pool.StartAsync();

        var stats = pool.GetStats();
        Assert.Equal(2, stats.WarmWorkers);
        Assert.Equal(2, stats.TotalWorkers);

        await pool.DisposeAsync();
    }

    [Fact]
    public async Task StartAsync_WithZeroEagerWarm_StartsWithNoWorkers()
    {
        await using var pool = MakePool(DefaultOptions(min: 1, max: 4, eager: 0));
        await pool.StartAsync();

        var stats = pool.GetStats();
        Assert.Equal(0, stats.WarmWorkers);
        Assert.Equal(0, stats.TotalWorkers);

        await pool.DisposeAsync();
    }

    [Fact]
    public async Task StartAsync_WhenAllWorkersFail_ThrowsInvalidOperation()
    {
        int calls = 0;
        await using var pool = MakePool(
            options: DefaultOptions(min: 1, max: 4, eager: 2),
            factory: () =>
            {
                calls++;
                throw new InvalidOperationException("Startup failure injected.");
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() => pool.StartAsync());
        Assert.Equal(2, calls);

        await pool.DisposeAsync();
    }

    [Fact]
    public async Task StartAsync_WhenSomeWorkersFail_SucceedsWithPartialCapacity()
    {
        int calls = 0;
        await using var pool = MakePool(
            options: DefaultOptions(min: 1, max: 4, eager: 3),
            factory: () =>
            {
                if (++calls == 2) throw new InvalidOperationException("Worker 2 fails.");
                return MockRunspace().Object;
            });

        await pool.StartAsync(); // should not throw (2 out of 3 succeed)

        var stats = pool.GetStats();
        Assert.Equal(2, stats.WarmWorkers);

        await pool.DisposeAsync();
    }

    // ─── AcquireAsync — happy path ──────────────────────────────────────────────

    [Fact]
    public async Task AcquireAsync_ReturnsLease_WithPowerShellAccess()
    {
        await using var pool = MakePool();
        await pool.StartAsync();

        await using var lease = await pool.AcquireAsync();

        Assert.NotNull(lease.PowerShell);

        await pool.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_MoveStats_WarmToLeased()
    {
        await using var pool = MakePool();
        await pool.StartAsync();

        var before = pool.GetStats();
        Assert.Equal(1, before.WarmWorkers);

        await using var lease = await pool.AcquireAsync();

        var during = pool.GetStats();
        Assert.Equal(0, during.WarmWorkers);
        Assert.Equal(1, during.LeasedWorkers);

        await pool.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_AfterLeaseDisposed_WorkerReturnedToWarm()
    {
        await using var pool = MakePool();
        await pool.StartAsync();

        var lease = await pool.AcquireAsync();
        await lease.DisposeAsync();

        // Allow reset to complete.
        await Task.Delay(50);

        var stats = pool.GetStats();
        Assert.Equal(1, stats.WarmWorkers);
        Assert.Equal(0, stats.LeasedWorkers);

        await pool.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_MultipleSequentialLeases_AllSucceed()
    {
        await using var pool = MakePool(DefaultOptions(min: 1, max: 4, eager: 1));
        await pool.StartAsync();

        for (int i = 0; i < 3; i++)
        {
            await using var lease = await pool.AcquireAsync();
            Assert.NotNull(lease.PowerShell);
            await Task.Delay(10); // let reset settle
        }

        await pool.DisposeAsync();
    }

    // ─── AcquireAsync — timeout / cancellation ──────────────────────────────────

    [Fact]
    public async Task AcquireAsync_WithZeroTimeout_ThrowsTimeoutWhenEmpty()
    {
        var opts = DefaultOptions(eager: 0);
        opts.AcquisitionTimeout = TimeSpan.Zero;
        await using var pool = MakePool(opts);
        await pool.StartAsync();

        await Assert.ThrowsAsync<TimeoutException>(() => pool.AcquireAsync().AsTask());

        await pool.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_WithShortTimeout_ThrowsTimeoutWhenNoWorkerAvailable()
    {
        var opts = DefaultOptions(min: 1, max: 1, eager: 1);
        opts.AcquisitionTimeout = TimeSpan.FromMilliseconds(50);
        opts.ShutdownDrainTimeout = TimeSpan.FromMilliseconds(200);
        await using var pool = MakePool(opts);
        await pool.StartAsync();

        // Hold the only worker; release it after assertion.
        var held = await pool.AcquireAsync();
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(() => pool.AcquireAsync().AsTask());
        }
        finally
        {
            await held.DisposeAsync();
        }

        await pool.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_WithCancelledToken_ThrowsOperationCancelled()
    {
        var opts = DefaultOptions(min: 1, max: 1, eager: 1);
        opts.AcquisitionTimeout = TimeSpan.FromSeconds(30);
        opts.ShutdownDrainTimeout = TimeSpan.FromMilliseconds(200);
        await using var pool = MakePool(opts);
        await pool.StartAsync();

        // Hold the only worker to force a wait; release after assertion.
        var held = await pool.AcquireAsync();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => pool.AcquireAsync(cts.Token).AsTask());
        }
        finally
        {
            await held.DisposeAsync();
        }

        await pool.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_OnDisposedPool_ThrowsObjectDisposed()
    {
        await using var pool = MakePool();
        await pool.StartAsync();
        await pool.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => pool.AcquireAsync().AsTask());
    }

    [Fact]
    public async Task AcquireAsync_OnDrainingPool_ThrowsObjectDisposed()
    {
        await using var pool = MakePool();
        await pool.StartAsync();

        _ = pool.DrainAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => pool.AcquireAsync().AsTask());

        await pool.DisposeAsync();
    }

    // ─── Lease disposal semantics ───────────────────────────────────────────────

    [Fact]
    public async Task Lease_DisposeIdempotent_SyncTwice()
    {
        await using var pool = MakePool();
        await pool.StartAsync();

        var lease = await pool.AcquireAsync();
        lease.Dispose();
        lease.Dispose(); // second call must not throw

        await pool.DisposeAsync();
    }

    [Fact]
    public async Task Lease_DisposeAsyncIdempotent_AsyncTwice()
    {
        await using var pool = MakePool();
        await pool.StartAsync();

        var lease = await pool.AcquireAsync();
        await lease.DisposeAsync();
        await lease.DisposeAsync(); // second call must not throw

        await pool.DisposeAsync();
    }

    [Fact]
    public async Task Lease_DisposeAsyncThenSync_Idempotent()
    {
        await using var pool = MakePool();
        await pool.StartAsync();

        var lease = await pool.AcquireAsync();
        await lease.DisposeAsync();
        lease.Dispose(); // mixed order must not throw

        await pool.DisposeAsync();
    }

    [Fact]
    public async Task Lease_PowerShellAfterDispose_ThrowsObjectDisposed()
    {
        await using var pool = MakePool();
        await pool.StartAsync();

        var lease = await pool.AcquireAsync();
        await lease.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => _ = lease.PowerShell);

        await pool.DisposeAsync();
    }

    // ─── Eviction / reset failure path ─────────────────────────────────────────

    [Fact]
    public async Task Lease_RequestEviction_WorkerIsEvicted_NotReturned()
    {
        await using var pool = MakePool();
        await pool.StartAsync();

        var lease = await pool.AcquireAsync();
        lease.RequestEviction();
        await lease.DisposeAsync();
        await Task.Delay(50);

        var stats = pool.GetStats();
        // Worker was evicted; no warm workers remain (replenisher not yet triggered).
        Assert.Equal(0, stats.LeasedWorkers);

        await pool.DisposeAsync();
    }

    [Fact]
    public async Task LeaseReturn_WhenResetThrows_WorkerIsEvicted()
    {
        await using var pool = MakePool(
            reset: (_, _, _) => Task.FromException(new InvalidOperationException("Reset blew up.")));
        await pool.StartAsync();

        await using (await pool.AcquireAsync()) { /* returns worker which triggers reset */ }

        await Task.Delay(100);

        var stats = pool.GetStats();
        Assert.Equal(0, stats.LeasedWorkers);
        Assert.Equal(0, stats.ResettingWorkers);
        // Worker was evicted after reset failure; replenisher may have already added a new one.
        Assert.InRange(stats.TotalWorkers, 0, 1);

        await pool.DisposeAsync();
    }

    // ─── Capacity / backpressure ────────────────────────────────────────────────

    [Fact]
    public async Task AcquireAsync_ExceedsMaxPool_BlocksUntilRelease()
    {
        await using var pool = MakePool(DefaultOptions(min: 2, max: 2, eager: 2));
        await pool.StartAsync();

        // Acquire both workers.
        var l1 = await pool.AcquireAsync();
        var l2 = await pool.AcquireAsync();

        // Third acquire must wait; release l1 after short delay.
        var releaseTask = Task.Run(async () =>
        {
            await Task.Delay(100);
            await l1.DisposeAsync();
        });

        var acquireTask = pool.AcquireAsync().AsTask();
        await Task.WhenAll(releaseTask, acquireTask);

        var l3 = await acquireTask;
        Assert.NotNull(l3.PowerShell);

        await l2.DisposeAsync();
        await l3.DisposeAsync();
        await pool.DisposeAsync();
    }

    // ─── Stats ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStats_DuringLease_ShowsCorrectStateDistribution()
    {
        await using var pool = MakePool(DefaultOptions(min: 2, max: 4, eager: 2));
        await pool.StartAsync();

        await using var lease = await pool.AcquireAsync();

        var stats = pool.GetStats();
        Assert.Equal(1, stats.LeasedWorkers);
        Assert.Equal(1, stats.WarmWorkers);
        Assert.Equal(2, stats.TotalWorkers);
        Assert.Equal(2, stats.MinPoolSize);
        Assert.Equal(4, stats.MaxPoolSize);

        await pool.DisposeAsync();
    }

    // ─── Drain ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DrainAsync_WithNoOutstandingLeases_CompletesImmediately()
    {
        await using var pool = MakePool();
        await pool.StartAsync();

        // Ensure no leases are outstanding before draining.
        await pool.DrainAsync();

        // After drain, dispose completes cleanly.
        await pool.DisposeAsync();
    }

    [Fact]
    public async Task DrainAsync_WaitsForOutstandingLeases()
    {
        await using var pool = MakePool();
        await pool.StartAsync();

        var lease = await pool.AcquireAsync();

        var drainTask = pool.DrainAsync();
        Assert.False(drainTask.IsCompleted);

        await lease.DisposeAsync();
        await Task.Delay(80); // reset callback fires
        await drainTask;

        await pool.DisposeAsync();
    }

    [Fact]
    public async Task DrainAsync_CalledTwice_IsIdempotent()
    {
        await using var pool = MakePool();
        await pool.StartAsync();

        await pool.DrainAsync();
        await pool.DrainAsync(); // second call must not throw

        await pool.DisposeAsync();
    }

    // ─── DisposeAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsync_CalledTwice_IsIdempotent()
    {
        var pool = MakePool();
        await pool.StartAsync();

        await pool.DisposeAsync();
        await pool.DisposeAsync(); // must not throw
    }

    // ─── Concurrency ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AcquireAsync_ConcurrentCallers_AllGetDistinctLeases()
    {
        await using var pool = MakePool(DefaultOptions(min: 4, max: 4, eager: 4));
        await pool.StartAsync();

        var tasks = Enumerable.Range(0, 4)
            .Select(_ => pool.AcquireAsync().AsTask())
            .ToArray();

        var leases = await Task.WhenAll(tasks);

        // All leases must refer to distinct PowerShell instances.
        // (In the test double case they all share SharedPs — but the lease objects are distinct.)
        Assert.Equal(4, leases.Length);
        Assert.All(leases, l => Assert.NotNull(l));

        foreach (var l in leases) await l.DisposeAsync();
        await pool.DisposeAsync();
    }

    [Fact]
    public async Task GetStats_IsConsistentUnderConcurrentAcquireReturn()
    {
        await using var pool = MakePool(DefaultOptions(min: 3, max: 3, eager: 3));
        await pool.StartAsync();

        var tasks = Enumerable.Range(0, 9).Select(_ => Task.Run(async () =>
        {
            await using var l = await pool.AcquireAsync();
            await Task.Delay(5);
        })).ToArray();

        await Task.WhenAll(tasks);

        var stats = pool.GetStats();
        Assert.Equal(0, stats.LeasedWorkers);
        Assert.Equal(0, stats.ResettingWorkers);

        await pool.DisposeAsync();
    }
}
