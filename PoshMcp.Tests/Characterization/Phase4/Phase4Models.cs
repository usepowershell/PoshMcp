using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PoshMcp.Tests.Characterization.Phase4;

/// <summary>
/// Root artifact for Phase 4 performance comparison.
/// Schema: poshmcp/v4-comparison/1.0
/// </summary>
internal sealed class Phase4ComparisonArtifact
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "poshmcp/v4-comparison/1.0";

    [JsonPropertyName("capturedAt")]
    public string CapturedAt { get; set; } = "";

    [JsonPropertyName("sdkPackageVersion")]
    public string SdkPackageVersion { get; set; } = "";

    /// <summary>
    /// Runtime-detected provenance of the ModelContextProtocol SDK DLL loaded by the current
    /// (Phase 4) server. Enables a machine gate that the baseline is v1 and current is v2.
    /// </summary>
    [JsonPropertyName("sdkAssembly")]
    public SdkAssemblyDescriptor? SdkAssembly { get; set; }

    [JsonPropertyName("commitSha")]
    public string CommitSha { get; set; } = "";

    [JsonPropertyName("runtimeInfo")]
    public CharacterizationRuntimeInfo RuntimeInfo { get; set; } = new();

    [JsonPropertyName("baselineProvenance")]
    public Phase4BaselineProvenance? BaselineProvenance { get; set; }

    /// <summary>
    /// True when Phase 0 and Phase 4 measurements were taken in the same CI job on the same
    /// runner process, guaranteeing hardware equivalence. False means cross-run comparison
    /// (advisory only — hardware variance may explain threshold breaches).
    /// </summary>
    [JsonPropertyName("sameJobPaired")]
    public bool SameJobPaired { get; set; }

    /// <summary>
    /// Predeclared product collection order for this attempt: "baseline_first" (v1→v2)
    /// or "current_first" (v2→v1). Required for TRUE counterbalancing (#380 AC1).
    /// </summary>
    [JsonPropertyName("plannedProductOrder")]
    public string PlannedProductOrder { get; set; } = "";

    /// <summary>
    /// Observed product collection order. Must match <see cref="PlannedProductOrder"/>;
    /// a mismatch is a methodology violation.
    /// </summary>
    [JsonPropertyName("observedProductOrder")]
    public string ObservedProductOrder { get; set; } = "";

    /// <summary>
    /// Transport mode collection order for this attempt: "stateless_first" or "stateful_first".
    /// </summary>
    [JsonPropertyName("modeOrder")]
    public string ModeOrder { get; set; } = "";

    /// <summary>
    /// Machine-enforced methodology fingerprint contract (#380 AC3).
    /// Validated field-by-field in CI before ratios are considered.
    /// </summary>
    [JsonPropertyName("methodologyContract")]
    public MethodologyContract? MethodologyContract { get; set; }

    /// <summary>
    /// Baseline methodology contract for cross-validation.
    /// </summary>
    [JsonPropertyName("baselineMethodologyContract")]
    public MethodologyContract? BaselineMethodologyContract { get; set; }

    /// <summary>
    /// Methodology contract validation results. Empty = all passed.
    /// </summary>
    [JsonPropertyName("methodologyValidation")]
    public List<string> MethodologyValidation { get; set; } = [];

    /// <summary>
    /// Statistical reports for warm-call and throughput with CV/range/confidence (#380 AC5).
    /// </summary>
    [JsonPropertyName("statisticalReports")]
    public List<StatisticalReport> StatisticalReports { get; set; } = [];

    /// <summary>
    /// Stage attribution estimates (#380 AC6). Hypotheses, not assertions.
    /// </summary>
    [JsonPropertyName("stageAttributions")]
    public List<StageAttribution> StageAttributions { get; set; } = [];

    [JsonPropertyName("modes")]
    public List<Phase4ModeComparison> Modes { get; set; } = [];

    /// <summary>
    /// Human-readable diagnostic warnings (e.g. environment mismatch, partial failure reasons).
    /// Empty list means no warnings.
    /// </summary>
    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// Provenance of pre-collected V2 samples consumed in deferred comparison (#380 AC1).
    /// Null in baseline_first attempts (direct measurement, no preloading).
    /// </summary>
    [JsonPropertyName("preloadedSampleProvenance")]
    public PreloadedSampleProvenance? PreloadedSampleProvenance { get; set; }

    [JsonPropertyName("overallPassed")]
    public bool OverallPassed { get; set; }

    /// <summary>
    /// 0 = all gates passed, 1 = one or more threshold breaches, 2 = invalid/missing inputs.
    /// </summary>
    [JsonPropertyName("exitCode")]
    public int ExitCode { get; set; }
}

