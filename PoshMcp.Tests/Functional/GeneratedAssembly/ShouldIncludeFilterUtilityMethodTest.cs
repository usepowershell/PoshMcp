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

namespace PoshMcp.Tests.Functional.GeneratedAssembly;

/// <summary>
/// Test for generated assembly filter utility method
/// </summary>
public partial class GeneratedInstance : PowerShellTestBase
{

    [Fact]
    public void ShouldIncludeFilterUtilityMethod()
    {
        // Arrange - Generate an assembly with some basic commands
        var commands = new List<CommandInfo>();

        // Act - Generate the assembly
        var assembly = AssemblyGenerator.GenerateAssembly(commands, Logger);
        var instance = AssemblyGenerator.GetGeneratedInstance(Logger);
        var type = instance.GetType();

        // Assert - Check that the filter utility method exists
        var filterMethod = type.GetMethod("filter_last_command_output");

        Assert.NotNull(filterMethod);
        Logger.LogInformation($"Found filter method: {filterMethod.Name}");

        // Verify method signature
        Assert.Equal(typeof(Task<string>), filterMethod.ReturnType);

        var filterParameters = filterMethod.GetParameters();
        Assert.Equal(3, filterParameters.Length);
        Assert.Equal(typeof(string), filterParameters[0].ParameterType); // filterScript
        Assert.Equal(typeof(bool), filterParameters[1].ParameterType); // updateCache
        Assert.Equal(typeof(CancellationToken), filterParameters[2].ParameterType); // cancellationToken

        Logger.LogInformation("Generated assembly includes filter utility method with correct signature");
    }
}
