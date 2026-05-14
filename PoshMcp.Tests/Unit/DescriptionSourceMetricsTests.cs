using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Reflection;
using PoshMcp.Server.Metrics;
using PoshMcp.Server.PowerShell;
using Xunit;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Spec 010 FR-590 — verifies the OTel counters
/// <c>poshmcp.tool_description.source</c> and
/// <c>poshmcp.parameter_description.source</c> emit one sample per
/// resolution with the FR-583 wire literal in the <c>step</c> tag.
/// </summary>
[Trait("Category", "Unit")]
public class DescriptionSourceMetricsTests : IDisposable
{
    private readonly McpMetrics _metrics;
    private readonly MeterListener _listener;
    private readonly List<(Instrument Instrument, long Value, IReadOnlyDictionary<string, object?> Tags)> _samples =
        new();

    public DescriptionSourceMetricsTests()
    {
        _metrics = new McpMetrics();
        SetFactoryMetrics(_metrics);

        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == McpMetrics.MeterName &&
                    (instrument.Name == "poshmcp.tool_description.source" ||
                     instrument.Name == "poshmcp.parameter_description.source"))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            var dict = new Dictionary<string, object?>(tags.Length);
            foreach (var kvp in tags)
            {
                dict[kvp.Key] = kvp.Value;
            }
            lock (_samples)
            {
                _samples.Add((instrument, measurement, dict));
            }
        });
        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
        SetFactoryMetrics(null);
        _metrics.Dispose();
    }

    [Theory]
    [InlineData(ToolDescriptionSource.Synopsis, "synopsis")]
    [InlineData(ToolDescriptionSource.Description, "description")]
    [InlineData(ToolDescriptionSource.Syntax, "syntax")]
    [InlineData(ToolDescriptionSource.Name, "name")]
    public void ToolDescriptionSourceCounter_emits_FR583_wire_value_for_each_source(
        ToolDescriptionSource source, string expectedTag)
    {
        InvokeRecordToolDescriptionSourceMetric(source);

        var sample = SingleSampleFor("poshmcp.tool_description.source");
        Assert.Equal(1L, sample.Value);
        Assert.True(sample.Tags.TryGetValue("step", out var tag));
        Assert.Equal(expectedTag, tag);
    }

    [Theory]
    [InlineData(ParameterDescriptionSource.HelpParameter, "helpParameter")]
    [InlineData(ParameterDescriptionSource.HelpMessage, "helpMessage")]
    [InlineData(ParameterDescriptionSource.ValidateSet, "validateSet")]
    [InlineData(ParameterDescriptionSource.TypeFallback, "typeFallback")]
    public void ParameterDescriptionSourceCounter_emits_FR583_wire_value_for_each_source(
        ParameterDescriptionSource source, string expectedTag)
    {
        InvokeRecordParameterDescriptionSourceMetric(source);

        var sample = SingleSampleFor("poshmcp.parameter_description.source");
        Assert.Equal(1L, sample.Value);
        Assert.True(sample.Tags.TryGetValue("step", out var tag));
        Assert.Equal(expectedTag, tag);
    }

    [Fact]
    public void Counter_emission_is_noop_when_metrics_not_configured()
    {
        // Charter: metrics must never crash the application. With no McpMetrics
        // wired, the helpers must silently skip emission.
        SetFactoryMetrics(null);

        var ex = Record.Exception(() =>
        {
            InvokeRecordToolDescriptionSourceMetric(ToolDescriptionSource.Synopsis);
            InvokeRecordParameterDescriptionSourceMetric(ParameterDescriptionSource.HelpParameter);
        });

        Assert.Null(ex);
        Assert.Empty(_samples);
    }

    [Fact]
    public void Counter_emission_swallows_unknown_enum_values()
    {
        // Vocabulary throws ArgumentOutOfRangeException for unknown enum values.
        // The metrics helper must absorb that — emission failures must never
        // propagate to callers (charter).
        var ex = Record.Exception(() =>
        {
            InvokeRecordToolDescriptionSourceMetric((ToolDescriptionSource)999);
            InvokeRecordParameterDescriptionSourceMetric((ParameterDescriptionSource)999);
        });

        Assert.Null(ex);
    }

    [Fact]
    public void Multiple_emissions_each_record_a_sample()
    {
        InvokeRecordToolDescriptionSourceMetric(ToolDescriptionSource.Synopsis);
        InvokeRecordToolDescriptionSourceMetric(ToolDescriptionSource.Description);
        InvokeRecordParameterDescriptionSourceMetric(ParameterDescriptionSource.HelpMessage);

        Assert.Equal(2, _samples.Count(s => s.Instrument.Name == "poshmcp.tool_description.source"));
        Assert.Equal(1, _samples.Count(s => s.Instrument.Name == "poshmcp.parameter_description.source"));
    }

    private (Instrument Instrument, long Value, IReadOnlyDictionary<string, object?> Tags) SingleSampleFor(string name)
    {
        lock (_samples)
        {
            var matches = _samples.Where(s => s.Instrument.Name == name).ToList();
            Assert.Single(matches);
            return matches[0];
        }
    }

    // The metric-emission helpers are private static methods on McpToolFactoryV2.
    // Reflect into them so the tests exercise the exact code path used by the
    // four production call sites (in-proc tool / in-proc param / OOP tool /
    // OOP param) without having to spin up a full discovery cycle.
    private static readonly MethodInfo s_recordToolMethod =
        typeof(McpToolFactoryV2).GetMethod(
            "RecordToolDescriptionSourceMetric",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("RecordToolDescriptionSourceMetric not found");

    private static readonly MethodInfo s_recordParameterMethod =
        typeof(McpToolFactoryV2).GetMethod(
            "RecordParameterDescriptionSourceMetric",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("RecordParameterDescriptionSourceMetric not found");

    private static readonly FieldInfo s_metricsField =
        typeof(McpToolFactoryV2).GetField(
            "_metrics",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("_metrics field not found");

    private static void InvokeRecordToolDescriptionSourceMetric(ToolDescriptionSource source)
    {
        s_recordToolMethod.Invoke(null, new object[] { source });
    }

    private static void InvokeRecordParameterDescriptionSourceMetric(ParameterDescriptionSource source)
    {
        s_recordParameterMethod.Invoke(null, new object[] { source });
    }

    private static void SetFactoryMetrics(McpMetrics? metrics)
    {
        s_metricsField.SetValue(null, metrics);
    }
}
