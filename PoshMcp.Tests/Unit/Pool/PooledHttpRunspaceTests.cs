using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.Pool;
using Xunit;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.Tests.Unit.Pool;

/// <summary>
/// Unit tests for <see cref="PooledHttpRunspace"/> using a real <see cref="StatelessRunspacePool"/>
/// backed by mock workers to avoid real PowerShell runspace creation.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PooledHttpRunspaceTests : IAsyncLifetime
{
    // ─── Helpers ────────────────────────────────────────────────────────────────

    private static Mock<IPowerShellRunspace> MockWorker()
    {
        var ps = PSPowerShell.Create();
        var mock = new Mock<IPowerShellRunspace>();
        mock.Setup(r => r.Instance).Returns(ps);
        mock.Setup(r => r.Dispose()).Callback(ps.Dispose);
        return mock;
    }

    private static RunspacePoolOptions FastOptions(int eager = 1) => new()
    {
        MinPoolSize = 1,
        MaxPoolSize = 4,
        EagerWarmCount = eager,
        AcquisitionTimeout = TimeSpan.FromSeconds(5),
        IdleTtl = TimeSpan.FromSeconds(300),
        SweepInterval = TimeSpan.FromSeconds(60),
        StopTimeout = TimeSpan.FromSeconds(2),
        ShutdownDrainTimeout = TimeSpan.FromMilliseconds(500),
        ReplenishCheckInterval = TimeSpan.FromSeconds(60),
    };

    private StatelessRunspacePool _pool = null!;
    private PooledHttpRunspace _adapter = null!;
    private Mock<IPowerShellRunspace> _discoveryMock = null!;

    public async Task InitializeAsync()
    {
        _pool = new StatelessRunspacePool(
            FastOptions(),
            loggerFactory: null,
            startupScript: null,
            workerFactory: () => MockWorker().Object,
            snapshotCapture: _ => new HashSet<string>(),
            driveSnapshotCapture: _ => new HashSet<string>(),
            functionSnapshotCapture: _ => new HashSet<string>(),
            resetProtocol: (_, _, _) => Task.CompletedTask);
        await _pool.StartAsync();

        // Use injected mock discovery runspace so no real PS runspace is created.
        _discoveryMock = MockWorker();
        _adapter = new PooledHttpRunspace(_pool, _discoveryMock.Object, NullLoggerFactory.Instance);
    }

    public async Task DisposeAsync()
    {
        _adapter.Dispose();
        await _pool.DisposeAsync();
    }

    // ─── Instance (discovery runspace) ──────────────────────────────────────────

    [Fact]
    public void Instance_ReturnsPowerShell_WithoutTouchingPool()
    {
        var statsBefore = _pool.GetStats();
        var ps = _adapter.Instance;
        var statsAfter = _pool.GetStats();

        Assert.NotNull(ps);
        // Instance must not consume a pool lease.
        Assert.Equal(statsBefore.LeasedWorkers, statsAfter.LeasedWorkers);
    }

    [Fact]
    public void Instance_ReturnsSameReference_OnRepeatedAccess()
    {
        var ps1 = _adapter.Instance;
        var ps2 = _adapter.Instance;
        Assert.Same(ps1, ps2);
    }

    // ─── ExecuteThreadSafeAsync (preferred path) ────────────────────────────────

    [Fact]
    public async Task ExecuteThreadSafeAsync_ReturnsResult()
    {
        var result = await _adapter.ExecuteThreadSafeAsync<int>(ps =>
        {
            Assert.NotNull(ps);
            return Task.FromResult(42);
        });
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task ExecuteThreadSafeAsync_LeasedCount_IsZeroAfterReturn()
    {
        await _adapter.ExecuteThreadSafeAsync<bool>(ps => Task.FromResult(true));
        var stats = _pool.GetStats();
        Assert.Equal(0, stats.LeasedWorkers);
    }

    [Fact]
    public async Task ExecuteThreadSafeAsync_LeaseIsHeld_DuringExecution()
    {
        var leasedDuringExec = 0;
        await _adapter.ExecuteThreadSafeAsync<bool>(ps =>
        {
            leasedDuringExec = _pool.GetStats().LeasedWorkers;
            return Task.FromResult(true);
        });
        Assert.Equal(1, leasedDuringExec);
    }

    [Fact]
    public async Task ExecuteThreadSafeAsync_OnException_ReturnsLeaseAndEvicts()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _adapter.ExecuteThreadSafeAsync<bool>(ps =>
                throw new InvalidOperationException("test")));

        // Lease must be returned (leasedWorkers = 0) even after exception.
        await WaitForStatsAsync(_pool, s => s.LeasedWorkers == 0);
    }

    // ─── ExecuteThreadSafe (sync bridge) ────────────────────────────────────────

    [Fact]
    public void ExecuteThreadSafe_Generic_ReturnsResult()
    {
        var result = _adapter.ExecuteThreadSafe(ps =>
        {
            Assert.NotNull(ps);
            return "hello";
        });
        Assert.Equal("hello", result);
    }

    [Fact]
    public void ExecuteThreadSafe_Action_ExecutesAndReleasesLease()
    {
        var executed = false;
        _adapter.ExecuteThreadSafe(ps =>
        {
            Assert.NotNull(ps);
            executed = true;
        });
        Assert.True(executed);
        Assert.Equal(0, _pool.GetStats().LeasedWorkers);
    }

    [Fact]
    public void ExecuteThreadSafe_Generic_OnException_ReleasesLease()
    {
        Assert.Throws<ArgumentException>(() =>
            _adapter.ExecuteThreadSafe<int>(_ => throw new ArgumentException("oops")));

        // Lease returned after exception.
        var stats = _pool.GetStats();
        Assert.Equal(0, stats.LeasedWorkers);
    }

    // ─── FinalizeDiscovery ───────────────────────────────────────────────────────

    [Fact]
    public void FinalizeDiscovery_DisposesDiscoveryRunspace()
    {
        var discoveryMock = MockWorker();
        using var adapter = new PooledHttpRunspace(_pool, discoveryMock.Object, NullLoggerFactory.Instance);

        // Access Instance to materialise the lazy discovery runspace.
        _ = adapter.Instance;

        adapter.FinalizeDiscovery();

        discoveryMock.Verify(r => r.Dispose(), Times.Once);
    }

    [Fact]
    public void FinalizeDiscovery_IsIdempotent()
    {
        using var adapter = new PooledHttpRunspace(_pool, MockWorker().Object, NullLoggerFactory.Instance);

        adapter.FinalizeDiscovery();
        adapter.FinalizeDiscovery(); // must not throw
    }

    [Fact]
    public void FinalizeDiscovery_WhenDiscoveryNotAccessed_DoesNotThrow()
    {
        using var adapter = new PooledHttpRunspace(_pool, MockWorker().Object, NullLoggerFactory.Instance);

        // No access to Instance — Lazy not materialized.
        adapter.FinalizeDiscovery(); // must not throw
    }

    [Fact]
    public void Instance_AfterFinalizeDiscovery_ThrowsInvalidOperationException()
    {
        using var adapter = new PooledHttpRunspace(_pool, MockWorker().Object, NullLoggerFactory.Instance);
        adapter.FinalizeDiscovery();

        Assert.Throws<InvalidOperationException>(() => _ = adapter.Instance);
    }

    // ─── Session-ID isolation (stateless guarantee) ─────────────────────────────

    [Fact]
    public async Task ConcurrentRequests_HoldSeparateLeases_DoNotSharePowerShellInstances()
    {
        // Two concurrent calls each see their own PSPowerShell — they cannot
        // observe each other's Commands because each holds a separate lease.
        var pool = new StatelessRunspacePool(
            new RunspacePoolOptions
            {
                MinPoolSize = 2,
                MaxPoolSize = 4,
                EagerWarmCount = 2,
                AcquisitionTimeout = TimeSpan.FromSeconds(5),
                IdleTtl = TimeSpan.FromSeconds(300),
                SweepInterval = TimeSpan.FromSeconds(60),
                StopTimeout = TimeSpan.FromSeconds(2),
                ShutdownDrainTimeout = TimeSpan.FromMilliseconds(500),
                ReplenishCheckInterval = TimeSpan.FromSeconds(60),
            },
            loggerFactory: null,
            startupScript: null,
            workerFactory: () => MockWorker().Object,
            snapshotCapture: _ => new HashSet<string>(),
            driveSnapshotCapture: _ => new HashSet<string>(),
            functionSnapshotCapture: _ => new HashSet<string>(),
            resetProtocol: (_, _, _) => Task.CompletedTask);
        await pool.StartAsync();
        await using var _ = pool;

        using var adapter = new PooledHttpRunspace(pool, MockWorker().Object, NullLoggerFactory.Instance);

        var barrier = new SemaphoreSlim(0, 2);
        var release = new SemaphoreSlim(0, 2);
        PSPowerShell? psA = null, psB = null;

        var taskA = adapter.ExecuteThreadSafeAsync<bool>(async ps =>
        {
            psA = ps;
            barrier.Release();
            await release.WaitAsync();
            return true;
        });

        var taskB = adapter.ExecuteThreadSafeAsync<bool>(async ps =>
        {
            psB = ps;
            barrier.Release();
            await release.WaitAsync();
            return true;
        });

        // Wait for both tasks to acquire their leases.
        await barrier.WaitAsync(TimeSpan.FromSeconds(5));
        await barrier.WaitAsync(TimeSpan.FromSeconds(5));

        // Each concurrent call received its own PS instance — state cannot cross over.
        Assert.NotNull(psA);
        Assert.NotNull(psB);
        Assert.NotSame(psA, psB);

        release.Release(2);
        await Task.WhenAll(taskA, taskB);
    }

    // ─── Pool exhaustion / recovery ──────────────────────────────────────────────

    [Fact]
    public async Task Exhaustion_AllWorkersBusy_AcquisitionTimesOut_ThenReleaseAllowsSuccess()
    {
        var shortTimeout = new RunspacePoolOptions
        {
            MinPoolSize = 1,
            MaxPoolSize = 1,
            EagerWarmCount = 1,
            AcquisitionTimeout = TimeSpan.FromMilliseconds(200),
            IdleTtl = TimeSpan.FromSeconds(300),
            SweepInterval = TimeSpan.FromSeconds(60),
            StopTimeout = TimeSpan.FromSeconds(2),
            ShutdownDrainTimeout = TimeSpan.FromMilliseconds(500),
            ReplenishCheckInterval = TimeSpan.FromSeconds(60),
        };
        var pool = new StatelessRunspacePool(
            shortTimeout,
            loggerFactory: null,
            startupScript: null,
            workerFactory: () => MockWorker().Object,
            snapshotCapture: _ => new HashSet<string>(),
            driveSnapshotCapture: _ => new HashSet<string>(),
            functionSnapshotCapture: _ => new HashSet<string>(),
            resetProtocol: (_, _, _) => Task.CompletedTask);
        await pool.StartAsync();
        await using var _ = pool;

        using var adapter = new PooledHttpRunspace(pool, MockWorker().Object, NullLoggerFactory.Instance);

        // Gate that holds the only worker busy.
        var holdGate = new SemaphoreSlim(0, 1);
        var workerAcquired = new SemaphoreSlim(0, 1);

        var holdingTask = adapter.ExecuteThreadSafeAsync<bool>(async ps =>
        {
            workerAcquired.Release();
            await holdGate.WaitAsync();
            return true;
        });

        // Wait until the worker is actually held.
        await workerAcquired.WaitAsync(TimeSpan.FromSeconds(5));

        // Pool is exhausted: second call must time out, not deadlock.
        await Assert.ThrowsAsync<TimeoutException>(() =>
            adapter.ExecuteThreadSafeAsync<bool>(ps => Task.FromResult(true)));

        // Release the held worker.
        holdGate.Release();
        await holdingTask;

        // After release a new call must succeed.
        var result = await adapter.ExecuteThreadSafeAsync<bool>(ps => Task.FromResult(true));
        Assert.True(result);
    }

    // ─── Dispose ────────────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var adapter = new PooledHttpRunspace(_pool, MockWorker().Object, NullLoggerFactory.Instance);
        adapter.Dispose();
        adapter.Dispose(); // must not throw
    }

    [Fact]
    public void Instance_AfterDispose_ThrowsObjectDisposedException()
    {
        var adapter = new PooledHttpRunspace(_pool, MockWorker().Object, NullLoggerFactory.Instance);
        adapter.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _ = adapter.Instance);
    }

    [Fact]
    public void ExecuteThreadSafe_AfterDispose_ThrowsObjectDisposedException()
    {
        var adapter = new PooledHttpRunspace(_pool, MockWorker().Object, NullLoggerFactory.Instance);
        adapter.Dispose();
        Assert.Throws<ObjectDisposedException>(() =>
            adapter.ExecuteThreadSafe(ps => 0));
    }

    [Fact]
    public async Task ExecuteThreadSafeAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var adapter = new PooledHttpRunspace(_pool, MockWorker().Object, NullLoggerFactory.Instance);
        adapter.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            adapter.ExecuteThreadSafeAsync(ps => Task.FromResult(0)));
    }

    // ─── Constructor guards ──────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullPool_ThrowsArgumentNullException()
    {
        // Both public and internal constructors enforce non-null pool.
        Assert.Throws<ArgumentNullException>(() =>
            new PooledHttpRunspace(null!, (string?)null, NullLoggerFactory.Instance));
        Assert.Throws<ArgumentNullException>(() =>
            new PooledHttpRunspace(null!, MockWorker().Object, NullLoggerFactory.Instance));
    }

    [Fact]
    public void Constructor_NullLoggerFactory_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PooledHttpRunspace(_pool, (string?)null, null!));
        Assert.Throws<ArgumentNullException>(() =>
            new PooledHttpRunspace(_pool, MockWorker().Object, null!));
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    private static async Task WaitForStatsAsync(
        StatelessRunspacePool pool,
        Func<RunspacePoolStats, bool> condition,
        TimeSpan? timeout = null)
    {
        var deadline = timeout ?? TimeSpan.FromSeconds(5);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < deadline)
        {
            if (condition(pool.GetStats())) return;
            await Task.Delay(10);
        }
        throw new TimeoutException($"WaitForStatsAsync: condition not met within {deadline}.");
    }
}
