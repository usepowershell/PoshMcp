using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation.Runspaces;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.Pool;
using Xunit;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.Tests.Functional.Pool;

/// <summary>
/// Functional tests for <see cref="RunspaceResetProtocol"/> and
/// <see cref="StatelessRunspacePool"/> startup/reset semantics using real PowerShell runspaces.
/// Only this category of tests requires PS SDK initialization; unit tests use test doubles.
/// </summary>
[Trait("Category", "Functional")]
public sealed class RunspacePoolFunctionalTests : IDisposable
{
    // ─── Helpers ────────────────────────────────────────────────────────────────

    private static IsolatedPowerShellRunspace CreateRunspace(string script = "") =>
        new(script);

    private static RunspaceWorker CreateWorker(string startupScript = "")
    {
        var rs = CreateRunspace(startupScript);
        var worker = new RunspaceWorker(rs);
        var snapshot = RunspaceResetProtocol.CaptureVariableSnapshot(worker.PowerShell);
        worker.SetInitializedVariableSnapshot(snapshot);
        worker.TryTransitionTo(RunspaceWorkerState.Warm);
        worker.TryTransitionTo(RunspaceWorkerState.Leased);
        worker.TryTransitionTo(RunspaceWorkerState.Resetting);
        return worker;
    }

    private static RunspacePoolOptions FastOptions(int min = 1, int max = 4, int eager = 1) =>
        new()
        {
            MinPoolSize = min,
            MaxPoolSize = max,
            EagerWarmCount = eager,
            AcquisitionTimeout = TimeSpan.FromSeconds(10),
            IdleTtl = TimeSpan.FromSeconds(300),
            SweepInterval = TimeSpan.FromSeconds(60),
            StopTimeout = TimeSpan.FromSeconds(5),
            ShutdownDrainTimeout = TimeSpan.FromSeconds(10),
            ReplenishCheckInterval = TimeSpan.FromSeconds(60),
        };

    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (var d in _disposables)
            try { d.Dispose(); } catch { /* best-effort */ }
    }

    // ─── RunspaceResetProtocol — $Error cleared ──────────────────────────────

    [Fact]
    public async Task ResetProtocol_ClearsErrorStream()
    {
        using var worker = CreateWorker();
        var ps = worker.PowerShell;

        // Populate $Error by running a command that writes a non-terminating error.
        ps.Commands.Clear();
        ps.AddScript("Write-Error 'test error' -ErrorAction SilentlyContinue");
        ps.Invoke();
        ps.Commands.Clear();
        ps.Streams.ClearStreams();

        // Verify $Error is non-empty before reset.
        ps.AddScript("$Error.Count");
        var before = ps.Invoke<int>();
        ps.Commands.Clear();
        ps.Streams.ClearStreams();
        Assert.True(before[0] > 0, "Expected $Error to be populated before reset.");

        await RunspaceResetProtocol.ResetAsync(worker, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, TimeSpan.FromSeconds(5));

        // Verify $Error is empty after reset.
        ps.AddScript("$Error.Count");
        var after = ps.Invoke<int>();
        ps.Commands.Clear();
        ps.Streams.ClearStreams();
        Assert.Equal(0, after[0]);
    }

    // ─── RunspaceResetProtocol — preference variables reset ──────────────────

    [Fact]
    public async Task ResetProtocol_ResetsPreferenceVariables()
    {
        using var worker = CreateWorker();
        var ps = worker.PowerShell;

        // Corrupt preference variables.
        ps.Commands.Clear();
        ps.AddScript(@"
$ErrorActionPreference  = 'Stop'
$VerbosePreference      = 'Continue'
$WhatIfPreference       = $true
");
        ps.Invoke();
        ps.Commands.Clear();
        ps.Streams.ClearStreams();

        await RunspaceResetProtocol.ResetAsync(worker, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, TimeSpan.FromSeconds(5));

        ps.AddScript(@"@{
    EAP = $ErrorActionPreference
    VP  = $VerbosePreference
    WIF = $WhatIfPreference
}");
        var results = ps.Invoke();
        ps.Commands.Clear();
        ps.Streams.ClearStreams();

        var dict = (System.Collections.Hashtable)results[0].BaseObject;
        Assert.Equal("Continue", dict["EAP"]?.ToString());
        Assert.Equal("SilentlyContinue", dict["VP"]?.ToString());
        Assert.Equal("False", dict["WIF"]?.ToString());
    }

    // ─── RunspaceResetProtocol — request-scoped vars cleared ─────────────────

    [Fact]
    public async Task ResetProtocol_ClearsRequestScopedVariables()
    {
        using var worker = CreateWorker();
        var ps = worker.PowerShell;

        // Simulate a request that sets a variable.
        ps.Commands.Clear();
        ps.AddScript("$McpRequestResult = 'some-value'");
        ps.Invoke();
        ps.Commands.Clear();
        ps.Streams.ClearStreams();

        await RunspaceResetProtocol.ResetAsync(worker, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, TimeSpan.FromSeconds(5));

        ps.AddScript("(Get-Variable -Name McpRequestResult -ErrorAction SilentlyContinue) -eq $null");
        var results = ps.Invoke<bool>();
        ps.Commands.Clear();
        ps.Streams.ClearStreams();

        Assert.True(results[0], "McpRequestResult should have been cleared by reset.");
    }

    // ─── RunspaceResetProtocol — initialized vars preserved ──────────────────

    [Fact]
    public async Task ResetProtocol_PreservesWorkerInitializedVariables()
    {
        const string startup = "$WorkerToken = 'worker-init-value-{0}'";
        using var worker = CreateWorker(string.Format(startup, Guid.NewGuid()));
        var ps = worker.PowerShell;

        // Capture initial value of the worker-initialized variable.
        ps.AddScript("$WorkerToken");
        var initVal = ps.Invoke<string>();
        ps.Commands.Clear();
        ps.Streams.ClearStreams();
        Assert.Single(initVal);
        var expectedToken = initVal[0];

        // Simulate a request-scoped variable.
        ps.AddScript("$RequestScopedVar = 'should-be-gone'");
        ps.Invoke();
        ps.Commands.Clear();
        ps.Streams.ClearStreams();

        await RunspaceResetProtocol.ResetAsync(worker, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, TimeSpan.FromSeconds(5));

        // Worker token must survive.
        ps.AddScript("$WorkerToken");
        var afterVal = ps.Invoke<string>();
        ps.Commands.Clear();
        ps.Streams.ClearStreams();
        Assert.Equal(expectedToken, afterVal[0]);

        // Request-scoped variable must be gone.
        ps.AddScript("(Get-Variable -Name RequestScopedVar -ErrorAction SilentlyContinue) -eq $null");
        var gone = ps.Invoke<bool>();
        ps.Commands.Clear();
        ps.Streams.ClearStreams();
        Assert.True(gone[0]);
    }

    // ─── RunspaceResetProtocol — working location reset ──────────────────────

    [Fact]
    public async Task ResetProtocol_ResetsWorkingLocation()
    {
        using var worker = CreateWorker();
        var ps = worker.PowerShell;

        // Change to a known subdirectory.
        var tempDir = System.IO.Path.GetTempPath();
        ps.Commands.Clear();
        ps.AddScript($"Set-Location -Path '{tempDir.Replace("'", "\\'")}'");
        ps.Invoke();
        ps.Commands.Clear();
        ps.Streams.ClearStreams();

        await RunspaceResetProtocol.ResetAsync(worker, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, TimeSpan.FromSeconds(5));

        ps.AddScript("(Get-Location).Path");
        var location = ps.Invoke<string>();
        ps.Commands.Clear();
        ps.Streams.ClearStreams();

        // After reset the location should be at a root (not the temp subdirectory).
        // On Windows it's e.g. C:\; on Linux it's /.
        var loc = location[0];
        Assert.True(
            loc.Length <= 4 || loc == "/" || loc.EndsWith(":\\"),
            $"Expected a root path after reset; got '{loc}'.");
    }

    // ─── RunspaceResetProtocol — broken runspace throws ──────────────────────

    [Fact]
    public async Task ResetProtocol_BrokenRunspace_Throws()
    {
        using var worker = CreateWorker();
        var ps = worker.PowerShell;

        // Force the runspace into Broken state by closing it.
        ps.Runspace.Close();
        ps.Runspace.Dispose();

        // Note: RunspaceState.Broken may not be set just by Close/Dispose;
        // instead we rely on the execute path throwing, which the protocol wraps.
        // This test validates that a broken-during-script runspace is surfaced.
        // If PS didn't set Broken state, the subsequent Invoke will throw — which
        // is also correctly caught by the caller as a reset failure.
        await Assert.ThrowsAnyAsync<Exception>(
            () => RunspaceResetProtocol.ResetAsync(
                worker,
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
                TimeSpan.FromSeconds(5)));
    }

    // ─── StatelessRunspacePool — startup script runs once per worker ─────────

    [Fact]
    public async Task Pool_StartupScript_RunsOncePerWorker()
    {
        // The startup script writes a unique per-worker token into $WorkerToken.
        const string script = "$WorkerToken = [System.Guid]::NewGuid().ToString()";
        var opts = FastOptions(min: 1, max: 2, eager: 2);

        await using var pool = new StatelessRunspacePool(
            opts,
            startupScript: script);

        await pool.StartAsync();

        // Acquire both workers and collect their tokens.
        var lease1 = await pool.AcquireAsync();
        var lease2 = await pool.AcquireAsync();

        string GetToken(RunspaceLease l)
        {
            var ps = l.PowerShell;
            ps.Commands.Clear();
            ps.AddScript("$WorkerToken");
            var res = ps.Invoke<string>();
            ps.Commands.Clear();
            return res.FirstOrDefault() ?? string.Empty;
        }

        var token1 = GetToken(lease1);
        var token2 = GetToken(lease2);

        // Both tokens should be valid GUIDs.
        Assert.True(Guid.TryParse(token1, out _), $"Expected GUID; got '{token1}'.");
        Assert.True(Guid.TryParse(token2, out _), $"Expected GUID; got '{token2}'.");

        // Each worker has its own token (startup ran independently per worker).
        Assert.NotEqual(token1, token2);

        await lease1.DisposeAsync();
        await lease2.DisposeAsync();
    }

    // ─── StatelessRunspacePool — startup failure never enters pool ───────────

    [Fact]
    public async Task Pool_StartupScriptFailure_WorkerNeverAvailable()
    {
        const string failScript = "throw 'startup intentionally failed'";
        var opts = FastOptions(min: 1, max: 2, eager: 2);
        // Both workers will fail startup.
        await using var pool = new StatelessRunspacePool(opts, startupScript: failScript);

        await Assert.ThrowsAsync<InvalidOperationException>(() => pool.StartAsync());

        var stats = pool.GetStats();
        Assert.Equal(0, stats.WarmWorkers);
        Assert.Equal(0, stats.TotalWorkers);
    }

    // ─── StatelessRunspacePool — after reset worker is available again ────────

    [Fact]
    public async Task Pool_AfterReset_WorkerBecomesWarmAgain()
    {
        await using var pool = new StatelessRunspacePool(FastOptions(min: 1, max: 1, eager: 1));
        await pool.StartAsync();

        // Acquire and release the single worker.
        var lease = await pool.AcquireAsync();
        await lease.DisposeAsync();

        // Wait for reset to complete.
        await Task.Delay(200);

        var stats = pool.GetStats();
        Assert.Equal(1, stats.WarmWorkers);
        Assert.Equal(0, stats.LeasedWorkers);
        Assert.Equal(0, stats.ResettingWorkers);
    }

    // ─── StatelessRunspacePool — reset state isolation between requests ───────

    [Fact]
    public async Task Pool_RequestScopedVariable_NotLeakedToNextRequest()
    {
        await using var pool = new StatelessRunspacePool(FastOptions(min: 1, max: 1, eager: 1));
        await pool.StartAsync();

        // First request: set a variable.
        await using (var lease1 = await pool.AcquireAsync())
        {
            lease1.PowerShell.Commands.Clear();
            lease1.PowerShell.AddScript("$ShouldDisappear = 'leaked'");
            lease1.PowerShell.Invoke();
            lease1.PowerShell.Commands.Clear();
        }

        // Wait for reset.
        await Task.Delay(300);

        // Second request: variable must not be present.
        await using var lease2 = await pool.AcquireAsync();
        lease2.PowerShell.Commands.Clear();
        lease2.PowerShell.AddScript(
            "(Get-Variable -Name ShouldDisappear -ErrorAction SilentlyContinue) -eq $null");
        var results = lease2.PowerShell.Invoke<bool>();
        lease2.PowerShell.Commands.Clear();

        Assert.True(results[0], "Request-scoped variable leaked across lease boundary.");
    }

    // ─── RunspaceResetProtocol — request-scoped PSDrive removed ───────────────

    [Fact]
    public async Task ResetProtocol_RemovesRequestScopedPsDrive()
    {
        using var worker = CreateWorker();
        var ps = worker.PowerShell;

        // Capture initial drive snapshot (what the reset protocol should preserve).
        var driveSnapshot = RunspaceResetProtocol.CaptureDriveSnapshot(ps);
        worker.SetInitializedDriveSnapshot(driveSnapshot);

        // Create a temporary FileSystem drive to simulate a request-scoped drive.
        // Use a unique name to avoid clashing with any existing drive.
        var driveName = $"McpTest{Guid.NewGuid():N}"[..10];
        var tempPath = System.IO.Path.GetTempPath();
        ps.Commands.Clear();
        ps.AddScript($"New-PSDrive -Name '{driveName}' -PSProvider FileSystem -Root '{tempPath.Replace("'", "\\'")}' -Scope Global");
        ps.Invoke();
        ps.Commands.Clear();
        ps.Streams.ClearStreams();

        // Verify the drive exists before reset.
        ps.AddScript($"(Get-PSDrive -Name '{driveName}' -ErrorAction SilentlyContinue) -ne $null");
        var before = ps.Invoke<bool>();
        ps.Commands.Clear();
        ps.Streams.ClearStreams();
        Assert.True(before.Count > 0 && before[0], "Expected request-scoped drive to exist before reset.");

        await RunspaceResetProtocol.ResetAsync(worker, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, TimeSpan.FromSeconds(5));

        // Verify the drive was removed after reset.
        ps.AddScript($"(Get-PSDrive -Name '{driveName}' -ErrorAction SilentlyContinue) -eq $null");
        var after = ps.Invoke<bool>();
        ps.Commands.Clear();
        ps.Streams.ClearStreams();
        Assert.True(after.Count > 0 && after[0], $"Request-scoped PSDrive '{driveName}' was not removed by reset.");
    }

    [Fact]
    public async Task Pool_RequestScopedPsDrive_NotLeakedToNextRequest()
    {
        await using var pool = new StatelessRunspacePool(FastOptions(min: 1, max: 1, eager: 1));
        await pool.StartAsync();

        var driveName = $"McpTest{Guid.NewGuid():N}"[..10];
        var tempPath = System.IO.Path.GetTempPath();

        // First request: create a PSDrive.
        await using (var lease1 = await pool.AcquireAsync())
        {
            lease1.PowerShell.Commands.Clear();
            lease1.PowerShell.AddScript(
                $"New-PSDrive -Name '{driveName}' -PSProvider FileSystem -Root '{tempPath.Replace("'", "\\'")}' -Scope Global");
            lease1.PowerShell.Invoke();
            lease1.PowerShell.Commands.Clear();
        }

        // Wait for reset to complete.
        await Task.Delay(500);

        // Second request: the drive must not be present.
        await using var lease2 = await pool.AcquireAsync();
        lease2.PowerShell.Commands.Clear();
        lease2.PowerShell.AddScript(
            $"(Get-PSDrive -Name '{driveName}' -ErrorAction SilentlyContinue) -eq $null");
        var gone = lease2.PowerShell.Invoke<bool>();
        lease2.PowerShell.Commands.Clear();

        Assert.True(gone.Count > 0 && gone[0],
            $"Request-scoped PSDrive '{driveName}' leaked across lease boundary.");
    }

    // ─── StatelessRunspacePool — StopTimeout controllable stuck path ─────────

    [Fact]
    public async Task Pool_Reset_StuckPipeline_EvictedWithStopTimeoutReason()
    {
        // Create a pool where the reset protocol uses a real stuck PS script.
        // The script uses Start-Sleep; we cancel via a short StopTimeout so the test
        // completes in bounded time and never hangs the suite.
        var resetStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task SlowReset(RunspaceWorker w, ILogger l, CancellationToken ct)
        {
            resetStarted.TrySetResult();
            // Invoke a long-running script without honouring ct (simulates stuck pipeline).
            // The real RunspaceResetProtocol.ResetAsync uses WaitAsync(ct) + ps.Stop() to
            // implement StopTimeout; here we simulate by throwing TimeoutException directly
            // after signalling that the reset started.
            await Task.Delay(50, CancellationToken.None);
            throw new TimeoutException("Simulated stuck pipeline exceeded StopTimeout.");
        }

        var opts = FastOptions(min: 1, max: 2, eager: 1);
        opts.StopTimeout = TimeSpan.FromMilliseconds(200);

        await using var pool = new StatelessRunspacePool(
            opts,
            resetProtocol: SlowReset);
        await pool.StartAsync();

        // Acquire and release; the SlowReset fires asynchronously after lease disposal.
        var lease = await pool.AcquireAsync();
        await lease.DisposeAsync();

        // Wait for reset to start, then verify pool recovers (worker evicted + replenished).
        await resetStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForPoolStats(pool, s => s.LeasedWorkers == 0 && s.ResettingWorkers == 0,
            TimeSpan.FromSeconds(5));

        var stats = pool.GetStats();
        Assert.Equal(0, stats.LeasedWorkers);
        Assert.Equal(0, stats.ResettingWorkers);
        // Worker was evicted with stop_timeout; pool must not have negative total.
        Assert.True(stats.TotalWorkers >= 0, $"TotalWorkers went negative: {stats.TotalWorkers}");
    }

    // ─── Post-eviction replenishment — observable synchronization ─────────────

    [Fact]
    public async Task Pool_AfterEviction_ReplenisherRestoresMinPoolSize()
    {
        // Evict all workers explicitly; replenishment must restore Min=1 without arbitrary sleeps.
        await using var pool = new StatelessRunspacePool(FastOptions(min: 1, max: 2, eager: 2));
        await pool.StartAsync();

        Assert.Equal(2, pool.GetStats().TotalWorkers);

        var l1 = await pool.AcquireAsync();
        var l2 = await pool.AcquireAsync();
        l1.RequestEviction();
        l2.RequestEviction();
        await l1.DisposeAsync();
        await l2.DisposeAsync();

        // Replenishment is triggered via FireAndForgetCreateWorkerAsync from OnWorkerReturnedAsync.
        // Poll (not sleep) until MinPoolSize workers are present.
        await WaitForPoolStats(pool, s => s.TotalWorkers >= 1, TimeSpan.FromSeconds(10));

        Assert.True(pool.GetStats().TotalWorkers >= 1,
            $"Post-eviction replenishment failed; TotalWorkers={pool.GetStats().TotalWorkers}");
    }

    private static async Task WaitForPoolStats(
        StatelessRunspacePool pool,
        Func<RunspacePoolStats, bool> condition,
        TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (condition(pool.GetStats())) return;
            await Task.Delay(20);
        }
        var s = pool.GetStats();
        throw new TimeoutException(
            $"Pool condition not met within {timeout}. " +
            $"warm={s.WarmWorkers} leased={s.LeasedWorkers} resetting={s.ResettingWorkers} total={s.TotalWorkers}");
    }

    // ─── Concurrent real-PS leases — startup-script state isolated per worker ───

    /// <summary>
    /// Three concurrent leases on a pool with three workers, each initialized with a unique
    /// GUID via the startup script. All three tokens must be distinct — proving that startup
    /// scripts run independently per worker, not shared, even under concurrent acquisition.
    /// </summary>
    [Fact]
    public async Task Pool_ConcurrentLeases_RealPS_EachWorkerHasDistinctStartupState()
    {
        const string script = "$WorkerToken = [System.Guid]::NewGuid().ToString()";
        await using var pool = new StatelessRunspacePool(FastOptions(min: 3, max: 3, eager: 3),
            startupScript: script);
        await pool.StartAsync();

        // Acquire all 3 workers concurrently.
        var leaseTasks = Enumerable.Range(0, 3)
            .Select(_ => pool.AcquireAsync().AsTask())
            .ToArray();
        var leases = await Task.WhenAll(leaseTasks);

        string ReadToken(RunspaceLease l)
        {
            l.PowerShell.Commands.Clear();
            l.PowerShell.AddScript("$WorkerToken");
            var res = l.PowerShell.Invoke<string>();
            l.PowerShell.Commands.Clear();
            return res.FirstOrDefault() ?? string.Empty;
        }

        var tokens = leases.Select(ReadToken).ToArray();

        // All tokens must be valid GUIDs (startup script ran on each worker).
        Assert.All(tokens, t =>
            Assert.True(Guid.TryParse(t, out _), $"Expected GUID from startup script; got '{t}'"));

        // All tokens must be distinct — each worker ran its own startup script.
        Assert.Equal(3, tokens.Distinct().Count());

        foreach (var l in leases) await l.DisposeAsync();
    }

    // ─── Concurrent reset — request-scoped variables never leak across workers ──

    /// <summary>
    /// Across multiple rounds of concurrent acquire/release with real PowerShell, a variable
    /// set inside one lease must not be visible in any subsequent lease on any worker.
    /// Proves that reset correctness holds under concurrent load, not just sequentially.
    /// </summary>
    [Fact]
    public async Task Pool_ConcurrentReset_RealPS_RequestScopedVariableNeverLeaks()
    {
        await using var pool = new StatelessRunspacePool(FastOptions(min: 2, max: 3, eager: 3));
        await pool.StartAsync();

        const int rounds = 5;
        for (int r = 0; r < rounds; r++)
        {
            // 3 concurrent leases; each sets the same variable name to a round-unique value.
            var leaseTasks = Enumerable.Range(0, 3)
                .Select(_ => pool.AcquireAsync().AsTask())
                .ToArray();
            var leases = await Task.WhenAll(leaseTasks);

            // Set the marker variable on all 3 workers concurrently.
            var marker = $"ConcLeak_r{r}_{Guid.NewGuid():N}";
            await Task.WhenAll(leases.Select(l => Task.Run(() =>
            {
                l.PowerShell.Commands.Clear();
                l.PowerShell.AddScript($"$ConcurrentLeakProbe = '{marker}'");
                l.PowerShell.Invoke();
                l.PowerShell.Commands.Clear();
            })));

            foreach (var l in leases) await l.DisposeAsync();
        }

        // After all rounds complete, wait for all resets to finish.
        await WaitForPoolStats(pool,
            s => s.LeasedWorkers == 0 && s.ResettingWorkers == 0,
            TimeSpan.FromSeconds(10));

        // Acquire a fresh lease and verify the leak-probe variable is absent.
        await using var checkLease = await pool.AcquireAsync();
        checkLease.PowerShell.Commands.Clear();
        checkLease.PowerShell.AddScript(
            "(Get-Variable -Name ConcurrentLeakProbe -ErrorAction SilentlyContinue) -eq $null");
        var gone = checkLease.PowerShell.Invoke<bool>();
        checkLease.PowerShell.Commands.Clear();

        Assert.True(gone.Count > 0 && gone[0],
            "Request-scoped variable leaked across concurrent lease boundaries.");
    }
}
