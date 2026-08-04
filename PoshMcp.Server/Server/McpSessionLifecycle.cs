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
/// <remarks>
/// This type is responsible solely for MCP transport/session protocol-version tracking.
/// It has no ownership of or cleanup responsibility for PowerShell workers;
/// the <see cref="PoshMcp.Server.PowerShell.Pool.IRunspacePool"/> is lifecycle-managed by
/// <see cref="RunspacePoolLifecycleService"/> through the host infrastructure.
/// </remarks>
internal sealed class McpSessionLifecycle
{
    private readonly ConcurrentDictionary<string, string> _protocolVersions = new(StringComparer.Ordinal);

    public McpSessionLifecycle()
    {
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
    }
}
