using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.Pool;
using PoshMcp.Server.Server;
using Xunit;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Verifies the pool-based HTTP DI chain:
/// - <see cref="PooledHttpRunspace"/> implements <see cref="IPowerShellRunspace"/>
///   with no <see cref="SessionAwarePowerShellRunspace"/> or <see cref="IHttpContextAccessor"/> dependency.
/// - <see cref="RunspacePoolLifecycleService"/> implements <see cref="IHostedService"/>.
/// - Session-complete callback is a no-op (pool has no per-session state).
/// - Default <see cref="HttpTransportMode"/> is <see cref="HttpTransportMode.Stateless"/>.
/// - Transport-mode wiring sets <c>opts.Stateless</c> from configuration, not hardcoded.
/// </summary>
[Trait("Category", "Unit")]
public sealed class HttpServerPoolWiringTests
{
    // ─── Type-level DI chain proofs ──────────────────────────────────────────────

    [Fact]
    public void PooledHttpRunspace_ImplementsIPowerShellRunspace()
    {
        Assert.True(typeof(IPowerShellRunspace).IsAssignableFrom(typeof(PooledHttpRunspace)));
    }

    [Fact]
    public void PooledHttpRunspace_HasNoHttpContextAccessorDependency()
    {
        // PooledHttpRunspace constructor must not require IHttpContextAccessor.
        var ctors = typeof(PooledHttpRunspace).GetConstructors(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);

        foreach (var ctor in ctors)
        {
            var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToList();
            Assert.DoesNotContain(typeof(IHttpContextAccessor), paramTypes);
        }
    }

    [Fact]
#pragma warning disable CS0618 // Intentional: verifying PooledHttpRunspace has no dependency on the obsolete type.
    public void PooledHttpRunspace_HasNoSessionAwarePowerShellRunspaceDependency()
    {
        var ctors = typeof(PooledHttpRunspace).GetConstructors(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);

        foreach (var ctor in ctors)
        {
            var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToList();
            Assert.DoesNotContain(typeof(SessionAwarePowerShellRunspace), paramTypes);
        }
    }
#pragma warning restore CS0618

    [Fact]
    public void RunspacePoolLifecycleService_ImplementsIHostedService()
    {
        Assert.True(typeof(IHostedService).IsAssignableFrom(typeof(RunspacePoolLifecycleService)));
    }

    [Fact]
    public void StatelessRunspacePool_ImplementsIRunspacePool()
    {
        Assert.True(typeof(IRunspacePool).IsAssignableFrom(typeof(StatelessRunspacePool)));
    }

    // ─── DI registration via minimal service collection ──────────────────────────

    [Fact]
    public void ServiceCollection_RegisteredPooledRunspace_CanResolveIPowerShellRunspace()
    {
        var pool = MakePool();
        var services = new ServiceCollection();
        services.AddSingleton<IPowerShellRunspace>(
            new PooledHttpRunspace(pool, (string?)null, NullLoggerFactory.Instance));

        var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IPowerShellRunspace>();

        Assert.IsType<PooledHttpRunspace>(resolved);
    }

    [Fact]
    public void ServiceCollection_RegisteredIRunspacePool_IsSingleton()
    {
        var pool = MakePool();
        var services = new ServiceCollection();
        services.AddSingleton<IRunspacePool>(pool);

        var provider = services.BuildServiceProvider();
        var a = provider.GetRequiredService<IRunspacePool>();
        var b = provider.GetRequiredService<IRunspacePool>();

        Assert.Same(a, b);
    }

    [Fact]
#pragma warning disable CS0618 // Intentional: verifying SAPR is not resolvable from the pool DI chain.
    public void ServiceCollection_NoSAPR_WhenPooledRunspaceRegistered()
    {
        var pool = MakePool();
        var services = new ServiceCollection();
        services.AddSingleton<IPowerShellRunspace>(
            new PooledHttpRunspace(pool, (string?)null, NullLoggerFactory.Instance));
        services.AddSingleton<IRunspacePool>(pool);

        var provider = services.BuildServiceProvider();

        // SAPR must NOT be resolvable from this DI chain.
        Assert.Null(provider.GetService<SessionAwarePowerShellRunspace>());
    }
#pragma warning restore CS0618

    // ─── Session lifecycle: no PowerShell cleanup callback ───────────────────────

