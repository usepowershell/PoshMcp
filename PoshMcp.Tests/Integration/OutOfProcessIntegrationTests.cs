using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.OutOfProcess;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Integration;

/// <summary>
/// Integration tests that exercise the real OOP pipeline with an actual pwsh subprocess.
/// Requires pwsh on PATH.
/// </summary>
[Trait("Category", "OutOfProcess")]
public class OutOfProcessIntegrationTests : IAsyncLifetime
{
    private OutOfProcessCommandExecutor? _executor;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly ITestOutputHelper _output;
    private readonly bool _skipTests;

    public OutOfProcessIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new TestOutputLoggerProvider(output));
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        _logger = _loggerFactory.CreateLogger<OutOfProcessIntegrationTests>();

        try
        {
            var path = OutOfProcessCommandExecutor.ResolvePwshPath();
            _skipTests = string.IsNullOrEmpty(path);
        }
        catch
        {
            _skipTests = true;
        }

        if (_skipTests)
        {
            _output.WriteLine("⚠️  Skipping OOP integration tests — pwsh is not available on PATH");
        }
    }

    public async Task InitializeAsync()
    {
        if (_skipTests) return;

        _executor = new OutOfProcessCommandExecutor(
            _loggerFactory.CreateLogger<OutOfProcessCommandExecutor>());
        await _executor.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_executor != null)
            await _executor.DisposeAsync();
        _loggerFactory.Dispose();
    }

    // ---- Subprocess lifecycle tests ----

    [Fact]
    public async Task CanStartAndPingSubprocess()
    {
        if (_skipTests) { _output.WriteLine("⏭️  Test skipped"); return; }

        // StartAsync already pings during InitializeAsync.
        // Verify the executor is alive by sending another ping via discover with empty config.
        Assert.NotNull(_executor);

        // If we got here, StartAsync succeeded (which includes a ping).
        // Exercise DiscoverCommandsAsync as a secondary liveness check.
        var config = new PowerShellConfiguration
        {
            FunctionNames = new List<string> { "Get-Process" },
            Modules = new List<string>(),
            IncludePatterns = new List<string>(),
            ExcludePatterns = new List<string>()
        };

        var schemas = await _executor!.DiscoverCommandsAsync(config);
        Assert.NotNull(schemas);
        _logger.LogInformation("Ping/discover returned {Count} schemas", schemas.Count);
    }

    [Fact]
    public async Task CanShutdownGracefully()
    {
        if (_skipTests) { _output.WriteLine("⏭️  Test skipped"); return; }

        // Create a separate executor for this test so we can dispose it independently.
        using var factory = LoggerFactory.Create(b =>
        {
            b.AddProvider(new TestOutputLoggerProvider(_output));
            b.SetMinimumLevel(LogLevel.Debug);
        });

        var executor = new OutOfProcessCommandExecutor(
            factory.CreateLogger<OutOfProcessCommandExecutor>());
        await executor.StartAsync();

        // DisposeAsync sends shutdown + waits for process exit.
        await executor.DisposeAsync();

        // Double dispose should be safe.
        await executor.DisposeAsync();
    }

    [Fact]
    public async Task CanHandleMultipleStartCalls()
    {
        if (_skipTests) { _output.WriteLine("⏭️  Test skipped"); return; }

        // Create a fresh executor to test multiple Start() calls.
        using var factory = LoggerFactory.Create(b =>
        {
            b.AddProvider(new TestOutputLoggerProvider(_output));
            b.SetMinimumLevel(LogLevel.Debug);
        });

        var executor = new OutOfProcessCommandExecutor(
            factory.CreateLogger<OutOfProcessCommandExecutor>());
        await executor.StartAsync();

        // Second start on an already-running executor.
        // Depending on implementation this should either be idempotent or throw.
        // The current implementation will attempt to re-launch (creating a second process).
        // We verify it doesn't crash the test.
        try
        {
            await executor.StartAsync();
            _logger.LogInformation("Second StartAsync succeeded (idempotent behavior).");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogInformation("Second StartAsync threw as expected: {Message}", ex.Message);
        }

        await executor.DisposeAsync();
    }

    // ---- Discovery tests with built-in commands ----

    [Fact]
    public async Task CanDiscoverBuiltInCommands()
    {
        if (_skipTests) { _output.WriteLine("⏭️  Test skipped"); return; }

        var config = new PowerShellConfiguration
        {
            FunctionNames = new List<string> { "Get-Process", "Get-ChildItem" },
            Modules = new List<string>(),
            IncludePatterns = new List<string>(),
            ExcludePatterns = new List<string>()
        };

        var schemas = await _executor!.DiscoverCommandsAsync(config);

        Assert.NotNull(schemas);
        Assert.NotEmpty(schemas);

        var names = schemas.Select(s => s.Name).Distinct().ToList();
        _logger.LogInformation("Discovered commands: {Commands}", string.Join(", ", names));

        Assert.Contains(schemas, s => s.Name == "Get-Process");
        Assert.Contains(schemas, s => s.Name == "Get-ChildItem");
    }

    [Fact]
    public async Task DiscoverReturnsParameterMetadata()
    {
        if (_skipTests) { _output.WriteLine("⏭️  Test skipped"); return; }

        var config = new PowerShellConfiguration
        {
            FunctionNames = new List<string> { "Get-Process" },
            Modules = new List<string>(),
            IncludePatterns = new List<string>(),
            ExcludePatterns = new List<string>()
        };

        var schemas = await _executor!.DiscoverCommandsAsync(config);
        Assert.NotEmpty(schemas);

        // Get-Process should have a Name parameter at minimum
        var getProcess = schemas.First(s => s.Name == "Get-Process");
        Assert.NotNull(getProcess.Parameters);
        Assert.NotEmpty(getProcess.Parameters);

        _logger.LogInformation("Get-Process parameters: {Params}",
            string.Join(", ", getProcess.Parameters.Select(p => $"{p.Name}:{p.TypeName}")));

        // Verify parameter metadata shape
        foreach (var param in getProcess.Parameters)
        {
            Assert.False(string.IsNullOrWhiteSpace(param.Name), "Parameter Name should not be empty");
            Assert.False(string.IsNullOrWhiteSpace(param.TypeName), "Parameter TypeName should not be empty");
        }
    }

    [Fact]
    public async Task DiscoverWithIncludePatternsWorks()
    {
        if (_skipTests) { _output.WriteLine("⏭️  Test skipped"); return; }

        // Use a fresh executor to avoid cache from earlier tests
        using var factory = LoggerFactory.Create(b =>
        {
            b.AddProvider(new TestOutputLoggerProvider(_output));
            b.SetMinimumLevel(LogLevel.Debug);
        });

        var executor = new OutOfProcessCommandExecutor(
            factory.CreateLogger<OutOfProcessCommandExecutor>());
        await executor.StartAsync();

        try
        {
            var config = new PowerShellConfiguration
            {
                FunctionNames = new List<string>(),
                Modules = new List<string>(),
                IncludePatterns = new List<string> { "Get-Process" },
                ExcludePatterns = new List<string>()
            };

            var schemas = await executor.DiscoverCommandsAsync(config);
            Assert.NotNull(schemas);
            Assert.NotEmpty(schemas);
            Assert.Contains(schemas, s => s.Name == "Get-Process");
        }
        finally
        {
            await executor.DisposeAsync();
        }
    }

    // ---- Invocation tests ----

    [Fact]
    public async Task CanInvokeGetProcess()
    {
        if (_skipTests) { _output.WriteLine("⏭️  Test skipped"); return; }

        var result = await _executor!.InvokeAsync(
            "Get-Process",
            new Dictionary<string, object?>());

        Assert.NotNull(result);
        Assert.NotEmpty(result);
        _logger.LogInformation("Get-Process output length: {Length}", result.Length);

        // Result should be valid JSON (array of process objects)
        Assert.True(result.TrimStart().StartsWith("[") || result.TrimStart().StartsWith("{"),
            $"Expected JSON output but got: {result[..Math.Min(200, result.Length)]}");
    }

    [Fact]
    public async Task CanInvokeGetChildItem()
    {
        if (_skipTests) { _output.WriteLine("⏭️  Test skipped"); return; }

        var result = await _executor!.InvokeAsync(
            "Get-ChildItem",
            new Dictionary<string, object?> { ["Path"] = "/tmp" });

        Assert.NotNull(result);
        _logger.LogInformation("Get-ChildItem /tmp output length: {Length}", result.Length);

        // Output should be JSON
        var trimmed = result.TrimStart();
        Assert.True(trimmed.StartsWith("[") || trimmed.StartsWith("{") || trimmed == "null" || trimmed == "\"\"",
            $"Expected JSON output but got: {result[..Math.Min(200, result.Length)]}");
    }

    [Fact]
    public async Task InvokeNonexistentCommandReturnsError()
    {
        if (_skipTests) { _output.WriteLine("⏭️  Test skipped"); return; }

        // Invoking a nonexistent command should throw (OOP error response).
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _executor!.InvokeAsync(
                "Not-A-Real-Command-XYZ987",
                new Dictionary<string, object?>());
        });

        _logger.LogInformation("Expected error for nonexistent command: {Message}", ex.Message);
        Assert.Contains("OOP error", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeWithParametersWorks()
    {
        if (_skipTests) { _output.WriteLine("⏭️  Test skipped"); return; }

        var result = await _executor!.InvokeAsync(
            "Get-ChildItem",
            new Dictionary<string, object?> { ["Path"] = "/tmp" });

        Assert.NotNull(result);
        Assert.NotEmpty(result);
        _logger.LogInformation("Get-ChildItem with Path param output length: {Length}", result.Length);
    }

    // ---- Error handling tests ----

    [Fact]
    public async Task TimeoutOnSlowCommand()
    {
        if (_skipTests) { _output.WriteLine("⏭️  Test skipped"); return; }

        // Create an executor with a very short timeout
        using var factory = LoggerFactory.Create(b =>
        {
            b.AddProvider(new TestOutputLoggerProvider(_output));
            b.SetMinimumLevel(LogLevel.Debug);
        });

        var executor = new OutOfProcessCommandExecutor(
            factory.CreateLogger<OutOfProcessCommandExecutor>(),
            requestTimeout: TimeSpan.FromSeconds(2));
        await executor.StartAsync();

        try
        {
            // Start-Sleep -Seconds 10 should exceed the 2s timeout
            var ex = await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await executor.InvokeAsync(
                    "Start-Sleep",
                    new Dictionary<string, object?> { ["Seconds"] = 10 });
            });

            _logger.LogInformation("Timeout exception caught as expected: {Message}", ex.Message);
        }
        finally
        {
            await executor.DisposeAsync();
        }
    }

    [Fact]
    public async Task DisposedExecutorThrowsObjectDisposedException()
    {
        if (_skipTests) { _output.WriteLine("⏭️  Test skipped"); return; }

        using var factory = LoggerFactory.Create(b =>
        {
            b.AddProvider(new TestOutputLoggerProvider(_output));
            b.SetMinimumLevel(LogLevel.Debug);
        });

        var executor = new OutOfProcessCommandExecutor(
            factory.CreateLogger<OutOfProcessCommandExecutor>());
        await executor.StartAsync();
        await executor.DisposeAsync();

        // All operations on a disposed executor should throw
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await executor.StartAsync();
        });

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await executor.InvokeAsync("Get-Process", new Dictionary<string, object?>());
        });

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            var config = new PowerShellConfiguration
            {
                FunctionNames = new List<string> { "Get-Process" },
                Modules = new List<string>(),
                IncludePatterns = new List<string>(),
                ExcludePatterns = new List<string>()
            };
            await executor.DiscoverCommandsAsync(config);
        });
    }

    [Fact]
    public async Task CancellationTokenStopsInvocation()
    {
        if (_skipTests) { _output.WriteLine("⏭️  Test skipped"); return; }

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await _executor!.InvokeAsync(
                "Get-Process",
                new Dictionary<string, object?>(),
                cts.Token);
        });
    }
}
