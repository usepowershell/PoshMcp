using System;
using System.Collections.Generic;
using System.Linq;

namespace PoshMcp.Tests.Soak;

/// <summary>
/// Pass/fail result for a single acceptance gate.
/// </summary>
public sealed record SoakGateResult(
    string Gate,
    bool Passed,
    string Status,
    string Detail,
    double? MeasuredValue = null,
    double? Threshold = null);

/// <summary>
/// Evaluates soak run samples against pre-declared criteria from <see cref="SoakConfig"/>.
/// All analysis math is implemented as pure static methods for unit-testability.
/// </summary>
public static class SoakAnalyzer
{
    // ─── Core math ────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes ordinary least-squares slope (change in y per unit x).
    /// Returns <c>null</c> for fewer than 2 points.
    /// </summary>
    public static double? Slope(IReadOnlyList<(double X, double Y)> points)
    {
        if (points is null) throw new ArgumentNullException(nameof(points));
        if (points.Count < 2) return null;

        var n = points.Count;
        var sumX = 0.0;
        var sumY = 0.0;
        var sumXX = 0.0;
        var sumXY = 0.0;

        foreach (var (x, y) in points)
        {
            sumX += x;
            sumY += y;
            sumXX += x * x;
            sumXY += x * y;
        }

        var denom = (n * sumXX) - (sumX * sumX);
        if (Math.Abs(denom) < double.Epsilon) return 0.0;
        return ((n * sumXY) - (sumX * sumY)) / denom;
    }

    /// <summary>
    /// Computes the mean of a sequence. Returns <c>null</c> for an empty sequence.
    /// </summary>
    public static double? Mean(IReadOnlyList<double> values)
    {
        if (values is null) throw new ArgumentNullException(nameof(values));
        if (values.Count == 0) return null;
        return values.Sum() / values.Count;
    }

    /// <summary>
    /// Computes the plateau delta: mean(last window) - mean(first window).
    /// Window size is <c>floor(count × fraction)</c>, minimum 1.
    /// Returns <c>null</c> when fewer than 2 samples are present.
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

    /// <summary>
    /// Computes error rate as errors / total. Returns <c>null</c> when total == 0.
    /// </summary>
    public static double? ErrorRate(long totalRequests, long errorRequests)
    {
        if (totalRequests < 0) throw new ArgumentOutOfRangeException(nameof(totalRequests));
        if (errorRequests < 0) throw new ArgumentOutOfRangeException(nameof(errorRequests));
        if (totalRequests == 0) return null;
        return (double)errorRequests / totalRequests;
    }

    // ─── Gate evaluators ─────────────────────────────────────────────────────

