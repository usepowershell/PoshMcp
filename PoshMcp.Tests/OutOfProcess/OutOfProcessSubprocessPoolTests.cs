using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.OutOfProcess;
using Xunit;

namespace PoshMcp.Tests.OutOfProcess;

/// <summary>
/// Unit tests for <see cref="OutOfProcessSubprocessPool"/> internals that don't
/// require a live pwsh subprocess: constructor validation, state transitions
/// before <c>StartAsync</c>, environment fingerprint stability, and the
/// <see cref="SubprocessHostMode"/> string detection.
/// </summary>
[Trait("Category", "OutOfProcess")]
public class OutOfProcessSubprocessPoolTests
{
    [Fact]
    public void Constructor_NullPwsh_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new OutOfProcessSubprocessPool(
                string.Empty, "x.ps1",
                new OutOfProcessSubprocessPoolOptions { PoolSize = 1 }));
    }

    [Fact]
    public void Constructor_NullScript_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new OutOfProcessSubprocessPool(
                "pwsh", string.Empty,
                new OutOfProcessSubprocessPoolOptions { PoolSize = 1 }));
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new OutOfProcessSubprocessPool("pwsh", "x.ps1", null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_InvalidPoolSize_Throws(int poolSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new OutOfProcessSubprocessPool(
                "pwsh", "x.ps1",
                new OutOfProcessSubprocessPoolOptions { PoolSize = poolSize }));
    }

    [Fact]
    public void Constructor_MinHealthyExceedsPoolSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new OutOfProcessSubprocessPool(
                "pwsh", "x.ps1",
                new OutOfProcessSubprocessPoolOptions { PoolSize = 2, MinHealthyForStartup = 5 }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_InvalidCleanupTimeout_Throws(int timeoutMilliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new OutOfProcessSubprocessPool(
                "pwsh", "x.ps1",
                new OutOfProcessSubprocessPoolOptions
                {
                    PoolSize = 1,
                    CleanupTimeout = TimeSpan.FromMilliseconds(timeoutMilliseconds),
                }));
    }

    [Fact]
    public async Task DisposeAsync_BeforeStart_DoesNotThrow()
    {
        var pool = new OutOfProcessSubprocessPool(
            "pwsh", "x.ps1",
            new OutOfProcessSubprocessPoolOptions { PoolSize = 1 });
        await pool.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var pool = new OutOfProcessSubprocessPool(
            "pwsh", "x.ps1",
            new OutOfProcessSubprocessPoolOptions { PoolSize = 1 });
        await pool.DisposeAsync();
        await pool.DisposeAsync();
    }

    [Fact]
    public async Task AwaitCleanupAsync_TimeoutIsBoundedByConfiguredTimeProvider()
    {
        var timeProvider = new FakeTimeProvider();
        var pool = new OutOfProcessSubprocessPool(
            "pwsh", "x.ps1",
            new OutOfProcessSubprocessPoolOptions
            {
                PoolSize = 1,
                CleanupTimeout = TimeSpan.FromSeconds(5),
                TimeProvider = timeProvider,
            });
        var neverCompletes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var cleanup = pool.AwaitCleanupAsync(neverCompletes.Task, "test worker");
        Assert.False(cleanup.IsCompleted);

        timeProvider.Advance(TimeSpan.FromSeconds(5));

        var failure = await cleanup;
        Assert.IsType<TimeoutException>(failure);
        await pool.DisposeAsync();
    }

    [Fact]
    public async Task AwaitCleanupAsync_UsesCallerCancellationWithoutWaitingForTimeout()
    {
        var pool = new OutOfProcessSubprocessPool(
            "pwsh", "x.ps1",
            new OutOfProcessSubprocessPoolOptions
            {
                PoolSize = 1,
                CleanupTimeout = TimeSpan.FromHours(1),
            });
        var neverCompletes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var failure = await pool.AwaitCleanupAsync(
            neverCompletes.Task,
            "test worker",
            cancellation.Token);

        Assert.IsType<OperationCanceledException>(failure);
        await pool.DisposeAsync();
    }

    [Fact]
    public async Task AwaitCleanupAsync_LogsAndReturnsDisposalFailure()
    {
        var loggerProvider = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(loggerProvider));
        var pool = new OutOfProcessSubprocessPool(
            "pwsh", "x.ps1",
            new OutOfProcessSubprocessPoolOptions { PoolSize = 1 },
            loggerFactory);
        var expected = new InvalidOperationException("worker teardown failed");

        var failure = await pool.AwaitCleanupAsync(
            Task.FromException(expected),
            "test worker");

        Assert.Same(expected, failure);
        Assert.Contains(
            loggerProvider.Messages,
            message => message.Level == LogLevel.Error
                && message.Exception == expected
                && message.Message.Contains("OOP cleanup failed", StringComparison.Ordinal));
        await pool.DisposeAsync();
    }

    [Fact]
    public async Task InvokeAsync_BeforeStart_Throws()
    {
        var pool = new OutOfProcessSubprocessPool(
            "pwsh", "x.ps1",
            new OutOfProcessSubprocessPoolOptions { PoolSize = 1 });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pool.InvokeAsync("Get-Date", new Dictionary<string, object?>(), CancellationToken.None));

        await pool.DisposeAsync();
    }

    [Fact]
    public void HealthyCount_BeforeStart_IsZero()
    {
        var pool = new OutOfProcessSubprocessPool(
            "pwsh", "x.ps1",
            new OutOfProcessSubprocessPoolOptions { PoolSize = 4 });

        Assert.Equal(0, pool.HealthyCount);
        Assert.Equal(4, pool.PoolSize);
    }

    // ---- ComputeEnvironmentFingerprint ----

    [Fact]
    public void Fingerprint_IdenticalConfigs_ProduceSameHash()
    {
        var c1 = new EnvironmentConfiguration
        {
            ImportModules = { "Pester", "PSReadLine" },
            ModulePaths = { "/a", "/b" },
            TrustPSGallery = true,
        };
        var c2 = new EnvironmentConfiguration
        {
            ImportModules = { "Pester", "PSReadLine" },
            ModulePaths = { "/a", "/b" },
            TrustPSGallery = true,
        };

        var f1 = OutOfProcessSubprocessPool.ComputeEnvironmentFingerprint(c1, null);
        var f2 = OutOfProcessSubprocessPool.ComputeEnvironmentFingerprint(c2, null);
        Assert.Equal(f1, f2);
        Assert.NotEqual(string.Empty, f1);
        Assert.Equal(64, f1.Length); // SHA-256 hex
    }

    [Fact]
    public void Fingerprint_OrderingDifferences_ProduceSameHash()
    {
        var c1 = new EnvironmentConfiguration
        {
            ImportModules = { "Pester", "PSReadLine" },
            ModulePaths = { "/a", "/b" },
        };
        var c2 = new EnvironmentConfiguration
        {
            ImportModules = { "PSReadLine", "Pester" },
            ModulePaths = { "/b", "/a" },
        };

        var f1 = OutOfProcessSubprocessPool.ComputeEnvironmentFingerprint(c1, null);
        var f2 = OutOfProcessSubprocessPool.ComputeEnvironmentFingerprint(c2, null);
        Assert.Equal(f1, f2);
    }

    [Fact]
    public void Fingerprint_DiscoveryModulesIncluded()
    {
        var config = new EnvironmentConfiguration { ImportModules = { "Pester" } };
        var f1 = OutOfProcessSubprocessPool.ComputeEnvironmentFingerprint(config, null);
        var f2 = OutOfProcessSubprocessPool.ComputeEnvironmentFingerprint(
            config, new[] { "Az.Accounts" });
        Assert.NotEqual(f1, f2);
    }

    [Fact]
    public void Fingerprint_StartupScriptChange_ProducesDifferentHash()
    {
        var c1 = new EnvironmentConfiguration { StartupScript = "Write-Host 'a'" };
        var c2 = new EnvironmentConfiguration { StartupScript = "Write-Host 'b'" };
        Assert.NotEqual(
            OutOfProcessSubprocessPool.ComputeEnvironmentFingerprint(c1, null),
            OutOfProcessSubprocessPool.ComputeEnvironmentFingerprint(c2, null));
    }

    [Fact]
    public void Fingerprint_TrustPSGalleryChange_ProducesDifferentHash()
    {
        var c1 = new EnvironmentConfiguration { TrustPSGallery = true };
        var c2 = new EnvironmentConfiguration { TrustPSGallery = false };
        Assert.NotEqual(
            OutOfProcessSubprocessPool.ComputeEnvironmentFingerprint(c1, null),
            OutOfProcessSubprocessPool.ComputeEnvironmentFingerprint(c2, null));
    }

    [Fact]
    public void Fingerprint_NullConfig_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => OutOfProcessSubprocessPool.ComputeEnvironmentFingerprint(null!, null));
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<LogMessage> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose() { }
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly ConcurrentQueue<LogMessage> _messages;

        public CapturingLogger(ConcurrentQueue<LogMessage> messages) => _messages = messages;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _messages.Enqueue(new LogMessage(logLevel, exception, formatter(state, exception)));
        }
    }

    private sealed record LogMessage(LogLevel Level, Exception? Exception, string Message);
}
