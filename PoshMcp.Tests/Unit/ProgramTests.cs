using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Unit tests for Program class extracted methods
/// </summary>
public class ProgramTests : PowerShellTestBase
{
    public ProgramTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task ResolveConfigurationPath_WhenConfigPathExists_ReturnsExistingPath()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            // Act
            var result = await Program.ResolveConfigurationPath(tempFile);

            // Assert
            Assert.Equal(tempFile, result);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ResolveConfigurationPath_WhenCurrentDirectoryHasAppSettings_ReturnsAppSettings()
    {
        // Arrange
        var originalDirectory = Directory.GetCurrentDirectory();
        var tempDir = Path.GetTempPath();
        var appSettingsPath = Path.Combine(tempDir, "appsettings.json");
        var configPath = Path.Combine(tempDir, "config.json");

        try
        {
            Directory.SetCurrentDirectory(tempDir);
            await File.WriteAllTextAsync(appSettingsPath, "{}");

            // Act
            var result = await Program.ResolveConfigurationPath(configPath);

            // Assert
            Assert.Equal(appSettingsPath, result);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            if (File.Exists(appSettingsPath))
                File.Delete(appSettingsPath);
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public async Task ResolveConfigurationPath_WhenNeitherExists_CreatesDefaultAndReturnsPath()
    {
        // Arrange
        var originalDirectory = Directory.GetCurrentDirectory();
        var tempDir = Path.GetTempPath();
        var configPath = Path.Combine(tempDir, $"test_config_{Path.GetRandomFileName()}.json");
        var tempWorkingDir = Path.Combine(tempDir, Path.GetRandomFileName());
        var userConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PoshMcp",
            "appsettings.json");
        var userConfigExistedBefore = File.Exists(userConfigPath);
        Directory.CreateDirectory(tempWorkingDir);

        try
        {
            // Change to a directory that doesn't have appsettings.json
            Directory.SetCurrentDirectory(tempWorkingDir);

            // Act
            var result = await Program.ResolveConfigurationPath(configPath);

            // Assert
            Assert.Equal(userConfigPath, result);
            Assert.True(File.Exists(userConfigPath));
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            if (File.Exists(configPath))
                File.Delete(configPath);
            if (!userConfigExistedBefore && File.Exists(userConfigPath))
                File.Delete(userConfigPath);
            if (Directory.Exists(tempWorkingDir))
                Directory.Delete(tempWorkingDir, true);
        }
    }

    [Fact]
    public void CreateLoggerFactory_WithValidLogLevel_ReturnsLoggerFactory()
    {
        // Arrange
        var logLevel = LogLevel.Information;

        // Act
        using var loggerFactory = Program.CreateLoggerFactory(logLevel);

        // Assert
        Assert.NotNull(loggerFactory);
        var logger = loggerFactory.CreateLogger("Test");
        Assert.NotNull(logger);
    }

    [Fact]
    public void LoadPowerShellConfiguration_WithValidConfigPath_ReturnsConfiguration()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var configJson = @"{
  ""PowerShellConfiguration"": {
    ""FunctionNames"": [""Get-Process""],
    ""Modules"": [],
    ""ExcludePatterns"": [],
    ""IncludePatterns"": []
  }
}";
        File.WriteAllText(tempFile, configJson);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger("Test");

        try
        {
            // Act
            var config = Program.LoadPowerShellConfiguration(tempFile, logger);

            // Assert
            Assert.NotNull(config);
            Assert.Single(config.FunctionNames);
            Assert.Equal("Get-Process", config.FunctionNames[0]);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadPowerShellConfiguration_WithEnableDynamicReloadToolsTrue_ReturnsConfigurationWithFlagEnabled()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var configJson = @"{
  ""PowerShellConfiguration"": {
    ""FunctionNames"": [""Get-Process""],
    ""Modules"": [],
    ""ExcludePatterns"": [],
    ""IncludePatterns"": [],
    ""EnableDynamicReloadTools"": true
  }
}";
        File.WriteAllText(tempFile, configJson);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger("Test");

        try
        {
            // Act
            var config = Program.LoadPowerShellConfiguration(tempFile, logger);

            // Assert
            Assert.NotNull(config);
            Assert.True(config.EnableDynamicReloadTools);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadPowerShellConfiguration_WithEnableDynamicReloadToolsFalse_ReturnsConfigurationWithFlagDisabled()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var configJson = @"{
  ""PowerShellConfiguration"": {
    ""FunctionNames"": [""Get-Process""],
    ""Modules"": [],
    ""ExcludePatterns"": [],
    ""IncludePatterns"": [],
    ""EnableDynamicReloadTools"": false
  }
}";
        File.WriteAllText(tempFile, configJson);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger("Test");

        try
        {
            // Act
            var config = Program.LoadPowerShellConfiguration(tempFile, logger);

            // Assert
            Assert.NotNull(config);
            Assert.False(config.EnableDynamicReloadTools);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadPowerShellConfiguration_WithoutEnableDynamicReloadToolsProperty_ReturnsConfigurationWithDefaultFalse()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var configJson = @"{
  ""PowerShellConfiguration"": {
    ""FunctionNames"": [""Get-Process""],
    ""Modules"": [],
    ""ExcludePatterns"": [],
    ""IncludePatterns"": []
  }
}";
        File.WriteAllText(tempFile, configJson);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger("Test");

        try
        {
            // Act
            var config = Program.LoadPowerShellConfiguration(tempFile, logger);

            // Assert
            Assert.NotNull(config);
            Assert.False(config.EnableDynamicReloadTools); // Should default to false
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}