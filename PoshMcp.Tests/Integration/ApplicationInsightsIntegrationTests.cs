using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Integration;

[Trait("Category", "Integration")]
public class ApplicationInsightsIntegrationTests : PowerShellTestBase
{
    public ApplicationInsightsIntegrationTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task Server_StartsSuccessfully_WithAppInsightsEnabled_NoConnectionString()
    {
        using var server = new AppInsightsTestHttpServer(
            environmentOverrides: new Dictionary<string, string>
            {
                ["ApplicationInsights__Enabled"] = "true"
            });

        try
        {
            await server.StartAsync();

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            var healthResponse = await httpClient.GetAsync($"{server.ServerUrl}/health");
            var readyResponse = await httpClient.GetAsync($"{server.ServerUrl}/health/ready");

            Assert.True(healthResponse.IsSuccessStatusCode,
                $"Expected /health to succeed with AppInsights enabled (no connection string), got {(int)healthResponse.StatusCode}");
            Assert.True(readyResponse.IsSuccessStatusCode,
                $"Expected /health/ready to succeed with AppInsights enabled (no connection string), got {(int)readyResponse.StatusCode}");
        }
        catch (Exception ex)
        {
            Output.WriteLine(server.GetCapturedOutput());
            throw new Xunit.Sdk.XunitException(
                $"Server failed to start with ApplicationInsights.Enabled=true and no connection string: {ex.Message}");
        }
    }

    [Fact]
    public async Task Server_LogsWarning_WhenAppInsightsEnabled_NoConnectionString()
    {
        using var server = new AppInsightsTestHttpServer(
            environmentOverrides: new Dictionary<string, string>
            {
                ["ApplicationInsights__Enabled"] = "true"
            });

        try
        {
            await server.StartAsync();

            // Give the server a moment to flush warning output
            await Task.Delay(500);

            var capturedOutput = server.GetCapturedOutput();
            Output.WriteLine($"Captured output:\n{capturedOutput}");

            Assert.Contains("Application Insights is enabled but no connection string was found",
                capturedOutput, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Output.WriteLine(server.GetCapturedOutput());
            throw new Xunit.Sdk.XunitException(
                $"Failed to verify warning message for missing connection string: {ex.Message}");
        }
    }

    [Fact]
    public async Task Server_McpToolsStillRespond_WithAppInsightsEnabled()
    {
        using var server = new AppInsightsTestHttpServer(
            environmentOverrides: new Dictionary<string, string>
            {
                ["ApplicationInsights__Enabled"] = "true"
            });

        try
        {
            await server.StartAsync();

            using var httpClient = new HttpClient
            {
                BaseAddress = new Uri(server.ServerUrl),
                Timeout = TimeSpan.FromSeconds(10)
            };

            httpClient.DefaultRequestHeaders.Accept.Clear();
            httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");

            var initializeRequest = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { tools = new { } },
                    clientInfo = new
                    {
                        name = "appinsights-integration-test",
                        version = "1.0.0"
                    }
                }
            };

            var (initializeResponse, sessionId) = await SendMcpRequestAsync(httpClient, initializeRequest);
            Assert.Equal("2.0", initializeResponse["jsonrpc"]?.ToString());
            Assert.NotNull(initializeResponse["result"]);
            Assert.False(string.IsNullOrWhiteSpace(sessionId));

            var listToolsRequest = new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/list"
            };

            var (listToolsResponse, _) = await SendMcpRequestAsync(httpClient, listToolsRequest, sessionId);
            var tools = listToolsResponse["result"]?["tools"] as JArray;

            Assert.NotNull(tools);
            Assert.NotEmpty(tools!);
            Assert.Contains(tools!, tool =>
                string.Equals(tool?["name"]?.ToString(), "get_last_command_output", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Output.WriteLine(server.GetCapturedOutput());
            throw new Xunit.Sdk.XunitException(
                $"MCP tools/list failed with ApplicationInsights enabled: {ex.Message}");
        }
    }

    [Fact]
    public async Task Server_StartsNormally_WithAppInsightsDisabled()
    {
        using var server = new AppInsightsTestHttpServer(
            environmentOverrides: new Dictionary<string, string>
            {
                ["ApplicationInsights__Enabled"] = "false"
            });

        try
        {
            await server.StartAsync();

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            var healthResponse = await httpClient.GetAsync($"{server.ServerUrl}/health");

            Assert.True(healthResponse.IsSuccessStatusCode,
                $"Expected /health to succeed with AppInsights disabled, got {(int)healthResponse.StatusCode}");

            var capturedOutput = server.GetCapturedOutput();

            // When disabled, no warning about connection string should appear
            Assert.DoesNotContain("Application Insights is enabled but no connection string was found",
                capturedOutput, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Output.WriteLine(server.GetCapturedOutput());
            throw new Xunit.Sdk.XunitException(
                $"Server failed to start with ApplicationInsights disabled: {ex.Message}");
        }
    }

    private static async Task<(JObject Response, string? SessionId)> SendMcpRequestAsync(
        HttpClient httpClient, object request, string? sessionId = null)
    {
        var requestJson = JsonConvert.SerializeObject(request);
        using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Content = content
        };

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            requestMessage.Headers.Add("Mcp-Session-Id", sessionId);
        }

