using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using PoshMcp.Server.PowerShell.OutOfProcess;

namespace PoshMcp.Tests.Shared;

/// <summary>
/// Test helper that resolves the path to <c>oop-host.ps1</c> the same way
/// <see cref="OutOfProcessCommandExecutor.ResolveHostScriptPathAsync"/> does, but
/// callable from tests without going through the full executor lifecycle.
/// </summary>
internal static class OopTestPaths
{
    /// <summary>
    /// Resolves <c>oop-host.ps1</c> for tests. Returns <c>null</c> if the script
    /// cannot be found (e.g., CI without server build artifacts copied).
    /// </summary>
    public static async Task<string?> ResolveHostScriptAsync()
    {
        var overridePath = Environment.GetEnvironmentVariable("POSHMCP_OOP_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
            return overridePath;

        var serverAssembly = typeof(OutOfProcessHost).Assembly;
        var resourceName = Array.Find(
            serverAssembly.GetManifestResourceNames(),
            name => name.EndsWith("oop-host.ps1", StringComparison.OrdinalIgnoreCase));

        if (resourceName is not null)
        {
            using var stream = serverAssembly.GetManifestResourceStream(resourceName);
            if (stream is not null)
            {
                var bytes = new byte[stream.Length];
                await stream.ReadExactlyAsync(bytes);
                var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                var dir = Path.Combine(Path.GetTempPath(), "poshmcp-tests");
                var path = Path.Combine(dir, "oop-host.ps1");
                Directory.CreateDirectory(dir);
                if (!File.Exists(path) || ContentHash(path) != hash)
                {
                    await File.WriteAllBytesAsync(path, bytes);
                }
                return path;
            }
        }

        var basePath = Path.Combine(AppContext.BaseDirectory, "PowerShell", "OutOfProcess", "oop-host.ps1");
        return File.Exists(basePath) ? basePath : null;
    }

    private static string ContentHash(string filePath)
    {
        using var fs = File.OpenRead(filePath);
        return Convert.ToHexStringLower(SHA256.HashData(fs));
    }
}
