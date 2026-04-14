using System;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Threading;
using PoshMcp.Server.PowerShell;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Unit tests for PowerShellParameterUtils.IsUnserializableType — verifies that the method
/// correctly identifies types that cannot be meaningfully represented in a JSON schema.
/// </summary>
public class UnserializableTypeTests
{
    private readonly ITestOutputHelper _output;

    public UnserializableTypeTests(ITestOutputHelper output) => _output = output;

    // ── Serializable types (IsUnserializableType must return false) ──────────

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(int))]
    [InlineData(typeof(long))]
    [InlineData(typeof(double))]
    [InlineData(typeof(float))]
    [InlineData(typeof(decimal))]
    [InlineData(typeof(bool))]
    [InlineData(typeof(DateTime))]
    [InlineData(typeof(Guid))]
    [InlineData(typeof(Uri))]
    [InlineData(typeof(SwitchParameter))]
    [InlineData(typeof(CancellationToken))]
    [InlineData(typeof(string[]))]
    [InlineData(typeof(int[]))]
    public void IsUnserializableType_ReturnsFalse_ForSerializableTypes(Type type)
    {
        Assert.False(PowerShellParameterUtils.IsUnserializableType(type),
            $"{type.Name} should be considered serializable");
    }

    [Fact]
    public void IsUnserializableType_ReturnsFalse_ForNullableInt()
    {
        Assert.False(PowerShellParameterUtils.IsUnserializableType(typeof(int?)));
    }

    [Fact]
    public void IsUnserializableType_ReturnsFalse_ForEnumType()
    {
        Assert.False(PowerShellParameterUtils.IsUnserializableType(typeof(ActionPreference)));
    }

    // ── Unserializable types (IsUnserializableType must return true) ─────────

    [Fact]
    public void IsUnserializableType_ReturnsTrue_ForPSObject()
    {
        Assert.True(PowerShellParameterUtils.IsUnserializableType(typeof(PSObject)));
    }

    [Fact]
    public void IsUnserializableType_ReturnsTrue_ForScriptBlock()
    {
        Assert.True(PowerShellParameterUtils.IsUnserializableType(typeof(ScriptBlock)));
    }

    [Fact]
    public void IsUnserializableType_ReturnsTrue_ForSystemObject()
    {
        Assert.True(PowerShellParameterUtils.IsUnserializableType(typeof(object)));
    }

    [Fact]
    public void IsUnserializableType_ReturnsTrue_ForIntPtr()
    {
        Assert.True(PowerShellParameterUtils.IsUnserializableType(typeof(IntPtr)));
    }

    [Fact]
    public void IsUnserializableType_ReturnsTrue_ForUIntPtr()
    {
        Assert.True(PowerShellParameterUtils.IsUnserializableType(typeof(UIntPtr)));
    }

    [Fact]
    public void IsUnserializableType_ReturnsTrue_ForStream()
    {
        Assert.True(PowerShellParameterUtils.IsUnserializableType(typeof(Stream)));
    }

    [Fact]
    public void IsUnserializableType_ReturnsTrue_ForFileStream()
    {
        Assert.True(PowerShellParameterUtils.IsUnserializableType(typeof(FileStream)));
    }

    [Fact]
    public void IsUnserializableType_ReturnsTrue_ForWaitHandle()
    {
        Assert.True(PowerShellParameterUtils.IsUnserializableType(typeof(WaitHandle)));
    }

    [Fact]
    public void IsUnserializableType_ReturnsTrue_ForDelegate()
    {
        Assert.True(PowerShellParameterUtils.IsUnserializableType(typeof(Delegate)));
    }

    [Fact]
    public void IsUnserializableType_ReturnsTrue_ForAction()
    {
        Assert.True(PowerShellParameterUtils.IsUnserializableType(typeof(Action)));
    }

    [Fact]
    public void IsUnserializableType_ReturnsTrue_ForFuncOfString()
    {
        Assert.True(PowerShellParameterUtils.IsUnserializableType(typeof(Func<string>)));
    }

    [Fact]
    public void IsUnserializableType_ReturnsTrue_ForSystemReflectionAssembly()
    {
        Assert.True(PowerShellParameterUtils.IsUnserializableType(typeof(System.Reflection.Assembly)));
    }

    [Fact]
    public void IsUnserializableType_ReturnsTrue_ForPowerShellInstance()
    {
        Assert.True(PowerShellParameterUtils.IsUnserializableType(typeof(System.Management.Automation.PowerShell)));
    }

    [Fact]
    public void IsUnserializableType_ReturnsTrue_ForRunspace()
    {
        Assert.True(PowerShellParameterUtils.IsUnserializableType(typeof(Runspace)));
    }

    [Fact]
    public void IsUnserializableType_ReturnsTrue_ForRunspacePool()
    {
        Assert.True(PowerShellParameterUtils.IsUnserializableType(typeof(RunspacePool)));
    }

    [Fact]
    public void IsUnserializableType_ReturnsTrue_ForArrayOfPSObject()
    {
        Assert.True(PowerShellParameterUtils.IsUnserializableType(typeof(PSObject[])));
    }

    [Fact]
    public void IsUnserializableType_ReturnsTrue_ForArrayOfScriptBlock()
    {
        Assert.True(PowerShellParameterUtils.IsUnserializableType(typeof(ScriptBlock[])));
    }
}
