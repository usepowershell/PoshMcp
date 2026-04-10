using Microsoft.Extensions.Logging;
using PoshMcp.Server.PowerShell;
using System;
using System.Collections.Generic;
using System.IO;
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

    [Fact]
    public void GetToolsList_WithConfiguredModuleAndAutoloadDisabled_ImportsModuleBeforeNameDiscovery()
    {
        var moduleName = "HermesModule";
        var commandName = "Get-HermesValue";
        var tempRoot = Path.Combine(Path.GetTempPath(), "poshmcp-tests", Guid.NewGuid().ToString("N"));
        var moduleDir = Path.Combine(tempRoot, moduleName);
        Directory.CreateDirectory(moduleDir);

        var moduleFilePath = Path.Combine(moduleDir, moduleName + ".psm1");
        File.WriteAllText(moduleFilePath, @"
function Get-HermesValue {
    return 'ok'
}

Export-ModuleMember -Function Get-HermesValue
");

        var runspace = new IsolatedPowerShellRunspace();
        try
        {
            runspace.ExecuteThreadSafe(ps =>
            {
                ps.Commands.Clear();
                ps.AddScript($"$PSModuleAutoLoadingPreference = 'None'; $env:PSModulePath = '{tempRoot.Replace("'", "''")}{Path.PathSeparator}' + $env:PSModulePath");
                ps.Invoke();
                ps.Commands.Clear();
            });

            var toolFactory = new McpToolFactoryV2(runspace);
            var config = new PowerShellConfiguration
            {
                FunctionNames = new List<string> { commandName },
                Modules = new List<string> { moduleName }
            };

            var tools = toolFactory.GetToolsList(config, Logger);
            Assert.Contains(tools, t => t.ProtocolTool.Name.StartsWith("get_hermes_value", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            runspace.Dispose();
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}