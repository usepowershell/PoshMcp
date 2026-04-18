using System.Collections.Generic;

namespace PoshMcp.Server.McpPrompts;

/// <summary>
/// Top-level configuration section "McpPrompts" — holds the list of configured MCP prompts.
/// </summary>
public class McpPromptsConfiguration
{
    public List<McpPromptConfiguration> Prompts { get; set; } = new();
}
