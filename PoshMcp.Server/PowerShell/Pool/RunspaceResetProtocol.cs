using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
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
///   <item>Functions/aliases present in the worker initialization snapshots</item>
///   <item>Loaded modules (module unload is deferred to worker eviction)</item>
/// </list>
/// </para>
/// <para>
/// Implementation note: reset uses <see cref="SessionStateProxy"/> / provider intrinsics directly
/// rather than invoking a PowerShell script pipeline. Native reset preserves the isolation
/// contract while keeping per-call cost in the sub-millisecond range on the warm path.
/// Provider enumeration failures are fail-closed (throw → pool evicts) so a worker is never
/// returned warm when isolation cannot be verified. Per-item remove failures keep the prior
/// SilentlyContinue semantics except for request-scoped aliases, which still throw.
/// </para>
/// <para>
/// Throws <see cref="InvalidOperationException"/> if the runspace is <c>Broken</c> before or
/// after reset, or if session-state enumeration required for isolation fails. The caller
/// (<see cref="StatelessRunspacePool"/>) must evict the worker on throw.
/// </para>
/// </remarks>
internal static class RunspaceResetProtocol
{
    // PS automatic variables and preference variables never removed during reset.
    // Preference names are listed because ResetPreferenceVariables rewrites their values;
    // they must not be deleted. Legacy inject-script names retained so residuals cannot self-delete.
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
        "__PoshMcpResetExclude__", "__PoshMcpResetExcludeDrives__", "__PoshMcpResetExcludeFuncs__",
        "__PoshMcpResetExcludeAliases__",
    };

    // Constant | Private — matches prior script filter: ($_.Options -band 6) -eq 0
    private const ScopedItemOptions NonRemovableVariableOptions =
        ScopedItemOptions.Constant | ScopedItemOptions.Private;

    /// <summary>
    /// Resets the given worker's PowerShell state to its worker-initialized baseline.
    /// </summary>
    /// <param name="worker">The worker to reset. Must be in <c>Resetting</c> state.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="stopTimeout">
    /// Unused by the native reset path (no PS pipeline to <c>Stop()</c>). Retained so the
    /// method matches <see cref="StatelessRunspacePool"/>'s injectable reset delegate signature
    /// and existing call sites / test doubles.
    /// </param>
    /// <param name="cancellationToken">
    /// Used to cancel a long-running reset. On cancellation the method throws
    /// <see cref="OperationCanceledException"/>; the caller must evict the worker.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Runspace is <c>Broken</c>, or session-state enumeration required for isolation failed.
    /// </exception>
    /// <exception cref="OperationCanceledException">Reset cancelled via token.</exception>
    public static Task ResetAsync(
        RunspaceWorker worker,
        ILogger logger,
        TimeSpan stopTimeout,
        CancellationToken cancellationToken = default)
    {
        // Signature-compat only: pool/tests pass StopTimeout; native reset has no pipeline Stop.
        _ = stopTimeout;

        ResetCore(worker, logger, cancellationToken);
        return Task.CompletedTask;
    }

    private static void ResetCore(
        RunspaceWorker worker,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var ps = worker.PowerShell;
        var runspace = ps.Runspace;

        ThrowIfBroken(runspace, "before");

        // 1. Clear pipeline immediately (same as script path).
        ps.Commands.Clear();
        ps.Streams.ClearStreams();

        var proxy = runspace.SessionStateProxy;

        // 2. Build exclusion sets: automatic vars + worker-initialized vars/drives/functions/aliases.
        var excludeVars = new HashSet<string>(PsAutomaticVars, StringComparer.OrdinalIgnoreCase);
        if (worker.InitializedVariableNames is { } initVars)
            excludeVars.UnionWith(initVars);

        var excludeDrives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (worker.InitializedDriveNames is { } initDrives)
            excludeDrives.UnionWith(initDrives);

        var excludeFuncs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (worker.InitializedFunctionNames is { } initFuncs)
            excludeFuncs.UnionWith(initFuncs);

        var excludeAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (worker.InitializedAliasNames is { } initAliases)
            excludeAliases.UnionWith(initAliases);

        try
        {
            // 3. Preference variables → PS defaults.
            ResetPreferenceVariables(proxy);
            cancellationToken.ThrowIfCancellationRequested();

            // 4. Remove request-scoped variables (always have automatic-var baseline).
            RemoveRequestScopedVariables(proxy, excludeVars, cancellationToken);

            // 5–7. Drive/function/alias cleanup requires an initialization snapshot. Without one we
            // cannot distinguish built-in/startup items (e.g. constant aliases like '%') from
            // request-scoped pollution, so skip rather than thrash built-ins. Production workers
            // always capture snapshots during CreateWorkerAsync.
            if (worker.InitializedDriveNames is not null)
            {
                RemoveRequestScopedDrives(proxy, excludeDrives, cancellationToken);
            }

            if (worker.InitializedFunctionNames is not null)
            {
                RemoveRequestScopedProviderItems(
                    proxy,
                    providerPath: "Function:",
                    exclude: excludeFuncs,
                    force: true,
                    throwOnFailure: false,
                    cancellationToken);
            }

            if (worker.InitializedAliasNames is not null)
            {
                // Unremovable request aliases (e.g. Constant) throw so the pool evicts rather than
                // returning a contaminated worker.
                RemoveRequestScopedProviderItems(
                    proxy,
                    providerPath: "Alias:",
                    exclude: excludeAliases,
                    force: true,
                    throwOnFailure: true,
                    cancellationToken);
            }

            // 8. Reset working location to filesystem root.
            ResetWorkingLocation(proxy);

            // 9. Clear $Error.
            ClearErrorVariable(proxy);
        }
        catch (OperationCanceledException)
        {
            try { ps.Commands.Clear(); ps.Streams.ClearStreams(); } catch { /* best-effort */ }
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reset threw unexpectedly; worker will be evicted.");
            try { ps.Commands.Clear(); ps.Streams.ClearStreams(); } catch { /* best-effort */ }
            throw;
        }

        ThrowIfBroken(runspace, "after");

        logger.LogDebug("Reset completed for worker created at {CreatedAt}.", worker.CreatedAt);
    }

    private static void ResetPreferenceVariables(SessionStateProxy proxy)
    {
        proxy.SetVariable("ErrorActionPreference", ActionPreference.Continue);
        proxy.SetVariable("WarningPreference", ActionPreference.Continue);
        proxy.SetVariable("VerbosePreference", ActionPreference.SilentlyContinue);
        proxy.SetVariable("DebugPreference", ActionPreference.SilentlyContinue);
        proxy.SetVariable("ProgressPreference", ActionPreference.Continue);
        proxy.SetVariable("InformationPreference", ActionPreference.SilentlyContinue);
        proxy.SetVariable("ConfirmPreference", ConfirmImpact.High);
        proxy.SetVariable("WhatIfPreference", false);
    }

    private static void RemoveRequestScopedVariables(
        SessionStateProxy proxy,
        IReadOnlySet<string> exclude,
        CancellationToken cancellationToken)
    {
        // Enumerate Variable: provider once; remove by name via PSVariableIntrinsics.
        // Enumeration failure is fail-closed: cannot prove isolation → throw → pool evicts.
        System.Collections.ObjectModel.Collection<PSObject> items;
        try
        {
            items = proxy.InvokeProvider.ChildItem.Get("Variable:", recurse: false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to enumerate Variable: provider during reset; worker must be evicted.", ex);
        }

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryGetProviderItemName(item, out var name) || exclude.Contains(name))
                continue;

            PSVariable? psVar = null;
            try
            {
                psVar = proxy.PSVariable.Get(name);
            }
            catch
            {
                continue;
            }

            if (psVar is null)
                continue;

            // Match prior script: skip Constant | Private; Remove-Variable -Force clears ReadOnly.
            if ((psVar.Options & NonRemovableVariableOptions) != ScopedItemOptions.None)
                continue;

            try
            {
                // Temporarily clear ReadOnly so Remove succeeds (equivalent to -Force).
                if ((psVar.Options & ScopedItemOptions.ReadOnly) != 0)
                {
                    psVar.Options &= ~ScopedItemOptions.ReadOnly;
                    proxy.PSVariable.Set(psVar);
                }

                proxy.PSVariable.Remove(name);
            }
            catch
            {
                // SilentlyContinue parity with prior script path for per-item remove.
            }
        }
    }

    private static void RemoveRequestScopedDrives(
        SessionStateProxy proxy,
        IReadOnlySet<string> exclude,
        CancellationToken cancellationToken)
    {
        System.Collections.ObjectModel.Collection<PSDriveInfo> drives;
        try
        {
            drives = proxy.Drive.GetAll();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to enumerate PSDrives during reset; worker must be evicted.", ex);
        }

        // Snapshot names first — Remove mutates the drive collection.
        var toRemove = new List<string>();
        foreach (var drive in drives)
        {
            if (drive is null || string.IsNullOrEmpty(drive.Name))
                continue;
            if (!exclude.Contains(drive.Name))
                toRemove.Add(drive.Name);
        }

        foreach (var name in toRemove)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // force=true; null scope = current/global session scope for local runspaces
                proxy.Drive.Remove(name, force: true, scope: null!);
            }
            catch
            {
                // SilentlyContinue parity for per-drive remove.
            }
        }
    }

    private static void RemoveRequestScopedProviderItems(
        SessionStateProxy proxy,
        string providerPath,
        IReadOnlySet<string> exclude,
        bool force,
        bool throwOnFailure,
        CancellationToken cancellationToken)
    {
        System.Collections.ObjectModel.Collection<PSObject> items;
        try
        {
            items = proxy.InvokeProvider.ChildItem.Get(providerPath, recurse: false);
        }
        catch (Exception ex)
        {
            // Enumeration failure is always fail-closed (cannot verify isolation).
            throw new InvalidOperationException(
                $"Failed to enumerate {providerPath} provider during reset; worker must be evicted.", ex);
        }

        var toRemove = new List<string>();
        foreach (var item in items)
        {
            if (!TryGetProviderItemName(item, out var name) || exclude.Contains(name))
                continue;
            toRemove.Add(name);
        }

        foreach (var name in toRemove)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = providerPath.EndsWith(':')
                ? providerPath + name
                : providerPath + "\\" + name;

            try
            {
                proxy.InvokeProvider.Item.Remove(path, force);
            }
            catch (Exception) when (!throwOnFailure)
            {
                // SilentlyContinue parity for functions.
            }
        }
    }

    private static void ResetWorkingLocation(SessionStateProxy proxy)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var current = proxy.Path.CurrentFileSystemLocation?.Path;
                if (!string.IsNullOrEmpty(current))
                {
                    var root = Path.GetPathRoot(current);
                    if (!string.IsNullOrEmpty(root))
                        proxy.Path.SetLocation(root);
                }
            }
            else
            {
                proxy.Path.SetLocation("/");
            }
        }
        catch
        {
            // SilentlyContinue parity.
        }
    }

    private static void ClearErrorVariable(SessionStateProxy proxy)
    {
        try
        {
            var error = proxy.GetVariable("Error");
            if (error is IList list)
            {
                list.Clear();
            }
            else if (error is IEnumerable and not string)
            {
                // Some hosts surface $Error as a non-IList enumerable; fall back to reassignment.
                proxy.SetVariable("Error", new ArrayList());
            }
        }
        catch
        {
            // best-effort
        }
    }

    private static bool TryGetProviderItemName(PSObject item, out string name)
    {
        name = string.Empty;
        if (item is null)
            return false;

        switch (item.BaseObject)
        {
            case PSVariable psVar:
                name = psVar.Name;
                return !string.IsNullOrEmpty(name);
            case FunctionInfo fi:
                name = fi.Name;
                return !string.IsNullOrEmpty(name);
            case AliasInfo ai:
                name = ai.Name;
                return !string.IsNullOrEmpty(name);
            case PSDriveInfo di:
                name = di.Name;
                return !string.IsNullOrEmpty(name);
            case string s when !string.IsNullOrEmpty(s):
                name = s;
                return true;
        }

        if (item.Properties["Name"]?.Value is string propName && !string.IsNullOrEmpty(propName))
        {
            name = propName;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Captures the names of all variables currently in scope, used to build the
    /// worker's initialization snapshot immediately after the startup script runs.
    /// </summary>
    public static IReadOnlySet<string> CaptureVariableSnapshot(PSPowerShell ps)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var items = ps.Runspace.SessionStateProxy.InvokeProvider.ChildItem.Get("Variable:", recurse: false);
            foreach (var obj in items)
            {
                if (TryGetProviderItemName(obj, out var name))
                    names.Add(name);
            }

            if (names.Count > 0)
                return names;
        }
        catch
        {
            // Fall back to Get-Variable pipeline below.
        }

        ps.Commands.Clear();
        ps.AddCommand("Get-Variable").AddParameter("Scope", "Global");
        var vars = ps.Invoke();
        ps.Commands.Clear();
        ps.Streams.ClearStreams();

        foreach (var obj in vars)
        {
            if (obj.BaseObject is PSVariable psVar)
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
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var drive in ps.Runspace.SessionStateProxy.Drive.GetAll())
            {
                if (!string.IsNullOrEmpty(drive?.Name))
                    names.Add(drive.Name);
            }

            if (names.Count > 0)
                return names;
        }
        catch
        {
            // Fall back to Get-PSDrive pipeline below.
        }

        ps.Commands.Clear();
        ps.AddCommand("Get-PSDrive").AddParameter("ErrorAction", "SilentlyContinue");
        var drives = ps.Invoke();
        ps.Commands.Clear();
        ps.Streams.ClearStreams();

        foreach (var obj in drives)
        {
            if (obj.Properties["Name"]?.Value is string name)
                names.Add(name);
        }
        return names;
    }

    /// <summary>
    /// Captures the names of all functions currently in scope, used to build the
    /// worker's initialization snapshot immediately after the startup script runs.
    /// Request-scoped functions (defined after this snapshot) will be removed on reset.
    /// </summary>
    public static IReadOnlySet<string> CaptureFunctionSnapshot(PSPowerShell ps)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var items = ps.Runspace.SessionStateProxy.InvokeProvider.ChildItem.Get("Function:", recurse: false);
            foreach (var obj in items)
            {
                if (TryGetProviderItemName(obj, out var name))
                    names.Add(name);
            }

            if (names.Count > 0)
                return names;
        }
        catch
        {
            // Fall back below.
        }

        ps.Commands.Clear();
        ps.AddScript("Get-ChildItem Function:\\ -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name");
        var funcs = ps.Invoke<string>();
        ps.Commands.Clear();
        ps.Streams.ClearStreams();

        foreach (var name in funcs)
            if (!string.IsNullOrEmpty(name))
                names.Add(name);
        return names;
    }

    /// <summary>
    /// Captures the names of all aliases currently in scope, used to build the
    /// worker's initialization snapshot immediately after the startup script runs.
    /// Request-scoped aliases (created after this snapshot) will be removed on reset.
    /// A request alias that cannot be removed (e.g., Constant option) causes reset failure
    /// and worker eviction.
    /// </summary>
    public static IReadOnlySet<string> CaptureAliasSnapshot(PSPowerShell ps)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var items = ps.Runspace.SessionStateProxy.InvokeProvider.ChildItem.Get("Alias:", recurse: false);
            foreach (var obj in items)
            {
                if (TryGetProviderItemName(obj, out var name))
                    names.Add(name);
            }

            if (names.Count > 0)
                return names;
        }
        catch
        {
            // Fall back below.
        }

        ps.Commands.Clear();
        ps.AddCommand("Get-Alias").AddParameter("ErrorAction", "SilentlyContinue");
        var aliases = ps.Invoke();
        ps.Commands.Clear();
        ps.Streams.ClearStreams();

        foreach (var obj in aliases)
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
}
