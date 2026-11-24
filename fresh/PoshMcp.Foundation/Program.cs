using System.Text.Json;

namespace PoshMcp.Foundation;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            Contract.VerifyPackageHost();
            if (args is ["--foundation-info"])
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    phase = "P0",
                    rid = Contract.NativeRid,
                    embeddedPowerShell = false,
                    workerProtocol = Contract.WorkerVersion,
                    targetMcpRevision = Contract.TargetMcpRevision,
                    mcpImplemented = false,
                    powerShellPrerequisite = Contract.PowerShellVersion
                }, Contract.Json));
                return 0;
            }
            if (args is ["--crash-supervisor", var executable, var kind])
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
                await using var probe = await WorkerProbe.StartAsync(WorkerProbe.Profile(executable,
                    Enum.Parse<RuntimeKind>(kind), "minimal"), TimeSpan.FromSeconds(30), deadline.Token);
                var descendantPid = await probe.InvokeForSupervisorCrashQualificationAsync(deadline.Token);
                Console.WriteLine(JsonSerializer.Serialize(new { workerPid = probe.Ready.Pid, descendantPid }, Contract.Json));
                await Console.Out.FlushAsync();
                await Task.Delay(Timeout.Infinite, deadline.Token);
                return 1;
            }
            if (args is ["--measure", var runtime, var runtimeKind, var output])
            {
                await Measurements.RunAsync(runtime, Enum.Parse<RuntimeKind>(runtimeKind), output);
                return 0;
            }
            if (args is ["--probe", var probeRuntime, var probeKind])
            {
                await using var probe = await WorkerProbe.StartAsync(WorkerProbe.Profile(probeRuntime,
                    Enum.Parse<RuntimeKind>(probeKind), "minimal"), TimeSpan.FromSeconds(30));
                var (result, measurement) = await probe.InvokeAsync(
                    new("Invoke-Probe", System.Collections.Immutable.ImmutableDictionary<string, JsonElement>.Empty),
                    TimeSpan.FromSeconds(10));
                Console.WriteLine(JsonSerializer.Serialize(new { result, measurement }, Contract.Json));
                return result.Success ? 0 : 1;
            }
            Console.Error.WriteLine("P0 foundation only. Use --foundation-info. Production MCP adapters are deferred.");
            return 2;
        }
        catch (Exception ex) when (ex is ArgumentException or PlatformNotSupportedException or IOException or InvalidDataException or OperationCanceledException or InvalidOperationException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        finally
        {
            WorkerAssets.Cleanup();
        }
    }
}
