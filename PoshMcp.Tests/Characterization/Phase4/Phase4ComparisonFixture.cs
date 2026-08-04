using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    // Predeclared N values matching V1BaselineCharacterizationTests constants.
    // These are used when CollectOnly=true (baseline not yet available).
    private const int PredeclaredColdStartN = 5;
    private const int PredeclaredWarmCallN = 50;
    private const int PredeclaredThroughputN = 15;

    internal CharacterizationArtifact? Baseline => _baseline;

    /// <summary>
    /// Returns the sample count for the given scenario. When baseline is loaded, derives from
    /// baseline. In collect-only mode, returns predeclared constants.
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

    public async Task InitializeAsync()
    {
        // Collect-only mode: baseline not required. Used for TRUE counterbalancing (#380 AC1)
        // where v2 measurement must run before v1 baseline exists.
        CollectOnly = Environment.GetEnvironmentVariable("PHASE4_COLLECT_ONLY") == "1";

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
        foreach (var s in _baseline!.Scenarios)
        {
            measuredIterations[s.Scenario] = s.Iterations;
        }

        return MethodologyContract.CaptureCurrentEnvironment(
            sdkMajorVersion: currentSdk.MajorVersion,
            sdkSha256: currentSdk.Sha256,
            sourceCommitSha: Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "local",
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
            SdkMajorVersion = _baseline.SdkAssembly?.MajorVersion ?? 0,
            SdkSha256 = _baseline.SdkAssembly?.Sha256 ?? "",
            SourceCommitSha = _baseline.CommitSha,
            WarmupCounts = fp.WarmupCounts,
            MeasuredIterations = fp.ScenarioSampleCounts,
        };
    }
}
