namespace PoshMcp.Server.PowerShell.Pool;

/// <summary>
/// Point-in-time snapshot of pool utilization returned by <see cref="IRunspacePool.GetStats"/>.
/// </summary>
public sealed record RunspacePoolStats(
    int MinPoolSize,
    int MaxPoolSize,
    int WarmWorkers,
    int LeasedWorkers,
    int ResettingWorkers,
    int TotalWorkers,
    int CreatingWorkers = 0,
    bool IsDraining = false,
    bool IsStarted = true);
