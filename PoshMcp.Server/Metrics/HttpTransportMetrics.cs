using System;
using System.Diagnostics.Metrics;
using PoshMcp.Server.PowerShell.Pool;

namespace PoshMcp.Server.Metrics;

/// <summary>
/// OpenTelemetry metrics for HTTP transport mode.
/// Emits a stable low-cardinality gauge: 0 = Stateless, 1 = Stateful.
/// One instance per DI container; one instrument registration per process.
/// </summary>
public sealed class HttpTransportMetrics : IDisposable
{
    private readonly Meter _meter;

    /// <summary>
    /// Initializes <see cref="HttpTransportMetrics"/> with the configured transport mode.
    /// The gauge is registered once at construction and never re-registered.
    /// </summary>
    /// <param name="configuration">Resolved server configuration. Must not be null.</param>
    public HttpTransportMetrics(McpServerConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _meter = new Meter(McpMetrics.MeterName, McpMetrics.MeterVersion);

        long modeValue = configuration.HttpTransportMode == HttpTransportMode.Stateless ? 0L : 1L;

        _meter.CreateObservableGauge<long>(
            "poshmcp.http_transport_mode",
            () => modeValue,
            description: "HTTP transport mode. 0 = Stateless, 1 = Stateful. " +
                         "Stable for the lifetime of the server process.");
    }

    public void Dispose() => _meter.Dispose();
}
