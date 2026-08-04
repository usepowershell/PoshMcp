using System;
using PoshMcp.Server.PowerShell.Pool;

namespace PoshMcp;

/// <summary>
/// Configuration options for the MCP server runtime behavior.
/// </summary>
public class McpServerConfiguration
{
    /// <summary>
    /// HTTP transport session semantics.
    /// Default: <see cref="HttpTransportMode.Stateless"/> — enables <c>server/discover</c>
    /// and protocol <c>2026-07-28</c>.
    /// Set to <see cref="HttpTransportMode.Stateful"/> for backward-compatibility with
    /// clients that require <c>Mcp-Session-Id</c>.
    /// Neither mode restores session-affine PowerShell state.
    /// </summary>
    public HttpTransportMode HttpTransportMode { get; set; } = HttpTransportMode.Stateless;

    /// <summary>
    /// Warm-worker runspace pool options for HTTP execution.
    /// Bind from the <c>McpServer:RunspacePool</c> configuration section.
    /// Deprecated per-field aliases (<c>SessionRunspace*</c>) fall back to these when the
    /// new keys are absent; use <see cref="McpServerConfigurationResolver"/> for source-aware resolution.
    /// </summary>
    public RunspacePoolOptions RunspacePool { get; set; } = new();

    /// <summary>
    /// Timeout in seconds before an idle MCP session is closed.
    /// Set higher than the default (60s) when auth flows take time.
    /// Default: 60 seconds.
    /// </summary>
    public int IdleSessionTimeoutSeconds { get; set; } = 60;

    /// <summary>Maximum number of stateful PowerShell runspaces assigned to HTTP MCP sessions.</summary>
    /// <remarks>
    /// Superseded by <c>McpServer:RunspacePool:MaxPoolSize</c>.
    /// This property will be removed in the next major version of PoshMcp.
    /// </remarks>
    [Obsolete(
        "SessionRunspaceCapacity is superseded by McpServer:RunspacePool:MaxPoolSize. " +
        "Configure the pool via the McpServer:RunspacePool configuration section. " +
        "HTTP sessions no longer imply session-affine PowerShell state. " +
        "This property will be removed in the next major version.")]
    public int SessionRunspaceCapacity { get; set; } = 16;

    /// <summary>Seconds an inactive session runspace is retained before it is released.</summary>
    /// <remarks>
    /// Superseded by <c>McpServer:RunspacePool:IdleTtl</c> (TimeSpan).
    /// This property will be removed in the next major version of PoshMcp.
    /// </remarks>
    [Obsolete(
        "SessionRunspaceIdleTtlSeconds is superseded by McpServer:RunspacePool:IdleTtl. " +
        "Configure the pool via the McpServer:RunspacePool configuration section. " +
        "HTTP sessions no longer imply session-affine PowerShell state. " +
        "This property will be removed in the next major version.")]
    public int SessionRunspaceIdleTtlSeconds { get; set; } = 300;

    /// <summary>Seconds between idle runspace sweeps.</summary>
    /// <remarks>
    /// Superseded by <c>McpServer:RunspacePool:SweepInterval</c> (TimeSpan).
    /// This property will be removed in the next major version of PoshMcp.
    /// </remarks>
    [Obsolete(
        "SessionRunspaceSweepIntervalSeconds is superseded by McpServer:RunspacePool:SweepInterval. " +
        "Configure the pool via the McpServer:RunspacePool configuration section. " +
        "HTTP sessions no longer imply session-affine PowerShell state. " +
        "This property will be removed in the next major version.")]
    public int SessionRunspaceSweepIntervalSeconds { get; set; } = 30;

    /// <summary>Number of clean initialized runspaces kept ready for newly created sessions.</summary>
    /// <remarks>
    /// Superseded by <c>McpServer:RunspacePool:MinPoolSize</c> and <c>McpServer:RunspacePool:EagerWarmCount</c>.
    /// This property will be removed in the next major version of PoshMcp.
    /// </remarks>
    [Obsolete(
        "SessionRunspaceWarmStandbyCount is superseded by McpServer:RunspacePool:MinPoolSize and McpServer:RunspacePool:EagerWarmCount. " +
        "Configure the pool via the McpServer:RunspacePool configuration section. " +
        "HTTP sessions no longer imply session-affine PowerShell state. " +
        "This property will be removed in the next major version.")]
    public int SessionRunspaceWarmStandbyCount { get; set; } = 2;

    /// <summary>Seconds to wait for session runspace capacity before failing acquisition.</summary>
    /// <remarks>
    /// Superseded by <c>McpServer:RunspacePool:AcquisitionTimeout</c> (TimeSpan).
    /// This property will be removed in the next major version of PoshMcp.
    /// </remarks>
    [Obsolete(
        "SessionRunspaceAcquisitionTimeoutSeconds is superseded by McpServer:RunspacePool:AcquisitionTimeout. " +
        "Configure the pool via the McpServer:RunspacePool configuration section. " +
        "HTTP sessions no longer imply session-affine PowerShell state. " +
        "This property will be removed in the next major version.")]
    public int SessionRunspaceAcquisitionTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Enables the deprecated HTTP-with-SSE endpoints for clients that cannot use
    /// the Streamable HTTP transport. Disabled by default.
    /// </summary>
    public bool EnableLegacySse { get; set; }
}
