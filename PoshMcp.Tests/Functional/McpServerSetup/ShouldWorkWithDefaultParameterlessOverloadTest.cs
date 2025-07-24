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
/// Test for default parameterless GetToolsList overload
/// </summary>
public partial class SetupTests : PowerShellTestBase
{
    [Fact]
    public void GetToolsList_WithDefaultParameterlessOverload_ShouldWork()
    {
        // Act
        var tools = McpToolFactoryV2.GetToolsList(Logger);

        // Assert
        Assert.NotNull(tools);
        Logger.LogInformation($"Parameterless GetToolsList returned {tools.Count} tools");
    }
}
