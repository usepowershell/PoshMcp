using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace PoshMcp.Benchmarks.Scenarios;

/// <summary>
/// Warm-invoke throughput: sustained throughput once the executor is hot.
/// Threshold from experiment plan §4: I/O-bound network-shaped workload
/// must reach ≥ 4× baseline at 10 concurrent clients on Options A and B.
///
/// Each iteration fires N parallel <c>Invoke-WebRequest</c> calls against
/// the in-process <see cref="HttpListenerTestServer"/>. Single-runspace
/// executors serialize them; Pool / ProcessPool overlap them and should
/// finish in roughly <c>per-request-latency</c> time.
/// </summary>
[MemoryDiagnoser]
[MinIterationCount(5)]
[MaxIterationCount(20)]
public class WarmInvokeThroughputBenchmark
{
    private HttpListenerTestServer? _server;
    private BenchExecutor? _bench;

    [Params(HostMode.Single, HostMode.Pool, HostMode.ProcessPool)]
    public HostMode Mode { get; set; }

    /// <summary>
    /// Concurrent invoke count per iteration — the parallelism axis the
    /// experiment plan calls out. 10 is the threshold gate; smaller values
    /// keep total run time sane while still showing the parallelism win.
    /// </summary>
    [Params(10)]
    public int Concurrency { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _server = new HttpListenerTestServer
        {
            // Short delay so each request takes ~50 ms — enough to expose
            // serialization in Single mode without making the bench drag.
            ResponseDelay = System.TimeSpan.FromMilliseconds(50),
        };
        _server.Start();

        _bench = await ExecutorFactory.CreateAsync(Mode);

        // Warm-up invoke so the executor is past first-request cost.
        _ = await _bench.Executor.InvokeAsync(
            "Get-Date", new Dictionary<string, object?>());
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        if (_bench is not null) await _bench.DisposeAsync();
        _server?.Dispose();
    }

    [Benchmark(Description = "Warm invoke @ N concurrency (network-shaped, 4× bar)")]
    public async Task WarmInvoke()
    {
        var url = _server!.Url;
        var tasks = Enumerable.Range(0, Concurrency).Select(_ =>
            _bench!.Executor.InvokeAsync(
                "Invoke-WebRequest",
                new Dictionary<string, object?>
                {
                    ["Uri"] = url,
                    ["UseBasicParsing"] = true,
                })).ToArray();
        await Task.WhenAll(tasks);
    }
}
