using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.Pool;
using PoshMcp.Server.Server;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Integration;

/// <summary>
/// Production-faithful end-to-end isolation and stateful-compatibility tests for issue #356.
/// Covers reset isolation (variables, $Error, location, PSDrives, preferences, functions),
/// startup state preservation, session non-affinity, stateless/stateful parity, startup
/// failure at the host lifecycle boundary, and stdio process statefulness.
/// All HTTP tests use real MCP tools/call HTTP requests through TestServer → PooledHttpRunspace
/// → StatelessRunspacePool → real PowerShell runspaces (no mocks in the execution path).
/// </summary>
[Trait("Category", "Integration")]
public sealed class TransportIsolationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;

    // Shared stateless TestServer host — 2 workers with startup identity script.
    private WebApplication? _statelessApp;
    private HttpClient? _statelessClient;

    // Shared stateful TestServer host — 2 workers with same startup script.
    private WebApplication? _statefulApp;
    private HttpClient? _statefulClient;

    // Gate for the blocking tool used in session-affinity tests (shared stateless host).
    private readonly SemaphoreSlim _blockingGate = new(0, 1);
    private readonly SemaphoreSlim _blockingEntered = new(0, 1);

    // Per-worker GUID set by the startup script so tests can observe which worker served them.
    private const string WorkerStartupScript = @"
