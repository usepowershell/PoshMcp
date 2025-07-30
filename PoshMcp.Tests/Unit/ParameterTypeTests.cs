using PoshMcp.Server.PowerShell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Tests for PowerShell parameter type handling in dynamic assembly generation
/// </summary>
public class ParameterTypeTests : PowerShellTestBase
{
    public ParameterTypeTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void GeneratedMethod_ShouldHandleParameterTypes()
    {
        // Arrange
        SetupTestPowerShellFunction();
        var commands = GetTestCommands();

        AssemblyGenerator.GenerateAssembly(commands, Logger);
        var methods = AssemblyGenerator.GetGeneratedMethods();

        foreach (var kvp in methods)
        {
            var methodName = kvp.Key;
            var method = kvp.Value;

            Logger.LogInformation($"\nTesting parameter types for method: {methodName}");

            var parameters = method.GetParameters();

            foreach (var param in parameters)
            {
                Logger.LogInformation($"  Parameter: {param.Name} ({param.ParameterType.Name})");

                // Verify parameter has a name
                Assert.False(string.IsNullOrEmpty(param.Name), $"Parameter in method {methodName} has no name");

                // Verify parameter type is supported
                Assert.True(IsSupportedParameterType(param.ParameterType),
                    $"Unsupported parameter type {param.ParameterType.Name} in method {methodName}");
            }
        }
    }

    [Fact]
    public void IsSupportedParameterType_ShouldHandleCommonTypes()
    {
        // Arrange & Act & Assert
        Assert.True(IsSupportedParameterType(typeof(string)));
        Assert.True(IsSupportedParameterType(typeof(int)));
        Assert.True(IsSupportedParameterType(typeof(bool)));
        Assert.True(IsSupportedParameterType(typeof(double)));
        Assert.True(IsSupportedParameterType(typeof(DateTime)));
        Assert.True(IsSupportedParameterType(typeof(SwitchParameter)));
        Assert.True(IsSupportedParameterType(typeof(System.Threading.CancellationToken)));

        // Test nullable types
        Assert.True(IsSupportedParameterType(typeof(int?)));
        Assert.True(IsSupportedParameterType(typeof(bool?)));
        Assert.True(IsSupportedParameterType(typeof(DateTime?)));

        // Test array types
        Assert.True(IsSupportedParameterType(typeof(string[])));
        Assert.True(IsSupportedParameterType(typeof(int[])));
    }

    [Fact]
    public void IsSupportedParameterType_ShouldHandlePowerShellTypes()
    {
        // Test PowerShell-specific types by name
        var actionPreferenceType = typeof(ActionPreference);
        Assert.True(IsSupportedParameterType(actionPreferenceType));

        Logger.LogInformation($"ActionPreference type name: {actionPreferenceType.Name}");
        Logger.LogInformation($"Is enum: {actionPreferenceType.IsEnum}");
    }

    private void SetupTestPowerShellFunction()
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
        [int]$Count = 1,
        
        [Parameter(Mandatory=$false)]
        [bool]$IncludeTimestamp = $false,
        
        [Parameter(Mandatory=$false)]
        [DateTime]$StartDate = (Get-Date),
        
        [Parameter(Mandatory=$false)]
        [string[]]$Tags = @()
    )
    
    $result = @()
    for ($i = 1; $i -le $Count; $i++) {
        $item = ""Data item $i for $Name""
        if ($IncludeTimestamp) {
            $item += "" at $StartDate""
        }
        if ($Tags) {
            $item += "" [Tags: $($Tags -join ', ')]""
        }
        $result += $item
    }
    
    return $result -join ""`n""
}";

            PowerShellRunspace.ExecuteThreadSafeAsync<object>(ps =>
            {
                ps.Commands.Clear();
                ps.AddScript(testFunctionScript);
                SafeInvokePowerShell(ps, "setting up test function for parameter types");
                ps.Commands.Clear();

                if (ps.HadErrors)
                {
                    var errors = ps.Streams.Error.ReadAll();
                    Logger.LogWarning($"Errors setting up test function: {string.Join("; ", errors)}");
                    ps.Streams.Error.Clear();
                }
                else
                {
                    Logger.LogInformation("Test PowerShell function with various parameter types set up successfully");
                }

                return Task.FromResult<object>(null!);
            }).Wait();
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Exception setting up test function: {ex.Message}");
        }
    }

    private List<CommandInfo> GetTestCommands()
    {
        try
        {
            return PowerShellRunspace.ExecuteThreadSafeAsync<List<CommandInfo>>(ps =>
            {
                var commands = new List<CommandInfo>();

                // Try to get the test command
                ps.Commands.Clear();
                ps.AddCommand("Get-Command").AddParameter("Name", "Get-SomeOtherData");
                var cmdInfo = ps.Invoke<CommandInfo>().FirstOrDefault();
                ps.Commands.Clear();

                if (cmdInfo != null)
                {
                    commands.Add(cmdInfo);
                    Logger.LogInformation($"Found test command: {cmdInfo.Name}");
                }
                else
                {
                    Logger.LogWarning("Test command not found");
                }

                return Task.FromResult(commands);
            }).Result;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Error getting test commands: {ex.Message}");
            return new List<CommandInfo>();
        }
    }

    private static bool IsSupportedParameterType(Type type)
    {
        // Handle nullable types
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            var underlyingType = Nullable.GetUnderlyingType(type);
            return underlyingType != null && IsSupportedParameterType(underlyingType);
        }

        // List of supported parameter types
        var supportedTypes = new[]
        {
            typeof(string),
            typeof(int), typeof(bool), typeof(double), typeof(decimal), typeof(DateTime),
            typeof(SwitchParameter),
            typeof(System.Threading.CancellationToken)
        };

        // Check if it's a basic supported type
        if (supportedTypes.Contains(type))
            return true;

        // Check if it's an array of supported types
        if (type.IsArray && type.GetElementType() != null && supportedTypes.Contains(type.GetElementType()))
            return true;

        // Handle enums
        if (type.IsEnum)
            return true;

        // Handle PowerShell-specific types
        if (type.Name == "ActionPreference")
            return true;

        // Handle FlagsExpression and other complex PowerShell types
        if (type.IsGenericType && type.Name.StartsWith("FlagsExpression"))
        {
            // For now, we'll accept FlagsExpression types but they might need special handling
            return true;
        }

        // Handle other PowerShell command-specific parameter types
        if (type.Namespace?.StartsWith("System.Management.Automation") == true)
        {
            return true;
        }

        return false;
    }
}
