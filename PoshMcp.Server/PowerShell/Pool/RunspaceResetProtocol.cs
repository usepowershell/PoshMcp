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
/// Implementation note: reset prefers PowerShell internal session-state tables
/// (<c>GetVariableTable</c>/<c>GetFunctionTable</c>/<c>GetAliasTable</c> via
/// <see cref="SessionStateInternalAccessor"/>) over provider <c>ChildItem.Get</c> enumeration.
/// Tables are an order of magnitude cheaper on the clean warm path (no allocations of PSObject
/// wrappers per alias/function) while preserving the same name-based isolation contract.
/// When internal tables are unavailable, the prior provider path is used as fallback.
/// Enumeration failures are fail-closed (throw → pool evicts) so a worker is never returned warm
/// when isolation cannot be verified. Per-item remove failures keep the prior SilentlyContinue
/// semantics except for request-scoped aliases, which still throw.
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

        // 2. Exclusion sets: automatic vars + worker-initialized snapshots (cached on worker).
        EnsureExcludeSets(worker);
        var excludeVars = worker.ResetExcludeVariables!;
        var excludeDrives = worker.ResetExcludeDrives;
        var excludeFuncs = worker.ResetExcludeFunctions;
        var excludeAliases = worker.ResetExcludeAliases;

        try
        {
            // 3–9. Acquire internal session-state tables first so preference-reset and
            // $Error-clear can use the fast direct-assignment path instead of going
            // through SessionStateProxy on every warm call.
            bool hasTables = SessionStateInternalAccessor.TryGetTables(
                runspace, out var varTable, out var funcTable, out var aliasTable);

            // 3. Preference variables → PS defaults (fast path via table, proxy fallback).
            ResetPreferenceVariables(proxy, hasTables ? varTable : null);
            cancellationToken.ThrowIfCancellationRequested();

            // 4–7. Variable/drive/function/alias cleanup.
            if (hasTables)
            {
                RemoveRequestScopedVariablesFromTable(proxy, varTable!, excludeVars, cancellationToken);

                if (worker.InitializedDriveNames is not null && excludeDrives is not null)
                    RemoveRequestScopedDrives(proxy, excludeDrives, cancellationToken);

                if (worker.InitializedFunctionNames is not null && excludeFuncs is not null)
                {
                    RemoveRequestScopedNamesFromTable(
                        proxy,
                        funcTable!,
                        providerPath: "Function:",
                        exclude: excludeFuncs,
                        force: true,
                        throwOnFailure: false,
                        cancellationToken);
                }

                if (worker.InitializedAliasNames is not null && excludeAliases is not null)
                {
                    // Unremovable request aliases (e.g. Constant) throw so the pool evicts.
                    RemoveRequestScopedNamesFromTable(
                        proxy,
                        aliasTable!,
                        providerPath: "Alias:",
                        exclude: excludeAliases,
                        force: true,
                        throwOnFailure: true,
                        cancellationToken);
                }
            }
            else
            {
                // Provider fallback — same isolation contract, higher per-call cost.
                RemoveRequestScopedVariables(proxy, excludeVars, cancellationToken);

                if (worker.InitializedDriveNames is not null && excludeDrives is not null)
                    RemoveRequestScopedDrives(proxy, excludeDrives, cancellationToken);

                if (worker.InitializedFunctionNames is not null && excludeFuncs is not null)
                {
                    RemoveRequestScopedProviderItems(
                        proxy,
                        providerPath: "Function:",
                        exclude: excludeFuncs,
                        force: true,
                        throwOnFailure: false,
                        cancellationToken);
                }

                if (worker.InitializedAliasNames is not null && excludeAliases is not null)
                {
                    RemoveRequestScopedProviderItems(
                        proxy,
                        providerPath: "Alias:",
                        exclude: excludeAliases,
                        force: true,
                        throwOnFailure: true,
                        cancellationToken);
                }
            }

            // 8. Reset working location — skip SetLocation when already at root.
            ResetWorkingLocation(proxy);

            // 9. Clear $Error (fast path via table, proxy fallback).
            ClearErrorVariable(proxy, hasTables ? varTable : null);
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

    /// <summary>
    /// Builds and caches exclusion sets on the worker (once per snapshot generation).
    /// </summary>
    private static void EnsureExcludeSets(RunspaceWorker worker)
    {
        if (worker.ResetExcludeVariables is null)
        {
            var excludeVars = new HashSet<string>(PsAutomaticVars, StringComparer.OrdinalIgnoreCase);
            if (worker.InitializedVariableNames is { } initVars)
                excludeVars.UnionWith(initVars);
            worker.ResetExcludeVariables = excludeVars;
        }

        if (worker.ResetExcludeDrives is null && worker.InitializedDriveNames is { } initDrives)
        {
            worker.ResetExcludeDrives = initDrives is HashSet<string> hs
                ? hs
                : new HashSet<string>(initDrives, StringComparer.OrdinalIgnoreCase);
        }

        if (worker.ResetExcludeFunctions is null && worker.InitializedFunctionNames is { } initFuncs)
        {
            worker.ResetExcludeFunctions = initFuncs is HashSet<string> hs
                ? hs
                : new HashSet<string>(initFuncs, StringComparer.OrdinalIgnoreCase);
        }

        if (worker.ResetExcludeAliases is null && worker.InitializedAliasNames is { } initAliases)
        {
            worker.ResetExcludeAliases = initAliases is HashSet<string> hs
                ? hs
                : new HashSet<string>(initAliases, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Resets PS preference variables to their default values.
    /// When <paramref name="varTable"/> is supplied (from <see cref="SessionStateInternalAccessor"/>),
    /// values are written directly to the <see cref="PSVariable"/> objects in the table,
    /// bypassing the per-call lock-and-proxy overhead of <see cref="SessionStateProxy.SetVariable"/>.
    /// Falls back to the proxy path when the table is unavailable.
    /// </summary>
    private static void ResetPreferenceVariables(SessionStateProxy proxy, IDictionary? varTable)
    {
        if (varTable != null)
        {
            SetPsVarInTable(varTable, "ErrorActionPreference", ActionPreference.Continue);
            SetPsVarInTable(varTable, "WarningPreference", ActionPreference.Continue);
            SetPsVarInTable(varTable, "VerbosePreference", ActionPreference.SilentlyContinue);
            SetPsVarInTable(varTable, "DebugPreference", ActionPreference.SilentlyContinue);
            SetPsVarInTable(varTable, "ProgressPreference", ActionPreference.Continue);
            SetPsVarInTable(varTable, "InformationPreference", ActionPreference.SilentlyContinue);
            SetPsVarInTable(varTable, "ConfirmPreference", ConfirmImpact.High);
            SetPsVarInTable(varTable, "WhatIfPreference", false);
            return;
        }

        // Proxy fallback — used when internal tables are unavailable.
        proxy.SetVariable("ErrorActionPreference", ActionPreference.Continue);
        proxy.SetVariable("WarningPreference", ActionPreference.Continue);
        proxy.SetVariable("VerbosePreference", ActionPreference.SilentlyContinue);
        proxy.SetVariable("DebugPreference", ActionPreference.SilentlyContinue);
        proxy.SetVariable("ProgressPreference", ActionPreference.Continue);
        proxy.SetVariable("InformationPreference", ActionPreference.SilentlyContinue);
        proxy.SetVariable("ConfirmPreference", ConfirmImpact.High);
        proxy.SetVariable("WhatIfPreference", false);
    }

    /// <summary>
    /// Sets a <see cref="PSVariable"/> value directly in the internal session-state table.
    /// Best-effort: silently skips when the variable is not found or the assignment throws.
    /// The proxy fallback in <see cref="ResetPreferenceVariables"/> covers any miss.
    /// </summary>
    private static void SetPsVarInTable(IDictionary table, string name, object value)
    {
        try
        {
            if (table[name] is PSVariable v)
                v.Value = value;
        }
        catch
        {
            // best-effort; caller has proxy fallback for the entire batch
        }
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

            TryRemoveVariable(proxy, name, psVar);
        }
    }

    /// <summary>
    /// Table-based variable cleanup (preferred hot path). Same isolation rules as the provider path.
    /// </summary>
    private static void RemoveRequestScopedVariablesFromTable(
        SessionStateProxy proxy,
        IDictionary table,
        IReadOnlySet<string> exclude,
        CancellationToken cancellationToken)
    {
        // Snapshot names first — table may be live; removes must not mutate during enumeration.
        List<string>? toRemove = null;
        List<PSVariable>? toRemoveVars = null;

        try
        {
            foreach (DictionaryEntry entry in table)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (entry.Key is not string name || exclude.Contains(name))
                    continue;

                PSVariable? psVar = entry.Value as PSVariable;
                if (psVar is null)
                {
                    try { psVar = proxy.PSVariable.Get(name); }
                    catch { continue; }
                }

                if (psVar is null)
                    continue;

                if ((psVar.Options & NonRemovableVariableOptions) != ScopedItemOptions.None)
                    continue;

                toRemove ??= new List<string>();
                toRemoveVars ??= new List<PSVariable>();
                toRemove.Add(name);
                toRemoveVars.Add(psVar);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to enumerate variable table during reset; worker must be evicted.", ex);
        }

        if (toRemove is null)
            return;

        for (var i = 0; i < toRemove.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryRemoveVariable(proxy, toRemove[i], toRemoveVars![i]);
        }
    }

    private static void TryRemoveVariable(SessionStateProxy proxy, string name, PSVariable psVar)
    {
        // Match prior script: skip Constant | Private; Remove-Variable -Force clears ReadOnly.
        if ((psVar.Options & NonRemovableVariableOptions) != ScopedItemOptions.None)
            return;

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

    /// <summary>
    /// Removes request-scoped function/alias names discovered via internal session-state tables.
    /// </summary>
    private static void RemoveRequestScopedNamesFromTable(
        SessionStateProxy proxy,
        IDictionary table,
        string providerPath,
        IReadOnlySet<string> exclude,
        bool force,
        bool throwOnFailure,
        CancellationToken cancellationToken)
    {
        List<string>? toRemove = null;

        try
        {
            foreach (DictionaryEntry entry in table)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string? name = entry.Key as string;
                if (string.IsNullOrEmpty(name) || exclude.Contains(name))
                    continue;

                toRemove ??= new List<string>();
                toRemove.Add(name);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to enumerate {providerPath} table during reset; worker must be evicted.", ex);
        }

        if (toRemove is null)
            return;

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
                    // Only call SetLocation when the runspace is not already at the drive
                    // root. On the common warm path (no Set-Location was called), this
                    // avoids a redundant round-trip through the filesystem provider.
                    if (!string.IsNullOrEmpty(root) &&
                        !string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
                    {
                        proxy.Path.SetLocation(root);
                    }
                }
            }
            else
            {
                var current = proxy.Path.CurrentFileSystemLocation?.Path;
                if (current != "/")
                    proxy.Path.SetLocation("/");
            }
        }
        catch
        {
            // SilentlyContinue parity.
        }
    }

    private static void ClearErrorVariable(SessionStateProxy proxy, IDictionary? varTable)
    {
        try
        {
            IList? list = null;
            if (varTable?["Error"] is PSVariable errorVar)
            {
                list = errorVar.Value as IList;
            }
            else
            {
                var error = proxy.GetVariable("Error");
                if (error is IList directList)
                    list = directList;
                else if (error is IEnumerable and not string)
                {
                    proxy.SetVariable("Error", new ArrayList());
                    return;
                }
            }
            list?.Clear();
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
