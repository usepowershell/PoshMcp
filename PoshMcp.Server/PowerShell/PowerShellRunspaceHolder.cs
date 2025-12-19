using System;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Holder class to maintain a singleton PowerShell runspace instance with thread-safe access
/// </summary>
public static class PowerShellRunspaceHolder
{
    private static Lazy<PSPowerShell>? _instance;
    private static readonly object _lock = new object();
    private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
    private static PowerShellConfiguration? _configuration;
    private static ILogger? _logger;
    private static bool _isInitialized = false;

    /// <summary>
    /// Initializes the PowerShell runspace holder with configuration and logger
    /// Must be called before accessing Instance. Can only be called once.
    /// </summary>
    /// <param name="config">PowerShell configuration</param>
    /// <param name="logger">Logger for diagnostics</param>
    /// <exception cref="InvalidOperationException">Thrown if Initialize is called more than once</exception>
    public static void Initialize(PowerShellConfiguration config, ILogger logger)
    {
        lock (_lock)
        {
            if (_isInitialized)
            {
                throw new InvalidOperationException(
                    "PowerShellRunspaceHolder has already been initialized and cannot be re-initialized");
            }

            _configuration = config;
            _logger = logger;
            _instance = new Lazy<PSPowerShell>(() =>
            {
                var script = GetProductionInitializationScript();
                return PowerShellRunspaceInitializer.CreateInitializedRunspace(script);
            });
            _isInitialized = true;
        }
    }

    /// <summary>
    /// Resets the initialization state for testing purposes only
    /// WARNING: This method should only be called from test code
    /// </summary>
    internal static void ResetForTesting()
    {
        lock (_lock)
        {
            _instance = null;
            _configuration = null;
            _logger = null;
            _isInitialized = false;
        }
    }

    /// <summary>
    /// Gets the production initialization script used for the singleton runspace
    /// This script can be reused for session-aware runspaces to ensure consistency
    /// </summary>
    public static string GetProductionInitializationScript()
    {
        // If we have configuration and logger, try to load custom script
        if (_configuration != null && _logger != null)
        {
            return InitializationScriptLoader.LoadInitializationScript(_configuration, _logger);
        }

        // Fall back to default script
        return InitializationScriptLoader.GetDefaultInitializationScript();
    }

    public static PSPowerShell Instance
    {
        get
        {
            lock (_lock)
            {
                if (_instance == null)
                {
                    throw new InvalidOperationException(
                        "PowerShellRunspaceHolder must be initialized by calling Initialize() before accessing Instance");
                }
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
