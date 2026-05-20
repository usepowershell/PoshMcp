using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PoshMcp.Server.McpResources;
using Xunit;

namespace PoshMcp.Tests.Unit.McpResources;

/// <summary>
/// Unit tests for <see cref="NounRegistry"/> — noun extraction, resource name derivation,
/// conflict resolution, and lookup methods (Spec 012, §2 and §3).
/// </summary>
[Trait("Category", "Unit")]
public class NounRegistryTests
{
    // ── Capturing logger for warning-verification tests ──────────────────────

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Messages.Add($"[{logLevel}] {formatter(state, exception)}");
        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    // ── Group 1: Build — basic registration ──────────────────────────────────

    [Fact]
    public void Build_WithSingleGetCommand_RegistersNounEntry()
    {
        var registry = NounRegistry.Build(["Get-Foo"], NullLogger.Instance);

        var entry = registry.GetEntry("Foo");

        Assert.NotNull(entry);
        Assert.Equal("Foo", entry.Noun);
        Assert.Equal("foo", entry.ResourceName);
        Assert.Equal("poshmcp://resources/foo", entry.Uri);
        Assert.Equal("Get-Foo", entry.CanonicalGetCommand);
        Assert.False(entry.IsConflicted);
    }

    [Fact]
    public void Build_WithGetSetRemoveCommands_RegistersOnlyOneEntry()
    {
        var registry = NounRegistry.Build(["Get-Foo", "Set-Foo", "Remove-Foo"], NullLogger.Instance);

        Assert.Single(registry.AllEntries);
        Assert.NotNull(registry.GetEntry("Foo"));
    }

    [Fact]
    public void Build_WithOnlySetCommand_RegistersNoEntries()
    {
        var registry = NounRegistry.Build(["Set-Foo"], NullLogger.Instance);

        Assert.Empty(registry.AllEntries);
        Assert.Null(registry.GetEntry("Foo"));
    }

    [Fact]
    public void Build_WithGetCommandRequiringUserParameters_DoesNotRegisterEntry()
    {
        var registry = NounRegistry.Build(
            [new NounCommandCandidate("Get-Foo", CanInvokeWithoutRequiredUserParameters: false)],
            NullLogger.Instance);

        Assert.Empty(registry.AllEntries);
        Assert.Null(registry.GetEntry("Foo"));
        Assert.Null(registry.GetEntryByResourceName("foo"));
    }

    [Fact]
    public void Build_WithEmptyCommandList_ReturnsEmptyRegistry()
    {
        var registry = NounRegistry.Build(Array.Empty<string>(), NullLogger.Instance);

        Assert.Empty(registry.AllEntries);
        Assert.Null(registry.GetEntry("Foo"));
        Assert.Null(registry.GetEntryByResourceName("foo"));
    }

    // ── Group 2: Resource name derivation ────────────────────────────────────

    [Theory]
    [InlineData("BamiTenantUser", "bami_tenant_user")]
    [InlineData("User", "user")]
    [InlineData("HTMLParser", "html_parser")]
    [InlineData("HTTPSProxy", "https_proxy")]
    [InlineData("MyService", "my_service")]
    public void DeriveResourceName_PascalCaseNoun_ProducesSnakeCaseName(string noun, string expected)
    {
        Assert.Equal(expected, NounRegistry.DeriveResourceName(noun));
    }

    [Theory]
    [InlineData("Get-BamiTenantUser", "bami_tenant_user")]
    [InlineData("Get-User", "user")]
    [InlineData("Get-HTMLParser", "html_parser")]
    [InlineData("Get-HTTPSProxy", "https_proxy")]
    [InlineData("Get-MyService", "my_service")]
    public void Build_GetCommand_ProducesCorrectResourceName(string command, string expectedResourceName)
    {
        var registry = NounRegistry.Build([command], NullLogger.Instance);
        var entry = registry.GetEntry(NounRegistry.ExtractNounFromCommandName(command)!);

        Assert.NotNull(entry);
        Assert.Equal(expectedResourceName, entry.ResourceName);
    }

    // ── Group 3: Noun extraction ──────────────────────────────────────────────

