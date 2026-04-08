using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Integration;

/// <summary>
/// Integration tests for the web server that run the MCP web server and communicate via HTTP
/// </summary>
public class WebServerWithHttpClient : PowerShellTestBase, IAsyncLifetime
{
    private InProcessWebServer? _sharedServer;
    private HttpMcpClient? _sharedClient;

    public WebServerWithHttpClient(ITestOutputHelper output) : base(output)
    {
    }

    public async Task InitializeAsync()
    {
        Logger.LogInformation("=== Initializing shared MCP web server and HTTP client for integration tests ===");

        _sharedServer = new InProcessWebServer(Logger);
        await _sharedServer.StartAsync();

        _sharedClient = new HttpMcpClient(Logger, _sharedServer.ServerUrl);
        await _sharedClient.StartAsync();

        Logger.LogInformation("=== Shared MCP web server and HTTP client initialized successfully ===");
    }

    public Task DisposeAsync()
    {
        Logger.LogInformation("=== Disposing shared MCP web server and HTTP client ===");

        _sharedClient?.Dispose();
        _sharedServer?.Dispose();

        Logger.LogInformation("=== Shared MCP web server and HTTP client disposed ===");

        return Task.CompletedTask;
    }

    [Fact]
    public async Task ShouldInitializeAndListTools()
    {
        Logger.LogInformation("=== Starting WebServer ShouldInitializeAndListTools Test ===");

        // Use shared client instance
        var client = _sharedClient ?? throw new InvalidOperationException("Shared client not initialized");

        // List available tools
        var toolsResponse = await client.SendListToolsAsync();
        Assert.NotNull(toolsResponse);
        Assert.True(toolsResponse["result"]?["tools"] != null);

        var tools = toolsResponse["result"]?["tools"] as JArray;
        Assert.NotNull(tools);
        // There should be 8 tools available when reload configuration is enabled (1 command + 4 utility methods + 3 configuration reload tools)
        Assert.Equal(8, tools.Count);

        Logger.LogInformation($"Found {tools.Count} tools via HTTP client");

        foreach (var tool in tools.OrderBy(x => x["name"]))
        {
            var toolName = tool["name"]?.ToString();
            var toolTitle = tool["title"]?.ToString();
            var toolDescription = tool["description"]?.ToString();
            Logger.LogInformation($"  Tool: {toolName} - {toolTitle}");
        }
    }

    [Fact]
    public async Task ShouldExecutePowerShellCommand()
    {
        Logger.LogInformation("=== Starting WebServer ShouldExecutePowerShellCommand Test ===");

        // Use shared client instance
        var client = _sharedClient ?? throw new InvalidOperationException("Shared client not initialized");

        // Verify tools are available
        var toolsResponse = await client.SendListToolsAsync();
        Assert.NotNull(toolsResponse);
        Assert.True(toolsResponse["result"]?["tools"] != null);

        // Call the PowerShell function through MCP
        var callResponse = await client.SendToolCallAsync("get_some_data", new
        {
            test = "FromWebIntegrationTest"
        });

        // Assert: Verify the response
        Assert.NotNull(callResponse);
        Assert.Equal("2.0", callResponse["jsonrpc"]?.ToString());
        Assert.NotNull(callResponse["result"]);

        var content = callResponse["result"]?["content"];
        Assert.NotNull(content);

        var contentArray = content as JArray;
        Assert.NotNull(contentArray);
        Assert.True(contentArray.Count > 0);

        var textContent = contentArray[0]?["text"]?.ToString();
        Assert.NotNull(textContent);

        Logger.LogInformation($"Tool call result from HTTP client: {textContent}");

        // Verify the output contains expected data
        Assert.Contains("FromWebIntegrationTest", textContent);

        Logger.LogInformation("PowerShell command executed successfully via HTTP client");
    }

    [Fact]
    public async Task ShouldHandleErrors()
    {
        Logger.LogInformation("=== Starting WebServer ShouldHandleErrors Test ===");

        // Use shared client instance
        var client = _sharedClient ?? throw new InvalidOperationException("Shared client not initialized");

        // Try to call a non-existent tool
        var errorResponse = await client.SendToolCallAsync("NonExistentTool", new { });

        // Assert: Verify error handling
        Assert.NotNull(errorResponse);
        Assert.NotNull(errorResponse["error"]);

        var errorMessage = errorResponse["error"]?["message"]?.ToString();
        Assert.NotNull(errorMessage);
        Assert.Contains("Unknown tool:", errorMessage, StringComparison.OrdinalIgnoreCase);

        Logger.LogInformation($"Error handled correctly via HTTP client: {errorMessage}");
    }
}

