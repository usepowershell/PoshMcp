using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.PowerShell.Pool;

namespace PoshMcp.Server.Health;

/// <summary>
/// Health check for the HTTP warm-worker runspace pool.
/// Reports Healthy, Degraded, or Unhealthy based on pool state and available capacity.
/// Reads only <see cref="IRunspacePool.GetStats"/>; never acquires a worker.
/// </summary>
/// <remarks>
/// Classification uses <c>(WarmWorkers + LeasedWorkers) >= MinPoolSize</c> as the
/// Healthy threshold. This correctly distinguishes two fundamentally different states:
/// <list type="bullet">
///   <item><b>Healthy (at capacity):</b> All MinPoolSize workers exist and are in service
///     (warm=idle or leased=actively serving a request). A pool at full utilization is healthy.</item>
///   <item><b>Degraded/Unhealthy:</b> The total number of active (non-evicted, non-failed)
///     workers has dropped below MinPoolSize — workers have been genuinely lost.</item>
/// </list>
/// Using warm-only (<c>WarmWorkers >= MinPoolSize</c>) would create an observer-interference
/// false negative: liveness health checks that acquire pool leases while this check
/// concurrently reads stats would cause this check to report Unhealthy even though the pool
/// is serving the minimum number of concurrent requests correctly.
/// </remarks>
public sealed class RunspacePoolHealthCheck : IHealthCheck
{
    private readonly IRunspacePool _pool;
    private readonly ILogger<RunspacePoolHealthCheck> _logger;

    public RunspacePoolHealthCheck(
        IRunspacePool pool,
        ILogger<RunspacePoolHealthCheck> logger)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stats = _pool.GetStats();

            var data = new Dictionary<string, object>
            {
                ["warm"] = stats.WarmWorkers,
                ["leased"] = stats.LeasedWorkers,
                ["resetting"] = stats.ResettingWorkers,
                ["creating"] = stats.CreatingWorkers,
                ["total"] = stats.TotalWorkers,
                ["min"] = stats.MinPoolSize,
                ["max"] = stats.MaxPoolSize,
                ["is_started"] = stats.IsStarted,
                ["is_draining"] = stats.IsDraining,
            };

            // Classification precedence:
            // 1. Not started → Unhealthy (initializing/not yet ready)
            if (!stats.IsStarted)
            {
                _logger.LogDebug("Runspace pool health check: not started (initializing).");
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "Runspace pool not started (initializing)", data: data));
            }

            // 2. Draining → Degraded (blocks readiness)
            if (stats.IsDraining)
            {
                _logger.LogDebug("Runspace pool health check: draining.");
                return Task.FromResult(HealthCheckResult.Degraded(
                    "Runspace pool is draining", data: data));
            }

            // 3. (Warm + Leased) >= Min → Healthy.
            // Leased workers are actively serving requests — they are not lost capacity.
            // Counting only Warm would create a false-negative when other checks hold leases.
            int available = stats.WarmWorkers + stats.LeasedWorkers;
            if (available >= stats.MinPoolSize)
            {
                _logger.LogDebug(
                    "Runspace pool health check: healthy. Warm={Warm}, Leased={Leased}, " +
                    "Available={Available}/{Min}.",
                    stats.WarmWorkers, stats.LeasedWorkers, available, stats.MinPoolSize);
                return Task.FromResult(HealthCheckResult.Healthy(
                    $"Runspace pool healthy: {stats.WarmWorkers}/{stats.MinPoolSize} warm " +
                    $"(+{stats.LeasedWorkers} leased)",
                    data: data));
            }

            // 4. (Warm + Leased) < Min and creation in progress → Degraded
            if (stats.CreatingWorkers > 0)
            {
                _logger.LogDebug(
                    "Runspace pool health check: degraded. Available={Available}/{Min}, Creating={Creating}.",
                    available, stats.MinPoolSize, stats.CreatingWorkers);
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"Runspace pool degraded: {available}/{stats.MinPoolSize} workers available, " +
                    $"{stats.CreatingWorkers} creating",
                    data: data));
            }

            // 5. (Warm + Leased) < Min, no workers creating → Unhealthy
            _logger.LogWarning(
                "Runspace pool health check: unhealthy. Available={Available}/{Min}, no workers creating.",
                available, stats.MinPoolSize);
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Runspace pool unhealthy: {available}/{stats.MinPoolSize} workers available, " +
                "no workers creating",
                data: data));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Runspace pool health check threw exception.");
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Health check failed: {ex.Message}", ex));
        }
    }
}