/// <summary>
/// Provenance record for V2 samples pre-collected in collect-only mode and consumed
/// in a subsequent deferred comparison step. Enables audit of the actual sample source.
/// </summary>
internal sealed class PreloadedSampleProvenance
{
    /// <summary>Path of the collect-only artifact file that was loaded.</summary>
    [JsonPropertyName("artifactPath")]
    public string ArtifactPath { get; set; } = "";

    /// <summary>
    /// SHA-256 of the collect-only artifact at load time.
    /// Validated against PHASE4_COLLECT_ONLY_{MODE}_SHA256 env var to detect overwrites.
    /// </summary>
    [JsonPropertyName("artifactSha256")]
    public string ArtifactSha256 { get; set; } = "";

    /// <summary>
    /// Expected SHA-256 from the workflow env var. Empty if env var was not set (no hash validation).
    /// </summary>
    [JsonPropertyName("expectedSha256")]
    public string ExpectedSha256 { get; set; } = "";

    /// <summary>CapturedAt timestamp from the collect-only artifact.</summary>
    [JsonPropertyName("collectOnlyCapturedAt")]
    public string CollectOnlyCapturedAt { get; set; } = "";

    /// <summary>Mode label this provenance record applies to (e.g. "stateless").</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "";
}

/// <summary>
/// Identifies the Phase 0 JSON artifact used as the comparison baseline.
/// </summary>
internal sealed class Phase4BaselineProvenance
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "";

    [JsonPropertyName("capturedAt")]
    public string CapturedAt { get; set; } = "";

    [JsonPropertyName("sdkPackageVersion")]
    public string SdkPackageVersion { get; set; } = "";

    /// <summary>Runtime-detected SDK provenance of the baseline (v1) server binary.</summary>
    [JsonPropertyName("sdkAssembly")]
    public SdkAssemblyDescriptor? SdkAssembly { get; set; }

    [JsonPropertyName("runtimeInfo")]
    public CharacterizationRuntimeInfo? RuntimeInfo { get; set; }

    [JsonPropertyName("artifactRunId")]
    public string ArtifactRunId { get; set; } = "";

    [JsonPropertyName("artifactSource")]
    public string ArtifactSource { get; set; } = "";
}

/// <summary>
/// All measurements and threshold results for one transport mode.
/// </summary>
internal sealed class Phase4ModeComparison
{
    [JsonPropertyName("transportMode")]
    public string TransportMode { get; set; } = "";

    [JsonPropertyName("scenarios")]
    public List<CharacterizationScenario> Scenarios { get; set; } = [];

    [JsonPropertyName("thresholdChecks")]
    public List<Phase4ThresholdCheck> ThresholdChecks { get; set; } = [];

    [JsonPropertyName("allPassed")]
    public bool AllPassed { get; set; }
}

/// <summary>
/// One threshold comparison with pass/fail verdict.
/// </summary>
internal sealed class Phase4ThresholdCheck
{
    /// <summary>
    /// Dotted metric path, e.g. <c>cold_start_http_no_script.p95</c>.
    /// </summary>
    [JsonPropertyName("metric")]
    public string Metric { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("unit")]
    public string Unit { get; set; } = "milliseconds";

    [JsonPropertyName("baselineValue")]
    public double BaselineValue { get; set; }

    [JsonPropertyName("measuredValue")]
    public double MeasuredValue { get; set; }

    /// <summary>
    /// Actual ratio: measuredValue / baselineValue.
    /// Values ≤ maxRatio pass; values > maxRatio breach the gate.
    /// </summary>
    [JsonPropertyName("ratio")]
    public double Ratio { get; set; }

    /// <summary>
    /// Maximum allowed ratio. All gates use the convention that ratio ≤ maxRatio = pass.
    /// For throughput this encodes "≥ 95% throughput" as "≤ 1/0.95 wall-clock ratio".
    /// </summary>
    [JsonPropertyName("maxRatio")]
    public double MaxRatio { get; set; }

    [JsonPropertyName("passed")]
    public bool Passed { get; set; }
}
