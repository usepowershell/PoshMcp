using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PoshMcp.Tests.Shared;

namespace PoshMcp.Tests.Characterization;

/// <summary>
/// Starts the PoshMcp HTTP server as a subprocess via <c>dotnet run --no-build</c>,
/// polls the health endpoint, and exposes the subprocess for working-set measurement.
/// </summary>
internal sealed class CharacterizationHttpServer : IAsyncDisposable
{
    private readonly int _port;
    private Process? _serverProcess;

    public CharacterizationHttpServer()
    {
        _port = DynamicPort.AllocateUnique();
    }

    public string ServerUrl => $"http://127.0.0.1:{_port}";

    /// <summary>
    /// Returns the server subprocess's current working-set memory in bytes.
    /// Returns 0 if the process has exited or the value cannot be read.
    /// </summary>
    public long GetWorkingSetBytes()
    {
        if (_serverProcess is null || _serverProcess.HasExited) return 0;
        try
        {
            _serverProcess.Refresh();
            return _serverProcess.WorkingSet64;
        }
        catch
        {
            return 0;
        }
    }

    public async Task StartAsync(string? configPath = null)
    {
        var (workspaceRoot, buildConfiguration) = ResolveWorkspace();
        var serverProjectPath = Path.Combine(workspaceRoot, "PoshMcp.Server", "PoshMcp.csproj");
        var resolvedConfig = configPath ?? Path.Combine(workspaceRoot, "PoshMcp.Server", "appsettings.json");

        if (!File.Exists(serverProjectPath))
            throw new FileNotFoundException($"Server project not found: {serverProjectPath}");
        if (!File.Exists(resolvedConfig))
            throw new FileNotFoundException($"Config not found: {resolvedConfig}");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Directory.GetCurrentDirectory(),
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add(buildConfiguration);
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(serverProjectPath);
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("serve");
        startInfo.ArgumentList.Add("--transport");
        startInfo.ArgumentList.Add("http");
        startInfo.ArgumentList.Add("--url");
        startInfo.ArgumentList.Add(ServerUrl);
        startInfo.ArgumentList.Add("--config");
        startInfo.ArgumentList.Add(resolvedConfig);

        startInfo.Environment["ApplicationInsights__Enabled"] = "false";
        startInfo.Environment["APPLICATIONINSIGHTS_CONNECTION_STRING"] = string.Empty;

        _serverProcess = new Process { StartInfo = startInfo };
        _serverProcess.OutputDataReceived += static (_, _) => { };
        _serverProcess.ErrorDataReceived += static (_, _) => { };
        _serverProcess.Start();
        _serverProcess.BeginOutputReadLine();
        _serverProcess.BeginErrorReadLine();

        await WaitForReadyAsync();
    }

    private async Task WaitForReadyAsync()
    {
        using var healthClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * 60);

        while (Stopwatch.GetTimestamp() < deadline)
        {
            if (_serverProcess!.HasExited)
                throw new InvalidOperationException(
                    $"Characterization server exited prematurely (code {_serverProcess.ExitCode}).");
            try
            {
                var r = await healthClient.GetAsync(new Uri(ServerUrl + "/health"));
                if (r.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }

            await Task.Delay(250);
        }

        throw new TimeoutException($"Characterization server at {ServerUrl} did not become ready within 60 seconds.");
    }

    private static (string WorkspaceRoot, string BuildConfiguration) ResolveWorkspace()
    {
        var current = Directory.GetCurrentDirectory();
        var root = current;
        while (!File.Exists(Path.Combine(root, "PoshMcp.sln")) && Path.GetDirectoryName(root) != null)
            root = Path.GetDirectoryName(root)!;

        var baseDir = AppContext.BaseDirectory;
        var config = baseDir.IndexOf(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase) >= 0 ? "Release" : "Debug";

        return (root, config);
    }

    public async ValueTask DisposeAsync()
    {
        if (_serverProcess is null) return;
        if (!_serverProcess.HasExited)
        {
            _serverProcess.Kill(entireProcessTree: true);
            await _serverProcess.WaitForExitAsync();
        }
        _serverProcess.Dispose();
        _serverProcess = null;
    }
}

/// <summary>
/// Minimal MCP HTTP client for characterization tests.
/// Supports session initialization and a single <c>get_date</c> tool call.
/// </summary>
internal sealed class CharacterizationMcpClient : IAsyncDisposable
{
    private const string McpProtocolVersion = "2025-11-25";
    private static long _nextId;

    private readonly HttpClient _client;
    private string? _sessionId;

    public CharacterizationMcpClient(string serverUrl)
    {
        _client = new HttpClient
        {
            BaseAddress = new Uri(serverUrl),
            Timeout = TimeSpan.FromSeconds(30),
        };
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
    }

    public async Task InitializeAsync()
    {
        var id = Interlocked.Increment(ref _nextId);
        var body = $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"initialize\",\"params\":" +
                   $"{{\"protocolVersion\":\"{McpProtocolVersion}\",\"capabilities\":{{\"tools\":{{}}}},\"clientInfo\":{{\"name\":\"poshmcp-characterization\",\"version\":\"1.0\"}}}}}}";

        using var response = await PostAsync(body, sessionId: null);
        response.EnsureSuccessStatusCode();

        _sessionId = response.Headers.TryGetValues("Mcp-Session-Id", out var values)
            ? System.Linq.Enumerable.FirstOrDefault(values)
            : null;

        if (string.IsNullOrWhiteSpace(_sessionId))
            throw new InvalidOperationException("Server did not return a session ID from initialize.");
    }

    /// <summary>
    /// Calls the <c>get_date</c> tool and returns elapsed milliseconds.
    /// </summary>
    public async Task<double> CallGetDateAsync()
    {
        var id = Interlocked.Increment(ref _nextId);
        var body = $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"tools/call\",\"params\":{{\"name\":\"get_date\",\"arguments\":{{}}}}}}";

        var sw = Stopwatch.StartNew();
        using var response = await PostAsync(body, _sessionId);
        response.EnsureSuccessStatusCode();
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    private Task<HttpResponseMessage> PostAsync(string body, string? sessionId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            request.Headers.Add("Mcp-Session-Id", sessionId);
            request.Headers.Add("MCP-Protocol-Version", McpProtocolVersion);
        }
        return _client.SendAsync(request);
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
