namespace PoshMcp.Foundation;

public static class WorkerAssets
{
    private static readonly Lazy<string> DirectoryPath = new(Extract);
    private static readonly string[] Names = ["worker.ps1", "baseline.ps1", "minimal.ps1", "heavy.ps1", "slow.ps1", "failing.ps1"];

    public static string Script(string name)
    {
        if (!Names.Contains(name))
            throw new ArgumentException("Unknown trusted fixture.", nameof(name));
        return Path.Combine(DirectoryPath.Value, name);
    }

    private static string Extract()
    {
        var directory = Directory.CreateTempSubdirectory("poshmcp-p0-");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(directory.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        foreach (var name in Names)
        {
            using var source = typeof(WorkerAssets).Assembly.GetManifestResourceStream(name)
                ?? throw new InvalidDataException($"Missing trusted worker resource {name}.");
            using var target = File.Create(Path.Combine(directory.FullName, name));
            source.CopyTo(target);
        }
        return directory.FullName;
    }

    public static void Cleanup()
    {
        if (!DirectoryPath.IsValueCreated) return;
        foreach (var name in Names) File.Delete(Path.Combine(DirectoryPath.Value, name));
        Directory.Delete(DirectoryPath.Value);
    }
}
