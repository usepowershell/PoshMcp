using System.Collections.Generic;
using System.Text.Json;
using PoshMcp.Server.PowerShell;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Unit tests for tool name to command name conversion
/// </summary>
public class ToolNameMappingTests
{
    private readonly ITestOutputHelper _output;

    public ToolNameMappingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData("get_process_name", "Get-ProcessName")]
    [InlineData("update_tenant_user", "Update-TenantUser")]
    [InlineData("get_tenant_user_role", "Get-TenantUserRole")]
    [InlineData("simple_command", "Simple-Command")]
    [InlineData("singleword", "singleword")]
    public void ConvertToolNameToCommandName_ShouldMapCorrectly(string toolName, string expectedCommandName)
    {
        // Arrange & Act
        var commandName = ConvertToolNameToCommandName(toolName);

        // Assert
        Assert.Equal(expectedCommandName, commandName);
        _output.WriteLine($"Tool '{toolName}' -> Command '{commandName}'");
    }

    private static string ConvertToolNameToCommandName(string toolName)
    {
        // Convert tool names like "get_process_name" back to "Get-Process"
        // Handle multi-word tool names like "get_tenant_user_role" -> "Get-TenantUserRole"
        var parts = toolName.Split('_');
        if (parts.Length >= 2)
        {
            var verb = char.ToUpper(parts[0][0]) + parts[0].Substring(1);

            // Combine all remaining parts as the noun, capitalizing each part
            var nounParts = new string[parts.Length - 1];
            for (int i = 1; i < parts.Length; i++)
            {
                nounParts[i - 1] = char.ToUpper(parts[i][0]) + parts[i].Substring(1);
            }
            var noun = string.Join("", nounParts);

            return $"{verb}-{noun}";
        }

        return toolName;
    }
}