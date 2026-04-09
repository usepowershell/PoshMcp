using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PoshMcp.Server.PowerShell;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Functional.PowerShellCommandExecution;

/// <summary>
/// Validates Phase 3 large-result-performance behavior:
/// property filtering, _AllProperties override, _MaxResults composition,
/// and fallback when no DefaultDisplayPropertySet exists.
/// </summary>
public class ShouldApplyPhase3PropertyFiltering : PowerShellTestBase
{
    public ShouldApplyPhase3PropertyFiltering(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task GetProcess_UsesDefaultDisplayPropertySet_WhenAllPropertiesNotRequested()
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
            null!,
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
        Assert.Contains("Id", process.Keys);
        Assert.DoesNotContain("Modules", process.Keys);
        Assert.DoesNotContain("Threads", process.Keys);
    }

    [Fact]
    public async Task GetProcess_AllPropertiesTrue_BypassesDefaultDisplayFiltering()
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
        Assert.Contains("ProcessName", process.Keys);
    }

    [Fact]
    public async Task GetService_RequestedPropertiesAndMaxResults_AreAppliedTogether()
    {
        var parameterInfos = new[]
        {
            new PowerShellParameterInfo("_AllProperties", typeof(bool?), false),
            new PowerShellParameterInfo("_MaxResults", typeof(int?), false),
            new PowerShellParameterInfo("_RequestedProperties", typeof(string[]), false)
        };

        var parameterValues = new object[]
        {
            null!,
            1,
            new[] { "Name" }
        };

        var result = await PowerShellAssemblyGenerator.ExecutePowerShellCommandTyped(
            "Get-Service",
            parameterInfos,
            parameterValues,
            CancellationToken.None,
            PowerShellRunspace,
            Logger);

        Assert.NotNull(result);
        Assert.DoesNotContain("\"error\"", result, StringComparison.OrdinalIgnoreCase);

        var deserialized = ConvertJsonToObjects(result);
        Assert.Single(deserialized);

        var service = Assert.IsType<Dictionary<string, object>>(deserialized[0]);
        Assert.Single(service);
        Assert.Contains("Name", service.Keys);
    }

    [Fact]
    public async Task FallbackToAllProperties_WhenNoDisplayPropertySetExists()
    {
        await PowerShellRunspace.ExecuteThreadSafeAsync<object>(ps =>
        {
            ps.Commands.Clear();
            ps.AddScript(@"
function Get-Phase3NoDisplaySet {
    [pscustomobject]@{ Alpha = 1; Beta = 2 }
}");
            _ = SafeInvokePowerShell(ps, "defining no-display-set test function");
            ps.Commands.Clear();
            return Task.FromResult((object)new object());
        });

        var parameterInfos = new[]
        {
            new PowerShellParameterInfo("_AllProperties", typeof(bool?), false),
            new PowerShellParameterInfo("_MaxResults", typeof(int?), false),
            new PowerShellParameterInfo("_RequestedProperties", typeof(string[]), false)
        };

        var parameterValues = new object[]
        {
            null!,
            null!,
            null!
        };

        var result = await PowerShellAssemblyGenerator.ExecutePowerShellCommandTyped(
            "Get-Phase3NoDisplaySet",
            parameterInfos,
            parameterValues,
            CancellationToken.None,
            PowerShellRunspace,
            Logger);

        Assert.NotNull(result);
        Assert.DoesNotContain("\"error\"", result, StringComparison.OrdinalIgnoreCase);

        var deserialized = ConvertJsonToObjects(result);
        Assert.Single(deserialized);

        var item = Assert.IsType<Dictionary<string, object>>(deserialized[0]);
        Assert.Contains("Alpha", item.Keys);
        Assert.Contains("Beta", item.Keys);
    }
}
