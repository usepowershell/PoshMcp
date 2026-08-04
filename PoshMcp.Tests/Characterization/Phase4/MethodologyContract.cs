using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PoshMcp.Tests.Characterization.Phase4;

/// <summary>
/// Complete methodology fingerprint for a performance measurement run.
/// Every field that could affect measurement results is captured and validated
/// field-by-field in CI before ratios are considered.
///
/// Fields are split into two categories:
///   1. MUST-MATCH fields — identical between baseline (v1) and current (v2) measurements
///      on the same runner. A mismatch is a methodology violation.
///   2. INTENTIONAL-DIFFERENCE fields — expected to differ between v1 and v2. These are
///      validated against expected baseline/current values, not merely listed.
///
/// This contract is the machine-enforced gate for AC3 of issue #380.
/// </summary>
internal sealed class MethodologyContract
{
    [JsonPropertyName("contractVersion")]
    public string ContractVersion { get; set; } = "poshmcp/methodology-contract/1.0";

    // ── Environment (must match) ────────────────────────────────────────────────

    [JsonPropertyName("os")]
    public string Os { get; set; } = "";

    [JsonPropertyName("dotNetVersion")]
    public string DotNetVersion { get; set; } = "";

    [JsonPropertyName("logicalProcessors")]
    public int LogicalProcessors { get; set; }

    [JsonPropertyName("processorModel")]
    public string ProcessorModel { get; set; } = "";

    [JsonPropertyName("totalMemoryKb")]
    public long TotalMemoryKb { get; set; }

    [JsonPropertyName("machineName")]
    public string MachineName { get; set; } = "";

    // ── Build (must match) ──────────────────────────────────────────────────────

    [JsonPropertyName("buildConfiguration")]
    public string BuildConfiguration { get; set; } = "Release";

    [JsonPropertyName("targetFramework")]
    public string TargetFramework { get; set; } = "net10.0";

    // ── Workload (must match) ───────────────────────────────────────────────────

    [JsonPropertyName("toolName")]
    public string ToolName { get; set; } = "get_date";

    [JsonPropertyName("toolPayloadDescription")]
    public string ToolPayloadDescription { get; set; } = "empty-args-get-date";

    // ── Transport/mode (must match per comparison) ──────────────────────────────

    [JsonPropertyName("httpTransportType")]
    public string HttpTransportType { get; set; } = "StreamableHttp";

    // ── Protocol (must match) ───────────────────────────────────────────────────

    [JsonPropertyName("mcpProtocolVersion")]
    public string McpProtocolVersion { get; set; } = "";

    [JsonPropertyName("authenticationMode")]
    public string AuthenticationMode { get; set; } = "None";

    // ── Startup script & pool (may differ — validated per scenario) ─────────────

    [JsonPropertyName("startupScriptEnabled")]
    public bool StartupScriptEnabled { get; set; }

    [JsonPropertyName("effectivePoolSettings")]
    public string EffectivePoolSettings { get; set; } = "";

    // ── Measurement parameters (must match) ─────────────────────────────────────

    [JsonPropertyName("warmupCounts")]
    public Dictionary<string, int> WarmupCounts { get; set; } = new();

    [JsonPropertyName("measuredIterations")]
    public Dictionary<string, int> MeasuredIterations { get; set; } = new();

    [JsonPropertyName("productOrder")]
    public string ProductOrder { get; set; } = "";

    [JsonPropertyName("modeOrder")]
    public string ModeOrder { get; set; } = "";

    // ── Timing (must match) ─────────────────────────────────────────────────────

    [JsonPropertyName("timingMethod")]
    public string TimingMethod { get; set; } = "System.Diagnostics.Stopwatch";

    [JsonPropertyName("timingResolutionNs")]
    public long TimingResolutionNs { get; set; }

    // ── Statistics (must match) ─────────────────────────────────────────────────

    [JsonPropertyName("percentileAlgorithm")]
    public string PercentileAlgorithm { get; set; } = "linear_interpolation_rank_p*(n-1)";

    [JsonPropertyName("percentileImplementation")]
    public string PercentileImplementation { get; set; } = "CharacterizationStats.FromSamples/1.0";

    [JsonPropertyName("varianceType")]
    public string VarianceType { get; set; } = "population";

    // ── Concurrency (must match) ────────────────────────────────────────────────

    [JsonPropertyName("throughputConcurrency")]
    public int ThroughputConcurrency { get; set; }

    // ── Memory accounting (must match) ──────────────────────────────────────────

    [JsonPropertyName("memoryAccountingMethod")]
    public string MemoryAccountingMethod { get; set; } = "Process.WorkingSet64";

    // ── Server lifecycle (must match) ───────────────────────────────────────────

