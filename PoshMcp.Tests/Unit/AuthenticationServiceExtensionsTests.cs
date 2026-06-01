using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PoshMcp.Server.Authentication;
using Xunit;

namespace PoshMcp.Tests.Unit;

[Trait("Category", "Unit")]
public class AuthenticationServiceExtensionsTests
{
    [Fact]
    public void WhenAuthEnabled_IOptionsAuthenticationConfiguration_ReflectsConfig()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Enabled"] = "true",
                ["Authentication:DefaultScheme"] = "Bearer",
                ["Authentication:Schemes:Bearer:Type"] = "JwtBearer",
                ["Authentication:Schemes:Bearer:Authority"] = "https://login.microsoftonline.com/tenant",
                ["Authentication:Schemes:Bearer:Audience"] = "api://my-app",
                ["Authentication:Schemes:ApiKey:Type"] = "ApiKey",
                ["Authentication:Schemes:ApiKey:Keys:test-key:Scopes:0"] = "mcp:read",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddPoshMcpAuthentication(config);

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<AuthenticationConfiguration>>();

        Assert.True(options.Value.Enabled);
        Assert.Equal("Bearer", options.Value.DefaultScheme);
        Assert.Equal(2, options.Value.Schemes.Count);
    }

    [Fact]
    public void WhenAuthDisabled_IOptionsAuthenticationConfiguration_IsRegisteredWithEnabledFalse()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Enabled"] = "false",
                ["Authentication:DefaultScheme"] = "Bearer",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddPoshMcpAuthentication(config);

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<AuthenticationConfiguration>>();

        Assert.False(options.Value.Enabled);
        Assert.Equal("Bearer", options.Value.DefaultScheme);
    }

    [Fact]
    public void WhenValidAudiencesConfigured_IOptionsAuthenticationConfiguration_ReflectsAll()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Enabled"] = "true",
                ["Authentication:DefaultScheme"] = "Bearer",
                ["Authentication:Schemes:Bearer:Type"] = "JwtBearer",
                ["Authentication:Schemes:Bearer:Authority"] = "https://login.microsoftonline.com/tenant/v2.0",
                ["Authentication:Schemes:Bearer:Audience"] = "api://my-app",
                ["Authentication:Schemes:Bearer:ValidAudiences:0"] = "api://my-app",
                ["Authentication:Schemes:Bearer:ValidAudiences:1"] = "80939099-d811-4488-8333-83eb0409ed53",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddPoshMcpAuthentication(config);

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<AuthenticationConfiguration>>();

        Assert.True(options.Value.Enabled);
        Assert.Equal(2, options.Value.Schemes["Bearer"].ValidAudiences.Count);
        Assert.Contains("api://my-app", options.Value.Schemes["Bearer"].ValidAudiences);
        Assert.Contains("80939099-d811-4488-8333-83eb0409ed53", options.Value.Schemes["Bearer"].ValidAudiences);
    }

    [Fact]
    public void WhenNoAuthSection_IOptionsAuthenticationConfiguration_DoesNotThrow()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddPoshMcpAuthentication(config);

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<AuthenticationConfiguration>>();

        Assert.False(options.Value.Enabled);
    }

    [Fact]
    public void WhenNameClaimNotConfigured_JwtBearerNameClaimType_PreservesDefault()
    {
        // Backwards compatibility: if NameClaim is absent from config, do NOT mutate
        // the JwtBearer default ("name"). Existing deployments must keep the same
        // Identity.Name resolution behavior.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Enabled"] = "true",
                ["Authentication:DefaultScheme"] = "Bearer",
                ["Authentication:Schemes:Bearer:Type"] = "JwtBearer",
                ["Authentication:Schemes:Bearer:Authority"] = "https://login.microsoftonline.com/tenant/v2.0",
                ["Authentication:Schemes:Bearer:Audience"] = "api://my-app",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddPoshMcpAuthentication(config);

        var sp = services.BuildServiceProvider();
        var jwtOptions = sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("Bearer");

        // JwtBearer's stock default is the SOAP-style name URI; make sure we left it alone.
        const string defaultNameClaim = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name";
        Assert.Equal(defaultNameClaim, jwtOptions.TokenValidationParameters.NameClaimType);
        Assert.Null(sp.GetRequiredService<IOptions<AuthenticationConfiguration>>().Value
            .Schemes["Bearer"].ClaimsMapping.NameClaim);
    }

    [Fact]
    public void WhenNameClaimConfigured_JwtBearerNameClaimType_IsOverridden()
    {
        // AAD v2.0 access tokens carry "preferred_username" instead of "name".
        // Verify operators can wire that through ClaimsMapping.NameClaim so
        // Identity.Name (and therefore the doctor report) resolves correctly.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Enabled"] = "true",
                ["Authentication:DefaultScheme"] = "Bearer",
                ["Authentication:Schemes:Bearer:Type"] = "JwtBearer",
                ["Authentication:Schemes:Bearer:Authority"] = "https://login.microsoftonline.com/tenant/v2.0",
                ["Authentication:Schemes:Bearer:Audience"] = "api://my-app",
                ["Authentication:Schemes:Bearer:ClaimsMapping:NameClaim"] = "preferred_username",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddPoshMcpAuthentication(config);

        var sp = services.BuildServiceProvider();
        var jwtOptions = sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("Bearer");

        Assert.Equal("preferred_username", jwtOptions.TokenValidationParameters.NameClaimType);
        Assert.Equal("preferred_username",
            sp.GetRequiredService<IOptions<AuthenticationConfiguration>>().Value
                .Schemes["Bearer"].ClaimsMapping.NameClaim);
    }

    [Fact]
    public void SafeAuthClaimSummary_IncludesOnlyAllowedClaimValues()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("aud", "api://my-app"),
            new Claim("scp", "mcp:read"),
            new Claim("scope", "mcp:write"),
            new Claim("roles", "operator"),
            new Claim("role", "auditor"),
            new Claim("iss", "https://login.microsoftonline.com/tenant/v2.0"),
            new Claim("preferred_username", "person@example.test"),
            new Claim("oid", "00000000-0000-0000-0000-000000000000"),
        }, "Bearer"));

        var summary = AuthClaimDiagnostics.BuildSafeSummary(principal);
        var serialized = string.Join("|", summary.Audience, summary.Scopes, summary.Roles, summary.Issuer);

        Assert.Equal("api://my-app", summary.Audience);
        Assert.Equal("mcp:read,mcp:write", summary.Scopes);
        Assert.Equal("operator,auditor", summary.Roles);
        Assert.Equal("https://login.microsoftonline.com/tenant/v2.0", summary.Issuer);
        Assert.DoesNotContain("preferred_username", serialized);
        Assert.DoesNotContain("person@example.test", serialized);
        Assert.DoesNotContain("oid", serialized);
        Assert.DoesNotContain("00000000-0000-0000-0000-000000000000", serialized);
    }

    [Fact]
    public void SafeAuthClaimSummary_ExcludesAuthLikeButUnsupportedClaimTypes()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("aud", "api://my-app"),
            new Claim("scp", "mcp:read"),
            new Claim("roles", "operator"),
            new Claim("iss", "https://login.microsoftonline.com/tenant/v2.0"),
            new Claim("audience", "api://leaked-audience"),
            new Claim("scp_extra", "leaked-scope"),
            new Claim("roles_extra", "leaked-role"),
            new Claim("issuer", "https://leaked-issuer.example.test"),
        }, "Bearer"));

        var summary = AuthClaimDiagnostics.BuildSafeSummary(principal);
        var serialized = string.Join("|", summary.Audience, summary.Scopes, summary.Roles, summary.Issuer);

        Assert.Equal("api://my-app", summary.Audience);
        Assert.Equal("mcp:read", summary.Scopes);
        Assert.Equal("operator", summary.Roles);
        Assert.Equal("https://login.microsoftonline.com/tenant/v2.0", summary.Issuer);
        Assert.DoesNotContain("api://leaked-audience", serialized);
        Assert.DoesNotContain("leaked-scope", serialized);
        Assert.DoesNotContain("leaked-role", serialized);
        Assert.DoesNotContain("https://leaked-issuer.example.test", serialized);
    }

    [Fact]
    public void SafeAuthClaimSummary_WhenPrincipalMissing_ReturnsEmptyFields()
    {
        var summary = AuthClaimDiagnostics.BuildSafeSummary(null);

        Assert.Equal(string.Empty, summary.Audience);
        Assert.Equal(string.Empty, summary.Scopes);
        Assert.Equal(string.Empty, summary.Roles);
        Assert.Equal(string.Empty, summary.Issuer);
    }
}
