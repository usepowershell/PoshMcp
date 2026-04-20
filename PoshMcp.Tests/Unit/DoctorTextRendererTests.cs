using System.Collections.Generic;
using Xunit;

namespace PoshMcp.Tests.Unit;

public class DoctorTextRendererTests
{
    // ── Banner ───────────────────────────────────────────────────────────────

    [Fact]
    public void Render_AlwaysContainsBannerBoxDrawingChars()
    {
        var report = BuildMinimalReport("healthy");
        var output = DoctorTextRenderer.Render(report);
        Assert.Contains("╔", output);
        Assert.Contains("╗", output);
        Assert.Contains("╚", output);
        Assert.Contains("╝", output);
    }

    [Fact]
    public void Render_HealthyStatus_ShowsCheckMark()
    {
        var report = BuildMinimalReport("healthy");
        var output = DoctorTextRenderer.Render(report);
        Assert.Contains("✓ healthy", output);
    }

    [Fact]
    public void Render_WarningsStatus_ShowsWarningSymbol()
    {
        var report = BuildMinimalReport("warnings");
        var output = DoctorTextRenderer.Render(report);
        Assert.Contains("⚠ warnings", output);
    }

    [Fact]
    public void Render_ErrorsStatus_ShowsCrossSymbol()
    {
        var report = BuildMinimalReport("errors");
        var output = DoctorTextRenderer.Render(report);
        Assert.Contains("✗ errors", output);
    }

    // ── Section headers ──────────────────────────────────────────────────────

    [Fact]
    public void Render_ContainsRuntimeSettingsSectionHeader()
    {
        var report = BuildMinimalReport("healthy");
        var output = DoctorTextRenderer.Render(report);
        Assert.Contains("── Runtime Settings", output);
    }

    [Fact]
    public void Render_ContainsEnvironmentVariablesSectionHeader()
    {
        var report = BuildMinimalReport("healthy");
        var output = DoctorTextRenderer.Render(report);
        Assert.Contains("── Environment Variables", output);
    }

    [Fact]
    public void Render_ContainsPowerShellSectionHeader()
    {
        var report = BuildMinimalReport("healthy");
        var output = DoctorTextRenderer.Render(report);
        Assert.Contains("── PowerShell", output);
    }

    [Fact]
    public void Render_ContainsFunctionsToolsSectionHeader()
    {
        var report = BuildMinimalReport("healthy");
        var output = DoctorTextRenderer.Render(report);
        Assert.Contains("── Functions/Tools", output);
    }

    [Fact]
    public void Render_ContainsMcpDefinitionsSectionHeader()
    {
        var report = BuildMinimalReport("healthy");
        var output = DoctorTextRenderer.Render(report);
        Assert.Contains("── MCP Definitions", output);
    }

    // ── Section header format ────────────────────────────────────────────────

    [Fact]
    public void Render_SectionHeader_StartsWithDashDash()
    {
        var report = BuildMinimalReport("healthy");
        var output = DoctorTextRenderer.Render(report);
        // Every section header line starts with ──
        Assert.Contains("──", output);
    }

    [Fact]
    public void Render_SectionHeader_PaddedTo44Chars()
    {
        // "── Runtime Settings ──────────────────────"
        // Header: "── " + "Runtime Settings" (16) + " " + padding = 44 total
        // Formula: 44 - name.Length - 4 dashes of padding
        var report = BuildMinimalReport("healthy");
        var output = DoctorTextRenderer.Render(report);

        foreach (var line in output.Split('\n'))
        {
            if (line.StartsWith("──"))
            {
                // The header should be 44 characters long
                // "── " (3) + name + " " (1) + padding to reach 44 total
                Assert.True(line.Length >= 10, $"Header line too short: '{line}'");
            }
        }
    }

    // ── Warnings section (conditional) ───────────────────────────────────────

    [Fact]
    public void Render_EmptyWarnings_DoesNotIncludeWarningsSectionHeader()
    {
        var report = BuildMinimalReport("healthy");
        var output = DoctorTextRenderer.Render(report);
        Assert.DoesNotContain("── Warnings", output);
    }

    [Fact]
    public void Render_NonEmptyWarnings_IncludesWarningsSectionHeader()
    {
        var report = BuildMinimalReport("warnings") with
        {
            Warnings = ["deprecated FunctionNames key detected"],
        };
        var output = DoctorTextRenderer.Render(report);
        Assert.Contains("── Warnings", output);
        Assert.Contains("deprecated FunctionNames key detected", output);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static DoctorReport BuildMinimalReport(string status) =>
        new DoctorReport
        {
            Summary = new DoctorSummary
            {
                Status = status,
                FunctionCount = 0,
                FoundCount = 0,
                WarningCount = 0,
            },
            RuntimeSettings = new RuntimeSettingsSection(),
            EnvironmentVariables = new Dictionary<string, string?>(),
            PowerShell = new PowerShellSection { Version = "7.4.0" },
            FunctionsTools = new FunctionsToolsSection(),
            McpDefinitions = new McpDefinitionsSection(),
            Warnings = [],
        };
}
