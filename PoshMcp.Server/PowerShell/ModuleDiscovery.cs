using System;
using System.Collections.Generic;
using System.Management.Automation;
using Microsoft.Extensions.Logging;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Result of probing a single configured module via
/// <c>Get-Module -ListAvailable -Name &lt;name&gt;</c>. One result is produced
/// per configured module name (FR-263-10: one PowerShell call per module,
/// never per command).
/// </summary>
/// <param name="Name">
/// The configured module name, preserved verbatim from the input. This is
/// the key downstream consumers (doctor report, OOP wire format) match on.
/// </param>
/// <param name="Found">
/// <c>true</c> when <c>Get-Module -ListAvailable</c> returned at least one
/// module record for this name; <c>false</c> when the module is not
/// installed in any path on <c>$env:PSModulePath</c>, or when probing threw.
/// </param>
/// <param name="Version">
/// The version string of the first matching module (highest precedence path
/// in <c>$env:PSModulePath</c>), or <c>null</c> when <see cref="Found"/> is
/// <c>false</c> or the module record had no <c>Version</c> property value.
/// </param>
/// <param name="Path">
/// The <c>ModuleBase</c> directory of the first matching module — i.e., the
/// directory containing the module manifest (<c>.psd1</c>) or root module
/// (<c>.psm1</c>). <c>null</c> when <see cref="Found"/> is <c>false</c> or
/// the module record had no <c>ModuleBase</c> property value.
/// </param>
public sealed record ModuleProbeResult(
    string Name,
    bool Found,
    string? Version,
    string? Path);

/// <summary>
/// In-process helper for probing PowerShell module availability via
/// <c>Get-Module -ListAvailable</c>. Used by the doctor report
/// (<c>moduleImports</c> section) and by the in-process discovery pipeline
/// to surface diagnostics about configured modules without importing them
/// (no side effects).
/// </summary>
/// <remarks>
/// Implements the in-process half of spec 011 (FR-263-10): exactly one
/// <c>Get-Module</c> call per configured module name. Reuses the supplied
/// runspace — never spawns a new <c>pwsh</c> process and never creates a
/// new runspace lifecycle.
/// </remarks>
public static class ModuleDiscovery
{
    /// <summary>
    /// Probes each configured module name once, returning availability,
    /// version, and path information.
    /// </summary>
    /// <param name="runspace">
    /// The runspace to use for probing. Production callers pass the
    /// existing tool-discovery runspace; doctor callers may pass an
    /// isolated runspace. The runspace is used in a thread-safe manner via
    /// <see cref="IPowerShellRunspace.ExecuteThreadSafe(System.Action{PSPowerShell})"/>.
    /// </param>
    /// <param name="moduleNames">
    /// The configured module names from
    /// <see cref="PowerShellConfiguration.Modules"/>. Each non-blank entry
    /// produces exactly one <see cref="ModuleProbeResult"/> in the output,
    /// preserving input order. <c>null</c>, empty, or blank entries are
    /// skipped.
    /// </param>
    /// <param name="logger">
    /// Optional logger for diagnostic output. Probe failures are logged at
    /// warning level and yield a <c>Found=false</c> result; they never
    /// throw out of this method.
    /// </param>
    /// <returns>
    /// One <see cref="ModuleProbeResult"/> per non-blank input module name,
    /// in input order. Empty when <paramref name="moduleNames"/> is empty
    /// or contains only blank entries.
    /// </returns>
    public static IReadOnlyList<ModuleProbeResult> ProbeModules(
        IPowerShellRunspace runspace,
        IReadOnlyList<string>? moduleNames,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(runspace);

        if (moduleNames is null || moduleNames.Count == 0)
        {
            return Array.Empty<ModuleProbeResult>();
        }

        var results = new List<ModuleProbeResult>(moduleNames.Count);

        runspace.ExecuteThreadSafe(ps =>
        {
            foreach (var rawName in moduleNames)
            {
                if (string.IsNullOrWhiteSpace(rawName))
                {
                    continue;
                }

                var name = rawName.Trim();
                results.Add(ProbeOne(ps, name, logger));
            }
        });

        return results;
    }

    private static ModuleProbeResult ProbeOne(PSPowerShell ps, string name, ILogger? logger)
    {
        try
        {
            ps.Commands.Clear();
            ps.AddCommand("Get-Module")
                .AddParameter("Name", name)
                .AddParameter("ListAvailable")
                .AddParameter("ErrorAction", "SilentlyContinue");

            var moduleInfos = ps.Invoke();
            ps.Commands.Clear();

            if (moduleInfos is null || moduleInfos.Count == 0)
            {
                return new ModuleProbeResult(name, Found: false, Version: null, Path: null);
            }

            var first = moduleInfos[0];
            var version = TryReadProperty(first, "Version");
            var path = TryReadProperty(first, "ModuleBase");

            return new ModuleProbeResult(name, Found: true, Version: version, Path: path);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to probe module '{ModuleName}': {Message}", name, ex.Message);
            // Make sure a partial AddCommand state can't leak to the next iteration.
            try { ps.Commands.Clear(); } catch { /* best-effort cleanup */ }
            return new ModuleProbeResult(name, Found: false, Version: null, Path: null);
        }
    }

    private static string? TryReadProperty(PSObject psObject, string propertyName)
    {
        if (psObject is null)
        {
            return null;
        }

        try
        {
            var prop = psObject.Properties[propertyName];
            var value = prop?.Value;
            if (value is null)
            {
                return null;
            }

            var str = value.ToString();
            return string.IsNullOrWhiteSpace(str) ? null : str;
        }
        catch
        {
            return null;
        }
    }
}
