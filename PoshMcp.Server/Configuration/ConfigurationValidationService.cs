using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PoshMcp.Server.PowerShell;

namespace PoshMcp.Server.Configuration;

/// <summary>
/// Hosted service that validates configuration on startup
/// </summary>
public class ConfigurationValidationService : IHostedService
{
    private readonly ILogger<ConfigurationValidationService> _logger;
    private readonly IServiceProvider _serviceProvider;

    public ConfigurationValidationService(
        ILogger<ConfigurationValidationService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Validating configuration on startup...");

            // Validate PowerShell configuration
            ValidatePowerShellConfiguration();

            _logger.LogInformation("Configuration validation completed successfully");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Configuration validation failed. Application will not start.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private void ValidatePowerShellConfiguration()
    {
        try
        {
            // Get the configured PowerShell configuration
            var options = _serviceProvider.GetRequiredService<IOptions<PowerShellConfiguration>>();
            var config = options.Value;

            _logger.LogDebug("PowerShell configuration loaded:");
            _logger.LogDebug("  FunctionNames: [{FunctionNames}]", string.Join(", ", config.FunctionNames));
            _logger.LogDebug("  Modules: [{Modules}]", string.Join(", ", config.Modules));
            _logger.LogDebug("  IncludePatterns: [{IncludePatterns}]", string.Join(", ", config.IncludePatterns));
            _logger.LogDebug("  ExcludePatterns: [{ExcludePatterns}]", string.Join(", ", config.ExcludePatterns));
            _logger.LogDebug("  EnableDynamicReloadTools: {EnableDynamicReloadTools}", config.EnableDynamicReloadTools);

            // The validation is automatically performed by IValidateOptions<PowerShellConfiguration>
            // If we reach here, validation passed
            _logger.LogInformation("PowerShell configuration is valid");

            // Perform additional runtime validation if needed
            ValidateRuntimeConfiguration(config);
        }
        catch (OptionsValidationException ex)
        {
            _logger.LogError("PowerShell configuration validation failed:");
            foreach (var failure in ex.Failures)
            {
                _logger.LogError("  - {Failure}", failure);
            }
            throw new InvalidOperationException("PowerShell configuration is invalid. Check the logs for details.", ex);
        }
    }

    private void ValidateRuntimeConfiguration(PowerShellConfiguration config)
    {
        // Additional validation that requires runtime context
        // For example, checking if specified modules are actually available

        if (config.Modules.Count > 0)
        {
            _logger.LogDebug("Checking module availability would be performed here in a full implementation");
            // Note: We could add PowerShell module availability checking here
            // but it would require initializing PowerShell, which might be expensive at startup
        }

        if (config.FunctionNames.Count > 0)
        {
            _logger.LogDebug("Checking function availability would be performed here in a full implementation");
            // Similar to modules, we could validate function existence
        }

        // Log configuration summary
        var totalSources = config.FunctionNames.Count + config.Modules.Count + config.IncludePatterns.Count;
        _logger.LogInformation("Configuration summary: {TotalSources} source(s) configured, {ExcludePatterns} exclude pattern(s), Dynamic reload: {DynamicReload}",
            totalSources, config.ExcludePatterns.Count, config.EnableDynamicReloadTools);
    }
}