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
/// Test for configuration handling with invalid JSON
/// </summary>
public partial class SetupTests : PowerShellTestBase
{
    [Fact]
    public async Task Configuration_InvalidJsonFile_ShouldThrowException()
    {
        // Arrange
        var tempConfigFile = Path.GetTempFileName();
        var invalidJson = @"{ invalid json content }";

        try
        {
            await File.WriteAllTextAsync(tempConfigFile, invalidJson);

            // Act & Assert
            var exception = Assert.Throws<System.IO.InvalidDataException>(() =>
            {
                var configuration = new ConfigurationBuilder()
                    .AddJsonFile(tempConfigFile, optional: false)
                    .Build();
            });

            Logger.LogInformation($"Invalid JSON correctly threw exception: {exception.Message}");
        }
        finally
        {
            if (File.Exists(tempConfigFile))
            {
                File.Delete(tempConfigFile);
            }
        }
    }
}
