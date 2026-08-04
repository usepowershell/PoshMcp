using System;
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
/// The pool is started explicitly during bootstrap (before <c>WebApplication.RunAsync()</c>)
/// so that tool discovery has warm workers available. <see cref="StartAsync"/> therefore only
/// confirms the pool is ready and logs its initial stats; it does not re-start the pool.
/// </para>
/// <para>
/// <see cref="StopAsync"/> drains the pool (stops accepting new acquisitions, waits for all
/// outstanding leases to be returned) and then disposes it asynchronously. This ensures all
/// background loops (sweeper, replenisher) terminate and all worker resources are released
/// before the process exits. Both <see cref="IRunspacePool.DrainAsync"/> and
/// <see cref="IAsyncDisposable.DisposeAsync"/> are idempotent, so double-invocation is safe.
/// </para>
/// </remarks>
internal sealed class RunspacePoolLifecycleService : IHostedService
{
    private readonly IRunspacePool _pool;
    private readonly ILogger<RunspacePoolLifecycleService> _logger;

    /// <summary>
    /// Creates a <see cref="RunspacePoolLifecycleService"/>.
    /// </summary>
    /// <param name="pool">The pool to manage; must already be started before host start.</param>
    /// <param name="logger">Logger for lifecycle events.</param>
    public RunspacePoolLifecycleService(IRunspacePool pool, ILogger<RunspacePoolLifecycleService> logger)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(logger);
        _pool = pool;
        _logger = logger;
    }

    /// <summary>
    /// Logs initial pool stats to confirm the pool is warm before the server accepts requests.
    /// The pool was already started during bootstrap so no startup work is performed here.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var stats = _pool.GetStats();
        _logger.LogInformation(
            "RunspacePool ready: Warm={Warm}, Total={Total}.",
            stats.WarmWorkers, stats.TotalWorkers);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Drains the pool (prevents new acquisitions, awaits return of all outstanding leases),
    /// then disposes it to release all worker resources and stop background loops.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Draining RunspacePool on host stop.");
        try
        {
            await _pool.DrainAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("RunspacePool drained; disposing.");
            await _pool.DisposeAsync().ConfigureAwait(false);
            _logger.LogInformation("RunspacePool disposed.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Error during RunspacePool drain/dispose.");
        }
    }
}
