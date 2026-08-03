using System;
using System.Threading;
using System.Threading.Tasks;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.Server.PowerShell.Pool;

/// <summary>
/// Grants exclusive, scoped access to a <see cref="RunspaceWorker"/> for one request.
/// Disposing the lease returns ownership to the pool, which then executes the reset protocol
/// and re-queues or evicts the worker.
/// </summary>
/// <remarks>
/// <para>
/// Disposal is exactly-once regardless of how many times <see cref="Dispose"/> or
/// <see cref="DisposeAsync"/> is called, and regardless of which overload is called first.
/// </para>
/// <para>
/// If the caller detects a corrupted runspace (e.g., <c>PSInvocationState.Running</c> after
/// a cancelled command), call <see cref="RequestEviction"/> before disposing to instruct the
/// pool to evict rather than reset-and-return the worker.
/// </para>
/// <para>
/// This type has no <c>IHttpContextAccessor</c> dependency. It must not be used to carry HTTP
/// session identity; one lease serves exactly one anonymous request.
/// </para>
/// </remarks>
public sealed class RunspaceLease : IDisposable, IAsyncDisposable
{
    private RunspaceWorker? _worker;
    private readonly Func<RunspaceWorker, bool, ValueTask> _onReturn;
    private volatile bool _evictionRequested;
    private int _disposed;

    /// <summary>
    /// Creates a lease for <paramref name="worker"/>.
    /// </summary>
    /// <param name="worker">The worker being leased.</param>
    /// <param name="onReturn">
    /// Callback invoked exactly once on disposal.
    /// <c>bool</c> parameter is <c>true</c> when eviction was requested via
    /// <see cref="RequestEviction"/>; the pool uses this to decide between reset-and-return or
    /// evict-and-dispose.
    /// </param>
    internal RunspaceLease(RunspaceWorker worker, Func<RunspaceWorker, bool, ValueTask> onReturn)
    {
        ArgumentNullException.ThrowIfNull(worker);
        ArgumentNullException.ThrowIfNull(onReturn);
        _worker = worker;
        _onReturn = onReturn;
    }

    /// <summary>
    /// The <c>PSPowerShell</c> instance to use for command execution during this lease.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The lease has already been disposed.</exception>
    public PSPowerShell PowerShell =>
        (_worker ?? throw new ObjectDisposedException(nameof(RunspaceLease))).PowerShell;

    /// <summary>
    /// Signals the pool that this worker should be evicted rather than reset and returned
    /// when the lease is disposed. Call before <see cref="Dispose"/> or <see cref="DisposeAsync"/>.
    /// </summary>
    public void RequestEviction() => _evictionRequested = true;

    /// <summary>
    /// Returns the worker to the pool synchronously. Exactly-once and idempotent.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var worker = Interlocked.Exchange(ref _worker, null)!;
        _onReturn(worker, _evictionRequested).AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Returns the worker to the pool asynchronously. Exactly-once and idempotent.
    /// Prefer this overload inside async call paths to avoid blocking a thread pool thread.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var worker = Interlocked.Exchange(ref _worker, null)!;
        await _onReturn(worker, _evictionRequested).ConfigureAwait(false);
    }
}
