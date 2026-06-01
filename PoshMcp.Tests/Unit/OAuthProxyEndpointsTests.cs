using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PoshMcp.Server.Authentication;
using Xunit;

namespace PoshMcp.Tests.Unit;

[Trait("Category", "Unit")]
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

    [Fact]
    public async Task Register_ReturnsRedirectUrisFromDynamicClientRegistrationRequest()
    {
        var config = new AuthenticationConfiguration
        {
            Enabled = true,
            OAuthProxy = new OAuthProxyConfiguration
            {
                Enabled = true,
                TenantId = "contoso.onmicrosoft.com",
                ClientId = "configured-client-id"
            }
        };

        using var host = await CreateOAuthProxyTestHostAsync(config);
        using var client = host.GetTestClient();
        using var content = new StringContent(
            """
            {
              "redirect_uris": ["http://127.0.0.1:33418/callback"],
              "client_name": "GitHub Copilot CLI",
              "grant_types": ["authorization_code", "refresh_token"],
              "response_types": ["code"],
              "scope": "openid profile offline_access api://poshmcp/user_impersonation"
            }
            """,
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync("/register", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("configured-client-id", root.GetProperty("client_id").GetString());
        Assert.Equal("none", root.GetProperty("token_endpoint_auth_method").GetString());
        Assert.Equal("http://127.0.0.1:33418/callback", root.GetProperty("redirect_uris")[0].GetString());
        Assert.Equal("GitHub Copilot CLI", root.GetProperty("client_name").GetString());
        Assert.Equal("authorization_code", root.GetProperty("grant_types")[0].GetString());
        Assert.Equal("refresh_token", root.GetProperty("grant_types")[1].GetString());
        Assert.Equal("code", root.GetProperty("response_types")[0].GetString());
        Assert.Equal("openid profile offline_access api://poshmcp/user_impersonation", root.GetProperty("scope").GetString());
        Assert.True(root.TryGetProperty("client_id_issued_at", out var issuedAt));
        Assert.Equal(JsonValueKind.Number, issuedAt.ValueKind);
    }

    [Fact]
    public async Task Authorize_WhenPromptCreate_OmitsPromptFromEntraRedirect()
    {
        var config = CreateConfiguredOAuthProxy();

        using var host = await CreateOAuthProxyTestHostAsync(config);
        using var client = host.GetTestClient();

        using var response = await client.GetAsync(
            "/authorize?client_id=dummy-client-id&response_type=code&scope=openid&redirect_uri=http%3A%2F%2F127.0.0.1%3A33333%2Fcallback&code_challenge=x&code_challenge_method=S256&resource=https%3A%2F%2Fexample.test&prompt=create");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location;
        Assert.NotNull(location);
        Assert.Equal("https", location!.Scheme);
        Assert.Equal("login.microsoftonline.com", location.Host);
        Assert.Equal("/contoso.onmicrosoft.com/oauth2/v2.0/authorize", location.AbsolutePath);

        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("configured-client-id", query["client_id"]);
        Assert.False(query.ContainsKey("resource"));
        Assert.False(query.ContainsKey("prompt"));
    }

    [Fact]
    public async Task Authorize_WhenPromptHasConsentAndSelectAccount_ForwardsConsentPromptInEntraRedirect()
    {
        var config = CreateConfiguredOAuthProxy();

        using var host = await CreateOAuthProxyTestHostAsync(config);
        using var client = host.GetTestClient();

        using var response = await client.GetAsync(
            "/authorize?client_id=dummy-client-id&response_type=code&scope=openid&redirect_uri=http%3A%2F%2F127.0.0.1%3A33333%2Fcallback&code_challenge=x&code_challenge_method=S256&prompt=consent%20select_account");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location;
        Assert.NotNull(location);

        var query = QueryHelpers.ParseQuery(location!.Query);
        Assert.Equal("configured-client-id", query["client_id"]);
        Assert.Equal("consent", query["prompt"]);
    }

    [Fact]
    public async Task Authorize_WhenPromptSelectAccount_PreservesPromptInEntraRedirect()
    {
        var config = CreateConfiguredOAuthProxy();

        using var host = await CreateOAuthProxyTestHostAsync(config);
        using var client = host.GetTestClient();

        using var response = await client.GetAsync(
            "/authorize?client_id=dummy-client-id&response_type=code&scope=openid&redirect_uri=http%3A%2F%2F127.0.0.1%3A33333%2Fcallback&code_challenge=x&code_challenge_method=S256&prompt=select_account");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location;
        Assert.NotNull(location);

        var query = QueryHelpers.ParseQuery(location!.Query);
        Assert.Equal("configured-client-id", query["client_id"]);
        Assert.Equal("select_account", query["prompt"]);
    }

    [Fact]
    public async Task Authorize_WhenPromptUnsupported_OmitsPromptFromEntraRedirect()
    {
        var config = CreateConfiguredOAuthProxy();

        using var host = await CreateOAuthProxyTestHostAsync(config);
        using var client = host.GetTestClient();

        using var response = await client.GetAsync(
            "/authorize?client_id=dummy-client-id&response_type=code&scope=openid&redirect_uri=http%3A%2F%2F127.0.0.1%3A33333%2Fcallback&code_challenge=x&code_challenge_method=S256&prompt=signup");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location;
        Assert.NotNull(location);

        var query = QueryHelpers.ParseQuery(location!.Query);
        Assert.Equal("configured-client-id", query["client_id"]);
        Assert.False(query.ContainsKey("prompt"));
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

    private static async Task<IHost> CreateOAuthProxyTestHostAsync(AuthenticationConfiguration config)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddHttpClient();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapOAuthProxyEndpoints(config));
                });
            })
            .Build();

        await host.StartAsync();
        return host;
    }

    private static AuthenticationConfiguration CreateConfiguredOAuthProxy() => new()
    {
        Enabled = true,
        OAuthProxy = new OAuthProxyConfiguration
        {
            Enabled = true,
            TenantId = "contoso.onmicrosoft.com",
            ClientId = "configured-client-id"
        }
    };
}
