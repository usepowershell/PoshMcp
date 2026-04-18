using System.Collections.Generic;

namespace PoshMcp.Server.McpResources;

/// <summary>
/// Top-level configuration for MCP resources. Bound from the "McpResources" section of appsettings.json.
/// </summary>
public class McpResourcesConfiguration
{
    /// <summary>
    /// The list of configured MCP resources.
    /// </summary>
    public List<McpResourceConfiguration> Resources { get; set; } = new();
}