    [Theory]
    [InlineData("Get-BamiTenantUser", "BamiTenantUser")]
    [InlineData("Get-User", "User")]
    [InlineData("Set-BamiTenantUser", "BamiTenantUser")]
    public void ExtractNounFromCommandName_VerbNounCommand_ReturnsNoun(string command, string expectedNoun)
    {
        Assert.Equal(expectedNoun, NounRegistry.ExtractNounFromCommandName(command));
    }

    [Fact]
    public void ExtractNounFromCommandName_ModuleQualified_ReturnsNoun()
    {
        Assert.Equal("User", NounRegistry.ExtractNounFromCommandName("ModuleA\\Get-User"));
    }

    [Fact]
    public void ExtractNounFromCommandName_NoHyphen_ReturnsNull()
    {
        Assert.Null(NounRegistry.ExtractNounFromCommandName("NoHyphen"));
    }

    [Fact]
    public void ExtractNounFromCommandName_TrailingDash_ReturnsNull()
    {
        Assert.Null(NounRegistry.ExtractNounFromCommandName("TrailingDash-"));
    }

    [Fact]
    public void Build_WithModuleQualifiedGetCommand_RegistersNounEntry()
    {
        var registry = NounRegistry.Build(["ModuleA\\Get-User"], NullLogger.Instance);

        Assert.Single(registry.AllEntries);
        Assert.NotNull(registry.GetEntry("User"));
        Assert.Equal("user", registry.GetEntry("User")!.ResourceName);
    }

    [Fact]
    public void Build_WithNoHyphenCommand_RegistersNoEntries()
    {
        var registry = NounRegistry.Build(["NoHyphen"], NullLogger.Instance);

        Assert.Empty(registry.AllEntries);
    }

    [Fact]
    public void Build_WithTrailingDashCommand_RegistersNoEntries()
    {
        var registry = NounRegistry.Build(["TrailingDash-"], NullLogger.Instance);

        Assert.Empty(registry.AllEntries);
    }

    // ── Group 4: Conflict resolution ─────────────────────────────────────────

    [Fact]
    public void Build_WithTwoDistinctNouns_RegistersTwoNonConflictedEntries()
    {
        var registry = NounRegistry.Build(["Get-Foo", "Get-Bar"], NullLogger.Instance);

        Assert.Equal(2, registry.AllEntries.Count);
        Assert.False(registry.GetEntry("Foo")!.IsConflicted);
        Assert.False(registry.GetEntry("Bar")!.IsConflicted);
    }

    [Fact]
    public void Build_WithDuplicateGetCommands_FirstWinsSecondConflicted()
    {
        var registry = NounRegistry.Build(["Get-Foo", "Get-Foo"], NullLogger.Instance);

        Assert.Equal(2, registry.AllEntries.Count);

        var winner = registry.GetEntry("Foo");
        Assert.NotNull(winner);
        Assert.False(winner.IsConflicted);
    }

    [Fact]
    public void Build_WithDuplicateGetCommands_AllEntriesContainsBothWinnerAndConflicted()
    {
        var registry = NounRegistry.Build(["Get-Foo", "Get-Foo"], NullLogger.Instance);

        Assert.Equal(2, registry.AllEntries.Count);
        Assert.Contains(registry.AllEntries, e => !e.IsConflicted);
        Assert.Contains(registry.AllEntries, e => e.IsConflicted);
    }

    [Fact]
    public void Build_WithConflictingGetCommands_GetEntryReturnsWinner()
    {
        var registry = NounRegistry.Build(["Get-Foo", "Get-Foo"], NullLogger.Instance);

        var entry = registry.GetEntry("Foo");

        Assert.NotNull(entry);
        Assert.False(entry.IsConflicted);
    }

    [Fact]
    public void Build_WithConflictingGetCommands_GetEntryByResourceNameReturnsWinner()
    {
        var registry = NounRegistry.Build(["Get-Foo", "Get-Foo"], NullLogger.Instance);

        var entry = registry.GetEntryByResourceName("foo");

        Assert.NotNull(entry);
        Assert.False(entry.IsConflicted);
    }

