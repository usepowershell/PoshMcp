using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Characterization.Phase4;

/// <summary>
/// Phase 4 performance comparison tests: measures SDK v2 (post-migration) performance for
/// both <c>Stateless</c> and <c>Stateful</c> transport modes and gates the results against
/// the Phase 0 (SDK 1.4.1) baseline.
///
/// Each test:
///   1. Starts the warm server FIRST (before cold-start iterations), matching Phase 0
///      methodology where CharacterizationFixture.InitializeAsync pre-starts WarmServer.
///   2. Runs all measurement scenarios for one transport mode.
///   3. Compares results to the Phase 0 baseline via <see cref="PerformanceComparator"/>.
///   4. Records the <see cref="Phase4ModeComparison"/> in the fixture for artifact generation.
///   5. Asserts all threshold checks pass — test failure = gate breach = release blocked.
///
/// Thresholds:
///   Cold-start p95      ≤ 110% of baseline
///   Warm-call p95       ≤ 105% of baseline
///   Throughput mean     ≤ 1/0.95 × baseline  (≥ 95% throughput rate)
///   Peak memory mean    ≤ 110% of baseline
///
/// Sample sizes:
///   Cold-start: N=10 (was 5) — with N=5 a single outlier dominated p95 at 80% weight;
///     N=10 limits single-outlier weight to ≤ 50%.
///   Throughput: N=20 bursts (was 5) + 3 burst warmups — N=5 was dominated by first-burst
///     outlier; N=20 gives stable mean.
///
/// CI: <c>dotnet test --filter Category=PerformanceComparison</c>
/// Requires: <c>V1_BASELINE_PATH</c> env var → Phase 0 artifact path.
/// Artifact: <c>PHASE4_ARTIFACT_PATH</c> env var (default TestResults/phase4-comparison.json).
/// </summary>
[Trait("Category", "PerformanceComparison")]
public class Phase4ComparisonTests : IClassFixture<Phase4ComparisonFixture>
{
    // N=10 gives a stable p95: with 10 samples, the 95th percentile is the average of
    // sorted[8] and sorted[9], so a single outlier contributes ≤ 50% weight.
    // N=5 was too small — one outlier dominated (80% weight), inflating cold-start p95.
    private const int ColdStartIterations = 10;
    private const int WarmCallIterations = 20;
    private const int WarmCallWarmupRounds = 3;
    // N=20 bursts for a stable mean; N=5 was dominated by a single slow first-burst outlier.
    private const int ThroughputBursts = 20;
    private const int ThroughputWarmupBursts = 3;
    private const int ThroughputConcurrency = 4;

    private readonly Phase4ComparisonFixture _fixture;
    private readonly ITestOutputHelper _output;

