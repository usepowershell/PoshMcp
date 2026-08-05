using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.PowerShell;

namespace PoshMcp.Tests.Soak;

/// <summary>
/// Launches the production PoshMcp HTTP server as an external process, drives
/// mixed MCP traffic at bounded concurrency, and records fixed-interval samples
/// for the soak acceptance gate.
/// </summary>
public sealed class SoakHarness : IAsyncDisposable
{
    private const string ProtocolVersion = "2025-11-25";
    private const string MeterToolCall = "tools/call";
    private const string MethodToolsList = "tools/list";
    private const string MethodInitialize = "initialize";

    private readonly SoakConfig _config;
    private readonly ILogger _log;
    private readonly string _configPath;
    private readonly string? _artifactDir;
    private readonly string _baseUrl;
    private readonly Uri _baseUri;

    private Process? _serverProcess;
    private HttpClient? _monitorClient;
    private readonly List<SoakSample> _samples = new();
    private readonly object _samplesLock = new();

    /// <summary>Current run phase, read by the continuous sampler. Written on phase transitions.</summary>
    private volatile string _currentPhase = SoakAnalyzer.PhaseBaseline;

    // Cumulative counters (interlocked)
    private long _totalRequests;
    private long _successRequests;
    private long _errorRequests;

    // Per-request-type cumulative counters (interlocked)
    private long _initializeRequests;
    private long _toolsListRequests;
    private long _toolsCallRequests;
    private long _toolsCallPsSuccess;

    /// <summary>Server process id captured once at startup (for provenance).</summary>
    public int? ServerProcessId { get; private set; }

    /// <summary>Server process start time captured once at startup (for provenance).</summary>
    public DateTime? ServerStartTimeUtc { get; private set; }

    /// <summary>
    /// Set when the server process is detected to have exited without being killed by the harness.
    /// Null when the server ran normally through to planned shutdown.
    /// </summary>
    public int? UnexpectedExitCode { get; private set; }

    // Tracks whether the harness itself is performing shutdown (so normal Kill() is not a crash).
    private volatile bool _harnessKilling;

    // Prevents recording a duplicate SERVER_CRASH note across multiple sampler ticks.
    private volatile bool _serverCrashRecorded;

    // stdout / stderr writers (null when artifactDir is not supplied)
    private readonly object _stdioLock = new();
    private StreamWriter? _stdoutWriter;
    private StreamWriter? _stderrWriter;

    // Interval state (protected by _intervalLock)
    private readonly object _intervalLock = new();
    private long _intervalStartRequests;
    private long _intervalStartErrors;
    private readonly List<double> _intervalLatencies = new();

    private readonly Stopwatch _elapsed = new();

    // ─── Construction ────────────────────────────────────────────────────────

    /// <summary>
    /// Constructs the soak harness.
    /// </summary>
    /// <param name="config">Pre-declared soak configuration and acceptance criteria.</param>
    /// <param name="log">Logger for harness diagnostics.</param>
    /// <param name="configPath">Path to the soak server appsettings.json file.</param>
    /// <param name="artifactDir">
    /// Optional directory for durable artifacts. When provided, server stdout and stderr are
    /// captured to <c>server-stdout.txt</c> and <c>server-stderr.txt</c> in this directory
    /// in addition to being logged at Debug level. The directory must already exist.
    /// </param>
    public SoakHarness(SoakConfig config, ILogger log, string configPath, string? artifactDir = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _configPath = configPath ?? throw new ArgumentNullException(nameof(configPath));
        _artifactDir = artifactDir;

        var port = AllocatePort();
        _baseUrl = $"http://127.0.0.1:{port}";
        _baseUri = new Uri(_baseUrl);
    }

    public IReadOnlyList<SoakSample> Samples
    {
        get
        {
            lock (_samplesLock)
                return _samples.ToList().AsReadOnly();
        }
    }

