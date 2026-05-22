using System;
using System.Collections.Generic;
using ModelContextProtocol.Server;
using PoshMcp;
using Xunit;

namespace PoshMcp.Tests.Unit;

[Trait("Category", "Unit")]
public class ConfigurationHelpersTests
{
    private sealed record ConfiguredFunctionStatus(IReadOnlyList<string> MatchedToolNames);

    [Theory]
    [InlineData("Get-Date", "get_date")]
    [InlineData("Get-ProcessName", "get_process_name")]
    [InlineData("Get-TenantUserRole", "get_tenant_user_role")]
    [InlineData("Invoke-RestMethod", "invoke_rest_method")]
    [InlineData("New-AzResourceGroupDeployment", "new_az_resource_group_deployment")]
    [InlineData("Get-CIMInstance", "get_ciminstance")]
    [InlineData("Verb-Noun123Test", "verb_noun123_test")]
    public void ToToolName_NormalizesPowerShellFunctionNames(string functionName, string expectedToolName)
    {
        var actual = ConfigurationHelpers.ToToolName(functionName);

        Assert.Equal(expectedToolName, actual);
    }

    [Fact]
    public void GetExpectedToolNames_AlwaysIncludesFixedOutputTools()
    {
        var configuredFunctionStatus = new[]
        {
            new ConfiguredFunctionStatus(new[] { "zeta_tool", "alpha_tool" }),
            new ConfiguredFunctionStatus(new[] { "alpha_tool", "beta_tool" }),
        };

        var actual = ConfigurationHelpers.GetExpectedToolNames(
            configuredFunctionStatus,
            status => status.MatchedToolNames,
            enableDynamicReloadTools: false);

        Assert.Equal(
            new[]
            {
                "alpha_tool",
                "beta_tool",
                "filter_last_command_output",
                "get_last_command_output",
                "group_last_command_output",
                "sort_last_command_output",
                "zeta_tool",
            },
            actual);
        Assert.DoesNotContain("reload_configuration_from_file", actual);
        Assert.DoesNotContain("update_configuration", actual);
        Assert.DoesNotContain("get_configuration_status", actual);
    }

    [Fact]
    public void GetExpectedToolNames_IncludesReloadToolsWhenEnabled()
    {
        var configuredFunctionStatus = new[]
        {
            new ConfiguredFunctionStatus(new[] { "zeta_tool", "alpha_tool" }),
            new ConfiguredFunctionStatus(new[] { "beta_tool" }),
        };

        var actual = ConfigurationHelpers.GetExpectedToolNames(
            configuredFunctionStatus,
            status => status.MatchedToolNames,
            enableDynamicReloadTools: true);

        Assert.Equal(
            new[]
            {
                "alpha_tool",
                "beta_tool",
                "filter_last_command_output",
                "get_configuration_status",
                "get_last_command_output",
                "group_last_command_output",
                "reload_configuration_from_file",
                "sort_last_command_output",
                "update_configuration",
                "zeta_tool",
            },
            actual);
    }

    [Fact]
    public void GetDiscoveredToolNames_ExtractsDistinctSortedNamesFromRealMcpServerTools()
    {
        var tools = new List<McpServerTool>
        {
            MakeTool("zeta_tool"),
            MakeTool("Alpha_Tool"),
            MakeTool("alpha_tool"),
            MakeTool("beta_tool"),
        };

        var actual = ConfigurationHelpers.GetDiscoveredToolNames(tools);

        Assert.Equal(new[] { "Alpha_Tool", "beta_tool", "zeta_tool" }, actual);
    }

    private static McpServerTool MakeTool(string toolName)
    {
        var stub = new Func<string>(() => "stub");
        return McpServerTool.Create(stub, new McpServerToolCreateOptions
        {
            Name = toolName,
            Title = toolName,
            Description = "stub",
        });
    }
}
