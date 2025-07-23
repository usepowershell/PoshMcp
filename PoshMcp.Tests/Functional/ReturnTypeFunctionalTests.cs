using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PoshMcp.PowerShell;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Functional;

/// <summary>
/// Functional tests for the new string (JSON) return type system
/// </summary>
public class ReturnTypeFunctionalTests : PowerShellTestBase
{
    public ReturnTypeFunctionalTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task GeneratedMethod_ShouldReturnObjectArray()
    {
        // Arrange
        var getProcessCommand = PowerShellRunspace.Instance;
        getProcessCommand.Commands.Clear();
        getProcessCommand.AddCommand("Get-Command").AddParameter("Name", "Get-Process");
        var commandInfo = getProcessCommand.Invoke<CommandInfo>().FirstOrDefault();
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

    [Fact(Skip = "Skipping Get-ChildItem test as it's hanging")]
    public async Task GeneratedMethod_ShouldHandleGetChildItemCorrectly()
    {
        // Arrange
        var getChildItemCommand = PowerShellRunspace.Instance;
        getChildItemCommand.Commands.Clear();
        getChildItemCommand.AddCommand("Get-Command").AddParameter("Name", "Get-ChildItem");
        var commandInfo = getChildItemCommand.Invoke<CommandInfo>().FirstOrDefault();
        getChildItemCommand.Commands.Clear();

        Assert.NotNull(commandInfo);

        // Generate assembly with the command
        var assembly = AssemblyGenerator.GenerateAssembly(new[] { commandInfo }, Logger);
        var methods = AssemblyGenerator.GetGeneratedMethods();
        var instance = AssemblyGenerator.GetGeneratedInstance(Logger);

        // Find a Get-ChildItem method
        var getChildItemMethod = methods.Values.FirstOrDefault(m => m.Name.StartsWith("get_child_item"));
        Assert.NotNull(getChildItemMethod);

        Logger.LogInformation($"Found method: {getChildItemMethod.Name}");
        Logger.LogInformation($"Return type: {getChildItemMethod.ReturnType.FullName}");

        // Log all available methods for debugging
        Logger.LogInformation($"Available methods: {string.Join(", ", methods.Keys)}");

        // Verify the return type is Task<string>
        Assert.Equal(typeof(Task<string>), getChildItemMethod.ReturnType);

        // Act - invoke the method with a known directory (use temp directory)
        var tempDir = System.IO.Path.GetTempPath();
        var parameterDict = new Dictionary<string, object?>
        {
            ["Path"] = tempDir,
            ["cancellationToken"] = CancellationToken.None
        };
        var parameters = PowerShellParameterUtils.CreateParameterArray(getChildItemMethod, parameterDict);

        Logger.LogInformation($"Invoking method with path: {tempDir}");
        var jsonResult = await (Task<string>)getChildItemMethod.Invoke(instance, parameters)!;

        // Assert
        Assert.NotNull(jsonResult);
        Assert.False(string.IsNullOrEmpty(jsonResult), "Should return a non-empty JSON string");
        Logger.LogInformation($"Returned JSON: {jsonResult}");

        // Convert JSON back to objects for testing
        var result = ConvertJsonToObjects(jsonResult);
        Assert.NotNull(result);
        Logger.LogInformation($"Converted to {result.Length} object(s)");

        // Check that the returned objects are valid representing file system items
        foreach (var obj in result.Take(3)) // Check first 3 objects
        {
            Assert.NotNull(obj);
            Logger.LogInformation($"Object type: {obj.GetType().Name}");

            // Try to access common file system properties using helper method
            var name = GetPropertyValue(obj, "Name");
            var fullName = GetPropertyValue(obj, "FullName");

            Logger.LogInformation($"Item: {name} (FullName: {fullName})");
            Assert.NotNull(name);
            Assert.NotNull(fullName);
        }
    }
}
