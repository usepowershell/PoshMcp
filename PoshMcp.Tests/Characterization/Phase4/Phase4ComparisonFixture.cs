using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Xunit;

namespace PoshMcp.Tests.Characterization.Phase4;

/// <summary>
/// xUnit class fixture for Phase 4 comparison tests.
///
/// Initialization:
///   - Loads and validates the Phase 0 baseline from <c>V1_BASELINE_PATH</c> env var.
///     Throws with an actionable message if the variable is unset or the file is missing.
///
/// Teardown:
///   - Writes the Phase 4 comparison artifact to <c>PHASE4_ARTIFACT_PATH</c> env var
///     (defaults to <c>TestResults/phase4-comparison.json</c> relative to CWD).
///     Written even when tests fail so CI can upload it with <c>if: always()</c>.
/// </summary>
public sealed class Phase4ComparisonFixture : IAsyncLifetime
{
    private CharacterizationArtifact? _baseline;
    private string? _baselineRunId;
    private readonly ConcurrentBag<Phase4ModeComparison> _modeComparisons = new();
    private readonly ConcurrentBag<StatisticalReport> _statisticalReports = new();
    private readonly ConcurrentBag<StageAttribution> _stageAttributions = new();

    /// <summary>
    /// When true, the fixture runs in collect-only mode: measurements proceed with predeclared
    /// N values, baseline is not required, and comparison is deferred to a post-hoc step.
    /// Set via PHASE4_COLLECT_ONLY=1 environment variable. Used for TRUE counterbalancing (#380 AC1)
    /// where v2 measurement must complete before v1 baseline is available.
    /// </summary>
    internal bool CollectOnly { get; private set; }

    /// <summary>
    /// When true, the fixture loads pre-collected V2 samples from collect-only artifacts instead
    /// of re-measuring. Set via PHASE4_LOAD_SAMPLES_FROM env var pointing to a directory containing
    /// per-mode collect-only artifact files. Implements fail-closed deferred comparison (#380 AC1).
    /// Deferred comparison MUST NOT re-execute measurements.
    /// </summary>
    internal bool LoadSamplesFromArtifact { get; private set; }

    private string? _preloadedArtifactDir;
    private readonly ConcurrentDictionary<string, PreloadedModeData> _preloadedData = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentBag<PreloadedSampleProvenance> _preloadedProvenance = new();

    // Predeclared N values matching V1BaselineCharacterizationTests constants.
    // These are used when CollectOnly=true (baseline not yet available).
    private const int PredeclaredColdStartN = 5;
    private const int PredeclaredWarmCallN = 50;
    private const int PredeclaredThroughputN = 15;

    internal CharacterizationArtifact? Baseline => _baseline;

    /// <summary>
    /// Returns the sample count for the given scenario. When baseline is loaded, derives from
    /// baseline. In collect-only mode (or LoadSamplesFromArtifact), returns predeclared constants.
    /// </summary>
    internal int GetBaselineSampleCount(string baselineScenarioKey)
    {
        if (CollectOnly || _baseline is null)
        {
            return baselineScenarioKey switch
            {
                "cold_start_http_with_script" or "cold_start_http_no_script" => PredeclaredColdStartN,
                "warm_call_latency_ms" => PredeclaredWarmCallN,
                "concurrent_throughput_ms" => PredeclaredThroughputN,
                _ => throw new KeyNotFoundException(
                    $"No predeclared N for scenario '{baselineScenarioKey}' in collect-only mode.")
            };
        }
        var scenario = _baseline.Scenarios.FirstOrDefault(s => s.Scenario == baselineScenarioKey);
        if (scenario is null)
            throw new KeyNotFoundException(
                $"Baseline scenario '{baselineScenarioKey}' not found. " +
                $"Available: [{string.Join(", ", _baseline.Scenarios.Select(s => s.Scenario).OrderBy(s => s))}]");
        return scenario.Iterations;
    }

