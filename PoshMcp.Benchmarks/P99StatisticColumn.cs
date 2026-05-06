using System;
using System.Globalization;
using System.Linq;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace PoshMcp.Benchmarks;

/// <summary>
/// Custom <see cref="IColumn"/> reporting the 99th percentile of a benchmark's
/// per-invocation latency. BenchmarkDotNet 0.14 ships <see cref="StatisticColumn.P95"/>
/// but does not expose a built-in P99 column; the AC for issue #194 calls for
/// both, so this fills the gap by walking the same <c>Statistics</c> bag the
/// built-in percentile columns use.
/// </summary>
internal sealed class P99StatisticColumn : IColumn
{
    public string Id => nameof(P99StatisticColumn);
    public string ColumnName => "P99";
    public string Legend => "Percentile 99 of per-operation latency.";
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Statistics;
    public int PriorityInCategory => 0;
    public bool IsNumeric => true;
    public UnitType UnitType => UnitType.Time;

    public bool IsAvailable(Summary summary) => true;
    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
        => GetValue(summary, benchmarkCase, SummaryStyle.Default);

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
    {
        var report = summary[benchmarkCase];
        var stats = report?.ResultStatistics;
        if (stats is null || stats.OriginalValues is null || stats.OriginalValues.Count == 0)
        {
            return "NA";
        }

        // Sort once, then linear-interpolated rank (R-7) for P99.
        var sorted = stats.OriginalValues.OrderBy(v => v).ToArray();
        var rank = 0.99 * (sorted.Length - 1);
        var lo = (int)Math.Floor(rank);
        var hi = (int)Math.Ceiling(rank);
        var weight = rank - lo;
        var p99Ns = sorted[lo] * (1 - weight) + sorted[hi] * weight;

        // Render in ms with 3 decimals — matches the magnitude of the other
        // time columns for the OOP benchmarks (per-invoke latencies typically
        // range from sub-ms warm invokes to multi-second cold starts).
        var p99Ms = p99Ns / 1_000_000.0;
        return p99Ms.ToString("N3", style.CultureInfo ?? CultureInfo.InvariantCulture) + " ms";
    }
}
