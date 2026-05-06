using BenchmarkDotNet.Attributes;

namespace PoshMcp.Benchmarks.Scenarios;

/// <summary>
/// Process-crash recovery: wall-clock from induced
/// <c>[System.Environment]::Exit(1)</c> in one host to next successful
/// invoke. This scenario gates Option B per experiment plan §4:
/// "Option B passes isolation if process crash of one host produces no
/// server-visible error on requests routed elsewhere within 100 ms of
/// the crash."
///
/// STUB — meaningful comparison requires the Option B pool implementation
/// from issue #192, wired in issue #194. For Option A and Single this
/// scenario degenerates into "restart the only process," which is the
/// same as the baseline.
/// </summary>
[MinIterationCount(3)]
[MaxIterationCount(10)]
public class ProcessCrashRecoveryBenchmark
{
    [Params(HostMode.Single, HostMode.Pool, HostMode.ProcessPool)]
    public HostMode Mode { get; set; }

    [Benchmark(Baseline = true, Description = "Process crash → next invoke (Option B isolation gate)")]
    public int CrashRecovery()
    {
        // STUB: placeholder. #194 replaces with:
        //   await executor.InvokeAsync("ForceCrash", ...);  // exits one host
        //   var sw = Stopwatch.StartNew();
        //   await executor.InvokeAsync("Get-Date", ...);    // any other host serves it
        //   return (int)sw.ElapsedMilliseconds;             // gate: < 100ms for Option B
        return 0;
    }
}
