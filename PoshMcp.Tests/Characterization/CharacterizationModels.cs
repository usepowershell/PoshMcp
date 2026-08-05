using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace PoshMcp.Tests.Characterization;

/// <summary>
/// Root artifact written once per characterization run.
/// Schema: poshmcp/v1-characterization/1.0  (see README.md in this folder).
/// </summary>
internal sealed class CharacterizationArtifact
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "poshmcp/v1-characterization/1.0";

    [JsonPropertyName("capturedAt")]
    public string CapturedAt { get; set; } = "";

    [JsonPropertyName("sdkPackageVersion")]
    public string SdkPackageVersion { get; set; } = "";

    /// <summary>
    /// Runtime-detected provenance of the ModelContextProtocol SDK DLL loaded by the measured
    /// server (name/version/path/SHA-256). Null on legacy artifacts produced before this field
    /// existed. Populated from <see cref="SdkAssemblyInfo.DetectFromMeasuredServer"/>.
    /// </summary>
    [JsonPropertyName("sdkAssembly")]
    public SdkAssemblyDescriptor? SdkAssembly { get; set; }

    /// <summary>Git commit SHA of the repository when this artifact was captured.</summary>
    [JsonPropertyName("commitSha")]
    public string CommitSha { get; set; } = "";

    [JsonPropertyName("runtimeInfo")]
    public CharacterizationRuntimeInfo RuntimeInfo { get; set; } = new();

    /// <summary>
    /// Machine-readable methodology description: tool, protocol, sample counts, warmups.
    /// Used by Phase 4 comparator to validate that Phase 0 and Phase 4 use identical
    /// measurement contracts. Missing on old artifacts (produced before this field was added);
    /// comparator falls back to per-scenario <see cref="CharacterizationScenario.Iterations"/>.
    /// </summary>
    [JsonPropertyName("methodologyFingerprint")]
    public CharacterizationMethodologyFingerprint? MethodologyFingerprint { get; set; }

    [JsonPropertyName("scenarios")]
    public List<CharacterizationScenario> Scenarios { get; set; } = [];
}

internal sealed class CharacterizationRuntimeInfo
{
    [JsonPropertyName("dotNetVersion")]
    public string DotNetVersion { get; set; } = "";

    [JsonPropertyName("os")]
    public string Os { get; set; } = "";

    [JsonPropertyName("logicalProcessors")]
    public int LogicalProcessors { get; set; }

    [JsonPropertyName("machineName")]
    public string MachineName { get; set; } = "";

    /// <summary>
    /// CPU model string. On Linux runners populated from RUNNER_CPU_MODEL env var
    /// (set by CI from /proc/cpuinfo). Empty when unavailable.
    /// </summary>
    [JsonPropertyName("processorModel")]
    public string ProcessorModel { get; set; } = "";

    /// <summary>
    /// Total physical memory in kibibytes. On Linux populated from RUNNER_TOTAL_MEM_KB
    /// env var (set by CI from /proc/meminfo). Zero when unavailable.
    /// </summary>
    [JsonPropertyName("totalMemoryKb")]
    public long TotalMemoryKb { get; set; }
}

/// <summary>
/// Canonical isolation-mode tokens recorded in methodology fingerprints (#380 Decision B).
/// Warm/throughput ratios must pair isolation-equivalent baselines to v2 pool+reset —
/// never sticky session-affine no-reset against mandatory isolation.
/// </summary>
internal static class IsolationModes
{
    /// <summary>v1 sticky session runspace reused across calls with no per-call isolation.</summary>
    public const string SessionAffineNoReset = "session_affine_no_reset";

    /// <summary>
    /// v1 isolation-equivalent path: each measured call pays runspace create+dispose
    /// (session ended after the call, or sessionless ephemeral execute).
    /// </summary>
    public const string EphemeralCreateDispose = "ephemeral_create_dispose";

    /// <summary>v2 shipped contract: shared pool worker + mandatory per-call reset.</summary>
    public const string PoolReset = "pool_reset";

    /// <summary>Cold-start pairing: full process start + initialize + first tools/call on both sides.</summary>
    public const string LikeForLikeCold = "like_for_like_cold";

    /// <summary>Memory pairing: working-set under the same load shape on both sides.</summary>
    public const string LikeForLikeWorkingSet = "like_for_like_working_set";

    internal static bool IsIsolationEquivalentBaseline(string? mode) =>
        string.Equals(mode, EphemeralCreateDispose, StringComparison.Ordinal);
}

/// <summary>
/// Machine-readable summary of measurement methodology.
/// Enables Phase 4 comparator to detect mismatched sample counts, tool, or protocol
/// between Phase 0 baseline and Phase 4 current measurements.
/// </summary>
internal sealed class CharacterizationMethodologyFingerprint
{
    /// <summary>Schema version for this fingerprint object.</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    /// <summary>MCP tool name used for warm-call and throughput measurements.</summary>
    [JsonPropertyName("toolName")]
    public string ToolName { get; set; } = "get_date";

