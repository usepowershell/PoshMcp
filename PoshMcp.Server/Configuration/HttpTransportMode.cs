namespace PoshMcp;

/// <summary>
/// HTTP transport session semantics for the MCP server.
/// Controls whether the SDK issues <c>Mcp-Session-Id</c> headers and maintains
/// transport-level session state.
/// </summary>
/// <remarks>
/// Neither mode restores session-affine PowerShell state; both stateful and stateless
/// HTTP transport use the same warm-worker pool for PowerShell execution.
/// <para>
/// See the PoshMcp configuration migration guide for details.
/// </para>
/// </remarks>
public enum HttpTransportMode
{
    /// <summary>
    /// Stateless HTTP transport (default).
    /// No session identity or transport-level affinity.
    /// Enables <c>server/discover</c> and protocol version <c>2026-07-28</c>.
    /// </summary>
    Stateless = 0,

    /// <summary>
    /// Stateful HTTP transport (operator compatibility mode).
    /// The SDK issues <c>Mcp-Session-Id</c> and activates <c>RunSessionHandler</c> lifecycle
    /// callbacks and SSE channel management.
    /// Does not restore session-affine PowerShell state.
    /// </summary>
    Stateful = 1,
}
