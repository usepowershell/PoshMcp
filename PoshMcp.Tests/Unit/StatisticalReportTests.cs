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
        var httpSamples = new double[] { 1.0, 1.1, 0.9, 1.0, 1.0 };

        var attr = StageAttribution.Create("Stateless", warmSamples, httpSamples);

        Assert.Equal("Stateless", attr.TransportMode);
        Assert.Contains("HYPOTHESIS", attr.Hypothesis);
        Assert.True(attr.McpOverheadEstimateMs > 0);
        Assert.Contains("subtraction", attr.AttributionMethod);
    }

    [Fact]
    public void StageAttribution_Create_McpOverhead_Equals_WarmMedian_Minus_HttpMedian()
    {
        var warmSamples = new double[] { 10.0, 12.0, 11.0 };  // sorted: 10,11,12 → median=11
        var httpSamples = new double[] { 1.0, 2.0, 1.5 };     // sorted: 1,1.5,2 → median=1.5

        var attr = StageAttribution.Create("Stateful", warmSamples, httpSamples);

        Assert.Equal(11.0 - 1.5, attr.McpOverheadEstimateMs, 10);
    }

    [Fact]
    public void StageAttribution_EnumeratesAllRequiredStages()
    {
        // AC6 requires: HTTP/MCP round trip, lease acquisition, PS execution,
        // reset/return, startup/eager/script stages.
        var warmSamples = new double[] { 10.0, 11.0, 12.0 };
        var httpSamples = new double[] { 1.0, 1.5, 2.0 };
        var coldWithScript = new double[] { 500.0, 550.0, 600.0 };
        var coldNoScript = new double[] { 400.0, 430.0, 460.0 };

        var attr = StageAttribution.Create("Stateless", warmSamples, httpSamples,
            coldWithScript, coldNoScript);

        var stageNames = attr.Stages.Select(s => s.Stage).ToList();
        Assert.Contains("http_mcp_roundtrip", stageNames);
        Assert.Contains("mcp_overhead", stageNames);
        Assert.Contains("lease_acquisition", stageNames);
        Assert.Contains("powershell_execution", stageNames);
        Assert.Contains("reset_return", stageNames);
        Assert.Contains("startup_eager", stageNames);
        Assert.Contains("startup_script", stageNames);
    }

    [Fact]
    public void StageAttribution_NotSeparableStages_AreDocumented()
    {
        var warmSamples = new double[] { 10.0, 11.0, 12.0 };
        var httpSamples = new double[] { 1.0, 1.5, 2.0 };

        var attr = StageAttribution.Create("Stateless", warmSamples, httpSamples);

        Assert.True(attr.NotSeparableWithoutPerturbation.Count >= 3);
        Assert.Contains(attr.NotSeparableWithoutPerturbation,
            s => s.Contains("lease_acquisition"));
        Assert.Contains(attr.NotSeparableWithoutPerturbation,
            s => s.Contains("powershell_execution"));
        Assert.Contains(attr.NotSeparableWithoutPerturbation,
            s => s.Contains("reset_return"));

        // Non-separable stages should have method = "not_separable" and null estimate
        var leaseDet = attr.Stages.First(s => s.Stage == "lease_acquisition");
        Assert.Equal("not_separable", leaseDet.Method);
        Assert.Null(leaseDet.EstimateMs);
    }

    [Fact]
    public void StageAttribution_WithColdStartSamples_ComputesStartupScriptDelta()
    {
        var warmSamples = new double[] { 10.0, 11.0, 12.0 };
        var httpSamples = new double[] { 1.0, 1.5, 2.0 };
        // cold_with_script median = 550, cold_no_script median = 430
        var coldWithScript = new double[] { 500.0, 550.0, 600.0 };
        var coldNoScript = new double[] { 400.0, 430.0, 460.0 };

        var attr = StageAttribution.Create("Stateless", warmSamples, httpSamples,
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
        var warmSamples = new double[] { 10.0, 11.0, 12.0 };
        var httpSamples = new double[] { 1.0, 1.5, 2.0 };

        var attr = StageAttribution.Create("Stateless", warmSamples, httpSamples);

        Assert.Null(attr.StartupScriptEstimateMs);
        Assert.Null(attr.ServerStartupEstimateMs);
        // Should still have the warm-call stages
        Assert.Contains(attr.Stages, s => s.Stage == "http_mcp_roundtrip");
        Assert.Contains(attr.Stages, s => s.Stage == "mcp_overhead");
        // But should NOT have startup stages
        Assert.DoesNotContain(attr.Stages, s => s.Stage == "startup_eager");
        Assert.DoesNotContain(attr.Stages, s => s.Stage == "startup_script");
    }

    [Fact]
    public void StageAttribution_MeasurementOverheadBound_IsHttpRange()
    {
        var warmSamples = new double[] { 10.0, 11.0, 12.0 };
        var httpSamples = new double[] { 1.0, 1.5, 2.0 }; // range = 1.0

        var attr = StageAttribution.Create("Stateless", warmSamples, httpSamples);

        Assert.Equal(1.0, attr.MeasurementOverheadBoundMs, 10);
    }
}
