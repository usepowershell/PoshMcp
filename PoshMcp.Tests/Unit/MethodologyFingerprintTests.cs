using System.Collections.Generic;
using PoshMcp.Tests.Characterization.Phase4;
using Xunit;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Deterministic tests for <see cref="MethodologyContractValidator"/> (#380 AC3).
/// Proves field-by-field validation, intentional-difference whitelisting,
/// ENV-derived capture gap handling, and expected-value enforcement.
/// </summary>
[Trait("Category", "Unit")]
public class MethodologyFingerprintTests
{
    private static MethodologyContract CreateBaseContract(int sdkMajor = 1) => new()
    {
        Os = "Unix 5.15.0.1",
        DotNetVersion = "10.0.0",
        LogicalProcessors = 4,
        ProcessorModel = "AMD EPYC 7763",
        TotalMemoryKb = 16000000,
        MachineName = "runner-1",
        BuildConfiguration = "Release",
        TargetFramework = "net10.0",
        ToolName = "get_date",
        ToolPayloadDescription = "empty-args-get-date",
        HttpTransportType = "StreamableHttp",
        McpProtocolVersion = "2025-11-25",
        AuthenticationMode = "None",
        TimingMethod = "System.Diagnostics.Stopwatch",
        PercentileAlgorithm = "linear_interpolation_rank_p*(n-1)",
        PercentileImplementation = "CharacterizationStats.FromSamples/1.0",
        VarianceType = "population",
        ThroughputConcurrency = 4,
        MemoryAccountingMethod = "Process.WorkingSet64",
        ServerLifecycle = "per-iteration-cold|shared-warm",
        SdkMajorVersion = sdkMajor,
        SdkSha256 = sdkMajor == 1 ? "aaa111" : "bbb222",
        SourceCommitSha = sdkMajor == 1 ? "baseline-sha" : "current-sha",
        WarmupCounts = new Dictionary<string, int>
        {
            ["warm_call"] = 3,
            ["throughput_per_client"] = 1,
        },
        MeasuredIterations = new Dictionary<string, int>
        {
            ["cold_start"] = 5,
            ["warm_call"] = 50,
            ["throughput"] = 20,
        },
    };

    // ── Valid comparison: no violations ────────────────────────────────────────

    [Fact]
    public void MatchingContracts_NoViolations()
    {
        var baseline = CreateBaseContract(1);
        var current = CreateBaseContract(2);
        var violations = MethodologyContractValidator.Validate(baseline, current);
        Assert.Empty(violations);
    }

    // ── Must-match field mismatches ────────────────────────────────────────────

    [Fact]
    public void DotNetVersion_Mismatch_IsViolation()
    {
        var baseline = CreateBaseContract(1);
        var current = CreateBaseContract(2);
        current.DotNetVersion = "9.0.0";
        var violations = MethodologyContractValidator.Validate(baseline, current);
        Assert.Contains(violations, v => v.Contains("dotNetVersion"));
    }

    [Fact]
    public void Os_Mismatch_IsViolation()
    {
        var baseline = CreateBaseContract(1);
        var current = CreateBaseContract(2);
        current.Os = "Windows 10.0.22000";
        var violations = MethodologyContractValidator.Validate(baseline, current);
        Assert.Contains(violations, v => v.Contains("os"));
    }

    [Fact]
    public void LogicalProcessors_Mismatch_IsViolation()
    {
        var baseline = CreateBaseContract(1);
        var current = CreateBaseContract(2);
        current.LogicalProcessors = 8;
        var violations = MethodologyContractValidator.Validate(baseline, current);
        Assert.Contains(violations, v => v.Contains("logicalProcessors"));
    }

    [Fact]
    public void MachineName_Mismatch_IsViolation()
    {
        var baseline = CreateBaseContract(1);
        var current = CreateBaseContract(2);
        current.MachineName = "runner-2";
        var violations = MethodologyContractValidator.Validate(baseline, current);
        Assert.Contains(violations, v => v.Contains("machineName"));
    }

    [Fact]
    public void BuildConfiguration_Mismatch_IsViolation()
    {
        var baseline = CreateBaseContract(1);
        var current = CreateBaseContract(2);
        current.BuildConfiguration = "Debug";
        var violations = MethodologyContractValidator.Validate(baseline, current);
        Assert.Contains(violations, v => v.Contains("buildConfiguration"));
    }

    [Fact]
    public void ToolName_Mismatch_IsViolation()
    {
        var baseline = CreateBaseContract(1);
        var current = CreateBaseContract(2);
        current.ToolName = "get_process";
        var violations = MethodologyContractValidator.Validate(baseline, current);
        Assert.Contains(violations, v => v.Contains("toolName"));
    }

    [Fact]
    public void ThroughputConcurrency_Mismatch_IsViolation()
    {
        var baseline = CreateBaseContract(1);
        var current = CreateBaseContract(2);
        current.ThroughputConcurrency = 8;
        var violations = MethodologyContractValidator.Validate(baseline, current);
        Assert.Contains(violations, v => v.Contains("throughputConcurrency"));
    }

    [Fact]
    public void PercentileAlgorithm_Mismatch_IsViolation()
    {
        var baseline = CreateBaseContract(1);
        var current = CreateBaseContract(2);
        current.PercentileAlgorithm = "nearest_rank";
        var violations = MethodologyContractValidator.Validate(baseline, current);
        Assert.Contains(violations, v => v.Contains("percentileAlgorithm"));
    }

    [Fact]
    public void MeasuredIterations_Mismatch_IsViolation()
    {
        var baseline = CreateBaseContract(1);
        var current = CreateBaseContract(2);
        current.MeasuredIterations["warm_call"] = 30;
        var violations = MethodologyContractValidator.Validate(baseline, current);
        Assert.Contains(violations, v => v.Contains("measuredIterations[warm_call]"));
    }

    [Fact]
    public void WarmupCounts_Mismatch_IsViolation()
    {
        var baseline = CreateBaseContract(1);
        var current = CreateBaseContract(2);
        current.WarmupCounts["warm_call"] = 5;
        var violations = MethodologyContractValidator.Validate(baseline, current);
        Assert.Contains(violations, v => v.Contains("warmupCounts[warm_call]"));
    }

    // ── ENV-derived: capture gap is not a violation ────────────────────────────

    [Fact]
    public void ProcessorModel_OneEmpty_NotAViolation()
    {
        var baseline = CreateBaseContract(1);
        var current = CreateBaseContract(2);
        current.ProcessorModel = "";
        var violations = MethodologyContractValidator.Validate(baseline, current);
        Assert.DoesNotContain(violations, v => v.Contains("processorModel"));
    }

    [Fact]
    public void TotalMemoryKb_OneZero_NotAViolation()
    {
        var baseline = CreateBaseContract(1);
        var current = CreateBaseContract(2);
        current.TotalMemoryKb = 0;
        var violations = MethodologyContractValidator.Validate(baseline, current);
        Assert.DoesNotContain(violations, v => v.Contains("totalMemoryKb"));
    }

    [Fact]
    public void ProcessorModel_BothPresent_Different_IsViolation()
    {
        var baseline = CreateBaseContract(1);
        var current = CreateBaseContract(2);
        current.ProcessorModel = "Intel Xeon E5";
        var violations = MethodologyContractValidator.Validate(baseline, current);
        Assert.Contains(violations, v => v.Contains("processorModel"));
    }

    [Fact]
    public void TotalMemoryKb_BothPresent_Different_IsViolation()
    {
        var baseline = CreateBaseContract(1);
        var current = CreateBaseContract(2);
        current.TotalMemoryKb = 32000000;
        var violations = MethodologyContractValidator.Validate(baseline, current);
        Assert.Contains(violations, v => v.Contains("totalMemoryKb"));
    }

    // ── Intentional differences: SDK version validation ───────────────────────

    [Fact]
    public void SdkMajor_SameVersion_IsViolation()
    {
        var baseline = CreateBaseContract(1);
        var current = CreateBaseContract(1);
        current.SdkSha256 = "different-sha"; // different binary but same major
        var violations = MethodologyContractValidator.Validate(baseline, current);
        Assert.Contains(violations, v => v.Contains("identical"));
    }

    [Fact]
    public void SdkMajor_Baseline_WrongExpected_IsViolation()
    {
        var baseline = CreateBaseContract(2); // wrong: expected 1
        var current = CreateBaseContract(2);
        current.SdkSha256 = "different-sha";
        var violations = MethodologyContractValidator.Validate(baseline, current);
        Assert.Contains(violations, v => v.Contains("baseline sdkMajorVersion=2, expected 1"));
    }

    [Fact]
    public void SdkMajor_Current_WrongExpected_IsViolation()
    {
        var baseline = CreateBaseContract(1);
        var current = CreateBaseContract(1);
        current.SdkMajorVersion = 3;
        current.SdkSha256 = "different-sha";
        var violations = MethodologyContractValidator.Validate(baseline, current);
        Assert.Contains(violations, v => v.Contains("current sdkMajorVersion=3, expected 2"));
    }

    [Fact]
    public void SdkSha256_Identical_IsViolation()
    {
        var baseline = CreateBaseContract(1);
        var current = CreateBaseContract(2);
        current.SdkSha256 = baseline.SdkSha256; // same binary
        var violations = MethodologyContractValidator.Validate(baseline, current);
        Assert.Contains(violations, v => v.Contains("sdkSha256 are identical"));
    }

    // ── Null inputs ───────────────────────────────────────────────────────────

    [Fact]
    public void NullBaseline_ReturnsViolation()
    {
        var violations = MethodologyContractValidator.Validate(null!, CreateBaseContract(2));
        Assert.Single(violations);
        Assert.Contains("null", violations[0]);
    }

    [Fact]
    public void NullCurrent_ReturnsViolation()
    {
        var violations = MethodologyContractValidator.Validate(CreateBaseContract(1), null!);
        Assert.Single(violations);
        Assert.Contains("null", violations[0]);
    }

    // ── McpProtocolVersion: empty-on-one-side is not a violation ────────────

    [Fact]
    public void McpProtocolVersion_OneEmpty_NotAViolation()
    {
        var baseline = CreateBaseContract(1);
        baseline.McpProtocolVersion = "";
        var current = CreateBaseContract(2);
        var violations = MethodologyContractValidator.Validate(baseline, current);
        Assert.DoesNotContain(violations, v => v.Contains("mcpProtocolVersion"));
    }

    [Fact]
    public void McpProtocolVersion_BothPresent_Different_IsViolation()
    {
        var baseline = CreateBaseContract(1);
        var current = CreateBaseContract(2);
        current.McpProtocolVersion = "2024-11-05";
        var violations = MethodologyContractValidator.Validate(baseline, current);
        Assert.Contains(violations, v => v.Contains("mcpProtocolVersion"));
    }

    // ── Fail-closed key detection (#380 AC3 revision) ─────────────────────────

    [Fact]
    public void WarmupCounts_MissingKeyInCurrent_IsViolation()
    {
        var baseline = CreateBaseContract(1);
        var current = CreateBaseContract(2);
        // Remove a key from current that baseline has
        current.WarmupCounts.Remove("warm_call");
        var violations = MethodologyContractValidator.Validate(baseline, current);
        Assert.Contains(violations, v => v.Contains("warmupCounts[warm_call]") && v.Contains("missing from current"));
    }

    [Fact]
    public void WarmupCounts_ExtraKeyInCurrent_IsViolation()
    {
        var baseline = CreateBaseContract(1);
        var current = CreateBaseContract(2);
        // Add a key to current that baseline doesn't have
        current.WarmupCounts["extra_diagnostic"] = 5;
        var violations = MethodologyContractValidator.Validate(baseline, current);
        Assert.Contains(violations, v => v.Contains("warmupCounts[extra_diagnostic]") && v.Contains("missing from baseline"));
    }

    [Fact]
    public void WarmupCounts_RenamedKey_DetectedAsTwoViolations()
    {
        // This is the exact bug that existed: baseline has "warm_call_latency_ms"
        // but current has "warm_call". Both should be detected.
        var baseline = CreateBaseContract(1);
        baseline.WarmupCounts.Clear();
        baseline.WarmupCounts["warm_call_latency_ms"] = 3;
        baseline.WarmupCounts["concurrent_throughput_ms"] = 4;

        var current = CreateBaseContract(2);
        current.WarmupCounts.Clear();
        current.WarmupCounts["warm_call"] = 3;
        current.WarmupCounts["throughput_per_client"] = 1;

        var violations = MethodologyContractValidator.Validate(baseline, current);
        // Should detect: warm_call_latency_ms missing from current, concurrent_throughput_ms missing from current
        // and: warm_call missing from baseline, throughput_per_client missing from baseline
        Assert.Contains(violations, v => v.Contains("warmupCounts[warm_call_latency_ms]") && v.Contains("missing from current"));
        Assert.Contains(violations, v => v.Contains("warmupCounts[concurrent_throughput_ms]") && v.Contains("missing from current"));
        Assert.Contains(violations, v => v.Contains("warmupCounts[warm_call]") && v.Contains("missing from baseline"));
        Assert.Contains(violations, v => v.Contains("warmupCounts[throughput_per_client]") && v.Contains("missing from baseline"));
    }

    [Fact]
    public void MeasuredIterations_MissingKeyInCurrent_IsViolation()
    {
        var baseline = CreateBaseContract(1);
        var current = CreateBaseContract(2);
        current.MeasuredIterations.Remove("cold_start");
        var violations = MethodologyContractValidator.Validate(baseline, current);
        Assert.Contains(violations, v => v.Contains("measuredIterations[cold_start]") && v.Contains("missing from current"));
    }

    [Fact]
    public void MeasuredIterations_ExtraKeyInCurrent_IsViolation()
    {
        var baseline = CreateBaseContract(1);
        var current = CreateBaseContract(2);
        current.MeasuredIterations["new_scenario"] = 10;
        var violations = MethodologyContractValidator.Validate(baseline, current);
        Assert.Contains(violations, v => v.Contains("measuredIterations[new_scenario]") && v.Contains("missing from baseline"));
    }
}
