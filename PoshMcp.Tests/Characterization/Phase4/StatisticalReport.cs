using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;

namespace PoshMcp.Tests.Characterization.Phase4;

/// <summary>
/// Statistical summary for a performance metric across multiple attempts.
/// Reports median, range, CV, and a predeclared confidence interpretation.
///
/// Confidence method (predeclared, not post-hoc):
///   - CV ≤ 5%: HIGH confidence — measurement is stable
///   - CV ≤ 15%: MODERATE confidence — some noise, ratio is directionally reliable
///   - CV > 15%: LOW confidence — noisy, ratio may be unreliable
///   - N &lt; 3: INSUFFICIENT — too few samples for any confidence statement
///
/// Stage attribution labels (hypotheses, not assertions):
///   - http_roundtrip: pure HTTP GET /health latency (no MCP/PS)
///   - mcp_plus_ps: total warm-call minus http_roundtrip ≈ MCP framing + lease + PS execute + reset
///   - startup_eager: cold-start includes server startup + eager pool warming
///   - ps_execution: PowerShell script/command execution (estimated from warm-call - http overhead)
///
/// These are diagnostic estimates; they do NOT gate the release.
/// </summary>
internal sealed class StatisticalReport
{
    [JsonPropertyName("metric")]
    public string Metric { get; set; } = "";

    [JsonPropertyName("unit")]
    public string Unit { get; set; } = "";

    [JsonPropertyName("sampleCount")]
    public int SampleCount { get; set; }

    [JsonPropertyName("median")]
    public double Median { get; set; }

    [JsonPropertyName("min")]
    public double Min { get; set; }

    [JsonPropertyName("max")]
    public double Max { get; set; }

    [JsonPropertyName("range")]
    public double Range { get; set; }

    [JsonPropertyName("mean")]
    public double Mean { get; set; }

    [JsonPropertyName("stdDev")]
    public double StdDev { get; set; }

    /// <summary>
    /// Coefficient of variation (stdDev / mean × 100), as a percentage.
    /// NaN when mean is zero or N &lt; 2.
    /// </summary>
    [JsonPropertyName("cvPercent")]
    public double CvPercent { get; set; }

    /// <summary>
    /// Predeclared confidence level based on CV and N.
    /// One of: "HIGH", "MODERATE", "LOW", "INSUFFICIENT".
    /// </summary>
    [JsonPropertyName("confidence")]
    public string Confidence { get; set; } = "";

    [JsonPropertyName("confidenceRationale")]
    public string ConfidenceRationale { get; set; } = "";

    /// <summary>
    /// All sample values in collection order (not sorted).
    /// </summary>
    [JsonPropertyName("samples")]
    public double[] Samples { get; set; } = [];

    /// <summary>
    /// Computes a statistical report from an array of samples.
    /// </summary>
    internal static StatisticalReport FromSamples(string metric, string unit, double[] samples)
    {
        if (samples is null || samples.Length == 0)
        {
            return new StatisticalReport
            {
                Metric = metric,
                Unit = unit,
                SampleCount = 0,
                Median = double.NaN,
                Min = double.NaN,
                Max = double.NaN,
                Range = double.NaN,
                Mean = double.NaN,
                StdDev = double.NaN,
                CvPercent = double.NaN,
                Confidence = "INSUFFICIENT",
                ConfidenceRationale = "No samples provided.",
                Samples = [],
            };
        }

        var sorted = samples.OrderBy(x => x).ToArray();
        var mean = sorted.Average();
        var min = sorted[0];
        var max = sorted[^1];
        var range = max - min;

        double stdDev;
        if (sorted.Length > 1)
        {
            var variance = sorted.Sum(x => (x - mean) * (x - mean)) / sorted.Length;
            stdDev = Math.Sqrt(variance);
        }
        else
        {
            stdDev = 0.0;
        }

        var cv = (mean > 0 && sorted.Length > 1) ? (stdDev / mean) * 100.0 : double.NaN;
        var median = ComputeMedian(sorted);
        var (confidence, rationale) = ClassifyConfidence(cv, sorted.Length);

        return new StatisticalReport
        {
            Metric = metric,
            Unit = unit,
            SampleCount = sorted.Length,
            Median = median,
            Min = min,
            Max = max,
            Range = range,
            Mean = mean,
            StdDev = stdDev,
            CvPercent = cv,
            Confidence = confidence,
            ConfidenceRationale = rationale,
            Samples = samples, // preserve original order
        };
    }

    internal static double ComputeMedian(double[] sorted)
    {
        if (sorted.Length == 0) return double.NaN;
        if (sorted.Length == 1) return sorted[0];

        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2.0
            : sorted[mid];
    }

