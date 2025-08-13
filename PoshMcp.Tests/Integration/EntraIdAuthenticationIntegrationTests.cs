using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoshMcp.Web.Authentication;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Integration;

/// <summary>
/// Integration tests for Entra ID authentication scenarios
/// </summary>
public class EntraIdAuthenticationIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public EntraIdAuthenticationIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void WebApplicationBuilder_ConfiguresEntraIdAuthentication_WhenDisabled()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();

        // Configure to disable authentication
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EntraId:Enabled"] = "false"
        });

        // Act
        builder.Services.AddEntraIdAuthentication(builder.Configuration);
        var app = builder.Build();

        // Assert
        var entraIdConfig = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<EntraIdConfiguration>>();
        Assert.False(entraIdConfig.Value.Enabled);
        _output.WriteLine("Authentication correctly configured as disabled");
    }

    [Fact]
    public void WebApplicationBuilder_ConfiguresEntraIdAuthentication_WhenEnabled()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();

        // Configure to enable authentication with required settings
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EntraId:Enabled"] = "true",
            ["EntraId:TenantId"] = "12345678-1234-1234-1234-123456789012",
            ["EntraId:ClientId"] = "87654321-4321-4321-4321-210987654321",
            ["EntraId:RequireHttpsMetadata"] = "false" // For testing
        });

        // Act
        builder.Services.AddEntraIdAuthentication(builder.Configuration);
        var app = builder.Build();

        // Assert
        var entraIdConfig = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<EntraIdConfiguration>>();
        Assert.True(entraIdConfig.Value.Enabled);
        Assert.Equal("12345678-1234-1234-1234-123456789012", entraIdConfig.Value.TenantId);
        Assert.Equal("87654321-4321-4321-4321-210987654321", entraIdConfig.Value.ClientId);
        _output.WriteLine("Authentication correctly configured as enabled");
    }

    [Fact]
    public void McpAuthorizationExtensions_AppliesConditionalAuth_WhenDisabled()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EntraId:Enabled"] = "false"
        });

        builder.Services.AddEntraIdAuthentication(builder.Configuration);

        // Add minimal MCP server services for the extension to work
        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools(new List<ModelContextProtocol.Server.McpServerTool>());

        var app = builder.Build();

        // Act & Assert - should not throw
        app.MapMcpWithConditionalAuth();
        _output.WriteLine("MCP endpoints configured without authentication requirements");
    }

    [Fact]
    public void McpAuthorizationExtensions_AppliesConditionalAuth_WhenEnabled()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EntraId:Enabled"] = "true",
            ["EntraId:TenantId"] = "12345678-1234-1234-1234-123456789012",
            ["EntraId:ClientId"] = "87654321-4321-4321-4321-210987654321",
            ["EntraId:RequireHttpsMetadata"] = "false"
        });

        builder.Services.AddEntraIdAuthentication(builder.Configuration);

        // Add minimal MCP server services for the extension to work
        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools(new List<ModelContextProtocol.Server.McpServerTool>());

        var app = builder.Build();

        // Act & Assert - should not throw
        app.MapMcpWithConditionalAuth();
        _output.WriteLine("MCP endpoints configured with authentication requirements");
    }

    [Fact]
    public void EntraIdConfiguration_LoadsFromAppSettings()
    {
        // Arrange
        var tempConfigFile = Path.GetTempFileName();
        var configJson = @"{
  ""EntraId"": {
    ""Enabled"": true,
    ""TenantId"": ""test-tenant-id"",
    ""ClientId"": ""test-client-id"",
    ""RequireHttpsMetadata"": false,
    ""ValidateIssuer"": false,
    ""ValidateAudience"": false,
    ""ValidateLifetime"": false,
    ""RequiredScopes"": [""api.read"", ""api.write""]
  }
}";

        try
        {
            File.WriteAllText(tempConfigFile, configJson);

            var configuration = new ConfigurationBuilder()
                .AddJsonFile(tempConfigFile)
                .Build();

            var entraIdConfig = new EntraIdConfiguration();
            configuration.GetSection("EntraId").Bind(entraIdConfig);

            // Act & Assert
            Assert.True(entraIdConfig.Enabled);
            Assert.Equal("test-tenant-id", entraIdConfig.TenantId);
            Assert.Equal("test-client-id", entraIdConfig.ClientId);
            Assert.False(entraIdConfig.RequireHttpsMetadata);
            Assert.False(entraIdConfig.ValidateIssuer);
            Assert.False(entraIdConfig.ValidateAudience);
            Assert.False(entraIdConfig.ValidateLifetime);
            Assert.Equal(2, entraIdConfig.RequiredScopes.Count);
            Assert.Contains("api.read", entraIdConfig.RequiredScopes);
            Assert.Contains("api.write", entraIdConfig.RequiredScopes);

            _output.WriteLine($"Configuration loaded successfully: Authority={entraIdConfig.GetAuthority()}, Audience={entraIdConfig.GetAudience()}");
        }
        finally
        {
            if (File.Exists(tempConfigFile))
            {
                File.Delete(tempConfigFile);
            }
        }
    }
}