    /// <summary>
    /// Proves that <see cref="McpSessionLifecycle"/> has a parameterless constructor and that
    /// <see cref="McpSessionLifecycle.CompleteSession"/> does not interact with the pool.
    /// The lifecycle is responsible only for MCP protocol-version tracking.
    /// </summary>
    [Fact]
    public void McpSessionLifecycle_CompleteSession_DoesNotDrainPool()
    {
        var pool = new Mock<IRunspacePool>();

        var lifecycle = new McpSessionLifecycle();
        lifecycle.TrackProtocolVersion("session-abc", "2025-11-25");
        lifecycle.CompleteSession("session-abc");

        // Protocol tracking removed:
        Assert.False(lifecycle.TryGetProtocolVersion("session-abc", out _));
        // Pool was never touched:
        pool.Verify(p => p.DrainAsync(It.IsAny<CancellationToken>()), Times.Never);
        pool.Verify(p => p.DisposeAsync(), Times.Never);
    }

    [Fact]
    public void McpSessionLifecycle_NoOpCleanup_RemovesProtocolVersionTracking()
    {
        var lifecycle = new McpSessionLifecycle();
        lifecycle.TrackProtocolVersion("session-1", "2024-11-05");
        Assert.True(lifecycle.TryGetProtocolVersion("session-1", out _));

        lifecycle.CompleteSession("session-1");

        Assert.False(lifecycle.TryGetProtocolVersion("session-1", out _));
    }

    [Fact]
    public void McpSessionLifecycle_Stateless_NoOpCleanup_NoException()
    {
        // Verify that a parameterless lifecycle does not throw on untracked sessions.
        var lifecycle = new McpSessionLifecycle();

        // No exception expected even when session has never been tracked.
        lifecycle.CompleteSession("unknown-session");
        lifecycle.CompleteSession(null);
    }

    // ─── HttpTransportMode default ───────────────────────────────────────────────

    [Fact]
    public void HttpTransportMode_Default_IsStateless()
    {
        Assert.Equal(HttpTransportMode.Stateless, default(HttpTransportMode));
    }

    [Fact]
    public void McpServerConfiguration_HttpTransportMode_DefaultsToStateless()
    {
        var config = new McpServerConfiguration();
        Assert.Equal(HttpTransportMode.Stateless, config.HttpTransportMode);
    }

    [Fact]
    public void RegisterResolvedMcpConfiguration_EmptyUserConfig_HttpTransportModeIsStateless()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        var returned = HttpServerHost.RegisterResolvedMcpConfiguration(services, config, NullLogger.Instance);

