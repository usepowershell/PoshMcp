using System;
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

[Trait("Category", "Http")]
[Collection("TransportSelectionTests")]
public class UnifiedHttpTransportIntegrationTests : PowerShellTestBase
{
    public UnifiedHttpTransportIntegrationTests(ITestOutputHelper output) : base(output)
    {
    }

    [Theory]
    [InlineData("application/json", "{\"jsonrpc\":\"2.0\",\"result\":{\"tools\":[]}}")]
    [InlineData("text/event-stream", "event: message\ndata: {\"jsonrpc\":\"2.0\",\"result\":{\"tools\":[]}}\n\n")]
    public void StreamableHttpResponseParser_ShouldSupportJsonAndSse(string mediaType, string responseText)
    {
        var response = ParseJsonOrSsePayload(responseText, mediaType);

        Assert.Equal("2.0", response["jsonrpc"]?.ToString());
        Assert.NotNull(response["result"]);
    }

    [Fact]
    public async Task ServeHttpTransport_ShouldStartAndExposeHealthEndpoint()
    {
        using var server = new InProcessUnifiedHttpServer();

        try
        {
            await server.StartAsync();

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            var healthResponse = await GetWithSocketRetryAsync(httpClient, $"{server.ServerUrl}/health");
            var readyResponse = await GetWithSocketRetryAsync(httpClient, $"{server.ServerUrl}/health/ready");

            Assert.True(healthResponse.IsSuccessStatusCode, $"Expected /health to succeed, got {(int)healthResponse.StatusCode}");
            Assert.True(readyResponse.IsSuccessStatusCode, $"Expected /health/ready to succeed, got {(int)readyResponse.StatusCode}");

            var contentType = healthResponse.Content.Headers.ContentType?.MediaType;
            Assert.Equal("application/json", contentType);
        }
        catch (Exception ex)
        {
            Output.WriteLine(server.GetCapturedOutput());
            throw new Xunit.Sdk.XunitException($"Unified HTTP server failed to start or respond: {ex.Message}");
        }
    }