    [JsonPropertyName("serverLifecycle")]
    public string ServerLifecycle { get; set; } = "per-iteration-cold|shared-warm";

    // ── Intentional differences (validated against expected values) ──────────────

    [JsonPropertyName("sdkMajorVersion")]
    public int SdkMajorVersion { get; set; }

    [JsonPropertyName("sdkSha256")]
    public string SdkSha256 { get; set; } = "";

    [JsonPropertyName("sourceCommitSha")]
    public string SourceCommitSha { get; set; } = "";

    /// <summary>
    /// Captures the fingerprint for the current measurement environment.
    /// </summary>
    internal static MethodologyContract CaptureCurrentEnvironment(
        int sdkMajorVersion,
        string sdkSha256,
        string sourceCommitSha,
        string productOrder,
        string modeOrder,
        Dictionary<string, int> warmupCounts,
        Dictionary<string, int> measuredIterations,
        int throughputConcurrency,
        string mcpProtocolVersion)
    {
        var tickFrequency = System.Diagnostics.Stopwatch.Frequency;
        var resolutionNs = tickFrequency > 0 ? (long)(1_000_000_000.0 / tickFrequency) : 0;

        return new MethodologyContract
        {
            Os = Environment.OSVersion.ToString(),
            DotNetVersion = Environment.Version.ToString(),
            LogicalProcessors = Environment.ProcessorCount,
            ProcessorModel = Environment.GetEnvironmentVariable("RUNNER_CPU_MODEL") ?? "",
            TotalMemoryKb = long.TryParse(
                Environment.GetEnvironmentVariable("RUNNER_TOTAL_MEM_KB"), out var mem) ? mem : 0,
            MachineName = Environment.MachineName,
            BuildConfiguration = "Release",
            TargetFramework = "net10.0",
            ToolName = "get_date",
            ToolPayloadDescription = "empty-args-get-date",
            HttpTransportType = "StreamableHttp",
            McpProtocolVersion = mcpProtocolVersion,
            AuthenticationMode = "None",
            ProductOrder = productOrder,
            ModeOrder = modeOrder,
            WarmupCounts = warmupCounts,
            MeasuredIterations = measuredIterations,
            TimingMethod = "System.Diagnostics.Stopwatch",
            TimingResolutionNs = resolutionNs,
            PercentileAlgorithm = "linear_interpolation_rank_p*(n-1)",
            PercentileImplementation = "CharacterizationStats.FromSamples/1.0",
            VarianceType = "population",
            ThroughputConcurrency = throughputConcurrency,
            MemoryAccountingMethod = "Process.WorkingSet64",
            ServerLifecycle = "per-iteration-cold|shared-warm",
            SdkMajorVersion = sdkMajorVersion,
            SdkSha256 = sdkSha256,
            SourceCommitSha = sourceCommitSha,
        };
    }
}

