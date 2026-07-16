using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PoshMcp;

/// <summary>
/// Validates browser origins for Streamable HTTP MCP endpoints.
/// </summary>
internal sealed class McpOriginValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HashSet<string> _mcpPaths;
    private readonly HashSet<string> _allowedOrigins;

    public McpOriginValidationMiddleware(
        RequestDelegate next,
        IEnumerable<string> mcpPaths,
        IEnumerable<string> allowedOrigins)
    {
        _next = next;
        _mcpPaths = new HashSet<string>(
            mcpPaths.Select(NormalizeMcpPath),
            StringComparer.OrdinalIgnoreCase);
        _allowedOrigins = new HashSet<string>(
            allowedOrigins
                .Select(NormalizeOrigin)
                .Where(origin => origin is not null)
                .Cast<string>(),
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_mcpPaths.Contains(NormalizeMcpPath(context.Request.Path.Value)) ||
            !context.Request.Headers.TryGetValue("Origin", out var origin))
        {
            await _next(context);
            return;
        }

        if (!IsAllowedOrigin(context.Request, origin.ToString()))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await _next(context);
    }

    internal bool IsAllowedOrigin(HttpRequest request, string origin)
    {
        var normalizedOrigin = NormalizeOrigin(origin);
        if (normalizedOrigin is null)
        {
            return false;
        }

        return string.Equals(normalizedOrigin, GetRequestOrigin(request), StringComparison.OrdinalIgnoreCase)
            || _allowedOrigins.Contains(normalizedOrigin);
    }

    private static string? GetRequestOrigin(HttpRequest request)
    {
        if (!Uri.TryCreate($"{request.Scheme}://{request.Host}", UriKind.Absolute, out var requestUri))
        {
            return null;
        }

        return NormalizeOrigin(requestUri.AbsoluteUri);
    }

    private static string? NormalizeOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin) ||
            !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.Equals(uri.AbsolutePath, "/", StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    private static string NormalizeMcpPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
        {
            return path ?? string.Empty;
        }

        return path.TrimEnd('/');
    }
}
