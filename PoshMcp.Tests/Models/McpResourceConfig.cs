using System.Collections.Generic;

namespace PoshMcp.Tests.Models;

/// <summary>
/// Stub configuration model for an MCP Resource entry.
/// Mirrors the schema specified in Spec 002 (McpResources configuration).
/// Replace with direct PoshMcp.Server type references once the implementation PR lands.
/// </summary>
public class McpResourceDefinition
{
    public string Uri { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? MimeType { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? Path { get; set; }
    public string? Command { get; set; }
}

/// <summary>
/// Container for the McpResources configuration section (top-level sibling to PowerShellConfiguration).
/// </summary>
public class McpResourcesSection
{
    public List<McpResourceDefinition> Resources { get; set; } = new();
}

/// <summary>
/// Stub configuration model for a prompt argument.
/// Mirrors the schema specified in Spec 002.
/// </summary>
public class McpPromptArgument
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Required { get; set; } = false;
}

/// <summary>
/// Stub configuration model for an MCP Prompt entry.
/// Mirrors the schema specified in Spec 002 (McpPrompts configuration).
/// Replace with direct PoshMcp.Server type references once the implementation PR lands.
/// </summary>
public class McpPromptDefinition
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? Path { get; set; }
    public string? Command { get; set; }
    public List<McpPromptArgument> Arguments { get; set; } = new();
}

/// <summary>
/// Container for the McpPrompts configuration section (top-level sibling to PowerShellConfiguration).
/// </summary>
public class McpPromptsSection
{
    public List<McpPromptDefinition> Prompts { get; set; } = new();
}
