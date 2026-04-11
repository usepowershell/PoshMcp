using System;
using System.IO;
using PoshMcp.Server.PowerShell.OutOfProcess;
using Xunit;

namespace PoshMcp.Tests;

/// <summary>
/// A custom xUnit [Fact] attribute that skips the test when pwsh is not available
/// or when the vendored Az modules are not present at the expected path.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AzModulesAvailableFactAttribute : FactAttribute
{
    /// <summary>
    /// Path to the vendored Az modules used by integration tests.
    /// </summary>
    public static readonly string VendoredModulesPath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "integration", "Modules"));

    private static readonly Lazy<string?> SkipReasonLazy = new(DetectSkipReason);

    public AzModulesAvailableFactAttribute()
    {
        if (SkipReasonLazy.Value is not null)
        {
            Skip = SkipReasonLazy.Value;
        }
    }

    private static string? DetectSkipReason()
    {
        // First check pwsh availability
        try
        {
            var path = OutOfProcessCommandExecutor.ResolvePwshPath();
            if (string.IsNullOrEmpty(path))
            {
                return "pwsh is not available on PATH";
            }
        }
        catch (FileNotFoundException)
        {
            return "pwsh is not available on PATH";
        }
        catch (Exception ex)
        {
            return $"pwsh availability check failed: {ex.Message}";
        }

        // Then check vendored Az.Accounts module
        var azAccountsPath = Path.Combine(VendoredModulesPath, "Az.Accounts");
        if (!Directory.Exists(azAccountsPath))
        {
            return $"Vendored Az.Accounts module not found at {azAccountsPath}";
        }

        // Verify at least one version directory with the manifest exists
        var versionDirs = Directory.GetDirectories(azAccountsPath);
        if (versionDirs.Length == 0)
        {
            return $"No version directory found under {azAccountsPath}";
        }

        var hasManifest = false;
        foreach (var dir in versionDirs)
        {
            if (File.Exists(Path.Combine(dir, "Az.Accounts.psd1")))
            {
                hasManifest = true;
                break;
            }
        }

        if (!hasManifest)
        {
            return $"Az.Accounts.psd1 manifest not found in any version directory under {azAccountsPath}";
        }

        return null;
    }
}
