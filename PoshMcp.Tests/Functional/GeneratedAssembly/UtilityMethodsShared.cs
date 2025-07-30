using PoshMcp.Server.PowerShell;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Functional.GeneratedAssembly;

/// <summary>
/// Shared utilities for assembly generation tests
/// </summary>
public partial class GeneratedInstance : PowerShellTestBase
{
    private void SetupTestPowerShellFunction()
    {
        try
        {
            var testFunctionScript = @"
# Remove any existing Get-SomeOtherData functions
if (Get-Command Get-SomeOtherData -ErrorAction SilentlyContinue) { 
    Remove-Item function:Get-SomeOtherData -Force
}

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
}
";

            // Execute the script using the PowerShell runspace
            PowerShellRunspace.ExecuteThreadSafeAsync<object>(ps =>
            {
                ps.Commands.Clear();
                ps.AddScript(testFunctionScript);
                var result = SafeInvokePowerShell(ps, "setting up test function for assembly generation");
                ps.Commands.Clear();

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

                return Task.FromResult<object>(result);
            }).Wait();
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Exception setting up test function: {ex.Message}");
        }
    }

    private System.Collections.Generic.List<CommandInfo> GetTestCommands()
    {
        try
        {
            return PowerShellRunspace.ExecuteThreadSafeAsync<System.Collections.Generic.List<CommandInfo>>(ps =>
            {
                var commands = new System.Collections.Generic.List<CommandInfo>();

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
                    Logger.LogWarning("Test command Get-SomeOtherData not found, using Get-Process as fallback");
                    // Fallback to Get-Process for basic testing
                    ps.AddCommand("Get-Command").AddParameter("Name", "Get-Process");
                    var fallbackCmd = ps.Invoke<CommandInfo>().FirstOrDefault();
                    ps.Commands.Clear();
                    if (fallbackCmd != null)
                    {
                        commands.Add(fallbackCmd);
                        Logger.LogInformation($"Using fallback command: {fallbackCmd.Name}");
                    }
                    else
                    {
                        Logger.LogError("Could not find any suitable commands for testing");
                    }
                }

                Logger.LogInformation($"Total commands found: {commands.Count}");
                return Task.FromResult(commands);
            }).Result;
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error getting test commands: {ex.Message}");
            return new System.Collections.Generic.List<CommandInfo>();
        }
    }
}
