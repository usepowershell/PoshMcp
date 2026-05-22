using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using PoshMcp.Server.Authentication;
using PoshMcp.Server.PowerShell;
using Xunit;

namespace PoshMcp.Tests.Unit;

[Trait("Category", "Unit")]
public class ToolListAuthorizationFilterTests
{
    [Fact]
    public async Task AuthDisabled_ReturnsFullList()
    {
        var result = CreateResult(CreateTool("public-tool"), CreateTool("protected-tool"));
        var sut = CreateSut(new AuthenticationConfiguration { Enabled = false }, new PowerShellConfiguration());

        var filtered = await InvokeFilterAsync(sut, CreateContext(), result);

        Assert.Same(result, filtered);
        Assert.Equal(["public-tool", "protected-tool"], GetToolNames(filtered));
    }

    [Fact]
    public async Task NextHandler_RunsBeforeAuthorizationFiltering()
    {
        var authConfig = CreateEnabledAuthConfig();
        var result = CreateResult(CreateTool("protected-tool"));
        var sut = CreateSut(authConfig, new PowerShellConfiguration());
        var context = CreateContext();
        var nextCallCount = 0;
        McpRequestHandler<ListToolsRequestParams, ListToolsResult> next = (ctx, ct) =>
        {
            nextCallCount++;
            authConfig.Enabled = false;
            return ValueTask.FromResult(result);
        };

        var filtered = await sut.AsFilter()(next)(context, CancellationToken.None);

        Assert.Equal(1, nextCallCount);
        Assert.Equal(["protected-tool"], GetToolNames(filtered));
    }

    [Fact]
    public async Task EmptyToolList_ReturnsEmpty()
    {
        var result = new ListToolsResult { Tools = new List<Tool>() };
        var sut = CreateSut(CreateEnabledAuthConfig(), new PowerShellConfiguration());

        var filtered = await InvokeFilterAsync(sut, CreateContext(), result);

        Assert.Same(result, filtered);
        Assert.NotNull(filtered.Tools);
        Assert.Empty(filtered.Tools);
    }

    [Fact]
    public async Task NullToolList_ReturnsNull()
    {
        var result = new ListToolsResult { Tools = null! };
        var sut = CreateSut(CreateEnabledAuthConfig(), new PowerShellConfiguration());

        var filtered = await InvokeFilterAsync(sut, CreateContext(), result);

        Assert.Same(result, filtered);
        Assert.Null(filtered.Tools);
    }

    [Fact]
    public async Task Unauthenticated_HidesProtectedTools()
    {
        var result = CreateResult(CreateTool("anonymous-tool"), CreateTool("protected-tool"));
        var psConfig = new PowerShellConfiguration
        {
            CommandOverrides = new Dictionary<string, FunctionOverride>
            {
                ["anonymous-tool"] = new() { AllowAnonymous = true }
            }
        };
        var sut = CreateSut(CreateEnabledAuthConfig(), psConfig);

        var filtered = await InvokeFilterAsync(sut, CreateContext(), result);

        Assert.Equal(["anonymous-tool"], GetToolNames(filtered));
    }

    [Fact]
    public async Task AllowAnonymousTool_AlwaysVisible()
    {
        var result = CreateResult(CreateTool("anonymous-tool"));
        var psConfig = new PowerShellConfiguration
        {
            CommandOverrides = new Dictionary<string, FunctionOverride>
            {
                ["anonymous-tool"] = new() { AllowAnonymous = true }
            }
        };
        var sut = CreateSut(CreateEnabledAuthConfig(), psConfig);

        var filtered = await InvokeFilterAsync(sut, CreateContext(), result);

        Assert.Equal(["anonymous-tool"], GetToolNames(filtered));
    }

    [Fact]
    public async Task AuthenticatedUser_SeesAllDefaultTools()
    {
        var result = CreateResult(CreateTool("tool-one"), CreateTool("tool-two"));
        var sut = CreateSut(CreateEnabledAuthConfig(), new PowerShellConfiguration());
        var user = CreateUser(isAuthenticated: true);

        var filtered = await InvokeFilterAsync(sut, CreateContext(user), result);

        Assert.Equal(["tool-one", "tool-two"], GetToolNames(filtered));
    }

    [Fact]
    public async Task MissingScope_HidesTool()
    {
        var result = CreateResult(CreateTool("scoped-tool"));
        var psConfig = new PowerShellConfiguration
        {
            CommandOverrides = new Dictionary<string, FunctionOverride>
            {
                ["scoped-tool"] = new() { RequiredScopes = ["tools:write"] }
            }
        };
        var sut = CreateSut(CreateEnabledAuthConfig(), psConfig);
        var user = CreateUser(isAuthenticated: true, scopes: ["tools:read"]);

        var filtered = await InvokeFilterAsync(sut, CreateContext(user), result);

        Assert.Empty(filtered.Tools!);
    }

    [Fact]
    public async Task MissingRole_HidesTool()
    {
        var result = CreateResult(CreateTool("role-tool"));
        var psConfig = new PowerShellConfiguration
        {
            CommandOverrides = new Dictionary<string, FunctionOverride>
            {
                ["role-tool"] = new() { RequiredRoles = ["admin"] }
            }
        };
        var sut = CreateSut(CreateEnabledAuthConfig(), psConfig);
        var user = CreateUser(isAuthenticated: true, roles: ["reader"]);

        var filtered = await InvokeFilterAsync(sut, CreateContext(user), result);

        Assert.Empty(filtered.Tools!);
    }

