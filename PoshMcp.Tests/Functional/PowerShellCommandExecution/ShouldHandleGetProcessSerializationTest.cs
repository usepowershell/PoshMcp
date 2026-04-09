using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PoshMcp.Server.PowerShell;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Functional.PowerShellCommandExecution;

/// <summary>
/// Regression test for serializing Get-Process output without traversing expensive live process graphs.
/// </summary>
public class HandleGetProcessSerialization : PowerShellTestBase
{
    public HandleGetProcessSerialization(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task ShouldSerializeGetProcessWithoutExpandingLiveCollections()
    {
        var parameterInfos = new[]
        {
            new PowerShellParameterInfo("Id", typeof(int[]), false),
            new PowerShellParameterInfo("_AllProperties", typeof(bool?), false),
            new PowerShellParameterInfo("_MaxResults", typeof(int?), false),
            new PowerShellParameterInfo("_RequestedProperties", typeof(string[]), false)
        };

        var parameterValues = new object[]
        {
            new[] { Environment.ProcessId },
            true,
            null!,
            null!
        };

        var result = await PowerShellAssemblyGenerator.ExecutePowerShellCommandTyped(
            "Get-Process",
            parameterInfos,
            parameterValues,
            CancellationToken.None,
            PowerShellRunspace,
            Logger);

        Assert.NotNull(result);
        Assert.DoesNotContain("\"error\"", result, StringComparison.OrdinalIgnoreCase);

        var deserialized = ConvertJsonToObjects(result);
        Assert.Single(deserialized);

        var process = Assert.IsType<Dictionary<string, object>>(deserialized[0]);
        Assert.Equal(Environment.ProcessId, Convert.ToInt32(process["Id"]));
        Assert.True(process.ContainsKey("ProcessName"));

        Assert.True(process.TryGetValue("Modules", out var modulesValue));
        Assert.True(modulesValue is null or string, $"Expected Modules to remain shallow, got {modulesValue?.GetType().FullName}");

        Assert.True(process.TryGetValue("Threads", out var threadsValue));
        Assert.True(threadsValue is null or string, $"Expected Threads to remain shallow, got {threadsValue?.GetType().FullName}");
    }
}