    /// <summary>
    /// Evaluates all acceptance gates against the given samples + config.
    /// Warmup samples (Phase == "warmup") are excluded from trend analysis.
    /// </summary>
    public static IReadOnlyList<SoakGateResult> Evaluate(
        IReadOnlyList<SoakSample> allSamples,
        SoakConfig cfg)
    {
        if (allSamples is null) throw new ArgumentNullException(nameof(allSamples));
        if (cfg is null) throw new ArgumentNullException(nameof(cfg));

        var results = new List<SoakGateResult>();

        var soakSamples = allSamples
            .Where(s => s.Phase != "warmup")
            .OrderBy(s => s.ElapsedMs)
            .ToList();

        // ── Gate 1: Minimum soak duration ─────────────────────────────────────
        var lastSample = allSamples.OrderBy(s => s.ElapsedMs).LastOrDefault();
        var warmupSamples = allSamples.Where(s => s.Phase == "warmup").ToList();
        var warmupMs = warmupSamples.Count > 0 ? warmupSamples.Max(s => s.ElapsedMs) : 0L;
        var soakMs = lastSample is not null ? lastSample.ElapsedMs - warmupMs : 0L;
        var minSoakMs = (long)cfg.SoakDuration.TotalMilliseconds;
        results.Add(soakMs >= minSoakMs
            ? Pass("soak_duration", $"Soak ran {soakMs / 60000.0:F1} min ≥ {cfg.SoakDuration.TotalMinutes:F0} min required", soakMs, minSoakMs)
            : Fail("soak_duration", $"Soak ran only {soakMs / 60000.0:F1} min; required {cfg.SoakDuration.TotalMinutes:F0} min", soakMs, minSoakMs));

        // ── Gate 2: Error rate ─────────────────────────────────────────────────
        var lastCumSample = allSamples.OrderBy(s => s.ElapsedMs).LastOrDefault();
        if (lastCumSample is null)
        {
            results.Add(Fail("error_rate", "No samples — cannot evaluate error rate", null, cfg.MaxErrorRate));
        }
        else
        {
            var rate = ErrorRate(lastCumSample.TotalRequests, lastCumSample.ErrorRequests);
            if (rate is null)
                results.Add(Fail("error_rate", "Zero total requests — no valid traffic", 0.0, cfg.MaxErrorRate));
            else
                results.Add(rate <= cfg.MaxErrorRate
                    ? Pass("error_rate",
                        $"Error rate {rate * 100:F4}% ({lastCumSample.ErrorRequests}/{lastCumSample.TotalRequests}) ≤ {cfg.MaxErrorRate * 100:F1}% threshold",
                        rate, cfg.MaxErrorRate)
                    : Fail("error_rate",
                        $"Error rate {rate * 100:F4}% ({lastCumSample.ErrorRequests}/{lastCumSample.TotalRequests}) exceeds {cfg.MaxErrorRate * 100:F1}% threshold",
                        rate, cfg.MaxErrorRate));
        }

        // ── Gate 3: Memory growth slope ───────────────────────────────────────
        if (soakSamples.Count >= 2)
        {
            var memPoints = soakSamples
                .Select(s => (X: s.ElapsedMs / 1000.0, Y: (double)s.WorkingSetBytes))
                .ToList();
            var slope = Slope(memPoints);
            results.Add(slope is null
                ? Fail("memory_slope", "Insufficient samples for slope calculation", null, cfg.MaxMemorySlopeBytesPerSecond)
                : slope <= cfg.MaxMemorySlopeBytesPerSecond
                    ? Pass("memory_slope",
                        $"Memory slope {slope / 1024.0:F2} KB/s ≤ {cfg.MaxMemorySlopeBytesPerSecond / 1024.0:F0} KB/s threshold",
                        slope, cfg.MaxMemorySlopeBytesPerSecond)
                    : Fail("memory_slope",
                        $"Memory slope {slope / 1024.0:F2} KB/s exceeds {cfg.MaxMemorySlopeBytesPerSecond / 1024.0:F0} KB/s threshold",
                        slope, cfg.MaxMemorySlopeBytesPerSecond));
        }
        else
        {
            results.Add(Fail("memory_slope", $"Only {soakSamples.Count} post-warmup samples; need ≥ 2", null, cfg.MaxMemorySlopeBytesPerSecond));
        }

        // ── Gate 4: Memory plateau delta ──────────────────────────────────────
        if (soakSamples.Count >= 2)
        {
            var memValues = soakSamples.Select(s => (double)s.WorkingSetBytes).ToList();
            var delta = PlateauDelta(memValues, cfg.PlateauWindowFraction);
            results.Add(delta is null
                ? Fail("memory_plateau", "Insufficient samples for plateau comparison", null, (double)cfg.MaxMemoryPlateauDeltaBytes)
                : delta <= cfg.MaxMemoryPlateauDeltaBytes
                    ? Pass("memory_plateau",
                        $"Plateau delta {delta / (1024 * 1024.0):F1} MB ≤ {cfg.MaxMemoryPlateauDeltaBytes / (1024 * 1024.0):F0} MB threshold",
                        delta, cfg.MaxMemoryPlateauDeltaBytes)
                    : Fail("memory_plateau",
                        $"Plateau delta {delta / (1024 * 1024.0):F1} MB exceeds {cfg.MaxMemoryPlateauDeltaBytes / (1024 * 1024.0):F0} MB threshold",
                        delta, cfg.MaxMemoryPlateauDeltaBytes));
        }
        else
        {
            results.Add(Fail("memory_plateau", $"Only {soakSamples.Count} post-warmup samples", null, (double)cfg.MaxMemoryPlateauDeltaBytes));
        }

        // ── Gate 5: Handle count slope (Windows only) ─────────────────────────
        var handleSamples = soakSamples.Where(s => s.HandleCountSupported).ToList();
        if (handleSamples.Count == 0)
        {
            results.Add(new SoakGateResult("handle_slope", Passed: true,
                Status: "UNSUPPORTED",
                Detail: "Process handle count not supported on this OS; gate skipped."));
        }
        else if (handleSamples.Count >= 2)
        {
            var hPoints = handleSamples
                .Select(s => (X: s.ElapsedMs / 1000.0, Y: (double)s.ProcessHandleCount))
                .ToList();
            var slope = Slope(hPoints);
            results.Add(slope is null
                ? Fail("handle_slope", "Insufficient handle samples", null, cfg.MaxHandleSlopePerSecond)
                : slope <= cfg.MaxHandleSlopePerSecond
                    ? Pass("handle_slope", $"Handle slope {slope:F5} /s ≤ {cfg.MaxHandleSlopePerSecond:F3} /s threshold", slope, cfg.MaxHandleSlopePerSecond)
                    : Fail("handle_slope", $"Handle slope {slope:F5} /s exceeds {cfg.MaxHandleSlopePerSecond:F3} /s threshold", slope, cfg.MaxHandleSlopePerSecond));
        }
        else
        {
            results.Add(Fail("handle_slope", "Insufficient supported handle samples", null, cfg.MaxHandleSlopePerSecond));
        }

        // ── Gate 6: Thread count slope ────────────────────────────────────────
        if (soakSamples.Count >= 2)
        {
            var tPoints = soakSamples
                .Select(s => (X: s.ElapsedMs / 1000.0, Y: (double)s.ProcessThreadCount))
                .ToList();
            var slope = Slope(tPoints);
            results.Add(slope is null
                ? Fail("thread_slope", "Insufficient samples", null, cfg.MaxThreadSlopePerSecond)
                : slope <= cfg.MaxThreadSlopePerSecond
                    ? Pass("thread_slope", $"Thread slope {slope:F5} /s ≤ {cfg.MaxThreadSlopePerSecond:F3} /s threshold", slope, cfg.MaxThreadSlopePerSecond)
                    : Fail("thread_slope", $"Thread slope {slope:F5} /s exceeds {cfg.MaxThreadSlopePerSecond:F3} /s threshold", slope, cfg.MaxThreadSlopePerSecond));
        }
        else
        {
            results.Add(Fail("thread_slope", $"Only {soakSamples.Count} post-warmup samples", null, cfg.MaxThreadSlopePerSecond));
        }

        // ── Gate 7: Worker upper bound ────────────────────────────────────────
        if (cfg.EnforceWorkerUpperBound && soakSamples.Count > 0)
        {
            var violations = soakSamples
                .Where(s => s.PoolStatsAvailable && s.PoolTotal > s.PoolMax)
                .ToList();
            results.Add(violations.Count == 0
                ? Pass("worker_upper_bound", $"TotalWorkers ≤ MaxPoolSize at all {soakSamples.Count} soak samples", 0, 0)
                : Fail("worker_upper_bound",
                    $"{violations.Count} sample(s) had TotalWorkers > MaxPoolSize. " +
                    $"First violation: elapsed={violations[0].ElapsedMs}ms total={violations[0].PoolTotal} max={violations[0].PoolMax}",
                    violations.Count, 0));
        }
        else if (!cfg.EnforceWorkerUpperBound)
        {
            results.Add(new SoakGateResult("worker_upper_bound", Passed: true, Status: "SKIP", Detail: "EnforceWorkerUpperBound=false"));
        }
        else
        {
            results.Add(Fail("worker_upper_bound", "No soak samples to evaluate", null, 0));
        }

        // ── Gate 8: Pool replenishment recovery ───────────────────────────────
        results.Add(EvaluateReplenishmentRecovery(soakSamples, cfg));

        // ── Gate 9: Stable end state ──────────────────────────────────────────
        results.Add(EvaluateStableEndState(soakSamples, cfg));

        // ── Gate 10: Server did not crash ─────────────────────────────────────
        var serverCrashSamples = soakSamples.Where(s => s.Note != null && s.Note.Contains("SERVER_CRASH")).ToList();
        results.Add(serverCrashSamples.Count == 0
            ? Pass("server_stability", "No server crash detected during soak run", 0, 0)
            : Fail("server_stability", $"Server crash detected at {serverCrashSamples[0].ElapsedMs}ms", 1, 0));

        return results.AsReadOnly();
    }

