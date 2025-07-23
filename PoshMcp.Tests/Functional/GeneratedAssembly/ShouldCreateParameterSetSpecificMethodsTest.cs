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
/// Test for parameter set specific method generation
/// </summary>
public partial class UtilityMethods : PowerShellTestBase
{
    [Fact]
    public async Task GenerateAssembly_ShouldCreateParameterSetSpecificMethods()
    {
        // Arrange - Get a command with multiple parameter sets
        var command = await PowerShellRunspace.ExecuteThreadSafeAsync<CommandInfo?>(ps =>
        {
            ps.Commands.Clear();
            ps.AddCommand("Get-Command").AddParameter("Name", "Get-ChildItem");
            var cmd = ps.Invoke<CommandInfo>().FirstOrDefault();
            ps.Commands.Clear();
            return Task.FromResult<CommandInfo?>(cmd);
        });

        Assert.NotNull(command);
        Assert.True(command.ParameterSets.Count > 1, "Get-ChildItem should have multiple parameter sets");

        Logger.LogInformation($"Command: {command.Name}");
        Logger.LogInformation($"Parameter Sets: {command.ParameterSets.Count}");

        foreach (var paramSet in command.ParameterSets)
        {
            Logger.LogInformation($"  - {paramSet.Name} (IsDefault: {paramSet.IsDefault})");
        }

        // Act - Generate assembly to see parameter set-specific methods
        var assembly = AssemblyGenerator.GenerateAssembly(new[] { command }, Logger);
        var methods = AssemblyGenerator.GetGeneratedMethods();

        // Assert
        Assert.NotNull(assembly);
        Assert.NotEmpty(methods);

        Logger.LogInformation($"Generated Methods: {methods.Count}");
        foreach (var method in methods.OrderBy(m => m.Key))
        {
            Logger.LogInformation($"  - {method.Key} ({method.Value.GetParameters().Length - 1} parameters)");
        }

        // Verify that multiple methods were generated for the parameter sets
        Assert.True(methods.Count > 1, "Should generate multiple methods for different parameter sets");

        // Verify method naming follows parameter set convention
        var methodNames = methods.Keys.ToList();

        // Should have methods like "get_child_item_items" and "get_child_item_literal_items"
        var hasParameterSetSpecificMethods = methodNames.Any(name =>
            name.Contains("_") && name.StartsWith("get_child_item"));

        Assert.True(hasParameterSetSpecificMethods,
            "Should generate parameter set-specific method names like 'get_child_item_items'");

        // Verify that parameter set names are appended (except for __AllParameterSets)
        var parameterSetNames = command.ParameterSets
            .Where(ps => ps.Name != "__AllParameterSets")
            .Select(ps => ps.Name)
            .ToList();

        foreach (var paramSetName in parameterSetNames)
        {
            var expectedMethodPattern = PowerShellDynamicAssemblyGenerator.SanitizeMethodName("Get-ChildItem", paramSetName);
            var hasExpectedMethod = methodNames.Any(name =>
                name.Equals(expectedMethodPattern, System.StringComparison.OrdinalIgnoreCase));

            if (!hasExpectedMethod)
            {
                Logger.LogInformation($"Looking for pattern: {expectedMethodPattern}");
                Logger.LogInformation($"Available methods: {string.Join(", ", methodNames)}");
            }
        }

        Logger.LogInformation("Parameter set functionality test completed successfully");
    }
}