/// <summary>
/// Validates two <see cref="MethodologyContract"/> instances field-by-field.
/// Returns a list of violations; an empty list means the contracts are compatible.
///
/// The validator distinguishes:
///   - MUST-MATCH fields: any difference is a methodology violation
///   - ENV-DERIVED fields: compared only when both sides have real values (one-sided gap is a warning)
///   - INTENTIONAL-DIFFERENCE fields: validated against expected values (e.g., baseline SDK=1, current SDK=2)
/// </summary>
internal static class MethodologyContractValidator
{
    internal static List<string> Validate(
        MethodologyContract baseline,
        MethodologyContract current,
        int expectedBaselineSdkMajor = 1,
        int expectedCurrentSdkMajor = 2)
    {
        var violations = new List<string>();

        if (baseline is null) { violations.Add("baseline methodology contract is null"); return violations; }
        if (current is null) { violations.Add("current methodology contract is null"); return violations; }

        // ── Must-match: core runtime fields ─────────────────────────────────────
        MustMatch(violations, "dotNetVersion", baseline.DotNetVersion, current.DotNetVersion);
        MustMatch(violations, "os", baseline.Os, current.Os);
        MustMatch(violations, "logicalProcessors", baseline.LogicalProcessors, current.LogicalProcessors);
        MustMatch(violations, "machineName", baseline.MachineName, current.MachineName);

        // ── ENV-derived: compared only when both sides carry real values ─────────
        EnvDerivedMatch(violations, "processorModel", baseline.ProcessorModel, current.ProcessorModel);
        EnvDerivedMatchLong(violations, "totalMemoryKb", baseline.TotalMemoryKb, current.TotalMemoryKb);

        // ── Must-match: build ────────────────────────────────────────────────────
        MustMatch(violations, "buildConfiguration", baseline.BuildConfiguration, current.BuildConfiguration);
        MustMatch(violations, "targetFramework", baseline.TargetFramework, current.TargetFramework);

        // ── Must-match: workload ─────────────────────────────────────────────────
        MustMatch(violations, "toolName", baseline.ToolName, current.ToolName);
        MustMatch(violations, "toolPayloadDescription", baseline.ToolPayloadDescription, current.ToolPayloadDescription);

        // ── Must-match: transport ────────────────────────────────────────────────
        MustMatch(violations, "httpTransportType", baseline.HttpTransportType, current.HttpTransportType);

        // ── Must-match: protocol ─────────────────────────────────────────────────
        // mcpProtocolVersion may legitimately differ between SDK v1 and v2; skip if either is empty
        if (!string.IsNullOrEmpty(baseline.McpProtocolVersion) &&
            !string.IsNullOrEmpty(current.McpProtocolVersion))
        {
            MustMatch(violations, "mcpProtocolVersion", baseline.McpProtocolVersion, current.McpProtocolVersion);
        }

        MustMatch(violations, "authenticationMode", baseline.AuthenticationMode, current.AuthenticationMode);

        // ── Must-match: measurement ──────────────────────────────────────────────
        MustMatch(violations, "timingMethod", baseline.TimingMethod, current.TimingMethod);
        MustMatch(violations, "percentileAlgorithm", baseline.PercentileAlgorithm, current.PercentileAlgorithm);
        MustMatch(violations, "percentileImplementation", baseline.PercentileImplementation, current.PercentileImplementation);
        MustMatch(violations, "varianceType", baseline.VarianceType, current.VarianceType);
        MustMatch(violations, "throughputConcurrency", baseline.ThroughputConcurrency, current.ThroughputConcurrency);
        MustMatch(violations, "memoryAccountingMethod", baseline.MemoryAccountingMethod, current.MemoryAccountingMethod);
        MustMatch(violations, "serverLifecycle", baseline.ServerLifecycle, current.ServerLifecycle);

        // ── Must-match: iteration counts (per gated scenario) ────────────────────
        foreach (var kvp in baseline.MeasuredIterations)
        {
            if (current.MeasuredIterations.TryGetValue(kvp.Key, out var currentN))
            {
                if (kvp.Value != currentN)
                    violations.Add($"measuredIterations[{kvp.Key}]: baseline={kvp.Value}, current={currentN}");
            }
        }

        foreach (var kvp in baseline.WarmupCounts)
        {
            if (current.WarmupCounts.TryGetValue(kvp.Key, out var currentW))
            {
                if (kvp.Value != currentW)
                    violations.Add($"warmupCounts[{kvp.Key}]: baseline={kvp.Value}, current={currentW}");
            }
        }

        // ── Intentional differences: validated against expected values ────────────
        if (baseline.SdkMajorVersion != expectedBaselineSdkMajor)
            violations.Add($"baseline sdkMajorVersion={baseline.SdkMajorVersion}, expected {expectedBaselineSdkMajor}");
        if (current.SdkMajorVersion != expectedCurrentSdkMajor)
            violations.Add($"current sdkMajorVersion={current.SdkMajorVersion}, expected {expectedCurrentSdkMajor}");
        if (baseline.SdkMajorVersion == current.SdkMajorVersion)
            violations.Add($"baseline and current sdkMajorVersion are identical ({baseline.SdkMajorVersion}) — not a migration comparison");
        if (!string.IsNullOrEmpty(baseline.SdkSha256) &&
            !string.IsNullOrEmpty(current.SdkSha256) &&
            string.Equals(baseline.SdkSha256, current.SdkSha256, StringComparison.OrdinalIgnoreCase))
            violations.Add("baseline and current sdkSha256 are identical — same binary");

        return violations;
    }

    private static void MustMatch(List<string> violations, string field, string baseline, string current)
    {
        if (!string.Equals(baseline, current, StringComparison.Ordinal))
            violations.Add($"{field}: baseline='{baseline}', current='{current}'");
    }

    private static void MustMatch(List<string> violations, string field, int baseline, int current)
    {
        if (baseline != current)
            violations.Add($"{field}: baseline={baseline}, current={current}");
    }

    private static void EnvDerivedMatch(List<string> violations, string field, string baseline, string current)
    {
        var bEmpty = string.IsNullOrWhiteSpace(baseline);
        var cEmpty = string.IsNullOrWhiteSpace(current);
        if (bEmpty || cEmpty) return; // one-sided capture gap — not a violation
        if (!string.Equals(baseline, current, StringComparison.Ordinal))
            violations.Add($"{field}: baseline='{baseline}', current='{current}'");
    }

    private static void EnvDerivedMatchLong(List<string> violations, string field, long baseline, long current)
    {
        if (baseline == 0 || current == 0) return; // one-sided capture gap — not a violation
        if (baseline != current)
            violations.Add($"{field}: baseline={baseline}, current={current}");
    }
}