    // ─── Run ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the server and runs the full four-phase soak
    /// (<c>baseline → warmup → load → cooldown</c>) with a single continuous background sampler.
    /// Returns all recorded samples. <see cref="DisposeAsync"/> should be called in a finally block.
    /// </summary>
    public async Task<IReadOnlyList<SoakSample>> RunAsync(CancellationToken ct = default)
    {
        await StartServerAsync(ct);

        _elapsed.Restart();

        // One continuous sampler for the whole run; it tags each sample with _currentPhase.
        using var samplerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var samplerTask = RunSamplerAsync(samplerCts.Token);

        try
        {
            // ── Baseline phase (no load traffic; establishes pre-load idle floor) ──
            _currentPhase = SoakAnalyzer.PhaseBaseline;
            _log.LogInformation("Baseline phase ({Duration}) — server idle, pool warm, no load traffic.", _config.BaselineDuration);
            await QuietPhaseAsync(_config.BaselineDuration, ct);

            // ── Warmup phase (full traffic; excluded from trend gates) ────────────
            _currentPhase = SoakAnalyzer.PhaseWarmup;
            _log.LogInformation("Warmup phase ({Duration}) — full traffic, excluded from analysis.", _config.WarmupDuration);
            await RunBoundedTrafficAsync(_config.WarmupDuration, _config.ConcurrencyLevel, ct);

            // ── Load phase (measured; ≥ MinLoadDuration) ─────────────────────────
            _currentPhase = SoakAnalyzer.PhaseLoad;
            _log.LogInformation("Load phase ({Duration}) — measured sustained load.", _config.LoadDuration);
            var loadSchedule = BuildPhaseSchedule(_config.LoadDuration);
            using (var loadCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                loadCts.CancelAfter(_config.LoadDuration);
                await RunPhasedTrafficAsync(loadSchedule, loadCts.Token);
            }

            // ── Cooldown phase (NO traffic, NO forced GC; observe natural recovery) ──
            _currentPhase = SoakAnalyzer.PhaseCooldown;
            _log.LogInformation("Cooldown phase ({Duration}) — traffic stopped, no forced GC, observing natural recovery.", _config.CooldownDuration);
            await QuietPhaseAsync(_config.CooldownDuration, ct);
        }
        finally
        {
            samplerCts.Cancel();
            try { await samplerTask; } catch (OperationCanceledException) { }
        }

        // Final terminal sample (cooldown).
        await TakeSampleAsync("final");

        return Samples;
    }

