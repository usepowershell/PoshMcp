namespace PoshMcp.Benchmarks;

/// <summary>
/// The three executor configurations being compared by this harness.
/// See specs/004-out-of-process-execution/runspace-pool-experiment-plan.md §4.
///
/// Wiring from these enum values to concrete <see cref="PoshMcp.Server.PowerShell.OutOfProcess.ICommandExecutor"/>
/// implementations happens in issue #194. For now, scenarios use this as a
/// [Params] axis so BenchmarkDotNet can enumerate the comparison shape.
/// </summary>
public enum HostMode
{
    /// <summary>
    /// Baseline: existing single-subprocess, single-runspace executor
    /// (<c>OutOfProcessCommandExecutor</c> in Single mode). Captured in the
    /// same run as the experimental modes so machine/version drift cannot
    /// pollute the comparison.
    /// </summary>
    Single,

    /// <summary>
    /// Option A: single subprocess, runspace pool of N. Wired in issue #194
    /// once #190 (oop-host extraction) and #191 (Option A prototype) land.
    /// </summary>
    Pool,

    /// <summary>
    /// Option B: pool of N subprocesses, single runspace each. Wired in
    /// issue #194 once #192 (Option B prototype) lands.
    /// </summary>
    ProcessPool,
}
