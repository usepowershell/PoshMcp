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

namespace PoshMcp.Tests.Functional.GetLastCommandOutput;

/// <summary>
/// Test for cache retrieval when no cache exists
/// </summary>
public class GetLastCommandResult : PowerShellTestBase
{
    public GetLastCommandResult(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task ShouldReturnNull_WhenNoCacheExists()
    {
        // Arrange - Clear any existing cache by running a PowerShell command that sets LastCommandOutput to null
        await PowerShellRunspace.ExecuteThreadSafeAsync<object?>(ps =>
        {
            ps.Commands.Clear();
            ps.AddScript("$global:LastCommandOutput = $null; Remove-Variable -Name 'LastCommandOutput' -Scope Global -ErrorAction SilentlyContinue");

            // Use safe invoke method
            SafeInvokePowerShell(ps, "clearing cache");

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
}
