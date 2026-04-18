using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PoshMcp.Server.PowerShell;

namespace PoshMcp.Server.McpPrompts;

/// <summary>
/// Handles <c>prompts/list</c> and <c>prompts/get</c> MCP requests for file- and command-backed prompts.
/// </summary>
public class McpPromptHandler
{
    private readonly McpPromptsConfiguration _config;
    private readonly string _configDirectory;
    private readonly ILogger<McpPromptHandler> _logger;

    public McpPromptHandler(
        McpPromptsConfiguration config,
        string configDirectory,
        ILogger<McpPromptHandler> logger)
    {
        _config = config;
        _configDirectory = configDirectory;
        _logger = logger;
    }

    /// <summary>
    /// Returns the configured prompt list for <c>prompts/list</c>.
    /// </summary>
    public ValueTask<ListPromptsResult> HandleListPromptsAsync(
        RequestContext<ListPromptsRequestParams> context,
        CancellationToken cancellationToken)
    {
        var prompts = _config.Prompts.Select(p => new Prompt
        {
            Name = p.Name,
            Description = p.Description,
            Arguments = p.Arguments.Select(a => new PromptArgument
            {
                Name = a.Name,
                Description = a.Description,
                Required = a.Required
            }).ToList()
        }).ToList();

        return ValueTask.FromResult(new ListPromptsResult { Prompts = prompts });
    }

    /// <summary>
    /// Renders a prompt by name for <c>prompts/get</c>, reading a file or executing a PowerShell command.
    /// </summary>
    public async ValueTask<GetPromptResult> HandleGetPromptAsync(
        RequestContext<GetPromptRequestParams> context,
        CancellationToken cancellationToken)
    {
        var name = context.Params?.Name ?? string.Empty;
        var prompt = _config.Prompts.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (prompt == null)
        {
            throw new McpProtocolException(
                $"Prompt '{name}' not found.",
                McpErrorCode.InvalidParams);
        }

        string content;
        if (string.Equals(prompt.Source, "file", StringComparison.OrdinalIgnoreCase))
        {
            content = await ReadFilePromptAsync(prompt, cancellationToken);
        }
        else if (string.Equals(prompt.Source, "command", StringComparison.OrdinalIgnoreCase))
        {
            var rawArgs = context.Params?.Arguments;
            var stringArgs = ToStringDictionary(rawArgs);
            content = ExecuteCommandPrompt(prompt, stringArgs);
        }
        else
        {
            throw new McpProtocolException(
                $"Unknown prompt source '{prompt.Source}' for prompt '{name}'. Supported values: file, command.",
                McpErrorCode.InvalidParams);
        }

        return new GetPromptResult
        {
            Description = prompt.Description,
            Messages = new List<PromptMessage>
            {
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock { Text = content }
                }
            }
        };
    }

    /// <summary>
    /// Converts the prompt arguments dictionary (which uses JsonElement values) to a string dictionary
    /// for variable injection into PowerShell.
    /// </summary>
    private static Dictionary<string, string?> ToStringDictionary(IDictionary<string, JsonElement>? source)
    {
        if (source == null)
        {
            return new Dictionary<string, string?>();
        }

        var result = new Dictionary<string, string?>(source.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in source)
        {
            result[kvp.Key] = kvp.Value.ValueKind == JsonValueKind.Null
                ? null
                : kvp.Value.GetString() ?? kvp.Value.GetRawText();
        }
        return result;
    }

    private async Task<string> ReadFilePromptAsync(
        McpPromptConfiguration prompt,
        CancellationToken cancellationToken)
    {
        var path = prompt.Path!;
        if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(_configDirectory, path);
        }

        try
        {
            return await File.ReadAllTextAsync(path, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read prompt file '{Path}'", path);
            throw new McpProtocolException(
                $"Failed to read prompt file '{path}': {ex.Message}",
                McpErrorCode.InternalError);
        }
    }

    private string ExecuteCommandPrompt(
        McpPromptConfiguration prompt,
        Dictionary<string, string?> args)
    {
        try
        {
            return PowerShellRunspaceHolder.ExecuteThreadSafe(ps =>
            {
                ps.Commands.Clear();
                ps.Streams.Error.Clear();

                // Inject each argument value as a PowerShell variable before execution (FR-032).
                foreach (var arg in args)
                {
                    ps.Runspace.SessionStateProxy.SetVariable(arg.Key, arg.Value);
                }

                ps.AddScript(prompt.Command!);
                var results = ps.Invoke();
                ps.Commands.Clear();

                if (ps.HadErrors)
                {
                    var errors = string.Join("; ", ps.Streams.Error.Select(e => e.ToString()));
                    throw new InvalidOperationException($"PowerShell errors executing prompt command: {errors}");
                }

                return string.Join(
                    Environment.NewLine,
                    results.Select(r => r?.ToString() ?? string.Empty));
            });
        }
        catch (McpProtocolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute prompt command '{Command}'", prompt.Command);
            throw new McpProtocolException(
                $"Failed to execute prompt command '{prompt.Command}': {ex.Message}",
                McpErrorCode.InternalError);
        }
    }
}
