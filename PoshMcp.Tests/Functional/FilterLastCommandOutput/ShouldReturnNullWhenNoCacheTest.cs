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
/// Test for filter behavior when no cache exists
/// </summary>
public partial class FilterCachedResults : PowerShellTestBase
{

    [Fact]
    public async Task FilterLastCommandOutput_ShouldReturnNull_WhenNoCacheExists()
    {
        // Arrange - Clear any existing cache
        await PowerShellRunspace.ExecuteThreadSafeAsync<object?>(ps =>
        {
            ps.Commands.Clear();
            ps.AddScript("$global:LastCommandOutput = $null; Remove-Variable -Name 'LastCommandOutput' -Scope Global -ErrorAction SilentlyContinue");
            SafeInvokePowerShell(ps, "clearing cache for filter test");

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
}
