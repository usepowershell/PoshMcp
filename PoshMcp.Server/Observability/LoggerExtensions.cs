using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace PoshMcp.Server.Observability;

/// <summary>
/// Extension methods for logging with correlation IDs
/// </summary>
public static class LoggerExtensions
{
    /// <summary>
    /// Creates a logging scope that includes the current correlation ID
    /// </summary>
    /// <remarks>
    /// Use this method at the start of an operation to create a scope that applies to all subsequent log statements.
    /// This is the RECOMMENDED pattern for methods with multiple log statements.
    /// </remarks>
    /// <example>
    /// <code>
    /// using (logger.BeginCorrelationScope())
    /// {
    ///     logger.LogInformation("Operation started");
    ///     await DoWork();
    ///     logger.LogInformation("Operation completed");
    /// }
    /// </code>
    /// </example>
    public static IDisposable? BeginCorrelationScope(this ILogger logger)
    {
        return logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = OperationContext.CorrelationId,
            ["OperationName"] = OperationContext.OperationName ?? "Unknown"
        });
    }

    /// <summary>
    /// Logs information with correlation ID automatically included
    /// </summary>
    /// <remarks>
    /// ⚠️ PERFORMANCE WARNING: This method creates a new logging scope for each call.
    /// For hot paths or methods with multiple log statements, use BeginCorrelationScope() instead
    /// to create one scope at the method entry point.
    /// This method is suitable for single, isolated log statements only.
    /// </remarks>
    public static void LogInformationWithCorrelation(
        this ILogger logger,
        string message,
        params object[] args)
    {
        using (logger.BeginCorrelationScope())
        {
            logger.LogInformation(message, args);
        }
    }

    /// <summary>
    /// Logs warning with correlation ID automatically included
    /// </summary>
    /// <remarks>
    /// ⚠️ PERFORMANCE WARNING: This method creates a new logging scope for each call.
    /// For hot paths or methods with multiple log statements, use BeginCorrelationScope() instead
    /// to create one scope at the method entry point.
    /// This method is suitable for single, isolated log statements only.
    /// </remarks>
    public static void LogWarningWithCorrelation(
        this ILogger logger,
        string message,
        params object[] args)
    {
        using (logger.BeginCorrelationScope())
        {
            logger.LogWarning(message, args);
        }
    }

    /// <summary>
    /// Logs error with correlation ID automatically included
    /// </summary>
    /// <remarks>
    /// ⚠️ PERFORMANCE WARNING: This method creates a new logging scope for each call.
    /// For hot paths or methods with multiple log statements, use BeginCorrelationScope() instead
    /// to create one scope at the method entry point.
    /// This method is suitable for single, isolated log statements only.
    /// </remarks>
    public static void LogErrorWithCorrelation(
        this ILogger logger,
        Exception exception,
        string message,
        params object[] args)
    {
        using (logger.BeginCorrelationScope())
        {
            logger.LogError(exception, message, args);
        }
    }

    /// <summary>
    /// Logs debug with correlation ID automatically included
    /// </summary>
    /// <remarks>
    /// ⚠️ PERFORMANCE WARNING: This method creates a new logging scope for each call.
    /// For hot paths or methods with multiple log statements, use BeginCorrelationScope() instead
    /// to create one scope at the method entry point.
    /// This method is suitable for single, isolated log statements only.
    /// </remarks>
    public static void LogDebugWithCorrelation(
        this ILogger logger,
        string message,
        params object[] args)
    {
        using (logger.BeginCorrelationScope())
        {
            logger.LogDebug(message, args);
        }
    }

    /// <summary>
    /// Logs trace with correlation ID automatically included
    /// </summary>
    /// <remarks>
    /// ⚠️ PERFORMANCE WARNING: This method creates a new logging scope for each call.
    /// For hot paths or methods with multiple log statements, use BeginCorrelationScope() instead
    /// to create one scope at the method entry point.
    /// This method is suitable for single, isolated log statements only.
    /// </remarks>
    public static void LogTraceWithCorrelation(
        this ILogger logger,
        string message,
        params object[] args)
    {
        using (logger.BeginCorrelationScope())
        {
            logger.LogTrace(message, args);
        }
    }
}
