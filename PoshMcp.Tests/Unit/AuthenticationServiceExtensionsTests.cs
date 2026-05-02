using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PoshMcp.Server.Authentication;
using Xunit;

namespace PoshMcp.Tests.Unit;

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
}
