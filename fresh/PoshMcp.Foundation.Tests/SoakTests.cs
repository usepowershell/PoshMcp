namespace PoshMcp.Foundation.Tests;

[Trait("Category", "Soak")]
[Collection("External workers")]
public sealed class SoakTests
{
    [Fact]
    public async Task RepeatedSingleUseCyclesHaveDistinctPidsAndRetire()
    {
        var profile = WorkerTests.Profile();
        profile.Validate();
        var pids = new HashSet<int>();
        for (var iteration = 0; iteration < 10; iteration++)
        {
            await using var probe = await WorkerProbe.StartAsync(profile, TimeSpan.FromSeconds(30));
            Assert.True(pids.Add(probe.Ready.Pid), "A process was reused in the repeated-cycle fixture.");
            var (result, _) = await probe.InvokeAsync(WorkerTests.Invoke("Invoke-Probe"), TimeSpan.FromSeconds(10));
            Assert.True(result.Success);
            await WorkerTests.AssertGoneAsync(probe.Ready.Pid);
        }
    }
}
