using System.Collections.Generic;
using PoshMcp.Server.PowerShell;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Tests for PerformanceConfiguration and FunctionOverride resolution logic.
/// Validates default values, per-function override priority, null fallthrough,
/// explicit property lists, and configuration validation.
/// Spec reference: specs/large-result-performance.md sections 3.2, 4.3, 5.1, 5.2
/// </summary>
public class PerformanceConfigurationTests : PowerShellTestBase
{
    public PerformanceConfigurationTests(ITestOutputHelper output) : base(output) { }

    // --- Default values ---

    [Fact]
    public void PerformanceConfiguration_Defaults_EnableResultCachingIsFalse()
    {
        // Spec: "Default: false (opt-in)"
        var config = new PerformanceConfiguration();
        Assert.False(config.EnableResultCaching);
    }

    [Fact]
    public void PerformanceConfiguration_Defaults_UseDefaultDisplayPropertiesIsTrue()
    {
        // Spec: "Default: true (on by default — major payload reduction)"
        var config = new PerformanceConfiguration();
        Assert.True(config.UseDefaultDisplayProperties);
    }

    [Fact]
    public void PowerShellConfiguration_Defaults_PerformanceIsNotNull()
    {
        var config = new PowerShellConfiguration();
        Assert.NotNull(config.Performance);
    }

    [Fact]
    public void PowerShellConfiguration_Defaults_FunctionOverridesIsEmpty()
    {
        var config = new PowerShellConfiguration();
        Assert.NotNull(config.FunctionOverrides);
        Assert.Empty(config.FunctionOverrides);
    }

    // --- Per-function override takes priority over global ---

    [Fact]
    public void FunctionOverride_EnableResultCaching_TakesPriorityOverGlobal()
    {
        // Spec section 3.2: Per-function EnableResultCaching overrides global
        var config = new PowerShellConfiguration
        {
            Performance = new PerformanceConfiguration { EnableResultCaching = false },
            FunctionOverrides = new Dictionary<string, FunctionOverride>
            {
                ["Get-Service"] = new FunctionOverride { EnableResultCaching = true }
            }
        };

        // Per-function override says true, global says false → per-function wins
        var functionOverride = config.FunctionOverrides["Get-Service"];
        Assert.True(functionOverride.EnableResultCaching);

        // Global remains false for other functions
        Assert.False(config.Performance.EnableResultCaching);
    }

    [Fact]
    public void FunctionOverride_UseDefaultDisplayProperties_TakesPriorityOverGlobal()
    {
        // Spec section 4.3: Per-function UseDefaultDisplayProperties overrides global
        var config = new PowerShellConfiguration
        {
            Performance = new PerformanceConfiguration { UseDefaultDisplayProperties = true },
            FunctionOverrides = new Dictionary<string, FunctionOverride>
            {
                ["Get-Service"] = new FunctionOverride { UseDefaultDisplayProperties = false }
            }
        };

        var functionOverride = config.FunctionOverrides["Get-Service"];
        Assert.False(functionOverride.UseDefaultDisplayProperties);
    }

    // --- Null override falls through to global ---

    [Fact]
    public void FunctionOverride_NullEnableResultCaching_FallsThroughToGlobal()
    {
        // Spec section 5.2: null override → global → default
        var config = new PowerShellConfiguration
        {
            Performance = new PerformanceConfiguration { EnableResultCaching = true },
            FunctionOverrides = new Dictionary<string, FunctionOverride>
            {
                ["Get-Process"] = new FunctionOverride { EnableResultCaching = null }
            }
        };

        var functionOverride = config.FunctionOverrides["Get-Process"];
        Assert.Null(functionOverride.EnableResultCaching);

        // Should fall through to global config which is true
        var resolved = functionOverride.EnableResultCaching ?? config.Performance.EnableResultCaching;
        Assert.True(resolved);
    }

    [Fact]
    public void FunctionOverride_NullUseDefaultDisplayProperties_FallsThroughToGlobal()
    {
        var config = new PowerShellConfiguration
        {
            Performance = new PerformanceConfiguration { UseDefaultDisplayProperties = false },
            FunctionOverrides = new Dictionary<string, FunctionOverride>
            {
                ["Get-Process"] = new FunctionOverride { UseDefaultDisplayProperties = null }
            }
        };

        var functionOverride = config.FunctionOverrides["Get-Process"];
        Assert.Null(functionOverride.UseDefaultDisplayProperties);

        var resolved = functionOverride.UseDefaultDisplayProperties ?? config.Performance.UseDefaultDisplayProperties;
        Assert.False(resolved);
    }

    [Fact]
    public void FunctionOverride_NoOverrideForFunction_FallsThroughToGlobal()
    {
        // No FunctionOverrides entry at all → global → default
        var config = new PowerShellConfiguration
        {
            Performance = new PerformanceConfiguration { EnableResultCaching = true }
        };

        config.FunctionOverrides.TryGetValue("Get-Process", out var functionOverride);
        Assert.Null(functionOverride);

        // Resolution falls through to global
        var resolved = functionOverride?.EnableResultCaching ?? config.Performance.EnableResultCaching;
        Assert.True(resolved);
    }

    // --- FunctionOverride with explicit DefaultProperties list ---