    internal static (string confidence, string rationale) ClassifyConfidence(double cvPercent, int n)
    {
        if (n < 3)
            return ("INSUFFICIENT",
                string.Format(CultureInfo.InvariantCulture,
                    "N={0} is below the minimum of 3 required for confidence classification.", n));

        if (double.IsNaN(cvPercent) || !double.IsFinite(cvPercent))
            return ("INSUFFICIENT",
                string.Format(CultureInfo.InvariantCulture,
                    "CV is not a finite number (CV={0:F1}%). Cannot classify.", cvPercent));

        if (cvPercent <= 5.0)
            return ("HIGH",
                string.Format(CultureInfo.InvariantCulture,
                    "CV={0:F1}% ≤ 5.0%. Measurement is stable; ratio is reliable.", cvPercent));

        if (cvPercent <= 15.0)
            return ("MODERATE",
                string.Format(CultureInfo.InvariantCulture,
                    "CV={0:F1}% ≤ 15.0%. Some noise present; ratio is directionally reliable.", cvPercent));

        return ("LOW",
            string.Format(CultureInfo.InvariantCulture,
                "CV={0:F1}% > 15.0%. Measurement is noisy; ratio may be unreliable. " +
                "Consider increasing N or investigating environmental variance.", cvPercent));
    }
}

/// <summary>
/// Stage attribution estimate for a warm-call measurement.
/// Each field is a hypothesis-labeled estimate, not a direct measurement.
/// </summary>
internal sealed class StageAttribution
{
    [JsonPropertyName("transportMode")]
    public string TransportMode { get; set; } = "";

    /// <summary>Pure HTTP round-trip latency (GET /health, no MCP/PS). Diagnostic, non-gated.</summary>
    [JsonPropertyName("httpRoundtripMs")]
    public StatisticalReport? HttpRoundtripMs { get; set; }

    /// <summary>
    /// Total warm-call latency. Gated metric.
    /// </summary>
    [JsonPropertyName("totalWarmCallMs")]
    public StatisticalReport? TotalWarmCallMs { get; set; }

    /// <summary>
    /// HYPOTHESIS: MCP framing + lease acquisition + PS execution + reset/return.
    /// Estimated as totalWarmCall.median - httpRoundtrip.median.
    /// This is NOT a direct measurement; instrumentation overhead could bias it.
    /// </summary>
    [JsonPropertyName("mcpPlusPsEstimateMs")]
    public double McpPlusPsEstimateMs { get; set; }

    [JsonPropertyName("attributionMethod")]
    public string AttributionMethod { get; set; } = "subtraction: warmCall.median - httpHealth.median";

    [JsonPropertyName("attributionConfidence")]
    public string AttributionConfidence { get; set; } = "";

    [JsonPropertyName("hypothesis")]
    public string Hypothesis { get; set; } = "";

    /// <summary>
    /// Creates a stage attribution from warm-call and HTTP health samples.
    /// </summary>
    internal static StageAttribution Create(
        string transportMode,
        double[] warmCallSamples,
        double[] httpHealthSamples)
    {
        var warmReport = StatisticalReport.FromSamples(
            $"warm_call_latency_ms_{transportMode.ToLowerInvariant()}", "milliseconds", warmCallSamples);
        var httpReport = StatisticalReport.FromSamples(
            $"diagnostic_http_health_ms_{transportMode.ToLowerInvariant()}", "milliseconds", httpHealthSamples);

        var mcpPlusPs = warmReport.Median - httpReport.Median;
        var httpPct = warmReport.Median > 0
            ? (httpReport.Median / warmReport.Median) * 100.0
            : double.NaN;

        string confidence;
        if (warmReport.Confidence == "HIGH" && httpReport.Confidence == "HIGH")
            confidence = "HIGH — both components are stable";
        else if (warmReport.Confidence == "INSUFFICIENT" || httpReport.Confidence == "INSUFFICIENT")
            confidence = "INSUFFICIENT — one or both components have too few samples";
        else
            confidence = "MODERATE — at least one component has notable variance";

        var hypothesis = string.Format(CultureInfo.InvariantCulture,
            "Of {0:F1}ms total warm-call median, ~{1:F1}ms ({2:F0}%) is HTTP round-trip and " +
            "~{3:F1}ms ({4:F0}%) is MCP framing + lease + PS execution + reset/return. " +
            "This is a subtraction estimate (HYPOTHESIS), not a direct stage measurement.",
            warmReport.Median,
            httpReport.Median,
            httpPct,
            mcpPlusPs,
            100.0 - (double.IsNaN(httpPct) ? 0 : httpPct));

        return new StageAttribution
        {
            TransportMode = transportMode,
            HttpRoundtripMs = httpReport,
            TotalWarmCallMs = warmReport,
            McpPlusPsEstimateMs = mcpPlusPs,
            AttributionConfidence = confidence,
            Hypothesis = hypothesis,
        };
    }
}
