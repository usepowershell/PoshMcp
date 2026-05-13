using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ModelContextProtocol.Server;
using PoshMcp;
using PoshMcp.Server.PowerShell;
using Xunit;

namespace PoshMcp.Tests.Unit.Diagnostics;

/// <summary>
/// Spec 010 FR-582 / FR-583 / SC-207 coverage: each precedence step in
/// FR-500 (tool descriptions) and FR-510 (parameter descriptions) MUST surface
/// the matching <c>descriptionSource</c> wire literal in doctor JSON output.
/// </summary>
public class DoctorDescriptionSourceTests
{
    private const string TestCommandName = "Get-Doctor";

    // ── Tool description sources (FR-500) ───────────────────────────────────

    [Fact]
    public void Tool_Synopsis_Source_Reports_synopsis()
    {
        var (entries, _) = BuildEntriesForToolRequest(new ToolDescriptionRequest(
            CommandName: TestCommandName,
            ParameterSetName: "Default",
            Synopsis: "Diagnose a thing.",
            LongDescription: null,
            ParameterSetSyntax: "[-Name <string>]"));

        var entry = Assert.Single(entries);
        Assert.Equal(ToolDescriptionSource.Synopsis, entry.DescriptionSource);
        Assert.Equal("synopsis", SerializeAndReadField(entry, "descriptionSource"));
    }

    [Fact]
    public void Tool_LongDescription_Source_Reports_description()
    {
        var (entries, _) = BuildEntriesForToolRequest(new ToolDescriptionRequest(
            CommandName: TestCommandName,
            ParameterSetName: "Default",
            Synopsis: null,
            LongDescription: "A longer multi-paragraph body describing the tool.",
            ParameterSetSyntax: "[-Name <string>]"));

        var entry = Assert.Single(entries);
        Assert.Equal(ToolDescriptionSource.Description, entry.DescriptionSource);
        Assert.Equal("description", SerializeAndReadField(entry, "descriptionSource"));
    }

    [Fact]
    public void Tool_Syntax_Source_Reports_syntax()
    {
        var (entries, _) = BuildEntriesForToolRequest(new ToolDescriptionRequest(
            CommandName: TestCommandName,
            ParameterSetName: "Default",
            Synopsis: null,
            LongDescription: null,
            ParameterSetSyntax: "[-Name <string>] [-Force]"));

        var entry = Assert.Single(entries);
        Assert.Equal(ToolDescriptionSource.Syntax, entry.DescriptionSource);
        Assert.Equal("syntax", SerializeAndReadField(entry, "descriptionSource"));
    }

    [Fact]
    public void Tool_Name_Source_Reports_name()
    {
        var (entries, _) = BuildEntriesForToolRequest(new ToolDescriptionRequest(
            CommandName: TestCommandName,
            ParameterSetName: "Default",
            Synopsis: null,
            LongDescription: null,
            ParameterSetSyntax: null));

        var entry = Assert.Single(entries);
        Assert.Equal(ToolDescriptionSource.Name, entry.DescriptionSource);
        Assert.Equal("name", SerializeAndReadField(entry, "descriptionSource"));
    }

    // ── Parameter description sources (FR-510) ──────────────────────────────

    [Fact]
    public void Parameter_HelpParameter_Source_Reports_helpParameter()
    {
        var entry = SingleParameterEntryFor(new ParameterDescriptionRequest(
            CommandName: TestCommandName,
            ParameterName: "Name",
            ParameterTypeName: "System.String",
            HelpParameterDescription: "The thing's name.",
            HelpMessage: null,
            ValidateSetValues: null,
            ValidateSetAppliesToArrayElement: false));

        Assert.Equal(ParameterDescriptionSource.HelpParameter, entry.DescriptionSource);
        Assert.Equal("helpParameter", SerializeAndReadField(entry, "descriptionSource"));
    }

    [Fact]
    public void Parameter_HelpMessage_Source_Reports_helpMessage()
    {
        var entry = SingleParameterEntryFor(new ParameterDescriptionRequest(
            CommandName: TestCommandName,
            ParameterName: "Name",
            ParameterTypeName: "System.String",
            HelpParameterDescription: null,
            HelpMessage: "The thing's name (from attribute).",
            ValidateSetValues: null,
            ValidateSetAppliesToArrayElement: false));

        Assert.Equal(ParameterDescriptionSource.HelpMessage, entry.DescriptionSource);
        Assert.Equal("helpMessage", SerializeAndReadField(entry, "descriptionSource"));
    }

    [Fact]
    public void Parameter_ValidateSet_Source_Reports_validateSet()
    {
        var entry = SingleParameterEntryFor(new ParameterDescriptionRequest(
            CommandName: TestCommandName,
            ParameterName: "Mode",
            ParameterTypeName: "System.String",
            HelpParameterDescription: null,
            HelpMessage: null,
            ValidateSetValues: new[] { "A", "B", "C" },
            ValidateSetAppliesToArrayElement: false));

        Assert.Equal(ParameterDescriptionSource.ValidateSet, entry.DescriptionSource);
        Assert.Equal("validateSet", SerializeAndReadField(entry, "descriptionSource"));
    }