    [Fact]
    public void FunctionOverride_DefaultProperties_ExplicitListIsPreserved()
    {
        // Spec section 4.3: Explicit property list overrides DefaultDisplayPropertySet
        var config = new PowerShellConfiguration
        {
            FunctionOverrides = new Dictionary<string, FunctionOverride>
            {
                ["Get-Process"] = new FunctionOverride
                {
                    DefaultProperties = new List<string> { "Id", "ProcessName", "CPU", "WorkingSet64", "StartTime" }
                }
            }
        };

        var functionOverride = config.FunctionOverrides["Get-Process"];
        Assert.NotNull(functionOverride.DefaultProperties);
        Assert.Equal(5, functionOverride.DefaultProperties.Count);
        Assert.Contains("Id", functionOverride.DefaultProperties);
        Assert.Contains("ProcessName", functionOverride.DefaultProperties);
        Assert.Contains("CPU", functionOverride.DefaultProperties);
        Assert.Contains("WorkingSet64", functionOverride.DefaultProperties);
        Assert.Contains("StartTime", functionOverride.DefaultProperties);
    }

    [Fact]
    public void FunctionOverride_DefaultProperties_NullMeansUseAutoDiscovery()
    {
        var functionOverride = new FunctionOverride { DefaultProperties = null };
        Assert.Null(functionOverride.DefaultProperties);
    }

    [Fact]
    public void FunctionOverride_DefaultProperties_EmptyListMeansNoProperties()
    {
        var functionOverride = new FunctionOverride { DefaultProperties = new List<string>() };
        Assert.NotNull(functionOverride.DefaultProperties);
        Assert.Empty(functionOverride.DefaultProperties);
    }

    // --- Configuration validation ---

    [Fact]
    public void FunctionOverride_AllNullValues_IsValidDefaultState()
    {
        // A FunctionOverride with all nulls is valid — means "inherit everything from global"
        var functionOverride = new FunctionOverride();
        Assert.Null(functionOverride.EnableResultCaching);
        Assert.Null(functionOverride.UseDefaultDisplayProperties);
        Assert.Null(functionOverride.DefaultProperties);
    }

    [Fact]
    public void PowerShellConfiguration_MultipleFunctionOverrides_IndependentResolution()
    {
        // Spec: different functions can have different override combinations
        var config = new PowerShellConfiguration
        {
            Performance = new PerformanceConfiguration
            {
                EnableResultCaching = false,
                UseDefaultDisplayProperties = true
            },
            FunctionOverrides = new Dictionary<string, FunctionOverride>
            {
                ["Get-Process"] = new FunctionOverride
                {
                    DefaultProperties = new List<string> { "Id", "ProcessName", "CPU", "WorkingSet64", "StartTime" },
                    EnableResultCaching = true
                },
                ["Get-Service"] = new FunctionOverride
                {
                    UseDefaultDisplayProperties = false
                }
            }
        };

        // Get-Process: caching enabled (overrides global false), custom properties
        var processOverride = config.FunctionOverrides["Get-Process"];
        Assert.True(processOverride.EnableResultCaching);
        Assert.NotNull(processOverride.DefaultProperties);

        // Get-Service: property filtering disabled, caching inherits from global (false)
        var serviceOverride = config.FunctionOverrides["Get-Service"];
        Assert.Null(serviceOverride.EnableResultCaching); // falls through to global false
        Assert.False(serviceOverride.UseDefaultDisplayProperties);

        // Get-ChildItem: not in overrides, inherits everything from global
        Assert.False(config.FunctionOverrides.ContainsKey("Get-ChildItem"));
    }

    // --- Full resolution order (pseudocode from spec 5.2) ---

    [Fact]
    public void ResolutionLogic_FullChain_PerFunctionTakesHighestPriority()
    {
        // Simulates the resolution order from spec section 5.2
        var config = new PowerShellConfiguration
        {
            Performance = new PerformanceConfiguration
            {
                EnableResultCaching = false,
                UseDefaultDisplayProperties = false
            },
            FunctionOverrides = new Dictionary<string, FunctionOverride>
            {
                ["Get-Process"] = new FunctionOverride
                {
                    EnableResultCaching = true,
                    UseDefaultDisplayProperties = true
                }
            }
        };

        // Resolve for Get-Process
        config.FunctionOverrides.TryGetValue("Get-Process", out var gpOverride);
        var enableCaching = gpOverride?.EnableResultCaching ?? config.Performance.EnableResultCaching;
        var useDefaultProps = gpOverride?.UseDefaultDisplayProperties ?? config.Performance.UseDefaultDisplayProperties;

        Assert.True(enableCaching);     // per-function true overrides global false
        Assert.True(useDefaultProps);   // per-function true overrides global false

        // Resolve for Get-Service (no override entry)
        config.FunctionOverrides.TryGetValue("Get-Service", out var gsOverride);
        enableCaching = gsOverride?.EnableResultCaching ?? config.Performance.EnableResultCaching;
        useDefaultProps = gsOverride?.UseDefaultDisplayProperties ?? config.Performance.UseDefaultDisplayProperties;

        Assert.False(enableCaching);    // falls through to global false
        Assert.False(useDefaultProps);  // falls through to global false
    }
}
