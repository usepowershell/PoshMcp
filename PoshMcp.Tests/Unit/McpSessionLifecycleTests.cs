using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.AspNetCore;
using PoshMcp.Server.PowerShell;
using Xunit;

namespace PoshMcp.Tests.Unit;

[Trait("Category", "Unit")]
public class McpSessionLifecycleTests
{
    [Fact]
    public async Task IdleSessionExpiry_ReleasesProtocolAndRunspaceState()
    {
        var timeProvider = new FakeTimeProvider();
        var httpContextAccessor = new HttpContextAccessor();
        using var runspaces = new SessionAwarePowerShellRunspace(
            httpContextAccessor,
            NullLogger<SessionAwarePowerShellRunspace>.Instance);
        var lifecycle = new McpSessionLifecycle(runspaces.CleanupSession);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(lifecycle);
        builder.Services
            .AddMcpServer()
            .WithHttpTransport(options =>
            {
                options.Stateless = false; // Stateful HTTP required for Mcp-Session-Id and lifecycle callbacks.
#pragma warning disable MCP9006 // Intentional: this test exercises stateful-mode IdleTimeout behavior.
                options.IdleTimeout = TimeSpan.FromHours(1);
#pragma warning restore MCP9006
                options.TimeProvider = timeProvider;
#pragma warning disable MCPEXP002 // The lifecycle callback is the production cleanup integration point.
                options.RunSessionHandler = lifecycle.RunSessionAsync;
#pragma warning restore MCPEXP002
            });

        await using var app = builder.Build();
        app.UseMiddleware<McpProtocolVersionMiddleware>((object)new[] { "/" });
        app.MapMcp();
        await app.StartAsync();

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");

        using var initializeResponse = await client.PostAsync(
            "/",
            new StringContent(
                JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "initialize",
                    @params = new
                    {
                        protocolVersion = McpProtocolVersionMiddleware.CurrentProtocolVersion,
                        capabilities = new { },
                        clientInfo = new { name = "idle-expiry-test", version = "1.0" }
                    }
                }),
                Encoding.UTF8,
                "application/json"));

        Assert.Equal(HttpStatusCode.OK, initializeResponse.StatusCode);
        var sessionId = initializeResponse.Headers.GetValues("Mcp-Session-Id").Single();
        Assert.True(lifecycle.TryGetProtocolVersion(sessionId, out var protocolVersion));
        Assert.Equal(McpProtocolVersionMiddleware.CurrentProtocolVersion, protocolVersion);

        httpContextAccessor.HttpContext = new DefaultHttpContext();
        httpContextAccessor.HttpContext.Request.Headers["Mcp-Session-Id"] = sessionId;
        _ = runspaces.Instance;
        Assert.Equal(1, runspaces.GetStats().ActiveSessions);

        timeProvider.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(5));
        await WaitForAsync(
            () => !lifecycle.TryGetProtocolVersion(sessionId, out _) &&
                  runspaces.GetStats().ActiveSessions == 0);

        using var expiredRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/")
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""",
                Encoding.UTF8,
                "application/json")
        };
        expiredRequest.Headers.Add("Mcp-Session-Id", sessionId);
        expiredRequest.Headers.Add("MCP-Protocol-Version", McpProtocolVersionMiddleware.CurrentProtocolVersion);
        using var expiredResponse = await client.SendAsync(expiredRequest);

        Assert.Equal(HttpStatusCode.NotFound, expiredResponse.StatusCode);
    }

    [Fact]
    public async Task ServerShutdown_ReleasesSessionState()
    {
        var httpContextAccessor = new HttpContextAccessor();
        using var runspaces = new SessionAwarePowerShellRunspace(
            httpContextAccessor,
            NullLogger<SessionAwarePowerShellRunspace>.Instance);
        var lifecycle = new McpSessionLifecycle(runspaces.CleanupSession);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(lifecycle);
        builder.Services
            .AddMcpServer()
            .WithHttpTransport(options =>
            {
                options.Stateless = false; // Stateful HTTP required for Mcp-Session-Id and lifecycle callbacks.
#pragma warning disable MCP9006 // Intentional: this test exercises stateful-mode shutdown cleanup behavior.
                options.IdleTimeout = Timeout.InfiniteTimeSpan;
#pragma warning restore MCP9006
#pragma warning disable MCPEXP002 // The lifecycle callback is the production cleanup integration point.
                options.RunSessionHandler = lifecycle.RunSessionAsync;
#pragma warning restore MCPEXP002
            });

        await using var app = builder.Build();
        app.UseMiddleware<McpProtocolVersionMiddleware>((object)new[] { "/" });
        app.MapMcp();
        await app.StartAsync();

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
        using var initializeResponse = await client.PostAsync(
            "/",
            new StringContent(
                JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "initialize",
                    @params = new
                    {
                        protocolVersion = McpProtocolVersionMiddleware.CurrentProtocolVersion,
                        capabilities = new { },
                        clientInfo = new { name = "shutdown-cleanup-test", version = "1.0" }
                    }
                }),
                Encoding.UTF8,
                "application/json"));

        var sessionId = initializeResponse.Headers.GetValues("Mcp-Session-Id").Single();
        httpContextAccessor.HttpContext = new DefaultHttpContext();
        httpContextAccessor.HttpContext.Request.Headers["Mcp-Session-Id"] = sessionId;
        _ = runspaces.Instance;
        Assert.Equal(1, runspaces.GetStats().ActiveSessions);

        await app.StopAsync();
        await WaitForAsync(
            () => !lifecycle.TryGetProtocolVersion(sessionId, out _) &&
                  runspaces.GetStats().ActiveSessions == 0);
    }

    [Fact]
    public async Task ApplicationStopped_DisposesSessionRunspaceManager()
    {
        var httpContextAccessor = new HttpContextAccessor();
        using var runspaces = new SessionAwarePowerShellRunspace(
            httpContextAccessor,
            NullLogger<SessionAwarePowerShellRunspace>.Instance,
            new SessionRunspaceOptions { WarmStandbyCount = 1 });

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IPowerShellRunspace>(runspaces);

        await using var app = builder.Build();
        app.Lifetime.ApplicationStopped.Register(runspaces.Dispose);
        await app.StartAsync();

        Assert.Equal(1, runspaces.GetStats().WarmStandbyCount);

        await app.StopAsync();

        Assert.Equal(0, runspaces.GetStats().WarmStandbyCount);
        Assert.Throws<ObjectDisposedException>(() => runspaces.ExecuteThreadSafe(_ => 0));
    }

    /// <summary>
    /// Validates that setting <see cref="HttpServerTransportOptions.Stateless"/> = false
    /// (the PoshMcp default) causes the server to return a <c>Mcp-Session-Id</c> header
    /// on initialize, confirming stateful HTTP transport is active.
    /// </summary>
    [Fact]
    public async Task StatelessFalse_ReturnsMcpSessionId_OnInitialize()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(new McpSessionLifecycle(_ => { }));
        builder.Services
            .AddMcpServer()
            .WithHttpTransport(options =>
            {
                options.Stateless = false; // Explicit PoshMcp default — stateful HTTP transport.
            });

        await using var app = builder.Build();
        app.MapMcp();
        await app.StartAsync();

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");

        using var initResponse = await client.PostAsync(
            "/",
            new StringContent(
                JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "initialize",
                    @params = new
                    {
                        protocolVersion = McpProtocolVersionMiddleware.CurrentProtocolVersion,
                        capabilities = new { },
                        clientInfo = new { name = "stateful-option-test", version = "1.0" }
                    }
                }),
                Encoding.UTF8,
                "application/json"));

        Assert.Equal(System.Net.HttpStatusCode.OK, initResponse.StatusCode);
        // Stateful transport MUST return Mcp-Session-Id. Absence indicates Stateless=true is active.
        Assert.True(
            initResponse.Headers.Contains("Mcp-Session-Id"),
            "Stateful HTTP (Stateless=false) must return Mcp-Session-Id on initialize.");
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Timed out waiting for the idle session cleanup callback.");
            }

            await Task.Delay(10);
        }
    }
}
