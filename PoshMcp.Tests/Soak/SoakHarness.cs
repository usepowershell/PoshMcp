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
    private readonly string _baseUrl;
    private readonly Uri _baseUri;

    private Process? _serverProcess;
    private readonly List<SoakSample> _samples = new();
    private readonly object _samplesLock = new();

    // Cumulative counters (interlocked)
    private long _totalRequests;
    private long _successRequests;
    private long _errorRequests;

    // Interval state (protected by _intervalLock)
    private readonly object _intervalLock = new();
    private long _intervalStartRequests;
    private long _intervalStartErrors;
    private readonly List<double> _intervalLatencies = new();

    private readonly Stopwatch _elapsed = new();

    // ─── Construction ────────────────────────────────────────────────────────

    public SoakHarness(SoakConfig config, ILogger log, string configPath)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _configPath = configPath ?? throw new ArgumentNullException(nameof(configPath));

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
    /// Starts the server, runs warmup + soak, and returns all recorded samples.
    /// Calls <see cref="DisposeAsync"/> should be made in a finally block.
    /// </summary>
    public async Task<IReadOnlyList<SoakSample>> RunAsync(CancellationToken ct = default)
    {
        await StartServerAsync(ct);

        _log.LogInformation("Server ready at {Url}. Starting warmup ({Duration})...",
            _baseUrl, _config.WarmupDuration);

        _elapsed.Restart();

        // ── Warmup phase ─────────────────────────────────────────────────────
        using var warmupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        warmupCts.CancelAfter(_config.WarmupDuration);

        await RunTrafficPhaseAsync("warmup", _config.ConcurrencyLevel, warmupCts.Token);

        _log.LogInformation("Warmup complete. Starting soak ({Duration})...", _config.SoakDuration);

        // ── Soak phase ───────────────────────────────────────────────────────
        var phaseSchedule = BuildPhaseSchedule();
        var soakCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        soakCts.CancelAfter(_config.SoakDuration);

        using var samplerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Start sampler background loop
        var samplerTask = RunSamplerAsync("soak", samplerCts.Token);

        // Run traffic phases
        await RunPhasedTrafficAsync(phaseSchedule, soakCts.Token);

        // Stop sampler
        samplerCts.Cancel();
        try { await samplerTask; } catch (OperationCanceledException) { }

        // Collect final sample
        await TakeSampleAsync("soak", "final");

        return Samples;
    }

    // ─── Phase schedule ──────────────────────────────────────────────────────

    private sealed record PhaseEntry(string Name, int Workers, TimeSpan Duration);

    private List<PhaseEntry> BuildPhaseSchedule()
    {
        var phases = new List<PhaseEntry>();
        var remaining = _config.SoakDuration;

        while (remaining > TimeSpan.Zero)
        {
            var normalDur = remaining > _config.NormalPhaseDuration ? _config.NormalPhaseDuration : remaining;
            phases.Add(new PhaseEntry("normal", _config.ConcurrencyLevel, normalDur));
            remaining -= normalDur;

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
            // Mixed workload: 50% tools/list, 40% tools/call, 10% initialize
            var roll = rng.NextDouble();
            try
            {
                var sw = Stopwatch.StartNew();
                if (roll < 0.10)
                {
                    await SendInitializeAsync(client, ct);
                }
                else if (roll < 0.60)
                {
                    await SendToolsListAsync(client, ct);
                }
                else
                {
                    // "Get-Date" is registered per parameter set; "get_date_date_and_format" is the
                    // variant with -Date and -Format parameters (all optional), exercising real PS execution.
                    await SendToolCallAsync(client, "get_date_date_and_format", new { }, ct);
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

    private async Task RunSamplerAsync(string phase, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_config.SampleInterval, ct);
                await TakeSampleAsync(phase, null);
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

    private async Task TakeSampleAsync(string phase, string? note)
    {
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

        // Pool stats from /health
        var poolStats = await TryGetPoolStatsAsync();

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
            IntervalRequests = intRequests,
            IntervalErrors = intErrors,
            P50LatencyMs = p50,
            P99LatencyMs = p99,
            WorkingSetBytes = workingSet,
            ProcessHandleCount = handleCount,
            HandleCountSupported = handleSupported,
            ProcessThreadCount = threadCount,
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
            Note = note,
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

    private async Task<PoolStatsSnapshot?> TryGetPoolStatsAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var response = await client.GetAsync(new Uri(_baseUri, "health")).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);

            var checks = doc.RootElement.GetProperty("checks");
            foreach (var check in checks.EnumerateArray())
            {
                if (!check.TryGetProperty("name", out var nameProp)) continue;
                if (nameProp.GetString() != "runspace_pool") continue;
                if (!check.TryGetProperty("data", out var data)) return null;

                return new PoolStatsSnapshot(
                    Warm: data.TryGetProperty("warm", out var w) ? w.GetInt32() : 0,
                    Leased: data.TryGetProperty("leased", out var l) ? l.GetInt32() : 0,
                    Resetting: data.TryGetProperty("resetting", out var r) ? r.GetInt32() : 0,
                    Creating: data.TryGetProperty("creating", out var cr) ? cr.GetInt32() : 0,
                    Total: data.TryGetProperty("total", out var tot) ? tot.GetInt32() : 0,
                    Min: data.TryGetProperty("min", out var mn) ? mn.GetInt32() : 0,
                    Max: data.TryGetProperty("max", out var mx) ? mx.GetInt32() : 0,
                    IsStarted: data.TryGetProperty("is_started", out var st) && st.GetBoolean(),
                    IsDraining: data.TryGetProperty("is_draining", out var dr) && dr.GetBoolean());
            }

            return null;
        }
        catch
        {
            return null;
        }
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

    private async Task SendToolCallAsync(HttpClient client, string toolName, object args, CancellationToken ct)
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

        // Isolation check: verify response is valid JSON-RPC with a result
        var body = await response.Content.ReadAsStringAsync();
        var bodyText = body.StartsWith("event:", StringComparison.Ordinal)
            ? body.Split('\n').FirstOrDefault(l => l.StartsWith("data:", StringComparison.Ordinal))?[5..].TrimStart() ?? body
            : body;
        using var doc = JsonDocument.Parse(bodyText);
        if (doc.RootElement.TryGetProperty("error", out _))
        {
            throw new InvalidOperationException($"MCP tool call returned error: {bodyText[..Math.Min(200, bodyText.Length)]}");
        }
    }

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

        _serverProcess.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) _log.LogDebug("[server stdout] {Line}", e.Data);
        };
        _serverProcess.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) _log.LogDebug("[server stderr] {Line}", e.Data);
        };
        _serverProcess.BeginOutputReadLine();
        _serverProcess.BeginErrorReadLine();

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
        if (_serverProcess is not null)
        {
            if (!_serverProcess.HasExited)
            {
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
    }
}
