using System.Collections.Generic;
using System.Text.Json;
using PoshMcp.Server.PowerShell;
using Xunit;

namespace PoshMcp.Tests.Unit;

public class FunctionOverrideAuthPropertiesTests
{
    [Fact]
    public void FunctionOverride_DefaultValues_AllNullable()
    {
        var fo = new FunctionOverride();

        Assert.Null(fo.RequiredScopes);
        Assert.Null(fo.RequiredRoles);
        Assert.Null(fo.AllowAnonymous);
    }

    [Fact]
    public void FunctionOverride_CanSet_RequiredScopes()
    {
        var scopes = new List<string> { "tools:read", "tools:write" };
        var fo = new FunctionOverride { RequiredScopes = scopes };

        Assert.NotNull(fo.RequiredScopes);
        Assert.Equal(2, fo.RequiredScopes.Count);
        Assert.Contains("tools:read", fo.RequiredScopes);
        Assert.Contains("tools:write", fo.RequiredScopes);
    }

    [Fact]
    public void FunctionOverride_CanSet_RequiredRoles()
    {
        var roles = new List<string> { "admin", "operator" };
        var fo = new FunctionOverride { RequiredRoles = roles };

        Assert.NotNull(fo.RequiredRoles);
        Assert.Equal(2, fo.RequiredRoles.Count);
        Assert.Contains("admin", fo.RequiredRoles);
        Assert.Contains("operator", fo.RequiredRoles);
    }

    [Fact]
    public void FunctionOverride_CanSet_AllowAnonymous()
    {
        var foTrue = new FunctionOverride { AllowAnonymous = true };
        var foFalse = new FunctionOverride { AllowAnonymous = false };

        Assert.True(foTrue.AllowAnonymous);
        Assert.False(foFalse.AllowAnonymous);
    }

    [Fact]
    public void FunctionOverride_DeserializesFromJson()
    {
        const string json = """
            {
              "RequiredScopes": ["tools:read", "tools:write"],
              "RequiredRoles": ["admin"],
              "AllowAnonymous": false
            }
            """;

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var fo = JsonSerializer.Deserialize<FunctionOverride>(json, options);

        Assert.NotNull(fo);
        Assert.NotNull(fo.RequiredScopes);
        Assert.Equal(2, fo.RequiredScopes.Count);
        Assert.Contains("tools:read", fo.RequiredScopes);
        Assert.Contains("tools:write", fo.RequiredScopes);
        Assert.NotNull(fo.RequiredRoles);
        Assert.Single(fo.RequiredRoles);
        Assert.Contains("admin", fo.RequiredRoles);
        Assert.False(fo.AllowAnonymous);
    }
}
