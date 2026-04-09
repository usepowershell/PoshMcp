using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PoshMcp.Server.PowerShell;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Tests for RuntimeCachingState — the thread-safe runtime override container
/// for result caching, set via the set-result-caching MCP tool.
/// Spec reference: specs/large-result-performance.md section 3.6
/// </summary>
public class RuntimeCachingStateTests : PowerShellTestBase
{
    public RuntimeCachingStateTests(ITestOutputHelper output) : base(output) { }

    // --- Global toggle on/off ---

    [Fact]
    public void SetGlobalOverride_True_ResolvesAsTrue()
    {
        var state = new RuntimeCachingState();
        state.SetGlobalOverride(true);

        var result = state.Resolve("Get-Process");
        Assert.True(result);
    }

    [Fact]
    public void SetGlobalOverride_False_ResolvesAsFalse()
    {
        var state = new RuntimeCachingState();
        state.SetGlobalOverride(false);

        var result = state.Resolve("Get-Process");
        Assert.False(result);
    }

    [Fact]
    public void NoOverrideSet_ResolvesAsNull()
    {
        // No override → returns null (fall through to config)
        var state = new RuntimeCachingState();

        var result = state.Resolve("Get-Process");
        Assert.Null(result);
    }

    // --- Per-function toggle on/off ---

    [Fact]
    public void SetFunctionOverride_True_ResolvesAsTrue()
    {
        var state = new RuntimeCachingState();
        state.SetFunctionOverride("Get-Process", true);

        var result = state.Resolve("Get-Process");
        Assert.True(result);
    }

    [Fact]
    public void SetFunctionOverride_False_ResolvesAsFalse()
    {
        var state = new RuntimeCachingState();
        state.SetFunctionOverride("Get-Process", false);

        var result = state.Resolve("Get-Process");
        Assert.False(result);
    }

    // --- Resolution priority: runtime per-function > runtime global ---

    [Fact]
    public void PerFunctionOverride_TakesPriorityOverGlobalOverride()
    {
        // Spec section 3.6.2: per-function runtime > global runtime
        var state = new RuntimeCachingState();
        state.SetGlobalOverride(false);
        state.SetFunctionOverride("Get-Process", true);

        // Get-Process: per-function true wins over global false
        Assert.True(state.Resolve("Get-Process"));

        // Get-Service: no per-function, falls to global false
        Assert.False(state.Resolve("Get-Service"));
    }

    [Fact]
    public void GlobalOverride_AppliesWhenNoPerFunctionOverride()
    {
        var state = new RuntimeCachingState();
        state.SetGlobalOverride(true);

        // No per-function override for Get-Service → falls to global true
        Assert.True(state.Resolve("Get-Service"));
    }

    // --- Reset semantics: null removes override, falls back to config ---

    [Fact]
    public void SetGlobalOverride_Null_ClearsOverride()
    {
        // Spec: "Pass null to remove the override and fall back to config."
        var state = new RuntimeCachingState();
        state.SetGlobalOverride(true);
        Assert.True(state.Resolve("Get-Process"));

        state.SetGlobalOverride(null);
        Assert.Null(state.Resolve("Get-Process")); // falls through to config
    }

    [Fact]
    public void SetFunctionOverride_Null_ClearsOverride()
    {
        // Spec: null removes per-function override
        var state = new RuntimeCachingState();
        state.SetFunctionOverride("Get-Process", true);
        Assert.True(state.Resolve("Get-Process"));

        state.SetFunctionOverride("Get-Process", null);
        Assert.Null(state.Resolve("Get-Process")); // falls through to global/config
    }

    [Fact]
    public void SetFunctionOverride_Null_FallsThroughToGlobalOverride()
    {
        var state = new RuntimeCachingState();
        state.SetGlobalOverride(false);
        state.SetFunctionOverride("Get-Process", true);

        // Per-function override active
        Assert.True(state.Resolve("Get-Process"));

        // Clear per-function override
        state.SetFunctionOverride("Get-Process", null);

        // Now falls through to global override (false)
        Assert.False(state.Resolve("Get-Process"));
    }

    // --- Per-function scope independence ---

    [Fact]
    public void TogglingOneFunction_DoesNotAffectOthers()
    {
        var state = new RuntimeCachingState();
        state.SetFunctionOverride("Get-Process", true);
        state.SetFunctionOverride("Get-Service", false);

        Assert.True(state.Resolve("Get-Process"));
        Assert.False(state.Resolve("Get-Service"));
        Assert.Null(state.Resolve("Get-ChildItem")); // untouched
    }

    [Fact]
    public void ClearingOneFunction_DoesNotAffectOthers()
    {
        var state = new RuntimeCachingState();
        state.SetFunctionOverride("Get-Process", true);
        state.SetFunctionOverride("Get-Service", false);

        state.SetFunctionOverride("Get-Process", null); // clear Get-Process

        Assert.Null(state.Resolve("Get-Process"));    // cleared
        Assert.False(state.Resolve("Get-Service"));    // unaffected
    }

    // --- Case insensitivity ---

    [Fact]
    public void FunctionOverride_IsCaseInsensitive()
    {
        // Spec: ConcurrentDictionary with StringComparer.OrdinalIgnoreCase
        var state = new RuntimeCachingState();
        state.SetFunctionOverride("Get-Process", true);

        Assert.True(state.Resolve("get-process"));
        Assert.True(state.Resolve("GET-PROCESS"));
        Assert.True(state.Resolve("Get-Process"));
    }

