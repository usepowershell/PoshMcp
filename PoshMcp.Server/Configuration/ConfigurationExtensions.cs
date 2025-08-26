using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PoshMcp.Server.PowerShell;

namespace PoshMcp.Server.Configuration;

/// <summary>
/// Extension methods for configuration setup
/// </summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// Adds PowerShell configuration with validation
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configurationSection">Configuration section</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddPowerShellConfiguration(
        this IServiceCollection services, 
        Microsoft.Extensions.Configuration.IConfigurationSection configurationSection)
    {
        // Configure the PowerShell configuration options
        services.Configure<PowerShellConfiguration>(configurationSection);

        // Add validation for the configuration
        services.AddSingleton<IValidateOptions<PowerShellConfiguration>, PowerShellConfigurationValidator>();

        // Add startup validation service
        services.AddHostedService<ConfigurationValidationService>();

        // Enable options validation to run on first access
        services.AddOptions<PowerShellConfiguration>()
            .ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Adds PowerShell configuration with validation using a configuration delegate
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureOptions">Configuration delegate</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddPowerShellConfiguration(
        this IServiceCollection services,
        System.Action<PowerShellConfiguration> configureOptions)
    {
        // Configure the PowerShell configuration options
        services.Configure(configureOptions);

        // Add validation for the configuration
        services.AddSingleton<IValidateOptions<PowerShellConfiguration>, PowerShellConfigurationValidator>();

        // Add startup validation service
        services.AddHostedService<ConfigurationValidationService>();

        // Enable options validation to run on first access
        services.AddOptions<PowerShellConfiguration>()
            .ValidateOnStart();

        return services;
    }
}