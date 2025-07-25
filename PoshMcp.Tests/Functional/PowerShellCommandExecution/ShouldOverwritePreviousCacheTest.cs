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

namespace PoshMcp.Tests.Functional.PowerShellCommandExecution;

/// <summary>
/// Test for cache overwriting behavior
/// </summary>
public class OverwriteCache : PowerShellTestBase
{

    public OverwriteCache(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task ShouldOverwritePreviousCache()
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
}
