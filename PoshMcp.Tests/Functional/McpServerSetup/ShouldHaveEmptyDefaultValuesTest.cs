using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using PoshMcp.Server.PowerShell;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Functional.McpServerSetup;

/// <summary>
/// Test for PowerShellConfiguration default values
/// </summary>
public partial class SetupTests : PowerShellTestBase
{
    [Fact]
    public void PowerShellConfiguration_DefaultValues_ShouldBeEmpty()
    {
        // Arrange & Act
        var config = new PowerShellConfiguration();

        // Assert
        Assert.NotNull(config.FunctionNames);
        Assert.Empty(config.FunctionNames);

        Assert.NotNull(config.Modules);
        Assert.Empty(config.Modules);

        Assert.NotNull(config.ExcludePatterns);
        Assert.Empty(config.ExcludePatterns);

        Assert.NotNull(config.IncludePatterns);
        Assert.Empty(config.IncludePatterns);

        Logger.LogInformation("Default PowerShellConfiguration has correct empty values");
    }
}
