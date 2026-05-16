using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PoshMcp;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.OutOfProcess;
using Xunit;

namespace PoshMcp.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ToolImportSourceTrackerTests
{
    [Fact]
    public void Tracker_RecordToolSource_KeepsFirstValue()
    {
        var tracker = new ToolImportSourceTracker();

        tracker.RecordToolSource("Get-Date", ToolImportSource.CommandName, "Get-Date");
        tracker.RecordToolSource("Get-Date", ToolImportSource.Module, "Microsoft.PowerShell.Utility");

        var recorded = Assert.IsType<ToolImportSourceInfo>(tracker.ToolSources["Get-Date"]);
        Assert.Equal(ToolImportSource.CommandName, recorded.Source);
        Assert.Equal("Get-Date", recorded.SourceDetail);
    }

    [Fact]
    public async Task GetToolsListAsync_InProcess_MixedDiscovery_RecordsCommandAndModuleSources()
    {
        var tracker = new ToolImportSourceTracker();
        using var runspace = new IsolatedPowerShellRunspace();
        var factory = new McpToolFactoryV2(runspace, metadataSource: null, descriptionSourceTracker: null, importSourceTracker: tracker);
        var config = new PowerShellConfiguration
        {
            CommandNames = new() { "Get-Date" },
            Modules = new() { "Microsoft.PowerShell.Management" },
            IncludePatterns = new() { "Get-*" },
        };

        var tools = await factory.GetToolsListAsync(config, NullLogger.Instance);

        Assert.NotEmpty(tools);
        Assert.True(tracker.ToolSources.TryGetValue("Get-Date", out var getDate));
        Assert.Equal(ToolImportSource.CommandName, getDate.Source);
        Assert.Equal("Get-Date", getDate.SourceDetail);

        Assert.True(tracker.ToolSources.TryGetValue("Get-Process", out var getProcess));
        Assert.Equal(ToolImportSource.Module, getProcess.Source);
        Assert.Equal("Microsoft.PowerShell.Management", getProcess.SourceDetail);
    }

    [Fact]
    public async Task GetToolsListAsync_InProcess_PatternDiscovery_RecordsPatternSource()
    {
        var tracker = new ToolImportSourceTracker();
        using var runspace = new IsolatedPowerShellRunspace();
        var factory = new McpToolFactoryV2(runspace, metadataSource: null, descriptionSourceTracker: null, importSourceTracker: tracker);
        var config = new PowerShellConfiguration
        {
            IncludePatterns = new() { "Get-Date" },
        };

        var tools = await factory.GetToolsListAsync(config, NullLogger.Instance);

        Assert.NotEmpty(tools);
        var recorded = Assert.IsType<ToolImportSourceInfo>(tracker.ToolSources["Get-Date"]);
        Assert.Equal(ToolImportSource.Pattern, recorded.Source);
        Assert.Equal("Get-Date", recorded.SourceDetail);
    }

    [Fact]
    public async Task GetToolsListAsync_OutOfProcess_UsesRemoteSchemaSourceFields()
    {
        var tracker = new ToolImportSourceTracker();
        var executor = new Mock<ICommandExecutor>();
        executor
            .Setup(e => e.DiscoverCommandsAsync(It.IsAny<PowerShellConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RemoteToolSchema>
            {
                new()
                {
                    Name = "Get-Date",
                    Description = "Gets the current date.",
                    SourceDetail = "Get-Date",
                    Parameters = new List<RemoteParameterSchema>(),
                },
                new()
                {
                    Name = "Get-Process",
                    Description = "Gets processes.",
                    SourceModule = "Microsoft.PowerShell.Management",
                    SourceDetail = "Microsoft.PowerShell.Management",
                    Parameters = new List<RemoteParameterSchema>(),
                },
                new()
                {
                    Name = "Get-Thing",
                    Description = "Gets a thing.",
                    SourcePattern = "Get-*",
                    SourceDetail = "Get-*",
                    Parameters = new List<RemoteParameterSchema>(),
                },
                new()
                {
                    Name = "Get-Legacy",
                    Description = "Legacy host result.",
                    Parameters = new List<RemoteParameterSchema>(),
                },
            });

        var factory = new McpToolFactoryV2(executor.Object, metadataSource: null, descriptionSourceTracker: null, importSourceTracker: tracker);
        var config = new PowerShellConfiguration
        {
            RuntimeMode = RuntimeMode.OutOfProcess,
        };

        var tools = await factory.GetToolsListAsync(config, NullLogger.Instance);

        Assert.Equal(4, tools.Count);
        Assert.Equal(ToolImportSource.CommandName, tracker.ToolSources["Get-Date"].Source);
        Assert.Equal(ToolImportSource.Module, tracker.ToolSources["Get-Process"].Source);
        Assert.Equal(ToolImportSource.Pattern, tracker.ToolSources["Get-Thing"].Source);
        Assert.Equal(ToolImportSource.Unknown, tracker.ToolSources["Get-Legacy"].Source);
        Assert.Equal(string.Empty, tracker.ToolSources["Get-Legacy"].SourceDetail);
    }
}
