namespace PoshMcp;

/// <summary>
/// Configuration options for the MCP server runtime behavior.
/// </summary>
public class McpServerConfiguration
{
    /// <summary>
    /// Timeout in seconds before an idle MCP session is closed.
    /// Set higher than the default (60s) when auth flows take time.
    /// Default: 60 seconds.
    /// </summary>
    public int IdleSessionTimeoutSeconds { get; set; } = 60;
}