    public Phase4ComparisonTests(Phase4ComparisonFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>
    /// Measures all scenarios for <c>Stateless</c> mode and gates against Phase 0 baseline.
    /// </summary>
    [Fact(DisplayName = "phase4_stateless_vs_baseline")]
    public async Task Stateless_CompareToBaseline()
    {
        await RunModeComparisonAsync("Stateless",
            withScriptConfig: "phase4-stateless.appsettings.json",
            noScriptConfig: "phase4-stateless-no-script.appsettings.json");
    }

    /// <summary>
    /// Measures all scenarios for <c>Stateful</c> mode and gates against Phase 0 baseline.
    /// </summary>
    [Fact(DisplayName = "phase4_stateful_vs_baseline")]
    public async Task Stateful_CompareToBaseline()
    {
        await RunModeComparisonAsync("Stateful",
            withScriptConfig: "phase4-stateful.appsettings.json",
            noScriptConfig: "phase4-stateful-no-script.appsettings.json");
    }

    // ---------------------------------------------------------------------------

    private async Task RunModeComparisonAsync(
        string transportMode,
        string withScriptConfig,
        string noScriptConfig)
    {
        var scenarios = new List<CharacterizationScenario>();
        var modeLabel = transportMode.ToLowerInvariant();

        // ── Start warm server BEFORE cold-start iterations ─────────────────────
        // Phase 0 methodology: the CharacterizationFixture starts WarmServer in
        // InitializeAsync — before any tests run. To match that exactly, we start
        // the warm server here so it is alive throughout all cold-start work below.
        // This prevents cold-start subprocess spawning from contaminating warm-call
        // JIT / HttpClient state at the time of measurement.
        await using var warmServer = new CharacterizationHttpServer();
        await warmServer.StartAsync(Phase4ComparisonFixture.ResolveAssetPath(withScriptConfig));
        _output.WriteLine($"[{transportMode}] Warm server ready at {warmServer.ServerUrl} (started before cold-start iterations)");

        // ── Cold-start: with startup script ────────────────────────────────────
        _output.WriteLine($"[{transportMode}] Cold-start (with startup script) — {ColdStartIterations} iterations");
        var coldWithScript = await MeasureColdStartsAsync(
            Phase4ComparisonFixture.ResolveAssetPath(withScriptConfig),
            ColdStartIterations,
            $"{transportMode}:cold+script");
        scenarios.Add(new CharacterizationScenario
        {
            Scenario = $"cold_start_http_with_script_{modeLabel}",
            Description = $"HTTP server start + module import + startup script + initialize + first tools/call [{transportMode}]",
            Unit = "milliseconds",
            Iterations = coldWithScript.Length,
            Stats = CharacterizationStats.FromSamples(coldWithScript),
            RawSamples = coldWithScript,
        });

        // ── Cold-start: no startup script ──────────────────────────────────────
        _output.WriteLine($"[{transportMode}] Cold-start (no startup script) — {ColdStartIterations} iterations");
        var coldNoScript = await MeasureColdStartsAsync(
            Phase4ComparisonFixture.ResolveAssetPath(noScriptConfig),
            ColdStartIterations,
            $"{transportMode}:cold-noscript");
        scenarios.Add(new CharacterizationScenario
        {
            Scenario = $"cold_start_http_no_script_{modeLabel}",
            Description = $"HTTP server start + module import + initialize + first tools/call (no startup script) [{transportMode}]",
            Unit = "milliseconds",
            Iterations = coldNoScript.Length,
            Stats = CharacterizationStats.FromSamples(coldNoScript),
            RawSamples = coldNoScript,
        });

        // (warm server is already running — started before cold starts above)

        // ── Warm-call latency ──────────────────────────────────────────────────
        // Warm server is already running (started before cold-start iterations above).
        // This matches Phase 0 methodology: the CharacterizationFixture.WarmServer
        // was alive before any cold-start tests ran.
        _output.WriteLine($"[{transportMode}] Warm-call latency — {WarmCallWarmupRounds} warmup + {WarmCallIterations} measured");
        await using var warmClient = new CharacterizationMcpClient(warmServer.ServerUrl);
        await warmClient.InitializeAsync();
        for (var w = 0; w < WarmCallWarmupRounds; w++)
            await warmClient.CallGetDateAsync();

        var warmSamples = new double[WarmCallIterations];
        for (var i = 0; i < WarmCallIterations; i++)
        {
            warmSamples[i] = await warmClient.CallGetDateAsync();
            _output.WriteLine($"  warm {i + 1}/{WarmCallIterations}: {warmSamples[i]:F2} ms");
        }
        scenarios.Add(new CharacterizationScenario
        {
            Scenario = $"warm_call_latency_ms_{modeLabel}",
            Description = $"Per-call HTTP round-trip latency on pre-initialized session [{transportMode}]",
            Unit = "milliseconds",
            Iterations = warmSamples.Length,
            Stats = CharacterizationStats.FromSamples(warmSamples),
            RawSamples = warmSamples,
        });

        // ── Concurrent throughput ──────────────────────────────────────────────
        _output.WriteLine($"[{transportMode}] Concurrent throughput — {ThroughputWarmupBursts} warmup + {ThroughputBursts} measured bursts × {ThroughputConcurrency} concurrent");
        var thrClients = new CharacterizationMcpClient[ThroughputConcurrency];
        try
        {
            for (var i = 0; i < ThroughputConcurrency; i++)
            {
                thrClients[i] = new CharacterizationMcpClient(warmServer.ServerUrl);
                await thrClients[i].InitializeAsync();
                await thrClients[i].CallGetDateAsync(); // per-client warmup
            }
            // Additional burst warmups to stabilise pool state before measurement.
            for (var w = 0; w < ThroughputWarmupBursts; w++)
                await Task.WhenAll(thrClients.Select(c => c.CallGetDateAsync()));

            var thrSamples = new double[ThroughputBursts];
            for (var burst = 0; burst < ThroughputBursts; burst++)
            {
                var sw = Stopwatch.StartNew();
                await Task.WhenAll(thrClients.Select(c => c.CallGetDateAsync()));
                sw.Stop();
                thrSamples[burst] = sw.Elapsed.TotalMilliseconds;
                _output.WriteLine($"  burst {burst + 1}/{ThroughputBursts}: {thrSamples[burst]:F2} ms ({ThroughputConcurrency} concurrent)");
            }
            scenarios.Add(new CharacterizationScenario
            {
                Scenario = $"concurrent_throughput_ms_{modeLabel}",
                Description = $"Wall-clock ms for {ThroughputConcurrency} concurrent tools/call completions on warm sessions [{transportMode}]",
                Unit = "milliseconds",
                Iterations = thrSamples.Length,
                Stats = CharacterizationStats.FromSamples(thrSamples),
                RawSamples = thrSamples,
            });
        }
        finally
        {
            foreach (var c in thrClients)
                await c.DisposeAsync();
        }

        // ── Memory: idle ───────────────────────────────────────────────────────
        _output.WriteLine($"[{transportMode}] Memory: idle");
        await using var idleServer = new CharacterizationHttpServer();
        await idleServer.StartAsync(Phase4ComparisonFixture.ResolveAssetPath(withScriptConfig));
        var idleMb = idleServer.GetWorkingSetBytes() / (1024.0 * 1024.0);
        _output.WriteLine($"  idle: {idleMb:F1} MB");
        scenarios.Add(new CharacterizationScenario
        {
            Scenario = $"memory_idle_mb_{modeLabel}",
            Description = $"Server working-set at idle — after startup, before any sessions [{transportMode}]",
            Unit = "megabytes",
            Iterations = 1,
            Stats = CharacterizationStats.FromSamples([idleMb]),
            RawSamples = [idleMb],
        });

        // ── Memory: light load ─────────────────────────────────────────────────
        _output.WriteLine($"[{transportMode}] Memory: light load (10 sequential calls)");
        await using var lightServer = new CharacterizationHttpServer();
        await lightServer.StartAsync(Phase4ComparisonFixture.ResolveAssetPath(withScriptConfig));
        await using var lightClient = new CharacterizationMcpClient(lightServer.ServerUrl);
        await lightClient.InitializeAsync();
        for (var i = 0; i < 10; i++)
            await lightClient.CallGetDateAsync();
        var lightMb = lightServer.GetWorkingSetBytes() / (1024.0 * 1024.0);
        _output.WriteLine($"  light load: {lightMb:F1} MB");
        scenarios.Add(new CharacterizationScenario
        {
            Scenario = $"memory_light_load_mb_{modeLabel}",
            Description = $"Server working-set after 10 sequential tools/call on one session [{transportMode}]",
            Unit = "megabytes",
            Iterations = 1,
            Stats = CharacterizationStats.FromSamples([lightMb]),
            RawSamples = [lightMb],
        });

        // ── Memory: moderate load ──────────────────────────────────────────────
        _output.WriteLine($"[{transportMode}] Memory: moderate load (3 rounds × {ThroughputConcurrency} concurrent)");
        await using var modServer = new CharacterizationHttpServer();
        await modServer.StartAsync(Phase4ComparisonFixture.ResolveAssetPath(withScriptConfig));
        var modClients = new CharacterizationMcpClient[ThroughputConcurrency];
        try
        {
            for (var i = 0; i < ThroughputConcurrency; i++)
            {
                modClients[i] = new CharacterizationMcpClient(modServer.ServerUrl);
                await modClients[i].InitializeAsync();
            }
            for (var round = 0; round < 3; round++)
                await Task.WhenAll(modClients.Select(c => c.CallGetDateAsync()));
        }
        finally
        {
            foreach (var c in modClients)
                await c.DisposeAsync();
        }
        var modMb = modServer.GetWorkingSetBytes() / (1024.0 * 1024.0);
        _output.WriteLine($"  moderate load: {modMb:F1} MB");
        scenarios.Add(new CharacterizationScenario
        {
            Scenario = $"memory_moderate_load_mb_{modeLabel}",
            Description = $"Server working-set after 3 rounds × {ThroughputConcurrency} concurrent tools/call [{transportMode}]",
            Unit = "megabytes",
            Iterations = 1,
            Stats = CharacterizationStats.FromSamples([modMb]),
            RawSamples = [modMb],
        });

        // ── Compare to Phase 0 baseline ────────────────────────────────────────
        _output.WriteLine($"[{transportMode}] Comparing to Phase 0 baseline…");
        var comparison = PerformanceComparator.Compare(transportMode, _fixture.Baseline, scenarios);
        _fixture.RecordModeComparison(comparison);

        LogComparisonResults(transportMode, comparison);

        if (!comparison.AllPassed)
        {
            var failures = comparison.ThresholdChecks
                .Where(c => !c.Passed)
                .Select(c =>
                    $"  {c.Metric}: measured={c.MeasuredValue:F3} {c.Unit}, " +
                    $"baseline={c.BaselineValue:F3} {c.Unit}, " +
                    $"ratio={c.Ratio * 100:F1}% (max {c.MaxRatio * 100:F1}%)");
            Assert.Fail(
                $"Phase 4 [{transportMode}] performance gate breached. " +
                $"One or more thresholds exceeded the Phase 0 baseline:\n" +
                string.Join("\n", failures));
        }
    }

    private void LogComparisonResults(string transportMode, Phase4ModeComparison comparison)
    {
        _output.WriteLine($"\n── Phase 4 [{transportMode}] threshold results ──");
        foreach (var c in comparison.ThresholdChecks)
        {
            var verdict = c.Passed ? "PASS" : "FAIL";
            _output.WriteLine(
                $"  [{verdict}] {c.Metric}: " +
                $"{c.MeasuredValue:F3} / {c.BaselineValue:F3} = {c.Ratio * 100:F1}% " +
                $"(threshold ≤ {c.MaxRatio * 100:F1}%)");
        }
        var overall = comparison.AllPassed ? "ALL PASSED" : "GATE BREACHED";
        _output.WriteLine($"  Overall: {overall}");
    }

    private async Task<double[]> MeasureColdStartsAsync(
        string configPath,
        int iterations,
        string label)
    {
        var samples = new double[iterations];
        for (var i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            await using var server = new CharacterizationHttpServer();
            await server.StartAsync(configPath);
            await using var client = new CharacterizationMcpClient(server.ServerUrl);
            await client.InitializeAsync();
            await client.CallGetDateAsync();
            sw.Stop();
            samples[i] = sw.Elapsed.TotalMilliseconds;
            _output.WriteLine($"  [{label}] cold start {i + 1}/{iterations}: {samples[i]:F1} ms");
        }
        return samples;
    }
}
