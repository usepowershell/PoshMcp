using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Soak;

/// <summary>
/// Sustained-load soak test for issue #349.
///
/// <para>Run shape (phases): <c>baseline → warmup → load → cooldown</c>. Only the load phase
/// feeds the memory/handle-floor/thread trend gates; warmup is excluded; the cooldown idle floor
/// is compared to the baseline idle floor to prove handle recovery. See <see cref="SoakConfig"/>
/// for the full mathematical contract, including why the handle gate regresses per-window floors
/// rather than the raw sawtooth series.</para>
///
/// <para>Phase durations are the release contract (in <see cref="SoakConfig"/> defaults) but may be
/// shortened for a smoke/plumbing run via <c>SOAK_*_MINUTES</c> / <c>SOAK_SAMPLE_SECONDS</c>
/// environment overrides. Acceptance <em>thresholds</em> are never overridable.</para>
///
/// <para>Run category: "Soak" — excluded from PR CI. Execute via soak.yml or
/// <c>dotnet test --filter "Category=Soak"</c>.</para>
/// </summary>
[Trait("Category", "Soak")]
public sealed class SoakTest
{
    private readonly ITestOutputHelper _output;

    public SoakTest(ITestOutputHelper output) => _output = output;

    [Fact(Timeout = 7_200_000)] // 120 min hard timeout (release run ≈ 74 min + build/start + buffer)
    public async Task SustainedLoad_SixtyMinutes_MeetsAllAcceptanceCriteria()
    {
        // ── Pre-declared contract (durations overridable for smoke; thresholds fixed) ──
        var cfg = new SoakConfig
        {
            BaselineDuration = EnvMinutes("SOAK_BASELINE_MINUTES", TimeSpan.FromMinutes(3)),
            WarmupDuration = EnvMinutes("SOAK_WARMUP_MINUTES", TimeSpan.FromMinutes(5)),
            LoadDuration = EnvMinutes("SOAK_LOAD_MINUTES", TimeSpan.FromMinutes(61)),
            CooldownDuration = EnvMinutes("SOAK_COOLDOWN_MINUTES", TimeSpan.FromMinutes(5)),
            MinLoadDuration = EnvMinutes("SOAK_MIN_LOAD_MINUTES", TimeSpan.FromMinutes(60)),
            SampleInterval = EnvSeconds("SOAK_SAMPLE_SECONDS", TimeSpan.FromSeconds(30)),
            NormalPhaseDuration = EnvMinutes("SOAK_NORMAL_MINUTES", TimeSpan.FromMinutes(13)),
            EvictionPhaseDuration = EnvMinutes("SOAK_EVICTION_MINUTES", TimeSpan.FromMinutes(2)),
            HandleFloorWindow = EnvMinutes("SOAK_FLOOR_WINDOW_MINUTES", TimeSpan.FromMinutes(5)),
            BurstConcurrencyLevel = EnvInt("SOAK_BURST_WORKERS", 0),
            TrafficMode = EnvTrafficMode("SOAK_TRAFFIC_MODE", SoakTrafficMode.FullMix),
        };
        cfg.Validate();

        _output.WriteLine("=== Soak Test #349 — Pre-declared Acceptance Contract (schema v2) ===");
        _output.WriteLine($"  Phases: baseline {cfg.BaselineDuration.TotalMinutes:F0}m → warmup {cfg.WarmupDuration.TotalMinutes:F0}m → load {cfg.LoadDuration.TotalMinutes:F0}m → cooldown {cfg.CooldownDuration.TotalMinutes:F0}m");
        _output.WriteLine($"  Sample interval: {cfg.SampleInterval.TotalSeconds:F0}s");
        _output.WriteLine($"  Min measured load: {cfg.MinLoadDuration.TotalMinutes:F0}m (load phase only)");
        _output.WriteLine($"  Error rate ≤ {cfg.MaxErrorRate * 100:F1}% (all traffic)");
        _output.WriteLine($"  Memory slope ≤ {cfg.MaxMemorySlopeBytesPerSecond / 1024.0:F0} KB/s; plateau ≤ {cfg.MaxMemoryPlateauDeltaBytes / (1024 * 1024.0):F0} MB (load, warmup excluded)");
        _output.WriteLine($"  Handle FLOOR slope ≤ {cfg.MaxHandleFloorSlopePerSecond:F3}/s over {cfg.HandleFloorWindow.TotalMinutes:F0}m windows, p{cfg.HandleFloorQuantile * 100:F0} floor, ≥{cfg.MinHandleFloorWindowSamples} samples/window (Windows)");
        _output.WriteLine($"  Handle cooldown plateau ≤ max({cfg.HandleCooldownPlateauMaxDeltaAbsolute:F0} abs, {cfg.HandleCooldownPlateauMaxDeltaRelative * 100:F0}% rel) vs baseline floor");
        _output.WriteLine($"  Thread slope ≤ {cfg.MaxThreadSlopePerSecond:F3}/s");
        _output.WriteLine($"  Pool/health coverage ≥ {cfg.MinPoolStatsCoverage * 100:F0}% of load samples");
        _output.WriteLine($"  Worker upper bound: TotalWorkers ≤ MaxPoolSize; recovery within {cfg.ReplenishmentRecoverySamples} samples");
        if (cfg.BurstConcurrencyLevel > 0)
            _output.WriteLine($"  Burst workers: {cfg.BurstConcurrencyLevel} for {cfg.BurstPhaseDuration.TotalMinutes:F0}m per cycle (diagnostic; pool scaling exercise)");
        _output.WriteLine($"  Traffic mode: {cfg.TrafficMode} (full_mix=PS execution; tools_list_only=HTTP/MCP comparison)");

        // ── Provenance ───────────────────────────────────────────────────────
        var provenance = CaptureProvenance();
        var startedAt = DateTimeOffset.UtcNow;
        var runId = startedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        _output.WriteLine($"\n  Commit: {provenance.CommitSha} (dirty={provenance.Dirty})");
        _output.WriteLine($"  Runtime: {provenance.Runtime}");
        _output.WriteLine($"  OS: {provenance.Os}");
        _output.WriteLine($"  CPUs: {provenance.CpuCount}");
        _output.WriteLine($"  Server DLL: {provenance.ServerDllPath} (sha256={provenance.ServerDllSha256})");
        _output.WriteLine($"  Config: {provenance.ConfigPath} (sha256={provenance.ConfigSha256})");
        _output.WriteLine($"  Workflow: run={provenance.WorkflowRunId} attempt={provenance.WorkflowRunAttempt} job={provenance.WorkflowJob}");
        _output.WriteLine($"  Run ID: {runId}");

        Assert.True(File.Exists(provenance.ConfigPath), $"Soak config not found at {provenance.ConfigPath}");

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
        // Pass artifactDir so the harness can capture server stdout/stderr to durable files.
        var harness = new SoakHarness(cfg, log, provenance.ConfigPath, artifactDir);
        try
        {
            samples = await harness.RunAsync();
        }
        catch (Exception ex)
        {
            _output.WriteLine($"\nSoak run failed with exception: {ex}");
            provenance = provenance with { ServerPid = harness.ServerProcessId, ServerStartTimeUtc = harness.ServerStartTimeUtc, ServerUnexpectedExitCode = harness.UnexpectedExitCode };
            var partial = harness.Samples;
            // Always emit an explicit FAILED summary so a partial/aborted run never disappears.
            WriteArtifacts(artifactDir, runId, cfg, partial, provenance, startedAt, DateTimeOffset.UtcNow,
                new List<SoakGateResult>(), failed: true, failureReason: ex.GetType().Name + ": " + ex.Message);
            throw;
        }
        finally
        {
            await harness.DisposeAsync();
            loggerFactory.Dispose();
        }

        provenance = provenance with { ServerPid = harness.ServerProcessId, ServerStartTimeUtc = harness.ServerStartTimeUtc, ServerUnexpectedExitCode = harness.UnexpectedExitCode };
        var endedAt = DateTimeOffset.UtcNow;

        // ── Evaluate gates ───────────────────────────────────────────────────
        _output.WriteLine($"\n=== Soak Run Complete: {samples.Count} samples ===");
        var byPhase = samples.GroupBy(s => s.Phase).ToDictionary(g => g.Key, g => g.Count());
        foreach (var kv in byPhase.OrderBy(k => k.Key))
            _output.WriteLine($"  phase '{kv.Key}': {kv.Value} samples");

        var gates = SoakAnalyzer.Evaluate(samples, cfg);

        _output.WriteLine("\n=== Gate Results ===");
        foreach (var gate in gates)
        {
            var marker = gate.Status == "DIAGNOSTIC" ? "i"
                : gate.Passed ? "PASS"
                : gate.Status is "UNSUPPORTED" or "SKIP" ? "~" : "FAIL";
            _output.WriteLine($"  [{marker,-4}] {gate.Gate,-28} {gate.Status,-12} {gate.Detail}");
        }

        // ── Write artifacts (before asserting) ────────────────────────────────
        WriteArtifacts(artifactDir, runId, cfg, samples, provenance, startedAt, endedAt, gates, failed: false, failureReason: null);
        _output.WriteLine($"\nArtifacts written to: {artifactDir}");

        // ── Assert all real gates pass (DIAGNOSTIC/SKIP/UNSUPPORTED excluded) ──
        var failedGates = gates
            .Where(g => !g.Passed && g.Status is not ("UNSUPPORTED" or "SKIP" or "DIAGNOSTIC"))
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

        _output.WriteLine("\nAll soak acceptance gates passed.");
    }

