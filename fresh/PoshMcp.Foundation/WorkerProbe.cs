using System.Diagnostics;
using System.Collections.Immutable;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace PoshMcp.Foundation;

public sealed record ProbeMeasurement(string Rid, WorkerReady Runtime, double InitializationMs,
    double DispatchMs, double ExecutionMs, double RetirementMs, double WorkerCpuMs,
    long WorkerPeakBytes, int SupervisorHandlesBefore, int SupervisorHandlesAfter,
    long StdoutChars, long StderrChars);

// P0 feasibility only: no manager, refill loop, admission queue or production dispatcher.
public sealed class WorkerProbe : IAsyncDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private readonly Process _process;
    private readonly Task<long> _stdout;
    private readonly Task<long> _stderr;
    private readonly SingleUseLease _lease = new();
    private readonly int _handlesBefore;
    private readonly double _initializationMs;
    public WorkerReady Ready { get; }

    private WorkerProbe(NamedPipeServerStream pipe, Process process, Task<long> stdout, Task<long> stderr,
        WorkerReady ready, double initializationMs, int handlesBefore)
    {
        _pipe = pipe;
        _process = process;
        _stdout = stdout;
        _stderr = stderr;
        Ready = ready;
        _initializationMs = initializationMs;
        _handlesBefore = handlesBefore;
    }

    public static RuntimeProfile Profile(string executable, RuntimeKind kind, string fixture)
    {
        var baseline = StartupFile.FromPath(WorkerAssets.Script("baseline.ps1"));
        var startup = StartupFile.FromPath(WorkerAssets.Script(fixture + ".ps1"));
        return new("probe-" + fixture, kind, Path.GetFullPath(executable),
            kind == RuntimeKind.PowerShell7 ? Contract.PowerShellVersion : "5.1",
            RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(), AppContext.BaseDirectory,
            [baseline, startup], fixture == "heavy" ? [new("PSScriptAnalyzer", "1.24.0")] : [],
            ["Invoke-Probe", "Invoke-ProbeError", "Invoke-ProbeHang", "Invoke-ProbeCrash", "Invoke-ProbeDescendant"],
            Environment.GetEnvironmentVariable("POSHMCP_TEST_MODULE_PATH") is { } modulePath
                ? ImmutableDictionary<string, string>.Empty.Add("PSModulePath", modulePath)
                : ImmutableDictionary<string, string>.Empty);
    }

    public static async Task<WorkerProbe> StartAsync(RuntimeProfile profile, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        profile.Validate();
        var handlesBefore = HandleCount();
        var stopwatch = Stopwatch.StartNew();
        var pipeName = "poshmcp-" + Guid.NewGuid().ToString("N");
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var launcher = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "poshmcp-launcher.exe" : "poshmcp-launcher");
        if (!File.Exists(launcher))
        {
            pipe.Dispose();
            throw new FileNotFoundException("The native containment launcher must be built on this host.", launcher);
        }
        var start = new ProcessStartInfo(launcher)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = profile.WorkingDirectory
        };
        if (OperatingSystem.IsWindows())
            start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.ArgumentList.Add(profile.Executable);
        foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", WorkerAssets.Script("worker.ps1") })
            start.ArgumentList.Add(arg);
        var process = new Process { StartInfo = start };
        start.Environment.Clear();
        foreach (var key in new[] { "PATH", "HOME", "USERPROFILE", "SystemRoot", "WINDIR", "TEMP", "TMP", "TMPDIR", "LANG", "LC_ALL", "DOTNET_ROOT" })
            if (Environment.GetEnvironmentVariable(key) is { } value) start.Environment[key] = value;
        foreach (var pair in profile.EnvironmentVariables) start.Environment[pair.Key] = pair.Value;
        Task<long>? stdout = null;
        Task<long>? stderr = null;
        var started = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            process.Start();
            started = true;
            stdout = DrainAsync(process.StandardOutput);
            stderr = DrainAsync(process.StandardError);
            // Only bootstrap identity travels over stdin. Invocation/credentials never use argv or files.
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { pipe = pipeName, nonce }, Contract.Json).AsMemory(), deadline.Token);
            process.StandardInput.Close();
            await pipe.WaitForConnectionAsync(deadline.Token);
            var hello = await Framing.ReadAsync(pipe, "hello", nonce, deadline.Token);
            if (hello.Payload.GetProperty("nonce").GetString() != nonce)
                throw new InvalidDataException("Worker authentication failed.");
            var id = Guid.NewGuid().ToString("N");
            await Framing.WriteAsync(pipe, Framing.Message("initialize", id, new { profile, generation = profile.Generation }), deadline.Token);
            var message = await Framing.ReadAsync(pipe, "ready", id, deadline.Token);
            var ready = message.Payload.Deserialize<WorkerReady>(Contract.Json)
                ?? throw new InvalidDataException("Missing Ready identity.");
            var edition = profile.Kind == RuntimeKind.PowerShell7 ? "Core" : "Desktop";
            var correctVersion = profile.Kind == RuntimeKind.PowerShell7
                ? ready.Version == profile.Version : ready.Version.StartsWith("5.1.", StringComparison.Ordinal);
            if (!correctVersion || ready.Edition != edition || ready.Architecture != profile.Architecture ||
                ready.Generation != profile.Generation || ready.Pid <= 0 || process.HasExited)
                throw new InvalidDataException("Ready runtime or generation does not match the trusted baseline.");
            return new(pipe, process, stdout, stderr, ready, stopwatch.Elapsed.TotalMilliseconds, handlesBefore);
        }
        catch
        {
            pipe.Dispose();
            if (started) await RetireAsync(process);
            if (stdout != null) await stdout;
            if (stderr != null) await stderr;
            if (started)
            {
                process.StandardOutput.Dispose();
                process.StandardError.Dispose();
            }
            process.Dispose();
            throw;
        }
    }

    public async Task<int> InvokeForSupervisorCrashQualificationAsync(CancellationToken cancellationToken)
    {
        _lease.Consume();
        var id = Guid.NewGuid().ToString("N");
        await Framing.WriteAsync(_pipe, Framing.Message("invoke", id,
            new Invocation("Invoke-ProbeDescendant", ImmutableDictionary<string, JsonElement>.Empty)), cancellationToken);
        await Framing.ReadAsync(_pipe, "executing", id, cancellationToken);
        var message = await Framing.ReadAsync(_pipe, "result", id, cancellationToken);
        var result = message.Payload.Deserialize<WorkerResult>(Contract.Json)
            ?? throw new InvalidDataException("Missing descendant qualification result.");
        if (!result.Success) throw new InvalidDataException("Descendant qualification invocation failed.");
        return result.Output[0].GetInt32();
    }

    public async Task<(WorkerResult Result, ProbeMeasurement Measurement)> InvokeAsync(Invocation invocation,
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        _lease.Consume();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var id = Guid.NewGuid().ToString("N");
        var dispatch = Stopwatch.StartNew();
        WorkerResult result;
        double dispatchMs;
        double cpuMs;
        long peakBytes;
        using (var worker = Process.GetProcessById(Ready.Pid))
        {
            try
            {
                await Framing.WriteAsync(_pipe, Framing.Message("invoke", id, invocation), deadline.Token);
                await Framing.ReadAsync(_pipe, "executing", id, deadline.Token);
                dispatchMs = dispatch.Elapsed.TotalMilliseconds;
                var message = await Framing.ReadAsync(_pipe, "result", id, deadline.Token);
                result = message.Payload.Deserialize<WorkerResult>(Contract.Json)
                    ?? throw new InvalidDataException("Missing structured worker result.");
                // Worker waits for retire so resource sampling cannot race process exit.
                worker.Refresh();
                cpuMs = worker.TotalProcessorTime.TotalMilliseconds;
                peakBytes = worker.PeakWorkingSet64;
            }
            catch
            {
                await DisposeAsync();
                throw;
            }
        }
        var retirement = Stopwatch.StartNew();
        await Framing.WriteAsync(_pipe, Framing.Message("retire", id, new { }), deadline.Token);
        await _process.WaitForExitAsync(deadline.Token);
        if (_process.ExitCode != 0)
            throw new IOException($"Containment launcher exited with status {_process.ExitCode}.");
        await DisposeAsync();
        var stdout = await _stdout;
        var stderr = await _stderr;
        deadline.Dispose();
        if (stdout > Contract.MaxDiagnosticChars || stderr > Contract.MaxDiagnosticChars)
            throw new InvalidDataException("Worker stdout/stderr exceeded the diagnostic budget.");
        return (result, new(Contract.NativeRid, Ready, _initializationMs, dispatchMs, result.ExecutionMs,
            retirement.Elapsed.TotalMilliseconds, cpuMs, peakBytes, _handlesBefore, HandleCount(), stdout, stderr));
    }

    private static async Task<long> DrainAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        long count = 0;
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0) count += read;
        return count;
    }

    public static int HandleCount()
    {
        if (OperatingSystem.IsLinux()) return Directory.EnumerateFiles("/proc/self/fd").Count();
        using var process = Process.GetCurrentProcess();
        return process.HandleCount;
    }

    private static async Task RetireAsync(Process process)
    {
        if (process.HasExited) return;
        if (OperatingSystem.IsLinux())
        {
            if (Kill(process.Id, 15) != 0 && !process.HasExited)
                throw new IOException("Could not signal containment launcher.");
        }
        else process.Kill();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int pid, int signal);

    public async ValueTask DisposeAsync()
    {
        _pipe.Dispose();
        if (!_processDisposed)
        {
            await RetireAsync(_process);
            await Task.WhenAll(_stdout, _stderr).WaitAsync(TimeSpan.FromSeconds(10));
            _process.StandardOutput.Dispose();
            _process.StandardError.Dispose();
            _process.Dispose();
            _processDisposed = true;
        }
    }

    private bool _processDisposed;
}
