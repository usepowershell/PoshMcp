using System;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Threading;
using System.Threading.Tasks;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Shared PowerShell runspace initialization logic
/// </summary>
public static class PowerShellRunspaceInitializer
{
    /// <summary>
    /// Creates and initializes a PowerShell runspace with common setup
    /// </summary>
    /// <param name="sessionType">Type of session (Production, Test, etc.)</param>
    /// <param name="customScript">Additional custom initialization script</param>
    /// <returns>Initialized PowerShell instance</returns>
    public static PSPowerShell CreateInitializedRunspace(string? customScript = "")
    {
        // Create a new runspace
        var runspace = RunspaceFactory.CreateRunspace();
        runspace.Open();

        // Create PowerShell instance and associate it with the runspace
        var powerShell = PSPowerShell.Create();
        powerShell.Runspace = runspace;

        // Build the common initialization script
        var baseScript = @"
            # Set execution policy for this session if on Windows
            if ($PSVersionTable.Platform -eq 'Windows') {
                Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process -Force
            }";

        var fullScript = baseScript;

        // Add custom script if provided
        if (!string.IsNullOrWhiteSpace(customScript))
        {
            fullScript += Environment.NewLine + customScript;
        }

        // Set up the PowerShell session with the combined script
        powerShell.AddScript(fullScript);

        // Execute the initialization script safely
        if (powerShell.Commands.Commands.Count > 0)
        {
            powerShell.Invoke();
        }

        // Clear any commands from the initialization
        powerShell.Commands.Clear();

        return powerShell;
    }
}
