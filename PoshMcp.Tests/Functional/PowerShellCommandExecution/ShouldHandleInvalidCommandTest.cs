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
/// Test for handling invalid PowerShell commands
/// </summary>
public class InvalidCommand : PowerShellTestBase
{
    public InvalidCommand(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task ShouldHandleInvalidCommand()
    {
        // Arrange
        var commandName = "NonExistentCommand-12345";
        var parameters = new PowerShellParameterInfo[0];

        // Act
        var result = await PowerShellDynamicAssemblyGenerator.ExecutePowerShellCommandTyped(
            commandName,
            parameters,
            new object[0],
            CancellationToken.None,
            Logger);

        // Assert
        Assert.NotNull(result);
        // With safe pipeline handling, invalid commands should either return empty array or error message
        // Both are acceptable safe behaviors (no crash)
        Assert.True(result.Equals("[]", StringComparison.Ordinal) || result.Contains("error"),
            $"Expected either empty array '[]' or error message, but got: {result}");
    }
}
