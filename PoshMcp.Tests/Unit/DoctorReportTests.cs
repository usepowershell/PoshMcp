using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace PoshMcp.Tests.Unit;

public class DoctorReportTests
{
    // ── ComputeStatus ────────────────────────────────────────────────────────

    [Fact]
    public void ComputeStatus_AllFunctionsFound_NoWarnings_ReturnsHealthy()
    {
        var report = new DoctorReport
        {
            FunctionsTools = new FunctionsToolsSection
            {
                ConfiguredFunctionsMissing = 0,
            },
            McpDefinitions = new McpDefinitionsSection(),
            Warnings = [],
        };

        Assert.Equal("healthy", DoctorReport.ComputeStatus(report));
    }

    [Fact]
    public void ComputeStatus_SomeFunctionsMissing_ReturnsErrors()
    {
        var report = new DoctorReport
        {
            FunctionsTools = new FunctionsToolsSection
            {
                ConfiguredFunctionsMissing = 1,
            },
            McpDefinitions = new McpDefinitionsSection(),
            Warnings = [],
        };

        Assert.Equal("errors", DoctorReport.ComputeStatus(report));
    }

    [Fact]
    public void ComputeStatus_ResourceErrors_ReturnsErrors()
    {
        var report = new DoctorReport
        {
            FunctionsTools = new FunctionsToolsSection { ConfiguredFunctionsMissing = 0 },
            McpDefinitions = new McpDefinitionsSection
            {
                Resources = new McpResourcesDiagSummary
                {
                    Errors = ["resource validation failed"],
                },
            },
            Warnings = [],
        };

        Assert.Equal("errors", DoctorReport.ComputeStatus(report));
    }

    [Fact]
    public void ComputeStatus_PromptErrors_ReturnsErrors()
    {
        var report = new DoctorReport
        {
            FunctionsTools = new FunctionsToolsSection { ConfiguredFunctionsMissing = 0 },
            McpDefinitions = new McpDefinitionsSection
            {
                Prompts = new McpPromptsDiagSummary
                {
                    Errors = ["prompt validation failed"],
                },
            },
            Warnings = [],
        };

        Assert.Equal("errors", DoctorReport.ComputeStatus(report));
    }

    [Fact]
    public void ComputeStatus_AllFoundWithNonEmptyWarnings_ReturnsWarnings()
    {
        var report = new DoctorReport
        {
            FunctionsTools = new FunctionsToolsSection { ConfiguredFunctionsMissing = 0 },
            McpDefinitions = new McpDefinitionsSection(),
            Warnings = ["deprecated FunctionNames key"],
        };

        Assert.Equal("warnings", DoctorReport.ComputeStatus(report));
    }

    [Fact]
    public void ComputeStatus_ResourceWarnings_ReturnsWarnings()
    {
        var report = new DoctorReport
        {
            FunctionsTools = new FunctionsToolsSection { ConfiguredFunctionsMissing = 0 },
            McpDefinitions = new McpDefinitionsSection
            {
                Resources = new McpResourcesDiagSummary
                {
                    Warnings = ["mime type not specified"],
                },
            },
            Warnings = [],
        };

        Assert.Equal("warnings", DoctorReport.ComputeStatus(report));
    }

    // ── DoctorSummary properties ─────────────────────────────────────────────

    [Fact]
    public void DoctorSummary_FunctionCount_MatchesInput()
    {
        var summary = new DoctorSummary { FunctionCount = 5 };
        Assert.Equal(5, summary.FunctionCount);
    }

    [Fact]
    public void DoctorSummary_FoundCount_MatchesInput()
    {
        var summary = new DoctorSummary { FoundCount = 3 };
        Assert.Equal(3, summary.FoundCount);
    }

    [Fact]
    public void DoctorSummary_WarningCount_MatchesInput()
    {
        var summary = new DoctorSummary { WarningCount = 2 };
        Assert.Equal(2, summary.WarningCount);
    }

    // ── JSON serialization top-level keys (T021) ─────────────────────────────

    [Fact]
    public void Serialize_DoctorReport_ProducesExpectedTopLevelKeys()
    {
        var report = new DoctorReport
        {
            Summary = new DoctorSummary { Status = "healthy" },
            RuntimeSettings = new RuntimeSettingsSection(),
            EnvironmentVariables = new Dictionary<string, string?> { ["POSHMCP_TRANSPORT"] = null },
            PowerShell = new PowerShellSection { Version = "7.4.0" },
            FunctionsTools = new FunctionsToolsSection(),
            McpDefinitions = new McpDefinitionsSection(),
            Warnings = [],
        };

        var json = JsonSerializer.Serialize(report);
        var node = JsonNode.Parse(json)?.AsObject();
        Assert.NotNull(node);

        Assert.True(node!.ContainsKey("summary"), "missing 'summary'");
        Assert.True(node.ContainsKey("runtimeSettings"), "missing 'runtimeSettings'");
        Assert.True(node.ContainsKey("environmentVariables"), "missing 'environmentVariables'");
        Assert.True(node.ContainsKey("powerShell"), "missing 'powerShell'");
        Assert.True(node.ContainsKey("functionsTools"), "missing 'functionsTools'");
        Assert.True(node.ContainsKey("mcpDefinitions"), "missing 'mcpDefinitions'");
        Assert.True(node.ContainsKey("warnings"), "missing 'warnings'");
    }

    [Fact]
    public void Serialize_DoctorReport_DoesNotContainEffectivePowerShellConfiguration()
    {
        var report = new DoctorReport();
        var json = JsonSerializer.Serialize(report);
        var node = JsonNode.Parse(json)?.AsObject();
        Assert.NotNull(node);
        Assert.False(node!.ContainsKey("effectivePowerShellConfiguration"),
            "effectivePowerShellConfiguration was dropped in spec-006 and must not appear");
    }

    [Fact]
    public void Serialize_DoctorReport_CamelCasePropertyNames()
    {
        var report = new DoctorReport
        {
            Summary = new DoctorSummary
            {
                Status = "healthy",
                FunctionCount = 1,
                FoundCount = 1,
                WarningCount = 0,
            },
        };

        var json = JsonSerializer.Serialize(report);
        var node = JsonNode.Parse(json)?.AsObject();
        Assert.NotNull(node);

        var summary = node!["summary"]?.AsObject();
        Assert.NotNull(summary);
        Assert.True(summary!.ContainsKey("status"), "summary.status missing (camelCase)");
        Assert.True(summary.ContainsKey("functionCount"), "summary.functionCount missing (camelCase)");
        Assert.True(summary.ContainsKey("foundCount"), "summary.foundCount missing (camelCase)");
        Assert.True(summary.ContainsKey("warningCount"), "summary.warningCount missing (camelCase)");

        var runtimeSettings = node["runtimeSettings"]?.AsObject();
        Assert.NotNull(runtimeSettings);
        Assert.True(runtimeSettings!.ContainsKey("configurationMode"), "runtimeSettings.configurationMode missing (camelCase)");
    }
}
