using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using PoshMcp;
using PoshMcp.Server.PowerShell;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Integration;

/// <summary>
/// Issue #272 runtime parity coverage. Verifies that the production status and
/// troubleshooting report paths preserve the same authoritative tool source
/// attribution as CLI doctor.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Issue", "272")]
public sealed class ToolImportRuntimeParityTests : PowerShellTestBase
{
    public ToolImportRuntimeParityTests(ITestOutputHelper output) : base(output)
    {
    }

    [PwshAvailableFact]
    public async Task GetConfigurationStatus_PreservesCliDoctorToolSources()
    {
        using var scenario = await CreateScenarioAsync("InProcess");
        var reloadService = new PowerShellConfigurationReloadService(
            scenario.LoggerFactory.CreateLogger<PowerShellConfigurationReloadService>(),
            new McpToolFactoryV2(),
            scenario.Configuration,
            scenario.ConfigPath);
        var reloadTools = new ConfigurationReloadTools(
            reloadService,
            scenario.ConfigPath,
            SettingsResolver.CwdSource,
            "stdio",
            null,
            scenario.Configuration.RuntimeMode.ToString(),
            null,
            () => scenario.Tools,
            scenario.LoggerFactory.CreateLogger<ConfigurationReloadTools>(),
            scenario.ImportSourceTracker);

        var runtimeReport = DeserializeReport(await reloadTools.GetConfigurationStatus(CancellationToken.None));

        AssertToolSourcesMatchCliDoctor(runtimeReport, scenario.CliReport);
    }

    [PwshAvailableFact]
    public async Task ConfigurationTroubleshooting_PreservesCliDoctorToolSources()
    {
        using var scenario = await CreateScenarioAsync("InProcess");

        var runtimeReport = DeserializeReport(McpToolSetupService.BuildConfigurationTroubleshootingJson(
            configurationPath: scenario.ConfigPath,
            effectiveTransport: "stdio",
            effectiveSessionMode: null,
            effectiveRuntimeMode: scenario.Configuration.RuntimeMode.ToString(),
            effectiveMcpPath: null,
            registeredToolsProvider: () => scenario.Tools,
            logger: scenario.Logger,
            importSourceTracker: scenario.ImportSourceTracker));

        AssertToolSourcesMatchCliDoctor(runtimeReport, scenario.CliReport);
    }

    private static void AssertToolSourcesMatchCliDoctor(DoctorReport runtimeReport, DoctorReport cliReport)
    {
        var expected = SelectRelevantEntries(cliReport, "Get-FixtureSynopsisOnly", "Get-FixtureFullHelp");
        var actual = SelectRelevantEntries(runtimeReport, "Get-FixtureSynopsisOnly", "Get-FixtureFullHelp");

        Assert.Equal(expected, actual);
    }

