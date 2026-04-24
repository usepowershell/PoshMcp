using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PoshMcp.Server.Authentication;
using PoshMcp.Server.PowerShell;

namespace PoshMcp.Server.Health;

/// <summary>
/// Health check for PowerShell configuration validity
/// </summary>
public class ConfigurationHealthCheck : IHealthCheck
{
    private readonly IOptions<PowerShellConfiguration> _configuration;
    private readonly IOptions<AuthenticationConfiguration> _authConfiguration;
    private readonly ILogger<ConfigurationHealthCheck> _logger;

    public ConfigurationHealthCheck(
        IOptions<PowerShellConfiguration> configuration,
        IOptions<AuthenticationConfiguration> authConfiguration,
        ILogger<ConfigurationHealthCheck> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _authConfiguration = authConfiguration ?? throw new ArgumentNullException(nameof(authConfiguration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var config = _configuration.Value;

            if (config == null)
            {
                _logger.LogError("PowerShell configuration is null");
                return Task.FromResult(HealthCheckResult.Unhealthy("Configuration is null"));
            }

            var effectiveCommandNames = config.GetEffectiveCommandNames();

            // Check if we have any commands or modules configured
            var hasFunctions = effectiveCommandNames.Any();
            var hasModules = config.Modules?.Any() == true;
            var hasIncludes = config.IncludePatterns?.Any() == true;

            if (!hasFunctions && !hasModules && !hasIncludes)
            {
                _logger.LogWarning("Configuration is valid but has no functions, modules, or include patterns defined");
                return Task.FromResult(HealthCheckResult.Degraded(
                    "Configuration valid but no functions/modules/patterns defined"));
            }

            var data = new System.Collections.Generic.Dictionary<string, object>
            {
                ["FunctionCount"] = effectiveCommandNames.Count,
                ["ModuleCount"] = config.Modules?.Count ?? 0,
                ["IncludePatternCount"] = config.IncludePatterns?.Count ?? 0,
                ["ExcludePatternCount"] = config.ExcludePatterns?.Count ?? 0
            };

            var authConfig = _authConfiguration.Value;
            data["AuthEnabled"] = authConfig?.Enabled ?? false;
            data["AuthSchemes"] = authConfig?.Enabled == true && authConfig.Schemes.Count > 0
                ? string.Join(", ", authConfig.Schemes.Keys)
                : "none";
            data["ToolsWithAuthOverrides"] = config.GetEffectiveCommandOverrides().Values
                .Count(o => (o.RequiredScopes?.Count > 0) || (o.RequiredRoles?.Count > 0));

            _logger.LogDebug("Configuration health check passed");
            return Task.FromResult(HealthCheckResult.Healthy("Configuration is valid", data));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Configuration health check failed");
            return Task.FromResult(HealthCheckResult.Unhealthy($"Configuration check failed: {ex.Message}", ex));
        }
    }
}
