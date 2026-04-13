using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PoshMcp.Server.PowerShell;

namespace PoshMcp.Server.Authentication;

/// <summary>
/// MCP request filter that enforces per-tool authorization policies for tools/call requests.
/// When authentication is disabled, the filter is a no-op.
/// </summary>
public class ToolAuthorizationFilter(
    AuthenticationConfiguration authConfig,
    PowerShellConfiguration psConfig,
    IHttpContextAccessor httpContextAccessor,
    ILogger<ToolAuthorizationFilter> logger)
{
    private readonly string _scopeClaim = AuthorizationHelpers.GetScopeClaim(authConfig);

    /// <summary>
    /// Returns a <see cref="McpRequestFilter{TParams,TResult}"/> that enforces authorization
    /// before forwarding the request to the next handler.
    /// </summary>
    public McpRequestFilter<CallToolRequestParams, CallToolResult> AsFilter() =>
        (next) => async (context, ct) => await InvokeAsync(context, ct, next);

    private async Task<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> context,
        CancellationToken ct,
        McpRequestHandler<CallToolRequestParams, CallToolResult> next)
    {
        if (!authConfig.Enabled)
            return await next(context, ct);

        var toolName = context.Params?.Name ?? string.Empty;
        var toolOverride = AuthorizationHelpers.GetToolOverride(toolName, psConfig);

        if (toolOverride?.AllowAnonymous == true)
        {
            logger.LogDebug("Tool '{ToolName}' allows anonymous access — bypassing authorization", toolName);
            return await next(context, ct);
        }

        var user = context.User ?? httpContextAccessor.HttpContext?.User;

        if (authConfig.DefaultPolicy.RequireAuthentication && user?.Identity?.IsAuthenticated != true)
        {
            logger.LogWarning("Unauthenticated request to tool '{ToolName}' denied", toolName);
            return ErrorResult($"Authentication required to call tool '{toolName}'");
        }

        var requiredScopes = toolOverride?.RequiredScopes ?? authConfig.DefaultPolicy.RequiredScopes;
        var requiredRoles = toolOverride?.RequiredRoles ?? authConfig.DefaultPolicy.RequiredRoles;

        if (!AuthorizationHelpers.HasRequiredScopes(user, requiredScopes, _scopeClaim) ||
            !AuthorizationHelpers.HasRequiredRoles(user, requiredRoles))
        {
            logger.LogWarning("Insufficient permissions for user calling tool '{ToolName}'", toolName);
            return ErrorResult($"Insufficient permissions to call tool '{toolName}'");
        }

        logger.LogDebug("Authorization passed for tool '{ToolName}'", toolName);
        return await next(context, ct);
    }

    private static CallToolResult ErrorResult(string message) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = message }]
    };
}
