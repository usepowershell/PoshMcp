using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;
using PoshMcp;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.OutOfProcess;
using PoshMcp.Tests;
using Xunit;

namespace PoshMcp.Tests.Unit;

[Collection("TransportSelectionTests")]
[Trait("Category", "Unit")]
public sealed class McpToolSetupServiceDecisionTests
{
    [Fact]
    public void InferConfigurationPathSource_UsesEnvSource_WhenPathIsMissing()
    {
        Assert.Equal("env", McpToolSetupService.InferConfigurationPathSource(null));
        Assert.Equal("env", McpToolSetupService.InferConfigurationPathSource(string.Empty));
        Assert.Equal("env", McpToolSetupService.InferConfigurationPathSource("   "));
    }

    [Fact]
    public void InferConfigurationPathSource_UsesRuntimeSource_WhenPathIsExplicit()
    {
        Assert.Equal("runtime", McpToolSetupService.InferConfigurationPathSource(@"C:\\config\\appsettings.json"));
    }

    [Fact]
    public async Task StartOutOfProcessExecutorIfNeededAsync_InProcessMode_ReturnsNull()
    {
        var config = new PowerShellConfiguration
        {
            RuntimeMode = RuntimeMode.InProcess
        };

        var lease = await McpToolSetupService.StartOutOfProcessExecutorIfNeededAsync(
            config,
            NullLoggerFactory.Instance,
            NullLogger.Instance);

        Assert.Null(lease);
    }

    [PwshAvailableFact]
    public async Task SetupMcpToolsAsync_WhenAllOptionalToolFlagsAreDisabled_OnlyAddsAlwaysOnCachingTool()
    {
        using var configFile = new TemporaryConfigFile();
        var config = CreateConfig(enableDynamicReloadTools: false, enableConfigurationTroubleshootingTool: false);

        var toolSetupResult = await McpToolSetupService.SetupMcpToolsAsync(
            NullLoggerFactory.Instance,
            config,
            NullLogger.Instance,
            configFile.Path,
            "runtime",
            commandExecutor: null);

        var toolNames = GetToolNames(toolSetupResult.Tools);

        Assert.Contains("set-result-caching", toolNames);
        Assert.DoesNotContain("reload-configuration-from-file", toolNames);
        Assert.DoesNotContain("update-configuration", toolNames);
        Assert.DoesNotContain("get-configuration-status", toolNames);
        Assert.DoesNotContain("get-configuration-guidance", toolNames);
        Assert.DoesNotContain("get-configuration-troubleshooting", toolNames);
    }

    [PwshAvailableFact]
    public async Task SetupMcpToolsAsync_WhenDynamicReloadToolsAreEnabled_AddsOnlyReloadToolSet()
    {
        using var configFile = new TemporaryConfigFile();
        var config = CreateConfig(enableDynamicReloadTools: true, enableConfigurationTroubleshootingTool: false);

        var toolSetupResult = await McpToolSetupService.SetupMcpToolsAsync(
            NullLoggerFactory.Instance,
            config,
            NullLogger.Instance,
            configFile.Path,
            "runtime",
            commandExecutor: null);

        var toolNames = GetToolNames(toolSetupResult.Tools);

        Assert.Contains("reload-configuration-from-file", toolNames);
        Assert.Contains("update-configuration", toolNames);
        Assert.Contains("get-configuration-status", toolNames);
        Assert.Contains("set-result-caching", toolNames);
        Assert.DoesNotContain("get-configuration-guidance", toolNames);
        Assert.DoesNotContain("get-configuration-troubleshooting", toolNames);
    }

    [PwshAvailableFact]
    public async Task SetupMcpToolsAsync_WhenConfigurationTroubleshootingIsEnabled_AddsGuidanceAndTroubleshootingTools()
    {
        using var configFile = new TemporaryConfigFile();
        var config = CreateConfig(enableDynamicReloadTools: false, enableConfigurationTroubleshootingTool: true);

        var toolSetupResult = await McpToolSetupService.SetupMcpToolsAsync(
            NullLoggerFactory.Instance,
            config,
            NullLogger.Instance,
            configFile.Path,
            "runtime",
            commandExecutor: null);

        var toolNames = GetToolNames(toolSetupResult.Tools);

        Assert.Contains("get-configuration-guidance", toolNames);
        Assert.Contains("get-configuration-troubleshooting", toolNames);
        Assert.Contains("set-result-caching", toolNames);
        Assert.DoesNotContain("reload-configuration-from-file", toolNames);
        Assert.DoesNotContain("update-configuration", toolNames);
        Assert.DoesNotContain("get-configuration-status", toolNames);
    }

