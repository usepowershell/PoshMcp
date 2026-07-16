using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace PoshMcp;

/// <summary>
/// Tracks state associated with stateful HTTP MCP sessions and releases it when the SDK closes a session.
/// </summary>
internal sealed class McpSessionLifecycle
{
    private readonly ConcurrentDictionary<string, string> _protocolVersions = new(StringComparer.Ordinal);
    private readonly Action<string> _cleanupSession;

    public McpSessionLifecycle(Action<string> cleanupSession)
    {
        _cleanupSession = cleanupSession ?? throw new ArgumentNullException(nameof(cleanupSession));
    }

    public bool TryGetProtocolVersion(string sessionId, [NotNullWhen(true)] out string? protocolVersion) =>
        _protocolVersions.TryGetValue(sessionId, out protocolVersion);

    public void TrackProtocolVersion(string sessionId, string protocolVersion)
    {
        _protocolVersions[sessionId] = protocolVersion;
    }

    public void RemoveProtocolVersion(string sessionId)
    {
        _protocolVersions.TryRemove(sessionId, out _);
    }

    public async Task RunSessionAsync(HttpContext _, McpServer session, CancellationToken cancellationToken)
    {
        try
        {
            await session.RunAsync(cancellationToken);
        }
        finally
        {
            CompleteSession(session.SessionId);
        }
    }

    internal void CompleteSession(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        RemoveProtocolVersion(sessionId);
        _cleanupSession(sessionId);
    }
}
