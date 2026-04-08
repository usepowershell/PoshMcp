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
    public async Task UnknownCommand_ReturnsStructuredErrorJson_NotEmptyArray()
    {
        // Arrange
        var commandName = "NonExistentCommand-UnknownCommand_ReturnsStructuredErrorJson_NotEmptyArray";
        var parameters = Array.Empty<PowerShellParameterInfo>();

        // Act
        var result = await PowerShellAssemblyGenerator.ExecutePowerShellCommandTyped(
            commandName,
            parameters,
            Array.Empty<object>(),
            CancellationToken.None,
            PowerShellRunspace,
            Logger);

        // Assert: response is not null/empty and not the historical empty-array regression payload
        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.NotEqual("[]", result.Trim());

        // Assert: response is valid JSON object with a non-empty error property
        var token = JToken.Parse(result);
        var json = Assert.IsType<JObject>(token);
        var error = json["error"]?.Value<string>();

        Assert.False(string.IsNullOrWhiteSpace(error), $"Expected a non-empty error message JSON payload, but got: {result}");
        Assert.Contains(commandName, error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not recognized", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldHandleInvalidCommand()
    {
        // Arrange
        var commandName = "NonExistentCommand-12345";
        var parameters = new PowerShellParameterInfo[0];

        // Act
        var result = await PowerShellAssemblyGenerator.ExecutePowerShellCommandTyped(
            commandName,
            parameters,
            new object[0],
            CancellationToken.None,
            PowerShellRunspace,
            Logger);

        // Assert
        Assert.NotNull(result);
        var json = JObject.Parse(result);
        var error = json["error"]?.Value<string>();

        Assert.False(string.IsNullOrWhiteSpace(error), $"Expected a non-empty error message JSON payload, but got: {result}");
        Assert.Contains(commandName, error, StringComparison.Ordinal);
        Assert.Contains("not recognized", error, StringComparison.OrdinalIgnoreCase);
    }
}
