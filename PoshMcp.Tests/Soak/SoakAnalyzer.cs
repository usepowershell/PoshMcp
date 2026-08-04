using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PoshMcp.Tests.Soak;

/// <summary>
/// Pass/fail (or diagnostic) result for a single acceptance gate.
/// <para><see cref="Status"/> is one of PASS, FAIL, SKIP, UNSUPPORTED, DIAGNOSTIC.</para>
/// </summary>
public sealed record SoakGateResult(
    string Gate,
    bool Passed,
    string Status,
    string Detail,
    double? MeasuredValue = null,
    double? Threshold = null);

/// <summary>
/// Evaluates soak run samples against the pre-declared criteria in <see cref="SoakConfig"/>.
///
/// <para>All statistical math is implemented as pure static methods so it can be unit-tested
/// independently of a live run. The handle-stability gate deliberately regresses the per-window
/// <em>floor</em> of the handle count (not the raw sawtooth series) — see <see cref="SoakConfig"/>
/// for the mathematical rationale.</para>
/// </summary>
public static class SoakAnalyzer
{
    public const string PhaseBaseline = "baseline";
    public const string PhaseWarmup = "warmup";
    public const string PhaseLoad = "load";
    public const string PhaseCooldown = "cooldown";

    // ─── Core math (pure) ──────────────────────────────────────────────────────

    /// <summary>
    /// Ordinary least-squares slope (Δy per unit x). Returns <c>null</c> for fewer than 2 points.
    /// Throws <see cref="ArgumentException"/> if any coordinate is NaN or Infinity.
    /// </summary>
    public static double? Slope(IReadOnlyList<(double X, double Y)> points)
    {
        if (points is null) throw new ArgumentNullException(nameof(points));
        if (points.Count < 2) return null;

        var n = points.Count;
        double sumX = 0, sumY = 0, sumXX = 0, sumXY = 0;
        foreach (var (x, y) in points)
        {
            if (!double.IsFinite(x) || !double.IsFinite(y))
                throw new ArgumentException("OLS input contains a non-finite value (NaN/Infinity).", nameof(points));
            sumX += x;
            sumY += y;
            sumXX += x * x;
            sumXY += x * y;
        }

        var denom = (n * sumXX) - (sumX * sumX);
        if (Math.Abs(denom) < double.Epsilon) return 0.0;
        return ((n * sumXY) - (sumX * sumY)) / denom;
    }

    /// <summary>Mean of a sequence. Returns <c>null</c> for an empty sequence. Rejects non-finite input.</summary>
    public static double? Mean(IReadOnlyList<double> values)
    {
        if (values is null) throw new ArgumentNullException(nameof(values));
        if (values.Count == 0) return null;
        double sum = 0;
        foreach (var v in values)
        {
            if (!double.IsFinite(v))
                throw new ArgumentException("Mean input contains a non-finite value (NaN/Infinity).", nameof(values));
            sum += v;
        }
        return sum / values.Count;
    }

    /// <summary>
    /// Linear-interpolation quantile (type-7). <paramref name="q"/> in [0,1].
    /// Returns <c>null</c> for an empty sequence. Rejects non-finite input.
    /// </summary>
    public static double? Quantile(IReadOnlyList<double> values, double q)
    {
        if (values is null) throw new ArgumentNullException(nameof(values));
        if (q < 0 || q > 1) throw new ArgumentOutOfRangeException(nameof(q), "Quantile must be in [0,1].");
        if (values.Count == 0) return null;

        var sorted = new List<double>(values.Count);
        foreach (var v in values)
        {
            if (!double.IsFinite(v))
                throw new ArgumentException("Quantile input contains a non-finite value (NaN/Infinity).", nameof(values));
            sorted.Add(v);
        }
        sorted.Sort();

        if (sorted.Count == 1) return sorted[0];
        var rank = q * (sorted.Count - 1);
        var lo = (int)Math.Floor(rank);
        var hi = (int)Math.Ceiling(rank);
        if (lo == hi) return sorted[lo];
        var frac = rank - lo;
        return sorted[lo] + (frac * (sorted[hi] - sorted[lo]));
    }

