using System;
using System.Management.Automation;
using System.Threading.Tasks;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.PowerShell;

/// <summary>
/// Interface for PowerShell runspace operations, allowing for different implementations
/// (singleton for production, isolated instances for testing)
/// </summary>
public interface IPowerShellRunspace
{
    /// <summary>
    /// Gets the PowerShell instance for operations
    /// </summary>
    PSPowerShell Instance { get; }

    /// <summary>
    /// Execute PowerShell operations in a thread-safe manner
    /// </summary>
    T ExecuteThreadSafe<T>(Func<PSPowerShell, T> operation);

    /// <summary>
    /// Execute PowerShell operations in a thread-safe manner
    /// </summary>
    void ExecuteThreadSafe(Action<PSPowerShell> operation);

    /// <summary>
    /// Execute async PowerShell operations in a thread-safe manner
    /// </summary>
    Task<T> ExecuteThreadSafeAsync<T>(Func<PSPowerShell, Task<T>> operation);

    /// <summary>
    /// Cleanup resources (important for test instances)
    /// </summary>
    void Dispose();
}
