using PoshMcp.PowerShell;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Functional.GeneratedAssembly;

/// <summary>
/// Test for generated methods validation
/// </summary>
public partial class UtilityMethods : PowerShellTestBase
{
    [Fact]
    public void GetGeneratedMethods_ShouldReturnMethodsForCommands()
    {
        // Arrange - Setup test PowerShell function
        SetupTestPowerShellFunction();
        var commands = GetTestCommands();

        // Verify we have commands to work with
        Assert.NotEmpty(commands);
        Logger.LogInformation($"Testing with {commands.Count} commands");

        // Generate assembly first
        AssemblyGenerator.GenerateAssembly(commands, Logger);

        // Act
        var methods = AssemblyGenerator.GetGeneratedMethods();

        // Assert
        Assert.NotNull(methods);
        Assert.NotEmpty(methods);

        foreach (var kvp in methods)
        {
            var methodName = kvp.Key;
            var method = kvp.Value;

            Logger.LogInformation($"Method: {methodName}");
            Logger.LogInformation($"  Return type: {method.ReturnType.Name}");
            Logger.LogInformation($"  Parameters: {method.GetParameters().Length}");

            // Verify method signature
            Assert.Equal(typeof(System.Threading.Tasks.Task<string>), method.ReturnType);
            Assert.True(method.GetParameters().Length > 0); // Should have at least CancellationToken

            // Verify last parameter is CancellationToken
            var lastParam = method.GetParameters().Last();
            Assert.Equal(typeof(System.Threading.CancellationToken), lastParam.ParameterType);
            Assert.Equal("cancellationToken", lastParam.Name);
        }
    }
}
