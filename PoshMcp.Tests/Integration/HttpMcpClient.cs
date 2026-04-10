using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PoshMcp.Tests.Integration;

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
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public string? SessionId => _sessionId;

    public HttpMcpClient(ILogger logger, string baseUrl)
    {
        _logger = logger;
        _baseUrl = baseUrl;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = RequestTimeout
        };

        // Set Accept headers as required by the MCP server
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
    }

    public async Task StartAsync()
    {
        _logger.LogInformation($"Testing web server readiness at {_baseUrl}...");

        var startupTimeout = TimeSpan.FromSeconds(45);
        var retryDelay = 1000; // 1 second between retries
        var initialized = false;
        var attempt = 0;
        var startupStopwatch = Stopwatch.StartNew();

        while (startupStopwatch.Elapsed < startupTimeout)
        {
            attempt++;

            try
            {
                // Try to send an initialize request
                await SendInitializeAsync();

                initialized = true;
                _logger.LogInformation($"Web server ready after {attempt} attempt(s)");
                break;
            }
            catch (Exception ex) when (startupStopwatch.Elapsed < startupTimeout)
            {
                _logger.LogDebug($"Initialization attempt {attempt} failed: {ex.Message}. Retrying in {retryDelay}ms...");
                await Task.Delay(retryDelay);
            }
        }

        if (!initialized)
        {
            throw new TimeoutException($"Web server did not become ready after {attempt} attempts in {startupTimeout.TotalSeconds:0} seconds");
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

            // Parse first Server-Sent Events payload line without waiting for stream end.
            var responseJson = await ExtractJsonFromSSEAsync(response.Content);
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

    private static async Task<string> ExtractJsonFromSSEAsync(HttpContent content)
    {
        await using var stream = await content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line == null)
            {
                break;
            }

            if (line.StartsWith("data: "))
            {
                return line.Substring(6); // Remove "data: " prefix
            }
        }

        throw new InvalidOperationException("No data line found in SSE response stream");
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing HTTP MCP client...");
        _httpClient?.Dispose();
    }
}
