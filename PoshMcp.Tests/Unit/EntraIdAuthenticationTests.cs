using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoshMcp.Web.Authentication;
using System;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Unit tests for Entra ID authentication configuration
/// </summary>
public class EntraIdAuthenticationTests
{
    private readonly ITestOutputHelper _output;

    public EntraIdAuthenticationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void EntraIdConfiguration_DefaultsCorrectly()
    {
        // Arrange & Act
        var config = new EntraIdConfiguration();

        // Assert
        Assert.False(config.Enabled);
        Assert.True(config.RequireHttpsMetadata);
        Assert.True(config.ValidateIssuer);
        Assert.True(config.ValidateAudience);
        Assert.True(config.ValidateLifetime);
        Assert.Empty(config.RequiredScopes);
    }

    [Fact]
    public void EntraIdConfiguration_GetAuthority_ReturnsAuthorityWhenSet()
    {
        // Arrange
        var config = new EntraIdConfiguration
        {
            Authority = "https://custom.authority.com/"
        };

        // Act
        var authority = config.GetAuthority();

        // Assert
        Assert.Equal("https://custom.authority.com/", authority);
    }

    [Fact]
    public void EntraIdConfiguration_GetAuthority_ConstructsFromTenantId()
    {
        // Arrange
        var config = new EntraIdConfiguration
        {
            TenantId = "12345678-1234-1234-1234-123456789012"
        };

        // Act
        var authority = config.GetAuthority();

        // Assert
        Assert.Equal("https://login.microsoftonline.com/12345678-1234-1234-1234-123456789012/v2.0", authority);
    }

    [Fact]
    public void EntraIdConfiguration_GetAuthority_ThrowsWhenNeitherSet()
    {
        // Arrange
        var config = new EntraIdConfiguration();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => config.GetAuthority());
        Assert.Contains("Either Authority or TenantId must be specified", exception.Message);
    }

    [Fact]
    public void EntraIdConfiguration_GetAudience_ReturnsAudienceWhenSet()
    {
        // Arrange
        var config = new EntraIdConfiguration
        {
            Audience = "api://custom-audience"
        };

        // Act
        var audience = config.GetAudience();

        // Assert
        Assert.Equal("api://custom-audience", audience);
    }

    [Fact]
    public void EntraIdConfiguration_GetAudience_ReturnsClientIdWhenAudienceNotSet()
    {
        // Arrange
        var config = new EntraIdConfiguration
        {
            ClientId = "12345678-1234-1234-1234-123456789012"
        };

        // Act
        var audience = config.GetAudience();

        // Assert
        Assert.Equal("12345678-1234-1234-1234-123456789012", audience);
    }

    [Fact]
    public void EntraIdConfiguration_GetAudience_ThrowsWhenNeitherSet()
    {
        // Arrange
        var config = new EntraIdConfiguration();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => config.GetAudience());
        Assert.Contains("Either Audience or ClientId must be specified", exception.Message);
    }

    [Fact]
    public void AddEntraIdAuthentication_DisabledByDefault_DoesNotRegisterAuthentication()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EntraId:Enabled"] = "false"
            })
            .Build();

        // Act
        services.AddEntraIdAuthentication(configuration);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        // When authentication is disabled, the authentication services should not be registered
        var authService = serviceProvider.GetService<Microsoft.AspNetCore.Authentication.IAuthenticationService>();
        Assert.Null(authService);
    }

    [Fact]
    public void AddEntraIdAuthentication_ThrowsWhenEnabledButMissingConfig()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EntraId:Enabled"] = "true"
                // Missing TenantId/Authority and ClientId/Audience
            })
            .Build();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => services.AddEntraIdAuthentication(configuration));
        Assert.Contains("TenantId or Authority must be specified", exception.Message);
    }

    [Fact]
    public void AddEntraIdAuthentication_ConfiguresCorrectlyWhenEnabled()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(); // Required for authentication services

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EntraId:Enabled"] = "true",
                ["EntraId:TenantId"] = "12345678-1234-1234-1234-123456789012",
                ["EntraId:ClientId"] = "87654321-4321-4321-4321-210987654321",
                ["EntraId:RequireHttpsMetadata"] = "true",
                ["EntraId:ValidateIssuer"] = "true",
                ["EntraId:ValidateAudience"] = "true",
                ["EntraId:ValidateLifetime"] = "true"
            })
            .Build();

        // Act
        services.AddEntraIdAuthentication(configuration);

        // Assert - should not throw and services should be registered
        var serviceProvider = services.BuildServiceProvider();
        var entraIdConfig = serviceProvider.GetService<Microsoft.Extensions.Options.IOptions<EntraIdConfiguration>>();
        Assert.NotNull(entraIdConfig);
        Assert.True(entraIdConfig.Value.Enabled);
        Assert.Equal("12345678-1234-1234-1234-123456789012", entraIdConfig.Value.TenantId);
        Assert.Equal("87654321-4321-4321-4321-210987654321", entraIdConfig.Value.ClientId);
    }
}