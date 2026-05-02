using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace PoshMcp.Tests.Unit;

public class ConfigureApplicationInsightsTests
{
    private static readonly MethodInfo ConfigureMethod = typeof(PoshMcp.StdioServerHost)
        .GetMethod("ConfigureApplicationInsights", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void InvokeConfigureApplicationInsights(
        IServiceCollection services,
        IConfiguration configuration,
        bool isStdioMode = false)
    {
        ConfigureMethod.Invoke(null, [services, configuration, isStdioMode]);
    }

    private static IConfiguration BuildConfiguration(bool enabled, string connectionString = "", int samplingPercentage = 100)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("ApplicationInsights:Enabled", enabled.ToString()),
                new KeyValuePair<string, string?>("ApplicationInsights:ConnectionString", connectionString),
                new KeyValuePair<string, string?>("ApplicationInsights:SamplingPercentage", samplingPercentage.ToString()),
            })
            .Build();
        return config;
    }

    [Fact]
    public void Enabled_False_DoesNotRegisterAzureMonitor()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(enabled: false);

        // Act
        InvokeConfigureApplicationInsights(services, configuration);

        // Assert — no OpenTelemetry services should be registered
        var descriptors = services.Where(d =>
            d.ServiceType.FullName?.Contains("OpenTelemetry") == true ||
            d.ServiceType.FullName?.Contains("AzureMonitor") == true).ToList();

        Assert.Empty(descriptors);
    }

    [Fact]
    public void Enabled_True_WithConnectionString_RegistersAzureMonitor()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var connectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://eastus-0.in.applicationinsights.azure.com/";
        var configuration = BuildConfiguration(enabled: true, connectionString: connectionString);

        // Act
        InvokeConfigureApplicationInsights(services, configuration);

        // Assert — OpenTelemetry services should be registered
        var hasOpenTelemetry = services.Any(d =>
            d.ServiceType.FullName?.Contains("OpenTelemetry") == true ||
            d.ImplementationType?.FullName?.Contains("OpenTelemetry") == true ||
            d.ServiceType.FullName?.Contains("AzureMonitor") == true);

        Assert.True(hasOpenTelemetry, "Expected OpenTelemetry/AzureMonitor services to be registered when Enabled=true with a connection string.");
    }

    [Fact]
    public void Enabled_True_NoConnectionString_LogsWarning_NoCrash()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(enabled: true, connectionString: "");

        // Clear env var to ensure no fallback
        var originalEnvVar = Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
        Environment.SetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING", null);

        try
        {
            // Capture stderr
            var stderr = new StringWriter();
            var originalStderr = Console.Error;
            Console.SetError(stderr);

            try
            {
                // Act — should not throw
                InvokeConfigureApplicationInsights(services, configuration);

                // Assert — warning written to stderr
                var output = stderr.ToString();
                Assert.Contains("no connection string", output, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Console.SetError(originalStderr);
            }

            // Assert — no OpenTelemetry services registered
            var hasOpenTelemetry = services.Any(d =>
                d.ServiceType.FullName?.Contains("OpenTelemetry") == true ||
                d.ServiceType.FullName?.Contains("AzureMonitor") == true);
            Assert.False(hasOpenTelemetry, "Expected no OpenTelemetry services when connection string is missing.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING", originalEnvVar);
        }
    }

    [Fact]
    public void SamplingPercentage_50_SetsSamplingRatio()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var connectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://eastus-0.in.applicationinsights.azure.com/";
        var configuration = BuildConfiguration(enabled: true, connectionString: connectionString, samplingPercentage: 50);

        // Capture stderr to verify sampling info message
        var stderr = new StringWriter();
        var originalStderr = Console.Error;
        Console.SetError(stderr);

        try
        {
            // Act
            InvokeConfigureApplicationInsights(services, configuration);

            // Assert — the info message should report 50% sampling
            var output = stderr.ToString();
            Assert.Contains("Sampling: 50%", output);
        }
        finally
        {
            Console.SetError(originalStderr);
        }

        // Assert — OpenTelemetry services should be registered (meaning config was accepted)
        var hasOpenTelemetry = services.Any(d =>
            d.ServiceType.FullName?.Contains("OpenTelemetry") == true ||
            d.ImplementationType?.FullName?.Contains("OpenTelemetry") == true);
        Assert.True(hasOpenTelemetry, "Expected OpenTelemetry services to be registered with 50% sampling.");
    }

    [Fact]
    public void Enabled_True_ConnectionString_FromEnvVar_RegistersAzureMonitor()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = BuildConfiguration(enabled: true, connectionString: ""); // empty in config
        var envConnStr = "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://eastus-0.in.applicationinsights.azure.com/";

        var originalEnvVar = Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
        Environment.SetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING", envConnStr);

        try
        {
            // Act
            InvokeConfigureApplicationInsights(services, configuration);

            // Assert — OpenTelemetry services should be registered via env var fallback
            var hasOpenTelemetry = services.Any(d =>
                d.ServiceType.FullName?.Contains("OpenTelemetry") == true ||
                d.ImplementationType?.FullName?.Contains("OpenTelemetry") == true ||
                d.ServiceType.FullName?.Contains("AzureMonitor") == true);

            Assert.True(hasOpenTelemetry, "Expected OpenTelemetry/AzureMonitor services when connection string comes from env var.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING", originalEnvVar);
        }
    }

    [Fact]
    public void Enabled_True_WithConnectionString_AddsLoggerFilterRule()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var connectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://eastus-0.in.applicationinsights.azure.com/";
        var configuration = BuildConfiguration(enabled: true, connectionString: connectionString);

        // Act
        InvokeConfigureApplicationInsights(services, configuration);

        // Assert — LoggerFilterOptions should be configured (for OTel log suppression)
        var hasLoggerFilterConfig = services.Any(d =>
            d.ServiceType == typeof(Microsoft.Extensions.Options.IConfigureOptions<LoggerFilterOptions>));

        Assert.True(hasLoggerFilterConfig, "Expected LoggerFilterOptions to be configured for OTel log suppression.");
    }
}
