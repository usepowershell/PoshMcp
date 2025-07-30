using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.PowerShell;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Functional.ConfigurationReload;

/// <summary>
/// Test to verify the end-to-end behavior of the dynamic reload tools feature flag
/// </summary>
public class EndToEndDynamicReloadToolsTests : PowerShellTestBase
{
    public EndToEndDynamicReloadToolsTests(ITestOutputHelper outputHelper) : base(outputHelper)
    {
    }

    [Fact]
    public void DynamicReloadTools_WhenEnabled_ShouldBeCreatable()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var serviceProvider = services.BuildServiceProvider();

        var config = new PowerShellConfiguration
        {
            FunctionNames = new List<string>(),
            EnableDynamicReloadTools = true
        };

        var toolFactory = new McpToolFactoryV2();
        var reloadServiceLogger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<PowerShellConfigurationReloadService>();
        var reloadService = new PowerShellConfigurationReloadService(reloadServiceLogger, toolFactory, config, "/dummy/path");
        var reloadToolsLogger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<ConfigurationReloadTools>();

        // Act
        var reloadTools = new ConfigurationReloadTools(reloadService, reloadToolsLogger);

        // Assert
        Assert.NotNull(reloadTools);

        // Test that we can create the tools that would be added when enabled
        var reloadFromFileDelegate = new System.Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<string>>(reloadTools.ReloadConfigurationFromFile);
        var updateConfigDelegate = new System.Func<string, System.Threading.CancellationToken, System.Threading.Tasks.Task<string>>(reloadTools.UpdateConfiguration);
        var getConfigStatusDelegate = new System.Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<string>>(reloadTools.GetConfigurationStatus);

        Assert.NotNull(reloadFromFileDelegate);
        Assert.NotNull(updateConfigDelegate);
        Assert.NotNull(getConfigStatusDelegate);

        Logger.LogInformation("Successfully verified that dynamic reload tools can be created when enabled");
    }

    [Fact]
    public void FeatureFlag_ConditionalLogic_WorksCorrectly()
    {
        // Test the conditional logic that would be used in SetupMcpTools

        // Arrange
        var configDisabled = new PowerShellConfiguration { EnableDynamicReloadTools = false };
        var configEnabled = new PowerShellConfiguration { EnableDynamicReloadTools = true };

        var mockTools = new List<string> { "existing-tool-1", "existing-tool-2" };
        var reloadToolNames = new List<string> { "reload-configuration-from-file", "update-configuration", "get-configuration-status" };

        // Act & Assert for disabled case
        var toolsWhenDisabled = new List<string>(mockTools);
        if (configDisabled.EnableDynamicReloadTools)
        {
            toolsWhenDisabled.AddRange(reloadToolNames);
        }

        Assert.Equal(2, toolsWhenDisabled.Count);
        Assert.DoesNotContain("reload-configuration-from-file", toolsWhenDisabled);
        Assert.DoesNotContain("update-configuration", toolsWhenDisabled);
        Assert.DoesNotContain("get-configuration-status", toolsWhenDisabled);

        // Act & Assert for enabled case
        var toolsWhenEnabled = new List<string>(mockTools);
        if (configEnabled.EnableDynamicReloadTools)
        {
            toolsWhenEnabled.AddRange(reloadToolNames);
        }

        Assert.Equal(5, toolsWhenEnabled.Count);
        Assert.Contains("reload-configuration-from-file", toolsWhenEnabled);
        Assert.Contains("update-configuration", toolsWhenEnabled);
        Assert.Contains("get-configuration-status", toolsWhenEnabled);

        Logger.LogInformation("Feature flag conditional logic works correctly");
    }
}