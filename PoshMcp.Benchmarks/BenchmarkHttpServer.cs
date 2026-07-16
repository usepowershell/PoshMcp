using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PoshMcp.Server.PowerShell;

namespace PoshMcp.Benchmarks;

internal sealed class BenchmarkHttpServer : IAsyncDisposable
{
    private const string ProtocolVersion = "2025-11-25";
    private readonly Process _process;
    private readonly HttpClient _healthClient = new() { Timeout = TimeSpan.FromSeconds(2) };

    private BenchmarkHttpServer(Process process, Uri baseUri)
    {
        _process = process;
        BaseUri = baseUri;
    }

    public Uri BaseUri { get; }

    public static async Task<BenchmarkHttpServer> StartAsync(int sessionRunspaceCapacity = 4)
    {
        var serverAssembly = typeof(PowerShellConfiguration).Assembly.Location;
        var configPath = Path.Combine(
            AppContext.BaseDirectory,
            "BenchmarkAssets",
            "http-session-benchmark.appsettings.json");
        var port = AllocatePort();
        var baseUri = new Uri($"http://127.0.0.1:{port}");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        startInfo.ArgumentList.Add(serverAssembly);
        startInfo.ArgumentList.Add("serve");
        startInfo.ArgumentList.Add("--transport");
        startInfo.ArgumentList.Add("http");
        startInfo.ArgumentList.Add("--url");
        startInfo.ArgumentList.Add(baseUri.ToString().TrimEnd('/'));
        startInfo.ArgumentList.Add("--config");
        startInfo.ArgumentList.Add(configPath);
        startInfo.Environment["ApplicationInsights__Enabled"] = "false";
        startInfo.Environment["APPLICATIONINSIGHTS_CONNECTION_STRING"] = string.Empty;
        startInfo.Environment["McpServer__SessionRunspaceCapacity"] = sessionRunspaceCapacity.ToString();

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the benchmark HTTP server.");
        var server = new BenchmarkHttpServer(process, baseUri);

        try
        {
            await server.WaitForReadyAsync().ConfigureAwait(false);
            return server;
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public BenchmarkMcpClient CreateClient() => new(BaseUri);

    private async Task WaitForReadyAsync()
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * 60);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException(
                    $"Benchmark HTTP server exited with code {_process.ExitCode} before becoming ready.");
            }

            try
            {
                using var response = await _healthClient.GetAsync(new Uri(BaseUri, "health")).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            await Task.Delay(100).ConfigureAwait(false);
        }

        throw new TimeoutException($"Benchmark HTTP server did not become ready at {BaseUri}.");
    }

    public async ValueTask DisposeAsync()
    {
        _healthClient.Dispose();
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync().ConfigureAwait(false);
        }

        _process.Dispose();
    }

    private static int AllocatePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

internal sealed class BenchmarkMcpClient : IAsyncDisposable
{
    private const string ProtocolVersion = "2025-11-25";
    private static long _nextRequestId;
    private readonly HttpClient _client;

    public BenchmarkMcpClient(Uri baseUri)
    {
        _client = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(30) };
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
    }

    public string? SessionId { get; private set; }

    public async Task InitializeAsync()
    {
        using var response = await SendAsync(new
        {
            jsonrpc = "2.0",
            id = Interlocked.Increment(ref _nextRequestId),
            method = "initialize",
            @params = new
            {
                protocolVersion = ProtocolVersion,
                capabilities = new { tools = new { } },
                clientInfo = new { name = "poshmcp-benchmark", version = "1.0" }
            }
        }).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        SessionId = response.Headers.TryGetValues("Mcp-Session-Id", out var values)
            ? values.FirstOrDefault()
            : throw new InvalidOperationException("The benchmark MCP server did not return a session ID.");
    }

    public Task<HttpResponseMessage> CallGetDateAsync() => SendAsync(new
    {
        jsonrpc = "2.0",
        id = Interlocked.Increment(ref _nextRequestId),
        method = "tools/call",
        @params = new { name = "get_date", arguments = new { } }
    });

    public static async Task<bool> IsMcpErrorAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (payload.StartsWith("event:", StringComparison.Ordinal))
        {
            payload = payload.Split('\n')
                .FirstOrDefault(line => line.StartsWith("data:", StringComparison.Ordinal))?[5..].TrimStart()
                ?? throw new InvalidOperationException("The benchmark MCP server returned an SSE response without JSON data.");
        }

        using var document = JsonDocument.Parse(payload);
        return IsMcpError(document.RootElement);
    }

    internal static bool IsMcpError(JsonElement response)
    {
        return response.TryGetProperty("error", out _)
            || (response.TryGetProperty("result", out var result)
                && result.TryGetProperty("isError", out var isError)
                && isError.ValueKind is JsonValueKind.True);
    }

    private Task<HttpResponseMessage> SendAsync(object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(SessionId))
        {
            request.Headers.Add("Mcp-Session-Id", SessionId);
            request.Headers.Add("MCP-Protocol-Version", ProtocolVersion);
        }

        return _client.SendAsync(request);
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
