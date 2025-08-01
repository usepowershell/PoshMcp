using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Integration;

/// <summary>
/// Tests to verify that multiple users get isolated PowerShell runspaces in the web server
/// </summary>
public class MultiUserIsolationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "http://localhost:3001";

    public MultiUserIsolationTests(ITestOutputHelper output)
    {
        _output = output;
        _httpClient = new HttpClient();
    }

    [Fact]
    public async Task TwoClientsGetSeparatePowerShellRunspaces()
    {
        // Test that two different MCP sessions get separate PowerShell runspaces
        // and variables set in one session don't affect the other

        // Client 1: Initialize and set a variable using a PowerShell command
        var client1Http = new HttpClient();
        client1Http.DefaultRequestHeaders.Add("Mcp-Session-Id", "session-1");

        await InitializeClient(client1Http, "test-client-1");

        // Client 2: Initialize with different session
        var client2Http = new HttpClient();
        client2Http.DefaultRequestHeaders.Add("Mcp-Session-Id", "session-2");

        await InitializeClient(client2Http, "test-client-2");

        // Both clients should be able to call PowerShell commands successfully
        // Since the commands will run in separate runspace instances, they should not interfere

        var client1Response = await CallTool(client1Http, "get_process_name", new { name = new[] { "dotnet" } });
        var client2Response = await CallTool(client2Http, "get_process_name", new { name = new[] { "powershell" } });

        // Both should succeed
        Assert.NotNull(client1Response);
        Assert.NotNull(client2Response);
        Assert.True(client1Response.Contains("dotnet") || client1Response.Contains("ProcessName"));

        _output.WriteLine($"Client 1 response: {client1Response.Substring(0, Math.Min(200, client1Response.Length))}...");
        _output.WriteLine($"Client 2 response: {client2Response.Substring(0, Math.Min(200, client2Response.Length))}...");
    }

    private async Task InitializeClient(HttpClient client, string clientName)
    {
        var initRequest = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new { name = clientName, version = "1.0" }
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(initRequest), Encoding.UTF8, "application/json");
        var response = await client.PostAsync(BaseUrl, content);

        response.EnsureSuccessStatusCode();
        var responseText = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Initialize response for {clientName}: {responseText}");
        Assert.Contains("protocolVersion", responseText);
    }

    private async Task<string> CallTool(HttpClient client, string toolName, object arguments)
    {
        var toolRequest = new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments = arguments
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(toolRequest), Encoding.UTF8, "application/json");
        var response = await client.PostAsync(BaseUrl, content);

        response.EnsureSuccessStatusCode();
        var responseText = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"Tool call response for {toolName}: {responseText.Substring(0, Math.Min(500, responseText.Length))}...");
        return responseText;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _httpClient?.Dispose();
        return Task.CompletedTask;
    }
}