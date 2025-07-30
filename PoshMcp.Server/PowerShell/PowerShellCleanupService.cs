using System;
using System.Management.Automation;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Hosted service to properly clean up PowerShell runspace on application shutdown
/// </summary>
public class PowerShellCleanupService : IHostedService
{
    private readonly ILogger<PowerShellCleanupService> _logger;

    public PowerShellCleanupService(ILogger<PowerShellCleanupService> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("PowerShell cleanup service started");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cleaning up PowerShell runspace");

        try
        {
            // Get the PowerShell instance to dispose it properly
            var powerShell = PowerShellRunspaceHolder.Instance;
            var runspace = powerShell.Runspace;

            // Clear any pending commands before disposal to prevent empty pipeline issues
            if (powerShell.InvocationStateInfo.State == System.Management.Automation.PSInvocationState.NotStarted ||
                powerShell.InvocationStateInfo.State == System.Management.Automation.PSInvocationState.Completed)
            {
                powerShell.Commands?.Clear();
            }
            else if (powerShell.InvocationStateInfo.State == System.Management.Automation.PSInvocationState.Running)
            {
                // Stop any running operations before disposal
                powerShell.Stop();
                powerShell.Commands?.Clear();
            }

            powerShell.Dispose();
            runspace?.Dispose();

            _logger.LogInformation("PowerShell runspace disposed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing PowerShell runspace");
        }

        return Task.CompletedTask;
    }
}
