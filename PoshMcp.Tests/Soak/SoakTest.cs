using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Soak;

/// <summary>
/// Sustained-load soak test for issue #349.
///
/// Acceptance contract (pre-declared before run):
///   1. ≥ 60 continuous minutes of load after 5-minute warmup
///   2. Error rate &lt; 0.1% (errors / total requests &lt; 0.001)
///   3. No server crash
///   4. Memory growth slope &lt; 1 MB/s (OLS regression on post-warmup WorkingSet samples)
///   5. Plateau delta &lt; 100 MB (mean of last 10% minus mean of first 10% of post-warmup samples)
///   6. Process handle slope &lt; 0.01/s (Windows); UNSUPPORTED on Linux/macOS
///   7. Process thread slope &lt; 0.01/s
///   8. TotalWorkers ≤ MaxPoolSize at every sample
///   9. Pool recovers to ≥ MinPoolSize within 6 samples (3 min) after any dip
///  10. Last 5 samples: WarmWorkers+LeasedWorkers ≥ MinPoolSize (stable end state)
///
/// Run category: "Soak" — excluded from PR CI. Execute via soak.yml workflow or
/// dotnet test --filter "Category=Soak".
/// </summary>
[Trait("Category", "Soak")]
public sealed class SoakTest
{
    private readonly ITestOutputHelper _output;

    public SoakTest(ITestOutputHelper output) => _output = output;

    [Fact(Timeout = 5_760_000)] // 96 min hard timeout (5+60 min + 31 min buffer)
    public async Task SustainedLoad_SixtyMinutes_MeetsAllAcceptanceCriteria()
    {
        // ── Pre-declare criteria ─────────────────────────────────────────────
        // These rules are recorded here before execution so reviewers can audit
        // the gate definition independently of run results.
        var cfg = new SoakConfig
        {
            WarmupDuration = TimeSpan.FromMinutes(5),
            SoakDuration = TimeSpan.FromMinutes(60),
            SampleInterval = TimeSpan.FromSeconds(30),
            ConcurrencyLevel = 4,
            MinRequestDelayMs = 50,
            MaxRequestDelayMs = 200,
            EvictionPhaseDuration = TimeSpan.FromMinutes(2),
            NormalPhaseDuration = TimeSpan.FromMinutes(13),
            MaxMemorySlopeBytesPerSecond = 1_048_576, // 1 MB/s
            MaxMemoryPlateauDeltaBytes = 100L * 1024 * 1024, // 100 MB
            PlateauWindowFraction = 0.10,
            MaxErrorRate = 0.001, // 0.1%
            MaxHandleSlopePerSecond = 0.01,
            MaxThreadSlopePerSecond = 0.01,
            EnforceWorkerUpperBound = true,
            ReplenishmentRecoverySamples = 6,
            StableEndSamples = 5,
        };

        _output.WriteLine("=== Soak Test #349 — Pre-declared Acceptance Criteria ===");
        _output.WriteLine($"  Duration: warmup {cfg.WarmupDuration.TotalMinutes:F0} min + soak {cfg.SoakDuration.TotalMinutes:F0} min");
        _output.WriteLine($"  Sample interval: {cfg.SampleInterval.TotalSeconds:F0}s");
        _output.WriteLine($"  Concurrency: {cfg.ConcurrencyLevel} workers");
        _output.WriteLine($"  Error rate threshold: {cfg.MaxErrorRate * 100:F1}% (denominator: all requests)");
        _output.WriteLine($"  Memory slope threshold: {cfg.MaxMemorySlopeBytesPerSecond / 1024.0:F0} KB/s (OLS, warmup excluded)");
        _output.WriteLine($"  Memory plateau delta threshold: {cfg.MaxMemoryPlateauDeltaBytes / (1024 * 1024.0):F0} MB");
        _output.WriteLine($"  Handle slope threshold: {cfg.MaxHandleSlopePerSecond:F3}/s (OS support required)");
        _output.WriteLine($"  Thread slope threshold: {cfg.MaxThreadSlopePerSecond:F3}/s");
        _output.WriteLine($"  Worker upper bound: TotalWorkers ≤ MaxPoolSize always");
        _output.WriteLine($"  Recovery: pool back to ≥ MinPoolSize within {cfg.ReplenishmentRecoverySamples} samples");

        // ── Environment metadata ─────────────────────────────────────────────
        var commitSha = GetCommitSha();
        var runtimeInfo = RuntimeInformation.FrameworkDescription;
        var osInfo = RuntimeInformation.OSDescription;
        var cpuCount = Environment.ProcessorCount;
        var startedAt = DateTimeOffset.UtcNow;
        var runId = startedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        _output.WriteLine($"\n  Commit: {commitSha}");
        _output.WriteLine($"  Runtime: {runtimeInfo}");
        _output.WriteLine($"  OS: {osInfo}");
        _output.WriteLine($"  CPUs: {cpuCount}");
        _output.WriteLine($"  Run ID: {runId}");

        // ── Locate config ────────────────────────────────────────────────────
        var configPath = Path.Combine(AppContext.BaseDirectory, "Soak", "Assets", "soak-appsettings.json");
        Assert.True(File.Exists(configPath), $"Soak config not found at {configPath}");

        // ── Artifact directory ────────────────────────────────────────────────
        var artifactDir = DetermineArtifactDir(runId);
        Directory.CreateDirectory(artifactDir);
        _output.WriteLine($"  Artifacts: {artifactDir}");

        // ── Logger ───────────────────────────────────────────────────────────
        var loggerFactory = LoggerFactory.Create(b =>
        {
            b.AddProvider(new TestOutputLoggerProvider(_output));
            b.SetMinimumLevel(LogLevel.Information);
        });
        var log = loggerFactory.CreateLogger<SoakTest>();

        // ── Run soak ─────────────────────────────────────────────────────────
        IReadOnlyList<SoakSample> samples;
        var harness = new SoakHarness(cfg, log, configPath);
        try
        {
            samples = await harness.RunAsync();
        }
        catch (Exception ex)
        {
            _output.WriteLine($"\nSoak run failed with exception: {ex}");
            // Write partial artifacts before re-throwing
            var partial = harness.Samples;
            if (partial.Count > 0)
                WriteArtifacts(artifactDir, runId, cfg, partial, commitSha, runtimeInfo, osInfo, cpuCount, startedAt, DateTimeOffset.UtcNow, new List<SoakGateResult>(), failed: true);
            throw;
        }
        finally
        {
            await harness.DisposeAsync();
            loggerFactory.Dispose();
        }

        var endedAt = DateTimeOffset.UtcNow;

        // ── Evaluate gates ───────────────────────────────────────────────────
        _output.WriteLine($"\n=== Soak Run Complete: {samples.Count} samples ===");

        var gates = SoakAnalyzer.Evaluate(samples, cfg);

        _output.WriteLine("\n=== Gate Results ===");
        foreach (var gate in gates)
        {
            var marker = gate.Passed ? "✓" : (gate.Status == "UNSUPPORTED" || gate.Status == "SKIP" ? "~" : "✗");
            _output.WriteLine($"  [{marker}] {gate.Gate,-28} {gate.Status,-12} {gate.Detail}");
        }

        // ── Write artifacts ──────────────────────────────────────────────────
        WriteArtifacts(artifactDir, runId, cfg, samples, commitSha, runtimeInfo, osInfo, cpuCount, startedAt, endedAt, gates, failed: false);
        _output.WriteLine($"\nArtifacts written to: {artifactDir}");

        // ── Assert all gates pass ────────────────────────────────────────────
        var failedGates = gates
            .Where(g => !g.Passed && g.Status != "UNSUPPORTED" && g.Status != "SKIP")
            .ToList();

        if (failedGates.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Soak test failed: {failedGates.Count} gate(s) did not pass.");
            foreach (var g in failedGates)
                sb.AppendLine($"  FAIL [{g.Gate}]: {g.Detail}");
            sb.AppendLine($"Artifacts: {artifactDir}");
            Assert.Fail(sb.ToString());
        }

        _output.WriteLine("\n✓ All soak acceptance gates passed.");
    }