    [Fact]
    public void Build_WithConflictingGetCommands_LogsWarning()
    {
        var logger = new CapturingLogger();
        NounRegistry.Build(["Get-Foo", "Get-Foo"], logger);

        Assert.Contains(logger.Messages, m => m.StartsWith("[Warning]", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_WithModuleQualifiedConflictingCommands_FirstWinsSecondConflicted()
    {
        var logger = new CapturingLogger();
        var registry = NounRegistry.Build(["ModuleA\\Get-User", "ModuleB\\Get-User"], logger);

        Assert.Equal(2, registry.AllEntries.Count);
        Assert.NotNull(registry.GetEntry("User"));
        Assert.False(registry.GetEntry("User")!.IsConflicted);
        Assert.Contains(logger.Messages, m => m.StartsWith("[Warning]", StringComparison.OrdinalIgnoreCase));
    }

    // ── Group 5: Lookup methods ───────────────────────────────────────────────

    [Fact]
    public void GetEntry_WithKnownNoun_ReturnsEntry()
    {
        var registry = NounRegistry.Build(["Get-Foo"], NullLogger.Instance);

        Assert.NotNull(registry.GetEntry("Foo"));
    }

    [Fact]
    public void GetEntry_WithUnknownNoun_ReturnsNull()
    {
        var registry = NounRegistry.Build(["Get-Foo"], NullLogger.Instance);

        Assert.Null(registry.GetEntry("Bar"));
    }

    [Theory]
    [InlineData("Foo")]
    [InlineData("foo")]
    [InlineData("FOO")]
    [InlineData("fOo")]
    public void GetEntry_CaseInsensitive_ReturnsEntry(string noun)
    {
        var registry = NounRegistry.Build(["Get-Foo"], NullLogger.Instance);

        Assert.NotNull(registry.GetEntry(noun));
    }

    [Fact]
    public void GetEntryByResourceName_WithKnownName_ReturnsEntry()
    {
        var registry = NounRegistry.Build(["Get-Foo"], NullLogger.Instance);

        Assert.NotNull(registry.GetEntryByResourceName("foo"));
    }

    [Fact]
    public void GetEntryByResourceName_WithUnknownName_ReturnsNull()
    {
        var registry = NounRegistry.Build(["Get-Foo"], NullLogger.Instance);

        Assert.Null(registry.GetEntryByResourceName("bar"));
    }

    [Theory]
    [InlineData("foo")]
    [InlineData("Foo")]
    [InlineData("FOO")]
    [InlineData("fOo")]
    public void GetEntryByResourceName_CaseInsensitive_ReturnsEntry(string resourceName)
    {
        var registry = NounRegistry.Build(["Get-Foo"], NullLogger.Instance);

        Assert.NotNull(registry.GetEntryByResourceName(resourceName));
    }

    // ── Group 6: URI format ───────────────────────────────────────────────────

    [Fact]
    public void Build_Entry_HasCorrectUriFormat()
    {
        var registry = NounRegistry.Build(["Get-BamiTenantUser"], NullLogger.Instance);

        var entry = registry.GetEntry("BamiTenantUser");

        Assert.NotNull(entry);
        Assert.Equal("poshmcp://resources/bami_tenant_user", entry.Uri);
        Assert.StartsWith("poshmcp://resources/", entry.Uri);
        Assert.EndsWith(entry.ResourceName, entry.Uri);
    }

    [Theory]
    [InlineData("Get-Foo", "poshmcp://resources/foo")]
    [InlineData("Get-BamiTenantUser", "poshmcp://resources/bami_tenant_user")]
    [InlineData("Get-HTMLParser", "poshmcp://resources/html_parser")]
    public void Build_Entry_UriMatchesExpectedPrefix_AndResourceName(string command, string expectedUri)
    {
        var registry = NounRegistry.Build([command], NullLogger.Instance);
        var noun = NounRegistry.ExtractNounFromCommandName(command)!;
        var entry = registry.GetEntry(noun);

        Assert.NotNull(entry);
        Assert.Equal(expectedUri, entry.Uri);
    }
}
