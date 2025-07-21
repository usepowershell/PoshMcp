using PoshMcp.PowerShell;
using System;
using System.Linq;
using System.Management.Automation;
using System.Threading;
using System.Threading.Tasks;
using Json.More;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests;

/// <summary>
/// Integration tests for the complete PowerShell dynamic assembly workflow
/// </summary>
public class IntegrationTests : PowerShellTestBase
{
    public IntegrationTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task CreateLoadAndExecute_DynamicAssemblyWithPowerShellFunction_ShouldSucceed()
    {
        // Arrange
        Logger.LogInformation("=== Starting Integration Test ===");

        // Define a simple PowerShell function
        var functionDefinition = @"
function Get-SomeThing {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$false)]
        [string]$Message = 'Hello from PowerShell!'
    )
    
    return ""Function executed: $Message""
}";

        var command = PowerShellRunspace.ExecuteThreadSafe(ps =>
        {
            ps.Commands.Clear();
            ps.AddScript(functionDefinition);
            var defineResult = ps.Invoke();
            ps.Commands.Clear();

            if (ps.HadErrors)
            {
                var errors = ps.Streams.Error.ReadAll();
                Logger.LogError("Error defining function: {Errors}", string.Join("; ", errors));
                throw new InvalidOperationException("Failed to define PowerShell function");
            }

            Logger.LogInformation("PowerShell function defined successfully");

            // Get the function command info
            ps.AddCommand("Get-Command").AddParameter("Name", "Get-SomeThing");
            var commands = ps.Invoke<CommandInfo>();
            ps.Commands.Clear();

            if (!commands.Any())
            {
                throw new InvalidOperationException("Get-SomeThing function not found");
            }

            return commands.First();
        });

        Logger.LogInformation($"Found command: {command.Name} with {command.Parameters.Count} parameters");

        // Act - Generate assembly with dynamic method
        var assembly = AssemblyGenerator.GenerateAssembly(new[] { command }, Logger);
        var instance = AssemblyGenerator.GetGeneratedInstance(Logger);
        var methods = AssemblyGenerator.GetGeneratedMethods();

        // Verify method was generated
        Assert.Contains("get_some_thing", methods.Keys);
        var method = methods["get_some_thing"];


        Logger.LogInformation($"Generated method: {method.Name}");

        // Execute the generated method
        var parameters = method.GetParameters();
        var args = new object[parameters.Length];

        // Log all parameter information for debugging
        Logger.LogInformation($"Method has {parameters.Length} parameters:");
        for (int i = 0; i < parameters.Length; i++)
        {
            Logger.LogInformation($"  Parameter {i}: {parameters[i].Name} ({parameters[i].ParameterType.Name})");
        }

        // Set the Message parameter (find by name, case-insensitive)
        for (int i = 0; i < parameters.Length; i++)
        {
            if (string.Equals(parameters[i].Name, "message", StringComparison.OrdinalIgnoreCase))
            {
                args[i] = "Hello from integration test!";
                Logger.LogInformation($"Set parameter '{parameters[i].Name}' = 'Hello from integration test!'");
                break;
            }
        }

        // Set CancellationToken (usually the last parameter)
        if (parameters.Length > 0 && parameters.Last().ParameterType == typeof(CancellationToken))
        {
            args[parameters.Length - 1] = CancellationToken.None;
        }

        var result = method.Invoke(instance, args);


        // Assert
        Assert.NotNull(result);

        var taskResult = (Task<string>)result;
        var output = ConvertJsonToObjects(await taskResult);

        Assert.NotNull(output);
        Assert.Contains("Function executed", (string)output[0]);
        Assert.Contains("Hello from integration test!", (string)output[0]);

        Logger.LogInformation($"Integration test completed successfully. Output: {output}");
    }

    [Fact]
    public void Test_AssemblyIsolation_BetweenTests()
    {
        // This test ensures that our test isolation is working correctly

        // Arrange
        Logger.LogInformation("Testing assembly isolation between tests");

        // Clear cache explicitly
        AssemblyGenerator.ClearCache();

        // Try to get methods without generating assembly first - should throw
        var exception = Assert.Throws<InvalidOperationException>(() =>
            AssemblyGenerator.GetGeneratedMethods());

        Assert.Contains("Assembly has not been generated yet", exception.Message);
        Logger.LogInformation("Assembly isolation test passed - no cached assembly found");
    }
}
