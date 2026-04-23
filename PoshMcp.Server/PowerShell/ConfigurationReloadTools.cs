using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// MCP tools for dynamic configuration reload functionality
/// </summary>
public class ConfigurationReloadTools
{
    private readonly PowerShellConfigurationReloadService _reloadService;
    private readonly string _configurationPath;
    private readonly string _configurationPathSource;
    private readonly string _effectiveTransport;
    private readonly string? _effectiveSessionMode;
    private readonly string? _effectiveRuntimeMode;
    private readonly string? _effectiveMcpPath;
    private readonly Func<List<McpServerTool>> _registeredToolsProvider;
    private readonly ILogger<ConfigurationReloadTools> _logger;

    public ConfigurationReloadTools(
        PowerShellConfigurationReloadService reloadService,
        ILogger<ConfigurationReloadTools> logger)
        : this(
            reloadService,
            reloadService.GetStatus().ConfigurationFilePath,
            InferConfigurationPathSource(reloadService.GetStatus().ConfigurationFilePath),
            "stdio",
            null,
            reloadService.CurrentConfiguration.RuntimeMode.ToString(),
            null,
            static () => new List<McpServerTool>(),
            logger)
    {
    }

    public ConfigurationReloadTools(
        PowerShellConfigurationReloadService reloadService,
        string configurationPath,
        string configurationPathSource,
        string effectiveTransport,
        string? effectiveSessionMode,
        string? effectiveRuntimeMode,
        string? effectiveMcpPath,
        Func<List<McpServerTool>> registeredToolsProvider,
        ILogger<ConfigurationReloadTools> logger)
    {
        _reloadService = reloadService ?? throw new ArgumentNullException(nameof(reloadService));
        _configurationPath = configurationPath ?? string.Empty;
        _configurationPathSource = string.IsNullOrWhiteSpace(configurationPathSource) ? "runtime" : configurationPathSource;
        _effectiveTransport = effectiveTransport ?? throw new ArgumentNullException(nameof(effectiveTransport));
        _effectiveSessionMode = effectiveSessionMode;
        _effectiveRuntimeMode = effectiveRuntimeMode;
        _effectiveMcpPath = effectiveMcpPath;
        _registeredToolsProvider = registeredToolsProvider ?? throw new ArgumentNullException(nameof(registeredToolsProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Reloads PowerShell configuration from the configuration file
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the reload operation</returns>
    public async Task<string> ReloadConfigurationFromFile(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing reload configuration from file request");

            var result = await _reloadService.ReloadFromFileAsync();

            if (result.Success)
            {
                _logger.LogInformation($"Configuration reload successful: {result.Message}");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = result.Message,
                    toolCount = result.ToolCount
                }, new JsonSerializerOptions { WriteIndented = true });
            }
            else
            {
                _logger.LogError($"Configuration reload failed: {result.ErrorMessage}");
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = result.ErrorMessage
                }, new JsonSerializerOptions { WriteIndented = true });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during configuration reload from file");
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Unexpected error: {ex.Message}"
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    /// <summary>
    /// Updates PowerShell configuration with new settings
    /// </summary>
    /// <param name="configurationJson">JSON representation of new PowerShell configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the update operation</returns>
    public async Task<string> UpdateConfiguration(string configurationJson, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing update configuration request");
            _logger.LogDebug($"New configuration JSON: {configurationJson}");

            if (string.IsNullOrWhiteSpace(configurationJson))
            {
                var error = "Configuration JSON cannot be empty";
                _logger.LogError(error);
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = error
                }, new JsonSerializerOptions { WriteIndented = true });
            }

            // Parse the new configuration
            PowerShellConfiguration? newConfig;
            try
            {
                newConfig = JsonSerializer.Deserialize<PowerShellConfiguration>(configurationJson);
                if (newConfig == null)
                {
                    throw new JsonException("Deserialization resulted in null configuration");
                }
            }
            catch (JsonException ex)
            {
                var error = $"Invalid configuration JSON: {ex.Message}";
                _logger.LogError(error);
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = error
                }, new JsonSerializerOptions { WriteIndented = true });
            }

            var result = await _reloadService.ReloadConfigurationAsync(newConfig);

            if (result.Success)
            {
                _logger.LogInformation($"Configuration update successful: {result.Message}");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = result.Message,
                    toolCount = result.ToolCount
                }, new JsonSerializerOptions { WriteIndented = true });
            }
            else
            {
                _logger.LogError($"Configuration update failed: {result.ErrorMessage}");
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = result.ErrorMessage
                }, new JsonSerializerOptions { WriteIndented = true });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during configuration update");
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Unexpected error: {ex.Message}"
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    /// <summary>
    /// Gets the current configuration status and tool information
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Current configuration status</returns>
    public async Task<string> GetConfigurationStatus(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing get configuration status request");

            var currentConfig = _reloadService.CurrentConfiguration;
            var report = Program.BuildDoctorReportFromConfig(
                configurationPath: _configurationPath,
                configurationPathSource: _configurationPathSource,
                effectiveLogLevel: LoggingHelpers.InferEffectiveLogLevel(_logger),
                effectiveLogLevelSource: "runtime",
                effectiveTransport: _effectiveTransport,
                effectiveTransportSource: "runtime",
                effectiveSessionMode: _effectiveSessionMode,
                effectiveSessionModeSource: "runtime",
                effectiveRuntimeMode: _effectiveRuntimeMode,
                effectiveRuntimeModeSource: "runtime",
                effectiveMcpPath: _effectiveMcpPath,
                effectiveMcpPathSource: "runtime",
                config: currentConfig,
                tools: _registeredToolsProvider());

            return await Task.FromResult(Program.BuildDoctorJson(report));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting configuration status");
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Unexpected error: {ex.Message}"
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    private static string InferConfigurationPathSource(string configurationPath)
    {
        return string.IsNullOrWhiteSpace(configurationPath) ? SettingsResolver.EnvSource : "runtime";
    }
}