using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PoshMcp.Server.PowerShell.Pool;
using PoshMcp.Server.Server;
using Xunit;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="RunspacePoolLifecycleService"/> using a mock <see cref="IRunspacePool"/>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RunspacePoolLifecycleServiceTests
{
    // ─── StartAsync ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_LogsPoolStats_WithWarmAndTotal()
    {
        var pool = new Mock<IRunspacePool>();
        pool.Setup(p => p.GetStats())
            .Returns(new RunspacePoolStats(2, 16, WarmWorkers: 3, LeasedWorkers: 0, ResettingWorkers: 0, TotalWorkers: 3));

        var log = new TestLogger<RunspacePoolLifecycleService>();
        var svc = new RunspacePoolLifecycleService(pool.Object, log);

        await svc.StartAsync(CancellationToken.None);

        Assert.Contains(log.Messages, m =>
            m.Contains("3") && m.Contains("Warm", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task StartAsync_DoesNotCallDrainOrDispose()
    {
        var pool = new Mock<IRunspacePool>();
        pool.Setup(p => p.GetStats()).Returns(new RunspacePoolStats(1, 4, 1, 0, 0, 1));

        var svc = new RunspacePoolLifecycleService(pool.Object, NullLogger<RunspacePoolLifecycleService>.Instance);
        await svc.StartAsync(CancellationToken.None);

        pool.Verify(p => p.DrainAsync(It.IsAny<CancellationToken>()), Times.Never);
        pool.Verify(p => p.DisposeAsync(), Times.Never);
    }

    // ─── StopAsync ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task StopAsync_CallsDrainBeforeDispose()
    {
        var order = new List<string>();
        var pool = new Mock<IRunspacePool>();
        pool.Setup(p => p.DrainAsync(It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("drain"))
            .Returns(Task.CompletedTask);
        pool.Setup(p => p.DisposeAsync())
            .Callback(() => order.Add("dispose"))
            .Returns(ValueTask.CompletedTask);
        pool.Setup(p => p.GetStats()).Returns(new RunspacePoolStats(1, 4, 1, 0, 0, 1));

        var svc = new RunspacePoolLifecycleService(pool.Object, NullLogger<RunspacePoolLifecycleService>.Instance);
        await svc.StartAsync(CancellationToken.None);
        await svc.StopAsync(CancellationToken.None);

        Assert.Equal(new[] { "drain", "dispose" }, order);
    }

    [Fact]
    public async Task StopAsync_CallsDrainExactlyOnce()
    {
        var pool = new Mock<IRunspacePool>();
        pool.Setup(p => p.DrainAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        pool.Setup(p => p.DisposeAsync()).Returns(ValueTask.CompletedTask);
        pool.Setup(p => p.GetStats()).Returns(new RunspacePoolStats(1, 4, 1, 0, 0, 1));

        var svc = new RunspacePoolLifecycleService(pool.Object, NullLogger<RunspacePoolLifecycleService>.Instance);
        await svc.StartAsync(CancellationToken.None);
        await svc.StopAsync(CancellationToken.None);

        pool.Verify(p => p.DrainAsync(It.IsAny<CancellationToken>()), Times.Once);
        pool.Verify(p => p.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task StopAsync_WhenDrainThrowsNonCancellation_DoesNotPropagate()
    {
        var pool = new Mock<IRunspacePool>();
        pool.Setup(p => p.DrainAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("drain failed"));
        pool.Setup(p => p.GetStats()).Returns(new RunspacePoolStats(1, 4, 1, 0, 0, 1));

        var svc = new RunspacePoolLifecycleService(pool.Object, NullLogger<RunspacePoolLifecycleService>.Instance);
        await svc.StartAsync(CancellationToken.None);

        // Non-cancellation exceptions during drain/dispose are logged, not rethrown.
        await svc.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_WhenCancelled_PropagatesOperationCanceledException()
    {
        var pool = new Mock<IRunspacePool>();
        using var cts = new CancellationTokenSource();
        pool.Setup(p => p.DrainAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken ct) =>
            {
                await Task.Delay(100, ct);
            });
        pool.Setup(p => p.GetStats()).Returns(new RunspacePoolStats(1, 4, 1, 0, 0, 1));

        var svc = new RunspacePoolLifecycleService(pool.Object, NullLogger<RunspacePoolLifecycleService>.Instance);
        await svc.StartAsync(CancellationToken.None);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            svc.StopAsync(cts.Token));
    }

    // ─── Constructor guards ──────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullPool_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new RunspacePoolLifecycleService(null!, NullLogger<RunspacePoolLifecycleService>.Instance));
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var pool = new Mock<IRunspacePool>();
        Assert.Throws<ArgumentNullException>(() =>
            new RunspacePoolLifecycleService(pool.Object, null!));
    }

    // ─── Helper ──────────────────────────────────────────────────────────────────

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        IDisposable? ILogger.BeginScope<TState>(TState state) => NullLogger.Instance.BeginScope(state);

        bool ILogger.IsEnabled(LogLevel logLevel) => true;

        void ILogger.Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
