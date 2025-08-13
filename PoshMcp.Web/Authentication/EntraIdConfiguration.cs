using System;
using System.Collections.Generic;

namespace PoshMcp.Web.Authentication;

/// <summary>
/// Configuration options for Entra ID (Azure AD) authentication
/// </summary>
public class EntraIdConfiguration
{
    /// <summary>
    /// Whether authentication is enabled. When false, endpoints are accessible without authentication.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// The Azure AD tenant ID
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// The Azure AD application client ID
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// The audience for token validation. If not specified, defaults to ClientId.
    /// </summary>
    public string? Audience { get; set; }

    /// <summary>
    /// The authority URL for the Azure AD tenant. If not specified, will be constructed from TenantId.
    /// </summary>
    public string? Authority { get; set; }

    /// <summary>
    /// Whether to require HTTPS for metadata discovery. Default is true.
    /// Set to false only for development scenarios.
    /// </summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// Additional scopes required for access
    /// </summary>
    public List<string> RequiredScopes { get; set; } = new();

    /// <summary>
    /// Whether to validate the issuer. Default is true.
    /// </summary>
    public bool ValidateIssuer { get; set; } = true;

    /// <summary>
    /// Whether to validate the audience. Default is true.
    /// </summary>
    public bool ValidateAudience { get; set; } = true;

    /// <summary>
    /// Whether to validate the token lifetime. Default is true.
    /// </summary>
    public bool ValidateLifetime { get; set; } = true;

    /// <summary>
    /// Gets the authority URL, constructing it from TenantId if not explicitly set
    /// </summary>
    public string GetAuthority()
    {
        if (!string.IsNullOrEmpty(Authority))
        {
            return Authority;
        }

        if (!string.IsNullOrEmpty(TenantId))
        {
            return $"https://login.microsoftonline.com/{TenantId}/v2.0";
        }

        throw new InvalidOperationException("Either Authority or TenantId must be specified for Entra ID authentication");
    }

    /// <summary>
    /// Gets the audience for token validation, defaulting to ClientId if not explicitly set
    /// </summary>
    public string GetAudience()
    {
        if (!string.IsNullOrEmpty(Audience))
        {
            return Audience;
        }

        if (!string.IsNullOrEmpty(ClientId))
        {
            return ClientId;
        }

        throw new InvalidOperationException("Either Audience or ClientId must be specified for Entra ID authentication");
    }
}