using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using PoshMcp.Server.PowerShell.Pool;
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
///   2. Derives all sample counts (N) from the Phase 0 baseline so both measurements
///      use the same N — required by PerformanceComparator.ValidateMethodologyMatch().
///   3. Runs all measurement scenarios for one transport mode.
///   4. Compares results to the Phase 0 baseline via <see cref="PerformanceComparator"/>.
///   5. Records the <see cref="Phase4ModeComparison"/> in the fixture for artifact generation.
///   6. Asserts all threshold checks pass — test failure = gate breach = release blocked.
///
/// Thresholds:
///   Cold-start p95      ≤ 110% of baseline
///   Warm-call p95       ≤ 105% of baseline
///   Throughput mean     ≤ 1/0.95 × baseline  (≥ 95% throughput rate)
///   Peak memory mean    ≤ 110% of baseline
///
/// Sample counts: derived at runtime from baseline.Scenarios[key].Iterations.
/// Currently Phase 0 uses: cold-start N=5, warm-call N=20, throughput N=5.
/// Phase 4 matches these automatically via GetBaselineSampleCount().
///
/// CI: <c>dotnet test --filter Category=PerformanceComparison</c>
/// Requires: <c>V1_BASELINE_PATH</c> env var → Phase 0 artifact path (ideally fresh same-runner).
/// Artifact: <c>PHASE4_ARTIFACT_PATH</c> env var (default TestResults/phase4-comparison.json).
/// </summary>
[Trait("Category", "PerformanceComparison")]
public class Phase4ComparisonTests : IClassFixture<Phase4ComparisonFixture>
{
    // Sample counts are read from the Phase 0 baseline at test runtime via
    // _fixture.GetBaselineSampleCount(). This ensures Phase 4 always uses the
    // same N as Phase 0 — the comparator's methodology-match check will fail if
    // they diverge. Do NOT hardcode different N values here.
    private const int WarmCallWarmupRounds = 3; // must match V1BaselineCharacterizationTests

    // Per-client warmup: 1 call per client before measurement — must match Phase 0.
    // Phase 0 ConcurrentThroughput does exactly 1 warmup call per client, no extra bursts.
    private const int ThroughputPerClientWarmupCalls = 1;

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
        var thresholdBreached = false;

        // ── Derive N from Phase 0 baseline (ensures methodology match) ───────
        var coldN = _fixture.GetBaselineSampleCount("cold_start_http_with_script");
        var warmCallN = _fixture.GetBaselineSampleCount("warm_call_latency_ms");
        var throughputN = _fixture.GetBaselineSampleCount("concurrent_throughput_ms");

        // Variables holding sample arrays — set by either measurement or preloaded path.
        double[] coldWithScript;
        double[] coldNoScript;
        double[] warmSamples;
        double[] thrSamples;

