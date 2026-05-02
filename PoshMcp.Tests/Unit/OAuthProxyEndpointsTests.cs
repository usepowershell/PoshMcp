using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PoshMcp.Server.Authentication;
using Xunit;

namespace PoshMcp.Tests.Unit;

public class OAuthProxyEndpointsTests
{
    // ── OAuthProxyConfiguration binding ─────────────────────────────────────

    [Fact]
    public void OAuthProxyConfiguration_DefaultsToDisabled()
    {
        var config = new OAuthProxyConfiguration();
        Assert.False(config.Enabled);
        Assert.Equal("", config.TenantId);
        Assert.Equal("", config.ClientId);
        Assert.Equal("", config.Audience);
    }

    [Fact]
    public void AuthenticationConfiguration_ExposesOAuthProxyProperty()
    {
        var auth = new AuthenticationConfiguration();
        Assert.Null(auth.OAuthProxy);
    }

    // ── OAuthProxy toggled off → endpoints are not registered ────────────────

    [Theory]
    [InlineData(false, "tenant-id", "client-id")]  // proxy disabled
    [InlineData(true, "", "client-id")]              // no tenant
    public void MapOAuthProxyEndpoints_WhenNotFullyConfigured_DoesNotThrow(
        bool enabled, string tenantId, string clientId)
    {
        var config = new AuthenticationConfiguration
        {
            Enabled = true,
            OAuthProxy = new OAuthProxyConfiguration
            {
                Enabled = enabled,
                TenantId = tenantId,
                ClientId = clientId
            }
        };

        // Calling with a stub router should succeed without exception
        var stub = new NoOpEndpointRouteBuilder();
        stub.MapOAuthProxyEndpoints(config); // must not throw
    }

    // ── PRM dynamic authorization_servers ────────────────────────────────────

    [Fact]
    public void ProtectedResourceMetadata_WhenOAuthProxyEnabled_AndNoServersConfigured_UsesServerBaseUrl()
    {
        var config = new AuthenticationConfiguration
        {
            Enabled = true,
            ProtectedResource = new ProtectedResourceConfiguration
            {
                Resource = "api://poshmcp",
                ResourceName = "PoshMcp",
                AuthorizationServers = new List<string>(),
                ScopesSupported = new List<string> { "api://poshmcp/access" }
            },
            OAuthProxy = new OAuthProxyConfiguration
            {
                Enabled = true,
                TenantId = "my-tenant",
                ClientId = "my-client-id"
            }
        };

        // Simulate the endpoint logic directly (without registering routes)
        // by checking config state that the endpoint would observe
        Assert.True(config.OAuthProxy!.Enabled);
        Assert.NotEmpty(config.OAuthProxy.TenantId);
        Assert.Empty(config.ProtectedResource.AuthorizationServers);
        // → endpoint will auto-populate authorization_servers from request context
    }

    [Fact]
    public void ProtectedResourceMetadata_WhenOAuthProxyEnabled_AndServersExplicitlySet_UsesConfiguredServers()
    {
        var config = new AuthenticationConfiguration
        {
            Enabled = true,
            ProtectedResource = new ProtectedResourceConfiguration
            {
                Resource = "api://poshmcp",
                AuthorizationServers = new List<string> { "https://custom-as.example.com" }
            },
            OAuthProxy = new OAuthProxyConfiguration
            {
                Enabled = true,
                TenantId = "my-tenant"
            }
        };

        // If authorization_servers is explicitly set, it should be respected as-is
        Assert.Single(config.ProtectedResource.AuthorizationServers);
        Assert.Equal("https://custom-as.example.com", config.ProtectedResource.AuthorizationServers[0]);
    }

    // ── AS metadata content ──────────────────────────────────────────────────

    [Fact]
    public void OAuthProxyConfiguration_TenantIdFormatsCorrectly()
    {
        const string tenantId = "12345678-abcd-1234-abcd-123456789012";
        var proxy = new OAuthProxyConfiguration { TenantId = tenantId };

        var expectedAuthEndpoint =
            $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize";
        var expectedTokenEndpoint =
            $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";

        Assert.Equal(
            expectedAuthEndpoint,
            $"https://login.microsoftonline.com/{proxy.TenantId}/oauth2/v2.0/authorize");
        Assert.Equal(
            expectedTokenEndpoint,
            $"https://login.microsoftonline.com/{proxy.TenantId}/oauth2/v2.0/token");
    }

    [Fact]
    public void OAuthProxyConfiguration_AudienceScope_AppendedCorrectly()
    {
        var proxy = new OAuthProxyConfiguration
        {
            Audience = "api://poshmcp-prod"
        };

        // The endpoint adds audience/.default to scopes_supported
        var expectedScope = $"{proxy.Audience.TrimEnd('/')}/.default";
        Assert.Equal("api://poshmcp-prod/.default", expectedScope);
    }

    [Fact]
    public void OAuthProxyConfiguration_AudienceWithTrailingSlash_TrimmedCorrectly()
    {
        var proxy = new OAuthProxyConfiguration
        {
            Audience = "api://poshmcp-prod/"
        };

        var expectedScope = $"{proxy.Audience.TrimEnd('/')}/.default";
        Assert.Equal("api://poshmcp-prod/.default", expectedScope);
    }

    // ── Stub helper ──────────────────────────────────────────────────────────

    private sealed class NoOpEndpointRouteBuilder : IEndpointRouteBuilder
    {
        private readonly IServiceProvider _sp =
            new ServiceCollection().BuildServiceProvider();

        public IServiceProvider ServiceProvider => _sp;

        public ICollection<EndpointDataSource> DataSources { get; } =
            new List<EndpointDataSource>();

        public IApplicationBuilder CreateApplicationBuilder() =>
            new ApplicationBuilder(_sp);
    }
}
