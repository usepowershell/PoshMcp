using Microsoft.Extensions.Logging;
using PoshMcp.Server.PowerShell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
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
        await File.WriteAllTextAsync(tempFile, "{}");
        try
        {
            // Act
            var result = await CommandHandlers.ResolveConfigurationPath(tempFile);

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
    public async Task ResolveConfigurationPath_WhenExistingConfigMissingTopLevelSection_AddsMissingDefaultsOnly()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var configJson = @"{
    ""PowerShellConfiguration"": {
        ""FunctionNames"": [""Get-ChildItem""],
        ""Modules"": [],
        ""ExcludePatterns"": [],
        ""IncludePatterns"": []
    }
}";
        await File.WriteAllTextAsync(tempFile, configJson);

        try
        {
            // Act
            var result = await CommandHandlers.ResolveConfigurationPath(tempFile);

            // Assert
            Assert.Equal(tempFile, result);

            var upgradedRoot = JsonNode.Parse(await File.ReadAllTextAsync(tempFile))?.AsObject();
            Assert.NotNull(upgradedRoot);
            Assert.NotNull(upgradedRoot!["Logging"]);

            var functionNames = upgradedRoot["PowerShellConfiguration"]?["FunctionNames"]?.AsArray();
            Assert.NotNull(functionNames);
            Assert.Single(functionNames!);
            Assert.Equal("Get-ChildItem", functionNames[0]?.GetValue<string>());
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ResolveConfigurationPath_WhenExistingConfigMissingNestedKey_AddsNestedDefaultsWithoutOverwriting()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var configJson = @"{
    ""Logging"": {
        ""LogLevel"": {
            ""Default"": ""Warning""
        }
    },
    ""PowerShellConfiguration"": {
        ""FunctionNames"": [""Get-Process""],
        ""Modules"": [],
        ""ExcludePatterns"": [],
        ""IncludePatterns"": []
    }
}";
        await File.WriteAllTextAsync(tempFile, configJson);

        try
        {
            // Act
            _ = await CommandHandlers.ResolveConfigurationPath(tempFile);

            // Assert
            var upgradedRoot = JsonNode.Parse(await File.ReadAllTextAsync(tempFile))?.AsObject();
            Assert.NotNull(upgradedRoot);

            var logLevel = upgradedRoot!["Logging"]?["LogLevel"]?.AsObject();
            Assert.NotNull(logLevel);
            Assert.Equal("Warning", logLevel!["Default"]?.GetValue<string>());
            Assert.Equal("Information", logLevel["Microsoft.Hosting.Lifetime"]?.GetValue<string>());

            var dynamicReloadTools = upgradedRoot["PowerShellConfiguration"]?["EnableDynamicReloadTools"]?.GetValue<bool>();
            Assert.False(dynamicReloadTools);
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
            var result = await CommandHandlers.ResolveConfigurationPath(configPath);

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
            var result = await CommandHandlers.ResolveConfigurationPath(configPath);

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
        using var loggerFactory = LoggingHelpers.CreateLoggerFactory(logLevel);

        // Assert
        Assert.NotNull(loggerFactory);
        var logger = loggerFactory.CreateLogger("Test");
        Assert.NotNull(logger);
    }

    [Theory]
    [InlineData(null, "stdio")]
    [InlineData("", "stdio")]
    [InlineData("  ", "stdio")]
    [InlineData("stdio", "stdio")]
    [InlineData("STDIO", "stdio")]
    [InlineData("http", "http")]
    [InlineData("HTTP", "http")]
    [InlineData(" http ", "http")]
    [InlineData("sse", "sse")]
    public void NormalizeTransportValue_ReturnsExpectedNormalizedValue(string? input, string expected)
    {
        // Act
        var result = SettingsResolver.NormalizeTransportValue(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null, "Stdio")]
    [InlineData("", "Stdio")]
    [InlineData("stdio", "Stdio")]
    [InlineData("STDIO", "Stdio")]
    [InlineData("http", "Http")]
    [InlineData("HTTP", "Http")]
    [InlineData("sse", "Unsupported")]
    [InlineData("unknown", "Unsupported")]
    public void ResolveTransportMode_ReturnsExpectedMode(string? input, string expected)
    {
        // Act
        var result = SettingsResolver.ResolveTransportMode(input);

        // Assert
        Assert.Equal(expected, result.ToString());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("mcp", "/mcp")]
    [InlineData("/mcp", "/mcp")]
    [InlineData(" custom/path ", "/custom/path")]
    public void NormalizeMcpPath_ReturnsExpectedPath(string? input, string? expected)
    {
        // Act
        var result = SettingsResolver.NormalizeMcpPath(input);

        // Assert
        Assert.Equal(expected, result);
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
            var config = ConfigurationLoader.LoadPowerShellConfiguration(tempFile, logger);

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
            var config = ConfigurationLoader.LoadPowerShellConfiguration(tempFile, logger);

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
            var config = ConfigurationLoader.LoadPowerShellConfiguration(tempFile, logger);

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
            var config = ConfigurationLoader.LoadPowerShellConfiguration(tempFile, logger);

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

    [Fact]
    public void LoadPowerShellConfiguration_WithoutFile_UsesEnvironmentVariables()
    {
        // Arrange
        const string commandName = "Get-Date";
        var originalCommandName = Environment.GetEnvironmentVariable("PowerShellConfiguration__CommandNames__0");
        var originalDynamicReload = Environment.GetEnvironmentVariable("PowerShellConfiguration__EnableDynamicReloadTools");

        Environment.SetEnvironmentVariable("PowerShellConfiguration__CommandNames__0", commandName);
        Environment.SetEnvironmentVariable("PowerShellConfiguration__EnableDynamicReloadTools", "true");

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger("Test");

        try
        {
            // Act
            var config = ConfigurationLoader.LoadPowerShellConfiguration(string.Empty, logger);

            // Assert
            Assert.Contains(commandName, config.GetEffectiveCommandNames());
            Assert.True(config.EnableDynamicReloadTools);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PowerShellConfiguration__CommandNames__0", originalCommandName);
            Environment.SetEnvironmentVariable("PowerShellConfiguration__EnableDynamicReloadTools", originalDynamicReload);
        }
    }

    [Fact]
    public void HasEnvironmentAppSettingsOverrides_WithPowerShellConfigurationPrefix_ReturnsTrue()
    {
        var originalCommandName = Environment.GetEnvironmentVariable("PowerShellConfiguration__CommandNames__0");
        Environment.SetEnvironmentVariable("PowerShellConfiguration__CommandNames__0", "Get-Process");

        try
        {
            Assert.True(SettingsResolver.HasEnvironmentAppSettingsOverrides());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PowerShellConfiguration__CommandNames__0", originalCommandName);
        }
    }

    [Fact]
    public void SerializeEffectivePowerShellConfiguration_IncludesPerformanceAndEffectiveSettings()
    {
        // Arrange
        var config = new PowerShellConfiguration
        {
            FunctionNames = new() { "Get-Process" },
            Commands = new() { "Get-Service" },
            Modules = new() { "Microsoft.PowerShell.Management" },
            ExcludePatterns = new() { "*Secret*" },
            IncludePatterns = new() { "Get-*" },
            EnableDynamicReloadTools = true,
            Environment = new EnvironmentConfiguration
            {
                ImportModules = new() { "Pester" },
                ModulePaths = new() { "C:/Modules" },
                StartupScript = "Write-Host startup",
                StartupScriptPath = "./startup.ps1",
                TrustPSGallery = false,
                SkipPublisherCheck = false,
                AllowClobber = true,
                InstallTimeoutSeconds = 120
            },
            Performance = new PerformanceConfiguration
            {
                EnableResultCaching = true,
                UseDefaultDisplayProperties = false
            },
            CommandOverrides =
            {
                ["Get-Process"] = new FunctionOverride
                {
                    DefaultProperties = new() { "Name", "Id" },
                    EnableResultCaching = false,
                    UseDefaultDisplayProperties = true
                }
            }
        };

        // Act
        var json = Program.SerializeEffectivePowerShellConfiguration(config);
        var root = JsonNode.Parse(json)?.AsObject();

        // Assert
        Assert.NotNull(root);
        Assert.Equal("Get-Process", root!["FunctionNames"]?[0]?.GetValue<string>());
        Assert.Equal("Get-Service", root["Commands"]?[0]?.GetValue<string>());
        Assert.Equal("Microsoft.PowerShell.Management", root["Modules"]?[0]?.GetValue<string>());
        Assert.Equal("*Secret*", root["ExcludePatterns"]?[0]?.GetValue<string>());
        Assert.Equal("Get-*", root["IncludePatterns"]?[0]?.GetValue<string>());
        Assert.True(root["EnableDynamicReloadTools"]?.GetValue<bool>());

        Assert.True(root["Performance"]?["EnableResultCaching"]?.GetValue<bool>());
        Assert.False(root["Performance"]?["UseDefaultDisplayProperties"]?.GetValue<bool>());

        Assert.Equal("Pester", root["Environment"]?["ImportModules"]?[0]?.GetValue<string>());
        Assert.Equal("C:/Modules", root["Environment"]?["ModulePaths"]?[0]?.GetValue<string>());
        Assert.Equal("Write-Host startup", root["Environment"]?["StartupScript"]?.GetValue<string>());
        Assert.Equal("./startup.ps1", root["Environment"]?["StartupScriptPath"]?.GetValue<string>());
        Assert.False(root["Environment"]?["TrustPSGallery"]?.GetValue<bool>());
        Assert.False(root["Environment"]?["SkipPublisherCheck"]?.GetValue<bool>());
        Assert.True(root["Environment"]?["AllowClobber"]?.GetValue<bool>());
        Assert.Equal(120, root["Environment"]?["InstallTimeoutSeconds"]?.GetValue<int>());

        Assert.Equal("Name", root["CommandOverrides"]?["Get-Process"]?["DefaultProperties"]?[0]?.GetValue<string>());
        Assert.False(root["CommandOverrides"]?["Get-Process"]?["EnableResultCaching"]?.GetValue<bool>());
        Assert.True(root["CommandOverrides"]?["Get-Process"]?["UseDefaultDisplayProperties"]?.GetValue<bool>());
    }

    [Fact]
    public void BuildDoctorJson_WithEnvironmentModulePaths_IncludesResolvedOopModulePaths()
    {
        // Arrange
        var configPath = Path.Combine(Path.GetTempPath(), $"poshmcp-doctor-json-{Guid.NewGuid():N}", "appsettings.json");
        var relativeModulePath = ".\\Modules\\Custom";
        var config = new PowerShellConfiguration
        {
            FunctionNames = new() { "Get-Date" },
            Environment = new EnvironmentConfiguration
            {
                ModulePaths = new() { relativeModulePath }
            }
        };

        // Act
        var report = Program.BuildDoctorReportFromConfig(
            configurationPath: configPath,
            configurationPathSource: "test",
            effectiveLogLevel: "Information",
            effectiveLogLevelSource: "test",
            effectiveTransport: "stdio",
            effectiveTransportSource: "test",
            effectiveSessionMode: null,
            effectiveSessionModeSource: "test",
            effectiveRuntimeMode: "InProcess",
            effectiveRuntimeModeSource: "test",
            effectiveMcpPath: null,
            effectiveMcpPathSource: "test",
            config: config,
            tools: new List<ModelContextProtocol.Server.McpServerTool>());
        var json = Program.BuildDoctorJson(report);
        var root = JsonNode.Parse(json)?.AsObject();

        // Assert
        Assert.NotNull(root);
        var oopModulePaths = root!["powerShell"]?["oopModulePaths"]?.AsArray()
            .Select(node => node?.GetValue<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray()
            ?? Array.Empty<string>();

        var expectedResolvedPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath))!, relativeModulePath));
        Assert.Contains(expectedResolvedPath, oopModulePaths);
        Assert.Equal(oopModulePaths.Length, root["powerShell"]?["oopModulePathEntries"]?.GetValue<int>());
    }

    [Fact]
    public void BuildDoctorReportFromConfig_EnvironmentOnlyConfiguration_SetsEnvironmentOnlyMode()
    {
        // Arrange
        var config = new PowerShellConfiguration
        {
            FunctionNames = new() { "Get-Date" }
        };

        // Act
        var report = Program.BuildDoctorReportFromConfig(
            configurationPath: "(environment-only configuration)",
            configurationPathSource: SettingsResolver.EnvSource,
            effectiveLogLevel: "Information",
            effectiveLogLevelSource: "test",
            effectiveTransport: "http",
            effectiveTransportSource: "test",
            effectiveSessionMode: null,
            effectiveSessionModeSource: "test",
            effectiveRuntimeMode: "InProcess",
            effectiveRuntimeModeSource: "test",
            effectiveMcpPath: null,
            effectiveMcpPathSource: "test",
            config: config,
            tools: new List<ModelContextProtocol.Server.McpServerTool>());

        // Assert
        Assert.Equal("environment-only", report.RuntimeSettings.ConfigurationMode.Value);
        Assert.Equal(SettingsResolver.EnvSource, report.RuntimeSettings.ConfigurationMode.Source);
    }
}