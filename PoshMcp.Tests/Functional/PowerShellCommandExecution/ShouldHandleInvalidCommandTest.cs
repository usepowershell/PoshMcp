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

namespace PoshMcp.Tests.Functional.PowerShellCommandExecution;

/// <summary>
/// Test for handling invalid PowerShell commands
/// </summary>
[Trait("Category", "Functional")]
public class InvalidCommand : PowerShellTestBase
{
    public InvalidCommand(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task UnknownCommand_ThrowsExecutionError()
    {
        // Arrange
        var commandName = "NonExistentCommand-UnknownCommand_ReturnsStructuredErrorJson_NotEmptyArray";
        var parameters = Array.Empty<PowerShellParameterInfo>();

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PowerShellAssemblyGenerator.ExecutePowerShellCommandTyped(
                commandName,
                parameters,
                Array.Empty<object>(),
                CancellationToken.None,
                PowerShellRunspace,
                Logger));

        Assert.Contains(commandName, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not recognized", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldHandleInvalidCommand()
    {
        // Arrange
        var commandName = "NonExistentCommand-12345";
        var parameters = new PowerShellParameterInfo[0];

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PowerShellAssemblyGenerator.ExecutePowerShellCommandTyped(
                commandName,
                parameters,
                Array.Empty<object>(),
                CancellationToken.None,
                PowerShellRunspace,
                Logger));

        Assert.Contains(commandName, exception.Message, StringComparison.Ordinal);
        Assert.Contains("not recognized", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