    /// <summary>
    /// Plateau delta: mean(last window) − mean(first window). Window size is
    /// <c>floor(count × fraction)</c>, minimum 1. Returns <c>null</c> for fewer than 2 values.
    /// </summary>
    public static double? PlateauDelta(IReadOnlyList<double> values, double windowFraction)
    {
        if (values is null) throw new ArgumentNullException(nameof(values));
        if (windowFraction <= 0 || windowFraction > 1)
            throw new ArgumentOutOfRangeException(nameof(windowFraction), "Must be in (0,1].");
        if (values.Count < 2) return null;

        var windowSize = Math.Max(1, (int)Math.Floor(values.Count * windowFraction));
        var first = values.Take(windowSize).ToList();
        var last = values.TakeLast(windowSize).ToList();
        return Mean(last) - Mean(first);
    }

    /// <summary>Error rate = errors / total. Returns <c>null</c> when total == 0.</summary>
    public static double? ErrorRate(long totalRequests, long errorRequests)
    {
        if (totalRequests < 0) throw new ArgumentOutOfRangeException(nameof(totalRequests));
        if (errorRequests < 0) throw new ArgumentOutOfRangeException(nameof(errorRequests));
        if (totalRequests == 0) return null;
        return (double)errorRequests / totalRequests;
    }

    /// <summary>
    /// Partitions <paramref name="handlePoints"/> (elapsedSeconds, handleCount) into fixed windows
    /// of <paramref name="windowSeconds"/> and returns, per non-empty window, the mean elapsed second
    /// (X) and the <paramref name="quantile"/> floor of that window's handle counts (Y). Windows are
    /// anchored at the first sample's elapsed time so results are deterministic and irregular sample
    /// spacing is handled by true elapsed time.
    /// </summary>
    public static IReadOnlyList<(double CenterSeconds, double Floor)> WindowFloors(
        IReadOnlyList<(double ElapsedSeconds, double Handles)> handlePoints,
        double windowSeconds,
        double quantile)
    {
        if (handlePoints is null) throw new ArgumentNullException(nameof(handlePoints));
        if (windowSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(windowSeconds));
        var result = new List<(double, double)>();
        if (handlePoints.Count == 0) return result;

        var ordered = handlePoints.OrderBy(p => p.ElapsedSeconds).ToList();
        var origin = ordered[0].ElapsedSeconds;

        var groups = ordered
            .GroupBy(p => (long)Math.Floor((p.ElapsedSeconds - origin) / windowSeconds))
            .OrderBy(g => g.Key);

        foreach (var g in groups)
        {
            var xs = g.Select(p => p.ElapsedSeconds).ToList();
            var ys = g.Select(p => p.Handles).ToList();
            var center = Mean(xs)!.Value;
            var floor = Quantile(ys, quantile)!.Value;
            result.Add((center, floor));
        }

        return result;
    }

    // ─── Gate evaluation ───────────────────────────────────────────────────────

