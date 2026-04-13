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
