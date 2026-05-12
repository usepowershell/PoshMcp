using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.OutOfProcess;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Integration;

/// <summary>
/// Integration tests that exercise the runspace-pool OOP host
/// (<c>oop-host-pool.ps1</c>) selected via
/// <see cref="SubprocessHostMode.Pool"/>. Validates pool startup,
/// concurrent invoke behavior, and the quiesce protocol triggered by
/// <c>SetupAsync</c>.
/// </summary>
[Trait("Category", "OutOfProcess")]
[Trait("HostMode", "Pool")]
public class OutOfProcessPoolHostIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public OutOfProcessPoolHostIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private ILoggerFactory CreateLoggerFactory() => LoggerFactory.Create(b =>
    {
        b.AddProvider(new TestOutputLoggerProvider(_output));
        b.SetMinimumLevel(LogLevel.Debug);
    });

    [PwshAvailableFact]
    public async Task PoolHost_StartsAndDiscovers()
    {
        using var factory = CreateLoggerFactory();
        await using var executor = new OutOfProcessCommandExecutor(
            factory.CreateLogger<OutOfProcessCommandExecutor>(),
            requestTimeout: TimeSpan.FromSeconds(30),
            hostMode: SubprocessHostMode.Pool,
            runspacePoolSize: 3);

        await executor.StartAsync();

        Assert.Equal(SubprocessHostMode.Pool, executor.HostMode);

        var config = new PowerShellConfiguration
        {
            FunctionNames = new List<string> { "Get-Date" },
            Modules = new List<string>(),
            IncludePatterns = new List<string>(),
            ExcludePatterns = new List<string>()
        };

        var schemas = await executor.DiscoverCommandsAsync(config);
        Assert.NotNull(schemas);
        Assert.Contains(schemas, s => s.Name == "Get-Date");
    }

    [PwshAvailableFact]
    public async Task PoolHost_ConcurrentInvokesRunInParallel()
    {
        using var factory = CreateLoggerFactory();
        await using var executor = new OutOfProcessCommandExecutor(
            factory.CreateLogger<OutOfProcessCommandExecutor>(),
            requestTimeout: TimeSpan.FromSeconds(30),
            hostMode: SubprocessHostMode.Pool,
            runspacePoolSize: 4);

        await executor.StartAsync();

        const int invokeCount = 4;
        const int sleepMs = 500;

        var sw = Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, invokeCount).Select(_ =>
            executor.InvokeAsync(
                "Start-Sleep",
                new Dictionary<string, object?> { ["Milliseconds"] = sleepMs })).ToArray();

        var results = await Task.WhenAll(tasks);
        sw.Stop();

        Assert.Equal(invokeCount, results.Length);
        Assert.All(results, r => Assert.NotNull(r));

        // Serial execution would take >= invokeCount * sleepMs (= 2000 ms).
        // Parallel pool of 4 should complete in roughly one slot (~ sleepMs)
        // plus startup/dispatch overhead. We assert well under serial time
        // to guard against accidental serialization without being flaky.
        var serialMs = invokeCount * sleepMs;
        Assert.True(
            sw.ElapsedMilliseconds < serialMs * 0.75,
            $"Pool host appears to be serializing invokes: elapsed={sw.ElapsedMilliseconds}ms, serial baseline={serialMs}ms");

        _output.WriteLine($"Pool concurrent elapsed: {sw.ElapsedMilliseconds}ms vs serial baseline {serialMs}ms");
    }

    [PwshAvailableFact]
    public async Task PoolHost_SetupQuiesceCompletesAfterInvokes()
    {
        using var factory = CreateLoggerFactory();
        await using var executor = new OutOfProcessCommandExecutor(
            factory.CreateLogger<OutOfProcessCommandExecutor>(),
            requestTimeout: TimeSpan.FromSeconds(30),
            hostMode: SubprocessHostMode.Pool,
            runspacePoolSize: 2);

        await executor.StartAsync();

        // 1. Run a batch of concurrent invokes so the pool has live runspaces.
        var firstBatch = Enumerable.Range(0, 4).Select(_ =>
            executor.InvokeAsync(
                "Start-Sleep",
                new Dictionary<string, object?> { ["Milliseconds"] = 100 })).ToArray();
        await Task.WhenAll(firstBatch);

        // 2. Trigger setup. The pool host quiesces (drains in-flight invokes),
        //    rebuilds InitialSessionState, reopens the pool, and resumes.
        var envConfig = new EnvironmentConfiguration
        {
            ImportModules = new List<string> { "Microsoft.PowerShell.Utility" },
            TrustPSGallery = false
        };
        await executor.SetupAsync(
            envConfig,
            configFilePath: null,
            setupRequestTimeout: TimeSpan.FromSeconds(60),
            discoveryModules: Array.Empty<string>());

        // 3. After quiesce + reopen, the pool must accept new invokes
        //    and still execute them concurrently.
        var postSetup = Enumerable.Range(0, 4).Select(_ =>
            executor.InvokeAsync(
                "Get-Date",
                new Dictionary<string, object?>())).ToArray();
        var postResults = await Task.WhenAll(postSetup);

        Assert.Equal(4, postResults.Length);
        Assert.All(postResults, r => Assert.False(string.IsNullOrWhiteSpace(r)));
    }

    [PwshAvailableFact]
    public async Task PoolHost_StreamIsolation_WriteHostDoesNotPolluteStdout()
    {
        // The pool host installs a custom PSHostUserInterface that routes
        // Write-Host / Write-Warning / progress to stderr. If $Host.UI ever
        // routed to stdout, it would corrupt the ndjson protocol and the
        // following invokes would either fail or return malformed JSON.
        using var factory = CreateLoggerFactory();
        await using var executor = new OutOfProcessCommandExecutor(
            factory.CreateLogger<OutOfProcessCommandExecutor>(),
            requestTimeout: TimeSpan.FromSeconds(30),
            hostMode: SubprocessHostMode.Pool,
            runspacePoolSize: 2);

        await executor.StartAsync();

        var noisy = await executor.InvokeAsync(
            "Write-Host",
            new Dictionary<string, object?> { ["Object"] = "stream-isolation-test" });

        // Write-Host returns nothing on the success stream; the JSON-serialized
        // result should still parse cleanly (null or empty JSON value).
        Assert.NotNull(noisy);

        // Subsequent invocations must continue working — proves the wire
        // protocol stayed intact.
        var followup = await executor.InvokeAsync(
            "Get-Date",
            new Dictionary<string, object?>());
        Assert.False(string.IsNullOrWhiteSpace(followup));
    }

    /// <summary>
    /// Faithful repro for the user-reported "previous tool's response came back"
    /// scenario, pool variant. Two DIFFERENT commands invoked back-to-back on
    /// the same pool executor: A returns a marker; B returns something else.
    /// Asserts B's payload contains no trace of A's marker.
    /// </summary>
    [PwshAvailableFact]
    public async Task PoolHost_TwoDifferentSuccessfulCommands_SecondDoesNotReturnFirstOutput()
    {
        using var factory = CreateLoggerFactory();
        await using var executor = new OutOfProcessCommandExecutor(
            factory.CreateLogger<OutOfProcessCommandExecutor>(),
            requestTimeout: TimeSpan.FromSeconds(30),
            hostMode: SubprocessHostMode.Pool,
            runspacePoolSize: 2);

        await executor.StartAsync();

        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "poshmcp-pool-stale-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempDir);
        try
        {
            var markerA = "poshmcp-pool-a-" + Guid.NewGuid().ToString("N");
            var markerDir = System.IO.Path.Combine(tempDir, markerA);
            System.IO.Directory.CreateDirectory(markerDir);

            var first = await executor.InvokeAsync(
                "Get-Item",
                new Dictionary<string, object?> { ["Path"] = markerDir });
            Assert.Contains(markerA, first, StringComparison.Ordinal);

            // Run B many times on the pool so we hit every runspace at least once.
            // Pool size 2 + 6 invokes guarantees coverage of both runspaces and
            // exercises the runspace reuse path that would expose stale state.
            for (var i = 0; i < 6; i++)
            {
                var next = await executor.InvokeAsync(
                    "Get-Date",
                    new Dictionary<string, object?>());

                Assert.False(string.IsNullOrWhiteSpace(next),
                    $"Pool invoke #{i} must produce output.");
                Assert.DoesNotContain(markerA, next, StringComparison.Ordinal);
            }
        }
        finally
        {
            try { System.IO.Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Pool variant of the error-after-success scenario. A succeeds with a marker,
    /// then a DIFFERENT command (Get-ChildItem on a missing path) triggers a
    /// non-terminating error. The thrown InvalidOperationException must not
    /// carry A's marker in its message.
    /// </summary>
    [PwshAvailableFact]
    public async Task PoolHost_ErrorInDifferentCommandAfterSuccess_DoesNotReturnFirstOutput()
    {
        using var factory = CreateLoggerFactory();
        await using var executor = new OutOfProcessCommandExecutor(
            factory.CreateLogger<OutOfProcessCommandExecutor>(),
            requestTimeout: TimeSpan.FromSeconds(30),
            hostMode: SubprocessHostMode.Pool,
            runspacePoolSize: 2);

        await executor.StartAsync();

        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "poshmcp-pool-err-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempDir);
        try
        {
            var markerA = "poshmcp-pool-x-" + Guid.NewGuid().ToString("N");
            var markerDir = System.IO.Path.Combine(tempDir, markerA);
            System.IO.Directory.CreateDirectory(markerDir);

            var first = await executor.InvokeAsync(
                "Get-Item",
                new Dictionary<string, object?> { ["Path"] = markerDir });
            Assert.Contains(markerA, first, StringComparison.Ordinal);

            var missingPath = System.IO.Path.Combine(tempDir,
                "missing-b-" + Guid.NewGuid().ToString("N"));
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await executor.InvokeAsync(
                    "Get-ChildItem",
                    new Dictionary<string, object?>
                    {
                        ["Path"] = missingPath,
                        ["ErrorAction"] = "Continue"
                    });
            });

            Assert.DoesNotContain(markerA, ex.Message, StringComparison.Ordinal);
            Assert.Contains("Get-ChildItem", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { System.IO.Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Exact reproduction of the user-reported cross-invocation state leak,
    /// pool variant: an empty-returning command run first, then a sentinel-
    /// producing command, then the empty command RE-RUN. The rerun must not
    /// contain the sentinel from the intermediate invoke.
    /// </summary>
    [PwshAvailableFact]
    public async Task PoolHost_EmptyCommand_AfterPriorOutput_DoesNotReturnPriorOutput()
    {
        using var factory = CreateLoggerFactory();
        await using var executor = new OutOfProcessCommandExecutor(
            factory.CreateLogger<OutOfProcessCommandExecutor>(),
            requestTimeout: TimeSpan.FromSeconds(30),
            hostMode: SubprocessHostMode.Pool,
            runspacePoolSize: 2);

        await executor.StartAsync();

        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "poshmcp-pool-empty-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempDir);
        try
        {
            var emptyParams = new Dictionary<string, object?>
            {
                ["Message"] = "poshmcp-test-verbose-message"
            };

            var resultA = await executor.InvokeAsync("Write-Verbose", emptyParams);

            var sentinel = "POOL-SENTINEL-" + Guid.NewGuid().ToString("N");
            var sentinelDir = System.IO.Path.Combine(tempDir, sentinel);
            System.IO.Directory.CreateDirectory(sentinelDir);

            // Run the sentinel-producing command MANY times to hit every
            // runspace in the pool at least once.
            for (var i = 0; i < 6; i++)
            {
                var resultB = await executor.InvokeAsync(
                    "Get-Item",
                    new Dictionary<string, object?> { ["Path"] = sentinelDir });
                Assert.Contains(sentinel, resultB, StringComparison.Ordinal);
            }

            // Rerun the empty command multiple times to maximize the chance
            // it lands on a runspace that previously ran the sentinel command.
            for (var i = 0; i < 6; i++)
            {
                var resultC = await executor.InvokeAsync("Write-Verbose", emptyParams);
                Assert.DoesNotContain(sentinel, resultC ?? string.Empty, StringComparison.Ordinal);
                Assert.Equal(resultA ?? string.Empty, resultC ?? string.Empty);
            }
        }
        finally
        {
            try { System.IO.Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
