using PoshMcp.PowerShell;
using System;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Functional;

/// <summary>
/// Tests for generated method execution
/// </summary>
public class MethodExecutionTests : PowerShellTestBase
{
    public MethodExecutionTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task GeneratedMethod_ShouldBeCallableDirectly()
    {
        // Arrange
        Logger.LogInformation("=== Starting GeneratedMethod_ShouldBeCallableDirectly Test ===");

        // Define the PowerShell function with explicit parameter positions
        var functionDefinition = @"
function Get-SomeOtherData {
    [CmdletBinding()]
    param(
        
        [Parameter(Position=0)]
        [string]$Name = 'DefaultUser',
        
        [Parameter(Position=1)]
        [int]$Count = 1
    )
    
    Write-Host ""Function called with parameters:""
    Write-Host ""Parameter 0: Name = $Name (Type: $($Name.GetType().Name))""
    Write-Host ""Parameter 1: Count = $Count (Type: $($Count.GetType().Name))""
    
    for ($i = 1; $i -le $Count; $i++) {
        ""Data item $i for $Name""
    }
}";

        var powerShell = PowerShellRunspace.Instance;
        powerShell.Commands.Clear();
        powerShell.AddScript(functionDefinition);
        SafeInvokePowerShell(powerShell, "setting up function definition for method execution test");
        powerShell.Commands.Clear();

        // Get the function command info
        powerShell.AddCommand("Get-Command").AddParameter("Name", "Get-SomeOtherData");
        var commandResults = SafeInvokePowerShell(powerShell, "getting command info");
        var commands = commandResults.Select(p => p.BaseObject).Cast<CommandInfo>().ToList();
        powerShell.Commands.Clear();

        Logger.LogInformation($"Found {commands.Count} commands");

        // Generate assembly
        var assembly = AssemblyGenerator.GenerateAssembly(commands, Logger);
        var instance = AssemblyGenerator.GetGeneratedInstance(Logger);
        var methods = AssemblyGenerator.GetGeneratedMethods();

        Assert.Contains("get_some_other_data", methods.Keys);

        var method = methods["get_some_other_data"];
        var parameters = method.GetParameters();

        Logger.LogInformation($"Method '{method.Name}' has {parameters.Length} parameters:");
        for (int i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];
            Logger.LogInformation($"  [{i}] {param.ParameterType.Name} {param.Name}");
        }

        // Act - Call the generated method directly with typed parameters
        var name = "TestUser";
        var count = 3;
        var cancellationToken = CancellationToken.None;

        Logger.LogInformation($"Invoking method with parameters: Name='{name}', Count={count}");

        var result = method.Invoke(instance, new object[] { name, count, cancellationToken });

        // Assert
        Assert.NotNull(result);

        var taskResult = (Task<string>)result;
        var output = await taskResult;
        var objectResult = ConvertJsonToObjects(output);
        Logger.LogInformation($"Method execution result: {output}");

        Assert.NotNull(objectResult);
        Assert.DoesNotContain("Command completed with errors", objectResult);

        if (objectResult is Array array)
        {
            Assert.Equal(3, array.Length);
        }
        else
        {
            Assert.Fail("Expected result to be an array");
        }

        Assert.Contains("TestUser", (string)objectResult[0]);
        Assert.Contains("Data item 1 for TestUser", (string)objectResult[0]);
        Assert.Contains("Data item 2 for TestUser", (string)objectResult[1]);
        Assert.Contains("Data item 3 for TestUser", (string)objectResult[2]);
    }
}
