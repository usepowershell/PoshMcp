using System;
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
    {
        int current = Volatile.Read(ref _state);

        if (!IsValidTransition((RunspaceWorkerState)current, target))
            return false;

        int prev = Interlocked.CompareExchange(ref _state, (int)target, current);
        if (prev != current)
            return false;

        if (target == RunspaceWorkerState.Warm && (RunspaceWorkerState)current == RunspaceWorkerState.Resetting)
            LastLeaseCompletedAt = DateTimeOffset.UtcNow;

        return true;
    }

    /// <inheritdoc cref="RunspaceWorkerState"/>
    private static bool IsValidTransition(RunspaceWorkerState from, RunspaceWorkerState to) => (from, to) switch
    {
        (RunspaceWorkerState.Creating,   RunspaceWorkerState.Warm)      => true,
        (RunspaceWorkerState.Creating,   RunspaceWorkerState.Evicted)   => true,
        (RunspaceWorkerState.Warm,       RunspaceWorkerState.Leased)    => true,
        (RunspaceWorkerState.Warm,       RunspaceWorkerState.Evicted)   => true,
        (RunspaceWorkerState.Leased,     RunspaceWorkerState.Resetting) => true,
        (RunspaceWorkerState.Leased,     RunspaceWorkerState.Evicted)   => true,
        (RunspaceWorkerState.Resetting,  RunspaceWorkerState.Warm)      => true,
        (RunspaceWorkerState.Resetting,  RunspaceWorkerState.Evicted)   => true,
        (RunspaceWorkerState.Evicted,    RunspaceWorkerState.Disposed)  => true,
        _ => false
    };

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
