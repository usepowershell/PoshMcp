using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System;
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
        return requiredRoles.Any(r => user.IsInRole(r));
    }

    public static FunctionOverride? GetToolOverride(string toolName, PowerShellConfiguration psConfig)
    {
        foreach (var candidate in GetOverrideKeyCandidates(toolName, psConfig))
        {
            if (psConfig.TryGetCommandOverride(candidate, out var overrideConfig))
            {
                return overrideConfig;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetOverrideKeyCandidates(string toolName, PowerShellConfiguration psConfig)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            yield break;
        }

        // 1) Raw tool name from MCP request.
        yield return toolName;

        // 2) Common normalization from generated method naming.
        var normalized = toolName.Replace('_', '-');
        if (!string.Equals(normalized, toolName, StringComparison.Ordinal))
        {
            yield return normalized;
        }

        // 3) Match generated snake_case tool names back to configured command names.
        foreach (var candidate in GetCommandNameCandidatesFromSnakeCase(toolName, psConfig))
        {
            yield return candidate;
        }

        // 4) Fallback: progressively trim suffix segments for names carrying parameter-set tails.
        foreach (var candidate in GetTruncatedHyphenCandidates(normalized))
        {
            yield return candidate;
        }
    }

    private static IEnumerable<string> GetCommandNameCandidatesFromSnakeCase(string toolName, PowerShellConfiguration psConfig)
    {
        var parts = toolName
            .Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            yield break;
        }

        var configuredCommands = psConfig.GetEffectiveCommandNames();
        var verb = Capitalize(parts[0]);
        var nounParts = parts.Skip(1).Select(Capitalize).ToArray();

        for (var partCount = nounParts.Length; partCount >= 1; partCount--)
        {
            var candidate = $"{verb}-{string.Concat(nounParts.Take(partCount))}";
            if (configuredCommands.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                yield return candidate;
            }
        }

        for (var partCount = nounParts.Length; partCount >= 1; partCount--)
        {
            var candidate = $"{verb}-{string.Concat(nounParts.Take(partCount))}";
            yield return candidate;
        }
    }

    private static IEnumerable<string> GetTruncatedHyphenCandidates(string normalizedToolName)
    {
        if (string.IsNullOrWhiteSpace(normalizedToolName) || !normalizedToolName.Contains('-', StringComparison.Ordinal))
        {
            yield break;
        }

        var parts = normalizedToolName.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            yield break;
        }

        for (var count = parts.Length - 1; count >= 2; count--)
        {
            yield return string.Join('-', parts.Take(count));
        }
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }

    public static string GetScopeClaim(AuthenticationConfiguration authConfig)
    {
        return authConfig.Schemes.TryGetValue(authConfig.DefaultScheme, out var scheme)
            ? scheme.ClaimsMapping?.ScopeClaim ?? "scp"
            : "scp";
    }
}
