using Microsoft.Extensions.Logging;
using PoshMcp.Server.Metrics;
using PoshMcp.Server.PowerShell;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Unit;

public class MetricsTests
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<MetricsTests> _logger;

    public MetricsTests(ITestOutputHelper output)
    {
        _output = output;
        var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _logger = loggerFactory.CreateLogger<MetricsTests>();
    }

    [Fact]
    public void McpMetrics_ShouldInitializeSuccessfully()
    {
        // Arrange & Act
        var metrics = new McpMetrics();

        // Assert
        Assert.NotNull(metrics);
        Assert.NotNull(metrics.ToolInvocationTotal);
        Assert.NotNull(metrics.ToolExecutionDurationSeconds);
        Assert.NotNull(metrics.ToolExecutionErrorsTotal);
        Assert.NotNull(metrics.ToolRegistrationTotal);

        _output.WriteLine("McpMetrics initialized successfully with all counters and histograms");
    }

    [Fact]
    public void McpToolFactoryV2_ShouldAcceptMetricsConfiguration()
    {
        // Arrange
        var metrics = new McpMetrics();

        // Act
        McpToolFactoryV2.SetMetrics(metrics);

        // Assert - no exception should be thrown
        _output.WriteLine("McpToolFactoryV2 accepted metrics configuration successfully");
    }

    [Fact]
    public void PowerShellAssemblyGenerator_ShouldAcceptMetricsConfiguration()
    {
        // Arrange
        var metrics = new McpMetrics();

        // Act
        PowerShellAssemblyGenerator.SetMetrics(metrics);

        // Assert - no exception should be thrown
        _output.WriteLine("PowerShellAssemblyGenerator accepted metrics configuration successfully");
    }

    [Fact]
    public async Task McpMetrics_ShouldRecordToolInvocation()
    {
        // Arrange
        var metrics = new McpMetrics();

        // Act
        metrics.ToolInvocationTotal.Add(1,
            new System.Diagnostics.TagList { { "tool_name", "test-tool" }, { "status", "success" } });

        metrics.ToolExecutionDurationSeconds.Record(1.5,
            new System.Diagnostics.TagList { { "tool_name", "test-tool" } });

        metrics.ToolRegistrationTotal.Add(5,
            new System.Diagnostics.TagList { { "source", "test" } });

        // Assert - no exception should be thrown
        _output.WriteLine("Successfully recorded metrics for tool invocation, execution duration, and registration");

        await Task.CompletedTask; // For async pattern consistency
    }
}