using PoshMcp.PowerShell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Tests for case-insensitive parameter matching in PowerShell command execution
/// </summary>
public class CaseInsensitiveParameterTests : PowerShellTestBase
{
    public CaseInsensitiveParameterTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task ExecutePowerShellCommandTyped_ShouldHandleCaseInsensitiveParameterNames()
    {
        // Arrange
        SetupTestPowerShellFunction();

        // Create parameter infos with different casing than the actual PowerShell parameter names
        var parameterInfos = new[]
        {
            new PowerShellParameterInfo("name", typeof(string), false),      // PowerShell param: "Name"
            new PowerShellParameterInfo("COUNT", typeof(int), false),       // PowerShell param: "Count"
            new PowerShellParameterInfo("includetimestamp", typeof(bool), false) // PowerShell param: "IncludeTimestamp"
        };

        var parameterValues = new object[]
        {
            "TestValue",
            5,
            true
        };

        // Act
        var result = await PowerShellAssemblyGenerator.ExecutePowerShellCommandTyped(
            "Get-CaseTestFunction",
            parameterInfos,
            parameterValues,
            CancellationToken.None,
            PowerShellRunspace,
            Logger);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("TestValue", result);
        Assert.Contains("5", result);
        Logger.LogInformation($"Result: {result}");
    }

    [Fact]
    public async Task ExecutePowerShellCommandTyped_ShouldHandlePartialParameterNames()
    {
        // Arrange
        SetupTestPowerShellFunction();

        // Use partial parameter names (PowerShell allows this if unambiguous)
        var parameterInfos = new[]
        {
            new PowerShellParameterInfo("inc", typeof(bool), false)  // Should match "IncludeTimestamp"
        };

        var parameterValues = new object[]
        {
            true
        };

        // Act
        var result = await PowerShellAssemblyGenerator.ExecutePowerShellCommandTyped(
            "Get-CaseTestFunction",
            parameterInfos,
            parameterValues,
            CancellationToken.None,
            PowerShellRunspace,
            Logger);

        // Assert
        Assert.NotNull(result);
        Logger.LogInformation($"Result with partial parameter: {result}");
    }

    [Fact]
    public async Task ExecutePowerShellCommandTyped_ShouldHandleExactParameterMatch()
    {
        // Arrange
        SetupTestPowerShellFunction();

        // Use exact parameter names (should still work)
        var parameterInfos = new[]
        {
            new PowerShellParameterInfo("Name", typeof(string), false),
            new PowerShellParameterInfo("Count", typeof(int), false)
        };

        var parameterValues = new object[]
        {
            "ExactMatch",
            3
        };

        // Act
        var result = await PowerShellAssemblyGenerator.ExecutePowerShellCommandTyped(
            "Get-CaseTestFunction",
            parameterInfos,
            parameterValues,
            CancellationToken.None,
            PowerShellRunspace,
            Logger);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("ExactMatch", result);
        Assert.Contains("3", result);
        Logger.LogInformation($"Result with exact match: {result}");
    }

    [Fact]
    public async Task ExecutePowerShellCommandTyped_ShouldHandleNonExistentParameter()
    {
        // Arrange
        SetupTestPowerShellFunction();

        // Use a parameter name that doesn't exist
        var parameterInfos = new[]
        {
            new PowerShellParameterInfo("NonExistentParam", typeof(string), false)
        };

        var parameterValues = new object[]
        {
            "SomeValue"
        };

        // Act & Assert
        // This should not throw an exception, but the parameter will be ignored or cause a PowerShell error
        var result = await PowerShellAssemblyGenerator.ExecutePowerShellCommandTyped(
            "Get-CaseTestFunction",
            parameterInfos,
            parameterValues,
            CancellationToken.None,
            PowerShellRunspace,
            Logger);

        // The result might contain an error, but the method should not throw
        Assert.NotNull(result);
        Logger.LogInformation($"Result with non-existent parameter: {result}");
    }

    private void SetupTestPowerShellFunction()
    {
        try
        {
            var testFunctionScript = @"
function Get-CaseTestFunction {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$false, Position=0)]
        [string]$Name = 'DefaultName',

        [Parameter(Mandatory=$false, Position=1)]
        [int]$Count = 1,

        [Parameter(Mandatory=$false)]
        [bool]$IncludeTimestamp = $false
    )

    $result = @()
    for ($i = 1; $i -le $Count; $i++) {
        $item = ""Item $i for $Name""
        if ($IncludeTimestamp) {
            $item += "" at $(Get-Date)""
        }
        $result += $item
    }

    return $result
}";

            PowerShellRunspace.ExecuteThreadSafeAsync<object>(ps =>
            {
                ps.Commands.Clear();
                ps.AddScript(testFunctionScript);
                SafeInvokePowerShell(ps, "setting up case test function");
                ps.Commands.Clear();

                if (ps.HadErrors)
                {
                    var errors = ps.Streams.Error.ReadAll();
                    Logger.LogWarning($"Errors setting up case test function: {string.Join("; ", errors)}");
                    ps.Streams.Error.Clear();
                }
                else
                {
                    Logger.LogInformation("Case test PowerShell function set up successfully");
                }

                return Task.FromResult<object>(null!);
            }).Wait();
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Exception setting up case test function: {ex.Message}");
        }
    }
}