using System.Collections.Generic;
using System.Text.Json;
using PoshMcp.Server.PowerShell;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Tests for the flexible command configuration parser
/// </summary>
public class CommandConfigurationParserTests
{
    private readonly ITestOutputHelper _output;

    public CommandConfigurationParserTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void ParseCommandsConfiguration_ShouldHandleUserFriendlyFormat()
    {
        // Arrange - JSON format similar to the desired YAML structure from the issue
        var json = """
        [
          "Get-Tenant",
          "Get-TenantUser",
          {
            "role": "Global Administrator",
            "commands": ["Update-TenantUser"]
          },
          {
            "permission": "poshmcp.read",
            "commands": ["Get-TenantUserRole"]
          }
        ]
        """;

        // Act
        using var doc = JsonDocument.Parse(json);
        var config = CommandConfigurationParser.ParseCommandsConfiguration(doc.RootElement);

        // Assert
        Assert.NotNull(config);
        Assert.Equal(4, config.CommandGroups.Count);

        // Check first group (Get-Tenant) - no auth
        var getTenantGroup = config.GetAuthenticationRequirements("Get-Tenant");
        Assert.NotNull(getTenantGroup);
        Assert.Equal(AuthenticationType.None, getTenantGroup.Type);

        // Check second group (Get-TenantUser) - no auth
        var getTenantUserGroup = config.GetAuthenticationRequirements("Get-TenantUser");
        Assert.NotNull(getTenantUserGroup);
        Assert.Equal(AuthenticationType.None, getTenantUserGroup.Type);

        // Check third group (Update-TenantUser) - role required
        var updateTenantUserGroup = config.GetAuthenticationRequirements("Update-TenantUser");
        Assert.NotNull(updateTenantUserGroup);
        Assert.Equal(AuthenticationType.Role, updateTenantUserGroup.Type);
        Assert.Equal("Global Administrator", updateTenantUserGroup.Role);

        // Check fourth group (Get-TenantUserRole) - permission required
        var getTenantUserRoleGroup = config.GetAuthenticationRequirements("Get-TenantUserRole");
        Assert.NotNull(getTenantUserRoleGroup);
        Assert.Equal(AuthenticationType.Permission, getTenantUserRoleGroup.Type);
        Assert.Equal("poshmcp.read", getTenantUserRoleGroup.Permission);

        var allCommands = config.GetAllCommands();
        Assert.Equal(4, allCommands.Count);
        Assert.Contains("Get-Tenant", allCommands);
        Assert.Contains("Get-TenantUser", allCommands);
        Assert.Contains("Update-TenantUser", allCommands);
        Assert.Contains("Get-TenantUserRole", allCommands);

        _output.WriteLine("User-friendly configuration format parsed successfully");
    }

    [Fact]
    public void ParseCommandsConfiguration_ShouldHandleObjectWithCommandGroups()
    {
        // Arrange - Alternative object format
        var json = """
        {
          "commandGroups": [
            {
              "type": 0,
              "commands": ["Get-Info"]
            },
            {
              "role": "Administrator",
              "commands": ["Set-Config", "Remove-Config"]
            }
          ]
        }
        """;

        // Act
        using var doc = JsonDocument.Parse(json);
        var config = CommandConfigurationParser.ParseCommandsConfiguration(doc.RootElement);

        // Assert
        Assert.NotNull(config);
        Assert.Equal(2, config.CommandGroups.Count);

        var getInfoAuth = config.GetAuthenticationRequirements("Get-Info");
        Assert.NotNull(getInfoAuth);
        Assert.Equal(AuthenticationType.None, getInfoAuth.Type);

        var setConfigAuth = config.GetAuthenticationRequirements("Set-Config");
        Assert.NotNull(setConfigAuth);
        Assert.Equal(AuthenticationType.Role, setConfigAuth.Type);
        Assert.Equal("Administrator", setConfigAuth.Role);

        var removeConfigAuth = config.GetAuthenticationRequirements("Remove-Config");
        Assert.NotNull(removeConfigAuth);
        Assert.Equal(AuthenticationType.Role, removeConfigAuth.Type);
        Assert.Equal("Administrator", removeConfigAuth.Role);

        _output.WriteLine("Object with commandGroups format parsed successfully");
    }

    [Fact]
    public void ParseCommandsConfiguration_ShouldHandleEmptyArray()
    {
        // Arrange
        var json = "[]";

        // Act
        using var doc = JsonDocument.Parse(json);
        var config = CommandConfigurationParser.ParseCommandsConfiguration(doc.RootElement);

        // Assert
        Assert.NotNull(config);
        Assert.Empty(config.CommandGroups);
        Assert.Empty(config.GetAllCommands());

        _output.WriteLine("Empty array handled correctly");
    }

    [Fact]
    public void ParseCommandsConfiguration_ShouldHandleMixedStringAndObjectArray()
    {
        // Arrange
        var json = """
        [
          "Simple-Command",
          {
            "permission": "read.data",
            "commands": ["Complex-Command"]
          },
          "Another-Simple-Command"
        ]
        """;

        // Act
        using var doc = JsonDocument.Parse(json);
        var config = CommandConfigurationParser.ParseCommandsConfiguration(doc.RootElement);

        // Assert
        Assert.NotNull(config);
        Assert.Equal(3, config.CommandGroups.Count);

        var simpleAuth = config.GetAuthenticationRequirements("Simple-Command");
        Assert.NotNull(simpleAuth);
        Assert.Equal(AuthenticationType.None, simpleAuth.Type);

        var complexAuth = config.GetAuthenticationRequirements("Complex-Command");
        Assert.NotNull(complexAuth);
        Assert.Equal(AuthenticationType.Permission, complexAuth.Type);
        Assert.Equal("read.data", complexAuth.Permission);

        var anotherSimpleAuth = config.GetAuthenticationRequirements("Another-Simple-Command");
        Assert.NotNull(anotherSimpleAuth);
        Assert.Equal(AuthenticationType.None, anotherSimpleAuth.Type);

        _output.WriteLine("Mixed string and object array parsed successfully");
    }

    [Fact]
    public void AuthenticationAwareConfigurationConverter_ShouldWork()
    {
        // Arrange
        var json = """
        {
          "commands": [
            "Get-Process",
            {
              "role": "Admin",
              "commands": ["Stop-Process"]
            }
          ]
        }
        """;

        // Act
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        var config = JsonSerializer.Deserialize<PowerShellConfiguration>(json, options);

        // Assert
        Assert.NotNull(config);
        Assert.NotNull(config.Commands);
        Assert.Equal(2, config.Commands.CommandGroups.Count);

        var processAuth = config.GetAuthenticationRequirements("Get-Process");
        Assert.NotNull(processAuth);
        Assert.Equal(AuthenticationType.None, processAuth.Type);

        var stopProcessAuth = config.GetAuthenticationRequirements("Stop-Process");
        Assert.NotNull(stopProcessAuth);
        Assert.Equal(AuthenticationType.Role, stopProcessAuth.Type);
        Assert.Equal("Admin", stopProcessAuth.Role);

        _output.WriteLine("JsonConverter works correctly with PowerShellConfiguration");
    }
}