using Microsoft.Extensions.Logging;
using Serilog.Events;

namespace PoshMcp;

internal static class LoggingHelpers
{
    internal static ILoggerFactory CreateLoggerFactory(LogLevel logLevel)
    {
        return LoggerFactory.Create(builder =>
            builder.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace)
                   .SetMinimumLevel(logLevel));
    }

    internal static LogEventLevel MapToSerilogLevel(LogLevel level) =>
        level switch
        {
            LogLevel.Trace => LogEventLevel.Verbose,
            LogLevel.Debug => LogEventLevel.Debug,
            LogLevel.Warning => LogEventLevel.Warning,
            LogLevel.Error => LogEventLevel.Error,
            LogLevel.Critical => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        };

    internal static string InferEffectiveLogLevel(ILogger logger)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            return LogLevel.Trace.ToString();
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            return LogLevel.Debug.ToString();
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            return LogLevel.Information.ToString();
        }

        if (logger.IsEnabled(LogLevel.Warning))
        {
            return LogLevel.Warning.ToString();
        }

        if (logger.IsEnabled(LogLevel.Error))
        {
            return LogLevel.Error.ToString();
        }

        return LogLevel.None.ToString();
    }
}
