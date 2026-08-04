using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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

    internal CharacterizationArtifact Baseline =>
        _baseline ?? throw new InvalidOperationException(
            "Phase4ComparisonFixture was not successfully initialized. " +
            "Check that V1_BASELINE_PATH is set and points to a valid Phase 0 artifact.");

    internal void RecordModeComparison(Phase4ModeComparison comparison) =>
        _modeComparisons.Add(comparison);

    /// <summary>Resolves a Phase 4 config asset from the test output directory.</summary>
    internal static string ResolveAssetPath(string filename) =>
        CharacterizationFixture.ResolveAssetPath(filename);

    public async Task InitializeAsync()
    {
        var baselinePath = Environment.GetEnvironmentVariable("V1_BASELINE_PATH");
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
    }

    public async Task DisposeAsync()
    {
        if (_baseline is null) return;

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
        var baselineOs = _baseline!.RuntimeInfo?.Os ?? "";
        var currentOs = Environment.OSVersion.ToString();
        if (!string.IsNullOrEmpty(baselineOs) &&
            !baselineOs.Equals(currentOs, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(
                $"Environment mismatch: baseline captured on '{baselineOs}' " +
                $"but this run is on '{currentOs}'. Threshold results may not reflect true regressions. " +
                "Use the Linux CI job for authoritative comparisons.");
        }

        var baselineProcs = _baseline!.RuntimeInfo?.LogicalProcessors ?? 0;
        if (baselineProcs > 0 && baselineProcs != Environment.ProcessorCount)
        {
            warnings.Add(
                $"Processor count mismatch: baseline had {baselineProcs} logical processors, " +
                $"this run has {Environment.ProcessorCount}. Concurrency and throughput results may differ.");
        }

        var artifact = new Phase4ComparisonArtifact
        {
            CapturedAt = DateTime.UtcNow.ToString("O"),
            SdkPackageVersion = "ModelContextProtocol 1.4.1",
            CommitSha = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "local",
            RuntimeInfo = new CharacterizationRuntimeInfo
            {
                DotNetVersion = Environment.Version.ToString(),
                Os = Environment.OSVersion.ToString(),
                LogicalProcessors = Environment.ProcessorCount,
                MachineName = Environment.MachineName,
            },
            BaselineProvenance = new Phase4BaselineProvenance
            {
                SchemaVersion = _baseline!.SchemaVersion,
                CapturedAt = _baseline.CapturedAt,
                SdkPackageVersion = _baseline.SdkPackageVersion,
                RuntimeInfo = _baseline.RuntimeInfo,
                ArtifactRunId = _baselineRunId ?? "unknown",
                ArtifactSource = $"github-actions/v1-baseline-characterization/run/{_baselineRunId}",
            },
            Modes = modes,
            Warnings = warnings,
            OverallPassed = overallPassed,
            ExitCode = overallPassed ? 0 : 1,
        };

        var json = JsonSerializer.Serialize(artifact, new JsonSerializerOptions { WriteIndented = true });
        return File.WriteAllTextAsync(path, json);
    }
}
