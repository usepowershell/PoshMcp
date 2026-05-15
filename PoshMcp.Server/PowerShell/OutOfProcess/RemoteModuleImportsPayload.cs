using System.Collections.Generic;

namespace PoshMcp.Server.PowerShell.OutOfProcess;

/// <summary>
/// Spec 011 FR-263-2 / FR-263-3 / FR-263-10: payload returned alongside the
/// <c>commands</c> array on the OOP <c>discover</c> response. Surfaces the
/// per-module probe data and per-pattern match data the OOP host already
/// computed during discovery so the .NET consumer (DoctorService) can build
/// the <c>moduleImports</c> doctor section without re-running
/// <c>Get-Module -ListAvailable</c> in an in-process runspace.
/// </summary>
/// <remarks>
/// Older OOP hosts (predating spec 011) omit this payload entirely. The
/// .NET deserializer treats a missing top-level <c>moduleImports</c>
/// property as <c>null</c>; consumers MUST fall back to the in-process
/// probe path and emit a one-time warning to <c>DoctorReport.Warnings</c>.
/// </remarks>
public sealed class RemoteModuleImportsPayload
{
    /// <summary>
    /// Per-module probe results (one entry per configured module name in the
    /// <c>discover</c> request's <c>modules</c> array, regardless of whether
    /// the module was found). Order mirrors the configured module order.
    /// </summary>
    public List<RemoteModuleProbe> Modules { get; set; } = new();

    /// <summary>
    /// Per-pattern match counts for include and exclude patterns. Order
    /// mirrors the configured pattern order (includes first, then excludes).
    /// </summary>
    public List<RemotePatternMatch> Patterns { get; set; } = new();
}

/// <summary>
/// Spec 011 FR-263-2 / FR-263-10: per-module probe result emitted by the OOP
/// host. Mirrors <c>ModuleProbeResult</c> on the in-process side so the
/// .NET consumer can build <c>ModuleImportsSection.Modules</c> directly
/// from this payload when running in OOP mode.
/// </summary>
public sealed class RemoteModuleProbe
{
    /// <summary>
    /// The configured module name (verbatim, as it appeared in the
    /// configuration's <c>Modules</c> array).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// <c>true</c> when <c>Get-Module -ListAvailable -Name &lt;Name&gt;</c>
    /// returned at least one module; <c>false</c> otherwise (including when
    /// the cmdlet threw).
    /// </summary>
    public bool Found { get; set; }

    /// <summary>
    /// Module version string (from <c>ModuleInfo.Version.ToString()</c>) for
    /// the first matching module returned by <c>Get-Module -ListAvailable</c>.
    /// <c>null</c> when <see cref="Found"/> is <c>false</c>.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Module manifest path (from <c>ModuleInfo.Path</c>) for the first
    /// matching module returned by <c>Get-Module -ListAvailable</c>.
    /// <c>null</c> when <see cref="Found"/> is <c>false</c>.
    /// </summary>
    public string? Path { get; set; }
}

/// <summary>
/// Spec 011 FR-263-3 / FR-263-10: per-pattern match data emitted by the OOP
/// host. Captures how many discovered commands matched each include or
/// exclude pattern so the .NET consumer can build
/// <c>ModuleImportsSection.Patterns</c> without re-walking the discovered
/// command set.
/// </summary>
public sealed class RemotePatternMatch
{
    /// <summary>
    /// The configured pattern (verbatim, as it appeared in
    /// <c>IncludePatterns</c> or <c>ExcludePatterns</c>).
    /// </summary>
    public string Pattern { get; set; } = string.Empty;

    /// <summary>
    /// <c>"include"</c> for entries from <c>IncludePatterns</c>;
    /// <c>"exclude"</c> for entries from <c>ExcludePatterns</c>.
    /// </summary>
    public string Kind { get; set; } = "include";

    /// <summary>
    /// Pattern role per FR-263-3:
    /// <list type="bullet">
    ///   <item><c>"discovery"</c> — pattern was the sole driver (no modules
    ///   or command names configured).</item>
    ///   <item><c>"filter"</c> — pattern narrowed an existing candidate set
    ///   (modules and/or command names also configured).</item>
    ///   <item><c>"exclude"</c> — pattern dropped commands from the
    ///   candidate set.</item>
    /// </list>
    /// </summary>
    public string Role { get; set; } = "filter";

    /// <summary>
    /// Number of discovered commands whose names matched this pattern.
    /// For include patterns, this is the contributing count; for exclude
    /// patterns, this is the dropped count (always 0 when the pattern
    /// dropped everything that matched it; non-zero indicates leakage).
    /// </summary>
    public int MatchedCount { get; set; }
}
