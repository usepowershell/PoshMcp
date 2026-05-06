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
}
