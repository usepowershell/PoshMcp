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
/// Test for tools list generation with empty configuration
/// </summary>
public partial class SetupTests : PowerShellTestBase
{
    [Fact]
    public void GetToolsList_WithEmptyConfiguration_ShouldReturnEmptyList()
    {
        // Arrange
        var config = new PowerShellConfiguration
        {
            FunctionNames = new List<string>(),
            Modules = new List<string>(),
            ExcludePatterns = new List<string>(),
            IncludePatterns = new List<string>()
        };

        // Act
        var tools = McpToolFactoryV2.GetToolsList(config, Logger);

        // Assert
        Assert.NotNull(tools);
        Assert.Empty(tools);
        Logger.LogInformation("Empty configuration correctly returned no tools");
    }
}