    /// <summary>
    /// Evaluates all acceptance gates against the given samples + config.
    /// Warmup samples (<see cref="PhaseWarmup"/>) are excluded from every trend gate.
    /// Trend/duration gates operate on <see cref="PhaseLoad"/> samples; the cooldown-plateau
    /// gate compares <see cref="PhaseCooldown"/> to <see cref="PhaseBaseline"/>.
    /// </summary>
    public static IReadOnlyList<SoakGateResult> Evaluate(
        IReadOnlyList<SoakSample> allSamples,
        SoakConfig cfg)
    {
        if (allSamples is null) throw new ArgumentNullException(nameof(allSamples));
        if (cfg is null) throw new ArgumentNullException(nameof(cfg));

        var results = new List<SoakGateResult>();

        var ordered = allSamples.OrderBy(s => s.ElapsedMs).ToList();
        var baseline = ordered.Where(s => s.Phase == PhaseBaseline).ToList();
        var load = ordered.Where(s => s.Phase == PhaseLoad).ToList();
        var cooldown = ordered.Where(s => s.Phase == PhaseCooldown).ToList();

        // ── Gate: sample integrity (no duplicate timestamps within a phase) ────
        results.Add(EvaluateSampleIntegrity(ordered));

        // ── Gate: load duration (measured load span only) ─────────────────────
        var minLoadMs = (long)cfg.MinLoadDuration.TotalMilliseconds;
        if (load.Count >= 2)
        {
            var loadSpanMs = load[^1].ElapsedMs - load[0].ElapsedMs;
            results.Add(loadSpanMs >= minLoadMs
                ? Pass("load_duration", $"Load span {loadSpanMs / 60000.0:F1} min ≥ {cfg.MinLoadDuration.TotalMinutes:F1} min required", loadSpanMs, minLoadMs)
                : Fail("load_duration", $"Load span {loadSpanMs / 60000.0:F1} min < {cfg.MinLoadDuration.TotalMinutes:F1} min required", loadSpanMs, minLoadMs));
        }
        else
        {
            results.Add(Fail("load_duration", $"Only {load.Count} load-phase samples; need ≥ 2", load.Count, minLoadMs));
        }

        // ── Gate: error rate (all real traffic) ───────────────────────────────
        var lastCum = ordered.LastOrDefault();
        if (lastCum is null)
        {
            results.Add(Fail("error_rate", "No samples — cannot evaluate error rate", null, cfg.MaxErrorRate));
        }
        else
        {
            var rate = ErrorRate(lastCum.TotalRequests, lastCum.ErrorRequests);
            if (rate is null)
                results.Add(Fail("error_rate", "Zero total requests — no valid traffic", 0.0, cfg.MaxErrorRate));
            else
                results.Add(rate <= cfg.MaxErrorRate
                    ? Pass("error_rate", $"Error rate {rate * 100:F4}% ({lastCum.ErrorRequests}/{lastCum.TotalRequests}) ≤ {cfg.MaxErrorRate * 100:F1}%", rate, cfg.MaxErrorRate)
                    : Fail("error_rate", $"Error rate {rate * 100:F4}% ({lastCum.ErrorRequests}/{lastCum.TotalRequests}) exceeds {cfg.MaxErrorRate * 100:F1}%", rate, cfg.MaxErrorRate));
        }

        // ── Gate: PS execution proof (intended mix + executed PS volume) ──────
        results.Add(EvaluatePsExecution(lastCum, cfg));

        // ── Gate: memory slope (load phase) ───────────────────────────────────
        results.Add(EvaluateSlopeGate(
            "memory_slope", load, s => (double)s.WorkingSetBytes, cfg.MaxMemorySlopeBytesPerSecond,
            slope => $"Memory slope {slope / 1024.0:F2} KB/s",
            $"threshold {cfg.MaxMemorySlopeBytesPerSecond / 1024.0:F0} KB/s"));

        // ── Gate: memory plateau delta (load phase) ───────────────────────────
        if (load.Count >= 2)
        {
            var delta = PlateauDelta(load.Select(s => (double)s.WorkingSetBytes).ToList(), cfg.PlateauWindowFraction);
            results.Add(delta is null
                ? Fail("memory_plateau", "Insufficient samples for plateau comparison", null, cfg.MaxMemoryPlateauDeltaBytes)
                : delta <= cfg.MaxMemoryPlateauDeltaBytes
                    ? Pass("memory_plateau", $"Plateau delta {delta / (1024 * 1024.0):F1} MB ≤ {cfg.MaxMemoryPlateauDeltaBytes / (1024 * 1024.0):F0} MB", delta, cfg.MaxMemoryPlateauDeltaBytes)
                    : Fail("memory_plateau", $"Plateau delta {delta / (1024 * 1024.0):F1} MB exceeds {cfg.MaxMemoryPlateauDeltaBytes / (1024 * 1024.0):F0} MB", delta, cfg.MaxMemoryPlateauDeltaBytes));
        }
        else
        {
            results.Add(Fail("memory_plateau", $"Only {load.Count} load-phase samples", null, cfg.MaxMemoryPlateauDeltaBytes));
        }

        // ── Gate: handle FLOOR slope (Windows; load phase, windowed floors) ───
        results.Add(EvaluateHandleFloorSlope(load, cfg));

        // ── Gate: handle cooldown plateau (baseline vs cooldown floor) ────────
        results.Add(EvaluateHandleCooldownPlateau(baseline, cooldown, cfg));

        // ── Diagnostic (non-gating): handle amplitude / peak ──────────────────
        results.Add(EvaluateHandleAmplitudeDiagnostic(load, cfg));

        // ── Gate: thread slope (load phase) ───────────────────────────────────
        results.Add(EvaluateSlopeGate(
            "thread_slope", load, s => (double)s.ProcessThreadCount, cfg.MaxThreadSlopePerSecond,
            slope => $"Thread slope {slope:F5} /s",
            $"threshold {cfg.MaxThreadSlopePerSecond:F3} /s"));

        // ── Gate: pool stats coverage (load phase) ────────────────────────────
        results.Add(EvaluatePoolCoverage(load, cfg));

        // ── Gate: worker upper bound (load phase) ─────────────────────────────
        results.Add(EvaluateWorkerUpperBound(load, cfg));

        // ── Gate: replenishment recovery (load phase) ─────────────────────────
        results.Add(EvaluateReplenishmentRecovery(load, cfg));

        // ── Gate: stable end state (end of load phase) ────────────────────────
        results.Add(EvaluateStableEndState(load, cfg));

        // ── Gate: server stability (any phase) ────────────────────────────────
        var crash = ordered.FirstOrDefault(s => s.Note != null && s.Note.Contains("SERVER_CRASH"));
        results.Add(crash is null
            ? Pass("server_stability", "No server crash detected during run", 0, 0)
            : Fail("server_stability", $"Server crash detected at {crash.ElapsedMs}ms", 1, 0));

        return results.AsReadOnly();
    }

