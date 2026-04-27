namespace PoshMcp.Server;

/// <summary>
/// Configuration options for optional Azure Application Insights telemetry export.
/// </summary>
public sealed class ApplicationInsightsOptions
{
    public const string SectionName = "ApplicationInsights";

    /// <summary>
    /// When false (default), no Application Insights SDK is loaded. Zero overhead.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Application Insights connection string. If empty and Enabled is true,
    /// falls back to APPLICATIONINSIGHTS_CONNECTION_STRING environment variable.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Percentage of telemetry to sample (1-100). Default: 100 (all telemetry).
    /// </summary>
    public int SamplingPercentage { get; set; } = 100;
}
