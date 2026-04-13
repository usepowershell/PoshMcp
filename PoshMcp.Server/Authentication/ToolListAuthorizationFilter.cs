using System.Collections.Generic;
using System.Linq;
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
/// MCP request filter that removes tools from tools/list responses that the caller
/// is not authorized to invoke. Prevents information leakage about inaccessible tools.
/// When authentication is disabled, the filter is a no-op.
/// </summary>
public class ToolListAuthorizationFilter(
    AuthenticationConfiguration authConfig,
    PowerShellConfiguration psConfig,
    IHttpContextAccessor httpContextAccessor,
    ILogger<ToolListAuthorizationFilter> logger)
{
    private readonly string _scopeClaim = AuthorizationHelpers.GetScopeClaim(authConfig);

    /// <summary>
    /// Returns a <see cref="McpRequestFilter{TParams,TResult}"/> that filters the tools list
    /// to only include tools the caller is authorized to invoke.
    /// </summary>
    public McpRequestFilter<ListToolsRequestParams, ListToolsResult> AsFilter() =>
        (next) => async (context, ct) => await InvokeAsync(context, ct, next);

    private async Task<ListToolsResult> InvokeAsync(
        RequestContext<ListToolsRequestParams> context,
        CancellationToken ct,
        McpRequestHandler<ListToolsRequestParams, ListToolsResult> next)
    {
        var result = await next(context, ct);

        if (!authConfig.Enabled)
            return result;

        if (result.Tools == null || result.Tools.Count == 0)
            return result;

        var user = context.User ?? httpContextAccessor.HttpContext?.User;
        var filtered = result.Tools.Where(tool => CanAccessTool(tool.Name, user)).ToList();
        result.Tools = filtered;
        return result;
    }

    private bool CanAccessTool(string toolName, ClaimsPrincipal? user)
    {
        var toolOverride = AuthorizationHelpers.GetToolOverride(toolName, psConfig);

        if (toolOverride?.AllowAnonymous == true)
            return true;

        if (authConfig.DefaultPolicy.RequireAuthentication && user?.Identity?.IsAuthenticated != true)
        {
            logger.LogDebug("Tool '{ToolName}' excluded from list: unauthenticated user", toolName);
            return false;
        }

        var requiredScopes = toolOverride?.RequiredScopes ?? authConfig.DefaultPolicy.RequiredScopes;
        var requiredRoles = toolOverride?.RequiredRoles ?? authConfig.DefaultPolicy.RequiredRoles;

        var hasAccess = AuthorizationHelpers.HasRequiredScopes(user, requiredScopes, _scopeClaim) &&
                        AuthorizationHelpers.HasRequiredRoles(user, requiredRoles);

        if (!hasAccess)
            logger.LogDebug("Tool '{ToolName}' excluded from list: insufficient permissions", toolName);

        return hasAccess;
    }
}