        // ── DEFERRED COMPARISON: load pre-collected samples, do NOT re-measure ──────────────
        if (_fixture.LoadSamplesFromArtifact)
        {
            if (!_fixture.TryGetPreloadedData(modeLabel, out var preloaded) || preloaded is null)
                throw new InvalidOperationException(
                    $"[{transportMode}] PHASE4_LOAD_SAMPLES_FROM is set but mode '{modeLabel}' has no " +
                    "preloaded data. Fail-closed: deferred comparison requires pre-collected samples for " +
                    "every executed mode. Ensure the collect-only step produced a complete artifact.");

            _output.WriteLine(
                $"[{transportMode}] DEFERRED COMPARISON: consuming preloaded samples captured at " +
                $"{preloaded.CapturedAt}. warm N={preloaded.WarmSamples.Length}, " +
                $"cold N={preloaded.ColdWithScript.Length}, throughput N={preloaded.ThroughputSamples.Length}. " +
                "No re-measurement of V2.");

            coldWithScript = preloaded.ColdWithScript;
            coldNoScript = preloaded.ColdNoScript;
            warmSamples = preloaded.WarmSamples;
            thrSamples = preloaded.ThroughputSamples;
            scenarios.AddRange(preloaded.AllScenarios);
        }
        else
        {
            // ── DIRECT MEASUREMENT (baseline_first path) ────────────────────────────────────

            // Start warm server BEFORE cold-start iterations (matches Phase 0 methodology).
            await using var warmServer = new CharacterizationHttpServer();
            await warmServer.StartAsync(Phase4ComparisonFixture.ResolveAssetPath(withScriptConfig));
            _output.WriteLine($"[{transportMode}] Warm server ready at {warmServer.ServerUrl}");

            // Cold-start: with startup script
            _output.WriteLine($"[{transportMode}] Cold-start (with startup script) — N={coldN}");
            coldWithScript = await MeasureColdStartsAsync(
                Phase4ComparisonFixture.ResolveAssetPath(withScriptConfig), coldN,
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

            // Cold-start: no startup script
            _output.WriteLine($"[{transportMode}] Cold-start (no startup script) — N={coldN}");
            coldNoScript = await MeasureColdStartsAsync(
                Phase4ComparisonFixture.ResolveAssetPath(noScriptConfig), coldN,
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

            // Warm-call latency
            _output.WriteLine($"[{transportMode}] Warm-call latency — {WarmCallWarmupRounds} warmup + N={warmCallN}");
            await using var warmClient = new CharacterizationMcpClient(warmServer.ServerUrl);
            await warmClient.InitializeAsync();
            for (var w = 0; w < WarmCallWarmupRounds; w++)
                await warmClient.CallGetDateAsync();

            warmSamples = new double[warmCallN];
            for (var i = 0; i < warmCallN; i++)
            {
                warmSamples[i] = await warmClient.CallGetDateAsync();
                _output.WriteLine($"  warm {i + 1}/{warmCallN}: {warmSamples[i]:F2} ms");
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

            // Concurrent throughput
            _output.WriteLine($"[{transportMode}] Concurrent throughput — {ThroughputPerClientWarmupCalls} warmup/client + N={throughputN} × {ThroughputConcurrency}");
            var thrClients = new CharacterizationMcpClient[ThroughputConcurrency];
            thrSamples = new double[throughputN];
            try
            {
                for (var i = 0; i < ThroughputConcurrency; i++)
                {
                    thrClients[i] = new CharacterizationMcpClient(warmServer.ServerUrl);
                    await thrClients[i].InitializeAsync();
                    for (var wc = 0; wc < ThroughputPerClientWarmupCalls; wc++)
                        await thrClients[i].CallGetDateAsync();
                }
                for (var burst = 0; burst < throughputN; burst++)
                {
                    var bsw = Stopwatch.StartNew();
                    await Task.WhenAll(thrClients.Select(c => c.CallGetDateAsync()));
                    bsw.Stop();
                    thrSamples[burst] = bsw.Elapsed.TotalMilliseconds;
                    _output.WriteLine($"  burst {burst + 1}/{throughputN}: {thrSamples[burst]:F2} ms");
                }
                scenarios.Add(new CharacterizationScenario
                {
                    Scenario = $"concurrent_throughput_ms_{modeLabel}",
                    Description = $"Wall-clock ms for {ThroughputConcurrency} concurrent tools/call completions [{transportMode}]",
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

            // Diagnostic: pure HTTP health-check latency
            _output.WriteLine($"[{transportMode}] Diagnostic HTTP health-check (20 samples, not gated)");
            await AddHealthCheckDiagnosticAsync(scenarios, warmServer.ServerUrl, modeLabel);

            // Memory: idle
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

            // Memory: light load
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
                Description = $"Server working-set after 10 sequential tools/call [{transportMode}]",
                Unit = "megabytes",
                Iterations = 1,
                Stats = CharacterizationStats.FromSamples([lightMb]),
                RawSamples = [lightMb],
            });

            // Memory: moderate load
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
        } // end direct measurement block

        // ── Compare to Phase 0 baseline (skipped in collect-only mode) ─────────
        if (_fixture.CollectOnly || _fixture.Baseline is null)
        {
            _output.WriteLine($"[{transportMode}] COLLECT-ONLY mode: skipping comparison (baseline deferred).");
            _fixture.RecordModeComparison(new Phase4ModeComparison
            {
                TransportMode = transportMode,
                AllPassed = false,
                Scenarios = scenarios,
                ThresholdChecks = [],
            });
        }
        else
        {
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
                _output.WriteLine(
                    $"[{transportMode}] THRESHOLD BREACH (deferred to classifier):\n" +
                    string.Join("\n", failures));
                thresholdBreached = true;
            }
        }

        // ── Record statistical reports for warm-call and throughput (#380 AC5) ──
        _fixture.RecordStatisticalReport(
            StatisticalReport.FromSamples($"warm_call_latency_ms_{modeLabel}", "milliseconds", warmSamples));
        _fixture.RecordStatisticalReport(
            StatisticalReport.FromSamples($"concurrent_throughput_ms_{modeLabel}", "milliseconds", thrSamples));

        // ── In-process pool diagnostic (#380 AC6) ──────────────────────────────
        // Separate diagnostic pass using StatelessRunspacePool + MeterListener.
        // NOT the gated subprocess measurement. Non-fatal: failure is logged but does not block gate.
        var poolDiagnostic = await RunPoolMetricsDiagnosticAsync(modeLabel);

        // ── Record stage attribution (#380 AC6) ────────────────────────────────
        var coldWithScriptScenario = scenarios.FirstOrDefault(
            s => s.Scenario == $"cold_start_http_with_script_{modeLabel}");
        var coldNoScriptScenario = scenarios.FirstOrDefault(
            s => s.Scenario == $"cold_start_http_no_script_{modeLabel}");
        var attribution = StageAttribution.Create(
            transportMode,
            warmSamples,
            coldWithScriptScenario?.RawSamples ?? [],
            coldNoScriptScenario?.RawSamples ?? [],
            poolDiagnostic);
        _fixture.RecordStageAttribution(attribution);
        _output.WriteLine($"[{transportMode}] Stage attribution: {attribution.Hypothesis}");

        if (thresholdBreached)
        {
            Assert.Fail(
                $"Phase 4 [{transportMode}] performance gate breached. " +
                "See comparison artifact for details. Gate decision deferred to Invoke-Phase4Gate.ps1.");
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

    private async Task AddHealthCheckDiagnosticAsync(
        List<CharacterizationScenario> scenarios,
        string serverUrl,
        string modeLabel)
    {
        const int N = 20;
        const int Warmups = 3;
        using var http = new HttpClient { BaseAddress = new Uri(serverUrl), Timeout = TimeSpan.FromSeconds(10) };
        for (var w = 0; w < Warmups; w++)
            await http.GetAsync("/health");

        var samples = new double[N];
        for (var i = 0; i < N; i++)
        {
            var sw = Stopwatch.StartNew();
            using var r = await http.GetAsync("/health");
            sw.Stop();
            r.EnsureSuccessStatusCode();
            samples[i] = sw.Elapsed.TotalMilliseconds;
            _output.WriteLine($"  health {i + 1}/{N}: {samples[i]:F2} ms");
        }

        scenarios.Add(new CharacterizationScenario
        {
            Scenario = $"diagnostic_http_health_ms_{modeLabel}",
            Description = $"Diagnostic: pure HTTP GET /health round-trip — not gated. " +
                          $"Subtract from warm_call_latency_ms for HTTP vs MCP+PS+reset attribution [{modeLabel}].",
            Iterations = N,
            Stats = CharacterizationStats.FromSamples(samples),
            RawSamples = samples,
        });
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

    /// <summary>
    /// In-process pool diagnostic via MeterListener on a freshly created StatelessRunspacePool.
    /// Provides acquisition, lease, and reset duration data mapped to lifecycle stages.
    /// This is a SEPARATE diagnostic pass — not the gated subprocess measurement.
    /// Limitation: the gated CharacterizationHttpServer runs in a subprocess; this data
    /// is from an in-process pool and is NOT a measurement of the subprocess server behavior.
    /// Non-fatal: exceptions are caught and logged; returns null if collection fails.
    /// </summary>
    private async Task<PoolDiagnosticData?> RunPoolMetricsDiagnosticAsync(
        string modeLabel, int warmupN = 5, int measuredN = 20)
    {
        _output.WriteLine($"[{modeLabel}] In-process pool diagnostic: {warmupN} warmup + {measuredN} measured calls");
        try
        {
            var options = new RunspacePoolOptions
            {
                MinPoolSize = 1,
                MaxPoolSize = 1,
                EagerWarmCount = 1,
            };
            await using var pool = new StatelessRunspacePool(options, loggerFactory: null);
            await pool.StartAsync(default);

            // Warmup — discard; listener not yet attached.
            for (var i = 0; i < warmupN; i++)
            {
                var lease = await pool.AcquireAsync(default);
                await using (lease)
                {
                    lease.PowerShell.AddScript("Get-Date");
                    await Task.Run(() => lease.PowerShell.Invoke());
                    lease.PowerShell.Commands.Clear();
                }
            }

            // Attach listener AFTER warmup so it only sees steady-state measurements.
            using var capture = new PoolDurationCapture(pool.MetricsMeter);

            var sw = Stopwatch.StartNew();
            for (var i = 0; i < measuredN; i++)
            {
                var lease = await pool.AcquireAsync(default);
                await using (lease)
                {
                    lease.PowerShell.AddScript("Get-Date");
                    await Task.Run(() => lease.PowerShell.Invoke());
                    lease.PowerShell.Commands.Clear();
                }
            }
            sw.Stop();

            capture.Dispose();
            var acqMs = capture.AcquisitionMs;
            var leaseMs = capture.LeaseMs;
            var resetMs = capture.ResetMs;

            if (acqMs.Count == 0 && leaseMs.Count == 0)
            {
                _output.WriteLine($"[{modeLabel}] Pool diagnostic: no samples captured; skipping.");
                return null;
            }

            // Estimate PS execution per-observation as lease - reset, excluding negatives.
            var psExecMs = new List<double>();
            var n = Math.Min(leaseMs.Count, resetMs.Count);
            for (var i = 0; i < n; i++)
            {
                var diff = leaseMs[i] - resetMs[i];
                if (diff >= 0) psExecMs.Add(diff);
            }

            double? Median(IReadOnlyList<double> xs) =>
                xs.Count == 0 ? null : xs.OrderBy(x => x).ElementAt(xs.Count / 2);

            var result = new PoolDiagnosticData
            {
                AcquisitionMs = [.. acqMs],
                LeaseMs = [.. leaseMs],
                ResetMs = [.. resetMs],
                PsExecutionMs = [.. psExecMs],
                WarmupCallCount = warmupN,
                MeasuredCallCount = measuredN,
                TotalDiagnosticSeconds = sw.Elapsed.TotalSeconds,
                Note = "In-process StatelessRunspacePool diagnostic (N=1 pool, Get-Date). " +
                       "This is NOT the subprocess server. Values are lifecycle timing proxies, " +
                       "not direct measurements of the gated HTTP/MCP path.",
            };

            var acqMedian = Median(acqMs);
            var leaseMedian = Median(leaseMs);
            var resetMedian = Median(resetMs);
            var psMedian = Median(psExecMs);
            _output.WriteLine(
                $"[{modeLabel}] Pool diagnostic: acq(median)={acqMedian?.ToString("F2") ?? "n/a"} ms, " +
                $"lease(median)={leaseMedian?.ToString("F2") ?? "n/a"} ms, " +
                $"reset(median)={resetMedian?.ToString("F2") ?? "n/a"} ms, " +
                $"psExec(median)={psMedian?.ToString("F2") ?? "n/a"} ms " +
                $"(n={result.AcquisitionMs.Length} acq, {result.LeaseMs.Length} lease, {result.ResetMs.Length} reset)");
            return result;
        }
        catch (Exception ex)
        {
            _output.WriteLine($"[{modeLabel}] Pool diagnostic failed (non-fatal): {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Collects histogram measurements from a specific <see cref="Meter"/> instance via
    /// <see cref="MeterListener"/>. Filtered by reference equality to avoid cross-contamination.
    /// Must be disposed to detach the listener before the pool is disposed.
    /// </summary>
    private sealed class PoolDurationCapture : IDisposable
    {
        private readonly MeterListener _listener;
        private readonly ConcurrentQueue<double> _acquisition = new();
        private readonly ConcurrentQueue<double> _lease = new();
        private readonly ConcurrentQueue<double> _reset = new();
        private readonly Meter _meter;

        internal PoolDurationCapture(Meter meter)
        {
            _meter = meter;
            _listener = new MeterListener();
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter, _meter) &&
                    instrument is Histogram<double>)
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<double>(OnMeasurement);
            _listener.Start();
        }

        private void OnMeasurement(
            Instrument instrument, double measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
        {
            // Convert seconds → milliseconds; ignore non-positive values.
            if (measurement <= 0) return;
            var ms = measurement * 1000.0;
            if (instrument.Name == "poshmcp.runspace_pool.acquisition_duration_seconds") _acquisition.Enqueue(ms);
            else if (instrument.Name == "poshmcp.runspace_pool.lease_duration_seconds") _lease.Enqueue(ms);
            else if (instrument.Name == "poshmcp.runspace_pool.reset_duration_seconds") _reset.Enqueue(ms);
        }

        internal IReadOnlyList<double> AcquisitionMs => [.. _acquisition];
        internal IReadOnlyList<double> LeaseMs => [.. _lease];
        internal IReadOnlyList<double> ResetMs => [.. _reset];

        public void Dispose() => _listener.Dispose();
    }
}
