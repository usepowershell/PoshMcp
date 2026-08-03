using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.Pool;
using Xunit;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.Tests.Unit.Pool;

[Trait("Category", "Unit")]
public sealed class RunspacePoolContractsTests
{
    // ─── RunspacePoolOptions — default values ───────────────────────────────────

    [Fact]
    public void RunspacePoolOptions_Defaults_MinPoolSize_Is2()
    {
        Assert.Equal(2, new RunspacePoolOptions().MinPoolSize);
    }

    [Fact]
    public void RunspacePoolOptions_Defaults_MaxPoolSize_Is16()
    {
        Assert.Equal(16, new RunspacePoolOptions().MaxPoolSize);
    }

    [Fact]
    public void RunspacePoolOptions_Defaults_EagerWarmCount_Is2()
    {
        Assert.Equal(2, new RunspacePoolOptions().EagerWarmCount);
    }

    [Fact]
    public void RunspacePoolOptions_Defaults_AcquisitionTimeout_Is15Seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(15), new RunspacePoolOptions().AcquisitionTimeout);
    }

    [Fact]
    public void RunspacePoolOptions_Defaults_IdleTtl_Is300Seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(300), new RunspacePoolOptions().IdleTtl);
    }

    [Fact]
    public void RunspacePoolOptions_Defaults_SweepInterval_Is30Seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), new RunspacePoolOptions().SweepInterval);
    }

    [Fact]
    public void RunspacePoolOptions_Defaults_StopTimeout_Is5Seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), new RunspacePoolOptions().StopTimeout);
    }

    [Fact]
    public void RunspacePoolOptions_Defaults_ShutdownDrainTimeout_Is30Seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), new RunspacePoolOptions().ShutdownDrainTimeout);
    }

    [Fact]
    public void RunspacePoolOptions_Defaults_ReplenishCheckInterval_Is5Seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), new RunspacePoolOptions().ReplenishCheckInterval);
    }

    [Fact]
    public void RunspacePoolOptions_Defaults_Validate_ReturnsNoErrors()
    {
        Assert.Empty(new RunspacePoolOptions().Validate());
    }

    // ─── RunspacePoolOptions — validation ───────────────────────────────────────

    [Fact]
    public void RunspacePoolOptions_Validate_MinPoolSizeZero_ReturnsError()
    {
        var opts = new RunspacePoolOptions { MinPoolSize = 0 };
        Assert.NotEmpty(opts.Validate());
    }

    [Fact]
    public void RunspacePoolOptions_Validate_MinPoolSizeNegative_ReturnsError()
    {
        var opts = new RunspacePoolOptions { MinPoolSize = -1 };
        Assert.NotEmpty(opts.Validate());
    }

    [Fact]
    public void RunspacePoolOptions_Validate_MaxPoolSizeLessThanMin_ReturnsError()
    {
        var opts = new RunspacePoolOptions { MinPoolSize = 4, MaxPoolSize = 2 };
        Assert.NotEmpty(opts.Validate());
    }

    [Fact]
    public void RunspacePoolOptions_Validate_EagerWarmCountExceedsMax_ReturnsError()
    {
        var opts = new RunspacePoolOptions { MaxPoolSize = 4, EagerWarmCount = 8 };
        Assert.NotEmpty(opts.Validate());
    }

    [Fact]
    public void RunspacePoolOptions_Validate_EagerWarmCountNegative_ReturnsError()
    {
        var opts = new RunspacePoolOptions { EagerWarmCount = -1 };
        Assert.NotEmpty(opts.Validate());
    }

    [Fact]
    public void RunspacePoolOptions_Validate_AcquisitionTimeoutNegative_ReturnsError()
    {
        var opts = new RunspacePoolOptions { AcquisitionTimeout = TimeSpan.FromSeconds(-1) };
        Assert.NotEmpty(opts.Validate());
    }

    [Fact]
    public void RunspacePoolOptions_Validate_AcquisitionTimeoutZero_IsValid_InstantFail()
    {
        // TimeSpan.Zero means instant fail — valid sentinel value
        var opts = new RunspacePoolOptions { AcquisitionTimeout = TimeSpan.Zero };
        Assert.Empty(opts.Validate());
    }

    [Theory]
    [InlineData(nameof(RunspacePoolOptions.IdleTtl))]
    [InlineData(nameof(RunspacePoolOptions.SweepInterval))]
    [InlineData(nameof(RunspacePoolOptions.StopTimeout))]
    [InlineData(nameof(RunspacePoolOptions.ShutdownDrainTimeout))]
    [InlineData(nameof(RunspacePoolOptions.ReplenishCheckInterval))]
    public void RunspacePoolOptions_Validate_IntervalSetToZero_ReturnsError(string propertyName)
    {
        var opts = new RunspacePoolOptions();
        var prop = typeof(RunspacePoolOptions).GetProperty(propertyName)!;
        prop.SetValue(opts, TimeSpan.Zero);
        Assert.NotEmpty(opts.Validate());
    }

    [Theory]
    [InlineData(nameof(RunspacePoolOptions.IdleTtl))]
    [InlineData(nameof(RunspacePoolOptions.SweepInterval))]
    [InlineData(nameof(RunspacePoolOptions.StopTimeout))]
    [InlineData(nameof(RunspacePoolOptions.ShutdownDrainTimeout))]
    [InlineData(nameof(RunspacePoolOptions.ReplenishCheckInterval))]
    public void RunspacePoolOptions_Validate_IntervalSetToNegative_ReturnsError(string propertyName)
    {
        var opts = new RunspacePoolOptions();
        var prop = typeof(RunspacePoolOptions).GetProperty(propertyName)!;
        prop.SetValue(opts, TimeSpan.FromSeconds(-1));
        Assert.NotEmpty(opts.Validate());
    }

    // ─── RunspaceWorker — initial state ─────────────────────────────────────────

    [Fact]
    public void RunspaceWorker_InitialState_IsCreating()
    {
        using var worker = CreateWorker();
        Assert.Equal(RunspaceWorkerState.Creating, worker.State);
    }

    [Fact]
    public void RunspaceWorker_CreatedAt_IsRecordedAtConstruction()
    {
        var before = DateTimeOffset.UtcNow;
        using var worker = CreateWorker();
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(worker.CreatedAt, before, after);
    }

    [Fact]
    public void RunspaceWorker_LastLeaseCompletedAt_IsNullBeforeFirstCycle()
    {
        using var worker = CreateWorker();
        Assert.Null(worker.LastLeaseCompletedAt);
    }

    // ─── RunspaceWorker — valid state transitions ────────────────────────────────

    [Theory]
    [InlineData(RunspaceWorkerState.Creating,  RunspaceWorkerState.Warm)]
    [InlineData(RunspaceWorkerState.Creating,  RunspaceWorkerState.Evicted)]
    [InlineData(RunspaceWorkerState.Warm,      RunspaceWorkerState.Leased)]
    [InlineData(RunspaceWorkerState.Warm,      RunspaceWorkerState.Evicted)]
    [InlineData(RunspaceWorkerState.Leased,    RunspaceWorkerState.Resetting)]
    [InlineData(RunspaceWorkerState.Leased,    RunspaceWorkerState.Evicted)]
    [InlineData(RunspaceWorkerState.Resetting, RunspaceWorkerState.Warm)]
    [InlineData(RunspaceWorkerState.Resetting, RunspaceWorkerState.Evicted)]
    [InlineData(RunspaceWorkerState.Evicted,   RunspaceWorkerState.Disposed)]
    public void RunspaceWorker_ValidTransition_Succeeds(RunspaceWorkerState from, RunspaceWorkerState to)
    {
        using var worker = CreateWorkerInState(from);
        Assert.True(worker.TryTransitionTo(to));
        Assert.Equal(to, worker.State);
    }

    [Fact]
    public void RunspaceWorker_ResettingToWarm_UpdatesLastLeaseCompletedAt()
    {
        using var worker = CreateWorkerInState(RunspaceWorkerState.Resetting);
        var before = DateTimeOffset.UtcNow;

        Assert.True(worker.TryTransitionTo(RunspaceWorkerState.Warm));

        var after = DateTimeOffset.UtcNow;
        Assert.NotNull(worker.LastLeaseCompletedAt);
        Assert.InRange(worker.LastLeaseCompletedAt!.Value, before, after);
    }

    [Fact]
    public void RunspaceWorker_NonResettingToWarm_DoesNotUpdateLastLeaseCompletedAt()
    {
        // Creating → Warm should NOT set LastLeaseCompletedAt (no lease cycle occurred)
        using var worker = CreateWorker();
        Assert.True(worker.TryTransitionTo(RunspaceWorkerState.Warm));
        Assert.Null(worker.LastLeaseCompletedAt);
    }

    // ─── RunspaceWorker — invalid state transitions ──────────────────────────────

    [Theory]
    [InlineData(RunspaceWorkerState.Creating,  RunspaceWorkerState.Leased)]
    [InlineData(RunspaceWorkerState.Creating,  RunspaceWorkerState.Resetting)]
    [InlineData(RunspaceWorkerState.Creating,  RunspaceWorkerState.Disposed)]
    [InlineData(RunspaceWorkerState.Warm,      RunspaceWorkerState.Creating)]
    [InlineData(RunspaceWorkerState.Warm,      RunspaceWorkerState.Resetting)]
    [InlineData(RunspaceWorkerState.Warm,      RunspaceWorkerState.Disposed)]
    [InlineData(RunspaceWorkerState.Leased,    RunspaceWorkerState.Creating)]
    [InlineData(RunspaceWorkerState.Leased,    RunspaceWorkerState.Warm)]
    [InlineData(RunspaceWorkerState.Leased,    RunspaceWorkerState.Disposed)]
    [InlineData(RunspaceWorkerState.Resetting, RunspaceWorkerState.Creating)]
    [InlineData(RunspaceWorkerState.Resetting, RunspaceWorkerState.Leased)]
    [InlineData(RunspaceWorkerState.Resetting, RunspaceWorkerState.Disposed)]
    [InlineData(RunspaceWorkerState.Evicted,   RunspaceWorkerState.Creating)]
    [InlineData(RunspaceWorkerState.Evicted,   RunspaceWorkerState.Warm)]
    [InlineData(RunspaceWorkerState.Evicted,   RunspaceWorkerState.Leased)]
    [InlineData(RunspaceWorkerState.Evicted,   RunspaceWorkerState.Resetting)]
    [InlineData(RunspaceWorkerState.Disposed,  RunspaceWorkerState.Creating)]
    [InlineData(RunspaceWorkerState.Disposed,  RunspaceWorkerState.Warm)]
    [InlineData(RunspaceWorkerState.Disposed,  RunspaceWorkerState.Evicted)]
    public void RunspaceWorker_InvalidTransition_ReturnsFalse(RunspaceWorkerState from, RunspaceWorkerState to)
    {
        using var worker = CreateWorkerInState(from);
        Assert.False(worker.TryTransitionTo(to));
    }

    [Fact]
    public void RunspaceWorker_SameStateTransition_ReturnsFalse()
    {
        using var worker = CreateWorkerInState(RunspaceWorkerState.Warm);
        Assert.False(worker.TryTransitionTo(RunspaceWorkerState.Warm));
    }

    // ─── RunspaceWorker — Dispose ────────────────────────────────────────────────

    [Fact]
    public void RunspaceWorker_Dispose_TransitionsToDisposed()
    {
        var worker = CreateWorkerInState(RunspaceWorkerState.Evicted);
        worker.Dispose();
        Assert.Equal(RunspaceWorkerState.Disposed, worker.State);
    }

    [Fact]
    public void RunspaceWorker_Dispose_IsIdempotent()
    {
        var mockRunspace = new Mock<IPowerShellRunspace>();
        var worker = new RunspaceWorker(mockRunspace.Object);
        worker.Dispose();
        worker.Dispose();

        mockRunspace.Verify(r => r.Dispose(), Times.Once);
    }

    // ─── RunspaceLease — disposal contract ──────────────────────────────────────

    [Fact]
    public void RunspaceLease_Dispose_InvokesCallbackExactlyOnce()
    {
        int callCount = 0;
        using var worker = CreateWorkerInState(RunspaceWorkerState.Leased);
        var lease = new RunspaceLease(worker, (_, _) =>
        {
            Interlocked.Increment(ref callCount);
            return ValueTask.CompletedTask;
        });

        lease.Dispose();

        Assert.Equal(1, callCount);
    }

    [Fact]
    public void RunspaceLease_DoubleDispose_CallbackInvokedOnlyOnce()
    {
        int callCount = 0;
        using var worker = CreateWorkerInState(RunspaceWorkerState.Leased);
        var lease = new RunspaceLease(worker, (_, _) =>
        {
            Interlocked.Increment(ref callCount);
            return ValueTask.CompletedTask;
        });

        lease.Dispose();
        lease.Dispose();

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task RunspaceLease_DisposeAsync_InvokesCallbackExactlyOnce()
    {
        int callCount = 0;
        using var worker = CreateWorkerInState(RunspaceWorkerState.Leased);
        var lease = new RunspaceLease(worker, (_, _) =>
        {
            Interlocked.Increment(ref callCount);
            return ValueTask.CompletedTask;
        });

        await lease.DisposeAsync();

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task RunspaceLease_DoubleDisposeAsync_CallbackInvokedOnlyOnce()
    {
        int callCount = 0;
        using var worker = CreateWorkerInState(RunspaceWorkerState.Leased);
        var lease = new RunspaceLease(worker, (_, _) =>
        {
            Interlocked.Increment(ref callCount);
            return ValueTask.CompletedTask;
        });

        await lease.DisposeAsync();
        await lease.DisposeAsync();

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task RunspaceLease_SyncThenAsyncDispose_CallbackInvokedOnlyOnce()
    {
        int callCount = 0;
        using var worker = CreateWorkerInState(RunspaceWorkerState.Leased);
        var lease = new RunspaceLease(worker, (_, _) =>
        {
            Interlocked.Increment(ref callCount);
            return ValueTask.CompletedTask;
        });

        lease.Dispose();
        await lease.DisposeAsync();

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task RunspaceLease_AsyncThenSyncDispose_CallbackInvokedOnlyOnce()
    {
        int callCount = 0;
        using var worker = CreateWorkerInState(RunspaceWorkerState.Leased);
        var lease = new RunspaceLease(worker, (_, _) =>
        {
            Interlocked.Increment(ref callCount);
            return ValueTask.CompletedTask;
        });

        await lease.DisposeAsync();
        lease.Dispose();

        Assert.Equal(1, callCount);
    }

    [Fact]
    public void RunspaceLease_Dispose_PassesCorrectWorkerToCallback()
    {
        RunspaceWorker? received = null;
        using var worker = CreateWorkerInState(RunspaceWorkerState.Leased);
        var lease = new RunspaceLease(worker, (w, _) =>
        {
            received = w;
            return ValueTask.CompletedTask;
        });

        lease.Dispose();

        Assert.Same(worker, received);
    }

    [Fact]
    public void RunspaceLease_AccessPowerShellAfterDispose_ThrowsObjectDisposedException()
    {
        using var worker = CreateWorkerInState(RunspaceWorkerState.Leased);
        var lease = new RunspaceLease(worker, (_, _) => ValueTask.CompletedTask);

        lease.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = lease.PowerShell);
    }

    // ─── RunspaceLease — eviction flag ──────────────────────────────────────────

    [Fact]
    public void RunspaceLease_WithoutRequestEviction_CallbackReceivesFalse()
    {
        bool? evictFlag = null;
        using var worker = CreateWorkerInState(RunspaceWorkerState.Leased);
        var lease = new RunspaceLease(worker, (_, evict) =>
        {
            evictFlag = evict;
            return ValueTask.CompletedTask;
        });

        lease.Dispose();

        Assert.False(evictFlag);
    }

    [Fact]
    public void RunspaceLease_AfterRequestEviction_CallbackReceivesTrue()
    {
        bool? evictFlag = null;
        using var worker = CreateWorkerInState(RunspaceWorkerState.Leased);
        var lease = new RunspaceLease(worker, (_, evict) =>
        {
            evictFlag = evict;
            return ValueTask.CompletedTask;
        });

        lease.RequestEviction();
        lease.Dispose();

        Assert.True(evictFlag);
    }

    [Fact]
    public async Task RunspaceLease_RequestEviction_PropagatesViaDisposeAsync()
    {
        bool? evictFlag = null;
        using var worker = CreateWorkerInState(RunspaceWorkerState.Leased);
        var lease = new RunspaceLease(worker, (_, evict) =>
        {
            evictFlag = evict;
            return ValueTask.CompletedTask;
        });

        lease.RequestEviction();
        await lease.DisposeAsync();

        Assert.True(evictFlag);
    }

    // ─── RunspacePoolStats — shape ───────────────────────────────────────────────

    [Fact]
    public void RunspacePoolStats_Record_CanBeConstructedWithAllFields()
    {
        var stats = new RunspacePoolStats(
            MinPoolSize: 2,
            MaxPoolSize: 16,
            WarmWorkers: 2,
            LeasedWorkers: 1,
            ResettingWorkers: 0,
            TotalWorkers: 3);

        Assert.Equal(2, stats.MinPoolSize);
        Assert.Equal(16, stats.MaxPoolSize);
        Assert.Equal(2, stats.WarmWorkers);
        Assert.Equal(1, stats.LeasedWorkers);
        Assert.Equal(0, stats.ResettingWorkers);
        Assert.Equal(3, stats.TotalWorkers);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    private static RunspaceWorker CreateWorker()
    {
        var mockRunspace = new Mock<IPowerShellRunspace>();
        mockRunspace.Setup(r => r.Instance).Returns((PSPowerShell)null!);
        return new RunspaceWorker(mockRunspace.Object);
    }

    private static RunspaceWorker CreateWorkerInState(RunspaceWorkerState target)
    {
        var mockRunspace = new Mock<IPowerShellRunspace>();
        mockRunspace.Setup(r => r.Instance).Returns((PSPowerShell)null!);
        var worker = new RunspaceWorker(mockRunspace.Object);

        // Drive the state machine from Creating to the desired state
        foreach (var (from, to) in PathToState(target))
        {
            if (!worker.TryTransitionTo(to))
                throw new InvalidOperationException(
                    $"Could not drive worker from {from} to {to} while setting up state {target}.");
        }

        return worker;
    }

    // Returns the minimal sequence of (from, to) transitions to reach the target state.
    private static (RunspaceWorkerState From, RunspaceWorkerState To)[] PathToState(RunspaceWorkerState target) => target switch
    {
        RunspaceWorkerState.Creating  => [],
        RunspaceWorkerState.Warm      => [(RunspaceWorkerState.Creating,  RunspaceWorkerState.Warm)],
        RunspaceWorkerState.Leased    => [(RunspaceWorkerState.Creating,  RunspaceWorkerState.Warm),
                                          (RunspaceWorkerState.Warm,      RunspaceWorkerState.Leased)],
        RunspaceWorkerState.Resetting => [(RunspaceWorkerState.Creating,  RunspaceWorkerState.Warm),
                                          (RunspaceWorkerState.Warm,      RunspaceWorkerState.Leased),
                                          (RunspaceWorkerState.Leased,    RunspaceWorkerState.Resetting)],
        RunspaceWorkerState.Evicted   => [(RunspaceWorkerState.Creating,  RunspaceWorkerState.Warm),
                                          (RunspaceWorkerState.Warm,      RunspaceWorkerState.Evicted)],
        RunspaceWorkerState.Disposed  => [(RunspaceWorkerState.Creating,  RunspaceWorkerState.Warm),
                                          (RunspaceWorkerState.Warm,      RunspaceWorkerState.Evicted),
                                          (RunspaceWorkerState.Evicted,   RunspaceWorkerState.Disposed)],
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null)
    };
}
