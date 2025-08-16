using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using PoshMcp.Server.PowerShell;
using System.Reflection;

namespace PoshMcp.Web.Authentication;

/// <summary>
/// Authorization-aware wrapper for MCP tools that enforces command-specific authentication requirements
/// </summary>
public class AuthorizedMcpToolWrapper
{
    private readonly CommandAuthenticationGroup? _authRequirements;
    private readonly ILogger _logger;
    private readonly string _toolName;

    public AuthorizedMcpToolWrapper(
        string toolName,
        CommandAuthenticationGroup? authRequirements,
        ILogger logger)
    {
        _toolName = toolName ?? throw new ArgumentNullException(nameof(toolName));
        _authRequirements = authRequirements;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates an authorized version of the given tool delegate
    /// </summary>
    public TDelegate CreateAuthorizedDelegate<TDelegate>(TDelegate originalDelegate, IHttpContextAccessor httpContextAccessor) where TDelegate : Delegate
    {
        // If no authentication requirements, return the original delegate
        if (_authRequirements == null || _authRequirements.Type == AuthenticationType.None)
        {
            return originalDelegate;
        }

        // Create a wrapper delegate that enforces authorization
        var wrapperMethod = typeof(AuthorizedMcpToolWrapper)
            .GetMethod(nameof(CreateAuthorizationWrapper), BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(typeof(TDelegate));

        return (TDelegate)wrapperMethod.Invoke(this, new object[] { originalDelegate, httpContextAccessor })!;
    }

    private TDelegate CreateAuthorizationWrapper<TDelegate>(TDelegate originalDelegate, IHttpContextAccessor httpContextAccessor) where TDelegate : Delegate
    {
        var originalMethod = originalDelegate.Method;
        var parameterTypes = originalMethod.GetParameters().Select(p => p.ParameterType).ToArray();
        var returnType = originalMethod.ReturnType;

        // Create a dynamic method that wraps the original with authorization
        var wrapper = new Func<object?[], object?>(args =>
        {
            try
            {
                // Get the current HTTP context
                var httpContext = httpContextAccessor.HttpContext;
                if (httpContext == null)
                {
                    _logger.LogWarning($"No HTTP context available for authorization check on tool '{_toolName}'");
                    throw new UnauthorizedAccessException("Authentication context not available");
                }

                // Check authorization requirements
                var isAuthorized = CheckAuthorization(httpContext.User);
                if (!isAuthorized)
                {
                    _logger.LogWarning($"Authorization failed for tool '{_toolName}' - user does not meet requirements: {GetRequirementsDescription()}");
                    throw new UnauthorizedAccessException($"Access denied: {GetRequirementsDescription()}");
                }

                _logger.LogDebug($"Authorization successful for tool '{_toolName}'");

                // If authorized, call the original delegate
                return originalDelegate.DynamicInvoke(args);
            }
            catch (UnauthorizedAccessException)
            {
                throw; // Re-throw authorization exceptions as-is
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during authorized execution of tool '{_toolName}': {ex.Message}");
                throw;
            }
        });

        // Convert the wrapper function back to the expected delegate type
        return (TDelegate)Delegate.CreateDelegate(typeof(TDelegate), wrapper.Target, wrapper.Method);
    }

    private bool CheckAuthorization(ClaimsPrincipal user)
    {
        if (_authRequirements == null || _authRequirements.Type == AuthenticationType.None)
        {
            return true; // No authentication required
        }

        // If authentication is required but user is not authenticated
        if (!user.Identity?.IsAuthenticated == true)
        {
            _logger.LogDebug($"User not authenticated for tool '{_toolName}' requiring {_authRequirements.Type}");
            return false;
        }

        switch (_authRequirements.Type)
        {
            case AuthenticationType.Role:
                return CheckRoleRequirement(user);

            case AuthenticationType.Permission:
                return CheckPermissionRequirement(user);

            default:
                _logger.LogWarning($"Unknown authentication type: {_authRequirements.Type}");
                return false;
        }
    }

    private bool CheckRoleRequirement(ClaimsPrincipal user)
    {
        if (string.IsNullOrEmpty(_authRequirements?.Role))
        {
            _logger.LogWarning($"Role requirement specified but no role configured for tool '{_toolName}'");
            return false;
        }

        // Check for the required role in user claims
        var hasRole = user.IsInRole(_authRequirements.Role) || 
                     user.HasClaim(ClaimTypes.Role, _authRequirements.Role) ||
                     user.HasClaim("roles", _authRequirements.Role);

        _logger.LogDebug($"Role check for '{_authRequirements.Role}' on tool '{_toolName}': {hasRole}");
        return hasRole;
    }

    private bool CheckPermissionRequirement(ClaimsPrincipal user)
    {
        if (string.IsNullOrEmpty(_authRequirements?.Permission))
        {
            _logger.LogWarning($"Permission requirement specified but no permission configured for tool '{_toolName}'");
            return false;
        }

        // Check for the required permission/scope in user claims
        var hasPermission = user.HasClaim("scp", _authRequirements.Permission) ||
                           user.HasClaim("scope", _authRequirements.Permission) ||
                           user.HasClaim("permissions", _authRequirements.Permission);

        _logger.LogDebug($"Permission check for '{_authRequirements.Permission}' on tool '{_toolName}': {hasPermission}");
        return hasPermission;
    }

    private string GetRequirementsDescription()
    {
        return _authRequirements?.Type switch
        {
            AuthenticationType.Role => $"Role '{_authRequirements.Role}' required",
            AuthenticationType.Permission => $"Permission '{_authRequirements.Permission}' required",
            _ => "Authentication required"
        };
    }
}

/// <summary>
/// Factory for creating authorization-aware MCP tools
/// </summary>
public static class AuthorizedMcpToolFactory
{
    /// <summary>
    /// Wraps a list of MCP tools with authorization enforcement based on PowerShell configuration
    /// </summary>
    /// <param name="tools">Original MCP tools</param>
    /// <param name="config">PowerShell configuration containing authentication requirements</param>
    /// <param name="httpContextAccessor">HTTP context accessor for getting user claims</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>List of authorization-aware MCP tools</returns>
    public static List<McpServerTool> CreateAuthorizedTools(
        List<McpServerTool> tools,
        PowerShellConfiguration config,
        IHttpContextAccessor httpContextAccessor,
        ILogger logger)
    {
        var authorizedTools = new List<McpServerTool>();

        foreach (var tool in tools)
        {
            try
            {
                // Extract command name from tool 
                var commandName = ExtractCommandNameFromTool(tool);
                
                // Get authentication requirements for this command
                var authRequirements = config.GetAuthenticationRequirements(commandName);

                // If no authentication requirements, add the original tool
                if (authRequirements == null || authRequirements.Type == AuthenticationType.None)
                {
                    authorizedTools.Add(tool);
                    logger.LogDebug($"Added tool '{GetToolName(tool)}' for command '{commandName}' with no authentication requirements");
                    continue;
                }

                // For tools that require authentication, we would need to create new authorized versions
                // However, this is complex because McpServerTool is abstract and the actual implementation
                // details are not accessible. For now, we'll add the original tool but log the requirement.
                authorizedTools.Add(tool);
                logger.LogInformation($"Tool '{GetToolName(tool)}' for command '{commandName}' requires {authRequirements.Type} authentication: {GetAuthRequirementDescription(authRequirements)}");
                
                // TODO: In a future iteration, we could implement tool-level authorization by:
                // 1. Using middleware to intercept tool calls
                // 2. Creating custom tool handlers with authorization logic
                // 3. Implementing authorization at the MCP server level
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Failed to process authorization for tool: {ex.Message}");
                // Add the original tool if processing fails
                authorizedTools.Add(tool);
            }
        }

        logger.LogInformation($"Processed {authorizedTools.Count} MCP tools for authorization");
        return authorizedTools;
    }

    private static string ExtractCommandNameFromTool(McpServerTool tool)
    {
        // Use reflection to get tool properties since the API is not directly accessible
        var nameProperty = tool.GetType().GetProperty("Name");
        var titleProperty = tool.GetType().GetProperty("Title");
        
        var toolName = nameProperty?.GetValue(tool)?.ToString() ?? "unknown";
        var toolTitle = titleProperty?.GetValue(tool)?.ToString() ?? "";
        
        // If the title looks like a PowerShell command (contains hyphens), use it
        if (toolTitle.Contains('-'))
        {
            return toolTitle.Split(' ')[0]; // Take first word if there's a description
        }
        
        // Otherwise, try to extract from the tool name
        // Convert "get_process_name" to "Get-Process"
        var parts = toolName.Split('_');
        if (parts.Length >= 2)
        {
            var verb = char.ToUpper(parts[0][0]) + parts[0].Substring(1);
            var noun = char.ToUpper(parts[1][0]) + parts[1].Substring(1);
            return $"{verb}-{noun}";
        }
        
        return toolName;
    }

    private static string GetToolName(McpServerTool tool)
    {
        var nameProperty = tool.GetType().GetProperty("Name");
        return nameProperty?.GetValue(tool)?.ToString() ?? "unknown";
    }

    private static string GetAuthRequirementDescription(CommandAuthenticationGroup authRequirements)
    {
        return authRequirements.Type switch
        {
            AuthenticationType.Role => $"Role '{authRequirements.Role}'",
            AuthenticationType.Permission => $"Permission '{authRequirements.Permission}'",
            _ => "Authentication"
        };
    }
}