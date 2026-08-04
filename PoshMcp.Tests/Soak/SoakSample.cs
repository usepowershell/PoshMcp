using System;

namespace PoshMcp.Tests.Soak;

/// <summary>
/// Immutable fixed-interval snapshot recorded during a soak run.
/// All fields are nullable only when genuinely unsupported on the current OS.
/// Schema version 1.
/// </summary>
public sealed record SoakSample
{
    // ─── Timing ──────────────────────────────────────────────────────────────

    public DateTimeOffset Timestamp { get; init; }
    public long ElapsedMs { get; init; }
    public string Phase { get; init; } = "";

    // ─── Request counters (cumulative) ────────────────────────────────────────

    public long TotalRequests { get; init; }
    public long SuccessRequests { get; init; }
    public long ErrorRequests { get; init; }

    // ─── Interval counters (since previous sample) ────────────────────────────

    public long IntervalRequests { get; init; }
    public long IntervalErrors { get; init; }

    // ─── Latency percentiles (ms, current interval) ──────────────────────────

    public double? P50LatencyMs { get; init; }
    public double? P99LatencyMs { get; init; }

    // ─── Server process memory (bytes) ────────────────────────────────────────

    public long WorkingSetBytes { get; init; }

    // ─── Server process handles / threads ─────────────────────────────────────

    /// <summary>-1 when unsupported on the current OS.</summary>
    public int ProcessHandleCount { get; init; }
    public bool HandleCountSupported { get; init; }
    public int ProcessThreadCount { get; init; }

    // ─── Pool stats (from /health runspace_pool check) ────────────────────────

    public int PoolWarm { get; init; }
    public int PoolLeased { get; init; }
    public int PoolResetting { get; init; }
    public int PoolCreating { get; init; }
    public int PoolTotal { get; init; }
    public int PoolMin { get; init; }
    public int PoolMax { get; init; }
    public bool PoolIsStarted { get; init; }
    public bool PoolIsDraining { get; init; }
    public bool PoolStatsAvailable { get; init; }

    // ─── Annotations ──────────────────────────────────────────────────────────

    public string? Note { get; init; }
}
