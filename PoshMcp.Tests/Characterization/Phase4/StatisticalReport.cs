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
/// Non-perturbing stage attribution across the required stages (#380 AC6):
///   1. HTTP/MCP round trip — measured directly via GET /health (no MCP/PS)
///   2. Lease acquisition — estimated from MCP overhead minus PS execution
///   3. PowerShell execution — not directly separable without perturbing instrumentation
///   4. Reset/return — included in MCP overhead estimate (not separable non-perturbingly)
///   5. Startup/eager/script — measured via cold-start with-script vs without-script delta
///
/// Attribution method: subtraction using non-perturbing diagnostic snapshots.
/// Each stage is a HYPOTHESIS-labeled estimate; overhead of the measurement itself
/// is bounded by the HTTP health-check variance (sub-millisecond on CI runners).
///
/// Stages that cannot be separated non-perturbingly (lease acquisition, PS execution,
/// reset/return) are reported as a combined "mcpOverhead" bucket with an explicit
/// "notSeparable" list. Adding per-stage instrumentation (e.g., Stopwatch around
/// lease acquire) would perturb the measured path and is intentionally omitted.
/// </summary>
internal sealed class StageAttribution
{
    [JsonPropertyName("transportMode")]
    public string TransportMode { get; set; } = "";

    /// <summary>Pure HTTP round-trip latency (GET /health, no MCP/PS). Diagnostic, non-gated.</summary>
    [JsonPropertyName("httpRoundtripMs")]
    public StatisticalReport? HttpRoundtripMs { get; set; }

    /// <summary>Total warm-call latency. Gated metric.</summary>
    [JsonPropertyName("totalWarmCallMs")]
    public StatisticalReport? TotalWarmCallMs { get; set; }

    /// <summary>
    /// HYPOTHESIS: MCP framing + lease acquisition + PS execution + reset/return.
    /// Estimated as totalWarmCall.median - httpRoundtrip.median.
    /// This is NOT a direct measurement; it aggregates all non-HTTP overhead.
    /// </summary>
    [JsonPropertyName("mcpOverheadEstimateMs")]
    public double McpOverheadEstimateMs { get; set; }

    /// <summary>
    /// Startup-script execution cost estimate (cold-start only).
    /// Computed as cold_start_with_script.median - cold_start_no_script.median.
    /// Null when cold-start samples are not available.
    /// </summary>
    [JsonPropertyName("startupScriptEstimateMs")]
    public double? StartupScriptEstimateMs { get; set; }

    /// <summary>
    /// Eager-pool + server startup cost (everything except startup script).
    /// Estimated from cold_start_no_script.median (includes process start, module import,
    /// MCP initialize, first tools/call). Null when cold-start samples are not available.
    /// </summary>
    [JsonPropertyName("serverStartupEstimateMs")]
    public double? ServerStartupEstimateMs { get; set; }

    /// <summary>
    /// Enumeration of all stages required by AC6, with their attribution status.
    /// Each entry describes whether the stage is directly measured, estimated by
    /// subtraction, or not separable without perturbing instrumentation.
    /// </summary>
    [JsonPropertyName("stages")]
    public List<StageDetail> Stages { get; set; } = [];

    [JsonPropertyName("attributionMethod")]
    public string AttributionMethod { get; set; } = "";

    [JsonPropertyName("attributionConfidence")]
    public string AttributionConfidence { get; set; } = "";

    [JsonPropertyName("hypothesis")]
    public string Hypothesis { get; set; } = "";

    /// <summary>
    /// Stages whose overhead cannot be separated non-perturbingly from the MCP overhead
    /// aggregate. Adding per-stage Stopwatch instrumentation would invalidate the
    /// measured path — these are documented as combined.
    /// </summary>
    [JsonPropertyName("notSeparableWithoutPerturbation")]
    public List<string> NotSeparableWithoutPerturbation { get; set; } = [];

    /// <summary>
    /// Measurement overhead bound: the variance of the HTTP health-check diagnostic.
    /// Since the health-check shares the same HTTP transport but excludes MCP/PS/lease,
    /// its variance bounds the overhead that our attribution method adds.
    /// </summary>
    [JsonPropertyName("measurementOverheadBoundMs")]
    public double MeasurementOverheadBoundMs { get; set; }

