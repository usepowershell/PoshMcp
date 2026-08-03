using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation.Runspaces;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.Server.PowerShell.Pool;

/// <summary>
/// Executes the reset protocol on a <see cref="RunspaceWorker"/> after each lease.
/// Reset restores the runspace to a worker-initialized baseline, clearing all
/// request-scoped state while preserving variables and drives established by the startup script.
/// </summary>
/// <remarks>
/// <para>
/// State cleared per reset cycle:
/// <list type="bullet">
///   <item>Pending commands and pipeline streams (<c>Commands.Clear</c> / <c>Streams.ClearStreams</c>)</item>
///   <item><c>$Error</c> collection (<c>$Error.Clear()</c>)</item>
///   <item>Shell preference variables (reset to PS defaults)</item>
///   <item>User-defined variables not in the worker's initialization snapshot</item>
///   <item>Working location (reset to filesystem root of the current drive)</item>
///   <item>PSDrives created after warm initialization (drives in the worker snapshot are preserved)</item>
/// </list>
/// </para>
/// <para>
/// State preserved:
/// <list type="bullet">
///   <item>PS automatic variables (<c>$PSVersionTable</c>, <c>$IsWindows</c>, etc.)</item>
///   <item>Variables present in <see cref="RunspaceWorker.InitializedVariableNames"/>
///         (captured after the startup script ran)</item>
///   <item>PSDrives present in <see cref="RunspaceWorker.InitializedDriveNames"/>
///         (captured after the startup script ran)</item>
///   <item>Loaded modules and functions (module cleanup is deferred to worker eviction)</item>
/// </list>
/// </para>
/// <para>
/// Throws <see cref="InvalidOperationException"/> if the runspace is <c>Broken</c> before or
/// after reset. The caller (<see cref="StatelessRunspacePool"/>) must evict the worker on throw.
/// </para>
/// <para>
/// Throws <see cref="TimeoutException"/> if the pipeline does not stop within
/// <paramref name="stopTimeout"/> after a cancellation. The caller must evict the worker.
/// </para>
/// </remarks>
internal static class RunspaceResetProtocol
{
    // PS automatic variables and preference variables never removed during reset.
    // Preference variables are included here because we reset them explicitly above.
    // Internal inject variable names used by ResetAsync are also excluded.
    private static readonly IReadOnlySet<string> PsAutomaticVars = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        "_", "?", "args", "ConsoleFileName", "Error", "ErrorActionPreference",
        "WarningPreference", "VerbosePreference", "DebugPreference", "ProgressPreference",
        "InformationPreference", "ConfirmPreference", "WhatIfPreference",
        "ExecutionContext", "false", "HOME", "Host", "input", "IsCoreCLR",
        "IsLinux", "IsMacOS", "IsWindows", "MaximumErrorCount", "null", "PID",
        "PROFILE", "PSBoundParameters", "PSCommandPath", "PSCulture", "PSEdition",
        "PSHOME", "PSItem", "PSScriptRoot", "PSUICulture", "PSVersionTable",
        "PWD", "ShellId", "StackTrace", "true", "PSCmdlet",
        "PSDefaultParameterValues", "OFS", "OutputEncoding", "NestedPromptLevel",
        "PSModuleAutoLoadingPreference", "MaximumAliasCount", "MaximumDriveCount",
        "MaximumFunctionCount", "MaximumHistoryCount", "MaximumVariableCount",
        "PSSessionApplicationName", "PSSessionConfigurationName", "PSSessionOption",
        "Transcript", "PSEmailServer",
        "__PoshMcpResetExclude__", "__PoshMcpResetExcludeDrives__",
    };

    /// <summary>
    /// Resets the given worker's PowerShell state to its worker-initialized baseline.
    /// </summary>
    /// <param name="worker">The worker to reset. Must be in <c>Resetting</c> state.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="stopTimeout">
    /// Maximum time to wait for <c>PSPowerShell.Stop()</c> to complete after cancellation
    /// before throwing <see cref="TimeoutException"/>. The caller must evict on timeout.
    /// </param>
    /// <param name="cancellationToken">
    /// Used to cancel a long-running reset. On cancellation, <c>PSPowerShell.Stop()</c> is
    /// requested and the method waits up to <paramref name="stopTimeout"/> before throwing.
    /// </param>
    /// <exception cref="InvalidOperationException">Runspace is <c>Broken</c>.</exception>
    /// <exception cref="TimeoutException">
    /// Pipeline did not stop within <paramref name="stopTimeout"/> after cancellation.
    /// Caller must evict the worker as quarantined/uncertain.
    /// </exception>
    public static async Task ResetAsync(
        RunspaceWorker worker,
        ILogger logger,
        TimeSpan stopTimeout,
        CancellationToken cancellationToken = default)
    {
        var ps = worker.PowerShell;
        var runspace = ps.Runspace;

        ThrowIfBroken(runspace, "before");

        // 1. Clear pipeline immediately.
        ps.Commands.Clear();
        ps.Streams.ClearStreams();

        // 2. Build exclusion sets: automatic vars + worker-initialized vars/drives.
        var exclude = new HashSet<string>(PsAutomaticVars, StringComparer.OrdinalIgnoreCase);
        if (worker.InitializedVariableNames is { } initVars)
            exclude.UnionWith(initVars);

        var excludeDrives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (worker.InitializedDriveNames is { } initDrives)
            excludeDrives.UnionWith(initDrives);

        // 3. Inject exclusion lists directly into the runspace via SessionStateProxy
        //    instead of using param()+AddArgument, which can silently fail to bind
        //    a string[] positional argument in some PS SDK host configurations.
        var script = BuildResetScript();
        ps.Runspace.SessionStateProxy.SetVariable("__PoshMcpResetExclude__", exclude.ToArray());
        ps.Runspace.SessionStateProxy.SetVariable("__PoshMcpResetExcludeDrives__", excludeDrives.ToArray());
        ps.AddScript(script);

        // 4. Run reset in a background task. We do NOT pass a CancellationToken to Task.Run
        //    because Task.Run's CT only controls scheduling, not the running delegate.
        //    Actual pipeline cancellation is done via ps.Stop() below.
        var invokeTask = Task.Run(() =>
        {
            ps.Invoke();
            ps.Commands.Clear();
            ps.Streams.ClearStreams();
        });

        // Register cancellation handler that calls ps.Stop() to request pipeline stop.
        // CancellationTokenRegistration must be disposed to avoid a leak if ct never fires.
        await using var stopReg = cancellationToken.Register(
            static state => { try { ((PSPowerShell)state!).Stop(); } catch { /* best-effort */ } },
            ps,
            useSynchronizationContext: false);

        try
        {
            // WaitAsync(ct) unblocks when ct fires even while invokeTask is still running.
            await invokeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // ct fired; ps.Stop() was already requested via the cancellation registration.
            // Wait up to stopTimeout for the pipeline to actually finish.
            bool didStop = false;
            try
            {
                await invokeTask.WaitAsync(stopTimeout).ConfigureAwait(false);
                didStop = true;
            }
            catch (TimeoutException)
            {
                // Pipeline did not stop within StopTimeout. Worker is stuck/uncertain.
                logger.LogWarning(
                    "Reset pipeline did not stop within StopTimeout ({Timeout}); " +
                    "worker {CreatedAt} will be quarantined.",
                    stopTimeout, worker.CreatedAt);
            }
            catch
            {
                // invokeTask faulted (e.g., ps.Stop() caused a pipeline exception).
                // Accept it as stopped.
                didStop = true;
            }

            try { ps.Commands.Clear(); ps.Streams.ClearStreams(); } catch { /* best-effort */ }

            if (!didStop)
                throw new TimeoutException(
                    $"Reset pipeline did not stop within StopTimeout ({stopTimeout}); worker will be quarantined.");

            // Pipeline stopped cleanly; re-throw the original cancellation.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reset script threw unexpectedly; worker will be evicted.");
            try { ps.Commands.Clear(); ps.Streams.ClearStreams(); } catch { /* best-effort */ }
            throw;
        }

        ThrowIfBroken(runspace, "after");

        logger.LogDebug("Reset completed for worker created at {CreatedAt}.", worker.CreatedAt);
    }

    /// <summary>
    /// Captures the names of all variables currently in scope, used to build the
    /// worker's initialization snapshot immediately after the startup script runs.
    /// </summary>
    public static IReadOnlySet<string> CaptureVariableSnapshot(PSPowerShell ps)
    {
        ps.Commands.Clear();
        ps.AddCommand("Get-Variable").AddParameter("Scope", "Global");
        var vars = ps.Invoke();
        ps.Commands.Clear();
        ps.Streams.ClearStreams();

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var obj in vars)
        {
            if (obj.BaseObject is System.Management.Automation.PSVariable psVar)
                names.Add(psVar.Name);
            else if (obj.Properties["Name"]?.Value is string name)
                names.Add(name);
        }
        return names;
    }

    /// <summary>
    /// Captures the names of all PSDrives currently in scope, used to build the
    /// worker's initialization snapshot immediately after the startup script runs.
    /// Request-scoped drives (created after this snapshot) will be removed on reset.
    /// </summary>
    public static IReadOnlySet<string> CaptureDriveSnapshot(PSPowerShell ps)
    {
        ps.Commands.Clear();
        ps.AddCommand("Get-PSDrive").AddParameter("ErrorAction", "SilentlyContinue");
        var drives = ps.Invoke();
        ps.Commands.Clear();
        ps.Streams.ClearStreams();

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var obj in drives)
        {
            if (obj.Properties["Name"]?.Value is string name)
                names.Add(name);
        }
        return names;
    }

    private static void ThrowIfBroken(Runspace runspace, string phase)
    {
        if (runspace.RunspaceStateInfo.State == RunspaceState.Broken)
            throw new InvalidOperationException(
                $"Runspace is Broken {phase} reset; worker must be evicted.");
    }

    // Reset script: resets preferences, removes request-scoped PSDrives and variables,
    // resets working location, then clears $Error.
    // $__PoshMcpResetExclude__ (string[]) = variable names to preserve.
    // $__PoshMcpResetExcludeDrives__ (string[]) = drive names to preserve.
    // Options flags: 2=ReadOnly, 4=Constant; (Options -band 6) -eq 0 skips both.
    private static string BuildResetScript() => @"
$ErrorActionPreference  = 'Continue'
$WarningPreference      = 'Continue'
$VerbosePreference      = 'SilentlyContinue'
$DebugPreference        = 'SilentlyContinue'
$ProgressPreference     = 'Continue'
$InformationPreference  = 'SilentlyContinue'
$ConfirmPreference      = 'High'
$WhatIfPreference       = $false
Get-Variable -Scope Global -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -notin $__PoshMcpResetExclude__ -and ($_.Options -band 6) -eq 0 } |
    Remove-Variable -Scope Global -Force -ErrorAction SilentlyContinue
Get-PSDrive -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -notin $__PoshMcpResetExcludeDrives__ } |
    Remove-PSDrive -Force -ErrorAction SilentlyContinue
if ($IsWindows) {
    $driveRoot = [System.IO.Path]::GetPathRoot($PWD.Path)
    if ($driveRoot) { Set-Location -Path $driveRoot -ErrorAction SilentlyContinue }
} else {
    Set-Location -Path '/' -ErrorAction SilentlyContinue
}
$Error.Clear()
";
}
