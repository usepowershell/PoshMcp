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
/// request-scoped state while preserving variables established by the startup script.
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
/// </list>
/// </para>
/// <para>
/// State preserved:
/// <list type="bullet">
///   <item>PS automatic variables (<c>$PSVersionTable</c>, <c>$IsWindows</c>, etc.)</item>
///   <item>Variables present in <see cref="RunspaceWorker.InitializedVariableNames"/>
///         (captured after the startup script ran)</item>
///   <item>Loaded modules and functions (module cleanup is deferred to worker eviction)</item>
/// </list>
/// </para>
/// <para>
/// Throws <see cref="InvalidOperationException"/> if the runspace is <c>Broken</c> before or
/// after reset. The caller (<see cref="StatelessRunspacePool"/>) must evict the worker on throw.
/// </para>
/// </remarks>
internal static class RunspaceResetProtocol
{
    // PS automatic variables and preference variables never removed during reset.
    // Preference variables are included here because we reset them explicitly above.
    // __PoshMcpResetExclude__ is the internal inject-variable name used by ResetAsync.
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
        "__PoshMcpResetExclude__",
    };

    /// <summary>
    /// Resets the given worker's PowerShell state to its worker-initialized baseline.
    /// </summary>
    /// <param name="worker">The worker to reset. Must be in <c>Resetting</c> state.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="cancellationToken">
    /// Used to cancel a long-running reset script. Cancellation leaves the worker in an
    /// uncertain state; callers must evict it.
    /// </param>
    /// <exception cref="InvalidOperationException">Runspace is <c>Broken</c>.</exception>
    public static async Task ResetAsync(
        RunspaceWorker worker,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var ps = worker.PowerShell;
        var runspace = ps.Runspace;

        ThrowIfBroken(runspace, "before");

        // 1. Clear pipeline immediately.
        ps.Commands.Clear();
        ps.Streams.ClearStreams();

        // 2. Build exclusion set: automatic vars + worker-initialized vars.
        var exclude = new HashSet<string>(PsAutomaticVars, StringComparer.OrdinalIgnoreCase);
        if (worker.InitializedVariableNames is { } initVars)
            exclude.UnionWith(initVars);

        // 3. Run reset script.
        //    Inject the exclusion list directly into the runspace via SessionStateProxy
        //    instead of using param()+AddArgument, which can silently fail to bind
        //    a string[] positional argument in some PS SDK host configurations.
        var script = BuildResetScript();
        try
        {
            ps.Runspace.SessionStateProxy.SetVariable("__PoshMcpResetExclude__", exclude.ToArray());
            ps.AddScript(script);
            await Task.Run(() =>
            {
                ps.Invoke();
                ps.Commands.Clear();
                ps.Streams.ClearStreams();
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Leave pipeline clear for the caller to evict
            try { ps.Commands.Clear(); ps.Streams.ClearStreams(); } catch { /* best-effort */ }
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

    private static void ThrowIfBroken(Runspace runspace, string phase)
    {
        if (runspace.RunspaceStateInfo.State == RunspaceState.Broken)
            throw new InvalidOperationException(
                $"Runspace is Broken {phase} reset; worker must be evicted.");
    }

    // Reset script: resets preferences, removes request-scoped variables,
    // resets working location, then clears $Error.
    // $__PoshMcpResetExclude__ is injected into the runspace via SessionStateProxy
    // before this script runs.
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
if ($IsWindows) {
    $driveRoot = [System.IO.Path]::GetPathRoot($PWD.Path)
    if ($driveRoot) { Set-Location -Path $driveRoot -ErrorAction SilentlyContinue }
} else {
    Set-Location -Path '/' -ErrorAction SilentlyContinue
}
$Error.Clear()
";
}
