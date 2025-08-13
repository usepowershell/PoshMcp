using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace PoshMcp.Web.Authentication;

/// <summary>
/// Extension methods for conditional authorization of MCP endpoints
/// </summary>
public static class McpAuthorizationExtensions
{
    /// <summary>
    /// Maps MCP endpoints with conditional authorization based on Entra ID configuration
    /// </summary>
    /// <param name="app">The web application</param>
    /// <returns>The web application for chaining</returns>
    public static WebApplication MapMcpWithConditionalAuth(this WebApplication app)
    {
        // Get the Entra ID configuration
        var entraIdConfig = app.Services.GetService<IOptions<EntraIdConfiguration>>()?.Value ?? new EntraIdConfiguration();

        // Map MCP endpoints
        var mcpEndpoints = app.MapMcp();

        // Apply authorization policy if authentication is enabled
        if (entraIdConfig.Enabled)
        {
            mcpEndpoints.RequireAuthorization("McpEndpointAccess");
        }

        return app;
    }
}