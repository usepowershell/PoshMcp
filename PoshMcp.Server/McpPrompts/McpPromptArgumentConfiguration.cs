namespace PoshMcp.Server.McpPrompts;

/// <summary>
/// Defines a single argument on an MCP prompt — name, optional description, and whether it is required.
/// </summary>
public class McpPromptArgumentConfiguration
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Required { get; set; } = false;
}
