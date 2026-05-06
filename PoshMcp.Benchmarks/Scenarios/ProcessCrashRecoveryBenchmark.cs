using System.Collections.Generic;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace PoshMcp.Benchmarks.Scenarios;

/// <summary>
/// Process-crash recovery: wall-clock from killing one underlying pwsh
/// subprocess to the next successful invoke. This scenario gates Option B
/// per experiment plan §4: "Option B passes isolation if process crash of
/// one host produces no server-visible error on requests routed elsewhere
/// within 100 ms of the crash."
///
/// <para>
/// Each iteration kills exactly one host and then runs an invoke; the Mean
/// column IS the per-mode recovery time (and the column the AC's
/// "crash-recovery time" refers to for this scenario).
/// </para>
/// <para>
/// Per-mode behaviour:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <see cref="HostMode.ProcessPool"/>: kills 1 of N hosts. The pool's
///     channel reader skips the stale slot; another healthy host serves the
///     probe. The reconciler replaces the dead slot in the background.
///     This is the configuration the gate applies to (target &lt; 100 ms).
///   </description></item>
///   <item><description>
///     <see cref="HostMode.Single"/> and <see cref="HostMode.Pool"/>: there
///     is only one subprocess; killing it leaves the executor unrecoverable
///     in-place. To still produce a number, the iteration disposes the dead
///     executor and constructs a fresh one before the probe. The Mean for
///     these rows therefore reports cold-start cost — which is the correct
///     answer to "how long until the next request succeeds" for those modes.
///   </description></item>
/// </list>
/// </summary>
[MinIterationCount(3)]
[MaxIterationCount(10)]
public class ProcessCrashRecoveryBenchmark
{
    private BenchExecutor? _bench;

    [Params(HostMode.Single, HostMode.Pool, HostMode.ProcessPool)]
    public HostMode Mode { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _bench = await ExecutorFactory.CreateAsync(Mode);
        _ = await _bench.Executor.InvokeAsync(
            "Get-Date", new Dictionary<string, object?>());
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        if (_bench is not null) await _bench.DisposeAsync();
    }

    [Benchmark(Description = "Kill one host → next invoke succeeds")]
    [InvocationCount(1)]
    public async Task<int> CrashRecovery()
    {
        // Kill one underlying pwsh process.
        _bench!.KillOneHost();

        if (Mode == HostMode.ProcessPool)
        {
            // Pool routes around the dead slot — a single invoke should land
            // on a different healthy host. The bench Mean therefore reflects
            // the lease-loop's skip-stale cost + one round-trip.
            var json = await _bench.Executor.InvokeAsync(
                "Get-Date", new Dictionary<string, object?>());
            return json.Length;
        }

        // Single / Pool: only one subprocess existed; it's dead. The executor
        // does not auto-restart, so producing a number means tearing down and
        // reconstructing. This degrades gracefully into the cold-start cost,
        // which IS the answer to "time until next successful request" for
        // these modes. Documented in PoshMcp.Benchmarks/README.md.
        await _bench.DisposeAsync();
        _bench = await ExecutorFactory.CreateAsync(Mode);
        var freshJson = await _bench.Executor.InvokeAsync(
            "Get-Date", new Dictionary<string, object?>());
        return freshJson.Length;
    }
}
