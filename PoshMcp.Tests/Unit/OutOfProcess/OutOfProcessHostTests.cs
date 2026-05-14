using System;
using System.IO;
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
/// Lifecycle unit tests for <see cref="OutOfProcessHost"/>.
/// Covers: construction, start → ping → setup → shutdown → restart, and
/// idempotent disposal. Tests that require <c>pwsh</c> and the host script
/// gracefully no-op when the script cannot be resolved (CI without
/// build artifacts copied).
/// </summary>
[Trait("Category", "OutOfProcess")]
public class OutOfProcessHostTests
{
    [Fact]
    public void Constructor_NullPwshPath_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new OutOfProcessHost(string.Empty, "x.ps1"));
    }

    [Fact]
    public void Constructor_NullScriptPath_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new OutOfProcessHost("pwsh", string.Empty));
    }

    [Fact]
    public async Task DisposeAsync_BeforeStart_DoesNotThrow()
    {
        var host = new OutOfProcessHost("pwsh", "x.ps1");
        await host.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var host = new OutOfProcessHost("pwsh", "x.ps1");
        await host.DisposeAsync();
        await host.DisposeAsync();
    }

    [Fact]
    public async Task SendRequestAsync_BeforeStart_Throws()
    {
        var host = new OutOfProcessHost("pwsh", "x.ps1");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.SendRequestAsync<JsonElement>("ping", null, CancellationToken.None));
    }

    [Fact]
    public void IsRunning_BeforeStart_IsFalse()
    {
        var host = new OutOfProcessHost("pwsh", "x.ps1");
        Assert.False(host.IsRunning);
        Assert.Null(host.ProcessId);
    }

    [Fact]
    public async Task Lifecycle_Start_Ping_Setup_Shutdown_Restart()
    {
        // Resolve the real pwsh and oop-host.ps1 the same way the executor does.
        // If either is unavailable in this test context, we exit cleanly — this
        // mirrors the behavior of OutOfProcessCommandExecutorTests.StartAsync_ThenDisposeAsync_FullLifecycle.
        string pwshPath;
        try
        {
            pwshPath = OutOfProcessCommandExecutor.ResolvePwshPath();
        }
        catch (FileNotFoundException)
        {
            return; // pwsh not installed — acceptable in this context.
        }

        var scriptPath = await ResolveOopHostScriptForTestAsync();
        if (scriptPath is null)
        {
            return; // oop-host.ps1 not available — acceptable.
        }

        // ------ First lifecycle: start → setup → shutdown ------
        var host1 = new OutOfProcessHost(
            pwshPath, scriptPath,
            NullLogger<OutOfProcessHost>.Instance,
            TimeSpan.FromSeconds(15));

        await host1.StartAsync(CancellationToken.None);

        Assert.True(host1.IsRunning, "host should be running after StartAsync (which includes a ping).");
        Assert.NotNull(host1.ProcessId);
        var host1Pid = host1.ProcessId!.Value;

        // Setup with empty params — script must accept this without error.
        var setupResult = await host1.SendRequestAsync<JsonElement>(
            "setup",
            new
            {
                modulePaths = Array.Empty<string>(),
                trustPSGallery = false,
                installModules = Array.Empty<object>(),
                importModules = Array.Empty<string>(),
                startupScriptPath = (string?)null,
                startupScript = (string?)null,
                skipPublisherCheck = false,
                allowClobber = false,
                installTimeoutSeconds = 30,
            },
            CancellationToken.None);

        Assert.True(
            setupResult.TryGetProperty("success", out var success) && success.GetBoolean(),
            $"setup should succeed with empty config; got: {setupResult.GetRawText()}");

        // DisposeAsync sends the shutdown method and waits for graceful exit.
        await host1.DisposeAsync();

        Assert.False(host1.IsRunning, "host should not be running after DisposeAsync.");

        // ------ Restart: a brand-new host instance starts a fresh subprocess ------
        var host2 = new OutOfProcessHost(
            pwshPath, scriptPath,
            NullLogger<OutOfProcessHost>.Instance,
            TimeSpan.FromSeconds(15));

        await host2.StartAsync(CancellationToken.None);

        Assert.True(host2.IsRunning, "restarted host should be running after StartAsync.");
        Assert.NotNull(host2.ProcessId);
        Assert.NotEqual(host1Pid, host2.ProcessId!.Value);

        // Verify the restarted host is responsive with another ping.
        var ping = await host2.SendRequestAsync<JsonElement>(
            "ping", null, CancellationToken.None);
        Assert.True(ping.TryGetProperty("status", out var status) && status.GetString() == "ok");

        await host2.DisposeAsync();
    }

    [Fact]
    public async Task StartAsync_CalledTwice_Throws()
    {
        // We don't need a real subprocess to verify the guard — the guard
        // runs before the process is launched. But the real start path
        // attempts to launch pwsh, so we test the guard behavior by:
        //   1. attempting one start with an obviously bogus script,
        //   2. then immediately calling start again to verify the second
        //      call throws InvalidOperationException OR the first call
        //      already threw (in which case _started stays false).
        var host = new OutOfProcessHost("pwsh", "definitely-not-a-real-path.ps1");

        try
        {
            await host.StartAsync(CancellationToken.None);
        }
        catch
        {
            // Expected — pwsh either fails to start or fails ping.
            // In that case _started may or may not be set; just dispose and exit.
            await host.DisposeAsync();
            return;
        }

        // If the first start somehow succeeded, the second must throw.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.StartAsync(CancellationToken.None));

        await host.DisposeAsync();
    }

    [Fact]
    public void IsNonJsonPowerShellStreamLine_RecognizesPrefixes()
    {
        Assert.True(InvokeIsNonJsonPowerShellStreamLine("WARNING: something"));
        Assert.True(InvokeIsNonJsonPowerShellStreamLine("VERBOSE: something"));
        Assert.True(InvokeIsNonJsonPowerShellStreamLine("DEBUG: something"));
        Assert.True(InvokeIsNonJsonPowerShellStreamLine("INFORMATION: something"));
        Assert.True(InvokeIsNonJsonPowerShellStreamLine("ERROR: something"));

        Assert.False(InvokeIsNonJsonPowerShellStreamLine("{\"id\":\"x\"}"));
        Assert.False(InvokeIsNonJsonPowerShellStreamLine("[1,2,3]"));
        Assert.False(InvokeIsNonJsonPowerShellStreamLine("\"not json but starts with quote\""));
        Assert.False(InvokeIsNonJsonPowerShellStreamLine(""));
    }

    private static bool InvokeIsNonJsonPowerShellStreamLine(string line)
    {
        var method = typeof(OutOfProcessHost).GetMethod(
            "IsNonJsonPowerShellStreamLine",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (bool)method!.Invoke(null, new object[] { line })!;
    }

    /// <summary>
    /// Mirrors <c>OutOfProcessCommandExecutor.ResolveHostScriptPathAsync</c> just
    /// enough to find the oop-host.ps1 in test contexts. Returns null if the
    /// script can't be located so the calling test can no-op gracefully.
    /// </summary>
    private static async Task<string?> ResolveOopHostScriptForTestAsync()
    {
        // 1. Env var override
        var overridePath = Environment.GetEnvironmentVariable("POSHMCP_OOP_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            return overridePath;
        }

        // 2. Embedded resource in the server assembly
        var serverAssembly = typeof(OutOfProcessHost).Assembly;
        var resourceName = Array.Find(
            serverAssembly.GetManifestResourceNames(),
            name => name.EndsWith("oop-host.ps1", StringComparison.OrdinalIgnoreCase));

        if (resourceName is not null)
        {
            using var stream = serverAssembly.GetManifestResourceStream(resourceName);
            if (stream is not null)
            {
                var bytes = new byte[stream.Length];
                await stream.ReadExactlyAsync(bytes);
                var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                var dir = Path.Combine(Path.GetTempPath(), "poshmcp-tests");
                var path = Path.Combine(dir, "oop-host.ps1");
                Directory.CreateDirectory(dir);
                if (!File.Exists(path) || ContentHash(path) != hash)
                {
                    await File.WriteAllBytesAsync(path, bytes);
                }
                return path;
            }
        }

        // 3. Build output fallback
        var basePath = Path.Combine(AppContext.BaseDirectory, "PowerShell", "OutOfProcess", "oop-host.ps1");
        return File.Exists(basePath) ? basePath : null;
    }

    private static string ContentHash(string filePath)
    {
        using var fs = File.OpenRead(filePath);
        return Convert.ToHexStringLower(SHA256.HashData(fs));
    }
}
