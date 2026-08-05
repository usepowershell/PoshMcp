using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Characterization;

/// <summary>
/// SDK 1.4.1 v1 baseline characterization suite.
///
/// Measures and records: cold-start latency (with and without startup script),
/// warm-call latency, concurrent throughput, and memory footprint at idle, light,
/// and moderate load. Results are written as a JSON artifact via
/// <see cref="CharacterizationFixture.DisposeAsync"/>.
///
/// These tests do NOT assert on timing thresholds. They record observed values so
/// Phase 4 (post SDK v2 upgrade) can compare relative regressions and improvements.
/// All scenarios must pass functionally: timeouts or server failures are real errors.
///
/// CI: <c>dotnet test --filter Category=Characterization</c>
/// Artifact: <c>TestResults/v1-baseline-characterization.json</c>
/// </summary>
[Trait("Category", "Characterization")]
public class V1BaselineCharacterizationTests : IClassFixture<CharacterizationFixture>
{
    // Predeclared sample counts — rationale (#380 AC2):
    //   Cold-start: 5 is sufficient (p95/p99 inherently noisy for cold starts;
    //     additional samples add runtime cost without statistical value).
    //   Warm-call: 50 (up from 20) — warm-call CV was >15% at N=20, yielding
    //     LOW confidence. N=50 targets CV≤5% for HIGH confidence with stable
    //     warm-call distributions. At ~10-30ms/call this adds <2s total.
    //   Throughput: 15 bursts (up from 5) — throughput variance was high at N=5.
    //     N=15 provides meaningful percentile spreads and CV≤10% target.
    //     At ~200ms/burst with concurrency=4 this adds <3s total.
    //   Concurrency: 4 unchanged — matches typical CI runner vCPU count.
    private const int ColdStartIterations = 5;
    private const int WarmCallIterations = 50;
    private const int ThroughputBursts = 15;
    private const int ThroughputConcurrency = 4;

    private readonly CharacterizationFixture _fixture;
    private readonly ITestOutputHelper _output;

