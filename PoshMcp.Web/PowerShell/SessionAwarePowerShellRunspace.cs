using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.PowerShell;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.Web.PowerShell;

/// <summary>
/// Proxy PowerShell runspace that delegates to session-specific isolated runspaces
/// This is registered as a singleton but creates separate runspace instances per HTTP session
/// </summary>
public class SessionAwarePowerShellRunspace : IPowerShellRunspace, IDisposable
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<SessionAwarePowerShellRunspace> _logger;
    private readonly ConcurrentDictionary<string, IsolatedPowerShellRunspace> _sessionRunspaces;
    private readonly object _lock = new object();
    private bool _disposed = false;

    public SessionAwarePowerShellRunspace(
        IHttpContextAccessor httpContextAccessor,
        ILogger<SessionAwarePowerShellRunspace> logger)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sessionRunspaces = new ConcurrentDictionary<string, IsolatedPowerShellRunspace>();

        _logger.LogInformation("SessionAwarePowerShellRunspace created - this will proxy to session-specific runspaces");
    }

    /// <summary>
    /// Gets the session ID from the HTTP context, creating one if it doesn't exist
    /// </summary>
    private string GetSessionId()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            // Fallback for non-HTTP contexts (like tool generation at startup)
            _logger.LogDebug("No HTTP context available, using default session");
            return "default";
        }

        // Try to get session ID from session
        if (httpContext.Session?.Id != null)
        {
            return httpContext.Session.Id;
        }

        // Fallback to connection ID if session is not available
        var connectionId = httpContext.Connection?.Id;
        if (!string.IsNullOrEmpty(connectionId))
        {
            return $"conn_{connectionId}";
        }

        // Final fallback to trace identifier
        return $"trace_{httpContext.TraceIdentifier ?? "unknown"}";
    }

    /// <summary>
    /// Gets or creates a PowerShell runspace for the current session
    /// </summary>
    private IsolatedPowerShellRunspace GetSessionRunspace()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SessionAwarePowerShellRunspace));

        var sessionId = GetSessionId();

        return _sessionRunspaces.GetOrAdd(sessionId, id =>
        {
            _logger.LogInformation($"Creating new PowerShell runspace for session: {id}");
            return new IsolatedPowerShellRunspace();
        });
    }

    public PSPowerShell Instance
    {
        get
        {
            var sessionRunspace = GetSessionRunspace();
            return sessionRunspace.Instance;
        }
    }

    public T ExecuteThreadSafe<T>(Func<PSPowerShell, T> operation)
    {
        var sessionId = GetSessionId();
        _logger.LogDebug($"Executing PowerShell operation for session: {sessionId}");

        var sessionRunspace = GetSessionRunspace();
        return sessionRunspace.ExecuteThreadSafe(operation);
    }

    public void ExecuteThreadSafe(Action<PSPowerShell> operation)
    {
        var sessionId = GetSessionId();
        _logger.LogDebug($"Executing PowerShell operation for session: {sessionId}");

        var sessionRunspace = GetSessionRunspace();
        sessionRunspace.ExecuteThreadSafe(operation);
    }

    public Task<T> ExecuteThreadSafeAsync<T>(Func<PSPowerShell, Task<T>> operation)
    {
        var sessionId = GetSessionId();
        _logger.LogDebug($"Executing async PowerShell operation for session: {sessionId}");

        var sessionRunspace = GetSessionRunspace();
        return sessionRunspace.ExecuteThreadSafeAsync(operation);
    }

    /// <summary>
    /// Cleanup a specific session's runspace (called when session ends)
    /// </summary>
    public void CleanupSession(string sessionId)
    {
        if (_sessionRunspaces.TryRemove(sessionId, out var runspace))
        {
            _logger.LogInformation($"Cleaning up PowerShell runspace for session: {sessionId}");
            try
            {
                runspace.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Error disposing runspace for session {sessionId}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Gets statistics about active sessions
    /// </summary>
    public SessionRunspaceStats GetStats()
    {
        return new SessionRunspaceStats
        {
            ActiveSessions = _sessionRunspaces.Count,
            SessionIds = _sessionRunspaces.Keys.ToList()
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_lock)
        {
            if (_disposed)
                return;

            _logger.LogInformation($"Disposing SessionAwarePowerShellRunspace with {_sessionRunspaces.Count} active sessions");

            // Dispose all session runspaces
            foreach (var kvp in _sessionRunspaces)
            {
                try
                {
                    kvp.Value.Dispose();
                    _logger.LogDebug($"Disposed runspace for session: {kvp.Key}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Error disposing runspace for session {kvp.Key}: {ex.Message}");
                }
            }

            _sessionRunspaces.Clear();
            _disposed = true;
        }
    }
}

/// <summary>
/// Statistics about session runspaces
/// </summary>
public class SessionRunspaceStats
{
    public int ActiveSessions { get; set; }
    public List<string> SessionIds { get; set; } = new List<string>();
}