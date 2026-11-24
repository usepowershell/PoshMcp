using System;
using System.Linq;
using System.Text.Json;
using PoshMcp.Tests.Characterization;
using PoshMcp.Tests.Characterization.Phase4;
using Xunit;

namespace PoshMcp.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class Phase4PreloadedSamplesTests
{
    private static Phase4ModeComparison CollectedMode(string mode)
    {
        var label = mode.ToLowerInvariant();
        return new Phase4ModeComparison
        {
            TransportMode = mode,
            Scenarios = new[]
            {
                "cold_start_http_with_script", "cold_start_http_no_script",
                "warm_call_latency_ms", "concurrent_throughput_ms",
                "warm_call_latency_ms_v2ephemeral", "concurrent_throughput_ms_v2ephemeral",
            }.Select(name => new CharacterizationScenario
            {
                Scenario = $"{name}_{label}",
                Iterations = 3,
                RawSamples = [1, 1, 1],
                Stats = CharacterizationStats.FromSamples([1, 1, 1]),
            }).ToList(),
        };
    }

    [Theory]
    [InlineData("Stateless")]
    [InlineData("Stateful")]
    public void DeferredComparisonRetainsBothStrategiesAndTheirIsolationVerdicts(string mode)
    {
        var collected = CollectedMode(mode);
        collected.Scenarios.Single(s => s.Scenario == $"concurrent_throughput_ms_{mode.ToLowerInvariant()}")
            .RawSamples = [2, 2, 2];
        var throughput = collected.Scenarios.Single(s => s.Scenario == $"concurrent_throughput_ms_{mode.ToLowerInvariant()}");
        throughput.Stats = CharacterizationStats.FromSamples(throughput.RawSamples!);
        var roundTrip = JsonSerializer.Deserialize<Phase4ModeComparison>(JsonSerializer.Serialize(collected))!;
        var preloaded = PreloadedModeData.FromComparison(roundTrip, "captured-before-v1");

        Assert.Equal(4, preloaded.AllScenarios.Count);
        Assert.Equal(2, preloaded.EphemeralScenarios.Count);
        Assert.Equal(new double[] { 2, 2, 2 }, preloaded.ThroughputSamples);
        var compared = PerformanceComparator.CompareSameSdkIsolation(mode, preloaded.EphemeralScenarios, preloaded.AllScenarios);
        Assert.Equal(2, compared.SameSdkIsolationChecks.Count);
        Assert.False(compared.AllPassed);
        Assert.All(compared.SameSdkIsolationChecks, c => Assert.True(c.IsBlocking));
    }

    [Theory]
    [InlineData("warm_call_latency_ms_v2ephemeral")]
    [InlineData("concurrent_throughput_ms_v2ephemeral")]
    public void DeferredComparisonRejectsMissingEphemeralMeasurement(string scenario)
    {
        var collected = CollectedMode("Stateful");
        collected.Scenarios.RemoveAll(s => s.Scenario == $"{scenario}_stateful");
        Assert.Throws<InvalidOperationException>(() => PreloadedModeData.FromComparison(collected, "captured"));
    }

    [Fact]
    public void DeferredComparisonRejectsPartialSampleArray()
    {
        var collected = CollectedMode("Stateless");
        collected.Scenarios[^1].RawSamples = [1];
        Assert.Throws<InvalidOperationException>(() => PreloadedModeData.FromComparison(collected, "captured"));
    }
}
