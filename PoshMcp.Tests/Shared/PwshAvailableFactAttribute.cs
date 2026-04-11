using System;
using System.IO;
using PoshMcp.Server.PowerShell.OutOfProcess;
using Xunit;

namespace PoshMcp.Tests;

/// <summary>
/// A custom xUnit [Fact] attribute that skips the test when pwsh is not available on PATH.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class PwshAvailableFactAttribute : FactAttribute
{
    private static readonly Lazy<string?> PwshSkipReason = new(DetectPwshSkipReason);

    public PwshAvailableFactAttribute()
    {
        if (PwshSkipReason.Value is not null)
        {
            Skip = PwshSkipReason.Value;
        }
    }

    private static string? DetectPwshSkipReason()
    {
        try
        {
            var path = OutOfProcessCommandExecutor.ResolvePwshPath();
            if (string.IsNullOrEmpty(path))
            {
                return "pwsh is not available on PATH";
            }
            return null;
        }
        catch (FileNotFoundException)
        {
            return "pwsh is not available on PATH";
        }
        catch (Exception ex)
        {
            return $"pwsh availability check failed: {ex.Message}";
        }
    }
}
