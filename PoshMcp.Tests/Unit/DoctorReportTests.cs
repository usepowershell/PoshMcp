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

    [Fact]
    public void Build_WithAuthConfig_PopulatesAuthenticationSection()
    {
        var authConfig = new PoshMcp.Server.Authentication.AuthenticationConfiguration
        {
            Enabled = true,
            DefaultScheme = "Bearer",
            DefaultPolicy = new PoshMcp.Server.Authentication.AuthorizationPolicyConfiguration
            {
                RequireAuthentication = true,
                RequiredScopes = ["mcp:read"],
            },
            Schemes = new System.Collections.Generic.Dictionary<string, PoshMcp.Server.Authentication.AuthSchemeConfiguration>
            {
                ["Bearer"] = new PoshMcp.Server.Authentication.AuthSchemeConfiguration
                {
                    Type = "JwtBearer",
                    Authority = "https://login.microsoftonline.com/tenant",
                    Audience = "api://my-app",
                }
            }
        };

        var report = DoctorReport.Build(
            configurationPath: "test.json",
            configurationPathSource: "test",
            effectiveLogLevel: "Warning",
            effectiveLogLevelSource: "default",
            effectiveTransport: "stdio",
            effectiveTransportSource: "default",
            effectiveSessionMode: null,
            effectiveSessionModeSource: "default",
            effectiveRuntimeMode: "InProcess",
            effectiveRuntimeModeSource: "default",
            effectiveMcpPath: null,
            effectiveMcpPathSource: "default",
            configuredFunctionStatus: [],
            toolNames: [],
            powerShellVersion: "7.4.0",
            modulePathEntries: 0,
            modulePaths: [],
            oopModulePaths: [],
            resourcesDiagnostics: new PoshMcp.Server.McpResources.McpResourcesDiagnostics(0, 0, [], []),
            promptsDiagnostics: new PoshMcp.Server.McpPrompts.McpPromptsDiagnostics(0, 0, [], []),
            warnings: [],
            configurationErrors: [],
            environmentVariables: [],
            authConfig: authConfig);

        Assert.True(report.Authentication.Enabled);
        Assert.Equal("Bearer", report.Authentication.DefaultScheme);
        Assert.Equal(["mcp:read"], report.Authentication.RequiredScopes);
        Assert.Single(report.Authentication.ConfiguredSchemes);
        Assert.Equal("JwtBearer", report.Authentication.ConfiguredSchemes[0].Type);
        Assert.True(report.Authentication.ConfiguredSchemes[0].HasAuthority);
        Assert.True(report.Authentication.ConfiguredSchemes[0].HasAudience);
    }

    [Fact]
    public void Build_WithNullAuthConfig_ReturnsDisabledAuthentication()
    {
        var report = DoctorReport.Build(
            configurationPath: "test.json",
            configurationPathSource: "test",
            effectiveLogLevel: null,
            effectiveLogLevelSource: "default",
            effectiveTransport: "stdio",
            effectiveTransportSource: "default",
            effectiveSessionMode: null,
            effectiveSessionModeSource: "default",
            effectiveRuntimeMode: null,
            effectiveRuntimeModeSource: "default",
            effectiveMcpPath: null,
            effectiveMcpPathSource: "default",
            configuredFunctionStatus: [],
            toolNames: [],
            powerShellVersion: "7.4.0",
            modulePathEntries: 0,
            modulePaths: [],
            oopModulePaths: [],
            resourcesDiagnostics: new PoshMcp.Server.McpResources.McpResourcesDiagnostics(0, 0, [], []),
            promptsDiagnostics: new PoshMcp.Server.McpPrompts.McpPromptsDiagnostics(0, 0, [], []),
            warnings: [],
            configurationErrors: [],
            environmentVariables: []);

        Assert.False(report.Authentication.Enabled);
        Assert.False(report.Identity.Available);
    }

    [Fact]
    public void Build_WithAuthenticatedIdentity_PopulatesIdentitySection()
    {
        var claims = new System.Collections.Generic.List<System.Security.Claims.Claim>
        {
            new(System.Security.Claims.ClaimTypes.Name, "test-user"),
            new("scp", "mcp:read"),
            new("roles", "admin"),
        };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, "TestScheme");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);

        var report = DoctorReport.Build(
            configurationPath: "test.json",
            configurationPathSource: "test",
            effectiveLogLevel: null,
            effectiveLogLevelSource: "default",
            effectiveTransport: "http",
            effectiveTransportSource: "default",
            effectiveSessionMode: null,
            effectiveSessionModeSource: "default",
            effectiveRuntimeMode: null,
            effectiveRuntimeModeSource: "default",
            effectiveMcpPath: null,
            effectiveMcpPathSource: "default",
            configuredFunctionStatus: [],
            toolNames: [],
            powerShellVersion: "7.4.0",
            modulePathEntries: 0,
            modulePaths: [],
            oopModulePaths: [],
            resourcesDiagnostics: new PoshMcp.Server.McpResources.McpResourcesDiagnostics(0, 0, [], []),
            promptsDiagnostics: new PoshMcp.Server.McpPrompts.McpPromptsDiagnostics(0, 0, [], []),
            warnings: [],
            configurationErrors: [],
            environmentVariables: [],
            currentIdentity: principal);

        Assert.True(report.Identity.Available);
        Assert.True(report.Identity.IsAuthenticated);
        Assert.Equal("test-user", report.Identity.Name);
        Assert.Contains("mcp:read", report.Identity.Scopes);
        Assert.Contains("admin", report.Identity.Roles);
    }
}
