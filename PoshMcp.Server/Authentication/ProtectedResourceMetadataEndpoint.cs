using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace PoshMcp.Server.Authentication;

public static class ProtectedResourceMetadataEndpoint
{
    public static IEndpointRouteBuilder MapProtectedResourceMetadata(
        this IEndpointRouteBuilder app,
        AuthenticationConfiguration config)
    {
        if (!config.Enabled || config.ProtectedResource is null)
            return app;

        app.MapGet("/.well-known/oauth-protected-resource", () =>
        {
            var metadata = new
            {
                resource = config.ProtectedResource.Resource,
                resource_name = config.ProtectedResource.ResourceName,
                authorization_servers = config.ProtectedResource.AuthorizationServers,
                scopes_supported = config.ProtectedResource.ScopesSupported,
                bearer_methods_supported = config.ProtectedResource.BearerMethodsSupported
                    .Count > 0 ? config.ProtectedResource.BearerMethodsSupported : new List<string> { "header" }
            };
            return Results.Ok(metadata);
        }).AllowAnonymous(); // Metadata endpoint is always public per RFC 9728

        return app;
    }
}
