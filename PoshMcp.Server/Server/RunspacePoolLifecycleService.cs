using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.PowerShell.Pool;

namespace PoshMcp.Server.Server;

/// <summary>
/// Manages the lifecycle of the HTTP <see cref="IRunspacePool"/> through the ASP.NET Core
/// hosted-service infrastructure.
/// </summary>
/// <remarks>
/// <para>
/// <b>Startup.</b> <see cref="StartAsync"/> calls <see cref="IRunspacePool.StartAsync"/> so that
/// eager-warm workers are ready before the host begins accepting requests. If pool startup fails
/// (all eager workers fail), the exception propagates and the host cannot start.
/// </para>
/// <para>
/// <b>Shutdown.</b> <see cref="StopAsync"/> drains the pool (stops new acquisitions, awaits
/// outstanding leases), then disposes it. Disposal is guaranteed even if drain fails; if both
/// drain and dispose fail the exceptions are combined in an <see cref="AggregateException"/>.
/// </para>
/// </remarks>
internal sealed class RunspacePoolLifecycleService : IHostedService
{
    private readonly IRunspacePool _pool;
    private readonly ILogger<RunspacePoolLifecycleService> _logger;

    /// <summary>
    /// Creates a <see cref="RunspacePoolLifecycleService"/>.
    /// </summary>
    /// <param name="pool">The pool to manage.</param>
    /// <param name="logger">Logger for lifecycle events.</param>
    public RunspacePoolLifecycleService(IRunspacePool pool, ILogger<RunspacePoolLifecycleService> logger)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(logger);
        _pool = pool;
        _logger = logger;
    }

    /// <summary>
    /// Starts the pool and blocks until eager-warm workers are ready.
    /// The host will not accept requests until this method returns successfully.
    /// Propagates any exception from <see cref="IRunspacePool.StartAsync"/> so that a
    /// failed-startup condition (all eager workers dead) prevents the host from opening.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _pool.StartAsync(cancellationToken).ConfigureAwait(false);
        var stats = _pool.GetStats();
        _logger.LogInformation(
            "RunspacePool ready: Warm={Warm}, Total={Total}.",
            stats.WarmWorkers, stats.TotalWorkers);
    }

    /// <summary>
    /// Drains the pool (prevents new acquisitions, awaits return of all outstanding leases),
    /// then disposes it to release all worker resources and stop background loops.
    /// Disposal is guaranteed even if drain fails. If both drain and dispose fail, an
    /// <see cref="AggregateException"/> containing both is thrown. If only one fails, that
    /// exception is surfaced with its original stack trace preserved.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Draining RunspacePool on host stop.");
        Exception? drainException = null;
        try
        {
            await _pool.DrainAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("RunspacePool drained; disposing.");
        }
        catch (Exception ex)
        {
            drainException = ex;
            _logger.LogError(ex, "Error during RunspacePool drain; will still dispose.");
        }

        Exception? disposeException = null;
        try
        {
            await _pool.DisposeAsync().ConfigureAwait(false);
            _logger.LogInformation("RunspacePool disposed.");
        }
        catch (Exception ex)
        {
            disposeException = ex;
            _logger.LogError(ex, "Error disposing RunspacePool.");
        }

        if (drainException is not null && disposeException is not null)
            throw new AggregateException(
                "RunspacePool drain and dispose both failed.", drainException, disposeException);

        if (disposeException is not null)
            ExceptionDispatchInfo.Capture(disposeException).Throw();

        if (drainException is not null)
            ExceptionDispatchInfo.Capture(drainException).Throw();
    }
}
