using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace PoshMcp.Tests.Unit;

[Trait("Category", "Unit")]
public class McpOriginValidationMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_AllowsSameOriginMcpRequest()
    {
        var called = false;
        var middleware = CreateMiddleware(() => called = true);
        var context = CreateContext("/mcp", "https://localhost:8080");

        await middleware.InvokeAsync(context);

        Assert.True(called);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_AllowsConfiguredCrossOrigin()
    {
        var called = false;
        var middleware = CreateMiddleware(
            () => called = true,
            new[] { "https://client.example" });
        var context = CreateContext("/mcp", "https://client.example");

        await middleware.InvokeAsync(context);

        Assert.True(called);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_RejectsInvalidMcpOrigin()
    {
        var called = false;
        var middleware = CreateMiddleware(() => called = true);
        var context = CreateContext("/mcp", "https://attacker.example");

        await middleware.InvokeAsync(context);

        Assert.False(called);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_DoesNotApplyToNonMcpEndpoint()
    {
        var called = false;
        var middleware = CreateMiddleware(() => called = true);
        var context = CreateContext("/health", "https://attacker.example");

        await middleware.InvokeAsync(context);

        Assert.True(called);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    private static McpOriginValidationMiddleware CreateMiddleware(
        System.Action nextAction,
        IEnumerable<string>? allowedOrigins = null)
    {
        return new McpOriginValidationMiddleware(
            _ =>
            {
                nextAction();
                return Task.CompletedTask;
            },
            new[] { "/mcp" },
            allowedOrigins ?? []);
    }

    private static DefaultHttpContext CreateContext(string path, string origin)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost", 8080);
        context.Request.Path = path;
        context.Request.Headers.Origin = origin;
        return context;
    }
}
