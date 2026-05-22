using System;
using System.Collections.Generic;
using PoshMcp.Server.PowerShell;
using Xunit;

namespace PoshMcp.Tests.Unit;

[Collection("PropertySetDiscoveryTests")]
[Trait("Category", "Unit")]
public sealed class PropertySetDiscoveryTests
{
    private const string KnownCommand = "Get-Process";
    private const string MissingCommand = "Not-A-Real-Command-XYZ";

    public PropertySetDiscoveryTests()
    {
        PropertySetDiscovery.ClearCache();
    }

    [Fact]
    public void DiscoverDefaultDisplayProperties_NullCommandName_ReturnsNull()
    {
        var result = PropertySetDiscovery.DiscoverDefaultDisplayProperties(commandName: null!);

        Assert.Null(result);
    }

    [Fact]
    public void DiscoverDefaultDisplayProperties_EmptyCommandName_ReturnsNull()
    {
        var result = PropertySetDiscovery.DiscoverDefaultDisplayProperties(string.Empty);

        Assert.Null(result);
    }

    [Fact]
    public void DiscoverDefaultDisplayProperties_WhitespaceCommandName_ReturnsNull()
    {
        var result = PropertySetDiscovery.DiscoverDefaultDisplayProperties("  ");

        Assert.Null(result);
    }

    [Fact]
    public void DiscoverDefaultDisplayProperties_KnownCommand_ReturnsCachedOnSecondCall()
    {
        var first = PropertySetDiscovery.DiscoverDefaultDisplayProperties(KnownCommand);
        var second = PropertySetDiscovery.DiscoverDefaultDisplayProperties(KnownCommand);

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void DiscoverDefaultDisplayProperties_NonexistentCommand_ReturnsNull()
    {
        var result = PropertySetDiscovery.DiscoverDefaultDisplayProperties(MissingCommand);

        Assert.Null(result);
    }

    [Fact]
    public void ClearCache_ThenReDiscover_ReturnsNewResult()
    {
        var first = PropertySetDiscovery.DiscoverDefaultDisplayProperties(KnownCommand);

        PropertySetDiscovery.ClearCache();

        var second = PropertySetDiscovery.DiscoverDefaultDisplayProperties(KnownCommand);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Contains("Id", second, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Name", second, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiscoverAll_NullInput_ReturnsEmptyDictionary()
    {
        var result = PropertySetDiscovery.DiscoverAll(commandNames: null!);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void DiscoverAll_EmptyList_ReturnsEmptyDictionary()
    {
        var result = PropertySetDiscovery.DiscoverAll(Array.Empty<string>());

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void DiscoverAll_WithCommands_ReturnsDictionaryWithEntries()
    {
        var result = PropertySetDiscovery.DiscoverAll(new[] { KnownCommand });

        Assert.True(result.TryGetValue(KnownCommand, out var properties));
        Assert.NotNull(properties);
        Assert.Contains("Id", properties, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Name", properties, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiscoverAll_CachesResults()
    {
        var batch = PropertySetDiscovery.DiscoverAll(new[] { KnownCommand });
        var cached = PropertySetDiscovery.DiscoverDefaultDisplayProperties(KnownCommand);

        var properties = Assert.IsAssignableFrom<IReadOnlyList<string>>(batch[KnownCommand]);
        Assert.Same(properties, cached);
    }

    [Fact]
    public void DiscoverAll_NonexistentCommand_CachesNullEntry()
    {
        var result = PropertySetDiscovery.DiscoverAll(new[] { KnownCommand, MissingCommand });
        var cached = PropertySetDiscovery.DiscoverDefaultDisplayProperties(MissingCommand);

        Assert.True(result.ContainsKey(MissingCommand));
        Assert.Null(result[MissingCommand]);
        Assert.Null(cached);
    }

    [Fact]
    public void DiscoverDefaultDisplayProperties_GetProcess_ReturnsProperties()
    {
        var result = PropertySetDiscovery.DiscoverDefaultDisplayProperties(KnownCommand);

        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Contains("Id", result, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Name", result, StringComparer.OrdinalIgnoreCase);
    }
}
