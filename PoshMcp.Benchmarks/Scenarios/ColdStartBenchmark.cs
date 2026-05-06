using System.Collections.Generic;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace PoshMcp.Benchmarks.Scenarios;

/// <summary>
/// Cold-start scenario: wall-clock from executor construction → first
/// successful invoke → dispose. Favors Option A (one process started once);
/// is the primary cost Option B has to amortize.
///
/// Spec reference: experiment plan §4 "Cold-start cost" row.
///
/// Each iteration constructs a fresh executor, starts it, runs one
/// <c>Get-Date</c> invoke, and disposes. <c>[InvocationCount(1)] +
/// [UnrollFactor(1)]</c> ensures one cold-start per iteration so the Mean
/// column reports per-cold-start cost (not amortized). <c>SetupAsync</c> is
/// intentionally skipped — built-in cmdlets are callable without environment
/// customization, and the bench measures executor cost (process launch +
/// runspace open + first round-trip) rather than module install time.
/// </summary>
[MinIterationCount(3)]
[MaxIterationCount(10)]
public class ColdStartBenchmark
{
    [Params(HostMode.Single, HostMode.Pool, HostMode.ProcessPool)]
    public HostMode Mode { get; set; }

    [Benchmark(Description = "Cold start: ctor → start → first invoke → dispose")]
    [InvocationCount(1)]
    public async Task ColdStart()
    {
        await using var bench = await ExecutorFactory.CreateAsync(Mode);
        _ = await bench.Executor.InvokeAsync(
            "Get-Date",
            new Dictionary<string, object?>());
    }
}