    public V1BaselineCharacterizationTests(CharacterizationFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>
    /// Cold-start cost: server process start + module import + startup-script execution
    /// + MCP session initialize + first tools/call.
    /// </summary>
    [Fact(DisplayName = "cold_start_http_with_script")]
    public async Task ColdStartHttpWithScript()
    {
        var configPath = CharacterizationFixture.ResolveAssetPath("with-startup-script.appsettings.json");
        var samples = await MeasureColdStartsAsync(configPath, ColdStartIterations);

        _fixture.RecordScenario(new CharacterizationScenario
        {
            Scenario = "cold_start_http_with_script",
            Description = "HTTP server start + Microsoft.PowerShell.Utility import + inline startup script + first initialize + first tools/call",
            Iterations = samples.Length,
            Stats = CharacterizationStats.FromSamples(samples),
            RawSamples = samples,
        });
    }

    /// <summary>
    /// Cold-start cost without a startup script. Delta vs
    /// <see cref="ColdStartHttpWithScript"/> isolates startup-script execution cost.
    /// </summary>
    [Fact(DisplayName = "cold_start_http_no_script")]
    public async Task ColdStartHttpNoScript()
    {
        var configPath = CharacterizationFixture.ResolveAssetPath("no-startup-script.appsettings.json");
        var samples = await MeasureColdStartsAsync(configPath, ColdStartIterations);

        _fixture.RecordScenario(new CharacterizationScenario
        {
            Scenario = "cold_start_http_no_script",
            Description = "HTTP server start + Microsoft.PowerShell.Utility import + first initialize + first tools/call (no startup script)",
            Iterations = samples.Length,
            Stats = CharacterizationStats.FromSamples(samples),
            RawSamples = samples,
        });
    }

    /// <summary>
    /// Warm-call latency: repeated tool calls on a pre-initialized session.
    /// Uses the shared warm server from <see cref="CharacterizationFixture"/>.
    /// </summary>
    [Fact(DisplayName = "warm_call_latency_ms")]
    public async Task WarmCallLatency()
    {
        const int WarmupRounds = 3;
        await using var client = new CharacterizationMcpClient(_fixture.WarmServer.ServerUrl);
        await client.InitializeAsync();

        // Warmup rounds — excluded from measurement.
        for (var w = 0; w < WarmupRounds; w++)
            await client.CallGetDateAsync();

        var samples = new double[WarmCallIterations];
        for (var i = 0; i < WarmCallIterations; i++)
        {
            samples[i] = await client.CallGetDateAsync();
            _output.WriteLine($"  warm call {i + 1}/{WarmCallIterations}: {samples[i]:F1} ms");
        }

        _fixture.RecordWarmupCount("warm_call_latency_ms", WarmupRounds);
        _fixture.RecordScenario(new CharacterizationScenario
        {
            Scenario = "warm_call_latency_ms",
            Description = "Per-call HTTP round-trip latency on a pre-initialized session (runspace already acquired)",
            Iterations = samples.Length,
            Stats = CharacterizationStats.FromSamples(samples),
            RawSamples = samples,
        });
    }

    /// <summary>
    /// Concurrent throughput: wall-clock time for <see cref="ThroughputConcurrency"/>
    /// parallel tools/call completions on pre-warmed sessions.
    /// </summary>
    [Fact(DisplayName = "concurrent_throughput_ms")]
    public async Task ConcurrentThroughput()
    {
        const int PerClientWarmupCalls = 1;
        var clients = new CharacterizationMcpClient[ThroughputConcurrency];
        try
        {
            for (var i = 0; i < ThroughputConcurrency; i++)
            {
                clients[i] = new CharacterizationMcpClient(_fixture.WarmServer.ServerUrl);
                await clients[i].InitializeAsync();
                await clients[i].CallGetDateAsync(); // per-client warmup
            }

            var samples = new double[ThroughputBursts];
            for (var burst = 0; burst < ThroughputBursts; burst++)
            {
                var sw = Stopwatch.StartNew();
                await Task.WhenAll(clients.Select(c => c.CallGetDateAsync()));
                sw.Stop();
                samples[burst] = sw.Elapsed.TotalMilliseconds;
                _output.WriteLine($"  burst {burst + 1}/{ThroughputBursts}: {samples[burst]:F1} ms ({ThroughputConcurrency} concurrent)");
            }

            _fixture.RecordWarmupCount("concurrent_throughput_ms", PerClientWarmupCalls * ThroughputConcurrency);
            _fixture.RecordScenario(new CharacterizationScenario
            {
                Scenario = "concurrent_throughput_ms",
                Description = $"Wall-clock ms for {ThroughputConcurrency} concurrent tools/call completions on warm sessions",
                Iterations = samples.Length,
                Stats = CharacterizationStats.FromSamples(samples),
                RawSamples = samples,
            });
        }
        finally
        {
            foreach (var c in clients)
                await c.DisposeAsync();
        }
    }

    /// <summary>Server working-set at idle — right after startup, before any sessions.</summary>
    [Fact(DisplayName = "memory_idle_mb")]
    public async Task MemoryIdle()
    {
        await using var server = new CharacterizationHttpServer();
        await server.StartAsync(CharacterizationFixture.ResolveAssetPath("with-startup-script.appsettings.json"));

        var mb = server.GetWorkingSetBytes() / (1024.0 * 1024.0);
        _output.WriteLine($"  memory idle: {mb:F1} MB");

        _fixture.RecordScenario(new CharacterizationScenario
        {
            Scenario = "memory_idle_mb",
            Description = "Server process working-set (MB) at idle — after startup and module import, before any sessions",
            Unit = "megabytes",
            Iterations = 1,
            Stats = CharacterizationStats.FromSamples([mb]),
            RawSamples = [mb],
        });
    }

    /// <summary>Server working-set after light load: one session, 10 sequential calls.</summary>
    [Fact(DisplayName = "memory_light_load_mb")]
    public async Task MemoryLightLoad()
    {
        await using var server = new CharacterizationHttpServer();
        await server.StartAsync(CharacterizationFixture.ResolveAssetPath("with-startup-script.appsettings.json"));

        await using var client = new CharacterizationMcpClient(server.ServerUrl);
        await client.InitializeAsync();
        for (var i = 0; i < 10; i++)
            await client.CallGetDateAsync();

        var mb = server.GetWorkingSetBytes() / (1024.0 * 1024.0);
        _output.WriteLine($"  memory light load: {mb:F1} MB");

        _fixture.RecordScenario(new CharacterizationScenario
        {
            Scenario = "memory_light_load_mb",
            Description = "Server process working-set (MB) after 10 sequential tools/call on one session",
            Unit = "megabytes",
            Iterations = 1,
            Stats = CharacterizationStats.FromSamples([mb]),
            RawSamples = [mb],
        });
    }

    /// <summary>
    /// Server working-set under moderate load: <see cref="ThroughputConcurrency"/> sessions,
    /// 3 rounds of concurrent calls.
    /// </summary>
    [Fact(DisplayName = "memory_moderate_load_mb")]
    public async Task MemoryModerateLoad()
    {
        await using var server = new CharacterizationHttpServer();
        await server.StartAsync(CharacterizationFixture.ResolveAssetPath("with-startup-script.appsettings.json"));

        var clients = new CharacterizationMcpClient[ThroughputConcurrency];
        try
        {
            for (var i = 0; i < ThroughputConcurrency; i++)
            {
                clients[i] = new CharacterizationMcpClient(server.ServerUrl);
                await clients[i].InitializeAsync();
            }

            for (var round = 0; round < 3; round++)
                await Task.WhenAll(clients.Select(c => c.CallGetDateAsync()));

            var mb = server.GetWorkingSetBytes() / (1024.0 * 1024.0);
            _output.WriteLine($"  memory moderate load: {mb:F1} MB");

            _fixture.RecordScenario(new CharacterizationScenario
            {
                Scenario = "memory_moderate_load_mb",
                Description = $"Server process working-set (MB) after 3 rounds of {ThroughputConcurrency} concurrent tools/call",
                Unit = "megabytes",
                Iterations = 1,
                Stats = CharacterizationStats.FromSamples([mb]),
                RawSamples = [mb],
            });
        }
        finally
        {
            foreach (var c in clients)
                await c.DisposeAsync();
        }
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Diagnostic: pure HTTP GET /health latency.
    /// Not gated; used for stage attribution — subtract from warm_call_latency_ms
    /// to approximate MCP-layer + PowerShell + reset overhead.
    /// </summary>
    [Fact(DisplayName = "diagnostic_http_health_ms")]
    public async Task DiagnosticHttpHealth()
    {
        const int N = 20;
        const int Warmups = 3;
        using var http = new System.Net.Http.HttpClient
        {
            BaseAddress = new Uri(_fixture.WarmServer.ServerUrl),
            Timeout = TimeSpan.FromSeconds(10),
        };

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

        _fixture.RecordScenario(new CharacterizationScenario
        {
            Scenario = "diagnostic_http_health_ms",
            Description = "Diagnostic: pure HTTP GET /health round-trip (no MCP/PS). " +
                          "Not gated. Subtract from warm_call_latency_ms for stage attribution.",
            Iterations = samples.Length,
            Stats = CharacterizationStats.FromSamples(samples),
            RawSamples = samples,
        });
    }

    private async Task<double[]> MeasureColdStartsAsync(string configPath, int iterations)
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
            _output.WriteLine($"  cold start {i + 1}/{iterations}: {samples[i]:F1} ms");
        }
        return samples;
    }
}
