using Microsoft.Extensions.Logging;
using System.Linq;
using System.Management.Automation;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Unit tests for McpToolFactoryV2 class extracted methods
/// </summary>
public class McpToolFactoryV2Tests : PowerShellTestBase
{
    public McpToolFactoryV2Tests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void CreateDefaultCommandMetadata_CreatesMetadataWithDefaults()
    {
        // This test verifies the method creates metadata with expected default values
        // We'll create a minimal test command info for testing purposes
        using var powerShell = System.Management.Automation.PowerShell.Create();
        powerShell.AddCommand("Get-Command").AddParameter("Name", "Get-Process");
        var commandInfos = powerShell.Invoke<CommandInfo>();

        if (commandInfos.Count > 0)
        {
            var commandInfo = commandInfos.First();

            // Act
            var metadata = McpToolFactoryV2.CreateDefaultCommandMetadata(commandInfo);

            // Assert
            Assert.NotNull(metadata);
            Assert.Equal(commandInfo.Name, metadata.CommandName);
            Assert.Equal(commandInfo.Name, metadata.Description);
            Assert.False(metadata.IsDestructive);
            Assert.False(metadata.IsReadOnly);
            Assert.False(metadata.IsIdempotent);
        }
        else
        {
            // Skip test if Get-Process is not available
            Assert.True(true, "Skipping test - Get-Process command not available");
        }
    }

    [Fact]
    public void ExtractVerbFromCommandName_WithHyphenatedCommand_ReturnsVerbPart()
    {
        // Act
        var verb = McpToolFactoryV2.ExtractVerbFromCommandName("Get-Process");

        // Assert
        Assert.Equal("Get", verb);
    }

    [Fact]
    public void ExtractVerbFromCommandName_WithSingleWordCommand_ReturnsFullCommand()
    {
        // Act
        var verb = McpToolFactoryV2.ExtractVerbFromCommandName("Test");

        // Assert
        Assert.Equal("Test", verb);
    }

    [Fact]
    public void IsReadOnlyVerb_WithGetVerb_ReturnsTrue()
    {
        // Act
        var isReadOnly = McpToolFactoryV2.IsReadOnlyVerb("Get");

        // Assert
        Assert.True(isReadOnly);
    }

    [Fact]
    public void IsReadOnlyVerb_WithSetVerb_ReturnsFalse()
    {
        // Act
        var isReadOnly = McpToolFactoryV2.IsReadOnlyVerb("Set");

        // Assert
        Assert.False(isReadOnly);
    }

    [Fact]
    public void IsDestructiveVerb_WithRemoveVerb_ReturnsTrue()
    {
        // Act
        var isDestructive = McpToolFactoryV2.IsDestructiveVerb("Remove");

        // Assert
        Assert.True(isDestructive);
    }

    [Fact]
    public void IsDestructiveVerb_WithGetVerb_ReturnsFalse()
    {
        // Act
        var isDestructive = McpToolFactoryV2.IsDestructiveVerb("Get");

        // Assert
        Assert.False(isDestructive);
    }
}