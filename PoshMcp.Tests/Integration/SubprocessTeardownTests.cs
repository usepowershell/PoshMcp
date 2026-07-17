using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Integration;

/// <summary>
/// Smoke test for the centralized subprocess teardown contract introduced by
/// spec 009 FR-412 / GitHub #218. Spawns a real <c>pwsh</c> child, tears it
/// down via <see cref="SubprocessTeardown"/>, and asserts that no new
/// <c>pwsh</c> processes remain attributable to this test runner.
///
/// Lives in the <c>Integration</c> category because it spawns <c>pwsh</c>
/// (FR-401 forbids that under <c>Unit</c>).
/// </summary>
[Trait("Category", "Integration")]
public class SubprocessTeardownTests : PowerShellTestBase
{
    public SubprocessTeardownTests(ITestOutputHelper output) : base(output)
    {
    }

    [PwshAvailableFact]
    public async Task TeardownAsync_AfterShortLivedPwsh_LeavesNoOrphans()
    {
        var process = StartShortLivedPwsh();
        var processId = process.Id;

        try
        {
            // Let the child finish naturally on its own short script.
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(process.HasExited, "Pwsh child did not exit within 15s.");
        }
        finally
        {
            await SubprocessTeardown.TeardownAsync(process, Logger);
        }

        Assert.False(
            IsProcessRunning(processId),
            $"Expected pwsh process {processId} to be gone after teardown.");
    }

    [PwshAvailableFact]
    public async Task TeardownAsync_AfterHungPwsh_KillsTreeAndLeavesNoOrphans()
    {
        var process = StartHungPwsh();
        var processId = process.Id;

        try
        {
            // Confirm the child is still alive — teardown must kill it.
            await Task.Delay(250);
            Assert.False(process.HasExited, "Hung pwsh exited prematurely; test setup is wrong.");
        }
        finally
        {
            await SubprocessTeardown.TeardownAsync(process, Logger);
        }

        Assert.False(
            IsProcessRunning(processId),
            $"Expected pwsh process {processId} to be gone after teardown.");
    }

    private static Process StartShortLivedPwsh()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "pwsh",
            Arguments = "-NoProfile -NonInteractive -Command \"exit 0\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        var process = new Process { StartInfo = psi };
        process.Start();
        TestProcessRegistry.Register(process);
        return process;
    }

    private static Process StartHungPwsh()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "pwsh",
            Arguments = "-NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 120\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        var process = new Process { StartInfo = psi };
        process.Start();
        TestProcessRegistry.Register(process);
        return process;
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
