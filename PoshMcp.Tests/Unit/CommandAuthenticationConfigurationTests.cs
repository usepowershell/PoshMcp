using System.Collections.Generic;
using System.Text.Json;
using PoshMcp.Server.PowerShell;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Unit tests for command authentication configuration
/// </summary>
public class CommandAuthenticationConfigurationTests
{
    private readonly ITestOutputHelper _output;

    public CommandAuthenticationConfigurationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void CommandAuthenticationGroup_ShouldSerializeAndDeserialize()
    {
        // Arrange
        var group = new CommandAuthenticationGroup
        {
            Type = AuthenticationType.Role,
            Role = "Global Administrator",
            Commands = new List<string> { "Update-TenantUser", "Remove-TenantUser" }
        };

        // Act
        var json = JsonSerializer.Serialize(group);
        var deserialized = JsonSerializer.Deserialize<CommandAuthenticationGroup>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(AuthenticationType.Role, deserialized.Type);
        Assert.Equal("Global Administrator", deserialized.Role);
        Assert.Equal(2, deserialized.Commands.Count);
        Assert.Contains("Update-TenantUser", deserialized.Commands);
        Assert.Contains("Remove-TenantUser", deserialized.Commands);
        
        _output.WriteLine($"Serialized JSON: {json}");
    }

    [Fact]
    public void AuthenticationAwareConfiguration_GetAllCommands_ShouldReturnAllCommands()
    {
        // Arrange
        var config = new AuthenticationAwareConfiguration
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
        };

        // Act
        var allCommands = config.GetAllCommands();

        // Assert
        Assert.Equal(4, allCommands.Count);
        Assert.Contains("Get-Tenant", allCommands);
        Assert.Contains("Get-TenantUser", allCommands);
        Assert.Contains("Update-TenantUser", allCommands);
        Assert.Contains("Get-TenantUserRole", allCommands);
    }

    [Fact]
    public void AuthenticationAwareConfiguration_GetAuthenticationRequirements_ShouldReturnCorrectGroup()
    {
        // Arrange
        var config = new AuthenticationAwareConfiguration
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
                }
            }
        };

        // Act
        var noneGroup = config.GetAuthenticationRequirements("Get-Tenant");
        var roleGroup = config.GetAuthenticationRequirements("Update-TenantUser");
        var notFound = config.GetAuthenticationRequirements("NonExistentCommand");

        // Assert
        Assert.NotNull(noneGroup);
        Assert.Equal(AuthenticationType.None, noneGroup.Type);
        
        Assert.NotNull(roleGroup);
        Assert.Equal(AuthenticationType.Role, roleGroup.Type);
        Assert.Equal("Global Administrator", roleGroup.Role);
        
        Assert.Null(notFound);
    }

    [Fact]
    public void PowerShellConfiguration_GetAllFunctionNames_ShouldCombineLegacyAndNew()
    {
        // Arrange
        var config = new PowerShellConfiguration
        {
            FunctionNames = new List<string> { "Get-Process", "Get-Service" },
            Commands = new AuthenticationAwareConfiguration
            {
                CommandGroups = new List<CommandAuthenticationGroup>
                {
                    new()
                    {
                        Type = AuthenticationType.Role,
                        Role = "Admin",
                        Commands = new List<string> { "Update-Something", "Get-Process" } // Intentional duplicate
                    }
                }
            }
        };

        // Act
        var allNames = config.GetAllFunctionNames();

        // Assert
        Assert.Equal(3, allNames.Count); // Should deduplicate Get-Process
        Assert.Contains("Get-Process", allNames);
        Assert.Contains("Get-Service", allNames);
        Assert.Contains("Update-Something", allNames);
    }

    [Fact]
    public void PowerShellConfiguration_GetAuthenticationRequirements_ShouldPrioritizeLegacyAsNone()
    {
        // Arrange
        var config = new PowerShellConfiguration
        {
            FunctionNames = new List<string> { "Get-Process" },
            Commands = new AuthenticationAwareConfiguration
            {
                CommandGroups = new List<CommandAuthenticationGroup>
                {
                    new()
                    {
                        Type = AuthenticationType.Role,
                        Role = "Admin",
                        Commands = new List<string> { "Update-Something" }
                    }
                }
            }
        };

        // Act
        var legacyCommandAuth = config.GetAuthenticationRequirements("Get-Process");
        var newCommandAuth = config.GetAuthenticationRequirements("Update-Something");
        var notFoundAuth = config.GetAuthenticationRequirements("NonExistent");

        // Assert
        Assert.NotNull(legacyCommandAuth);
        Assert.Equal(AuthenticationType.None, legacyCommandAuth.Type);
        
        Assert.NotNull(newCommandAuth);
        Assert.Equal(AuthenticationType.Role, newCommandAuth.Type);
        Assert.Equal("Admin", newCommandAuth.Role);
        
        Assert.Null(notFoundAuth);
    }
}