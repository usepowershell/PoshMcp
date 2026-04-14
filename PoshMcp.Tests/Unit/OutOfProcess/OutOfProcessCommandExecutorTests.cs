using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.OutOfProcess;
using Xunit;

namespace PoshMcp.Tests.Unit.OutOfProcess;

/// <summary>
/// Unit tests for OutOfProcessCommandExecutor.
/// Tests construction, static helpers, stub methods (Phase 3/4),
/// and lifecycle management.
/// </summary>
public class OutOfProcessCommandExecutorTests
{
    private readonly ILogger<OutOfProcessCommandExecutor> _logger;

    public OutOfProcessCommandExecutorTests()
    {
        _logger = NullLogger<OutOfProcessCommandExecutor>.Instance;
    }

    [Fact]
    public void Constructor_WithLogger_DoesNotThrow()
    {
        var executor = new OutOfProcessCommandExecutor(_logger);
        Assert.NotNull(executor);
    }

    [Fact]
    public void Constructor_WithNullLoggerFactory_UsesNullLogger()
    {
        var logger = NullLoggerFactory.Instance.CreateLogger<OutOfProcessCommandExecutor>();
        var executor = new OutOfProcessCommandExecutor(logger);
        Assert.NotNull(executor);
    }

    [Fact]
    public void Constructor_WithCustomTimeout_DoesNotThrow()
    {
        var executor = new OutOfProcessCommandExecutor(_logger, TimeSpan.FromSeconds(60));
        Assert.NotNull(executor);
    }

    [Fact]
    public async Task DiscoverCommandsAsync_WithoutStarting_ThrowsInvalidOperationException()
    {
        var executor = new OutOfProcessCommandExecutor(_logger);
        var config = new PowerShellConfiguration();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.DiscoverCommandsAsync(config, CancellationToken.None));
    }

    [Fact]
    public async Task DiscoverCommandsAsync_WithModules_WithoutStarting_ThrowsInvalidOperationException()
    {
        var executor = new OutOfProcessCommandExecutor(_logger);
        var config = new PowerShellConfiguration
        {
            Modules = new List<string> { "Az.Accounts" }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.DiscoverCommandsAsync(config, CancellationToken.None));
    }

    [Fact]
    public async Task InvokeAsync_WithoutStarting_ThrowsInvalidOperationException()
    {
        var executor = new OutOfProcessCommandExecutor(_logger);
        var parameters = new Dictionary<string, object?>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.InvokeAsync("Get-Process", parameters, CancellationToken.None));
    }

    [Fact]
    public async Task InvokeAsync_WithParameters_WithoutStarting_ThrowsInvalidOperationException()
    {
        var executor = new OutOfProcessCommandExecutor(_logger);
        var parameters = new Dictionary<string, object?>
        {
            ["Name"] = "pwsh",
            ["Id"] = 1234
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.InvokeAsync("Get-Process", parameters, CancellationToken.None));
    }

    [Fact]
    public async Task DisposeAsync_DoesNotThrow()
    {
        var executor = new OutOfProcessCommandExecutor(_logger);

        var exception = await Record.ExceptionAsync(async () =>
        {
            await executor.DisposeAsync();
        });

        Assert.Null(exception);
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes()
    {
        var executor = new OutOfProcessCommandExecutor(_logger);

        await executor.DisposeAsync();

        var exception = await Record.ExceptionAsync(async () =>
        {
            await executor.DisposeAsync();
        });

        Assert.Null(exception);
    }

    [Fact]
    public void Executor_ImplementsICommandExecutor()
    {
        var executor = new OutOfProcessCommandExecutor(_logger);
        Assert.IsAssignableFrom<ICommandExecutor>(executor);
    }

    [Fact]
    public void Executor_ImplementsIAsyncDisposable()
    {
        var executor = new OutOfProcessCommandExecutor(_logger);
        Assert.IsAssignableFrom<IAsyncDisposable>(executor);
    }

    [Fact]
    public void ResolvePwshPath_FindsPwshOnPath()
    {
        // pwsh should be available in the dev container
        var path = OutOfProcessCommandExecutor.ResolvePwshPath();
        Assert.NotNull(path);
        Assert.True(File.Exists(path), $"Resolved pwsh path does not exist: {path}");
    }

    [Fact]
    public async Task StartAsync_ThenDisposeAsync_FullLifecycle()
    {
        // Skip if oop-host.ps1 isn't available at the expected path
        // (only works when run from build output with the script copied)
        var executor = new OutOfProcessCommandExecutor(_logger, TimeSpan.FromSeconds(15));

        try
        {
            await executor.StartAsync(CancellationToken.None);
        }
        catch (FileNotFoundException)
        {
            // oop-host.ps1 not in output — acceptable in unit test context
            return;
        }

        // If we get here, subprocess started and ping succeeded
        await executor.DisposeAsync();
    }

    [Fact]
    public void ResolveModulePaths_FiltersNullOrWhitespaceEntries()
    {
        var baseDir = Path.GetTempPath();
        string?[] configuredPaths =
        {
            null,
            "   ",
            string.Empty,
            "./Modules/Custom"
        };

        var resolved = OutOfProcessCommandExecutor.ResolveModulePaths(configuredPaths, baseDir);

        Assert.Single(resolved);
        Assert.Equal(Path.GetFullPath(Path.Combine(baseDir, "./Modules/Custom")), resolved[0]);
    }

    [Fact]
    public void ResolveModulePaths_DeduplicatesCaseInsensitively()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "PoshMcp-ResolveModulePaths");
        var duplicateA = Path.Combine(baseDir, "Modules");
        var duplicateB = duplicateA.ToUpperInvariant();

        var resolved = OutOfProcessCommandExecutor.ResolveModulePaths(new[] { duplicateA, duplicateB }, baseDir);

        Assert.Single(resolved);
        Assert.Equal(Path.GetFullPath(duplicateA), resolved.Single());
    }
}
