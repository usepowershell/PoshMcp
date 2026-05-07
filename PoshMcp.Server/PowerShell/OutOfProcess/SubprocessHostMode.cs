namespace PoshMcp.Server.PowerShell.OutOfProcess;

/// <summary>
/// Selects which subprocess host script is launched when
/// <see cref="RuntimeMode"/> is <see cref="RuntimeMode.OutOfProcess"/>.
/// </summary>
/// <remarks>
/// The default at the configuration layer
/// (<see cref="PoshMcp.Server.PowerShell.PowerShellConfiguration.SubprocessHostMode"/>)
/// is <see cref="Pool"/>. Constructor defaults on the executor itself remain
/// <see cref="Single"/> for backward compatibility with code that constructs
/// <see cref="OutOfProcessCommandExecutor"/> directly without specifying a mode.
/// </remarks>
public enum SubprocessHostMode
{
    /// <summary>
    /// Single runspace per subprocess (oop-host.ps1).
    /// All invokes are serialized inside the host. Supported as an opt-in
    /// fallback for bisecting regressions or for callers that want the legacy
    /// serialized behavior.
    /// </summary>
    Single,

    /// <summary>
    /// Runspace pool inside a single subprocess (oop-host-pool.ps1, recommended
    /// default at the configuration layer).
    /// Multiple invokes execute concurrently on pre-warmed runspaces.
    /// </summary>
    Pool,

    /// <summary>
    /// Pool of N independent subprocess hosts (OutOfProcessSubprocessPool).
    /// Each request leases one host; crashes are reconciled per-slot
    /// without disturbing other hosts. Use for trust-boundary or
    /// tail-latency-sensitive workloads.
    /// </summary>
    ProcessPool
}
