using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;
using PoshMcp;
using PoshMcp.Server.PowerShell;
using Xunit;

namespace PoshMcp.Tests.Unit.Diagnostics;

/// <summary>
/// Issue #272 acceptance coverage for the tool-import attribution seam.
/// Mirrors the spec-010 tracker tests: assert vocabulary mapping, first-writer-wins
/// tracker semantics, doctor wire-shape population, and legacy-host fallback.
/// </summary>
[Trait("Category", "Unit")]
public class DoctorToolImportSourceTests
{
    private static McpServerTool MakeTool(string toolName, string commandName)
    {
        var stub = new Func<string>(() => "stub");
        return McpServerTool.Create(stub, new McpServerToolCreateOptions
        {
            Name = toolName,
            Title = commandName,
            Description = "stub",
        });
    }

    private static ModuleProbeResult Found(string name, string version = "1.0.0", string path = "C:\\Modules\\stub")
        => new(name, true, version, path);

    [Theory]
    [InlineData(ToolImportSource.CommandName, "commandName")]
    [InlineData(ToolImportSource.Module, "module")]
    [InlineData(ToolImportSource.Pattern, "pattern")]
    [InlineData(ToolImportSource.Unknown, "unknown")]
    public void Vocabulary_MapsToWireValues(ToolImportSource source, string expected)
    {
        Assert.Equal(expected, ToolImportSourceVocabulary.ToWireValue(source));
    }

    [Fact]
    public void Tracker_RecordToolSource_KeepsFirstWhenCalledMultipleTimes()
    {
        var tracker = new ToolImportSourceTracker();
        tracker.RecordToolSource("Get-Item", ToolImportSource.Module, "Microsoft.PowerShell.Management");
        tracker.RecordToolSource("Get-Item", ToolImportSource.Pattern, "Get-*");

        var recorded = tracker.ToolSources["Get-Item"];
        Assert.Equal(ToolImportSource.Module, recorded.Source);
        Assert.Equal("Microsoft.PowerShell.Management", recorded.SourceDetail);
    }

    [Fact]
    public void BuildModuleImportsSection_Tracker_PopulatesDoctorToolWireFields()
    {
        var config = new PowerShellConfiguration
        {
            CommandNames = new() { "Get-Date" },
            Modules = new() { "Microsoft.PowerShell.Management", "Microsoft.PowerShell.Archive" },
            IncludePatterns = new() { "Out-String" },
        };
        var tools = new List<McpServerTool>
        {
            MakeTool("get_date", "Get-Date"),
            MakeTool("get_item", "Get-Item"),
            MakeTool("out_string", "Out-String"),
        };
        var probes = new List<ModuleProbeResult>
        {
            Found("Microsoft.PowerShell.Management"),
            Found("Microsoft.PowerShell.Archive"),
        };
        var tracker = new ToolImportSourceTracker();
        tracker.RecordToolSource("Get-Date", ToolImportSource.CommandName, "Get-Date");
        tracker.RecordToolSource("Get-Item", ToolImportSource.Module, "Microsoft.PowerShell.Management");
        tracker.RecordToolSource("Out-String", ToolImportSource.Pattern, "Out-String");

        var section = DoctorService.BuildModuleImportsSection(config, tools, probes, NullLogger.Instance, tracker);
        var byTool = section.Tools.ToDictionary(t => t.ToolName, StringComparer.Ordinal);

        Assert.Equal("commandName", byTool["get_date"].Source);
        Assert.Equal("Get-Date", byTool["get_date"].SourceDetail);
        Assert.Equal("module", byTool["get_item"].Source);
        Assert.Equal("Microsoft.PowerShell.Management", byTool["get_item"].SourceDetail);
        Assert.Equal("pattern", byTool["out_string"].Source);
        Assert.Equal("Out-String", byTool["out_string"].SourceDetail);
        Assert.Equal("pattern", SerializeAndReadField(byTool["out_string"], "source"));
    }

    [Fact]
    public void BuildModuleImportsSection_MissingTrackerSource_ReportsUnknown()
    {
        var config = new PowerShellConfiguration
        {
            Modules = new() { "Microsoft.PowerShell.Management", "Microsoft.PowerShell.Archive" },
            IncludePatterns = new() { "Out-String" },
        };
        var tools = new List<McpServerTool>
        {
            MakeTool("get_item", "Get-Item"),
            MakeTool("out_string", "Out-String"),
        };
        var probes = new List<ModuleProbeResult>
        {
            Found("Microsoft.PowerShell.Management"),
            Found("Microsoft.PowerShell.Archive"),
        };

        var section = DoctorService.BuildModuleImportsSection(config, tools, probes, NullLogger.Instance, importSourceTracker: null);

        Assert.All(section.Tools, entry =>
        {
            Assert.Equal("unknown", entry.Source);
            Assert.Equal(string.Empty, entry.SourceDetail);
        });
    }

    private static string? SerializeAndReadField(object record, string field)
    {
        var json = JsonSerializer.Serialize(record);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty(field, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
    }
}
