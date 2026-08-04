using System;
using System.Linq;
using PoshMcp.Tests.Characterization.Phase4;
using Xunit;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Deterministic tests for <see cref="StatisticalReport"/> (#380 AC5).
/// Covers CV/range/median calculations, confidence classification,
/// low-N edge cases, invalid data handling, and noisy interpretation.
/// </summary>
[Trait("Category", "Unit")]
public class StatisticalReportTests
{
    // ── Median calculation ─────────────────────────────────────────────────────

    [Fact]
    public void Median_OddCount_ReturnsMiddleValue()
    {
        var sorted = new double[] { 1.0, 2.0, 3.0, 4.0, 5.0 };
        Assert.Equal(3.0, StatisticalReport.ComputeMedian(sorted), 10);
    }

    [Fact]
    public void Median_EvenCount_ReturnsAverageOfMiddleTwo()
    {
        var sorted = new double[] { 1.0, 2.0, 3.0, 4.0 };
        Assert.Equal(2.5, StatisticalReport.ComputeMedian(sorted), 10);
    }

    [Fact]
    public void Median_SingleValue_ReturnsThatValue()
    {
        Assert.Equal(42.0, StatisticalReport.ComputeMedian([42.0]), 10);
    }

    [Fact]
    public void Median_Empty_ReturnsNaN()
    {
        Assert.True(double.IsNaN(StatisticalReport.ComputeMedian([])));
    }

    // ── CV calculation ─────────────────────────────────────────────────────────

    [Fact]
    public void FromSamples_IdenticalValues_CvIsZero()
    {
        var report = StatisticalReport.FromSamples("test", "ms", [5.0, 5.0, 5.0, 5.0, 5.0]);
        Assert.Equal(0.0, report.CvPercent, 10);
    }

    [Fact]
    public void FromSamples_KnownValues_CvIsCorrect()
    {
        // samples: 10, 20, 30. mean=20, stddev(pop)=sqrt((100+0+100)/3)=sqrt(200/3)≈8.165
        // CV = 8.165/20 * 100 = 40.825%
        var report = StatisticalReport.FromSamples("test", "ms", [10.0, 20.0, 30.0]);
        var expectedMean = 20.0;
        var expectedStdDev = Math.Sqrt((100.0 + 0.0 + 100.0) / 3.0);
        var expectedCv = (expectedStdDev / expectedMean) * 100.0;
        Assert.Equal(expectedCv, report.CvPercent, 8);
    }

    // ── Range ──────────────────────────────────────────────────────────────────

    [Fact]
    public void FromSamples_Range_IsMaxMinusMin()
    {
        var report = StatisticalReport.FromSamples("test", "ms", [3.0, 7.0, 5.0, 1.0, 9.0]);
        Assert.Equal(1.0, report.Min, 10);
        Assert.Equal(9.0, report.Max, 10);
        Assert.Equal(8.0, report.Range, 10);
    }

    // ── Confidence classification ──────────────────────────────────────────────

    [Fact]
    public void Confidence_CvUnder5_IsHigh()
    {
        var (confidence, _) = StatisticalReport.ClassifyConfidence(3.0, 5);
        Assert.Equal("HIGH", confidence);
    }

    [Fact]
    public void Confidence_CvExactly5_IsHigh()
    {
        var (confidence, _) = StatisticalReport.ClassifyConfidence(5.0, 5);
        Assert.Equal("HIGH", confidence);
    }

    [Fact]
    public void Confidence_CvUnder15_IsModerate()
    {
        var (confidence, _) = StatisticalReport.ClassifyConfidence(10.0, 5);
        Assert.Equal("MODERATE", confidence);
    }

    [Fact]
    public void Confidence_CvExactly15_IsModerate()
    {
        var (confidence, _) = StatisticalReport.ClassifyConfidence(15.0, 5);
        Assert.Equal("MODERATE", confidence);
    }

    [Fact]
    public void Confidence_CvOver15_IsLow()
    {
        var (confidence, _) = StatisticalReport.ClassifyConfidence(20.0, 5);
        Assert.Equal("LOW", confidence);
    }

    [Fact]
    public void Confidence_NLessThan3_IsInsufficient()
    {
        var (confidence, _) = StatisticalReport.ClassifyConfidence(3.0, 2);
        Assert.Equal("INSUFFICIENT", confidence);
    }

    [Fact]
    public void Confidence_NEquals1_IsInsufficient()
    {
        var (confidence, _) = StatisticalReport.ClassifyConfidence(0.0, 1);
        Assert.Equal("INSUFFICIENT", confidence);
    }

    [Fact]
    public void Confidence_CvIsNaN_IsInsufficient()
    {
        var (confidence, _) = StatisticalReport.ClassifyConfidence(double.NaN, 5);
        Assert.Equal("INSUFFICIENT", confidence);
    }

    [Fact]
    public void Confidence_CvIsInfinity_IsInsufficient()
    {
        var (confidence, _) = StatisticalReport.ClassifyConfidence(double.PositiveInfinity, 5);
        Assert.Equal("INSUFFICIENT", confidence);
    }

    // ── Low-N and invalid data ─────────────────────────────────────────────────

    [Fact]
    public void FromSamples_NullArray_ReturnsInsufficient()
    {
        var report = StatisticalReport.FromSamples("test", "ms", null!);
        Assert.Equal("INSUFFICIENT", report.Confidence);
        Assert.Equal(0, report.SampleCount);
        Assert.True(double.IsNaN(report.Median));
    }

    [Fact]
    public void FromSamples_EmptyArray_ReturnsInsufficient()
    {
        var report = StatisticalReport.FromSamples("test", "ms", []);
        Assert.Equal("INSUFFICIENT", report.Confidence);
        Assert.Equal(0, report.SampleCount);
    }

    [Fact]
    public void FromSamples_SingleSample_CvIsNaN()
    {
        var report = StatisticalReport.FromSamples("test", "ms", [42.0]);
        Assert.True(double.IsNaN(report.CvPercent));
        Assert.Equal(42.0, report.Median, 10);
        Assert.Equal(42.0, report.Mean, 10);
        Assert.Equal(0.0, report.Range, 10);
    }

    [Fact]
    public void FromSamples_TwoSamples_CvCalculated_ButInsufficient()
    {
        var report = StatisticalReport.FromSamples("test", "ms", [10.0, 20.0]);
        Assert.Equal("INSUFFICIENT", report.Confidence);
        Assert.Equal(2, report.SampleCount);
        // CV should be calculated for N=2 but classified as INSUFFICIENT
        Assert.True(double.IsFinite(report.CvPercent));
    }

    // ── Order effects ──────────────────────────────────────────────────────────

    [Fact]
    public void FromSamples_PreservesOriginalOrder_InSamplesArray()
    {
        var input = new double[] { 5.0, 3.0, 1.0, 4.0, 2.0 };
        var report = StatisticalReport.FromSamples("test", "ms", input);
        // Samples should preserve original collection order (not sorted)
        Assert.Equal(input, report.Samples);
    }

    [Fact]
    public void FromSamples_Median_IsFromSortedValues_NotCollectionOrder()
    {
        // Collection order: 5, 3, 1, 4, 2 → sorted: 1, 2, 3, 4, 5 → median=3
        var report = StatisticalReport.FromSamples("test", "ms", [5.0, 3.0, 1.0, 4.0, 2.0]);
        Assert.Equal(3.0, report.Median, 10);
    }

    // ── Noisy interpretation ───────────────────────────────────────────────────

    [Fact]
    public void FromSamples_HighVariance_ClassifiedAsLow()
    {
        // Samples with high variance: 1, 1, 1, 1, 100
        // mean ≈ 20.8, stddev ≈ large, CV > 15%
        var report = StatisticalReport.FromSamples("test", "ms", [1.0, 1.0, 1.0, 1.0, 100.0]);
        Assert.Equal("LOW", report.Confidence);
    }

    [Fact]
    public void FromSamples_LowVariance_ClassifiedAsHigh()
    {
        // Very stable: 10.0, 10.1, 9.9, 10.0, 10.0
        var report = StatisticalReport.FromSamples("test", "ms", [10.0, 10.1, 9.9, 10.0, 10.0]);
        Assert.Equal("HIGH", report.Confidence);
    }

    // ── Confidence rationale includes CV value ─────────────────────────────────

    [Fact]
    public void ConfidenceRationale_ContainsCvValue()
    {
        var report = StatisticalReport.FromSamples("test", "ms", [10.0, 11.0, 9.0, 10.5, 9.5]);
        Assert.Contains("CV=", report.ConfidenceRationale);
    }

    // ── Stage attribution ──────────────────────────────────────────────────────

    [Fact]
    public void StageAttribution_Create_ProducesHypothesisLabel()
    {
        var warmSamples = new double[] { 10.0, 11.0, 9.5, 10.5, 10.0 };

        var attr = StageAttribution.Create("Stateless", warmSamples, [], []);

        Assert.Equal("Stateless", attr.TransportMode);
        Assert.Contains("HYPOTHESIS", attr.Hypothesis);
        Assert.True(attr.SteadyStatePerRequestMs > 0);
        Assert.Contains("First-call vs steady-state", attr.AttributionMethod);
    }

    [Fact]
    public void StageAttribution_Create_ConnectionOverhead_IsFirstCallMinusMedian()
    {
        // first call = 20.0, sorted remaining: 10,11,12 → median ~11
        var warmSamples = new double[] { 20.0, 10.0, 12.0, 11.0 };

        var attr = StageAttribution.Create("Stateful", warmSamples, [], []);

        // Median of all 4 sorted (10,11,12,20) = (11+12)/2 = 11.5
        // Connection overhead = first(20) - median(11.5) = 8.5
        Assert.Equal(20.0 - 11.5, attr.ConnectionOverheadEstimateMs, 10);
        Assert.Equal(20.0, attr.FirstCallMs, 10);
        Assert.Equal(11.5, attr.SteadyStatePerRequestMs, 10);
    }

    [Fact]
    public void StageAttribution_EnumeratesAllRequiredStages()
    {
        // AC6 requires: end-to-end warm call, connection, lease acquisition,
        // PS execution, reset/return, startup/eager/script stages.
        var warmSamples = new double[] { 15.0, 10.0, 11.0, 12.0 };
        var coldWithScript = new double[] { 500.0, 550.0, 600.0 };
        var coldNoScript = new double[] { 400.0, 430.0, 460.0 };

        var attr = StageAttribution.Create("Stateless", warmSamples,
            coldWithScript, coldNoScript);

        var stageNames = attr.Stages.Select(s => s.Stage).ToList();
        Assert.Contains("end_to_end_warm_call", stageNames);
        Assert.Contains("connection_initialization", stageNames);
        Assert.Contains("lease_acquisition", stageNames);
        Assert.Contains("powershell_execution", stageNames);
        Assert.Contains("reset_return", stageNames);
        Assert.Contains("startup_eager", stageNames);
        Assert.Contains("startup_script", stageNames);
    }

    [Fact]
    public void StageAttribution_RequiresServerInstrumentation_AreDocumented()
    {
        var warmSamples = new double[] { 15.0, 10.0, 11.0, 12.0 };

        var attr = StageAttribution.Create("Stateless", warmSamples, [], []);

        Assert.True(attr.RequiresServerInstrumentation.Count >= 3);
        Assert.Contains(attr.RequiresServerInstrumentation,
            s => s.Contains("lease_acquisition"));
        Assert.Contains(attr.RequiresServerInstrumentation,
            s => s.Contains("powershell_execution"));
        Assert.Contains(attr.RequiresServerInstrumentation,
            s => s.Contains("reset_return"));

        // These stages should have method = "requires_server_instrumentation" and null estimate
        var leaseDet = attr.Stages.First(s => s.Stage == "lease_acquisition");
        Assert.Equal("requires_server_instrumentation", leaseDet.Method);
        Assert.Null(leaseDet.EstimateMs);
    }

    [Fact]
    public void StageAttribution_WithColdStartSamples_ComputesStartupScriptDelta()
    {
        var warmSamples = new double[] { 15.0, 10.0, 11.0, 12.0 };
        // cold_with_script median = 550, cold_no_script median = 430
        var coldWithScript = new double[] { 500.0, 550.0, 600.0 };
        var coldNoScript = new double[] { 400.0, 430.0, 460.0 };

        var attr = StageAttribution.Create("Stateless", warmSamples,
            coldWithScript, coldNoScript);

        Assert.NotNull(attr.StartupScriptEstimateMs);
        Assert.Equal(550.0 - 430.0, attr.StartupScriptEstimateMs!.Value, 10);
        Assert.NotNull(attr.ServerStartupEstimateMs);
        Assert.Equal(430.0, attr.ServerStartupEstimateMs!.Value, 10);
        Assert.Contains("startup script", attr.Hypothesis);
    }

    [Fact]
    public void StageAttribution_WithoutColdStartSamples_NullStartupFields()
    {
        var warmSamples = new double[] { 15.0, 10.0, 11.0, 12.0 };

        var attr = StageAttribution.Create("Stateless", warmSamples, [], []);

        Assert.Null(attr.StartupScriptEstimateMs);
        Assert.Null(attr.ServerStartupEstimateMs);
        // Should still have the warm-call stages
        Assert.Contains(attr.Stages, s => s.Stage == "end_to_end_warm_call");
        Assert.Contains(attr.Stages, s => s.Stage == "connection_initialization");
        // But should NOT have startup stages
        Assert.DoesNotContain(attr.Stages, s => s.Stage == "startup_eager");
        Assert.DoesNotContain(attr.Stages, s => s.Stage == "startup_script");
    }

    [Fact]
    public void StageAttribution_MeasurementOverheadBound_IsWarmCallIQR()
    {
        // sorted: 1.0, 9.5, 10.0, 10.5, 11.0 → Q1=9.5, Q3=10.5, IQR=1.0
        var warmSamples = new double[] { 10.0, 11.0, 9.5, 10.5, 1.0 };

        var attr = StageAttribution.Create("Stateless", warmSamples, [], []);

        // IQR computed as sorted[len/4] .. sorted[3*len/4]
        // sorted: [1.0, 9.5, 10.0, 10.5, 11.0], Q1idx=1→9.5, Q3idx=3→10.5, IQR=1.0
        Assert.Equal(1.0, attr.MeasurementOverheadBoundMs, 10);
    }

    [Fact]
    public void StageAttribution_NegativeConnectionOverhead_NotProduced()
    {
        // If first call is lower than median (unusual), overhead is negative but
        // the system should still produce a valid report (no crash, no NaN).
        var warmSamples = new double[] { 5.0, 10.0, 11.0, 12.0, 10.5 };

        var attr = StageAttribution.Create("Stateless", warmSamples, [], []);

        // first=5, median=10.5, overhead = 5 - 10.5 = -5.5 (but clamped to 0? No, report honestly)
        Assert.True(attr.ConnectionOverheadEstimateMs < 0);
        Assert.Contains("HYPOTHESIS", attr.Hypothesis);
    }
}
