using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.OutOfProcess;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace PoshMcp;

internal static class SettingsResolver
{
    internal const string CliSource = "cli";
    internal const string EnvSource = "env";
    internal const string DefaultSource = "default";
    internal const string CwdSource = "cwd";
    internal const string UserSource = "user";
    internal const string EmbeddedDefaultSource = "embedded-default";
    internal const string ConfigSource = "config";

    private const string TransportEnvVar = "POSHMCP_TRANSPORT";
    private const string ConfigurationEnvVar = "POSHMCP_CONFIGURATION";
    private const string McpPathEnvVar = "POSHMCP_MCP_PATH";
    private const string SessionModeEnvVar = "POSHMCP_SESSION_MODE";
    private const string RuntimeModeEnvVar = "POSHMCP_RUNTIME_MODE";
    private const string LogLevelEnvVar = "POSHMCP_LOG_LEVEL";
    private const string LogFileEnvVar = "POSHMCP_LOG_FILE";

    internal static LogLevel ParseLogLevel(string? logLevelText, string? environmentVariableName = null)
    {
        var resolvedLogLevelText = string.IsNullOrWhiteSpace(environmentVariableName)
            ? logLevelText
            : ResolveArgumentOrEnvironment(logLevelText, environmentVariableName!);

        if (string.IsNullOrWhiteSpace(resolvedLogLevelText))
        {
            return LogLevel.Information;
        }

        return resolvedLogLevelText.Trim().ToLowerInvariant() switch
        {
            "trace" => LogLevel.Trace,
            "debug" => LogLevel.Debug,
            "info" => LogLevel.Information,
            "information" => LogLevel.Information,
            "warn" => LogLevel.Warning,
            "warning" => LogLevel.Warning,
            "error" => LogLevel.Error,
            _ => LogLevel.Information
        };
    }

    internal static string? ResolveArgumentOrEnvironment(string? argumentValue, string environmentVariableName, string? defaultValue = null)
    {
        if (!string.IsNullOrWhiteSpace(argumentValue))
        {
            return argumentValue;
        }

        var environmentValue = Environment.GetEnvironmentVariable(environmentVariableName);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue;
        }

