using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PoshMcp.Server.PowerShell;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Integration;

/// <summary>
/// Integration tests for command authentication configuration
/// </summary>
public class CommandAuthenticationIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public CommandAuthenticationIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Configuration_ShouldParseAuthenticationAwareCommands()
    {
        // Arrange - JSON configuration matching the desired YAML structure
        var configJson = """
        {
          "PowerShellConfiguration": {
            "FunctionNames": [
              "Get-Process",
              "Get-Service"
            ],
            "commands": {
              "commandGroups": [
                {
                  "type": 0,
                  "commands": ["Get-Tenant", "Get-TenantUser"]
                },
                {
                  "type": 1,
                  "role": "Global Administrator",
                  "commands": ["Update-TenantUser"]
                },
                {
                  "type": 2,
                  "permission": "poshmcp.read",
                  "commands": ["Get-TenantUserRole"]
                }
              ]
            }
          }
        }
        """;

        var configDict = JsonSerializer.Deserialize<Dictionary<string, object>>(configJson);
        var inMemoryConfig = new Dictionary<string, string?>();
        
        // Flatten the JSON for in-memory configuration
        inMemoryConfig["PowerShellConfiguration:FunctionNames:0"] = "Get-Process";
        inMemoryConfig["PowerShellConfiguration:FunctionNames:1"] = "Get-Service";
        inMemoryConfig["PowerShellConfiguration:commands:commandGroups:0:type"] = "0";
        inMemoryConfig["PowerShellConfiguration:commands:commandGroups:0:commands:0"] = "Get-Tenant";
        inMemoryConfig["PowerShellConfiguration:commands:commandGroups:0:commands:1"] = "Get-TenantUser";
        inMemoryConfig["PowerShellConfiguration:commands:commandGroups:1:type"] = "1";
        inMemoryConfig["PowerShellConfiguration:commands:commandGroups:1:role"] = "Global Administrator";
        inMemoryConfig["PowerShellConfiguration:commands:commandGroups:1:commands:0"] = "Update-TenantUser";
        inMemoryConfig["PowerShellConfiguration:commands:commandGroups:2:type"] = "2";
        inMemoryConfig["PowerShellConfiguration:commands:commandGroups:2:permission"] = "poshmcp.read";
        inMemoryConfig["PowerShellConfiguration:commands:commandGroups:2:commands:0"] = "Get-TenantUserRole";

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemoryConfig)
            .Build();

        var services = new ServiceCollection();
        services.Configure<PowerShellConfiguration>(
            configuration.GetSection("PowerShellConfiguration"));

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var configOptions = serviceProvider.GetRequiredService<IOptions<PowerShellConfiguration>>();
        var config = configOptions.Value;

        // Assert
        Assert.NotNull(config);
        
        // Verify legacy function names still work
        Assert.Equal(2, config.FunctionNames.Count);
        Assert.Contains("Get-Process", config.FunctionNames);
        Assert.Contains("Get-Service", config.FunctionNames);

        // Verify authentication-aware configuration
        Assert.NotNull(config.Commands);
        Assert.Equal(3, config.Commands.CommandGroups.Count);

        // Verify all commands are accessible
        var allCommands = config.GetAllFunctionNames();
        Assert.Equal(6, allCommands.Count); // 2 legacy + 4 from groups
        Assert.Contains("Get-Process", allCommands);
        Assert.Contains("Get-Service", allCommands);
        Assert.Contains("Get-Tenant", allCommands);
        Assert.Contains("Get-TenantUser", allCommands);
        Assert.Contains("Update-TenantUser", allCommands);
        Assert.Contains("Get-TenantUserRole", allCommands);

        // Verify authentication requirements
        var legacyAuth = config.GetAuthenticationRequirements("Get-Process");
        Assert.NotNull(legacyAuth);
        Assert.Equal(AuthenticationType.None, legacyAuth.Type);

        var noAuthCommand = config.GetAuthenticationRequirements("Get-Tenant");
        Assert.NotNull(noAuthCommand);
        Assert.Equal(AuthenticationType.None, noAuthCommand.Type);

        var roleCommand = config.GetAuthenticationRequirements("Update-TenantUser");
        Assert.NotNull(roleCommand);
        Assert.Equal(AuthenticationType.Role, roleCommand.Type);
        Assert.Equal("Global Administrator", roleCommand.Role);

        var permissionCommand = config.GetAuthenticationRequirements("Get-TenantUserRole");
        Assert.NotNull(permissionCommand);
        Assert.Equal(AuthenticationType.Permission, permissionCommand.Type);
        Assert.Equal("poshmcp.read", permissionCommand.Permission);

        _output.WriteLine("Configuration parsed successfully with authentication requirements");
        _output.WriteLine($"Total commands: {allCommands.Count}");
        _output.WriteLine($"Legacy commands (no auth): {config.FunctionNames.Count}");
        _output.WriteLine($"Authentication groups: {config.Commands.CommandGroups.Count}");
    }

    [Fact]  
    public void Configuration_ShouldWorkWithOnlyLegacyFormat()
    {
        // Arrange - Configuration with only legacy FunctionNames
        var inMemoryConfig = new Dictionary<string, string?>
        {
            ["PowerShellConfiguration:FunctionNames:0"] = "Get-Process",
            ["PowerShellConfiguration:FunctionNames:1"] = "Get-Service"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemoryConfig)
            .Build();

        var services = new ServiceCollection();
        services.Configure<PowerShellConfiguration>(
            configuration.GetSection("PowerShellConfiguration"));

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var configOptions = serviceProvider.GetRequiredService<IOptions<PowerShellConfiguration>>();
        var config = configOptions.Value;

        // Assert
        Assert.NotNull(config);
        Assert.Equal(2, config.FunctionNames.Count);
        Assert.Null(config.Commands); // Should be null when not configured

        var allCommands = config.GetAllFunctionNames();
        Assert.Equal(2, allCommands.Count);
        Assert.Contains("Get-Process", allCommands);
        Assert.Contains("Get-Service", allCommands);

        // All legacy commands should have no authentication requirements
        var processAuth = config.GetAuthenticationRequirements("Get-Process");
        Assert.NotNull(processAuth);
        Assert.Equal(AuthenticationType.None, processAuth.Type);

        var serviceAuth = config.GetAuthenticationRequirements("Get-Service");
        Assert.NotNull(serviceAuth);
        Assert.Equal(AuthenticationType.None, serviceAuth.Type);

        _output.WriteLine("Legacy configuration format works correctly");
    }

    [Fact]
    public void Configuration_ShouldWorkWithOnlyNewFormat()
    {
        // Arrange - Configuration with only new authentication-aware format
        var inMemoryConfig = new Dictionary<string, string?>
        {
            ["PowerShellConfiguration:commands:commandGroups:0:type"] = "1",
            ["PowerShellConfiguration:commands:commandGroups:0:role"] = "Administrator",
            ["PowerShellConfiguration:commands:commandGroups:0:commands:0"] = "Remove-TenantUser",
            ["PowerShellConfiguration:commands:commandGroups:1:type"] = "2",
            ["PowerShellConfiguration:commands:commandGroups:1:permission"] = "tenant.read",
            ["PowerShellConfiguration:commands:commandGroups:1:commands:0"] = "Get-TenantDetails"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemoryConfig)
            .Build();

        var services = new ServiceCollection();
        services.Configure<PowerShellConfiguration>(
            configuration.GetSection("PowerShellConfiguration"));

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var configOptions = serviceProvider.GetRequiredService<IOptions<PowerShellConfiguration>>();
        var config = configOptions.Value;

        // Assert
        Assert.NotNull(config);
        Assert.Empty(config.FunctionNames); // Should be empty
        Assert.NotNull(config.Commands);
        Assert.Equal(2, config.Commands.CommandGroups.Count);

        var allCommands = config.GetAllFunctionNames();
        Assert.Equal(2, allCommands.Count);
        Assert.Contains("Remove-TenantUser", allCommands);
        Assert.Contains("Get-TenantDetails", allCommands);

        // Verify authentication requirements
        var removeAuth = config.GetAuthenticationRequirements("Remove-TenantUser");
        Assert.NotNull(removeAuth);
        Assert.Equal(AuthenticationType.Role, removeAuth.Type);
        Assert.Equal("Administrator", removeAuth.Role);

        var getAuth = config.GetAuthenticationRequirements("Get-TenantDetails");
        Assert.NotNull(getAuth);
        Assert.Equal(AuthenticationType.Permission, getAuth.Type);
        Assert.Equal("tenant.read", getAuth.Permission);

        _output.WriteLine("New authentication-aware configuration format works correctly");
    }
}