    [PwshAvailableFact]
    public async Task SetupMcpToolsAsync_UpdatesPowerShellAssemblyGeneratorStaticConfiguration()
    {
        using var firstConfigFile = new TemporaryConfigFile();
        using var secondConfigFile = new TemporaryConfigFile();

        var disabledConfig = CreateConfig(enableDynamicReloadTools: false, enableConfigurationTroubleshootingTool: false);
        disabledConfig.Performance.EnableResultCaching = false;

        await McpToolSetupService.SetupMcpToolsAsync(
            NullLoggerFactory.Instance,
            disabledConfig,
            NullLogger.Instance,
            firstConfigFile.Path,
            "runtime",
            commandExecutor: null);

        Assert.False(PowerShellAssemblyGenerator.ResolveCachingSetting("Get-Date"));

        var enabledConfig = CreateConfig(enableDynamicReloadTools: false, enableConfigurationTroubleshootingTool: false);
        enabledConfig.Performance.EnableResultCaching = true;

        await McpToolSetupService.SetupMcpToolsAsync(
            NullLoggerFactory.Instance,
            enabledConfig,
            NullLogger.Instance,
            secondConfigFile.Path,
            "runtime",
            commandExecutor: null);

        Assert.True(PowerShellAssemblyGenerator.ResolveCachingSetting("Get-Date"));
    }

    [Fact]
    public void BuildConfigurationTroubleshootingJson_MergesConfigurationAndDiscoveryFailures()
    {
        var json = McpToolSetupService.BuildConfigurationTroubleshootingJson(
            configurationPath: @"C:\\does-not-exist\\appsettings.json",
            effectiveTransport: "stdio",
            effectiveSessionMode: null,
            effectiveRuntimeMode: "NotARealRuntimeMode",
            effectiveMcpPath: null,
            registeredToolsProvider: () => throw new InvalidOperationException("boom"),
            logger: NullLogger.Instance);

        var payload = JsonNode.Parse(json)?.AsObject();
        Assert.NotNull(payload);

        var errors = payload!["configurationErrors"]?.AsArray().Select(node => node?.GetValue<string>()).OfType<string>().ToArray();
        Assert.NotNull(errors);
        Assert.Contains(errors!, error => error.Contains("Failed to load PowerShell configuration", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors!, error => error.Contains("Tool discovery failed: boom", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("errors", payload["summary"]?["status"]?.GetValue<string>());
    }

    private static PowerShellConfiguration CreateConfig(bool enableDynamicReloadTools, bool enableConfigurationTroubleshootingTool)
    {
        return new PowerShellConfiguration
        {
            CommandNames = new List<string> { "Get-Date" },
            Modules = new List<string>(),
            IncludePatterns = new List<string>(),
            ExcludePatterns = new List<string>(),
            EnableDynamicReloadTools = enableDynamicReloadTools,
            EnableConfigurationTroubleshootingTool = enableConfigurationTroubleshootingTool
        };
    }

    private static HashSet<string> GetToolNames(IEnumerable<McpServerTool> tools)
    {
        return tools.Select(tool => tool.ProtocolTool.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class TemporaryConfigFile : IDisposable
    {
        public string Path { get; }

        public TemporaryConfigFile()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"poshmcp-tool-setup-tests-{Guid.NewGuid():N}.json");

            var json = JsonSerializer.Serialize(new
            {
                PowerShellConfiguration = new
                {
                    CommandNames = new[] { "Get-Date" },
                    Modules = Array.Empty<string>(),
                    IncludePatterns = Array.Empty<string>(),
                    ExcludePatterns = Array.Empty<string>(),
                    EnableDynamicReloadTools = false,
                    EnableConfigurationTroubleshootingTool = false
                },
                Authentication = new
                {
                    Enabled = false
                }
            });

            File.WriteAllText(Path, json);
        }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}
