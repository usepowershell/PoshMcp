namespace PoshMcp.Server.McpResources;

/// <summary>Per-noun configuration override for noun-derived MCP resources.</summary>
public class NounResourceOverride
{
    /// <summary>If true, no resource is created for this noun and no resourceLinkBlock is injected.</summary>
    public bool Disabled { get; set; } = false;

    /// <summary>Override the derived snake_case resource name. Must be unique across all resources.</summary>
    public string? ResourceName { get; set; }

    /// <summary>Override the full poshmcp:// resource URI. Must be unique.</summary>
    public string? Uri { get; set; }

    /// <summary>Override the human-readable resource description.</summary>
    public string? Description { get; set; }

    /// <summary>If true, the resource is listed but tools for this noun do not receive a resourceLinkBlock.</summary>
    public bool DisableResourceLinkBlock { get; set; } = false;
}
