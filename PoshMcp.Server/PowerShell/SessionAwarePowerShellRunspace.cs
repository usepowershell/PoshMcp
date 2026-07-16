using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Limits and maintains the isolated PowerShell runspaces used by HTTP MCP sessions.
/// </summary>
public sealed class SessionRunspaceOptions
{
    public int Capacity { get; set; } = 16;
    public TimeSpan IdleTtl { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(30);
    public int WarmStandbyCount { get; set; } = 2;
    public TimeSpan AcquisitionTimeout { get; set; } = TimeSpan.FromSeconds(15);
}

/// <summary>
/// Proxy PowerShell runspace that delegates to a bounded, session-specific isolated runspace.
/// A runspace is never reassigned after it has served a session.
/// </summary>
public class SessionAwarePowerShellRunspace : IPowerShellRunspace, IDisposable
{
    private const string DefaultSessionId = "default";
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<SessionAwarePowerShellRunspace> _logger;
    private readonly ConcurrentDictionary<string, SessionRunspaceEntry> _sessionRunspaces = new(StringComparer.Ordinal);
    private readonly Queue<IsolatedPowerShellRunspace> _warmStandbys = new();
    private readonly object _gate = new();
    private readonly SessionRunspaceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly Timer _sweepTimer;
    private int _ownedSessionRunspaces;
    private bool _disposed;

    public SessionAwarePowerShellRunspace(
        IHttpContextAccessor httpContextAccessor,
        ILogger<SessionAwarePowerShellRunspace> logger)
        : this(httpContextAccessor, logger, new SessionRunspaceOptions())
    {
    }

    public SessionAwarePowerShellRunspace(
        IHttpContextAccessor httpContextAccessor,
        ILogger<SessionAwarePowerShellRunspace> logger,
        SessionRunspaceOptions options,
        TimeProvider? timeProvider = null)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = Normalize(options ?? throw new ArgumentNullException(nameof(options)));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _sweepTimer = new Timer(_ => SweepIdleRunspaces(), null, _options.SweepInterval, _options.SweepInterval);

        lock (_gate)
        {
            RefillWarmStandbys_NoLock();
        }

        _logger.LogInformation(
            "Session runspace manager started with capacity {Capacity}, idle TTL {IdleTtl}, and {WarmStandbyCount} warm standbys",
            _options.Capacity,
            _options.IdleTtl,
            _options.WarmStandbyCount);
    }

    private static SessionRunspaceOptions Normalize(SessionRunspaceOptions options) => new()
    {
        Capacity = Math.Max(1, options.Capacity),
        IdleTtl = options.IdleTtl <= TimeSpan.Zero ? TimeSpan.FromMinutes(5) : options.IdleTtl,
        SweepInterval = options.SweepInterval <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : options.SweepInterval,
        WarmStandbyCount = Math.Max(0, options.WarmStandbyCount),
        AcquisitionTimeout = options.AcquisitionTimeout < TimeSpan.Zero ? TimeSpan.Zero : options.AcquisitionTimeout
    };

