using Microsoft.Extensions.Logging.Abstractions;
using PoshMcp.Server.PowerShell;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class PowerShellHelpResolverCachingTests
{
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
    public void ClearCache_ThenResolve_InvokesGetHelpAgain()
    {
        using var powerShell = PSPowerShell.Create();
        var resolver = new PowerShellHelpResolver();

        var first = resolver.Resolve("Get-Date", powerShell, NullLogger.Instance);
        resolver.ClearCache();
        var second = resolver.Resolve("Get-Date", powerShell, NullLogger.Instance);

        Assert.NotSame(CommandHelpInfo.Empty, first);
        Assert.NotSame(CommandHelpInfo.Empty, second);
        Assert.NotSame(first, second);
        Assert.Equal(first.Synopsis, second.Synopsis);
        Assert.Equal(first.LongDescription, second.LongDescription);
        Assert.Equal(first.ParameterDescriptions, second.ParameterDescriptions);
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

    [Fact]
    public void CachingContract_OneGetHelpInvocation()
    {
        using var powerShell = PSPowerShell.Create();
        var resolver = new PowerShellHelpResolver();

        var first = resolver.Resolve("Get-Process", powerShell, NullLogger.Instance);
        var second = resolver.Resolve("Get-Process", powerShell, NullLogger.Instance);

        Assert.NotSame(CommandHelpInfo.Empty, first);
        Assert.Same(first, second);
    }

    private static PSPowerShell CreateDisposedPowerShell()
    {
        var powerShell = PSPowerShell.Create();
        powerShell.Dispose();
        return powerShell;
    }
}
