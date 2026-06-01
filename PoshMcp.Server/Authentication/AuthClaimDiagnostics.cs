using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using PoshMcp.Server.Observability;

namespace PoshMcp.Server.Authentication;

/// <summary>
/// Builds safe authentication claim diagnostics from a validated principal.
/// </summary>
internal static class AuthClaimDiagnostics
{
    private static readonly string[] AudienceClaimTypes = ["aud"];
    private static readonly string[] ScopeClaimTypes = ["scp", "scope"];
    private static readonly string[] RoleClaimTypes = ["roles", "role", ClaimTypes.Role];
    private static readonly string[] IssuerClaimTypes = ["iss"];

    /// <summary>
    /// Creates a sanitized diagnostic summary containing only auth-relevant safe fields.
    /// </summary>
    /// <param name="principal">Validated claims principal. May be null.</param>
    /// <returns>A sanitized diagnostic summary for audience, scope, roles, and issuer.</returns>
    public static SafeAuthClaimSummary BuildSafeSummary(ClaimsPrincipal? principal)
    {
        return new SafeAuthClaimSummary(
            GetSafeValues(principal, AudienceClaimTypes),
            GetSafeValues(principal, ScopeClaimTypes),
            GetSafeValues(principal, RoleClaimTypes),
            GetSafeValues(principal, IssuerClaimTypes));
    }

    private static string GetSafeValues(ClaimsPrincipal? principal, IReadOnlyCollection<string> claimTypes)
    {
        if (principal is null)
            return string.Empty;

        var values = principal.Claims
            .Where(claim => claimTypes.Contains(claim.Type, StringComparer.OrdinalIgnoreCase))
            .Select(claim => claim.Value)
            .Where(value => !string.IsNullOrEmpty(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return LogSanitizer.Scrub(string.Join(",", values));
    }
}

/// <summary>
/// Sanitized JWT authorization diagnostic values safe for structured logging.
/// </summary>
/// <param name="Audience">Allowed audience claim values.</param>
/// <param name="Scopes">Allowed scope claim values from <c>scp</c> or <c>scope</c>.</param>
/// <param name="Roles">Allowed role claim values from <c>roles</c> or <c>role</c>.</param>
/// <param name="Issuer">Allowed issuer claim values.</param>
internal sealed record SafeAuthClaimSummary(string Audience, string Scopes, string Roles, string Issuer);