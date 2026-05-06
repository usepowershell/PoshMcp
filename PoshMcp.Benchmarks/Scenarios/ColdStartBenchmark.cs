using BenchmarkDotNet.Attributes;

namespace PoshMcp.Benchmarks.Scenarios;

/// <summary>
/// Cold-start scenario: wall-clock from executor construction → first
/// successful invoke. Favors Option A (one process started once); is the
/// primary cost Option B has to amortize.
///
/// Spec reference: experiment plan §4 "Cold-start cost" row.
///
/// STUB — wiring to a real <see cref="PoshMcp.Server.PowerShell.OutOfProcess.ICommandExecutor"/>
/// happens in issue #194. The body is shaped so BenchmarkDotNet can
/// discover and enumerate the scenario today.
/// </summary>
[MemoryDiagnoser]
[MinIterationCount(3)]
[MaxIterationCount(10)]
public class ColdStartBenchmark
{
    [Params(HostMode.Single, HostMode.Pool, HostMode.ProcessPool)]
    public HostMode Mode { get; set; }

    [Benchmark(Baseline = true, Description = "Cold start: ctor → first invoke")]
    public int ColdStart()
    {
        // STUB: returns a placeholder value. #194 will replace this with:
        //   var executor = ExecutorFactory.Create(Mode);
        //   await executor.StartAsync();
        //   await executor.SetupAsync(...);
        //   var result = await executor.InvokeAsync("Get-Date", ...);
        //   await executor.DisposeAsync();
        return (int)Mode;
    }
}
