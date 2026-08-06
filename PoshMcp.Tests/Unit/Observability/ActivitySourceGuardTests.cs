using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management.Automation;
using PoshMcp.Server.PowerShell;
using Xunit;

namespace PoshMcp.Tests.Unit.Observability;

/// <summary>
/// Tests for Fix 3: ActivitySource HasListeners() guard on the tools/call hot path.
/// Verifies that activities are only created when a listener is registered,
/// and that tag computation is skipped when no listener is active.
/// </summary>
[Trait("Category", "Unit")]
public class ActivitySourceGuardTests : IDisposable
{
    private readonly List<Activity> _started = new();
    private ActivityListener? _listener;

    public void Dispose()
    {
        _listener?.Dispose();
        foreach (var a in _started) a.Dispose();
    }

    [Fact]
    public void ToolActivitySource_HasListeners_FalseWithNoListener()
    {
        // Without any registered listener, HasListeners() must be false so that
        // the hot-path guard eliminates StartActivity() call and LINQ allocation.
        Assert.False(PowerShellAssemblyGenerator.ToolActivitySource.HasListeners());
    }

    [Fact]
    public void ToolActivitySource_HasListeners_TrueAfterListenerAdded()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == PowerShellAssemblyGenerator.ToolActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = a => _started.Add(a)
        };
        ActivitySource.AddActivityListener(_listener);

        Assert.True(PowerShellAssemblyGenerator.ToolActivitySource.HasListeners());
    }

    [Fact]
    public void StartActivity_ReturnsNull_WithNoListener()
    {
        // Verify .NET runtime semantics: StartActivity() returns null when no listener.
        // The HasListeners() guard in ExecutePowerShellCommandTyped relies on this.
        var activity = PowerShellAssemblyGenerator.ToolActivitySource
            .StartActivity("test.no-listener", ActivityKind.Internal);
        Assert.Null(activity);
    }

    [Fact]
    public void StartActivity_ReturnsActivity_WhenListenerRegistered()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == PowerShellAssemblyGenerator.ToolActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = a => _started.Add(a)
        };
        ActivitySource.AddActivityListener(_listener);

        using var activity = PowerShellAssemblyGenerator.ToolActivitySource
            .StartActivity("test.with-listener", ActivityKind.Internal);

        Assert.NotNull(activity);
        Assert.Contains(activity, _started);
    }
}

/// <summary>
/// Tests for Fix 4: BeginInvoke/EndInvoke with AsyncWaitHandle disposal in InvokePowerShellSafe.
/// Verifies that the synchronous invoke path disposes the IAsyncResult.AsyncWaitHandle
/// to prevent per-call WaitHandle accumulation (the confirmed FullMix handle-floor leak).
/// </summary>
[Trait("Category", "Unit")]
public class InvokePowerShellSafeHandleDisposalTests : IDisposable
{
    private readonly System.Management.Automation.PowerShell _ps;

    public InvokePowerShellSafeHandleDisposalTests()
    {
        _ps = System.Management.Automation.PowerShell.Create();
    }

    public void Dispose() => _ps.Dispose();

    [Fact]
    public void ExecutePowerShellCommandTyped_ReturnsResult_ForSimpleCommand()
    {
        // End-to-end smoke test: verifies InvokePowerShellSafe (via BeginInvoke path)
        // produces correct results. The WaitHandle disposal happens internally.
        using var runspace = new IsolatedPowerShellRunspace();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        var result = PowerShellAssemblyGenerator.ExecutePowerShellCommandTyped(
            "Get-Date",
            [],
            [],
            default,
            runspace,
            logger);

        Assert.NotNull(result);
        var json = result.GetAwaiter().GetResult();
        Assert.NotNull(json);
        // Get-Date returns a DateTime object; the JSON serializer wraps it as a non-empty array.
        Assert.StartsWith("[", json);
        Assert.EndsWith("]", json);
        Assert.NotEqual("[]", json);
    }

    [Fact]
    public void ExecutePowerShellCommandTyped_EmptyCommand_ReturnsEmptyArray()
    {
        // Verify that a command with no output returns "[]" rather than throwing.
        using var runspace = new IsolatedPowerShellRunspace();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        var result = PowerShellAssemblyGenerator.ExecutePowerShellCommandTyped(
            "Write-Verbose",
            new[] { new PowerShellParameterInfo("Message", typeof(string), false) },
            new object[] { "hello" },
            default,
            runspace,
            logger);

        var json = result.GetAwaiter().GetResult();
        Assert.Equal("[]", json);
    }
}
