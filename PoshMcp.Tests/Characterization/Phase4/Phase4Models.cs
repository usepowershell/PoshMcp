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

    [JsonPropertyName("commitSha")]
    public string CommitSha { get; set; } = "";

    [JsonPropertyName("runtimeInfo")]
    public CharacterizationRuntimeInfo RuntimeInfo { get; set; } = new();

    [JsonPropertyName("baselineProvenance")]
    public Phase4BaselineProvenance BaselineProvenance { get; set; } = new();

    [JsonPropertyName("modes")]
    public List<Phase4ModeComparison> Modes { get; set; } = [];

    /// <summary>
    /// Human-readable diagnostic warnings (e.g. environment mismatch, partial failure reasons).
    /// Empty list means no warnings.
    /// </summary>
    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = [];

    [JsonPropertyName("overallPassed")]
    public bool OverallPassed { get; set; }

    /// <summary>
    /// 0 = all gates passed, 1 = one or more threshold breaches, 2 = invalid/missing inputs.
    /// </summary>
    [JsonPropertyName("exitCode")]
    public int ExitCode { get; set; }
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
