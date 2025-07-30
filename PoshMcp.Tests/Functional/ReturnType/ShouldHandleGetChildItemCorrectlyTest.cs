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
/// Test for Get-ChildItem handling (currently skipped)
/// </summary>
public partial class GeneratedMethod : PowerShellTestBase
{
    [Fact(Skip = "Skipping Get-ChildItem test as it's hanging")]
    public async Task ShouldHandleGetChildItemCorrectly()
    {
        // Arrange
        var getChildItemCommand = PowerShellRunspace.Instance;
        getChildItemCommand.Commands.Clear();
        getChildItemCommand.AddCommand("Get-Command").AddParameter("Name", "Get-ChildItem");
        var safeResults = SafeInvokePowerShell(getChildItemCommand, "getting Get-ChildItem command info");
        var commandInfo = safeResults.Select(pso => pso.BaseObject).OfType<CommandInfo>().FirstOrDefault();
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
