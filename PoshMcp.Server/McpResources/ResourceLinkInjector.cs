using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PoshMcp.Server.PowerShell;

namespace PoshMcp.Server.McpResources;

/// <summary>
/// Wraps <see cref="McpServerTool"/> instances with resource link injection, augmenting
/// successful tool call results with an <see cref="EmbeddedResourceBlock"/> that points to the
/// noun-derived MCP resource for the tool's noun.
/// </summary>
internal static class ResourceLinkInjector
{
    private static readonly JsonSerializerOptions ResourceLinkJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Wraps tools in the list with resource link injection for any tool whose noun has a
    /// non-conflicted <see cref="NounEntry"/> in the registry or has an explicit associated resource.
    /// Returns a new list; does not mutate the input.
    /// </summary>
    /// <param name="tools">The list of tools to process.</param>
    /// <param name="registry">The noun registry to look up entries.</param>
    /// <param name="commandOverrides">The effective per-command overrides.</param>
    /// <param name="resourcesConfig">The configured static/custom resources.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public static List<McpServerTool> WrapToolsWithResourceLinks(
        List<McpServerTool> tools,
        EffectiveNounResourceRegistry? registry,
        IReadOnlyDictionary<string, FunctionOverride> commandOverrides,
        McpResourcesConfiguration resourcesConfig,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(commandOverrides);
        ArgumentNullException.ThrowIfNull(resourcesConfig);
        ArgumentNullException.ThrowIfNull(logger);

        var exposedResourcesByUri = BuildExposedResourceLookup(resourcesConfig, registry);
        var result = new List<McpServerTool>(tools.Count);
        var wrappedCount = 0;

        foreach (var tool in tools)
        {
            var commandName = ExtractCommandNameFromToolTitle(tool);
            if (commandName is null)
            {
                result.Add(tool);
                continue;
            }

            var explicitResource = ResolveAssociatedResource(
                commandName,
                commandOverrides,
                exposedResourcesByUri,
                logger);

            EffectiveNounResourceEntry? nounEntry = null;
            if (registry is not null)
            {
                var noun = NounRegistry.ExtractNounFromCommandName(commandName);
                if (noun is not null)
                {
                    nounEntry = registry.GetEntry(noun);
                }
            }

            if (explicitResource is null && nounEntry is null)
            {
                result.Add(tool);
                continue;
            }

            result.Add(new ResourceLinkInjectorTool(tool, explicitResource, nounEntry));
            wrappedCount++;
        }

        logger.LogInformation(
            "ResourceLinkInjector: wrapped {WrappedCount} of {TotalCount} tools with resource links",
            wrappedCount, tools.Count);

