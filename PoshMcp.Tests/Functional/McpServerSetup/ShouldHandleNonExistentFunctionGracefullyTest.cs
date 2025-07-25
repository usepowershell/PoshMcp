using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using PoshMcp.PowerShell;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Functional.McpServerSetup;

/// <summary>
/// Test for tools list generation with non-existent function
/// </summary>
public partial class SetupTests : PowerShellTestBase
{
    [Fact]
    public void GetToolsList_WithNonExistentFunction_ShouldHandleGracefully()
    {
        // Arrange
        var config = new PowerShellConfiguration
        {
            FunctionNames = new List<string> { "NonExistentFunction-12345", "Get-Process" },
            Modules = new List<string>(),
            ExcludePatterns = new List<string>(),
            IncludePatterns = new List<string>()
        };

        // Act
        var tools = ToolFactory.GetToolsList(config, Logger);

        // Assert
        Assert.NotNull(tools);
        // Should still return tools for valid functions, ignoring invalid ones
        Logger.LogInformation($"Configuration with invalid function returned {tools.Count} tools");
    }
}
