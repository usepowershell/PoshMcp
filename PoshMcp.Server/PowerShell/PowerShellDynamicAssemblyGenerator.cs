using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Static wrapper for backward compatibility - delegates to instance-based generator
/// </summary>
public static class PowerShellDynamicAssemblyGenerator
{
    private static readonly object _lock = new object();
    private static PowerShellAssemblyGenerator? _instance;

    /// <summary>
    /// Gets or creates the static instance using singleton runspace
    /// </summary>
    private static PowerShellAssemblyGenerator GetInstance()
    {
        lock (_lock)
        {
            return _instance ??= new PowerShellAssemblyGenerator(new SingletonPowerShellRunspace());
        }
    }

    /// <summary>
    /// Generates or retrieves the cached in-memory assembly containing PowerShell command methods
    /// </summary>
    /// <param name="commands">List of PowerShell commands to generate methods for</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>The generated assembly</returns>
    public static Assembly GenerateAssembly(IEnumerable<CommandInfo> commands, ILogger logger)
    {
        return GetInstance().GenerateAssembly(commands, logger);
    }

    /// <summary>
    /// Gets an instance of the generated PowerShell commands class
    /// </summary>
    /// <param name="logger">Logger instance to inject</param>
    /// <returns>Instance of the generated class</returns>
    public static object GetGeneratedInstance(ILogger logger)
    {
        return GetInstance().GetGeneratedInstance(logger);
    }

    /// <summary>
    /// Gets all generated methods from the assembly
    /// </summary>
    /// <returns>Dictionary mapping command names to their generated methods</returns>
    public static Dictionary<string, MethodInfo> GetGeneratedMethods()
    {
        return GetInstance().GetGeneratedMethods();
    }

    /// <summary>
    /// Clears the cached assembly and instance. Used for testing to ensure clean state between tests.
    /// </summary>
    public static void ClearCache()
    {
        lock (_lock)
        {
            _instance?.ClearCache();
            _instance = null;
        }
    }

    /// <summary>
    /// Sets the PowerShell runspace to use for command execution (for dependency injection)
    /// No longer needed with instance-based design - kept for compatibility
    /// </summary>
    /// <param name="runspace">The PowerShell runspace implementation to use</param>
    [Obsolete("Use PowerShellAssemblyGenerator instance constructor instead")]
    public static void SetPowerShellRunspace(IPowerShellRunspace runspace)
    {
        lock (_lock)
        {
            _instance = new PowerShellAssemblyGenerator(runspace);
        }
    }

    /// <summary>
    /// Static method called by generated IL code to execute PowerShell commands
    /// Delegates to the PowerShellAssemblyGenerator static method for backward compatibility
    /// </summary>
    public static async Task<string> ExecutePowerShellCommandTyped(
        string commandName,
        PowerShellParameterInfo[] parameterInfos,
        object[] parameterValues,
        CancellationToken cancellationToken,
        ILogger logger)
    {
        // Get the singleton instance to access its runspace
        var instance = GetInstance();
        // Access the runspace through reflection for compatibility
        var runspaceField = typeof(PowerShellAssemblyGenerator).GetField("_powerShellRunspace",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var runspace = (IPowerShellRunspace)runspaceField!.GetValue(instance)!;

        // Delegate to PowerShellAssemblyGenerator static method with correct parameter order
        return await PowerShellAssemblyGenerator.ExecutePowerShellCommandTyped(
            commandName, parameterInfos, parameterValues, cancellationToken, runspace, logger);
    }

    /// <summary>
    /// Retrieves the cached output from the last executed PowerShell command
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON-serialized cached command output, or null if no cache exists</returns>
    public static async Task<string?> GetLastCommandOutput(ILogger logger, CancellationToken cancellationToken = default)
    {
        // Get the singleton instance to access its runspace
        var instance = GetInstance();
        // Access the runspace through reflection for compatibility
        var runspaceField = typeof(PowerShellAssemblyGenerator).GetField("_powerShellRunspace",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var runspace = (IPowerShellRunspace)runspaceField!.GetValue(instance)!;

        return await PowerShellAssemblyGenerator.GetLastCommandOutput(runspace, logger, cancellationToken);
    }

    /// <summary>
    /// Sorts the cached output from the last executed PowerShell command using Sort-Object
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="property">Property name to sort by (optional)</param>
    /// <param name="descending">Whether to sort in descending order</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON-serialized sorted cached command output, or null if no cache exists</returns>
    public static async Task<string?> SortLastCommandOutput(
        ILogger logger,
        string? property = null,
        bool descending = false,
        CancellationToken cancellationToken = default)
    {
        // Get the singleton instance to access its runspace
        var instance = GetInstance();
        // Access the runspace through reflection for compatibility
        var runspaceField = typeof(PowerShellAssemblyGenerator).GetField("_powerShellRunspace",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var runspace = (IPowerShellRunspace)runspaceField!.GetValue(instance)!;

        return await PowerShellAssemblyGenerator.SortLastCommandOutput(runspace, logger, property, descending, cancellationToken);
    }

    /// <summary>
    /// Filters the cached output from the last executed PowerShell command using Where-Object
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="filterScript">PowerShell filter script block (e.g., "$_.Name -like 'dot*'")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON-serialized filtered cached command output, or null if no cache exists</returns>
    public static async Task<string?> FilterLastCommandOutput(
        ILogger logger,
        string? filterScript = null,
        CancellationToken cancellationToken = default)
    {
        // Get the singleton instance to access its runspace
        var instance = GetInstance();
        // Access the runspace through reflection for compatibility
        var runspaceField = typeof(PowerShellAssemblyGenerator).GetField("_powerShellRunspace",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var runspace = (IPowerShellRunspace)runspaceField!.GetValue(instance)!;

        return await PowerShellAssemblyGenerator.FilterLastCommandOutput(runspace, logger, filterScript, false, cancellationToken);
    }

}

/// <summary>
/// Information about a PowerShell parameter for use in generated methods
/// </summary>
public class PowerShellParameterInfo
{
    public string Name { get; }
    public Type Type { get; }
    public bool IsMandatory { get; }

    public PowerShellParameterInfo(string name, Type type, bool isMandatory)
    {
        Name = name;
        Type = type;
        IsMandatory = isMandatory;
    }
}
