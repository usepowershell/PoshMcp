using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.PowerShell;

namespace PoshMcp.Server.Health;

/// <summary>
/// Health check for PowerShell runspace responsiveness
/// </summary>
public class PowerShellRunspaceHealthCheck : IHealthCheck
{
    private readonly IPowerShellRunspace _runspace;
    private readonly ILogger<PowerShellRunspaceHealthCheck> _logger;
    private static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromMilliseconds(500);

    public PowerShellRunspaceHealthCheck(
        IPowerShellRunspace runspace,
        ILogger<PowerShellRunspaceHealthCheck> logger)
    {
        _runspace = runspace ?? throw new ArgumentNullException(nameof(runspace));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Test runspace responsiveness with a simple command, enforcing timeout
            var healthCheckTask = Task.Run(() =>
            {
                try
                {
                    var output = _runspace.ExecuteThreadSafe(ps =>
                    {
                        ps.Commands.Clear();
                        ps.AddScript("1 + 1");
                        var results = ps.Invoke();

                        if (ps.HadErrors)
                        {
                            var errors = string.Join("; ", ps.Streams.Error);
                            return (false, $"PowerShell execution had errors: {errors}");
                        }

                        if (results.Count == 0)
                        {
                            return (false, "PowerShell returned no results");
                        }

                        return (true, "PowerShell runspace responsive");
                    });

                    return output;
                }
                catch (Exception ex)
                {
                    return (false, $"Exception during health check: {ex.Message}");
                }
            }, cancellationToken);

            // Enforce timeout to ensure health check completes within 500ms
            var result = await healthCheckTask.WaitAsync(HealthCheckTimeout, cancellationToken);

            if (result.Item1)
            {
                _logger.LogDebug("PowerShell runspace health check passed");
                return HealthCheckResult.Healthy(result.Item2);
            }
            else
            {
                _logger.LogWarning("PowerShell runspace health check failed: {Reason}", result.Item2);
                return HealthCheckResult.Unhealthy(result.Item2);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("PowerShell runspace health check cancelled or timed out");
            return HealthCheckResult.Unhealthy("Health check cancelled or timed out");
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("PowerShell runspace health check exceeded {Timeout}ms timeout", HealthCheckTimeout.TotalMilliseconds);
            return HealthCheckResult.Unhealthy($"Health check exceeded {HealthCheckTimeout.TotalMilliseconds}ms timeout");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PowerShell runspace health check threw exception");
            return HealthCheckResult.Unhealthy($"Health check failed: {ex.Message}", ex);
        }
    }
}
