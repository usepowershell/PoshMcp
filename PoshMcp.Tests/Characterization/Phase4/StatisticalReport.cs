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
///   1. End-to-end warm call — directly measured (N=50, the gated metric)
///   2. Connection/initialization overhead — first-call vs steady-state delta
///   3. Lease acquisition — requires server-side instrumentation (documented)
///   4. PowerShell execution — requires server-side instrumentation (documented)
///   5. Reset/return — requires server-side instrumentation (documented)
///   6. Startup/eager — cold-start no-script median
///   7. Startup/script — cold with-script minus cold no-script
///
/// Attribution method: uses warm-call warmup data (first call includes connection setup/JIT)
/// versus steady-state calls to separate connection overhead from per-call cost. Cold-start
/// with/without script provides startup attribution.
///
/// Per-call stages (lease/PS/reset) within the warm path require server-side Stopwatch
/// instrumentation that would perturb primary gate measurements. They are reported as a
/// combined "warm_call_per_request" total with individual stages documented as requiring
/// dedicated diagnostic instrumentation.
/// </summary>
internal sealed class StageAttribution
{
    [JsonPropertyName("transportMode")]
    public string TransportMode { get; set; } = "";

    /// <summary>Total warm-call latency (steady-state). Gated metric.</summary>
    [JsonPropertyName("totalWarmCallMs")]
    public StatisticalReport? TotalWarmCallMs { get; set; }

    /// <summary>
    /// First warm-call latency (includes connection setup, TLS handshake, session init, JIT).
    /// Compared to steady-state median to estimate connection/init overhead.
    /// </summary>
    [JsonPropertyName("firstCallMs")]
    public double FirstCallMs { get; set; }

    /// <summary>
    /// Connection/initialization overhead estimate: first warm-call minus steady-state median.
    /// Represents one-time setup cost amortized away in persistent-connection steady state.
    /// </summary>
    [JsonPropertyName("connectionOverheadEstimateMs")]
    public double ConnectionOverheadEstimateMs { get; set; }

    /// <summary>
    /// Steady-state per-request cost: the warm-call median (connection already established).
    /// This represents: HTTP request/response + MCP framing + lease + PS execute + reset/return.
    /// </summary>
    [JsonPropertyName("steadyStatePerRequestMs")]
    public double SteadyStatePerRequestMs { get; set; }

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
    /// Stages whose per-call overhead cannot be separated non-perturbingly.
    /// Server-side Stopwatch instrumentation is required — documented for future work.
    /// </summary>
    [JsonPropertyName("requiresServerInstrumentation")]
    public List<string> RequiresServerInstrumentation { get; set; } = [];

    /// <summary>
    /// In-process pool metrics from a separate diagnostic pass using StatelessRunspacePool
    /// with MeterListener. NOT the gated subprocess measurement. Directional evidence only.
    /// Collection uses poshmcp.runspace_pool.acquisition/lease/reset_duration_seconds instruments.
    /// </summary>
    [JsonPropertyName("inProcessPoolDiagnostic")]
    public PoolDiagnosticData? InProcessPoolDiagnostic { get; set; }

    /// <summary>
    /// Measurement overhead bound: the warm-call inter-quartile range provides
    /// a bound on measurement noise within the primary metric.
    /// </summary>
    [JsonPropertyName("measurementOverheadBoundMs")]
    public double MeasurementOverheadBoundMs { get; set; }