        return result;
    }

    private static Dictionary<string, ResourceLinkTarget> BuildExposedResourceLookup(
        McpResourcesConfiguration resourcesConfig,
        EffectiveNounResourceRegistry? registry)
    {
        var lookup = new Dictionary<string, ResourceLinkTarget>(StringComparer.OrdinalIgnoreCase);

        foreach (var resource in resourcesConfig.Resources)
        {
            if (string.IsNullOrWhiteSpace(resource.Uri))
            {
                continue;
            }

            lookup.TryAdd(
                resource.Uri,
                new ResourceLinkTarget(
                    resource.Uri,
                    DeriveResourceName(resource.Uri, resource.Name),
                    resource.Description ?? resource.Name ?? resource.Uri,
                    null));
        }

        if (registry is not null)
        {
            foreach (var nounEntry in registry.AllEntries)
            {
                lookup.TryAdd(
                    nounEntry.Uri,
                    new ResourceLinkTarget(
                        nounEntry.Uri,
                        nounEntry.ResourceName,
                        nounEntry.Description,
                        nounEntry.Noun));
            }
        }

        return lookup;
    }

    private static ResourceLinkTarget? ResolveAssociatedResource(
        string commandName,
        IReadOnlyDictionary<string, FunctionOverride> commandOverrides,
        IReadOnlyDictionary<string, ResourceLinkTarget> exposedResourcesByUri,
        ILogger logger)
    {
        if (!commandOverrides.TryGetValue(commandName, out var commandOverride) ||
            string.IsNullOrWhiteSpace(commandOverride.AssociatedResourceUri))
        {
            return null;
        }

        var associatedResourceUri = commandOverride.AssociatedResourceUri.Trim();
        if (exposedResourcesByUri.TryGetValue(associatedResourceUri, out var target))
        {
            return target;
        }

        logger.LogWarning(
            "Command override AssociatedResourceUri {AssociatedResourceUri} for {CommandName} does not resolve to an exposed MCP resource. Falling back to noun-derived resource-link injection when available.",
            associatedResourceUri,
            commandName);
        return null;
    }

    private static string? ExtractCommandNameFromToolTitle(McpServerTool tool)
    {
        string? title;
        try { title = tool.ProtocolTool.Title; }
        catch { return null; }

        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        return title;
    }

    private static string DeriveResourceName(string uri, string? fallbackName)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            var absolutePath = parsed.AbsolutePath.Trim('/');
            if (!string.IsNullOrWhiteSpace(absolutePath))
            {
                var segments = absolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length > 0)
                {
                    return segments[^1];
                }
            }
        }

        var lastSlash = uri.LastIndexOf('/');
        if (lastSlash >= 0 && lastSlash < uri.Length - 1)
        {
            return uri[(lastSlash + 1)..];
        }

        return string.IsNullOrWhiteSpace(fallbackName) ? uri : fallbackName;
    }

    internal static EmbeddedResourceBlock CreateResourceLinkBlock(
        string uri,
        string resourceName,
        string? noun,
        string relationship,
        string description)
    {
        return new EmbeddedResourceBlock
        {
            Resource = new TextResourceContents
            {
                Uri = uri,
                MimeType = "application/json+mcp-resource-link",
                Text = JsonSerializer.Serialize(new
                {
                    resourceLink = new
                    {
                        uri,
                        resourceName,
                        noun,
                        relationship,
                        description,
                    }
                }, ResourceLinkJsonOptions)
            }
        };
    }

    internal sealed record ResourceLinkTarget(
        string Uri,
        string ResourceName,
        string Description,
        string? Noun);
}

/// <summary>
/// A <see cref="DelegatingMcpServerTool"/> that appends an <see cref="EmbeddedResourceBlock"/>
/// pointing to the noun-derived resource URI after a successful tool call.
/// </summary>
internal sealed class ResourceLinkInjectorTool : DelegatingMcpServerTool
{
    private readonly ResourceLinkInjector.ResourceLinkTarget? _explicitResource;
    private readonly EffectiveNounResourceEntry? _nounEntry;

    public ResourceLinkInjectorTool(
        McpServerTool innerTool,
        ResourceLinkInjector.ResourceLinkTarget? explicitResource,
        EffectiveNounResourceEntry? nounEntry)
        : base(innerTool)
    {
        _explicitResource = explicitResource;
        _nounEntry = nounEntry;
    }

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        var result = await base.InvokeAsync(request, cancellationToken);

        if (result.IsError == true)
        {
            return result;
        }

        if (_explicitResource is not null)
        {
            result.Content.Add(ResourceLinkInjector.CreateResourceLinkBlock(
                _explicitResource.Uri,
                _explicitResource.ResourceName,
                _explicitResource.Noun,
                "context",
                _explicitResource.Description));
            return result;
        }

        if (_nounEntry is not null && !_nounEntry.DisableResourceLinkBlock)
        {
            result.Content.Add(ResourceLinkInjector.CreateResourceLinkBlock(
                _nounEntry.Uri,
                _nounEntry.ResourceName,
                _nounEntry.Noun,
                "subject",
                _nounEntry.Description));
        }

        return result;
    }
}
