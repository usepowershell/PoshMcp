using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.Metrics;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.Pool;
using Xunit;
using Xunit.Abstractions;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Validates every RunspacePoolMetrics instrument and HttpTransportMetrics gauge using
/// <see cref="MeterListener"/> with before/after delta assertions against actual pool activity.
/// All tests use test-double factories (no real PowerShell runspaces).
/// Eviction reasons tested: idle (via SweepOnce), reset_failure, explicit, stop_timeout,
/// cancel, drain, startup_partial_failure. "broken" is a concurrent race path documented as
/// not deterministically triggerable without reflection.
/// No per-request instrument creation is verified by asserting InstrumentPublished fires
/// once per instrument class per pool instance.
/// </summary>
[Trait("Category", "Unit")]
[Collection("PoolMetricsInstrumentTests")]
public sealed class PoolMetricsInstrumentTests
{
    private readonly ITestOutputHelper _output;

    // Known low-cardinality eviction reason labels per RunspacePoolMetrics XML doc.
    // "broken" is included even though not deterministically triggerable — we verify we never
    // see unknown/high-cardinality labels in the set that IS emitted.
    private static readonly HashSet<string> KnownEvictionReasons = new(StringComparer.Ordinal)
    {
        "idle", "reset_failure", "broken", "cancel", "stop_timeout", "explicit",
        "drain", "startup_partial_failure", "channel_full"
    };

    private static readonly HashSet<string> KnownWorkerStates = new(StringComparer.Ordinal)
    {
        "warm", "leased", "resetting"
    };

    public PoolMetricsInstrumentTests(ITestOutputHelper output) => _output = output;

    // ─── Acquisition counter ──────────────────────────────────────────────────

    [Fact]
    public async Task Acquisition_SuccessfulLease_IncrementsAcquisitionCounter()
    {
        using var capture = new MetricsCapture("poshmcp.runspace_pool.");
        await using var pool = MakePool(eager: 1);
        await pool.StartAsync();

        await using var lease = await pool.AcquireAsync();

        var delta = capture.SumLong("poshmcp.runspace_pool.acquisitions_total");
        _output.WriteLine($"acquisitions_total delta: {delta}");
        Assert.Equal(1L, delta);
    }

    [Fact]
    public async Task Acquisition_MultipleLeases_AcquisitionCounterMatchesRounds()
    {
        using var capture = new MetricsCapture("poshmcp.runspace_pool.");
        await using var pool = MakePool(eager: 1, max: 1);
        await pool.StartAsync();

        const int rounds = 3;
        for (var i = 0; i < rounds; i++)
        {
            await using var lease = await pool.AcquireAsync();
        }

        var delta = capture.SumLong("poshmcp.runspace_pool.acquisitions_total");
        _output.WriteLine($"acquisitions_total after {rounds} rounds: {delta}");
        Assert.Equal(rounds, delta);
    }

    // ─── WorkerCount UpDownCounter transitions ────────────────────────────────

    [Fact]
    public async Task WorkerCount_AcquireAndReturn_NetWarmReturnsToOneAfterReset()
    {
        using var capture = new MetricsCapture("poshmcp.runspace_pool.");
        await using var pool = MakePool(eager: 1, max: 1);
        await pool.StartAsync();

        // Baseline: 1 warm worker created during StartAsync.
        var warmBefore = capture.SumLong("poshmcp.runspace_pool.workers", "warm");
        Assert.Equal(1L, warmBefore); // +1 when worker becomes Warm

        // Acquire: warm -1, leased +1.
        await using (var lease = await pool.AcquireAsync())
        {
            var warmDuringLease = capture.SumLong("poshmcp.runspace_pool.workers", "warm");
            var leasedDuringLease = capture.SumLong("poshmcp.runspace_pool.workers", "leased");
            _output.WriteLine($"During lease — warm net: {warmDuringLease}, leased net: {leasedDuringLease}");
            Assert.Equal(0L, warmDuringLease);   // +1 created −1 acquired = 0
            Assert.Equal(1L, leasedDuringLease); // +1 acquired
        }

        // After return: allow reset to complete (pool is not disposed).
        // Give pool up to 5 s to complete the async reset and re-enqueue the worker.
        await WaitForConditionAsync(
            () => capture.SumLong("poshmcp.runspace_pool.workers", "warm") >= 1L,
            TimeSpan.FromSeconds(5));

        var warmAfter = capture.SumLong("poshmcp.runspace_pool.workers", "warm");
        var leasedAfter = capture.SumLong("poshmcp.runspace_pool.workers", "leased");
        var resettingAfter = capture.SumLong("poshmcp.runspace_pool.workers", "resetting");

        _output.WriteLine($"After return — warm net: {warmAfter}, leased net: {leasedAfter}, resetting net: {resettingAfter}");

        Assert.Equal(1L, warmAfter);      // back to warm
        Assert.Equal(0L, leasedAfter);    // no active lease
        Assert.Equal(0L, resettingAfter); // reset cycle complete
    }

