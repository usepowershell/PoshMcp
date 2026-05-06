using BenchmarkDotNet.Attributes;

namespace PoshMcp.Benchmarks.Scenarios;

/// <summary>
/// Warm-invoke throughput: sustained throughput once the executor is hot.
/// Threshold from experiment plan §4: I/O-bound network-shaped workload
/// must reach ≥ 4× baseline at 10 concurrent clients on Options A and B.
///
/// STUB — uses <see cref="HttpListenerTestServer"/> for the network shape.
/// Real invocation through ICommandExecutor lands in issue #194.
/// </summary>
[MemoryDiagnoser]
[MinIterationCount(5)]
[MaxIterationCount(20)]
public class WarmInvokeThroughputBenchmark
{
    private HttpListenerTestServer? _server;

    [Params(HostMode.Single, HostMode.Pool, HostMode.ProcessPool)]
    public HostMode Mode { get; set; }

    /// <summary>
    /// Concurrent client count — the parallelism axis the experiment plan
    /// calls out (10, 50, 100). Default to 10 for the threshold gate; #194
    /// can extend the params list once real executors are wired.
    /// </summary>
    [Params(10)]
    public int Concurrency { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _server = new HttpListenerTestServer
        {
            ResponseDelay = TimeSpan.FromMilliseconds(500),
        };
        _server.Start();
    }

    [GlobalCleanup]
    public void Cleanup() => _server?.Dispose();

    [Benchmark(Baseline = true, Description = "Warm invoke @ N concurrency (network-shaped, 4× bar)")]
    public int WarmInvoke()
    {
        // STUB: a noop returning the URL hash. #194 replaces with:
        //   await Parallel.ForEachAsync(Enumerable.Range(0, Concurrency), async (_, _) => {
        //       await executor.InvokeAsync("Invoke-WebRequest", new() { ["Uri"] = _server!.Url });
        //   });
        return _server?.Url.Length ?? 0;
    }
}
