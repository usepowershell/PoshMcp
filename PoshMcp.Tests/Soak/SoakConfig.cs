using System;

namespace PoshMcp.Tests.Soak;

/// <summary>
/// Pre-declared configuration and acceptance criteria for a soak run.
///
/// <para>
/// Evaluation thresholds are declared here BEFORE the run executes so reviewers can
/// audit the gate definition independently of the run results. Phase <em>durations</em>
/// are run parameters (a short smoke/CI run may shorten them via environment overrides),
/// but the acceptance <em>thresholds</em> are a fixed contract and MUST NOT be tuned in
/// response to observed data.
/// </para>
///
/// <para><b>Run phases.</b> A run is divided into four explicitly-labeled phases so the
/// analyzer can reason about them independently:
/// <list type="bullet">
///   <item><c>baseline</c> — server started, pool warm, no load-generator traffic. Establishes
///     the pre-load idle handle floor used by the cooldown-plateau comparison.</item>
///   <item><c>warmup</c> — full traffic; JIT / pool ramp. Excluded from all trend analysis.</item>
///   <item><c>load</c> — full traffic for at least <see cref="MinLoadDuration"/>; the only phase
///     whose samples feed the memory/handle-floor/thread trend gates.</item>
///   <item><c>cooldown</c> — traffic stopped (no forced GC); lets finalizers/GC naturally reclaim
///     transient handles so the terminal idle floor can be compared to <c>baseline</c>.</item>
/// </list>
/// </para>
///
/// <para><b>Handle-stability contract (why not whole-run OLS).</b> Under load the process
/// handle count is a <em>bounded sawtooth</em>: it climbs as PowerShell invocations create
/// short-lived kernel handles, then collapses when the finalizer queue/GC reclaims them. A
/// whole-run ordinary-least-squares fit over the raw sawtooth is dominated by peak amplitude
/// and the arbitrary phase at which the run ends, so it produces false positives (a healthy,
/// fully-reclaiming process can show a large positive slope with near-zero R²). This contract
/// instead gates on the <em>floor</em>: within fixed <see cref="HandleFloorWindow"/> windows of
/// the load phase it takes a low-quantile (<see cref="HandleFloorQuantile"/>) floor estimate,
/// runs OLS on those window floors (<see cref="MaxHandleFloorSlopePerSecond"/>), and separately
/// checks that the post-load idle floor returns close to the pre-load baseline floor
/// (<see cref="HandleCooldownPlateauMaxDeltaAbsolute"/> / <see cref="HandleCooldownPlateauMaxDeltaRelative"/>).
/// Peak/sawtooth amplitude is reported as a diagnostic only and never gates leak status.
/// </para>
///
/// <para>Schema version 2.</para>
/// </summary>
public sealed record SoakConfig
{
    /// <summary>Schema version of the emitted acceptance contract.</summary>
    public const int SchemaVersion = 2;

    // ─── Phase durations (run parameters, overridable for smoke/CI) ────────────

