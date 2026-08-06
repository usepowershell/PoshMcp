using System;
using System.Collections.Generic;
using System.Linq;

namespace PoshMcp.Tests.Characterization.Phase4;

/// <summary>
/// Pure static comparator that evaluates Phase 4 measurements against Phase 0 baselines.
///
/// Threshold rules (all "lower is better" metrics with ratio = measured / baseline):
///
///   Same-SDK isolation gate (Decision C §4B — blocking, EXIT 1 on breach):
///     Same-SDK warm p95   ≤ 1.10  (v2-pool-reset / v2-ephemeral; same SDK, same runner)
///     Same-SDK tput mean  ≤ 1.10  (v2-pool-reset / v2-ephemeral; same SDK, same runner)
///
///   Cold-start p95       ≤ 1.10   (≤ 110% of baseline) — like-for-like cold pairing
///   Peak memory mean     ≤ 1.10   (≤ 110% of baseline) — like-for-like working-set pairing
///
///   Cross-SDK warm/throughput (Decision C §4A — informational only, IsBlocking=false):
///     Warm-call p95  (v1-ephemeral vs v2-pool-reset) — recorded, never EXIT 1
///     Throughput mean (v1-ephemeral vs v2-pool-reset) — recorded, never EXIT 1
///
/// Phase 4 scenario names are suffixed with the transport mode in lower-case
/// (e.g. "cold_start_http_no_script_stateless").
/// Phase 0 baseline scenario names have no suffix.
/// V2-ephemeral scenario names use suffix "_v2ephemeral_{mode}"
/// (e.g. "warm_call_latency_ms_v2ephemeral_stateless").
/// </summary>
internal static class PerformanceComparator
{
    // ── Same-SDK isolation gate constants (Decision C §5, blocking) ─────────────
    // Cite: .squad/decisions/inbox/farnsworth-decision-c-warm-gate.md §5
    internal const double SameSdkWarmCallP95MaxRatio = 1.10;
    internal const double SameSdkThroughputMeanMaxRatio = 1.10;

    // ── Cold/memory blocking thresholds (unchanged from Decision B) ─────────────
    internal const double ColdStartP95MaxRatio = 1.10;
    internal const double MemoryPeakMeanMaxRatio = 1.10;

    // ── Cross-SDK constants (Decision C §5: informational only, NOT blocking) ────
    // Retained as documentation values for the cross-SDK informational check.
    // These constants MUST NOT drive EXIT 1. (Decision C supersedes Decision B warm/tput gate.)
    internal const double WarmCallP95MaxRatio = 1.05;
    internal const double ThroughputMeanMaxRatio = 1.0 / 0.95; // ≈ 1.0526

    internal const string ExpectedBaselineSchemaVersion = "poshmcp/v1-characterization/1.0";

