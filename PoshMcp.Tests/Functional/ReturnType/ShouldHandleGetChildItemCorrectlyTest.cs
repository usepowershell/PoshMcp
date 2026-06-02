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
    [Fact]
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

        // Select a generated overload that explicitly accepts Path so invocation is deterministic.
        var getChildItemMethod = methods.Values.FirstOrDefault(m =>
            m.Name.StartsWith("get_child_item", StringComparison.Ordinal)
            && m.GetParameters().Any(p => string.Equals(p.Name, "path", StringComparison.OrdinalIgnoreCase)));
        Assert.NotNull(getChildItemMethod);

        Logger.LogInformation($"Found method: {getChildItemMethod.Name}");
        Logger.LogInformation($"Return type: {getChildItemMethod.ReturnType.FullName}");

        // Log all available methods for debugging
        Logger.LogInformation($"Available methods: {string.Join(", ", methods.Keys)}");

        // Verify the return type is Task<string>
        Assert.Equal(typeof(Task<string>), getChildItemMethod.ReturnType);

        // Act - invoke against a tiny, dedicated temp directory to avoid expensive enumeration.
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"poshmcp-gci-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(tempDir);
        var probeFileName = "probe.txt";
        var probeFilePath = System.IO.Path.Combine(tempDir, probeFileName);
        await System.IO.File.WriteAllTextAsync(probeFilePath, "probe");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        try
        {
            var parameterDict = new Dictionary<string, object?>
            {
                ["Path"] = tempDir,
                ["cancellationToken"] = cts.Token
            };
            var parameters = PowerShellParameterUtils.CreateParameterArray(getChildItemMethod, parameterDict);

            Logger.LogInformation($"Invoking method with path: {tempDir}");
            var jsonResult = await ((Task<string>)getChildItemMethod.Invoke(instance, parameters)!).WaitAsync(cts.Token);

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
                var length = GetPropertyValue(obj, "Length");

                Logger.LogInformation($"Item: {name} (Length: {length})");
                Assert.NotNull(name);
                Assert.NotNull(length);
            }

            Assert.Contains(probeFileName, jsonResult, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                System.IO.Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }
}
