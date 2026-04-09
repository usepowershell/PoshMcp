using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Proxy PowerShell runspace that delegates to session-specific isolated runspaces.
/// Registered as a singleton, but creates separate runspace instances per MCP session
/// identified by the Mcp-Session-Id request header.
/// </summary>
public class SessionAwarePowerShellRunspace : IPowerShellRunspace, IDisposable
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<SessionAwarePowerShellRunspace> _logger;
    private readonly ConcurrentDictionary<string, IsolatedPowerShellRunspace> _sessionRunspaces;
    private readonly object _lock = new object();
    private bool _disposed;

    public SessionAwarePowerShellRunspace(
        IHttpContextAccessor httpContextAccessor,
        ILogger<SessionAwarePowerShellRunspace> logger)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sessionRunspaces = new ConcurrentDictionary<string, IsolatedPowerShellRunspace>(StringComparer.Ordinal);

        _logger.LogInformation("SessionAwarePowerShellRunspace created - runspaces are keyed by Mcp-Session-Id");
    }

    private string GetSessionId()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            _logger.LogDebug("No HTTP context available, using default session");
            return "default";
        }

        if (httpContext.Request.Headers.TryGetValue("Mcp-Session-Id", out var sessionHeader))
        {
            var sessionId = sessionHeader.ToString().Trim();
            if (!string.IsNullOrEmpty(sessionId))
            {
                return sessionId;
            }
        }

        var connectionId = httpContext.Connection?.Id;
        if (!string.IsNullOrEmpty(connectionId))
        {
            return $"conn_{connectionId}";
        }

        return $"trace_{httpContext.TraceIdentifier ?? "unknown"}";
    }

    private IsolatedPowerShellRunspace GetSessionRunspace()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SessionAwarePowerShellRunspace));
        }

        var sessionId = GetSessionId();

        return _sessionRunspaces.GetOrAdd(sessionId, id =>
        {
            _logger.LogInformation("Creating PowerShell runspace for session: {SessionId}", id);
            var initScript = PowerShellRunspaceHolder.GetProductionInitializationScript();
            return new IsolatedPowerShellRunspace(initScript);
        });
    }

    public PSPowerShell Instance => GetSessionRunspace().Instance;

    public T ExecuteThreadSafe<T>(Func<PSPowerShell, T> operation)
    {
        var sessionRunspace = GetSessionRunspace();
        return sessionRunspace.ExecuteThreadSafe(operation);
    }

    public void ExecuteThreadSafe(Action<PSPowerShell> operation)
    {
        var sessionRunspace = GetSessionRunspace();
        sessionRunspace.ExecuteThreadSafe(operation);
    }

    public Task<T> ExecuteThreadSafeAsync<T>(Func<PSPowerShell, Task<T>> operation)
    {
        var sessionRunspace = GetSessionRunspace();
        return sessionRunspace.ExecuteThreadSafeAsync(operation);
    }

    public void CleanupSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        if (_sessionRunspaces.TryRemove(sessionId, out var runspace))
        {
            _logger.LogInformation("Cleaning up PowerShell runspace for session: {SessionId}", sessionId);
            try
            {
                runspace.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing runspace for session {SessionId}", sessionId);
            }
        }
    }

    public SessionRunspaceStats GetStats()
    {
        return new SessionRunspaceStats
        {
            ActiveSessions = _sessionRunspaces.Count,
            SessionIds = _sessionRunspaces.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList()
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _logger.LogInformation("Disposing SessionAwarePowerShellRunspace with {Count} active sessions", _sessionRunspaces.Count);

            foreach (var kvp in _sessionRunspaces)
            {
                try
                {
                    kvp.Value.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disposing runspace for session {SessionId}", kvp.Key);
                }
            }

            _sessionRunspaces.Clear();
            _disposed = true;
        }
    }
}

public sealed class SessionRunspaceStats
{
    public int ActiveSessions { get; set; }
    public List<string> SessionIds { get; set; } = new List<string>();
}
