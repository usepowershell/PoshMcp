using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace PoshMcp;

/// <summary>
/// Enforces Streamable HTTP protocol-version headers after session negotiation.
/// </summary>
internal sealed class McpProtocolVersionMiddleware
{
    internal const string CurrentProtocolVersion = "2025-11-25";
    internal const string CompatibilityProtocolVersion = "2024-11-05";

    private readonly RequestDelegate _next;
    private readonly HashSet<string> _mcpPaths;
    private readonly McpSessionLifecycle _sessionLifecycle;

    public McpProtocolVersionMiddleware(
        RequestDelegate next,
        McpSessionLifecycle sessionLifecycle,
        IEnumerable<string> mcpPaths)
    {
        _next = next;
        _sessionLifecycle = sessionLifecycle;
        _mcpPaths = new HashSet<string>(mcpPaths, StringComparer.OrdinalIgnoreCase);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_mcpPaths.Contains(context.Request.Path.Value ?? string.Empty))
        {
            await _next(context);
            return;
        }

        var sessionId = context.Request.Headers["Mcp-Session-Id"].ToString();
        if (!string.IsNullOrWhiteSpace(sessionId) &&
            _sessionLifecycle.TryGetProtocolVersion(sessionId, out var negotiatedVersion) &&
            !IsValidProtocolHeader(context.Request, negotiatedVersion))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var initializeProtocolVersion = string.IsNullOrWhiteSpace(sessionId)
            ? await GetInitializeProtocolVersionAsync(context.Request)
            : null;

        await _next(context);

        if (!string.IsNullOrWhiteSpace(initializeProtocolVersion) &&
            context.Response.StatusCode < StatusCodes.Status400BadRequest)
        {
            var createdSessionId = context.Response.Headers["Mcp-Session-Id"].ToString();
            if (!string.IsNullOrWhiteSpace(createdSessionId))
            {
                _sessionLifecycle.TrackProtocolVersion(createdSessionId, initializeProtocolVersion);
            }
        }

        if (HttpMethods.IsDelete(context.Request.Method) &&
            !string.IsNullOrWhiteSpace(sessionId) &&
            context.Response.StatusCode is StatusCodes.Status200OK or StatusCodes.Status404NotFound)
        {
            _sessionLifecycle.RemoveProtocolVersion(sessionId);
        }
    }

    private static bool IsValidProtocolHeader(HttpRequest request, string negotiatedVersion)
    {
        var protocolVersion = request.Headers["MCP-Protocol-Version"].ToString();

        if (string.Equals(negotiatedVersion, CompatibilityProtocolVersion, StringComparison.Ordinal))
        {
            return string.IsNullOrWhiteSpace(protocolVersion) ||
                string.Equals(protocolVersion, negotiatedVersion, StringComparison.Ordinal);
        }

        return string.Equals(protocolVersion, negotiatedVersion, StringComparison.Ordinal) &&
            string.Equals(protocolVersion, CurrentProtocolVersion, StringComparison.Ordinal);
    }

    private static async Task<string?> GetInitializeProtocolVersionAsync(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method) || !request.HasJsonContentType())
        {
            return null;
        }

        request.EnableBuffering();
        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body);
            var root = document.RootElement;
            if (!root.TryGetProperty("method", out var method) ||
                !string.Equals(method.GetString(), "initialize", StringComparison.Ordinal) ||
                !root.TryGetProperty("params", out var parameters) ||
                !parameters.TryGetProperty("protocolVersion", out var protocolVersion))
            {
                return null;
            }

            return protocolVersion.GetString();
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            request.Body.Position = 0;
        }
    }
}