    private static SoakGateResult EvaluateSampleIntegrity(IReadOnlyList<SoakSample> ordered)
    {
        var dupes = ordered
            .GroupBy(s => (s.Phase, s.ElapsedMs))
            .Where(g => g.Count() > 1)
            .ToList();
        if (dupes.Count == 0)
            return Pass("sample_integrity", $"{ordered.Count} samples; no duplicate (phase,elapsed) keys", ordered.Count, null);
        var first = dupes[0];
        return Fail("sample_integrity",
            $"{dupes.Count} duplicate sample key(s); first: phase={first.Key.Phase} elapsed={first.Key.ElapsedMs}ms ×{first.Count()}",
            dupes.Count, 0);
    }

    private static SoakGateResult EvaluatePsExecution(SoakSample? last, SoakConfig cfg)
    {
        if (last is null)
            return Fail("ps_execution", "No samples — cannot verify PS execution", null, null);

        if (last.InitializeRequests <= 0 || last.ToolsListRequests <= 0 || last.ToolsCallRequests <= 0)
            return Fail("ps_execution",
                $"Intended request mix not exercised: initialize={last.InitializeRequests} tools/list={last.ToolsListRequests} tools/call={last.ToolsCallRequests}",
                last.ToolsCallRequests, null);

        var minSuccess = last.ToolsCallRequests * (1.0 - cfg.MaxErrorRate);
        return last.ToolsCallPsSuccess >= minSuccess
            ? Pass("ps_execution",
                $"PowerShell executed: {last.ToolsCallPsSuccess}/{last.ToolsCallRequests} tools/call produced valid Get-Date output " +
                $"(initialize={last.InitializeRequests}, tools/list={last.ToolsListRequests})",
                last.ToolsCallPsSuccess, minSuccess)
            : Fail("ps_execution",
                $"Only {last.ToolsCallPsSuccess}/{last.ToolsCallRequests} tools/call produced valid Get-Date output (need ≥ {minSuccess:F0})",
                last.ToolsCallPsSuccess, minSuccess);
    }

