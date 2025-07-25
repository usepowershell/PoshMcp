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
/// Test for assembly generation validation
/// </summary>
public partial class GeneratedInstance : PowerShellTestBase
{
    [Fact]
    public void ShouldCreateValidAssembly()
    {
        // Arrange - Setup test PowerShell function
        SetupTestPowerShellFunction();
        var commands = GetTestCommands();

        // Act
        var assembly = AssemblyGenerator.GenerateAssembly(commands, Logger);

        // Assert
        Assert.NotNull(assembly);
        Assert.True(assembly.IsDynamic);
        Assert.NotEmpty(assembly.GetTypes());

        Logger.LogInformation($"Generated assembly: {assembly.FullName}");
        Logger.LogInformation($"Assembly is dynamic: {assembly.IsDynamic}");
    }
}
