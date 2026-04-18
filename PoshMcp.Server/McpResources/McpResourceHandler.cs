using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PoshMcp.Server.PowerShell;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.Server.McpResources;

/// <summary>
/// Handles MCP resources/list and resources/read requests backed by file and command sources.
/// </summary>
public class McpResourceHandler
{
    private readonly McpResourcesConfiguration _config;
    private readonly IPowerShellRunspace _runspace;
    private readonly string _configDirectory;
    private readonly ILogger<McpResourceHandler> _logger;

    /// <summary>
    /// Creates a new McpResourceHandler instance.
    /// </summary>
    /// <param name="config">The resources configuration loaded from appsettings.json.</param>
    /// <param name="runspace">The shared PowerShell runspace for command-backed resources.</param>
    /// <param name="configDirectory">Directory of appsettings.json, used for relative path resolution.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public McpResourceHandler(
        McpResourcesConfiguration config,
        IPowerShellRunspace runspace,
        string configDirectory,
        ILogger<McpResourceHandler> logger)
    {
        _config = config;
        _runspace = runspace;
        _configDirectory = configDirectory;
        _logger = logger;
    }

    /// <summary>
    /// Handles the resources/list MCP request, returning all configured resources.
    /// </summary>
    public ValueTask<ListResourcesResult> HandleListAsync(
        RequestContext<ListResourcesRequestParams> context,
        CancellationToken cancellationToken)
    {
        var resources = _config.Resources
            .Select(r => new Resource
            {
                Uri = r.Uri,
                Name = r.Name,
                Description = r.Description,
                MimeType = string.IsNullOrWhiteSpace(r.MimeType) ? "text/plain" : r.MimeType,
            })
            .ToList();

        _logger.LogDebug("resources/list returning {Count} resource(s)", resources.Count);

        return ValueTask.FromResult(new ListResourcesResult { Resources = resources });
    }

    /// <summary>
    /// Handles the resources/read MCP request, fetching content from a file or command source.
    /// </summary>
    public async ValueTask<ReadResourceResult> HandleReadAsync(
        RequestContext<ReadResourceRequestParams> context,
        CancellationToken cancellationToken)
    {
        var requestedUri = context.Params?.Uri;
        if (string.IsNullOrWhiteSpace(requestedUri))
        {
            throw new McpProtocolException("resources/read requires a non-empty uri parameter", McpErrorCode.InvalidParams);
        }

        var resourceConfig = _config.Resources.FirstOrDefault(r =>
            string.Equals(r.Uri, requestedUri, StringComparison.OrdinalIgnoreCase));

        if (resourceConfig is null)
        {
            _logger.LogWarning("resources/read: unknown URI {Uri}", requestedUri);
            throw new McpProtocolException($"Resource not found: {requestedUri}", McpErrorCode.ResourceNotFound);
        }

        _logger.LogDebug("resources/read {Uri} (source={Source})", requestedUri, resourceConfig.Source);

        var mimeType = string.IsNullOrWhiteSpace(resourceConfig.MimeType) ? "text/plain" : resourceConfig.MimeType;
        string content;

        try
        {
            content = resourceConfig.Source.ToLowerInvariant() switch
            {
                "file" => await ReadFileResourceAsync(resourceConfig, cancellationToken),
                "command" => await ReadCommandResourceAsync(resourceConfig, cancellationToken),
                _ => throw new McpProtocolException(
                    $"Unknown resource source '{resourceConfig.Source}' for URI {requestedUri}",
                    McpErrorCode.InvalidParams)
            };
        }
        catch (McpException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read resource {Uri}", requestedUri);
            throw new McpProtocolException(
                $"Failed to read resource '{requestedUri}': {ex.Message}",
                McpErrorCode.InternalError);
        }

        return new ReadResourceResult
        {
            Contents = new List<ResourceContents>
            {
                new TextResourceContents
                {
                    Uri = resourceConfig.Uri,
                    MimeType = mimeType,
                    Text = content,
                }
            }
        };
    }

    private async Task<string> ReadFileResourceAsync(McpResourceConfiguration config, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.Path))
        {
            throw new McpProtocolException(
                $"Resource '{config.Uri}' has source 'file' but no Path is configured.",
                McpErrorCode.InvalidParams);
        }

        var resolvedPath = Path.IsPathRooted(config.Path)
            ? config.Path
            : Path.GetFullPath(Path.Combine(_configDirectory, config.Path));

        _logger.LogDebug("Reading file resource at {Path}", resolvedPath);

        return await File.ReadAllTextAsync(resolvedPath, cancellationToken);
    }

    private Task<string> ReadCommandResourceAsync(McpResourceConfiguration config, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.Command))
        {
            throw new McpProtocolException(
                $"Resource '{config.Uri}' has source 'command' but no Command is configured.",
                McpErrorCode.InvalidParams);
        }

        _logger.LogDebug("Executing command resource: {Command}", config.Command);

        var result = _runspace.ExecuteThreadSafe(ps =>
        {
            ps.Commands.Clear();
            ps.AddScript(config.Command);
            var output = ps.Invoke();
            ps.Commands.Clear();

            if (ps.HadErrors)
            {
                var errors = ps.Streams.Error
                    .Select(e => e.Exception?.Message ?? e.ToString())
                    .ToList();
                ps.Streams.ClearStreams();
                throw new InvalidOperationException(
                    $"Command execution failed: {string.Join("; ", errors)}");
            }

            ps.Streams.ClearStreams();
            return output;
        });

        return Task.FromResult(SerializeCommandOutput(result));
    }

    private static string SerializeCommandOutput(System.Collections.ObjectModel.Collection<PSObject> results)
    {
        if (results is null || results.Count == 0)
        {
            return string.Empty;
        }

        if (results.Count == 1)
        {
            var single = results[0];
            if (single is null)
            {
                return string.Empty;
            }

            var baseObject = single.BaseObject;

            if (baseObject is string s)
            {
                return s;
            }

            if (IsScalar(baseObject))
            {
                return baseObject.ToString() ?? string.Empty;
            }
        }

        // Multiple results or complex objects — serialize to JSON
        var normalized = results
            .Where(r => r is not null)
            .Select(r => PowerShellObjectSerializer.FlattenPSObject(r))
            .ToArray();

        if (normalized.Length == 1)
        {
            return JsonSerializer.Serialize(normalized[0]);
        }

        return JsonSerializer.Serialize(normalized);
    }

    private static bool IsScalar(object? value)
    {
        if (value is null)
        {
            return true;
        }

        var type = value.GetType();
        return type.IsPrimitive
            || type.IsEnum
            || type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan)
            || type == typeof(Guid)
            || type == typeof(Uri)
            || type == typeof(Version);
    }
}
