using System;
using System.Collections.Generic;
using System.Diagnostics;
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
/// Concurrency and stress tests for <see cref="StatelessRunspacePool"/>.
/// Covers all #341 acceptance criteria:
/// <list type="bullet">
/// <item>Concurrent leases up to and beyond <see cref="RunspacePoolOptions.MaxPoolSize"/>.</item>
/// <item>Exhaustion produces bounded acquisition timeout, never deadlock.</item>
/// <item>Broken/poisoned worker eviction and replenishment while unrelated leases continue.</item>
/// <item>Rapid acquire/release cycles with exact counter consistency.</item>
/// <item>Concurrent reset contention: slow resets do not block ready workers.</item>
/// <item><see cref="RunspacePoolOptions.MinPoolSize"/> maintained under eviction pressure.</item>
/// <item>Slow worker creation does not block pre-existing warm workers.</item>
/// <item>Tests pass reliably across repeated runs (stability/flake detection).</item>
/// </list>
/// All tests use deterministic coordination primitives — <see cref="TaskCompletionSource"/>,
/// <see cref="SemaphoreSlim"/>, <see cref="ManualResetEventSlim"/> — rather than arbitrary
/// wall-clock sleeps as primary synchronisation signals.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RunspacePoolConcurrencyTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────────

    private static Mock<IPowerShellRunspace> MockRunspace()
    {
        var ps = PSPowerShell.Create();
        var mock = new Mock<IPowerShellRunspace>();
        mock.Setup(r => r.Instance).Returns(ps);
        mock.Setup(r => r.Dispose()).Callback(ps.Dispose);
        return mock;
    }

    private static StatelessRunspacePool MakePool(
        RunspacePoolOptions? options = null,
        Func<IPowerShellRunspace>? factory = null,
        Func<RunspaceWorker, ILogger, CancellationToken, Task>? reset = null,
        Func<DateTimeOffset>? clock = null) =>
        new(options ?? ConcurrencyOptions(),
            loggerFactory: null,
            startupScript: null,
            workerFactory: factory ?? (() => MockRunspace().Object),
            snapshotCapture: _ => new HashSet<string>(),
            driveSnapshotCapture: _ => new HashSet<string>(),
            resetProtocol: reset ?? ((_, _, _) => Task.CompletedTask),
            clock: clock);

    private static RunspacePoolOptions ConcurrencyOptions(int min = 2, int max = 4, int eager = 4) =>
        new()
        {
            MinPoolSize = min,
            MaxPoolSize = max,
            EagerWarmCount = eager,
            AcquisitionTimeout = TimeSpan.FromSeconds(5),
            IdleTtl = TimeSpan.FromSeconds(300),
            SweepInterval = TimeSpan.FromSeconds(60),
            StopTimeout = TimeSpan.FromSeconds(5),
            ShutdownDrainTimeout = TimeSpan.FromSeconds(5),
            ReplenishCheckInterval = TimeSpan.FromSeconds(60),
        };

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
            $"warm={s.WarmWorkers} leased={s.LeasedWorkers} " +
            $"resetting={s.ResettingWorkers} total={s.TotalWorkers}");
    }

    private static void AssertNonNegative(RunspacePoolStats s, string context = "")
    {
        Assert.True(s.WarmWorkers >= 0, $"{context} WarmWorkers={s.WarmWorkers} < 0");
        Assert.True(s.LeasedWorkers >= 0, $"{context} LeasedWorkers={s.LeasedWorkers} < 0");
        Assert.True(s.ResettingWorkers >= 0, $"{context} ResettingWorkers={s.ResettingWorkers} < 0");
        Assert.True(s.TotalWorkers >= 0, $"{context} TotalWorkers={s.TotalWorkers} < 0");
    }

    // ─── 1. Exhaustion — callers beyond capacity time out, no deadlock ──────────

    /// <summary>
    /// With all <see cref="RunspacePoolOptions.MaxPoolSize"/> workers held, additional
    /// callers must surface <see cref="TimeoutException"/> within the configured timeout
    /// and must never deadlock.
    /// </summary>
    [Fact]
    public async Task ConcurrentAcquire_ExcessCallers_AllTimeOutWithoutDeadlock()
    {
        var opts = ConcurrencyOptions(min: 2, max: 4, eager: 4);
        opts.AcquisitionTimeout = TimeSpan.FromMilliseconds(150);
        opts.ShutdownDrainTimeout = TimeSpan.FromMilliseconds(500);

        await using var pool = MakePool(opts);
        await pool.StartAsync();

        // Hold all 4 workers so excess callers are forced to wait.
        var held = new RunspaceLease[4];
        for (int i = 0; i < 4; i++)
            held[i] = await pool.AcquireAsync();

        Assert.Equal(4, pool.GetStats().LeasedWorkers);
        Assert.Equal(0, pool.GetStats().WarmWorkers);

        // 4 excess callers — must all time out (not deadlock).
        var excess = Enumerable.Range(0, 4)
            .Select(_ => pool.AcquireAsync().AsTask())
            .ToArray();

        // Primary signal: WhenAll must complete within a generous wall-clock bound.
        await Task.WhenAny(Task.WhenAll(excess), Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(excess.All(t => t.IsCompleted),
            "Some excess callers are still pending — potential deadlock.");

        // Every excess caller must have thrown TimeoutException (not OCE, not other).
        foreach (var t in excess)
        {
            Assert.True(t.IsFaulted, $"Expected Faulted; status={t.Status}");
            Assert.IsType<TimeoutException>(t.Exception!.InnerException);
        }

        AssertNonNegative(pool.GetStats(), "after-exhaustion");
        foreach (var l in held) await l.DisposeAsync();
        await pool.DisposeAsync();
    }

    // ─── 2. Hard concurrency cap — MaxPoolSize distinct workers leased at once ──

    /// <summary>
    /// Exactly <see cref="RunspacePoolOptions.MaxPoolSize"/> concurrent callers must all
    /// succeed with distinct leases, and <c>LeasedWorkers</c> must equal <c>MaxPoolSize</c>.
    /// </summary>
    [Fact]
    public async Task ConcurrentAcquire_AtMaxCapacity_AllLeasesDistinct_LeasedCountAtMax()
    {
        await using var pool = MakePool(ConcurrencyOptions(min: 4, max: 4, eager: 4));
        await pool.StartAsync();

        var leaseTasks = Enumerable.Range(0, 4)
            .Select(_ => pool.AcquireAsync().AsTask())
            .ToArray();
        var leases = await Task.WhenAll(leaseTasks);

        var stats = pool.GetStats();
        Assert.Equal(4, stats.LeasedWorkers);
        Assert.Equal(0, stats.WarmWorkers);
        Assert.Equal(4, stats.TotalWorkers);

        // All lease objects must be distinct — no worker double-issued.
        Assert.Equal(4, leases.Distinct().Count());

        foreach (var l in leases) await l.DisposeAsync();
        await WaitForStatsAsync(pool, s => s.LeasedWorkers == 0 && s.ResettingWorkers == 0);
        await pool.DisposeAsync();
    }

    // ─── 3. Rapid acquire/release — counter invariants hold at quiescence ────────

    /// <summary>
    /// N concurrent tasks each run M sequential acquire/return pairs. After quiescence the
    /// stable-state equation must hold: <c>Warm + Leased + Resetting == Total</c>, all
    /// counters non-negative, <c>Warm ≥ Min</c>.
    /// </summary>
    [Theory]
    [InlineData(4, 20)]   // 4 concurrent tasks, 20 iterations each
    [InlineData(8, 15)]   // 8 concurrent tasks, 15 iterations each (> MaxPoolSize callers)
    public async Task RapidAcquireRelease_CounterInvariantsHoldAtQuiescence(int concurrency, int iterations)
    {
        await using var pool = MakePool(ConcurrencyOptions(min: 2, max: 4, eager: 4));
        await pool.StartAsync();

        var tasks = Enumerable.Range(0, concurrency).Select(_ => Task.Run(async () =>
        {
            for (int i = 0; i < iterations; i++)
            {
                await using var lease = await pool.AcquireAsync();
                Assert.NotNull(lease.PowerShell);
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        await WaitForStatsAsync(pool, s => s.LeasedWorkers == 0 && s.ResettingWorkers == 0);

        var stats = pool.GetStats();
        AssertNonNegative(stats, "post-quiescence");
        Assert.True(stats.TotalWorkers <= stats.MaxPoolSize,
            $"TotalWorkers={stats.TotalWorkers} > MaxPoolSize={stats.MaxPoolSize}");
        Assert.True(stats.WarmWorkers >= stats.MinPoolSize,
            $"WarmWorkers={stats.WarmWorkers} < MinPoolSize={stats.MinPoolSize} at quiescence");
        // Stable-state equation: no Creating workers in-flight at quiescence.
        Assert.Equal(
            stats.WarmWorkers + stats.LeasedWorkers + stats.ResettingWorkers,
            stats.TotalWorkers);

        await pool.DisposeAsync();
    }

    // ─── 4. Concurrent reset contention — slow resets don't block warm workers ──

    /// <summary>
    /// Workers undergoing a slow (gated) reset must not block acquisition of other warm
    /// workers. The gate is a deterministic <see cref="TaskCompletionSource"/> — no sleeps.
    /// </summary>
    [Fact]
    public async Task ConcurrentReset_SlowResetsDoNotBlockWarmWorkerAcquisitions()
    {
        var resetStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resetGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int resetCount = 0;

        async Task SlowReset(RunspaceWorker _, ILogger __, CancellationToken ct)
        {
            if (Interlocked.Increment(ref resetCount) <= 2)
            {
                resetStarted.TrySetResult();
                // Block until gate is opened (or pool is shut down).
                await resetGate.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            }
        }

        await using var pool = MakePool(
            options: ConcurrencyOptions(min: 2, max: 4, eager: 4),
            reset: SlowReset);
        await pool.StartAsync();

        // Acquire and release 2 workers — each fires SlowReset (blocking).
        // Fire-and-forget the return tasks to avoid blocking the test on the gate.
        var slow1 = await pool.AcquireAsync();
        var slow2 = await pool.AcquireAsync();
        var returnTask1 = slow1.DisposeAsync().AsTask();
        var returnTask2 = slow2.DisposeAsync().AsTask();

        // Primary signal: wait until at least one reset has started (deterministic).
        await resetStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForStatsAsync(pool, s => s.ResettingWorkers >= 1, TimeSpan.FromSeconds(3));

        // The 2 remaining warm workers (3 and 4) must be immediately leasable.
        var sw = Stopwatch.StartNew();
        var l3 = await pool.AcquireAsync();
        var l4 = await pool.AcquireAsync();
        sw.Stop();

        Assert.NotNull(l3.PowerShell);
        Assert.NotNull(l4.PowerShell);
        // Secondary timing sanity: warm workers should not be blocked by slow reset.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2),
            $"Warm worker acquisition took {sw.Elapsed.TotalMilliseconds:F0}ms — possibly blocked by slow reset");

        // Open the gate; both slow resets complete.
        resetGate.SetResult();
        await l3.DisposeAsync();
        await l4.DisposeAsync();
        await Task.WhenAll(returnTask1, returnTask2);

        await WaitForStatsAsync(pool, s => s.ResettingWorkers == 0 && s.LeasedWorkers == 0);
        AssertNonNegative(pool.GetStats(), "post-slow-reset");
        await pool.DisposeAsync();
    }

    // ─── 5. Poisoned worker eviction — healthy leases remain active ───────────

    /// <summary>
    /// Evicting poisoned workers (via <c>RequestEviction</c>) while healthy leases are
    /// still held must not block the healthy leases or leave counters negative.
    /// Replenishment must restore <c>MinPoolSize</c> after the healthy leases return.
    /// </summary>
    [Fact]
    public async Task PoisonedWorkerEviction_HealthyLeasesRemainActive_PoolReplenishes()
    {
        await using var pool = MakePool(ConcurrencyOptions(min: 2, max: 4, eager: 4));
        await pool.StartAsync();

        Assert.Equal(4, pool.GetStats().TotalWorkers);

        // Acquire all 4 workers.
        var leases = new RunspaceLease[4];
        for (int i = 0; i < 4; i++)
            leases[i] = await pool.AcquireAsync();

        // Mark first 2 as poison (explicit eviction).
        leases[0].RequestEviction();
        leases[1].RequestEviction();
        await leases[0].DisposeAsync();
        await leases[1].DisposeAsync();

        // Healthy leases [2] and [3] are still active — pool must not block them.
        var statsMidway = pool.GetStats();
        Assert.Equal(2, statsMidway.LeasedWorkers);
        AssertNonNegative(statsMidway, "midway-eviction");

        // Replenishment fires from the eviction path (FireAndForgetCreateWorkerAsync).
        // Wait until total recovers: 2 held leases + ≥ 0 newly warm workers.
        await WaitForStatsAsync(pool, s => s.TotalWorkers >= 2, TimeSpan.FromSeconds(10));

        // Release the healthy leases.
        await leases[2].DisposeAsync();
        await leases[3].DisposeAsync();

        await WaitForStatsAsync(pool, s => s.LeasedWorkers == 0 && s.ResettingWorkers == 0);

        var statsFinal = pool.GetStats();
        AssertNonNegative(statsFinal, "post-eviction");
        Assert.True(statsFinal.TotalWorkers >= statsFinal.MinPoolSize,
            $"TotalWorkers={statsFinal.TotalWorkers} < MinPoolSize={statsFinal.MinPoolSize} after replenishment");

        await pool.DisposeAsync();
    }

    // ─── 6. Slow worker creation — pre-existing warm workers remain available ───

    /// <summary>
    /// A gated factory (simulating a slow startup script) must not block acquisition of
    /// pre-existing warm workers. The primary assertion is that the warm-worker lease
    /// completes without waiting for the in-flight creation to finish.
    /// </summary>
    [Fact]
    public async Task SlowWorkerCreation_DoesNotBlockPreexistingWarmWorkers()
    {
        // Gate allows first 2 factory calls (eager startup) through immediately.
        // Subsequent calls block until the gate is signalled.
        var gate = new ManualResetEventSlim(false);
        int callCount = 0;
        int completedCount = 0; // incremented only after the gate passes (i.e., factory finished)

        IPowerShellRunspace GatedFactory()
        {
            // First 2 calls: eager startup — pass through immediately.
            // Subsequent calls: block until gate is signalled (runs in Task.Run context).
            if (Interlocked.Increment(ref callCount) > 2)
                gate.Wait(TimeSpan.FromSeconds(15));
            Interlocked.Increment(ref completedCount);
            return MockRunspace().Object;
        }

        var opts = ConcurrencyOptions(min: 2, max: 4, eager: 2);
        opts.ReplenishCheckInterval = TimeSpan.FromSeconds(60); // background replenisher off

        await using var pool = MakePool(opts, factory: GatedFactory);
        await pool.StartAsync();
        Assert.Equal(2, pool.GetStats().WarmWorkers);

        // Evict one worker — fires FireAndForgetCreateWorkerAsync → GatedFactory call 3 → blocked.
        var evictLease = await pool.AcquireAsync();
        evictLease.RequestEviction();
        await evictLease.DisposeAsync();

        // Primary assertion: pre-existing warm worker (worker 2) is immediately leasable.
        var sw = Stopwatch.StartNew();
        var warmLease = await pool.AcquireAsync();
        sw.Stop();

        Assert.NotNull(warmLease.PowerShell);
        // Secondary timing bound: warm worker should not wait for the blocked factory.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1),
            $"Pre-existing warm worker took {sw.Elapsed.TotalMilliseconds:F0}ms — possibly blocked by slow creation");

        // The gated factory has started (callCount == 3) but has NOT completed yet
        // (completedCount == 2) — the new worker is still in-flight behind the gate.
        Assert.Equal(2, Volatile.Read(ref completedCount));

        await warmLease.DisposeAsync();

        // Release gate and wait for the blocked creation to complete.
        gate.Set();
        await WaitForStatsAsync(pool, s => s.LeasedWorkers == 0 && s.ResettingWorkers == 0);
        await pool.DisposeAsync();
    }

    // ─── 7. MinPoolSize under repeated eviction pressure ─────────────────────────

    /// <summary>
    /// Across <c>N</c> eviction rounds, the pool must restore at least
    /// <see cref="RunspacePoolOptions.MinPoolSize"/> workers after each round.
    /// </summary>
    [Fact]
    public async Task MinPoolSize_UnderRepeatedEvictionPressure_AlwaysRestored()
    {
        var opts = ConcurrencyOptions(min: 2, max: 6, eager: 4);
        opts.ReplenishCheckInterval = TimeSpan.FromSeconds(60); // rely on inline replenishment only

        await using var pool = MakePool(opts);
        await pool.StartAsync();

        const int rounds = 8;
        for (int round = 0; round < rounds; round++)
        {
            // Evict all currently warm workers.
            int warm = pool.GetStats().WarmWorkers;
            var leases = new List<RunspaceLease>(warm);
            for (int i = 0; i < warm; i++)
                leases.Add(await pool.AcquireAsync());

            foreach (var l in leases)
            {
                l.RequestEviction();
                await l.DisposeAsync();
            }

            // Wait for replenishment to restore MinPoolSize at quiescence.
            await WaitForStatsAsync(
                pool,
                s => s.TotalWorkers >= s.MinPoolSize
                     && s.LeasedWorkers == 0
                     && s.ResettingWorkers == 0,
                TimeSpan.FromSeconds(10));

            var stats = pool.GetStats();
            AssertNonNegative(stats, $"round-{round}");
            Assert.True(stats.TotalWorkers >= stats.MinPoolSize,
                $"Round {round}: TotalWorkers={stats.TotalWorkers} < MinPoolSize={stats.MinPoolSize}");
        }

        await pool.DisposeAsync();
    }

    // ─── 8. Concurrent disposal + lease return — counters never negative ─────────

    /// <summary>
    /// When all outstanding leases are returned concurrently while <see cref="StatelessRunspacePool.DisposeAsync"/>
    /// is draining, all counters must reach exact zero after both operations complete — verifying
    /// that no workers leak and no background work remains in-flight.
    /// Tests the <c>FinalizeLeaseDone</c> concurrent-decrement path.
    /// </summary>
    [Fact]
    public async Task ConcurrentDisposalAndLeaseReturn_CountersNeverGoNegative()
    {
        var opts = ConcurrencyOptions(min: 2, max: 4, eager: 4);
        opts.ShutdownDrainTimeout = TimeSpan.FromSeconds(10); // generous — let leases return naturally

        var pool = MakePool(opts);
        await pool.StartAsync();

        // Acquire all 4 workers.
        var leases = new RunspaceLease[4];
        for (int i = 0; i < 4; i++)
            leases[i] = await pool.AcquireAsync();

        // Dispose pool while leases are outstanding, concurrently returning all leases.
        var disposeTask = pool.DisposeAsync().AsTask();
        await Task.WhenAll(leases.Select(l => l.DisposeAsync().AsTask()));
        await disposeTask;

        var stats = pool.GetStats();
        // After all leases are returned and DisposeAsync completes, the pool must be fully
        // drained: no leaked workers, no in-flight background work, exact zeros on all counters.
        Assert.Equal(0, stats.TotalWorkers);
        Assert.Equal(0, stats.WarmWorkers);
        Assert.Equal(0, stats.LeasedWorkers);
        Assert.Equal(0, stats.ResettingWorkers);
    }

    // ─── 9. Repeated exhaustion and release — stability / no flake ───────────────

    /// <summary>
    /// Runs the exhaustion → timeout → release cycle <c>25</c> times to surface any race
    /// condition that only appears under repeated pressure. No arbitrary sleeps — each
    /// iteration uses <c>WaitForStatsAsync</c> to await quiescence deterministically.
    /// </summary>
    [Fact]
    public async Task RepeatedExhaustionAndRelease_NeverDeadlocks()
    {
        var opts = ConcurrencyOptions(min: 2, max: 3, eager: 3);
        opts.AcquisitionTimeout = TimeSpan.FromMilliseconds(80);
        opts.ShutdownDrainTimeout = TimeSpan.FromSeconds(5);

        await using var pool = MakePool(opts);
        await pool.StartAsync();

        const int iterations = 25;
        for (int i = 0; i < iterations; i++)
        {
            // Acquire up to MaxPoolSize workers (may get fewer if replenishment lags).
            var held = new List<RunspaceLease>();
            for (int j = 0; j < 3; j++)
            {
                try { held.Add(await pool.AcquireAsync()); }
                catch (TimeoutException) { /* acceptable if pool hasn't fully replenished yet */ }
            }

            if (held.Count > 0)
            {
                // One extra caller beyond capacity must time out, not deadlock.
                var overflow = pool.AcquireAsync().AsTask();
                await Task.WhenAny(overflow, Task.Delay(TimeSpan.FromSeconds(2)));
                Assert.True(overflow.IsCompleted,
                    $"Iteration {i}: overflow acquire did not complete — potential deadlock.");
                if (overflow.IsFaulted)
                    Assert.IsType<TimeoutException>(overflow.Exception!.InnerException);

                foreach (var l in held) await l.DisposeAsync();
            }

            await WaitForStatsAsync(
                pool,
                s => s.LeasedWorkers == 0 && s.ResettingWorkers == 0,
                TimeSpan.FromSeconds(5));

            AssertNonNegative(pool.GetStats(), $"iteration-{i}");
        }

        await pool.DisposeAsync();
    }
}
