using System;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Threading;
using System.Threading.Tasks;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.PowerShell;

/// <summary>
/// Holder class to maintain a singleton PowerShell runspace instance with thread-safe access
/// </summary>
public static class PowerShellRunspaceHolder
{
    private static readonly Lazy<PSPowerShell> _instance = new(() =>
    {
        // Create a new runspace
        var runspace = RunspaceFactory.CreateRunspace();
        runspace.Open();

        // Create PowerShell instance and associate it with the runspace
        var powerShell = PSPowerShell.Create();
        powerShell.Runspace = runspace;

        // Set up the PowerShell session with some initial configuration
        powerShell.AddScript(@"
            # Set execution policy for this session if on Windows
            if ($PSVersionTable.Platform -eq 'Windows') {
                Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process -Force
            } 
        
            # Set up some useful variables
            $McpServerStartTime = Get-Date
            $McpServerVersion = '1.0.0'
            
            # Create a function to get session info
            function Get-McpSessionInfo {
                return @{
                    StartTime = $McpServerStartTime
                    Version = $McpServerVersion
                    Location = Get-Location
                    Variables = (Get-Variable | Measure-Object).Count
                    Functions = (Get-ChildItem Function: | Measure-Object).Count
                    Modules = (Get-Module | Measure-Object).Count
                }
            }
            
            Write-Host 'MCP PowerShell session initialized' -ForegroundColor Green

            function Get-SomeData ([string]$test = 'This is some persistent data from the MCP server.') {
                # Example function to demonstrate state persistence
                return $test
            }
        ");

        // Execute the initialization script safely
        if (powerShell.Commands.Commands.Count > 0)
        {
            powerShell.Invoke();
        }

        // Clear any commands from the initialization
        powerShell.Commands.Clear();

        return powerShell;
    });

    private static readonly object _lock = new object();
    private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

    public static PSPowerShell Instance
    {
        get
        {
            lock (_lock)
            {
                return _instance.Value;
            }
        }
    }

    /// <summary>
    /// Execute PowerShell operations in a thread-safe manner
    /// </summary>
    public static T ExecuteThreadSafe<T>(Func<PSPowerShell, T> operation)
    {
        lock (_lock)
        {
            var ps = Instance;
            return operation(ps);
        }
    }

    /// <summary>
    /// Execute PowerShell operations in a thread-safe manner
    /// </summary>
    public static void ExecuteThreadSafe(Action<PSPowerShell> operation)
    {
        lock (_lock)
        {
            var ps = Instance;
            operation(ps);
        }
    }

    /// <summary>
    /// Execute async PowerShell operations in a thread-safe manner
    /// </summary>
    public static async Task<T> ExecuteThreadSafeAsync<T>(Func<PSPowerShell, Task<T>> operation)
    {
        await _semaphore.WaitAsync();
        try
        {
            return await operation(Instance);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