    [Fact]
    public void Parameter_TypeFallback_Source_Reports_typeFallback()
    {
        var entry = SingleParameterEntryFor(new ParameterDescriptionRequest(
            CommandName: TestCommandName,
            ParameterName: "Other",
            ParameterTypeName: "System.Int32",
            HelpParameterDescription: null,
            HelpMessage: null,
            ValidateSetValues: null,
            ValidateSetAppliesToArrayElement: false));

        Assert.Equal(ParameterDescriptionSource.TypeFallback, entry.DescriptionSource);
        Assert.Equal("typeFallback", SerializeAndReadField(entry, "descriptionSource"));
    }

    // ── Aggregation behavior ────────────────────────────────────────────────

    [Fact]
    public void Tracker_RecordToolSource_KeepsFirstWhenCalledMultipleTimes()
    {
        var tracker = new ToolDescriptionSourceTracker();
        tracker.RecordToolSource(TestCommandName, ToolDescriptionSource.Synopsis);
        tracker.RecordToolSource(TestCommandName, ToolDescriptionSource.Syntax);

        Assert.Equal(ToolDescriptionSource.Synopsis, tracker.ToolSources[TestCommandName]);
    }

    [Fact]
    public void Tracker_RecordParameterSource_KeepsFirstWhenCalledMultipleTimes()
    {
        var tracker = new ToolDescriptionSourceTracker();
        tracker.RecordParameterSource(TestCommandName, "Name", ParameterDescriptionSource.HelpParameter);
        tracker.RecordParameterSource(TestCommandName, "Name", ParameterDescriptionSource.TypeFallback);

        Assert.Equal(
            ParameterDescriptionSource.HelpParameter,
            tracker.ParameterSources[TestCommandName]["Name"]);
    }

    [Fact]
    public void BuildToolDescriptionEntries_NoTracker_ReturnsEmpty()
    {
        var entries = DoctorService.BuildToolDescriptionEntries(new List<McpServerTool>(), tracker: null);
        Assert.Empty(entries);
    }

    [Fact]
    public void BuildToolDescriptionEntries_PopulatesNestedParameters()
    {
        var tracker = new ToolDescriptionSourceTracker();
        tracker.RecordToolSource(TestCommandName, ToolDescriptionSource.Synopsis);
        tracker.RecordParameterSource(TestCommandName, "Name", ParameterDescriptionSource.HelpParameter);
        tracker.RecordParameterSource(TestCommandName, "Force", ParameterDescriptionSource.HelpMessage);

        var entries = DoctorService.BuildToolDescriptionEntries(new List<McpServerTool>(), tracker);

        var entry = Assert.Single(entries);
        Assert.Equal(TestCommandName, entry.Name);
        Assert.Equal(TestCommandName, entry.CommandName);
        Assert.Equal(ToolDescriptionSource.Synopsis, entry.DescriptionSource);
        Assert.Equal(2, entry.Parameters.Count);
        // Parameters are sorted alphabetically for deterministic doctor output.
        Assert.Equal("Force", entry.Parameters[0].Name);
        Assert.Equal(ParameterDescriptionSource.HelpMessage, entry.Parameters[0].DescriptionSource);
        Assert.Equal("Name", entry.Parameters[1].Name);
        Assert.Equal(ParameterDescriptionSource.HelpParameter, entry.Parameters[1].DescriptionSource);
    }

    // ── DescriptionSourceVocabulary mapping ─────────────────────────────────

    [Theory]
    [InlineData(ToolDescriptionSource.Synopsis, "synopsis")]
    [InlineData(ToolDescriptionSource.Description, "description")]
    [InlineData(ToolDescriptionSource.Syntax, "syntax")]
    [InlineData(ToolDescriptionSource.Name, "name")]
    public void Vocabulary_ToolSource_MapsToFr583Literals(ToolDescriptionSource source, string expected)
    {
        Assert.Equal(expected, DescriptionSourceVocabulary.ToWireValue(source));
    }

    [Theory]
    [InlineData(ParameterDescriptionSource.HelpParameter, "helpParameter")]
    [InlineData(ParameterDescriptionSource.HelpMessage, "helpMessage")]
    [InlineData(ParameterDescriptionSource.ValidateSet, "validateSet")]
    [InlineData(ParameterDescriptionSource.TypeFallback, "typeFallback")]
    public void Vocabulary_ParameterSource_MapsToFr583Literals(ParameterDescriptionSource source, string expected)
    {
        Assert.Equal(expected, DescriptionSourceVocabulary.ToWireValue(source));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static (List<ToolDescriptionDoctorEntry> entries, ToolDescriptionSourceTracker tracker)
        BuildEntriesForToolRequest(ToolDescriptionRequest request)
    {
        var tracker = new ToolDescriptionSourceTracker();
        var resolver = new HelpAwareToolMetadataSource();
        var result = resolver.ResolveToolDescription(in request);
        tracker.RecordToolSource(request.CommandName, result.Source);

        var entries = DoctorService.BuildToolDescriptionEntries(new List<McpServerTool>(), tracker);
        return (entries, tracker);
    }

    private static ParameterDescriptionDoctorEntry SingleParameterEntryFor(ParameterDescriptionRequest request)
    {
        var tracker = new ToolDescriptionSourceTracker();
        var resolver = new HelpAwareToolMetadataSource();
        var result = resolver.ResolveParameterDescription(in request);
        tracker.RecordParameterSource(request.CommandName, request.ParameterName, result.Source);

        var entries = DoctorService.BuildToolDescriptionEntries(new List<McpServerTool>(), tracker);
        var toolEntry = Assert.Single(entries);
        return Assert.Single(toolEntry.Parameters);
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