    /// <summary>
    /// Creates stage attribution from warm-call samples and cold-start samples.
    /// Uses first-call vs steady-state to separate connection overhead.
    /// Does NOT use GET /health (different endpoint with different connection behavior).
    /// </summary>
    internal static StageAttribution Create(
        string transportMode,
        double[] warmCallSamples,
        double[] coldStartWithScriptSamples,
        double[] coldStartNoScriptSamples,
        PoolDiagnosticData? poolDiagnostic = null)
    {
        var mode = transportMode.ToLowerInvariant();
        var warmReport = StatisticalReport.FromSamples(
            $"warm_call_latency_ms_{mode}", "milliseconds", warmCallSamples);

        // First call includes connection setup; steady-state (median of remaining) is per-request
        var firstCall = warmCallSamples.Length > 0 ? warmCallSamples[0] : double.NaN;
        var steadyState = warmReport.Median;
        var connectionOverhead = warmCallSamples.Length > 1
            ? firstCall - steadyState
            : double.NaN;

        // Cold-start stage attribution (startup/eager/script)
        // signedScriptDeltaMs: signed raw delta, always preserved regardless of sign.
        // startupScriptMs: null when delta is negative (physically implausible — INCONCLUSIVE).
        double? signedScriptDeltaMs = null;
        double? startupScriptMs = null;
        double? serverStartupMs = null;
        StatisticalReport? withScriptReport = null;
        StatisticalReport? noScriptReport = null;
        if (coldStartWithScriptSamples.Length > 0 && coldStartNoScriptSamples.Length > 0)
        {
            withScriptReport = StatisticalReport.FromSamples(
                $"cold_start_with_script_{mode}", "milliseconds", coldStartWithScriptSamples);
            noScriptReport = StatisticalReport.FromSamples(
                $"cold_start_no_script_{mode}", "milliseconds", coldStartNoScriptSamples);
            var rawDelta = withScriptReport.Median - noScriptReport.Median;
            signedScriptDeltaMs = rawDelta;
            if (rawDelta >= 0)
                startupScriptMs = rawDelta;
            serverStartupMs = noScriptReport.Median;
        }

        // Build the per-stage detail enumeration
        var stages = new List<StageDetail>
        {
            new()
            {
                Stage = "end_to_end_warm_call",
                Description = "Complete MCP tools/call round-trip on persistent connection (HTTP + MCP + lease + PS + reset)",
                Method = "direct_measurement",
                EstimateMs = steadyState,
                Confidence = warmReport.Confidence,
                Source = $"warm_call_latency_ms_{mode} (N={warmReport.SampleCount}, steady-state median)",
            },
            new()
            {
                Stage = "connection_initialization",
                Description = "One-time connection setup: TCP/TLS handshake + MCP session init + first JIT",
                Method = "subtraction: first_warm_call - steady_state_median",
                EstimateMs = double.IsNaN(connectionOverhead) ? null : connectionOverhead,
                Confidence = warmCallSamples.Length >= 10 ? "MODERATE" : "INSUFFICIENT",
                Source = $"first_warm_call ({firstCall:F2}ms) - median ({steadyState:F2}ms)",
            },
            new()
            {
                Stage = "lease_acquisition",
                Description = "Runspace pool acquire time. " +
                    "In-process diagnostic pool (poshmcp.runspace_pool.acquisition_duration_seconds). " +
                    "NOT the gated subprocess measurement — directional evidence only.",
                Method = poolDiagnostic?.AcquisitionMs is { Length: > 0 }
                    ? "diagnostic_in_process_pool"
                    : "requires_server_instrumentation",
                EstimateMs = poolDiagnostic?.AcquisitionMs is { Length: > 0 }
                    ? StatisticalReport.ComputeMedian([.. poolDiagnostic.AcquisitionMs.OrderBy(x => x)])
                    : (double?)null,
                Confidence = poolDiagnostic?.AcquisitionMs is { Length: > 0 } ? "DIAGNOSTIC_IN_PROCESS" : "NOT_AVAILABLE",
                Source = poolDiagnostic?.AcquisitionMs is { Length: > 0 }
                    ? string.Format(CultureInfo.InvariantCulture,
                        "poshmcp.runspace_pool.acquisition_duration_seconds N={0} (in-process diagnostic, separate from gated N={1} subprocess measurement)",
                        poolDiagnostic.AcquisitionMs.Length, warmCallSamples.Length)
                    : "Requires poshmcp.runspace_pool.acquisition_duration_seconds from server process; " +
                      "cross-process metric collection not yet wired (CharacterizationHttpServer is a subprocess). " +
                      "Included in end_to_end_warm_call.",
            },
            new()
            {
                Stage = "powershell_execution",
                Description = "PowerShell command invocation time: lease_duration - reset_duration from in-process diagnostic. " +
                    "NOT the gated subprocess measurement.",
                Method = poolDiagnostic?.PsExecutionMs is { Length: > 0 }
                    ? "diagnostic_in_process: lease_duration_seconds - reset_duration_seconds"
                    : "requires_server_instrumentation",
                EstimateMs = poolDiagnostic?.PsExecutionMs is { Length: > 0 }
                    ? StatisticalReport.ComputeMedian([.. poolDiagnostic.PsExecutionMs.OrderBy(x => x)])
                    : (double?)null,
                Confidence = poolDiagnostic?.PsExecutionMs is { Length: > 0 } ? "DIAGNOSTIC_IN_PROCESS" : "NOT_AVAILABLE",
                Source = poolDiagnostic?.PsExecutionMs is { Length: > 0 }
                    ? string.Format(CultureInfo.InvariantCulture,
                        "lease_duration - reset_duration N={0} (in-process diagnostic)",
                        poolDiagnostic.PsExecutionMs.Length)
                    : "Requires per-call lease_duration and reset_duration from server process. " +
                      "Included in end_to_end_warm_call.",
            },
            new()
            {
                Stage = "reset_return",
                Description = "Pool reset protocol duration after lease return. " +
                    "In-process diagnostic pool (poshmcp.runspace_pool.reset_duration_seconds). " +
                    "NOT the gated subprocess measurement.",
                Method = poolDiagnostic?.ResetMs is { Length: > 0 }
                    ? "diagnostic_in_process_pool"
                    : "requires_server_instrumentation",
                EstimateMs = poolDiagnostic?.ResetMs is { Length: > 0 }
                    ? StatisticalReport.ComputeMedian([.. poolDiagnostic.ResetMs.OrderBy(x => x)])
                    : (double?)null,
                Confidence = poolDiagnostic?.ResetMs is { Length: > 0 } ? "DIAGNOSTIC_IN_PROCESS" : "NOT_AVAILABLE",
                Source = poolDiagnostic?.ResetMs is { Length: > 0 }
                    ? string.Format(CultureInfo.InvariantCulture,
                        "poshmcp.runspace_pool.reset_duration_seconds N={0} (in-process diagnostic)",
                        poolDiagnostic.ResetMs.Length)
                    : "Requires poshmcp.runspace_pool.reset_duration_seconds from server process. " +
                      "Included in end_to_end_warm_call.",
            },
        };

        if (serverStartupMs.HasValue)
        {
            stages.Add(new StageDetail
            {
                Stage = "startup_eager",
                Description = "Server process start + module import + MCP initialize + first tools/call (no startup script)",
                Method = "direct_measurement",
                EstimateMs = serverStartupMs.Value,
                Confidence = coldStartNoScriptSamples.Length >= 3 ? "MODERATE" : "INSUFFICIENT",
                Source = $"cold_start_http_no_script_{mode} median (N={coldStartNoScriptSamples.Length})",
            });
        }

        if (signedScriptDeltaMs.HasValue)
        {
            var delta = signedScriptDeltaMs.Value;
            bool positive = delta >= 0;
            stages.Add(new StageDetail
            {
                Stage = "startup_script",
                Description = "Startup-script execution cost (module import + inline script). " +
                    "Estimated as cold_with_script.median - cold_no_script.median. " +
                    "Negative signed delta indicates arithmetic inconsistency (noise or small N); " +
                    "cannot support causal attribution and is NOT clamped to zero.",
                Method = "subtraction: cold_with_script.median - cold_no_script.median",
                SignedRawDeltaMs = delta,
                EstimateMs = positive ? delta : (double?)null,
                Confidence = !positive
                    ? "INCONCLUSIVE"
                    : (coldStartWithScriptSamples.Length >= 3 && coldStartNoScriptSamples.Length >= 3
                        ? "MODERATE"
                        : "INSUFFICIENT"),
                Source = positive
                    ? string.Format(CultureInfo.InvariantCulture,
                        "cold_start_http_with_script_{0} ({1:F2}ms) - cold_start_http_no_script_{0} ({2:F2}ms) = {3:F2}ms",
                        mode, withScriptReport?.Median ?? double.NaN, noScriptReport?.Median ?? double.NaN, delta)
                    : string.Format(CultureInfo.InvariantCulture,
                        "cold_start_http_with_script_{0} ({1:F2}ms) - cold_start_http_no_script_{0} ({2:F2}ms) = {3:F2}ms " +
                        "(NEGATIVE: arithmetic inconsistency — signed delta preserved; " +
                        "estimateMs is null; cannot claim script cost from negative result)",
                        mode, withScriptReport?.Median ?? double.NaN, noScriptReport?.Median ?? double.NaN, delta),
            });
        }

        string confidence;
        if (warmReport.Confidence == "HIGH")
            confidence = "HIGH — warm-call measurement is stable";
        else if (warmReport.Confidence == "INSUFFICIENT")
            confidence = "INSUFFICIENT — too few warm-call samples";
        else
            confidence = "MODERATE — warm-call has notable variance";

        var hypothesis = string.Format(CultureInfo.InvariantCulture,
            "HYPOTHESIS: Steady-state warm-call is {0:F2}ms (N={1}). " +
            "First-call overhead is {2:F2}ms (connection/init). " +
            "Per-request cost (lease+PS+reset) is within the {0:F2}ms total but " +
            "individual stages require server-side instrumentation to separate.",
            steadyState, warmReport.SampleCount,
            double.IsNaN(connectionOverhead) ? 0.0 : connectionOverhead);

        if (signedScriptDeltaMs.HasValue && serverStartupMs.HasValue)
        {
            if (signedScriptDeltaMs.Value < 0)
                hypothesis += string.Format(CultureInfo.InvariantCulture,
                    " Cold-start startup-script: INCONCLUSIVE (signed delta={0:F2}ms, negative — arithmetic inconsistency, not a causal estimate).",
                    signedScriptDeltaMs.Value);
            else
                hypothesis += string.Format(CultureInfo.InvariantCulture,
                    " Cold-start: ~{0:F0}ms server startup/eager, ~{1:F0}ms startup script.",
                    serverStartupMs.Value, signedScriptDeltaMs.Value);
        }

        // IQR as measurement noise bound
        var iqrBound = 0.0;
        if (warmCallSamples.Length >= 4)
        {
            var sorted = warmCallSamples.OrderBy(x => x).ToArray();
            var q1Idx = sorted.Length / 4;
            var q3Idx = 3 * sorted.Length / 4;
            iqrBound = sorted[q3Idx] - sorted[q1Idx];
        }

        return new StageAttribution
        {
            TransportMode = transportMode,
            TotalWarmCallMs = warmReport,
            FirstCallMs = firstCall,
            ConnectionOverheadEstimateMs = double.IsNaN(connectionOverhead) ? 0.0 : connectionOverhead,
            SteadyStatePerRequestMs = steadyState,
            StartupScriptEstimateMs = startupScriptMs,
            ServerStartupEstimateMs = serverStartupMs,
            Stages = stages,
            InProcessPoolDiagnostic = poolDiagnostic,
            AttributionMethod = "First-call vs steady-state delta for connection overhead; " +
                "cold-start with/without script for startup stages; " +
                "lease/PS/reset via in-process StatelessRunspacePool MeterListener diagnostic (separate N, separate pool)",
            AttributionConfidence = confidence,
            Hypothesis = hypothesis,
            RequiresServerInstrumentation =
            [
                "subprocess_lease_acquisition — poshmcp.runspace_pool.acquisition_duration_seconds cross-process collection requires OTLP/Prometheus endpoint on CharacterizationHttpServer",
                "subprocess_powershell_execution — lease_duration - reset_duration for the actual gated subprocess server",
                "subprocess_reset_return — poshmcp.runspace_pool.reset_duration_seconds from the gated subprocess server",
            ],
            MeasurementOverheadBoundMs = iqrBound,
        };
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

    /// <summary>Estimated milliseconds. Null when not separable or when estimate is INCONCLUSIVE.</summary>
    [JsonPropertyName("estimateMs")]
    public double? EstimateMs { get; set; }

    /// <summary>
    /// Signed raw delta in milliseconds. Always preserved regardless of sign.
    /// Present for subtraction-derived stages (startup_script). Null for directly-measured stages.
    /// A negative value means the arithmetic result is implausible; EstimateMs will be null.
    /// </summary>
    [JsonPropertyName("signedRawDeltaMs")]
    public double? SignedRawDeltaMs { get; set; }

    /// <summary>Confidence level for this estimate.</summary>
    [JsonPropertyName("confidence")]
    public string Confidence { get; set; } = "";

    /// <summary>Data source / computation description.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";
}

/// <summary>
/// Raw pool-stage metrics from a separate in-process diagnostic pass using
/// <see cref="PoshMcp.Server.PowerShell.Pool.StatelessRunspacePool"/> and
/// <see cref="System.Diagnostics.Metrics.MeterListener"/>.
///
/// IMPORTANT: These measurements are from an in-process diagnostic pool, NOT the
/// subprocess server used for the gated N=50 measurements. They are directional
/// evidence for acquisition/lease/reset stage durations under clean conditions,
/// without HTTP/MCP overhead. Do not use as a direct substitute for subprocess metrics.
/// </summary>
internal sealed class PoolDiagnosticData
{
    /// <summary>
    /// Raw acquisition durations in milliseconds (converted from seconds).
    /// From poshmcp.runspace_pool.acquisition_duration_seconds.
    /// </summary>
    [JsonPropertyName("acquisitionMs")]
    public double[] AcquisitionMs { get; init; } = [];

