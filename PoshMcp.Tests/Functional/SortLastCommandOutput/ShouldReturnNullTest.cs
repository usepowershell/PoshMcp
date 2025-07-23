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
/// Test for sort behavior when no cache exists
/// </summary>
public class SortLastCommandOutput_ShouldReturnNullTest : PowerShellTestBase
{
    public SortLastCommandOutput_ShouldReturnNullTest(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task SortLastCommandOutput_ShouldReturnNull_WhenNoCacheExists()
    {
        // Arrange - Clear any existing cache
        await PowerShellRunspace.ExecuteThreadSafeAsync<object?>(ps =>
        {
            ps.Commands.Clear();
            ps.AddScript("$global:LastCommandOutput = $null; Remove-Variable -Name 'LastCommandOutput' -Scope Global -ErrorAction SilentlyContinue");
            SafeInvokePowerShell(ps, "clearing cache for sort test");

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
}