    // --- Full 5-layer resolution order ---

    [Fact]
    public void FullResolution_RuntimePerFunction_IsHighestPriority()
    {
        // Simulates full 5-layer resolution from spec section 5.2
        var runtimeState = new RuntimeCachingState();
        var config = new PowerShellConfiguration
        {
            Performance = new PerformanceConfiguration { EnableResultCaching = false },
            FunctionOverrides = new Dictionary<string, FunctionOverride>
            {
                ["Get-Process"] = new FunctionOverride { EnableResultCaching = false }
            }
        };

        // Set runtime per-function override to true
        runtimeState.SetFunctionOverride("Get-Process", true);

        // Full resolution: runtime per-function (true) beats everything
        config.FunctionOverrides.TryGetValue("Get-Process", out var funcOverride);
        var resolved = runtimeState.Resolve("Get-Process")
                       ?? funcOverride?.EnableResultCaching
                       ?? config.Performance.EnableResultCaching;

        Assert.True(resolved);
    }

    [Fact]
    public void FullResolution_RuntimeGlobal_BeatsStaticConfig()
    {
        var runtimeState = new RuntimeCachingState();
        var config = new PowerShellConfiguration
        {
            Performance = new PerformanceConfiguration { EnableResultCaching = false },
            FunctionOverrides = new Dictionary<string, FunctionOverride>
            {
                ["Get-Process"] = new FunctionOverride { EnableResultCaching = false }
            }
        };

        // Set runtime global to true (no per-function runtime override)
        runtimeState.SetGlobalOverride(true);

        config.FunctionOverrides.TryGetValue("Get-Process", out var funcOverride);
        var resolved = runtimeState.Resolve("Get-Process")
                       ?? funcOverride?.EnableResultCaching
                       ?? config.Performance.EnableResultCaching;

        Assert.True(resolved); // runtime global (true) beats static per-function and static global
    }

    [Fact]
    public void FullResolution_NoRuntimeOverride_FallsToStaticPerFunction()
    {
        var runtimeState = new RuntimeCachingState();
        var config = new PowerShellConfiguration
        {
            Performance = new PerformanceConfiguration { EnableResultCaching = false },
            FunctionOverrides = new Dictionary<string, FunctionOverride>
            {
                ["Get-Process"] = new FunctionOverride { EnableResultCaching = true }
            }
        };

        // No runtime overrides set
        config.FunctionOverrides.TryGetValue("Get-Process", out var funcOverride);
        var resolved = runtimeState.Resolve("Get-Process")
                       ?? funcOverride?.EnableResultCaching
                       ?? config.Performance.EnableResultCaching;

        Assert.True(resolved); // static per-function (true) beats static global (false)
    }

    [Fact]
    public void FullResolution_NoOverridesAtAll_FallsToDefault()
    {
        var runtimeState = new RuntimeCachingState();
        var config = new PowerShellConfiguration(); // all defaults

        config.FunctionOverrides.TryGetValue("Get-Process", out var funcOverride);
        var resolved = runtimeState.Resolve("Get-Process")
                       ?? funcOverride?.EnableResultCaching
                       ?? config.Performance.EnableResultCaching;

        Assert.False(resolved); // default is false
    }

    // --- Thread safety: concurrent reads/writes ---

    [Fact]
    public async Task ConcurrentGlobalOverrideWrites_DoNotCorruptState()
    {
        var state = new RuntimeCachingState();
        var iterations = 1000;
        var tasks = new List<Task>();

        // Concurrent writes to global override
        for (int i = 0; i < iterations; i++)
        {
            var value = i % 2 == 0;
            tasks.Add(Task.Run(() => state.SetGlobalOverride(value)));
        }

        await Task.WhenAll(tasks);

        // State should be a valid bool? (true or false), not corrupted
        var result = state.Resolve("AnyFunction");
        Assert.NotNull(result); // last write wins, but it's a valid value
        Assert.True(result == true || result == false);
    }

    [Fact]
    public async Task ConcurrentPerFunctionWrites_DoNotCorruptState()
    {
        var state = new RuntimeCachingState();
        var iterations = 1000;
        var tasks = new List<Task>();
        var functions = new[] { "Get-Process", "Get-Service", "Get-ChildItem" };

        // Concurrent writes to different functions
        for (int i = 0; i < iterations; i++)
        {
            var funcName = functions[i % functions.Length];
            var value = i % 2 == 0;
            tasks.Add(Task.Run(() => state.SetFunctionOverride(funcName, value)));
        }

        await Task.WhenAll(tasks);

        // Each function should have a valid value
        foreach (var func in functions)
        {
            var result = state.Resolve(func);
            Assert.NotNull(result);
            Assert.True(result == true || result == false);
        }
    }

    [Fact]
    public async Task ConcurrentReadsAndWrites_DoNotThrow()
    {
        var state = new RuntimeCachingState();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var exceptions = new ConcurrentBag<Exception>();

        var writerTask = Task.Run(async () =>
        {
            int i = 0;
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    state.SetGlobalOverride(i % 2 == 0);
                    state.SetFunctionOverride("Get-Process", i % 3 == 0);
                    i++;
                    await Task.Yield();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        });

        var readerTask = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    _ = state.Resolve("Get-Process");
                    _ = state.Resolve("Get-Service");
                    await Task.Yield();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        });

        await Task.WhenAll(writerTask, readerTask);

        Assert.Empty(exceptions);
    }
}