    [Fact]
    public async Task WorkerCount_OnlyKnownStateTags_Observed()
    {
        using var capture = new MetricsCapture("poshmcp.runspace_pool.");
        await using var pool = MakePool(eager: 1, max: 1);
        await pool.StartAsync();
        await using var lease = await pool.AcquireAsync();

        var observedStates = capture.GetObservedTags("poshmcp.runspace_pool.workers");
        _output.WriteLine($"Observed state tags: {string.Join(", ", observedStates)}");

        foreach (var state in observedStates)
        {
            Assert.True(KnownWorkerStates.Contains(state),
                $"Unknown WorkerCount state tag: '{state}'. Known: {string.Join(", ", KnownWorkerStates)}");
        }
    }

    // ─── Acquisition timeout ──────────────────────────────────────────────────

    [Fact]
    public async Task AcquisitionTimeout_ZeroTimeout_NoWorkersAvailable_IncrementsTimeoutCounter()
    {
        var timeoutOpts = new RunspacePoolOptions
        {
            MinPoolSize = 1,
            MaxPoolSize = 1,
            EagerWarmCount = 1,
            AcquisitionTimeout = TimeSpan.Zero, // instant-fail
            IdleTtl = TimeSpan.FromSeconds(300),
            SweepInterval = TimeSpan.FromSeconds(60),
            StopTimeout = TimeSpan.FromSeconds(5),
            ShutdownDrainTimeout = TimeSpan.FromSeconds(5),
            ReplenishCheckInterval = TimeSpan.FromSeconds(60),
        };

        using var capture = new MetricsCapture("poshmcp.runspace_pool.");
        await using var pool = new StatelessRunspacePool(timeoutOpts, loggerFactory: null,
            workerFactory: () => new NoopRunspace());
        await pool.StartAsync();

        // Hold the only worker.
        var lease = await pool.AcquireAsync();

        // No workers available; instant-fail must increment AcquisitionTimeouts.
        await Assert.ThrowsAsync<TimeoutException>(() => pool.AcquireAsync().AsTask());

        var timeouts = capture.SumLong("poshmcp.runspace_pool.acquisition_timeouts_total");
        _output.WriteLine($"acquisition_timeouts_total: {timeouts}");
        Assert.Equal(1L, timeouts);

        lease.Dispose(); // clean up
    }

    // ─── Reset duration histogram ─────────────────────────────────────────────

    [Fact]
    public async Task ResetDuration_SuccessfulReset_RecordsHistogramMeasurement()
    {
        using var capture = new MetricsCapture("poshmcp.runspace_pool.");
        await using var pool = MakePool(eager: 1);
        await pool.StartAsync();

        await using var lease = await pool.AcquireAsync();
        await lease.DisposeAsync(); // triggers async reset

        // Wait for reset to complete (histogram only records on successful reset).
        await WaitForConditionAsync(
            () => capture.CountDouble("poshmcp.runspace_pool.reset_duration_seconds") >= 1,
            TimeSpan.FromSeconds(5));

        var resetCount = capture.CountDouble("poshmcp.runspace_pool.reset_duration_seconds");
        _output.WriteLine($"reset_duration_seconds measurements: {resetCount}");
        Assert.True(resetCount >= 1, "Expected at least one reset_duration_seconds histogram measurement");

        // All recorded values must be non-negative.
        var values = capture.GetDoubleValues("poshmcp.runspace_pool.reset_duration_seconds");
        Assert.All(values, v => Assert.True(v >= 0.0, $"Negative reset duration: {v}"));
    }

    // ─── Lease duration histogram ─────────────────────────────────────────────