        return defaultValue;
    }

    internal static ResolvedSetting ResolveArgumentOrEnvironmentWithSource(string? argumentValue, string environmentVariableName, string? defaultValue = null)
    {
        if (!string.IsNullOrWhiteSpace(argumentValue))
        {
            return new ResolvedSetting(argumentValue, CliSource);
        }

        var environmentValue = Environment.GetEnvironmentVariable(environmentVariableName);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return new ResolvedSetting(environmentValue, EnvSource);
        }

        return new ResolvedSetting(defaultValue, DefaultSource);
    }

    /// <summary>
    /// Resolves the log file path with precedence: CLI option > POSHMCP_LOG_FILE env var > Logging:File:Path config > null (silent).
    /// </summary>
    internal static ResolvedSetting ResolveLogFilePath(string? cliValue, IConfiguration? config = null)
    {
        var resolved = ResolveArgumentOrEnvironmentWithSource(cliValue, LogFileEnvVar, null);
        if (!string.IsNullOrWhiteSpace(resolved.Value))
        {
            return resolved;
        }

        var configPath = config?["Logging:File:Path"];
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            return new ResolvedSetting(configPath, ConfigSource);
        }

        return new ResolvedSetting(null, DefaultSource);
    }

    internal static ResolvedSetting ResolveEffectiveLogLevel(string[] args, string? logLevelText)
    {
        if (HasOption(args, "--trace", "-t"))
        {
            return new ResolvedSetting(LogLevel.Trace.ToString(), CliSource);
        }

        if (HasOption(args, "--debug", "-d") || HasOption(args, "--verbose", "-v"))
        {
            return new ResolvedSetting(LogLevel.Debug.ToString(), CliSource);
        }

        var resolvedLogLevel = ResolveArgumentOrEnvironmentWithSource(logLevelText, LogLevelEnvVar);
        var parsedLogLevel = ParseLogLevel(resolvedLogLevel.Value);
        return new ResolvedSetting(parsedLogLevel.ToString(), resolvedLogLevel.Source);
    }

    internal static bool HasOption(string[] args, string longName, string shortName)
    {
        return args.Any(arg =>
            string.Equals(arg, longName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, shortName, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool ShouldPrintResolvedSettings(LogLevel logLevel)
    {
        return logLevel == LogLevel.Debug || logLevel == LogLevel.Trace;
    }

    internal static async Task<ResolvedCommandSettings> ResolveCommandSettingsAsync(
        string[] args,
        string? configPath,
        string? logLevelText,
        string? transport,
        string? sessionMode,
        string? runtimeMode,
        string? mcpPath)
    {
        var preferredConfigPath = ResolveArgumentOrEnvironmentWithSource(configPath, ConfigurationEnvVar);
        var resolvedConfigPath = await ResolveConfigurationPathWithSourceAsync(preferredConfigPath);
        var resolvedLogLevel = ResolveEffectiveLogLevel(args, logLevelText);
        var resolvedTransport = ResolveArgumentOrEnvironmentWithSource(transport, TransportEnvVar, "stdio");
        var normalizedTransport = new ResolvedSetting(NormalizeTransportValue(resolvedTransport.Value), resolvedTransport.Source);
        var resolvedSessionMode = ResolveArgumentOrEnvironmentWithSource(sessionMode, SessionModeEnvVar);
        var resolvedRuntimeMode = ResolveEffectiveRuntimeMode(resolvedConfigPath.Value, runtimeMode);
        var resolvedMcpPath = ResolveArgumentOrEnvironmentWithSource(mcpPath, McpPathEnvVar);

        return new ResolvedCommandSettings(
            resolvedConfigPath,
            resolvedConfigPath.Value ?? string.Empty,
            resolvedLogLevel,
            normalizedTransport,
            resolvedSessionMode,
            resolvedRuntimeMode,
            resolvedMcpPath);
    }

    internal static ResolvedSetting ResolveEffectiveRuntimeMode(string? configurationPath, string? runtimeModeOverride)
    {
        var overrideSetting = ResolveArgumentOrEnvironmentWithSource(runtimeModeOverride, RuntimeModeEnvVar);
        if (!string.IsNullOrWhiteSpace(overrideSetting.Value))
        {
            return new ResolvedSetting(NormalizeRuntimeModeValue(overrideSetting.Value), overrideSetting.Source);
        }

        return ResolveEffectiveRuntimeModeFromConfiguration(configurationPath);
    }

    internal static ResolvedSetting ResolveEffectiveRuntimeModeFromConfiguration(string? configurationPath)
    {
        if (!string.IsNullOrWhiteSpace(configurationPath) && File.Exists(configurationPath))
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(configurationPath, optional: false, reloadOnChange: false)
                .Build();

            var configuredRuntimeMode = configuration.GetSection("PowerShellConfiguration")[nameof(PowerShellConfiguration.RuntimeMode)];
            if (!string.IsNullOrWhiteSpace(configuredRuntimeMode))
            {
                return new ResolvedSetting(NormalizeRuntimeModeValue(configuredRuntimeMode), ConfigSource);
            }
        }

        return new ResolvedSetting(RuntimeMode.InProcess.ToString(), DefaultSource);
    }

    internal static ResolvedSetting ResolveEffectiveRuntimeModeFromConfiguration(string? configuredRuntimeMode, string? runtimeModeOverride)
    {
        var overrideSetting = ResolveArgumentOrEnvironmentWithSource(runtimeModeOverride, RuntimeModeEnvVar);
        if (!string.IsNullOrWhiteSpace(overrideSetting.Value))
        {
            return new ResolvedSetting(NormalizeRuntimeModeValue(overrideSetting.Value), overrideSetting.Source);
        }

        if (!string.IsNullOrWhiteSpace(configuredRuntimeMode))
        {
            return new ResolvedSetting(NormalizeRuntimeModeValue(configuredRuntimeMode), ConfigSource);
        }

        return new ResolvedSetting(RuntimeMode.InProcess.ToString(), DefaultSource);
    }

    internal static string NormalizeTransportValue(string? transport)
    {
        if (string.IsNullOrWhiteSpace(transport))
        {
            return "stdio";
        }

        return transport.Trim().ToLowerInvariant();
    }

    internal static TransportMode ResolveTransportMode(string? transport)
    {
        return NormalizeTransportValue(transport) switch
        {
            "stdio" => TransportMode.Stdio,
            "http" => TransportMode.Http,
            _ => TransportMode.Unsupported
        };
    }

    internal static string NormalizeRuntimeModeValue(string? runtimeMode)
    {
        if (string.IsNullOrWhiteSpace(runtimeMode))
        {
            return RuntimeMode.InProcess.ToString();
        }

        var normalized = runtimeMode.Trim().ToLowerInvariant().Replace("-", string.Empty).Replace("_", string.Empty);
        return normalized switch
        {
            "inprocess" => RuntimeMode.InProcess.ToString(),
            "outofprocess" => RuntimeMode.OutOfProcess.ToString(),
            _ => runtimeMode.Trim()
        };
    }

    internal static RuntimeMode ResolveRuntimeMode(string? runtimeMode)
    {
        var normalized = NormalizeRuntimeModeValue(runtimeMode);
        return normalized switch
        {
            nameof(RuntimeMode.InProcess) => RuntimeMode.InProcess,
            nameof(RuntimeMode.OutOfProcess) => RuntimeMode.OutOfProcess,
            _ => RuntimeMode.Unsupported
        };
    }

    internal static string? NormalizeMcpPath(string? mcpPath)
    {
        if (string.IsNullOrWhiteSpace(mcpPath))
        {
            return null;
        }

        var normalized = mcpPath.Trim();
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        return normalized;
    }

    internal static void PrintResolvedSettings(string commandName, ResolvedCommandSettings settings)
    {
        Console.Error.WriteLine($"{commandName} resolved settings:");
        Console.Error.WriteLine($"Configuration: {settings.FinalConfigPath} (source: {settings.ConfigPath.Source})");
        Console.Error.WriteLine($"Effective log level: {settings.LogLevel.Value} (source: {settings.LogLevel.Source})");
        Console.Error.WriteLine($"Effective transport: {settings.Transport.Value} (source: {settings.Transport.Source})");
        Console.Error.WriteLine($"Effective session mode: {settings.SessionMode.Value ?? "(not set)"} (source: {settings.SessionMode.Source})");
        Console.Error.WriteLine($"Effective runtime mode: {settings.RuntimeMode.Value} (source: {settings.RuntimeMode.Source})");
        Console.Error.WriteLine($"Effective MCP path: {settings.McpPath.Value ?? "(not set)"} (source: {settings.McpPath.Source})");
        Console.Error.WriteLine();
    }

    internal static async Task<ResolvedSetting> ResolveConfigurationPathWithSourceAsync(ResolvedSetting preferredConfigPath)
    {
        if (!string.IsNullOrWhiteSpace(preferredConfigPath.Value))
        {
            var absoluteConfigPath = Path.GetFullPath(preferredConfigPath.Value);
            if (!File.Exists(absoluteConfigPath))
            {
                throw new FileNotFoundException($"Configuration file not found: {absoluteConfigPath}");
            }

            await UpgradeConfigWithMissingDefaultsAsync(absoluteConfigPath);
            return new ResolvedSetting(absoluteConfigPath, preferredConfigPath.Source);
        }

        var currentDirectoryConfigPath = Path.GetFullPath("appsettings.json");
        if (File.Exists(currentDirectoryConfigPath))
        {
            await UpgradeConfigWithMissingDefaultsAsync(currentDirectoryConfigPath);
            return new ResolvedSetting(currentDirectoryConfigPath, CwdSource);
        }

        var userConfigPath = GetUserConfigPath();
        if (File.Exists(userConfigPath))
        {
            await UpgradeConfigWithMissingDefaultsAsync(userConfigPath);
            return new ResolvedSetting(userConfigPath, UserSource);
        }

        await InstallEmbeddedDefaultConfigToUserLocationAsync(userConfigPath);
        return new ResolvedSetting(userConfigPath, EmbeddedDefaultSource);
    }

    internal static string GetUserConfigPath()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appDataPath, "PoshMcp", "appsettings.json");
    }

    internal static async Task InstallEmbeddedDefaultConfigToUserLocationAsync(string userConfigPath)
    {
        var directory = Path.GetDirectoryName(userConfigPath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var defaultConfigJson = LoadEmbeddedDefaultConfig();
        await File.WriteAllTextAsync(userConfigPath, defaultConfigJson);
    }

    internal static async Task UpgradeConfigWithMissingDefaultsAsync(string configPath)
    {
        var defaultConfigJson = LoadEmbeddedDefaultConfig();
        var existingConfigJson = await File.ReadAllTextAsync(configPath);

        var parseOptions = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        var defaultRoot = JsonNode.Parse(defaultConfigJson, documentOptions: parseOptions)?.AsObject()
            ?? throw new InvalidOperationException("Embedded default configuration must be a JSON object.");
        var existingRoot = JsonNode.Parse(existingConfigJson, documentOptions: parseOptions)?.AsObject()
            ?? throw new InvalidOperationException($"Configuration file '{configPath}' must be a JSON object.");

        var changed = MergeMissingProperties(defaultRoot, existingRoot);
        if (!changed)
        {
            return;
        }

        var updatedConfigJson = existingRoot.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(configPath, updatedConfigJson + Environment.NewLine);
    }

    internal static bool MergeMissingProperties(JsonObject defaultObject, JsonObject targetObject)
    {
        var changed = false;

        foreach (var defaultProperty in defaultObject)
        {
            if (!targetObject.TryGetPropertyValue(defaultProperty.Key, out var existingValue))
            {
                targetObject[defaultProperty.Key] = defaultProperty.Value?.DeepClone();
                changed = true;
                continue;
            }

            if (defaultProperty.Value is JsonObject defaultChildObject && existingValue is JsonObject existingChildObject)
            {
                changed |= MergeMissingProperties(defaultChildObject, existingChildObject);
            }
        }

        return changed;
    }

    internal static string LoadEmbeddedDefaultConfig()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("default.appsettings.json", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            throw new InvalidOperationException("Embedded default configuration resource was not found.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Unable to open embedded configuration resource '{resourceName}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

internal sealed record ResolvedSetting(string? Value, string Source);

internal sealed record ResolvedCommandSettings(
    ResolvedSetting ConfigPath,
    string FinalConfigPath,
    ResolvedSetting LogLevel,
    ResolvedSetting Transport,
    ResolvedSetting SessionMode,
    ResolvedSetting RuntimeMode,
    ResolvedSetting McpPath);

internal enum TransportMode
{
    Stdio,
    Http,
    Unsupported
}
