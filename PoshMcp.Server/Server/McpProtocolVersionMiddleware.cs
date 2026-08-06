using Microsoft.AspNetCore.Http;
using System;
using System.Buffers;
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

        // Buffer the body so the MCP SDK can re-read it after our peek.
        request.EnableBuffering();
        try
        {
            // Fast path: read only the first 512 bytes to find the JSON-RPC method field.
            // For initialize requests, the body is tiny. For tools/call, we skip the full parse.
            var buffer = ArrayPool<byte>.Shared.Rent(512);
            try
            {
                int bytesRead = await request.Body.ReadAsync(buffer.AsMemory(0, 512));
                request.Body.Position = 0;

                if (!TryGetMethodField(buffer.AsSpan(0, bytesRead), out var method) ||
                    !string.Equals(method, "initialize", StringComparison.Ordinal))
                {
                    return null;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            // Only parse the full document for initialize requests to extract protocolVersion.
            using var document = await JsonDocument.ParseAsync(request.Body);
            var root = document.RootElement;
            if (!root.TryGetProperty("params", out var parameters) ||
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

    /// <summary>
    /// Scans a partial UTF-8 JSON buffer to extract the top-level "method" string value.
    /// Returns false if the field is not found within the buffer or the buffer is malformed.
    /// </summary>
    private static bool TryGetMethodField(ReadOnlySpan<byte> bytes, out string? method)
    {
        method = null;
        if (bytes.IsEmpty)
            return false;

        try
        {
            var reader = new Utf8JsonReader(bytes, isFinalBlock: false, state: default);
            var depth = 0;
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        depth++;
                        break;
                    case JsonTokenType.EndObject:
                        depth--;
                        break;
                    case JsonTokenType.PropertyName when depth == 1 && reader.ValueTextEquals("method"u8):
                        if (reader.Read() && reader.TokenType == JsonTokenType.String)
                        {
                            method = reader.GetString();
                            return true;
                        }
                        return false;
                }
            }
            return false;
        }
        catch
        {
            // Partial buffer or malformed JSON — treat as not found.
            return false;
        }
    }
}
