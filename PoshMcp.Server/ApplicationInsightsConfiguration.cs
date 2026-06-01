using Microsoft.Extensions.Configuration;
using System;

namespace PoshMcp.Server;

internal static class ApplicationInsightsConfiguration
{
    internal static ApplicationInsightsOptions GetOptions(IConfiguration configuration)
    {
        return configuration.GetSection(ApplicationInsightsOptions.SectionName).Get<ApplicationInsightsOptions>()
               ?? new ApplicationInsightsOptions();
    }

    internal static string? ResolveConnectionString(ApplicationInsightsOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
            return options.ConnectionString;

        return Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
    }

    internal static bool IsConfigured(IConfiguration configuration)
    {
        var options = GetOptions(configuration);
        return options.Enabled && !string.IsNullOrWhiteSpace(ResolveConnectionString(options));
    }
}