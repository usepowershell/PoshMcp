using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;

namespace PoshMcp.Benchmarks;

/// <summary>
/// Shared BenchmarkDotNet configuration for the OOP executor comparison.
/// Per the experiment plan (specs/004-out-of-process-execution/runspace-pool-experiment-plan.md §4),
/// per-scenario thresholds live on individual [Benchmark] methods (via
/// [MinIterationCount]/[MaxIterationCount]) rather than as a single
/// global bar — a 4× speedup target on CPU-bound work would reject a
/// winning design on a scenario that isn't its job.
/// </summary>
public sealed class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig()
    {
        // Markdown output for results-table consumption by spec-004 followups.
        AddExporter(MarkdownExporter.GitHub);

        // Console logger so individual runs are visible during long benchmarks.
        AddLogger(ConsoleLogger.Default);

        // Inherit the default columns / column providers / validators from
        // BenchmarkDotNet so we don't have to enumerate them here. Scenarios
        // that need per-scenario tuning use attributes on the [Benchmark] methods.
        Add(DefaultConfig.Instance);

        // Surface percentile columns called out in the AC for #194:
        // "Markdown table includes columns: mode, scenario, payload size,
        //  mean, p95, p99, crash-recovery time".
        // BDN ships a static StatisticColumn.P95 but not P99 — see
        // P99StatisticColumn for the custom column we add to fill the gap.
        // For crash-recovery scenarios the Mean column IS the recovery time
        // (each iteration kills + recovers); documented in
        // PoshMcp.Benchmarks/README.md.
        AddColumn(StatisticColumn.P95);
        AddColumn(new P99StatisticColumn());
        AddColumn(new CapacityOutcomeColumn());

        SummaryStyle = SummaryStyle.Default.WithRatioStyle(RatioStyle.Trend);
    }
}
