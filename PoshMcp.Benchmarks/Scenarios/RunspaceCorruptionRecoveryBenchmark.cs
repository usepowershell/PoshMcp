using BenchmarkDotNet.Attributes;

namespace PoshMcp.Benchmarks.Scenarios;

/// <summary>
/// Runspace-corruption recovery: one invoke pollutes a type accelerator
/// or mutates a shared static; a parallel invoke verifies it is
/// unaffected. Per experiment plan §4 this is the real isolation gate
/// for Option A — process-kill is not the right discriminator because
/// in Option A it is the same gate as the baseline.
///
/// STUB — #194 wires this through the runspace-pool host (Option A,
/// issue #191).
/// </summary>
[MinIterationCount(3)]
[MaxIterationCount(10)]
public class RunspaceCorruptionRecoveryBenchmark
{
    [Params(HostMode.Single, HostMode.Pool, HostMode.ProcessPool)]
    public HostMode Mode { get; set; }

    [Benchmark(Baseline = true, Description = "Runspace corruption → parallel invoke unaffected (Option A isolation gate)")]
    public int CorruptionRecovery()
    {
        // STUB: placeholder. #194 replaces with:
        //   var corrupt = executor.InvokeAsync("Pollute-TypeAccelerator", ...);
        //   var probe   = executor.InvokeAsync("Verify-Clean", ...);
        //   await Task.WhenAll(corrupt, probe);
        //   return probe.Result.Contains("clean") ? 1 : 0;
        return 1;
    }
}
