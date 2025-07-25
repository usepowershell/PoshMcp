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
/// Test for filter behavior with invalid scripts
/// </summary>
public partial class FilterCachedResults : PowerShellTestBase
{

    [Fact]
    public async Task ShouldReturnNull_WhenInvalidScript()
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
}