    internal void RecordModeComparison(Phase4ModeComparison comparison) =>
        _modeComparisons.Add(comparison);

    internal void RecordStatisticalReport(StatisticalReport report) =>
        _statisticalReports.Add(report);

    internal void RecordStageAttribution(StageAttribution attribution) =>
        _stageAttributions.Add(attribution);

    /// <summary>Resolves a Phase 4 config asset from the test output directory.</summary>
    internal static string ResolveAssetPath(string filename) =>
        CharacterizationFixture.ResolveAssetPath(filename);

    /// <summary>
    /// Returns pre-collected samples for the given mode. Fail-closed if LoadSamplesFromArtifact
    /// is true but the mode's data was not loaded. Returns false when not in deferred mode.
    /// </summary>
    internal bool TryGetPreloadedData(string modeLabel, out PreloadedModeData? data)
    {
        if (!LoadSamplesFromArtifact)
        {
            data = null;
            return false;
        }
        return _preloadedData.TryGetValue(modeLabel, out data);
    }

    public async Task InitializeAsync()
    {
        // Collect-only mode: baseline not required. Used for TRUE counterbalancing (#380 AC1)
        // where v2 measurement must run before v1 baseline exists.
        CollectOnly = Environment.GetEnvironmentVariable("PHASE4_COLLECT_ONLY") == "1";

        // Deferred comparison: load pre-collected V2 samples. Must NOT re-measure.
        // Activated by PHASE4_LOAD_SAMPLES_FROM pointing to the directory containing
        // per-mode collect-only artifacts (e.g. TestResults/phase4-stateless-collect-only.json).
        var loadFrom = Environment.GetEnvironmentVariable("PHASE4_LOAD_SAMPLES_FROM");
        if (!string.IsNullOrEmpty(loadFrom))
        {
            LoadSamplesFromArtifact = true;
            _preloadedArtifactDir = loadFrom;
            await LoadPreloadedSamplesAsync(loadFrom);
        }

        var baselinePath = Environment.GetEnvironmentVariable("V1_BASELINE_PATH");
        if (CollectOnly)
        {
            // In collect-only mode, baseline is optional. If present, load it; otherwise proceed
            // with predeclared N values and skip comparison.
            if (!string.IsNullOrEmpty(baselinePath) && File.Exists(baselinePath))
            {
                var bjson = await File.ReadAllTextAsync(baselinePath);
                _baseline = JsonSerializer.Deserialize<CharacterizationArtifact>(bjson);
            }
            _baselineRunId = "collect-only";
            return;
        }

        if (string.IsNullOrEmpty(baselinePath))
            throw new InvalidOperationException(
                "V1_BASELINE_PATH environment variable is not set. " +
                "This variable must point to the v1-baseline-characterization.json Phase 0 artifact. " +
                "In CI it is set by the 'Download Phase 0 baseline' workflow step. " +
                "Locally: gh run download <run-id> --name v1-baseline-characterization --dir ./baseline " +
                "then set V1_BASELINE_PATH=./baseline/v1-baseline-characterization.json");

        if (!File.Exists(baselinePath))
            throw new FileNotFoundException(
                $"Phase 0 baseline not found at '{baselinePath}'. " +
                "Ensure V1_BASELINE_PATH points to a valid v1-baseline-characterization.json file.",
                baselinePath);

        var json = await File.ReadAllTextAsync(baselinePath);
        _baseline = JsonSerializer.Deserialize<CharacterizationArtifact>(json)
            ?? throw new InvalidOperationException(
                $"Phase 0 baseline at '{baselinePath}' deserialized to null. " +
                "The file may be empty or contain invalid JSON.");

        PerformanceComparator.ValidateBaseline(_baseline);
        _baselineRunId = Environment.GetEnvironmentVariable("V1_BASELINE_RUN_ID") ?? "unknown";

        // Authoritative CI runs set POSHMCP_REQUIRE_SDK_MIGRATION_PAIR=1 to enforce that the
        // baseline is a genuine v1 (1.x) build and the current server is v2 (2.x). This is the
        // machine gate that rejects the v2-vs-v2 pairing that invalidated the prior runs.
        if (Environment.GetEnvironmentVariable("POSHMCP_REQUIRE_SDK_MIGRATION_PAIR") == "1")
        {
            var currentSdk = SdkAssemblyInfo.DetectFromMeasuredServer();
            PerformanceComparator.ValidateSdkVersionPair(_baseline.SdkAssembly, currentSdk);
        }
    }

