using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.OutOfProcess;
using PoshMcp.Tests.Shared;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="OutOfProcessSubprocessPool"/> exercising the real
/// pwsh subprocess pool over pool sizes 1, 2, and 4.
/// </summary>
[Trait("Category", "OutOfProcess")]
public class OutOfProcessSubprocessPoolIntegrationTests : IAsyncLifetime
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly ITestOutputHelper _output;
    private string? _pwshPath;
    private string? _hostScriptPath;

    public OutOfProcessSubprocessPoolIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new TestOutputLoggerProvider(output));
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        _logger = _loggerFactory.CreateLogger<OutOfProcessSubprocessPoolIntegrationTests>();
    }

    public async Task InitializeAsync()
    {
        try { _pwshPath = OutOfProcessCommandExecutor.ResolvePwshPath(); }
        catch { _pwshPath = null; }

        _hostScriptPath = await OopTestPaths.ResolveHostScriptAsync();
    }

    public Task DisposeAsync()
    {
        _loggerFactory.Dispose();
        return Task.CompletedTask;
    }

    private OutOfProcessSubprocessPool CreatePool(int poolSize, int? minHealthy = null)
    {
        Assert.NotNull(_pwshPath);
        Assert.NotNull(_hostScriptPath);

        return new OutOfProcessSubprocessPool(
            _pwshPath!, _hostScriptPath!,
            new OutOfProcessSubprocessPoolOptions
            {
                PoolSize = poolSize,
                MinHealthyForStartup = minHealthy ?? 1,
                ReconcilerInterval = TimeSpan.FromMilliseconds(250),
            },
            _loggerFactory,
            requestTimeout: TimeSpan.FromSeconds(20));
    }

    // ---- Lifecycle ----

    [PwshAvailableTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task Pool_StartAsync_BringsAllHostsHealthy(int poolSize)
    {
        if (_hostScriptPath is null) return;

        await using var pool = CreatePool(poolSize);
        await pool.StartAsync();

        Assert.Equal(poolSize, pool.HealthyCount);
    }

    [PwshAvailableTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task Pool_SetupThenInvoke_Succeeds(int poolSize)
    {
        if (_hostScriptPath is null) return;

        await using var pool = CreatePool(poolSize);
        await pool.StartAsync();

        // Empty environment: a no-op setup that the host script must accept.
        await pool.SetupAsync(new EnvironmentConfiguration());

        var output = await pool.InvokeAsync(
            "Get-Date",
            new Dictionary<string, object?>(),
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(output));
        _logger.LogInformation("Get-Date returned: {Output}", output);
    }

    [PwshAvailableTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task Pool_DiscoverCommands_CachesAcrossCalls(int poolSize)
    {
        if (_hostScriptPath is null) return;

        await using var pool = CreatePool(poolSize);
        await pool.StartAsync();
        await pool.SetupAsync(new EnvironmentConfiguration());

        var config = new PowerShellConfiguration
        {
            FunctionNames = new List<string> { "Get-Date" },
            Modules = new List<string>(),
            IncludePatterns = new List<string>(),
            ExcludePatterns = new List<string>()
        };

        var schemas1 = await pool.DiscoverCommandsAsync(config);
        var schemas2 = await pool.DiscoverCommandsAsync(config);

        Assert.NotEmpty(schemas1);
        // Cache hit: same instance reference indicates the cached path was taken.
        Assert.Same(schemas1, schemas2);
    }

    // ---- Concurrent dispatch ----

    [PwshAvailableTheory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task Pool_ConcurrentInvokes_SpreadAcrossHosts(int poolSize)
    {
        if (_hostScriptPath is null) return;

        await using var pool = CreatePool(poolSize);
        await pool.StartAsync();
        await pool.SetupAsync(new EnvironmentConfiguration());

        // Each invoke prints the current process id from inside pwsh. With concurrent
        // invokes across N >= 2 hosts, we expect at least 2 distinct PIDs in the results.
        // Each invoke also sleeps so the hosts overlap in time.
        const int requestCount = 8;
        var results = new ConcurrentBag<string>();

        var tasks = Enumerable.Range(0, requestCount).Select(_ => Task.Run(async () =>
        {
            var output = await pool.InvokeAsync(
                "Start-Sleep",
                new Dictionary<string, object?> { ["Milliseconds"] = 200 },
                CancellationToken.None);
            results.Add(output);
        })).ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(requestCount, results.Count);

        // Now verify multiple PIDs by invoking $PID on each host in parallel.
        var pidResults = new ConcurrentBag<string>();
        var pidTasks = Enumerable.Range(0, poolSize * 4).Select(_ => Task.Run(async () =>
        {
            // Use Get-Variable to fetch $PID — works across host script versions.
            var output = await pool.InvokeAsync(
                "Get-Variable",
                new Dictionary<string, object?>
                {
                    ["Name"] = "PID",
                    ["ValueOnly"] = true,
                },
                CancellationToken.None);
            pidResults.Add(output);
        })).ToArray();

        await Task.WhenAll(pidTasks);

        var distinctPids = pidResults
            .Select(r => r.Trim())
            .Where(r => !string.IsNullOrEmpty(r))
            .Distinct()
            .ToArray();

        _logger.LogInformation(
            "Distinct PIDs observed across {Total} pool invocations: {Pids}",
            pidResults.Count, string.Join(",", distinctPids));

        // The pool size is the upper bound; we want to see > 1 PID for poolSize >= 2.
        Assert.True(
            distinctPids.Length >= 2,
            $"Expected >=2 distinct PIDs across pool of {poolSize}; got {distinctPids.Length}.");
    }

    // ---- Crash recovery ----

    [PwshAvailableTheory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task Pool_HostKilledMidLease_ReconcilerReplaces(int poolSize)
    {
        if (_hostScriptPath is null) return;

        await using var pool = CreatePool(poolSize);
        await pool.StartAsync();
        await pool.SetupAsync(new EnvironmentConfiguration());

        // Find the pids the pool currently knows about by invoking $PID multiple times
        // to fan out across hosts.
        var pidsBefore = new ConcurrentBag<int>();
        var beforeTasks = Enumerable.Range(0, poolSize * 4).Select(_ => Task.Run(async () =>
        {
            var output = await pool.InvokeAsync(
                "Get-Variable",
                new Dictionary<string, object?>
                {
                    ["Name"] = "PID",
                    ["ValueOnly"] = true,
                },
                CancellationToken.None);
            if (int.TryParse(output.Trim(), out var pid))
                pidsBefore.Add(pid);
        })).ToArray();
        await Task.WhenAll(beforeTasks);

        var distinctBefore = pidsBefore.Distinct().ToArray();
        Assert.NotEmpty(distinctBefore);

        // Kill ONE pwsh subprocess externally to simulate a crash.
        var victimPid = distinctBefore.First();
        try
        {
            var victim = Process.GetProcessById(victimPid);
            _logger.LogWarning("Killing victim PID {Pid} to simulate host crash.", victimPid);
            victim.Kill(entireProcessTree: false);
            victim.WaitForExit(TimeSpan.FromSeconds(5));
        }
        catch (ArgumentException)
        {
            return; // Already gone; skip the test.
        }

        // Wait up to ~5s for the reconciler to replace the dead host and the pool to
        // return to full health.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline && pool.HealthyCount < poolSize)
        {
            await Task.Delay(150);
        }

        Assert.Equal(poolSize, pool.HealthyCount);

        // Subsequent invokes must succeed and the dead PID must NOT appear in results.
        var pidsAfter = new ConcurrentBag<int>();
        var afterTasks = Enumerable.Range(0, poolSize * 4).Select(_ => Task.Run(async () =>
        {
            try
            {
                var output = await pool.InvokeAsync(
                    "Get-Variable",
                    new Dictionary<string, object?>
                    {
                        ["Name"] = "PID",
                        ["ValueOnly"] = true,
                    },
                    CancellationToken.None);
                if (int.TryParse(output.Trim(), out var pid))
                    pidsAfter.Add(pid);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Post-crash invoke failed (expected for the in-flight one).");
            }
        })).ToArray();
        await Task.WhenAll(afterTasks);

        Assert.NotEmpty(pidsAfter);
        Assert.DoesNotContain(victimPid, pidsAfter);
    }

    // ---- Per-request kill on timeout ----

    [PwshAvailableFact]
    public async Task Pool_PerRequestTimeout_KillsHostAndPoolRecovers()
    {
        if (_hostScriptPath is null) return;

        // Use a moderate request timeout: long enough for the host startup ping
        // to round-trip on slow machines (cold pwsh.exe on Windows can take >1s),
        // short enough that a Start-Sleep 30 invoke fires the timeout quickly.
        await using var pool = new OutOfProcessSubprocessPool(
            _pwshPath!, _hostScriptPath!,
            new OutOfProcessSubprocessPoolOptions
            {
                PoolSize = 2,
                MinHealthyForStartup = 1,
                ReconcilerInterval = TimeSpan.FromMilliseconds(200),
            },
            _loggerFactory,
            requestTimeout: TimeSpan.FromSeconds(5));

        await pool.StartAsync();
        await pool.SetupAsync(new EnvironmentConfiguration());

        // This sleep is much longer than the request timeout — the underlying host
        // will be killed by the pool's per-request timeout handling.
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await pool.InvokeAsync(
                "Start-Sleep",
                new Dictionary<string, object?> { ["Seconds"] = 30 },
                CancellationToken.None);
        });

        // After the timeout, the pool should reconcile back to full health.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline && pool.HealthyCount < 2)
        {
            await Task.Delay(150);
        }

        Assert.Equal(2, pool.HealthyCount);

        // And subsequent invokes must succeed.
        var output = await pool.InvokeAsync(
            "Get-Date",
            new Dictionary<string, object?>(),
            CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(output));
    }

    // ---- Fail-fast on first host startup ----

    [PwshAvailableFact]
    public async Task Pool_BadHostScript_FailsFast()
    {
        // A bogus host script path forces every host to fail startup.
        // The first-host fail-fast contract means StartAsync must throw — even if
        // MinHealthyForStartup is 1.
        var pool = new OutOfProcessSubprocessPool(
            _pwshPath!,
            Path.Combine(Path.GetTempPath(), $"definitely-not-a-real-host-{Guid.NewGuid():N}.ps1"),
            new OutOfProcessSubprocessPoolOptions
            {
                PoolSize = 2,
                MinHealthyForStartup = 1,
                StartupRetryCount = 1,
                StartupBackoffInitial = TimeSpan.FromMilliseconds(10),
                StartupBackoffMax = TimeSpan.FromMilliseconds(50),
                ReconcilerInterval = TimeSpan.FromSeconds(1),
            },
            _loggerFactory);

        await Assert.ThrowsAnyAsync<Exception>(() => pool.StartAsync());
        await pool.DisposeAsync();
    }
}
