using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;
using PoshMcp;
using PoshMcp.Server.PowerShell;
using Xunit;

namespace PoshMcp.Tests.Unit.Diagnostics;

/// <summary>
/// Spec 011 (Issue #267): exercise <see cref="DoctorService.BuildModuleImportsSection"/>
/// across the eight FR-263-12 scenarios plus the SC-263-4 backward-compatibility
/// regression and a renderer snapshot.
/// </summary>
[Trait("Category", "Unit")]
public class DoctorModuleImportsTests
{
    // ── Test fakes ──────────────────────────────────────────────────────────

    private static McpServerTool MakeTool(string toolName, string commandName)
    {
        var stub = new System.Func<string>(() => "stub");
        return McpServerTool.Create(stub, new McpServerToolCreateOptions
        {
            Name = toolName,
            Title = commandName, // McpToolFactoryV2 stores the PowerShell command name in Title
            Description = "stub",
        });
    }

    private static ModuleProbeResult Found(string name, string version = "1.0.0", string path = "C:\\Modules\\stub")
        => new(name, true, version, path);

    private static ModuleProbeResult NotFound(string name)
        => new(name, false, null, null);

    // ── FR-263-12 case 1: known module that resolves and contributes tools ──

    [Fact]
    public void BuildModuleImportsSection_KnownModule_ResolvesAndContributesTools()
    {
        var config = new PowerShellConfiguration { Modules = new() { "Az.Accounts" } };
        var tools = new List<McpServerTool>
        {
            MakeTool("connect_az_account", "Connect-AzAccount"),
            MakeTool("get_az_context", "Get-AzContext"),
        };
        var probes = new List<ModuleProbeResult> { Found("Az.Accounts", "2.13.0", "C:\\Az.Accounts") };

        var section = DoctorService.BuildModuleImportsSection(config, tools, probes, NullLogger.Instance);

        var module = Assert.Single(section.Modules);
        Assert.Equal("Az.Accounts", module.Name);
        Assert.True(module.Found);
        Assert.Equal("2.13.0", module.Version);
        Assert.Equal(2, module.ContributedToolCount);
        Assert.Equal("ok", module.Status);
        Assert.Null(module.Diagnostic);
        Assert.All(section.Tools, t => Assert.Equal("module", t.Source));
        Assert.All(section.Tools, t => Assert.Equal("Az.Accounts", t.SourceDetail));
    }

    // ── FR-263-12 case 2: misnamed module that does not resolve ─────────────

    [Fact]
    public void BuildModuleImportsSection_MisnamedModule_FlagsErrorStatus()
    {
        var config = new PowerShellConfiguration { Modules = new() { "Az.Acconts" } }; // typo
        var tools = new List<McpServerTool>();
        var probes = new List<ModuleProbeResult> { NotFound("Az.Acconts") };

        var section = DoctorService.BuildModuleImportsSection(config, tools, probes, NullLogger.Instance);

        var module = Assert.Single(section.Modules);
        Assert.False(module.Found);
        Assert.Equal("error", module.Status);
        Assert.NotNull(module.Diagnostic);
        Assert.Contains("not found", module.Diagnostic!);
    }

    // ── FR-263-12 case 3: include pattern acting as filter ──────────────────

    [Fact]
    public void BuildModuleImportsSection_IncludePattern_AsFilter_TagsRoleFilter()
    {
        var config = new PowerShellConfiguration
        {
            Modules = new() { "Az.Accounts" },
            IncludePatterns = new() { "Get-*" },
        };
        var tools = new List<McpServerTool>
        {
            MakeTool("get_az_context", "Get-AzContext"),
        };
        var probes = new List<ModuleProbeResult> { Found("Az.Accounts") };

        var section = DoctorService.BuildModuleImportsSection(config, tools, probes, NullLogger.Instance);

        var pattern = Assert.Single(section.Patterns);
        Assert.Equal("filter", pattern.Role);
        Assert.Equal("include", pattern.Kind);
        Assert.Equal(1, pattern.MatchedCount);
        Assert.Equal("ok", pattern.Status);
    }

    // ── FR-263-12 case 4: include pattern acting as discovery ───────────────

    [Fact]
    public void BuildModuleImportsSection_IncludePattern_AsDiscovery_TagsRoleDiscovery()
    {
        var config = new PowerShellConfiguration
        {
            IncludePatterns = new() { "Get-*" },
        };
        var tools = new List<McpServerTool>
        {
            MakeTool("get_thing", "Get-Thing"),
        };
        var probes = new List<ModuleProbeResult>();

        var section = DoctorService.BuildModuleImportsSection(config, tools, probes, NullLogger.Instance);

        var pattern = Assert.Single(section.Patterns);
        Assert.Equal("discovery", pattern.Role);
        Assert.Equal(1, pattern.MatchedCount);
        Assert.Equal("ok", pattern.Status);
    }

    // ── FR-263-12 case 5: exclude pattern that drops nothing ────────────────

    [Fact]
    public void BuildModuleImportsSection_ExcludePattern_NoMatches_StatusOk()
    {
        var config = new PowerShellConfiguration
        {
            Modules = new() { "Az.Accounts" },
            ExcludePatterns = new() { "Remove-*" },
        };
        var tools = new List<McpServerTool>
        {
            MakeTool("get_az_context", "Get-AzContext"),
        };
        var probes = new List<ModuleProbeResult> { Found("Az.Accounts") };

        var section = DoctorService.BuildModuleImportsSection(config, tools, probes, NullLogger.Instance);

        var pattern = Assert.Single(section.Patterns);
        Assert.Equal("exclude", pattern.Role);
        Assert.Equal(0, pattern.MatchedCount);
        Assert.Equal("ok", pattern.Status);
    }

