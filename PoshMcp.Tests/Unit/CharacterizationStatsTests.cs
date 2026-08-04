using System;
using System.Collections.Generic;
using System.Linq;
using PoshMcp.Tests.Characterization;
using PoshMcp.Tests.Characterization.Phase4;
using Xunit;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Deterministic tests for the percentile/summary math in
/// <see cref="CharacterizationStats.FromSamples"/> and the sameJobPaired merge contract in
/// <see cref="Phase4MergeValidator"/>.
///
/// The percentile tests pin the exact linear-interpolation algorithm (rank = p·(n−1)) so a
/// silent change to the estimator is caught, and cross-check that the summary fields are
/// self-consistent with an independent recomputation from the raw samples.
/// </summary>
[Trait("Category", "Unit")]
public class CharacterizationStatsTests
{
    // ── Percentile algorithm (linear interpolation on rank = p*(n-1)) ───────────────

    [Fact]
    public void FromSamples_KnownArray_ExactPercentiles()
    {
        // samples 1..5. rank(p) = p*(n-1) = p*4.
        //  p50 -> rank 2.0  -> sorted[2]            = 3.0
        //  p95 -> rank 3.8  -> 0.2*sorted[3]+0.8*sorted[4] = 0.2*4 + 0.8*5 = 4.8
        //  p99 -> rank 3.96 -> 0.04*4 + 0.96*5           = 4.96
        var stats = CharacterizationStats.FromSamples([5, 3, 1, 4, 2]);

        Assert.Equal(1.0, stats.Min, 10);
        Assert.Equal(5.0, stats.Max, 10);
        Assert.Equal(3.0, stats.Mean, 10);
        Assert.Equal(3.0, stats.P50, 10);
        Assert.Equal(4.8, stats.P95, 10);
        Assert.Equal(4.96, stats.P99, 10);
        Assert.Equal(5, stats.SampleCount);
    }

    [Fact]
    public void FromSamples_SingleSample_AllEqual()
    {
        var stats = CharacterizationStats.FromSamples([42.0]);

        Assert.Equal(42.0, stats.Min, 10);
        Assert.Equal(42.0, stats.Max, 10);
        Assert.Equal(42.0, stats.Mean, 10);
        Assert.Equal(42.0, stats.P50, 10);
        Assert.Equal(42.0, stats.P95, 10);
        Assert.Equal(0.0, stats.StdDev, 10);
        Assert.Equal(1, stats.SampleCount);
    }

    [Fact]
    public void FromSamples_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => CharacterizationStats.FromSamples([]));
    }

    [Fact]
    public void FromSamples_RawSummaryConsistency()
    {
        var samples = new double[] { 1.10, 0.98, 1.05, 2.30, 1.02, 1.20, 0.99, 1.15, 1.08, 1.01 };
        var stats = CharacterizationStats.FromSamples(samples);

        // Independent recomputation of mean and the same-formula p95.
        var expectedMean = samples.Sum() / samples.Length;
        Assert.Equal(expectedMean, stats.Mean, 10);
        Assert.Equal(samples.Min(), stats.Min, 10);
        Assert.Equal(samples.Max(), stats.Max, 10);

        var sorted = samples.OrderBy(x => x).ToArray();
        double rank = 0.95 * (sorted.Length - 1);
        int lo = (int)rank;
        int hi = Math.Min(lo + 1, sorted.Length - 1);
        double frac = rank - lo;
        double expectedP95 = sorted[lo] * (1 - frac) + sorted[hi] * frac;
        Assert.Equal(expectedP95, stats.P95, 10);

        // Population std-dev cross-check.
        var variance = samples.Sum(x => (x - expectedMean) * (x - expectedMean)) / samples.Length;
        Assert.Equal(Math.Sqrt(variance), stats.StdDev, 10);
    }

    // ── sameJobPaired merge contract ────────────────────────────────────────────────

    [Fact]
    public void MergeSameJobPaired_AllTrue_ReturnsTrue()
    {
        Assert.True(Phase4MergeValidator.MergeSameJobPaired(new bool?[] { true, true }));
    }

    [Fact]
    public void MergeSameJobPaired_AnyFalse_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => Phase4MergeValidator.MergeSameJobPaired(new bool?[] { true, false }));
    }

    [Fact]
    public void MergeSameJobPaired_MissingFlag_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => Phase4MergeValidator.MergeSameJobPaired(new bool?[] { true, null }));
    }

    [Fact]
    public void MergeSameJobPaired_MixedFalseAndMissing_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => Phase4MergeValidator.MergeSameJobPaired(new bool?[] { null, false }));
    }

    [Fact]
    public void MergeSameJobPaired_Empty_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => Phase4MergeValidator.MergeSameJobPaired(Array.Empty<bool?>()));
    }
}
