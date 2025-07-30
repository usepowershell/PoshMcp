using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.PowerShell;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Functional.ReturnType;

/// <summary>
/// Test for generated method return type validation
/// </summary>
public partial class GeneratedMethod : PowerShellTestBase
{
    [Fact]
    public async Task ShouldReturnObjectArray()
    {
        // Arrange
        var getProcessCommand = PowerShellRunspace.Instance;
        getProcessCommand.Commands.Clear();
        getProcessCommand.AddCommand("Get-Command").AddParameter("Name", "Get-Process");
        var safeResults = SafeInvokePowerShell(getProcessCommand, "getting Get-Process command info");
        var commandInfo = safeResults.Select(pso => pso.BaseObject).OfType<CommandInfo>().FirstOrDefault();
        getProcessCommand.Commands.Clear();

        Assert.NotNull(commandInfo);

        // Generate assembly with the command
        var assembly = AssemblyGenerator.GenerateAssembly(new[] { commandInfo }, Logger);
        var methods = AssemblyGenerator.GetGeneratedMethods();
        var instance = AssemblyGenerator.GetGeneratedInstance(Logger);

        // Log all available methods for debugging
        Logger.LogInformation($"Available methods: {string.Join(", ", methods.Keys)}");

        var getProcessMethod = methods.Values.FirstOrDefault(m =>
            m.Name.StartsWith("get_process_id"));
        Assert.NotNull(getProcessMethod);

        Logger.LogInformation($"Found method: {getProcessMethod.Name}");
        Logger.LogInformation($"Return type: {getProcessMethod.ReturnType.FullName}");

        // Verify the return type is Task<string>
        Assert.Equal(typeof(Task<string>), getProcessMethod.ReturnType);

        // Act - invoke the method with current process ID only
        var currentProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;
        var parameterDict = new Dictionary<string, object?>
        {
            ["Id"] = new Int32[] { currentProcessId },
            ["cancellationToken"] = CancellationToken.None
        };
        var parameters = PowerShellParameterUtils.CreateParameterArray(getProcessMethod, parameterDict);

        Logger.LogInformation($"Invoking method with process ID: {currentProcessId}");
        var result = await (Task<string>)getProcessMethod.Invoke(instance, parameters)!;

        // Assert
        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result), "Should return a non-empty JSON string");
        Logger.LogInformation($"Returned JSON: {result}");

        // Verify the result is valid JSON and contains expected process data
        Assert.Contains("ProcessName", result);
        Assert.Contains("Id", result);
        Assert.Contains(currentProcessId.ToString(), result);
    }
}
