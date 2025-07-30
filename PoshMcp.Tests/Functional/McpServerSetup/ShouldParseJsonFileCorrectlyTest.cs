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
/// Test for configuration loading from JSON file
/// </summary>
public partial class SetupTests : PowerShellTestBase
{
    [Fact]
    public async Task Configuration_LoadFromJsonFile_ShouldParseCorrectly()
    {
        // Arrange
        var tempConfigFile = Path.GetTempFileName();
        var configJson = @"{
  ""PowerShellConfiguration"": {
    ""FunctionNames"": [
      ""Get-Process"",
      ""Get-Service"",
      ""Get-Date""
    ],
    ""Modules"": [
      ""Microsoft.PowerShell.Management""
    ],
    ""ExcludePatterns"": [
      ""*-EventLog""
    ],
    ""IncludePatterns"": [
      ""Get-*""
    ]
  }
}";

        try
        {
            await File.WriteAllTextAsync(tempConfigFile, configJson);

            // Act
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(tempConfigFile, optional: false)
                .Build();

            var powerShellConfig = new PowerShellConfiguration();
            configuration.GetSection("PowerShellConfiguration").Bind(powerShellConfig);

            // Assert
            Assert.NotNull(powerShellConfig);
            Assert.Equal(3, powerShellConfig.FunctionNames.Count);
            Assert.Contains("Get-Process", powerShellConfig.FunctionNames);
            Assert.Contains("Get-Service", powerShellConfig.FunctionNames);
            Assert.Contains("Get-Date", powerShellConfig.FunctionNames);

            Assert.Single(powerShellConfig.Modules);
            Assert.Contains("Microsoft.PowerShell.Management", powerShellConfig.Modules);

            Assert.Single(powerShellConfig.ExcludePatterns);
            Assert.Contains("*-EventLog", powerShellConfig.ExcludePatterns);

            Assert.Single(powerShellConfig.IncludePatterns);
            Assert.Contains("Get-*", powerShellConfig.IncludePatterns);

            Logger.LogInformation("Configuration file parsed successfully");
            Logger.LogInformation($"Function names: {string.Join(", ", powerShellConfig.FunctionNames)}");
            Logger.LogInformation($"Modules: {string.Join(", ", powerShellConfig.Modules)}");
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
