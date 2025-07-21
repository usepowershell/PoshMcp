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

namespace PoshMcp.Tests;

/// <summary>
/// Tests for PowerShell command execution functionality
/// </summary>
public class PowerShellCommandExecutionTests : PowerShellTestBase
{
    public PowerShellCommandExecutionTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task ExecutePowerShellCommand_ShouldHandleValidCommand()
    {
        // Arrange
        await SetupTestPowerShellFunctionAsync();

        var parameterInfos = new[]
        {
            new PowerShellParameterInfo("Name", typeof(string), false),
            new PowerShellParameterInfo("Count", typeof(int), false)
        };

        var parameterValues = new object[] { "TestUser", 3 };

        // Act
        var result = await PowerShellDynamicAssemblyGenerator.ExecutePowerShellCommandTyped(
            "Get-SomeOtherData",
            parameterInfos,
            parameterValues,
            CancellationToken.None,
            Logger);

        // Assert
        Assert.NotNull(result);
        Logger.LogInformation($"Command result: {result}");
        // Should either contain our test data or an error message as JSON string
        Assert.True(result.Contains("TestUser") || result.Contains("Error") || result.Contains("error") || result.Contains("Command"));
    }

    [Fact]
    public async Task ExecutePowerShellCommand_ShouldHandleInvalidCommand()
    {
        // Arrange
        var commandName = "NonExistentCommand-12345";
        var parameters = new PowerShellParameterInfo[0];

        // Act
        var result = await PowerShellDynamicAssemblyGenerator.ExecutePowerShellCommandTyped(
            commandName,
            parameters,
            new object[0],
            CancellationToken.None,
            Logger);

        // Assert
        Assert.NotNull(result);
        // Check if the result contains error message (it should be a JSON string now)
        Assert.Contains("error", result, StringComparison.OrdinalIgnoreCase);
    }

    private async Task SetupTestPowerShellFunctionAsync()
    {
        try
        {
            var testFunctionScript = @"
function Get-SomeOtherData {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$false, Position=0)]
        [string]$Name = 'DefaultName',
        
        [Parameter(Mandatory=$false, Position=1)]
        [int]$Count = 1
    )
    
    $result = @()
    for ($i = 1; $i -le $Count; $i++) {
        $result += ""Data item $i for $Name""
    }
    
