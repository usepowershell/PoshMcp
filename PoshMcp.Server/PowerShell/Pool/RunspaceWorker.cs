using System;
using System.Collections.Generic;
using System.Threading;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.Server.PowerShell.Pool;

/// <summary>
/// A single warm-worker runspace in the HTTP pool, wrapping an <see cref="IPowerShellRunspace"/>
/// and enforcing the pool lifecycle state machine with thread-safe transitions.
/// </summary>
/// <remarks>
/// <para>
/// Workers are owned by <see cref="IRunspacePool"/> implementations. Callers interact exclusively
/// through <see cref="RunspaceLease"/>; they must not access workers directly.
/// </para>
/// <para>
/// State transitions are atomic: <see cref="TryTransitionTo"/> uses
/// <see cref="Interlocked.CompareExchange(ref int, int, int)"/> so concurrent racing callers are
/// correctly arbitrated — exactly one wins; the rest receive <c>false</c>.
/// </para>
/// <para>
/// <see cref="Dispose"/> is the terminal cleanup operation and bypasses the state machine; call it
/// only after the worker has been removed from the pool (typically after entering
/// <see cref="RunspaceWorkerState.Evicted"/>).
/// </para>
/// </remarks>
public sealed class RunspaceWorker : IDisposable
{
    private readonly IPowerShellRunspace _runspace;
    private int _state = (int)RunspaceWorkerState.Creating;
    private int _disposed;

