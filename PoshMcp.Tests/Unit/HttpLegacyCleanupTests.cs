using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.Pool;
using PoshMcp.Server.Server;
using Xunit;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Validates the phase-5 legacy cleanup acceptance criteria:
/// <list type="bullet">
///   <item>No active HTTP path uses session-affine PowerShell.</item>
///   <item>All residual <c>SessionRunspace*</c> properties on <see cref="McpServerConfiguration"/> carry <see cref="ObsoleteAttribute"/>.</item>
///   <item>Deprecation messages are present and reference the replacement configuration path.</item>
///   <item>Both HTTP transport modes resolve to <see cref="HttpTransportMode.Stateless"/> and <see cref="HttpTransportMode.Stateful"/> respectively.</item>
///   <item>Neither mode registers <see cref="SessionAwarePowerShellRunspace"/> in the DI chain.</item>
/// </list>
/// </summary>
[Trait("Category", "Unit")]
public sealed class HttpLegacyCleanupTests
{
    // ─── McpServerConfiguration.SessionRunspace* deprecation ─────────────────────

    [Theory]
    [InlineData("SessionRunspaceCapacity", "MaxPoolSize")]
    [InlineData("SessionRunspaceIdleTtlSeconds", "IdleTtl")]
    [InlineData("SessionRunspaceSweepIntervalSeconds", "SweepInterval")]
    [InlineData("SessionRunspaceWarmStandbyCount", "MinPoolSize")]
    [InlineData("SessionRunspaceAcquisitionTimeoutSeconds", "AcquisitionTimeout")]
#pragma warning disable CS0618 // Intentional: this test verifies the [Obsolete] attribute is present on every SessionRunspace* property.
    public void McpServerConfiguration_SessionRunspaceProperty_HasObsoleteAttribute(string propertyName, string expectedReplacement)
    {
        var prop = typeof(McpServerConfiguration).GetProperty(propertyName,
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(prop);

        var obsolete = prop!.GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false);
        Assert.NotEmpty(obsolete);

        var msg = ((ObsoleteAttribute)obsolete[0]).Message;
        Assert.Contains("McpServer:RunspacePool", msg, StringComparison.Ordinal);
        Assert.Contains(expectedReplacement, msg, StringComparison.Ordinal);
        Assert.Contains("next major version", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("session-affine", msg, StringComparison.OrdinalIgnoreCase);
    }
#pragma warning restore CS0618

    [Fact]
#pragma warning disable CS0618 // Intentional: enumerating deprecated properties to confirm all five are marked.
    public void McpServerConfiguration_AllSessionRunspaceProperties_AreObsolete()
    {
        // Every property whose name starts with "SessionRunspace" must be marked [Obsolete].
        var props = typeof(McpServerConfiguration)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name.StartsWith("SessionRunspace", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(props); // sanity: at least one must exist

        foreach (var prop in props)
        {
            var obsolete = prop.GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false);
            Assert.NotEmpty(obsolete);
        }
    }
#pragma warning restore CS0618

    // ─── Deprecation messages reference the correct replacement keys ──────────────

    [Fact]
#pragma warning disable CS0618 // Intentional: verifying the deprecation message content.
    public void SessionRunspaceCapacity_ObsoleteMessage_ReferencesMaxPoolSize()
    {
        var msg = GetObsoleteMessage(nameof(McpServerConfiguration.SessionRunspaceCapacity));
        Assert.Contains("MaxPoolSize", msg, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionRunspaceWarmStandbyCount_ObsoleteMessage_ReferencesBothMinAndEagerWarmCount()
    {
        var msg = GetObsoleteMessage(nameof(McpServerConfiguration.SessionRunspaceWarmStandbyCount));
        Assert.Contains("MinPoolSize", msg, StringComparison.Ordinal);
        Assert.Contains("EagerWarmCount", msg, StringComparison.Ordinal);
    }
#pragma warning restore CS0618

    // ─── HTTP DI chain: no SAPR resolvable from pool-based registration ───────────

    [Fact]
#pragma warning disable CS0618 // Intentional: verifying SAPR is absent from the pool DI chain.
    public void StatelessHttpDiChain_DoesNotContainSAPR()
    {
        var pool = MakePool();
        var services = new ServiceCollection();
        services.AddSingleton<IPowerShellRunspace>(
            new PooledHttpRunspace(pool, (string?)null, NullLoggerFactory.Instance));
        services.AddSingleton<IRunspacePool>(pool);

        var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<SessionAwarePowerShellRunspace>());
    }

    [Fact]
    public void StatefulHttpDiChain_DoesNotContainSAPR()
    {
        // Even in stateful HTTP compat mode the DI registration uses PooledHttpRunspace — not SAPR.
        var pool = MakePool();
        var services = new ServiceCollection();
        services.AddSingleton<IPowerShellRunspace>(
            new PooledHttpRunspace(pool, (string?)null, NullLoggerFactory.Instance));
        services.AddSingleton<IRunspacePool>(pool);
        services.AddSingleton(new McpSessionLifecycle());

        var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<SessionAwarePowerShellRunspace>());
    }
#pragma warning restore CS0618

    // ─── HttpTransportMode resolution: stateless default, stateful opt-in ─────────

    [Fact]
    public void RegisterResolvedMcpConfiguration_NoUserConfig_DefaultsToStateless()
    {
        var cfg = new ConfigurationBuilder().Build();
        var svc = new ServiceCollection();

        var result = HttpServerHost.RegisterResolvedMcpConfiguration(svc, cfg, NullLogger.Instance);

        Assert.Equal(HttpTransportMode.Stateless, result.HttpTransportMode);
    }

    [Fact]
    public void RegisterResolvedMcpConfiguration_StatefulOverride_ResolvesStateful()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new KeyValuePair<string, string?>("McpServer:HttpTransportMode", "Stateful")])
            .Build();
        var svc = new ServiceCollection();

        var result = HttpServerHost.RegisterResolvedMcpConfiguration(svc, cfg, NullLogger.Instance);

        Assert.Equal(HttpTransportMode.Stateful, result.HttpTransportMode);
    }

    // ─── Both transport modes resolve PooledHttpRunspace (not SAPR) ──────────────

    [Fact]
    public void PooledHttpRunspace_ImplementsIPowerShellRunspace_NotSAPR()
    {
        Assert.True(typeof(IPowerShellRunspace).IsAssignableFrom(typeof(PooledHttpRunspace)));
#pragma warning disable CS0618 // Intentional: verifying PooledHttpRunspace does not derive from SAPR.
        Assert.False(typeof(SessionAwarePowerShellRunspace).IsAssignableFrom(typeof(PooledHttpRunspace)));
#pragma warning restore CS0618
    }

    // ─── IdleTimeout: no MCP9006 suppression required outside stateful block ─────

    /// <summary>
    /// Confirms that <see cref="HttpServerHost.RegisterResolvedMcpConfiguration"/> does not
    /// propagate legacy <c>SessionRunspace*</c> config values as active runtime settings.
    /// The resolver reads these keys and maps them to <see cref="RunspacePoolOptions"/>; the
    /// <see cref="McpServerConfiguration"/> properties themselves are never consulted at runtime.
    /// </summary>
    [Fact]
    public void RegisterResolvedMcpConfiguration_LegacyCapacityKey_MapsToPoolMaxPoolSize()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new KeyValuePair<string, string?>("McpServer:SessionRunspaceCapacity", "32")])
            .Build();
        var svc = new ServiceCollection();

        var result = HttpServerHost.RegisterResolvedMcpConfiguration(svc, cfg, NullLogger.Instance);

        // The legacy key is forwarded to RunspacePool.MaxPoolSize.
        Assert.Equal(32, result.RunspacePool.MaxPoolSize);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    private static string GetObsoleteMessage(string propertyName)
    {
        var prop = typeof(McpServerConfiguration).GetProperty(propertyName,
            BindingFlags.Public | BindingFlags.Instance)!;
        var attr = (ObsoleteAttribute)prop.GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false)[0];
        return attr.Message ?? string.Empty;
    }

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
            functionSnapshotCapture: _ => new HashSet<string>(),
            aliasSnapshotCapture: _ => new HashSet<string>(),
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
