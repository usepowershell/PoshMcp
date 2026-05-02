using System.Collections.Generic;

namespace PoshMcp.Server.Authentication;

public class AuthenticationConfiguration
{
    public bool Enabled { get; set; } = false;
    public string DefaultScheme { get; set; } = "Bearer";
    public AuthorizationPolicyConfiguration DefaultPolicy { get; set; } = new();
    public Dictionary<string, AuthSchemeConfiguration> Schemes { get; set; } = new();
    public ProtectedResourceConfiguration? ProtectedResource { get; set; }
    public CorsConfiguration? Cors { get; set; }
    public OAuthProxyConfiguration? OAuthProxy { get; set; }
}

public class AuthorizationPolicyConfiguration
{
    public bool RequireAuthentication { get; set; } = true;
    public List<string> RequiredScopes { get; set; } = new();
    public List<string> RequiredRoles { get; set; } = new();
}

public class AuthSchemeConfiguration
{
    public string Type { get; set; } = "JwtBearer"; // "JwtBearer" | "ApiKey"
    // JWT Bearer fields
    public string? Authority { get; set; }
    public string? Audience { get; set; }
    public List<string> ValidIssuers { get; set; } = new();
    public bool RequireHttpsMetadata { get; set; } = true;
    public ClaimsMappingConfiguration ClaimsMapping { get; set; } = new();
    // API Key fields
    public string HeaderName { get; set; } = "X-API-Key";
    public Dictionary<string, ApiKeyDefinition> Keys { get; set; } = new();
}

public class ClaimsMappingConfiguration
{
    public string ScopeClaim { get; set; } = "scp";
    public string RoleClaim { get; set; } = "roles";
}

public class ApiKeyDefinition
{
    public List<string> Scopes { get; set; } = new();
    public List<string> Roles { get; set; } = new();
}

public class ProtectedResourceConfiguration
{
    public string? Resource { get; set; }
    public string? ResourceName { get; set; }
    public List<string> AuthorizationServers { get; set; } = new();
    public List<string> ScopesSupported { get; set; } = new();
    public List<string> BearerMethodsSupported { get; set; } = new() { "header" };
}

public class CorsConfiguration
{
    public List<string> AllowedOrigins { get; set; } = new();
    public bool AllowCredentials { get; set; } = false;
}

/// <summary>
/// Configuration for the OAuth proxy that enables MCP clients to discover
/// Entra ID authorization endpoints without requiring manual client_id entry.
/// When enabled, PoshMcp exposes:
///   /.well-known/oauth-authorization-server  — AS metadata pointing to Entra
///   /register                                — DCR proxy returning the configured ClientId
/// </summary>
public class OAuthProxyConfiguration
{
    public bool Enabled { get; set; } = false;

    /// <summary>Azure AD / Entra ID tenant identifier (GUID or name).</summary>
    public string TenantId { get; set; } = "";

    /// <summary>
    /// Client ID returned by the DCR proxy (/register) so MCP clients
    /// don't have to ask the user for one.
    /// </summary>
    public string ClientId { get; set; } = "";

    /// <summary>
    /// Application ID URI used as the audience in token validation
    /// (e.g., "api://poshmcp-prod").  Exposed in the AS metadata scopes.
    /// </summary>
    public string Audience { get; set; } = "";
}
