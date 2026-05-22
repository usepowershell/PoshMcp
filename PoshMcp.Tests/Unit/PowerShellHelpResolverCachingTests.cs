using Microsoft.Extensions.Logging.Abstractions;
using PoshMcp.Server.PowerShell;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class PowerShellHelpResolverCachingTests
{
    // Individual Get-Help-backed cases stay under the issue's 500ms per-test budget, so this
    // coverage remains in Unit/ even though the filtered suite's cumulative runtime is higher.

    [Fact]
    public void Resolve_SecondCall_ReturnsCachedResult()
    {
        using var powerShell = PSPowerShell.Create();
        var resolver = new PowerShellHelpResolver();

        var first = resolver.Resolve("Get-Date", powerShell, NullLogger.Instance);
        var second = resolver.Resolve("Get-Date", powerShell, NullLogger.Instance);

        Assert.NotSame(CommandHelpInfo.Empty, first);
        Assert.Same(first, second);
    }

    [Fact]
    public void ClearCache_ThenTwoResolves_ReusesSinglePostClearLookup()
    {
        using var powerShell = PSPowerShell.Create();
        var resolver = new PowerShellHelpResolver();

        var beforeClear = resolver.Resolve("Get-Date", powerShell, NullLogger.Instance);
        resolver.ClearCache();

        var firstAfterClear = resolver.Resolve("Get-Date", powerShell, NullLogger.Instance);
        var secondAfterClear = resolver.Resolve("Get-Date", powerShell, NullLogger.Instance);

        Assert.NotSame(CommandHelpInfo.Empty, beforeClear);
        Assert.NotSame(CommandHelpInfo.Empty, firstAfterClear);
        Assert.NotSame(beforeClear, firstAfterClear);
        Assert.Equal(beforeClear.Synopsis, firstAfterClear.Synopsis);
        Assert.Equal(beforeClear.LongDescription, firstAfterClear.LongDescription);
        Assert.Equal(beforeClear.ParameterDescriptions, firstAfterClear.ParameterDescriptions);

        // Resolve uses ConcurrentDictionary.GetOrAdd, so returning the exact same cached instance
        // on the second post-clear call proves the value factory (ResolveCore/Get-Help) did not
        // run a second time for that command after ClearCache.
        Assert.Same(firstAfterClear, secondAfterClear);
    }

    [Fact]
    public void Resolve_DifferentCommands_CachesIndependently()
    {
        using var powerShell = PSPowerShell.Create();
        var resolver = new PowerShellHelpResolver();

        var getDateFirst = resolver.Resolve("Get-Date", powerShell, NullLogger.Instance);
        var getProcessFirst = resolver.Resolve("Get-Process", powerShell, NullLogger.Instance);
        var getDateSecond = resolver.Resolve("Get-Date", powerShell, NullLogger.Instance);
        var getProcessSecond = resolver.Resolve("Get-Process", powerShell, NullLogger.Instance);

        Assert.NotSame(CommandHelpInfo.Empty, getDateFirst);
        Assert.NotSame(CommandHelpInfo.Empty, getProcessFirst);
        Assert.NotSame(getDateFirst, getProcessFirst);
        Assert.Same(getDateFirst, getDateSecond);
        Assert.Same(getProcessFirst, getProcessSecond);
    }

    [Fact]
    public void Resolve_EmptyCommandName_ReturnsEmpty()
    {
        using var powerShell = CreateDisposedPowerShell();
        var resolver = new PowerShellHelpResolver();

        var result = resolver.Resolve(string.Empty, powerShell, NullLogger.Instance);

        Assert.Same(CommandHelpInfo.Empty, result);
    }

    [Fact]
    public void Resolve_NullCommandName_ReturnsEmpty()
    {
        using var powerShell = CreateDisposedPowerShell();
        var resolver = new PowerShellHelpResolver();

        var result = resolver.Resolve(null!, powerShell, NullLogger.Instance);

        Assert.Same(CommandHelpInfo.Empty, result);
    }

    [Fact]
    public void Resolve_WhitespaceCommandName_ReturnsEmpty()
    {
        using var powerShell = CreateDisposedPowerShell();
        var resolver = new PowerShellHelpResolver();

        var result = resolver.Resolve("  ", powerShell, NullLogger.Instance);

        Assert.Same(CommandHelpInfo.Empty, result);
    }

    [Fact]
    public void Resolve_ExceptionDuringGetHelp_ReturnsEmpty()
    {
        using var powerShell = CreateDisposedPowerShell();
        var resolver = new PowerShellHelpResolver();

        var result = resolver.Resolve("Get-Date", powerShell, NullLogger.Instance);

        Assert.Same(CommandHelpInfo.Empty, result);
    }

    [Fact]
    public void Resolve_KnownCommand_ReturnsNonEmpty()
    {
        using var powerShell = PSPowerShell.Create();
        var resolver = new PowerShellHelpResolver();

        var result = resolver.Resolve("Get-Date", powerShell, NullLogger.Instance);

        Assert.NotSame(CommandHelpInfo.Empty, result);
        Assert.True(
            !string.IsNullOrWhiteSpace(result.Synopsis) ||
            !string.IsNullOrWhiteSpace(result.LongDescription) ||
            result.ParameterDescriptions.Count > 0,
            "Expected Get-Date help to include a synopsis, description, or parameter help.");
    }

    private static PSPowerShell CreateDisposedPowerShell()
    {
        var powerShell = PSPowerShell.Create();
        powerShell.Dispose();
        return powerShell;
    }
}
