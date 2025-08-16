using System;
using System.IO;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PoshMcp.Server.PowerShell;

namespace PoshMcp.Web.Authentication;

/// <summary>
/// Middleware that enforces per-command authentication requirements for MCP tool calls
/// </summary>
public class McpToolAuthorizationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly PowerShellConfiguration _config;
    private readonly ILogger<McpToolAuthorizationMiddleware> _logger;

    public McpToolAuthorizationMiddleware(
        RequestDelegate next,
        IOptions<PowerShellConfiguration> config,
        ILogger<McpToolAuthorizationMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only check authorization for MCP tool calls
        if (IsToolCallRequest(context.Request))
        {
            var toolCallInfo = await ParseToolCallRequest(context);
            if (toolCallInfo != null)
            {
                var authResult = CheckToolAuthorization(toolCallInfo.ToolName, context.User);
                if (!authResult.IsAuthorized)
                {
                    _logger.LogWarning($"Authorization denied for tool '{toolCallInfo.ToolName}': {authResult.Reason}");
                    await WriteUnauthorizedResponse(context, authResult.Reason);
                    return;
                }

                _logger.LogDebug($"Authorization granted for tool '{toolCallInfo.ToolName}'");
            }
        }

        // Continue to the next middleware
        await _next(context);
    }

    private static bool IsToolCallRequest(HttpRequest request)
    {
        return request.Method == HttpMethods.Post &&
               (request.Path.StartsWithSegments("/tools/call") ||
                request.Path.StartsWithSegments("/v1/tools/call") ||
                request.Path.Value?.Contains("tools/call") == true);
    }

    private async Task<ToolCallInfo?> ParseToolCallRequest(HttpContext context)
    {
        try
        {
            // Enable buffering so we can read the body multiple times
            context.Request.EnableBuffering();
            
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            
            // Reset the stream position for downstream middleware
            context.Request.Body.Position = 0;

            if (string.IsNullOrEmpty(body))
            {
                return null;
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            // Look for tool name in different possible locations
            var toolName = ExtractToolNameFromRequest(root);
            if (!string.IsNullOrEmpty(toolName))
            {
                return new ToolCallInfo { ToolName = toolName };
            }

            _logger.LogDebug("Could not extract tool name from request body");
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogDebug($"Failed to parse tool call request JSON: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error parsing tool call request: {ex.Message}");
            return null;
        }
    }

    private static string? ExtractToolNameFromRequest(JsonElement root)
    {
        // Check for tool name in MCP format: { "params": { "name": "tool_name" } }
        if (root.TryGetProperty("params", out var paramsElement) &&
            paramsElement.TryGetProperty("name", out var nameElement))
        {
            return nameElement.GetString();
        }

        // Check for direct tool name: { "name": "tool_name" }
        if (root.TryGetProperty("name", out var directNameElement))
        {
            return directNameElement.GetString();
        }

        // Check for tool name in method: { "method": "tools/call", "params": { "name": "tool_name" } }
        if (root.TryGetProperty("method", out var methodElement) &&
            methodElement.GetString() == "tools/call" &&
            root.TryGetProperty("params", out var methodParamsElement) &&
            methodParamsElement.TryGetProperty("name", out var methodNameElement))
        {
            return methodNameElement.GetString();
        }

        return null;
    }

    private AuthorizationResult CheckToolAuthorization(string toolName, ClaimsPrincipal user)
    {
        try
        {
            // Convert tool name back to PowerShell command name
            var commandName = ConvertToolNameToCommandName(toolName);
            
            // Get authentication requirements for this command
            var authRequirements = _config.GetAuthenticationRequirements(commandName);

            if (authRequirements == null || authRequirements.Type == AuthenticationType.None)
            {
                return AuthorizationResult.Success();
            }

            // Check if user is authenticated when authentication is required
            if (!user.Identity?.IsAuthenticated == true)
            {
                return AuthorizationResult.Failure("Authentication required");
            }

            switch (authRequirements.Type)
            {
                case AuthenticationType.Role:
                    return CheckRoleRequirement(user, authRequirements.Role);

                case AuthenticationType.Permission:
                    return CheckPermissionRequirement(user, authRequirements.Permission);

                default:
                    return AuthorizationResult.Failure($"Unknown authentication type: {authRequirements.Type}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error checking authorization for tool '{toolName}': {ex.Message}");
            return AuthorizationResult.Failure("Authorization check failed");
        }
    }

    private AuthorizationResult CheckRoleRequirement(ClaimsPrincipal user, string? requiredRole)
    {
        if (string.IsNullOrEmpty(requiredRole))
        {
            return AuthorizationResult.Failure("Role requirement configured but no role specified");
        }

        var hasRole = user.IsInRole(requiredRole) ||
                     user.HasClaim(ClaimTypes.Role, requiredRole) ||
                     user.HasClaim("roles", requiredRole);

        return hasRole
            ? AuthorizationResult.Success()
            : AuthorizationResult.Failure($"Role '{requiredRole}' required");
    }

    private AuthorizationResult CheckPermissionRequirement(ClaimsPrincipal user, string? requiredPermission)
    {
        if (string.IsNullOrEmpty(requiredPermission))
        {
            return AuthorizationResult.Failure("Permission requirement configured but no permission specified");
        }

        var hasPermission = user.HasClaim("scp", requiredPermission) ||
                           user.HasClaim("scope", requiredPermission) ||
                           user.HasClaim("permissions", requiredPermission);

        return hasPermission
            ? AuthorizationResult.Success()
            : AuthorizationResult.Failure($"Permission '{requiredPermission}' required");
    }

    private static string ConvertToolNameToCommandName(string toolName)
    {
        // Convert tool names like "get_process_name" back to "Get-Process"
        var parts = toolName.Split('_');
        if (parts.Length >= 2)
        {
            var verb = char.ToUpper(parts[0][0]) + parts[0].Substring(1);
            var noun = char.ToUpper(parts[1][0]) + parts[1].Substring(1);
            return $"{verb}-{noun}";
        }

        return toolName;
    }

    private async Task WriteUnauthorizedResponse(HttpContext context, string reason)
    {
        context.Response.StatusCode = 403;
        context.Response.ContentType = "application/json";

        var response = new
        {
            error = new
            {
                code = -32600,
                message = "Forbidden",
                data = reason
            }
        };

        var json = JsonSerializer.Serialize(response);
        await context.Response.WriteAsync(json);
    }

    private class ToolCallInfo
    {
        public string ToolName { get; set; } = string.Empty;
    }

    private class AuthorizationResult
    {
        public bool IsAuthorized { get; private set; }
        public string Reason { get; private set; } = string.Empty;

        private AuthorizationResult(bool isAuthorized, string reason = "")
        {
            IsAuthorized = isAuthorized;
            Reason = reason;
        }

        public static AuthorizationResult Success() => new(true);
        public static AuthorizationResult Failure(string reason) => new(false, reason);
    }
}