    // ─── Provenance ─────────────────────────────────────────────────────────

    private sealed record Provenance
    {
        public string CommitSha { get; init; } = "unknown";
        public bool Dirty { get; init; }
        public string Runtime { get; init; } = "";
        public string Os { get; init; } = "";
        public int CpuCount { get; init; }
        public string ServerDllPath { get; init; } = "";
        public string ServerDllSha256 { get; init; } = "";
        public string ConfigPath { get; init; } = "";
        public string ConfigSha256 { get; init; } = "";
        public string? WorkflowRunId { get; init; }
        public string? WorkflowRunAttempt { get; init; }
        public string? WorkflowJob { get; init; }
        public string? WorkflowName { get; init; }
        public int? ServerPid { get; init; }
        public DateTime? ServerStartTimeUtc { get; init; }
        /// <summary>Non-null when the server process exited unexpectedly during the run; null on normal harness shutdown.</summary>
        public int? ServerUnexpectedExitCode { get; init; }
    }

    private static Provenance CaptureProvenance()
    {
        var serverDll = typeof(PoshMcp.Server.PowerShell.PowerShellConfiguration).Assembly.Location;
        var configPath = Path.Combine(AppContext.BaseDirectory, "Soak", "Assets", "soak-appsettings.json");
        return new Provenance
        {
            CommitSha = GetCommitSha(),
            Dirty = IsWorkingTreeDirty(),
            Runtime = RuntimeInformation.FrameworkDescription,
            Os = RuntimeInformation.OSDescription,
            CpuCount = Environment.ProcessorCount,
            ServerDllPath = serverDll,
            ServerDllSha256 = Sha256File(serverDll),
            ConfigPath = configPath,
            ConfigSha256 = Sha256File(configPath),
            WorkflowRunId = Environment.GetEnvironmentVariable("GITHUB_RUN_ID"),
            WorkflowRunAttempt = Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT"),
            WorkflowJob = Environment.GetEnvironmentVariable("GITHUB_JOB"),
            WorkflowName = Environment.GetEnvironmentVariable("GITHUB_WORKFLOW"),
        };
    }