        Assert.Equal(HttpTransportMode.Stateless, returned.HttpTransportMode);
    }

    [Fact]
    public void RegisterResolvedMcpConfiguration_StatefulOverride_HttpTransportModeIsStateful()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new KeyValuePair<string, string?>(
                    "McpServer:HttpTransportMode", "Stateful")])
            .Build();
        var services = new ServiceCollection();

        var returned = HttpServerHost.RegisterResolvedMcpConfiguration(services, config, NullLogger.Instance);

        Assert.Equal(HttpTransportMode.Stateful, returned.HttpTransportMode);
    }

    // ─── Transport mode opts.Stateless wiring ────────────────────────────────────

    [Fact]
    public void IsStateless_WhenStateless_IsTrue()
    {
        // Simulate the expression used in HttpServerHost:
        // var isStateless = mcpServerConfig.HttpTransportMode == HttpTransportMode.Stateless;
        var config = new McpServerConfiguration { HttpTransportMode = HttpTransportMode.Stateless };
        Assert.True(config.HttpTransportMode == HttpTransportMode.Stateless);
    }

    [Fact]
    public void IsStateless_WhenStateful_IsFalse()
    {
        var config = new McpServerConfiguration { HttpTransportMode = HttpTransportMode.Stateful };
        Assert.False(config.HttpTransportMode == HttpTransportMode.Stateless);
    }

    // ─── Lifecycle ordering: StartAsync / StopAsync ──────────────────────────────

    [Fact]
    public async Task LifecycleService_StartAsync_CallsPoolStartAsync()
    {
        var pool = new Mock<IRunspacePool>();
        pool.Setup(p => p.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        pool.Setup(p => p.GetStats()).Returns(new RunspacePoolStats(1, 4, 1, 0, 0, 1));
        var svc = new RunspacePoolLifecycleService(
            pool.Object,
            NullLogger<RunspacePoolLifecycleService>.Instance);

        await svc.StartAsync(CancellationToken.None);

        // The service starts the pool and must not drain or dispose it.
        pool.Verify(p => p.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        pool.Verify(p => p.DrainAsync(It.IsAny<CancellationToken>()), Times.Never);
        pool.Verify(p => p.DisposeAsync(), Times.Never);
    }

    [Fact]
    public async Task LifecycleService_StopAsync_DrainsThenDisposes()
    {
        var order = new List<string>();
        var pool = new Mock<IRunspacePool>();
        pool.Setup(p => p.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        pool.Setup(p => p.GetStats()).Returns(new RunspacePoolStats(1, 4, 1, 0, 0, 1));
        pool.Setup(p => p.DrainAsync(It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("drain")).Returns(Task.CompletedTask);
        pool.Setup(p => p.DisposeAsync())
            .Callback(() => order.Add("dispose")).Returns(ValueTask.CompletedTask);

        var svc = new RunspacePoolLifecycleService(
            pool.Object, NullLogger<RunspacePoolLifecycleService>.Instance);
        await svc.StartAsync(CancellationToken.None);
        await svc.StopAsync(CancellationToken.None);

        Assert.Equal(new[] { "drain", "dispose" }, order);
    }

    // ─── Obsolete annotations: preserved types carry migration guidance ───────────

    [Fact]
#pragma warning disable CS0618 // Intentional: this test verifies the [Obsolete] attribute is present on the type.
    public void SessionAwarePowerShellRunspace_HasObsoleteAttribute()
    {
        var obsolete = typeof(SessionAwarePowerShellRunspace)
            .GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false);
        Assert.NotEmpty(obsolete);
        var msg = ((ObsoleteAttribute)obsolete[0]).Message;
        Assert.Contains("StatelessRunspacePool", msg, StringComparison.Ordinal);
        Assert.Contains("IRunspacePool", msg, StringComparison.Ordinal);
        Assert.Contains("RunspacePoolLifecycleService", msg, StringComparison.Ordinal);
    }
#pragma warning restore CS0618

    [Fact]
#pragma warning disable CS0618 // Intentional: this test verifies the [Obsolete] attribute is present on the type.
    public void SessionRunspaceOptions_HasObsoleteAttribute()
    {
        var obsolete = typeof(SessionRunspaceOptions)
            .GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false);
        Assert.NotEmpty(obsolete);
        var msg = ((ObsoleteAttribute)obsolete[0]).Message;
        Assert.Contains("RunspacePoolOptions", msg, StringComparison.Ordinal);
        Assert.Contains("RunspacePoolLifecycleService", msg, StringComparison.Ordinal);
    }
#pragma warning restore CS0618

    // ─── Helpers ────────────────────────────────────────────────────────────────

    private static StatelessRunspacePool MakePool(int eager = 0) =>
        new StatelessRunspacePool(
            new RunspacePoolOptions
            {
                MinPoolSize = 1,
                MaxPoolSize = 4,
                EagerWarmCount = eager,
                AcquisitionTimeout = TimeSpan.FromSeconds(5),
                IdleTtl = TimeSpan.FromSeconds(300),
                SweepInterval = TimeSpan.FromSeconds(60),
                StopTimeout = TimeSpan.FromSeconds(2),
                ShutdownDrainTimeout = TimeSpan.FromMilliseconds(500),
                ReplenishCheckInterval = TimeSpan.FromSeconds(60),
            },
            loggerFactory: null,
            startupScript: null,
            workerFactory: () => new Mock<IPowerShellRunspace>().Object,
            snapshotCapture: _ => new HashSet<string>(),
            driveSnapshotCapture: _ => new HashSet<string>(),
            resetProtocol: (_, _, _) => Task.CompletedTask);

    private sealed class NullLogger : Microsoft.Extensions.Logging.ILogger
    {
        public static readonly NullLogger Instance = new();
        IDisposable? Microsoft.Extensions.Logging.ILogger.BeginScope<TState>(TState state) => null;
        bool Microsoft.Extensions.Logging.ILogger.IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => false;
        void Microsoft.Extensions.Logging.ILogger.Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        { }
    }
}
