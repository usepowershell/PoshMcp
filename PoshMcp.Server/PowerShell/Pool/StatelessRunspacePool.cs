using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.Server.PowerShell.Pool;

/// <summary>
/// Server-owned warm-worker pool for HTTP transport.
/// Implements <see cref="IRunspacePool"/> using a <see cref="Channel{T}"/>-based available
/// queue with interlocked counters for lock-free hot-path stats.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifecycle.</b> Call <see cref="StartAsync"/> once before the first
/// <see cref="AcquireAsync"/>. Workers are created asynchronously; the pool does not accept
/// requests until <see cref="StartAsync"/> returns. After startup, the background replenisher
/// keeps <see cref="RunspacePoolOptions.MinPoolSize"/> warm workers alive and the sweeper
/// evicts surplus workers that have been idle beyond <see cref="RunspacePoolOptions.IdleTtl"/>.
/// </para>
/// <para>
/// <b>Thread safety.</b> All public methods are thread-safe. Hot-path state (<c>_warmCount</c>,
/// <c>_leasedCount</c>, <c>_resettingCount</c>, <c>_totalCount</c>) uses
/// <see cref="Interlocked"/> operations. Worker state transitions use
/// <see cref="RunspaceWorker.TryTransitionTo"/> which itself uses
/// <see cref="Interlocked.CompareExchange(ref int, int, int)"/>.
/// </para>
/// <para>
/// <b>Testability.</b> The optional <paramref name="workerFactory"/> constructor parameter
/// injects a custom <see cref="IPowerShellRunspace"/> factory used by unit tests to avoid
/// creating real PowerShell runspaces.
/// </para>
/// </remarks>
public sealed class StatelessRunspacePool : IRunspacePool
{
    private readonly RunspacePoolOptions _options;
    private readonly ILogger<StatelessRunspacePool> _logger;
    private readonly RunspacePoolMetrics _metrics;
    private readonly string? _startupScript;
    private readonly Func<IPowerShellRunspace> _workerFactory;
    private readonly Func<PSPowerShell, IReadOnlySet<string>> _snapshotCapture;
    private readonly Func<RunspaceWorker, ILogger, CancellationToken, Task> _resetProtocol;

    // All live workers tracked for sweep/drain.
    private readonly ConcurrentDictionary<RunspaceWorker, byte> _allWorkers = new();

    // Available warm workers. Unbounded so stale evicted entries do not block
    // replenishment writes; total-worker cap is enforced via _totalCount.
    private readonly Channel<RunspaceWorker> _available = Channel.CreateUnbounded<RunspaceWorker>(
        new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

    // Lifecycle counters — all updated via Interlocked.
    private int _warmCount;
    private int _leasedCount;
    private int _resettingCount;

    // Total live workers: Creating + Warm + Leased + Resetting.
    // Incremented before creation begins; decremented when Evicted.
    private int _totalCount;

    // Drain/dispose state.
    private int _draining;   // 1 after DrainAsync is called
    private int _disposed;   // 1 after DisposeAsync completes
    private int _outstandingLeases;
    private readonly TaskCompletionSource _allLeasesReturned =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Background task shutdown.
    private readonly CancellationTokenSource _shutdownCts = new();
    // Captured immediately so callers never touch a disposed CTS.
    private readonly CancellationToken _shutdownToken;
    private Task? _sweeperTask;
    private Task? _replenisherTask;

    /// <summary>
    /// Creates a new <see cref="StatelessRunspacePool"/>.
    /// Call <see cref="StartAsync"/> before the first <see cref="AcquireAsync"/>.
    /// </summary>
    /// <param name="options">Pool configuration. Validated on construction.</param>
    /// <param name="loggerFactory">Logger factory; defaults to <see cref="NullLoggerFactory"/>.</param>
    /// <param name="startupScript">
    /// Optional PowerShell script executed once per worker at initialization.
    /// </param>
    /// <param name="workerFactory">
    /// Optional factory for <see cref="IPowerShellRunspace"/> instances.
    /// Defaults to <c>() => new IsolatedPowerShellRunspace(startupScript)</c>.
    /// Inject a test double to avoid real runspace creation in unit tests.
    /// </param>
    /// <param name="snapshotCapture">
    /// Optional delegate that captures variable names from a <c>PSPowerShell</c> after
    /// startup. Defaults to <see cref="RunspaceResetProtocol.CaptureVariableSnapshot"/>.
    /// Inject a no-op in unit tests.
    /// </param>
    /// <param name="resetProtocol">
    /// Optional delegate that executes the reset cycle on a returned worker.
    /// Defaults to <see cref="RunspaceResetProtocol.ResetAsync"/>.
    /// Inject a no-op in unit tests.
    /// </param>
    public StatelessRunspacePool(
        RunspacePoolOptions options,
        ILoggerFactory? loggerFactory = null,
        string? startupScript = null,
        Func<IPowerShellRunspace>? workerFactory = null,
        Func<PSPowerShell, IReadOnlySet<string>>? snapshotCapture = null,
        Func<RunspaceWorker, ILogger, CancellationToken, Task>? resetProtocol = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = options.Validate();
        if (errors.Count > 0)
            throw new ArgumentException(
                $"Invalid RunspacePoolOptions: {string.Join("; ", errors)}", nameof(options));

        _options = options;
        _startupScript = startupScript;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance)
            .CreateLogger<StatelessRunspacePool>();
        _metrics = new RunspacePoolMetrics();
        _workerFactory = workerFactory ?? (() => new IsolatedPowerShellRunspace(_startupScript ?? string.Empty));
        _snapshotCapture = snapshotCapture ?? RunspaceResetProtocol.CaptureVariableSnapshot;
        _resetProtocol = resetProtocol ?? RunspaceResetProtocol.ResetAsync;
        _shutdownToken = _shutdownCts.Token;
    }