    /// <summary>
    /// Raw lease hold durations in milliseconds (converted from seconds).
    /// From poshmcp.runspace_pool.lease_duration_seconds.
    /// Includes PowerShell execution + any post-invoke overhead before lease return.
    /// </summary>
    [JsonPropertyName("leaseMs")]
    public double[] LeaseMs { get; init; } = [];

    /// <summary>
    /// Raw reset durations in milliseconds (converted from seconds).
    /// From poshmcp.runspace_pool.reset_duration_seconds.
    /// </summary>
    [JsonPropertyName("resetMs")]
    public double[] ResetMs { get; init; } = [];

    /// <summary>
    /// Per-call PowerShell execution estimates: leaseMs[i] - resetMs[i].
    /// Arithmetic derived from direct measurements. Negative values excluded.
    /// </summary>
    [JsonPropertyName("psExecutionMs")]
    public double[] PsExecutionMs { get; init; } = [];

    /// <summary>Number of warmup calls excluded from captured measurements.</summary>
    [JsonPropertyName("warmupCallCount")]
    public int WarmupCallCount { get; init; }

    /// <summary>Number of diagnostic calls included in captured measurements.</summary>
    [JsonPropertyName("measuredCallCount")]
    public int MeasuredCallCount { get; init; }

    /// <summary>Total elapsed time for the diagnostic phase in seconds.</summary>
    [JsonPropertyName("totalDiagnosticSeconds")]
    public double TotalDiagnosticSeconds { get; init; }

    /// <summary>
    /// Caveat note documenting the diagnostic methodology and its differences from
    /// the gated subprocess measurement.
    /// </summary>
    [JsonPropertyName("note")]
    public string Note { get; init; } = "";
}
