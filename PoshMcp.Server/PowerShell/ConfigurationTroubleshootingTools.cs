using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// MCP tool for doctor-style configuration troubleshooting against the active server configuration.
/// </summary>
public class ConfigurationTroubleshootingTools
{
    private readonly string _configurationPath;
    private readonly string _effectiveTransport;
    private readonly string? _effectiveSessionMode;
    private readonly string? _effectiveMcpPath;
    private readonly Func<List<McpServerTool>> _registeredToolsProvider;
    private readonly ILogger<ConfigurationTroubleshootingTools> _logger;

    public ConfigurationTroubleshootingTools(
        string configurationPath,
        string effectiveTransport,
        string? effectiveSessionMode,
        string? effectiveMcpPath,
        Func<List<McpServerTool>> registeredToolsProvider,
        ILogger<ConfigurationTroubleshootingTools> logger)
    {
        _configurationPath = configurationPath ?? throw new ArgumentNullException(nameof(configurationPath));
        _effectiveTransport = effectiveTransport ?? throw new ArgumentNullException(nameof(effectiveTransport));
        _effectiveSessionMode = effectiveSessionMode;
        _effectiveMcpPath = effectiveMcpPath;
        _registeredToolsProvider = registeredToolsProvider ?? throw new ArgumentNullException(nameof(registeredToolsProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Returns doctor-style configuration diagnostics for the running server.
    /// </summary>
    public Task<string> GetConfigurationTroubleshooting(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing configuration troubleshooting request");

            var config = Program.LoadPowerShellConfiguration(_configurationPath, _logger);
            var tools = _registeredToolsProvider();
            var logLevel = InferEffectiveLogLevel();

            return Task.FromResult(Program.BuildDoctorJson(
                configurationPath: _configurationPath,
                configurationPathSource: "runtime",
                effectiveLogLevel: logLevel,
                effectiveLogLevelSource: "runtime",
                effectiveTransport: _effectiveTransport,
                effectiveTransportSource: "runtime",
                effectiveSessionMode: _effectiveSessionMode,
                effectiveSessionModeSource: "runtime",
                effectiveMcpPath: _effectiveMcpPath,
                effectiveMcpPathSource: "runtime",
                config: config,
                tools: tools));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating configuration troubleshooting output");
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Unexpected error: {ex.Message}"
            }));
        }
    }

    private string InferEffectiveLogLevel()
    {
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            return LogLevel.Trace.ToString();
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            return LogLevel.Debug.ToString();
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            return LogLevel.Information.ToString();
        }

        if (_logger.IsEnabled(LogLevel.Warning))
        {
            return LogLevel.Warning.ToString();
        }

        if (_logger.IsEnabled(LogLevel.Error))
        {
            return LogLevel.Error.ToString();
        }

        return LogLevel.None.ToString();
    }
}