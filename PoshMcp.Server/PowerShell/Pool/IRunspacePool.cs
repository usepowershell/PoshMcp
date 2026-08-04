using System;
using System.Threading;
using System.Threading.Tasks;

namespace PoshMcp.Server.PowerShell.Pool;

/// <summary>
/// Server-owned warm-worker pool for HTTP transport.
/// All HTTP requests lease a worker for the duration of one tool call, then return it for reset.
/// <c>Mcp-Session-Id</c> must never be used to select or retain a worker.
/// Stdio transport uses <see cref="PoshMcp.Server.PowerShell.SingletonPowerShellRunspace"/> and
/// is outside this pool.
/// </summary>
/// <remarks>
/// Implementation details (queuing, replenishment, sweep, metrics, reset, DI wiring) are deferred
/// to issue #348. This interface defines the acquisition and lifecycle contract only.
/// </remarks>
public interface IRunspacePool : IAsyncDisposable
{
    /// <summary>
    /// Initialises the pool: creates <see cref="RunspacePoolOptions.EagerWarmCount"/> warm workers
    /// and starts background sweeper/replenisher loops.
    /// </summary>
    /// <remarks>
    /// Callers must await this before the first <see cref="AcquireAsync"/> call.
    /// Throws <see cref="InvalidOperationException"/> if all eager workers fail to start.
    /// </remarks>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires a warm worker from the pool and returns a <see cref="RunspaceLease"/> that grants
    /// exclusive access to its <c>PSPowerShell</c> instance for one request.
    /// </summary>
    /// <param name="cancellationToken">
    /// Token that cancels the wait if no worker becomes available before the caller's deadline.
    /// The pool also applies <see cref="RunspacePoolOptions.AcquisitionTimeout"/> as an additional
    /// bounded timeout; whichever fires first terminates the wait.
    /// </param>
    /// <returns>
    /// A <see cref="RunspaceLease"/> whose disposal returns (or evicts) the worker.
    /// Callers must dispose the lease, preferably with <c>await using</c>.
    /// </returns>
    /// <exception cref="System.TimeoutException">
    /// <see cref="RunspacePoolOptions.AcquisitionTimeout"/> elapsed before a worker was available.
    /// </exception>
    /// <exception cref="System.OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled before a worker was available.
    /// </exception>
    /// <exception cref="System.ObjectDisposedException">
    /// The pool is draining or disposed; no new acquisitions are accepted.
    /// </exception>
    ValueTask<RunspaceLease> AcquireAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Signals the pool to stop accepting new acquisitions and waits for all outstanding leases
    /// to be returned.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancels the drain wait; the pool force-disposes remaining workers when the token fires or
    /// when <see cref="RunspacePoolOptions.ShutdownDrainTimeout"/> elapses.
    /// </param>
    Task DrainAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a point-in-time snapshot of pool utilization without acquiring a worker.
    /// </summary>
    RunspacePoolStats GetStats();
}
