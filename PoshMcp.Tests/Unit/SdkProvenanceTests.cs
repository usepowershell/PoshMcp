using System;
using System.IO;
using System.Text.RegularExpressions;
using PoshMcp.Tests.Characterization;
using PoshMcp.Tests.Characterization.Phase4;
using Xunit;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Tests for runtime SDK provenance detection (<see cref="SdkAssemblyInfo"/>) and the
/// migration-pair gate (<see cref="PerformanceComparator.ValidateSdkVersionPair"/>).
///
/// These prove the harness no longer trusts a hardcoded "ModelContextProtocol 1.4.1" label:
/// the current build is detected as v2 from the real DLL, and a v2-vs-v2 / v1-vs-v1 / swapped /
/// same-binary pairing is rejected — the exact failure that invalidated the prior runs.
/// </summary>
[Trait("Category", "Unit")]
public class SdkProvenanceTests
{
    private static SdkAssemblyDescriptor Desc(int major, string sha) => new()
    {
        AssemblyName = "ModelContextProtocol",
        InformationalVersion = $"{major}.0.0",
        FileVersion = $"{major}.0.0.0",
        MajorVersion = major,
        Path = $"/build/{sha}/ModelContextProtocol.dll",
        Sha256 = sha,
        PackageDisplay = $"ModelContextProtocol {major}.0.0",
    };

    // ── Runtime detection of the current (HEAD, v2) SDK ─────────────────────────────

    [Fact]
    public void DetectFromMeasuredServer_ReportsCurrentSdkAsV2()
    {
        var descriptor = SdkAssemblyInfo.DetectFromMeasuredServer();

        Assert.Equal(2, descriptor.MajorVersion);
        Assert.Contains("ModelContextProtocol", descriptor.AssemblyName);
        Assert.StartsWith("ModelContextProtocol 2", descriptor.PackageDisplay);
        Assert.True(File.Exists(descriptor.Path), $"Resolved SDK DLL should exist: {descriptor.Path}");
        Assert.Matches(new Regex("^[0-9a-f]{64}$"), descriptor.Sha256);
    }

    [Fact]
    public void DetectFromFile_MissingDll_Throws()
    {
        var missing = Path.Combine(Path.GetTempPath(), "does-not-exist", "ModelContextProtocol.dll");
        Assert.Throws<FileNotFoundException>(() => SdkAssemblyInfo.DetectFromFile(missing));
    }

    // ── Migration-pair gate ─────────────────────────────────────────────────────────

    [Fact]
    public void ValidateSdkVersionPair_V1ToV2_Passes()
    {
        // Should not throw for a genuine 1.x -> 2.x pairing.
        PerformanceComparator.ValidateSdkVersionPair(Desc(1, "aa"), Desc(2, "bb"));
    }

    [Fact]
    public void ValidateSdkVersionPair_V2vsV2_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => PerformanceComparator.ValidateSdkVersionPair(Desc(2, "aa"), Desc(2, "bb")));
        Assert.Contains("identical", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateSdkVersionPair_V1vsV1_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => PerformanceComparator.ValidateSdkVersionPair(Desc(1, "aa"), Desc(1, "bb")));
    }

    [Fact]
    public void ValidateSdkVersionPair_Swapped_V2Baseline_Throws()
    {
        // baseline v2, current v1 — direction reversed.
        var ex = Assert.Throws<InvalidOperationException>(
            () => PerformanceComparator.ValidateSdkVersionPair(Desc(2, "aa"), Desc(1, "bb")));
        Assert.Contains("Baseline MCP SDK major", ex.Message);
    }

    [Fact]
    public void ValidateSdkVersionPair_IdenticalSha_Throws()
    {
        // Different majors pass the major check, but identical bytes = same binary.
        var ex = Assert.Throws<InvalidOperationException>(
            () => PerformanceComparator.ValidateSdkVersionPair(Desc(1, "deadbeef"), Desc(2, "deadbeef")));
        Assert.Contains("identical SHA-256", ex.Message);
    }

    [Fact]
    public void ValidateSdkVersionPair_NullDescriptors_Throw()
    {
        Assert.Throws<InvalidOperationException>(
            () => PerformanceComparator.ValidateSdkVersionPair(null, Desc(2, "bb")));
        Assert.Throws<InvalidOperationException>(
            () => PerformanceComparator.ValidateSdkVersionPair(Desc(1, "aa"), null));
    }

    [Fact]
    public void ValidateSdkVersionPair_UndetectableMajor_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => PerformanceComparator.ValidateSdkVersionPair(Desc(0, "aa"), Desc(2, "bb")));
        Assert.Contains("could not be detected", ex.Message);
    }

    [Fact]
    public void ValidateSdkVersionPair_CustomExpectedMajors_Respected()
    {
        // Explicit expected majors let the gate be reused for future migrations.
        PerformanceComparator.ValidateSdkVersionPair(Desc(2, "aa"), Desc(3, "bb"),
            expectedBaselineMajor: 2, expectedCurrentMajor: 3);
    }
}