    private static SoakGateResult EvaluateSlopeGate(
        string gate,
        IReadOnlyList<SoakSample> load,
        Func<SoakSample, double> selector,
        double threshold,
        Func<double, string> describe,
        string thresholdText)
    {
        if (load.Count < 2)
            return Fail(gate, $"Only {load.Count} load-phase samples; need ≥ 2", null, threshold);

        var points = load.Select(s => (X: s.ElapsedMs / 1000.0, Y: selector(s))).ToList();
        var slope = Slope(points);
        if (slope is null)
            return Fail(gate, "Insufficient samples for slope", null, threshold);
        return slope <= threshold
            ? Pass(gate, $"{describe(slope.Value)} ≤ {thresholdText}", slope, threshold)
            : Fail(gate, $"{describe(slope.Value)} exceeds {thresholdText}", slope, threshold);
    }

    private static SoakGateResult EvaluateHandleFloorSlope(IReadOnlyList<SoakSample> load, SoakConfig cfg)
    {
        var supported = load.Where(s => s.HandleCountSupported).ToList();
        if (supported.Count == 0)
            return new SoakGateResult("handle_floor_slope", true, "UNSUPPORTED",
                "Process.HandleCount not supported on this OS; gate skipped.");

        var points = supported
            .Select(s => (ElapsedSeconds: s.ElapsedMs / 1000.0, Handles: (double)s.ProcessHandleCount))
            .ToList();
        var floors = WindowFloors(points, cfg.HandleFloorWindow.TotalSeconds, cfg.HandleFloorQuantile);
        if (floors.Count < 2)
            return Fail("handle_floor_slope",
                $"Only {floors.Count} handle floor window(s); need ≥ 2 ({cfg.HandleFloorWindow.TotalMinutes:F0}-min windows)",
                floors.Count, cfg.MaxHandleFloorSlopePerSecond);

        var slope = Slope(floors.Select(f => (f.CenterSeconds, f.Floor)).ToList());
        var floorDesc = string.Join(", ", floors.Select(f => $"{f.Floor:F0}@{f.CenterSeconds / 60.0:F0}m"));
        return slope is null
            ? Fail("handle_floor_slope", "Insufficient floor windows for slope", null, cfg.MaxHandleFloorSlopePerSecond)
            : slope <= cfg.MaxHandleFloorSlopePerSecond
                ? Pass("handle_floor_slope",
                    $"Handle floor slope {slope:F5} /s ≤ {cfg.MaxHandleFloorSlopePerSecond:F3} /s over {floors.Count} windows [{floorDesc}]",
                    slope, cfg.MaxHandleFloorSlopePerSecond)
                : Fail("handle_floor_slope",
                    $"Handle floor slope {slope:F5} /s exceeds {cfg.MaxHandleFloorSlopePerSecond:F3} /s over {floors.Count} windows [{floorDesc}]",
                    slope, cfg.MaxHandleFloorSlopePerSecond);
    }

