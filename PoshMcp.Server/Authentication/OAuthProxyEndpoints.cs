using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
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
            // Advertise the explicit delegated scope, not .default.
            // .default causes Entra to issue v1.0 tokens (sts.windows.net issuer)
            // when the app registration targets v1.0, failing v2.0 issuer validation.
            var audienceBase = proxy.Audience.TrimEnd('/');
            var explicitScope = config.DefaultPolicy?.RequiredScopes
                .FirstOrDefault(s => s.StartsWith(audienceBase, StringComparison.OrdinalIgnoreCase));
            scopesSupported.Add(explicitScope ?? $"{audienceBase}/user_impersonation");
        }

        // ── /.well-known/oauth-authorization-server ──────────────────────────
        app.MapGet("/.well-known/oauth-authorization-server", (HttpContext httpContext) =>
        {
            var baseUrl = GetServerBaseUrl(httpContext);
            var registrationEndpoint = $"{baseUrl}/register";
            var issuer = $"https://login.microsoftonline.com/{proxy.TenantId}/v2.0";

            // Point authorization_endpoint and token_endpoint to this server's proxy
            // endpoints so that VS Code routes all OAuth traffic through them.
            // The proxies strip the `resource` parameter (v1.0 only) which causes
            // AADSTS9010010 when Entra v2.0 receives both `resource` and `scope`.
            var metadata = new
            {
                issuer,
                authorization_endpoint = $"{baseUrl}/authorize",
                token_endpoint = $"{baseUrl}/token",
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

        // ── /authorize (redirect proxy) ───────────────────────────────────────
        // VS Code constructs the auth URL as {authorization_server_base}/authorize rather
        // than using authorization_endpoint from AS metadata directly.  This endpoint
        // intercepts that request, swaps the ephemeral DCR client_id for the real Entra
        // client_id, and issues a 302 redirect to Entra's real authorize endpoint.
        app.MapGet("/authorize", (HttpContext httpContext, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("PoshMcp.Server.Authentication.OAuthProxyEndpoints");

            if (string.IsNullOrWhiteSpace(proxy.ClientId))
            {
                return Results.Problem(
                    detail: "OAuth proxy ClientId is not configured on this server.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            // Build new query params: strip `resource` (v1.0 only — not valid on v2.0
            // endpoint and causes AADSTS9010010), forward everything else, replace client_id.
            var queryParams = httpContext.Request.Query
                .Where(kvp => !kvp.Key.Equals("resource", StringComparison.OrdinalIgnoreCase))
                .SelectMany(kvp => kvp.Value.Select(v =>
                    new KeyValuePair<string, string?>(
                        kvp.Key,
                        kvp.Key.Equals("client_id", StringComparison.OrdinalIgnoreCase)
                            ? proxy.ClientId
                            : v)))
                .ToList();

            // Ensure client_id is always present even if missing from the original request.
            if (!queryParams.Any(kv => kv.Key.Equals("client_id", StringComparison.OrdinalIgnoreCase)))
                queryParams.Add(new KeyValuePair<string, string?>("client_id", proxy.ClientId));

            var redirectUrl = $"{authEndpoint}{QueryString.Create(queryParams)}";

            logger.LogDebug(
                "Redirecting /authorize to Entra tenant {TenantId} (client_id replaced with configured value)",
                proxy.TenantId);

            return Results.Redirect(redirectUrl, permanent: false);
        }).AllowAnonymous();

        // ── /token (proxy) ────────────────────────────────────────────────────
        // VS Code sends the authorization-code token exchange to token_endpoint (from AS
        // metadata).  Per MCP spec (RFC 8707) it includes a `resource` parameter taken from
        // the PRM.  Entra v2.0 rejects requests that combine `resource` (v1.0) with `scope`
        // pointing to a different resource (AADSTS9010010).  This proxy strips `resource`
        // before forwarding to Entra's real token endpoint so the exchange succeeds.
        app.MapPost("/token", async (HttpContext httpContext, IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("PoshMcp.Server.Authentication.OAuthProxyEndpoints");

            var form = await httpContext.Request.ReadFormAsync();
            var fields = form
                .Where(kv => !kv.Key.Equals("resource", StringComparison.OrdinalIgnoreCase))
                .SelectMany(kv => kv.Value.Select(v => new KeyValuePair<string, string?>(kv.Key, v)))
                .ToList();

            logger.LogDebug(
                "Proxying /token to Entra tenant {TenantId}, stripped `resource` parameter if present",
                proxy.TenantId);

            using var client = httpClientFactory.CreateClient();
            var entraResponse = await client.PostAsync(tokenEndpoint, new FormUrlEncodedContent(fields!));

            var responseBody = await entraResponse.Content.ReadAsStringAsync();
            var contentType = entraResponse.Content.Headers.ContentType?.ToString() ?? "application/json";

            return Results.Content(responseBody, contentType, statusCode: (int)entraResponse.StatusCode);
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
