using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PoshMcp.Server.McpPrompts;
using PoshMcp.Server.McpResources;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.OutOfProcess;
using PoshMcp.Tests.Shared;
using Xunit;

namespace PoshMcp.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ConfigurationLoaderTests
{
    [Fact]
    public void BuildRootConfiguration_WithValidFile_LoadsFromFile()
    {
        using var tempDirectory = new TempDirectory("config-loader-root");
        var configPath = WriteConfigFile(tempDirectory, "appsettings.json", """
        {
          "TestKey": "ExpectedValue"
        }
        """);

        IConfigurationRoot configuration = ConfigurationLoader.BuildRootConfiguration(configPath, reloadOnChange: false);

        Assert.Equal("ExpectedValue", configuration["TestKey"]);
    }

    [Fact]
    public void BuildRootConfiguration_WithNullPath_StillBuilds()
    {
        using var envVar = new EnvironmentVariableScope("ConfigurationLoaderTests__BuildRoot__NullPath", "env-value");

        IConfigurationRoot configuration = ConfigurationLoader.BuildRootConfiguration(null, reloadOnChange: false);

        Assert.NotNull(configuration);
        Assert.Equal("env-value", configuration["ConfigurationLoaderTests:BuildRoot:NullPath"]);
    }

    [Fact]
    public void BuildRootConfiguration_WithNonexistentFile_StillBuilds()
    {
        using var tempDirectory = new TempDirectory("config-loader-missing");
        using var envVar = new EnvironmentVariableScope("ConfigurationLoaderTests__BuildRoot__MissingPath", "env-value");
        var configPath = tempDirectory.Combine("missing.json");

        IConfigurationRoot configuration = ConfigurationLoader.BuildRootConfiguration(configPath, reloadOnChange: false);

        Assert.NotNull(configuration);
        Assert.Equal("env-value", configuration["ConfigurationLoaderTests:BuildRoot:MissingPath"]);
    }

    [Fact]
    public void BuildRootConfiguration_WithEmptyPath_StillBuilds()
    {
        using var envVar = new EnvironmentVariableScope("ConfigurationLoaderTests__BuildRoot__EmptyPath", "env-value");

        IConfigurationRoot configuration = ConfigurationLoader.BuildRootConfiguration("   ", reloadOnChange: false);

        Assert.NotNull(configuration);
        Assert.Equal("env-value", configuration["ConfigurationLoaderTests:BuildRoot:EmptyPath"]);
    }

    [Fact]
    public void LoadPowerShellConfiguration_ValidConfig_BindsCorrectly()
    {
        using var tempDirectory = new TempDirectory("config-loader-bind");
        var configPath = WriteConfigFile(tempDirectory, "appsettings.json", """
        {
          "PowerShellConfiguration": {
            "RuntimeMode": "InProcess",
            "CommandNames": ["Get-Process", "Get-Service"],
            "Modules": ["Microsoft.PowerShell.Management"],
            "IncludePatterns": ["Get-*"],
            "ExcludePatterns": ["Get-Secret"],
            "EnableConfigurationTroubleshootingTool": false
          }
        }
        """);

        PowerShellConfiguration configuration = ConfigurationLoader.LoadPowerShellConfiguration(configPath, NullLogger.Instance, runtimeModeOverride: null);

        Assert.Equal(RuntimeMode.InProcess, configuration.RuntimeMode);
        Assert.Equal(new[] { "Get-Process", "Get-Service" }, configuration.CommandNames);
        Assert.Equal(new[] { "Microsoft.PowerShell.Management" }, configuration.Modules);
        Assert.Equal(new[] { "Get-*" }, configuration.IncludePatterns);
        Assert.Equal(new[] { "Get-Secret" }, configuration.ExcludePatterns);
        Assert.False(configuration.EnableConfigurationTroubleshootingTool);
    }

    [Fact]
    public void LoadPowerShellConfiguration_UnsupportedRuntimeMode_ThrowsInvalidOperationException()
    {
        using var tempDirectory = new TempDirectory("config-loader-unsupported");
        var configPath = WriteConfigFile(tempDirectory, "appsettings.json", """
        {
          "PowerShellConfiguration": {
            "RuntimeMode": "Unsupported"
          }
        }
        """);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ConfigurationLoader.LoadPowerShellConfiguration(configPath, NullLogger.Instance, runtimeModeOverride: null));

