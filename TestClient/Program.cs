using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Text;

namespace TestClient;

public class McpTestClient
{
    private readonly ILogger<McpTestClient> _logger;
    private readonly bool _autoStartServer;
    private Process? _serverProcess;
    private int _requestId = 1;

    public McpTestClient(ILogger<McpTestClient> logger, bool autoStartServer = true)
    {
        _logger = logger;
        _autoStartServer = autoStartServer;
    }

    public async Task StartAsync()
    {
        Console.WriteLine("=== MCP PowerShell Server Test Client ===");
        Console.WriteLine("This client allows you to interactively test the MCP server.");

        if (_autoStartServer)
        {
            Console.WriteLine("Type 'help' for available commands or 'quit' to exit.\n");
        }
        else
        {
            Console.WriteLine("Server auto-start is disabled. Use 'start-server' command to manually start the server.");
            Console.WriteLine("Type 'help' for available commands or 'quit' to exit.\n");
        }

        try
        {
            if (_autoStartServer)
            {
                await StartServerAsync();
            }
            await RunInteractiveLoopAsync();
        }
        finally
        {
            if (_autoStartServer)
            {
                await StopServerAsync();
            }
        }
    }

    private async Task StartServerAsync()
    {
        _logger.LogInformation("Starting MCP server from {WorkingDirectory}...", Environment.CurrentDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "run --project PoshMcpServer/PoshMcp.csproj",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = "/home/stmuraws/source/PoshMcp" // Use absolute path to solution root
        };

        _serverProcess = new Process { StartInfo = startInfo };

        // Handle server output
        _serverProcess.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                try
                {
                    var json = JObject.Parse(e.Data);
                    Console.WriteLine($"[SERVER RESPONSE] {json.ToString(Formatting.Indented)}");
                }
                catch
                {
                    Console.WriteLine($"[SERVER] {e.Data}");
                }
            }
        };

        _serverProcess.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                Console.WriteLine($"[SERVER ERROR] {e.Data}");
            }
        };

        _serverProcess.Start();
        _serverProcess.BeginOutputReadLine();
        _serverProcess.BeginErrorReadLine();

        // Wait a moment for the server to start
        await Task.Delay(2000);

        // Send initialization request
        await SendInitializeRequestAsync();

        _logger.LogInformation("MCP server started successfully");
    }

    private async Task StopServerAsync()
    {
        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            _logger.LogInformation("Stopping MCP server...");
            _serverProcess.Kill();
            await _serverProcess.WaitForExitAsync();
            _serverProcess.Dispose();
        }
    }

    private async Task RunInteractiveLoopAsync()
    {
        while (true)
        {
            Console.Write("\nMCP> ");
            var input = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(input))
                continue;

            if (input.ToLower() == "quit" || input.ToLower() == "exit")
                break;

            await ProcessCommandAsync(input);
        }
    }

    private async Task ProcessCommandAsync(string command)
    {
        try
        {
            switch (command.ToLower())
            {
                case "help":
                    ShowHelp();
                    break;

                case "init":
                    await SendInitializeRequestAsync();
                    break;

                case "list-tools":
                    await SendListToolsRequestAsync();
                    break;

                case "ping":
                    await SendPingRequestAsync();
                    break;

                case "start-server":
                    if (_serverProcess == null || _serverProcess.HasExited)
                    {
                        await StartServerAsync();
                    }
                    else
                    {
                        Console.WriteLine("Server is already running.");
                    }
                    break;

                case "stop-server":
                    await StopServerAsync();
                    break;

                default:
                    if (command.StartsWith("call "))
                    {
                        var toolName = command.Substring(5).Trim();
                        await SendToolCallRequestAsync(toolName);
                    }
                    else if (command.StartsWith("call-with-params "))
                    {
                        var parts = command.Substring(17).Split(' ', 2);
                        var toolName = parts[0];
                        var parameters = parts.Length > 1 ? parts[1] : "";
                        await SendToolCallWithParamsRequestAsync(toolName, parameters);
                    }
                    else if (command.StartsWith("raw "))
                    {
                        var json = command.Substring(4);
                        await SendRawJsonAsync(json);
                    }
                    else
                    {
                        Console.WriteLine($"Unknown command: {command}. Type 'help' for available commands.");
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing command: {ex.Message}");
        }
    }

    private void ShowHelp()
    {
        Console.WriteLine(@"
Available commands:
  help                           - Show this help message
  init                          - Send initialize request
  list-tools                    - List available tools
  ping                          - Send a ping request
  start-server                  - Manually start the MCP server
  stop-server                   - Stop the MCP server
  call <tool-name>              - Call a tool without parameters
  call-with-params <tool> <json> - Call a tool with JSON parameters
  raw <json>                    - Send raw JSON message
  quit/exit                     - Exit the client

Examples:
  call get_azactiongroup
  call-with-params get_azactiongroup {""ResourceGroupName"":""myRG""}
  call-with-params get_process {""Name"":""notepad""}
  raw {""jsonrpc"":""2.0"",""method"":""ping"",""id"":1}
");
    }

    private async Task SendInitializeRequestAsync()
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
                    name = "test-client",
                    version = "1.0.0"
                }
            }
        };

        await SendMessageAsync(request);
    }

    private async Task SendListToolsRequestAsync()
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = _requestId++,
            method = "tools/list"
        };

        await SendMessageAsync(request);
    }

    private async Task SendPingRequestAsync()
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = _requestId++,
            method = "ping"
        };

        await SendMessageAsync(request);
    }

    private async Task SendToolCallRequestAsync(string toolName)
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = _requestId++,
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments = new { }
            }
        };

        await SendMessageAsync(request);
    }

    private async Task SendToolCallWithParamsRequestAsync(string toolName, string parameters)
    {
        object arguments;
        try
        {
            // Parse JSON string into object, or use empty object if empty/null
            arguments = string.IsNullOrWhiteSpace(parameters) ? new { } : JsonConvert.DeserializeObject(parameters) ?? new { };
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Invalid JSON parameters: {ex.Message}");
            arguments = new { };
        }

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

        await SendMessageAsync(request);
    }

    private async Task SendRawJsonAsync(string json)
    {
        try
        {
            // Validate JSON
            JObject.Parse(json);
            await SendRawMessageAsync(json);
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Invalid JSON: {ex.Message}");
        }
    }

    private async Task SendMessageAsync(object message)
    {
        var json = JsonConvert.SerializeObject(message, Formatting.None);
        await SendRawMessageAsync(json);
    }

    private async Task SendRawMessageAsync(string json)
    {
        if (_serverProcess?.StandardInput != null)
        {
            Console.WriteLine($"[CLIENT REQUEST] {JObject.Parse(json).ToString(Formatting.Indented)}");

            await _serverProcess.StandardInput.WriteLineAsync(json);
            await _serverProcess.StandardInput.FlushAsync();
        }
        else
        {
            Console.WriteLine("Server is not running or stdin is not available");
        }
    }
}

class Program
{
    static async Task Main(string[] args)
    {
        // Parse command line arguments
        bool autoStartServer = true;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLower())
            {
                case "--no-auto-start":
                case "-n":
                    autoStartServer = false;
                    break;
                case "--help":
                case "-h":
                    ShowUsage();
                    return;
            }
        }

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .AddConsole()
                .SetMinimumLevel(LogLevel.Information);
        });

        var logger = loggerFactory.CreateLogger<McpTestClient>();
        var client = new McpTestClient(logger, autoStartServer);

        try
        {
            await client.StartAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Environment.Exit(1);
        }
    }

    static void ShowUsage()
    {
        Console.WriteLine(@"
MCP PowerShell Server Test Client

Usage: TestClient [OPTIONS]

Options:
  --no-auto-start, -n    Don't automatically start the MCP server
  --help, -h             Show this help message

Examples:
  TestClient                     # Start with auto-server mode
  TestClient --no-auto-start     # Start in manual mode
");
    }
}
