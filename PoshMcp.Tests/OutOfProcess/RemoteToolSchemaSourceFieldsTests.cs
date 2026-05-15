using System.Collections.Generic;
using Newtonsoft.Json;
using PoshMcp.Server.PowerShell.OutOfProcess;
using Xunit;

namespace PoshMcp.Tests.OutOfProcess;

/// <summary>
/// Spec 011 Phase 2 (Issue #268): unit tests for the new wire-format
/// additions on <see cref="RemoteToolSchema"/> and the parallel
/// <see cref="RemoteModuleImportsPayload"/> top-level object emitted by
/// the OOP host on the <c>discover</c> response.
/// </summary>
[Trait("Category", "OutOfProcess")]
public class RemoteToolSchemaSourceFieldsTests
{
    [Fact]
    public void SourceFields_DefaultToNull()
    {
        var schema = new RemoteToolSchema();
        Assert.Null(schema.SourceModule);
        Assert.Null(schema.SourcePattern);
        Assert.Null(schema.SourceDetail);
    }

    [Fact]
    public void SourceFields_RoundTripJson()
    {
        var original = new RemoteToolSchema
        {
            Name = "Get-AzContext",
            SourceModule = "Az.Accounts",
            SourcePattern = null,
            SourceDetail = "Az.Accounts",
        };
        var json = JsonConvert.SerializeObject(original);
        var deserialized = JsonConvert.DeserializeObject<RemoteToolSchema>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("Az.Accounts", deserialized.SourceModule);
        Assert.Null(deserialized.SourcePattern);
        Assert.Equal("Az.Accounts", deserialized.SourceDetail);
    }

    [Fact]
    public void OlderHostJson_LackingSourceFields_DeserializesWithNulls()
    {
        // SC-263-4: backward compat. JSON emitted by an older OOP host (predating
        // spec 011) has no SourceModule / SourcePattern / SourceDetail properties.
        const string olderHostJson =
            "{\"Name\":\"Get-Foo\",\"Description\":\"\",\"Parameters\":[]}";

        var schema = JsonConvert.DeserializeObject<RemoteToolSchema>(olderHostJson);

        Assert.NotNull(schema);
        Assert.Equal("Get-Foo", schema.Name);
        Assert.Null(schema.SourceModule);
        Assert.Null(schema.SourcePattern);
        Assert.Null(schema.SourceDetail);
    }

    [Fact]
    public void RemoteModuleImportsPayload_RoundTripJson()
    {
        var original = new RemoteModuleImportsPayload
        {
            Modules =
            [
                new RemoteModuleProbe { Name = "Az.Accounts", Found = true, Version = "2.13.0", Path = "C:\\Az" },
                new RemoteModuleProbe { Name = "Az.Compute", Found = false, Version = null, Path = null },
            ],
            Patterns =
            [
                new RemotePatternMatch { Pattern = "Get-*", Kind = "include", Role = "filter", MatchedCount = 5 },
                new RemotePatternMatch { Pattern = "*-Service", Kind = "exclude", Role = "exclude", MatchedCount = 0 },
            ],
        };
        var json = JsonConvert.SerializeObject(original);
        var deserialized = JsonConvert.DeserializeObject<RemoteModuleImportsPayload>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.Modules.Count);
        Assert.Equal("Az.Accounts", deserialized.Modules[0].Name);
        Assert.True(deserialized.Modules[0].Found);
        Assert.Equal("2.13.0", deserialized.Modules[0].Version);
        Assert.False(deserialized.Modules[1].Found);
        Assert.Null(deserialized.Modules[1].Version);
        Assert.Equal(2, deserialized.Patterns.Count);
        Assert.Equal("include", deserialized.Patterns[0].Kind);
        Assert.Equal("filter", deserialized.Patterns[0].Role);
        Assert.Equal(5, deserialized.Patterns[0].MatchedCount);
        Assert.Equal("exclude", deserialized.Patterns[1].Kind);
    }

    [Fact]
    public void RemoteModuleImportsPayload_DefaultsToEmptyCollections()
    {
        var payload = new RemoteModuleImportsPayload();
        Assert.NotNull(payload.Modules);
        Assert.Empty(payload.Modules);
        Assert.NotNull(payload.Patterns);
        Assert.Empty(payload.Patterns);
    }
}
