using System;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace PoshMcp.Tests;

/// <summary>
/// Logger provider that writes to xUnit test output
/// </summary>
public class TestOutputLoggerProvider : ILoggerProvider
{
    private readonly ITestOutputHelper _output;

    public TestOutputLoggerProvider(ITestOutputHelper output)
    {
        _output = output;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new TestOutputLogger(_output, categoryName);
    }

    public void Dispose()
    {
        // Nothing to dispose
    }
}

/// <summary>
/// Logger implementation that writes to xUnit test output
/// </summary>
public class TestOutputLogger : ILogger
{
    private readonly ITestOutputHelper _output;
    private readonly string _categoryName;

    public TestOutputLogger(ITestOutputHelper output, string categoryName)
    {
        _output = output;
        _categoryName = categoryName;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        try
        {
            var message = formatter(state, exception);
            var logMessage = $"[{logLevel}] {_categoryName}: {message}";

            if (exception != null)
            {
                logMessage += $"\nException: {exception}";
            }

            _output.WriteLine(logMessage);
        }
        catch
        {
            // Ignore logging errors to prevent test failures due to logging issues
        }
    }

    private class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new NullScope();
        public void Dispose() { }
    }
}
