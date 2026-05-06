using System;
using System.IO;
using PoshMcp.Server.PowerShell.OutOfProcess;
using Xunit;

namespace PoshMcp.Tests;

/// <summary>
/// A custom xUnit [Theory] attribute that skips the test when pwsh is not available on PATH.
/// Mirrors <see cref="PwshAvailableFactAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class PwshAvailableTheoryAttribute : TheoryAttribute
{
    private static readonly Lazy<string?> PwshSkipReason = new(DetectPwshSkipReason);

    public PwshAvailableTheoryAttribute()
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
