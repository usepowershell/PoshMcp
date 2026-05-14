using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PoshMcp.Server.PowerShell.OutOfProcess;
using Xunit;

namespace PoshMcp.Tests.Unit.OutOfProcess;

/// <summary>
/// Cancellation propagation tests for issue #188. Cancelling the .NET-side
/// <see cref="CancellationToken"/> must signal the in-flight pwsh pipeline to
/// stop within a bounded time, leave the host healthy for follow-up requests,
/// and (in Pool/ProcessPool) not block parallel work.
/// </summary>
[Trait("Category", "OutOfProcess")]
public class OutOfProcessCancellationTests
{
    private static readonly TimeSpan ObservationTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task Single_LongRunningInvoke_TokenCancelStopsPipeline()
    {
        if (!TryResolvePwsh(out var pwshPath)) return;

        var scriptPath = await ResolveOopHostScriptForTestAsync("oop-host.ps1");
        if (scriptPath is null) return;

        await using var host = new OutOfProcessHost(
            pwshPath, scriptPath,
            NullLogger<OutOfProcessHost>.Instance,
            TimeSpan.FromSeconds(60));

        await host.StartAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource();

        // Issue a 60s sleep and cancel after a short delay. Token cancel must
        // unblock the awaiter in well under the sleep duration.
        var invokeTask = host.SendRequestAsync<JsonElement>(
            "invoke",
            new
            {
                command = "Start-Sleep",
                parameters = new Dictionary<string, object?> { ["Seconds"] = 60 }
            },
            cts.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(500));
        var sw = Stopwatch.StartNew();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await invokeTask.WaitAsync(ObservationTimeout);
        });

        sw.Stop();
        Assert.True(sw.Elapsed < ObservationTimeout,
            $"Cancellation took {sw.Elapsed.TotalSeconds:F1}s — exceeded {ObservationTimeout.TotalSeconds}s budget.");

        // Follow-up ping must succeed promptly — the host is reusable.
        using var pingCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pingResult = await host.SendRequestAsync<JsonElement>(
            "ping", null, pingCts.Token);
        Assert.True(pingResult.TryGetProperty("status", out var status));
        Assert.Equal("ok", status.GetString());
    }

    [Fact]
    public async Task Pool_LongRunningInvoke_TokenCancelStopsPipelineNoHeadOfLine()
    {
        if (!TryResolvePwsh(out var pwshPath)) return;

        var scriptPath = await ResolveOopHostScriptForTestAsync("oop-host-pool.ps1");
        if (scriptPath is null) return;

        await using var host = new OutOfProcessHost(
            pwshPath, scriptPath,
            NullLogger<OutOfProcessHost>.Instance,
            TimeSpan.FromSeconds(60));

        await host.StartAsync(CancellationToken.None);

        // Apply a minimal setup so the pool sizes itself > 1.
        using var setupCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await host.SendRequestAsync<JsonElement>(
            "setup",
            new
            {
                modulePaths = Array.Empty<string>(),
                trustPSGallery = false,
                installModules = Array.Empty<object>(),
                importModules = Array.Empty<string>(),
                runspacePoolSize = 4,
            },
            setupCts.Token);

        using var sleepCts = new CancellationTokenSource();

        // Slow invoke (sleeps 60s) on one runspace in the pool.
        var slowInvoke = host.SendRequestAsync<JsonElement>(
            "invoke",
            new
            {
                command = "Start-Sleep",
                parameters = new Dictionary<string, object?> { ["Seconds"] = 60 }
            },
            sleepCts.Token);

        // Give it a moment to start.
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        // Concurrent fast invoke MUST complete normally — proves no head-of-line.
        using var fastCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var fastTask = host.SendRequestAsync<JsonElement>(
            "invoke",
            new
            {
                command = "Get-Date",
                parameters = new Dictionary<string, object?>()
            },
            fastCts.Token);

        var fastResult = await fastTask;
        Assert.True(fastResult.TryGetProperty("output", out _));

        // Now cancel the slow one — should unblock promptly.
        var sw = Stopwatch.StartNew();
        sleepCts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await slowInvoke.WaitAsync(ObservationTimeout);
        });

        sw.Stop();
        Assert.True(sw.Elapsed < ObservationTimeout,
            $"Pool cancellation took {sw.Elapsed.TotalSeconds:F1}s — exceeded {ObservationTimeout.TotalSeconds}s budget.");

        // Pool still healthy for follow-up.
        using var followUpCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var followUp = await host.SendRequestAsync<JsonElement>(
            "invoke",
            new
            {
                command = "Get-Date",
                parameters = new Dictionary<string, object?>()
            },
            followUpCts.Token);
        Assert.True(followUp.TryGetProperty("output", out _));
    }

    [Fact]
    public async Task ProcessPool_TokenCancel_StopsPipelineAndKeepsSlotsHealthy()
    {
        if (!TryResolvePwsh(out var pwshPath)) return;

        var scriptPath = await ResolveOopHostScriptForTestAsync("oop-host.ps1");
        if (scriptPath is null) return;

        var poolOptions = new OutOfProcessSubprocessPoolOptions
        {
            PoolSize = 2,
            MinHealthyForStartup = 1,
            ReconcilerInterval = TimeSpan.FromMilliseconds(500),
        };

        await using var pool = new OutOfProcessSubprocessPool(
            pwshPath, scriptPath, poolOptions,
            loggerFactory: null,
            requestTimeout: TimeSpan.FromSeconds(60));

        await pool.StartAsync(CancellationToken.None);

        // Two long invokes in parallel, both cancelled. Must observe OCE
        // promptly on both. Slots should stay healthy (soft cancel — no kill
        // required since Start-Sleep is interruptable).
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();

        var t1 = pool.InvokeAsync(
            "Start-Sleep",
            new Dictionary<string, object?> { ["Seconds"] = 60 },
            cts1.Token);
        var t2 = pool.InvokeAsync(
            "Start-Sleep",
            new Dictionary<string, object?> { ["Seconds"] = 60 },
            cts2.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(750));
        var sw = Stopwatch.StartNew();
        cts1.Cancel();
        cts2.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await t1.WaitAsync(ObservationTimeout));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await t2.WaitAsync(ObservationTimeout));

        sw.Stop();
        Assert.True(sw.Elapsed < ObservationTimeout,
            $"ProcessPool cancellation took {sw.Elapsed.TotalSeconds:F1}s — exceeded budget.");

        // Brief settle for the pipelines to fully unwind on the host side and
        // for the slots to be returned to the pool. Then a follow-up invoke
        // on the pool must succeed promptly — proves slots are reusable.
        await Task.Delay(TimeSpan.FromSeconds(1));

        using var followUpCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var output = await pool.InvokeAsync(
            "Get-Date",
            new Dictionary<string, object?>(),
            followUpCts.Token);
        Assert.False(string.IsNullOrEmpty(output));
        Assert.True(pool.HealthyCount >= 1,
            $"Pool reported HealthyCount={pool.HealthyCount} after soft cancel; expected >= 1.");
    }

    private static bool TryResolvePwsh(out string pwshPath)
    {
        try
        {
            pwshPath = OutOfProcessCommandExecutor.ResolvePwshPath();
            return true;
        }
        catch (FileNotFoundException)
        {
            pwshPath = string.Empty;
            return false;
        }
    }

    private static async Task<string?> ResolveOopHostScriptForTestAsync(string scriptName)
    {
        var overridePath = Environment.GetEnvironmentVariable("POSHMCP_OOP_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            return overridePath;
        }

        var serverAssembly = typeof(OutOfProcessHost).Assembly;
        var resourceName = Array.Find(
            serverAssembly.GetManifestResourceNames(),
            name => name.EndsWith(scriptName, StringComparison.OrdinalIgnoreCase));

        if (resourceName is not null)
        {
            using var stream = serverAssembly.GetManifestResourceStream(resourceName);
            if (stream is not null)
            {
                var bytes = new byte[stream.Length];
                await stream.ReadExactlyAsync(bytes);
                var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                var dir = Path.Combine(Path.GetTempPath(), "poshmcp-tests");
                var path = Path.Combine(dir, scriptName);
                Directory.CreateDirectory(dir);
                if (!File.Exists(path) || ContentHash(path) != hash)
                {
                    await File.WriteAllBytesAsync(path, bytes);
                }
                return path;
            }
        }

        var basePath = Path.Combine(AppContext.BaseDirectory, "PowerShell", "OutOfProcess", scriptName);
        return File.Exists(basePath) ? basePath : null;
    }

    private static string ContentHash(string filePath)
    {
        using var fs = File.OpenRead(filePath);
        return Convert.ToHexStringLower(SHA256.HashData(fs));
    }
}
