using System;
using System.Linq;
using System.Management.Automation;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PoshMcp.PowerShell;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Tests for examining PowerShell command OutputType information
/// </summary>
public class OutputTypeTests : PowerShellTestBase
{
    public OutputTypeTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task ExamineCommonCommandOutputTypes()
    {
        // Arrange
        var commandNames = new[] { "Get-Process", "Get-ChildItem", "Get-Content", "Get-Date", "Test-Path" };

        foreach (var cmdName in commandNames)
        {
            try
            {
                // Act - Use thread-safe execution
                var cmdInfo = await PowerShellRunspace.ExecuteThreadSafeAsync<CommandInfo?>(ps =>
                {
                    ps.Commands.Clear();
                    ps.AddCommand("Get-Command").AddParameter("Name", cmdName);
                    var result = ps.Invoke<CommandInfo>();
                    ps.Commands.Clear();
                    return Task.FromResult(result.FirstOrDefault());
                });

                // Assert and Log
                Assert.NotNull(cmdInfo);
                Logger.LogInformation($"\n=== {cmdInfo.Name} ===");
                Logger.LogInformation($"Command Type: {cmdInfo.GetType().Name}");

                // Check if it's a CmdletInfo which has OutputType
                if (cmdInfo is CmdletInfo cmdletInfo)
                {
                    Logger.LogInformation($"OutputType Count: {cmdletInfo.OutputType.Count}");
                    foreach (var outputType in cmdletInfo.OutputType)
                    {
                        Logger.LogInformation($"  OutputType: {outputType.Type}");
                        Logger.LogInformation($"  OutputType Name: {outputType.Name}");
                        if (outputType.Type != null)
                        {
                            Logger.LogInformation($"  .NET Type: {outputType.Type.FullName}");
                        }
                    }
                }
                else if (cmdInfo is FunctionInfo functionInfo)
                {
                    Logger.LogInformation($"OutputType Count: {functionInfo.OutputType.Count}");
                    foreach (var outputType in functionInfo.OutputType)
                    {
                        Logger.LogInformation($"  OutputType: {outputType.Type}");
                        Logger.LogInformation($"  OutputType Name: {outputType.Name}");
                        if (outputType.Type != null)
                        {
                            Logger.LogInformation($"  .NET Type: {outputType.Type.FullName}");
                        }
                    }
                }
                else
                {
                    Logger.LogInformation($"Command type {cmdInfo.GetType().Name} doesn't have OutputType information");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting info for {cmdName}: {ex.Message}");
            }
        }
    }

    [Fact]
    public async Task DetermineReturnTypeFromOutputType()
    {
        // Arrange
        var commandNames = new[] { "Get-Process", "Get-ChildItem", "Get-Date" };

        foreach (var cmdName in commandNames)
        {
            try
            {
                // Act - Use thread-safe execution
                var cmdInfo = await PowerShellRunspace.ExecuteThreadSafeAsync<CommandInfo?>(ps =>
                {
                    ps.Commands.Clear();
                    ps.AddCommand("Get-Command").AddParameter("Name", cmdName);
                    var result = ps.Invoke<CommandInfo>();
                    ps.Commands.Clear();
                    return Task.FromResult(result.FirstOrDefault());
                });

                // Determine return type
                if (cmdInfo == null)
                {
                    Logger.LogWarning($"Command {cmdName} not found");
                    Assert.Fail($"Command {cmdName} not found");
                }
                var returnType = DetermineReturnType(cmdInfo);

                // Assert and Log
                Assert.NotNull(cmdInfo);
                Logger.LogInformation($"\n=== {cmdInfo.Name} Return Type Analysis ===");
                Logger.LogInformation($"Determined Return Type: {returnType.FullName}");
                Logger.LogInformation($"Is Generic: {returnType.IsGenericType}");
                if (returnType.IsGenericType)
                {
                    Logger.LogInformation($"Generic Type Definition: {returnType.GetGenericTypeDefinition().FullName}");
                    Logger.LogInformation($"Generic Arguments: {string.Join(", ", returnType.GetGenericArguments().Select(t => t.FullName))}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error analyzing return type for {cmdName}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Determines the appropriate return type for a PowerShell command based on its OutputType metadata
    /// </summary>
    private Type DetermineReturnType(CommandInfo commandInfo)
    {
        if (commandInfo == null)
            return typeof(Task<PSObject[]>);

        // Get OutputType information
        var outputTypes = commandInfo switch
        {
            CmdletInfo cmdlet => cmdlet.OutputType,
            FunctionInfo function => function.OutputType,
            _ => null
        };

        if (outputTypes == null || outputTypes.Count == 0)
        {
            // No output type information - default to PSObject array
            return typeof(Task<PSObject[]>);
        }

        if (outputTypes.Count == 1)
        {
            var outputType = outputTypes[0];
            if (outputType.Type != null)
            {
                // Single known output type - return Task<T[]> where T is the output type
                var arrayType = outputType.Type.MakeArrayType();
                return typeof(Task<>).MakeGenericType(arrayType);
            }
            else
            {
                // Single output type but unknown .NET type - default to PSObject array
                return typeof(Task<PSObject[]>);
            }
        }
        else
        {
            // Multiple output types - check if they have a common base type
            var types = outputTypes.Where(ot => ot.Type != null).Select(ot => ot.Type).ToArray();

            if (types.Length == 0)
            {
                // No known .NET types - default to PSObject array
                return typeof(Task<PSObject[]>);
            }

            if (types.Length == 1)
            {
                // Only one known .NET type among multiple output types
                var arrayType = types[0].MakeArrayType();
                return typeof(Task<>).MakeGenericType(arrayType);
            }

            // Multiple known .NET types - find common base type
            var commonType = FindCommonBaseType(types);
            var commonArrayType = commonType.MakeArrayType();
            return typeof(Task<>).MakeGenericType(commonArrayType);
        }
    }

    /// <summary>
    /// Finds the most specific common base type for an array of types
    /// </summary>
    private Type FindCommonBaseType(Type[] types)
    {
        if (types.Length == 0)
            return typeof(PSObject);

        if (types.Length == 1)
            return types[0];

        var commonType = types[0];

        for (int i = 1; i < types.Length; i++)
        {
            commonType = FindCommonBaseType(commonType, types[i]);
        }

        return commonType;
    }

    /// <summary>
    /// Finds the common base type between two types
    /// </summary>
    private Type FindCommonBaseType(Type type1, Type type2)
    {
        if (type1 == type2)
            return type1;

        if (type1.IsAssignableFrom(type2))
            return type1;

        if (type2.IsAssignableFrom(type1))
            return type2;

        // Walk up the inheritance hierarchy
        var current = type1;
        while (current != null && current != typeof(object))
        {
            if (current.IsAssignableFrom(type2))
                return current;
            current = current.BaseType;
        }

        // No common base type found other than object - use PSObject for PowerShell compatibility
        return typeof(PSObject);
    }
}
