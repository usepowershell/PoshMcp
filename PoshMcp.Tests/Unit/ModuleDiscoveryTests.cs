using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PoshMcp.Server.PowerShell;
using Xunit;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="ModuleDiscovery"/>. Verifies in-process module
/// probing semantics from spec 011 — FR-263-10 (one Get-Module call per
/// module name, never per command) and the contract used by the doctor
/// report's <c>moduleImports</c> section.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ModuleDiscoveryTests : IDisposable
{
    private readonly IsolatedPowerShellRunspace _runspace;

    public ModuleDiscoveryTests()
    {
        _runspace = new IsolatedPowerShellRunspace();
    }

    [Fact]
    public void ProbeModules_NullList_ReturnsEmpty()
    {
        var results = ModuleDiscovery.ProbeModules(_runspace, moduleNames: null);

        Assert.NotNull(results);
        Assert.Empty(results);
    }

    [Fact]
    public void ProbeModules_EmptyList_ReturnsEmpty()
    {
        var results = ModuleDiscovery.ProbeModules(_runspace, Array.Empty<string>());

        Assert.NotNull(results);
        Assert.Empty(results);
    }

    [Fact]
    public void ProbeModules_OnlyBlankEntries_ReturnsEmpty()
    {
        var results = ModuleDiscovery.ProbeModules(_runspace, new[] { string.Empty, "   ", "\t" });

        Assert.NotNull(results);
        Assert.Empty(results);
    }

    [Fact]
    public void ProbeModules_NonExistentModule_ReturnsFoundFalse()
    {
        // A name that cannot exist on PSModulePath under any reasonable setup.
        var name = "PoshMcp_NonExistent_Module_" + Guid.NewGuid().ToString("N");

        var results = ModuleDiscovery.ProbeModules(_runspace, new[] { name });

        var single = Assert.Single(results);
        Assert.Equal(name, single.Name);
        Assert.False(single.Found);
        Assert.Null(single.Version);
        Assert.Null(single.Path);
    }

    [Fact]
    public void ProbeModules_BuiltInModule_ReturnsFoundWithVersionAndPath()
    {
        // Microsoft.PowerShell.Management ships with PowerShell itself and
        // is always available on PSModulePath.
        const string moduleName = "Microsoft.PowerShell.Management";

        var results = ModuleDiscovery.ProbeModules(_runspace, new[] { moduleName });

        var single = Assert.Single(results);
        Assert.Equal(moduleName, single.Name);
        Assert.True(single.Found, "Microsoft.PowerShell.Management should be available in any PowerShell install.");
        Assert.False(string.IsNullOrWhiteSpace(single.Version));
        Assert.False(string.IsNullOrWhiteSpace(single.Path));
        Assert.True(Directory.Exists(single.Path), $"ModuleBase '{single.Path}' should be an existing directory.");
    }

    [Fact]
    public void ProbeModules_PreservesInputOrder()
    {
        var missingName = "PoshMcp_Missing_" + Guid.NewGuid().ToString("N");
        var input = new[]
        {
            missingName,
            "Microsoft.PowerShell.Management",
            "Microsoft.PowerShell.Utility",
        };

        var results = ModuleDiscovery.ProbeModules(_runspace, input);

        Assert.Equal(input.Length, results.Count);
        Assert.Equal(input[0], results[0].Name);
        Assert.Equal(input[1], results[1].Name);
        Assert.Equal(input[2], results[2].Name);
    }

    [Fact]
    public void ProbeModules_TrimsWhitespaceAroundNames()
    {
        var results = ModuleDiscovery.ProbeModules(
            _runspace,
            new[] { "  Microsoft.PowerShell.Management  " });

        var single = Assert.Single(results);
        Assert.Equal("Microsoft.PowerShell.Management", single.Name);
        Assert.True(single.Found);
    }

    [Fact]
    public void ProbeModules_SkipsBlankEntriesButProbesValidOnes()
    {
        var input = new[] { string.Empty, "Microsoft.PowerShell.Management", "   " };

        var results = ModuleDiscovery.ProbeModules(_runspace, input);

        var single = Assert.Single(results);
        Assert.Equal("Microsoft.PowerShell.Management", single.Name);
        Assert.True(single.Found);
    }

    [Fact]
    public void ProbeModules_NullRunspace_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ModuleDiscovery.ProbeModules(runspace: null!, new[] { "Microsoft.PowerShell.Management" }));
    }

    [Fact]
    public void ProbeModules_DuplicateNames_ProducesOneResultPerInputEntry()
    {
        // FR-263-10 says one Get-Module call per *configured* module name.
        // If the user (or a config bug) supplies the same name twice, we
        // honor the input shape — one result per input entry, in order.
        var input = new[] { "Microsoft.PowerShell.Management", "Microsoft.PowerShell.Management" };

        var results = ModuleDiscovery.ProbeModules(_runspace, input);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("Microsoft.PowerShell.Management", r.Name));
        Assert.All(results, r => Assert.True(r.Found));
    }

    public void Dispose()
    {
        _runspace.Dispose();
    }
}
