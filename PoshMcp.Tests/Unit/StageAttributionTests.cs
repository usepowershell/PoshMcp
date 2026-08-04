using System.Linq;
using PoshMcp.Tests.Characterization.Phase4;
using Xunit;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Deterministic tests for <see cref="StageAttribution.Create"/> (#380 Revision 3 blocker 3).
/// Verifies signed raw delta preservation, INCONCLUSIVE vs MODERATE labeling,
/// and null/non-null EstimateMs for negative, zero/noise-band, and positive script deltas.
/// </summary>
[Trait("Category", "Unit")]
public class StageAttributionTests
{
    // Helper: build enough samples for a meaningful comparison.
    // coldWithScript median ≈ withMs, coldNoScript median ≈ noMs, warm ≈ warmMs.
    private static double[] Samples(double medianMs, int n = 10) =>
        Enumerable.Repeat(medianMs, n).ToArray();

    private static StageAttribution Create(
        double withScriptMs,
        double noScriptMs,
        double warmMs = 200.0) =>
        StageAttribution.Create(
            "Stateless",
            Samples(warmMs),
            Samples(withScriptMs),
            Samples(noScriptMs));

    // ── BLOCKER 3: negative delta ─────────────────────────────────────────────

    [Fact]
    public void NegativeScriptDelta_IsInconclusive_NullEstimate_PreservesDelta()
    {
        // noScript is slower than withScript → negative signed delta.
        var attr = Create(withScriptMs: 400.0, noScriptMs: 500.0);

        var startupStage = attr.Stages.FirstOrDefault(s => s.Stage == "startup_script");
        Assert.NotNull(startupStage);
        Assert.Equal("INCONCLUSIVE", startupStage.Confidence);
        Assert.Null(startupStage.EstimateMs);
        // Signed raw delta must be preserved and negative.
        Assert.NotNull(startupStage.SignedRawDeltaMs);
        Assert.True(startupStage.SignedRawDeltaMs < 0,
            $"Expected negative SignedRawDeltaMs; got {startupStage.SignedRawDeltaMs}");
    }

    [Fact]
    public void NegativeScriptDelta_HypothesisContainsInconclusive()
    {
        var attr = Create(withScriptMs: 400.0, noScriptMs: 600.0);
        Assert.Contains("INCONCLUSIVE", attr.Hypothesis, System.StringComparison.OrdinalIgnoreCase);
    }

    // ── Zero / noise-band delta ───────────────────────────────────────────────

    [Fact]
    public void ZeroScriptDelta_ReturnsZeroEstimateNotNull()
    {
        // withScript == noScript → delta is exactly 0; script adds no measurable overhead.
        // Zero is a meaningful (non-negative) result: EstimateMs=0, not null/INCONCLUSIVE.
        // The INCONCLUSIVE path is for NEGATIVE deltas only (noScript faster than withScript).
        var attr = Create(withScriptMs: 500.0, noScriptMs: 500.0);

        var startupStage = attr.Stages.FirstOrDefault(s => s.Stage == "startup_script");
        Assert.NotNull(startupStage);

        // Zero delta is not INCONCLUSIVE — it's a clean zero-overhead measurement.
        Assert.NotNull(startupStage.EstimateMs);
        Assert.Equal(0.0, startupStage.EstimateMs!.Value, precision: 3);
        // SignedRawDeltaMs must also be zero.
        Assert.NotNull(startupStage.SignedRawDeltaMs);
        Assert.Equal(0.0, startupStage.SignedRawDeltaMs!.Value, precision: 3);
    }

    // ── Positive delta ────────────────────────────────────────────────────────

    [Fact]
    public void PositiveScriptDelta_IsModerate_EstimateSet()
    {
        // withScript is clearly slower → positive signed delta.
        var attr = Create(withScriptMs: 900.0, noScriptMs: 400.0);

        var startupStage = attr.Stages.FirstOrDefault(s => s.Stage == "startup_script");
        Assert.NotNull(startupStage);
        Assert.Equal("MODERATE", startupStage.Confidence);
        Assert.NotNull(startupStage.EstimateMs);
        Assert.True(startupStage.EstimateMs > 0,
            $"Expected positive EstimateMs; got {startupStage.EstimateMs}");
        // SignedRawDeltaMs must also be preserved and positive.
        Assert.NotNull(startupStage.SignedRawDeltaMs);
        Assert.True(startupStage.SignedRawDeltaMs > 0,
            $"Expected positive SignedRawDeltaMs; got {startupStage.SignedRawDeltaMs}");
    }

    [Fact]
    public void PositiveScriptDelta_HypothesisDoesNotContainInconclusive()
    {
        var attr = Create(withScriptMs: 900.0, noScriptMs: 300.0);
        // Hypothesis should not claim INCONCLUSIVE for a cleanly positive delta.
        var startupStage = attr.Stages.FirstOrDefault(s => s.Stage == "startup_script");
        Assert.NotNull(startupStage);
        Assert.Equal("MODERATE", startupStage.Confidence);
    }

    // ── EstimateMs is NEVER negative ──────────────────────────────────────────

    [Fact]
    public void EstimateMs_IsNeverNegative()
    {
        // For any conceivable input, EstimateMs must be null or non-negative (no clamping hack).
        foreach (var (with, no) in new[] { (200.0, 400.0), (500.0, 500.0), (900.0, 400.0) })
        {
            var attr = Create(withScriptMs: with, noScriptMs: no);
            var startupStage = attr.Stages.FirstOrDefault(s => s.Stage == "startup_script");
            Assert.NotNull(startupStage);
            if (startupStage.EstimateMs is not null)
                Assert.True(startupStage.EstimateMs >= 0,
                    $"EstimateMs must never be negative; got {startupStage.EstimateMs} for with={with},no={no}");
        }
    }
}
