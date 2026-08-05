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

    // ── Isolation pairing (#380 Decision B) ─────────────────────────────────────
    // Warm/throughput: intentional difference (baseline ephemeral vs current pool_reset).
    // Cold/memory: must match like-for-like pairing tokens.

    /// <summary>
    /// Isolation mode for warm_call_latency_ms. Baseline expects
    /// <c>ephemeral_create_dispose</c>; current expects <c>pool_reset</c>.
    /// </summary>
    [JsonPropertyName("warmCallIsolationMode")]
    public string WarmCallIsolationMode { get; set; } = "";

    /// <summary>
    /// Isolation mode for concurrent_throughput_ms. Same pairing rules as warm-call.
    /// </summary>
    [JsonPropertyName("throughputIsolationMode")]
    public string ThroughputIsolationMode { get; set; } = "";

    /// <summary>Cold-start pairing token; must match on both sides (like_for_like_cold).</summary>
    [JsonPropertyName("coldStartPairingMode")]
    public string ColdStartPairingMode { get; set; } = IsolationModes.LikeForLikeCold;

    /// <summary>Memory pairing token; must match on both sides (like_for_like_working_set).</summary>
    [JsonPropertyName("memoryPairingMode")]
    public string MemoryPairingMode { get; set; } = IsolationModes.LikeForLikeWorkingSet;

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
            // v2 current side of Decision B pairing (pool + mandatory per-call reset).
            WarmCallIsolationMode = IsolationModes.PoolReset,
            ThroughputIsolationMode = IsolationModes.PoolReset,
            ColdStartPairingMode = IsolationModes.LikeForLikeCold,
            MemoryPairingMode = IsolationModes.LikeForLikeWorkingSet,
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

        // ── Isolation pairing (#380 Decision B) ───────────────────────────────────
        // Warm/throughput: reject sticky no-reset baseline vs pool+reset current.
        // Cold/memory: like-for-like pairing tokens must match (not retargeted by isolation).
        ValidateIsolationPairing(violations, "warmCallIsolationMode",
            baseline.WarmCallIsolationMode, current.WarmCallIsolationMode);
        ValidateIsolationPairing(violations, "throughputIsolationMode",
            baseline.ThroughputIsolationMode, current.ThroughputIsolationMode);
        MustMatch(violations, "coldStartPairingMode", baseline.ColdStartPairingMode, current.ColdStartPairingMode);
        MustMatch(violations, "memoryPairingMode", baseline.MemoryPairingMode, current.MemoryPairingMode);

        // ── Must-match: iteration counts (per gated scenario) ────────────────────
        // Fail-closed: ALL keys from both sides must be present and match (#380 AC3 fix).
        var allIterKeys = new HashSet<string>(baseline.MeasuredIterations.Keys);
        allIterKeys.UnionWith(current.MeasuredIterations.Keys);
        foreach (var key in allIterKeys)
        {
            var bHas = baseline.MeasuredIterations.TryGetValue(key, out var bVal);
            var cHas = current.MeasuredIterations.TryGetValue(key, out var cVal);
            if (!bHas)
                violations.Add($"measuredIterations[{key}]: missing from baseline (present in current={cVal})");
            else if (!cHas)
                violations.Add($"measuredIterations[{key}]: missing from current (present in baseline={bVal})");
            else if (bVal != cVal)
                violations.Add($"measuredIterations[{key}]: baseline={bVal}, current={cVal}");
        }

        // Fail-closed: ALL warmup keys from both sides must be present and match.
        var allWarmupKeys = new HashSet<string>(baseline.WarmupCounts.Keys);
        allWarmupKeys.UnionWith(current.WarmupCounts.Keys);
        foreach (var key in allWarmupKeys)
        {
            var bHas = baseline.WarmupCounts.TryGetValue(key, out var bW);
            var cHas = current.WarmupCounts.TryGetValue(key, out var cW);
            if (!bHas)
                violations.Add($"warmupCounts[{key}]: missing from baseline (present in current={cW})");
            else if (!cHas)
                violations.Add($"warmupCounts[{key}]: missing from current (present in baseline={bW})");
            else if (bW != cW)
                violations.Add($"warmupCounts[{key}]: baseline={bW}, current={cW}");
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

        // ── Previously unchecked fields: now fully validated (#380 AC4 Revision 3) ──────────────

        // timingResolutionNs: ENV-derived (skip if either is 0 — pre-field-capture format).
        EnvDerivedMatchLong(violations, "timingResolutionNs", baseline.TimingResolutionNs, current.TimingResolutionNs);

        // startupScriptEnabled/effectivePoolSettings/contractVersion: must match (same methodology).
        MustMatchBool(violations, "startupScriptEnabled", baseline.StartupScriptEnabled, current.StartupScriptEnabled);
        MustMatch(violations, "effectivePoolSettings", baseline.EffectivePoolSettings, current.EffectivePoolSettings);
        MustMatch(violations, "contractVersion", baseline.ContractVersion, current.ContractVersion);

        // productOrder/modeOrder: per-attempt metadata that legitimately differs between attempts.
        // Validate each side independently: must be non-empty (if set) AND a known value.
        ValidateKnownValue(violations, "baseline.productOrder", baseline.ProductOrder,
            ["baseline_first", "current_first"]);
        ValidateKnownValue(violations, "current.productOrder", current.ProductOrder,
            ["baseline_first", "current_first"]);
        ValidateKnownValue(violations, "baseline.modeOrder", baseline.ModeOrder,
            ["stateless_first", "stateful_first", "unknown"]);
        ValidateKnownValue(violations, "current.modeOrder", current.ModeOrder,
            ["stateless_first", "stateful_first", "unknown"]);

        // sourceCommitSha: both must be non-empty AND must differ (comparing different code versions).
        if (string.IsNullOrEmpty(baseline.SourceCommitSha))
            violations.Add("sourceCommitSha: baseline is empty — provenance not captured");
        if (string.IsNullOrEmpty(current.SourceCommitSha))
            violations.Add("sourceCommitSha: current is empty — provenance not captured");
        if (!string.IsNullOrEmpty(baseline.SourceCommitSha) &&
            !string.IsNullOrEmpty(current.SourceCommitSha) &&
            string.Equals(baseline.SourceCommitSha, current.SourceCommitSha, StringComparison.OrdinalIgnoreCase))
            violations.Add($"sourceCommitSha: baseline and current are identical ('{baseline.SourceCommitSha}') — not a different-commit comparison");

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

    private static void MustMatchBool(List<string> violations, string field, bool baseline, bool current)
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

    /// <summary>
    /// Validates that a per-side field contains a known value.
    /// Empty/missing fields are skipped when they represent an allowed capture gap.
    /// </summary>
    private static void ValidateKnownValue(
        List<string> violations,
        string label,
        string value,
        string[] knownValues)
    {
        // Empty value treated as a capture gap (env var not set) — not a violation.
        if (string.IsNullOrEmpty(value)) return;
        if (!Array.Exists(knownValues, k => string.Equals(k, value, StringComparison.Ordinal)))
            violations.Add(
                $"{label}: '{value}' is not a recognized value " +
                $"(expected one of: {string.Join(", ", knownValues)})");
    }

    /// <summary>
    /// Enforces #380 Decision B warm/throughput isolation pairing.
    /// Baseline must be isolation-equivalent (ephemeral create+dispose on real v1);
    /// current must be pool_reset. Sticky session_affine_no_reset is always a methodology failure.
    /// Empty on both sides is treated as a pre-Decision-B capture gap (not a violation) so
    /// legacy unit fixtures without the fields still validate other contract rules.
    /// </summary>
    private static void ValidateIsolationPairing(
        List<string> violations,
        string field,
        string baseline,
        string current)
    {
        var bEmpty = string.IsNullOrWhiteSpace(baseline);
        var cEmpty = string.IsNullOrWhiteSpace(current);
        if (bEmpty && cEmpty)
            return; // pre-Decision-B capture gap

        if (bEmpty)
        {
            violations.Add(
                $"{field}: baseline isolation mode is missing while current='{current}'. " +
                $"Baseline warm/throughput must record '{IsolationModes.EphemeralCreateDispose}' (#380 Decision B).");
            return;
        }

        if (cEmpty)
        {
            violations.Add(
                $"{field}: current isolation mode is missing while baseline='{baseline}'. " +
                $"Current warm/throughput must record '{IsolationModes.PoolReset}' (#380 Decision B).");
            return;
        }

        if (string.Equals(baseline, IsolationModes.SessionAffineNoReset, StringComparison.Ordinal))
        {
            violations.Add(
                $"{field}: baseline='{baseline}' is sticky no-reset — apples-to-oranges vs v2 pool+reset. " +
                $"Use isolation-equivalent '{IsolationModes.EphemeralCreateDispose}' (#380 Decision B).");
        }
        else if (!IsolationModes.IsIsolationEquivalentBaseline(baseline))
        {
            violations.Add(
                $"{field}: baseline='{baseline}', expected isolation-equivalent " +
                $"'{IsolationModes.EphemeralCreateDispose}' (#380 Decision B).");
        }

        if (!string.Equals(current, IsolationModes.PoolReset, StringComparison.Ordinal))
        {
            violations.Add(
                $"{field}: current='{current}', expected '{IsolationModes.PoolReset}' " +
                $"(v2 mandatory per-call isolation; #380 Decision B).");
        }
    }
}
