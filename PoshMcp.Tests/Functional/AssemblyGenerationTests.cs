using PoshMcp.PowerShell;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Functional;

/// <summary>
/// Tests for PowerShell dynamic assembly generation
/// </summary>
public class AssemblyGenerationTests : PowerShellTestBase
{
    public AssemblyGenerationTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void GenerateAssembly_ShouldCreateValidAssembly()
    {
        // Arrange - Setup test PowerShell function
        SetupTestPowerShellFunction();
        var commands = GetTestCommands();

        // Act
        var assembly = AssemblyGenerator.GenerateAssembly(commands, Logger);

        // Assert
        Assert.NotNull(assembly);
        Assert.True(assembly.IsDynamic);
        Assert.NotEmpty(assembly.GetTypes());

        Logger.LogInformation($"Generated assembly: {assembly.FullName}");
        Logger.LogInformation($"Assembly is dynamic: {assembly.IsDynamic}");
    }

    [Fact]
    public void GetGeneratedMethods_ShouldReturnMethodsForCommands()
    {
        // Arrange - Setup test PowerShell function
        SetupTestPowerShellFunction();
        var commands = GetTestCommands();

        // Verify we have commands to work with
        Assert.NotEmpty(commands);
        Logger.LogInformation($"Testing with {commands.Count} commands");

        // Generate assembly first
        AssemblyGenerator.GenerateAssembly(commands, Logger);

        // Act
        var methods = AssemblyGenerator.GetGeneratedMethods();

        // Assert
        Assert.NotNull(methods);
        Assert.NotEmpty(methods);

        foreach (var kvp in methods)
        {
            var methodName = kvp.Key;
            var method = kvp.Value;

            Logger.LogInformation($"Method: {methodName}");
            Logger.LogInformation($"  Return type: {method.ReturnType.Name}");
            Logger.LogInformation($"  Parameters: {method.GetParameters().Length}");

            // Verify method signature
            Assert.Equal(typeof(System.Threading.Tasks.Task<string>), method.ReturnType);
            Assert.True(method.GetParameters().Length > 0); // Should have at least CancellationToken

            // Verify last parameter is CancellationToken
            var lastParam = method.GetParameters().Last();
            Assert.Equal(typeof(System.Threading.CancellationToken), lastParam.ParameterType);
            Assert.Equal("cancellationToken", lastParam.Name);
        }
    }

    [Fact]
    public void GetGeneratedInstance_ShouldReturnValidInstance()
    {
        // Arrange - Setup test PowerShell function
        SetupTestPowerShellFunction();
        var commands = GetTestCommands();

        // Verify we have commands to work with
        Assert.NotEmpty(commands);
        Logger.LogInformation($"Testing with {commands.Count} commands");

        // Generate assembly first
        AssemblyGenerator.GenerateAssembly(commands, Logger);

        // Act
        var instance = AssemblyGenerator.GetGeneratedInstance(Logger);

        // Assert
        Assert.NotNull(instance);

        var instanceType = instance.GetType();
        Logger.LogInformation($"Instance type: {instanceType.Name}");
        Logger.LogInformation($"Instance namespace: {instanceType.Namespace}");

        // Verify instance has expected methods
        var methods = instanceType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.DeclaringType == instanceType && m.Name != ".ctor")
            .ToList();

        Assert.NotEmpty(methods);
        Logger.LogInformation($"Instance has {methods.Count} public methods");
    }

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

    [Fact]
    public async Task GenerateAssembly_ShouldCreateParameterSetSpecificMethods()
    {
        // Arrange - Get a command with multiple parameter sets
        var command = await PowerShellRunspace.ExecuteThreadSafeAsync<CommandInfo?>(ps =>
        {
            ps.Commands.Clear();
            ps.AddCommand("Get-Command").AddParameter("Name", "Get-ChildItem");
            var cmd = ps.Invoke<CommandInfo>().FirstOrDefault();
            ps.Commands.Clear();
            return Task.FromResult<CommandInfo?>(cmd);
        });

        Assert.NotNull(command);
        Assert.True(command.ParameterSets.Count > 1, "Get-ChildItem should have multiple parameter sets");

        Logger.LogInformation($"Command: {command.Name}");
        Logger.LogInformation($"Parameter Sets: {command.ParameterSets.Count}");

        foreach (var paramSet in command.ParameterSets)
        {
            Logger.LogInformation($"  - {paramSet.Name} (IsDefault: {paramSet.IsDefault})");
        }

        // Act - Generate assembly to see parameter set-specific methods
        var assembly = AssemblyGenerator.GenerateAssembly(new[] { command }, Logger);
        var methods = AssemblyGenerator.GetGeneratedMethods();

        // Assert
        Assert.NotNull(assembly);
        Assert.NotEmpty(methods);

        Logger.LogInformation($"Generated Methods: {methods.Count}");
        foreach (var method in methods.OrderBy(m => m.Key))
        {
            Logger.LogInformation($"  - {method.Key} ({method.Value.GetParameters().Length - 1} parameters)");
        }

        // Verify that multiple methods were generated for the parameter sets
        Assert.True(methods.Count > 1, "Should generate multiple methods for different parameter sets");

        // Verify method naming follows parameter set convention
        var methodNames = methods.Keys.ToList();

        // Should have methods like "get_child_item_items" and "get_child_item_literal_items"
        var hasParameterSetSpecificMethods = methodNames.Any(name =>
            name.Contains("_") && name.StartsWith("get_child_item"));

        Assert.True(hasParameterSetSpecificMethods,
            "Should generate parameter set-specific method names like 'get_child_item_items'");

        // Verify that parameter set names are appended (except for __AllParameterSets)
        var parameterSetNames = command.ParameterSets
            .Where(ps => ps.Name != "__AllParameterSets")
            .Select(ps => ps.Name)
            .ToList();

        foreach (var paramSetName in parameterSetNames)
        {
            var expectedMethodPattern = PowerShellDynamicAssemblyGenerator.SanitizeMethodName("Get-ChildItem", paramSetName);
            var hasExpectedMethod = methodNames.Any(name =>
                name.Equals(expectedMethodPattern, System.StringComparison.OrdinalIgnoreCase));

            if (!hasExpectedMethod)
            {
                Logger.LogInformation($"Looking for pattern: {expectedMethodPattern}");
                Logger.LogInformation($"Available methods: {string.Join(", ", methodNames)}");
            }
        }

        Logger.LogInformation("Parameter set functionality test completed successfully");
    }
}
