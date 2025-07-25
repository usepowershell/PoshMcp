using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Integration;

/// <summary>
/// Integration tests that run the MCP server in-process and communicate with external clients
/// </summary>
public class ServerWithExternalClient : PowerShellTestBase
{
    public ServerWithExternalClient(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task ShouldInitializeAndListTools()
    {
        Logger.LogInformation("=== Starting ShouldInitializeAndListTools Test ===");

        // Start the MCP server in-process
        using var server = await StartInProcessMcpServerAsync();

        // Start external client process and send requests
        using var client = await StartExternalClientAsync(server);

        // Send initialize request
        var initResponse = await client.SendInitializeAsync();
        Assert.NotNull(initResponse);
        Assert.Equal("2.0", initResponse["jsonrpc"]?.ToString());

        Logger.LogInformation("Server initialized successfully via external client");

        // List available tools
        var toolsResponse = await client.SendListToolsAsync();
        Assert.NotNull(toolsResponse);
        Assert.True(toolsResponse["result"]?["tools"] != null);

        var tools = toolsResponse["result"]?["tools"] as JArray;
        Assert.NotNull(tools);
        Assert.True(tools.Count > 0);

        Logger.LogInformation($"Found {tools.Count} tools via external client");

        foreach (var tool in tools)
        {
            var toolName = tool["name"]?.ToString();
            var toolDescription = tool["description"]?.ToString();
            Logger.LogInformation($"  Tool: {toolName} - {toolDescription}");
        }
    }

    [Fact]
    public async Task ShouldExecutePowerShellCommand()
    {
        Logger.LogInformation("=== Starting ShouldExecutePowerShellCommand Test ===");

        // Start the MCP server in-process  
        using var server = await StartInProcessMcpServerAsync();

        // Start external client process
        using var client = await StartExternalClientAsync(server);

        // Initialize client
        await client.SendInitializeAsync();

        // add a check to ensure the tool is available
        var toolsResponse = await client.SendListToolsAsync();
        Assert.NotNull(toolsResponse);
        Assert.True(toolsResponse["result"]?["tools"] != null);

        // Call the PowerShell function through MCP
        var callResponse = await client.SendToolCallAsync("get_process_name", new
        {
            name = "dotnet"
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

        Logger.LogInformation($"Tool call result from external client: {textContent}");

        // Verify the output contains expected data
        Assert.Contains("dotnet", textContent);

        Logger.LogInformation("PowerShell command executed successfully via external client");
    }

    [Fact]
    public async Task ShouldHandleErrors()
    {
        Logger.LogInformation("=== Starting ShouldHandleErrors Test ===");

        // Start the MCP server in-process
        using var server = await StartInProcessMcpServerAsync();

        // Start external client process
        using var client = await StartExternalClientAsync(server);

        // Initialize client
        await client.SendInitializeAsync();

        // Try to call a non-existent tool
        var errorResponse = await client.SendToolCallAsync("NonExistentTool", new { });

        // Assert: Verify error handling
        Assert.NotNull(errorResponse);
        Assert.NotNull(errorResponse["error"]);

        var errorMessage = errorResponse["error"]?["message"]?.ToString();
        Assert.NotNull(errorMessage);
        Assert.Contains("Unknown tool:", errorMessage, StringComparison.OrdinalIgnoreCase);

        Logger.LogInformation($"Error handled correctly via external client: {errorMessage}");
    }


    #region Helper Methods

    private async Task<InProcessMcpServer> StartInProcessMcpServerAsync()
    {
        Logger.LogInformation("Starting in-process MCP server for external client testing...");

        var server = new InProcessMcpServer(Logger);
        await server.StartAsync();

        Logger.LogInformation("In-process MCP server started successfully");
        return server;
    }

    private async Task<ExternalMcpClient> StartExternalClientAsync(InProcessMcpServer server)
    {
        Logger.LogInformation("Starting external MCP client process...");

        var client = new ExternalMcpClient(Logger, server);
        await client.StartAsync();

        Logger.LogInformation("External MCP client started successfully");
        return client;
    }

    #endregion
}

/// <summary>
/// In-process MCP server that can be debugged while external clients connect
/// </summary>
public class InProcessMcpServer : IDisposable
{
    private readonly ILogger _logger;
    private IHost? _host;
    private List<McpServerTool>? _tools;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private Process? _serverProcess;

    public bool IsRunning => _host != null;

    public InProcessMcpServer(ILogger logger)
    {
        _logger = logger;
        _cancellationTokenSource = new CancellationTokenSource();
    }

    public async Task StartAsync()
    {
        _logger.LogInformation("Starting in-process MCP server with external stdio...");

        // Start the actual server process that external clients can connect to
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "run --project PoshMcpServer/PoshMcp.csproj",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = "/home/stmuraws/source/PoshMcp"
        };

        _serverProcess = new Process { StartInfo = startInfo };

        // Handle server errors
        _serverProcess.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _logger.LogError($"[SERVER ERROR] {e.Data}");
            }
        };

        _serverProcess.Start();
        _serverProcess.BeginErrorReadLine();

        // Wait a moment for the server to start
        await Task.Delay(2000);

        _logger.LogInformation("In-process MCP server with stdio started successfully");
    }

    public Process GetServerProcess() => _serverProcess ?? throw new InvalidOperationException("Server not started");

    public void Dispose()
    {
        _logger.LogInformation("Disposing in-process MCP server...");

        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            _serverProcess.Kill();
            _serverProcess.WaitForExit(5000);
            _serverProcess.Dispose();
        }

        _cancellationTokenSource.Cancel();
        _host?.Dispose();
        _cancellationTokenSource.Dispose();
        _tools = null;

        _logger.LogInformation("In-process MCP server disposed");
    }
}

/// <summary>
/// External MCP client that communicates with the server via stdio
/// </summary>
public class ExternalMcpClient : IDisposable
{
    private readonly ILogger _logger;
    private readonly InProcessMcpServer _server;
    private Process? _serverProcess;
    private int _requestId = 1;

    public ExternalMcpClient(ILogger logger, InProcessMcpServer server)
    {
        _logger = logger;
        _server = server;
    }

    public async Task StartAsync()
    {
        _serverProcess = _server.GetServerProcess();

        // Wait a bit more for full initialization
        await Task.Delay(1000);

        _logger.LogInformation("External MCP client connected to server stdio");
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
                    name = "integration-test-client",
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
        if (_serverProcess?.StandardInput == null || _serverProcess?.StandardOutput == null)
            throw new InvalidOperationException("Server process not available");

        var requestJson = JsonConvert.SerializeObject(request);
        _logger.LogDebug($"[CLIENT REQUEST] {JObject.Parse(requestJson).ToString(Formatting.Indented)}");

        // Send request to server
        await _serverProcess.StandardInput.WriteLineAsync(requestJson);
        await _serverProcess.StandardInput.FlushAsync();

        // Read response from server
        var responseJson = await _serverProcess.StandardOutput.ReadLineAsync();
        if (string.IsNullOrEmpty(responseJson))
            throw new InvalidOperationException("No response from server");

        var response = JObject.Parse(responseJson);
        _logger.LogDebug($"[SERVER RESPONSE] {response.ToString(Formatting.Indented)}");

        return response;
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing external MCP client...");
        // Server process is owned by InProcessMcpServer, don't dispose it here
    }
}
