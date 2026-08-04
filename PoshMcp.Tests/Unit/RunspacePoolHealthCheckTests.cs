using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PoshMcp.Server.Health;
using PoshMcp.Server.Metrics;
using PoshMcp.Server.PowerShell.Pool;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="RunspacePoolHealthCheck"/>.
/// G1: all classification combinations, G5: gauge, G6: GetStats()-only, G7: edge cases.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RunspacePoolHealthCheckTests
{
    private readonly ITestOutputHelper _output;

    public RunspacePoolHealthCheckTests(ITestOutputHelper output) => _output = output;

    // ─── Helpers ────────────────────────────────────────────────────────────────

    private static Mock<IRunspacePool> MockPool(RunspacePoolStats stats)
    {
        var pool = new Mock<IRunspacePool>();
        pool.Setup(p => p.GetStats()).Returns(stats);
        return pool;
    }

    private static RunspacePoolHealthCheck MakeCheck(IRunspacePool pool) =>
        new(pool, NullLogger<RunspacePoolHealthCheck>.Instance);

    private static RunspacePoolStats Defaults(
        int min = 2, int max = 16, int warm = 2, int leased = 0,
        int resetting = 0, int total = 2, int creating = 0,
        bool isDraining = false, bool isStarted = true) =>
        new(min, max, warm, leased, resetting, total,
            CreatingWorkers: creating, IsDraining: isDraining, IsStarted: isStarted);

    // ─── G1: Classification combinations ────────────────────────────────────────

    [Fact]
    public async Task CheckHealth_NotStarted_ReturnsUnhealthy()
    {
        var stats = Defaults(warm: 0, total: 0, isStarted: false);
        var check = MakeCheck(MockPool(stats).Object);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("not started", result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("initializing", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckHealth_NotStarted_HasExpectedDataKeys()
    {
        var stats = Defaults(warm: 0, total: 0, isStarted: false);
        var check = MakeCheck(MockPool(stats).Object);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(false, result.Data["is_started"]);
    }

    [Fact]
    public async Task CheckHealth_Draining_ReturnsDegraded()
    {
        // Even with warm >= min, draining takes precedence over Healthy.
        var stats = Defaults(warm: 2, total: 2, isDraining: true);
        var check = MakeCheck(MockPool(stats).Object);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("draining", result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(true, result.Data["is_draining"]);
    }

    [Fact]
    public async Task CheckHealth_DrainingPrecedesWarmCount_ReturnsDegraded()
    {
        // Warm = min = 5, but draining → Degraded (not Healthy).
        var stats = Defaults(min: 5, warm: 5, total: 5, isDraining: true);
        var check = MakeCheck(MockPool(stats).Object);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task CheckHealth_WarmEqualsMin_ReturnsHealthy()
    {
        var stats = Defaults(min: 2, warm: 2, total: 2);
        var check = MakeCheck(MockPool(stats).Object);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("healthy", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckHealth_WarmExceedsMin_ReturnsHealthy()
    {
        var stats = Defaults(min: 1, warm: 4, total: 4);
        var check = MakeCheck(MockPool(stats).Object);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealth_WarmBelowMin_WithCreating_ReturnsDegraded()
    {
        // G1 — (warm+leased)=1 < MinPoolSize=2 but workers are being created → Degraded.
        var stats = Defaults(min: 2, warm: 1, total: 3, creating: 2);
        var check = MakeCheck(MockPool(stats).Object);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("degraded", result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, result.Data["creating"]);
    }

    [Fact]
    public async Task CheckHealth_WarmBelowMin_NoCreating_ReturnsUnhealthy()
    {
        // G1 — (warm+leased)=0 < MinPoolSize=2, no workers creating → Unhealthy.
        var stats = Defaults(min: 2, warm: 0, total: 0, creating: 0);
        var check = MakeCheck(MockPool(stats).Object);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("unhealthy", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckHealth_ZeroWarm_ZeroCreating_ReturnsUnhealthy()
    {
        var stats = Defaults(min: 1, warm: 0, total: 0, creating: 0);
        var check = MakeCheck(MockPool(stats).Object);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    /// <summary>
    /// All MinPoolSize workers are leased (actively serving requests).
    /// The pool is at capacity — healthy, not degraded.
    /// This is the key formula difference from the old warm-only threshold.
    /// </summary>
    [Fact]
    public async Task CheckHealth_AllWorkersLeased_AtMin_ReturnsHealthy()
    {
        var stats = Defaults(min: 2, warm: 0, leased: 2, total: 2, creating: 0);
        var check = MakeCheck(MockPool(stats).Object);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("healthy", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Mix: 1 warm + 1 leased with min=2 → (warm+leased)=2 >= 2 → Healthy.
    /// Concurrent checks hold one lease while pool still has one warm worker.
    /// </summary>
    [Fact]
    public async Task CheckHealth_PartialWarmPartialLeased_AtMin_ReturnsHealthy()
    {
        var stats = Defaults(min: 2, warm: 1, leased: 1, total: 2, creating: 0);
        var check = MakeCheck(MockPool(stats).Object);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    /// <summary>
    /// Warm=0, Leased=1, min=2, creating=0 → total active=1 &lt; 2 → Unhealthy.
    /// Leased is counted but total active capacity is still below min.
    /// </summary>
    [Fact]
    public async Task CheckHealth_WarmZero_LeasedBelowMin_NoCreating_ReturnsUnhealthy()
    {
        var stats = Defaults(min: 2, warm: 0, leased: 1, total: 1, creating: 0);
        var check = MakeCheck(MockPool(stats).Object);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    /// <summary>
    /// Warm=0, Leased=1, min=2, creating=1 → total active=1 &lt; 2 but rebuilding → Degraded.
    /// </summary>
    [Fact]
    public async Task CheckHealth_WarmZero_LeasedBelowMin_WithCreating_ReturnsDegraded()
    {
        var stats = Defaults(min: 2, warm: 0, leased: 1, total: 2, creating: 1);
        var check = MakeCheck(MockPool(stats).Object);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    // ─── G6: GetStats-only — AcquireAsync must never be called ────────────────

    [Fact]
    public async Task CheckHealth_NeverCallsAcquireAsync()
    {
        var pool = new Mock<IRunspacePool>(MockBehavior.Strict);
        pool.Setup(p => p.GetStats()).Returns(Defaults());
        // No setup for AcquireAsync — Strict mock will fail if it is called.

        var check = MakeCheck(pool.Object);
        await check.CheckHealthAsync(new HealthCheckContext());

        pool.Verify(p => p.GetStats(), Times.Once);
    }

    [Fact]
    public async Task CheckHealth_GetStatsThrows_ReturnsUnhealthy()
    {
        // G7: fault boundary.
        var pool = new Mock<IRunspacePool>();
        pool.Setup(p => p.GetStats()).Throws(new InvalidOperationException("pool fault"));
        var check = MakeCheck(pool.Object);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("pool fault", result.Description, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(result.Exception);
    }

    // ─── Constructor guard ────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullPool_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new RunspacePoolHealthCheck(null!, NullLogger<RunspacePoolHealthCheck>.Instance));
        Assert.Equal("pool", ex.ParamName);
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var pool = new Mock<IRunspacePool>();
        var ex = Assert.Throws<ArgumentNullException>(
            () => new RunspacePoolHealthCheck(pool.Object, null!));
        Assert.Equal("logger", ex.ParamName);
    }

    // ─── G5: HttpTransportMetrics gauge — Stateless = 0, Stateful = 1 ─────────

    [Fact]
    public void HttpTransportMetrics_Stateless_EmitsGaugeZero()
    {
        var config = new McpServerConfiguration
        {
            HttpTransportMode = HttpTransportMode.Stateless
        };

        long? observed = null;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, ml) =>
        {
            if (instrument.Name == "poshmcp.http_transport_mode")
                ml.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
        {
            if (instrument.Name == "poshmcp.http_transport_mode")
                observed = measurement;
        });
        listener.Start();

        using var metrics = new HttpTransportMetrics(config);
        listener.RecordObservableInstruments();

        Assert.Equal(0L, observed);
    }

    [Fact]
    public void HttpTransportMetrics_Stateful_EmitsGaugeOne()
    {
        var config = new McpServerConfiguration
        {
            HttpTransportMode = HttpTransportMode.Stateful
        };

        long? observed = null;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, ml) =>
        {
            if (instrument.Name == "poshmcp.http_transport_mode")
                ml.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
        {
            if (instrument.Name == "poshmcp.http_transport_mode")
                observed = measurement;
        });
        listener.Start();

        using var metrics = new HttpTransportMetrics(config);
        listener.RecordObservableInstruments();

        Assert.Equal(1L, observed);
    }

    [Fact]
    public void HttpTransportMetrics_NoDuplicateInstrument_SecondInstanceDistinct()
    {
        // Two distinct HttpTransportMetrics instances must not interfere:
        // each registers a gauge on its own Meter; both are independently observable.
        var statelessConfig = new McpServerConfiguration { HttpTransportMode = HttpTransportMode.Stateless };
        var statefulConfig = new McpServerConfiguration { HttpTransportMode = HttpTransportMode.Stateful };

        var measurements = new List<long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, ml) =>
        {
            if (instrument.Name == "poshmcp.http_transport_mode")
                ml.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
        {
            if (instrument.Name == "poshmcp.http_transport_mode")
                measurements.Add(measurement);
        });
        listener.Start();

        using var m1 = new HttpTransportMetrics(statelessConfig);
        using var m2 = new HttpTransportMetrics(statefulConfig);

        listener.RecordObservableInstruments();

        // Both meters observed; values include 0 (stateless) and 1 (stateful).
        Assert.Contains(0L, measurements);
        Assert.Contains(1L, measurements);
    }

    [Fact]
    public void HttpTransportMetrics_NullConfiguration_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new HttpTransportMetrics(null!));
        Assert.Equal("configuration", ex.ParamName);
    }

    // ─── Data fields exposed in result ────────────────────────────────────────

    [Fact]
    public async Task CheckHealth_Result_ContainsAllExpectedDataKeys()
    {
        var stats = Defaults();
        var check = MakeCheck(MockPool(stats).Object);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        var requiredKeys = new[] { "warm", "leased", "resetting", "creating", "total", "min", "max", "is_started", "is_draining" };
        foreach (var key in requiredKeys)
        {
            Assert.True(result.Data.ContainsKey(key), $"Expected data key '{key}' not found.");
        }
    }

    // ─── Cancellation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckHealth_CancelledToken_DoesNotBlock()
    {
        // Health check reads stats synchronously — cancellation should not hang.
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var stats = Defaults();
        var check = MakeCheck(MockPool(stats).Object);

        // Should complete without blocking (stats read is synchronous).
        var result = await check.CheckHealthAsync(new HealthCheckContext(), cts.Token);
        // HealthCheckResult is a struct — assert via status (not null check).
        Assert.True(result.Status is HealthStatus.Healthy or HealthStatus.Degraded or HealthStatus.Unhealthy);
    }
}