    /// <summary>Idle wait with no load traffic; the continuous sampler keeps recording.</summary>
    private static async Task QuietPhaseAsync(TimeSpan duration, CancellationToken ct)
    {
        if (duration <= TimeSpan.Zero) return;
        try { await Task.Delay(duration, ct); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
    }

    /// <summary>Runs full-concurrency traffic for a bounded duration (used for warmup).</summary>
    private async Task RunBoundedTrafficAsync(TimeSpan duration, int workers, CancellationToken ct)
    {
        if (duration <= TimeSpan.Zero) return;
        using var phaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        phaseCts.CancelAfter(duration);
        try { await RunTrafficPhaseAsync("warmup", workers, phaseCts.Token); }
        catch (OperationCanceledException) when (phaseCts.IsCancellationRequested && !ct.IsCancellationRequested) { }
    }

    // ─── Phase schedule ──────────────────────────────────────────────────────

    private sealed record PhaseEntry(string Name, int Workers, TimeSpan Duration);

    private List<PhaseEntry> BuildPhaseSchedule(TimeSpan totalDuration)
    {
        var phases = new List<PhaseEntry>();
        var remaining = totalDuration;

        while (remaining > TimeSpan.Zero)
        {
            var normalDur = remaining > _config.NormalPhaseDuration ? _config.NormalPhaseDuration : remaining;
            phases.Add(new PhaseEntry("normal", _config.ConcurrencyLevel, normalDur));
            remaining -= normalDur;

            if (remaining > TimeSpan.Zero && _config.BurstConcurrencyLevel > 0)
            {
                var burstDur = remaining > _config.BurstPhaseDuration ? _config.BurstPhaseDuration : remaining;
                phases.Add(new PhaseEntry("burst", _config.BurstConcurrencyLevel, burstDur));
                remaining -= burstDur;
            }

            if (remaining > TimeSpan.Zero)
            {
                var evictDur = remaining > _config.EvictionPhaseDuration ? _config.EvictionPhaseDuration : remaining;
                phases.Add(new PhaseEntry("low-traffic", 1, evictDur));
                remaining -= evictDur;
            }
        }

        return phases;
    }

    // ─── Traffic generation ──────────────────────────────────────────────────

    private async Task RunPhasedTrafficAsync(
        IReadOnlyList<PhaseEntry> phases,
        CancellationToken soakCt)
    {
        foreach (var phase in phases)
        {
            if (soakCt.IsCancellationRequested) break;

            _log.LogInformation("Phase '{Phase}': {Workers} worker(s) for {Duration}",
                phase.Name, phase.Workers, phase.Duration);

            using var phaseCts = CancellationTokenSource.CreateLinkedTokenSource(soakCt);
            phaseCts.CancelAfter(phase.Duration);
            try
            {
                await RunTrafficPhaseAsync(phase.Name, phase.Workers, phaseCts.Token);
            }
            catch (OperationCanceledException) when (phaseCts.IsCancellationRequested && !soakCt.IsCancellationRequested)
            {
                // Phase duration elapsed — continue to next phase
            }
        }
    }

    private async Task RunTrafficPhaseAsync(string phaseName, int workerCount, CancellationToken ct)
    {
        var workers = Enumerable
            .Range(0, workerCount)
            .Select(i => RunWorkerAsync(phaseName, i, ct))
            .ToArray();

        try { await Task.WhenAll(workers); }
        catch (OperationCanceledException) { }
    }

    private async Task RunWorkerAsync(string phase, int workerId, CancellationToken ct)
    {
        var rng = new Random(workerId ^ Environment.TickCount);
        using var client = CreateHttpClient();

        while (!ct.IsCancellationRequested)
        {
            var roll = rng.NextDouble();
            try
            {
                var sw = Stopwatch.StartNew();
                if (_config.TrafficMode == SoakTrafficMode.ToolsListOnly)
                {
                    // Comparison mode: tools/list only, no PowerShell execution via tools/call.
                    // Isolates HTTP + MCP protocol overhead from PowerShell execution overhead.
                    Interlocked.Increment(ref _toolsListRequests);
                    await SendToolsListAsync(client, ct);
                }
                else
                {
                    // Full-mix mode: ~10% initialize, ~50% tools/list, ~40% tools/call (real PS)
                    if (roll < 0.10)
                    {
                        Interlocked.Increment(ref _initializeRequests);
                        await SendInitializeAsync(client, ct);
                    }
                    else if (roll < 0.60)
                    {
                        Interlocked.Increment(ref _toolsListRequests);
                        await SendToolsListAsync(client, ct);
                    }
                    else
                    {
                        // "Get-Date" is registered per parameter set; "get_date_date_and_format" is the
                        // variant with -Date and -Format parameters (all optional), exercising real PS execution.
                        Interlocked.Increment(ref _toolsCallRequests);
                        var psOk = await SendToolCallAsync(client, "get_date_date_and_format", new { }, ct);
                        if (psOk) Interlocked.Increment(ref _toolsCallPsSuccess);
                    }
                }

                sw.Stop();
                RecordSuccess(sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogDebug("Worker {Id} request error: {Msg}", workerId, ex.Message);
                RecordError();
            }

            var delayMs = rng.Next(_config.MinRequestDelayMs, _config.MaxRequestDelayMs + 1);
            try { await Task.Delay(delayMs, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    // ─── Sampling ─────────────────────────────────────────────────────────────

    private async Task RunSamplerAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_config.SampleInterval, ct);
                await TakeSampleAsync(null);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarning("Sampler error: {Msg}", ex.Message);
            }
        }
    }

    private async Task TakeSampleAsync(string? note)
    {
        var phase = _currentPhase;
        var now = DateTimeOffset.UtcNow;
        var elapsedMs = _elapsed.ElapsedMilliseconds;

        // Snapshot interval counters atomically
        long intRequests, intErrors;
        double[] latencies;
        lock (_intervalLock)
        {
            var curRequests = Interlocked.Read(ref _totalRequests);
            var curErrors = Interlocked.Read(ref _errorRequests);
            intRequests = curRequests - _intervalStartRequests;
            intErrors = curErrors - _intervalStartErrors;
            _intervalStartRequests = curRequests;
            _intervalStartErrors = curErrors;
            latencies = _intervalLatencies.ToArray();
            _intervalLatencies.Clear();
        }

        // Process metrics from server process
        long workingSet = 0;
        int handleCount = -1;
        var handleSupported = false;
        int threadCount = 0;

        // Check for unexpected server exit before reading metrics.
        // _harnessKilling is set before Kill() in DisposeAsync, so normal shutdown is excluded.
        if (_serverProcess is { HasExited: true } && !_harnessKilling && !_serverCrashRecorded)
        {
            _serverCrashRecorded = true;
            var exitCode = -1;
            try { exitCode = _serverProcess.ExitCode; } catch { }
            UnexpectedExitCode = exitCode;
            note = $"SERVER_CRASH exit={exitCode}";
            _log.LogError("Server process exited unexpectedly with exit code {Code}", exitCode);
        }

        if (_serverProcess is not null && !_serverProcess.HasExited)
        {
            try
            {
                _serverProcess.Refresh();
                workingSet = _serverProcess.WorkingSet64;
                threadCount = _serverProcess.Threads.Count;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    handleCount = _serverProcess.HandleCount;
                    handleSupported = true;
                }
                else
                {
                    // Linux/macOS: count /proc/pid/fd entries
                    var fdPath = $"/proc/{_serverProcess.Id}/fd";
                    if (Directory.Exists(fdPath))
                    {
                        try
                        {
                            handleCount = Directory.GetFiles(fdPath).Length;
                            handleSupported = true;
                        }
                        catch
                        {
                            handleCount = -1;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogDebug("Process metrics read error: {Msg}", ex.Message);
            }
        }

        // Pool stats from /health (body parsed regardless of status; bounded retry)
        var (poolStats, poolFailReason) = await TryGetPoolStatsAsync();

        // Compute latency percentiles
        var sortedLatencies = latencies.OrderBy(l => l).ToArray();
        double? p50 = null, p99 = null;
        if (sortedLatencies.Length > 0)
        {
            p50 = Percentile(sortedLatencies, 50);
            p99 = Percentile(sortedLatencies, 99);
        }

        var sample = new SoakSample
        {
            Timestamp = now,
            ElapsedMs = elapsedMs,
            Phase = phase,
            TotalRequests = Interlocked.Read(ref _totalRequests),
            SuccessRequests = Interlocked.Read(ref _successRequests),
            ErrorRequests = Interlocked.Read(ref _errorRequests),
            InitializeRequests = Interlocked.Read(ref _initializeRequests),
            ToolsListRequests = Interlocked.Read(ref _toolsListRequests),
            ToolsCallRequests = Interlocked.Read(ref _toolsCallRequests),
            ToolsCallPsSuccess = Interlocked.Read(ref _toolsCallPsSuccess),
            IntervalRequests = intRequests,
            IntervalErrors = intErrors,
            P50LatencyMs = p50,
            P99LatencyMs = p99,
            WorkingSetBytes = workingSet,
            ProcessHandleCount = handleCount,
            HandleCountSupported = handleSupported,
            ProcessThreadCount = threadCount,
            GcCollectionCount = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2),
            PoolWarm = poolStats?.Warm ?? 0,
            PoolLeased = poolStats?.Leased ?? 0,
            PoolResetting = poolStats?.Resetting ?? 0,
            PoolCreating = poolStats?.Creating ?? 0,
            PoolTotal = poolStats?.Total ?? 0,
            PoolMin = poolStats?.Min ?? 0,
            PoolMax = poolStats?.Max ?? 0,
            PoolIsStarted = poolStats?.IsStarted ?? false,
            PoolIsDraining = poolStats?.IsDraining ?? false,
            PoolStatsAvailable = poolStats is not null,
            Note = poolStats is null && note is null ? $"pool_unavailable:{poolFailReason}" : note,
        };

        lock (_samplesLock)
        {
            _samples.Add(sample);
        }

        _log.LogDebug(
            "Sample @ {Elapsed:N0}ms | phase={Phase} req={Req} err={Err} ws={WS:N0}bytes " +
            "pool=W{W}/L{L}/T{T}(min{Min}max{Max})",
            elapsedMs, phase, sample.TotalRequests, sample.ErrorRequests,
            workingSet, sample.PoolWarm, sample.PoolLeased, sample.PoolTotal,
            sample.PoolMin, sample.PoolMax);
    }

    // ─── Pool stats via /health ──────────────────────────────────────────────

    private sealed record PoolStatsSnapshot(
        int Warm, int Leased, int Resetting, int Creating,
        int Total, int Min, int Max, bool IsStarted, bool IsDraining);

    // Number of health-endpoint attempts per sample. Fills transient gaps
    // (e.g. a connection reset) without masking a sustained outage: after all
    // attempts fail the sample still records N/A plus the failure reason.
    private const int PoolStatsAttempts = 3;

    private async Task<(PoolStatsSnapshot? Snapshot, string? FailureReason)> TryGetPoolStatsAsync()
    {
        var client = _monitorClient;
        if (client is null) return (null, "no_client");

        string? lastReason = null;
        for (var attempt = 1; attempt <= PoolStatsAttempts; attempt++)
        {
            try
            {
                // Read the body regardless of HTTP status: a Degraded/Unhealthy pool
                // returns 503 but the response body still carries the runspace_pool
                // check data we need. Gating on IsSuccessStatusCode would drop valid
                // pool observations exactly when the pool is under pressure.
                using var response = await client.GetAsync(new Uri(_baseUri, "health")).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(body);

                if (!doc.RootElement.TryGetProperty("checks", out var checks) || checks.ValueKind != JsonValueKind.Array)
                {
                    lastReason = $"no_checks_status{(int)response.StatusCode}";
                }
                else
                {
                    foreach (var check in checks.EnumerateArray())
                    {
                        if (!check.TryGetProperty("name", out var nameProp)) continue;
                        if (nameProp.GetString() != "runspace_pool") continue;
                        if (!check.TryGetProperty("data", out var data))
                        {
                            lastReason = "no_data";
                            break;
                        }

                        return (new PoolStatsSnapshot(
                            Warm: data.TryGetProperty("warm", out var w) ? w.GetInt32() : 0,
                            Leased: data.TryGetProperty("leased", out var l) ? l.GetInt32() : 0,
                            Resetting: data.TryGetProperty("resetting", out var r) ? r.GetInt32() : 0,
                            Creating: data.TryGetProperty("creating", out var cr) ? cr.GetInt32() : 0,
                            Total: data.TryGetProperty("total", out var tot) ? tot.GetInt32() : 0,
                            Min: data.TryGetProperty("min", out var mn) ? mn.GetInt32() : 0,
                            Max: data.TryGetProperty("max", out var mx) ? mx.GetInt32() : 0,
                            IsStarted: data.TryGetProperty("is_started", out var st) && st.GetBoolean(),
                            IsDraining: data.TryGetProperty("is_draining", out var dr) && dr.GetBoolean()), null);
                    }

                    lastReason ??= "no_runspace_pool_check";
                }
            }
            catch (Exception ex)
            {
                lastReason = ex.GetType().Name;
            }

            if (attempt < PoolStatsAttempts)
            {
                await Task.Delay(250).ConfigureAwait(false);
            }
        }

        return (null, lastReason);
    }

    // ─── Counter management ──────────────────────────────────────────────────

    private void RecordSuccess(long latencyMs)
    {
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Increment(ref _successRequests);
        lock (_intervalLock)
        {
            _intervalLatencies.Add(latencyMs);
        }
    }

    private void RecordError()
    {
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Increment(ref _errorRequests);
    }

    // ─── HTTP helpers ─────────────────────────────────────────────────────────

    private HttpClient CreateHttpClient()
    {
        var client = new HttpClient { BaseAddress = _baseUri, Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return client;
    }

    private static long _nextRequestId;

    private async Task SendInitializeAsync(HttpClient client, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _nextRequestId);
        var payload = new
        {
            jsonrpc = "2.0",
            id,
            method = MethodInitialize,
            @params = new
            {
                protocolVersion = ProtocolVersion,
                capabilities = new { tools = new { } },
                clientInfo = new { name = $"soak-worker-{id}", version = "1.0" }
            }
        };
        using var response = await SendMcpAsync(client, payload, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task SendToolsListAsync(HttpClient client, CancellationToken ct)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = Interlocked.Increment(ref _nextRequestId),
            method = MethodToolsList
        };
        using var response = await SendMcpAsync(client, payload, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<bool> SendToolCallAsync(HttpClient client, string toolName, object args, CancellationToken ct)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = Interlocked.Increment(ref _nextRequestId),
            method = MeterToolCall,
            @params = new { name = toolName, arguments = args }
        };
        using var response = await SendMcpAsync(client, payload, ct);
        response.EnsureSuccessStatusCode();

        // Traffic proof: parse response, reject top-level JSON-RPC error, require result.isError == false,
        // and require non-empty parseable Get-Date output in the result content.
        var body = await response.Content.ReadAsStringAsync(ct);
        var bodyText = body.StartsWith("event:", StringComparison.Ordinal)
            ? body.Split('\n').FirstOrDefault(l => l.StartsWith("data:", StringComparison.Ordinal))?[5..].TrimStart() ?? body
            : body;
        using var doc = JsonDocument.Parse(bodyText);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out _))
            throw new InvalidOperationException($"MCP tool call returned JSON-RPC error: {Truncate(bodyText)}");

        if (!root.TryGetProperty("result", out var result))
            throw new InvalidOperationException($"MCP tool call response missing 'result': {Truncate(bodyText)}");

        // isError is optional in MCP; absent means success. A true value is a tool-level failure.
        if (result.TryGetProperty("isError", out var isErr) && isErr.ValueKind == JsonValueKind.True)
            throw new InvalidOperationException($"MCP tool call result.isError == true: {Truncate(bodyText)}");

        var text = ExtractContentText(result);
        if (string.IsNullOrWhiteSpace(text) || !LooksLikeDate(text))
            throw new InvalidOperationException($"MCP tool call produced no parseable Get-Date output: {Truncate(bodyText)}");

        return true;
    }

    private static string? ExtractContentText(JsonElement result)
    {
        if (!result.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return null;
        var sb = new StringBuilder();
        foreach (var item in content.EnumerateArray())
        {
            if (item.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                sb.Append(t.GetString());
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    private static bool LooksLikeDate(string text)
    {
        if (DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _))
            return true;
        if (DateTime.TryParse(text, System.Globalization.CultureInfo.CurrentCulture,
                System.Globalization.DateTimeStyles.None, out _))
            return true;
        // Get-Date custom/culture formats may not round-trip through TryParse; require a year-like
        // 4-digit run plus at least one separator as a conservative non-empty-output proxy.
        var hasFourDigits = System.Text.RegularExpressions.Regex.IsMatch(text, "\\d{4}");
        var hasDigitCluster = System.Text.RegularExpressions.Regex.IsMatch(text, "\\d{1,2}[:/\\- ]\\d{1,2}");
        return hasFourDigits || hasDigitCluster;
    }

    private static string Truncate(string s) => s[..Math.Min(200, s.Length)];

    private Task<HttpResponseMessage> SendMcpAsync(HttpClient client, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);
        var request = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        return client.SendAsync(request, ct);
    }

    // ─── Server lifecycle ────────────────────────────────────────────────────

    private async Task StartServerAsync(CancellationToken ct)
    {
        var serverDll = typeof(PowerShellConfiguration).Assembly.Location;
        _log.LogInformation("Starting server from {Dll} at {Url}", serverDll, _baseUrl);

        // Open durable stdout/stderr capture files if an artifact directory was provided.
        // Files are opened before BeginOutputReadLine so no early lines are missed.
        if (_artifactDir is not null)
        {
            try
            {
                _stdoutWriter = new StreamWriter(Path.Combine(_artifactDir, "server-stdout.txt"), append: false, Encoding.UTF8);
                _stdoutWriter.AutoFlush = true;
                _stderrWriter = new StreamWriter(Path.Combine(_artifactDir, "server-stderr.txt"), append: false, Encoding.UTF8);
                _stderrWriter.AutoFlush = true;
            }
            catch (Exception ex)
            {
                _log.LogWarning("Could not open server stdio capture files: {Msg}", ex.Message);
            }
        }

        var psi = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(serverDll);
        psi.ArgumentList.Add("serve");
        psi.ArgumentList.Add("--transport");
        psi.ArgumentList.Add("http");
        psi.ArgumentList.Add("--url");
        psi.ArgumentList.Add(_baseUrl);
        psi.ArgumentList.Add("--config");
        psi.ArgumentList.Add(_configPath);
        psi.Environment["ApplicationInsights__Enabled"] = "false";
        psi.Environment["APPLICATIONINSIGHTS_CONNECTION_STRING"] = string.Empty;
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";

        _serverProcess = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start soak server process.");

        // Async readers prevent pipe-buffer deadlock. Each handler also writes durably to
        // the artifact file (if available) under a lock so lines are not interleaved.
        _serverProcess.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            _log.LogDebug("[server stdout] {Line}", e.Data);
            if (_stdoutWriter is not null)
            {
                lock (_stdioLock) { try { _stdoutWriter.WriteLine(e.Data); } catch { } }
            }
        };
        _serverProcess.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            _log.LogDebug("[server stderr] {Line}", e.Data);
            if (_stderrWriter is not null)
            {
                lock (_stdioLock) { try { _stderrWriter.WriteLine(e.Data); } catch { } }
            }
        };
        _serverProcess.BeginOutputReadLine();
        _serverProcess.BeginErrorReadLine();

        ServerProcessId = _serverProcess.Id;
        try { ServerStartTimeUtc = _serverProcess.StartTime.ToUniversalTime(); }
        catch { ServerStartTimeUtc = null; }

        // Dedicated monitoring client, isolated from load so heavy traffic cannot starve health polls.
        _monitorClient = new HttpClient { BaseAddress = _baseUri, Timeout = TimeSpan.FromSeconds(10) };
        _monitorClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        await WaitForReadyAsync(ct);
        _log.LogInformation("Server ready (PID {Pid})", _serverProcess.Id);
    }

    private async Task WaitForReadyAsync(CancellationToken ct)
    {
        var deadline = Stopwatch.StartNew();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        while (deadline.Elapsed < TimeSpan.FromMinutes(2))
        {
            ct.ThrowIfCancellationRequested();

            if (_serverProcess is { HasExited: true })
                throw new InvalidOperationException(
                    $"Server exited with code {_serverProcess.ExitCode} before becoming ready.");

            try
            {
                using var response = await client.GetAsync(new Uri(_baseUri, "health/ready"), ct);
                if (response.IsSuccessStatusCode) return;
            }
            catch (Exception ex) when (ex is HttpRequestException or SocketException or TaskCanceledException)
            {
                // Not ready yet
            }

            await Task.Delay(500, ct);
        }

        throw new TimeoutException($"Server did not become ready at {_baseUrl} within 2 minutes.");
    }

    // ─── Utilities ────────────────────────────────────────────────────────────

    private static double Percentile(double[] sorted, int p)
    {
        if (sorted.Length == 1) return sorted[0];
        var rank = (p / 100.0) * (sorted.Length - 1);
        var lower = (int)rank;
        var upper = Math.Min(lower + 1, sorted.Length - 1);
        var frac = rank - lower;
        return sorted[lower] + (frac * (sorted[upper] - sorted[lower]));
    }

    private static int AllocatePort()
    {
        using var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    // ─── IAsyncDisposable ─────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        _monitorClient?.Dispose();
        _monitorClient = null;

        if (_serverProcess is not null)
        {
            if (!_serverProcess.HasExited)
            {
                // Signal that this Kill() is an intentional harness shutdown, not a crash.
                _harnessKilling = true;
                _log.LogInformation("Stopping soak server (PID {Pid})...", _serverProcess.Id);
                try
                {
                    _serverProcess.Kill(entireProcessTree: true);
                    await _serverProcess.WaitForExitAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.LogWarning("Error stopping server: {Msg}", ex.Message);
                }
            }

            _serverProcess.Dispose();
            _serverProcess = null;
        }

        // Close durable stdio writers after the process has exited so all async output is flushed.
        lock (_stdioLock)
        {
            _stdoutWriter?.Dispose();
            _stdoutWriter = null;
            _stderrWriter?.Dispose();
            _stderrWriter = null;
        }
    }
}
