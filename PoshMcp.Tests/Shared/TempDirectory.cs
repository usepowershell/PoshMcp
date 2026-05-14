using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PoshMcp.Tests.Shared;

/// <summary>
/// Per-test scratch directory under <see cref="Path.GetTempPath"/>. Creates a
/// unique GUID-suffixed folder on construction and best-effort recursively
/// deletes it on <see cref="Dispose"/>.
/// </summary>
/// <remarks>
/// <para>
/// Canonical hygiene helper for spec 009 (FR-403, FR-410). Every test that
/// needs a writable temp folder should prefer this over hand-rolled
/// <c>Path.Combine(Path.GetTempPath(), ...)</c> snippets so cleanup is
/// guaranteed by <c>using</c>-scope rather than ad-hoc <c>try/finally</c>.
/// </para>
/// <para>
/// Failures during <see cref="Dispose"/> are swallowed (best-effort) and
/// recorded in a process-wide audit list reachable via
/// <see cref="GetUndeletedDirectories"/> for diagnostic post-test sweeps.
/// </para>
/// <para>
/// All directories created via this helper share the prefix
/// <see cref="Prefix"/> (default <c>poshmcp-test-</c>) so a post-suite sweep
/// of <c>Path.GetTempPath()</c> can identify and clean any stragglers.
/// </para>
/// </remarks>
internal sealed class TempDirectory : IDisposable
{
    /// <summary>Directory name prefix shared by all instances (audit anchor).</summary>
    public const string Prefix = "poshmcp-test-";

    private static readonly ConcurrentBag<string> s_undeleted = new();
    private bool _disposed;

    /// <summary>Creates a unique directory under the system temp path.</summary>
    /// <param name="label">
    /// Optional short label inserted between the prefix and the GUID to make the
    /// directory name self-describing in audit output (e.g., <c>"oop-pool"</c>).
    /// Whitespace-only or null labels are ignored.
    /// </param>
    public TempDirectory(string? label = null)
    {
        var name = string.IsNullOrWhiteSpace(label)
            ? $"{Prefix}{Guid.NewGuid():N}"
            : $"{Prefix}{label}-{Guid.NewGuid():N}";

        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), name);
        Directory.CreateDirectory(Path);
    }

    /// <summary>Absolute path of the created directory.</summary>
    public string Path { get; }

    /// <summary>Combines a relative segment with <see cref="Path"/>.</summary>
    public string Combine(params string[] paths)
    {
        var all = new string[paths.Length + 1];
        all[0] = Path;
        Array.Copy(paths, 0, all, 1, paths.Length);
        return System.IO.Path.Combine(all);
    }

    /// <summary>Best-effort recursive delete. Never throws.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch
        {
            // Best-effort: a leaked file handle (open NDJSON log, lingering pwsh
            // child) shouldn't fail the test that produced the directory. Record
            // for the post-suite audit instead.
            s_undeleted.Add(Path);
        }
    }

    /// <summary>
    /// Returns directories created via <see cref="TempDirectory"/> in this process
    /// whose <see cref="Dispose"/> failed. Diagnostic-only.
    /// </summary>
    public static IReadOnlyCollection<string> GetUndeletedDirectories()
        => s_undeleted.ToArray();

    /// <summary>
    /// Sweeps <see cref="Path.GetTempPath"/> for directories matching
    /// <see cref="Prefix"/>. Diagnostic-only — returns leftovers from any
    /// process (this run or stragglers from prior crashes).
    /// </summary>
    public static IReadOnlyCollection<string> AuditLeftoverDirectories()
    {
        try
        {
            return Directory
                .EnumerateDirectories(System.IO.Path.GetTempPath(), $"{Prefix}*", SearchOption.TopDirectoryOnly)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
