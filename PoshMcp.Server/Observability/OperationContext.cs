using System;
using System.Threading;

namespace PoshMcp.Server.Observability;

/// <summary>
/// Provides correlation ID and operation context tracking using AsyncLocal for async flow
/// </summary>
public static class OperationContext
{
    private static readonly AsyncLocal<string?> _correlationId = new AsyncLocal<string?>();
    private static readonly AsyncLocal<string?> _operationName = new AsyncLocal<string?>();

    /// <summary>
    /// Gets or sets the correlation ID for the current operation
    /// </summary>
    public static string CorrelationId
    {
        get => _correlationId.Value ?? GenerateCorrelationId();
        set => _correlationId.Value = value;
    }

    /// <summary>
    /// Gets or sets the operation name for the current operation
    /// </summary>
    public static string? OperationName
    {
        get => _operationName.Value;
        set => _operationName.Value = value;
    }

    /// <summary>
    /// Generates a new correlation ID
    /// </summary>
    public static string GenerateCorrelationId()
    {
        // Format: timestamp-guid (sortable and unique)
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var guid = Guid.NewGuid().ToString("N")[..8]; // First 8 chars of GUID
        return $"{timestamp}-{guid}";
    }

    /// <summary>
    /// Creates a new operation scope with a fresh correlation ID
    /// </summary>
    public static IDisposable BeginOperation(string operationName)
    {
        return new OperationScope(GenerateCorrelationId(), operationName);
    }

    /// <summary>
    /// Creates an operation scope with an existing correlation ID
    /// </summary>
    public static IDisposable BeginOperation(string correlationId, string operationName)
    {
        return new OperationScope(correlationId, operationName);
    }

    private class OperationScope : IDisposable
    {
        private readonly string? _previousCorrelationId;
        private readonly string? _previousOperationName;

        public OperationScope(string correlationId, string operationName)
        {
            _previousCorrelationId = _correlationId.Value;
            _previousOperationName = _operationName.Value;

            _correlationId.Value = correlationId;
            _operationName.Value = operationName;
        }

        public void Dispose()
        {
            _correlationId.Value = _previousCorrelationId;
            _operationName.Value = _previousOperationName;
        }
    }
}