        Assert.Contains("Unsupported runtime mode", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadPowerShellConfiguration_InProcessMode_Succeeds()
    {
        using var tempDirectory = new TempDirectory("config-loader-inproc");
        var configPath = WriteConfigFile(tempDirectory, "appsettings.json", """
        {
          "PowerShellConfiguration": {
            "RuntimeMode": "InProcess",
            "CommandNames": ["Get-Process"]
          }
        }
        """);

        PowerShellConfiguration configuration = ConfigurationLoader.LoadPowerShellConfiguration(configPath, NullLogger.Instance, runtimeModeOverride: null);

        Assert.Equal(RuntimeMode.InProcess, configuration.RuntimeMode);
        Assert.Equal(new[] { "Get-Process" }, configuration.CommandNames);
    }

    [Fact]
    public void LoadPowerShellConfiguration_OutOfProcessMode_Succeeds()
    {
        using var tempDirectory = new TempDirectory("config-loader-oop");
        var configPath = WriteConfigFile(tempDirectory, "appsettings.json", """
        {
          "PowerShellConfiguration": {
            "RuntimeMode": "OutOfProcess",
            "CommandNames": ["Get-Process"]
          }
        }
        """);

        PowerShellConfiguration configuration = ConfigurationLoader.LoadPowerShellConfiguration(configPath, NullLogger.Instance, runtimeModeOverride: null);

        Assert.Equal(RuntimeMode.OutOfProcess, configuration.RuntimeMode);
        Assert.Equal(new[] { "Get-Process" }, configuration.CommandNames);
    }

    [Fact]
    public void LoadPowerShellConfiguration_EnvVarOverridesConfigTroubleshootingTool()
    {
        using var tempDirectory = new TempDirectory("config-loader-env-override");
        using var envVar = new EnvironmentVariableScope(ConfigurationLoader.ConfigurationTroubleshootingToolEnvVar, "true");
        var configPath = WriteConfigFile(tempDirectory, "appsettings.json", """
        {
          "PowerShellConfiguration": {
            "CommandNames": ["Get-Process"],
            "EnableConfigurationTroubleshootingTool": false
          }
        }
        """);

        PowerShellConfiguration configuration = ConfigurationLoader.LoadPowerShellConfiguration(configPath, NullLogger.Instance, runtimeModeOverride: null);

        Assert.True(configuration.EnableConfigurationTroubleshootingTool);
    }

    [Fact]
    public void TryValidateResourcesAndPrompts_InvalidPath_ReturnsEmptyDiagnostics()
    {
        using var tempDirectory = new TempDirectory("config-loader-invalid-path");
        var configPath = tempDirectory.Combine("missing.json");

        var (resources, prompts) = ConfigurationLoader.TryValidateResourcesAndPrompts(configPath);

        AssertEmpty(resources);
        AssertEmpty(prompts);
    }

    [Fact]
    public void TryValidateResourcesAndPrompts_InvalidJson_ReturnsEmptyDiagnostics()
    {
        using var tempDirectory = new TempDirectory("config-loader-invalid-json");
        var configPath = WriteConfigFile(tempDirectory, "appsettings.json", "{ invalid json }");

        var (resources, prompts) = ConfigurationLoader.TryValidateResourcesAndPrompts(configPath);

        AssertEmpty(resources);
        AssertEmpty(prompts);
    }

    private static string WriteConfigFile(TempDirectory tempDirectory, string fileName, string json)
    {
        var configPath = tempDirectory.Combine(fileName);
        File.WriteAllText(configPath, json);
        return configPath;
    }

    private static void AssertEmpty(McpResourcesDiagnostics diagnostics)
    {
        Assert.Equal(0, diagnostics.Configured);
        Assert.Equal(0, diagnostics.Valid);
        Assert.Empty(diagnostics.Errors);
        Assert.Empty(diagnostics.Warnings);
    }

    private static void AssertEmpty(McpPromptsDiagnostics diagnostics)
    {
        Assert.Equal(0, diagnostics.Configured);
        Assert.Equal(0, diagnostics.Valid);
        Assert.Empty(diagnostics.Errors);
        Assert.Empty(diagnostics.Warnings);
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _originalValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _originalValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _originalValue);
        }
    }
}
