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

namespace PoshMcp.Tests.Functional.PowerShellCommandExecution;

/// <summary>
/// Test for caching PowerShell command results and retrieval
/// </summary>
[Collection("CachingStateTests")]
public class CacheResults : PowerShellTestBase
{
    public CacheResults(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task ShouldCacheResults_AndAllowRetrieval()
    {
        var runtimeCachingState = new RuntimeCachingState();
        runtimeCachingState.SetGlobalOverride(true);
        PowerShellAssemblyGenerator.SetRuntimeCachingState(runtimeCachingState);

        try
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
        finally
        {
            PowerShellAssemblyGenerator.SetRuntimeCachingState(new RuntimeCachingState());
        }
    }
}
