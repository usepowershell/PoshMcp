using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using PoshMcp;
using PoshMcp.Server.McpResources;
using PoshMcp.Server.PowerShell;
using Xunit;

namespace PoshMcp.Tests.Unit.Diagnostics;

[Trait("Category", "Unit")]
public class DoctorNounResourcesTests
{
    [Fact]
    public void BuildNounResourcesSection_UsesEffectiveOverrideValues_AndTracksSuppressedNouns()
    {
        var config = new PowerShellConfiguration
        {
            EnableNounResources = true,
            CommandNames = new()
            {
                "Get-NounResourceFixture",
                "Get-DisabledFixture"
            },
            NounResourceOverrides = new Dictionary<string, NounResourceOverride>(StringComparer.OrdinalIgnoreCase)
            {
                ["noun_resource_fixture"] = new NounResourceOverride
                {
                    ResourceName = "fixture_override",
                    Uri = "poshmcp://resources/fixture_override"
                },
                ["disabled_fixture"] = new NounResourceOverride
                {
                    Disabled = true
                }
            }
        };

        var nounRegistry = NounRegistry.Build(config.GetEffectiveCommandNames(), NullLogger.Instance);

        var section = DoctorService.BuildNounResourcesSection(config, nounRegistry);

        Assert.True(section.Enabled);

        var resource = Assert.Single(section.RegisteredResources);
        Assert.Equal("NounResourceFixture", resource.Noun);
        Assert.Equal("fixture_override", resource.ResourceName);
        Assert.Equal("poshmcp://resources/fixture_override", resource.Uri);
        Assert.Equal("Get-NounResourceFixture", resource.CanonicalGetCommand);

        Assert.Equal(new[] { "DisabledFixture" }, section.SuppressedNouns);
        Assert.Empty(section.Conflicts);
    }

    [Fact]
    public void Render_IncludesNounResourcesSectionWithEffectiveOverrideValues()
    {
        var report = BuildMinimalReport() with
        {
            NounResources = new NounResourcesSection
            {
                Enabled = true,
                RegisteredResources =
                [
                    new NounResourceEntry
                    {
                        Noun = "NounResourceFixture",
                        ResourceName = "fixture_override",
                        Uri = "poshmcp://resources/fixture_override",
                        CanonicalGetCommand = "Get-NounResourceFixture"
                    }
                ],
                SuppressedNouns = ["DisabledFixture"]
            }
        };

        var output = DoctorTextRenderer.Render(report);

        Assert.Contains("── Noun Resources", output);
        Assert.Contains("fixture_override (Get-NounResourceFixture) → poshmcp://resources/fixture_override", output);
        Assert.Contains("suppressed : 1 noun(s)", output);
        Assert.Contains("DisabledFixture", output);
        Assert.DoesNotContain("noun_resource_fixture (Get-NounResourceFixture)", output);
    }

    private static DoctorReport BuildMinimalReport() =>
        new()
        {
            Summary = new DoctorSummary
            {
                Status = "healthy",
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