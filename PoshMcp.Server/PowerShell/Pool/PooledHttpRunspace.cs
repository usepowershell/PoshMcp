using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.Server.PowerShell.Pool;

/// <summary>
/// Pool-backed <see cref="IPowerShellRunspace"/> adapter for HTTP transport.
/// Each <see cref="ExecuteThreadSafeAsync{T}"/> call acquires exactly one lease from the
/// <see cref="IRunspacePool"/>, executes the operation against that worker, and returns the
/// lease. <c>Mcp-Session-Id</c> must never flow into this adapter; workers are anonymous.
/// </summary>
/// <remarks>
/// <para>
/// <b>Discovery runspace.</b> The <see cref="Instance"/> property returns a dedicated
/// single-worker runspace used only during startup tool discovery (e.g.,
/// <c>Get-Command</c>/<c>Get-Help</c> introspection by <c>McpToolFactoryV2</c>).
/// It is not used for per-request tool execution.
/// </para>
/// <para>
/// <b>Sync bridge.</b> <see cref="ExecuteThreadSafe{T}"/> blocks the caller thread via
/// <c>GetAwaiter().GetResult()</c>. This is safe on .NET 10 ASP.NET Core thread-pool
/// threads (no ambient <see cref="System.Threading.SynchronizationContext"/>) and is
/// bounded by <see cref="RunspacePoolOptions.AcquisitionTimeout"/>, so the thread cannot
/// stall indefinitely. Use only where the call site cannot go async (e.g., resource
/// handlers); prefer <see cref="ExecuteThreadSafeAsync{T}"/> for all other callers.
/// </para>
/// </remarks>
internal sealed class PooledHttpRunspace : IPowerShellRunspace, IDisposable
{
    private readonly IRunspacePool _pool;
    private readonly ILogger<PooledHttpRunspace> _logger;
    private readonly Lazy<IPowerShellRunspace> _discoveryRunspace;
    private bool _disposed;

    /// <summary>
    /// Initialises a <see cref="PooledHttpRunspace"/>.
    /// </summary>
    /// <param name="pool">
    /// Warm pool to acquire workers from; must be started before the first
    /// <see cref="ExecuteThreadSafe{T}"/> or <see cref="ExecuteThreadSafeAsync{T}"/> call.
    /// </param>
    /// <param name="startupScript">
    /// Same initialisation script used for pool workers. Applied to the discovery runspace
    /// so that tool introspection reflects the production PS environment.
    /// </param>
    /// <param name="loggerFactory">Logger factory for adapter diagnostics.</param>
    public PooledHttpRunspace(IRunspacePool pool, string? startupScript, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _pool = pool;
        _logger = loggerFactory.CreateLogger<PooledHttpRunspace>();
        _discoveryRunspace = new Lazy<IPowerShellRunspace>(
            () => new IsolatedPowerShellRunspace(startupScript ?? string.Empty));
    }

    /// <summary>
    /// Internal constructor that accepts an <paramref name="discoveryRunspace"/> directly.
    /// Used by tests to inject a mock without creating a real PowerShell runspace.
    /// </summary>
    internal PooledHttpRunspace(IRunspacePool pool, IPowerShellRunspace discoveryRunspace, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(discoveryRunspace);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _pool = pool;
        _logger = loggerFactory.CreateLogger<PooledHttpRunspace>();
        _discoveryRunspace = new Lazy<IPowerShellRunspace>(() => discoveryRunspace);
    }

    /// <summary>
    /// Returns the dedicated startup discovery runspace.
    /// Used only by <c>MccToolFactoryV2</c> during server initialisation to introspect
    /// available PowerShell commands. Do NOT call during request handling.
    /// </summary>
    public PSPowerShell Instance
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _discoveryRunspace.Value.Instance;
        }
    }

    /// <summary>
    /// Acquires one pool lease, invokes <paramref name="operation"/> with the leased worker,
    /// returns the lease, and propagates the result. Sync bridge — see class remarks.
    /// </summary>
    public T ExecuteThreadSafe<T>(Func<PSPowerShell, T> operation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(operation);
        // AcquisitionTimeout bounds this blocking wait; no SynchronizationContext on
        // ASP.NET Core thread-pool threads makes GetAwaiter().GetResult() safe here.
        var lease = _pool.AcquireAsync().AsTask().GetAwaiter().GetResult();
        try
        {
            return operation(lease.PowerShell);
        }
        catch
        {
            lease.RequestEviction();
            throw;
        }
        finally
        {
            lease.Dispose();
        }
    }

    /// <summary>
    /// Acquires one pool lease, invokes <paramref name="operation"/> with the leased worker,
    /// and returns the lease. Sync bridge — see class remarks.
    /// </summary>
    public void ExecuteThreadSafe(Action<PSPowerShell> operation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(operation);
        var lease = _pool.AcquireAsync().AsTask().GetAwaiter().GetResult();
        try
        {
            operation(lease.PowerShell);
        }
        catch
        {
            lease.RequestEviction();
            throw;
        }
        finally
        {
            lease.Dispose();
        }
    }

    /// <summary>
    /// Acquires one pool lease asynchronously, invokes <paramref name="operation"/>,
    /// and returns the lease. Preferred over the sync overloads.
    /// </summary>
    public async Task<T> ExecuteThreadSafeAsync<T>(Func<PSPowerShell, Task<T>> operation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(operation);
        await using var lease = await _pool.AcquireAsync().ConfigureAwait(false);
        try
        {
            return await operation(lease.PowerShell).ConfigureAwait(false);
        }
        catch
        {
            lease.RequestEviction();
            throw;
        }
    }

    /// <summary>
    /// Disposes the discovery runspace if it was created.
    /// The shared pool is owned by <see cref="RunspacePoolLifecycleService"/> and must be
    /// drained through host stop; it is not disposed here.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_discoveryRunspace.IsValueCreated && _discoveryRunspace.Value is IDisposable d)
            d.Dispose();
    }
}
