using System;

namespace PoshMcp.Server.McpResources;

/// <summary>
/// Configuration for a single MCP resource entry, backed by a file or PowerShell command.
/// </summary>
public class McpResourceConfiguration
{
    /// <summary>
    /// The MCP resource URI (e.g., "poshmcp://resources/my-resource"). Required; must be unique.
    /// </summary>
    public string Uri { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable name for the resource. Required.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of the resource.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// MIME type of the resource content. Null when not specified in configuration;
    /// the runtime handler applies "text/plain" as the fallback when serving responses.
    /// </summary>
    public string? MimeType { get; set; }

    /// <summary>
    /// Source type: "file" or "command".
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// File path (absolute or relative to appsettings.json directory). Required when Source is "file".
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// PowerShell command to execute. Required when Source is "command".
    /// </summary>
    public string? Command { get; set; }
}