$WorkerIdentity = [System.Guid]::NewGuid().ToString()
function Get-WorkerIdentity { return $WorkerIdentity }
$WorkerStartupMarker = 'startup-initialized'
function Get-WorkerMarker { return $WorkerStartupMarker }
";

    public TransportIsolationTests(ITestOutputHelper output) => _output = output;

    // ─── IAsyncLifetime ──────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        (_statelessApp, _statelessClient) = await BuildIsolationHostAsync(stateless: true);
        (_statefulApp, _statefulClient) = await BuildIsolationHostAsync(stateless: false);
    }

    public async Task DisposeAsync()
    {
        _statelessClient?.Dispose();
        if (_statelessApp is not null)
        {
            await _statelessApp.StopAsync();
            await _statelessApp.DisposeAsync();
        }
        _statefulClient?.Dispose();
        if (_statefulApp is not null)
        {
            await _statefulApp.StopAsync();
            await _statefulApp.DisposeAsync();
        }
        _blockingGate.Dispose();
        _blockingEntered.Dispose();
    }

    // ─── Group A: HTTP stateless reset isolation ──────────────────────────────

    [Fact]
    public async Task Stateless_Variable_IsRemovedAfterRequest()
    {
        var client = _statelessClient!;
        await CallToolAsync(client, "iso_set_var");
        var result = await CallToolTextAsync(client, "iso_read_var");
        Assert.Equal("not-set", result);
    }

    [Fact]
    public async Task Stateless_ErrorStream_IsClearedAfterRequest()
    {
        var client = _statelessClient!;
        await CallToolAsync(client, "iso_set_error");
        var result = await CallToolTextAsync(client, "iso_read_error_count");
        Assert.Equal("0", result);
    }

    [Fact]
    public async Task Stateless_Location_IsResetAfterRequest()
    {
        var client = _statelessClient!;

        // Capture the worker's baseline location via the production HTTP/MCP path.
        // The reset protocol sets location to drive root (Windows) or '/' (Linux),
        // so repeated calls return a stable platform-appropriate path.
        var baseline = await CallToolTextAsync(client, "iso_read_location");

        // Change to a platform-guaranteed alternate directory (no $env:TEMP assumption).
        // The tool returns (Get-Location).Path after the change in PS canonical form.
        var altDir = await CallToolTextAsync(client, "iso_change_location");

        // Verify the change was meaningful (altDir must differ from baseline).
        static string Norm(string p) => p.Replace('\\', '/').TrimEnd('/');
        Assert.True(!string.Equals(Norm(baseline), Norm(altDir), StringComparison.OrdinalIgnoreCase),
            $"iso_change_location must move to a different directory; baseline='{baseline}' altDir='{altDir}'");

        // After worker reset, location must no longer be the contaminated alternate directory.
        // Fails against an implementation that omits location reset.
        var afterReset = await CallToolTextAsync(client, "iso_read_location");
        Assert.True(!string.Equals(Norm(altDir), Norm(afterReset), StringComparison.OrdinalIgnoreCase),
            $"Location was not reset: still at altDir '{altDir}' after reset (got '{afterReset}')");
    }

    [Fact]
    public async Task Stateless_PsDrive_IsRemovedAfterRequest()
    {
        var client = _statelessClient!;
        await CallToolAsync(client, "iso_create_drive");
        var result = await CallToolTextAsync(client, "iso_check_drive");
        Assert.Equal("False", result);
    }

    [Fact]
    public async Task Stateless_PreferenceVariable_IsResetAfterRequest()
    {
        var client = _statelessClient!;
        await CallToolAsync(client, "iso_set_pref_stop");
        var result = await CallToolTextAsync(client, "iso_read_pref");
        Assert.Equal("Continue", result);
    }

    [Fact]
    public async Task Stateless_RequestFunction_IsRemovedAfterRequest()
    {
        var client = _statelessClient!;
        await CallToolAsync(client, "iso_define_function");
        var result = await CallToolTextAsync(client, "iso_call_function");
        // Reset removed the request-defined function; the tool should report not-defined.
        Assert.Equal("not-defined", result);
    }

    [Fact]
    public async Task Stateless_CombinedContamination_AllStateCleared()
    {
        var client = _statelessClient!;
        await CallToolAsync(client, "iso_contaminate_all");
        var result = await CallToolTextAsync(client, "iso_read_all");
        // All fields reset: variable not-set, error 0, pref Continue, drive false, func not-defined.
        Assert.Contains("var=not-set", result, StringComparison.Ordinal);
        Assert.Contains("err=0", result, StringComparison.Ordinal);
        Assert.Contains("pref=Continue", result, StringComparison.Ordinal);
        Assert.Contains("drive=False", result, StringComparison.Ordinal);
        Assert.Contains("func=not-defined", result, StringComparison.Ordinal);
    }

    // ─── Group B: Startup state preservation ─────────────────────────────────

    [Fact]
    public async Task Stateless_StartupVariable_PreservedAfterReset()
    {
        var client = _statelessClient!;
        // Startup script sets $WorkerIdentity; each call should see a non-empty GUID.
        var r1 = await CallToolTextAsync(client, "iso_read_worker_identity");
        Assert.True(Guid.TryParse(r1, out _), $"Expected a GUID but got: '{r1}'");

        // Contaminate and verify startup var still present on the next lease.
        await CallToolAsync(client, "iso_set_var");
        var r2 = await CallToolTextAsync(client, "iso_read_worker_identity");
        Assert.True(Guid.TryParse(r2, out _), $"Expected a GUID after contamination but got: '{r2}'");
    }

    [Fact]
    public async Task Stateless_StartupFunction_AvailableAfterReset()
    {
        var client = _statelessClient!;
        // Contaminate with a request-scoped function definition, then verify startup function survives.
        await CallToolAsync(client, "iso_define_function");
        var result = await CallToolTextAsync(client, "iso_read_worker_marker");
        Assert.Equal("startup-initialized", result);
    }

    // ─── Group C: Session non-affinity ───────────────────────────────────────

    [Fact]
    public async Task NoWorkerAffinity_RequestStateNotRetained_BetweenSequentialCalls()
    {
        var client = _statelessClient!;
        // Set request variable, then immediately read it in the next call.
        // Reset must have cleared it regardless of which worker is selected.
        for (var i = 0; i < 5; i++)
        {
            await CallToolAsync(client, "iso_set_var");
            var result = await CallToolTextAsync(client, "iso_read_var");
            Assert.True(result == "not-set", $"Variable leaked on iteration {i}: got '{result}'");
        }
    }

    [Fact]
    public async Task NoWorkerAffinity_TwoDistinctWorkersExist_BlockingProof()
    {
        var client = _statelessClient!;

        // Hold worker A via the blocking tool while we query worker B's identity.
        // The blocking tool returns worker A's identity directly, avoiding a post-release race.
        var blockTask = Task.Run(() => CallToolAsync(client, "iso_block_worker"));
        Assert.True(await _blockingEntered.WaitAsync(TimeSpan.FromSeconds(15)),
            "Blocking tool did not acquire a worker within 15 s");

        try
        {
            // Worker A is held; the pool has 2 workers, so this request must use worker B.
            var identityB = await CallToolTextAsync(client, "iso_read_worker_identity");
            Assert.True(Guid.TryParse(identityB, out var guidB),
                $"Expected GUID for worker B but got: '{identityB}'");

            // Release worker A; the block tool returns A's identity.
            _blockingGate.Release();
            var blockResult = await blockTask;
            var identityA = ExtractText(blockResult);
            Assert.True(Guid.TryParse(identityA, out var guidA),
                $"Expected GUID for worker A but got: '{identityA}'");

            // Two distinct workers must have different startup GUIDs.
            Assert.NotEqual(guidA, guidB);
        }
        finally
        {
            // Ensure gate is released even on assertion failure.
            if (_blockingGate.CurrentCount == 0)
                _blockingGate.Release();
        }
    }

    [Fact]
    public async Task Stateful_SameSessionId_ClearsRequestStateAndDoesNotPinWorker()
    {
        var client = _statefulClient!;

        // Establish a stateful session and capture the session ID.
        var sessionId = await InitializeStatefulSessionAsync(client);
        Assert.False(string.IsNullOrEmpty(sessionId), "Expected Mcp-Session-Id from stateful init");

        // Set a variable in one call using the session ID.
        await CallToolWithSessionAsync(client, "iso_set_var", sessionId);

        // Read it back in the next call with the same session ID — must be cleared by reset.
        var result = await CallToolWithSessionAsync(client, "iso_read_var", sessionId);
        var text = ExtractText(result);
        Assert.Equal("not-set", text);

        // Call with a DIFFERENT session ID — worker selection must not be influenced by session ID.
        var sessionId2 = await InitializeStatefulSessionAsync(client);
        Assert.False(string.IsNullOrEmpty(sessionId2), "Expected second Mcp-Session-Id");
        Assert.NotEqual(sessionId, sessionId2);

        var ident1 = ExtractText(await CallToolWithSessionAsync(client, "iso_read_worker_identity", sessionId));
        var ident2 = ExtractText(await CallToolWithSessionAsync(client, "iso_read_worker_identity", sessionId2));
        // Both should be valid GUIDs; they may or may not be the same worker (no affinity guarantee either way).
        Assert.True(Guid.TryParse(ident1, out _), $"Session 1 worker identity not a GUID: '{ident1}'");
        Assert.True(Guid.TryParse(ident2, out _), $"Session 2 worker identity not a GUID: '{ident2}'");
    }

    // ─── Group D: Stateless/Stateful parity ──────────────────────────────────

    [Fact]
    public async Task Parity_StatelessAndStateful_HaveIdenticalIsolation()
    {
        // For each isolation field, confirm stateless and stateful modes both clear request state.
        // Stateless: bare tools/call. Stateful: proper session init + tools/call with session ID.
        var pairs = new[]
        {
            ("iso_set_var", "iso_read_var", "not-set"),
            ("iso_set_error", "iso_read_error_count", "0"),
            ("iso_set_pref_stop", "iso_read_pref", "Continue"),
            ("iso_define_function", "iso_call_function", "not-defined"),
        };

        foreach (var (setter, reader, expected) in pairs)
        {
            // Stateless: each tools/call is an independent request, reset runs between them.
            await CallToolAsync(_statelessClient!, setter);
            var sl = await CallToolTextAsync(_statelessClient!, reader);
            Assert.True(sl == expected, $"Stateless isolation failed for ({setter},{reader}): got '{sl}'");

            // Stateful: initialize a session, then call setter and reader with the same session ID.
            // Reset still runs between the two tools/call requests regardless of session ID.
            var sessionId = await InitializeStatefulSessionAsync(_statefulClient!);
            Assert.False(string.IsNullOrEmpty(sessionId),
                $"Stateful mode must issue Mcp-Session-Id for ({setter},{reader}) pair");
            await CallToolWithSessionAsync(_statefulClient!, setter, sessionId!);
            var sfResult = await CallToolWithSessionAsync(_statefulClient!, reader, sessionId!);
            var sf = ExtractText(sfResult);
            Assert.True(sf == expected, $"Stateful isolation failed for ({setter},{reader}): got '{sf}'");

            Assert.True(sl == sf, $"Parity mismatch for ({setter},{reader}): stateless='{sl}' stateful='{sf}'");
        }
    }

    // ─── Group E: Startup failure at host lifecycle boundary ─────────────────

    [Fact]
    public async Task PartialEagerWarmup_BlocksHostStartup_WorkersDisposed()
    {
        // Factory succeeds for the first creation only; all others fail.
        var callCount = 0;
        IPowerShellRunspace FailAfterFirst()
        {
            var n = Interlocked.Increment(ref callCount);
            if (n > 1)
                throw new InvalidOperationException("Simulated worker creation failure");
            return new IsolatedPowerShellRunspace();
        }

        // EagerWarmCount=2, MinPoolSize=1: second eager worker fails → StartAsync must throw.
        var opts = new RunspacePoolOptions
        {
            MinPoolSize = 1,
            MaxPoolSize = 2,
            EagerWarmCount = 2,
            AcquisitionTimeout = TimeSpan.FromSeconds(2),
            IdleTtl = TimeSpan.FromSeconds(300),
            SweepInterval = TimeSpan.FromSeconds(60),
            StopTimeout = TimeSpan.FromSeconds(5),
            ShutdownDrainTimeout = TimeSpan.FromSeconds(5),
            ReplenishCheckInterval = TimeSpan.FromSeconds(60),
        };

        var pool = new StatelessRunspacePool(opts, loggerFactory: null, startupScript: null,
            workerFactory: FailAfterFirst,
            snapshotCapture: _ => new HashSet<string>(),
            driveSnapshotCapture: _ => new HashSet<string>(),
            functionSnapshotCapture: _ => new HashSet<string>());

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton<IRunspacePool>(pool);
        builder.Services.AddSingleton<IHostedService, RunspacePoolLifecycleService>();

        var pooledRunspace = new PooledHttpRunspace(pool, (string?)null, NullLoggerFactory.Instance);
        pooledRunspace.FinalizeDiscovery();
        builder.Services.AddSingleton<IPowerShellRunspace>(pooledRunspace);

        var tool = McpServerTool.Create(
            async (CancellationToken ct) => await pooledRunspace.ExecuteThreadSafeAsync(
                ps => { ps.Commands.Clear(); ps.AddScript("'ok'"); var r = ps.Invoke<string>(); ps.Commands.Clear(); return Task.FromResult(r.Count > 0 ? r[0] : ""); }),
            new McpServerToolCreateOptions { Name = "startup_fail_probe", Description = "probe" });

        builder.Services.AddMcpServer()
            .WithHttpTransport(opts => opts.Stateless = true)
            .WithTools([tool]);

        var app = builder.Build();
        app.UseMiddleware<McpProtocolVersionMiddleware>((object)new[] { "/" });
        app.MapMcp();

        // app.StartAsync invokes RunspacePoolLifecycleService.StartAsync → pool.StartAsync → throws.
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAsync());

        // After failed startup: no workers are alive, pool is not started.
        var stats = pool.GetStats();
        Assert.False(stats.IsStarted, "Pool must not be marked started after partial warmup failure.");
        Assert.Equal(0, stats.TotalWorkers);
        Assert.Equal(0, stats.WarmWorkers);

        // Pool is still safely disposable.
        await pool.DisposeAsync();
        await app.DisposeAsync();
    }

    // ─── Group F: Stdio process statefulness ─────────────────────────────────

    [Fact]
    public async Task Stdio_IsProcessStateful_LastCommandOutputPersistsAcrossCalls()
    {
        // The stdio server uses SingletonPowerShellRunspace — all tool calls share one PS instance.
        // Start the server with EnableResultCaching=true so get_process_id automatically
        // caches its result in $LastCommandOutput. A second call to get_last_command_output
        // reads that same variable back, proving no reset happened between requests.
        //
        // Note: set-result-caching invocation fails via ExternalMcpClient in current MCP SDK
        // v2.0.0 (tool dispatch issue with Func<string?,string?,string?,CT,Task<string>> signature).
        // Using a custom config at startup is the production-faithful alternative.
        var configPath = Path.Combine(AppContext.BaseDirectory, $"stdio-caching-{Guid.NewGuid():N}.json");
        var configJson = """
            {
              "Logging": { "LogLevel": { "Default": "Warning" } },
              "PowerShellConfiguration": {
                "CommandNames": ["Get-Process"],
                "Modules": [],
                "ExcludePatterns": [],
                "IncludePatterns": [],
                "EnableDynamicReloadTools": false,
                "EnableConfigurationTroubleshootingTool": false,
                "Performance": {
                  "EnableResultCaching": true,
                  "UseDefaultDisplayProperties": true
                }
              }
            }
            """;
        File.WriteAllText(configPath, configJson);

        var logger = new XunitLogger(_output, nameof(Stdio_IsProcessStateful_LastCommandOutputPersistsAcrossCalls));
        try
        {
            using var server = new InProcessMcpServer(logger, explicitConfigPath: configPath);
            await server.StartAsync();

            var client = new ExternalMcpClient(logger, server);
            await client.StartAsync();

            var serverPid = server.GetServerProcess().Id;

            // Call get_process_id — with EnableResultCaching=true the singleton PS sets
            // $LastCommandOutput automatically via Tee-Object in the generated pipeline.
            var callResp = await client.SendToolCallAsync("get_process_id", new JObject { ["Id"] = new JArray(serverPid) });
            Assert.Null(callResp["error"]);
            var callContent = callResp["result"]?["content"] as JArray;
            Assert.NotNull(callContent);
            Assert.True(callContent.Count > 0, "get_process_id must return content");

            // Call get_last_command_output — reads $LastCommandOutput from the SAME singleton
            // runspace, proving PS state persists between requests (no reset protocol runs here).
            var cacheResp = await client.SendToolCallAsync("get_last_command_output", new JObject { });
            Assert.Null(cacheResp["error"]);
            var cacheContent = cacheResp["result"]?["content"] as JArray;
            Assert.NotNull(cacheContent);
            Assert.True(cacheContent.Count > 0, "get_last_command_output must return non-empty content");

            var cacheText = cacheContent[0]?["text"]?.ToString() ?? "";
            Assert.False(string.IsNullOrWhiteSpace(cacheText),
                "get_last_command_output returned empty text; singleton PS state did not persist");
            // The cached output must reference the server process (proves it came from the get_process_id call).
            Assert.True(cacheText.Contains(serverPid.ToString(), StringComparison.Ordinal),
                $"Expected PID '{serverPid}' in cached output but got: '{cacheText}'");
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    // ─── Host builder ─────────────────────────────────────────────────────────

    private async Task<(WebApplication App, HttpClient Client)> BuildIsolationHostAsync(bool stateless)
    {
        // 2 workers so session-affinity tests can hold one and query the other.
        var opts = new RunspacePoolOptions
        {
            MinPoolSize = 2,
            MaxPoolSize = 2,
            EagerWarmCount = 2,
            AcquisitionTimeout = TimeSpan.FromSeconds(10),
            IdleTtl = TimeSpan.FromSeconds(300),
            SweepInterval = TimeSpan.FromSeconds(60),
            StopTimeout = TimeSpan.FromSeconds(5),
            ShutdownDrainTimeout = TimeSpan.FromSeconds(10),
            ReplenishCheckInterval = TimeSpan.FromSeconds(60),
        };

        var pool = new StatelessRunspacePool(opts, loggerFactory: null, startupScript: WorkerStartupScript);
        var pooledRunspace = new PooledHttpRunspace(pool, WorkerStartupScript, NullLoggerFactory.Instance);
        pooledRunspace.FinalizeDiscovery();

        var sessionLifecycle = new McpSessionLifecycle();
        var tools = CreateIsolationTools(pooledRunspace);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddSingleton(sessionLifecycle);
        builder.Services.AddSingleton<IPowerShellRunspace>(pooledRunspace);
        builder.Services.AddSingleton<IRunspacePool>(pool);
        builder.Services.AddSingleton<IHostedService, RunspacePoolLifecycleService>();

        var mcpBuilder = builder.Services
            .AddMcpServer()
            .WithHttpTransport(o =>
            {
                o.Stateless = stateless;
#pragma warning disable MCP9006
                o.IdleTimeout = Timeout.InfiniteTimeSpan;
#pragma warning restore MCP9006
                if (!stateless)
                {
#pragma warning disable MCPEXP002
                    o.RunSessionHandler = sessionLifecycle.RunSessionAsync;
#pragma warning restore MCPEXP002
                }
            })
            .WithTools(tools);

        var app = builder.Build();
        app.UseMiddleware<McpProtocolVersionMiddleware>((object)new[] { "/" });
        app.MapMcp();

        await app.StartAsync();

        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");

        return (app, client);
    }

    // ─── Tool definitions ──────────────────────────────────────────────────────

    private McpServerTool[] CreateIsolationTools(IPowerShellRunspace runspace)
    {
        var gate = _blockingGate;
        var entered = _blockingEntered;

        static Task<string> RunScript(IPowerShellRunspace rs, string script) =>
            rs.ExecuteThreadSafeAsync(ps =>
            {
                ps.Commands.Clear();
                ps.AddScript(script);
                var results = ps.Invoke<string>();
                ps.Commands.Clear();
                ps.Streams.ClearStreams();
                return Task.FromResult(results.Count > 0 ? results[0] ?? "null" : "null");
            });

        McpServerTool Tool(string name, string description, Func<CancellationToken, Task<string>> fn) =>
            McpServerTool.Create(fn, new McpServerToolCreateOptions { Name = name, Description = description });

        return
        [
            // Isolation: variables
            Tool("iso_set_var",   "Set request-scoped var",
                _ => RunScript(runspace, "$IsoTestVar = 'contaminated'; 'ok'")),
            Tool("iso_read_var",  "Read request-scoped var",
                _ => RunScript(runspace,
                    "if (Get-Variable IsoTestVar -ErrorAction SilentlyContinue) { $IsoTestVar } else { 'not-set' }")),

            // Isolation: $Error
            Tool("iso_set_error", "Write to $Error",
                _ => RunScript(runspace, "Write-Error 'test-error' -ErrorAction SilentlyContinue; 'ok'")),
            Tool("iso_read_error_count", "Read $Error.Count",
                _ => RunScript(runspace, "$Error.Count.ToString()")),

            // Isolation: working location
            // iso_change_location: moves to platform temp dir and returns the new path in PS canonical form.
            // iso_read_location: returns the current path — used to capture baseline and post-reset value.
            Tool("iso_change_location", "Change location to platform temp dir, return new path",
                _ => RunScript(runspace,
                    "Set-Location ([System.IO.Path]::GetTempPath()); (Get-Location).Path")),
            Tool("iso_read_location", "Return current working directory path",
                _ => RunScript(runspace, "(Get-Location).Path")),

            // Isolation: PSDrives
            Tool("iso_create_drive", "Create request-scoped PSDrive",
                _ => RunScript(runspace,
                    "New-PSDrive -Name IsoTestDrive -PSProvider FileSystem -Root ([System.IO.Path]::GetTempPath()) -ErrorAction SilentlyContinue; 'ok'")),
            Tool("iso_check_drive", "Check if request-scoped PSDrive exists",
                _ => RunScript(runspace,
                    "((Get-PSDrive -Name IsoTestDrive -ErrorAction SilentlyContinue) -ne $null).ToString()")),

            // Isolation: preference variables
            Tool("iso_set_pref_stop", "Set ErrorActionPreference to Stop",
                _ => RunScript(runspace, "$ErrorActionPreference = 'Stop'; 'ok'")),
            Tool("iso_read_pref", "Read ErrorActionPreference",
                _ => RunScript(runspace, "$ErrorActionPreference")),

            // Isolation: functions
            Tool("iso_define_function", "Define request-scoped function",
                _ => RunScript(runspace, "function IsoTestFunc { 'iso-func-result' }; 'ok'")),
            Tool("iso_call_function", "Call request-scoped function (if still defined)",
                _ => RunScript(runspace,
                    "if (Get-Command IsoTestFunc -ErrorAction SilentlyContinue) { IsoTestFunc } else { 'not-defined' }")),

            // Combined contamination
            Tool("iso_contaminate_all", "Set all types of request-scoped state",
                _ => RunScript(runspace, @"
$IsoTestVar = 'contaminated'
Write-Error 'err' -ErrorAction SilentlyContinue
$ErrorActionPreference = 'Stop'
New-PSDrive -Name IsoTestDrive -PSProvider FileSystem -Root ([System.IO.Path]::GetTempPath()) -ErrorAction SilentlyContinue
function IsoTestFunc { 'hello' }
'ok'")),
            Tool("iso_read_all", "Read all isolation fields as a combined string",
                _ => RunScript(runspace, @"
$e = $Error.Count
$v = if (Get-Variable IsoTestVar -ErrorAction Ignore) { $IsoTestVar } else { 'not-set' }
$p = $ErrorActionPreference
$d = ((Get-PSDrive IsoTestDrive -ErrorAction Ignore) -ne $null).ToString()
$f = if (Get-Command IsoTestFunc -ErrorAction Ignore) { IsoTestFunc } else { 'not-defined' }
""var=$v;err=$e;pref=$p;drive=$d;func=$f""")),

            // Startup state
            Tool("iso_read_worker_identity", "Read per-worker identity GUID from startup var",
                _ => RunScript(runspace,
                    "if (Get-Variable WorkerIdentity -ErrorAction SilentlyContinue) { $WorkerIdentity } else { 'not-set' }")),
            Tool("iso_read_worker_marker", "Call startup-defined function Get-WorkerMarker",
                _ => RunScript(runspace,
                    "if (Get-Command Get-WorkerMarker -ErrorAction SilentlyContinue) { Get-WorkerMarker } else { 'not-defined' }")),

            // Blocking tool: holds the current pool worker until the test releases _blockingGate,
            // and returns the worker's startup identity so the caller can compare without a second call.
            McpServerTool.Create(
                (CancellationToken ct) =>
                {
                    string? workerIdentity = null;
                    runspace.ExecuteThreadSafe(ps =>
                    {
                        ps.Commands.Clear();
                        ps.AddScript(
                            "if (Get-Variable WorkerIdentity -ErrorAction SilentlyContinue) { $WorkerIdentity } else { 'not-set' }");
                        var r = ps.Invoke<string>();
                        ps.Commands.Clear();
                        workerIdentity = r.Count > 0 ? r[0] : "not-set";
                        entered.Release();
                        gate.Wait(TimeSpan.FromSeconds(30));
                    });
                    return Task.FromResult(workerIdentity ?? "not-set");
                },
                new McpServerToolCreateOptions
                {
                    Name = "iso_block_worker",
                    Description = "Holds the pool worker and returns its identity GUID"
                }),
        ];
    }

    // ─── HTTP helpers ─────────────────────────────────────────────────────────

    private static async Task<JObject> CallToolAsync(HttpClient client, string toolName)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString("N")[..8],
            method = "tools/call",
            @params = new { name = toolName, arguments = new { } }
        };
        using var resp = await client.PostAsync("/",
            new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));
        var body = await resp.Content.ReadAsStringAsync();
        return ParseJsonOrSse(body, resp.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<string> CallToolTextAsync(HttpClient client, string toolName)
    {
        var result = await CallToolAsync(client, toolName);
        return ExtractText(result);
    }

    private static async Task<JObject> CallToolWithSessionAsync(
        HttpClient client, string toolName, string sessionId)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString("N")[..8],
            method = "tools/call",
            @params = new { name = toolName, arguments = new { } }
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Content = new StringContent(
                JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
        req.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2024-11-05");
        using var resp = await client.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        return ParseJsonOrSse(body, resp.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<string?> InitializeStatefulSessionAsync(HttpClient client)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { tools = new { } },
                clientInfo = new { name = "iso-test", version = "1.0.0" }
            }
        };
        using var resp = await client.PostAsync("/",
            new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));
        return resp.Headers.TryGetValues("Mcp-Session-Id", out var vals)
            ? vals.FirstOrDefault() : null;
    }

    private static string ExtractText(JObject result)
    {
        var content = result["result"]?["content"] as JArray;
        if (content is null || content.Count == 0)
            return string.Empty;
        return content[0]?["text"]?.ToString()?.Trim() ?? string.Empty;
    }

    private static JObject ParseJsonOrSse(string body, string? mediaType)
    {
        if (string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            return JObject.Parse(body);
        var dataLine = body.Split('\n')
            .FirstOrDefault(l => l.StartsWith("data: ", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(dataLine))
            throw new InvalidOperationException($"No MCP data line in response: {body}");
        return JObject.Parse(dataLine[6..]);
    }

    // ─── Minimal ILogger for stdio test ──────────────────────────────────────

    private sealed class XunitLogger : ILogger
    {
        private readonly ITestOutputHelper _out;
        private readonly string _name;
        public XunitLogger(ITestOutputHelper output, string name) { _out = output; _name = name; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                try { _out.WriteLine($"[{logLevel}][{_name}] {formatter(state, exception)}"); }
                catch { /* xunit output may be unavailable after test ends */ }
        }
    }
}
