using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace PoshMcp.Tests.Characterization;

/// <summary>
/// xUnit class fixture shared across all <see cref="V1BaselineCharacterizationTests"/>.
/// Starts one shared warm HTTP server before the first test and disposes it after the
/// last, then writes the collected scenario results to the v1-baseline JSON artifact.
/// </summary>
public sealed class CharacterizationFixture : IAsyncLifetime
{
    private CharacterizationHttpServer? _warmServer;
    private readonly ConcurrentBag<CharacterizationScenario> _scenarios = new();
    private readonly ConcurrentDictionary<string, int> _warmupCounts = new();
    private bool _sameJobPaired;

    internal CharacterizationHttpServer WarmServer =>
        _warmServer ?? throw new InvalidOperationException("CharacterizationFixture not initialized.");

    internal void RecordScenario(CharacterizationScenario scenario) => _scenarios.Add(scenario);

    /// <summary>Records the warmup call count for a scenario (excluded from measurement).</summary>
    internal void RecordWarmupCount(string scenarioKey, int count) =>
        _warmupCounts[scenarioKey] = count;

    /// <summary>
    /// Marks this artifact as produced from a same-job paired measurement (Phase 0 and Phase 4
    /// in the same CI job). Set by CI before running characterization with POSHMCP_SERVER_DLL override.
    /// </summary>
    internal void MarkSameJobPaired() => _sameJobPaired = true;

    /// <summary>
    /// Resolves the path to a characterization config asset in the test output directory.
    /// </summary>
    internal static string ResolveAssetPath(string filename) =>
        Path.Combine(AppContext.BaseDirectory, "Characterization", "Assets", filename);

    public async Task InitializeAsync()
    {
        // POSHMCP_SAME_JOB_PAIRED=1 is set by CI when running Phase 0 characterization
        // alongside Phase 4 comparison (same CI job, same runner).
        if (Environment.GetEnvironmentVariable("POSHMCP_SAME_JOB_PAIRED") == "1")
            _sameJobPaired = true;

        _warmServer = new CharacterizationHttpServer();
        await _warmServer.StartAsync(ResolveAssetPath("with-startup-script.appsettings.json"));
    }

    public async Task DisposeAsync()
    {
        if (_warmServer is not null)
            await _warmServer.DisposeAsync();

        await WriteArtifactAsync();
    }

    private Task WriteArtifactAsync()
    {
        var path = Environment.GetEnvironmentVariable("CHARACTERIZATION_ARTIFACT_PATH")
            ?? Path.Combine("TestResults", "v1-baseline-characterization.json");

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var orderedScenarios = new List<CharacterizationScenario>(_scenarios);
        orderedScenarios.Sort((a, b) => string.Compare(a.Scenario, b.Scenario, StringComparison.Ordinal));

        // Build methodology fingerprint from collected scenario sample counts and warmup records.
        // Decision B (#380): warm/throughput baseline is isolation-equivalent ephemeral create+dispose
        // on the real v1 binary; cold/memory remain like-for-like pairings (not isolation-retargeted).
        var fingerprint = new CharacterizationMethodologyFingerprint
        {
            ScenarioSampleCounts = orderedScenarios.ToDictionary(s => s.Scenario, s => s.Iterations),
            WarmupCounts = new Dictionary<string, int>(_warmupCounts),
            SameJobPaired = _sameJobPaired,
            WarmCallIsolationMode = IsolationModes.EphemeralCreateDispose,
            ThroughputIsolationMode = IsolationModes.EphemeralCreateDispose,
            ColdStartPairingMode = IsolationModes.LikeForLikeCold,
            MemoryPairingMode = IsolationModes.LikeForLikeWorkingSet,
        };

        // Runtime-detect the SDK the measured server actually loaded, rather than hardcoding
        // a version label. For a genuine v1 baseline this reports ModelContextProtocol 1.x;
        // built from a post-migration commit it reports 2.x. Detection failure is fatal —
        // provenance must be real. (The server was started successfully above, so the DLL exists.)
        var sdk = SdkAssemblyInfo.DetectFromMeasuredServer();

        var artifact = new CharacterizationArtifact
        {
            CapturedAt = DateTime.UtcNow.ToString("O"),
            SdkPackageVersion = sdk.PackageDisplay,
            SdkAssembly = sdk,
            CommitSha = Environment.GetEnvironmentVariable("POSHMCP_SOURCE_COMMIT_SHA")
                ?? Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "",
            RuntimeInfo = new CharacterizationRuntimeInfo
            {
                DotNetVersion = Environment.Version.ToString(),
                Os = Environment.OSVersion.ToString(),
                LogicalProcessors = Environment.ProcessorCount,
                MachineName = Environment.MachineName,
                ProcessorModel = Environment.GetEnvironmentVariable("RUNNER_CPU_MODEL") ?? "",
                TotalMemoryKb = long.TryParse(
                    Environment.GetEnvironmentVariable("RUNNER_TOTAL_MEM_KB"), out var mem) ? mem : 0,
            },
            MethodologyFingerprint = fingerprint,
            Scenarios = orderedScenarios,
        };

        var json = JsonSerializer.Serialize(artifact, new JsonSerializerOptions { WriteIndented = true });
        return File.WriteAllTextAsync(path, json);
    }
}
