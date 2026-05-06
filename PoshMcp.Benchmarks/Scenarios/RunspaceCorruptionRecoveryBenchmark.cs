using System.Collections.Generic;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace PoshMcp.Benchmarks.Scenarios;

/// <summary>
/// Runspace-isolation / head-of-line-blocking probe. Per experiment plan §4
/// this is the real isolation gate for Option A — process-kill is not the
/// right discriminator because in Option A it is the same gate as the
/// baseline. The metric is: while one runspace is "stuck" on a slow command,
/// how long until a fast probe completes?
///
/// <para>
/// Implementation: each iteration fires a long-running invoke
/// (<c>Start-Sleep -Milliseconds 1000</c>) and does NOT await it, then
/// immediately awaits a fast probe (<c>Get-Date</c>). The Mean column IS
/// the recovery / probe-latency metric.
/// </para>
/// <para>
/// Per-mode behaviour:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <see cref="HostMode.Single"/>: a single runspace per host, requests
///     are serialized. The probe queues behind the 1 s sleeper. Mean ≈ 1 s.
///   </description></item>
///   <item><description>
///     <see cref="HostMode.Pool"/> (Option A): N runspaces inside one
///     subprocess. The probe should land on a different runspace and
///     complete quickly. This is the gate Option A passes if probe latency
///     stays ≪ 1 s.
///   </description></item>
///   <item><description>
///     <see cref="HostMode.ProcessPool"/> (Option B): N independent hosts.
///     The probe lands on a different host and completes quickly. Should
///     match or beat <see cref="HostMode.Pool"/>.
///   </description></item>
/// </list>
/// <para>
/// The blocking sleeper completes on its own well within the executor's
/// 30 s default request timeout, so nothing leaks between iterations.
/// </para>
/// </summary>
[MinIterationCount(3)]
[MaxIterationCount(10)]
public class RunspaceCorruptionRecoveryBenchmark
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

    [Benchmark(Description = "Slow invoke in flight → fast probe latency (Option A isolation gate)")]
    [InvocationCount(1)]
    public async Task<int> ProbeUnderHeadOfLine()
    {
        // Fire the slow command and do NOT await — we want it in flight when
        // the probe goes out.
        var slow = _bench!.Executor.InvokeAsync(
            "Start-Sleep",
            new Dictionary<string, object?> { ["Milliseconds"] = 1000 });

        // Probe — what we actually measure.
        var probe = await _bench.Executor.InvokeAsync(
            "Get-Date", new Dictionary<string, object?>());

        // Drain the slow request so it doesn't bleed into the next iteration.
        try { await slow; } catch { /* iteration boundary; best-effort */ }

        return probe.Length;
    }
}