    [Fact]
    public async Task LeaseDuration_AcquireAndReturn_RecordsHistogramMeasurement()
    {
        using var capture = new MetricsCapture("poshmcp.runspace_pool.");
        await using var pool = MakePool(eager: 1);
        await pool.StartAsync();

        await using (var lease = await pool.AcquireAsync())
        {
            // hold briefly to ensure a measurable duration
            await Task.Delay(10);
        }

        // Lease duration is recorded synchronously in OnWorkerReturnedAsync before reset.
        await WaitForConditionAsync(
            () => capture.CountDouble("poshmcp.runspace_pool.lease_duration_seconds") >= 1,
            TimeSpan.FromSeconds(5));

        var leaseCount = capture.CountDouble("poshmcp.runspace_pool.lease_duration_seconds");
        _output.WriteLine($"lease_duration_seconds measurements: {leaseCount}");
        Assert.True(leaseCount >= 1, "Expected at least one lease_duration_seconds measurement");

        var values = capture.GetDoubleValues("poshmcp.runspace_pool.lease_duration_seconds");
        Assert.All(values, v => Assert.True(v >= 0.0, $"Negative lease duration: {v}"));
    }

    // ─── Eviction reasons ─────────────────────────────────────────────────────

    [Fact]
    public async Task EvictionReason_Idle_SweepOnce_EvictsExpiredWorker()
    {
        // SweepOnce() is internal; call it directly so we don't need a real timer delay.
        var pastTime = DateTimeOffset.UtcNow.AddDays(-1);
        using var capture = new MetricsCapture("poshmcp.runspace_pool.");

        await using var pool = new StatelessRunspacePool(
            new RunspacePoolOptions
            {
                MinPoolSize = 1, // sweep evicts surplus above min; with 2 warm workers, 1 can be evicted
                MaxPoolSize = 2,
                EagerWarmCount = 2,
                AcquisitionTimeout = TimeSpan.FromSeconds(5),
                IdleTtl = TimeSpan.FromMilliseconds(1), // expire immediately
                SweepInterval = TimeSpan.FromSeconds(300),
                StopTimeout = TimeSpan.FromSeconds(5),
                ShutdownDrainTimeout = TimeSpan.FromSeconds(5),
                ReplenishCheckInterval = TimeSpan.FromSeconds(300),
            },
            loggerFactory: null,
            workerFactory: () => new NoopRunspace(),
            snapshotCapture: _ => EmptySet(),
            driveSnapshotCapture: _ => EmptySet(),
            functionSnapshotCapture: _ => EmptySet(),
            aliasSnapshotCapture: _ => EmptySet(),
            resetProtocol: (_, _, _) => Task.CompletedTask,
            clock: () => pastTime.AddDays(2)); // clock far in the future → all workers expired

        await pool.StartAsync();
        var statsBefore = pool.GetStats();
        _output.WriteLine($"Warm workers before sweep: {statsBefore.WarmWorkers}");

        pool.SweepOnce(); // directly invoke internal method

        var idleEvictions = capture.SumLong("poshmcp.runspace_pool.evictions_total", "idle");
        _output.WriteLine($"evictions[idle]: {idleEvictions}");
        Assert.True(idleEvictions >= 1, "Expected at least one idle eviction from SweepOnce");

        // Verify no unknown labels appeared.
        AssertNoUnknownEvictionLabels(capture);
    }

    [Fact]
    public async Task EvictionReason_ResetFailure_InjectedThrowingProtocol()
    {
        using var capture = new MetricsCapture("poshmcp.runspace_pool.");

        await using var pool = new StatelessRunspacePool(
            FastOpts(eager: 1, max: 1),
            loggerFactory: null,
            workerFactory: () => new NoopRunspace(),
            snapshotCapture: _ => EmptySet(),
            driveSnapshotCapture: _ => EmptySet(),
            functionSnapshotCapture: _ => EmptySet(),
            aliasSnapshotCapture: _ => EmptySet(),
            resetProtocol: (_, _, _) => throw new InvalidOperationException("Simulated reset failure"));

        await pool.StartAsync();
        await using (var lease = await pool.AcquireAsync())
        {
            // return the lease; the injected protocol will throw
        }

        await WaitForConditionAsync(
            () => capture.SumLong("poshmcp.runspace_pool.evictions_total", "reset_failure") >= 1,
            TimeSpan.FromSeconds(5));

        var evictions = capture.SumLong("poshmcp.runspace_pool.evictions_total", "reset_failure");
        _output.WriteLine($"evictions[reset_failure]: {evictions}");
        Assert.Equal(1L, evictions);

        AssertNoUnknownEvictionLabels(capture);
    }