    /// <summary>
    /// Machine gate that rejects a non-migration SDK pairing — the exact failure that made the
    /// prior "v2-vs-v2" runs meaningless. Given the runtime-detected SDK descriptors for the
    /// Phase 0 baseline and the Phase 4 current server, this throws when:
    /// <list type="bullet">
    ///   <item>either descriptor is null or its major version is undetectable (0),</item>
    ///   <item>both majors are identical (v2-vs-v2 or v1-vs-v1),</item>
    ///   <item>the two DLLs share a SHA-256 (literally the same binary compared to itself),</item>
    ///   <item>the baseline major is not <paramref name="expectedBaselineMajor"/> (e.g. swapped v2 baseline),</item>
    ///   <item>the current major is not <paramref name="expectedCurrentMajor"/>.</item>
    /// </list>
    /// Defaults enforce the intended 1.x → 2.x migration comparison.
    /// </summary>
    internal static void ValidateSdkVersionPair(
        SdkAssemblyDescriptor? baseline,
        SdkAssemblyDescriptor? current,
        int expectedBaselineMajor = 1,
        int expectedCurrentMajor = 2)
    {
        if (baseline is null)
            throw new InvalidOperationException(
                "Baseline SDK descriptor is missing. Runtime SDK detection is required for an " +
                "authoritative comparison; a legacy artifact without sdkAssembly cannot prove it is v1. " +
                "Re-capture the Phase 0 baseline from a genuine pre-migration (1.4.1) commit.");

        if (current is null)
            throw new InvalidOperationException(
                "Current SDK descriptor is missing. The Phase 4 fixture must detect the loaded " +
                "ModelContextProtocol assembly at runtime.");

        if (baseline.MajorVersion <= 0 || current.MajorVersion <= 0)
            throw new InvalidOperationException(
                $"SDK major version could not be detected (baseline='{baseline.PackageDisplay}' major={baseline.MajorVersion}, " +
                $"current='{current.PackageDisplay}' major={current.MajorVersion}). " +
                "Detection must resolve a real version from the DLL; a hardcoded label is not accepted.");

        if (baseline.MajorVersion == current.MajorVersion)
            throw new InvalidOperationException(
                $"Baseline and current MCP SDK major versions are identical (both v{current.MajorVersion}). " +
                $"This is NOT a migration comparison — baseline='{baseline.PackageDisplay}', current='{current.PackageDisplay}'. " +
                "This guard exists specifically to prevent v2-vs-v2 (or v1-vs-v1) false comparisons.");

        if (!string.IsNullOrEmpty(baseline.Sha256) &&
            string.Equals(baseline.Sha256, current.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Baseline and current MCP SDK DLLs have identical SHA-256 ({baseline.Sha256}). " +
                "The same binary is being compared to itself — the baseline archive/build is wrong.");

        if (baseline.MajorVersion != expectedBaselineMajor)
            throw new InvalidOperationException(
                $"Baseline MCP SDK major is v{baseline.MajorVersion} but v{expectedBaselineMajor} was expected " +
                $"(baseline='{baseline.PackageDisplay}'). The baseline may be swapped with the current build.");

        if (current.MajorVersion != expectedCurrentMajor)
            throw new InvalidOperationException(
                $"Current MCP SDK major is v{current.MajorVersion} but v{expectedCurrentMajor} was expected " +
                $"(current='{current.PackageDisplay}').");
    }

    /// <summary>
    /// Validates the Phase 0 baseline artifact. Throws <see cref="InvalidOperationException"/>
    /// with an actionable message when the artifact is null, has the wrong schema version,
    /// has no scenarios, or has non-positive/non-finite values in any measured stat.
    /// </summary>
    internal static void ValidateBaseline(CharacterizationArtifact baseline)
    {
        if (baseline is null)
            throw new ArgumentNullException(nameof(baseline),
                "Baseline artifact is null. Ensure V1_BASELINE_PATH env var points to a valid Phase 0 JSON file.");

        if (baseline.SchemaVersion != ExpectedBaselineSchemaVersion)
            throw new InvalidOperationException(
                $"Baseline schema version mismatch. " +
                $"Expected '{ExpectedBaselineSchemaVersion}', got '{baseline.SchemaVersion}'. " +
                $"Ensure V1_BASELINE_PATH points to a Phase 0 artifact produced by the characterization CI job.");

        if (baseline.Scenarios is null || baseline.Scenarios.Count == 0)
            throw new InvalidOperationException(
                "Baseline artifact contains no scenarios. The file may be corrupt or truncated.");

        foreach (var scenario in baseline.Scenarios)
        {
            if (scenario.Stats is null)
                throw new InvalidOperationException(
                    $"Baseline scenario '{scenario.Scenario}' has null stats. The baseline may be corrupt.");

            if (!double.IsFinite(scenario.Stats.P95) || scenario.Stats.P95 <= 0)
                throw new InvalidOperationException(
                    $"Baseline scenario '{scenario.Scenario}'.p95 = {scenario.Stats.P95} " +
                    $"is not a positive finite number. The baseline may be corrupt.");

            if (!double.IsFinite(scenario.Stats.Mean) || scenario.Stats.Mean <= 0)
                throw new InvalidOperationException(
                    $"Baseline scenario '{scenario.Scenario}'.mean = {scenario.Stats.Mean} " +
                    $"is not a positive finite number. The baseline may be corrupt.");
        }
    }

    /// <summary>
    /// Validates that Phase 4 scenario sample counts match the Phase 0 baseline for every
    /// gated metric. Throws <see cref="InvalidOperationException"/> with an actionable
    /// message if any gated scenario has a mismatched N.
    ///
    /// <para>
    /// Reads N from <c>baseline.Scenarios[key].Iterations</c> (always present) rather than
    /// the fingerprint (absent on old artifacts). This allows validation against the published
    /// Phase 0 artifact even before the fingerprint field was added.
    /// </para>
    /// </summary>
    internal static void ValidateMethodologyMatch(
        CharacterizationArtifact baseline,
        string transportMode,
        IReadOnlyList<CharacterizationScenario> phase4Scenarios)
    {
        var mode = transportMode.ToLowerInvariant();
        var b0Map = baseline.Scenarios.ToDictionary(s => s.Scenario, StringComparer.Ordinal);
        var p4Map = phase4Scenarios.ToDictionary(s => s.Scenario, StringComparer.Ordinal);

        // Gated scenario pairs: (baseline key, Phase 4 key suffix, metric label)
        var gated = new[]
        {
            ("cold_start_http_no_script",    $"cold_start_http_no_script_{mode}",   "cold-start no-script"),
            ("cold_start_http_with_script",  $"cold_start_http_with_script_{mode}", "cold-start with-script"),
            ("warm_call_latency_ms",         $"warm_call_latency_ms_{mode}",        "warm-call"),
            ("concurrent_throughput_ms",     $"concurrent_throughput_ms_{mode}",    "throughput"),
        };

        var mismatches = new List<string>();
        foreach (var (b0Key, p4Key, label) in gated)
        {
            if (!b0Map.TryGetValue(b0Key, out var b0Scenario)) continue;
            if (!p4Map.TryGetValue(p4Key, out var p4Scenario)) continue;

            var b0N = b0Scenario.Iterations;
            var p4N = p4Scenario.Iterations;
            if (b0N <= 0)
                mismatches.Add($"  {label} ({b0Key}): baseline Iterations={b0N} is uninitialized/zero — cannot validate methodology match");
            else if (b0N != p4N)
                mismatches.Add($"  {label} ({b0Key}): baseline N={b0N}, current N={p4N}");
        }

        if (mismatches.Count > 0)
            throw new InvalidOperationException(
                $"Methodology sample count mismatch for transport mode '{transportMode}' — " +
                $"Phase 0 baseline and Phase 4 measurements used different N values. " +
                $"The p95/mean estimators from different sample counts are not comparable.\n" +
                string.Join("\n", mismatches) + "\n" +
                $"Fix: ensure Phase 4 test iteration constants match the baseline. " +
                $"Phase 4 can read N from baseline.Scenarios[key].Iterations via Phase4ComparisonFixture.GetBaselineSampleCount().");
    }

    /// <summary>
    /// Compares Phase 4 measurements for one transport mode against the Phase 0 baseline.
    /// Validates the baseline and methodology match before comparing. Throws on invalid
    /// inputs, missing scenarios, or N mismatch.
    ///
    /// Cross-SDK warm/throughput checks (v1 vs v2) are recorded as informational only
    /// (IsBlocking=false, Decision C §4A). Cold-start and memory checks remain blocking.
    /// AllPassed reflects only the blocking checks.
    /// </summary>
    /// <param name="transportMode">
    /// "Stateless" or "Stateful" — used to construct the expected Phase 4 scenario name suffix.
    /// </param>
    /// <param name="baseline">Validated Phase 0 characterization artifact.</param>
    /// <param name="phase4Scenarios">
    /// Phase 4 scenario measurements. Names must be suffixed with
    /// <c>_{transportMode.ToLower()}</c> (e.g. "warm_call_latency_ms_stateless").
    /// </param>
    internal static Phase4ModeComparison Compare(
        string transportMode,
        CharacterizationArtifact baseline,
        IReadOnlyList<CharacterizationScenario> phase4Scenarios)
    {
        ValidateBaseline(baseline);
        ValidateMethodologyMatch(baseline, transportMode, phase4Scenarios);

        var mode = transportMode.ToLowerInvariant();
        var p4Map = phase4Scenarios.ToDictionary(s => s.Scenario, StringComparer.Ordinal);
        var b0Map = baseline.Scenarios.ToDictionary(s => s.Scenario, StringComparer.Ordinal);

        var checks = new List<Phase4ThresholdCheck>();

        AddP95Check(checks,
            "cold_start_http_no_script.p95",
            $"Cold-start p95 no script [{transportMode}]",
            "milliseconds",
            GetP95(b0Map, "cold_start_http_no_script"),
            GetP95(p4Map, $"cold_start_http_no_script_{mode}"),
            ColdStartP95MaxRatio,
            isBlocking: true);

        AddP95Check(checks,
            "cold_start_http_with_script.p95",
            $"Cold-start p95 with script [{transportMode}]",
            "milliseconds",
            GetP95(b0Map, "cold_start_http_with_script"),
            GetP95(p4Map, $"cold_start_http_with_script_{mode}"),
            ColdStartP95MaxRatio,
            isBlocking: true);

        // Cross-SDK warm check: v1-ephemeral vs v2-pool-reset — informational only (Decision C §4A).
        AddMeanCheck(checks,
            "warm_call_latency_ms.p95",
            $"Warm-call p95 [{transportMode}] cross-SDK (informational, non-blocking — Decision C §4A)",
            "milliseconds",
            GetP95(b0Map, "warm_call_latency_ms"),
            GetP95(p4Map, $"warm_call_latency_ms_{mode}"),
            WarmCallP95MaxRatio,
            isBlocking: false);

        // Cross-SDK throughput check: v1-ephemeral vs v2-pool-reset — informational only (Decision C §4A).
        AddMeanCheck(checks,
            "concurrent_throughput_ms.mean",
            $"Concurrent throughput mean [{transportMode}] cross-SDK (informational, non-blocking — Decision C §4A)",
            "milliseconds",
            GetMean(b0Map, "concurrent_throughput_ms"),
            GetMean(p4Map, $"concurrent_throughput_ms_{mode}"),
            ThroughputMeanMaxRatio,
            isBlocking: false);

        AddMeanCheck(checks,
            "memory_moderate_load_mb.mean",
            $"Peak memory moderate load [{transportMode}]",
            "megabytes",
            GetMean(b0Map, "memory_moderate_load_mb"),
            GetMean(p4Map, $"memory_moderate_load_mb_{mode}"),
            MemoryPeakMeanMaxRatio,
            isBlocking: true);

        // AllPassed reflects only blocking checks (Decision C: non-blocking checks never drive EXIT 1).
        return new Phase4ModeComparison
        {
            TransportMode = transportMode,
            Scenarios = new List<CharacterizationScenario>(phase4Scenarios),
            ThresholdChecks = checks,
            AllPassed = checks.All(c => !c.IsBlocking || c.Passed),
        };
    }

    /// <summary>
    /// Compares v2-ephemeral vs v2-pool-reset warm/throughput for same-SDK isolation gate.
    /// All checks produced by this method are IsBlocking=true (Decision C §4B).
    ///
    /// Scenario naming convention:
    ///   Ephemeral scenarios use suffix "_v2ephemeral_{mode}" (e.g. "warm_call_latency_ms_v2ephemeral_stateless").
    ///   Pool-reset scenarios use existing suffix "_{mode}" (e.g. "warm_call_latency_ms_stateless").
    ///
    /// Both lists must be non-empty and N must match for the same-job same-SDK check.
    /// </summary>
    /// <param name="transportMode">"Stateless" or "Stateful".</param>
    /// <param name="ephemeralScenarios">
    /// v2-ephemeral measurement scenarios with "_v2ephemeral_{mode}" suffix.
    /// </param>
    /// <param name="poolResetScenarios">
    /// v2-pool-reset measurement scenarios with "_{mode}" suffix (the standard Phase 4 scenarios).
    /// </param>
    internal static Phase4ModeComparison CompareSameSdkIsolation(
        string transportMode,
        IReadOnlyList<CharacterizationScenario> ephemeralScenarios,
        IReadOnlyList<CharacterizationScenario> poolResetScenarios)
    {
        if (ephemeralScenarios is null || ephemeralScenarios.Count == 0)
            throw new ArgumentException(
                "v2-ephemeral scenario list is empty. Same-SDK isolation gate requires v2-ephemeral " +
                "measurements taken in the same job on the same runner. " +
                "Ensure phase4-{mode}-v2ephemeral.appsettings.json is resolved and measurements ran.",
                nameof(ephemeralScenarios));

        if (poolResetScenarios is null || poolResetScenarios.Count == 0)
            throw new ArgumentException(
                "v2-pool-reset scenario list is empty. Same-SDK isolation gate requires pool-reset " +
                "measurements taken in the same job on the same runner.",
                nameof(poolResetScenarios));

        var mode = transportMode.ToLowerInvariant();
        var ephMap = ephemeralScenarios.ToDictionary(s => s.Scenario, StringComparer.Ordinal);
        var prMap = poolResetScenarios.ToDictionary(s => s.Scenario, StringComparer.Ordinal);

        // Validate N match for same-SDK check (same-job same-N is required for comparability).
        ValidateSameSdkN(ephMap, prMap, mode);

        var checks = new List<Phase4ThresholdCheck>();

        // Same-SDK warm-call gate: pool_reset / ephemeral ≤ SameSdkWarmCallP95MaxRatio (Decision C §4B).
        AddP95Check(checks,
            "warm_call_latency_ms.p95",
            $"Same-SDK warm-call p95 [{transportMode}] pool_reset/ephemeral (blocking — Decision C §4B)",
            "milliseconds",
            GetP95(ephMap, $"warm_call_latency_ms_v2ephemeral_{mode}"),
            GetP95(prMap, $"warm_call_latency_ms_{mode}"),
            SameSdkWarmCallP95MaxRatio,
            isBlocking: true);

        // Same-SDK throughput gate: pool_reset / ephemeral ≤ SameSdkThroughputMeanMaxRatio (Decision C §4B).
        AddMeanCheck(checks,
            "concurrent_throughput_ms.mean",
            $"Same-SDK throughput mean [{transportMode}] pool_reset/ephemeral (blocking — Decision C §4B)",
            "milliseconds",
            GetMean(ephMap, $"concurrent_throughput_ms_v2ephemeral_{mode}"),
            GetMean(prMap, $"concurrent_throughput_ms_{mode}"),
            SameSdkThroughputMeanMaxRatio,
            isBlocking: true);

        return new Phase4ModeComparison
        {
            TransportMode = transportMode,
            Scenarios = new List<CharacterizationScenario>(ephemeralScenarios),
            ThresholdChecks = [],
            SameSdkIsolationChecks = checks,
            AllPassed = checks.All(c => c.Passed),
        };
    }

    private static void ValidateSameSdkN(
        Dictionary<string, CharacterizationScenario> ephMap,
        Dictionary<string, CharacterizationScenario> prMap,
        string mode)
    {
        var pairs = new[]
        {
            ($"warm_call_latency_ms_v2ephemeral_{mode}", $"warm_call_latency_ms_{mode}", "warm-call"),
            ($"concurrent_throughput_ms_v2ephemeral_{mode}", $"concurrent_throughput_ms_{mode}", "throughput"),
        };

        var mismatches = new List<string>();
        foreach (var (ephKey, prKey, label) in pairs)
        {
            if (!ephMap.TryGetValue(ephKey, out var ephScenario)) continue;
            if (!prMap.TryGetValue(prKey, out var prScenario)) continue;

            var ephN = ephScenario.Iterations;
            var prN = prScenario.Iterations;
            if (ephN != prN)
                mismatches.Add($"  {label}: ephemeral N={ephN}, pool_reset N={prN}");
        }

        if (mismatches.Count > 0)
            throw new InvalidOperationException(
                $"Same-SDK isolation gate N mismatch for transport mode '{mode}' — " +
                $"v2-ephemeral and v2-pool-reset measurements used different N values. " +
                $"Both must use the same N for a valid same-job paired comparison.\n" +
                string.Join("\n", mismatches) + "\n" +
                $"Fix: derive N from baseline in both measurement paths via GetBaselineSampleCount().");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────

    private static void AddP95Check(
        List<Phase4ThresholdCheck> checks,
        string metric,
        string description,
        string unit,
        double baselineValue,
        double measuredValue,
        double maxRatio,
        bool isBlocking = true)
        => AddCheck(checks, metric, description, unit, baselineValue, measuredValue, maxRatio, isBlocking);

    private static void AddMeanCheck(
        List<Phase4ThresholdCheck> checks,
        string metric,
        string description,
        string unit,
        double baselineValue,
        double measuredValue,
        double maxRatio,
        bool isBlocking = true)
        => AddCheck(checks, metric, description, unit, baselineValue, measuredValue, maxRatio, isBlocking);

    private static void AddCheck(
        List<Phase4ThresholdCheck> checks,
        string metric,
        string description,
        string unit,
        double baselineValue,
        double measuredValue,
        double maxRatio,
        bool isBlocking = true)
    {
        if (!double.IsFinite(baselineValue) || baselineValue <= 0)
            throw new InvalidOperationException(
                $"Baseline value for metric '{metric}' = {baselineValue} is not positive-finite. " +
                $"Cannot compute gate ratio.");

        if (!double.IsFinite(measuredValue))
            throw new InvalidOperationException(
                $"Measured Phase 4 value for metric '{metric}' = {measuredValue} is not finite. " +
                $"The measurement may have failed or produced an invalid result.");

        var ratio = measuredValue / baselineValue;
        checks.Add(new Phase4ThresholdCheck
        {
            Metric = metric,
            Description = description,
            Unit = unit,
            BaselineValue = baselineValue,
            MeasuredValue = measuredValue,
            Ratio = ratio,
            MaxRatio = maxRatio,
            Passed = ratio <= maxRatio,
            IsBlocking = isBlocking,
        });
    }

    private static double GetP95(Dictionary<string, CharacterizationScenario> map, string key)
    {
        if (!map.TryGetValue(key, out var scenario))
            throw new KeyNotFoundException(
                $"Required scenario '{key}' not found. " +
                $"Available: [{string.Join(", ", map.Keys.OrderBy(k => k))}]");
        return scenario.Stats.P95;
    }

    private static double GetMean(Dictionary<string, CharacterizationScenario> map, string key)
    {
        if (!map.TryGetValue(key, out var scenario))
            throw new KeyNotFoundException(
                $"Required scenario '{key}' not found. " +
                $"Available: [{string.Join(", ", map.Keys.OrderBy(k => k))}]");
        return scenario.Stats.Mean;
    }
}
