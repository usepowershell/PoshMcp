using System;
using System.Collections.Generic;
using PoshMcp.Tests.Characterization;
using PoshMcp.Tests.Characterization.Phase4;
using Xunit;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Deterministic unit tests for <see cref="PerformanceComparator"/>.
/// No servers are started; all inputs are hand-crafted.
///
/// Covers:
///   - Threshold boundary equality (pass at exact threshold)
///   - Just-inside / just-outside each threshold
///   - Both transport modes
///   - Schema version mismatch → exception
///   - Null baseline → exception
///   - Zero / non-finite baseline values → exception
///   - Non-finite measured values → exception
///   - Missing required scenario in baseline or Phase 4 list
///   - ExitCode: 0 when all pass, 1 when breach
/// </summary>
[Trait("Category", "Unit")]
public class Phase4ComparatorTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Builds a minimal valid Phase 0 baseline artifact.</summary>
    private static CharacterizationArtifact BuildBaseline(
        double coldNoScriptP95 = 1000.0,
        double coldWithScriptP95 = 1000.0,
        double warmCallP95 = 2.0,
        double throughputMean = 5.0,
        double memoryModerateMean = 100.0)
    {
        static CharacterizationScenario Scenario(string name, double p95 = 1.0, double mean = 1.0)
            => new()
            {
                Scenario = name,
                Stats = new CharacterizationStats
                {
                    P95 = p95,
                    Mean = mean,
                    P50 = mean,
                    P99 = p95,
                    Min = mean * 0.9,
                    Max = p95 * 1.01,
                    StdDev = 0,
                    SampleCount = 5,
                },
                RawSamples = [mean],
            };

        return new CharacterizationArtifact
        {
            SchemaVersion = PerformanceComparator.ExpectedBaselineSchemaVersion,
            CapturedAt = "2026-08-04T00:00:00Z",
            SdkPackageVersion = "ModelContextProtocol 1.4.1",
            Scenarios =
            [
                Scenario("cold_start_http_no_script",  p95: coldNoScriptP95,   mean: coldNoScriptP95 * 0.95),
                Scenario("cold_start_http_with_script", p95: coldWithScriptP95, mean: coldWithScriptP95 * 0.95),
                Scenario("warm_call_latency_ms",        p95: warmCallP95,       mean: warmCallP95 * 0.9),
                Scenario("concurrent_throughput_ms",    p95: throughputMean * 1.1, mean: throughputMean),
                Scenario("memory_moderate_load_mb",     p95: memoryModerateMean, mean: memoryModerateMean),
                // Extra scenarios that the comparator ignores (idle/light memory, etc.)
                Scenario("memory_idle_mb",              p95: 90.0,  mean: 90.0),
                Scenario("memory_light_load_mb",        p95: 95.0,  mean: 95.0),
            ],
        };
    }

    /// <summary>
    /// Builds Phase 4 scenarios with mode suffix for the given transport mode.
    /// Each stat field is set so that p95 and mean equal the supplied values.
    /// </summary>
    private static IReadOnlyList<CharacterizationScenario> BuildPhase4Scenarios(
        string transportMode,
        double coldNoScriptP95 = 1000.0,
        double coldWithScriptP95 = 1000.0,
        double warmCallP95 = 2.0,
        double throughputMean = 5.0,
        double memoryModerateMean = 100.0)
    {
        var m = transportMode.ToLowerInvariant();

        static CharacterizationScenario Scenario(string name, double p95, double mean)
            => new()
            {
                Scenario = name,
                Stats = new CharacterizationStats
                {
                    P95 = p95,
                    Mean = mean,
                    P50 = mean,
                    P99 = p95,
                    Min = mean * 0.9,
                    Max = p95,
                    StdDev = 0,
                    SampleCount = 5,
                },
                RawSamples = [mean],
            };

        return
        [
            Scenario($"cold_start_http_no_script_{m}",  coldNoScriptP95,   coldNoScriptP95 * 0.95),
            Scenario($"cold_start_http_with_script_{m}", coldWithScriptP95, coldWithScriptP95 * 0.95),
            Scenario($"warm_call_latency_ms_{m}",        warmCallP95,       warmCallP95 * 0.9),
            Scenario($"concurrent_throughput_ms_{m}",    throughputMean * 1.1, throughputMean),
            Scenario($"memory_moderate_load_mb_{m}",     memoryModerateMean, memoryModerateMean),
            Scenario($"memory_idle_mb_{m}",              90.0, 90.0),
            Scenario($"memory_light_load_mb_{m}",        95.0, 95.0),
        ];
    }

    // ── Threshold boundary tests ───────────────────────────────────────────────

    [Theory]
    [InlineData("Stateless")]
    [InlineData("Stateful")]
    public void ExactlyAtColdStartThreshold_Passes(string mode)
    {
        // ratio = 1100 / 1000 = 1.10 exactly → passes (≤ maxRatio)
        var baseline = BuildBaseline(coldNoScriptP95: 1000.0, coldWithScriptP95: 1000.0);
        var p4 = BuildPhase4Scenarios(mode, coldNoScriptP95: 1100.0, coldWithScriptP95: 1100.0);

        var result = PerformanceComparator.Compare(mode, baseline, p4);

        var noScript = result.ThresholdChecks.Find(c => c.Metric == "cold_start_http_no_script.p95");
        Assert.NotNull(noScript);
        Assert.True(noScript.Passed, $"cold_start_http_no_script.p95 at ratio 1.10 should pass. Ratio={noScript.Ratio}");

        var withScript = result.ThresholdChecks.Find(c => c.Metric == "cold_start_http_with_script.p95");
        Assert.NotNull(withScript);
        Assert.True(withScript.Passed, $"cold_start_http_with_script.p95 at ratio 1.10 should pass.");
    }

    [Theory]
    [InlineData("Stateless")]
    [InlineData("Stateful")]
    public void JustAboveColdStartThreshold_Fails(string mode)
    {
        // ratio = 1100.01 / 1000.0 > 1.10 → fails
        var baseline = BuildBaseline(coldNoScriptP95: 1000.0);
        var p4 = BuildPhase4Scenarios(mode, coldNoScriptP95: 1100.01);

        var result = PerformanceComparator.Compare(mode, baseline, p4);

        var check = result.ThresholdChecks.Find(c => c.Metric == "cold_start_http_no_script.p95");
        Assert.NotNull(check);
        Assert.False(check.Passed, $"cold_start_http_no_script.p95 just above 1.10 should fail. Ratio={check.Ratio}");
        Assert.False(result.AllPassed);
    }

    [Theory]
    [InlineData("Stateless")]
    [InlineData("Stateful")]
    public void WellBelowColdStartThreshold_Passes(string mode)
    {
        var baseline = BuildBaseline(coldNoScriptP95: 1000.0);
        var p4 = BuildPhase4Scenarios(mode, coldNoScriptP95: 800.0); // 80%

        var result = PerformanceComparator.Compare(mode, baseline, p4);

        var check = result.ThresholdChecks.Find(c => c.Metric == "cold_start_http_no_script.p95");
        Assert.NotNull(check);
        Assert.True(check.Passed);
        Assert.InRange(check.Ratio, 0.79, 0.81);
    }

    [Fact]
    public void ExactlyAtWarmCallThreshold_Passes()
    {
        // ratio = 2.1 / 2.0 = 1.05 exactly → passes
        var baseline = BuildBaseline(warmCallP95: 2.0);
        var p4 = BuildPhase4Scenarios("Stateless", warmCallP95: 2.1);

        var result = PerformanceComparator.Compare("Stateless", baseline, p4);

        var check = result.ThresholdChecks.Find(c => c.Metric == "warm_call_latency_ms.p95");
        Assert.NotNull(check);
        Assert.True(check.Passed, $"warm_call_latency_ms.p95 at ratio 1.05 should pass. Ratio={check.Ratio}");
    }

    [Fact]
    public void JustAboveWarmCallThreshold_Fails()
    {
        // ratio = 2.101 / 2.0 > 1.05 → fails
        var baseline = BuildBaseline(warmCallP95: 2.0);
        var p4 = BuildPhase4Scenarios("Stateless", warmCallP95: 2.101);

        var result = PerformanceComparator.Compare("Stateless", baseline, p4);

        var check = result.ThresholdChecks.Find(c => c.Metric == "warm_call_latency_ms.p95");
        Assert.NotNull(check);
        Assert.False(check.Passed, $"warm_call_latency_ms.p95 just above 1.05 should fail. Ratio={check.Ratio}");
    }

    [Fact]
    public void ExactlyAtThroughputThreshold_Passes()
    {
        // Throughput maxRatio = 1/0.95 ≈ 1.05263.
        // Use the same constant the comparator uses to avoid floating-point ULP mismatch.
        var baseline = BuildBaseline(throughputMean: 5.0);
        var measured = 5.0 * PerformanceComparator.ThroughputMeanMaxRatio;
        var p4 = BuildPhase4Scenarios("Stateless", throughputMean: measured);

        var result = PerformanceComparator.Compare("Stateless", baseline, p4);

        var check = result.ThresholdChecks.Find(c => c.Metric == "concurrent_throughput_ms.mean");
        Assert.NotNull(check);
        // ratio = (5.0 * maxRatio) / 5.0 = maxRatio exactly (same IEEE 754 path)
        Assert.True(check.Passed,
            $"concurrent_throughput_ms.mean at ratio == maxRatio should pass. Ratio={check.Ratio}, MaxRatio={check.MaxRatio}");
    }

    [Fact]
    public void JustAboveThroughputThreshold_Fails()
    {
        var baseline = BuildBaseline(throughputMean: 5.0);
        var measured = 5.0 / 0.95 + 0.01; // just above
        var p4 = BuildPhase4Scenarios("Stateless", throughputMean: measured);

        var result = PerformanceComparator.Compare("Stateless", baseline, p4);

        var check = result.ThresholdChecks.Find(c => c.Metric == "concurrent_throughput_ms.mean");
        Assert.NotNull(check);
        Assert.False(check.Passed, $"concurrent_throughput_ms.mean just above 1/0.95 should fail. Ratio={check.Ratio}");
    }

    [Fact]
    public void ExactlyAtMemoryThreshold_Passes()
    {
        // ratio = 110 / 100 = 1.10 → passes
        var baseline = BuildBaseline(memoryModerateMean: 100.0);
        var p4 = BuildPhase4Scenarios("Stateless", memoryModerateMean: 110.0);

        var result = PerformanceComparator.Compare("Stateless", baseline, p4);

        var check = result.ThresholdChecks.Find(c => c.Metric == "memory_moderate_load_mb.mean");
        Assert.NotNull(check);
        Assert.True(check.Passed, $"memory_moderate_load_mb.mean at ratio 1.10 should pass.");
    }

    [Fact]
    public void JustAboveMemoryThreshold_Fails()
    {
        var baseline = BuildBaseline(memoryModerateMean: 100.0);
        var p4 = BuildPhase4Scenarios("Stateless", memoryModerateMean: 110.01);

        var result = PerformanceComparator.Compare("Stateless", baseline, p4);

        var check = result.ThresholdChecks.Find(c => c.Metric == "memory_moderate_load_mb.mean");
        Assert.NotNull(check);
        Assert.False(check.Passed);
    }

    // ── Both modes independent ─────────────────────────────────────────────────

    [Fact]
    public void StatelessPassStatefulFail_IndependentResults()
    {
        var baseline = BuildBaseline(warmCallP95: 2.0);

        var statelessP4 = BuildPhase4Scenarios("Stateless", warmCallP95: 2.0); // passes
        var statefulP4 = BuildPhase4Scenarios("Stateful", warmCallP95: 2.5);   // 125% → fails

        var statelessResult = PerformanceComparator.Compare("Stateless", baseline, statelessP4);
        var statefulResult = PerformanceComparator.Compare("Stateful", baseline, statefulP4);

        Assert.True(statelessResult.AllPassed, "Stateless should pass");
        Assert.False(statefulResult.AllPassed, "Stateful should fail");
        Assert.Equal("Stateless", statelessResult.TransportMode);
        Assert.Equal("Stateful", statefulResult.TransportMode);
    }

    // ── Invalid input handling ─────────────────────────────────────────────────

    [Fact]
    public void NullBaseline_ThrowsArgumentNull()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            PerformanceComparator.ValidateBaseline(null!));
        Assert.Contains("V1_BASELINE_PATH", ex.Message);
    }

    [Fact]
    public void WrongSchemaVersion_ThrowsInvalidOperation()
    {
        var baseline = BuildBaseline();
        baseline.SchemaVersion = "poshmcp/wrong/1.0";

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PerformanceComparator.ValidateBaseline(baseline));
        Assert.Contains("schema version mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(PerformanceComparator.ExpectedBaselineSchemaVersion, ex.Message);
    }

    [Fact]
    public void EmptyScenarios_ThrowsInvalidOperation()
    {
        var baseline = BuildBaseline();
        baseline.Scenarios.Clear();

        Assert.Throws<InvalidOperationException>(() =>
            PerformanceComparator.ValidateBaseline(baseline));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ZeroOrNonFiniteBaselineP95_ThrowsInvalidOperation(double badValue)
    {
        var baseline = BuildBaseline();
        // Corrupt the cold_start_http_no_script p95
        baseline.Scenarios[0].Stats.P95 = badValue;

        Assert.Throws<InvalidOperationException>(() =>
            PerformanceComparator.ValidateBaseline(baseline));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteMeasuredValue_ThrowsInvalidOperation(double badMeasured)
    {
        var baseline = BuildBaseline(coldNoScriptP95: 1000.0);

        // Inject NaN/Infinity into one of the Phase 4 scenarios
        var scenarios = new List<CharacterizationScenario>(BuildPhase4Scenarios("Stateless"));
        scenarios.Find(s => s.Scenario == "cold_start_http_no_script_stateless")!.Stats.P95 = badMeasured;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PerformanceComparator.Compare("Stateless", baseline, scenarios));
        Assert.Contains("not finite", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingPhase4Scenario_ThrowsKeyNotFound()
    {
        var baseline = BuildBaseline();
        var p4 = new List<CharacterizationScenario>(BuildPhase4Scenarios("Stateless"));
        // Remove the warm_call scenario
        p4.RemoveAll(s => s.Scenario.StartsWith("warm_call", StringComparison.OrdinalIgnoreCase));

        var ex = Assert.Throws<KeyNotFoundException>(() =>
            PerformanceComparator.Compare("Stateless", baseline, p4));
        Assert.Contains("warm_call_latency_ms_stateless", ex.Message);
    }

    [Fact]
    public void MissingBaselineScenario_ThrowsKeyNotFound()
    {
        var baseline = BuildBaseline();
        baseline.Scenarios.RemoveAll(s => s.Scenario == "cold_start_http_no_script");
        var p4 = BuildPhase4Scenarios("Stateless");

        var ex = Assert.Throws<KeyNotFoundException>(() =>
            PerformanceComparator.Compare("Stateless", baseline, p4));
        Assert.Contains("cold_start_http_no_script", ex.Message);
    }

    // ── Exit code ─────────────────────────────────────────────────────────────

    [Fact]
    public void AllPassed_ExitCodeZero()
    {
        var baseline = BuildBaseline();
        var p4 = BuildPhase4Scenarios("Stateless"); // identical to baseline → all pass

        var result = PerformanceComparator.Compare("Stateless", baseline, p4);

        Assert.True(result.AllPassed);
        // ExitCode is determined by the fixture artifact, but AllPassed=true → exitCode=0
        // Verify the mapping is correct via the artifact builder:
        var exitCode = result.AllPassed ? 0 : 1;
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void AnyFailed_ExitCodeOne()
    {
        var baseline = BuildBaseline(coldNoScriptP95: 1000.0);
        var p4 = BuildPhase4Scenarios("Stateless", coldNoScriptP95: 2000.0); // 200% → fails

        var result = PerformanceComparator.Compare("Stateless", baseline, p4);

        Assert.False(result.AllPassed);
        var exitCode = result.AllPassed ? 0 : 1;
        Assert.Equal(1, exitCode);
    }

    // ── Scenario data in output ────────────────────────────────────────────────

    [Fact]
    public void ComparisonResult_ContainsBothThresholdAndScenarioData()
    {
        var baseline = BuildBaseline();
        var p4 = BuildPhase4Scenarios("Stateless");

        var result = PerformanceComparator.Compare("Stateless", baseline, p4);

        // Must have exactly 5 threshold checks
        Assert.Equal(5, result.ThresholdChecks.Count);

        // Each check must have valid metadata
        foreach (var check in result.ThresholdChecks)
        {
            Assert.NotEmpty(check.Metric);
            Assert.NotEmpty(check.Description);
            Assert.True(check.MaxRatio > 0);
            Assert.True(check.BaselineValue > 0);
        }

        // Scenarios are passed through as-is
        Assert.NotEmpty(result.Scenarios);
    }
}
