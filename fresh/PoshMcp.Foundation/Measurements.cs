using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace PoshMcp.Foundation;

public static class Measurements
{
    public static async Task RunAsync(string executable, RuntimeKind kind, string output)
    {
        var samples = new List<object>();
        var failures = new List<string>();
        const int repetitions = 10;
        foreach (var fixture in new[] { "minimal", "heavy", "slow" })
        {
            var profile = WorkerProbe.Profile(executable, kind, fixture);
            var measurements = new List<ProbeMeasurement>();
            try
            {
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
                refill.Stop();
                const int replacementsPerInitializer = 5;
                var replacements = new ConcurrentBag<ProbeMeasurement>();
                var replacementTimer = Stopwatch.StartNew();
                await Task.WhenAll(Enumerable.Range(0, initializers).Select(async _ =>
                {
                    for (var iteration = 0; iteration < replacementsPerInitializer; iteration++)
                    {
                        await using var worker = await WorkerProbe.StartAsync(profile, TimeSpan.FromSeconds(60));
                        var (result, measurement) = await worker.InvokeAsync(
                            new("Invoke-Probe", ImmutableDictionary<string, JsonElement>.Empty), TimeSpan.FromSeconds(10));
                        if (!result.Success) throw new InvalidDataException("Replacement measurement invocation failed.");
                        replacements.Add(measurement);
                    }
                }));
                replacementTimer.Stop();
                var meanStartupSeconds = measurements.Average(m => m.InitializationMs) / 1000;
                samples.Add(new
                {
                    fixture,
                    measurements,
                    repetitions,
                    initializers,
                    measuredInitializeAndRetireRatePerSecond = initializers / refill.Elapsed.TotalSeconds,
                    replacementMeasurements = replacements.ToArray(),
                    replacementCount = replacements.Count,
                    replacementDurationSeconds = replacementTimer.Elapsed.TotalSeconds,
                    measuredInitializeInvokeRetireRatePerSecond = replacements.Count / replacementTimer.Elapsed.TotalSeconds,
                    replacementMeasurementStatus = "Two independent initializer slots, five single-use replacements each; finite closed-loop feasibility, not queued production throughput or a release soak.",
                    theoreticalRefillUpperBoundPerSecond = initializers / meanStartupSeconds,
                    readyToExecutingAckP95Ms = Percentile95(measurements.Select(m => m.DispatchMs)),
                    readyToCommandStartP95Ms = Percentile95(measurements.Select(m => m.CommandStartMs)),
                    commandStartMeasurement = "Supervisor invoke-frame send to worker PowerShell command invocation, using the same host monotonic clock; excludes P2 admission/queue wait.",
                    queueWaitMs = (double?)null,
                    queueMeasurementStatus = "P2 admission not implemented",
                    budgetsApproved = false
                });
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException)
            {
                failures.Add(fixture);
                samples.Add(new { fixture, measurements, failureType = ex.GetType().Name, completed = false });
                Console.Error.WriteLine($"Measurement fixture {fixture} failed ({ex.GetType().Name}); the report is incomplete.");
            }
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
            failures,
            completed = failures.Count == 0,
            releaseQualification = false,
            sampleLimitations = "Ten-sample feasibility, not a release percentile or sustained-load qualification."
        };
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, new JsonSerializerOptions(Contract.Json) { WriteIndented = true }));
        if (failures.Count != 0)
            throw new InvalidDataException("Some required measurement fixtures failed; inspect the preserved incomplete report.");
    }

    public static double Percentile95(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0) throw new ArgumentException("Percentiles require samples.");
        return sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1];
    }
}