    [Fact]
    public async Task ServeHttpTransport_ShouldRetain2024_11_05Compatibility()
    {
        using var server = new InProcessUnifiedHttpServer();

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
                    capabilities = new
                    {
                        tools = new { }
                    },
                    clientInfo = new
                    {
                        name = "unified-http-transport-integration-test",
                        version = "1.0.0"
                    }
                }
            };

            var (initializeResponse, sessionId) = await SendMcpRequestAsync(httpClient, initializeRequest);
            Assert.Equal("2.0", initializeResponse["jsonrpc"]?.ToString());
            Assert.NotNull(initializeResponse["result"]);
            // Stateless mode (production default): no Mcp-Session-Id is returned.
            // The 2024-11-05 protocol is still supported for backward compatibility.

            var listToolsRequest = new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/list"
            };

            // In stateless mode subsequent requests do not require a session ID.
            var (listToolsResponse, _) = await SendMcpRequestAsync(httpClient, listToolsRequest);
            var tools = listToolsResponse["result"]?["tools"] as JArray;

            Assert.NotNull(tools);
            Assert.NotEmpty(tools!);
            Assert.Contains(tools!, tool =>
                string.Equals(tool?["name"]?.ToString(), "get_last_command_output", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Output.WriteLine(server.GetCapturedOutput());
            throw new Xunit.Sdk.XunitException($"Unified HTTP MCP request flow failed: {ex.Message}");
        }
    }

    [Fact]
    public async Task ServeHttpTransport_Stateless_SupportsDirectMethodsAndNotifications()
    {
        // In stateless mode (production default) every request is independent — no session required.
        // Proves: tools/list works without initialize; notifications are accepted; GET is handled.
        using var server = new InProcessUnifiedHttpServer();

        try
        {
            await server.StartAsync();

            using var httpClient = CreateMcpHttpClient(server.ServerUrl);

            // tools/list: works directly without initialize or session ID
            var (toolsListResponse, noSessionId) = await SendMcpRequestAsync(httpClient,
                new { jsonrpc = "2.0", id = 1, method = "tools/list" });
            Assert.NotNull(toolsListResponse["result"]?["tools"]);
            Assert.True(string.IsNullOrWhiteSpace(noSessionId),
                "Stateless mode must not return Mcp-Session-Id on tools/list");

            // notifications: accepted without session ID
            using var notificationResponse = await SendRawMcpRequestAsync(
                httpClient,
                HttpMethod.Post,
                new { jsonrpc = "2.0", method = "notifications/initialized" },
                sessionId: null);
            Assert.Equal(System.Net.HttpStatusCode.Accepted, notificationResponse.StatusCode);

            // GET without session: handled by SDK (200 SSE stream or 4xx, not a server crash)
            using var getRequest = new HttpRequestMessage(HttpMethod.Get, "/");
            getRequest.Headers.Accept.ParseAdd("text/event-stream");
            using var getResponse = await httpClient.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead);
            Assert.True(
                getResponse.IsSuccessStatusCode || (int)getResponse.StatusCode < 500,
                $"GET without session in stateless mode must not cause a 5xx error: got {(int)getResponse.StatusCode}");
        }
        catch (Exception ex)
        {
            Output.WriteLine(server.GetCapturedOutput());
            throw new Xunit.Sdk.XunitException($"Stateless protocol conformance failed: {ex.Message}");
        }
    }

    [Fact]
    public async Task ServeHttpTransport_ShouldRejectInvalidOrigins()
    {
        using var server = new InProcessUnifiedHttpServer();

        try
        {
            await server.StartAsync();

            using var httpClient = CreateMcpHttpClient(server.ServerUrl);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/")
            {
                Content = new StringContent(
                    JsonConvert.SerializeObject(CreateInitializeRequest("2025-11-25")),
                    Encoding.UTF8,
                    "application/json")
            };
            request.Headers.Add("Origin", "https://attacker.example");

            using var response = await httpClient.SendAsync(request);

            Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
        }
        catch (Exception ex)
        {
            Output.WriteLine(server.GetCapturedOutput());
            throw new Xunit.Sdk.XunitException($"Streamable HTTP origin conformance failed: {ex.Message}");
        }
    }

    [Fact]
    public async Task ServeHttpTransport_ShouldCallToolOverMcp()
    {
        using var server = new InProcessUnifiedHttpServer();

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
                        name = "unified-http-tool-call-test",
                        version = "1.0.0"
                    }
                }
            };

            var (_, sessionId) = await SendMcpRequestAsync(httpClient, initializeRequest);
            // Stateless mode: sessionId is null/empty — tools work without session tracking.

            var listToolsRequest = new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/list"
            };

            var (listToolsResponse, _) = await SendMcpRequestAsync(httpClient, listToolsRequest);
            var tools = listToolsResponse["result"]?["tools"] as JArray;

            Assert.NotNull(tools);
            Assert.NotEmpty(tools!);

            var selectedToolName = tools!
                .Select(t => t?["name"]?.ToString())
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name) && name.StartsWith("get_date", StringComparison.OrdinalIgnoreCase))
                ?? tools.Select(t => t?["name"]?.ToString()).FirstOrDefault(name => string.Equals(name, "get_last_command_output", StringComparison.OrdinalIgnoreCase));

            Assert.False(string.IsNullOrWhiteSpace(selectedToolName));

            var callRequest = new
            {
                jsonrpc = "2.0",
                id = 3,
                method = "tools/call",
                @params = new
                {
                    name = selectedToolName,
                    arguments = new { }
                }
            };

            var (callResponse, _) = await SendMcpRequestAsync(httpClient, callRequest);
            Assert.Equal("2.0", callResponse["jsonrpc"]?.ToString());
            Assert.NotNull(callResponse["result"]);
            Assert.Null(callResponse["error"]);
        }
        catch (Exception ex)
        {
            Output.WriteLine(server.GetCapturedOutput());
            throw new Xunit.Sdk.XunitException($"Unified HTTP MCP tools/call flow failed: {ex.Message}");
        }
    }

    [Fact]
    public async Task ServeHttpTransport_ShouldReturnErrorForUnknownToolCall()
    {
        using var server = new InProcessUnifiedHttpServer();

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
                        name = "unified-http-error-test",
                        version = "1.0.0"
                    }
                }
            };

            var (_, sessionId) = await SendMcpRequestAsync(httpClient, initializeRequest);
            // Stateless mode: sessionId is null/empty — tools/call works without a session.

            var invalidToolCallRequest = new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/call",
                @params = new
                {
                    name = "tool_that_does_not_exist",
                    arguments = new { }
                }
            };

            var (errorResponse, _) = await SendMcpRequestAsync(httpClient, invalidToolCallRequest);
            Assert.Equal("2.0", errorResponse["jsonrpc"]?.ToString());
            Assert.NotNull(errorResponse["error"]);
            Assert.Null(errorResponse["result"]);
        }
        catch (Exception ex)
        {
            Output.WriteLine(server.GetCapturedOutput());
            throw new Xunit.Sdk.XunitException($"Unified HTTP MCP error-path flow failed: {ex.Message}");
        }
    }

    private static HttpClient CreateMcpHttpClient(string serverUrl)
    {
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(serverUrl),
            Timeout = TimeSpan.FromSeconds(10)
        };
        httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
        return httpClient;
    }

    private static object CreateInitializeRequest(string protocolVersion)
    {
        return new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion,
                capabilities = new { tools = new { } },
                clientInfo = new
                {
                    name = "unified-http-streamable-conformance-test",
                    version = "1.0.0"
                }
            }
        };
    }

    private static async Task<HttpResponseMessage> SendRawMcpRequestAsync(
        HttpClient httpClient,
        HttpMethod method,
        object request,
        string? sessionId,
        string? protocolVersion = null)
    {
        var requestJson = JsonConvert.SerializeObject(request);
        using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        using var requestMessage = new HttpRequestMessage(method, "/")
        {
            Content = content
        };

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            requestMessage.Headers.Add("Mcp-Session-Id", sessionId);
        }

        if (!string.IsNullOrWhiteSpace(protocolVersion))
        {
            requestMessage.Headers.Add("MCP-Protocol-Version", protocolVersion);
        }

        return await httpClient.SendAsync(requestMessage);
    }

    private static async Task<(JObject Response, string? SessionId)> SendMcpRequestAsync(
        HttpClient httpClient,
        object request,
        string? sessionId = null,
        string? protocolVersion = null)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
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

            if (!string.IsNullOrWhiteSpace(protocolVersion))
            {
                requestMessage.Headers.Add("MCP-Protocol-Version", protocolVersion);
            }

            try
            {
                using var response = await httpClient.SendAsync(requestMessage);
                var responseText = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {responseText}");
                }

                var responseObject = ParseJsonOrSsePayload(responseText, response.Content.Headers.ContentType?.MediaType);
                var responseSessionId = response.Headers.TryGetValues("Mcp-Session-Id", out var sessionIdValues)
                    ? sessionIdValues.FirstOrDefault()
                    : null;

                return (responseObject, responseSessionId);
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts && IsSocketAddressInUse(ex.Message))
            {
                await Task.Delay(100 * attempt);
            }
        }

        throw new InvalidOperationException("MCP request retry loop exhausted unexpectedly.");
    }

    private static async Task<HttpResponseMessage> GetWithSocketRetryAsync(HttpClient httpClient, string url)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await httpClient.GetAsync(url);
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts && IsSocketAddressInUse(ex.Message))
            {
                await Task.Delay(100 * attempt);
            }
        }

        throw new InvalidOperationException("HTTP GET retry loop exhausted unexpectedly.");
    }

    private static bool IsSocketAddressInUse(string message)
    {
        return message.Contains("Only one usage of each socket address", StringComparison.OrdinalIgnoreCase)
            || message.Contains("address already in use", StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertResponseIsJsonOrSse(HttpResponseMessage response)
    {
        Assert.Contains(
            response.Content.Headers.ContentType?.MediaType,
            new[] { "application/json", "text/event-stream" });
    }

    private static JObject ParseJsonOrSsePayload(string responseText, string? mediaType)
    {
        if (string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            return JObject.Parse(responseText);
        }

        var dataLine = responseText
            .Split('\n')
            .FirstOrDefault(line => line.StartsWith("data: ", StringComparison.Ordinal));

        if (string.IsNullOrWhiteSpace(dataLine))
        {
            throw new InvalidOperationException($"No MCP data payload found in response: {responseText}");
        }

        return JObject.Parse(dataLine.Substring(6));
    }
}

internal sealed class InProcessUnifiedHttpServer : IDisposable
{
    private readonly int _port;
    private Process? _serverProcess;
    private readonly object _outputLock = new();
    private readonly System.Text.StringBuilder _capturedOutput = new();

    public string ServerUrl => $"http://localhost:{_port}";

    public InProcessUnifiedHttpServer(int port = 0)
    {
        // FR-411: bind on a kernel-allocated free port (probe TcpListener on
        // port 0, read .Port, release). Replaces a Random.Shared.Next(6100,
        // 6900) range-picker that produced collisions across concurrent runs.
        _port = port == 0 ? PoshMcp.Tests.Shared.DynamicPort.AllocateUnique() : port;
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
                throw new InvalidOperationException($"Server process exited with code {_serverProcess.ExitCode}. Output:\n{GetCapturedOutput()}");
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

        throw new TimeoutException($"Unified HTTP server did not become ready at {ServerUrl}. Output:\n{GetCapturedOutput()}");
    }

    private void OnOutputData(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data))
        {
            return;
        }

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

        if (baseDirectory.IndexOf($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Release";
        }

        if (baseDirectory.IndexOf($"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Debug";
        }

        return "Debug";
    }

    public void Dispose()
    {
        if (_serverProcess == null)
        {
            return;
        }

        // Spec 009 FR-412 — centralized teardown.
        SubprocessTeardown.Teardown(_serverProcess);
        _serverProcess = null;
    }
}
