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

namespace PoshMcp.Tests.Functional.GeneratedAssembly;

/// <summary>
/// Test for generated assembly sort utility method
/// </summary>
public partial class GeneratedInstance : PowerShellTestBase
{
    [Fact]
    public void ShouldIncludeSortUtilityMethods()
    {
        // Arrange - Generate an assembly with some basic commands
        var commands = new List<CommandInfo>();
        // We don't need actual commands for this test since we're just checking for utility methods

        // Act - Generate the assembly
        var assembly = AssemblyGenerator.GenerateAssembly(commands, Logger);
        var instance = AssemblyGenerator.GetGeneratedInstance(Logger);
        var type = instance.GetType();

        // Assert - Check that the sort utility methods exist
        var getSortMethod = type.GetMethod("sort_last_command_output");
        var getLastMethod = type.GetMethod("get_last_command_output");

        Assert.NotNull(getSortMethod);
        Assert.NotNull(getLastMethod);

        Logger.LogInformation($"Found sort method: {getSortMethod.Name}");
        Logger.LogInformation($"Found get last method: {getLastMethod.Name}");

        // Verify method signatures
        Assert.Equal(typeof(Task<string>), getSortMethod.ReturnType);
        Assert.Equal(typeof(Task<string>), getLastMethod.ReturnType);

        var sortParameters = getSortMethod.GetParameters();
        Assert.Equal(3, sortParameters.Length);
        Assert.Equal(typeof(string), sortParameters[0].ParameterType); // property
        Assert.Equal(typeof(bool), sortParameters[1].ParameterType); // descending
        Assert.Equal(typeof(CancellationToken), sortParameters[2].ParameterType); // cancellationToken

        var getLastParameters = getLastMethod.GetParameters();
        Assert.Single(getLastParameters);
        Assert.Equal(typeof(CancellationToken), getLastParameters[0].ParameterType); // cancellationToken

        Logger.LogInformation("Generated assembly includes sort utility methods with correct signatures");
    }
}