        using var response = await httpClient.SendAsync(requestMessage);
        var responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {responseText}");
        }

        return (ParseSsePayload(responseText), response.Headers.TryGetValues("Mcp-Session-Id", out var vals)
            ? vals.FirstOrDefault()
            : null);
    }

    private static JObject ParseSsePayload(string sseResponse)
    {
        var dataLine = sseResponse
            .Split('\n')
            .FirstOrDefault(line => line.StartsWith("data: ", StringComparison.Ordinal));

        if (string.IsNullOrWhiteSpace(dataLine))
        {
            throw new InvalidOperationException($"No MCP data payload found in SSE response: {sseResponse}");
        }

        return JObject.Parse(dataLine.Substring(6));
    }
}

/// <summary>
/// HTTP server helper that supports custom environment variable overrides
/// for testing configuration scenarios like ApplicationInsights.
/// </summary>
internal sealed class AppInsightsTestHttpServer : IDisposable
{
    private readonly int _port;
    private readonly Dictionary<string, string> _environmentOverrides;
    private Process? _serverProcess;
    private readonly object _outputLock = new();
    private readonly StringBuilder _capturedOutput = new();

    public string ServerUrl => $"http://localhost:{_port}";

    public AppInsightsTestHttpServer(
        Dictionary<string, string>? environmentOverrides = null,
        int port = 0)
    {
        _port = port == 0 ? Random.Shared.Next(6100, 6900) : port;
        _environmentOverrides = environmentOverrides ?? new Dictionary<string, string>();
    }

    public async Task StartAsync()
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        var workspaceRoot = currentDirectory;

        while (!File.Exists(Path.Combine(workspaceRoot, "PoshMcp.sln")) && Path.GetDirectoryName(workspaceRoot) != null)
        {
            workspaceRoot = Path.GetDirectoryName(workspaceRoot)!;
        }

        var serverProjectPath = Path.Combine(workspaceRoot, "PoshMcp.Server", "PoshMcp.csproj");
        var configPath = Path.Combine(workspaceRoot, "PoshMcp.Server", "appsettings.json");
        var buildConfiguration = ResolveBuildConfiguration();

        if (!File.Exists(serverProjectPath))
        {
            throw new FileNotFoundException($"Server project not found at: {serverProjectPath}");
        }

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Server config not found at: {configPath}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --no-build --configuration {buildConfiguration} --project \"{serverProjectPath}\" -- serve --transport http --url \"{ServerUrl}\" --config \"{configPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = currentDirectory
        };

        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["ASPNETCORE_URLS"] = ServerUrl;

        // Clear any existing Application Insights env var so we test the "missing" path
        startInfo.Environment.Remove("APPLICATIONINSIGHTS_CONNECTION_STRING");

        // Apply custom environment overrides
        foreach (var kvp in _environmentOverrides)
        {
            startInfo.Environment[kvp.Key] = kvp.Value;
        }

        _serverProcess = new Process { StartInfo = startInfo };
        _serverProcess.OutputDataReceived += OnOutputData;
        _serverProcess.ErrorDataReceived += OnOutputData;

        _serverProcess.Start();
        TestProcessRegistry.Register(_serverProcess);
        _serverProcess.BeginOutputReadLine();
        _serverProcess.BeginErrorReadLine();

        var maxAttempts = 40;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (_serverProcess.HasExited)
            {
                throw new InvalidOperationException(
                    $"Server process exited with code {_serverProcess.ExitCode}. Output:\n{GetCapturedOutput()}");
            }

            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var response = await httpClient.GetAsync($"{ServerUrl}/health");

                if ((int)response.StatusCode >= 200 && (int)response.StatusCode < 500)
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

            await Task.Delay(500);
        }

        throw new TimeoutException(
            $"Server did not become ready at {ServerUrl}. Output:\n{GetCapturedOutput()}");
    }

    private void OnOutputData(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data)) return;

        lock (_outputLock)
        {
            _capturedOutput.AppendLine(e.Data);
        }
    }

    public string GetCapturedOutput()
    {
        lock (_outputLock)
        {
            return _capturedOutput.ToString();
        }
    }

    private static string ResolveBuildConfiguration()
    {
        var baseDirectory = AppContext.BaseDirectory;

        if (baseDirectory.IndexOf($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Release";
        }

        if (baseDirectory.IndexOf($"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Debug";
        }

        return "Debug";
    }

    public void Dispose()
    {
        if (_serverProcess == null) return;

        try
        {
            if (!_serverProcess.HasExited)
            {
                _serverProcess.Kill(entireProcessTree: true);
                _serverProcess.WaitForExit(5000);
            }
        }
        finally
        {
            TestProcessRegistry.Unregister(_serverProcess);
            _serverProcess.Dispose();
            _serverProcess = null;
        }
    }
}