    // ── FR-263-12 case 6: dead include pattern (matches nothing) ────────────

    [Fact]
    public void BuildModuleImportsSection_DeadIncludePattern_FlagsWarning()
    {
        var config = new PowerShellConfiguration
        {
            Modules = new() { "Az.Accounts" },
            IncludePatterns = new() { "DoesNotExist-*" },
        };
        var tools = new List<McpServerTool>
        {
            MakeTool("get_az_context", "Get-AzContext"),
        };
        var probes = new List<ModuleProbeResult> { Found("Az.Accounts") };

        var section = DoctorService.BuildModuleImportsSection(config, tools, probes, NullLogger.Instance);

        var pattern = Assert.Single(section.Patterns);
        Assert.Equal(0, pattern.MatchedCount);
        Assert.Equal("warning", pattern.Status);
        Assert.NotNull(pattern.Diagnostic);
    }

    // ── FR-263-12 case 7: mixed sources (commandName + module + pattern) ────

    [Fact]
    public void BuildModuleImportsSection_MixedSources_PrefersCommandNameThenModule()
    {
        var config = new PowerShellConfiguration
        {
            CommandNames = new() { "Get-Date" },
            Modules = new() { "Az.Accounts" },
            IncludePatterns = new() { "Get-*" },
        };
        var tools = new List<McpServerTool>
        {
            MakeTool("get_date", "Get-Date"),         // → commandName
            MakeTool("get_az_context", "Get-AzContext"), // → module (single-module heuristic)
        };
        var probes = new List<ModuleProbeResult> { Found("Az.Accounts") };

        var section = DoctorService.BuildModuleImportsSection(config, tools, probes, NullLogger.Instance);

        var byTool = section.Tools.ToDictionary(t => t.ToolName);
        Assert.Equal("commandName", byTool["get_date"].Source);
        Assert.Equal("Get-Date", byTool["get_date"].SourceDetail);
        Assert.Equal("module", byTool["get_az_context"].Source);
        Assert.Equal("Az.Accounts", byTool["get_az_context"].SourceDetail);
    }

    // ── FR-263-12 case 8 + SC-263-4: CommandNames-only omits the section ───

    [Fact]
    public void BuildModuleImportsSection_CommandNamesOnly_ReturnsEmptySection()
    {
        var config = new PowerShellConfiguration
        {
            CommandNames = new() { "Get-Date" },
        };
        var tools = new List<McpServerTool> { MakeTool("get_date", "Get-Date") };

        var section = DoctorService.BuildModuleImportsSection(
            config, tools, System.Array.Empty<ModuleProbeResult>(), NullLogger.Instance);

        // FR-263-6: section is empty (renderer omits it entirely).
        Assert.Empty(section.Modules);
        Assert.Empty(section.Patterns);
        Assert.Empty(section.Tools);
    }

    // ── ComputeStatus contract — flips healthy → errors on module errors ────

    [Fact]
    public void ComputeStatus_ModuleError_FlipsHealthyToErrors()
    {
        var report = new DoctorReport
        {
            ModuleImports = new ModuleImportsSection
            {
                Modules = new()
                {
                    new ModuleImportEntry
                    {
                        Name = "Az.Acconts",
                        Found = false,
                        Status = "error",
                        Diagnostic = "not found",
                    },
                },
            },
        };

        var status = DoctorReport.ComputeStatus(report);

        Assert.Equal("errors", status);
    }

    [Fact]
    public void ComputeStatus_PatternWarning_FlipsHealthyToWarnings()
    {
        var report = new DoctorReport
        {
            ModuleImports = new ModuleImportsSection
            {
                Patterns = new()
                {
                    new PatternImportEntry
                    {
                        Pattern = "DoesNotExist-*",
                        Kind = "include",
                        Role = "filter",
                        MatchedCount = 0,
                        Status = "warning",
                        Diagnostic = "no matches",
                    },
                },
            },
        };

        var status = DoctorReport.ComputeStatus(report);

        Assert.Equal("warnings", status);
    }

    // ── Renderer snapshot for case 1 (known module + tools) ─────────────────

    [Fact]
    public void Renderer_KnownModuleWithTools_IncludesModuleImportsSection()
    {
        var report = new DoctorReport
        {
            ModuleImports = new ModuleImportsSection
            {
                Modules = new()
                {
                    new ModuleImportEntry
                    {
                        Name = "Az.Accounts",
                        Found = true,
                        Version = "2.13.0",
                        ContributedToolCount = 1,
                        ContributedToolNames = new() { "get_az_context" },
                        Status = "ok",
                    },
                },
                Tools = new()
                {
                    new ToolImportEntry
                    {
                        ToolName = "get_az_context",
                        CommandName = "Get-AzContext",
                        Source = "module",
                        SourceDetail = "Az.Accounts",
                        Disposition = "exposed",
                    },
                },
            },
        };

        var rendered = DoctorTextRenderer.Render(report);

        Assert.Contains("Module Imports", rendered);
        Assert.Contains("Az.Accounts", rendered);
        Assert.Contains("v2.13.0", rendered);
        Assert.Contains("get_az_context", rendered);
        Assert.Contains("← Az.Accounts", rendered);
    }

    [Fact]
    public void Renderer_EmptyModuleImports_OmitsSectionHeader()
    {
        var report = new DoctorReport(); // ModuleImports default is empty

        var rendered = DoctorTextRenderer.Render(report);

        Assert.DoesNotContain("Module Imports", rendered);
    }
}