    // ─── Artifact writing ─────────────────────────────────────────────────────

    private static void WriteArtifacts(
        string dir,
        string runId,
        SoakConfig cfg,
        IReadOnlyList<SoakSample> samples,
        Provenance prov,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        IReadOnlyList<SoakGateResult> gates,
        bool failed,
        string? failureReason)
    {
        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        var ordered = samples.OrderBy(s => s.ElapsedMs).ToList();
        var lastSample = ordered.LastOrDefault();
        var totalRequests = lastSample?.TotalRequests ?? 0;
        var errorRequests = lastSample?.ErrorRequests ?? 0;
        var errorRate = totalRequests > 0 ? (double)errorRequests / totalRequests : 0;

        var loadSamples = ordered.Where(s => s.Phase == SoakAnalyzer.PhaseLoad).ToList();
        var loadSpanMinutes = loadSamples.Count >= 2
            ? (loadSamples[^1].ElapsedMs - loadSamples[0].ElapsedMs) / 60000.0
            : 0.0;

        var status = failed
            ? "FAILED"
            : gates.Count > 0 && gates.All(g => g.Passed || g.Status is "UNSUPPORTED" or "SKIP" or "DIAGNOSTIC")
                ? "PASSED"
                : "FAILED";

        var summary = new
        {
            schema = "poshmcp-soak-v2",
            run_id = runId,
            started_at = startedAt,
            ended_at = endedAt,
            total_duration_seconds = (endedAt - startedAt).TotalSeconds,
            status,
            failure_reason = failureReason,
            provenance = new
            {
                source_commit_sha = prov.CommitSha,
                dirty_working_tree = prov.Dirty,
                runtime = prov.Runtime,
                os = prov.Os,
                cpu_count = prov.CpuCount,
                server_dll_path = prov.ServerDllPath,
                server_dll_sha256 = prov.ServerDllSha256,
                config_path = prov.ConfigPath,
                config_sha256 = prov.ConfigSha256,
                server_pid = prov.ServerPid,
                server_start_time_utc = prov.ServerStartTimeUtc,
                server_unexpected_exit_code = prov.ServerUnexpectedExitCode,
                server_stdout_artifact = "server-stdout.txt",
                server_stderr_artifact = "server-stderr.txt",
                workflow_run_id = prov.WorkflowRunId,
                workflow_run_attempt = prov.WorkflowRunAttempt,
                workflow_job = prov.WorkflowJob,
                workflow_name = prov.WorkflowName,
            },
            config = new
            {
                schema_version = SoakConfig.SchemaVersion,
                baseline_minutes = cfg.BaselineDuration.TotalMinutes,
                warmup_minutes = cfg.WarmupDuration.TotalMinutes,
                load_minutes = cfg.LoadDuration.TotalMinutes,
                cooldown_minutes = cfg.CooldownDuration.TotalMinutes,
                min_load_minutes = cfg.MinLoadDuration.TotalMinutes,
                sample_interval_seconds = cfg.SampleInterval.TotalSeconds,
                concurrency = cfg.ConcurrencyLevel,
                burst_concurrency = cfg.BurstConcurrencyLevel,
                burst_phase_minutes = cfg.BurstPhaseDuration.TotalMinutes,
                min_handle_floor_window_samples = cfg.MinHandleFloorWindowSamples,
                max_error_rate_pct = cfg.MaxErrorRate * 100,
                max_memory_slope_kbps = cfg.MaxMemorySlopeBytesPerSecond / 1024.0,
                max_memory_plateau_delta_mb = cfg.MaxMemoryPlateauDeltaBytes / (1024 * 1024.0),
                handle_floor_window_minutes = cfg.HandleFloorWindow.TotalMinutes,
                handle_floor_quantile = cfg.HandleFloorQuantile,
                max_handle_floor_slope_per_sec = cfg.MaxHandleFloorSlopePerSecond,
                handle_cooldown_plateau_abs = cfg.HandleCooldownPlateauMaxDeltaAbsolute,
                handle_cooldown_plateau_rel = cfg.HandleCooldownPlateauMaxDeltaRelative,
                max_thread_slope_per_sec = cfg.MaxThreadSlopePerSecond,
                min_pool_stats_coverage = cfg.MinPoolStatsCoverage,
            },
            totals = new
            {
                total_requests = totalRequests,
                error_requests = errorRequests,
                error_rate_pct = errorRate * 100,
                sample_count = samples.Count,
                load_sample_count = loadSamples.Count,
                load_span_minutes = loadSpanMinutes,
                initialize_requests = lastSample?.InitializeRequests ?? 0,
                tools_list_requests = lastSample?.ToolsListRequests ?? 0,
                tools_call_requests = lastSample?.ToolsCallRequests ?? 0,
                tools_call_ps_success = lastSample?.ToolsCallPsSuccess ?? 0,
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
            samples = ordered.Select(s => new
            {
                ts = s.Timestamp,
                elapsed_ms = s.ElapsedMs,
                phase = s.Phase,
                req_total = s.TotalRequests,
                req_success = s.SuccessRequests,
                req_error = s.ErrorRequests,
                req_initialize = s.InitializeRequests,
                req_tools_list = s.ToolsListRequests,
                req_tools_call = s.ToolsCallRequests,
                req_tools_call_ps_success = s.ToolsCallPsSuccess,
                int_req = s.IntervalRequests,
                int_err = s.IntervalErrors,
                p50_ms = s.P50LatencyMs,
                p99_ms = s.P99LatencyMs,
                ws_bytes = s.WorkingSetBytes,
                handles = s.HandleCountSupported ? s.ProcessHandleCount : (int?)null,
                threads = s.ProcessThreadCount,
                gc_collections = s.GcCollectionCount >= 0 ? s.GcCollectionCount : (int?)null,
                pool_warm = s.PoolStatsAvailable ? s.PoolWarm : (int?)null,
                pool_leased = s.PoolStatsAvailable ? s.PoolLeased : (int?)null,
                pool_total = s.PoolStatsAvailable ? s.PoolTotal : (int?)null,
                pool_min = s.PoolStatsAvailable ? s.PoolMin : (int?)null,
                pool_max = s.PoolStatsAvailable ? s.PoolMax : (int?)null,
                pool_started = s.PoolStatsAvailable ? s.PoolIsStarted : (bool?)null,
                pool_available = s.PoolStatsAvailable,
                note = s.Note,
            }),
        };

        File.WriteAllText(Path.Combine(dir, "summary.json"), JsonSerializer.Serialize(summary, opts));

        // ── effective-config.json (standalone — reproducibility reference) ────
        // The full runtime SoakConfig and server appsettings are captured separately so
        // offline reproduction of the gate decisions does not require parsing summary.json.
        var effectiveConfig = new
        {
            schema = "poshmcp-soak-effective-config-v1",
            generated_at = DateTimeOffset.UtcNow,
            run_id = runId,
            soak_config = new
            {
                schema_version = SoakConfig.SchemaVersion,
                baseline_duration_minutes = cfg.BaselineDuration.TotalMinutes,
                warmup_duration_minutes = cfg.WarmupDuration.TotalMinutes,
                load_duration_minutes = cfg.LoadDuration.TotalMinutes,
                cooldown_duration_minutes = cfg.CooldownDuration.TotalMinutes,
                min_load_duration_minutes = cfg.MinLoadDuration.TotalMinutes,
                sample_interval_seconds = cfg.SampleInterval.TotalSeconds,
                concurrency_level = cfg.ConcurrencyLevel,
                burst_concurrency_level = cfg.BurstConcurrencyLevel,
                burst_phase_duration_minutes = cfg.BurstPhaseDuration.TotalMinutes,
                traffic_mode = cfg.TrafficMode.ToString(),
                min_request_delay_ms = cfg.MinRequestDelayMs,
                max_request_delay_ms = cfg.MaxRequestDelayMs,
                normal_phase_duration_minutes = cfg.NormalPhaseDuration.TotalMinutes,
                eviction_phase_duration_minutes = cfg.EvictionPhaseDuration.TotalMinutes,
                // thresholds — these are fixed by contract and must not be post-hoc tuned
                max_error_rate = cfg.MaxErrorRate,
                max_memory_slope_bytes_per_second = cfg.MaxMemorySlopeBytesPerSecond,
                max_memory_plateau_delta_bytes = cfg.MaxMemoryPlateauDeltaBytes,
                plateau_window_fraction = cfg.PlateauWindowFraction,
                handle_floor_window_minutes = cfg.HandleFloorWindow.TotalMinutes,
                handle_floor_quantile = cfg.HandleFloorQuantile,
                min_handle_floor_window_samples = cfg.MinHandleFloorWindowSamples,
                max_handle_floor_slope_per_second = cfg.MaxHandleFloorSlopePerSecond,
                handle_cooldown_plateau_max_delta_absolute = cfg.HandleCooldownPlateauMaxDeltaAbsolute,
                handle_cooldown_plateau_max_delta_relative = cfg.HandleCooldownPlateauMaxDeltaRelative,
                max_thread_slope_per_second = cfg.MaxThreadSlopePerSecond,
                enforce_worker_upper_bound = cfg.EnforceWorkerUpperBound,
                min_pool_stats_coverage = cfg.MinPoolStatsCoverage,
                replenishment_recovery_samples = cfg.ReplenishmentRecoverySamples,
                stable_end_samples = cfg.StableEndSamples,
            },
            server_config = new
            {
                path = prov.ConfigPath,
                sha256 = prov.ConfigSha256,
            },
            server_dll = new
            {
                path = prov.ServerDllPath,
                sha256 = prov.ServerDllSha256,
            },
        };
        File.WriteAllText(Path.Combine(dir, "effective-config.json"), JsonSerializer.Serialize(effectiveConfig, opts));

        // ── analyzer-inputs.json (decision evidence — raw gate inputs) ────────
        // Captures the intermediate per-gate inputs so gate decisions can be reproduced
        // and audited without re-running the soak: OLS points, window floors, and raw counts.
        var loadSamplesWithHandles = loadSamples.Where(s => s.HandleCountSupported).ToList();
        var handlePoints = loadSamplesWithHandles
            .Select(s => (ElapsedSeconds: s.ElapsedMs / 1000.0, Handles: (double)s.ProcessHandleCount))
            .ToList();
        var allFloors = SoakAnalyzer.WindowFloors(handlePoints, cfg.HandleFloorWindow.TotalSeconds, cfg.HandleFloorQuantile, 1);
        var qualifiedFloors = SoakAnalyzer.WindowFloors(handlePoints, cfg.HandleFloorWindow.TotalSeconds, cfg.HandleFloorQuantile, cfg.MinHandleFloorWindowSamples);
        var analyzerInputs = new
        {
            schema = "poshmcp-soak-analyzer-inputs-v1",
            generated_at = DateTimeOffset.UtcNow,
            run_id = runId,
            handle_floor_analysis = new
            {
                min_samples_per_window = cfg.MinHandleFloorWindowSamples,
                window_seconds = cfg.HandleFloorWindow.TotalSeconds,
                quantile = cfg.HandleFloorQuantile,
                threshold_slope_per_second = cfg.MaxHandleFloorSlopePerSecond,
                all_windows = allFloors.Select(f => new { center_seconds = f.CenterSeconds, floor = f.Floor }),
                qualified_windows = qualifiedFloors.Select(f => new { center_seconds = f.CenterSeconds, floor = f.Floor }),
                excluded_window_count = allFloors.Count - qualifiedFloors.Count,
                raw_handle_points_count = handlePoints.Count,
            },
            gate_decisions = gates.Select(g => new
            {
                gate = g.Gate,
                passed = g.Passed,
                status = g.Status,
                detail = g.Detail,
                measured = g.MeasuredValue,
                threshold = g.Threshold,
            }),
            handle_type_evidence = new
            {
                available = false,
                limitation = "Process.HandleCount on Windows returns a single total across all kernel object types. " +
                    "Type breakdown (File, Event, Mutex, etc.) requires ETW handle tracking or an external tool such as " +
                    "Sysinternals handle.exe, which cannot be safely automated on hosted GitHub Actions runners without " +
                    "a trusted binary dependency. A future investigation can enable ETW handle tracking via " +
                    "'Start-Trace -ETW -Provider Microsoft-Windows-Kernel-Process' with the HandleCreate/HandleClose " +
                    "events and correlate event IDs to object types. No fake signal is emitted here.",
            },
            comparison_modes = new
            {
                available = new[] { "full_mix", "tools_list_only" },
                current_mode = cfg.TrafficMode == SoakTrafficMode.ToolsListOnly ? "tools_list_only" : "full_mix",
                full_mix_description = "Default production mode: ~10% initialize, ~50% tools/list, ~40% tools/call with real PowerShell execution. " +
                    "This is the authoritative mode for acceptance gates.",
                tools_list_only_description = "Comparison/diagnostic mode: 100% tools/list requests; initialize and tools/call are omitted. " +
                    "Exercises list-only MCP protocol traffic over HTTP without PowerShell execution. " +
                    "Use SOAK_TRAFFIC_MODE=tools_list_only to enable. " +
                    "A handle-floor slope comparison between modes isolates protocol overhead from PowerShell execution overhead. " +
                    "Acceptance gates are identical in both modes except ps_execution is SKIP; no threshold is relaxed. " +
                    "full_mix remains the authoritative acceptance mode.",
                ps_execution_isolation_note = "A tools_list_only run that passes handle_floor_slope would confirm PowerShell execution " +
                    "(runspace creation, PSDataCollection event handles, finalizer pressure) is the primary handle source. " +
                    "A tools_list_only run that also fails would indicate the HTTP/MCP layer or .NET runtime is the source.",
            },
        };
        File.WriteAllText(Path.Combine(dir, "analyzer-inputs.json"), JsonSerializer.Serialize(analyzerInputs, opts));

        // ── samples.csv ───────────────────────────────────────────────────────
        var csv = new StringBuilder();
        csv.AppendLine("timestamp,elapsed_ms,phase,req_total,req_success,req_error,req_initialize,req_tools_list,req_tools_call,req_tools_call_ps_success,int_req,int_err,p50_ms,p99_ms,ws_bytes,handles,threads,gc_collections,pool_warm,pool_leased,pool_total,pool_min,pool_max,pool_started,pool_available,note");
        foreach (var s in ordered)
        {
            csv.AppendLine(string.Join(",",
                s.Timestamp.ToString("o", CultureInfo.InvariantCulture),
                s.ElapsedMs,
                s.Phase,
                s.TotalRequests,
                s.SuccessRequests,
                s.ErrorRequests,
                s.InitializeRequests,
                s.ToolsListRequests,
                s.ToolsCallRequests,
                s.ToolsCallPsSuccess,
                s.IntervalRequests,
                s.IntervalErrors,
                s.P50LatencyMs?.ToString("F1", CultureInfo.InvariantCulture) ?? "",
                s.P99LatencyMs?.ToString("F1", CultureInfo.InvariantCulture) ?? "",
                s.WorkingSetBytes,
                s.HandleCountSupported ? s.ProcessHandleCount.ToString(CultureInfo.InvariantCulture) : "N/A",
                s.ProcessThreadCount,
                s.GcCollectionCount >= 0 ? s.GcCollectionCount.ToString(CultureInfo.InvariantCulture) : "N/A",
                s.PoolStatsAvailable ? s.PoolWarm.ToString(CultureInfo.InvariantCulture) : "N/A",
                s.PoolStatsAvailable ? s.PoolLeased.ToString(CultureInfo.InvariantCulture) : "N/A",
                s.PoolStatsAvailable ? s.PoolTotal.ToString(CultureInfo.InvariantCulture) : "N/A",
                s.PoolStatsAvailable ? s.PoolMin.ToString(CultureInfo.InvariantCulture) : "N/A",
                s.PoolStatsAvailable ? s.PoolMax.ToString(CultureInfo.InvariantCulture) : "N/A",
                s.PoolStatsAvailable ? s.PoolIsStarted.ToString() : "N/A",
                s.PoolStatsAvailable,
                s.Note ?? ""));
        }

        File.WriteAllText(Path.Combine(dir, "samples.csv"), csv.ToString());
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static TimeSpan EnvMinutes(string name, TimeSpan fallback)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var m) && m > 0
            ? TimeSpan.FromMinutes(m)
            : fallback;
    }