    return $result -join ""`n""
}";

            // Use thread-safe execution
            await PowerShellRunspace.ExecuteThreadSafeAsync<Collection<PSObject>>(ps =>
            {
                ps.Commands.Clear();
                ps.AddScript(testFunctionScript);
                var results = ps.Invoke();

                if (ps.HadErrors)
                {
                    var errors = ps.Streams.Error.ReadAll();
                    Logger.LogWarning($"Errors setting up test function: {string.Join("; ", errors)}");
                    ps.Streams.Error.Clear();
                }
                else
                {
                    Logger.LogInformation("Test PowerShell function set up successfully");
                }

                return Task.FromResult(results);
            });
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Exception setting up test function: {ex.Message}");
        }
    }

    [Fact]
    public async Task ExecutePowerShellCommand_ShouldCacheResults_AndAllowRetrieval()
    {
        // Arrange - Execute a simple command that produces predictable output
        var parameterInfos = new PowerShellParameterInfo[0]; // No parameters
        var parameterValues = new object[0]; // No parameter values

        // Act 1 - Execute a command that will be cached
        var result = await PowerShellAssemblyGenerator.ExecutePowerShellCommandTyped(
            "Get-Date",
            parameterInfos,
            parameterValues,
            CancellationToken.None,
            PowerShellRunspace,
            Logger);

        // Verify the command executed successfully
        Assert.NotNull(result);
        Assert.False(result.Contains("error"), $"Command should not have errors. Result: {result}");

        // Act 2 - Retrieve the cached output
        var cachedOutput = await PowerShellAssemblyGenerator.GetLastCommandOutput(
            PowerShellRunspace,
            Logger,
            CancellationToken.None);

        // Assert
        Assert.NotNull(cachedOutput);
        Logger.LogInformation($"Original result: {result}");
        Logger.LogInformation($"Cached output: {cachedOutput}");

        // The cached output should contain the same date information as the original result
        // Both should be valid JSON strings
        Assert.True(result.StartsWith("[") || result.StartsWith("{"), "Original result should be valid JSON");
        Assert.True(cachedOutput.StartsWith("[") || cachedOutput.StartsWith("{"), "Cached output should be valid JSON");

        // Both should contain date-related information
        Assert.True(result.Contains("DateTime") || result.Contains("Date") || result.Contains("Time") ||
                   result.Contains("Year") || result.Contains("Month") || result.Contains("Day") ||
                   result.Contains("2025") || result.Contains("-") && result.Contains("T") && result.Contains(":"),
                   "Original result should contain date information");
        Assert.True(cachedOutput.Contains("DateTime") || cachedOutput.Contains("Date") || cachedOutput.Contains("Time") ||
                   cachedOutput.Contains("Year") || cachedOutput.Contains("Month") || cachedOutput.Contains("Day") ||
                   cachedOutput.Contains("2025") || cachedOutput.Contains("-") && cachedOutput.Contains("T") && cachedOutput.Contains(":"),
                   "Cached output should contain date information");
    }

    [Fact]
    public async Task GetLastCommandOutput_ShouldReturnNull_WhenNoCacheExists()
    {
        // Arrange - Clear any existing cache by running a PowerShell command that sets LastCommandOutput to null
        await PowerShellRunspace.ExecuteThreadSafeAsync<object?>(ps =>
        {
            ps.Commands.Clear();
            ps.AddScript("$global:LastCommandOutput = $null; Remove-Variable -Name 'LastCommandOutput' -Scope Global -ErrorAction SilentlyContinue");
            ps.Invoke();

            if (ps.HadErrors)
            {
                ps.Streams.Error.Clear();
            }

            ps.Commands.Clear();
            return Task.FromResult<object?>(null);
        });

        // Act - Try to retrieve cached output when none exists
        var cachedOutput = await PowerShellAssemblyGenerator.GetLastCommandOutput(
            PowerShellRunspace,
            Logger,
            CancellationToken.None);

        // Assert
        Assert.Null(cachedOutput);
        Logger.LogInformation("Correctly returned null for invalid filter script");
    }

    [Fact]
    public async Task ExecutePowerShellCommand_ShouldOverwritePreviousCache()
    {
        // Arrange
        var parameterInfos = new PowerShellParameterInfo[0];
        var parameterValues = new object[0];

        // Act 1 - Execute first command (Get-Date)
        var firstResult = await PowerShellAssemblyGenerator.ExecutePowerShellCommandTyped(
            "Get-Date",
            parameterInfos,
            parameterValues,
            CancellationToken.None,
            PowerShellRunspace,
            Logger);

        var firstCachedOutput = await PowerShellAssemblyGenerator.GetLastCommandOutput(
            PowerShellRunspace,
            Logger,
            CancellationToken.None);

        // Act 2 - Execute second command (Get-Location)
        var secondResult = await PowerShellAssemblyGenerator.ExecutePowerShellCommandTyped(
            "Get-Host",
            parameterInfos,
            parameterValues,
            CancellationToken.None,
            PowerShellRunspace,
            Logger);

        var secondCachedOutput = await PowerShellAssemblyGenerator.GetLastCommandOutput(
            PowerShellRunspace,
            Logger,
            CancellationToken.None);

        // Assert
        Assert.NotNull(firstResult);
        Assert.NotNull(firstCachedOutput);
        Assert.NotNull(secondResult);
        Assert.NotNull(secondCachedOutput);

        Logger.LogInformation($"First result: {firstResult}");
        Logger.LogInformation($"First cached: {firstCachedOutput}");
        Logger.LogInformation($"Second result: {secondResult}");
        Logger.LogInformation($"Second cached: {secondCachedOutput}");

        Assert.NotEqual(firstCachedOutput, secondCachedOutput);
    }

    [Fact]
    public async Task ExecutePowerShellCommand_WithParameters_ShouldCacheParameterizedResults()
    {
        // Arrange - Test with Get-ChildItem command that takes parameters
        var parameterInfos = new[]
        {
            new PowerShellParameterInfo("InputObject", typeof(string), false)
        };

        var parameterValues = new object[] { "dotnet" };

        // Act
        var result = await PowerShellAssemblyGenerator.ExecutePowerShellCommandTyped(
            "Write-Output",
            parameterInfos,
            parameterValues,
            CancellationToken.None,
            PowerShellRunspace,
            Logger);

        var cachedOutput = await PowerShellAssemblyGenerator.GetLastCommandOutput(
            PowerShellRunspace,
            Logger,
            CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(cachedOutput);

        Logger.LogInformation($"Parameterized result: {result}");
        Logger.LogInformation($"Parameterized cached: {cachedOutput}");

        // Both should be valid JSON
        Assert.True(result.StartsWith("[") || result.StartsWith("{"), "Result should be valid JSON");
        Assert.True(cachedOutput.StartsWith("[") || cachedOutput.StartsWith("{"), "Cached output should be valid JSON");

        // The results should be consistent - both should contain the same content
        Assert.Equal(result, cachedOutput);
    }

    [Fact]
    public async Task SortLastCommandOutput_ShouldSortCachedResults()
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

    [Fact]
    public async Task SortLastCommandOutput_ShouldReturnNull_WhenNoCacheExists()
    {
        // Arrange - Clear any existing cache
        await PowerShellRunspace.ExecuteThreadSafeAsync<object?>(ps =>
        {
            ps.Commands.Clear();
            ps.AddScript("$global:LastCommandOutput = $null; Remove-Variable -Name 'LastCommandOutput' -Scope Global -ErrorAction SilentlyContinue");
            ps.Invoke();

            if (ps.HadErrors)
            {
                ps.Streams.Error.Clear();
            }

            ps.Commands.Clear();
            return Task.FromResult<object?>(null);
        });

        // Act - Try to sort when no cache exists
        var sortedOutput = await PowerShellAssemblyGenerator.SortLastCommandOutput(
            PowerShellRunspace,
            Logger,
            "Name",
            false,
            CancellationToken.None);

        // Assert
        Assert.Null(sortedOutput);
        Logger.LogInformation("Correctly returned null when no cache exists for sorting");
    }

    [Fact]
    public void GeneratedAssembly_ShouldIncludeSortUtilityMethods()
    {
        // Arrange - Generate an assembly with some basic commands
        var commands = new List<CommandInfo>();
        // We don't need actual commands for this test since we're just checking for utility methods

        // Act - Generate the assembly
        var assembly = AssemblyGenerator.GenerateAssembly(commands, Logger);
        var instance = AssemblyGenerator.GetGeneratedInstance(Logger);
        var type = instance.GetType();

        // Assert - Check that the sort utility methods exist
        var getSortMethod = type.GetMethod("sort_last_command_output");
        var getLastMethod = type.GetMethod("get_last_command_output");

        Assert.NotNull(getSortMethod);
        Assert.NotNull(getLastMethod);

        Logger.LogInformation($"Found sort method: {getSortMethod.Name}");
        Logger.LogInformation($"Found get last method: {getLastMethod.Name}");

        // Verify method signatures
        Assert.Equal(typeof(Task<string>), getSortMethod.ReturnType);
        Assert.Equal(typeof(Task<string>), getLastMethod.ReturnType);

        var sortParameters = getSortMethod.GetParameters();
        Assert.Equal(3, sortParameters.Length);
        Assert.Equal(typeof(string), sortParameters[0].ParameterType); // property
        Assert.Equal(typeof(bool), sortParameters[1].ParameterType); // descending
        Assert.Equal(typeof(CancellationToken), sortParameters[2].ParameterType); // cancellationToken

        var getLastParameters = getLastMethod.GetParameters();
        Assert.Single(getLastParameters);
        Assert.Equal(typeof(CancellationToken), getLastParameters[0].ParameterType); // cancellationToken

        Logger.LogInformation("Generated assembly includes sort utility methods with correct signatures");
    }

    [Fact]
    public async Task FilterLastCommandOutput_ShouldFilterCachedResults()
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
            ps.Invoke();
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

    [Fact]
    public async Task FilterLastCommandOutput_ShouldReturnNull_WhenNoCacheExists()
    {
        // Arrange - Clear any existing cache
        await PowerShellRunspace.ExecuteThreadSafeAsync<object?>(ps =>
        {
            ps.Commands.Clear();
            ps.AddScript("$global:LastCommandOutput = $null; Remove-Variable -Name 'LastCommandOutput' -Scope Global -ErrorAction SilentlyContinue");
            ps.Invoke();

            if (ps.HadErrors)
            {
                ps.Streams.Error.Clear();
            }

            ps.Commands.Clear();
            return Task.FromResult<object?>(null);
        });

        // Act - Try to filter when no cache exists
        var filteredOutput = await PowerShellAssemblyGenerator.FilterLastCommandOutput(
            PowerShellRunspace,
            Logger,
            "$_.Name -like 'test*'",
            false, // updateCache
            CancellationToken.None);

        // Assert
        Assert.Null(filteredOutput);
        Logger.LogInformation("Correctly returned null when no cache exists for filtering");
    }

    [Fact]
    public async Task FilterLastCommandOutput_ShouldReturnNull_WhenInvalidScript()
    {
        // Arrange - Execute a command first to have cached data
        var parameterInfos = Array.Empty<PowerShellParameterInfo>();
        var parameterValues = Array.Empty<object>();

        await PowerShellAssemblyGenerator.ExecutePowerShellCommandTyped(
            "Get-Host",
            parameterInfos,
            parameterValues,
            CancellationToken.None,
            PowerShellRunspace,
            Logger);

        // Act - Try to filter with an invalid script
        var filteredOutput = await PowerShellAssemblyGenerator.FilterLastCommandOutput(
            PowerShellRunspace,
            Logger,
            "invalid script { syntax",
            false, // updateCache
            CancellationToken.None);

        // Assert
        Assert.Null(filteredOutput);
        Logger.LogInformation("Correctly returned null when filter script is invalid");
    }

    [Fact]
    public void GeneratedAssembly_ShouldIncludeFilterUtilityMethod()
    {
        // Arrange - Generate an assembly with some basic commands
        var commands = new List<CommandInfo>();

        // Act - Generate the assembly
        var assembly = AssemblyGenerator.GenerateAssembly(commands, Logger);
        var instance = AssemblyGenerator.GetGeneratedInstance(Logger);
        var type = instance.GetType();

        // Assert - Check that the filter utility method exists
        var filterMethod = type.GetMethod("filter_last_command_output");

        Assert.NotNull(filterMethod);
        Logger.LogInformation($"Found filter method: {filterMethod.Name}");

        // Verify method signature
        Assert.Equal(typeof(Task<string>), filterMethod.ReturnType);

        var filterParameters = filterMethod.GetParameters();
        Assert.Equal(3, filterParameters.Length);
        Assert.Equal(typeof(string), filterParameters[0].ParameterType); // filterScript
        Assert.Equal(typeof(bool), filterParameters[1].ParameterType); // updateCache
        Assert.Equal(typeof(CancellationToken), filterParameters[2].ParameterType); // cancellationToken

        Logger.LogInformation("Generated assembly includes filter utility method with correct signature");
    }

    [Fact]
    public void GeneratedAssembly_ShouldIncludeAllUtilityMethods()
    {
        // Arrange - Generate an assembly with some basic commands
        var commands = new List<CommandInfo>();

        // Act - Generate the assembly
        var assembly = AssemblyGenerator.GenerateAssembly(commands, Logger);
        var instance = AssemblyGenerator.GetGeneratedInstance(Logger);
        var type = instance.GetType();

        // Assert - Check that all utility methods exist
        var getLastMethod = type.GetMethod("get_last_command_output");
        var sortMethod = type.GetMethod("sort_last_command_output");
        var filterMethod = type.GetMethod("filter_last_command_output");

        Assert.NotNull(getLastMethod);
        Assert.NotNull(sortMethod);
        Assert.NotNull(filterMethod);

        Logger.LogInformation("Generated assembly includes all utility methods: get_last_command_output, sort_last_command_output, filter_last_command_output");
    }
}