    /// <summary>Quiet pre-load phase: server up, pool warm, no load traffic. Establishes baseline floor.</summary>
    public TimeSpan BaselineDuration { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>Warmup phase (full traffic); excluded from every trend gate.</summary>
    public TimeSpan WarmupDuration { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Main measured load phase. Set slightly above the 60-minute minimum so the measured
    /// sample span comfortably satisfies <see cref="MinLoadDuration"/> despite sampling edges.
    /// </summary>
    public TimeSpan LoadDuration { get; init; } = TimeSpan.FromMinutes(61);

    /// <summary>Quiet post-load phase: traffic stopped, NO forced GC; observe natural handle recovery.</summary>
    public TimeSpan CooldownDuration { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Interval between fixed-interval samples (applies to every phase).</summary>
    public TimeSpan SampleInterval { get; init; } = TimeSpan.FromSeconds(30);

    // ─── Load shaping ──────────────────────────────────────────────────────────

    /// <summary>Number of concurrent load-generator workers during normal load.</summary>
    public int ConcurrencyLevel { get; init; } = 4;

    /// <summary>Min delay per worker between requests (milliseconds).</summary>
    public int MinRequestDelayMs { get; init; } = 50;

    /// <summary>Max delay per worker between requests (milliseconds).</summary>
    public int MaxRequestDelayMs { get; init; } = 200;

    /// <summary>
    /// Duration of a low-traffic window inside the load phase; triggers idle-TTL eviction of
    /// surplus workers. Must exceed IdleTtl in soak-appsettings.json (45s) to guarantee a sweep.
    /// </summary>
    public TimeSpan EvictionPhaseDuration { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Duration of a normal-traffic window between eviction windows inside the load phase.</summary>
    public TimeSpan NormalPhaseDuration { get; init; } = TimeSpan.FromMinutes(13);

    // ─── Duration gate ─────────────────────────────────────────────────────────

    /// <summary>
    /// Minimum measured load span (last load sample elapsed − first load sample elapsed).
    /// Warmup, baseline, and cooldown do NOT count toward this minimum.
    /// </summary>
    public TimeSpan MinLoadDuration { get; init; } = TimeSpan.FromMinutes(60);

    // ─── Memory gates (load phase) ─────────────────────────────────────────────

    /// <summary>
    /// Max allowed OLS slope for server WorkingSet in bytes/second over load-phase samples,
    /// regressed on (elapsed_seconds, working_set_bytes).
    /// </summary>
    public double MaxMemorySlopeBytesPerSecond { get; init; } = 1_048_576; // 1 MB/s

    /// <summary>
    /// Max allowed delta between plateau means: mean(last <see cref="PlateauWindowFraction"/> of
    /// load samples) − mean(first <see cref="PlateauWindowFraction"/> of load samples).
    /// </summary>
    public long MaxMemoryPlateauDeltaBytes { get; init; } = 100L * 1024 * 1024; // 100 MB

    /// <summary>Fraction of load samples used for each plateau comparison window.</summary>
    public double PlateauWindowFraction { get; init; } = 0.10;

    // ─── Error-rate gate (all real traffic) ────────────────────────────────────

    /// <summary>
    /// Max allowed error rate = errors / total requests (fraction). Denominator is every request
    /// the load generator issued across baseline+warmup+load. Zero total requests fails the gate.
    /// </summary>
    public double MaxErrorRate { get; init; } = 0.001; // 0.1%

    // ─── Handle-floor slope gate (Windows Process.HandleCount, load phase) ──────

    /// <summary>Window length used to segment the load phase for floor estimation.</summary>
    public TimeSpan HandleFloorWindow { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Quantile (0,1) used as the per-window floor estimator. p10 is robust to the sawtooth peaks
    /// while still tracking a genuinely ratcheting floor.
    /// </summary>
    public double HandleFloorQuantile { get; init; } = 0.10;

    /// <summary>
    /// Max allowed OLS slope of the per-window handle floors (handles/second). A stable,
    /// fully-reclaiming process has a near-flat floor; 0.010/s tolerates ≈36 handles/hour of
    /// floor drift, which over a 60-minute run is well below any leak of operational concern.
    /// </summary>
    public double MaxHandleFloorSlopePerSecond { get; init; } = 0.010;

    /// <summary>
    /// Minimum number of samples required for a window to be included in the floor OLS regression.
    /// A terminal window cut short by the load-phase boundary typically contains far fewer samples
    /// than a full window (2 samples in the authoritative run vs. ~10 for full 5-minute windows)
    /// and produces an unrepresentative floor estimate that inflates the reported slope. Windows
    /// with fewer than this many samples are excluded from the OLS; the excluded count is reported
    /// in the gate detail. Must be ≥ 2.
    ///
    /// <para>Default 5: filters any terminal bin that captured less than half a normal window's
    /// samples, while retaining all full and near-full windows (≥ 50% populated).</para>
    /// </summary>
    public int MinHandleFloorWindowSamples { get; init; } = 5;

    // ─── Handle cooldown-plateau gate ──────────────────────────────────────────

    /// <summary>
    /// Absolute tolerance (handles) for (cooldown terminal floor − baseline floor). Combined with
    /// the relative tolerance via max(); the run passes if the delta is within either bound.
    /// </summary>
    public double HandleCooldownPlateauMaxDeltaAbsolute { get; init; } = 2048;

    /// <summary>Relative tolerance for the cooldown/baseline floor delta, as a fraction of the baseline floor.</summary>
    public double HandleCooldownPlateauMaxDeltaRelative { get; init; } = 1.0; // ≤ +100% of baseline floor

    // ─── Thread gate (load phase) ──────────────────────────────────────────────

    /// <summary>Max allowed OLS slope for process thread count (threads/second) over load samples.</summary>
    public double MaxThreadSlopePerSecond { get; init; } = 0.010;

    // ─── Pool observability + stability gates ──────────────────────────────────

    /// <summary>Pool workers must never exceed MaxPoolSize at any evaluated sample.</summary>
    public bool EnforceWorkerUpperBound { get; init; } = true;

    /// <summary>
    /// Minimum fraction of load-phase samples that must carry pool/health stats. The monitoring
    /// client is isolated from load so coverage should be ~100%; a small tolerance absorbs a rare
    /// health-check race during pool reset. Below this, the run fails rather than silently
    /// evaluating pool gates on a subset.
    /// </summary>
    public double MinPoolStatsCoverage { get; init; } = 0.98;

    /// <summary>After any dip below MinPoolSize, pool must recover within this many samples.</summary>
    public int ReplenishmentRecoverySamples { get; init; } = 6; // 6 × 30s = 3 min

    /// <summary>Last N load samples must show WarmWorkers+LeasedWorkers ≥ MinPoolSize (stable end state).</summary>
    public int StableEndSamples { get; init; } = 5;

    // ─── Burst-scaling profile (opt-in, diagnostic; does not alter thresholds) ──

    /// <summary>
    /// Concurrent workers during burst phases, interspersed within the load phase to exercise
    /// pool replenishment under short spikes. A value of 0 (default) disables burst phases so
    /// the load schedule remains identical to the production contract. When enabled, set to a
    /// value above <c>ConcurrencyLevel</c> but ≤ MaxPoolSize configured in soak-appsettings.json
    /// to avoid worker_upper_bound failures. Override via <c>SOAK_BURST_WORKERS</c>.
    /// </summary>
    public int BurstConcurrencyLevel { get; init; } = 0;

    /// <summary>Duration of each burst phase within the load schedule.</summary>
    public TimeSpan BurstPhaseDuration { get; init; } = TimeSpan.FromMinutes(2);

    // ─── Validation ────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates internal consistency of the contract. Throws <see cref="InvalidOperationException"/>
    /// on any violation so a misconfigured run fails fast before launching the server.
    /// </summary>
    public void Validate()
    {
        void Require(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException($"Invalid SoakConfig: {message}");
        }

        Require(SampleInterval > TimeSpan.Zero, "SampleInterval must be positive");
        Require(BaselineDuration >= TimeSpan.Zero, "BaselineDuration must be non-negative");
        Require(WarmupDuration >= TimeSpan.Zero, "WarmupDuration must be non-negative");
        Require(LoadDuration > TimeSpan.Zero, "LoadDuration must be positive");
        Require(CooldownDuration >= TimeSpan.Zero, "CooldownDuration must be non-negative");
        Require(MinLoadDuration > TimeSpan.Zero, "MinLoadDuration must be positive");
        Require(ConcurrencyLevel >= 1, "ConcurrencyLevel must be ≥ 1");
        Require(MinRequestDelayMs >= 0 && MaxRequestDelayMs >= MinRequestDelayMs, "request delay range invalid");
        Require(HandleFloorWindow >= SampleInterval, "HandleFloorWindow must be ≥ SampleInterval");
        Require(HandleFloorQuantile > 0 && HandleFloorQuantile < 1, "HandleFloorQuantile must be in (0,1)");
        Require(MaxHandleFloorSlopePerSecond >= 0, "MaxHandleFloorSlopePerSecond must be ≥ 0");
        Require(MinHandleFloorWindowSamples >= 2, "MinHandleFloorWindowSamples must be ≥ 2");
        Require(HandleCooldownPlateauMaxDeltaAbsolute >= 0, "HandleCooldownPlateauMaxDeltaAbsolute must be ≥ 0");
        Require(HandleCooldownPlateauMaxDeltaRelative >= 0, "HandleCooldownPlateauMaxDeltaRelative must be ≥ 0");
        Require(MaxMemorySlopeBytesPerSecond >= 0, "MaxMemorySlopeBytesPerSecond must be ≥ 0");
        Require(MaxThreadSlopePerSecond >= 0, "MaxThreadSlopePerSecond must be ≥ 0");
        Require(MaxErrorRate >= 0 && MaxErrorRate <= 1, "MaxErrorRate must be in [0,1]");
        Require(PlateauWindowFraction > 0 && PlateauWindowFraction <= 1, "PlateauWindowFraction must be in (0,1]");
        Require(MinPoolStatsCoverage > 0 && MinPoolStatsCoverage <= 1, "MinPoolStatsCoverage must be in (0,1]");
        Require(ReplenishmentRecoverySamples >= 1, "ReplenishmentRecoverySamples must be ≥ 1");
        Require(StableEndSamples >= 1, "StableEndSamples must be ≥ 1");
        Require(BurstConcurrencyLevel >= 0, "BurstConcurrencyLevel must be ≥ 0");
        Require(BurstPhaseDuration > TimeSpan.Zero, "BurstPhaseDuration must be positive");
    }
}
