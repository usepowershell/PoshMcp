using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Integration;

/// <summary>
/// Tests to verify that multiple users get isolated PowerShell runspaces in the web server
/// </summary>
public class MultiUserIsolationTests : PowerShellTestBase, IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private InProcessWebServer? _server;
    private string _baseUrl = string.Empty;

    public MultiUserIsolationTests(ITestOutputHelper output) : base(output)
    {
        _output = output;
    }

    [Fact]
    public async Task TwoClientsGetSeparatePowerShellRunspaces()
    {
        // Test that two different MCP clients get separate PowerShell runspaces
        // The MCP HTTP transport manages sessions automatically - each client gets its own session
        // Our SessionAwarePowerShellRunspace should create separate runspaces for each session

        Logger.LogInformation("=== Starting MultiUser TwoClientsGetSeparatePowerShellRunspaces Test ===");

        // Create two separate MCP clients - each will get its own MCP session automatically
        var client1 = new HttpMcpClient(Logger, _baseUrl);
        var client2 = new HttpMcpClient(Logger, _baseUrl);

        try
        {
            // Initialize both clients - this establishes separate MCP sessions
            Logger.LogInformation("Initializing client 1...");
            await client1.StartAsync();

            Logger.LogInformation("Initializing client 2...");
            await client2.StartAsync();

            // Both clients should be able to call PowerShell commands successfully
            // Since each client has its own MCP session, the SessionAwarePowerShellRunspace
            // will create separate PowerShell runspaces for each one

            Logger.LogInformation("Calling get_some_data from client 1...");
            var client1Response = await client1.SendToolCallAsync("get_some_data", new { test = "session-1-data" });
            Assert.NotNull(client1Response);
            var client1Text = client1Response.ToString();
            Assert.Contains("session-1-data", client1Text);
            
            // Verify that client2 does not see the output from client1's command execution
            // get_last_command_output should return null
            Logger.LogInformation("Calling get_last_command_output from client 2...");
            var client2Response = await client2.SendToolCallAsync("get_last_command_output", new { });
            var client2Text = client2Response.ToString();
            Assert.NotNull(client2Response);
            Assert.Contains("null", client2Text);
   
            Logger.LogInformation($"Client 1 response: {client1Text.Substring(0, Math.Min(200, client1Text.Length))}...");
            Logger.LogInformation($"Client 2 response: {client2Text.Substring(0, Math.Min(200, client2Text.Length))}...");

            Logger.LogInformation("=== MultiUser test completed successfully - both clients executed in separate runspaces ===");
        }
        finally
        {
            // Cleanup
            client1.Dispose();
            client2.Dispose();
        }
    }


    public async Task InitializeAsync()
    {
        Logger.LogInformation("=== Initializing MultiUserIsolationTests - starting web server ===");
        _server = new InProcessWebServer(Logger);
        await _server.StartAsync();
        _baseUrl = _server.ServerUrl;
        Logger.LogInformation($"=== Web server started at {_baseUrl} ===");
    }

    public Task DisposeAsync()
    {
        Logger.LogInformation("=== Disposing MultiUserIsolationTests - stopping web server ===");
        _server?.Dispose();
        Logger.LogInformation("=== Web server disposed ===");
        return Task.CompletedTask;
    }
}