/// <summary>
/// In-process MCP web server that can be debugged while HTTP clients connect
/// </summary>
public class InProcessWebServer : IDisposable
{
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private Process? _serverProcess;
    private readonly int _port;

    public string ServerUrl => $"http://localhost:{_port}";
    public bool IsRunning => _serverProcess != null && !_serverProcess.HasExited;

    public InProcessWebServer(ILogger logger, int port = 0)
    {
        _logger = logger;
        _cancellationTokenSource = new CancellationTokenSource();
        // Use random port from a wider range to avoid conflicts during testing
        _port = port == 0 ? Random.Shared.Next(5000, 6000) : port;
    }

    public async Task StartAsync()
    {
        _logger.LogInformation($"Starting in-process MCP web server on port {_port}...");

        // Get the absolute path to the web server project
        var currentDirectory = Directory.GetCurrentDirectory();
        var workspaceRoot = currentDirectory;

        // Navigate up until we find the workspace root (containing PoshMcp.sln)
        while (!File.Exists(Path.Combine(workspaceRoot, "PoshMcp.sln")) &&
               Path.GetDirectoryName(workspaceRoot) != null)
        {
            workspaceRoot = Path.GetDirectoryName(workspaceRoot)!;
        }

        var webProjectPath = Path.Combine(workspaceRoot, "PoshMcp.Web", "PoshMcp.Web.csproj");

        if (!File.Exists(webProjectPath))
        {
            throw new FileNotFoundException($"Web server project not found at: {webProjectPath}. Workspace root: {workspaceRoot}, Current directory: {currentDirectory}");
        }

        // Start the web server process
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{webProjectPath}\" --urls=\"http://localhost:{_port}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = currentDirectory
        };

        // Add environment variables for better debugging and configuration
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["ASPNETCORE_URLS"] = $"http://localhost:{_port}";
        startInfo.Environment["Logging__LogLevel__Default"] = "Information";
        startInfo.Environment["Logging__LogLevel__Microsoft"] = "Warning";

        _serverProcess = new Process { StartInfo = startInfo };

        // Capture error output for better debugging
        var errorOutput = new StringBuilder();
        var outputLock = new object();

        // Handle server output and errors
        _serverProcess.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _logger.LogDebug($"[WEB SERVER OUTPUT] {e.Data}");
            }
        };

        _serverProcess.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                lock (outputLock)
                {
                    errorOutput.AppendLine(e.Data);
                }
                _logger.LogError($"[WEB SERVER ERROR] {e.Data}");
            }
        };

        _serverProcess.Start();
        _serverProcess.BeginOutputReadLine();
        _serverProcess.BeginErrorReadLine();

        // Wait for the web server to be ready with retry logic
        var maxRetries = 30; // 30 seconds total
        var retryDelay = 1000; // 1 second between retries
        var serverReady = false;

        _logger.LogInformation("Testing web server readiness by attempting HTTP requests...");

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Check if the server process is still running
                if (_serverProcess.HasExited)
                {
                    string errorDetails = "";
                    lock (outputLock)
                    {
                        if (errorOutput.Length > 0)
                        {
                            errorDetails = $"\nError output:\n{errorOutput}";
                        }
                    }
                    throw new InvalidOperationException($"Web server process exited with code {_serverProcess.ExitCode} on port {_port}{errorDetails}");
                }

                // Test server readiness with a simple HTTP request
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                var healthCheckUrl = $"{ServerUrl}/health";

                try
                {
                    var response = await httpClient.GetAsync(healthCheckUrl);
                    // Even if health endpoint doesn't exist, getting any HTTP response means server is running
                    serverReady = true;
                    _logger.LogInformation($"Web server ready on {ServerUrl} after {attempt} attempt(s)");
                    break;
                }
                catch (HttpRequestException) when (attempt < maxRetries)
                {
                    // Server not ready yet, continue retrying
                    _logger.LogDebug($"Attempt {attempt}: Web server not responding to HTTP requests yet. Retrying...");
                    await Task.Delay(retryDelay);
                    continue;
                }
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                _logger.LogDebug($"Web server startup attempt {attempt} failed: {ex.Message}. Retrying in {retryDelay}ms...");
                await Task.Delay(retryDelay);
            }
        }

        if (!serverReady)
        {
            string errorDetails = "";
            lock (outputLock)
            {
                if (errorOutput.Length > 0)
                {
                    errorDetails = $"\nError output:\n{errorOutput}";
                }
            }

            string processStatus = _serverProcess.HasExited
                ? $"Process exited with code {_serverProcess.ExitCode}"
                : "Process still running but not responding";

            throw new TimeoutException($"Web server did not become ready after {maxRetries} attempts on {ServerUrl}. {processStatus}{errorDetails}");
        }

        _logger.LogInformation($"In-process MCP web server started successfully on {ServerUrl}");
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing in-process MCP web server...");

        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            _serverProcess.Kill();
            _serverProcess.WaitForExit(5000);
            _serverProcess.Dispose();
        }

        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();

        _logger.LogInformation("In-process MCP web server disposed");
    }
}

