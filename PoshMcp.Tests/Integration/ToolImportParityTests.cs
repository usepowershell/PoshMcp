using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PoshMcp;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Integration;

/// <summary>
/// Issue #272 parity coverage. Verifies that doctor <c>moduleImports.tools[]</c>
/// carries byte-identical source attribution in InProcess and OutOfProcess modes
/// for both mixed command/module discovery and pattern-only discovery.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Spec", "011")]
public sealed class ToolImportParityTests : PowerShellTestBase
{
    public ToolImportParityTests(ITestOutputHelper output) : base(output)
    {
    }

    [PwshAvailableFact]
    public async Task MixedCommandAndModuleSources_AreByteIdentical_AcrossRuntimeModes()
    {
        var inProcess = await BuildDoctorReportAsync(
            runtimeMode: "InProcess",
            commandNames: new[] { "Get-FixtureSynopsisOnly" },
            modules: new[] { "HelpParityFixture", "Microsoft.PowerShell.Management" },
            includePatterns: new[] { "Get-Fixture*" });
        var outOfProcess = await BuildDoctorReportAsync(
            runtimeMode: "OutOfProcess",
            commandNames: new[] { "Get-FixtureSynopsisOnly" },
            modules: new[] { "HelpParityFixture", "Microsoft.PowerShell.Management" },
            includePatterns: new[] { "Get-Fixture*" });

        var inProcessEntries = SelectRelevantEntries(inProcess, "Get-FixtureSynopsisOnly", "Get-FixtureFullHelp");
        var outOfProcessEntries = SelectRelevantEntries(outOfProcess, "Get-FixtureSynopsisOnly", "Get-FixtureFullHelp");

        Assert.Equal(inProcessEntries, outOfProcessEntries);

        Assert.All(
            inProcessEntries.Where(e => string.Equals(e.CommandName, "Get-FixtureSynopsisOnly", StringComparison.Ordinal)),
            entry =>
            {
                Assert.Equal("commandName", entry.Source);
                Assert.Equal("Get-FixtureSynopsisOnly", entry.SourceDetail);
            });

        Assert.All(
            inProcessEntries.Where(e => string.Equals(e.CommandName, "Get-FixtureFullHelp", StringComparison.Ordinal)),
            entry =>
            {
                Assert.Equal("module", entry.Source);
                Assert.Equal("HelpParityFixture", entry.SourceDetail);
            });
    }

    [PwshAvailableFact]
    public async Task PatternSources_AreByteIdentical_AcrossRuntimeModes()
    {
        var inProcess = await BuildDoctorReportAsync(
            runtimeMode: "InProcess",
            commandNames: Array.Empty<string>(),
            modules: Array.Empty<string>(),
            includePatterns: new[] { "Get-FixtureValidateSetScalar" });
        var outOfProcess = await BuildDoctorReportAsync(
            runtimeMode: "OutOfProcess",
            commandNames: Array.Empty<string>(),
            modules: Array.Empty<string>(),
            includePatterns: new[] { "Get-FixtureValidateSetScalar" });

        var inProcessEntries = SelectRelevantEntries(inProcess, "Get-FixtureValidateSetScalar");
        var outOfProcessEntries = SelectRelevantEntries(outOfProcess, "Get-FixtureValidateSetScalar");

        Assert.Equal(inProcessEntries, outOfProcessEntries);
        var entry = Assert.Single(inProcessEntries);
        Assert.Equal("pattern", entry.Source);
        Assert.Equal("Get-FixtureValidateSetScalar", entry.SourceDetail);
    }

    private async Task<DoctorReport> BuildDoctorReportAsync(
        string runtimeMode,
        IReadOnlyList<string> commandNames,
        IReadOnlyList<string> modules,
        IReadOnlyList<string> includePatterns)
    {
        var root = ResolveWorkspaceRoot();
        var fixtureRoot = Path.Combine(root, "PoshMcp.Tests", "Fixtures", "Modules");
        var fixtureManifest = Path.Combine(fixtureRoot, "HelpParityFixture", "HelpParityFixture.psd1");
        Assert.True(File.Exists(fixtureManifest), $"Fixture module missing: {fixtureManifest}");

        var configDirectory = Path.Combine(root, "PoshMcp.Tests", "Fixtures", "GeneratedConfigs");
        Directory.CreateDirectory(configDirectory);

        var configPath = Path.Combine(
            configDirectory,
            $"tool-import-parity-{runtimeMode.ToLowerInvariant()}-{Guid.NewGuid():N}.json");

        var previousModulePath = Environment.GetEnvironmentVariable("PSModulePath");
        var updatedModulePath = string.IsNullOrEmpty(previousModulePath)
            ? fixtureRoot
            : fixtureRoot + Path.PathSeparator + previousModulePath;
        Environment.SetEnvironmentVariable("PSModulePath", updatedModulePath);

        var configJson = $$"""
        {
          "Logging": { "LogLevel": { "Default": "Warning" } },
          "PowerShellConfiguration": {
            "RuntimeMode": "{{runtimeMode}}",
            "CommandNames": {{SerializeArray(commandNames)}},
            "Modules": {{SerializeArray(modules)}},
            "IncludePatterns": {{SerializeArray(includePatterns)}},
            "ExcludePatterns": [],
            "EnableDynamicReloadTools": false,
            "EnableConfigurationTroubleshootingTool": false,
            "SubprocessHostMode": "Pool",
            "Environment": {
              "ModulePaths": [{{Quote(fixtureRoot)}}],
              "ImportModules": ["HelpParityFixture"]
            }
          },
          "Authentication": { "Enabled": false }
        }
        """;

        await File.WriteAllTextAsync(configPath, configJson);

        try
        {
            var settings = new ResolvedCommandSettings(
                new ResolvedSetting(configPath, "test"),
                configPath,
                new ResolvedSetting("Warning", "test"),
                new ResolvedSetting("stdio", "test"),
                new ResolvedSetting(null, "test"),
                new ResolvedSetting(runtimeMode, "test"),
                new ResolvedSetting(null, "test"));

            return await DoctorService.BuildDoctorReportForCliAsync(settings, McpToolSetupService.DiscoverToolsForCliAsync);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PSModulePath", previousModulePath);
            if (File.Exists(configPath))
            {
                File.Delete(configPath);
            }
        }
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

    private static string SerializeArray(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return "[]";
        }

        return "[" + string.Join(", ", values.Select(Quote)) + "]";
    }

    private static string Quote(string value) =>
        $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

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
}
