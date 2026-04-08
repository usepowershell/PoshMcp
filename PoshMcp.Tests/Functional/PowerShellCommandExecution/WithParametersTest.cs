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

        var expectedText = "dotnet";
        var parameterValues = new object[] { expectedText };

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

        Assert.Equal($"[\"{expectedText}\"]", result);
        Assert.Equal(result, cachedOutput);
        Assert.DoesNotContain("\"Length\"", result, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Length\"", cachedOutput, StringComparison.Ordinal);

        var deserializedResult = ConvertJsonToObjects(result);
        Assert.Single(deserializedResult);
        Assert.Equal(expectedText, Assert.IsType<string>(deserializedResult[0]));

        var deserializedCachedOutput = ConvertJsonToObjects(cachedOutput);
        Assert.Single(deserializedCachedOutput);
        Assert.Equal(expectedText, Assert.IsType<string>(deserializedCachedOutput[0]));
    }
}
