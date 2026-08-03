using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using PoshMcp.Server.Metrics;

namespace PoshMcp.Server.PowerShell.Pool;

/// <summary>
/// OpenTelemetry metrics for the HTTP warm-worker runspace pool.
/// All instruments share the <c>PoshMcp</c> meter used by <see cref="McpMetrics"/>.
/// </summary>
internal sealed class RunspacePoolMetrics : IDisposable
{
    private readonly Meter _meter;

    /// <summary>
    /// Current number of pool workers by lifecycle state.
    /// Tag <c>state</c> values: <c>warm</c>, <c>leased</c>, <c>resetting</c>.
    /// </summary>
    public UpDownCounter<long> WorkerCount { get; }

    /// <summary>Total successful worker acquisitions.</summary>
    public Counter<long> AcquisitionsTotal { get; }

    /// <summary>
    /// Wall-clock time from <c>AcquireAsync</c> call to lease grant (seconds).
    /// Includes channel-wait time; excludes time already in a lease.
    /// </summary>
    public Histogram<double> AcquisitionDurationSeconds { get; }

    /// <summary>Number of acquisition attempts that timed out.</summary>
    public Counter<long> AcquisitionTimeouts { get; }

    /// <summary>
    /// Duration the caller held the lease (seconds).
    /// Measured from <c>AcquireAsync</c> return to lease disposal.
    /// </summary>
    public Histogram<double> LeaseDurationSeconds { get; }

    /// <summary>Workers that failed startup and were never enqueued.</summary>
    public Counter<long> StartupFailures { get; }

    /// <summary>
    /// Workers evicted from the pool.
    /// Tag <c>reason</c> values: <c>idle</c>, <c>reset_failure</c>, <c>broken</c>,
    /// <c>cancel</c>, <c>stop_timeout</c>, <c>explicit</c>, <c>drain</c>.
    /// </summary>
    public Counter<long> Evictions { get; }

    /// <summary>
    /// Time taken to execute the reset protocol after a lease is returned (seconds).
    /// Recorded only for successful resets.
    /// </summary>
    public Histogram<double> ResetDurationSeconds { get; }

    /// <summary>Replenishment cycles that created at least one new worker.</summary>
    public Counter<long> Replenishments { get; }

    public RunspacePoolMetrics()
    {
        _meter = new Meter(McpMetrics.MeterName, McpMetrics.MeterVersion);

        WorkerCount = _meter.CreateUpDownCounter<long>(
            "poshmcp.runspace_pool.workers",
            description: "Current runspace pool workers by lifecycle state. Tag 'state': warm|leased|resetting.");

        AcquisitionsTotal = _meter.CreateCounter<long>(
            "poshmcp.runspace_pool.acquisitions_total",
            description: "Total successful worker acquisitions from the pool.");

        AcquisitionDurationSeconds = _meter.CreateHistogram<double>(
            "poshmcp.runspace_pool.acquisition_duration_seconds",
            unit: "s",
            description: "Wall-clock time from AcquireAsync call to lease grant.");

        AcquisitionTimeouts = _meter.CreateCounter<long>(
            "poshmcp.runspace_pool.acquisition_timeouts_total",
            description: "Acquisition attempts that exceeded the configured AcquisitionTimeout.");

        LeaseDurationSeconds = _meter.CreateHistogram<double>(
            "poshmcp.runspace_pool.lease_duration_seconds",
            unit: "s",
            description: "Duration the caller held a worker lease.");

        StartupFailures = _meter.CreateCounter<long>(
            "poshmcp.runspace_pool.startup_failures_total",
            description: "Workers that failed startup initialization and were never enqueued.");

        Evictions = _meter.CreateCounter<long>(
            "poshmcp.runspace_pool.evictions_total",
            description: "Workers evicted from the pool. Tag 'reason': idle|reset_failure|broken|cancel|stop_timeout|explicit|drain.");

        ResetDurationSeconds = _meter.CreateHistogram<double>(
            "poshmcp.runspace_pool.reset_duration_seconds",
            unit: "s",
            description: "Time taken to execute the reset protocol on a returned worker.");

        Replenishments = _meter.CreateCounter<long>(
            "poshmcp.runspace_pool.replenishments_total",
            description: "Replenishment cycles that created at least one new worker to satisfy MinPoolSize.");
    }

    /// <summary>Returns the tag list for a <see cref="WorkerCount"/> sample.</summary>
    public static KeyValuePair<string, object?> StateTag(string state) =>
        new("state", state);

    /// <summary>Returns the tag list for an <see cref="Evictions"/> sample.</summary>
    public static KeyValuePair<string, object?> ReasonTag(string reason) =>
        new("reason", reason);

    public void Dispose() => _meter.Dispose();
}
