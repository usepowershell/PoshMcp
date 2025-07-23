using System;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Threading;
using System.Threading.Tasks;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.PowerShell;

/// <summary>
/// Production implementation using the singleton pattern for the server
/// </summary>
public class SingletonPowerShellRunspace : IPowerShellRunspace
{
    public PSPowerShell Instance => PowerShellRunspaceHolder.Instance;

    public T ExecuteThreadSafe<T>(Func<PSPowerShell, T> operation)
    {
        return PowerShellRunspaceHolder.ExecuteThreadSafe(operation);
    }

    public void ExecuteThreadSafe(Action<PSPowerShell> operation)
    {
        PowerShellRunspaceHolder.ExecuteThreadSafe(operation);
    }

    public Task<T> ExecuteThreadSafeAsync<T>(Func<PSPowerShell, Task<T>> operation)
    {
        return PowerShellRunspaceHolder.ExecuteThreadSafeAsync(operation);
    }

    public void Dispose()
    {
        // No disposal needed for singleton
    }
}

/// <summary>
/// Test implementation that creates isolated PowerShell instances
/// </summary>
public class IsolatedPowerShellRunspace : IPowerShellRunspace, IDisposable
{
    private readonly PSPowerShell _powerShell;
    private readonly Runspace _runspace;
    private readonly object _lock = new object();
    private bool _disposed = false;

    public IsolatedPowerShellRunspace()
    {
        // Create a new runspace for this test instance
        _runspace = RunspaceFactory.CreateRunspace();
        _runspace.Open();

        // Create PowerShell instance and associate it with the runspace
        _powerShell = PSPowerShell.Create();
        _powerShell.Runspace = _runspace;

        // Set up the PowerShell session with basic configuration
        _powerShell.AddScript(@"
            # Set execution policy for this session if on Windows
            if ($PSVersionTable.Platform -eq 'Windows') {
                Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process -Force
            } 
        
            # Set up some useful variables for testing
            $McpTestSessionStartTime = Get-Date
            $McpTestSessionId = [System.Guid]::NewGuid().ToString()
            
            Write-Host ""MCP Test PowerShell session initialized: $McpTestSessionId"" -ForegroundColor Yellow
        ");

        // Execute the initialization script safely
        if (_powerShell.Commands.Commands.Count > 0)
        {
            _powerShell.Invoke();
        }

        // Clear any commands from the initialization
        _powerShell.Commands.Clear();
    }

    public PSPowerShell Instance
    {
        get
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(IsolatedPowerShellRunspace));
            return _powerShell;
        }
    }

    public T ExecuteThreadSafe<T>(Func<PSPowerShell, T> operation)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(IsolatedPowerShellRunspace));

        lock (_lock)
        {
            return operation(_powerShell);
        }
    }

    public void ExecuteThreadSafe(Action<PSPowerShell> operation)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(IsolatedPowerShellRunspace));

        lock (_lock)
        {
            operation(_powerShell);
        }
    }

    public async Task<T> ExecuteThreadSafeAsync<T>(Func<PSPowerShell, Task<T>> operation)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(IsolatedPowerShellRunspace));

        // Use async-compatible locking with SemaphoreSlim
        return await ExecuteWithSemaphore(operation);
    }

    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

    private async Task<T> ExecuteWithSemaphore<T>(Func<PSPowerShell, Task<T>> operation)
    {
        await _semaphore.WaitAsync();
        try
        {
            return await operation(_powerShell);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_lock)
        {
            try
            {
                // Clear any pending commands before disposal to prevent empty pipeline issues
                if (_powerShell != null)
                {
                    if (_powerShell.InvocationStateInfo.State == PSInvocationState.Running)
                    {
                        _powerShell.Stop();
                    }
                    _powerShell.Commands?.Clear();
                    _powerShell.Dispose();
                }
            }
            catch (Exception)
            {
                // Ignore disposal errors to prevent crashes during cleanup
            }

            try
            {
                _runspace?.Dispose();
            }
            catch (Exception)
            {
                // Ignore disposal errors to prevent crashes during cleanup
            }

            try
            {
                _semaphore?.Dispose();
            }
            catch (Exception)
            {
                // Ignore disposal errors to prevent crashes during cleanup
            }

            _disposed = true;
        }
    }
}