    // ─── Artifact writing ─────────────────────────────────────────────────────

    private static void WriteArtifacts(
        string dir,
        string runId,
        SoakConfig cfg,
        IReadOnlyList<SoakSample> samples,
        string commitSha,
        string runtimeInfo,
        string osInfo,
        int cpuCount,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        IReadOnlyList<SoakGateResult> gates,
        bool failed)
    {
        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        // ── summary.json ──────────────────────────────────────────────────────
        var lastSample = samples.OrderBy(s => s.ElapsedMs).LastOrDefault();
        var totalRequests = lastSample?.TotalRequests ?? 0;
        var errorRequests = lastSample?.ErrorRequests ?? 0;
        var errorRate = totalRequests > 0 ? (double)errorRequests / totalRequests : 0;

        var summary = new
        {
            schema = "poshmcp-soak-v1",
            run_id = runId,
            started_at = startedAt,
            ended_at = endedAt,
            total_duration_seconds = (endedAt - startedAt).TotalSeconds,
            status = failed ? "FAILED" : (gates.All(g => g.Passed || g.Status == "UNSUPPORTED" || g.Status == "SKIP") ? "PASSED" : "FAILED"),
            commit_sha = commitSha,
            runtime = runtimeInfo,
            os = osInfo,
            cpu_count = cpuCount,
            config = new
            {
                soak_duration_minutes = cfg.SoakDuration.TotalMinutes,
                warmup_duration_minutes = cfg.WarmupDuration.TotalMinutes,
                sample_interval_seconds = cfg.SampleInterval.TotalSeconds,
                concurrency = cfg.ConcurrencyLevel,
                max_error_rate_pct = cfg.MaxErrorRate * 100,
                max_memory_slope_kbps = cfg.MaxMemorySlopeBytesPerSecond / 1024.0,
                max_memory_plateau_delta_mb = cfg.MaxMemoryPlateauDeltaBytes / (1024 * 1024.0),
                max_handle_slope_per_sec = cfg.MaxHandleSlopePerSecond,
                max_thread_slope_per_sec = cfg.MaxThreadSlopePerSecond,
            },
            totals = new
            {
                total_requests = totalRequests,
                error_requests = errorRequests,
                error_rate_pct = errorRate * 100,
                sample_count = samples.Count,
            },
            gates = gates.Select(g => new
            {
                gate = g.Gate,
                passed = g.Passed,
                status = g.Status,
                detail = g.Detail,
                measured = g.MeasuredValue,
                threshold = g.Threshold,
            }),
            samples = samples.Select(s => new
            {
                ts = s.Timestamp,
                elapsed_ms = s.ElapsedMs,
                phase = s.Phase,
                req_total = s.TotalRequests,
                req_success = s.SuccessRequests,
                req_error = s.ErrorRequests,
                int_req = s.IntervalRequests,
                int_err = s.IntervalErrors,
                p50_ms = s.P50LatencyMs,
                p99_ms = s.P99LatencyMs,
                ws_bytes = s.WorkingSetBytes,
                handles = s.HandleCountSupported ? s.ProcessHandleCount : (int?)null,
                threads = s.ProcessThreadCount,
                pool_warm = s.PoolStatsAvailable ? s.PoolWarm : (int?)null,
                pool_leased = s.PoolStatsAvailable ? s.PoolLeased : (int?)null,
                pool_total = s.PoolStatsAvailable ? s.PoolTotal : (int?)null,
                pool_min = s.PoolStatsAvailable ? s.PoolMin : (int?)null,
                pool_max = s.PoolStatsAvailable ? s.PoolMax : (int?)null,
                pool_started = s.PoolStatsAvailable ? s.PoolIsStarted : (bool?)null,
                note = s.Note,
            }),
        };

        var summaryJson = JsonSerializer.Serialize(summary, opts);
        File.WriteAllText(Path.Combine(dir, "summary.json"), summaryJson);

        // ── samples.csv ───────────────────────────────────────────────────────
        var csv = new StringBuilder();
        csv.AppendLine("timestamp,elapsed_ms,phase,req_total,req_success,req_error,int_req,int_err,p50_ms,p99_ms,ws_bytes,handles,threads,pool_warm,pool_leased,pool_total,pool_min,pool_max,pool_started,note");
        foreach (var s in samples.OrderBy(s => s.ElapsedMs))
        {
            csv.AppendLine(string.Join(",",
                s.Timestamp.ToString("o", CultureInfo.InvariantCulture),
                s.ElapsedMs,
                s.Phase,
                s.TotalRequests,
                s.SuccessRequests,
                s.ErrorRequests,
                s.IntervalRequests,
                s.IntervalErrors,
                s.P50LatencyMs?.ToString("F1", CultureInfo.InvariantCulture) ?? "",
                s.P99LatencyMs?.ToString("F1", CultureInfo.InvariantCulture) ?? "",
                s.WorkingSetBytes,
                s.HandleCountSupported ? s.ProcessHandleCount.ToString() : "N/A",
                s.ProcessThreadCount,
                s.PoolStatsAvailable ? s.PoolWarm.ToString() : "N/A",
                s.PoolStatsAvailable ? s.PoolLeased.ToString() : "N/A",
                s.PoolStatsAvailable ? s.PoolTotal.ToString() : "N/A",
                s.PoolStatsAvailable ? s.PoolMin.ToString() : "N/A",
                s.PoolStatsAvailable ? s.PoolMax.ToString() : "N/A",
                s.PoolStatsAvailable ? s.PoolIsStarted.ToString() : "N/A",
                s.Note ?? ""));
        }

        File.WriteAllText(Path.Combine(dir, "samples.csv"), csv.ToString());
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static string GetCommitSha()
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                Arguments = "rev-parse HEAD",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory,
            };
            using var p = Process.Start(psi);
            if (p is null) return "unknown";
            var sha = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(3000);
            return string.IsNullOrWhiteSpace(sha) ? "unknown" : sha;
        }
        catch
        {
            return "unknown";
        }
    }

    private static string DetermineArtifactDir(string runId)
    {
        // Allow CI to override via SOAK_ARTIFACT_DIR
        var envDir = Environment.GetEnvironmentVariable("SOAK_ARTIFACT_DIR");
        if (!string.IsNullOrWhiteSpace(envDir))
            return Path.Combine(envDir, runId);

        // Default: bench-runs/soak/<runId> relative to repo root
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory)
            ?? Path.Combine(Path.GetTempPath(), "poshmcp-soak");
        return Path.Combine(repoRoot, "bench-runs", "soak", runId);
    }

    private static string? FindRepoRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return null;
    }
}
