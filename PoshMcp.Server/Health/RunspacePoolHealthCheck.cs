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
/// Reports Healthy, Degraded, or Unhealthy based on pool state and warm/min worker counts.
/// Reads only <see cref="IRunspacePool.GetStats"/>; never acquires a worker.
/// </summary>
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

            // Classification precedence (ceremony decision 3):
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

            // 3. Warm >= Min → Healthy
            if (stats.WarmWorkers >= stats.MinPoolSize)
            {
                _logger.LogDebug(
                    "Runspace pool health check: healthy. Warm={Warm}/{Min}.",
                    stats.WarmWorkers, stats.MinPoolSize);
                return Task.FromResult(HealthCheckResult.Healthy(
                    $"Runspace pool healthy: {stats.WarmWorkers}/{stats.MinPoolSize} warm workers",
                    data: data));
            }

            // 4. Warm < Min and creation in progress → Degraded
            if (stats.CreatingWorkers > 0)
            {
                _logger.LogDebug(
                    "Runspace pool health check: degraded. Warm={Warm}/{Min}, Creating={Creating}.",
                    stats.WarmWorkers, stats.MinPoolSize, stats.CreatingWorkers);
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"Runspace pool degraded: {stats.WarmWorkers}/{stats.MinPoolSize} warm workers, " +
                    $"{stats.CreatingWorkers} creating",
                    data: data));
            }

            // 5. Warm < Min, no workers creating → Unhealthy
            _logger.LogWarning(
                "Runspace pool health check: unhealthy. Warm={Warm}/{Min}, no workers creating.",
                stats.WarmWorkers, stats.MinPoolSize);
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Runspace pool unhealthy: {stats.WarmWorkers}/{stats.MinPoolSize} warm workers, " +
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