/// <summary>
/// HTTP MCP client that communicates with the web server via HTTP requests
/// </summary>
public class HttpMcpClient : IDisposable
{
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private int _requestId = 1;
    private string? _sessionId;

    public HttpMcpClient(ILogger logger, string baseUrl)
    {
        _logger = logger;
        _baseUrl = baseUrl;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };

        // Set Accept headers as required by the MCP server
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
    }

    public async Task StartAsync()
    {
        _logger.LogInformation($"Testing web server readiness at {_baseUrl}...");

        var maxRetries = 30; // 30 seconds total
        var retryDelay = 1000; // 1 second between retries
        var initialized = false;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Try to send an initialize request
                await SendInitializeAsync();

                initialized = true;
                _logger.LogInformation($"Web server ready after {attempt} attempt(s)");
                break;
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                _logger.LogDebug($"Initialization attempt {attempt} failed: {ex.Message}. Retrying in {retryDelay}ms...");
                await Task.Delay(retryDelay);
            }
        }

        if (!initialized)
        {
            throw new TimeoutException($"Web server did not become ready after {maxRetries} attempts");
        }

        _logger.LogInformation("HTTP MCP client connected to web server and server is ready");
    }

    public async Task<JObject> SendInitializeAsync()
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = _requestId++,
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
                    name = "web-integration-test-client",
                    version = "1.0.0"
                }
            }
        };

        return await SendRequestAsync(request);
    }

    public async Task<JObject> SendListToolsAsync()
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = _requestId++,
            method = "tools/list"
        };

        return await SendRequestAsync(request);
    }

    public async Task<JObject> SendToolCallAsync(string toolName, object arguments)
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = _requestId++,
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments = arguments
            }
        };

        return await SendRequestAsync(request);
    }

    private async Task<JObject> SendRequestAsync(object request)
    {
        var requestJson = JsonConvert.SerializeObject(request);
        _logger.LogDebug($"[HTTP CLIENT REQUEST] {JObject.Parse(requestJson).ToString(Formatting.Indented)}");

        try
        {
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            // Add session ID header for non-initialize requests
            var headers = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(_sessionId))
            {
                headers["Mcp-Session-Id"] = _sessionId;
            }

            foreach (var header in headers)
            {
                _httpClient.DefaultRequestHeaders.Remove(header.Key);
                _httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
            }

            // The MCP HTTP endpoint is at the root /
            var response = await _httpClient.PostAsync("/", content);
            
            // Capture session ID from response headers if present
            if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessionIdValues))
            {
                _sessionId = sessionIdValues.FirstOrDefault();
                _logger.LogDebug($"Captured session ID from response: {_sessionId}");
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"HTTP {response.StatusCode}: {errorContent}");
            }

            var responseText = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(responseText))
                throw new InvalidOperationException("No response from web server (empty or null)");

            // Parse Server-Sent Events format
            var responseJson = ExtractJsonFromSSE(responseText);
            var responseObject = JObject.Parse(responseJson);
            _logger.LogDebug($"[WEB SERVER RESPONSE] {responseObject.ToString(Formatting.Indented)}");

            return responseObject;
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"HTTP request failed: {ex.Message}");
            throw;
        }
    }

    private string ExtractJsonFromSSE(string sseResponse)
    {
        // Response format is "event: message\ndata: {json}\n"
        var lines = sseResponse.Split('\n');
        foreach (var line in lines)
        {
            if (line.StartsWith("data: "))
            {
                return line.Substring(6); // Remove "data: " prefix
            }
        }
        throw new InvalidOperationException($"No data line found in SSE response: {sseResponse}");
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing HTTP MCP client...");
        _httpClient?.Dispose();
    }
}