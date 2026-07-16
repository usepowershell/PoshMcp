using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace PoshMcp.Benchmarks;

/// <summary>
/// Identifies the validated result condition for the bounded HTTP-session case.
/// BenchmarkDotNet records timing rather than benchmark return values, so the
/// benchmark throws when the required MCP error is not observed.
/// </summary>
internal sealed class CapacityOutcomeColumn : IColumn
{
    public string Id => nameof(CapacityOutcomeColumn);
    public string ColumnName => "Capacity outcome";
    public string Legend => "Validated bounded-capacity result; a displayed benchmark row means this condition passed.";
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Custom;
    public int PriorityInCategory => 0;
    public bool IsNumeric => false;
    public UnitType UnitType => UnitType.Dimensionless;

    public bool IsAvailable(Summary summary) => true;
    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
        => GetValue(summary, benchmarkCase, SummaryStyle.Default);

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
    {
        return benchmarkCase.Descriptor.WorkloadMethod.Name == nameof(Scenarios.HttpSessionBenchmark.BoundedCapacityRejection)
            ? "overflow MCP error (validated)"
            : "N/A";
    }
}
