using PoshMcp.Server.PowerShell;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Functional.SortLastCommandOutput;

/// <summary>
/// Test for sort behavior when no cache exists
/// </summary>
public class ShouldReturnNullTest : PowerShellTestBase
{
    public ShouldReturnNullTest(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task WhenNoCacheExists()
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
