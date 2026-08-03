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
    int TotalWorkers);
