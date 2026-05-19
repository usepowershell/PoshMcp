using System;
using System.Collections.Generic;
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
using PoshMcp.Server.PowerShell.OutOfProcess;

namespace PoshMcp.Server.McpResources;

/// <summary>
/// Handles MCP resources/list and resources/read for noun-derived resources backed by Get-{Noun} commands.
/// </summary>
internal class McpNounResourceHandler
{
    private const string UriPrefix = "poshmcp://resources/";

    private readonly EffectiveNounResourceRegistry _nounRegistry;
    private readonly IPowerShellRunspace? _runspace;
    private readonly ICommandExecutor? _commandExecutor;
    private readonly ILogger<McpNounResourceHandler> _logger;

    /// <summary>
    /// Creates a new McpNounResourceHandler instance.
    /// </summary>
    /// <param name="nounRegistry">The immutable registry of noun-to-resource mappings.</param>
    /// <param name="runspace">In-process PowerShell runspace; used when commandExecutor is null.</param>
    /// <param name="commandExecutor">Out-of-process executor; takes precedence over runspace when non-null.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public McpNounResourceHandler(
        EffectiveNounResourceRegistry nounRegistry,
        IPowerShellRunspace? runspace,
        ICommandExecutor? commandExecutor,
        ILogger<McpNounResourceHandler> logger)
    {
        _nounRegistry = nounRegistry ?? throw new ArgumentNullException(nameof(nounRegistry));
        if (runspace is null && commandExecutor is null)
            throw new InvalidOperationException("Either runspace or commandExecutor must be provided.");
        _runspace = runspace;
        _commandExecutor = commandExecutor;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles the resources/list MCP request, returning all non-conflicted noun-derived resources.
    /// </summary>
    public ValueTask<ListResourcesResult> HandleListAsync(
        RequestContext<ListResourcesRequestParams> context,
        CancellationToken cancellationToken)
    {
        var resources = _nounRegistry.AllEntries
            .Select(e => new Resource
            {
                Uri = e.Uri,
                Name = e.ResourceName,
                Description = e.Description,
                MimeType = "application/json",
            })
            .ToList();

        _logger.LogDebug("noun resources/list returning {Count} resource(s)", resources.Count);

        return ValueTask.FromResult(new ListResourcesResult { Resources = resources });
    }

    /// <summary>
    /// Handles the resources/read MCP request, executing the backing Get-{Noun} command.
    /// </summary>
    public async ValueTask<ReadResourceResult> HandleReadAsync(
        RequestContext<ReadResourceRequestParams> context,
        CancellationToken cancellationToken)
    {
        var uri = context.Params?.Uri;
        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new McpProtocolException("resources/read requires a non-empty uri parameter", McpErrorCode.InvalidParams);
        }

        var entry = _nounRegistry.GetEntryByUri(uri);
        if (entry is null)
        {
            var resourceName = uri.StartsWith(UriPrefix, StringComparison.OrdinalIgnoreCase)
                ? uri[UriPrefix.Length..]
                : uri;
            entry = _nounRegistry.GetEntryByResourceName(resourceName);
        }

        if (entry is null)
        {
            throw new McpProtocolException($"Resource not found: {uri}", McpErrorCode.ResourceNotFound);
        }

        _logger.LogDebug("noun resources/read {Uri} via {Command}", uri, entry.CanonicalGetCommand);

        string json;
        try
        {
            if (_commandExecutor is not null)
            {
                json = await _commandExecutor.InvokeAsync(
                    entry.CanonicalGetCommand,
                    new Dictionary<string, object?>(),
                    cancellationToken);
            }
            else if (_runspace is not null)
            {
                var output = _runspace.ExecuteThreadSafe(ps =>
                {
                    ps.Commands.Clear();
                    ps.AddScript(entry.CanonicalGetCommand);
                    var result = ps.Invoke();
                    ps.Commands.Clear();
                    if (ps.HadErrors)
                    {
                        var errors = ps.Streams.Error.Select(e => e.Exception?.Message ?? e.ToString()).ToList();
                        ps.Streams.ClearStreams();
                        throw new InvalidOperationException($"Command execution failed: {string.Join("; ", errors)}");
                    }
                    ps.Streams.ClearStreams();
                    return result;
                });
                json = SerializeCommandOutput(output);
            }
            else
            {
                throw new InvalidOperationException("No execution backend is available (runspace and commandExecutor are both null).");
            }
        }
        catch (McpException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read noun resource {Uri}", uri);
            throw new McpProtocolException(
                $"Failed to read noun resource '{uri}': {ex.Message}",
                McpErrorCode.InternalError);
        }

        return new ReadResourceResult
        {
            Contents = new List<ResourceContents>
            {
                new TextResourceContents
                {
                    Uri = entry.Uri,
                    MimeType = "application/json",
                    Text = json,
                }
            }
        };
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