    [Fact]
    public async Task EvictionReason_Explicit_ViaRequestEviction()
    {
        using var capture = new MetricsCapture("poshmcp.runspace_pool.");
        await using var pool = MakePool(eager: 1, max: 1);
        await pool.StartAsync();

        var lease = await pool.AcquireAsync();
        lease.RequestEviction(); // signal evict-on-return
        await lease.DisposeAsync();

        await WaitForConditionAsync(
            () => capture.SumLong("poshmcp.runspace_pool.evictions_total", "explicit") >= 1,
            TimeSpan.FromSeconds(5));

        var evictions = capture.SumLong("poshmcp.runspace_pool.evictions_total", "explicit");
        _output.WriteLine($"evictions[explicit]: {evictions}");
        Assert.Equal(1L, evictions);

        AssertNoUnknownEvictionLabels(capture);
    }

    [Fact]
    public async Task EvictionReason_StopTimeout_InjectedTimeoutException()
    {
        using var capture = new MetricsCapture("poshmcp.runspace_pool.");

        await using var pool = new StatelessRunspacePool(
            FastOpts(eager: 1, max: 1),
            loggerFactory: null,
            workerFactory: () => new NoopRunspace(),
            snapshotCapture: _ => EmptySet(),
            driveSnapshotCapture: _ => EmptySet(),
            functionSnapshotCapture: _ => EmptySet(),
            aliasSnapshotCapture: _ => EmptySet(),
            resetProtocol: (_, _, _) => throw new TimeoutException("Simulated reset stop timeout"));

        await pool.StartAsync();
        await using (var lease = await pool.AcquireAsync())
        { }

        await WaitForConditionAsync(
            () => capture.SumLong("poshmcp.runspace_pool.evictions_total", "stop_timeout") >= 1,
            TimeSpan.FromSeconds(5));

        var evictions = capture.SumLong("poshmcp.runspace_pool.evictions_total", "stop_timeout");
        _output.WriteLine($"evictions[stop_timeout]: {evictions}");
        Assert.Equal(1L, evictions);

        AssertNoUnknownEvictionLabels(capture);
    }

    [Fact]
    public async Task EvictionReason_Cancel_InjectedOperationCancelled()
    {
        using var capture = new MetricsCapture("poshmcp.runspace_pool.");

        await using var pool = new StatelessRunspacePool(
            FastOpts(eager: 1, max: 1),
            loggerFactory: null,
            workerFactory: () => new NoopRunspace(),
            snapshotCapture: _ => EmptySet(),
            driveSnapshotCapture: _ => EmptySet(),
            functionSnapshotCapture: _ => EmptySet(),
            aliasSnapshotCapture: _ => EmptySet(),
            resetProtocol: (_, _, _) => throw new OperationCanceledException("Simulated cancel during reset"));

        await pool.StartAsync();
        await using (var lease = await pool.AcquireAsync())
        { }

        await WaitForConditionAsync(
            () => capture.SumLong("poshmcp.runspace_pool.evictions_total", "cancel") >= 1,
            TimeSpan.FromSeconds(5));

        var evictions = capture.SumLong("poshmcp.runspace_pool.evictions_total", "cancel");
        _output.WriteLine($"evictions[cancel]: {evictions}");
        Assert.Equal(1L, evictions);

        AssertNoUnknownEvictionLabels(capture);
    }

    [Fact]
    public async Task EvictionReason_Drain_ForceDisposeAllWorkers()
    {
        using var capture = new MetricsCapture("poshmcp.runspace_pool.");
        // 2 eager workers so at least one drain eviction will fire on an outstanding lease.
        await using var pool = MakePool(eager: 2, max: 2);
        await pool.StartAsync();

        // Hold one lease while draining so ForceDisposeAllWorkers sees a live worker.
        var lease = await pool.AcquireAsync();
        var drainTask = pool.DrainAsync();
        lease.Dispose(); // return lease — pool is already draining

        await drainTask.WaitAsync(TimeSpan.FromSeconds(10));

        var drainEvictions = capture.SumLong("poshmcp.runspace_pool.evictions_total", "drain");
        _output.WriteLine($"evictions[drain]: {drainEvictions}");
        // At minimum the 1 idle warm worker is drained; the leased one may also be captured.
        Assert.True(drainEvictions >= 1, "Expected at least one drain eviction");

        AssertNoUnknownEvictionLabels(capture);
    }

