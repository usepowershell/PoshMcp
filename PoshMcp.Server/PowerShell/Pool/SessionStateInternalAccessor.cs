using System;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Management.Automation.Runspaces;
using System.Reflection;

namespace PoshMcp.Server.PowerShell.Pool;

/// <summary>
/// Cached reflection access to PowerShell's internal session-state tables
/// (<c>SessionStateInternal.GetVariableTable/GetFunctionTable/GetAliasTable</c>).
/// Used by <see cref="RunspaceResetProtocol"/> to avoid the expensive provider
/// <c>ChildItem.Get</c> path on the per-call reset hot path.
/// </summary>
/// <remarks>
/// Falls back cleanly when the internal shape is unavailable (non-Windows hosts,
/// future PS SDK changes). Callers must keep a provider-based fallback.
/// </remarks>
internal static class SessionStateInternalAccessor
{
    private static readonly object InitLock = new();
    private static bool _initialized;
    private static bool _available;
    private static Func<Runspace, object?>? _getEngineSessionState;
    private static Func<object, IDictionary?>? _getVariableTable;
    private static Func<object, IDictionary?>? _getFunctionTable;
    private static Func<object, IDictionary?>? _getAliasTable;

    /// <summary>
    /// Attempts to read the variable/function/alias tables for <paramref name="runspace"/>.
    /// Returns <c>false</c> when reflection is unavailable or any table cannot be read.
    /// </summary>
    public static bool TryGetTables(
        Runspace runspace,
        [NotNullWhen(true)] out IDictionary? variables,
        [NotNullWhen(true)] out IDictionary? functions,
        [NotNullWhen(true)] out IDictionary? aliases)
    {
        variables = null;
        functions = null;
        aliases = null;

        EnsureInitialized();
        if (!_available || runspace is null)
            return false;

        try
        {
            var engineSs = _getEngineSessionState!(runspace);
            if (engineSs is null)
                return false;

            variables = _getVariableTable!(engineSs);
            functions = _getFunctionTable!(engineSs);
            aliases = _getAliasTable!(engineSs);
            return variables is not null && functions is not null && aliases is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>True when internal table accessors bound successfully.</summary>
    internal static bool IsAvailable
    {
        get
        {
            EnsureInitialized();
            return _available;
        }
    }

    private static void EnsureInitialized()
    {
        if (_initialized)
            return;

        lock (InitLock)
        {
            if (_initialized)
                return;

            try
            {
                const BindingFlags flags =
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                // Runspace.ExecutionContext (internal) → EngineSessionState
                var rsType = typeof(Runspace);
                var ecProp = rsType.GetProperty("ExecutionContext", flags);
                if (ecProp is null)
                {
                    _available = false;
                    _initialized = true;
                    return;
                }

                var ecType = ecProp.PropertyType;
                var engSsProp = ecType.GetProperty("EngineSessionState", flags);
                if (engSsProp is null)
                {
                    _available = false;
                    _initialized = true;
                    return;
                }

                var engSsType = engSsProp.PropertyType;
                var getVar = engSsType.GetMethod("GetVariableTable", flags, binder: null, types: Type.EmptyTypes, modifiers: null);
                var getFunc = engSsType.GetMethod("GetFunctionTable", flags, binder: null, types: Type.EmptyTypes, modifiers: null);
                var getAlias = engSsType.GetMethod("GetAliasTable", flags, binder: null, types: Type.EmptyTypes, modifiers: null);
                if (getVar is null || getFunc is null || getAlias is null)
                {
                    _available = false;
                    _initialized = true;
                    return;
                }

                _getEngineSessionState = rs =>
                {
                    var ec = ecProp.GetValue(rs);
                    return ec is null ? null : engSsProp.GetValue(ec);
                };

                _getVariableTable = ss => getVar.Invoke(ss, null) as IDictionary;
                _getFunctionTable = ss => getFunc.Invoke(ss, null) as IDictionary;
                _getAliasTable = ss => getAlias.Invoke(ss, null) as IDictionary;
                _available = true;
            }
            catch
            {
                _available = false;
            }

            _initialized = true;
        }
    }
}
