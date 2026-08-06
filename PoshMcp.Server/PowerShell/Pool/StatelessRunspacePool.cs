using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
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
    private readonly Func<PSPowerShell, IReadOnlySet<string>> _driveSnapshotCapture;
    private readonly Func<PSPowerShell, IReadOnlySet<string>> _functionSnapshotCapture;
    private readonly Func<PSPowerShell, IReadOnlySet<string>> _aliasSnapshotCapture;
    private readonly Func<RunspaceWorker, ILogger, CancellationToken, Task> _resetProtocol;
    private readonly Func<DateTimeOffset> _clock;

    // All live workers tracked for sweep/drain.
    private readonly ConcurrentDictionary<RunspaceWorker, byte> _allWorkers = new();

    // Available warm workers. Bounded by MaxPoolSize.
    // Capacity proof: channel entries (warm + stale) ≤ MaxPoolSize at all times because:
    //   (a) workers are added only when they become Warm and _totalCount ≤ MaxPoolSize;
    //   (b) stale entries (Warm→Evicted by sweeper) are bounded to sweeper surplus ≤ MaxPoolSize−MinPoolSize;
    //   (c) replenishment after sweep never exceeds the remaining capacity (see analysis in PR).
    // A TryWrite failure would indicate a pool-accounting bug; it is logged and the worker is evicted.
    private readonly Channel<RunspaceWorker> _available;

    // Lifecycle counters — all updated via Interlocked.
    private int _warmCount;
    private int _leasedCount;
    private int _resettingCount;

    // Total live workers: Creating + Warm + Leased + Resetting.
    // Incremented before creation begins; decremented when Evicted.
    private int _totalCount;

    // Lifecycle / creating counters.
    private int _started;        // 1 after StartAsync completes successfully
    private int _creatingCount;  // number of in-flight factory calls

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

    // Guards the deterministic ReplenishOnceAsync test seam against overlapping passes.
    // Never touched by the production replenisher loop (which is single-threaded per pool),
    // so it has zero effect on runtime behavior.
    private int _replenishPassActive;

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
    /// <param name="driveSnapshotCapture">
    /// Optional delegate that captures PSDrive names from a <c>PSPowerShell</c> after
    /// startup. Defaults to <see cref="RunspaceResetProtocol.CaptureDriveSnapshot"/>.
    /// Inject a no-op in unit tests.
    /// </param>
    /// <param name="functionSnapshotCapture">
    /// Optional delegate that captures function names from a <c>PSPowerShell</c> after
    /// startup. Defaults to <see cref="RunspaceResetProtocol.CaptureFunctionSnapshot"/>.
    /// Inject a no-op in unit tests.
    /// </param>
    /// <param name="aliasSnapshotCapture">
    /// Optional delegate that captures alias names from a <c>PSPowerShell</c> after
    /// startup. Defaults to <see cref="RunspaceResetProtocol.CaptureAliasSnapshot"/>.
    /// Inject a no-op in unit tests.
    /// </param>
    /// <param name="resetProtocol">
    /// Optional delegate that executes the reset cycle on a returned worker.
    /// Defaults to <see cref="RunspaceResetProtocol.ResetAsync"/> with
    /// <see cref="RunspacePoolOptions.StopTimeout"/> as the bounded stop wait.
    /// Inject a no-op in unit tests.
    /// </param>
    /// <param name="clock">
    /// Optional function returning the current UTC time. Defaults to
    /// <see cref="DateTimeOffset.UtcNow"/>. Inject a controlled clock in unit tests
    /// to exercise idle-sweep logic without real-time delays.
    /// </param>
    public StatelessRunspacePool(
        RunspacePoolOptions options,
        ILoggerFactory? loggerFactory = null,
        string? startupScript = null,
        Func<IPowerShellRunspace>? workerFactory = null,
        Func<PSPowerShell, IReadOnlySet<string>>? snapshotCapture = null,
        Func<PSPowerShell, IReadOnlySet<string>>? driveSnapshotCapture = null,
        Func<PSPowerShell, IReadOnlySet<string>>? functionSnapshotCapture = null,
        Func<PSPowerShell, IReadOnlySet<string>>? aliasSnapshotCapture = null,
        Func<RunspaceWorker, ILogger, CancellationToken, Task>? resetProtocol = null,
        Func<DateTimeOffset>? clock = null)
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
        _driveSnapshotCapture = driveSnapshotCapture ?? RunspaceResetProtocol.CaptureDriveSnapshot;
        _functionSnapshotCapture = functionSnapshotCapture ?? RunspaceResetProtocol.CaptureFunctionSnapshot;
        _aliasSnapshotCapture = aliasSnapshotCapture ?? RunspaceResetProtocol.CaptureAliasSnapshot;
        _resetProtocol = resetProtocol ??
            ((worker, logger, ct) => RunspaceResetProtocol.ResetAsync(worker, logger, _options.StopTimeout, ct));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _shutdownToken = _shutdownCts.Token;

        // Bounded channel — capacity analysis guarantees TryWrite never fails during normal
        // operation. If it does, the worker is evicted to prevent a silent "warm but unreachable"
        // worker that would cause _warmCount to diverge from actual reachable workers.
        _available = Channel.CreateBounded<RunspaceWorker>(
            new BoundedChannelOptions(_options.MaxPoolSize)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });
    }

    // ─── Startup ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Initializes the pool by creating <see cref="RunspacePoolOptions.EagerWarmCount"/> workers
    /// and starting the sweeper and replenisher background loops.
    /// </summary>
    /// <remarks>
    /// If any eager-warm worker fails initialization and <c>EagerWarmCount &gt; 0</c>,
    /// this method throws. All <c>EagerWarmCount</c> workers must be warm before the host
    /// is allowed to serve requests — partial success is not permitted.
    /// Successfully-created partial workers are disposed before the exception is thrown.
    /// After a failed startup the pool has no live workers, <c>_started</c> remains
    /// <c>false</c>, and <see cref="DisposeAsync"/> may be called safely to release resources.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Pool has already been started, or fewer than <see cref="RunspacePoolOptions.EagerWarmCount"/>
    /// workers were successfully initialized.
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
            if (warm < _options.EagerWarmCount)
            {
                // Dispose any partial warm workers so they cannot be acquired and counters
                // return to zero. Background loops have not started, so no races exist.
                ForceDisposeAllWorkers("startup_partial_failure");
                throw new InvalidOperationException(
                    $"Only {warm}/{_options.EagerWarmCount} eager-warm worker(s) initialized. " +
                    "Pool cannot start: all EagerWarmCount workers must be warm before the host " +
                    "is allowed to serve requests.");
            }

            _logger.LogInformation(
                "Startup complete: {Warm}/{Eager} workers initialized.", warm, _options.EagerWarmCount);
        }

        var ct = _shutdownToken;
        _sweeperTask = Task.Run(() => SweeperLoopAsync(ct), ct);
        _replenisherTask = Task.Run(() => ReplenisherLoopAsync(ct), ct);
        Interlocked.Exchange(ref _started, 1);
    }

    // ─── IRunspacePool ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async ValueTask<RunspaceLease> AcquireAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_draining != 0 || _disposed != 0, this);

        var sw = Stopwatch.StartNew();

        // Combine caller CT with the pool's acquisition timeout.
        // linkedCts must be disposed alongside timeoutCts to avoid leaking CancellationCallbackInfo
        // registrations on the caller token for the lifetime of the request (one per AcquireAsync call).
        using var timeoutCts = _options.AcquisitionTimeout > TimeSpan.Zero
            ? new CancellationTokenSource(_options.AcquisitionTimeout)
            : null;
        using var linkedCts = timeoutCts != null && cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token)
            : null;
        var effectiveCt = linkedCts?.Token ?? timeoutCts?.Token ?? cancellationToken;

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
            Volatile.Read(ref _totalCount),
            CreatingWorkers: Volatile.Read(ref _creatingCount),
            IsDraining: Volatile.Read(ref _draining) != 0,
            IsStarted: Volatile.Read(ref _started) != 0);

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
        // EphemeralMode: always evict and recreate; never reset (Decision C §4B).
        if (evictRequested || _options.EphemeralMode || !worker.TryTransitionTo(RunspaceWorkerState.Resetting))
        {
            string reason = evictRequested ? "explicit" : _options.EphemeralMode ? "ephemeral" : "broken";
            EvictWorker(worker, reason);
            FinalizeLeaseDone();
            FireAndForgetCreateWorkerAsync(_shutdownToken);
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

                if (!_available.Writer.TryWrite(worker))
                {
                    // Bounded channel full despite capacity invariant — accounting bug.
                    _logger.LogCritical(
                        "BUG: bounded channel full on worker return. Worker will be orphaned-then-evicted.");
                    Interlocked.Decrement(ref _warmCount);
                    _metrics.WorkerCount.Add(-1, RunspacePoolMetrics.StateTag("warm"));
                    EvictWorker(worker, "channel_full");
                }
                else
                {
                    _logger.LogDebug("Worker reset complete; returned to pool.");
                }
            }
            else
            {
                // State changed under us (e.g., pool is disposing); evict.
                Interlocked.Decrement(ref _resettingCount);
                _metrics.WorkerCount.Add(-1, RunspacePoolMetrics.StateTag("resetting"));
                EvictWorker(worker, "drain");
            }
        }
        catch (TimeoutException)
        {
            // Reset pipeline did not stop within StopTimeout; worker is stuck/uncertain.
            _logger.LogWarning(
                "Worker {CreatedAt} evicted after reset StopTimeout.",
                worker.CreatedAt);
            Interlocked.Decrement(ref _resettingCount);
            _metrics.WorkerCount.Add(-1, RunspacePoolMetrics.StateTag("resetting"));
            EvictWorker(worker, "stop_timeout");
            FireAndForgetCreateWorkerAsync(_shutdownToken);
        }
        catch (OperationCanceledException)
        {
            Interlocked.Decrement(ref _resettingCount);
            _metrics.WorkerCount.Add(-1, RunspacePoolMetrics.StateTag("resetting"));
            EvictWorker(worker, "cancel");
            FireAndForgetCreateWorkerAsync(_shutdownToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reset failed for worker created at {CreatedAt}; evicting.", worker.CreatedAt);
            Interlocked.Decrement(ref _resettingCount);
            _metrics.WorkerCount.Add(-1, RunspacePoolMetrics.StateTag("resetting"));
            EvictWorker(worker, "reset_failure");
            FireAndForgetCreateWorkerAsync(_shutdownToken);
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
    /// Creates one worker, runs the startup script, captures the variable and drive snapshots,
    /// and enqueues it if successful. <c>_totalCount</c> is incremented before creation
    /// begins and decremented on failure.
    /// </summary>
    /// <remarks>
    /// On <see cref="OperationCanceledException"/>, the exception is re-thrown after cleanup so
    /// callers using <c>Task.WhenAll</c> (e.g., <see cref="StartAsync"/>) can detect
    /// cancellation. Fire-and-forget callers must observe the task to prevent unobserved
    /// exceptions; use <see cref="FireAndForgetCreateWorkerAsync"/> for that pattern.
    /// </remarks>
    private async Task CreateWorkerAsync(CancellationToken ct)
    {
        if (Interlocked.Increment(ref _totalCount) > _options.MaxPoolSize)
        {
            // Already at capacity; back off.
            Interlocked.Decrement(ref _totalCount);
            return;
        }

        Interlocked.Increment(ref _creatingCount);
        bool factoryCompleted = false;
        RunspaceWorker? worker = null;
        try
        {
            var runspace = await Task.Run(_workerFactory, ct).ConfigureAwait(false);
            factoryCompleted = true;
            Interlocked.Decrement(ref _creatingCount);
            worker = new RunspaceWorker(runspace);

            // Capture variable, drive, and function snapshots immediately after factory construction
            // (the factory runs the startup script internally via IsolatedPowerShellRunspace).
            var varSnapshot = _snapshotCapture(worker.PowerShell);
            worker.SetInitializedVariableSnapshot(varSnapshot);

            var driveSnapshot = _driveSnapshotCapture(worker.PowerShell);
            worker.SetInitializedDriveSnapshot(driveSnapshot);

            var funcSnapshot = _functionSnapshotCapture(worker.PowerShell);
            worker.SetInitializedFunctionSnapshot(funcSnapshot);

            var aliasSnapshot = _aliasSnapshotCapture(worker.PowerShell);
            worker.SetInitializedAliasSnapshot(aliasSnapshot);

            // Capture baseline internal-table counts for the skip-enumeration fast path.
            // These are read once here (before any request executes) and compared against
            // live table counts in ResetCore: when equal, no request-scoped names were added
            // and the expensive ~1700-entry enumeration is skipped entirely.
            // Guard: worker.PowerShell may be null in test mocks that don't set up Instance;
            // fall through gracefully (counts stay at -1, ResetCore uses full enumeration).
            var psForCounts = worker.PowerShell;
            if (psForCounts is not null &&
                SessionStateInternalAccessor.TryGetTables(
                    psForCounts.Runspace, out var vt, out var ft, out var at))
            {
                worker.SetInitializedTableCounts(vt.Count, ft.Count, at.Count);
            }

            if (!worker.TryTransitionTo(RunspaceWorkerState.Warm))
            {
                // Should never happen (Creating→Warm is always valid), but guard anyway.
                throw new InvalidOperationException("Unexpected state transition failure Creating→Warm.");
            }

            Interlocked.Increment(ref _warmCount);
            _metrics.WorkerCount.Add(1, RunspacePoolMetrics.StateTag("warm"));
            _allWorkers.TryAdd(worker, 0);

            if (!_available.Writer.TryWrite(worker))
            {
                // Channel full despite capacity proof — pool-accounting bug. Evict to prevent
                // a warm-but-unreachable worker that would cause _warmCount to diverge.
                _logger.LogCritical(
                    "BUG: bounded channel full when enqueuing warm worker. " +
                    "Worker will be orphaned-then-evicted. Max={Max}, Total={Total}.",
                    _options.MaxPoolSize, Volatile.Read(ref _totalCount));
                Interlocked.Decrement(ref _warmCount);
                _metrics.WorkerCount.Add(-1, RunspacePoolMetrics.StateTag("warm"));
                EvictWorker(worker, "channel_full");
                return;
            }

            _logger.LogDebug(
                "Worker {CreatedAt} initialized and enqueued. Total={Total}.",
                worker.CreatedAt, Volatile.Read(ref _totalCount));
        }
        catch (OperationCanceledException)
        {
            if (!factoryCompleted) Interlocked.Decrement(ref _creatingCount);
            _logger.LogDebug("Worker creation cancelled.");
            Interlocked.Decrement(ref _totalCount);

            if (worker is not null)
            {
                worker.TryTransitionTo(RunspaceWorkerState.Evicted);
                _allWorkers.TryRemove(worker, out _);
                worker.Dispose();
            }

            throw;  // Preserve cancellation semantics for Task.WhenAll callers (StartAsync).
        }
        catch (Exception ex)
        {
            if (!factoryCompleted) Interlocked.Decrement(ref _creatingCount);
            _logger.LogWarning(ex, "Worker startup failed; evicting without entering pool.");
            _metrics.StartupFailures.Add(1);
            Interlocked.Decrement(ref _totalCount);

            if (worker is not null)
            {
                // worker may have been transitioned to Warm but failed before/during channel write.
                // Decrement _warmCount only if worker reached Warm state.
                if (worker.State == RunspaceWorkerState.Warm)
                {
                    Interlocked.Decrement(ref _warmCount);
                    _metrics.WorkerCount.Add(-1, RunspacePoolMetrics.StateTag("warm"));
                }
                worker.TryTransitionTo(RunspaceWorkerState.Evicted);
                _allWorkers.TryRemove(worker, out _);
                worker.Dispose();
            }
        }
    }

    /// <summary>
    /// Fire-and-forget wrapper for <see cref="CreateWorkerAsync"/> that observes the task
    /// and silently swallows <see cref="OperationCanceledException"/> (expected on shutdown).
    /// </summary>
    private void FireAndForgetCreateWorkerAsync(CancellationToken ct)
        => ObserveCreation(CreateWorkerAsync(ct));

    /// <summary>
    /// Observes a fire-and-forget worker-creation task so an <see cref="OperationCanceledException"/>
    /// on shutdown is expected/ignored and only genuine faults surface. Shared by the replenisher
    /// loop and any other caller that starts a worker without awaiting it.
    /// </summary>
    private static void ObserveCreation(Task creation)
    {
        creation.ContinueWith(
            static t => { /* OCE on shutdown is expected; log only faults */ },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    // ─── Eviction helper ────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to evict a worker. Only succeeds if this call wins the <c>Evicted</c>
    /// CAS — exactly one racing caller will proceed; all others return without adjusting
    /// counters. Callers that pre-adjusted a state-specific counter (e.g., <c>_resettingCount</c>)
    /// before calling this method still own that adjustment regardless of the CAS outcome.
    /// </summary>
    private void EvictWorker(RunspaceWorker worker, string reason)
    {
        if (!worker.TryTransitionTo(RunspaceWorkerState.Evicted))
        {
            // Another path (e.g., ForceDisposeAllWorkers) already owns this eviction.
            // Do not decrement _totalCount a second time.
            return;
        }

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
            if (!_allWorkers.TryRemove(worker, out _))
                continue;

            // Use TryTransitionTo(out fromState) so the actual from-state drives counter
            // adjustments — not a separately-read snapshot that could be stale at CAS time.
            if (!worker.TryTransitionTo(RunspaceWorkerState.Evicted, out var fromState))
            {
                // Another path (OnWorkerReturnedAsync returning a lease) owns this eviction.
                continue;
            }

            Interlocked.Decrement(ref _totalCount);

            // Adjust only the counters we "own": Warm workers were not concurrently managed
            // by any active lease callback. Leased and Resetting workers are owned by
            // OnWorkerReturnedAsync which will decrement their counters when the callback fires.
            switch (fromState)
            {
                case RunspaceWorkerState.Warm:
                    Interlocked.Decrement(ref _warmCount);
                    _metrics.WorkerCount.Add(-1, RunspacePoolMetrics.StateTag("warm"));
                    break;
                    // Leased:    _leasedCount decremented in OnWorkerReturnedAsync early-return path.
                    // Resetting: _resettingCount decremented in OnWorkerReturnedAsync catch paths.
                    // Creating:  no state-specific counter.
            }

            _metrics.Evictions.Add(1, RunspacePoolMetrics.ReasonTag(reason));
            worker.Dispose();

            _logger.LogInformation(
                "Worker {CreatedAt} force-evicted from {FromState} (reason={Reason}). Total={Total}.",
                worker.CreatedAt, fromState, reason, Volatile.Read(ref _totalCount));
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

    private Task TryReplenishAsync(CancellationToken ct)
    {
        // Production path: start any deficit workers and observe each fire-and-forget so an
        // OCE on shutdown is swallowed. The counter is incremented inside the shared core.
        foreach (var creation in StartReplenishmentWorkers(ct))
            ObserveCreation(creation);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Shared replenishment core used by both the production loop and the
    /// <see cref="ReplenishOnceAsync"/> test seam. Computes the current deficit against
    /// <see cref="RunspacePoolOptions.MinPoolSize"/>, starts one <see cref="CreateWorkerAsync"/>
    /// per missing worker (bounded by <see cref="RunspacePoolOptions.MaxPoolSize"/>), and
    /// increments <c>replenishments_total</c> exactly once when at least one worker was started.
    /// Returns the started creation tasks so callers can fire-and-forget (loop) or await
    /// completion (seam) — the observable side effects are identical in both cases.
    /// </summary>
    private Task[] StartReplenishmentWorkers(CancellationToken ct)
    {
        if (Volatile.Read(ref _draining) != 0 || ct.IsCancellationRequested)
            return Array.Empty<Task>();

        int deficit = _options.MinPoolSize - Volatile.Read(ref _totalCount);
        if (deficit <= 0) return Array.Empty<Task>();

        var creations = new List<Task>(deficit);
        for (int i = 0; i < deficit; i++)
        {
            int current = Volatile.Read(ref _totalCount);
            if (current >= _options.MaxPoolSize) break;

            // CreateWorkerAsync increments _totalCount itself.
            creations.Add(CreateWorkerAsync(ct));
        }

        if (creations.Count > 0)
            _metrics.Replenishments.Add(1);

        return creations.ToArray();
    }

    /// <summary>
    /// Deterministic test seam mirroring <see cref="SweepOnce"/>. Runs exactly one replenishment
    /// pass through the same <see cref="StartReplenishmentWorkers"/> core the background loop uses,
    /// then awaits worker creation so callers observe terminal warm/worker-gauge restoration
    /// without polling or sleeps. The reentrancy guard guarantees a single caller cannot launch
    /// overlapping passes. Internal — for tests only; production replenishment is driven solely
    /// by the timer loop, which never calls this method.
    /// </summary>
    internal async Task ReplenishOnceAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _replenishPassActive, 1) != 0)
            throw new InvalidOperationException(
                "A replenishment pass is already in progress; overlapping passes are not permitted.");
        try
        {
            await Task.WhenAll(StartReplenishmentWorkers(ct)).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _replenishPassActive, 0);
        }
    }

    /// <summary>
    /// The <see cref="Meter"/> instance backing this pool's instruments. Exposed so tests can
    /// filter a <see cref="MeterListener"/> by meter <em>instance</em> (not name) and never
    /// attribute measurements from another pool sharing the same meter name.
    /// </summary>
    internal Meter MetricsMeter => _metrics.Meter;

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

    /// <summary>
    /// Evicts surplus warm workers that have been idle beyond <see cref="RunspacePoolOptions.IdleTtl"/>.
    /// Never evicts below <see cref="RunspacePoolOptions.MinPoolSize"/>.
    /// Exposed as <c>internal</c> so tests can trigger a sweep deterministically without waiting
    /// for the real sweep interval.
    /// </summary>
    internal void SweepOnce()
    {
        int warmCount = Volatile.Read(ref _warmCount);
        int surplus = warmCount - _options.MinPoolSize;
        if (surplus <= 0) return;

        var now = _clock();
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
