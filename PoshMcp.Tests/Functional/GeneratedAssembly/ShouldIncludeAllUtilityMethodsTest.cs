using PoshMcp.PowerShell;
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
/// Test for generated assembly including all utility methods
/// </summary>
public partial class UtilityMethods : PowerShellTestBase
{
    public UtilityMethods(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void ShouldIncludeAllUtilityMethods()
    {
        // Arrange - Generate an assembly with some basic commands
        var commands = new List<CommandInfo>();

        // Act - Generate the assembly
        var assembly = AssemblyGenerator.GenerateAssembly(commands, Logger);
        var instance = AssemblyGenerator.GetGeneratedInstance(Logger);
        var type = instance.GetType();

        // Assert - Check that all utility methods exist
        var getLastMethod = type.GetMethod("get_last_command_output");
        var sortMethod = type.GetMethod("sort_last_command_output");
        var filterMethod = type.GetMethod("filter_last_command_output");

        Assert.NotNull(getLastMethod);
        Assert.NotNull(sortMethod);
        Assert.NotNull(filterMethod);

        Logger.LogInformation("Generated assembly includes all utility methods: get_last_command_output, sort_last_command_output, filter_last_command_output");
    }
}
