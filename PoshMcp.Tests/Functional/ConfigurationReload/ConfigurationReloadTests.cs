using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.PowerShell;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Functional.ConfigurationReload;

/// <summary>
/// Tests for PowerShell configuration reload functionality
/// </summary>
public class ConfigurationReloadTests : PowerShellTestBase
{
    public ConfigurationReloadTests(ITestOutputHelper outputHelper) : base(outputHelper)
    {
    }

    [Fact]
    public async Task ReloadConfigurationAsync_WithValidConfiguration_ShouldSucceed()
    {
        // Arrange
        var initialConfig = new PowerShellConfiguration
        {
            FunctionNames = new List<string> { "Get-Process" },
            Modules = new List<string>(),
            ExcludePatterns = new List<string>(),
            IncludePatterns = new List<string>()
        };

        var newConfig = new PowerShellConfiguration
        {
            FunctionNames = new List<string> { "Get-Process", "Get-Service" },
            Modules = new List<string>(),
            ExcludePatterns = new List<string>(),
            IncludePatterns = new List<string>()
        };

        // Use a separate logger factory for the service since it requires ILogger<T>
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<PowerShellConfigurationReloadService>();
        var toolFactory = new McpToolFactoryV2();
        var reloadService = new PowerShellConfigurationReloadService(
            logger, toolFactory, initialConfig, "/dummy/path");

        // Act
        var result = await reloadService.ReloadConfigurationAsync(newConfig);

        // Assert
        Assert.True(result.Success, $"Reload should succeed: {result.ErrorMessage}");
        Assert.NotNull(result.Message);
        Assert.True(result.ToolCount >= 0);

        var status = reloadService.GetStatus();
        Assert.Equal(2, status.FunctionNamesCount); // Should have the new function count
    }