    private static TimeSpan EnvSeconds(string name, TimeSpan fallback)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s > 0
            ? TimeSpan.FromSeconds(s)
            : fallback;
    }

    private static int EnvInt(string name, int fallback)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return int.TryParse(v, out var i) && i >= 0 ? i : fallback;
    }

    private static SoakTrafficMode EnvTrafficMode(string name, SoakTrafficMode fallback)
    {
        var v = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(v)) return fallback;
        return v.Trim().ToLowerInvariant() switch
        {
            "tools_list_only" => SoakTrafficMode.ToolsListOnly,
            "full_mix" => SoakTrafficMode.FullMix,
            _ => fallback,
        };
    }

    private static string Sha256File(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }
        catch
        {
            return "unavailable";
        }
    }

    private static string GetCommitSha() => RunGit("rev-parse HEAD") is { Length: > 0 } sha ? sha : "unknown";

    private static bool IsWorkingTreeDirty()
    {
        var status = RunGit("status --porcelain");
        return !string.IsNullOrWhiteSpace(status);
    }

    private static string RunGit(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory,
            };
            using var p = Process.Start(psi);
            if (p is null) return "";
            var outText = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(5000);
            return outText;
        }
        catch
        {
            return "";
        }
    }

    private static string DetermineArtifactDir(string runId)
    {
        var envDir = Environment.GetEnvironmentVariable("SOAK_ARTIFACT_DIR");
        if (!string.IsNullOrWhiteSpace(envDir))
            return Path.Combine(envDir, runId);

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
