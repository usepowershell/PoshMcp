using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using PoshMcp.PowerShell;
using Xunit.Abstractions;

namespace PoshMcp.Tests;

/// <summary>
/// Base class for PowerShell dynamic assembly tests with isolated runspace per test
/// </summary>
public abstract class PowerShellTestBase : IDisposable
{
    protected readonly ILogger Logger;
    protected readonly ITestOutputHelper Output;
    protected readonly IPowerShellRunspace PowerShellRunspace;
    protected readonly PowerShellAssemblyGenerator AssemblyGenerator;

    protected PowerShellTestBase(ITestOutputHelper output)
    {
        Output = output;

        // Create a logger that writes to test output
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new TestOutputLoggerProvider(output));
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        Logger = loggerFactory.CreateLogger(GetType());

        // Create an isolated PowerShell runspace for this test
        PowerShellRunspace = new IsolatedPowerShellRunspace();

        // Create an instance-based assembly generator with the isolated runspace
        AssemblyGenerator = new PowerShellAssemblyGenerator(PowerShellRunspace);
    }    /// <summary>
         /// Helper method to convert JSON string results back to objects for testing.
         /// This allows tests to work with both the old PSObject[] format and new JSON string format.
         /// </summary>
         /// <param name="jsonResult">JSON string returned from PowerShell execution</param>
         /// <returns>Array of objects that can be used in test assertions</returns>
    protected object[] ConvertJsonToObjects(string jsonResult)
    {
        return PowerShellParameterUtils.DeserializeFromPowerShellJson(jsonResult);
    }

    /// <summary>
    /// Helper method to get a property value from a deserialized PowerShell object.
    /// Since the objects are now dictionaries after JSON deserialization, this provides a convenient way to access properties.
    /// </summary>
    /// <param name="obj">Deserialized PowerShell object (typically a Dictionary)</param>
    /// <param name="propertyName">Name of the property to retrieve</param>
    /// <returns>Property value or null if not found</returns>
    protected object? GetPropertyValue(object obj, string propertyName)
    {
        if (obj is System.Collections.Generic.Dictionary<string, object> dict)
        {
            dict.TryGetValue(propertyName, out var value);
            return value;
        }
        return null;
    }

    /// <summary>
    /// Safely invokes PowerShell commands with empty pipeline protection
    /// </summary>
    /// <param name="ps">PowerShell instance</param>
    /// <param name="operationName">Name of the operation for logging</param>
    /// <returns>Collection of PSObjects or empty collection if pipeline is empty</returns>
    protected Collection<PSObject> SafeInvokePowerShell(System.Management.Automation.PowerShell ps, string operationName = "PowerShell operation")
    {
        try
        {
            // Check if pipeline contains commands before invoking
            if (ps.Commands.Commands.Count == 0)
            {
                Logger.LogWarning($"Cannot execute {operationName}: PowerShell pipeline contains no commands");
                return new Collection<PSObject>();
            }

            return ps.Invoke();
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to execute {operationName}: {ex.Message}");
            ps.Commands.Clear();
            return new Collection<PSObject>();
        }
    }

    /// <summary>
    /// Clean up after each test to ensure no state pollution
    /// </summary>
    public virtual void Dispose()
    {
        // Clear the assembly generator cache
        AssemblyGenerator.ClearCache();

        // Dispose of our isolated PowerShell runspace
        PowerShellRunspace.Dispose();
    }
}
