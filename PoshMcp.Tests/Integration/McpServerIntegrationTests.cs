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
public class ServerWithExternalClient : PowerShellTestBase, IAsyncLifetime
{
    private InProcessMcpServer? _sharedServer;
    private ExternalMcpClient? _sharedClient;

    public ServerWithExternalClient(ITestOutputHelper output) : base(output)
    {
    }

    public async Task InitializeAsync()
    {
        Logger.LogInformation("=== Initializing shared MCP server and client for integration tests ===");

        _sharedServer = new InProcessMcpServer(Logger);
        await _sharedServer.StartAsync();

        _sharedClient = new ExternalMcpClient(Logger, _sharedServer);
        await _sharedClient.StartAsync();

        // Client is already initialized in StartAsync(), no need to call SendInitializeAsync() again

        Logger.LogInformation("=== Shared MCP server and client initialized successfully ===");
    }

    public Task DisposeAsync()
    {
        Logger.LogInformation("=== Disposing shared MCP server and client ===");

        _sharedClient?.Dispose();
        _sharedServer?.Dispose();

        Logger.LogInformation("=== Shared MCP server and client disposed ===");

        return Task.CompletedTask;
    }

    [Fact]
    public async Task ShouldInitializeAndListTools()
    {
        Logger.LogInformation("=== Starting ShouldInitializeAndListTools Test ===");

        // Use shared client instance
        var client = _sharedClient ?? throw new InvalidOperationException("Shared client not initialized");

        // List available tools
        var toolsResponse = await client.SendListToolsAsync();
        Assert.NotNull(toolsResponse);

        Logger.LogInformation($"Full tools response: {toolsResponse.ToString(Formatting.Indented)}");

        Assert.True(toolsResponse["result"]?["tools"] != null,
            $"Tools result is null. Full response: {toolsResponse.ToString(Formatting.None)}");

        var tools = toolsResponse["result"]?["tools"] as JArray;
        Assert.NotNull(tools);

        Logger.LogInformation($"Tools array count: {tools.Count}");

        // There should be at least some tools available (5 from config + any built-in tools)
        Assert.True(tools.Count > 0, $"Expected tools but found {tools.Count}. Full response: {toolsResponse.ToString(Formatting.None)}");

        Logger.LogInformation($"Found {tools.Count} tools via external client");

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
        Logger.LogInformation("=== Starting ShouldExecutePowerShellCommand Test ===");

        // Use shared client instance
        var client = _sharedClient ?? throw new InvalidOperationException("Shared client not initialized");

        // Verify tools are available first
        var toolsResponse = await client.SendListToolsAsync();
        Assert.NotNull(toolsResponse);

        Logger.LogInformation($"Tools response for PowerShell test: {toolsResponse.ToString(Formatting.Indented)}");

        Assert.True(toolsResponse["result"]?["tools"] != null,
            $"Tools result is null in PowerShell test. Full response: {toolsResponse.ToString(Formatting.None)}");

        var tools = toolsResponse["result"]?["tools"] as JArray;
        Assert.NotNull(tools);
        Assert.True(tools.Count > 0, $"No tools available for PowerShell test. Response: {toolsResponse.ToString(Formatting.None)}");

        // Look for a real tool that exists in the current test configuration
        var getProcessTool = tools.FirstOrDefault(t => t["name"]?.ToString() == "get_process_id");
        if (getProcessTool == null)
        {
            Logger.LogWarning("get_process_id tool not found. Available tools:");
            foreach (var tool in tools)
            {
                Logger.LogWarning($"  - {tool["name"]}");
            }
            Assert.Fail("get_process_id tool not found in available tools");
        }

        // Call a stable PowerShell command through MCP
        var currentProcessId = Process.GetCurrentProcess().Id;
        var callResponse = await client.SendToolCallAsync("get_process_id", new JObject
        {
            ["Id"] = new JArray(currentProcessId)
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
        Assert.Contains(currentProcessId.ToString(), textContent, StringComparison.Ordinal);

        Logger.LogInformation("PowerShell command executed successfully via external client");
    }

    [Fact]
    public async Task ShouldListAndCallGetProcessTool()
    {
        Logger.LogInformation("=== Starting ShouldListAndCallGetProcessTool Test ===");

        var client = _sharedClient ?? throw new InvalidOperationException("Shared client not initialized");

        var toolsResponse = await client.SendListToolsAsync();
        Assert.NotNull(toolsResponse);

        var tools = toolsResponse["result"]?["tools"] as JArray;
        Assert.NotNull(tools);

        var getProcessTool = tools.FirstOrDefault(t =>
            string.Equals(t["name"]?.ToString(), "get_process_id", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(getProcessTool);

        var currentProcessId = Process.GetCurrentProcess().Id;
        var callResponse = await client.SendToolCallAsync("get_process_id", new JObject
        {
            ["Id"] = new JArray(currentProcessId)
        });

        Assert.NotNull(callResponse);
        Assert.Null(callResponse["error"]);
        Assert.NotEqual(true, callResponse["result"]?["isError"]?.Value<bool>());

        var content = callResponse["result"]?["content"] as JArray;
        Assert.NotNull(content);
        Assert.True(content.Count > 0);

        var textContent = content[0]?["text"]?.ToString();
        Assert.False(string.IsNullOrWhiteSpace(textContent));
        Assert.Contains(currentProcessId.ToString(), textContent, StringComparison.Ordinal);

        Logger.LogInformation("Get-Process tool call succeeded for PID {Pid}", currentProcessId);
    }

    [Fact]
    public async Task ShouldHandleErrors()
    {
        Logger.LogInformation("=== Starting ShouldHandleErrors Test ===");

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

        Logger.LogInformation($"Error handled correctly via external client: {errorMessage}");
    }


    #region Helper Methods (No longer needed - using shared instances)

    // These methods are no longer used since we're using shared server/client instances
    // Left here for reference but could be removed

    #endregion
}

public class McpServerProcessLifecycleTests : PowerShellTestBase
{
    public McpServerProcessLifecycleTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task InProcessMcpServer_Dispose_ShouldTerminateServerProcess()
    {
        var server = new InProcessMcpServer(Logger);
        await server.StartAsync();

        var serverProcessId = server.GetServerProcess().Id;
        server.Dispose();

        await Task.Delay(250);

        Assert.False(IsProcessAlive(serverProcessId),
            $"Expected stdio server process {serverProcessId} to be terminated during dispose.");
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

/// <summary>
/// In-process MCP server that can be debugged while external clients connect
/// </summary>
public class InProcessMcpServer : IDisposable
{
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly string? _extraArgs;
    private readonly string? _explicitConfigPath;
    private readonly List<string> _standardErrorLines = new();
    private readonly object _standardErrorLock = new();
    private Process? _serverProcess;

    public bool IsRunning => _serverProcess != null && !_serverProcess.HasExited;

    public InProcessMcpServer(ILogger logger, string? extraArgs = null, string? explicitConfigPath = null)
    {
        _logger = logger;
        _cancellationTokenSource = new CancellationTokenSource();
        _extraArgs = extraArgs;
        _explicitConfigPath = explicitConfigPath;
    }

    public async Task StartAsync()
    {
        _logger.LogInformation("Starting in-process MCP server with external stdio...");

        // Get the absolute path to the server project
        // Navigate from the test output directory to the workspace root
        var currentDirectory = Directory.GetCurrentDirectory();
        var workspaceRoot = currentDirectory;

        // Navigate up until we find the workspace root (containing PoshMcp.sln)
        while (!File.Exists(Path.Combine(workspaceRoot, "PoshMcp.sln")) &&
               Path.GetDirectoryName(workspaceRoot) != null)
        {
            workspaceRoot = Path.GetDirectoryName(workspaceRoot)!;
        }

        var serverProjectPath = Path.Combine(workspaceRoot, "PoshMcp.Server", "PoshMcp.csproj");
        var buildConfiguration = ResolveBuildConfiguration();

        if (!File.Exists(serverProjectPath))
        {
            throw new FileNotFoundException($"Server project not found at: {serverProjectPath}. Workspace root: {workspaceRoot}, Current directory: {currentDirectory}");
        }

        // Start the actual server process that external clients can connect to
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = BuildRunArguments(buildConfiguration, serverProjectPath),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = currentDirectory
        };

        _serverProcess = new Process { StartInfo = startInfo };

        // Handle server errors only - stdout is used for MCP communication
        _serverProcess.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                lock (_standardErrorLock)
                {
                    _standardErrorLines.Add(e.Data);
                }

                _logger.LogError($"[SERVER ERROR] {e.Data}");
            }
        };

        try
        {
            _serverProcess.Start();
            TestProcessRegistry.Register(_serverProcess);
            _serverProcess.BeginErrorReadLine();
            // DO NOT call BeginOutputReadLine() because we need to read stdout ourselves for MCP communication

            // Give the process a moment to start before we begin testing connectivity
            await Task.Delay(3000);

            if (_serverProcess.HasExited)
            {
                throw new InvalidOperationException($"Server process exited with code {_serverProcess.ExitCode}");
            }

            _logger.LogInformation("In-process MCP server process started successfully");
        }
        catch
        {
            StopServerProcess();
            throw;
        }
    }

    public Process GetServerProcess() => _serverProcess ?? throw new InvalidOperationException("Server not started");

    public IReadOnlyList<string> GetStandardErrorLines()
    {
        lock (_standardErrorLock)
        {
            return _standardErrorLines.ToList();
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

    private string BuildRunArguments(string buildConfiguration, string serverProjectPath)
    {
        var arguments = $"run --no-build --configuration {buildConfiguration} --project \"{serverProjectPath}\"";
        var appArgs = _extraArgs?.Trim();

        if (!string.IsNullOrWhiteSpace(_explicitConfigPath))
        {
            var normalizedConfigPath = Path.GetFullPath(_explicitConfigPath);
            if (string.IsNullOrWhiteSpace(appArgs))
            {
                appArgs = $"serve --config \"{normalizedConfigPath}\"";
            }
            else if (!appArgs.Contains("--config", StringComparison.OrdinalIgnoreCase))
            {
                appArgs += $" --config \"{normalizedConfigPath}\"";
            }
        }

        if (!string.IsNullOrWhiteSpace(appArgs))
        {
            arguments += $" -- {appArgs}";
        }

        return arguments;
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing in-process MCP server...");

        StopServerProcess();

        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();

        _logger.LogInformation("In-process MCP server disposed");
    }

    private void StopServerProcess()
    {
        if (_serverProcess == null)
        {
            return;
        }

        try
        {
            if (!_serverProcess.HasExited)
            {
                _serverProcess.Kill(entireProcessTree: true);
                _serverProcess.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to stop stdio server process cleanly");
        }
        finally
        {
            TestProcessRegistry.Unregister(_serverProcess);
            _serverProcess.Dispose();
            _serverProcess = null;
        }
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
    private readonly SemaphoreSlim _streamSemaphore = new(1, 1); // Ensure only one request/response at a time
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    public ExternalMcpClient(ILogger logger, InProcessMcpServer server)
    {
        _logger = logger;
        _server = server;
    }

    public async Task StartAsync()
    {
        _serverProcess = _server.GetServerProcess();

        // Test server readiness by attempting to initialize until it succeeds
        var startupTimeout = TimeSpan.FromSeconds(45);
        var retryDelay = 1000; // 1 second between retries
        var initialized = false;
        var attempt = 0;
        var startupStopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Testing server readiness by attempting initialization...");

        while (startupStopwatch.Elapsed < startupTimeout)
        {
            attempt++;

            try
            {
                // Check if the server process is still running
                if (_serverProcess.HasExited)
                {
                    throw new InvalidOperationException($"Server process exited with code {_serverProcess.ExitCode}");
                }

                // Try to send an initialize request
                await SendInitializeAsync();

                // Additional verification: Check that tools are actually available
                // This ensures the server has completed tool discovery/registration
                var toolsResponse = await SendListToolsAsync();
                var tools = toolsResponse["result"]?["tools"] as JArray;

                if (tools != null && tools.Count > 0)
                {
                    initialized = true;
                    _logger.LogInformation($"Server ready with {tools.Count} tools after {attempt} attempt(s)");
                    break;
                }
                else
                {
                    _logger.LogDebug($"Attempt {attempt}: Server initialized but no tools available yet. Retrying...");
                    await Task.Delay(retryDelay);
                    continue;
                }
            }
            catch (Exception ex) when (startupStopwatch.Elapsed < startupTimeout)
            {
                _logger.LogDebug($"Initialization attempt {attempt} failed: {ex.Message}. Retrying in {retryDelay}ms...");
                await Task.Delay(retryDelay);
            }
        }

        if (!initialized)
        {
            throw new TimeoutException($"Server did not become ready with tools after {attempt} attempts in {startupTimeout.TotalSeconds:0} seconds");
        }

        _logger.LogInformation("External MCP client connected to server stdio and server is ready with tools");
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
        // Use semaphore to ensure thread-safe access to streams
        await _streamSemaphore.WaitAsync();

        try
        {
            if (_serverProcess?.StandardInput == null || _serverProcess?.StandardOutput == null)
                throw new InvalidOperationException("Server process not available");

            if (_serverProcess.HasExited)
                throw new InvalidOperationException($"Server process has exited with code {_serverProcess.ExitCode}");

            var requestJson = JsonConvert.SerializeObject(request);
            _logger.LogDebug($"[CLIENT REQUEST] {JObject.Parse(requestJson).ToString(Formatting.Indented)}");

            try
            {
                // Send request to server (synchronized)
                await _serverProcess.StandardInput.WriteLineAsync(requestJson);
                await _serverProcess.StandardInput.FlushAsync();

                // Cancel the read when timed out so we do not leave orphaned reads on the shared stdout stream.
                using var responseTimeout = new CancellationTokenSource(RequestTimeout);
                var responseJson = await _serverProcess.StandardOutput.ReadLineAsync(responseTimeout.Token);
                if (string.IsNullOrEmpty(responseJson))
                    throw new InvalidOperationException("No response from server (empty or null)");

                var response = JObject.Parse(responseJson);
                _logger.LogDebug($"[SERVER RESPONSE] {response.ToString(Formatting.Indented)}");

                return response;
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"Server did not respond within {RequestTimeout.TotalSeconds:0} seconds");
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Request failed: {ex.Message}");
                throw;
            }
        }
        finally
        {
            _streamSemaphore.Release();
        }
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing external MCP client...");
        // Server process is owned by InProcessMcpServer, don't dispose it here
    }
}
