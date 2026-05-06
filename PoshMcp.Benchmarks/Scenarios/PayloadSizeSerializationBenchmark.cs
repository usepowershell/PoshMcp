using System.Collections.Generic;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace PoshMcp.Benchmarks.Scenarios;

/// <summary>
/// Heavy serialization scenario, parameterized by payload size to expose
/// Option B's IPC/serialization overhead and Option A's <c>ConvertTo-Json</c>
/// contention. Threshold from experiment plan §4: ≥ 2× baseline on this
/// class of workload (under concurrency).
///
/// <para>
/// Implementation: invoke <c>Write-Output -InputObject &lt;string of size N&gt;</c>.
/// The string crosses the wire twice — once C#→pwsh as the request payload,
/// once pwsh→C# as the JSON-serialized result — so the metric reflects both
/// directions of serialization at the named <see cref="PayloadBytes"/> size.
/// </para>
/// <para>
/// Sizes are chosen to span small (1 KB) through medium (256 KB) to the
/// "large result" boundary called out in spec-005 (1 MB).
/// </para>
/// </summary>
[MemoryDiagnoser]
[MinIterationCount(5)]
[MaxIterationCount(15)]
public class PayloadSizeSerializationBenchmark
{
    private BenchExecutor? _bench;
    private string _payload = string.Empty;

    [Params(HostMode.Single, HostMode.Pool, HostMode.ProcessPool)]
    public HostMode Mode { get; set; }

    /// <summary>
    /// Payload size in bytes. 1 KB / 16 KB / 256 KB / 1 MB.
    /// </summary>
    [Params(1024, 16 * 1024, 256 * 1024, 1024 * 1024)]
    public int PayloadBytes { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        // ASCII filler so JSON serialization cost scales with the size param,
        // not with character escapes.
        _payload = new string('X', PayloadBytes);

        _bench = await ExecutorFactory.CreateAsync(
            Mode,
            // Larger payloads can take noticeably longer than 30 s on a cold
            // ProcessPool; bump the per-request timeout to keep iterations
            // from spuriously failing.
            requestTimeout: System.TimeSpan.FromSeconds(60));

        // Warm-up.
        _ = await _bench.Executor.InvokeAsync(
            "Get-Date", new Dictionary<string, object?>());
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        if (_bench is not null) await _bench.DisposeAsync();
    }

    [Benchmark(Description = "Round-trip a string of size PayloadBytes through Write-Output")]
    public async Task<int> Serialize()
    {
        var json = await _bench!.Executor.InvokeAsync(
            "Write-Output",
            new Dictionary<string, object?> { ["InputObject"] = _payload });
        return json.Length;
    }
}
