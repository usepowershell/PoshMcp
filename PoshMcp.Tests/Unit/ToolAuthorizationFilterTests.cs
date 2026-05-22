using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PoshMcp.Server.Authentication;
using PoshMcp.Server.Metrics;
using PoshMcp.Server.PowerShell;
using Xunit;

namespace PoshMcp.Tests.Unit;

[Trait("Category", "Unit")]
public class ToolAuthorizationFilterTests
{
    [Fact]
    public async Task AuthDisabled_CallsNextHandler()
    {
        var authConfig = CreateAuthConfig(enabled: false);
        var expected = CreateSuccessResult("passed-through");

        var (result, nextCalls) = await InvokeFilterAsync(
            authConfig,
            new PowerShellConfiguration(),
            toolName: "get_process",
            user: null,
            nextResult: expected);

        Assert.Same(expected, result);
        Assert.Equal(1, nextCalls);
    }

    [Fact]
    public async Task AllowAnonymousTool_Passthrough()
    {
        var authConfig = CreateAuthConfig(enabled: true, requireAuthentication: true);
        var psConfig = CreatePowerShellConfig("get_process", new FunctionOverride { AllowAnonymous = true });
        var expected = CreateSuccessResult("anonymous-ok");

        var (result, nextCalls) = await InvokeFilterAsync(
            authConfig,
            psConfig,
            toolName: "get_process",
            user: null,
            nextResult: expected);

        Assert.Same(expected, result);
        Assert.Equal(1, nextCalls);
    }

    [Fact]
    public async Task Unauthenticated_ReturnsError()
    {
        var authConfig = CreateAuthConfig(enabled: true, requireAuthentication: true);

        var (result, nextCalls) = await InvokeFilterAsync(
            authConfig,
            new PowerShellConfiguration(),
            toolName: "get_process",
            user: null);

        Assert.True(result.IsError is true);
        Assert.Equal(0, nextCalls);
    }

    [Fact]
    public async Task Unauthenticated_IncrementsMetric()
    {
        var authConfig = CreateAuthConfig(enabled: true, requireAuthentication: true);

        var denialCount = await MeasureToolDenialsAsync(metrics =>
            InvokeFilterAsync(
                authConfig,
                new PowerShellConfiguration(),
                toolName: "get_process",
                user: null,
                metrics: metrics));

        Assert.Equal(1, denialCount);
    }

    [Fact]
    public async Task MissingScope_ReturnsError()
    {
        var authConfig = CreateAuthConfig(
            enabled: true,
            requireAuthentication: true,
            requiredScopes: ["tools:read"]);
        var user = CreateAuthenticatedUser(scopes: ["tools:write"]);

        var (result, nextCalls) = await InvokeFilterAsync(
            authConfig,
            new PowerShellConfiguration(),
            toolName: "get_process",
            user: user);

        Assert.True(result.IsError is true);
        Assert.Equal(0, nextCalls);
    }

    [Fact]
    public async Task MissingScope_IncrementsMetric()
    {
        var authConfig = CreateAuthConfig(
            enabled: true,
            requireAuthentication: true,
            requiredScopes: ["tools:read"]);
        var user = CreateAuthenticatedUser(scopes: ["tools:write"]);

        var denialCount = await MeasureToolDenialsAsync(metrics =>
            InvokeFilterAsync(
                authConfig,
                new PowerShellConfiguration(),
                toolName: "get_process",
                user: user,
                metrics: metrics));

        Assert.Equal(1, denialCount);
    }

    [Fact]
    public async Task MissingRole_ReturnsError()
    {
        var authConfig = CreateAuthConfig(
            enabled: true,
            requireAuthentication: true,
            requiredRoles: ["admin"]);
        var user = CreateAuthenticatedUser(roles: ["reader"]);

        var (result, nextCalls) = await InvokeFilterAsync(
            authConfig,
            new PowerShellConfiguration(),
            toolName: "get_process",
            user: user);

        Assert.True(result.IsError is true);
        Assert.Equal(0, nextCalls);
    }

    [Fact]
    public async Task MissingRole_IncrementsMetric()
    {
        var authConfig = CreateAuthConfig(
            enabled: true,
            requireAuthentication: true,
            requiredRoles: ["admin"]);
        var user = CreateAuthenticatedUser(roles: ["reader"]);

        var denialCount = await MeasureToolDenialsAsync(metrics =>
            InvokeFilterAsync(
                authConfig,
                new PowerShellConfiguration(),
                toolName: "get_process",
                user: user,
                metrics: metrics));

        Assert.Equal(1, denialCount);
    }

    [Fact]
    public async Task FullyAuthorized_CallsNextHandler()
    {
        var authConfig = CreateAuthConfig(
            enabled: true,
            requireAuthentication: true,
            requiredScopes: ["tools:read"],
            requiredRoles: ["admin"]);
        var user = CreateAuthenticatedUser(scopes: ["tools:read"], roles: ["admin"]);

        var (_, nextCalls) = await InvokeFilterAsync(
            authConfig,
            new PowerShellConfiguration(),
            toolName: "get_process",
            user: user,
            nextResult: CreateSuccessResult("authorized"));

        Assert.Equal(1, nextCalls);
    }

    [Fact]
    public async Task FullyAuthorized_ReturnsNextResult()
    {
        var authConfig = CreateAuthConfig(
            enabled: true,
            requireAuthentication: true,
            requiredScopes: ["tools:read"],
            requiredRoles: ["admin"]);
        var user = CreateAuthenticatedUser(scopes: ["tools:read"], roles: ["admin"]);
        var expected = CreateSuccessResult("authorized-result");

        var (result, nextCalls) = await InvokeFilterAsync(
            authConfig,
            new PowerShellConfiguration(),
            toolName: "get_process",
            user: user,
            nextResult: expected);

        Assert.Same(expected, result);
        Assert.Equal(1, nextCalls);
    }