    private static SoakGateResult EvaluateHandleCooldownPlateau(
        IReadOnlyList<SoakSample> baseline,
        IReadOnlyList<SoakSample> cooldown,
        SoakConfig cfg)
    {
        var baseSupported = baseline.Where(s => s.HandleCountSupported).ToList();
        var coolSupported = cooldown.Where(s => s.HandleCountSupported).ToList();

        if (baseline.Count == 0 && cooldown.Count == 0)
            return new SoakGateResult("handle_cooldown_plateau", true, "UNSUPPORTED",
                "No baseline/cooldown samples; gate not applicable to this run shape.");
        if (baseSupported.Count == 0 && coolSupported.Count == 0)
            return new SoakGateResult("handle_cooldown_plateau", true, "UNSUPPORTED",
                "Process.HandleCount not supported on this OS; gate skipped.");
        if (baseSupported.Count == 0)
            return Fail("handle_cooldown_plateau", "No baseline handle samples to establish pre-load floor", null, null);
        if (coolSupported.Count == 0)
            return Fail("handle_cooldown_plateau", "No cooldown handle samples to prove recovery", null, null);

        var baseFloor = Quantile(baseSupported.Select(s => (double)s.ProcessHandleCount).ToList(), cfg.HandleFloorQuantile)!.Value;
        var coolFloor = Quantile(coolSupported.Select(s => (double)s.ProcessHandleCount).ToList(), cfg.HandleFloorQuantile)!.Value;
        var delta = coolFloor - baseFloor;
        var allowed = Math.Max(cfg.HandleCooldownPlateauMaxDeltaAbsolute, cfg.HandleCooldownPlateauMaxDeltaRelative * baseFloor);

        return delta <= allowed
            ? Pass("handle_cooldown_plateau",
                $"Cooldown floor {coolFloor:F0} − baseline floor {baseFloor:F0} = {delta:F0} handles ≤ {allowed:F0} allowed",
                delta, allowed)
            : Fail("handle_cooldown_plateau",
                $"Cooldown floor {coolFloor:F0} − baseline floor {baseFloor:F0} = {delta:F0} handles exceeds {allowed:F0} allowed (unrecovered handles)",
                delta, allowed);
    }

    private static SoakGateResult EvaluateHandleAmplitudeDiagnostic(IReadOnlyList<SoakSample> load, SoakConfig cfg)
    {
        var supported = load.Where(s => s.HandleCountSupported).ToList();
        if (supported.Count == 0)
            return new SoakGateResult("handle_amplitude_diagnostic", true, "DIAGNOSTIC", "No handle samples.");

        var values = supported.Select(s => (double)s.ProcessHandleCount).ToList();
        var min = values.Min();
        var max = values.Max();
        var floor = Quantile(values, cfg.HandleFloorQuantile)!.Value;
        var peak = Quantile(values, 1.0 - cfg.HandleFloorQuantile)!.Value;
        return new SoakGateResult("handle_amplitude_diagnostic", true, "DIAGNOSTIC",
            $"Handle sawtooth (diagnostic only, never gates): min={min:F0} p{cfg.HandleFloorQuantile * 100:F0}={floor:F0} " +
            $"p{(1 - cfg.HandleFloorQuantile) * 100:F0}={peak:F0} max={max:F0} amplitude={max - min:F0}",
            max - min, null);
    }

    private static SoakGateResult EvaluatePoolCoverage(IReadOnlyList<SoakSample> load, SoakConfig cfg)
    {
        if (load.Count == 0)
            return Fail("pool_stats_coverage", "No load-phase samples", null, cfg.MinPoolStatsCoverage);

        var withStats = load.Count(s => s.PoolStatsAvailable);
        var coverage = (double)withStats / load.Count;
        var missing = load.Count - withStats;
        return coverage >= cfg.MinPoolStatsCoverage
            ? Pass("pool_stats_coverage",
                $"Pool/health coverage {coverage * 100:F1}% ({withStats}/{load.Count}; {missing} missing) ≥ {cfg.MinPoolStatsCoverage * 100:F0}%",
                coverage, cfg.MinPoolStatsCoverage)
            : Fail("pool_stats_coverage",
                $"Pool/health coverage {coverage * 100:F1}% ({withStats}/{load.Count}; {missing} missing) below {cfg.MinPoolStatsCoverage * 100:F0}% required",
                coverage, cfg.MinPoolStatsCoverage);
    }

