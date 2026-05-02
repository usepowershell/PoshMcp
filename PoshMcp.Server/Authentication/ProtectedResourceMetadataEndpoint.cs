using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;

namespace PoshMcp.Server.Authentication;

public static class ProtectedResourceMetadataEndpoint
{
    public static IEndpointRouteBuilder MapProtectedResourceMetadata(
        this IEndpointRouteBuilder app,
        AuthenticationConfiguration config)
    {
        if (!config.Enabled || config.ProtectedResource is null)
            return app;

        app.MapGet("/.well-known/oauth-protected-resource", (HttpContext httpContext) =>
        {
            var req = httpContext.Request;
            var scheme = req.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? req.Scheme;
            var host = req.Headers["X-Forwarded-Host"].FirstOrDefault()
                       ?? req.Host.ToUriComponent();
            var serverBase = $"{scheme}://{host}{req.PathBase}".TrimEnd('/');

            // When the OAuth proxy is enabled and no authorization_servers are
            // explicitly configured, advertise this server itself as the AS.
            // MCP clients will then follow up with a request to
            // /.well-known/oauth-authorization-server on this server and discover
            // the real Entra ID auth/token endpoints + our DCR /register proxy.
            var authServers = config.ProtectedResource.AuthorizationServers;
            if (authServers.Count == 0
                && config.OAuthProxy is { Enabled: true }
                && !string.IsNullOrWhiteSpace(config.OAuthProxy.TenantId))
            {
                authServers = new List<string> { serverBase };
            }

            // RFC 9728 requires `resource` to be an HTTPS URI that MCP clients can use
            // to fetch this document.  When the configured value uses a non-HTTP scheme
            // (e.g. "api://..." for an Entra Application ID URI), substitute the server's
            // canonical HTTPS base URL so clients can validate the metadata URL round-trip.
            var resourceUri = config.ProtectedResource.Resource;
            if (!string.IsNullOrEmpty(resourceUri)
                && !resourceUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                && config.OAuthProxy is { Enabled: true })
            {
                resourceUri = serverBase;
            }

            var metadata = new
            {
                resource = resourceUri,
                resource_name = config.ProtectedResource.ResourceName,
                authorization_servers = authServers,
                scopes_supported = config.ProtectedResource.ScopesSupported,
                bearer_methods_supported = config.ProtectedResource.BearerMethodsSupported
                    .Count > 0 ? config.ProtectedResource.BearerMethodsSupported : new List<string> { "header" }
            };
            return Results.Ok(metadata);
        }).AllowAnonymous(); // Metadata endpoint is always public per RFC 9728

        return app;
    }
}
