namespace PoshMcp.Foundation.Tests;

[Trait("Category", "Fault")]
[Collection("External workers")]
public sealed class FaultTests
{
    [Fact]
    public async Task WorkerSurvivesLaunchingThreadExitWhileSupervisorLives()
    {
        var profile = WorkerTests.Profile();
        Task<WorkerProbe>? starting = null;
        var thread = new Thread(() => starting = WorkerProbe.StartAsync(profile, TimeSpan.FromSeconds(30)));
        thread.Start();
        thread.Join();
        await using var probe = await starting!;
        var (result, _) = await probe.InvokeAsync(WorkerTests.Invoke("Invoke-Probe"), TimeSpan.FromSeconds(10));
        Assert.True(result.Success);
    }

    [Theory]
    [InlineData("failing", 10)]
    [InlineData("slow", 1)]
    public async Task FailedOrSlowInitializationNeverBecomesReady(string fixture, int seconds)
    {
        var profile = WorkerTests.Profile(fixture);
        profile.Validate();
        if (fixture == "failing")
            await Assert.ThrowsAsync<EndOfStreamException>(() => WorkerProbe.StartAsync(profile, TimeSpan.FromSeconds(seconds)));
        else
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WorkerProbe.StartAsync(profile, TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData("Invoke-ProbeHang")]
    [InlineData("Invoke-ProbeCrash")]
    public async Task TimeoutOrCrashRetiresWorker(string command)
    {
        await using var probe = await WorkerProbe.StartAsync(WorkerTests.Profile(), TimeSpan.FromSeconds(30));
        var pid = probe.Ready.Pid;
        await Assert.ThrowsAnyAsync<Exception>(() => probe.InvokeAsync(WorkerTests.Invoke(command), TimeSpan.FromSeconds(1)));
        await WorkerTests.AssertGoneAsync(pid);
    }

    [Fact]
    public async Task SupervisorCrashKillsWorkerAndDescendant()
    {
        var profile = WorkerTests.Profile();
        profile.Validate();
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in new[] { typeof(Program).Assembly.Location, "--crash-supervisor", profile.Executable, profile.Kind.ToString() })
            start.ArgumentList.Add(arg);
        using var supervisor = System.Diagnostics.Process.Start(start)!;
        var stderr = supervisor.StandardError.ReadToEndAsync();
        try
        {
            var line = await supervisor.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(45));
            Assert.False(string.IsNullOrWhiteSpace(line), "Supervisor did not publish private-channel descendant evidence.");
            using var evidence = System.Text.Json.JsonDocument.Parse(line!);
            var worker = evidence.RootElement.GetProperty("workerPid").GetInt32();
            var descendant = evidence.RootElement.GetProperty("descendantPid").GetInt32();
            supervisor.Kill();
            await supervisor.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await WorkerTests.AssertGoneAsync(worker);
            await WorkerTests.AssertGoneAsync(descendant);
        }
        finally
        {
            if (!supervisor.HasExited) supervisor.Kill();
            await supervisor.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await stderr;
        }
    }
}
