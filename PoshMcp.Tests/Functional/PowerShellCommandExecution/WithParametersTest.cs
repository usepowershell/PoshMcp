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
/// Test for parameterized command caching
/// </summary>
public class ExecutePowerShellCommandWithParameters : PowerShellTestBase
{
    public ExecutePowerShellCommandWithParameters(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task ShouldCacheParameterizedResults()
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
}