    private static SoakGateResult EvaluateWorkerUpperBound(IReadOnlyList<SoakSample> load, SoakConfig cfg)
    {
        if (!cfg.EnforceWorkerUpperBound)
            return new SoakGateResult("worker_upper_bound", true, "SKIP", "EnforceWorkerUpperBound=false");
        var pool = load.Where(s => s.PoolStatsAvailable).ToList();
        if (pool.Count == 0)
            return Fail("worker_upper_bound", "No pool-bearing load samples to evaluate", null, 0);
        var violations = pool.Where(s => s.PoolTotal > s.PoolMax).ToList();
        return violations.Count == 0
            ? Pass("worker_upper_bound", $"TotalWorkers ≤ MaxPoolSize at all {pool.Count} pool samples", 0, 0)
            : Fail("worker_upper_bound",
                $"{violations.Count} sample(s) had TotalWorkers > MaxPoolSize. First: elapsed={violations[0].ElapsedMs}ms total={violations[0].PoolTotal} max={violations[0].PoolMax}",
                violations.Count, 0);
    }

    private static SoakGateResult EvaluateReplenishmentRecovery(IReadOnlyList<SoakSample> load, SoakConfig cfg)
    {
        var pool = load.Where(s => s.PoolStatsAvailable).ToList();
        if (pool.Count == 0)
            return Fail("replenishment_recovery", "No pool-bearing load samples to evaluate", null, 0);

        for (var i = 0; i < pool.Count; i++)
        {
            var s = pool[i];
            if (s.PoolTotal < s.PoolMin)
            {
                var limit = Math.Min(pool.Count, i + cfg.ReplenishmentRecoverySamples + 1);
                var recovered = false;
                for (var j = i + 1; j < limit; j++)
                {
                    if (pool[j].PoolTotal >= pool[j].PoolMin) { recovered = true; break; }
                }
                if (!recovered)
                    return Fail("replenishment_recovery",
                        $"Pool TotalWorkers={s.PoolTotal} < MinPoolSize={s.PoolMin} at elapsed={s.ElapsedMs}ms and did not recover within {cfg.ReplenishmentRecoverySamples} samples ({cfg.ReplenishmentRecoverySamples * cfg.SampleInterval.TotalSeconds:F0}s)",
                        s.PoolTotal, s.PoolMin);
            }
        }
        return Pass("replenishment_recovery", $"Pool recovered to ≥ MinPoolSize within {cfg.ReplenishmentRecoverySamples} samples after every dip", 0, 0);
    }

    private static SoakGateResult EvaluateStableEndState(IReadOnlyList<SoakSample> load, SoakConfig cfg)
    {
        var pool = load.Where(s => s.PoolStatsAvailable).ToList();
        if (pool.Count < cfg.StableEndSamples)
            return Fail("stable_end_state", $"Only {pool.Count} pool load samples; need {cfg.StableEndSamples}", pool.Count, cfg.StableEndSamples);

        var end = pool.TakeLast(cfg.StableEndSamples).ToList();
        var unstable = end.Where(s => (s.PoolWarm + s.PoolLeased) < s.PoolMin).ToList();
        return unstable.Count == 0
            ? Pass("stable_end_state", $"Last {cfg.StableEndSamples} pool load samples all have WarmWorkers+LeasedWorkers ≥ MinPoolSize", 0, 0)
            : Fail("stable_end_state",
                $"{unstable.Count}/{cfg.StableEndSamples} end samples had Warm+Leased < MinPoolSize. Last: warm={end[^1].PoolWarm} leased={end[^1].PoolLeased} min={end[^1].PoolMin}",
                unstable.Count, 0);
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private static SoakGateResult Pass(string gate, string detail, double? measured, double? threshold) =>
        new(gate, true, "PASS", detail, measured, threshold);

    private static SoakGateResult Fail(string gate, string detail, double? measured, double? threshold) =>
        new(gate, false, "FAIL", detail, measured, threshold);
}