    /// <param name="runspace">
    /// The pre-initialized (or being-initialized) isolated runspace that backs this worker.
    /// The worker takes ownership and disposes it on <see cref="Dispose"/>.
    /// </param>
    public RunspaceWorker(IPowerShellRunspace runspace)
    {
        ArgumentNullException.ThrowIfNull(runspace);
        _runspace = runspace;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Current lifecycle state. Snapshot only; state may advance concurrently.</summary>
    public RunspaceWorkerState State => (RunspaceWorkerState)Volatile.Read(ref _state);

    /// <summary>UTC timestamp recorded when the worker was constructed.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// UTC timestamp of the most recent <c>Resetting → Warm</c> transition, or <c>null</c> if the
    /// worker has never completed a lease cycle.
    /// Updated atomically as a side-effect of a successful <c>Resetting → Warm</c> transition.
    /// </summary>
    public DateTimeOffset? LastLeaseCompletedAt { get; private set; }

    /// <summary>
    /// The underlying <c>PSPowerShell</c> instance.
    /// Valid only while <see cref="State"/> is <see cref="RunspaceWorkerState.Warm"/>,
    /// <see cref="RunspaceWorkerState.Leased"/>, or <see cref="RunspaceWorkerState.Resetting"/>.
    /// </summary>
    public PSPowerShell PowerShell => _runspace.Instance;

    /// <summary>
    /// Attempts to transition the worker from its current state to <paramref name="target"/>.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the transition was allowed and the CAS succeeded.
    /// <c>false</c> if the transition is not valid from the current state, or if a concurrent
    /// caller won the race.
    /// </returns>
    public bool TryTransitionTo(RunspaceWorkerState target)
        => TryTransitionTo(target, out _);

    /// <summary>
    /// Attempts to transition the worker to <paramref name="target"/> and, on success,
    /// returns the actual state the worker was in immediately before the transition via
    /// <paramref name="actualFrom"/>. This is the authoritative from-state because it is the
    /// value that the CAS was performed against — callers can use it to update state-specific
    /// counters exactly once without a separate Volatile.Read race.
    /// </summary>
    /// <returns>
    /// <c>true</c> and <paramref name="actualFrom"/> set to the pre-transition state if the CAS
    /// succeeded. <c>false</c> (and <paramref name="actualFrom"/> set to the current actual state)
    /// if the transition is not valid or a concurrent caller won the race.
    /// </returns>
    public bool TryTransitionTo(RunspaceWorkerState target, out RunspaceWorkerState actualFrom)
    {
        int current = Volatile.Read(ref _state);
        actualFrom = (RunspaceWorkerState)current;

        if (!IsValidTransition(actualFrom, target))
            return false;

        int prev = Interlocked.CompareExchange(ref _state, (int)target, current);
        if (prev != current)
        {
            actualFrom = (RunspaceWorkerState)prev;
            return false;
        }

        if (target == RunspaceWorkerState.Warm && actualFrom == RunspaceWorkerState.Resetting)
            LastLeaseCompletedAt = DateTimeOffset.UtcNow;

        return true;
    }

    /// <inheritdoc cref="RunspaceWorkerState"/>
    private static bool IsValidTransition(RunspaceWorkerState from, RunspaceWorkerState to) => (from, to) switch
    {
        (RunspaceWorkerState.Creating, RunspaceWorkerState.Warm) => true,
        (RunspaceWorkerState.Creating, RunspaceWorkerState.Evicted) => true,
        (RunspaceWorkerState.Warm, RunspaceWorkerState.Leased) => true,
        (RunspaceWorkerState.Warm, RunspaceWorkerState.Evicted) => true,
        (RunspaceWorkerState.Leased, RunspaceWorkerState.Resetting) => true,
        (RunspaceWorkerState.Leased, RunspaceWorkerState.Evicted) => true,
        (RunspaceWorkerState.Resetting, RunspaceWorkerState.Warm) => true,
        (RunspaceWorkerState.Resetting, RunspaceWorkerState.Evicted) => true,
        (RunspaceWorkerState.Evicted, RunspaceWorkerState.Disposed) => true,
        _ => false
    };

    /// <summary>
    /// Captured variable names present in the runspace immediately after the startup script ran.
    /// <see cref="RunspaceResetProtocol"/> uses this to exclude worker-initialized state from
    /// variable cleanup during reset.
    /// </summary>
    internal IReadOnlySet<string>? InitializedVariableNames { get; private set; }

    /// <summary>
    /// Captured PSDrive names present in the runspace immediately after the startup script ran.
    /// <see cref="RunspaceResetProtocol"/> uses this to exclude worker-initialized drives from
    /// drive cleanup during reset.
    /// </summary>
    internal IReadOnlySet<string>? InitializedDriveNames { get; private set; }

    /// <summary>
    /// Captured function names present in the runspace immediately after the startup script ran.
    /// <see cref="RunspaceResetProtocol"/> uses this to exclude worker-initialized functions from
    /// function cleanup during reset.
    /// </summary>
    internal IReadOnlySet<string>? InitializedFunctionNames { get; private set; }

    /// <summary>
    /// Captured alias names present in the runspace immediately after the startup script ran.
    /// <see cref="RunspaceResetProtocol"/> uses this to exclude worker-initialized aliases from
    /// alias cleanup during reset.
    /// </summary>
    internal IReadOnlySet<string>? InitializedAliasNames { get; private set; }

    /// <summary>
    /// Precomputed variable exclusion set for reset (automatic + initialized). Lazily sealed by
    /// <see cref="RunspaceResetProtocol"/> so the hot path does not rebuild HashSets per call.
    /// </summary>
    internal IReadOnlySet<string>? ResetExcludeVariables { get; set; }

    /// <summary>
    /// Precomputed drive exclusion set for reset. Lazily sealed by <see cref="RunspaceResetProtocol"/>.
    /// </summary>
    internal IReadOnlySet<string>? ResetExcludeDrives { get; set; }

    /// <summary>
    /// Precomputed function exclusion set for reset. Lazily sealed by <see cref="RunspaceResetProtocol"/>.
    /// </summary>
    internal IReadOnlySet<string>? ResetExcludeFunctions { get; set; }

    /// <summary>
    /// Precomputed alias exclusion set for reset. Lazily sealed by <see cref="RunspaceResetProtocol"/>.
    /// </summary>
    internal IReadOnlySet<string>? ResetExcludeAliases { get; set; }

    /// <summary>
    /// Records the set of variable names present in the runspace immediately after the startup
    /// script completed. Must be called exactly once during the <c>Creating → Warm</c> transition,
    /// before the worker is enqueued in the available channel.
    /// </summary>
    internal void SetInitializedVariableSnapshot(IReadOnlySet<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        InitializedVariableNames = names;
        ResetExcludeVariables = null; // force reseal if snapshots change
    }

    /// <summary>
    /// Records the set of PSDrive names present in the runspace immediately after the startup
    /// script completed. Must be called exactly once during the <c>Creating → Warm</c> transition,
    /// before the worker is enqueued in the available channel.
    /// </summary>
    internal void SetInitializedDriveSnapshot(IReadOnlySet<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        InitializedDriveNames = names;
        ResetExcludeDrives = null;
    }

    /// <summary>
    /// Records the set of function names present in the runspace immediately after the startup
    /// script completed. Must be called exactly once during the <c>Creating → Warm</c> transition,
    /// before the worker is enqueued in the available channel.
    /// </summary>
    internal void SetInitializedFunctionSnapshot(IReadOnlySet<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        InitializedFunctionNames = names;
        ResetExcludeFunctions = null;
    }

    /// <summary>
    /// Records the set of alias names present in the runspace immediately after the startup
    /// script completed. Must be called exactly once during the <c>Creating → Warm</c> transition,
    /// before the worker is enqueued in the available channel.
    /// </summary>
    internal void SetInitializedAliasSnapshot(IReadOnlySet<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        InitializedAliasNames = names;
        ResetExcludeAliases = null;
    }

    /// <summary>
    /// Releases all resources held by this worker. Idempotent and thread-safe.
    /// Should only be called by the pool after the worker has been removed from all queues.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Volatile.Write(ref _state, (int)RunspaceWorkerState.Disposed);
        _runspace.Dispose();
    }
}
