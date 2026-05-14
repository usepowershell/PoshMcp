using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PoshMcp.Tests;

/// <summary>
/// Snapshots running <c>pwsh</c> processes at construction and reports any new
/// <c>pwsh</c> instances still alive at audit time. Used to verify spec 009
/// FR-412 acceptance: a post-test process audit confirms zero orphan
/// <c>pwsh</c> processes attributable to the test runner.
///
/// Diff-based rather than absolute count so a developer with unrelated
/// <c>pwsh</c> sessions on the box does not produce false positives.
/// </summary>
internal sealed class OrphanProcessAuditor
{
    private readonly HashSet<int> _baseline;
    private readonly string _processName;

    public OrphanProcessAuditor(string processName = "pwsh")
    {
        _processName = processName;
        _baseline = SnapshotPids(processName);
    }

    /// <summary>
    /// Pids that match the audited process name, are still alive, and were
    /// NOT present at construction time.
    /// </summary>
    public IReadOnlyCollection<int> NewLivingPids()
    {
        var current = SnapshotPids(_processName);
        current.ExceptWith(_baseline);
        return current;
    }

    /// <summary>
    /// Convenience wrapper around <see cref="NewLivingPids"/>.
    /// </summary>
    public int CountNewLiving() => NewLivingPids().Count;

    private static HashSet<int> SnapshotPids(string processName)
    {
        var result = new HashSet<int>();
        Process[] processes;

        try
        {
            processes = Process.GetProcessesByName(processName);
        }
        catch
        {
            return result;
        }

        foreach (var p in processes)
        {
            try
            {
                if (!p.HasExited)
                {
                    result.Add(p.Id);
                }
            }
            catch
            {
                // Skip races where the process exits between enumeration and inspection.
            }
            finally
            {
                p.Dispose();
            }
        }

        return result;
    }
}