    private static SoakGateResult EvaluateReplenishmentRecovery(
        IReadOnlyList<SoakSample> soakSamples,
        SoakConfig cfg)
    {
        var poolSamples = soakSamples.Where(s => s.PoolStatsAvailable).ToList();
        if (poolSamples.Count == 0)
            return new SoakGateResult("replenishment_recovery", Passed: true, Status: "SKIP", Detail: "No pool stats available to evaluate");

        // Find samples where workers fell below MinPoolSize after warmup.
        for (var i = 0; i < poolSamples.Count; i++)
        {
            var s = poolSamples[i];
            if (s.PoolTotal < s.PoolMin)
            {
                // Find recovery: look ahead for PoolTotal >= PoolMin
                var recoveryLimit = Math.Min(poolSamples.Count, i + cfg.ReplenishmentRecoverySamples + 1);
                var recovered = false;
                for (var j = i + 1; j < recoveryLimit; j++)
                {
                    if (poolSamples[j].PoolTotal >= poolSamples[j].PoolMin)
                    {
                        recovered = true;
                        break;
                    }
                }

                if (!recovered)
                {
                    return Fail("replenishment_recovery",
                        $"Pool had TotalWorkers={s.PoolTotal} < MinPoolSize={s.PoolMin} at elapsed={s.ElapsedMs}ms " +
                        $"and did not recover within {cfg.ReplenishmentRecoverySamples} samples " +
                        $"({cfg.ReplenishmentRecoverySamples * cfg.SampleInterval.TotalSeconds:F0}s)",
                        s.PoolTotal, s.PoolMin);
                }
            }
        }

        return Pass("replenishment_recovery",
            $"Pool recovered to ≥ MinPoolSize within {cfg.ReplenishmentRecoverySamples} samples after every dip", 0, 0);
    }

