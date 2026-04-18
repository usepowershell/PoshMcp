using System.Collections.Generic;

namespace PoshMcp.Server.McpPrompts;

/// <summary>
/// Defines a single MCP prompt — its name, description, source type (file or command),
/// source-specific details, and declared arguments.
/// </summary>
public class McpPromptConfiguration
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>"file" or "command"</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Required when Source = "file". Absolute or relative path to the prompt file.</summary>
    public string? Path { get; set; }

    /// <summary>Required when Source = "command". PowerShell script string to execute.</summary>
    public string? Command { get; set; }

    public List<McpPromptArgumentConfiguration> Arguments { get; set; } = new();
}
