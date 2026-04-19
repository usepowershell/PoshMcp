using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.McpPrompts;
using PoshMcp.Server.McpResources;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.OutOfProcess;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace PoshMcp;

internal static class ConfigurationLoader
{
    internal const string ConfigurationTroubleshootingToolEnvVar = "POSHMCP_ENABLE_CONFIGURATION_TROUBLESHOOTING_TOOL";

    internal static async Task<string> ResolveExplicitOrDefaultConfigPath(string? explicitConfigPath)
    {
        var preferredConfigPath = string.IsNullOrWhiteSpace(explicitConfigPath)
            ? new ResolvedSetting(null, SettingsResolver.DefaultSource)
            : new ResolvedSetting(explicitConfigPath, SettingsResolver.CliSource);

        var resolvedConfigPath = await SettingsResolver.ResolveConfigurationPathWithSourceAsync(preferredConfigPath);
        return resolvedConfigPath.Value ?? throw new InvalidOperationException("Resolved configuration path was empty.");
    }

    internal static PowerShellConfiguration LoadPowerShellConfiguration(string configPath, ILogger logger)
    {
        return LoadPowerShellConfiguration(configPath, logger, null);
    }

    internal static PowerShellConfiguration LoadPowerShellConfiguration(string configPath, ILogger logger, string? runtimeModeOverride)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(configPath, optional: false, reloadOnChange: false)
            .Build();

        var config = new PowerShellConfiguration();
        configuration.GetSection("PowerShellConfiguration").Bind(config);

        var configurationTroubleshootingOverride = Environment.GetEnvironmentVariable(ConfigurationTroubleshootingToolEnvVar);
        if (bool.TryParse(configurationTroubleshootingOverride, out var enableConfigurationTroubleshootingTool))
        {
            config.EnableConfigurationTroubleshootingTool = enableConfigurationTroubleshootingTool;
        }

        var runtimeModeSetting = SettingsResolver.ResolveEffectiveRuntimeModeFromConfiguration(config.RuntimeMode.ToString(), runtimeModeOverride);
        config.RuntimeMode = SettingsResolver.ResolveRuntimeMode(runtimeModeSetting.Value);
        if (config.RuntimeMode == RuntimeMode.Unsupported)
        {
            throw new InvalidOperationException($"Unsupported runtime mode '{runtimeModeSetting.Value}'. Supported runtime modes: in-process, out-of-process.");
        }

        LogConfigurationDetails(config, logger);
        return config;
    }

    internal static (McpResourcesConfiguration Resources, McpPromptsConfiguration Prompts) LoadResourcesAndPromptsConfiguration(string configPath)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(configPath, optional: false, reloadOnChange: false)
            .Build();

        var resourcesConfig = new McpResourcesConfiguration();
        configuration.GetSection("McpResources").Bind(resourcesConfig);

        var promptsConfig = new McpPromptsConfiguration();
        configuration.GetSection("McpPrompts").Bind(promptsConfig);

        return (resourcesConfig, promptsConfig);
    }

    internal static (McpResourcesDiagnostics Resources, McpPromptsDiagnostics Prompts) ValidateResourcesAndPrompts(string configPath)
    {
        var configDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath))
            ?? Directory.GetCurrentDirectory();

        var (resourcesConfig, promptsConfig) = LoadResourcesAndPromptsConfiguration(configPath);

        var resourcesDiag = McpResourcesValidator.Validate(resourcesConfig, configDirectory);
        var promptsDiag = McpPromptsValidator.Validate(promptsConfig, configDirectory);

        return (resourcesDiag, promptsDiag);
    }

    internal static (McpResourcesDiagnostics Resources, McpPromptsDiagnostics Prompts) TryValidateResourcesAndPrompts(string configurationPath)
    {
        try
        {
            return ValidateResourcesAndPrompts(configurationPath);
        }
        catch
        {
            return (
                new McpResourcesDiagnostics(0, 0, new List<string>(), new List<string>()),
                new McpPromptsDiagnostics(0, 0, new List<string>(), new List<string>()));
        }
    }

    internal static void LogConfigurationDetails(PowerShellConfiguration config, ILogger logger)
    {
        logger.LogDebug("Configuration loaded successfully");
        logger.LogTrace($"Command names: {string.Join(", ", config.GetEffectiveCommandNames())}");
        logger.LogTrace($"Modules: {string.Join(", ", config.Modules)}");
        logger.LogTrace($"Include patterns: {string.Join(", ", config.IncludePatterns)}");
        logger.LogTrace($"Exclude patterns: {string.Join(", ", config.ExcludePatterns)}");
        logger.LogTrace($"Runtime mode: {config.RuntimeMode}");
    }

    internal static McpResourcesConfiguration LoadMcpResourcesConfiguration(string configPath, ILogger logger)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(configPath, optional: false, reloadOnChange: false)
            .Build();

        var config = new McpResourcesConfiguration();
        configuration.GetSection("McpResources").Bind(config);

        logger.LogDebug("McpResources configuration loaded: {Count} resource(s)", config.Resources.Count);
        return config;
    }

    internal static McpPromptsConfiguration LoadPromptsConfiguration(string configPath)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(configPath, optional: false, reloadOnChange: false)
            .Build();

        var config = new McpPromptsConfiguration();
        configuration.GetSection("McpPrompts").Bind(config);
        return config;
    }
}
