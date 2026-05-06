namespace PoshMcp.Server.PowerShell.OutOfProcess;

/// <summary>
/// Selects which subprocess host script is launched when
/// <see cref="RuntimeMode"/> is <see cref="RuntimeMode.OutOfProcess"/>.
/// </summary>
public enum SubprocessHostMode
{
    /// <summary>
    /// Single runspace per subprocess (oop-host.ps1, default).
    /// All invokes are serialized inside the host.
    /// </summary>
    Single,

    /// <summary>
    /// Runspace pool inside a single subprocess (oop-host-pool.ps1).
    /// Multiple invokes execute concurrently on pre-warmed runspaces.
    /// </summary>
    Pool,

    /// <summary>
    /// Pool of N independent subprocess hosts (OutOfProcessSubprocessPool).
    /// Each request leases one host; crashes are reconciled per-slot
    /// without disturbing other hosts.
    /// </summary>
    ProcessPool
}