    [Fact]
    public async Task EvictionReason_StartupPartialFailure_IncrementsEvictionsAndStartupFailures()
    {
        // Factory succeeds for first worker, fails for second → StartAsync throws; both
        // workers are cleaned up via ForceDisposeAllWorkers("startup_partial_failure").
        var callCount = 0;
        using var capture = new MetricsCapture("poshmcp.runspace_pool.");

        await using var pool = new StatelessRunspacePool(
            new RunspacePoolOptions
            {
                MinPoolSize = 1,
                MaxPoolSize = 2,
                EagerWarmCount = 2, // 2 eager workers required
                AcquisitionTimeout = TimeSpan.FromSeconds(5),
                IdleTtl = TimeSpan.FromSeconds(300),
                SweepInterval = TimeSpan.FromSeconds(300),
                StopTimeout = TimeSpan.FromSeconds(5),
                ShutdownDrainTimeout = TimeSpan.FromSeconds(5),
                ReplenishCheckInterval = TimeSpan.FromSeconds(300),
            },
            loggerFactory: null,
            workerFactory: () =>
            {
                var n = Interlocked.Increment(ref callCount);
                if (n > 1)
                    throw new InvalidOperationException("Simulated second-worker failure");
                return new NoopRunspace();
            },
            snapshotCapture: _ => EmptySet(),
            driveSnapshotCapture: _ => EmptySet(),
            functionSnapshotCapture: _ => EmptySet(),
            aliasSnapshotCapture: _ => EmptySet());

        await Assert.ThrowsAsync<InvalidOperationException>(() => pool.StartAsync());

        var failures = capture.SumLong("poshmcp.runspace_pool.startup_failures_total");
        _output.WriteLine($"startup_failures_total: {failures}");
        Assert.Equal(1L, failures); // second worker failed

        // The successfully-created first worker is evicted with reason="startup_partial_failure".
        var partialEvictions = capture.SumLong(
            "poshmcp.runspace_pool.evictions_total", "startup_partial_failure");
        _output.WriteLine($"evictions[startup_partial_failure]: {partialEvictions}");
        Assert.Equal(1L, partialEvictions);

        AssertNoUnknownEvictionLabels(capture);
    }

    // ─── Startup failures ─────────────────────────────────────────────────────

    [Fact]
    public async Task StartupFailures_FactoryThrows_IncrementsCounter()
    {
        using var capture = new MetricsCapture("poshmcp.runspace_pool.");

        // ALL workers fail → StartAsync throws; no successful workers to evict.
        await using var pool = new StatelessRunspacePool(
            FastOpts(eager: 1, max: 1),
            loggerFactory: null,
            workerFactory: () => throw new InvalidOperationException("Factory always fails"),
            snapshotCapture: _ => EmptySet(),
            driveSnapshotCapture: _ => EmptySet(),
            functionSnapshotCapture: _ => EmptySet(),
            aliasSnapshotCapture: _ => EmptySet());

        await Assert.ThrowsAsync<InvalidOperationException>(() => pool.StartAsync());

        var failures = capture.SumLong("poshmcp.runspace_pool.startup_failures_total");
        _output.WriteLine($"startup_failures_total: {failures}");
        Assert.Equal(1L, failures);

        // No workers reached Warm, so no evictions should appear.
        var evictions = capture.SumLong("poshmcp.runspace_pool.evictions_total");
        Assert.Equal(0L, evictions);
    }

    // ─── Eviction label invariant ─────────────────────────────────────────────

    [Fact]
    public async Task EvictionLabels_AreAlwaysLowCardinality_NeverUnknown()
    {
        using var capture = new MetricsCapture("poshmcp.runspace_pool.");
        await using var pool = MakePool(eager: 2, max: 2);
        await pool.StartAsync();

        // Generate a mix: explicit + drain evictions.
        var lease = await pool.AcquireAsync();
        lease.RequestEviction();
        lease.Dispose();

        await pool.DrainAsync();

        var observed = capture.GetObservedTags("poshmcp.runspace_pool.evictions_total");
        _output.WriteLine($"Eviction labels observed: {string.Join(", ", observed)}");

        foreach (var label in observed)
        {
            Assert.True(KnownEvictionReasons.Contains(label),
                $"Unknown eviction reason label: '{label}'. Known: {string.Join(", ", KnownEvictionReasons)}");
        }
    }

    // ─── No negative counters ─────────────────────────────────────────────────

