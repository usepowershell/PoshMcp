using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Service for dynamically reloading PowerShell configuration and regenerating MCP tools
/// </summary>
public class PowerShellConfigurationReloadService
{
    private readonly ILogger<PowerShellConfigurationReloadService> _logger;
    private readonly McpToolFactoryV2 _toolFactory;
    private readonly object _reloadLock = new object();
    private PowerShellConfiguration _currentConfiguration;
    private List<McpServerTool> _currentTools;
    private readonly string _configurationFilePath;

    public PowerShellConfigurationReloadService(
        ILogger<PowerShellConfigurationReloadService> logger,
        McpToolFactoryV2 toolFactory,
        PowerShellConfiguration initialConfiguration,
        string configurationFilePath)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _toolFactory = toolFactory ?? throw new ArgumentNullException(nameof(toolFactory));
        _currentConfiguration = initialConfiguration ?? throw new ArgumentNullException(nameof(initialConfiguration));
        _configurationFilePath = configurationFilePath ?? throw new ArgumentNullException(nameof(configurationFilePath));
        _currentTools = new List<McpServerTool>();
    }

    /// <summary>
    /// Gets the current PowerShell configuration
    /// </summary>
    public PowerShellConfiguration CurrentConfiguration
    {
        get
        {
            lock (_reloadLock)
            {
                return _currentConfiguration;
            }
        }
    }

    /// <summary>
    /// Gets the current list of generated MCP tools
    /// </summary>
    public List<McpServerTool> CurrentTools
    {
        get
        {
            lock (_reloadLock)
            {
                return new List<McpServerTool>(_currentTools);
            }
        }
    }

    /// <summary>
    /// Reloads configuration from the configuration file and regenerates tools
    /// </summary>
    /// <returns>Result of the reload operation</returns>
    public async Task<ConfigurationReloadResult> ReloadFromFileAsync()
    {
        try
        {
            _logger.LogInformation($"Reloading PowerShell configuration from file: {_configurationFilePath}");

            if (!File.Exists(_configurationFilePath))
            {
                var error = $"Configuration file not found: {_configurationFilePath}";
                _logger.LogError(error);
                return new ConfigurationReloadResult
                {
                    Success = false,
                    ErrorMessage = error
                };
            }

            // Read and parse configuration file
            var configJson = await File.ReadAllTextAsync(_configurationFilePath);
            var configRoot = JsonSerializer.Deserialize<JsonElement>(configJson);

            // Extract PowerShellConfiguration section
            if (!configRoot.TryGetProperty("PowerShellConfiguration", out var psConfigSection))
            {
                var error = "PowerShellConfiguration section not found in configuration file";
                _logger.LogError(error);
                return new ConfigurationReloadResult
                {
                    Success = false,
                    ErrorMessage = error
                };
            }

            var newConfig = JsonSerializer.Deserialize<PowerShellConfiguration>(psConfigSection.GetRawText());
            if (newConfig == null)
            {
                var error = "Failed to deserialize PowerShellConfiguration from file";
                _logger.LogError(error);
                return new ConfigurationReloadResult
                {
                    Success = false,
                    ErrorMessage = error
                };
            }

            return await ReloadConfigurationAsync(newConfig);
        }
        catch (Exception ex)
        {
            var error = $"Failed to reload configuration from file: {ex.Message}";
            _logger.LogError(ex, error);
            return new ConfigurationReloadResult
            {
                Success = false,
                ErrorMessage = error
            };
        }
    }

    /// <summary>
    /// Updates configuration programmatically and regenerates tools
    /// </summary>
    /// <param name="newConfiguration">New configuration to apply</param>
    /// <returns>Result of the reload operation</returns>
    public Task<ConfigurationReloadResult> ReloadConfigurationAsync(PowerShellConfiguration newConfiguration)
    {
        if (newConfiguration == null)
        {
            throw new ArgumentNullException(nameof(newConfiguration));
        }

        try
        {
            lock (_reloadLock)
            {
                _logger.LogInformation("Updating PowerShell configuration and regenerating tools");

                // Log configuration changes
                LogConfigurationChanges(_currentConfiguration, newConfiguration);

                // Update current configuration
                _currentConfiguration = newConfiguration;

                // Clear the tool factory cache to force regeneration
                _toolFactory.ClearCache();

                // Generate new tools with updated configuration
                var newTools = _toolFactory.GetToolsList(newConfiguration, _logger);

                _logger.LogInformation($"Successfully generated {newTools.Count} tools with new configuration");

                // Update current tools list
                _currentTools = newTools;

                return Task.FromResult(new ConfigurationReloadResult
                {
                    Success = true,
                    ToolCount = newTools.Count,
                    Message = $"Successfully reloaded configuration and generated {newTools.Count} tools"
                });
            }
        }
        catch (Exception ex)
        {
            var error = $"Failed to reload configuration: {ex.Message}";
            _logger.LogError(ex, error);
            return Task.FromResult(new ConfigurationReloadResult
            {
                Success = false,
                ErrorMessage = error
            });
        }
    }

    /// <summary>
    /// Gets information about the current configuration and tools
    /// </summary>
    /// <returns>Configuration status information</returns>
    public ConfigurationStatusInfo GetStatus()
    {
        lock (_reloadLock)
        {
            return new ConfigurationStatusInfo
            {
                FunctionNamesCount = _currentConfiguration.FunctionNames.Count,
                ModulesCount = _currentConfiguration.Modules.Count,
                IncludePatternsCount = _currentConfiguration.IncludePatterns.Count,
                ExcludePatternsCount = _currentConfiguration.ExcludePatterns.Count,
                ToolCount = _currentTools.Count,
                ConfigurationFilePath = _configurationFilePath
            };
        }
    }

    private void LogConfigurationChanges(PowerShellConfiguration oldConfig, PowerShellConfiguration newConfig)
    {
        _logger.LogDebug("Configuration changes:");
        _logger.LogDebug($"  Function names: {oldConfig.FunctionNames.Count} -> {newConfig.FunctionNames.Count}");
        _logger.LogDebug($"  Modules: {oldConfig.Modules.Count} -> {newConfig.Modules.Count}");
        _logger.LogDebug($"  Include patterns: {oldConfig.IncludePatterns.Count} -> {newConfig.IncludePatterns.Count}");
        _logger.LogDebug($"  Exclude patterns: {oldConfig.ExcludePatterns.Count} -> {newConfig.ExcludePatterns.Count}");

        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace($"  New function names: [{string.Join(", ", newConfig.FunctionNames)}]");
            _logger.LogTrace($"  New modules: [{string.Join(", ", newConfig.Modules)}]");
            _logger.LogTrace($"  New include patterns: [{string.Join(", ", newConfig.IncludePatterns)}]");
            _logger.LogTrace($"  New exclude patterns: [{string.Join(", ", newConfig.ExcludePatterns)}]");
        }
    }
}

/// <summary>
/// Result of a configuration reload operation
/// </summary>
public class ConfigurationReloadResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }
    public int ToolCount { get; set; }
}

/// <summary>
/// Information about current configuration status
/// </summary>
public class ConfigurationStatusInfo
{
    public int FunctionNamesCount { get; set; }
    public int ModulesCount { get; set; }
    public int IncludePatternsCount { get; set; }
    public int ExcludePatternsCount { get; set; }
    public int ToolCount { get; set; }
    public string ConfigurationFilePath { get; set; } = string.Empty;
}