using BenchmarkDotNet.Attributes;

namespace PoshMcp.Benchmarks.Scenarios;

/// <summary>
/// Measures the HTTP MCP session path with the same configured module import and
/// startup script used for every benchmark server process.
/// </summary>
[MinIterationCount(3)]
[MaxIterationCount(10)]
public class HttpSessionBenchmark
{
    public const int DefaultSessionRunspaceCapacity = 4;

    private BenchmarkHttpServer? _server;
    private BenchmarkMcpClient? _warmClient;
    private List<BenchmarkMcpClient>? _throughputClients;
    private List<BenchmarkMcpClient>? _capacityClients;
    private BenchmarkMcpClient? _overflowClient;

    [Params(DefaultSessionRunspaceCapacity)]
    public int SessionRunspaceCapacity { get; set; }

    [GlobalSetup(Target = nameof(WarmSessionToolLatency))]
    public async Task StartWarmSessionAsync()
    {
        _server = await BenchmarkHttpServer.StartAsync(SessionRunspaceCapacity).ConfigureAwait(false);
        _warmClient = _server!.CreateClient();
        await _warmClient.InitializeAsync().ConfigureAwait(false);
        using var response = await _warmClient.CallGetDateAsync().ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    [GlobalSetup(Target = nameof(ConcurrentWarmSessionThroughput))]
    public async Task StartConcurrentWarmSessionsAsync()
    {
        _server = await BenchmarkHttpServer.StartAsync(SessionRunspaceCapacity).ConfigureAwait(false);
        _throughputClients = new List<BenchmarkMcpClient>();
        for (var index = 0; index < SessionRunspaceCapacity; index++)
        {
            var client = _server.CreateClient();
            await client.InitializeAsync().ConfigureAwait(false);
            using var response = await client.CallGetDateAsync().ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            _throughputClients.Add(client);
        }
    }

    [GlobalSetup(Target = nameof(BoundedCapacityRejection))]
    public async Task FillCapacityAsync()
    {
        _server = await BenchmarkHttpServer.StartAsync(SessionRunspaceCapacity).ConfigureAwait(false);
        _capacityClients = new List<BenchmarkMcpClient>();
        for (var index = 0; index < SessionRunspaceCapacity; index++)
        {
            var client = _server!.CreateClient();
            await client.InitializeAsync().ConfigureAwait(false);
            using var response = await client.CallGetDateAsync().ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            _capacityClients.Add(client);
        }
    }

    [IterationSetup(Target = nameof(BoundedCapacityRejection))]
    public void StartOverflowSession()
    {
        _overflowClient = _server!.CreateClient();
        _overflowClient.InitializeAsync().GetAwaiter().GetResult();
    }

    [IterationCleanup(Target = nameof(BoundedCapacityRejection))]
    public void DisposeOverflowSession()
    {
        if (_overflowClient is not null)
        {
            _overflowClient.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _overflowClient = null;
        }
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        if (_warmClient is not null)
        {
            await _warmClient.DisposeAsync().ConfigureAwait(false);
        }

        if (_capacityClients is not null)
        {
            foreach (var client in _capacityClients)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
        }

        if (_throughputClients is not null)
        {
            foreach (var client in _throughputClients)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
        }

        if (_server is not null)
        {
            await _server.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Benchmark(Description = "HTTP server startup + first session tool call (module/setup included)")]
    [InvocationCount(1)]
    public async Task FirstHttpSessionLatency()
    {
        await using var server = await BenchmarkHttpServer.StartAsync(SessionRunspaceCapacity).ConfigureAwait(false);
        await using var client = server.CreateClient();
        await client.InitializeAsync().ConfigureAwait(false);
        using var response = await client.CallGetDateAsync().ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    [Benchmark(Description = "Warm HTTP session tool-call latency")]
    public async Task WarmSessionToolLatency()
    {
        using var response = await _warmClient!.CallGetDateAsync().ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    [Benchmark(Description = "Concurrent warm-session throughput")]
    [Arguments(4)]
    public async Task ConcurrentWarmSessionThroughput(int concurrency)
    {
        var responses = await Task.WhenAll(_throughputClients!
            .Take(concurrency)
            .Select(client => client.CallGetDateAsync())).ConfigureAwait(false);
        try
        {
            foreach (var response in responses)
            {
                response.EnsureSuccessStatusCode();
            }
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Benchmark(Description = "Over-capacity session call validates an MCP error response")]
    [InvocationCount(1)]
    public async Task BoundedCapacityRejection()
    {
        using var response = await _overflowClient!.CallGetDateAsync().ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (!await BenchmarkMcpClient.IsMcpErrorAsync(response).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"Expected an MCP tool error after exhausting {SessionRunspaceCapacity} session runspaces.");
        }
    }
}