    public async Task DisposeAsync()
    {
        // Always write artifact: in collect-only mode the raw samples are needed even without baseline.
        // In normal mode, baseline is always loaded (enforced by InitializeAsync).
        if (!CollectOnly && _baseline is null) return;

        await WriteArtifactAsync();
    }

    private Task WriteArtifactAsync()
    {
        var path = Environment.GetEnvironmentVariable("PHASE4_ARTIFACT_PATH")
            ?? Path.Combine("TestResults", "phase4-comparison.json");

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var modes = new List<Phase4ModeComparison>(_modeComparisons);
        modes.Sort((a, b) => string.Compare(a.TransportMode, b.TransportMode, StringComparison.Ordinal));
        var overallPassed = modes.Count > 0 && modes.All(m => m.AllPassed);

        var warnings = new List<string>();
        var baselineOs = _baseline?.RuntimeInfo?.Os ?? "";
        var currentOs = Environment.OSVersion.ToString();
        if (!string.IsNullOrEmpty(baselineOs) &&
            !baselineOs.Equals(currentOs, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(
                $"Environment mismatch: baseline captured on '{baselineOs}' " +
                $"but this run is on '{currentOs}'. Threshold results may not reflect true regressions. " +
                "Use the Linux CI job for authoritative comparisons.");
        }

        var baselineProcs = _baseline?.RuntimeInfo?.LogicalProcessors ?? 0;
        if (baselineProcs > 0 && baselineProcs != Environment.ProcessorCount)
        {
            warnings.Add(
                $"Processor count mismatch: baseline had {baselineProcs} logical processors, " +
                $"this run has {Environment.ProcessorCount}. Concurrency and throughput results may differ.");
        }

        var currentSdk = SdkAssemblyInfo.DetectFromMeasuredServer();
        var productOrder = Environment.GetEnvironmentVariable("PHASE4_PRODUCT_ORDER") ?? "baseline_first";
        var observedProductOrder = Environment.GetEnvironmentVariable("PHASE4_OBSERVED_PRODUCT_ORDER") ?? productOrder;
        var modeOrder = Environment.GetEnvironmentVariable("PHASE4_MODE_ORDER") ?? "unknown";

        // Build methodology contracts for baseline and current
        var baselineContract = _baseline?.MethodologyFingerprint is not null
            ? BuildBaselineContract()
            : null;
        var currentContract = BuildCurrentContract(currentSdk, productOrder, modeOrder);

        // Validate methodology contracts
        var methodologyValidation = new List<string>();
        if (baselineContract is not null && currentContract is not null)
        {
            methodologyValidation = MethodologyContractValidator.Validate(baselineContract, currentContract);
        }

        var artifact = new Phase4ComparisonArtifact
        {
            CapturedAt = DateTime.UtcNow.ToString("O"),
            SdkPackageVersion = currentSdk.PackageDisplay,
            SdkAssembly = currentSdk,
            CommitSha = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "local",
            SameJobPaired = Environment.GetEnvironmentVariable("POSHMCP_SAME_JOB_PAIRED") == "1",
            PlannedProductOrder = productOrder,
            ObservedProductOrder = observedProductOrder,
            ModeOrder = modeOrder,
            MethodologyContract = currentContract,
            BaselineMethodologyContract = baselineContract,
            MethodologyValidation = methodologyValidation,
            StatisticalReports = new List<StatisticalReport>(_statisticalReports),
            StageAttributions = new List<StageAttribution>(_stageAttributions),
            RuntimeInfo = new CharacterizationRuntimeInfo
            {
                DotNetVersion = Environment.Version.ToString(),
                Os = Environment.OSVersion.ToString(),
                LogicalProcessors = Environment.ProcessorCount,
                MachineName = Environment.MachineName,
            },
            BaselineProvenance = _baseline is not null ? new Phase4BaselineProvenance
            {
                SchemaVersion = _baseline.SchemaVersion,
                CapturedAt = _baseline.CapturedAt,
                SdkPackageVersion = _baseline.SdkPackageVersion,
                SdkAssembly = _baseline.SdkAssembly,
                RuntimeInfo = _baseline.RuntimeInfo,
                ArtifactRunId = _baselineRunId ?? "unknown",
                ArtifactSource = $"github-actions/v1-baseline-characterization/run/{_baselineRunId}",
            } : null,
            Modes = modes,
            Warnings = warnings,
            PreloadedSampleProvenance = _preloadedProvenance.Count > 0
                ? _preloadedProvenance.OrderBy(p => p.Mode).FirstOrDefault()
                : null,
            OverallPassed = overallPassed,
            ExitCode = overallPassed ? 0 : 1,
        };

        var json = JsonSerializer.Serialize(artifact, new JsonSerializerOptions
        {
            WriteIndented = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        });
        return File.WriteAllTextAsync(path, json);
    }

    private MethodologyContract BuildCurrentContract(
        SdkAssemblyDescriptor currentSdk,
        string productOrder,
        string modeOrder)
    {
        // Keys MUST match V1BaselineCharacterizationTests.RecordWarmupCount() calls exactly.
        // Fail-closed validator rejects mismatched keys (#380 AC3).
        var warmupCounts = new Dictionary<string, int>
        {
            ["warm_call_latency_ms"] = 3,
            ["concurrent_throughput_ms"] = 4,  // PerClientWarmupCalls(1) * ThroughputConcurrency(4)
        };
        var measuredIterations = new Dictionary<string, int>();
        if (_baseline is not null)
        {
            foreach (var s in _baseline.Scenarios)
                measuredIterations[s.Scenario] = s.Iterations;
        }
        else
        {
            // Collect-only mode: baseline not yet available; use predeclared N values.
            // Must match V1BaselineCharacterizationTests scenario constants exactly.
            measuredIterations["cold_start_http_no_script"] = PredeclaredColdStartN;
            measuredIterations["cold_start_http_with_script"] = PredeclaredColdStartN;
            measuredIterations["warm_call_latency_ms"] = PredeclaredWarmCallN;
            measuredIterations["concurrent_throughput_ms"] = PredeclaredThroughputN;
            measuredIterations["diagnostic_http_health_ms"] = 20;
            measuredIterations["memory_idle_mb"] = 1;
            measuredIterations["memory_light_load_mb"] = 1;
            measuredIterations["memory_moderate_load_mb"] = 1;
        }

        return MethodologyContract.CaptureCurrentEnvironment(
            sdkMajorVersion: currentSdk.MajorVersion,
            sdkSha256: currentSdk.Sha256,
            sourceCommitSha: Environment.GetEnvironmentVariable("POSHMCP_SOURCE_COMMIT_SHA")
                             ?? Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "local",
            productOrder: productOrder,
            modeOrder: modeOrder,
            warmupCounts: warmupCounts,
            measuredIterations: measuredIterations,
            throughputConcurrency: 4,
            mcpProtocolVersion: "");
    }

    private MethodologyContract? BuildBaselineContract()
    {
        if (_baseline?.MethodologyFingerprint is null) return null;
        var fp = _baseline.MethodologyFingerprint;

        return new MethodologyContract
        {
            Os = _baseline.RuntimeInfo?.Os ?? "",
            DotNetVersion = _baseline.RuntimeInfo?.DotNetVersion ?? "",
            LogicalProcessors = _baseline.RuntimeInfo?.LogicalProcessors ?? 0,
            ProcessorModel = _baseline.RuntimeInfo?.ProcessorModel ?? "",
            TotalMemoryKb = _baseline.RuntimeInfo?.TotalMemoryKb ?? 0,
            MachineName = _baseline.RuntimeInfo?.MachineName ?? "",
            BuildConfiguration = "Release",
            TargetFramework = "net10.0",
            ToolName = fp.ToolName,
            ToolPayloadDescription = "empty-args-get-date",
            HttpTransportType = "StreamableHttp",
            McpProtocolVersion = fp.ProtocolVersion,
            AuthenticationMode = "None",
            TimingMethod = "System.Diagnostics.Stopwatch",
            PercentileAlgorithm = "linear_interpolation_rank_p*(n-1)",
            PercentileImplementation = "CharacterizationStats.FromSamples/1.0",
            VarianceType = "population",
            ThroughputConcurrency = 4,
            MemoryAccountingMethod = "Process.WorkingSet64",
            ServerLifecycle = "per-iteration-cold|shared-warm",
            // Prefer fingerprint values; default to Decision B isolation-equivalent tokens when
            // older artifacts omit the fields (same-job fresh baseline always writes them).
            WarmCallIsolationMode = string.IsNullOrWhiteSpace(fp.WarmCallIsolationMode)
                ? IsolationModes.EphemeralCreateDispose
                : fp.WarmCallIsolationMode,
            ThroughputIsolationMode = string.IsNullOrWhiteSpace(fp.ThroughputIsolationMode)
                ? IsolationModes.EphemeralCreateDispose
                : fp.ThroughputIsolationMode,
            ColdStartPairingMode = string.IsNullOrWhiteSpace(fp.ColdStartPairingMode)
                ? IsolationModes.LikeForLikeCold
                : fp.ColdStartPairingMode,
            MemoryPairingMode = string.IsNullOrWhiteSpace(fp.MemoryPairingMode)
                ? IsolationModes.LikeForLikeWorkingSet
                : fp.MemoryPairingMode,
            SdkMajorVersion = _baseline.SdkAssembly?.MajorVersion ?? 0,
            SdkSha256 = _baseline.SdkAssembly?.Sha256 ?? "",
            SourceCommitSha = _baseline.CommitSha,
            WarmupCounts = fp.WarmupCounts,
            MeasuredIterations = fp.ScenarioSampleCounts,
        };
    }

    /// <summary>
    /// Loads pre-collected V2 samples from per-mode collect-only artifacts in <paramref name="dir"/>.
    /// Validates SHA-256 when PHASE4_COLLECT_ONLY_{MODE}_SHA256 env vars are set.
    /// Fail-closed: throws if an artifact is missing, has wrong hash, or has empty required scenarios.
    /// </summary>
    private async Task LoadPreloadedSamplesAsync(string dir)
    {
        foreach (var mode in new[] { "stateless", "stateful" })
        {
            var artifactPath = Path.Combine(dir, $"phase4-{mode}-collect-only.json");
            if (!File.Exists(artifactPath))
                throw new InvalidOperationException(
                    $"Deferred comparison: collect-only artifact not found for mode '{mode}' " +
                    $"at '{artifactPath}'. " +
                    "The collect-only step must complete and write artifacts before the deferred " +
                    "comparison step. Check that the collect-only step did not crash before calling " +
                    "DisposeAsync (e.g., NullReferenceException in WriteArtifactAsync).");

            var expectedSha256 = Environment.GetEnvironmentVariable(
                $"PHASE4_COLLECT_ONLY_{mode.ToUpperInvariant()}_SHA256");

            // Read bytes first for hash validation, then deserialize.
            var fileBytes = await File.ReadAllBytesAsync(artifactPath);

            string actualSha256;
            using (var sha = SHA256.Create())
                actualSha256 = Convert.ToHexString(sha.ComputeHash(fileBytes));

            if (!string.IsNullOrEmpty(expectedSha256) &&
                !actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Collect-only artifact hash mismatch for mode '{mode}' at '{artifactPath}'. " +
                    $"Expected SHA-256: {expectedSha256}, actual: {actualSha256}. " +
                    "The artifact may have been overwritten or corrupted between the collect-only step " +
                    "and the deferred comparison step. Fail-closed: cannot compare stale samples.");
            }

            var text = System.Text.Encoding.UTF8.GetString(fileBytes);
            var artifact = JsonSerializer.Deserialize<Phase4ComparisonArtifact>(text)
                ?? throw new InvalidOperationException(
                    $"Collect-only artifact at '{artifactPath}' deserialized to null.");

            var modeComparison = artifact.Modes.FirstOrDefault(
                m => m.TransportMode.Equals(mode, StringComparison.OrdinalIgnoreCase)
                  || m.TransportMode.Equals(
                        char.ToUpperInvariant(mode[0]) + mode[1..], StringComparison.Ordinal));
            if (modeComparison is null)
                throw new InvalidOperationException(
                    $"Collect-only artifact at '{artifactPath}' does not contain mode '{mode}'. " +
                    $"Present modes: [{string.Join(", ", artifact.Modes.Select(m => m.TransportMode))}].");

            // Validate required gated scenarios are present with non-empty samples.
            var required = new[]
            {
                $"cold_start_http_with_script_{mode}",
                $"cold_start_http_no_script_{mode}",
                $"warm_call_latency_ms_{mode}",
                $"concurrent_throughput_ms_{mode}",
            };
            foreach (var name in required)
            {
                var s = modeComparison.Scenarios.FirstOrDefault(sc => sc.Scenario == name);
                if (s is null || s.RawSamples is null || s.RawSamples.Length == 0)
                    throw new InvalidOperationException(
                        $"Collect-only artifact missing or has empty RawSamples for scenario '{name}' " +
                        $"in mode '{mode}'. Fail-closed: deferred comparison requires pre-collected samples.");
            }

            double[] GetSamples(string scenario) =>
                modeComparison.Scenarios.First(s => s.Scenario == scenario).RawSamples!;

            // Derive per-call PS execution estimates from all scenarios (for diagnostic use).
            var allScenarios = modeComparison.Scenarios;

            _preloadedData[mode] = new PreloadedModeData(
                ColdWithScript: GetSamples($"cold_start_http_with_script_{mode}"),
                ColdNoScript: GetSamples($"cold_start_http_no_script_{mode}"),
                WarmSamples: GetSamples($"warm_call_latency_ms_{mode}"),
                ThroughputSamples: GetSamples($"concurrent_throughput_ms_{mode}"),
                AllScenarios: allScenarios,
                CapturedAt: artifact.CapturedAt);

            _preloadedProvenance.Add(new PreloadedSampleProvenance
            {
                ArtifactPath = artifactPath,
                ArtifactSha256 = actualSha256,
                ExpectedSha256 = expectedSha256 ?? "",
                CollectOnlyCapturedAt = artifact.CapturedAt,
                Mode = mode,
            });
        }
    }
}

/// <summary>
/// Pre-collected V2 samples for one transport mode, loaded from a collect-only artifact.
/// Consumed by <see cref="Phase4ComparisonTests"/> in deferred comparison mode (#380 AC1).
/// Deferred comparison MUST use these exact samples — do NOT re-measure V2.
/// </summary>
internal sealed record PreloadedModeData(
    double[] ColdWithScript,
    double[] ColdNoScript,
    double[] WarmSamples,
    double[] ThroughputSamples,
    List<CharacterizationScenario> AllScenarios,
    string CapturedAt);
