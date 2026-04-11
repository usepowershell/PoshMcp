using System;
using Xunit;

namespace PoshMcp.Tests.Unit.OutOfProcess;

/// <summary>
/// Unit tests for the RuntimeMode enum.
/// Validates that all expected enum values exist and parsing round-trips correctly.
/// </summary>
public class RuntimeModeTests
{
    [Fact]
    public void RuntimeMode_InProcess_Exists()
    {
        var mode = PoshMcp.Server.PowerShell.OutOfProcess.RuntimeMode.InProcess;
        Assert.Equal("InProcess", mode.ToString());
    }

    [Fact]
    public void RuntimeMode_OutOfProcess_Exists()
    {
        var mode = PoshMcp.Server.PowerShell.OutOfProcess.RuntimeMode.OutOfProcess;
        Assert.Equal("OutOfProcess", mode.ToString());
    }

    [Fact]
    public void RuntimeMode_Unsupported_Exists()
    {
        var mode = PoshMcp.Server.PowerShell.OutOfProcess.RuntimeMode.Unsupported;
        Assert.Equal("Unsupported", mode.ToString());
    }

    [Theory]
    [InlineData("InProcess")]
    [InlineData("OutOfProcess")]
    [InlineData("Unsupported")]
    public void RuntimeMode_Parse_RoundTrips(string value)
    {
        var parsed = Enum.Parse<PoshMcp.Server.PowerShell.OutOfProcess.RuntimeMode>(value);
        Assert.Equal(value, parsed.ToString());
    }

    [Theory]
    [InlineData("InProcess", true)]
    [InlineData("OutOfProcess", true)]
    [InlineData("Unsupported", true)]
    [InlineData("invalid", false)]
    [InlineData("inprocess", false)] // case-sensitive by default
    [InlineData("", false)]
    public void RuntimeMode_TryParse_ReturnsExpected(string value, bool expectedSuccess)
    {
        var success = Enum.TryParse<PoshMcp.Server.PowerShell.OutOfProcess.RuntimeMode>(value, ignoreCase: false, out var result);
        Assert.Equal(expectedSuccess, success);

        if (expectedSuccess)
        {
            Assert.Equal(value, result.ToString());
        }
    }

    [Theory]
    [InlineData("inprocess", true)]
    [InlineData("OUTOFPROCESS", true)]
    [InlineData("unsupported", true)]
    [InlineData("bogus", false)]
    public void RuntimeMode_TryParse_CaseInsensitive_ReturnsExpected(string value, bool expectedSuccess)
    {
        var success = Enum.TryParse<PoshMcp.Server.PowerShell.OutOfProcess.RuntimeMode>(value, ignoreCase: true, out _);
        Assert.Equal(expectedSuccess, success);
    }

    [Fact]
    public void RuntimeMode_HasExactlyThreeValues()
    {
        var values = Enum.GetValues<PoshMcp.Server.PowerShell.OutOfProcess.RuntimeMode>();
        Assert.Equal(3, values.Length);
    }
}
