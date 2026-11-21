using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace PoshMcp.Foundation;

public static class Measurements
{
    public static async Task RunAsync(string executable, RuntimeKind kind, string output)
    {
        var samples = new List<object>();
        const int repetitions = 10;
        foreach (var fixture in new[] { "minimal", "heavy", "slow" })
        {
            var profile = WorkerProbe.Profile(executable, kind, fixture);
            var measurements = new List<ProbeMeasurement>();
            for (var iteration = 0; iteration < repetitions; iteration++)
            {
                await using var probe = await WorkerProbe.StartAsync(profile, TimeSpan.FromSeconds(60));
                var (result, measurement) = await probe.InvokeAsync(
                    new("Invoke-Probe", ImmutableDictionary<string, JsonElement>.Empty), TimeSpan.FromSeconds(10));
                if (!result.Success) throw new InvalidDataException("Measurement invocation failed.");
                measurements.Add(measurement);
            }
            var refill = Stopwatch.StartNew();
            const int initializers = 2;
            await Task.WhenAll(Enumerable.Range(0, initializers).Select(async _ =>
            {
                await using var worker = await WorkerProbe.StartAsync(profile, TimeSpan.FromSeconds(60));
            }));
            var meanStartupSeconds = measurements.Average(m => m.InitializationMs) / 1000;
            samples.Add(new
            {
                fixture,
                measurements,
                repetitions,
                initializers,
                measuredRefillPerSecond = initializers / refill.Elapsed.TotalSeconds,
                theoreticalRefillUpperBoundPerSecond = initializers / meanStartupSeconds,
                warmDispatchP95Ms = Percentile95(measurements.Select(m => m.DispatchMs)),
                queueWaitMs = (double?)null,
                queueMeasurementStatus = "P2 admission not implemented",
                budgetsApproved = false
            });
        }
        var failing = WorkerProbe.Profile(executable, kind, "failing");
        var failureTimer = Stopwatch.StartNew();
        try
        {
            await using var unexpected = await WorkerProbe.StartAsync(failing, TimeSpan.FromSeconds(10));
            throw new InvalidDataException("Failing startup incorrectly became Ready.");
        }
        catch (EndOfStreamException)
        {
            samples.Add(new { fixture = "failing", failureMs = failureTimer.Elapsed.TotalMilliseconds, ready = false });
        }
        using var supervisor = Process.GetCurrentProcess();
        var report = new
        {
            schema = 1,
            recordedAtUtc = DateTimeOffset.UtcNow,
            rid = Contract.NativeRid,
            os = RuntimeInformation.OSDescription,
            cpuIdentifier = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ??
                (OperatingSystem.IsLinux() ? File.ReadLines("/proc/cpuinfo").FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal)) : null),
            logicalCpus = Environment.ProcessorCount,
            supervisorPeakBytes = supervisor.PeakWorkingSet64,
            supervisorCpuMs = supervisor.TotalProcessorTime.TotalMilliseconds,
            dotnet = RuntimeInformation.FrameworkDescription,
            runtimeKind = kind.ToString(),
            samples,
            releaseQualification = false,
            sampleLimitations = "Ten-sample feasibility, not a release percentile or sustained-load qualification."
        };
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, new JsonSerializerOptions(Contract.Json) { WriteIndented = true }));
    }

    public static double Percentile95(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0) throw new ArgumentException("Percentiles require samples.");
        return sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1];
    }
}
