using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace PoshMcp.Tests;

/// <summary>
/// Tracks child processes spawned by tests and ensures they are terminated
/// when the test host exits or crashes before normal Dispose paths run.
/// </summary>
internal static class TestProcessRegistry
{
    private static readonly ConcurrentDictionary<int, Process> RegisteredProcesses = new();

    static TestProcessRegistry()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => KillAllTrackedProcesses();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => KillAllTrackedProcesses();
    }

    public static void Register(Process process)
    {
        if (process == null)
        {
            return;
        }

        RegisteredProcesses[process.Id] = process;
    }

    public static void Unregister(Process process)
    {
        if (process == null)
        {
            return;
        }

        RegisteredProcesses.TryRemove(process.Id, out _);
    }

    private static void KillAllTrackedProcesses()
    {
        foreach (var entry in RegisteredProcesses)
        {
            try
            {
                var process = entry.Value;
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
            }
            catch
            {
                // Best effort cleanup during process teardown.
            }
        }
    }
}