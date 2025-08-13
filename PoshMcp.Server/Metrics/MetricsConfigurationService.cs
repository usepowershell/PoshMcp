using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace PoshMcp.Server.Metrics;

/// <summary>
/// Service to configure metrics when the host starts
/// </summary>
public class MetricsConfigurationService : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // This service exists just to ensure metrics are configured when DI container is built
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}