    [Fact]
    public async Task ConfigurationReloadTools_ReloadFromFile_WithValidFile_ShouldSucceed()
    {
        // Arrange
        var tempConfigPath = Path.GetTempFileName();
        try
        {
            var configJson = JsonSerializer.Serialize(new
            {
                PowerShellConfiguration = new PowerShellConfiguration
                {
                    FunctionNames = new List<string> { "Get-Date", "Get-Location" },
                    Modules = new List<string>(),
                    ExcludePatterns = new List<string>(),
                    IncludePatterns = new List<string>()
                }
            }, new JsonSerializerOptions { WriteIndented = true });

            await File.WriteAllTextAsync(tempConfigPath, configJson);

            var initialConfig = new PowerShellConfiguration
            {
                FunctionNames = new List<string> { "Get-Process" }
            };

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
            var serviceLogger = loggerFactory.CreateLogger<PowerShellConfigurationReloadService>();
            var toolFactory = new McpToolFactoryV2();
            var reloadService = new PowerShellConfigurationReloadService(
                serviceLogger, toolFactory, initialConfig, tempConfigPath);

            var toolsLogger = loggerFactory.CreateLogger<ConfigurationReloadTools>();
            var reloadTools = new ConfigurationReloadTools(reloadService, toolsLogger);

            // Act
            var result = await reloadTools.ReloadConfigurationFromFile(CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            var resultObj = JsonSerializer.Deserialize<JsonElement>(result);
            Assert.True(resultObj.GetProperty("success").GetBoolean());
        }
        finally
        {
            if (File.Exists(tempConfigPath))
            {
                File.Delete(tempConfigPath);
            }
        }
    }

    [Fact]
    public async Task ConfigurationReloadTools_UpdateConfiguration_WithValidJson_ShouldSucceed()
    {
        // Arrange
        var initialConfig = new PowerShellConfiguration
        {
            FunctionNames = new List<string> { "Get-Process" }
        };

        var newConfigJson = JsonSerializer.Serialize(new PowerShellConfiguration
        {
            FunctionNames = new List<string> { "Get-Process", "Get-Service", "Get-Date" },
            Modules = new List<string>(),
            ExcludePatterns = new List<string>(),
            IncludePatterns = new List<string>()
        });

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var serviceLogger = loggerFactory.CreateLogger<PowerShellConfigurationReloadService>();
        var toolFactory = new McpToolFactoryV2();
        var reloadService = new PowerShellConfigurationReloadService(
            serviceLogger, toolFactory, initialConfig, "/dummy/path");

        var toolsLogger = loggerFactory.CreateLogger<ConfigurationReloadTools>();
        var reloadTools = new ConfigurationReloadTools(reloadService, toolsLogger);

        // Act
        var result = await reloadTools.UpdateConfiguration(newConfigJson, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var resultObj = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.True(resultObj.GetProperty("success").GetBoolean());

        // Verify the configuration was actually updated
        var status = reloadService.GetStatus();
        Assert.Equal(3, status.FunctionNamesCount);
    }

    [Fact]
    public async Task ConfigurationReloadTools_GetStatus_ShouldReturnCurrentStatus()
    {
        // Arrange
        var config = new PowerShellConfiguration
        {
            FunctionNames = new List<string> { "Get-Process", "Get-Service" },
            Modules = new List<string> { "Microsoft.PowerShell.Management" },
            ExcludePatterns = new List<string> { "Remove-*" },
            IncludePatterns = new List<string> { "Get-*" }
        };

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var serviceLogger = loggerFactory.CreateLogger<PowerShellConfigurationReloadService>();
        var toolFactory = new McpToolFactoryV2();
        var reloadService = new PowerShellConfigurationReloadService(
            serviceLogger, toolFactory, config, "/test/path");

        var toolsLogger = loggerFactory.CreateLogger<ConfigurationReloadTools>();
        var registeredTools = new List<ModelContextProtocol.Server.McpServerTool>();
        var reloadTools = new ConfigurationReloadTools(
            reloadService,
            "/test/path",
            SettingsResolver.CwdSource,
            "http",
            null,
            config.RuntimeMode.ToString(),
            null,
            () => registeredTools,
            toolsLogger);

        // Act
        var result = await reloadTools.GetConfigurationStatus(CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var resultObj = JsonNode.Parse(result)?.AsObject();
        Assert.NotNull(resultObj);
        Assert.DoesNotContain("success", resultObj!);
        Assert.NotNull(resultObj["summary"]);
        Assert.Equal("/test/path", resultObj["runtimeSettings"]?["configurationPath"]?["value"]?.GetValue<string>());
        Assert.Equal(SettingsResolver.CwdSource, resultObj["runtimeSettings"]?["configurationPath"]?["source"]?.GetValue<string>());
        Assert.Equal("file-backed", resultObj["runtimeSettings"]?["configurationMode"]?["value"]?.GetValue<string>());
        Assert.Equal("http", resultObj["runtimeSettings"]?["transport"]?["value"]?.GetValue<string>());
        Assert.Equal(config.RuntimeMode.ToString(), resultObj["runtimeSettings"]?["runtimeMode"]?["value"]?.GetValue<string>());
        Assert.Equal(2, resultObj["functionsTools"]?["configuredFunctionStatus"]?.AsArray().Count);

        var expectedReport = Program.BuildDoctorReportFromConfig(
            configurationPath: "/test/path",
            configurationPathSource: SettingsResolver.CwdSource,
            effectiveLogLevel: "Debug",
            effectiveLogLevelSource: "runtime",
            effectiveTransport: "http",
            effectiveTransportSource: "runtime",
            effectiveSessionMode: null,
            effectiveSessionModeSource: "runtime",
            effectiveRuntimeMode: config.RuntimeMode.ToString(),
            effectiveRuntimeModeSource: "runtime",
            effectiveMcpPath: null,
            effectiveMcpPathSource: "runtime",
            config: config,
            tools: registeredTools);
        var expectedObj = JsonNode.Parse(Program.BuildDoctorJson(expectedReport))?.AsObject();

        Assert.NotNull(expectedObj);
        Assert.Equal(expectedObj!["runtimeSettings"]?.ToJsonString(), resultObj["runtimeSettings"]?.ToJsonString());
    }

    [Fact]
    public async Task ConfigurationReloadTools_GetStatus_WithEnvironmentOnlyConfiguration_ReturnsEnvironmentOnlyMode()
    {
        // Arrange
        var config = new PowerShellConfiguration
        {
            CommandNames = new List<string> { "Get-Date" }
        };

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var serviceLogger = loggerFactory.CreateLogger<PowerShellConfigurationReloadService>();
        var toolFactory = new McpToolFactoryV2();
        var reloadService = new PowerShellConfigurationReloadService(serviceLogger, toolFactory, config, string.Empty);

        var toolsLogger = loggerFactory.CreateLogger<ConfigurationReloadTools>();
        var reloadTools = new ConfigurationReloadTools(
            reloadService,
            string.Empty,
            SettingsResolver.EnvSource,
            "http",
            null,
            config.RuntimeMode.ToString(),
            null,
            static () => new List<ModelContextProtocol.Server.McpServerTool>(),
            toolsLogger);

        // Act
        var result = await reloadTools.GetConfigurationStatus(CancellationToken.None);

        // Assert
        var resultObj = JsonNode.Parse(result)?.AsObject();
        Assert.NotNull(resultObj);
        Assert.Equal("(environment-only configuration)", resultObj!["runtimeSettings"]?["configurationPath"]?["value"]?.GetValue<string>());
        Assert.Equal(SettingsResolver.EnvSource, resultObj["runtimeSettings"]?["configurationPath"]?["source"]?.GetValue<string>());
        Assert.Equal("environment-only", resultObj["runtimeSettings"]?["configurationMode"]?["value"]?.GetValue<string>());
        Assert.Equal(SettingsResolver.EnvSource, resultObj["runtimeSettings"]?["configurationMode"]?["source"]?.GetValue<string>());
    }

    [Fact]
    public async Task ConfigurationReloadTools_UpdateConfiguration_WithInvalidJson_ShouldFail()
    {
        // Arrange
        var initialConfig = new PowerShellConfiguration
        {
            FunctionNames = new List<string> { "Get-Process" }
        };

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var serviceLogger = loggerFactory.CreateLogger<PowerShellConfigurationReloadService>();
        var toolFactory = new McpToolFactoryV2();
        var reloadService = new PowerShellConfigurationReloadService(
            serviceLogger, toolFactory, initialConfig, "/dummy/path");

        var toolsLogger = loggerFactory.CreateLogger<ConfigurationReloadTools>();
        var reloadTools = new ConfigurationReloadTools(reloadService, toolsLogger);

        // Act
        var result = await reloadTools.UpdateConfiguration("{ invalid json", CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var resultObj = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.False(resultObj.GetProperty("success").GetBoolean());
        Assert.Contains("Invalid configuration JSON", resultObj.GetProperty("error").GetString());
    }
}