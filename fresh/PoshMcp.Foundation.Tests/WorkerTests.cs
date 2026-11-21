using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;

namespace PoshMcp.Foundation.Tests;

[Trait("Category", "Worker")]
public sealed class WorkerTests
{
    internal static RuntimeProfile Profile(string fixture = "minimal") => WorkerProbe.Profile(
        Environment.GetEnvironmentVariable("POSHMCP_TEST_RUNTIME")
            ?? throw new InvalidOperationException("POSHMCP_TEST_RUNTIME must name a provisioned absolute executable; missing runtimes are failures, not skips."),
        Environment.GetEnvironmentVariable("POSHMCP_TEST_KIND") == "WindowsPowerShell51"
            ? RuntimeKind.WindowsPowerShell51 : RuntimeKind.PowerShell7, fixture);

    internal static Invocation Invoke(string command) => new(command, ImmutableDictionary<string, JsonElement>.Empty);

    [Fact]
    public async Task InitializeReadyInvokeExitKeepsStdStreamsOffResultChannel()
    {
        await using var probe = await WorkerProbe.StartAsync(Profile(), TimeSpan.FromSeconds(30));
        var invocation = Invoke("Invoke-Probe") with
        {
            Parameters = ImmutableDictionary<string, JsonElement>.Empty
                .Add("Text", JsonSerializer.SerializeToElement("native \"quotes\"\n\u03bb"))
                .Add("Flag", JsonSerializer.SerializeToElement(true))
                .Add("Items", JsonSerializer.SerializeToElement(new[] { "a", "b" }))
        };
        var (result, measurement) = await probe.InvokeAsync(invocation, TimeSpan.FromSeconds(10));
        Assert.True(result.Success);
        Assert.Equal("native \"quotes\"\n\u03bb", result.Output[0].GetProperty("Text").GetString());
        Assert.True(result.Output[0].GetProperty("Flag").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.Output[0].GetProperty("Null").ValueKind);
        Assert.True(measurement.StdoutChars > 0);
        Assert.True(measurement.StderrChars > 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => probe.InvokeAsync(invocation, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task NonterminatingErrorPreservesPartialOutputAndIsNotSuccess()
    {
        await using var probe = await WorkerProbe.StartAsync(Profile(), TimeSpan.FromSeconds(30));
        var (result, _) = await probe.InvokeAsync(Invoke("Invoke-ProbeError"), TimeSpan.FromSeconds(10));
        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
        Assert.Equal("partial output", result.Output[0].GetString());
    }

    [Fact]
    public async Task NormalRetirementKillsWorkerCreatedDescendant()
    {
        await using var probe = await WorkerProbe.StartAsync(Profile(), TimeSpan.FromSeconds(30));
        var (result, _) = await probe.InvokeAsync(Invoke("Invoke-ProbeDescendant"), TimeSpan.FromSeconds(10));
        Assert.True(result.Success);
        await AssertGoneAsync(result.Output[0].GetInt32());
        await AssertGoneAsync(probe.Ready.Pid);
    }

    internal static async Task AssertGoneAsync(int pid)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (process.HasExited) return;
                // Linux zombies have no execution resources; the service/container init must reap them.
                if (OperatingSystem.IsLinux() && File.ReadAllText($"/proc/{pid}/stat").Split(')')[1].TrimStart().StartsWith("Z ", StringComparison.Ordinal)) return;
            }
            catch (ArgumentException) { return; }
            catch (FileNotFoundException) { return; }
            await Task.Delay(100);
        }
        Assert.Fail($"Owned process {pid} survived containment deadline.");
    }
}
