using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PoshMcp.Server.McpResources;

/// <summary>
/// Wraps <see cref="McpServerTool"/> instances with resource link injection, augmenting
/// successful tool call results with an <see cref="EmbeddedResourceBlock"/> that points to the
/// noun-derived MCP resource for the tool's noun.
/// </summary>
public static class ResourceLinkInjector
{
    /// <summary>
    /// Wraps tools in the list with resource link injection for any tool whose noun has a
    /// non-conflicted <see cref="NounEntry"/> in the registry.
    /// Returns a new list; does not mutate the input.
    /// </summary>
    /// <param name="tools">The list of tools to process.</param>
    /// <param name="registry">The noun registry to look up entries.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public static List<McpServerTool> WrapToolsWithResourceLinks(
        List<McpServerTool> tools,
        NounRegistry registry,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(logger);

        var result = new List<McpServerTool>(tools.Count);
        var wrappedCount = 0;

        foreach (var tool in tools)
        {
            var noun = ExtractNounFromToolTitle(tool);
            if (noun is null)
            {
                result.Add(tool);
                continue;
            }

            var entry = registry.GetEntry(noun);
            if (entry is null)
            {
                result.Add(tool);
                continue;
            }

            result.Add(new ResourceLinkInjectorTool(tool, entry));
            wrappedCount++;
        }

        logger.LogInformation(
            "ResourceLinkInjector: wrapped {WrappedCount} of {TotalCount} tools with resource links",
            wrappedCount, tools.Count);

        return result;
    }

    private static string? ExtractNounFromToolTitle(McpServerTool tool)
    {
        string? title;
        try { title = tool.ProtocolTool.Title; }
        catch { return null; }

        if (string.IsNullOrWhiteSpace(title))
            return null;

        return NounRegistry.ExtractNounFromCommandName(title);
    }
}

/// <summary>
/// A <see cref="DelegatingMcpServerTool"/> that appends an <see cref="EmbeddedResourceBlock"/>
/// pointing to the noun-derived resource URI after a successful tool call.
/// </summary>
internal sealed class ResourceLinkInjectorTool : DelegatingMcpServerTool
{
    private readonly NounEntry _nounEntry;

    public ResourceLinkInjectorTool(McpServerTool innerTool, NounEntry nounEntry)
        : base(innerTool)
    {
        _nounEntry = nounEntry ?? throw new ArgumentNullException(nameof(nounEntry));
    }

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        var result = await base.InvokeAsync(request, cancellationToken);

        if (result.IsError != true)
        {
            result.Content.Add(new EmbeddedResourceBlock
            {
                Resource = new TextResourceContents
                {
                    Uri = _nounEntry.Uri,
                    MimeType = "application/json",
                    Text = JsonSerializer.Serialize(new
                    {
                        resourceUri = _nounEntry.Uri,
                        resourceName = _nounEntry.ResourceName,
                    })
                }
            });
        }

        return result;
    }
}