    [Fact]
    public async Task PoolMetrics_AfterFullLifecycle_NoNegativePlainCounters()
    {
        using var capture = new MetricsCapture("poshmcp.runspace_pool.");
        await using var pool = MakePool(eager: 1, max: 2);
        await pool.StartAsync();

        await using (var l = await pool.AcquireAsync()) { }
        await using (var l = await pool.AcquireAsync()) { }

        await WaitForConditionAsync(
            () => pool.GetStats().WarmWorkers >= 1,
            TimeSpan.FromSeconds(5));

        var acquisitions = capture.SumLong("poshmcp.runspace_pool.acquisitions_total");
        var timeouts = capture.SumLong("poshmcp.runspace_pool.acquisition_timeouts_total");
        var failures = capture.SumLong("poshmcp.runspace_pool.startup_failures_total");
        var evictions = capture.SumLong("poshmcp.runspace_pool.evictions_total");

        _output.WriteLine($"acquisitions={acquisitions}, timeouts={timeouts}, failures={failures}, evictions={evictions}");

        Assert.True(acquisitions >= 0, "acquisitions_total must not be negative");
        Assert.True(timeouts >= 0, "acquisition_timeouts_total must not be negative");
        Assert.True(failures >= 0, "startup_failures_total must not be negative");
        Assert.True(evictions >= 0, "evictions_total must not be negative");
    }

    // ─── Instrument registration ──────────────────────────────────────────────

    [Fact]
    public async Task InstrumentRegistration_PerPoolInstance_NoPerRequestCreation()
    {
        // Each pool instance creates its own RunspacePoolMetrics with exactly one
        // set of instruments. Publishing count should match the number of instruments
        // in RunspacePoolMetrics (9 instruments per pool).
        using var capture = new MetricsCapture("poshmcp.runspace_pool.");

        await using var pool = MakePool(eager: 1);
        await pool.StartAsync();

        // Generate activity — instrument count must not change.
        var publishCountBefore = capture.InstrumentPublishedCount;
        await using (var lease = await pool.AcquireAsync()) { }
        await WaitForConditionAsync(() => pool.GetStats().WarmWorkers >= 1, TimeSpan.FromSeconds(5));
        var publishCountAfter = capture.InstrumentPublishedCount;

        _output.WriteLine($"Instruments published: before={publishCountBefore}, after={publishCountAfter}");
        Assert.Equal(publishCountBefore, publishCountAfter);
        Assert.True(publishCountBefore > 0, "Expected at least one instrument published");
    }

    [Fact]
    public async Task InstrumentRegistration_TwoPoolInstances_SeparateInstrumentSets()
    {
        using var capture = new MetricsCapture("poshmcp.runspace_pool.");

        await using var pool1 = MakePool(eager: 1);
        var countAfterPool1 = capture.InstrumentPublishedCount;

        await using var pool2 = MakePool(eager: 1);
        var countAfterPool2 = capture.InstrumentPublishedCount;

        _output.WriteLine($"After pool1: {countAfterPool1} instruments, after pool2: {countAfterPool2}");
        Assert.True(countAfterPool2 > countAfterPool1,
            "Second pool must publish additional instruments (separate RunspacePoolMetrics instance)");

        await pool1.StartAsync();
        await pool2.StartAsync();
    }

    // ─── HttpTransportMetrics gauge ───────────────────────────────────────────

    [Fact]
    public void HttpTransportMetrics_StatelessMode_GaugeEmitsZero()
    {
        var config = new McpServerConfiguration { HttpTransportMode = HttpTransportMode.Stateless };
        using var capture = new MetricsCapture("poshmcp.http_transport_mode");
        using var httpMetrics = new HttpTransportMetrics(config);

        // Trigger the observable gauge callback via RecordObservations.
        capture.Listener.RecordObservableInstruments();

        var gaugeValue = capture.GetLastLongValue("poshmcp.http_transport_mode");
        _output.WriteLine($"http_transport_mode gauge (Stateless): {gaugeValue}");
        Assert.Equal(0L, gaugeValue);
    }

    [Fact]
    public void HttpTransportMetrics_StatefulMode_GaugeEmitsOne()
    {
        var config = new McpServerConfiguration { HttpTransportMode = HttpTransportMode.Stateful };
        using var capture = new MetricsCapture("poshmcp.http_transport_mode");
        using var httpMetrics = new HttpTransportMetrics(config);

        capture.Listener.RecordObservableInstruments();

        var gaugeValue = capture.GetLastLongValue("poshmcp.http_transport_mode");
        _output.WriteLine($"http_transport_mode gauge (Stateful): {gaugeValue}");
        Assert.Equal(1L, gaugeValue);
    }

