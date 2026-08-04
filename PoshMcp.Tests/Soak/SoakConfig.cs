using System;

namespace PoshMcp.Tests.Soak;

/// <summary>
/// Pre-declared configuration and acceptance criteria for a soak run.
/// Evaluation rules are declared here BEFORE the run executes so reviewers
/// can audit the gate definition independently of the run results.
/// </summary>
public sealed class SoakConfig
{
    // ─── Run parameters ───────────────────────────────────────────────────────

    /// <summary>Duration of the warmup phase (excluded from trend analysis).</summary>
    public TimeSpan WarmupDuration { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Duration of the main soak phase (minimum one hour).</summary>
    public TimeSpan SoakDuration { get; init; } = TimeSpan.FromMinutes(60);

    /// <summary>Interval between fixed-interval samples.</summary>
    public TimeSpan SampleInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Number of concurrent load-generator workers.</summary>
    public int ConcurrencyLevel { get; init; } = 4;

    /// <summary>Min delay per worker between requests (milliseconds).</summary>
    public int MinRequestDelayMs { get; init; } = 50;

    /// <summary>Max delay per worker between requests (milliseconds).</summary>
    public int MaxRequestDelayMs { get; init; } = 200;

    // ─── Eviction cycle parameters ────────────────────────────────────────────

    /// <summary>
    /// Duration of a low-traffic window; triggers idle-TTL eviction of surplus workers.
    /// Must be > IdleTtl in soak-appsettings.json (45s) to guarantee at least one sweep pass.
    /// </summary>
    public TimeSpan EvictionPhaseDuration { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Duration of a normal-traffic window between eviction phases.</summary>
    public TimeSpan NormalPhaseDuration { get; init; } = TimeSpan.FromMinutes(13);

    // ─── Pre-declared acceptance criteria ─────────────────────────────────────

    /// <summary>
    /// Maximum allowed linear trend slope for server WorkingSet in bytes/second.
    /// Computed via ordinary least squares on (elapsed_seconds, working_set_bytes)
    /// with warmup samples excluded. Warmup exclusion: first floor(n × WarmupExclusionFraction)
    /// samples are dropped before regression, where n = total post-warmup sample count.
    /// </summary>
    public double MaxMemorySlopeBytesPerSecond { get; init; } = 1_048_576; // 1 MB/s

    /// <summary>
    /// Maximum allowed delta between the plateau means.
    /// Plateau mean = mean of the first/last <see cref="PlateauWindowFraction"/> of
    /// post-warmup samples. Pass if: (last_plateau_mean - first_plateau_mean) ≤ threshold.
    /// </summary>
    public long MaxMemoryPlateauDeltaBytes { get; init; } = 100L * 1024 * 1024; // 100 MB

    /// <summary>Fraction of samples used for plateau comparison windows.</summary>
    public double PlateauWindowFraction { get; init; } = 0.10;

    /// <summary>
    /// Maximum allowed error rate: errors / total_requests (fraction, not percent).
    /// Denominator: all requests sent by the load generator. Threshold: 0.001 (0.1%).
    /// If total_requests == 0 the gate fails (no traffic = no valid run).
    /// </summary>
    public double MaxErrorRate { get; init; } = 0.001;

    /// <summary>
    /// Max allowed linear trend slope for process handle count (handles/second).
    /// -1 when handle count is unsupported on the OS; gate skips with UNSUPPORTED status.
    /// </summary>
    public double MaxHandleSlopePerSecond { get; init; } = 0.01; // 1 handle per 100s

    /// <summary>
    /// Max allowed linear trend slope for process thread count (threads/second).
    /// </summary>
    public double MaxThreadSlopePerSecond { get; init; } = 0.01;

    /// <summary>
    /// Pool workers must never exceed MaxPoolSize at any sample.
    /// </summary>
    public bool EnforceWorkerUpperBound { get; init; } = true;

    /// <summary>
    /// After any eviction cycle, pool must recover to ≥ MinPoolSize within this many samples.
    /// Sample interval × RecoverySamples = max recovery wall time.
    /// </summary>
    public int ReplenishmentRecoverySamples { get; init; } = 6; // 6 × 30s = 3 min

    /// <summary>
    /// Last N samples must show WarmWorkers+LeasedWorkers ≥ MinPoolSize (stable end state).
    /// </summary>
    public int StableEndSamples { get; init; } = 5;
}
