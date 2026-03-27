using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.PowerShell;

namespace PoshMcp.Server.Health;

/// <summary>
/// Health check for PowerShell dynamic assembly generation
/// </summary>
public class AssemblyGenerationHealthCheck : IHealthCheck
{
    private readonly IPowerShellRunspace _runspace;
    private readonly ILogger<AssemblyGenerationHealthCheck> _logger;

    public AssemblyGenerationHealthCheck(
        IPowerShellRunspace runspace,
        ILogger<AssemblyGenerationHealthCheck> logger)
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
            // Test that we can create an assembly generator
            var generator = new PowerShellAssemblyGenerator(_runspace);

            if (generator == null)
            {
                _logger.LogWarning("Assembly generator creation returned null");
                return HealthCheckResult.Degraded("Assembly generator is null");
            }

            // Check if we can introspect PowerShell commands (basic functionality)
            var canIntrospect = await Task.Run(() =>
            {
                try
                {
                    var commandInfo = _runspace.ExecuteThreadSafe(ps =>
                    {
                        ps.Commands.Clear();
                        ps.AddCommand("Get-Command").AddParameter("Name", "Get-Date");
                        var results = ps.Invoke();

                        if (ps.HadErrors)
                        {
                            return false;
                        }

                        return results.Count > 0;
                    });

                    return commandInfo;
                }
                catch
                {
                    return false;
                }
            }, cancellationToken);

            if (canIntrospect)
            {
                _logger.LogDebug("Assembly generation subsystem health check passed");
                return HealthCheckResult.Healthy("Assembly generation ready");
            }
            else
            {
                _logger.LogWarning("Assembly generation health check: Cannot introspect PowerShell commands");
                return HealthCheckResult.Degraded("Cannot introspect PowerShell commands");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Assembly generation health check failed");
            return HealthCheckResult.Unhealthy($"Assembly generation check failed: {ex.Message}", ex);
        }
    }
}
