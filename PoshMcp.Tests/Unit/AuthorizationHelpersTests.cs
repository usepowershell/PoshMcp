using System.Collections.Generic;
using PoshMcp.Server.Authentication;
using PoshMcp.Server.PowerShell;
using Xunit;

namespace PoshMcp.Tests.Unit;

public class AuthorizationHelpersTests
{
    [Fact]
    public void GetToolOverride_PrefersExactToolNameOverride_BeforeCommandNameResolution()
    {
        var config = new PowerShellConfiguration
        {
            CommandNames = new List<string> { "Get-Process" },
            FunctionOverrides = new Dictionary<string, FunctionOverride>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["get_process_name"] = new FunctionOverride
                {
                    RequiredRoles = new List<string> { "generated" }
                },
                ["Get-Process"] = new FunctionOverride
                {
                    RequiredRoles = new List<string> { "command" }
                }
            }
        };

        var overrideConfig = AuthorizationHelpers.GetToolOverride("get_process_name", config);

        Assert.NotNull(overrideConfig);
        Assert.Equal(new List<string> { "generated" }, overrideConfig!.RequiredRoles);
    }

    [Fact]
    public void GetToolOverride_ResolvesConfiguredCommandName_FromGeneratedToolNameWithParameterSetSuffix()
    {
        var config = new PowerShellConfiguration
        {
            CommandNames = new List<string> { "Get-Process" },
            FunctionOverrides = new Dictionary<string, FunctionOverride>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["Get-Process"] = new FunctionOverride
                {
                    RequiredRoles = new List<string> { "ops" }
                }
            }
        };

        var overrideConfig = AuthorizationHelpers.GetToolOverride("get_process_name", config);

        Assert.NotNull(overrideConfig);
        Assert.Equal(new List<string> { "ops" }, overrideConfig!.RequiredRoles);
    }

    [Fact]
    public void GetToolOverride_PrefersLongerConfiguredCommand_WhenAmbiguous()
    {
        var config = new PowerShellConfiguration
        {
            CommandNames = new List<string> { "Get-ProcessName", "Get-Process" },
            FunctionOverrides = new Dictionary<string, FunctionOverride>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["Get-ProcessName"] = new FunctionOverride
                {
                    RequiredScopes = new List<string> { "tools:read:detailed" }
                },
                ["Get-Process"] = new FunctionOverride
                {
                    RequiredScopes = new List<string> { "tools:read" }
                }
            }
        };

        var overrideConfig = AuthorizationHelpers.GetToolOverride("get_process_name", config);

        Assert.NotNull(overrideConfig);
        Assert.Equal(new List<string> { "tools:read:detailed" }, overrideConfig!.RequiredScopes);
    }

    [Fact]
    public void GetToolOverride_ResolvesLegacyFunctionNames_WhenCommandNamesAreNotConfigured()
    {
        var config = new PowerShellConfiguration
        {
            FunctionNames = new List<string> { "Get-Service" },
            FunctionOverrides = new Dictionary<string, FunctionOverride>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["Get-Service"] = new FunctionOverride
                {
                    AllowAnonymous = false,
                    RequiredRoles = new List<string> { "reader" }
                }
            }
        };

        var overrideConfig = AuthorizationHelpers.GetToolOverride("get_service_display_name", config);

        Assert.NotNull(overrideConfig);
        Assert.False(overrideConfig!.AllowAnonymous);
        Assert.Equal(new List<string> { "reader" }, overrideConfig.RequiredRoles);
    }

    [Fact]
    public void GetToolOverride_ReturnsNull_WhenNoMatchingOverrideExists()
    {
        var config = new PowerShellConfiguration
        {
            CommandNames = new List<string> { "Get-Process" },
            FunctionOverrides = new Dictionary<string, FunctionOverride>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["Get-Service"] = new FunctionOverride
                {
                    RequiredRoles = new List<string> { "reader" }
                }
            }
        };

        var overrideConfig = AuthorizationHelpers.GetToolOverride("get_process_name", config);

        Assert.Null(overrideConfig);
    }
}