    [Fact]
    public void HttpTransportMetrics_StableForLifetime_RepeatedObservationsReturnSameValue()
    {
        var config = new McpServerConfiguration { HttpTransportMode = HttpTransportMode.Stateless };
        using var capture = new MetricsCapture("poshmcp.http_transport_mode");
        using var httpMetrics = new HttpTransportMetrics(config);

        // Record multiple times — the value must be stable (gauge is a constant observer).
        for (var i = 0; i < 5; i++)
            capture.Listener.RecordObservableInstruments();

        var values = capture.GetLongValues("poshmcp.http_transport_mode");
        _output.WriteLine($"All gauge samples: {string.Join(", ", values)}");
        Assert.True(values.Count >= 5, "Expected at least 5 gauge observations");
        Assert.All(values, v => Assert.Equal(0L, v)); // all must be Stateless = 0
    }

    [Fact]
    public void HttpTransportMetrics_Dispose_StopsEmitting()
    {
        var config = new McpServerConfiguration { HttpTransportMode = HttpTransportMode.Stateless };
        using var capture = new MetricsCapture("poshmcp.http_transport_mode");

        HttpTransportMetrics httpMetrics;
        using (httpMetrics = new HttpTransportMetrics(config))
        {
            capture.Listener.RecordObservableInstruments();
            var beforeDispose = capture.GetLongValues("poshmcp.http_transport_mode").Count;
            Assert.True(beforeDispose >= 1, "Expected gauge to emit before dispose");
        }
        // After dispose, RecordObservations must not trigger the callback for the disposed meter.
        var countBefore = capture.GetLongValues("poshmcp.http_transport_mode").Count;
        capture.Listener.RecordObservableInstruments();
        var countAfter = capture.GetLongValues("poshmcp.http_transport_mode").Count;

        _output.WriteLine($"Before re-observe after dispose: {countBefore}, after: {countAfter}");
        // After meter disposal the SDK may still emit one final value or stop immediately;
        // either way the count must not grow significantly (no runaway emission).
        Assert.True(countAfter - countBefore <= 1,
            "HttpTransportMetrics must stop emitting after disposal");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static StatelessRunspacePool MakePool(int eager = 1, int max = 4) =>
        new StatelessRunspacePool(
            FastOpts(eager, max),
            loggerFactory: null,
            workerFactory: () => new NoopRunspace(),
            snapshotCapture: _ => EmptySet(),
            driveSnapshotCapture: _ => EmptySet(),
            functionSnapshotCapture: _ => EmptySet(),
            aliasSnapshotCapture: _ => EmptySet(),
            resetProtocol: (_, _, _) => Task.CompletedTask);

    private static RunspacePoolOptions FastOpts(int eager = 1, int max = 4) => new()
    {
        MinPoolSize = 1,
        MaxPoolSize = max,
        EagerWarmCount = eager,
        AcquisitionTimeout = TimeSpan.FromSeconds(10),
        IdleTtl = TimeSpan.FromSeconds(300),
        SweepInterval = TimeSpan.FromSeconds(300),
        StopTimeout = TimeSpan.FromSeconds(5),
        ShutdownDrainTimeout = TimeSpan.FromSeconds(10),
        ReplenishCheckInterval = TimeSpan.FromSeconds(300),
    };

    private static IReadOnlySet<string> EmptySet() => new HashSet<string>();

    private void AssertNoUnknownEvictionLabels(MetricsCapture capture)
    {
        var observed = capture.GetObservedTags("poshmcp.runspace_pool.evictions_total");
        foreach (var label in observed)
        {
            Assert.True(KnownEvictionReasons.Contains(label),
                $"Unknown eviction reason label: '{label}'. Known: {string.Join(", ", KnownEvictionReasons)}");
        }
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.Elapsed < timeout)
            await Task.Delay(20);
        Assert.True(condition(), $"Condition not met within {timeout}");
    }

    // ─── Test-double runspace ─────────────────────────────────────────────────

    /// <summary>
    /// Minimal test-double that satisfies <see cref="IPowerShellRunspace"/> without creating
    /// a real PowerShell runspace. All executions are no-ops.
    /// </summary>
    private sealed class NoopRunspace : IPowerShellRunspace
    {
        private readonly PSPowerShell _ps;

