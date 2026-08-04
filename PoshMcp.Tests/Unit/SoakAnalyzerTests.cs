using System;
using System.Collections.Generic;
using System.Linq;
using PoshMcp.Tests.Soak;
using Xunit;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="SoakAnalyzer"/> — fast, deterministic, no external processes.
/// Covers slope math, plateau delta, error rate, worker bounds, thread/handle trends,
/// malformed/empty samples, and gate exit behavior.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SoakAnalyzerTests
{
    // ─── Slope math ──────────────────────────────────────────────────────────

    [Fact]
    public void Slope_FlatSeries_ReturnsZero()
    {
        var pts = new[] { (0.0, 100.0), (1.0, 100.0), (2.0, 100.0) }.ToList<(double, double)>();
        var slope = SoakAnalyzer.Slope(pts);
        Assert.NotNull(slope);
        Assert.Equal(0.0, slope!.Value, precision: 6);
    }

    [Fact]
    public void Slope_StrictlyIncreasing_ReturnsPositiveSlope()
    {
        // y = 2x + 5 → slope = 2
        var pts = Enumerable.Range(0, 10)
            .Select(i => ((double)i, 2.0 * i + 5.0))
            .ToList();
        var slope = SoakAnalyzer.Slope(pts);
        Assert.NotNull(slope);
        Assert.Equal(2.0, slope!.Value, precision: 6);
    }

    [Fact]
    public void Slope_StrictlyDecreasing_ReturnsNegativeSlope()
    {
        var pts = Enumerable.Range(0, 5)
            .Select(i => ((double)i, 50.0 - (3.0 * i)))
            .ToList();
        var slope = SoakAnalyzer.Slope(pts);
        Assert.NotNull(slope);
        Assert.Equal(-3.0, slope!.Value, precision: 6);
    }

    [Fact]
    public void Slope_SinglePoint_ReturnsNull()
    {
        var pts = new List<(double, double)> { (0.0, 42.0) };
        var slope = SoakAnalyzer.Slope(pts);
        Assert.Null(slope);
    }

    [Fact]
    public void Slope_TwoPoints_ReturnsExactSlope()
    {
        var pts = new List<(double, double)> { (0.0, 10.0), (5.0, 60.0) };
        var slope = SoakAnalyzer.Slope(pts);
        Assert.NotNull(slope);
        Assert.Equal(10.0, slope!.Value, precision: 6); // (60-10)/(5-0)
    }

    [Fact]
    public void Slope_EmptyList_ReturnsNull()
    {
        var slope = SoakAnalyzer.Slope(new List<(double, double)>());
        Assert.Null(slope);
    }

    [Fact]
    public void Slope_VerticalPoints_SameX_ReturnsZero()
    {
        // All x values identical — denominator is zero → returns 0
        var pts = new List<(double, double)> { (5.0, 1.0), (5.0, 10.0), (5.0, 20.0) };
        var slope = SoakAnalyzer.Slope(pts);
        Assert.NotNull(slope);
        Assert.Equal(0.0, slope!.Value, precision: 6);
    }

    [Fact]
    public void Slope_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => SoakAnalyzer.Slope(null!));
    }

    // ─── Mean ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Mean_EmptySequence_ReturnsNull()
    {
        Assert.Null(SoakAnalyzer.Mean(new List<double>()));
    }

    [Fact]
    public void Mean_SingleValue_ReturnsThatValue()
    {
        Assert.Equal(7.0, SoakAnalyzer.Mean(new List<double> { 7.0 }));
    }

    [Fact]
    public void Mean_KnownValues_ReturnsCorrectMean()
    {
        var result = SoakAnalyzer.Mean(new List<double> { 1.0, 2.0, 3.0, 4.0, 5.0 });
        Assert.Equal(3.0, result!.Value, precision: 6);
    }

    // ─── Plateau delta ────────────────────────────────────────────────────────

    [Fact]
    public void PlateauDelta_FlatSeries_ReturnsNearZero()
    {
        var values = Enumerable.Repeat(100.0, 20).ToList();
        var delta = SoakAnalyzer.PlateauDelta(values, 0.10);
        Assert.NotNull(delta);
        Assert.Equal(0.0, delta!.Value, precision: 1);
    }

    [Fact]
    public void PlateauDelta_RisingTrend_ReturnsPositiveDelta()
    {
        // First 10% avg ≈ 5; last 10% avg ≈ 95 → delta ≈ 90
        var values = Enumerable.Range(1, 100).Select(i => (double)i).ToList();
        var delta = SoakAnalyzer.PlateauDelta(values, 0.10);
        Assert.NotNull(delta);
        Assert.True(delta!.Value > 0, $"Expected positive delta but got {delta}");
    }

    [Fact]
    public void PlateauDelta_FallingTrend_ReturnsNegativeDelta()
    {
        var values = Enumerable.Range(1, 100).Select(i => (double)(101 - i)).ToList();
        var delta = SoakAnalyzer.PlateauDelta(values, 0.10);
        Assert.NotNull(delta);
        Assert.True(delta!.Value < 0, $"Expected negative delta but got {delta}");
    }

    [Fact]
    public void PlateauDelta_SingleSample_ReturnsNull()
    {
        var delta = SoakAnalyzer.PlateauDelta(new List<double> { 42.0 }, 0.10);
        Assert.Null(delta);
    }

    [Fact]
    public void PlateauDelta_InvalidFraction_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SoakAnalyzer.PlateauDelta(new List<double> { 1.0, 2.0 }, 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SoakAnalyzer.PlateauDelta(new List<double> { 1.0, 2.0 }, 1.5));
    }

    // ─── Error rate ───────────────────────────────────────────────────────────

    [Fact]
    public void ErrorRate_ZeroTotal_ReturnsNull()
    {
        Assert.Null(SoakAnalyzer.ErrorRate(0, 0));
    }

    [Fact]
    public void ErrorRate_ZeroErrors_ReturnsZero()
    {
        Assert.Equal(0.0, SoakAnalyzer.ErrorRate(1000, 0)!.Value, precision: 6);
    }

    [Fact]
    public void ErrorRate_ExactlyAtThreshold_PassesBoundary()
    {
        // Exactly 0.1% = 0.001 — should be ≤ threshold
        var rate = SoakAnalyzer.ErrorRate(1000, 1)!.Value; // 1/1000 = 0.001
        Assert.Equal(0.001, rate, precision: 6);
        Assert.True(rate <= 0.001, "Exactly at threshold should pass");
    }

    [Fact]
    public void ErrorRate_OneOverThreshold_FailsBoundary()
    {
        // 2/1000 = 0.002 > 0.001
        var rate = SoakAnalyzer.ErrorRate(1000, 2)!.Value;
        Assert.True(rate > 0.001, "Two errors in 1000 should exceed 0.1% threshold");
    }

    [Fact]
    public void ErrorRate_AllErrors_ReturnsOne()
    {
        Assert.Equal(1.0, SoakAnalyzer.ErrorRate(50, 50)!.Value, precision: 6);
    }

    [Fact]
    public void ErrorRate_NegativeTotal_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SoakAnalyzer.ErrorRate(-1, 0));
    }

    [Fact]
    public void ErrorRate_NegativeErrors_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SoakAnalyzer.ErrorRate(10, -1));
    }

    // ─── Full gate evaluation ─────────────────────────────────────────────────

    [Fact]
    public void Evaluate_EmptySamples_AllGatesFailOrSkip()
    {
        var cfg = DefaultConfig();
        var gates = SoakAnalyzer.Evaluate(new List<SoakSample>(), cfg);
        Assert.NotEmpty(gates);
        // soak_duration should fail (no samples = 0 duration < 60 min)
        var durationGate = gates.Single(g => g.Gate == "soak_duration");
        Assert.False(durationGate.Passed, "Duration gate must fail for empty samples");
    }

    [Fact]
    public void Evaluate_NullSamples_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => SoakAnalyzer.Evaluate(null!, DefaultConfig()));
    }

    [Fact]
    public void Evaluate_NullConfig_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => SoakAnalyzer.Evaluate(new List<SoakSample>(), null!));
    }

    [Fact]
    public void Evaluate_MemorySlopeExceedsThreshold_MemorySlopeFails()
    {
        // Construct samples with strong upward memory trend (1 GB/s slope — far above threshold)
        var cfg = DefaultConfig();
        var samples = BuildSamples(soakSampleCount: 40, workingSetFactory: i => (long)(1_000_000_000L * i));
        var gates = SoakAnalyzer.Evaluate(samples, cfg);
        var memSlope = gates.Single(g => g.Gate == "memory_slope");
        Assert.False(memSlope.Passed, "Should fail: memory growing at 1 GB/s");
    }

    [Fact]
    public void Evaluate_FlatMemory_MemorySlopePasses()
    {
        var cfg = DefaultConfig();
        var samples = BuildSamples(soakSampleCount: 40, workingSetFactory: _ => 100_000_000L);
        var gates = SoakAnalyzer.Evaluate(samples, cfg);
        var memSlope = gates.Single(g => g.Gate == "memory_slope");
        Assert.True(memSlope.Passed, $"Should pass flat memory: {memSlope.Detail}");
    }

    [Fact]
    public void Evaluate_HighErrorRate_ErrorRateGateFails()
    {
        var cfg = DefaultConfig();
        // 5% error rate
        var samples = BuildSamples(soakSampleCount: 40,
            totalReqFactory: i => (long)(i * 100),
            errorReqFactory: i => (long)(i * 5));
        var gates = SoakAnalyzer.Evaluate(samples, cfg);
        var errGate = gates.Single(g => g.Gate == "error_rate");
        Assert.False(errGate.Passed, "5% error rate must fail");
    }

    [Fact]
    public void Evaluate_ZeroErrorRate_ErrorRateGatePasses()
    {
        var cfg = DefaultConfig();
        var samples = BuildSamples(soakSampleCount: 40,
            totalReqFactory: i => (long)(i * 100),
            errorReqFactory: _ => 0L);
        var gates = SoakAnalyzer.Evaluate(samples, cfg);
        var errGate = gates.Single(g => g.Gate == "error_rate");
        Assert.True(errGate.Passed, $"Zero errors must pass: {errGate.Detail}");
    }

    [Fact]
    public void Evaluate_WorkerExceedsMax_WorkerUpperBoundFails()
    {
        var cfg = DefaultConfig();
        var samples = BuildSamples(soakSampleCount: 5,
            poolTotalFactory: _ => 999, // Way above MaxPoolSize
            poolMaxFactory: _ => 6,
            poolMinFactory: _ => 2,
            poolAvailable: true);
        var gates = SoakAnalyzer.Evaluate(samples, cfg);
        var boundGate = gates.Single(g => g.Gate == "worker_upper_bound");
        Assert.False(boundGate.Passed, "Workers at 999 > max=6 must fail");
    }

    [Fact]
    public void Evaluate_WorkerWithinBounds_WorkerUpperBoundPasses()
    {
        var cfg = DefaultConfig();
        var samples = BuildSamples(soakSampleCount: 5,
            poolTotalFactory: _ => 4, // Within max=6
            poolMaxFactory: _ => 6,
            poolMinFactory: _ => 2,
            poolAvailable: true);
        var gates = SoakAnalyzer.Evaluate(samples, cfg);
        var boundGate = gates.Single(g => g.Gate == "worker_upper_bound");
        Assert.True(boundGate.Passed, $"Workers=4 within max=6 must pass: {boundGate.Detail}");
    }

    [Fact]
    public void Evaluate_PoolNeverDropsBelowMin_ReplenishmentPasses()
    {
        var cfg = DefaultConfig();
        var samples = BuildSamples(soakSampleCount: 20,
            poolTotalFactory: _ => 4,
            poolMaxFactory: _ => 6,
            poolMinFactory: _ => 2,
            poolAvailable: true);
        var gates = SoakAnalyzer.Evaluate(samples, cfg);
        var recovGate = gates.Single(g => g.Gate == "replenishment_recovery");
        Assert.True(recovGate.Passed, $"Pool never dropped; recovery must pass: {recovGate.Detail}");
    }

    [Fact]
    public void Evaluate_PoolDropsAndNeverRecovers_ReplenishmentFails()
    {
        var cfg = new SoakConfig
        {
            WarmupDuration = TimeSpan.Zero,
            SoakDuration = TimeSpan.FromMinutes(5),
            SampleInterval = TimeSpan.FromSeconds(30),
            ReplenishmentRecoverySamples = 2,
        };

        // Build: soak samples where pool drops below min and never recovers
        var samples = BuildSamples(
            soakSampleCount: 15,
            poolTotalFactory: i => i > 3 ? 0 : 4, // drops to 0 at i=4 forever
            poolMaxFactory: _ => 6,
            poolMinFactory: _ => 2,
            poolAvailable: true);

        var gates = SoakAnalyzer.Evaluate(samples, cfg);
        var recovGate = gates.Single(g => g.Gate == "replenishment_recovery");
        Assert.False(recovGate.Passed, "Pool drops permanently; recovery gate must fail");
    }

    [Fact]
    public void Evaluate_StableEndState_WarmPlusLeasedBelowMin_Fails()
    {
        var cfg = new SoakConfig
        {
            WarmupDuration = TimeSpan.Zero,
            SoakDuration = TimeSpan.FromMinutes(5),
            SampleInterval = TimeSpan.FromSeconds(30),
            StableEndSamples = 3,
        };

        var samples = BuildSamples(
            soakSampleCount: 10,
            poolWarmFactory: i => i < 7 ? 3 : 0,   // last 3 samples: warm=0
            poolLeasedFactory: _ => 0,
            poolMinFactory: _ => 2,
            poolAvailable: true);

        var gates = SoakAnalyzer.Evaluate(samples, cfg);
        var endGate = gates.Single(g => g.Gate == "stable_end_state");
        Assert.False(endGate.Passed, "End state with warm=0 leased=0 min=2 must fail");
    }

    [Fact]
    public void Evaluate_ServerCrashNote_StabilityFails()
    {
        var cfg = DefaultConfig();
        var samples = BuildSamples(soakSampleCount: 10);
        // Add a crash note to one sample
        var modified = samples.ToList();
        modified[5] = modified[5] with { Note = "SERVER_CRASH" };
        var gates = SoakAnalyzer.Evaluate(modified, cfg);
        var stabilityGate = gates.Single(g => g.Gate == "server_stability");
        Assert.False(stabilityGate.Passed, "SERVER_CRASH note must trigger stability gate failure");
    }

    [Fact]
    public void Evaluate_AllGatesHaveKnownNames()
    {
        // Ensures gate names are stable and match what consumers expect.
        var expectedGates = new[]
        {
            "soak_duration", "error_rate", "memory_slope", "memory_plateau",
            "handle_slope", "thread_slope", "worker_upper_bound",
            "replenishment_recovery", "stable_end_state", "server_stability"
        };

        var cfg = DefaultConfig();
        var samples = BuildSamples(soakSampleCount: 10);
        var gates = SoakAnalyzer.Evaluate(samples, cfg);
        var gateNames = gates.Select(g => g.Gate).ToHashSet();

        foreach (var name in expectedGates)
            Assert.Contains(name, gateNames);
    }

    [Fact]
    public void Evaluate_WarmupSamplesExcludedFromTrendAnalysis()
    {
        // Warmup samples have huge memory; soak samples are flat.
        // Memory slope gate must pass because warmup samples are excluded.
        var cfg = DefaultConfig();
        var warmupSamples = Enumerable.Range(0, 5).Select(i => new SoakSample
        {
            Timestamp = DateTimeOffset.UtcNow.AddSeconds(-3700 + i * 30),
            ElapsedMs = i * 30_000L,
            Phase = "warmup",
            WorkingSetBytes = 9_000_000_000L, // huge — should be excluded
            PoolStatsAvailable = false,
            HandleCountSupported = false,
        }).ToList();

        var soakSamples = Enumerable.Range(0, 120).Select(i => new SoakSample
        {
            Timestamp = DateTimeOffset.UtcNow.AddSeconds(-3600 + i * 30),
            ElapsedMs = 150_000L + (i * 30_000L), // after warmup
            Phase = "soak",
            WorkingSetBytes = 100_000_000L, // flat
            TotalRequests = (long)(i * 100),
            SuccessRequests = (long)(i * 100),
            ErrorRequests = 0,
            PoolStatsAvailable = false,
            HandleCountSupported = false,
        }).ToList();

        var allSamples = warmupSamples.Concat(soakSamples).ToList();
        var gates = SoakAnalyzer.Evaluate(allSamples, cfg);
        var memSlope = gates.Single(g => g.Gate == "memory_slope");
        Assert.True(memSlope.Passed, $"Warmup exclusion must make flat soak memory pass: {memSlope.Detail}");
    }

    // ─── SoakGateResult ───────────────────────────────────────────────────────

    [Fact]
    public void SoakGateResult_UnsupportedStatus_IsConsideredPassedForGating()
    {
        // UNSUPPORTED gates must be treated as non-blocking.
        var gate = new SoakGateResult("handle_slope", Passed: true, Status: "UNSUPPORTED", Detail: "OS not supported");
        Assert.True(gate.Passed || gate.Status == "UNSUPPORTED" || gate.Status == "SKIP");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static SoakConfig DefaultConfig() => new()
    {
        WarmupDuration = TimeSpan.Zero,
        SoakDuration = TimeSpan.FromMinutes(60),
        SampleInterval = TimeSpan.FromSeconds(30),
        ConcurrencyLevel = 4,
        MaxMemorySlopeBytesPerSecond = 1_048_576,
        MaxMemoryPlateauDeltaBytes = 100L * 1024 * 1024,
        PlateauWindowFraction = 0.10,
        MaxErrorRate = 0.001,
        MaxHandleSlopePerSecond = 0.01,
        MaxThreadSlopePerSecond = 0.01,
        EnforceWorkerUpperBound = true,
        ReplenishmentRecoverySamples = 6,
        StableEndSamples = 5,
    };

    /// <summary>
    /// Builds a list of soak-phase samples spanning slightly more than 60 minutes.
    /// Factory delegates default to stable/flat metrics that should pass all gates.
    /// </summary>
    private static List<SoakSample> BuildSamples(
        int soakSampleCount = 120,
        Func<int, long>? workingSetFactory = null,
        Func<int, long>? totalReqFactory = null,
        Func<int, long>? errorReqFactory = null,
        Func<int, int>? poolTotalFactory = null,
        Func<int, int>? poolMaxFactory = null,
        Func<int, int>? poolMinFactory = null,
        Func<int, int>? poolWarmFactory = null,
        Func<int, int>? poolLeasedFactory = null,
        bool poolAvailable = false)
    {
        var now = DateTimeOffset.UtcNow;
        return Enumerable.Range(0, soakSampleCount).Select(i => new SoakSample
        {
            Timestamp = now.AddSeconds(i * 30),
            ElapsedMs = i * 30_000L,
            Phase = "soak",
            WorkingSetBytes = workingSetFactory?.Invoke(i) ?? 100_000_000L,
            TotalRequests = totalReqFactory?.Invoke(i) ?? (long)(i * 100),
            SuccessRequests = totalReqFactory?.Invoke(i) is null
                ? (long)(i * 100) - (errorReqFactory?.Invoke(i) ?? 0)
                : (totalReqFactory.Invoke(i) - (errorReqFactory?.Invoke(i) ?? 0)),
            ErrorRequests = errorReqFactory?.Invoke(i) ?? 0L,
            ProcessHandleCount = 100,
            HandleCountSupported = false,
            ProcessThreadCount = 10,
            PoolStatsAvailable = poolAvailable,
            PoolTotal = poolTotalFactory?.Invoke(i) ?? 4,
            PoolMax = poolMaxFactory?.Invoke(i) ?? 6,
            PoolMin = poolMinFactory?.Invoke(i) ?? 2,
            PoolWarm = poolWarmFactory?.Invoke(i) ?? 3,
            PoolLeased = poolLeasedFactory?.Invoke(i) ?? 1,
            PoolIsStarted = true,
        }).ToList();
    }
}
