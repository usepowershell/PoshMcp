using PoshMcp.PowerShell;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PoshMcp.Tests.Functional.FilterLastCommandOutput;

/// <summary>
/// Test for filtering cached results
/// </summary>
public partial class FilterCachedResults : PowerShellTestBase
{
    public FilterCachedResults(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task ShouldFilterCachedResults()
    {
        // Arrange - Execute a command that produces known filterable objects
        var parameterInfos = Array.Empty<PowerShellParameterInfo>();
        var parameterValues = Array.Empty<object>();

        // Create test data with objects that have Name properties
        await PowerShellRunspace.ExecuteThreadSafeAsync<object?>(ps =>
        {
            ps.Commands.Clear();
            ps.AddScript(@"
                $testData = @(
                    @{Name='dotnet'; Type='process'},
                    @{Name='apple'; Type='fruit'},
                    @{Name='docker'; Type='tool'},
                    @{Name='banana'; Type='fruit'}
                )
                $global:LastCommandOutput = $testData
            ");
            SafeInvokePowerShell(ps, "setting up test data for filter test");
            ps.Commands.Clear();
            return Task.FromResult<object?>(null);
        });

        // Act - Filter the cached results to only show items with names starting with 'd'
        var filteredOutput = await PowerShellAssemblyGenerator.FilterLastCommandOutput(
            PowerShellRunspace,
            Logger,
            "$_.Name -like 'd*'",
            false, // updateCache
            CancellationToken.None);

        // Assert
        Assert.NotNull(filteredOutput);
        Logger.LogInformation($"Filtered output: {filteredOutput}");

        // Verify it's valid JSON
        Assert.True(filteredOutput.StartsWith("[") || filteredOutput.StartsWith("{"), "Filtered output should be valid JSON");

        // Parse and verify filtering worked
        var jsonArray = JArray.Parse(filteredOutput);

        // Should have 2 items: dotnet and docker
        Assert.Equal(2, jsonArray.Count);

        // Verify both items have names starting with 'd'
        foreach (var item in jsonArray)
        {
            var name = item["Name"]?.ToString();
            Assert.NotNull(name);
            Assert.True(name.StartsWith("d", StringComparison.OrdinalIgnoreCase),
                       $"Item name '{name}' should start with 'd'");
        }

        // Verify we got the expected items
        var names = jsonArray.Select(item => item["Name"]?.ToString()).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "docker", "dotnet" }, names);

        Logger.LogInformation($"Successfully filtered to {jsonArray.Count} items starting with 'd'");
    }
}
