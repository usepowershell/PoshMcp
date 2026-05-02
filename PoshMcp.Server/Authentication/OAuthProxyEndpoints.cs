using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;

namespace PoshMcp.Server.Authentication;

/// <summary>
/// Registers the two endpoints required for MCP clients (including non-VS Code
/// clients that do not have a pre-registered client_id) to perform OAuth 2.1
/// Authorization Code + PKCE flow against Entra ID without prompting the user
/// for a client_id.
///
/// Endpoints:
///   GET  /.well-known/oauth-authorization-server
///        RFC 8414 AS metadata document.  Reports Entra ID's authorization
///        and token endpoints and advertises this server's /register as the
///        DCR (RFC 7591) endpoint so clients can obtain a client_id.
///
///   POST /register   (DCR proxy — RFC 7591)
///        Returns the statically-configured ClientId.  Entra does not support
///        real DCR for public clients; this proxy removes the need for the user
///        to paste a client_id into the MCP client.
/// </summary>
public static class OAuthProxyEndpoints
{
    private const string EntraV2BaseTemplate =
        "https://login.microsoftonline.com/{0}/oauth2/v2.0";

    public static IEndpointRouteBuilder MapOAuthProxyEndpoints(
        this IEndpointRouteBuilder app,
        AuthenticationConfiguration config)
    {
        var proxy = config.OAuthProxy;
        if (proxy is null || !proxy.Enabled)
            return app;

        if (string.IsNullOrWhiteSpace(proxy.TenantId))
            return app;

        var tenantBase = string.Format(EntraV2BaseTemplate, proxy.TenantId);
        var authEndpoint = $"{tenantBase}/authorize";
        var tokenEndpoint = $"{tenantBase}/token";

        // Scopes: always expose openid + offline_access + any configured audience scope
        var scopesSupported = new List<string>
        {
            "openid", "profile", "email", "offline_access"
        };
        if (!string.IsNullOrWhiteSpace(proxy.Audience))
        {
            // e.g. "api://poshmcp-prod/.default"
            scopesSupported.Add($"{proxy.Audience.TrimEnd('/')}/.default");
        }

        // ── /.well-known/oauth-authorization-server ──────────────────────────
        app.MapGet("/.well-known/oauth-authorization-server", (HttpContext httpContext) =>
        {
            var baseUrl = GetServerBaseUrl(httpContext);
            var registrationEndpoint = $"{baseUrl}/register";
            var issuer = $"https://login.microsoftonline.com/{proxy.TenantId}/v2.0";

            var metadata = new
            {
                issuer,
                authorization_endpoint = authEndpoint,
                token_endpoint = tokenEndpoint,
                registration_endpoint = registrationEndpoint,
                scopes_supported = scopesSupported,
                response_types_supported = new[] { "code" },
                grant_types_supported = new[] { "authorization_code", "refresh_token" },
                code_challenge_methods_supported = new[] { "S256" },
                token_endpoint_auth_methods_supported = new[] { "none" }
            };

            return Results.Ok(metadata);
        }).AllowAnonymous();

        // ── /register (DCR proxy) ─────────────────────────────────────────────
        app.MapPost("/register", () =>
        {
            if (string.IsNullOrWhiteSpace(proxy.ClientId))
            {
                return Results.Problem(
                    detail: "OAuth proxy ClientId is not configured on this server.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            var issuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var response = new
            {
                client_id = proxy.ClientId,
                client_id_issued_at = issuedAt,
                // Signal that this is a public client — no secret required
                token_endpoint_auth_method = "none"
            };

            return Results.Json(response, statusCode: StatusCodes.Status201Created);
        }).AllowAnonymous();

        return app;
    }

    private static string GetServerBaseUrl(HttpContext ctx)
    {
        var req = ctx.Request;
        // Honour X-Forwarded-* headers that Azure Container Apps sets
        var scheme = req.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? req.Scheme;
        var host = req.Headers["X-Forwarded-Host"].FirstOrDefault()
                   ?? req.Host.ToUriComponent();
        return $"{scheme}://{host}{req.PathBase}".TrimEnd('/');
    }
}