    [Fact]
    public async Task HasScopeAndRole_ShowsTool()
    {
        var result = CreateResult(CreateTool("restricted-tool"));
        var psConfig = new PowerShellConfiguration
        {
            CommandOverrides = new Dictionary<string, FunctionOverride>
            {
                ["restricted-tool"] = new()
                {
                    RequiredScopes = ["tools:read"],
                    RequiredRoles = ["admin"]
                }
            }
        };
        var sut = CreateSut(CreateEnabledAuthConfig(), psConfig);
        var user = CreateUser(isAuthenticated: true, scopes: ["tools:read"], roles: ["admin"]);

        var filtered = await InvokeFilterAsync(sut, CreateContext(user), result);

        Assert.Equal(["restricted-tool"], GetToolNames(filtered));
    }

    [Fact]
    public async Task MixedPermissions_FiltersCorrectly()
    {
        var result = CreateResult(CreateTool("default-tool"), CreateTool("scoped-tool"), CreateTool("admin-tool"));
        var psConfig = new PowerShellConfiguration
        {
            CommandOverrides = new Dictionary<string, FunctionOverride>
            {
                ["scoped-tool"] = new() { RequiredScopes = ["tools:read"] },
                ["admin-tool"] = new() { RequiredRoles = ["admin"] }
            }
        };
        var sut = CreateSut(CreateEnabledAuthConfig(), psConfig);
        var user = CreateUser(isAuthenticated: true, scopes: ["tools:read"], roles: ["reader"]);

        var filtered = await InvokeFilterAsync(sut, CreateContext(user), result);

        Assert.Equal(["default-tool", "scoped-tool"], GetToolNames(filtered));
    }

    [Fact]
    public async Task PerToolOverride_OverridesDefaultPolicy()
    {
        var authConfig = CreateEnabledAuthConfig();
        authConfig.DefaultPolicy.RequiredScopes = ["default-scope"];

        var result = CreateResult(CreateTool("override-tool"));
        var psConfig = new PowerShellConfiguration
        {
            CommandOverrides = new Dictionary<string, FunctionOverride>
            {
                ["override-tool"] = new() { RequiredScopes = ["override-scope"] }
            }
        };
        var sut = CreateSut(authConfig, psConfig);
        var user = CreateUser(isAuthenticated: true, scopes: ["override-scope"]);

        var filtered = await InvokeFilterAsync(sut, CreateContext(user), result);

        Assert.Equal(["override-tool"], GetToolNames(filtered));
    }

    private static async Task<ListToolsResult> InvokeFilterAsync(
        ToolListAuthorizationFilter sut,
        RequestContext<ListToolsRequestParams> context,
        ListToolsResult nextResult)
    {
        var nextCallCount = 0;
        McpRequestHandler<ListToolsRequestParams, ListToolsResult> next = (ctx, ct) =>
        {
            nextCallCount++;
            return ValueTask.FromResult(nextResult);
        };

        var filter = sut.AsFilter();
        var handler = filter(next);
        var result = await handler(context, CancellationToken.None);

        Assert.Equal(1, nextCallCount);
        return result;
    }

    private static ToolListAuthorizationFilter CreateSut(
        AuthenticationConfiguration authConfig,
        PowerShellConfiguration psConfig,
        ClaimsPrincipal? httpContextUser = null)
    {
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.SetupGet(a => a.HttpContext).Returns(httpContextUser == null
            ? null
            : new DefaultHttpContext { User = httpContextUser });

        return new ToolListAuthorizationFilter(
            authConfig,
            psConfig,
            httpContextAccessor.Object,
            Mock.Of<ILogger<ToolListAuthorizationFilter>>());
    }

    private static AuthenticationConfiguration CreateEnabledAuthConfig()
    {
        return new AuthenticationConfiguration
        {
            Enabled = true,
            DefaultPolicy = new AuthorizationPolicyConfiguration
            {
                RequireAuthentication = true,
                RequiredScopes = [],
                RequiredRoles = []
            }
        };
    }

    private static RequestContext<ListToolsRequestParams> CreateContext(ClaimsPrincipal? user = null)
    {
        return new RequestContext<ListToolsRequestParams>(
            new Mock<McpServer>().Object,
            new JsonRpcRequest { Method = "tools/list" },
            new ListToolsRequestParams())
        {
            User = user
        };
    }

    private static ClaimsPrincipal CreateUser(
        bool isAuthenticated,
        IEnumerable<string>? scopes = null,
        IEnumerable<string>? roles = null)
    {
        var claims = new List<Claim>();

        if (scopes != null)
        {
            foreach (var scope in scopes)
            {
                claims.Add(new Claim("scp", scope));
            }
        }

        if (roles != null)
        {
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }

        var identity = isAuthenticated
            ? new ClaimsIdentity(claims, authenticationType: "TestScheme", nameType: ClaimTypes.Name, roleType: ClaimTypes.Role)
            : new ClaimsIdentity(claims);

        return new ClaimsPrincipal(identity);
    }

    private static ListToolsResult CreateResult(params Tool[] tools) => new()
    {
        Tools = new List<Tool>(tools)
    };

    private static Tool CreateTool(string name) => new()
    {
        Name = name,
        Description = $"Tool {name}"
    };

    private static string[] GetToolNames(ListToolsResult result)
    {
        return result.Tools == null
            ? []
            : [.. System.Linq.Enumerable.Select(result.Tools, t => t.Name)];
    }
}
