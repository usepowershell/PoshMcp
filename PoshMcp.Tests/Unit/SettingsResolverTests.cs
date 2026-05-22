using System;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.PowerShell.OutOfProcess;
using Xunit;

namespace PoshMcp.Tests.Unit;

[Trait("Category", "Unit")]
public class SettingsResolverTests
{
    private const string LogLevelEnvVar = "POSHMCP_LOG_LEVEL";

    [Theory]
    [InlineData("Trace", LogLevel.Trace)]
    [InlineData("tRaCe", LogLevel.Trace)]
    [InlineData("Debug", LogLevel.Debug)]
    [InlineData("dEbUg", LogLevel.Debug)]
    [InlineData("Information", LogLevel.Information)]
    [InlineData("iNfOrMaTiOn", LogLevel.Information)]
    [InlineData("Warning", LogLevel.Warning)]
    [InlineData("wArNiNg", LogLevel.Warning)]
    [InlineData("Error", LogLevel.Error)]
    [InlineData("eRrOr", LogLevel.Error)]
    [InlineData("Critical", LogLevel.Critical)]
    [InlineData("cRiTiCaL", LogLevel.Critical)]
    [InlineData("None", LogLevel.None)]
    [InlineData("nOnE", LogLevel.None)]
    public void ParseLogLevel_ReturnsExpectedValue_ForValidInputs(string input, LogLevel expected)
    {
        var result = SettingsResolver.ParseLogLevel(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseLogLevel_BlankInput_ReturnsInformation(string? input)
    {
        var result = SettingsResolver.ParseLogLevel(input);

        Assert.Equal(LogLevel.Information, result);
    }

    [Theory]
    [InlineData("--trace", "Trace")]
    [InlineData("--debug", "Debug")]
    [InlineData("--verbose", "Debug")]
    public void ResolveEffectiveLogLevel_FlagOverridesArgumentAndEnvironment(string option, string expected)
    {
        using var envScope = new EnvironmentVariableScope(LogLevelEnvVar, "Error");

        var result = SettingsResolver.ResolveEffectiveLogLevel(new[] { option }, "Warning");

        Assert.Equal(expected, result.Value);
        Assert.Equal(SettingsResolver.CliSource, result.Source);
    }

    [Fact]
    public void ResolveEffectiveLogLevel_ArgumentOverridesEnvironmentVariable()
    {
        using var envScope = new EnvironmentVariableScope(LogLevelEnvVar, "Error");

        var result = SettingsResolver.ResolveEffectiveLogLevel(Array.Empty<string>(), "Warning");

        Assert.Equal(LogLevel.Warning.ToString(), result.Value);
        Assert.Equal(SettingsResolver.CliSource, result.Source);
    }

    [Fact]
    public void ResolveEffectiveLogLevel_UsesEnvironmentVariable_WhenArgumentMissing()
    {
        using var envScope = new EnvironmentVariableScope(LogLevelEnvVar, "Error");

        var result = SettingsResolver.ResolveEffectiveLogLevel(Array.Empty<string>(), null);

        Assert.Equal(LogLevel.Error.ToString(), result.Value);
        Assert.Equal(SettingsResolver.EnvSource, result.Source);
    }

    [Theory]
    [InlineData("in-process", "InProcess")]
    [InlineData("InProcess", "InProcess")]
    [InlineData("inprocess", "InProcess")]
    [InlineData("out-of-process", "OutOfProcess")]
    [InlineData("OutOfProcess", "OutOfProcess")]
    public void NormalizeRuntimeModeValue_ReturnsExpectedValue(string input, string expected)
    {
        var result = SettingsResolver.NormalizeRuntimeModeValue(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("InProcess", RuntimeMode.InProcess)]
    [InlineData("OutOfProcess", RuntimeMode.OutOfProcess)]
    [InlineData("UnsupportedValue", RuntimeMode.Unsupported)]
    public void ResolveRuntimeMode_ReturnsExpectedEnum(string input, RuntimeMode expected)
    {
        var result = SettingsResolver.ResolveRuntimeMode(input);

        Assert.Equal(expected, result);
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _originalValue;

        public EnvironmentVariableScope(string name, string? newValue)
        {
            _name = name;
            _originalValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, newValue);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _originalValue);
        }
    }
}
