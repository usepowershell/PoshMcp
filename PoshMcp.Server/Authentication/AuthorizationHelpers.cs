using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using PoshMcp.Server.PowerShell;

namespace PoshMcp.Server.Authentication;

public static class AuthorizationHelpers
{
    public static bool HasRequiredScopes(ClaimsPrincipal? user, List<string> requiredScopes, string scopeClaim)
    {
        if (requiredScopes.Count == 0) return true;
        if (user == null) return false;
        var userScopes = user.FindAll(scopeClaim).Select(c => c.Value).ToHashSet();
        return requiredScopes.All(s => userScopes.Contains(s));
    }

    public static bool HasRequiredRoles(ClaimsPrincipal? user, List<string> requiredRoles)
    {
        if (requiredRoles.Count == 0) return true;
        if (user == null) return false;
        return requiredRoles.All(r => user.IsInRole(r));
    }

    public static FunctionOverride? GetToolOverride(string toolName, PowerShellConfiguration psConfig)
    {
        if (psConfig.FunctionOverrides.TryGetValue(toolName, out var exact)) return exact;
        var normalized = toolName.Replace('_', '-');
        if (psConfig.FunctionOverrides.TryGetValue(normalized, out var norm)) return norm;
        return null;
    }

    public static string GetScopeClaim(AuthenticationConfiguration authConfig)
    {
        return authConfig.Schemes.TryGetValue(authConfig.DefaultScheme, out var scheme)
            ? scheme.ClaimsMapping?.ScopeClaim ?? "scp"
            : "scp";
    }
}