    private async Task<ToolImportScenario> CreateScenarioAsync(string runtimeMode)
    {
        var root = ResolveWorkspaceRoot();
        var fixtureRoot = Path.Combine(root, "PoshMcp.Tests", "Fixtures", "Modules");
        var fixtureManifest = Path.Combine(fixtureRoot, "HelpParityFixture", "HelpParityFixture.psd1");
        Assert.True(File.Exists(fixtureManifest), $"Fixture module missing: {fixtureManifest}");

        var configDirectory = Path.Combine(root, "PoshMcp.Tests", "Fixtures", "GeneratedConfigs");
        Directory.CreateDirectory(configDirectory);

        var configPath = Path.Combine(
            configDirectory,
            $"tool-import-runtime-parity-{runtimeMode.ToLowerInvariant()}-{Guid.NewGuid():N}.json");

        var previousModulePath = Environment.GetEnvironmentVariable("PSModulePath");
        var updatedModulePath = string.IsNullOrEmpty(previousModulePath)
            ? fixtureRoot
            : fixtureRoot + Path.PathSeparator + previousModulePath;
        Environment.SetEnvironmentVariable("PSModulePath", updatedModulePath);

        var configJson = JsonSerializer.Serialize(new
        {
            Logging = new
            {
                LogLevel = new
                {
                    Default = "Warning"
                }
            },
            PowerShellConfiguration = new
            {
                RuntimeMode = runtimeMode,
                CommandNames = new[] { "Get-FixtureSynopsisOnly" },
                Modules = new[] { "HelpParityFixture", "Microsoft.PowerShell.Management" },
                IncludePatterns = new[] { "Get-Fixture*" },
                ExcludePatterns = Array.Empty<string>(),
                EnableDynamicReloadTools = false,
                EnableConfigurationTroubleshootingTool = false,
                SubprocessHostMode = "Pool",
                Environment = new
                {
                    ModulePaths = new[] { fixtureRoot },
                    ImportModules = new[] { "HelpParityFixture" }
                }
            },
            Authentication = new
            {
                Enabled = false
            }
        });

        await File.WriteAllTextAsync(configPath, configJson);

        try
        {
            var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            var logger = loggerFactory.CreateLogger<ToolImportRuntimeParityTests>();
            var config = ConfigurationLoader.LoadPowerShellConfiguration(configPath, logger, runtimeMode);
            var tracker = new ToolImportSourceTracker();
            var tools = await McpToolSetupService.DiscoverToolsForCliAsync(
                config,
                loggerFactory,
                logger,
                configPath,
                toolMetadataSource: null,
                descriptionSourceTracker: null,
                importSourceTracker: tracker);
            var settings = new ResolvedCommandSettings(
                new ResolvedSetting(configPath, "test"),
                configPath,
                new ResolvedSetting("Warning", "test"),
                new ResolvedSetting("stdio", "test"),
                new ResolvedSetting(null, "test"),
                new ResolvedSetting(runtimeMode, "test"),
                new ResolvedSetting(null, "test"));
            var cliReport = await DoctorService.BuildDoctorReportForCliAsync(settings, McpToolSetupService.DiscoverToolsForCliAsync);

            return new ToolImportScenario(configPath, previousModulePath, loggerFactory, logger, config, tracker, tools, cliReport);
        }
        catch
        {
            Environment.SetEnvironmentVariable("PSModulePath", previousModulePath);
            if (File.Exists(configPath))
            {
                File.Delete(configPath);
            }

            throw;
        }
    }

    private static DoctorReport DeserializeReport(string json)
    {
        return JsonSerializer.Deserialize<DoctorReport>(json)
            ?? throw new InvalidOperationException("Failed to deserialize doctor report JSON.");
    }

    private static List<ToolImportProjection> SelectRelevantEntries(DoctorReport report, params string[] commandNames)
    {
        var wanted = new HashSet<string>(commandNames, StringComparer.OrdinalIgnoreCase);
        var entries = report.ModuleImports.Tools
            .Where(t => wanted.Contains(t.CommandName))
            .OrderBy(t => t.ToolName, StringComparer.Ordinal)
            .Select(t => new ToolImportProjection(t.ToolName, t.CommandName, t.Source, t.SourceDetail))
            .ToList();

        foreach (var commandName in commandNames)
        {
            Assert.Contains(entries, e => string.Equals(e.CommandName, commandName, StringComparison.Ordinal));
        }

        return entries;
    }

    private static string ResolveWorkspaceRoot()
    {
        var current = Directory.GetCurrentDirectory();
        while (current is not null && !File.Exists(Path.Combine(current, "PoshMcp.sln")))
        {
            current = Path.GetDirectoryName(current);
        }

        return current
            ?? throw new InvalidOperationException(
                $"Could not find workspace root from {Directory.GetCurrentDirectory()}");
    }

    private sealed record ToolImportProjection(
        string ToolName,
        string CommandName,
        string Source,
        string SourceDetail);

    private sealed class ToolImportScenario : IDisposable
    {
        public ToolImportScenario(
            string configPath,
            string? previousModulePath,
            ILoggerFactory loggerFactory,
            ILogger logger,
            PowerShellConfiguration configuration,
            ToolImportSourceTracker importSourceTracker,
            List<McpServerTool> tools,
            DoctorReport cliReport)
        {
            ConfigPath = configPath;
            PreviousModulePath = previousModulePath;
            LoggerFactory = loggerFactory;
            Logger = logger;
            Configuration = configuration;
            ImportSourceTracker = importSourceTracker;
            Tools = tools;
            CliReport = cliReport;
        }

        public string ConfigPath { get; }
        public string? PreviousModulePath { get; }
        public ILoggerFactory LoggerFactory { get; }
        public ILogger Logger { get; }
        public PowerShellConfiguration Configuration { get; }
        public ToolImportSourceTracker ImportSourceTracker { get; }
        public List<McpServerTool> Tools { get; }
        public DoctorReport CliReport { get; }

        public void Dispose()
        {
            LoggerFactory.Dispose();
            Environment.SetEnvironmentVariable("PSModulePath", PreviousModulePath);
            if (File.Exists(ConfigPath))
            {
                File.Delete(ConfigPath);
            }
        }
    }
}