    private static SoakGateResult EvaluateStableEndState(
        IReadOnlyList<SoakSample> soakSamples,
        SoakConfig cfg)
    {
        var poolSamples = soakSamples.Where(s => s.PoolStatsAvailable).ToList();
        if (poolSamples.Count < cfg.StableEndSamples)
            return new SoakGateResult("stable_end_state", Passed: true, Status: "SKIP",
                Detail: $"Only {poolSamples.Count} pool samples; need {cfg.StableEndSamples} for end-state check");

        var endSamples = poolSamples.TakeLast(cfg.StableEndSamples).ToList();
        var unstable = endSamples
            .Where(s => (s.PoolWarm + s.PoolLeased) < s.PoolMin)
            .ToList();

        return unstable.Count == 0
            ? Pass("stable_end_state",
                $"Last {cfg.StableEndSamples} samples all have WarmWorkers+LeasedWorkers ≥ MinPoolSize", 0, 0)
            : Fail("stable_end_state",
                $"{unstable.Count}/{cfg.StableEndSamples} end samples had Warm+Leased < MinPoolSize. " +
                $"Last sample: warm={endSamples.Last().PoolWarm} leased={endSamples.Last().PoolLeased} min={endSamples.Last().PoolMin}",
                unstable.Count, 0);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static SoakGateResult Pass(string gate, string detail, double? measured, double? threshold) =>
        new(gate, Passed: true, Status: "PASS", Detail: detail, MeasuredValue: measured, Threshold: threshold);

    private static SoakGateResult Fail(string gate, string detail, double? measured, double? threshold) =>
        new(gate, Passed: false, Status: "FAIL", Detail: detail, MeasuredValue: measured, Threshold: threshold);
}