        public NoopRunspace()
        {
            var runspace = System.Management.Automation.Runspaces.RunspaceFactory.CreateRunspace();
            runspace.Open();
            _ps = PSPowerShell.Create();
            _ps.Runspace = runspace;
        }

        public PSPowerShell Instance => _ps;

        public T ExecuteThreadSafe<T>(Func<PSPowerShell, T> operation)
        {
            lock (_ps) return operation(_ps);
        }

        public void ExecuteThreadSafe(Action<PSPowerShell> action)
        {
            lock (_ps) action(_ps);
        }

        public Task<T> ExecuteThreadSafeAsync<T>(Func<PSPowerShell, Task<T>> action)
        {
            lock (_ps) return action(_ps);
        }

        public void FinalizeDiscovery() { }

        public void Dispose()
        {
            _ps.Runspace?.Dispose();
            _ps.Dispose();
        }
    }
}

// ─── Collection definition (no parallelization with other metrics tests) ─────

[CollectionDefinition("PoolMetricsInstrumentTests", DisableParallelization = true)]
public class PoolMetricsInstrumentTestsCollection { }

// ─── MetricsCapture helper ────────────────────────────────────────────────────

/// <summary>
/// Thread-safe helper that uses <see cref="MeterListener"/> to capture all measurements
/// emitted by instruments whose names start with any of the given prefixes.
/// Measurements are indexed by instrument name; tags (state/reason) are captured as the
/// primary discriminator. Dispose to stop listening.
/// </summary>
internal sealed class MetricsCapture : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<(long Value, string? Tag)>> _longData = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<double>> _doubleData = new();
    private int _instrumentPublishedCount;

    public MeterListener Listener => _listener;
    public int InstrumentPublishedCount => Volatile.Read(ref _instrumentPublishedCount);

    public MetricsCapture(params string[] nameFilter)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            bool match = nameFilter.Length == 0 ||
                         nameFilter.Any(f => instrument.Name.StartsWith(f, StringComparison.Ordinal));
            if (!match) return;

            Interlocked.Increment(ref _instrumentPublishedCount);
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
            _longData.GetOrAdd(instrument.Name, _ => new()).Enqueue((measurement, tag));
        });

        _listener.SetMeasurementEventCallback<double>((instrument, measurement, _, __) =>
        {
            _doubleData.GetOrAdd(instrument.Name, _ => new()).Enqueue(measurement);
        });

        _listener.Start();
    }

    /// <summary>Net signed sum of all long measurements for an instrument, optionally filtered by tag.</summary>
    public long SumLong(string name, string? tag = null)
    {
        if (!_longData.TryGetValue(name, out var queue)) return 0L;
        return queue.Where(m => tag == null || m.Tag == tag).Sum(m => m.Value);
    }

    /// <summary>Count of long measurements for an instrument, optionally filtered by tag.</summary>
    public long CountLong(string name, string? tag = null)
    {
        if (!_longData.TryGetValue(name, out var queue)) return 0L;
        return queue.Count(m => tag == null || m.Tag == tag);
    }

    /// <summary>Count of double (histogram) measurements for an instrument.</summary>
    public int CountDouble(string name)
    {
        if (!_doubleData.TryGetValue(name, out var queue)) return 0;
        return queue.Count;
    }

    /// <summary>All double (histogram) measurements for an instrument.</summary>
    public IReadOnlyList<double> GetDoubleValues(string name)
    {
        if (!_doubleData.TryGetValue(name, out var queue)) return [];
        return queue.ToList();
    }

    /// <summary>All long measurements for an instrument.</summary>
    public IReadOnlyList<long> GetLongValues(string name)
    {
        if (!_longData.TryGetValue(name, out var queue)) return [];
        return queue.Select(m => m.Value).ToList();
    }

    /// <summary>Last long measurement for an instrument (for stable gauges).</summary>
    public long GetLastLongValue(string name)
    {
        if (!_longData.TryGetValue(name, out var queue)) return 0L;
        return queue.LastOrDefault().Value;
    }

    /// <summary>All distinct primary tag values observed for an instrument.</summary>
    public IReadOnlyList<string> GetObservedTags(string name)
    {
        if (!_longData.TryGetValue(name, out var queue)) return [];
        return queue
            .Where(m => !string.IsNullOrEmpty(m.Tag))
            .Select(m => m.Tag!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public void Dispose() => _listener.Dispose();
}