    private string GetSessionId()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.Request.Headers.TryGetValue("Mcp-Session-Id", out var sessionHeader) == true)
        {
            var sessionId = sessionHeader.ToString().Trim();
            if (!string.IsNullOrEmpty(sessionId))
            {
                return sessionId;
            }
        }

        return DefaultSessionId;
    }

    private SessionRunspaceEntry GetSessionRunspace()
    {
        var sessionId = GetSessionId();
        lock (_gate)
        {
            ThrowIfDisposed_NoLock();
            if (_sessionRunspaces.TryGetValue(sessionId, out var existing))
            {
                existing.Touch(_timeProvider.GetUtcNow());
                return existing;
            }

            var timeout = _options.AcquisitionTimeout;
            var stopwatch = Stopwatch.StartNew();
            while (_ownedSessionRunspaces >= _options.Capacity)
            {
                var remaining = timeout - stopwatch.Elapsed;
                if (timeout == TimeSpan.Zero || remaining <= TimeSpan.Zero || !Monitor.Wait(_gate, remaining))
                {
                    throw new TimeoutException($"Timed out acquiring a PowerShell runspace for MCP session '{sessionId}'.");
                }

                ThrowIfDisposed_NoLock();
                if (_sessionRunspaces.TryGetValue(sessionId, out existing))
                {
                    existing.Touch(_timeProvider.GetUtcNow());
                    return existing;
                }
            }

            var runspace = _warmStandbys.Count > 0
                ? _warmStandbys.Dequeue()
                : CreateInitializedRunspace();
            var entry = new SessionRunspaceEntry(runspace, _timeProvider.GetUtcNow, _timeProvider.GetUtcNow());
            if (!_sessionRunspaces.TryAdd(sessionId, entry))
            {
                runspace.Dispose();
                return _sessionRunspaces[sessionId];
            }

            _ownedSessionRunspaces++;
            _logger.LogInformation("Assigned clean PowerShell runspace to session {SessionId}", sessionId);
            return entry;
        }
    }

    public PSPowerShell Instance => GetSessionRunspace().Instance;

    public T ExecuteThreadSafe<T>(Func<PSPowerShell, T> operation) =>
        GetSessionRunspace().Execute(operation, CompleteReleasedEntry);

    public void ExecuteThreadSafe(Action<PSPowerShell> operation) =>
        GetSessionRunspace().Execute(operation, CompleteReleasedEntry);

    public Task<T> ExecuteThreadSafeAsync<T>(Func<PSPowerShell, Task<T>> operation) =>
        GetSessionRunspace().ExecuteAsync(operation, CompleteReleasedEntry);

    /// <summary>Releases state for a terminated MCP session without interrupting its active invocation.</summary>
    public void CleanupSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId == DefaultSessionId)
        {
            return;
        }

        ReleaseSession(sessionId, "session completed");
    }

    public void SweepIdleRunspaces()
    {
        if (_disposed)
        {
            return;
        }

        var cutoff = _timeProvider.GetUtcNow() - _options.IdleTtl;
        foreach (var pair in _sessionRunspaces)
        {
            if (pair.Value.LastTouched <= cutoff)
            {
                ReleaseSession(pair.Key, "idle TTL expired");
            }
        }
    }

    /// <summary>Releases every session-owned runspace so subsequent requests get clean state.</summary>
    public void ReleaseAllSessions()
    {
        foreach (var sessionId in _sessionRunspaces.Keys)
        {
            ReleaseSession(sessionId, "configuration reloaded");
        }
    }

    private void ReleaseSession(string sessionId, string reason)
    {
        if (!_sessionRunspaces.TryRemove(sessionId, out var entry))
        {
            return;
        }

        _logger.LogInformation("Releasing PowerShell runspace for session {SessionId}: {Reason}", sessionId, reason);
        if (entry.RequestRelease())
        {
            CompleteReleasedEntry(entry);
        }
    }

    private void CompleteReleasedEntry(SessionRunspaceEntry entry)
    {
        lock (_gate)
        {
            entry.Dispose();
            _ownedSessionRunspaces--;
            Monitor.PulseAll(_gate);
            if (!_disposed)
            {
                RefillWarmStandbys_NoLock();
            }
        }
    }

    private void RefillWarmStandbys_NoLock()
    {
        while (!_disposed && _warmStandbys.Count < _options.WarmStandbyCount)
        {
            _warmStandbys.Enqueue(CreateInitializedRunspace());
        }
    }

    private static IsolatedPowerShellRunspace CreateInitializedRunspace() =>
        new(PowerShellRunspaceHolder.GetProductionInitializationScript());

    private void ThrowIfDisposed_NoLock()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SessionAwarePowerShellRunspace));
        }
    }

    public SessionRunspaceStats GetStats()
    {
        lock (_gate)
        {
            return new SessionRunspaceStats
            {
                ActiveSessions = _sessionRunspaces.Count,
                SessionIds = _sessionRunspaces.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList(),
                WarmStandbyCount = _warmStandbys.Count,
                OwnedSessionRunspaces = _ownedSessionRunspaces
            };
        }
    }

    public void Dispose()
    {
        List<SessionRunspaceEntry> entries;
        List<IsolatedPowerShellRunspace> standbys;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _sweepTimer.Dispose();
            entries = _sessionRunspaces.Values.ToList();
            _sessionRunspaces.Clear();
            standbys = _warmStandbys.ToList();
            _warmStandbys.Clear();
        }

        foreach (var standby in standbys)
        {
            standby.Dispose();
        }

        foreach (var entry in entries)
        {
            if (entry.RequestRelease())
            {
                CompleteReleasedEntry(entry);
            }
        }
    }

    private sealed class SessionRunspaceEntry
    {
        private readonly IsolatedPowerShellRunspace _runspace;
        private readonly object _gate = new();
        private readonly Func<DateTimeOffset> _utcNow;
        private int _activeInvocations;
        private bool _releaseRequested;
        private bool _disposed;

        public SessionRunspaceEntry(
            IsolatedPowerShellRunspace runspace,
            Func<DateTimeOffset> utcNow,
            DateTimeOffset createdAt)
        {
            _runspace = runspace;
            _utcNow = utcNow;
            LastTouched = createdAt;
        }

        public DateTimeOffset LastTouched { get; private set; }
        public PSPowerShell Instance => _runspace.Instance;

        public void Touch(DateTimeOffset now)
        {
            lock (_gate)
            {
                LastTouched = now;
            }
        }

        public T Execute<T>(Func<PSPowerShell, T> operation, Action<SessionRunspaceEntry> release)
        {
            BeginInvocation();
            try
            {
                return _runspace.ExecuteThreadSafe(operation);
            }
            finally
            {
                EndInvocation(release);
            }
        }

        public void Execute(Action<PSPowerShell> operation, Action<SessionRunspaceEntry> release)
        {
            BeginInvocation();
            try
            {
                _runspace.ExecuteThreadSafe(operation);
            }
            finally
            {
                EndInvocation(release);
            }
        }

        public async Task<T> ExecuteAsync<T>(Func<PSPowerShell, Task<T>> operation, Action<SessionRunspaceEntry> release)
        {
            BeginInvocation();
            try
            {
                return await _runspace.ExecuteThreadSafeAsync(operation);
            }
            finally
            {
                EndInvocation(release);
            }
        }

        public bool RequestRelease()
        {
            lock (_gate)
            {
                _releaseRequested = true;
                return _activeInvocations == 0;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _runspace.Dispose();
            }
        }

        private void BeginInvocation()
        {
            lock (_gate)
            {
                if (_releaseRequested || _disposed)
                {
                    throw new ObjectDisposedException(nameof(SessionAwarePowerShellRunspace));
                }

                _activeInvocations++;
                LastTouched = _utcNow();
            }
        }

        private void EndInvocation(Action<SessionRunspaceEntry> release)
        {
            var dispose = false;
            lock (_gate)
            {
                _activeInvocations--;
                LastTouched = _utcNow();
                dispose = _releaseRequested && _activeInvocations == 0;
            }

            if (dispose)
            {
                release(this);
            }
        }
    }
}

public sealed class SessionRunspaceStats
{
    public int ActiveSessions { get; set; }
    public List<string> SessionIds { get; set; } = new();
    public int WarmStandbyCount { get; set; }
    public int OwnedSessionRunspaces { get; set; }
}