    [Fact]
    public async Task NullToolName_DoesNotThrow()
    {
        var authConfig = CreateAuthConfig(enabled: true, requireAuthentication: false);
        var expected = CreateSuccessResult("null-tool-name");

        var exception = await Record.ExceptionAsync(async () =>
        {
            var (result, nextCalls) = await InvokeFilterAsync(
                authConfig,
                new PowerShellConfiguration(),
                toolName: null,
                user: null,
                nextResult: expected);

            Assert.Same(expected, result);
            Assert.Equal(1, nextCalls);
        });

        Assert.Null(exception);
    }

    [Fact]
    public async Task DefaultPolicyScopes_UsedWhenNoOverride()
    {
        var authConfig = CreateAuthConfig(
            enabled: true,
            requireAuthentication: true,
            requiredScopes: ["tools:read"]);
        var user = CreateAuthenticatedUser(scopes: ["tools:write"]);

        var (result, nextCalls) = await InvokeFilterAsync(
            authConfig,
            new PowerShellConfiguration(),
            toolName: "get_process",
            user: user);

        Assert.True(result.IsError is true);
        Assert.Equal(0, nextCalls);
    }

    [Fact]
    public async Task ToolOverrideScopes_OverrideDefaultPolicy()
    {
        var authConfig = CreateAuthConfig(
            enabled: true,
            requireAuthentication: true,
            requiredScopes: ["default:scope"]);
        var psConfig = CreatePowerShellConfig(
            "get_process",
            new FunctionOverride { RequiredScopes = ["override:scope"] });
        var user = CreateAuthenticatedUser(scopes: ["override:scope"]);
        var expected = CreateSuccessResult("override-applied");

        var (result, nextCalls) = await InvokeFilterAsync(
            authConfig,
            psConfig,
            toolName: "get_process",
            user: user,
            nextResult: expected);

        Assert.Same(expected, result);
        Assert.Equal(1, nextCalls);
    }

    private static AuthenticationConfiguration CreateAuthConfig(
        bool enabled,
        bool requireAuthentication = true,
        List<string>? requiredScopes = null,
        List<string>? requiredRoles = null)
    {
        return new AuthenticationConfiguration
        {
            Enabled = enabled,
            DefaultPolicy = new AuthorizationPolicyConfiguration
            {
                RequireAuthentication = requireAuthentication,
                RequiredScopes = requiredScopes ?? [],
                RequiredRoles = requiredRoles ?? []
            }
        };
    }

    private static PowerShellConfiguration CreatePowerShellConfig(string toolName, FunctionOverride functionOverride)
    {
        return new PowerShellConfiguration
        {
            CommandOverrides = new Dictionary<string, FunctionOverride>(StringComparer.OrdinalIgnoreCase)
            {
                [toolName] = functionOverride
            }
        };
    }

    private static ClaimsPrincipal CreateAuthenticatedUser(
        List<string>? scopes = null,
        List<string>? roles = null)
    {
        var claims = new List<Claim>();

        if (scopes is not null)
        {
            foreach (var scope in scopes)
            {
                claims.Add(new Claim("scp", scope));
            }
        }

        if (roles is not null)
        {
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }

        var identity = new ClaimsIdentity(claims, authenticationType: "Test", nameType: ClaimTypes.Name, roleType: ClaimTypes.Role);
        return new ClaimsPrincipal(identity);
    }

    private static CallToolResult CreateSuccessResult(string message)
    {
        return new CallToolResult
        {
            IsError = false,
            Content = [new TextContentBlock { Text = message }]
        };
    }

    private static RequestContext<CallToolRequestParams> CreateRequestContext(string? toolName, ClaimsPrincipal? user)
    {
        return new RequestContext<CallToolRequestParams>(
            new Mock<McpServer>().Object,
            new JsonRpcRequest { Method = "tools/call" },
            new CallToolRequestParams { Name = toolName! })
        {
            User = user
        };
    }

    private static async Task<(CallToolResult Result, int NextCalls)> InvokeFilterAsync(
        AuthenticationConfiguration authConfig,
        PowerShellConfiguration psConfig,
        string? toolName,
        ClaimsPrincipal? user,
        CallToolResult? nextResult = null,
        McpMetrics? metrics = null)
    {
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        var logger = new Mock<ILogger<ToolAuthorizationFilter>>();
        var ownedMetrics = metrics is null ? new McpMetrics() : null;
        metrics ??= ownedMetrics!;

        try
        {
            var filter = new ToolAuthorizationFilter(
                authConfig,
                psConfig,
                httpContextAccessor.Object,
                metrics,
                logger.Object).AsFilter();

            var nextCalls = 0;
            McpRequestHandler<CallToolRequestParams, CallToolResult> next = (_, _) =>
            {
                nextCalls++;
                return ValueTask.FromResult(nextResult ?? CreateSuccessResult("next-called"));
            };

            var result = await filter(next)(CreateRequestContext(toolName, user), CancellationToken.None);
            return (result, nextCalls);
        }
        finally
        {
            ownedMetrics?.Dispose();
        }
    }

    private static async Task<long> MeasureToolDenialsAsync(Func<McpMetrics, Task> action)
    {
        var denialCount = 0L;
        var metrics = new McpMetrics();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == McpMetrics.MeterName && instrument.Name == "poshmcp.auth.tool_denials")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };

        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) =>
        {
            Interlocked.Add(ref denialCount, measurement);
        });
        listener.Start();

        try
        {
            await action(metrics);
            return denialCount;
        }
        finally
        {
            metrics.Dispose();
        }
    }
}
