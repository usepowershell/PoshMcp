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
/// Test for handling valid PowerShell commands
/// </summary>
public class ValidCommand : PowerShellTestBase
{
    public ValidCommand(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task ShouldHandleValidCommand()
    {
        // Arrange
        await SetupTestPowerShellFunctionAsync();

        var parameterInfos = new[]
        {
            new PowerShellParameterInfo("Name", typeof(string), false),
            new PowerShellParameterInfo("Count", typeof(int), false)
        };

        var parameterValues = new object[] { "TestUser", 3 };

        // Act
        var result = await PowerShellAssemblyGenerator.ExecutePowerShellCommandTyped(
            "Get-SomeOtherData",
            parameterInfos,
            parameterValues,
            CancellationToken.None,
            PowerShellRunspace,
            Logger);

        // Assert
        Assert.NotNull(result);
        Logger.LogInformation($"Command result: {result}");

        Assert.DoesNotContain("\"error\"", result, StringComparison.OrdinalIgnoreCase);
        var deserializedResult = ConvertJsonToObjects(result);
        Assert.Single(deserializedResult);

        var output = Assert.IsType<string>(deserializedResult[0]);
        Assert.Contains("Data item 1 for TestUser", output, StringComparison.Ordinal);
        Assert.Contains("Data item 3 for TestUser", output, StringComparison.Ordinal);
    }

    private async Task SetupTestPowerShellFunctionAsync()
    {
        try
        {
            var testFunctionScript = @"
function Get-SomeOtherData {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$false, Position=0)]
        [string]$Name = 'DefaultName',
        
        [Parameter(Mandatory=$false, Position=1)]
        [int]$Count = 1
    )
    
    $result = @()
    for ($i = 1; $i -le $Count; $i++) {
        $result += ""Data item $i for $Name""
    }
    
    return $result -join ""`n""
}";

            // Use thread-safe execution
            await PowerShellRunspace.ExecuteThreadSafeAsync<Collection<PSObject>>(ps =>
            {
                ps.Commands.Clear();
                ps.AddScript(testFunctionScript);

                // Use safe invoke method
                var results = SafeInvokePowerShell(ps, "setting up test function");

                if (ps.HadErrors)
                {
                    var errors = ps.Streams.Error.ReadAll();
                    Logger.LogWarning($"Errors setting up test function: {string.Join("; ", errors)}");
                    ps.Streams.Error.Clear();
                }
                else
                {
                    Logger.LogInformation("Test PowerShell function set up successfully");
                }

                return Task.FromResult(results);
            });
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Exception setting up test function: {ex.Message}");
        }
    }
}
