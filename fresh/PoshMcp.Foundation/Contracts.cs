using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoshMcp.Foundation;

public enum RuntimeKind { PowerShell7, WindowsPowerShell51 }
public enum WorkerState { Starting, Initializing, Ready, Leased, Executing, Faulted, Retiring, Exited }

public static class Contract
{
    public const int WorkerVersion = 1;
    public const int MaxFrameBytes = 1024 * 1024;
    public const int MaxDiagnosticChars = 32 * 1024;
    public const string TargetMcpRevision = "2026-07-28";
    public const string PowerShellVersion = "7.6.0";
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        MaxDepth = 32
    };

    public static string NativeRid =>
        $"{(OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsLinux() ? "linux" : throw new PlatformNotSupportedException())}-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";

    public static void VerifyPackageHost()
    {
        var target = typeof(Contract).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "TargetRid").Value;
        if (target != "portable" && target != NativeRid)
            throw new PlatformNotSupportedException($"Package {target} cannot qualify host {NativeRid}.");
    }
}

public sealed record StartupFile(string Path, string Sha256)
{
    public static StartupFile FromPath(string path) =>
        new(System.IO.Path.GetFullPath(path), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
}

public sealed record PinnedModule(string Name, string Version);

public sealed record RuntimeProfile(
    string Name, RuntimeKind Kind, string Executable, string Version, string Architecture,
    string WorkingDirectory, ImmutableArray<StartupFile> StartupFiles,
    ImmutableArray<PinnedModule> Modules, ImmutableArray<string> Commands,
    ImmutableDictionary<string, string> EnvironmentVariables)
{
    public void Validate()
    {
        if (!Path.IsPathFullyQualified(Executable) || !File.Exists(Executable))
            throw new ArgumentException("An existing absolute runtime executable is required.");
        if (!Directory.Exists(WorkingDirectory) || Commands.IsDefaultOrEmpty ||
            StartupFiles.IsDefault || Modules.IsDefault || EnvironmentVariables == null || string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("Profile baseline is incomplete.");
        if (Architecture is not ("x64" or "arm64"))
            throw new PlatformNotSupportedException("Only native x64/arm64 runtimes are in scope.");
        if (Kind == RuntimeKind.WindowsPowerShell51 &&
            (!OperatingSystem.IsWindows() || Architecture != "x64" || Version != "5.1"))
            throw new PlatformNotSupportedException("Windows PowerShell 5.1 is explicit and qualified on Windows x64 only.");
        if (Kind == RuntimeKind.PowerShell7 && Version != Contract.PowerShellVersion)
            throw new ArgumentException($"The qualification input is pinned to PowerShell {Contract.PowerShellVersion}.");
        if (Architecture != RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant())
            throw new PlatformNotSupportedException("Emulated/cross-architecture runtime is not native qualification.");
    }

    [JsonIgnore]
    public string Generation => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, Contract.Json)))).ToLowerInvariant();
}

public sealed record ToolContract(string Alias, string ModuleQualifiedCommand, string ProfileGeneration, JsonElement InputSchema);
public sealed record Catalog(string Generation, ImmutableArray<ToolContract> Tools);
public sealed record WorkerEnvelope(int Version, string Kind, string Id, JsonElement Payload);
public sealed record WorkerReady(int Pid, string Edition, string Version, string Architecture, string Generation);
public sealed record Invocation(string Command, ImmutableDictionary<string, JsonElement> Parameters);
public sealed record WorkerResult(bool Success, JsonElement Output, string[] Errors, string[] Warnings,
    int? NativeExitCode, double ExecutionMs, long CommandStartTimestamp, long TimestampFrequency);

public sealed class SingleUseLease
{
    private int _consumed;
    public void Consume()
    {
        if (Interlocked.Exchange(ref _consumed, 1) != 0)
            throw new InvalidOperationException("A worker is never returned to Ready or retried.");
    }
}
