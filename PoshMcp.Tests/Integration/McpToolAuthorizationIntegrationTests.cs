using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PoshMcp.Server.PowerShell;
using PoshMcp.Web.Authentication;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Integration;

/// <summary>
/// Integration tests for per-command authorization middleware
/// </summary>
public class McpToolAuthorizationIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public McpToolAuthorizationIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ToolCall_WithNoAuthRequirement_ShouldAllow()
    {
        // Arrange
        var config = CreateTestConfiguration();
        using var host = CreateTestHost(config);
        var client = host.GetTestClient();

        var toolCallRequest = CreateToolCallRequest("get_process_name");

        // Act
        var response = await client.PostAsync("/tools/call", 
            new StringContent(toolCallRequest, Encoding.UTF8, "application/json"));

        // Assert - Should not be forbidden (tool doesn't exist but authorization should pass)
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        _output.WriteLine($"Response status: {response.StatusCode}");
    }

    [Fact]
    public async Task ToolCall_WithRoleRequirement_WhenUserHasRole_ShouldAllow()
    {
        // Arrange
        var config = CreateTestConfiguration();
        using var host = CreateTestHost(config, claims: new[]
        {
            new Claim(ClaimTypes.Role, "Global Administrator"),
            new Claim(ClaimTypes.Name, "Test User")
        });
        var client = host.GetTestClient();

        var toolCallRequest = CreateToolCallRequest("update_tenant_user");

        // Act
        var response = await client.PostAsync("/tools/call",
            new StringContent(toolCallRequest, Encoding.UTF8, "application/json"));

        // Assert - Should not be forbidden
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        _output.WriteLine($"Response status for role-based tool: {response.StatusCode}");
    }

    [Fact]
    public async Task ToolCall_WithRoleRequirement_WhenUserLacksRole_ShouldDeny()
    {
        // Arrange
        var config = CreateTestConfiguration();
        using var host = CreateTestHost(config, claims: new[]
        {
            new Claim(ClaimTypes.Role, "User"),
            new Claim(ClaimTypes.Name, "Test User")
        });
        var client = host.GetTestClient();

        var toolCallRequest = CreateToolCallRequest("update_tenant_user");

        // Act
        var response = await client.PostAsync("/tools/call",
            new StringContent(toolCallRequest, Encoding.UTF8, "application/json"));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"Forbidden response: {responseContent}");
        
        Assert.Contains("Global Administrator", responseContent);
    }

    [Fact]
    public async Task ToolCall_WithPermissionRequirement_WhenUserHasPermission_ShouldAllow()
    {
        // Arrange
        var config = CreateTestConfiguration();
        using var host = CreateTestHost(config, claims: new[]
        {
            new Claim("scp", "poshmcp.read"),
            new Claim(ClaimTypes.Name, "Test User")
        });
        var client = host.GetTestClient();

        var toolCallRequest = CreateToolCallRequest("get_tenant_user_role");

        // Act
        var response = await client.PostAsync("/tools/call",
            new StringContent(toolCallRequest, Encoding.UTF8, "application/json"));

        // Assert - Should not be forbidden
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        _output.WriteLine($"Response status for permission-based tool: {response.StatusCode}");
    }

    [Fact]
    public async Task ToolCall_WithPermissionRequirement_WhenUserLacksPermission_ShouldDeny()
    {
        // Arrange
        var config = CreateTestConfiguration();
        using var host = CreateTestHost(config, claims: new[]
        {
            new Claim("scp", "other.permission"),
            new Claim(ClaimTypes.Name, "Test User")
        });
        var client = host.GetTestClient();

        var toolCallRequest = CreateToolCallRequest("get_tenant_user_role");

        // Act
        var response = await client.PostAsync("/tools/call",
            new StringContent(toolCallRequest, Encoding.UTF8, "application/json"));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"Forbidden response: {responseContent}");
        
        Assert.Contains("poshmcp.read", responseContent);
    }

    [Fact]
    public async Task ToolCall_WithAuthRequirement_WhenNotAuthenticated_ShouldDeny()
    {
        // Arrange
        var config = CreateTestConfiguration();
        using var host = CreateTestHost(config, authenticated: false);
        var client = host.GetTestClient();

        var toolCallRequest = CreateToolCallRequest("update_tenant_user");

        // Act
        var response = await client.PostAsync("/tools/call",
            new StringContent(toolCallRequest, Encoding.UTF8, "application/json"));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"Unauthenticated response: {responseContent}");
        
        Assert.Contains("Authentication required", responseContent);
    }

    [Fact]
    public async Task NonToolCall_Request_ShouldPassThrough()
    {
        // Arrange
        var config = CreateTestConfiguration();
        using var host = CreateTestHost(config);
        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert - Should pass through to next middleware (even if it 404s)
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        _output.WriteLine($"Non-tool call response status: {response.StatusCode}");
    }

    private static PowerShellConfiguration CreateTestConfiguration()
    {
        return new PowerShellConfiguration
        {
            FunctionNames = new List<string> { "Get-Process" },
            Commands = new AuthenticationAwareConfiguration
            {
                CommandGroups = new List<CommandAuthenticationGroup>
                {
                    new()
                    {
                        Type = AuthenticationType.None,
                        Commands = new List<string> { "Get-Tenant", "Get-TenantUser" }
                    },
                    new()
                    {
                        Type = AuthenticationType.Role,
                        Role = "Global Administrator",
                        Commands = new List<string> { "Update-TenantUser" }
                    },
                    new()
                    {
                        Type = AuthenticationType.Permission,
                        Permission = "poshmcp.read",
                        Commands = new List<string> { "Get-TenantUserRole" }
                    }
                }
            }
        };
    }

    private static string CreateToolCallRequest(string toolName)
    {
        return JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments = new { }
            }
        });
    }

    private IHost CreateTestHost(PowerShellConfiguration config, bool authenticated = true, Claim[]? claims = null)
    {
        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    // Add required services
                    services.AddSingleton(config);
                    services.AddLogging();
                    services.AddRouting();
                    
                    // Add authentication for testing
                    services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                            "Test", options => { });

                    services.AddAuthorization();
                });

                webHost.Configure(app =>
                {
                    // Set up test user claims
                    app.Use(async (context, next) =>
                    {
                        if (authenticated)
                        {
                            var testClaims = claims ?? new[]
                            {
                                new Claim(ClaimTypes.Name, "Test User"),
                                new Claim(ClaimTypes.NameIdentifier, "123")
                            };
                            
                            var identity = new ClaimsIdentity(testClaims, "Test");
                            context.User = new ClaimsPrincipal(identity);
                        }
                        
                        await next();
                    });

                    // Add the authorization middleware
                    app.UseMiddleware<McpToolAuthorizationMiddleware>();

                    // Add a simple endpoint to test
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapPost("/tools/call", async context =>
                        {
                            await context.Response.WriteAsync("Tool call processed");
                        });
                    });
                });
            });

        var host = hostBuilder.Build();
        host.Start(); // Start the host
        return host;
    }

    private class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Return success - the actual user setup is done in middleware
            var ticket = new AuthenticationTicket(Context.User, "Test");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}