    // ─── Startup ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Initializes the pool by creating <see cref="RunspacePoolOptions.EagerWarmCount"/> workers
    /// and starting the sweeper and replenisher background loops.
    /// </summary>
    /// <remarks>
    /// If all eager-warm workers fail initialization and <c>EagerWarmCount &gt; 0</c>,
    /// this method throws. Partial success is allowed: at least one worker must start.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Pool has already been started, or all eager-warm workers failed initialization.
    /// </exception>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        _logger.LogInformation(
            "Starting StatelessRunspacePool: min={Min}, max={Max}, eager={Eager}.",
            _options.MinPoolSize, _options.MaxPoolSize, _options.EagerWarmCount);

        if (_options.EagerWarmCount > 0)
        {
            var startupTasks = new List<Task>(_options.EagerWarmCount);
            for (var i = 0; i < _options.EagerWarmCount; i++)
                startupTasks.Add(CreateWorkerAsync(cancellationToken));

            await Task.WhenAll(startupTasks).ConfigureAwait(false);

            int warm = Volatile.Read(ref _warmCount);
            if (warm == 0)
                throw new InvalidOperationException(
                    $"All {_options.EagerWarmCount} eager-warm worker(s) failed initialization. " +
                    "Pool cannot start.");

            _logger.LogInformation(
                "Startup complete: {Warm}/{Eager} workers initialized.", warm, _options.EagerWarmCount);
        }

        var ct = _shutdownToken;
        _sweeperTask = Task.Run(() => SweeperLoopAsync(ct), ct);
        _replenisherTask = Task.Run(() => ReplenisherLoopAsync(ct), ct);
    }

    // ─── IRunspacePool ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async ValueTask<RunspaceLease> AcquireAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_draining != 0 || _disposed != 0, this);

        var sw = Stopwatch.StartNew();

        // Combine caller CT with the pool's acquisition timeout.
        using var timeoutCts = _options.AcquisitionTimeout > TimeSpan.Zero
            ? new CancellationTokenSource(_options.AcquisitionTimeout)
            : null;

        CancellationToken effectiveCt;
        CancellationTokenRegistration reg = default;
        if (timeoutCts != null && cancellationToken.CanBeCanceled)
        {
            effectiveCt = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token).Token;
        }
        else if (timeoutCts != null)
        {
            effectiveCt = timeoutCts.Token;
        }
        else
        {
            effectiveCt = cancellationToken;
        }

        try
        {
            // AcquisitionTimeout == Zero means instant-fail: don't wait on the channel.
            if (_options.AcquisitionTimeout == TimeSpan.Zero)
            {
                if (!_available.Reader.TryRead(out var immediateWorker) ||
                    !immediateWorker.TryTransitionTo(RunspaceWorkerState.Leased))
                {
                    _metrics.AcquisitionTimeouts.Add(1);
                    throw new TimeoutException(
                        "No warm worker available immediately (AcquisitionTimeout = 0).");
                }
                return FinalizeLease(immediateWorker, sw);
            }

            // Wait for a warm worker. Retry if a concurrently evicted stale entry is dequeued.
            while (true)
            {
                RunspaceWorker worker;
                try
                {
                    worker = await _available.Reader.ReadAsync(effectiveCt).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeoutCts?.Token.IsCancellationRequested == true)
                {
                    _metrics.AcquisitionTimeouts.Add(1);
                    throw new TimeoutException(
                        $"No warm worker became available within {_options.AcquisitionTimeout}.");
                }

                // Guard: draining may have been set while we waited.
                ObjectDisposedException.ThrowIf(_draining != 0 || _disposed != 0, this);

                if (worker.TryTransitionTo(RunspaceWorkerState.Leased))
                    return FinalizeLease(worker, sw);

                // Worker was evicted between enqueue and our read; discard and retry.
                _logger.LogDebug(
                    "Discarded stale channel entry for worker created at {CreatedAt}.",
                    worker.CreatedAt);
            }
        }
        finally
        {
            reg.Dispose();
        }
    }

    private RunspaceLease FinalizeLease(RunspaceWorker worker, Stopwatch sw)
    {
        Interlocked.Decrement(ref _warmCount);
        Interlocked.Increment(ref _leasedCount);
        _metrics.WorkerCount.Add(-1, RunspacePoolMetrics.StateTag("warm"));
        _metrics.WorkerCount.Add(1, RunspacePoolMetrics.StateTag("leased"));
        _metrics.AcquisitionsTotal.Add(1);
        _metrics.AcquisitionDurationSeconds.Record(sw.Elapsed.TotalSeconds);

        Interlocked.Increment(ref _outstandingLeases);
        var leaseStart = Stopwatch.StartNew();
        return new RunspaceLease(worker, (w, evict) => OnWorkerReturnedAsync(w, evict, leaseStart));
    }

    /// <inheritdoc/>
    public Task DrainAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _draining, 1) != 0)
            return Task.CompletedTask;

        _logger.LogInformation("Pool draining: stopping new acquisitions.");

        // If nothing is outstanding, signal immediately.
        if (Volatile.Read(ref _outstandingLeases) == 0)
            _allLeasesReturned.TrySetResult();

        var drainTimeout = _options.ShutdownDrainTimeout;
        return DrainInternalAsync(drainTimeout, cancellationToken);
    }

    private async Task DrainInternalAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);

        try
        {
            await _allLeasesReturned.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            _logger.LogInformation("All leases returned; drain complete.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Drain timed out or cancelled; force-disposing {Total} remaining worker(s).",
                Volatile.Read(ref _totalCount));
        }

        ForceDisposeAllWorkers("drain");
    }

    /// <inheritdoc/>
    public RunspacePoolStats GetStats() =>
        new(
            _options.MinPoolSize,
            _options.MaxPoolSize,
            Volatile.Read(ref _warmCount),
            Volatile.Read(ref _leasedCount),
            Volatile.Read(ref _resettingCount),
            Volatile.Read(ref _totalCount));

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _logger.LogInformation("Disposing StatelessRunspacePool.");

        // Cancel background loops.
        await _shutdownCts.CancelAsync().ConfigureAwait(false);

        // Drain outstanding leases then force-dispose.
        await DrainAsync(CancellationToken.None).ConfigureAwait(false);

        // Await background tasks.
        if (_sweeperTask is not null)
        {
            try { await _sweeperTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        if (_replenisherTask is not null)
        {
            try { await _replenisherTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        _shutdownCts.Dispose();
        _metrics.Dispose();

        _logger.LogInformation("StatelessRunspacePool disposed.");
    }

    // ─── Worker return callback ─────────────────────────────────────────────────

    private async ValueTask OnWorkerReturnedAsync(
        RunspaceWorker worker, bool evictRequested, Stopwatch leaseTimer)
    {
        _metrics.LeaseDurationSeconds.Record(leaseTimer.Elapsed.TotalSeconds);

        // If the worker was already force-evicted during drain/dispose, just clean up counters.
        var currentState = worker.State;
        if (currentState == RunspaceWorkerState.Evicted ||
            currentState == RunspaceWorkerState.Disposed)
        {
            Interlocked.Decrement(ref _leasedCount);
            _metrics.WorkerCount.Add(-1, RunspacePoolMetrics.StateTag("leased"));
            FinalizeLeaseDone();
            return;
        }

        Interlocked.Decrement(ref _leasedCount);
        _metrics.WorkerCount.Add(-1, RunspacePoolMetrics.StateTag("leased"));

        // Transition Leased → Resetting (evict path: Leased → Evicted).
        if (evictRequested || !worker.TryTransitionTo(RunspaceWorkerState.Resetting))
        {
            string reason = evictRequested ? "explicit" : "broken";
            EvictWorker(worker, reason);
            FinalizeLeaseDone();
            _ = TryReplenishAsync(_shutdownToken);
            return;
        }

        Interlocked.Increment(ref _resettingCount);
        _metrics.WorkerCount.Add(1, RunspacePoolMetrics.StateTag("resetting"));

        var resetSw = Stopwatch.StartNew();
        try
        {
            await _resetProtocol(worker, _logger, _shutdownToken)
                .ConfigureAwait(false);

            _metrics.ResetDurationSeconds.Record(resetSw.Elapsed.TotalSeconds);

            // Transition Resetting → Warm (re-enqueue).
            if (worker.TryTransitionTo(RunspaceWorkerState.Warm))
            {
                Interlocked.Decrement(ref _resettingCount);
                Interlocked.Increment(ref _warmCount);
                _metrics.WorkerCount.Add(-1, RunspacePoolMetrics.StateTag("resetting"));
                _metrics.WorkerCount.Add(1, RunspacePoolMetrics.StateTag("warm"));
                _available.Writer.TryWrite(worker);
                _logger.LogDebug("Worker reset complete; returned to pool.");
            }
            else
            {
                // State changed under us (e.g., pool is disposing); evict.
                Interlocked.Decrement(ref _resettingCount);
                _metrics.WorkerCount.Add(-1, RunspacePoolMetrics.StateTag("resetting"));
                EvictWorker(worker, "drain");
            }
        }
        catch (OperationCanceledException)
        {
            Interlocked.Decrement(ref _resettingCount);
            _metrics.WorkerCount.Add(-1, RunspacePoolMetrics.StateTag("resetting"));
            EvictWorker(worker, "cancel");
            _ = TryReplenishAsync(_shutdownToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reset failed for worker created at {CreatedAt}; evicting.", worker.CreatedAt);
            Interlocked.Decrement(ref _resettingCount);
            _metrics.WorkerCount.Add(-1, RunspacePoolMetrics.StateTag("resetting"));
            EvictWorker(worker, "reset_failure");
            _ = TryReplenishAsync(_shutdownToken);
        }

        FinalizeLeaseDone();
    }

    private void FinalizeLeaseDone()
    {
        if (Interlocked.Decrement(ref _outstandingLeases) == 0 &&
            Volatile.Read(ref _draining) != 0)
        {
            _allLeasesReturned.TrySetResult();
        }
    }

    // ─── Worker creation ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates one worker, runs the startup script, captures the variable snapshot,
    /// and enqueues it if successful. <c>_totalCount</c> is incremented before creation
    /// begins and decremented on failure.
    /// </summary>
    private async Task CreateWorkerAsync(CancellationToken ct)
    {
        if (Interlocked.Increment(ref _totalCount) > _options.MaxPoolSize)
        {
            // Already at capacity; back off.
            Interlocked.Decrement(ref _totalCount);
            return;
        }

        RunspaceWorker? worker = null;
        try
        {
            var runspace = await Task.Run(_workerFactory, ct).ConfigureAwait(false);
            worker = new RunspaceWorker(runspace);

            // Capture variable snapshot immediately after factory construction
            // (the factory runs the startup script internally via IsolatedPowerShellRunspace).
            var snapshot = _snapshotCapture(worker.PowerShell);
            worker.SetInitializedVariableSnapshot(snapshot);

            if (!worker.TryTransitionTo(RunspaceWorkerState.Warm))
            {
                // Should never happen (Creating→Warm is always valid), but guard anyway.
                throw new InvalidOperationException("Unexpected state transition failure Creating→Warm.");
            }

            Interlocked.Increment(ref _warmCount);
            _metrics.WorkerCount.Add(1, RunspacePoolMetrics.StateTag("warm"));
            _allWorkers.TryAdd(worker, 0);
            _available.Writer.TryWrite(worker);

            _logger.LogDebug(
                "Worker {CreatedAt} initialized and enqueued. Total={Total}.",
                worker.CreatedAt, Volatile.Read(ref _totalCount));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Worker startup failed; evicting without entering pool.");
            _metrics.StartupFailures.Add(1);
            Interlocked.Decrement(ref _totalCount);

            if (worker is not null)
            {
                worker.TryTransitionTo(RunspaceWorkerState.Evicted);
                _allWorkers.TryRemove(worker, out _);
                worker.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Worker creation cancelled.");
            Interlocked.Decrement(ref _totalCount);

            if (worker is not null)
            {
                worker.TryTransitionTo(RunspaceWorkerState.Evicted);
                _allWorkers.TryRemove(worker, out _);
                worker.Dispose();
            }
        }
    }

    // ─── Eviction helper ────────────────────────────────────────────────────────

    private void EvictWorker(RunspaceWorker worker, string reason)
    {
        worker.TryTransitionTo(RunspaceWorkerState.Evicted);
        Interlocked.Decrement(ref _totalCount);
        _allWorkers.TryRemove(worker, out _);
        _metrics.Evictions.Add(1, RunspacePoolMetrics.ReasonTag(reason));
        worker.Dispose();

        _logger.LogInformation(
            "Worker {CreatedAt} evicted (reason={Reason}). Total={Total}.",
            worker.CreatedAt, reason, Volatile.Read(ref _totalCount));
    }

    private void ForceDisposeAllWorkers(string reason)
    {
        foreach (var (worker, _) in _allWorkers)
        {
            if (_allWorkers.TryRemove(worker, out _))
            {
                var state = worker.State;
                worker.TryTransitionTo(RunspaceWorkerState.Evicted);
                Interlocked.Decrement(ref _totalCount);
                // Only decrement the counter for the state the worker was actually in.
                // Leased workers: _leasedCount is decremented in OnWorkerReturnedAsync.
                if (state == RunspaceWorkerState.Warm)
                    Interlocked.Decrement(ref _warmCount);
                else if (state == RunspaceWorkerState.Resetting)
                    Interlocked.Decrement(ref _resettingCount);
                _metrics.Evictions.Add(1, RunspacePoolMetrics.ReasonTag(reason));
                worker.Dispose();
            }
        }
    }

    // ─── Replenishment ──────────────────────────────────────────────────────────

    private async Task ReplenisherLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.ReplenishCheckInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await TryReplenishAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task TryReplenishAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _draining) != 0 || ct.IsCancellationRequested)
            return;

        int deficit = _options.MinPoolSize - Volatile.Read(ref _totalCount);
        if (deficit <= 0) return;

        bool started = false;
        for (int i = 0; i < deficit; i++)
        {
            int current = Volatile.Read(ref _totalCount);
            if (current >= _options.MaxPoolSize) break;

            // CreateWorkerAsync increments _totalCount itself, so don't double-count.
            started = true;
            _ = CreateWorkerAsync(ct);
        }

        if (started)
            _metrics.Replenishments.Add(1);
    }

    // ─── Idle sweep ─────────────────────────────────────────────────────────────

    private async Task SweeperLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.SweepInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            SweepOnce();
        }
    }

    private void SweepOnce()
    {
        int warmCount = Volatile.Read(ref _warmCount);
        int surplus = warmCount - _options.MinPoolSize;
        if (surplus <= 0) return;

        var now = DateTimeOffset.UtcNow;
        int evicted = 0;

        foreach (var (worker, _) in _allWorkers)
        {
            if (evicted >= surplus) break;
            if (worker.State != RunspaceWorkerState.Warm) continue;

            // Check idle TTL against the time the worker last completed a lease cycle,
            // falling back to creation time for never-leased workers.
            var lastActive = worker.LastLeaseCompletedAt ?? worker.CreatedAt;
            if (now - lastActive < _options.IdleTtl) continue;

            if (worker.TryTransitionTo(RunspaceWorkerState.Evicted))
            {
                Interlocked.Decrement(ref _warmCount);
                Interlocked.Decrement(ref _totalCount);
                _metrics.WorkerCount.Add(-1, RunspacePoolMetrics.StateTag("warm"));
                _allWorkers.TryRemove(worker, out _);
                _metrics.Evictions.Add(1, RunspacePoolMetrics.ReasonTag("idle"));
                worker.Dispose();
                evicted++;

                _logger.LogInformation(
                    "Idle sweep evicted worker {CreatedAt} (idle since {LastActive}). " +
                    "Remaining warm={Warm}.",
                    worker.CreatedAt, lastActive, Volatile.Read(ref _warmCount));
            }
        }
    }
}
