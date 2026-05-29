using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using PoshMcp.Server.McpResources;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.OutOfProcess;
using Xunit;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.Tests.Unit.McpResources;

[Trait("Category", "Unit")]
public class McpResourceHandlerTests
{
    [Fact]
    public async Task HandleReadAsync_CommandSource_WithSimpleCommandAndExecutor_UsesExecutor()
    {
        var config = CreateConfig("Get-BamiTenantConfiguration");
        var executor = new Mock<ICommandExecutor>();
        executor
            .Setup(e => e.InvokeAsync(
                "Get-BamiTenantConfiguration",
                It.Is<IDictionary<string, object?>>(p => p.Count == 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"tenant\":\"ok\"}");

        var handler = new McpResourceHandler(
            config,
            new ThrowingRunspace(),
            ".",
            NullLogger<McpResourceHandler>.Instance,
            executor.Object);

        var result = await handler.HandleReadAsync(CreateReadContext(), CancellationToken.None);

        var text = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents)).Text;
        Assert.Equal("{\"tenant\":\"ok\"}", text);
        executor.VerifyAll();
    }

    [Fact]
    public async Task HandleReadAsync_CommandSource_WithScriptCommandAndExecutor_UsesRunspace()
    {
        var config = CreateConfig("'from-runspace' | Out-String");
        var executor = new Mock<ICommandExecutor>(MockBehavior.Strict);

        using var runspace = new IsolatedPowerShellRunspace();
        var handler = new McpResourceHandler(
            config,
            runspace,
            ".",
            NullLogger<McpResourceHandler>.Instance,
            executor.Object);

        var result = await handler.HandleReadAsync(CreateReadContext(), CancellationToken.None);

        var text = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents)).Text;
        Assert.Contains("from-runspace", text, StringComparison.Ordinal);
    }

    private static McpResourcesConfiguration CreateConfig(string command) => new()
    {
        Resources =
        {
            new McpResourceConfiguration
            {
                Uri = "poshmcp://resources/test",
                Name = "Test",
                Source = "command",
                Command = command,
                MimeType = "application/json"
            }
        }
    };

    private static RequestContext<ReadResourceRequestParams> CreateReadContext() => new(
        new Mock<McpServer>().Object,
        new JsonRpcRequest { Method = "resources/read" },
        new ReadResourceRequestParams { Uri = "poshmcp://resources/test" });

    private sealed class ThrowingRunspace : IPowerShellRunspace
    {
        public PSPowerShell Instance => throw new InvalidOperationException("Runspace should not be used.");

        public T ExecuteThreadSafe<T>(Func<PSPowerShell, T> operation) =>
            throw new InvalidOperationException("Runspace should not be used.");

        public void ExecuteThreadSafe(Action<PSPowerShell> operation) =>
            throw new InvalidOperationException("Runspace should not be used.");

        public Task<T> ExecuteThreadSafeAsync<T>(Func<PSPowerShell, Task<T>> operation) =>
            throw new InvalidOperationException("Runspace should not be used.");

        public void Dispose()
        {
        }
    }
}