using System;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Threading;
using System.Threading.Tasks;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.Server.PowerShell;

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

    /// <summary>
    /// Creates an isolated PowerShell runspace with default test initialization
    /// </summary>
    public IsolatedPowerShellRunspace()
        : this(GetDefaultInitializationScript())
    {
    }

    /// <summary>
    /// Creates an isolated PowerShell runspace with custom initialization script
    /// </summary>
    /// <param name="initializationScript">Custom PowerShell script to run during initialization</param>
    public IsolatedPowerShellRunspace(string initializationScript)
    {
        _powerShell = PowerShellRunspaceInitializer.CreateInitializedRunspace(initializationScript);
        _runspace = _powerShell.Runspace;
    }

    private static string GetDefaultInitializationScript()
    {
        return @"
            # Set up some useful variables for testing
            $McpTestSessionStartTime = Get-Date
            $McpTestSessionId = [System.Guid]::NewGuid().ToString()
            
            Write-Host ""MCP Test PowerShell session initialized: $McpTestSessionId"" -ForegroundColor Yellow";
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
