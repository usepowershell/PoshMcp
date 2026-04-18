using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Unit tests for ResolveLogFilePath — verifies CLI > env > config > null precedence.
/// </summary>
public class StdioLoggingConfigurationTests
{
    private const string LogFileEnvVar = "POSHMCP_LOG_FILE";

    // ---------------------------------------------------------------------------
    // Reflection helpers
    // ---------------------------------------------------------------------------

    private static (string? Value, string Source) InvokeResolveLogFilePath(
        string? cliValue,
        IConfiguration? config = null)
    {
        var method = typeof(Program).GetMethod(
            "ResolveLogFilePath",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(string), typeof(IConfiguration) },
            modifiers: null)
            ?? throw new MissingMethodException(
                "Program.ResolveLogFilePath(string?, IConfiguration?) not found — was the method renamed or made public?");

        var result = method.Invoke(null, new object?[] { cliValue, config })
            ?? throw new InvalidOperationException("ResolveLogFilePath returned null unexpectedly");

        var resultType = result.GetType();
        var value = (string?)resultType.GetProperty("Value")!.GetValue(result);
        var source = (string?)resultType.GetProperty("Source")!.GetValue(result) ?? string.Empty;
        return (value, source);
    }

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public void ResolveLogFilePath_CliValue_TakesPrecedence()
    {
        const string cliPath = "/logs/cli-wins.log";
        const string envPath = "/logs/env-should-lose.log";

        using var envScope = new EnvironmentVariableScope(LogFileEnvVar, envPath);

        var (value, source) = InvokeResolveLogFilePath(cliPath);

        Assert.Equal(cliPath, value);
        Assert.Equal("cli", source);
    }

    [Fact]
    public void ResolveLogFilePath_EnvVar_UsedWhenNoCliValue()
    {
        const string envPath = "/logs/env-only.log";

        using var envScope = new EnvironmentVariableScope(LogFileEnvVar, envPath);

        var (value, source) = InvokeResolveLogFilePath(null);

        Assert.Equal(envPath, value);
        Assert.Equal("env", source);
    }

    [Fact]
    public void ResolveLogFilePath_ReturnsNull_WhenNeitherSet()
    {
        using var envScope = new EnvironmentVariableScope(LogFileEnvVar, null);

        var (value, source) = InvokeResolveLogFilePath(null, null);

        Assert.Null(value);
        Assert.Equal("default", source);
    }

    [Fact]
    public void ResolveLogFilePath_AppsettingsValue_UsedAsDefault()
    {
        const string configPath = "/logs/from-appsettings.log";

        using var envScope = new EnvironmentVariableScope(LogFileEnvVar, null);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("Logging:File:Path", configPath)
            })
            .Build();

        var (value, source) = InvokeResolveLogFilePath(null, config);

        Assert.Equal(configPath, value);
        Assert.Equal("config", source);
    }

    [Fact]
    public void ResolveLogFilePath_CliValue_WinsOverAppsettings()
    {
        const string cliPath = "/logs/cli-path.log";
        const string configPath = "/logs/config-path.log";

        using var envScope = new EnvironmentVariableScope(LogFileEnvVar, null);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("Logging:File:Path", configPath)
            })
            .Build();

        var (value, source) = InvokeResolveLogFilePath(cliPath, config);

        Assert.Equal(cliPath, value);
        Assert.Equal("cli", source);
    }

    [Fact]
    public void ResolveLogFilePath_EnvVar_WinsOverAppsettings()
    {
        const string envPath = "/logs/env-path.log";
        const string configPath = "/logs/config-path.log";

        using var envScope = new EnvironmentVariableScope(LogFileEnvVar, envPath);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("Logging:File:Path", configPath)
            })
            .Build();

        var (value, source) = InvokeResolveLogFilePath(null, config);

        Assert.Equal(envPath, value);
        Assert.Equal("env", source);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveLogFilePath_WhitespaceCliValue_FallsBackToEnvVar(string whitespaceCliValue)
    {
        const string envPath = "/logs/env-fallback.log";

        using var envScope = new EnvironmentVariableScope(LogFileEnvVar, envPath);

        var (value, source) = InvokeResolveLogFilePath(whitespaceCliValue);

        Assert.Equal(envPath, value);
        Assert.Equal("env", source);
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

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
