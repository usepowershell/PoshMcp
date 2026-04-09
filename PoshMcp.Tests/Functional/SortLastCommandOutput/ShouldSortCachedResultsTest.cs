using PoshMcp.Server.PowerShell;
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

namespace PoshMcp.Tests.Functional.SortLastCommand;

/// <summary>
/// Test for sorting cached results
/// </summary>
[Collection("CachingStateTests")]
public class Output_Test : PowerShellTestBase
{
    public Output_Test(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task ShouldSortCachedResults()
    {
        var runtimeCachingState = new RuntimeCachingState();
        runtimeCachingState.SetGlobalOverride(true);
        PowerShellAssemblyGenerator.SetRuntimeCachingState(runtimeCachingState);

        try
        {
        // Arrange - Execute a simpler command that produces known sortable output
        var parameterInfos = new PowerShellParameterInfo[0];
        var parameterValues = new object[0];

        // Use Write-Output with known values instead of Get-Process
        await PowerShellAssemblyGenerator.ExecutePowerShellCommandTyped(
            "Write-Output",
            new[] { new PowerShellParameterInfo("InputObject", typeof(object[]), false) },
            new object[] { new object[] { "zebra", "apple", "banana", "cherry" } },
            CancellationToken.None,
            PowerShellRunspace,
            Logger);

        // Act - Sort the cached results (no property needed for simple strings)
        var sortedOutput = await PowerShellAssemblyGenerator.SortLastCommandOutput(
            PowerShellRunspace,
            Logger,
            null, // No property for simple string sorting
            false,
            CancellationToken.None);

        // Act 2 - Sort the cached results in descending order
        var sortedDescendingOutput = await PowerShellAssemblyGenerator.SortLastCommandOutput(
            PowerShellRunspace,
            Logger,
            null, // No property for simple string sorting
            true,
            CancellationToken.None);

        // Assert
        Assert.NotNull(sortedOutput);
        Assert.NotNull(sortedDescendingOutput);

        // Parse JSON - these should be arrays of strings
        var sortedJson = JArray.Parse(sortedOutput);
        var sortedDescendingJson = JArray.Parse(sortedDescendingOutput);

        // Verify we have the expected number of items
        Assert.Equal(4, sortedJson.Count);
        Assert.Equal(4, sortedDescendingJson.Count);

        // Convert to string arrays for easier verification
        var sortedStrings = sortedJson.Select(t => t.ToString()).ToArray();
        var sortedDescendingStrings = sortedDescendingJson.Select(t => t.ToString()).ToArray();

        // Verify ascending sort order
        var expectedAscending = new[] { "apple", "banana", "cherry", "zebra" };
        Assert.Equal(expectedAscending, sortedStrings);

        // Verify descending sort order
        var expectedDescending = new[] { "zebra", "cherry", "banana", "apple" };
        Assert.Equal(expectedDescending, sortedDescendingStrings);

        // The outputs should be different (ascending vs descending)
        Assert.NotEqual(sortedOutput, sortedDescendingOutput);

        Logger.LogInformation($"Successfully sorted strings in both directions");
        }
        finally
        {
            PowerShellAssemblyGenerator.SetRuntimeCachingState(new RuntimeCachingState());
        }
    }
}