    /// <summary>MCP protocol version string used in request headers.</summary>
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = "2025-11-25";

    /// <summary>
    /// Sample count per scenario key. Phase 4 comparator validates these match.
    /// Keys are the Phase 0 canonical scenario names without mode suffix.
    /// </summary>
    [JsonPropertyName("scenarioSampleCounts")]
    public Dictionary<string, int> ScenarioSampleCounts { get; set; } = new();

    /// <summary>Warmup call count per scenario (calls excluded from measurement).</summary>
    [JsonPropertyName("warmupCounts")]
    public Dictionary<string, int> WarmupCounts { get; set; } = new();

    /// <summary>
    /// True when Phase 0 baseline and Phase 4 current were measured in the same CI job
    /// (same runner, same session). When false, cross-runner hardware variance may affect
    /// comparisons; results are advisory.
    /// </summary>
    [JsonPropertyName("sameJobPaired")]
    public bool SameJobPaired { get; set; }

    /// <summary>
    /// Isolation mode used for warm_call_latency_ms (#380 Decision B).
    /// Baseline must be <see cref="IsolationModes.EphemeralCreateDispose"/>;
    /// current must be <see cref="IsolationModes.PoolReset"/>.
    /// Defaults empty so missing JSON fields fail closed instead of silently becoming ephemeral.
    /// </summary>
    [JsonPropertyName("warmCallIsolationMode")]
    public string WarmCallIsolationMode { get; set; } = "";

    /// <summary>
    /// Isolation mode used for concurrent_throughput_ms (#380 Decision B).
    /// Same pairing rules as <see cref="WarmCallIsolationMode"/>.
    /// Defaults empty so missing JSON fields fail closed instead of silently becoming ephemeral.
    /// </summary>
    [JsonPropertyName("throughputIsolationMode")]
    public string ThroughputIsolationMode { get; set; } = "";

    /// <summary>
    /// Cold-start pairing rule. Both sides use full cold process start (like-for-like).
    /// Isolation mode does not retarget cold-start ratios.
    /// </summary>
    [JsonPropertyName("coldStartPairingMode")]
    public string ColdStartPairingMode { get; set; } = IsolationModes.LikeForLikeCold;

    /// <summary>
    /// Memory pairing rule. Both sides use working-set under the same load shape.
    /// Isolation mode does not retarget memory ratios.
    /// </summary>
    [JsonPropertyName("memoryPairingMode")]
    public string MemoryPairingMode { get; set; } = IsolationModes.LikeForLikeWorkingSet;
}

internal sealed class CharacterizationScenario
{
    [JsonPropertyName("scenario")]
    public string Scenario { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("unit")]
    public string Unit { get; set; } = "milliseconds";

    [JsonPropertyName("iterations")]
    public int Iterations { get; set; }

    [JsonPropertyName("stats")]
    public CharacterizationStats Stats { get; set; } = new();

    [JsonPropertyName("rawSamples")]
    public double[] RawSamples { get; set; } = [];
}

/// <summary>
/// Descriptive statistics for one scenario's sample set.
/// Static factory <see cref="FromSamples"/> computes all fields from a raw array.
/// </summary>
internal sealed class CharacterizationStats
{
    [JsonPropertyName("mean")]
    public double Mean { get; set; }

    [JsonPropertyName("p50")]
    public double P50 { get; set; }

    [JsonPropertyName("p95")]
    public double P95 { get; set; }

    [JsonPropertyName("p99")]
    public double P99 { get; set; }

    [JsonPropertyName("min")]
    public double Min { get; set; }

    [JsonPropertyName("max")]
    public double Max { get; set; }

    [JsonPropertyName("stdDev")]
    public double StdDev { get; set; }

    [JsonPropertyName("sampleCount")]
    public int SampleCount { get; set; }

    /// <summary>
    /// Computes mean, p50, p95, p99, min, max, and stdDev from <paramref name="samples"/>.
    /// Uses linear interpolation for percentile estimation.
    /// </summary>
    public static CharacterizationStats FromSamples(double[] samples)
    {
        if (samples is null || samples.Length == 0)
            throw new System.ArgumentException("At least one sample is required.", nameof(samples));

        var sorted = samples.OrderBy(x => x).ToArray();
        var mean = sorted.Average();
        var variance = sorted.Length > 1
            ? sorted.Sum(x => (x - mean) * (x - mean)) / sorted.Length
            : 0.0;

        return new CharacterizationStats
        {
            Mean = mean,
            P50 = Percentile(sorted, 0.50),
            P95 = Percentile(sorted, 0.95),
            P99 = Percentile(sorted, 0.99),
            Min = sorted[0],
            Max = sorted[^1],
            StdDev = Math.Sqrt(variance),
            SampleCount = sorted.Length,
        };
    }

    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 1) return sorted[0];
        double rank = p * (sorted.Length - 1);
        int lo = (int)rank;
        int hi = lo + 1;
        if (hi >= sorted.Length) return sorted[lo];
        double frac = rank - lo;
        return sorted[lo] * (1.0 - frac) + sorted[hi] * frac;
    }
}