    /// <summary>
    /// Creates a stage attribution from warm-call, HTTP health, and cold-start samples.
    /// </summary>
    internal static StageAttribution Create(
        string transportMode,
        double[] warmCallSamples,
        double[] httpHealthSamples,
        double[] coldStartWithScriptSamples,
        double[] coldStartNoScriptSamples)
    {
        var mode = transportMode.ToLowerInvariant();
        var warmReport = StatisticalReport.FromSamples(
            $"warm_call_latency_ms_{mode}", "milliseconds", warmCallSamples);
        var httpReport = StatisticalReport.FromSamples(
            $"diagnostic_http_health_ms_{mode}", "milliseconds", httpHealthSamples);

        var mcpOverhead = warmReport.Median - httpReport.Median;
        var httpPct = warmReport.Median > 0
            ? (httpReport.Median / warmReport.Median) * 100.0
            : double.NaN;
        var mcpPct = 100.0 - (double.IsNaN(httpPct) ? 0.0 : httpPct);

        // Cold-start stage attribution (startup/eager/script)
        double? startupScriptMs = null;
        double? serverStartupMs = null;
        if (coldStartWithScriptSamples.Length > 0 && coldStartNoScriptSamples.Length > 0)
        {
            var withScript = StatisticalReport.FromSamples(
                $"cold_start_with_script_{mode}", "milliseconds", coldStartWithScriptSamples);
            var noScript = StatisticalReport.FromSamples(
                $"cold_start_no_script_{mode}", "milliseconds", coldStartNoScriptSamples);
            startupScriptMs = withScript.Median - noScript.Median;
            serverStartupMs = noScript.Median;
        }

        // Build the per-stage detail enumeration
        var stages = new List<StageDetail>
        {
            new()
            {
                Stage = "http_mcp_roundtrip",
                Description = "HTTP transport round-trip (no MCP framing, no PowerShell)",
                Method = "direct_measurement",
                EstimateMs = httpReport.Median,
                Confidence = httpReport.Confidence,
                Source = $"diagnostic_http_health_ms_{mode} (GET /health, N={httpReport.SampleCount})",
            },
            new()
            {
                Stage = "mcp_overhead",
                Description = "MCP framing + lease acquisition + PowerShell execution + reset/return (combined)",
                Method = "subtraction: warmCall.median - httpHealth.median",
                EstimateMs = mcpOverhead,
                Confidence = warmReport.Confidence == "HIGH" && httpReport.Confidence == "HIGH" ? "HIGH" : "MODERATE",
                Source = $"warm_call_latency_ms_{mode} - diagnostic_http_health_ms_{mode}",
            },
            new()
            {
                Stage = "lease_acquisition",
                Description = "Runspace pool lease/acquire time",
                Method = "not_separable",
                EstimateMs = double.NaN,
                Confidence = "NOT_AVAILABLE",
                Source = "Included in mcp_overhead. Separating requires Stopwatch around RunspacePool.GetRunspaceAsync() which perturbs the measured path.",
            },
            new()
            {
                Stage = "powershell_execution",
                Description = "PowerShell command invocation (Get-Date)",
                Method = "not_separable",
                EstimateMs = double.NaN,
                Confidence = "NOT_AVAILABLE",
                Source = "Included in mcp_overhead. Separating requires Stopwatch around Pipeline.InvokeAsync() which perturbs the measured path.",
            },
            new()
            {
                Stage = "reset_return",
                Description = "Runspace reset and return to pool",
                Method = "not_separable",
                EstimateMs = double.NaN,
                Confidence = "NOT_AVAILABLE",
                Source = "Included in mcp_overhead. Separating requires Stopwatch around RunspacePool.ReleaseRunspace() which perturbs the measured path.",
            },
        };

        if (serverStartupMs.HasValue)
        {
            stages.Add(new StageDetail
            {
                Stage = "startup_eager",
                Description = "Server process start + module import + MCP initialize + first tools/call (no startup script)",
                Method = "subtraction: cold_start_no_script.median",
                EstimateMs = serverStartupMs.Value,
                Confidence = coldStartNoScriptSamples.Length >= 3 ? "MODERATE" : "INSUFFICIENT",
                Source = $"cold_start_http_no_script_{mode} (N={coldStartNoScriptSamples.Length})",
            });
        }

        if (startupScriptMs.HasValue)
        {
            stages.Add(new StageDetail
            {
                Stage = "startup_script",
                Description = "Startup-script execution cost (module import + inline script)",
                Method = "subtraction: cold_with_script.median - cold_no_script.median",
                EstimateMs = startupScriptMs.Value,
                Confidence = coldStartWithScriptSamples.Length >= 3 && coldStartNoScriptSamples.Length >= 3
                    ? "MODERATE" : "INSUFFICIENT",
                Source = $"cold_start_http_with_script_{mode} - cold_start_http_no_script_{mode}",
            });
        }

        string confidence;
        if (warmReport.Confidence == "HIGH" && httpReport.Confidence == "HIGH")
            confidence = "HIGH — both warm-call and HTTP components are stable";
        else if (warmReport.Confidence == "INSUFFICIENT" || httpReport.Confidence == "INSUFFICIENT")
            confidence = "INSUFFICIENT — one or both components have too few samples";
        else
            confidence = "MODERATE — at least one component has notable variance";

        var hypothesis = string.Format(CultureInfo.InvariantCulture,
            "HYPOTHESIS: Of {0:F1}ms total warm-call median: ~{1:F1}ms ({2:F0}%) is HTTP round-trip, " +
            "~{3:F1}ms ({4:F0}%) is MCP overhead (lease+PS+reset combined). " +
            "Lease acquisition, PS execution, and reset/return cannot be separated " +
            "without perturbing the measured path.",
            warmReport.Median, httpReport.Median, httpPct, mcpOverhead, mcpPct);

        if (startupScriptMs.HasValue && serverStartupMs.HasValue)
        {
            hypothesis += string.Format(CultureInfo.InvariantCulture,
                " Cold-start: ~{0:F0}ms server startup/eager, ~{1:F0}ms startup script.",
                serverStartupMs.Value, startupScriptMs.Value);
        }

        return new StageAttribution
        {
            TransportMode = transportMode,
            HttpRoundtripMs = httpReport,
            TotalWarmCallMs = warmReport,
            McpOverheadEstimateMs = mcpOverhead,
            StartupScriptEstimateMs = startupScriptMs,
            ServerStartupEstimateMs = serverStartupMs,
            Stages = stages,
            AttributionMethod = "subtraction using non-perturbing diagnostic snapshots; " +
                "HTTP health-check measures transport layer; cold-start with/without script " +
                "measures startup-script cost; remaining stages are combined as mcp_overhead",
            AttributionConfidence = confidence,
            Hypothesis = hypothesis,
            NotSeparableWithoutPerturbation =
            [
                "lease_acquisition — requires Stopwatch inside RunspacePool.GetRunspaceAsync()",
                "powershell_execution — requires Stopwatch inside Pipeline.InvokeAsync()",
                "reset_return — requires Stopwatch inside RunspacePool.ReleaseRunspace()",
            ],
            MeasurementOverheadBoundMs = httpReport.Range,
        };
    }

    /// <summary>
    /// Backward-compatible overload for tests that don't have cold-start samples.
    /// </summary>
    internal static StageAttribution Create(
        string transportMode,
        double[] warmCallSamples,
        double[] httpHealthSamples)
    {
        return Create(transportMode, warmCallSamples, httpHealthSamples, [], []);
    }
}

/// <summary>
/// Detail for one attribution stage: what it measures, how, and the estimate.
/// </summary>
internal sealed class StageDetail
{
    /// <summary>Stage identifier matching AC6 requirements.</summary>
    [JsonPropertyName("stage")]
    public string Stage { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>
    /// One of: "direct_measurement", "subtraction", "not_separable".
    /// "not_separable" means instrumentation would perturb the measured path.
    /// </summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    /// <summary>Estimated milliseconds. NaN when not separable.</summary>
    [JsonPropertyName("estimateMs")]
    public double EstimateMs { get; set; }

    /// <summary>Confidence level for this estimate.</summary>
    [JsonPropertyName("confidence")]
    public string Confidence { get; set; } = "";

    /// <summary>Data source / computation description.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";
}
