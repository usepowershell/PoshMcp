using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PoshMcp.Server.Authentication;
using PoshMcp.Server.Health;
using PoshMcp.Server.PowerShell;
using System.Threading.Tasks;
using Xunit;

namespace PoshMcp.Tests.Unit;

public class ConfigurationHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_WithNoCommandsModulesOrPatterns_ReturnsDegraded()
    {
        var config = new PowerShellConfiguration();
        var authConfig = new AuthenticationConfiguration();
        var healthCheck = new ConfigurationHealthCheck(
            Options.Create(config),
            Options.Create(authConfig),
            NullLogger<ConfigurationHealthCheck>.Instance);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("no functions/modules/patterns", result.Description ?? string.Empty);
    }

    [Fact]
    public async Task CheckHealthAsync_WithConfiguredCommandNames_ReturnsHealthy()
    {
        var config = new PowerShellConfiguration
        {
            CommandNames = { "Get-Process", "Get-Service" }
        };
        var authConfig = new AuthenticationConfiguration();
        var healthCheck = new ConfigurationHealthCheck(
            Options.Create(config),
            Options.Create(authConfig),
            NullLogger<ConfigurationHealthCheck>.Instance);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(2, result.Data["FunctionCount"]);
    }
}
