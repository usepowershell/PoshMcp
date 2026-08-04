using System;
using System.Collections.Generic;
using System.Linq;
using PoshMcp.Tests.Soak;
using Xunit;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="SoakAnalyzer"/> — fast, deterministic, no external processes.
///
/// <para>Covers the pure statistical helpers (OLS slope, mean, quantile, window floors, plateau
/// delta, error rate) with NaN/Infinity/boundary handling, and the phase-aware gate model:
/// load-duration, error rate, PS-execution proof, memory slope/plateau, the floor-based handle
/// stability gate (bounded-sawtooth-flat-floor passes; ratcheting-floor leak fails), the
/// cooldown-plateau recovery gate, thread slope, pool coverage, worker bounds, replenishment
/// recovery, stable end state, server stability, and sample integrity.</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class SoakAnalyzerTests
{
    private const int Interval = 30_000; // 30s in ms

    // ═══ Slope ══════════════════════════════════════════════════════════════

    [Fact]
    public void Slope_FlatSeries_ReturnsZero()
    {
        var pts = new[] { (0.0, 100.0), (1.0, 100.0), (2.0, 100.0) }.ToList<(double, double)>();
        Assert.Equal(0.0, SoakAnalyzer.Slope(pts)!.Value, precision: 6);
    }

    [Fact]
    public void Slope_Increasing_ReturnsExactSlope()
    {
        var pts = Enumerable.Range(0, 10).Select(i => ((double)i, 2.0 * i + 5.0)).ToList();
        Assert.Equal(2.0, SoakAnalyzer.Slope(pts)!.Value, precision: 6);
    }

    [Fact]
    public void Slope_Decreasing_ReturnsNegativeSlope()
    {
        var pts = Enumerable.Range(0, 5).Select(i => ((double)i, 50.0 - (3.0 * i))).ToList();
        Assert.Equal(-3.0, SoakAnalyzer.Slope(pts)!.Value, precision: 6);
    }

    [Fact]
    public void Slope_SinglePoint_ReturnsNull() =>
        Assert.Null(SoakAnalyzer.Slope(new List<(double, double)> { (0.0, 42.0) }));

    [Fact]
    public void Slope_NaN_Throws() =>
        Assert.Throws<ArgumentException>(() => SoakAnalyzer.Slope(new[] { (0.0, 1.0), (1.0, double.NaN) }.ToList<(double, double)>()));

    [Fact]
    public void Slope_Infinity_Throws() =>
        Assert.Throws<ArgumentException>(() => SoakAnalyzer.Slope(new[] { (0.0, 1.0), (double.PositiveInfinity, 2.0) }.ToList<(double, double)>()));

    // ═══ Mean ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Mean_Basic() => Assert.Equal(2.0, SoakAnalyzer.Mean(new[] { 1.0, 2.0, 3.0 })!.Value, precision: 9);

    [Fact]
    public void Mean_Empty_ReturnsNull() => Assert.Null(SoakAnalyzer.Mean(Array.Empty<double>()));

    [Fact]
    public void Mean_NaN_Throws() => Assert.Throws<ArgumentException>(() => SoakAnalyzer.Mean(new[] { 1.0, double.NaN }));

    // ═══ Quantile ═══════════════════════════════════════════════════════════

    [Fact]
    public void Quantile_Min_Max_Median()
    {
        var v = new[] { 10.0, 20.0, 30.0, 40.0, 50.0 };
        Assert.Equal(10.0, SoakAnalyzer.Quantile(v, 0.0)!.Value, precision: 9);
        Assert.Equal(50.0, SoakAnalyzer.Quantile(v, 1.0)!.Value, precision: 9);
        Assert.Equal(30.0, SoakAnalyzer.Quantile(v, 0.5)!.Value, precision: 9);
    }

    [Fact]
    public void Quantile_LinearInterpolation()
    {
        // 10 values 0..9, p10 → rank 0.9 → interpolate sorted[0]=0 and sorted[1]=1 → 0.9
        var v = Enumerable.Range(0, 10).Select(i => (double)i).ToArray();
        Assert.Equal(0.9, SoakAnalyzer.Quantile(v, 0.10)!.Value, precision: 9);
    }

    [Fact]
    public void Quantile_UnorderedInput_SortsFirst()
    {
        var v = new[] { 50.0, 10.0, 30.0, 20.0, 40.0 };
        Assert.Equal(10.0, SoakAnalyzer.Quantile(v, 0.0)!.Value, precision: 9);
    }

    [Fact]
    public void Quantile_Single() => Assert.Equal(7.0, SoakAnalyzer.Quantile(new[] { 7.0 }, 0.10)!.Value, precision: 9);

    [Fact]
    public void Quantile_Empty_ReturnsNull() => Assert.Null(SoakAnalyzer.Quantile(Array.Empty<double>(), 0.5));

    [Fact]
    public void Quantile_OutOfRange_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SoakAnalyzer.Quantile(new[] { 1.0 }, 1.5));

    [Fact]
    public void Quantile_NaN_Throws() =>
        Assert.Throws<ArgumentException>(() => SoakAnalyzer.Quantile(new[] { 1.0, double.NaN }, 0.5));

    // ═══ WindowFloors ═══════════════════════════════════════════════════════

    [Fact]
    public void WindowFloors_PartitionsByWindowAndTakesQuantileFloor()
    {
        // 20 points, 30s spacing, 300s (5-min) window → 2 windows of 10 points each.
        // Window k values: base(1000+…) so p10 is deterministic.
        var pts = new List<(double, double)>();
        for (var j = 0; j < 20; j++)
            pts.Add((j * 30.0, 1000.0 + (j % 10) * 100.0)); // each window: 1000..1900

        var floors = SoakAnalyzer.WindowFloors(pts, windowSeconds: 300, quantile: 0.10);
        Assert.Equal(2, floors.Count);
        // p10 of {1000..1900} = 1000 + 0.9*100 = 1090 for both windows (flat floor)
        Assert.Equal(1090.0, floors[0].Floor, precision: 6);
        Assert.Equal(1090.0, floors[1].Floor, precision: 6);
    }

    [Fact]
    public void WindowFloors_IrregularSpacing_UsesActualElapsedTime()
    {
        // Irregular timestamps but all within two 300s windows; floors still computed correctly.
        var pts = new List<(double, double)>
        {
            (0, 1000), (17, 1000), (140, 1000), (299, 5000), // window 0: floor 1000
            (300, 1000), (401, 1000), (560, 6000),            // window 1: floor 1000
        };
        var floors = SoakAnalyzer.WindowFloors(pts, 300, 0.10);
        Assert.Equal(2, floors.Count);
        Assert.Equal(1000.0, floors[0].Floor, precision: 6);
        Assert.Equal(1000.0, floors[1].Floor, precision: 6);
    }

    // ═══ PlateauDelta / ErrorRate ═══════════════════════════════════════════

    [Fact]
    public void PlateauDelta_Basic()
    {
        var values = Enumerable.Range(0, 100).Select(i => (double)(i < 90 ? 100 : 200)).ToList();
        var delta = SoakAnalyzer.PlateauDelta(values, 0.10);
        Assert.Equal(100.0, delta!.Value, precision: 6);
    }

    [Fact]
    public void PlateauDelta_TooFew_ReturnsNull() =>
        Assert.Null(SoakAnalyzer.PlateauDelta(new[] { 1.0 }, 0.10));

    [Fact]
    public void ErrorRate_Basic() => Assert.Equal(0.01, SoakAnalyzer.ErrorRate(1000, 10)!.Value, precision: 9);

    [Fact]
    public void ErrorRate_ZeroTotal_ReturnsNull() => Assert.Null(SoakAnalyzer.ErrorRate(0, 0));

    // ═══ Full healthy run ═══════════════════════════════════════════════════

    [Fact]
    public void Evaluate_HealthyRun_AllGatesPass()
    {
        var gates = SoakAnalyzer.Evaluate(HealthyRun(), Cfg());
        var failing = gates
            .Where(g => !g.Passed && g.Status is not ("UNSUPPORTED" or "SKIP" or "DIAGNOSTIC"))
            .ToList();
        Assert.True(failing.Count == 0, "Unexpected failing gates: " + string.Join("; ", failing.Select(g => $"{g.Gate}: {g.Detail}")));
    }

    [Fact]
    public void Evaluate_GateContract_ExactNameSet()
    {
        // Locks the gate contract: adding/removing a gate must update this set intentionally.
        var expected = new HashSet<string>
        {
            "sample_integrity", "load_duration", "error_rate", "ps_execution",
            "memory_slope", "memory_plateau", "handle_floor_slope", "handle_cooldown_plateau",
            "handle_amplitude_diagnostic", "thread_slope", "pool_stats_coverage",
            "worker_upper_bound", "replenishment_recovery", "stable_end_state", "server_stability",
        };
        var actual = SoakAnalyzer.Evaluate(HealthyRun(), Cfg()).Select(g => g.Gate).ToHashSet();
        Assert.True(expected.SetEquals(actual),
            $"Gate set drifted. Missing: [{string.Join(",", expected.Except(actual))}] Extra: [{string.Join(",", actual.Except(expected))}]");
    }

    [Fact]
    public void Evaluate_AmplitudeGate_IsDiagnosticOnly()
    {
        var g = SoakAnalyzer.Evaluate(HealthyRun(), Cfg()).Single(x => x.Gate == "handle_amplitude_diagnostic");
        Assert.Equal("DIAGNOSTIC", g.Status);
        Assert.True(g.Passed); // never gates
    }

    // ═══ Core false-positive fix: sawtooth floor vs raw OLS ════════════════

    [Fact]
    public void Evaluate_BoundedSawtoothFlatFloor_HandleGatePasses_EvenThoughRawSlopeIsPositive()
    {
        // Floor is flat at 1000; peaks GROW each window so the raw whole-run OLS is strongly positive
        // (the discredited gate). The floor gate must PASS.
        var run = RunWithLoadHandles((j, win) => 1000.0 + (j % 10 >= 8 ? 500.0 + 100.0 * win : 0.0));

        var rawSlope = SoakAnalyzer.Slope(
            run.Where(s => s.Phase == SoakAnalyzer.PhaseLoad)
               .Select(s => (s.ElapsedMs / 1000.0, (double)s.ProcessHandleCount)).ToList());
        Assert.True(rawSlope!.Value > 0.01, $"Test premise: raw OLS should be positive, was {rawSlope.Value}");

        var floorGate = SoakAnalyzer.Evaluate(run, Cfg()).Single(g => g.Gate == "handle_floor_slope");
        Assert.True(floorGate.Passed, $"Flat-floor sawtooth must pass floor gate: {floorGate.Detail}");
    }

    [Fact]
    public void Evaluate_RatchetingFloor_HandleGateFails()
    {
        // Floor ratchets up 60 handles per 5-min window → real leak → floor gate must FAIL.
        var run = RunWithLoadHandles((j, win) => 1000.0 + 60.0 * win + (j % 10 >= 8 ? 500.0 : 0.0));
        var floorGate = SoakAnalyzer.Evaluate(run, Cfg()).Single(g => g.Gate == "handle_floor_slope");
        Assert.False(floorGate.Passed, $"Ratcheting floor must fail: {floorGate.Detail}");
    }

    // ═══ Cooldown plateau ══════════════════════════════════════════════════

    [Fact]
    public void Evaluate_CooldownFloorNearBaseline_PlateauPasses()
    {
        var g = SoakAnalyzer.Evaluate(HealthyRun(), Cfg()).Single(x => x.Gate == "handle_cooldown_plateau");
        Assert.True(g.Passed, g.Detail);
    }

    [Fact]
    public void Evaluate_CooldownFloorFarAboveBaseline_PlateauFails()
    {
        // Cooldown handles stay 6000 above baseline (~1000) → unrecovered → fail.
        var run = HealthyRun().Select(s => s.Phase == SoakAnalyzer.PhaseCooldown
            ? s with { ProcessHandleCount = 7000 }
            : s).ToList();
        var g = SoakAnalyzer.Evaluate(run, Cfg()).Single(x => x.Gate == "handle_cooldown_plateau");
        Assert.False(g.Passed, g.Detail);
    }

    [Fact]
    public void Evaluate_NoCooldownSamples_PlateauFails()
    {
        var run = HealthyRun().Where(s => s.Phase != SoakAnalyzer.PhaseCooldown).ToList();
        var g = SoakAnalyzer.Evaluate(run, Cfg()).Single(x => x.Gate == "handle_cooldown_plateau");
        Assert.False(g.Passed, g.Detail);
    }

    [Fact]
    public void Evaluate_HandleUnsupported_FloorAndPlateauSkipped()
    {
        var run = HealthyRun().Select(s => s with { HandleCountSupported = false, ProcessHandleCount = -1 }).ToList();
        var gates = SoakAnalyzer.Evaluate(run, Cfg());
        Assert.Equal("UNSUPPORTED", gates.Single(g => g.Gate == "handle_floor_slope").Status);
        Assert.Equal("UNSUPPORTED", gates.Single(g => g.Gate == "handle_cooldown_plateau").Status);
    }

    // ═══ Duration ═══════════════════════════════════════════════════════════

    [Fact]
    public void Evaluate_LoadSpanBelowMinimum_DurationFails()
    {
        var cfg = Cfg() with { MinLoadDuration = TimeSpan.FromMinutes(120) };
        var g = SoakAnalyzer.Evaluate(HealthyRun(), cfg).Single(x => x.Gate == "load_duration");
        Assert.False(g.Passed, g.Detail);
    }

    [Fact]
    public void Evaluate_LoadSpanMeetsMinimum_DurationPasses()
    {
        var g = SoakAnalyzer.Evaluate(HealthyRun(), Cfg()).Single(x => x.Gate == "load_duration");
        Assert.True(g.Passed, g.Detail);
    }

    // ═══ Error rate boundary ════════════════════════════════════════════════

    [Fact]
    public void Evaluate_ErrorRateExactlyAtThreshold_Passes()
    {
        var run = HealthyRun();
        var last = run[^1];
        // Set final cumulative so errors/total == exactly MaxErrorRate (0.001): 10/10000
        run[^1] = last with { TotalRequests = 10_000, ErrorRequests = 10, SuccessRequests = 9_990 };
        var g = SoakAnalyzer.Evaluate(run, Cfg()).Single(x => x.Gate == "error_rate");
        Assert.True(g.Passed, g.Detail);
    }

    [Fact]
    public void Evaluate_ErrorRateJustAboveThreshold_Fails()
    {
        var run = HealthyRun();
        run[^1] = run[^1] with { TotalRequests = 10_000, ErrorRequests = 11, SuccessRequests = 9_989 };
        var g = SoakAnalyzer.Evaluate(run, Cfg()).Single(x => x.Gate == "error_rate");
        Assert.False(g.Passed, g.Detail);
    }

    // ═══ PS execution ═══════════════════════════════════════════════════════

    [Fact]
    public void Evaluate_NoToolsCall_PsExecutionFails()
    {
        var run = HealthyRun();
        run[^1] = run[^1] with { ToolsCallRequests = 0, ToolsCallPsSuccess = 0 };
        var g = SoakAnalyzer.Evaluate(run, Cfg()).Single(x => x.Gate == "ps_execution");
        Assert.False(g.Passed, g.Detail);
    }

    [Fact]
    public void Evaluate_PsSuccessShortfall_PsExecutionFails()
    {
        var run = HealthyRun();
        run[^1] = run[^1] with { ToolsCallRequests = 1000, ToolsCallPsSuccess = 500 };
        var g = SoakAnalyzer.Evaluate(run, Cfg()).Single(x => x.Gate == "ps_execution");
        Assert.False(g.Passed, g.Detail);
    }

    // ═══ Memory ═════════════════════════════════════════════════════════════

    [Fact]
    public void Evaluate_MemoryRamp_SlopeFails()
    {
        var run = HealthyRun();
        for (var i = 0; i < run.Count; i++)
            if (run[i].Phase == SoakAnalyzer.PhaseLoad)
                run[i] = run[i] with { WorkingSetBytes = 100_000_000L + run[i].ElapsedMs * 10_000L }; // ramps fast
        var g = SoakAnalyzer.Evaluate(run, Cfg()).Single(x => x.Gate == "memory_slope");
        Assert.False(g.Passed, g.Detail);
    }

    [Fact]
    public void Evaluate_WarmupMemoryExcludedFromSlope()
    {
        // Warmup has huge memory; load is flat. Slope gate must pass (warmup excluded).
        var run = HealthyRun();
        for (var i = 0; i < run.Count; i++)
            if (run[i].Phase == SoakAnalyzer.PhaseWarmup)
                run[i] = run[i] with { WorkingSetBytes = 9_000_000_000L };
        var g = SoakAnalyzer.Evaluate(run, Cfg()).Single(x => x.Gate == "memory_slope");
        Assert.True(g.Passed, g.Detail);
    }

    // ═══ Pool coverage ══════════════════════════════════════════════════════

    [Fact]
    public void Evaluate_PoolCoverageBelowThreshold_Fails()
    {
        var run = HealthyRun();
        // Blank pool stats on half the load samples.
        var toggle = false;
        for (var i = 0; i < run.Count; i++)
        {
            if (run[i].Phase != SoakAnalyzer.PhaseLoad) continue;
            toggle = !toggle;
            if (toggle) run[i] = run[i] with { PoolStatsAvailable = false };
        }
        var g = SoakAnalyzer.Evaluate(run, Cfg()).Single(x => x.Gate == "pool_stats_coverage");
        Assert.False(g.Passed, g.Detail);
    }

    [Fact]
    public void Evaluate_PoolCoverageFull_Passes()
    {
        var g = SoakAnalyzer.Evaluate(HealthyRun(), Cfg()).Single(x => x.Gate == "pool_stats_coverage");
        Assert.True(g.Passed, g.Detail);
    }

    // ═══ Worker/replenishment/end-state/stability ═══════════════════════════

    [Fact]
    public void Evaluate_WorkersExceedMax_UpperBoundFails()
    {
        var run = HealthyRun();
        var idx = run.FindIndex(s => s.Phase == SoakAnalyzer.PhaseLoad);
        run[idx] = run[idx] with { PoolTotal = 9, PoolMax = 6 };
        var g = SoakAnalyzer.Evaluate(run, Cfg()).Single(x => x.Gate == "worker_upper_bound");
        Assert.False(g.Passed, g.Detail);
    }

    [Fact]
    public void Evaluate_PoolDropsAndNeverRecovers_ReplenishmentFails()
    {
        var cfg = Cfg() with { ReplenishmentRecoverySamples = 2 };
        var run = HealthyRun();
        var loadIdx = run.Select((s, i) => (s, i)).Where(t => t.s.Phase == SoakAnalyzer.PhaseLoad).Select(t => t.i).ToList();
        for (var k = 10; k < loadIdx.Count; k++) // permanent drop to 0 from the 10th load sample onward
            run[loadIdx[k]] = run[loadIdx[k]] with { PoolTotal = 0, PoolWarm = 0, PoolLeased = 0 };
        var g = SoakAnalyzer.Evaluate(run, cfg).Single(x => x.Gate == "replenishment_recovery");
        Assert.False(g.Passed, g.Detail);
    }

    [Fact]
    public void Evaluate_EndStateWarmPlusLeasedBelowMin_Fails()
    {
        var run = HealthyRun();
        var loadIdx = run.Select((s, i) => (s, i)).Where(t => t.s.Phase == SoakAnalyzer.PhaseLoad).Select(t => t.i).ToList();
        foreach (var k in loadIdx.TakeLast(5))
            run[k] = run[k] with { PoolWarm = 0, PoolLeased = 0 };
        var g = SoakAnalyzer.Evaluate(run, Cfg()).Single(x => x.Gate == "stable_end_state");
        Assert.False(g.Passed, g.Detail);
    }

    [Fact]
    public void Evaluate_ServerCrashNote_StabilityFails()
    {
        var run = HealthyRun();
        run[run.Count / 2] = run[run.Count / 2] with { Note = "SERVER_CRASH exit=139" };
        var g = SoakAnalyzer.Evaluate(run, Cfg()).Single(x => x.Gate == "server_stability");
        Assert.False(g.Passed, g.Detail);
    }

    // ═══ Sample integrity ═══════════════════════════════════════════════════

    [Fact]
    public void Evaluate_DuplicateSampleKey_IntegrityFails()
    {
        var run = HealthyRun();
        // Duplicate an existing (phase, elapsed) key.
        run.Add(run[5] with { });
        var g = SoakAnalyzer.Evaluate(run, Cfg()).Single(x => x.Gate == "sample_integrity");
        Assert.False(g.Passed, g.Detail);
    }

    [Fact]
    public void Evaluate_OutOfOrderInput_StillEvaluatesCorrectly()
    {
        // Reverse order input; analyzer sorts by elapsed → gates unaffected.
        var run = HealthyRun();
        run.Reverse();
        var failing = SoakAnalyzer.Evaluate(run, Cfg())
            .Where(g => !g.Passed && g.Status is not ("UNSUPPORTED" or "SKIP" or "DIAGNOSTIC"))
            .ToList();
        Assert.True(failing.Count == 0, "Out-of-order input must not change outcome: " + string.Join("; ", failing.Select(g => g.Gate)));
    }

    // ═══ Builders ═══════════════════════════════════════════════════════════

    private static SoakConfig Cfg() => new(); // release-contract defaults

    /// <summary>
    /// Full healthy run: baseline (6) + load (124 ≈ 61.5 min) + cooldown (10). Flat memory/threads,
    /// flat handle floor (bounded sawtooth), full pool coverage, zero errors, intended mix present.
    /// </summary>
    private static List<SoakSample> HealthyRun() =>
        RunWithLoadHandles((j, win) => 1000.0 + (j % 10 >= 8 ? 500.0 : 0.0));

    /// <summary>
    /// Builds a run whose LOAD-phase handle count is produced by <paramref name="loadHandle"/>
    /// (j = within-load sample index, win = 5-min window index). Baseline and cooldown handles are
    /// flat at 1000. All other metrics are healthy.
    /// </summary>
    private static List<SoakSample> RunWithLoadHandles(Func<int, int, double> loadHandle)
    {
        const int baselineCount = 6;
        const int loadCount = 124; // span = 123 × 30s = 61.5 min ≥ 60
        const int cooldownCount = 10;

        var samples = new List<SoakSample>();
        var g = 0;
        long req = 0;

        SoakSample Make(string phase, int handles, long elapsed, bool traffic)
        {
            if (traffic) req += 100;
            return new SoakSample
            {
                Timestamp = DateTimeOffset.UnixEpoch.AddMilliseconds(elapsed),
                ElapsedMs = elapsed,
                Phase = phase,
                TotalRequests = req,
                SuccessRequests = req,
                ErrorRequests = 0,
                InitializeRequests = req / 10,
                ToolsListRequests = req / 2,
                ToolsCallRequests = (req * 4) / 10,
                ToolsCallPsSuccess = (req * 4) / 10,
                WorkingSetBytes = 100_000_000L,
                ProcessHandleCount = handles,
                HandleCountSupported = true,
                ProcessThreadCount = 20,
                PoolWarm = 3,
                PoolLeased = 1,
                PoolTotal = 4,
                PoolMin = 2,
                PoolMax = 6,
                PoolIsStarted = true,
                PoolStatsAvailable = true,
            };
        }

        for (var i = 0; i < baselineCount; i++, g++)
            samples.Add(Make(SoakAnalyzer.PhaseBaseline, 1000, (long)g * Interval, traffic: false));

        for (var j = 0; j < loadCount; j++, g++)
        {
            var win = j / 10;
            samples.Add(Make(SoakAnalyzer.PhaseLoad, (int)loadHandle(j, win), (long)g * Interval, traffic: true));
        }

        for (var i = 0; i < cooldownCount; i++, g++)
            samples.Add(Make(SoakAnalyzer.PhaseCooldown, 1000, (long)g * Interval, traffic: false));

        return samples;
    }
}
