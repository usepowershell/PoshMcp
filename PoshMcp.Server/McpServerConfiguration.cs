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
    public int SessionRunspaceCapacity { get; set; } = 16;

    /// <summary>Seconds an inactive session runspace is retained before it is released.</summary>
    public int SessionRunspaceIdleTtlSeconds { get; set; } = 300;

    /// <summary>Seconds between idle runspace sweeps.</summary>
    public int SessionRunspaceSweepIntervalSeconds { get; set; } = 30;

    /// <summary>Number of clean initialized runspaces kept ready for newly created sessions.</summary>
    public int SessionRunspaceWarmStandbyCount { get; set; } = 2;

    /// <summary>Seconds to wait for session runspace capacity before failing acquisition.</summary>
    public int SessionRunspaceAcquisitionTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Enables the deprecated HTTP-with-SSE endpoints for clients that cannot use
    /// the Streamable HTTP transport. Disabled by default.
    /// </summary>
    public bool EnableLegacySse { get; set; }
}
