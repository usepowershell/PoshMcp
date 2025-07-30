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
/// Test for configuration handling with missing file
/// </summary>
public partial class SetupTests : PowerShellTestBase
{
    [Fact]
    public void Configuration_MissingFile_ShouldThrowException()
    {
        // Arrange
        var nonExistentFile = Path.Combine(Path.GetTempPath(), "nonexistent-config.json");

        // Act & Assert
        var exception = Assert.Throws<FileNotFoundException>(() =>
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(nonExistentFile, optional: false)
                .Build();
        });

        Logger.LogInformation($"Missing file correctly threw exception: {exception.Message}");
    }
}
