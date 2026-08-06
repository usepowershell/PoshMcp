using System;
using System.Collections.Generic;

namespace PoshMcp.Server.PowerShell.Pool;

/// <summary>
/// Configuration for the HTTP warm-worker runspace pool.
/// Bind from the <c>McpServer:RunspacePool</c> configuration section.
/// </summary>
/// <remarks>
/// Approved defaults sourced from the MCP v2 migration plan (Section 6.1).
/// Do not change defaults without a new decision entry in <c>.squad/decisions.md</c>.
/// </remarks>
public sealed class RunspacePoolOptions
{
    /// <summary>
    /// Minimum number of warm workers kept alive at all times.
    /// The pool never evicts below this count, even if all workers are idle.
    /// Default: 2.
    /// </summary>
    public int MinPoolSize { get; set; } = 2;

    /// <summary>
    /// Absolute maximum number of concurrent workers (warm + leased).
    /// Default: 16.
    /// </summary>
    public int MaxPoolSize { get; set; } = 16;

    /// <summary>
    /// Number of workers pre-created synchronously at startup before the pool accepts requests.
    /// Must not exceed <see cref="MaxPoolSize"/>.
    /// Default: 2 (matches <see cref="MinPoolSize"/> default).
    /// </summary>
    public int EagerWarmCount { get; set; } = 2;

    /// <summary>
    /// Maximum time to wait for a warm worker to become available before throwing
    /// <see cref="TimeoutException"/>. Use <see cref="TimeSpan.Zero"/> for instant-fail behaviour.
    /// Default: 15 seconds.
    /// </summary>
    public TimeSpan AcquisitionTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Duration a warm (non-leased) surplus worker may remain idle before being eligible for
    /// eviction by the sweep timer.
    /// Workers within <see cref="MinPoolSize"/> are never evicted by idle TTL alone.
    /// Default: 300 seconds (5 minutes).
    /// </summary>
    public TimeSpan IdleTtl { get; set; } = TimeSpan.FromSeconds(300);

    /// <summary>
    /// Interval between idle-TTL sweep passes.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum time to wait for <c>PSPowerShell.Stop()</c> to complete after a cancellation
    /// or command timeout before the worker is force-evicted.
    /// Default: 5 seconds.
    /// </summary>
    public TimeSpan StopTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum time to wait for all outstanding leases to be returned during shutdown drain
    /// before force-disposing remaining workers.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Interval between replenishment checks that ensure <see cref="MinPoolSize"/> warm workers
    /// are maintained.
    /// Default: 5 seconds.
    /// </summary>
    public TimeSpan ReplenishCheckInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// When true, the pool operates in ephemeral mode: each worker is evicted and disposed
    /// after every lease return rather than being reset and re-queued. A fresh worker is
    /// created immediately to replace the evicted one (via the fire-and-forget path) so the
    /// next caller pays runspace creation cost instead of reset cost.
    ///
    /// Use ephemeral mode ONLY for characterization measurements that need to isolate
    /// per-call creation cost vs. reset cost (Decision C §4B same-SDK isolation gate).
    /// Do NOT use in production: ephemeral mode defeats the pool's purpose of amortizing
    /// runspace creation overhead.
    /// Default: false.
    /// </summary>
    public bool EphemeralMode { get; set; } = false;

    /// <summary>
    /// Validates the option values and returns a list of error messages.
    /// An empty list means the options are valid.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (MinPoolSize < 1)
            errors.Add($"{nameof(MinPoolSize)} must be at least 1 (was {MinPoolSize}).");

        if (MaxPoolSize < MinPoolSize)
            errors.Add($"{nameof(MaxPoolSize)} ({MaxPoolSize}) must be >= {nameof(MinPoolSize)} ({MinPoolSize}).");

        if (EagerWarmCount < 0)
            errors.Add($"{nameof(EagerWarmCount)} must be >= 0 (was {EagerWarmCount}).");

        if (EagerWarmCount > MaxPoolSize)
            errors.Add($"{nameof(EagerWarmCount)} ({EagerWarmCount}) must not exceed {nameof(MaxPoolSize)} ({MaxPoolSize}).");

        if (AcquisitionTimeout < TimeSpan.Zero)
            errors.Add($"{nameof(AcquisitionTimeout)} must be >= zero (was {AcquisitionTimeout}).");

        if (IdleTtl <= TimeSpan.Zero)
            errors.Add($"{nameof(IdleTtl)} must be positive (was {IdleTtl}).");

        if (SweepInterval <= TimeSpan.Zero)
            errors.Add($"{nameof(SweepInterval)} must be positive (was {SweepInterval}).");

        if (StopTimeout <= TimeSpan.Zero)
            errors.Add($"{nameof(StopTimeout)} must be positive (was {StopTimeout}).");

        if (ShutdownDrainTimeout <= TimeSpan.Zero)
            errors.Add($"{nameof(ShutdownDrainTimeout)} must be positive (was {ShutdownDrainTimeout}).");

        if (ReplenishCheckInterval <= TimeSpan.Zero)
            errors.Add($"{nameof(ReplenishCheckInterval)} must be positive (was {ReplenishCheckInterval}).");

        return errors;
    }
}
