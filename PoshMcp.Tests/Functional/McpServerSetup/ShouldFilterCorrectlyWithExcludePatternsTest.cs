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
/// Test for tools list filtering with exclude patterns
/// </summary>
public partial class SetupTests : PowerShellTestBase
{
    [Fact]
    public void GetToolsList_WithExcludePatterns_ShouldFilterCorrectly()
    {
        // Arrange
        var config = new PowerShellConfiguration
        {
            FunctionNames = new List<string> { "Get-Process", "Get-Service", "Get-ChildItem" },
            Modules = new List<string>(),
            ExcludePatterns = new List<string> { "*Service*" },
            IncludePatterns = new List<string>()
        };

        // Act
        var tools = ToolFactory.GetToolsList(config, Logger);

        // Assert
        Assert.NotNull(tools);
        var toolNames = tools.Select(t => t.ProtocolTool.Name).ToList();
        Logger.LogInformation($"Tools after exclude pattern: {string.Join(", ", toolNames)}");

        // Should not contain any service-related tools
        Assert.DoesNotContain(toolNames, name => name.Contains("Service", StringComparison.OrdinalIgnoreCase));
    }
}
