using PoshMcp.Tests.Characterization;
using PoshMcp.Tests.Characterization.Phase4;
using Xunit;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Guard tests that the performance thresholds in <see cref="PerformanceComparator"/> are
/// byte-for-byte unchanged (#380 AC8). If any threshold constant changes, these tests fail
/// — preventing accidental or unauthorized threshold relaxation.
///
/// All thresholds are immutable for the v1→v2 migration gate:
///   Cold-start p95   ≤ 1.10  (≤ 110% of baseline)
///   Warm-call p95    ≤ 1.05  (≤ 105% of baseline)
///   Throughput mean  ≤ 1/0.95 ≈ 1.05263 (≥ 95% throughput rate)
///   Memory peak mean ≤ 1.10  (≤ 110% of baseline)
/// </summary>
[Trait("Category", "Unit")]
public class ThresholdGuardTests
{
    [Fact]
    public void ColdStartP95MaxRatio_IsExactly_1_10()
    {
        Assert.Equal(1.10, PerformanceComparator.ColdStartP95MaxRatio, 15);
    }

    [Fact]
    public void WarmCallP95MaxRatio_IsExactly_1_05()
    {
        Assert.Equal(1.05, PerformanceComparator.WarmCallP95MaxRatio, 15);
    }

    [Fact]
    public void ThroughputMeanMaxRatio_IsExactly_OneOverPointNineFive()
    {
        // 1.0 / 0.95 = 1.052631578947368...
        Assert.Equal(1.0 / 0.95, PerformanceComparator.ThroughputMeanMaxRatio, 15);
    }

    [Fact]
    public void MemoryPeakMeanMaxRatio_IsExactly_1_10()
    {
        Assert.Equal(1.10, PerformanceComparator.MemoryPeakMeanMaxRatio, 15);
    }

    [Fact]
    public void ThroughputMaxRatio_IsGreaterThan_1_05_AndLessThan_1_06()
    {
        // Sanity: 1/0.95 ≈ 1.0526, must be in [1.05, 1.06)
        Assert.True(PerformanceComparator.ThroughputMeanMaxRatio > 1.05);
        Assert.True(PerformanceComparator.ThroughputMeanMaxRatio < 1.06);
    }

    [Fact]
    public void ExpectedBaselineSchemaVersion_IsV1Characterization()
    {
        Assert.Equal("poshmcp/v1-characterization/1.0", PerformanceComparator.ExpectedBaselineSchemaVersion);
    }

    /// <summary>
    /// Proves the gate script thresholds match the C# constants exactly.
    /// The gate script <c>Invoke-Phase4Gate.ps1</c> uses ExpectedThresholdChecksPerMode=5
    /// (5 checks per mode: cold-no-script, cold-with-script, warm-call, throughput, memory).
    /// </summary>
    [Fact]
    public void ExpectedThresholdChecksPerMode_Is_5()
    {
        // This matches Invoke-Phase4Gate.ps1 -ExpectedThresholdChecksPerMode default (5)
        // and the PerformanceComparator.Compare method which produces exactly 5 checks.
        // If you add/remove a threshold check, update BOTH the comparator AND the gate script.
        var baseline = CreateMinimalBaseline();
        var scenarios = CreateMinimalPhase4Scenarios("stateless");
        var result = PerformanceComparator.Compare("Stateless", baseline, scenarios);
        Assert.Equal(5, result.ThresholdChecks.Count);
    }

    private static CharacterizationArtifact CreateMinimalBaseline()
    {
        return new CharacterizationArtifact
        {
            SchemaVersion = "poshmcp/v1-characterization/1.0",
            Scenarios =
            [
                MakeScenario("cold_start_http_no_script", 100.0, 5),
                MakeScenario("cold_start_http_with_script", 200.0, 5),
                MakeScenario("warm_call_latency_ms", 10.0, 20),
                MakeScenario("concurrent_throughput_ms", 50.0, 5),
                MakeScenario("memory_moderate_load_mb", 100.0, 1),
            ]
        };
    }

    private static System.Collections.Generic.List<CharacterizationScenario> CreateMinimalPhase4Scenarios(string mode)
    {
        return
        [
            MakeScenario($"cold_start_http_no_script_{mode}", 100.0, 5),
            MakeScenario($"cold_start_http_with_script_{mode}", 200.0, 5),
            MakeScenario($"warm_call_latency_ms_{mode}", 10.0, 20),
            MakeScenario($"concurrent_throughput_ms_{mode}", 50.0, 5),
            MakeScenario($"memory_moderate_load_mb_{mode}", 100.0, 1),
        ];
    }

    private static CharacterizationScenario MakeScenario(string name, double value, int n)
    {
        var samples = new double[n];
        for (int i = 0; i < n; i++) samples[i] = value;
        return new CharacterizationScenario
        {
            Scenario = name,
            Iterations = n,
            Stats = CharacterizationStats.FromSamples(samples),
            RawSamples = samples,
        };
    }
}
