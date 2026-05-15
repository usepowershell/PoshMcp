using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;
using PoshMcp;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.OutOfProcess;
using Xunit;

namespace PoshMcp.Tests.Unit.Diagnostics;

/// <summary>
/// Spec 011 Phase 2 (Issue #268): exercise the
/// <see cref="DoctorService.BuildModuleImportsSection(PowerShellConfiguration, List{McpServerTool}, RemoteModuleImportsPayload?, Microsoft.Extensions.Logging.ILogger)"/>
/// overload that consumes the OOP host's <see cref="RemoteModuleImportsPayload"/>
/// instead of running the in-process <c>Get-Module -ListAvailable</c> probe.
/// </summary>
[Trait("Category", "Unit")]
public class DoctorModuleImportsOopPayloadTests
{
    private static McpServerTool MakeTool(string toolName, string commandName)
    {
        var stub = new System.Func<string>(() => "stub");
        return McpServerTool.Create(stub, new McpServerToolCreateOptions
        {
            Name = toolName,
            Title = commandName,
            Description = "stub",
        });
    }

    [Fact]
    public void OopPayload_KnownModule_UsesPayloadProbeData_NotInProcessProbe()
    {
        var config = new PowerShellConfiguration { Modules = new() { "Az.Accounts" } };
        var tools = new List<McpServerTool>
        {
            MakeTool("connect_az_account", "Connect-AzAccount"),
            MakeTool("get_az_context", "Get-AzContext"),
        };
        var payload = new RemoteModuleImportsPayload
        {
            Modules =
            [
                new RemoteModuleProbe
                {
                    Name = "Az.Accounts",
                    Found = true,
                    Version = "9.9.9",
                    Path = "C:\\OopProvided\\Az.Accounts.psd1",
                },
            ],
        };

        var section = DoctorService.BuildModuleImportsSection(config, tools, payload, NullLogger.Instance);

        var module = Assert.Single(section.Modules);
        // OOP-provided values appear verbatim — proves the in-process probe was NOT run.
        Assert.True(module.Found);
        Assert.Equal("9.9.9", module.Version);
        Assert.Equal("C:\\OopProvided\\Az.Accounts.psd1", module.Path);
        Assert.Equal("ok", module.Status);
    }

    [Fact]
    public void OopPayload_MissingEntryForConfiguredModule_FallsBackToNotFound()
    {
        var config = new PowerShellConfiguration { Modules = new() { "Az.Accounts", "Az.Compute" } };
        var tools = new List<McpServerTool>();
        // Payload only describes Az.Accounts; Az.Compute should fall back to not-found.
        var payload = new RemoteModuleImportsPayload
        {
            Modules =
            [
                new RemoteModuleProbe { Name = "Az.Accounts", Found = true, Version = "1.0.0", Path = "X" },
            ],
        };

        var section = DoctorService.BuildModuleImportsSection(config, tools, payload, NullLogger.Instance);

        Assert.Equal(2, section.Modules.Count);
        var compute = section.Modules.Single(m => m.Name == "Az.Compute");
        Assert.False(compute.Found);
        Assert.Null(compute.Version);
        Assert.Null(compute.Path);
        Assert.Equal("error", compute.Status);
    }

    [Fact]
    public void OopPayload_Null_BehavesIdenticallyToLegacyOverload()
    {
        // SC-263-4: backward-compat. With payload null and no modules configured
        // the section computation runs entirely from `tools` and returns the
        // same shape as the existing 3-arg overload.
        var config = new PowerShellConfiguration
        {
            CommandNames = new() { "Write-Host" },
        };
        var tools = new List<McpServerTool> { MakeTool("write_host", "Write-Host") };

        var withPayload = DoctorService.BuildModuleImportsSection(config, tools, oopPayload: null, NullLogger.Instance);
        var legacy = DoctorService.BuildModuleImportsSection(config, tools, NullLogger.Instance);

        // CommandNames-only configs return an empty section per FR-263-6.
        Assert.Empty(withPayload.Modules);
        Assert.Empty(withPayload.Patterns);
        Assert.Empty(withPayload.Tools);
        Assert.Empty(legacy.Modules);
        Assert.Empty(legacy.Patterns);
        Assert.Empty(legacy.Tools);
    }

    [Fact]
    public void OopPayload_CommandNamesOnly_ReturnsEmptySection()
    {
        // FR-263-6: even if the OOP host emits a payload, a CommandNames-only
        // config still yields an empty moduleImports section.
        var config = new PowerShellConfiguration { CommandNames = new() { "Write-Host" } };
        var tools = new List<McpServerTool> { MakeTool("write_host", "Write-Host") };
        var payload = new RemoteModuleImportsPayload
        {
            Modules = [new RemoteModuleProbe { Name = "Microsoft.PowerShell.Utility", Found = true, Version = "7.0", Path = "X" }],
        };

        var section = DoctorService.BuildModuleImportsSection(config, tools, payload, NullLogger.Instance);

        Assert.Empty(section.Modules);
        Assert.Empty(section.Patterns);
        Assert.Empty(section.Tools);
    }
}
