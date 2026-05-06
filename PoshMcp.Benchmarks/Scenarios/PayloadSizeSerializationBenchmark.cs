using BenchmarkDotNet.Attributes;

namespace PoshMcp.Benchmarks.Scenarios;

/// <summary>
/// Heavy serialization scenario, parameterized by payload size to expose
/// Option B's IPC/serialization overhead and Option A's <c>ConvertTo-Json</c>
/// contention. Threshold from experiment plan §4: ≥ 2× baseline on this
/// class of workload.
///
/// Models the spec's <c>Get-Process | Select-Object -First {N}</c> shape.
/// STUB — #194 wires to ICommandExecutor.
/// </summary>
[MemoryDiagnoser]
[MinIterationCount(5)]
[MaxIterationCount(15)]
public class PayloadSizeSerializationBenchmark
{
    [Params(HostMode.Single, HostMode.Pool, HostMode.ProcessPool)]
    public HostMode Mode { get; set; }

    /// <summary>Number of objects to serialize per invoke (10, 100, 1000).</summary>
    [Params(10, 100, 1000)]
    public int PayloadCount { get; set; }

    [Benchmark(Baseline = true, Description = "Heavy serialization (Get-Process | Select -First N), 2× bar")]
    public int Serialize()
    {
        // STUB: returns the expected payload size as a placeholder.
        // #194 replaces with:
        //   var json = await executor.InvokeAsync("Get-Process",
        //       new() { ["First"] = PayloadCount });
        //   return json.Length;
        return PayloadCount;
    }
}
