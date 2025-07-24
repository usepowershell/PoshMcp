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
/// Test for generated instance validation
/// </summary>
public partial class UtilityMethods : PowerShellTestBase
{
    [Fact]
    public void GetGeneratedInstance_ShouldReturnValidInstance()
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
        var instance = AssemblyGenerator.GetGeneratedInstance(Logger);

        // Assert
        Assert.NotNull(instance);

        var instanceType = instance.GetType();
        Logger.LogInformation($"Instance type: {instanceType.Name}");
        Logger.LogInformation($"Instance namespace: {instanceType.Namespace}");

        // Verify instance has expected methods
        var methods = instanceType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.DeclaringType == instanceType && m.Name != ".ctor")
            .ToList();

        Assert.NotEmpty(methods);
        Logger.LogInformation($"Instance has {methods.Count} public methods");
    }
}
