using System;
using System.Collections.Generic;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.PowerShell;

namespace PoshMcp;

/// <summary>
/// Command handler methods for CLI commands. Each handler is responsible for executing a specific command
/// and managing its lifecycle, error handling, and exit codes.
/// </summary>
public static class CommandHandlers
{
    public static async Task RunToolEvaluationAsync(LogLevel logLevel)
    {
        PrintToolEvaluationHeader();
        using var loggerFactory = LoggingHelpers.CreateLoggerFactory(logLevel);
        var logger = loggerFactory.CreateLogger("ToolEvaluation");

        try
        {
            LogEvaluationStart(logger, logLevel);
            var finalConfigPath = await DetermineConfigurationPath(logger);
            var config = ConfigurationLoader.LoadPowerShellConfiguration(finalConfigPath, logger);
            var tools = await McpToolSetupService.DiscoverToolsForCliAsync(config, loggerFactory, logger, finalConfigPath);
            McpToolSetupService.ReportToolDiscoveryResults(tools, logger);
        }
        catch (Exception ex)
        {
            McpToolSetupService.HandleToolEvaluationError(ex, logger);
        }
    }

    public static async Task RunListToolsAsync(LogLevel logLevel, string finalConfigPath, string? runtimeModeOverride, string format)
    {
        using var loggerFactory = LoggingHelpers.CreateLoggerFactory(logLevel);
        var logger = loggerFactory.CreateLogger("ListTools");

        var config = ConfigurationLoader.LoadPowerShellConfiguration(finalConfigPath, logger, runtimeModeOverride);
        var tools = await McpToolSetupService.DiscoverToolsAsync(config, loggerFactory, logger, finalConfigPath);

        if (format == "json")
        {
            var payload = new
            {
                configurationPath = ConfigurationHelpers.DescribeConfigurationPath(finalConfigPath),
                runtimeMode = config.RuntimeMode.ToString(),
                toolCount = tools.Count,
                commandNames = config.GetEffectiveCommandNames(),
                generatedAtUtc = DateTime.UtcNow
            };
            Console.WriteLine(JsonSerializer.Serialize(payload));
            return;
        }

        Console.WriteLine($"Configuration: {ConfigurationHelpers.DescribeConfigurationPath(finalConfigPath)}");
        Console.WriteLine($"Runtime mode: {config.RuntimeMode}");
        Console.WriteLine($"Discovered tools: {tools.Count}");
        Console.WriteLine("Configured command names:");
        foreach (var commandName in config.GetEffectiveCommandNames())
        {
            Console.WriteLine($"- {commandName}");
        }
    }

    public static async Task RunValidateConfigAsync(LogLevel logLevel, string finalConfigPath, string? runtimeModeOverride, string format)
    {
        using var loggerFactory = LoggingHelpers.CreateLoggerFactory(logLevel);
        var logger = loggerFactory.CreateLogger("ValidateConfig");

        var config = ConfigurationLoader.LoadPowerShellConfiguration(finalConfigPath, logger, runtimeModeOverride);
        var tools = await McpToolSetupService.DiscoverToolsAsync(config, loggerFactory, logger, finalConfigPath);
        var (resourcesDiag, promptsDiag) = ConfigurationLoader.TryValidateResourcesAndPrompts(finalConfigPath);

        var hasErrors = resourcesDiag.Errors.Count > 0 || promptsDiag.Errors.Count > 0;

        if (format == "json")
        {
            var payload = new
            {
                valid = !hasErrors,
                configurationPath = ConfigurationHelpers.DescribeConfigurationPath(finalConfigPath),
                runtimeMode = config.RuntimeMode.ToString(),
                commandCount = config.GetEffectiveCommandNames().Count,
                moduleCount = config.Modules.Count,
                toolCount = tools.Count,
                resources = new
                {
                    configured = resourcesDiag.Configured,
                    valid = resourcesDiag.Valid,
                    errors = resourcesDiag.Errors,
                    warnings = resourcesDiag.Warnings
                },
                prompts = new
                {
                    configured = promptsDiag.Configured,
                    valid = promptsDiag.Valid,
                    errors = promptsDiag.Errors,
                    warnings = promptsDiag.Warnings
                }
            };
            Console.WriteLine(JsonSerializer.Serialize(payload));
            return;
        }

        if (hasErrors)
        {
            Console.WriteLine("Configuration validation failed.");
        }
        else
        {
            Console.WriteLine("Configuration validation succeeded.");
        }
        Console.WriteLine($"Configuration: {ConfigurationHelpers.DescribeConfigurationPath(finalConfigPath)}");
        Console.WriteLine($"Runtime mode: {config.RuntimeMode}");
        Console.WriteLine($"Commands: {config.GetEffectiveCommandNames().Count} | Modules: {config.Modules.Count} | Tools: {tools.Count}");
        Console.WriteLine($"Resources configured: {resourcesDiag.Configured} | valid: {resourcesDiag.Valid}");
        foreach (var error in resourcesDiag.Errors)
            Console.WriteLine($"  ✖ {error}");
        foreach (var warning in resourcesDiag.Warnings)
            Console.WriteLine($"  ⚠ {warning}");
        Console.WriteLine($"Prompts configured: {promptsDiag.Configured} | valid: {promptsDiag.Valid}");
        foreach (var error in promptsDiag.Errors)
            Console.WriteLine($"  ✖ {error}");
        foreach (var warning in promptsDiag.Warnings)
            Console.WriteLine($"  ⚠ {warning}");
    }

    public static void RunPSModulePathCommand(LogLevel logLevel)
    {
        Console.Error.WriteLine("=== PowerShell MCP Server - PSModulePath Report ===");
        Console.Error.WriteLine();

        using var loggerFactory = LoggingHelpers.CreateLoggerFactory(logLevel);
        var logger = loggerFactory.CreateLogger("PSModulePath");

        try
        {
            logger.LogInformation("Starting PowerShell runspace to check PSModulePath");

            using var runspace = new IsolatedPowerShellRunspace();

            var psModulePath = runspace.ExecuteThreadSafe(ps =>
            {
                ps.Commands.Clear();
                ps.AddScript("$env:PSModulePath");

                var results = ps.Invoke();

                if (ps.HadErrors)
                {
                    var errors = string.Join(Environment.NewLine, ps.Streams.Error);
                    throw new InvalidOperationException($"PowerShell execution failed: {errors}");
                }

                return results.Count > 0 ? results[0]?.ToString() ?? string.Empty : string.Empty;
            });

            Console.WriteLine("PSModulePath:");
            Console.WriteLine(new string('=', 50));

            if (!string.IsNullOrEmpty(psModulePath))
            {
                // Split the path and display each entry on a separate line for better readability
                var paths = psModulePath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < paths.Length; i++)
                {
                    Console.WriteLine($"{i + 1:D2}. {paths[i]}");
                }

                Console.WriteLine();
                Console.WriteLine($"Total module paths: {paths.Length}");
            }
            else
            {
                Console.WriteLine("(empty or undefined)");
            }

            Console.WriteLine(new string('=', 50));
            logger.LogInformation("PSModulePath report completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while checking PSModulePath: {ErrorMessage}", ex.Message);
            Console.Error.WriteLine($"Error: {ex.Message}");
        }
    }

    public static async Task RunCreateConfigAsync(bool force, string format)
    {
        var outputFormat = ConfigurationFileManager.NormalizeFormat(format);
        var targetPath = Path.GetFullPath("appsettings.json");

        try
        {
            var created = await ConfigurationFileManager.CreateDefaultConfigInCurrentDirectoryAsync(targetPath, force);

            if (outputFormat == "json")
            {
                var payload = new
                {
                    success = true,
                    configurationPath = targetPath,
                    overwritten = created.WasOverwritten
                };
                Console.WriteLine(JsonSerializer.Serialize(payload));
            }
            else
            {
                Console.WriteLine(created.WasOverwritten
                    ? $"Overwrote default configuration: {targetPath}"
                    : $"Created default configuration: {targetPath}");
            }

            Environment.ExitCode = ExitCodes.Success;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Configuration error: {ex.Message}");
            Environment.ExitCode = ExitCodes.ConfigError;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Runtime error: {ex.Message}");
            Environment.ExitCode = ExitCodes.RuntimeError;
        }
    }

    public static async Task RunUpdateConfigAsync(string[] args, InvocationContext context)
    {
        var configPath = context.ParseResult.GetValueForOption(CliDefinition.ConfigOption);
        var logLevelText = context.ParseResult.GetValueForOption(CliDefinition.LogLevelOption);
        var format = context.ParseResult.GetValueForOption(CliDefinition.FormatOption);
        var addCommands = context.ParseResult.GetValueForOption(CliDefinition.AddCommandOption);
        var removeCommands = context.ParseResult.GetValueForOption(CliDefinition.RemoveCommandOption);
        var addModules = context.ParseResult.GetValueForOption(CliDefinition.AddModuleOption);
        var removeModules = context.ParseResult.GetValueForOption(CliDefinition.RemoveModuleOption);
        var addIncludePatterns = context.ParseResult.GetValueForOption(CliDefinition.AddIncludePatternOption);
        var removeIncludePatterns = context.ParseResult.GetValueForOption(CliDefinition.RemoveIncludePatternOption);
        var addExcludePatterns = context.ParseResult.GetValueForOption(CliDefinition.AddExcludePatternOption);
        var removeExcludePatterns = context.ParseResult.GetValueForOption(CliDefinition.RemoveExcludePatternOption);
        var enableDynamicReloadTools = context.ParseResult.GetValueForOption(CliDefinition.EnableDynamicReloadToolsOption);
        var enableConfigurationTroubleshootingTool = context.ParseResult.GetValueForOption(CliDefinition.EnableConfigurationTroubleshootingToolOption);
        var enableResultCaching = context.ParseResult.GetValueForOption(CliDefinition.EnableResultCachingOption);
        var useDefaultDisplayProperties = context.ParseResult.GetValueForOption(CliDefinition.UseDefaultDisplayPropertiesOption);
        var setAuthEnabled = context.ParseResult.GetValueForOption(CliDefinition.SetAuthEnabledOption);
        var runtimeMode = context.ParseResult.GetValueForOption(CliDefinition.RuntimeModeOption);
        var nonInteractive = context.ParseResult.GetValueForOption(CliDefinition.NonInteractiveOption);

        var resolvedSettings = await SettingsResolver.ResolveCommandSettingsAsync(args,
            configPath, logLevelText, null, null, null, null);
        var parsedLogLevel = SettingsResolver.ParseLogLevel(resolvedSettings.LogLevel.Value);
        var outputFormat = ConfigurationFileManager.NormalizeFormat(format);

        try
        {
            if (SettingsResolver.ShouldPrintResolvedSettings(parsedLogLevel))
            {
                SettingsResolver.PrintResolvedSettings("update-config", resolvedSettings);
            }

            var updateRequest = new ConfigUpdateRequest(
                addCommands ?? Array.Empty<string>(),
                removeCommands ?? Array.Empty<string>(),
                addCommands ?? Array.Empty<string>(),
                removeCommands ?? Array.Empty<string>(),
                addModules ?? Array.Empty<string>(),
                removeModules ?? Array.Empty<string>(),
                addIncludePatterns ?? Array.Empty<string>(),
                removeIncludePatterns ?? Array.Empty<string>(),
                addExcludePatterns ?? Array.Empty<string>(),
                removeExcludePatterns ?? Array.Empty<string>(),
                ConfigurationFileManager.TryParseRequiredBoolean(enableDynamicReloadTools),
                ConfigurationFileManager.TryParseRequiredBoolean(enableConfigurationTroubleshootingTool),
                ConfigurationFileManager.TryParseRequiredBoolean(enableResultCaching),
                ConfigurationFileManager.TryParseRequiredBoolean(useDefaultDisplayProperties),
                ConfigurationFileManager.TryParseRequiredBoolean(setAuthEnabled),
                ConfigurationFileManager.NormalizeRuntimeMode(runtimeMode),
                nonInteractive);

            var result = await ConfigurationFileManager.UpdateConfigurationFileAsync(resolvedSettings.FinalConfigPath, updateRequest);

            if (outputFormat == "json")
            {
                var payload = new
                {
                    success = true,
                    configurationPath = result.ConfigurationPath,
                    changed = result.Changed,
                    addedFunctions = result.AddedFunctions,
                    removedFunctions = result.RemovedFunctions,
                    addedCommands = result.AddedCommands,
                    removedCommands = result.RemovedCommands,
                    advancedPromptedCommandCount = result.AdvancedPromptedFunctionCount,
                    advancedPromptedFunctionCount = result.AdvancedPromptedFunctionCount,
                    settingsChanged = result.SettingsChanged
                };
                Console.WriteLine(JsonSerializer.Serialize(payload));
            }
            else
            {
                Console.WriteLine(result.Changed
                    ? $"Updated configuration: {result.ConfigurationPath}"
                    : $"No changes applied to configuration: {result.ConfigurationPath}");
                Console.WriteLine($"Added commands: {result.AddedCommands} | Removed commands: {result.RemovedCommands}");
                if (result.SettingsChanged > 0)
                {
                    Console.WriteLine($"Settings changed: {result.SettingsChanged}");
                }
                if (result.AdvancedPromptedFunctionCount > 0)
                {
                    Console.WriteLine($"Advanced prompts completed for {result.AdvancedPromptedFunctionCount} command(s).");
                }
            }

            Environment.ExitCode = ExitCodes.Success;
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"Configuration error: {ex.Message}");
            Environment.ExitCode = ExitCodes.ConfigError;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Configuration error: {ex.Message}");
            Environment.ExitCode = ExitCodes.ConfigError;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Runtime error: {ex.Message}");
            Environment.ExitCode = ExitCodes.RuntimeError;
        }
    }

    public static void RunBuildCommand(InvocationContext context)
    {
        try
        {
            var modules = context.ParseResult.GetValueForOption(CliDefinition.BuildModulesOption);
            var type = context.ParseResult.GetValueForOption(CliDefinition.BuildTypeOption);
            var tag = context.ParseResult.GetValueForOption(CliDefinition.BuildTagOption);
            var dockerFile = context.ParseResult.GetValueForOption(CliDefinition.BuildDockerFileOption);
            var sourceImage = context.ParseResult.GetValueForOption(CliDefinition.BuildSourceImageOption);
            var sourceTag = context.ParseResult.GetValueForOption(CliDefinition.BuildSourceTagOption);
            var generateDockerfile = context.ParseResult.GetValueForOption(CliDefinition.BuildGenerateDockerfileOption);
            var dockerfileOutput = context.ParseResult.GetValueForOption(CliDefinition.BuildDockerfileOutputOption);
            var appSettings = context.ParseResult.GetValueForOption(CliDefinition.BuildAppSettingsOption);

            var buildType = string.IsNullOrWhiteSpace(type)
                ? "custom"
                : type.ToLowerInvariant();
            var imageTag = string.IsNullOrWhiteSpace(tag) ? "poshmcp:latest" : tag;

            if (buildType != "base" && buildType != "custom")
            {
                Console.Error.WriteLine("Error: --type must be 'custom' or 'base'");
                Environment.ExitCode = ExitCodes.ConfigError;
                return;
            }

            var imageFile = string.IsNullOrWhiteSpace(dockerFile)
                ? (buildType == "base" ? "Dockerfile" : "examples/Dockerfile.user")
                : dockerFile;

            // When the imageFile maps to an embedded resource or we're generating a Dockerfile,
            // the file does not need to exist on disk. For custom Dockerfiles it must.
            var hasEmbeddedDockerfile = DockerRunner.GetEmbeddedDockerfileName(imageFile) != null;
            if (!generateDockerfile && !hasEmbeddedDockerfile && !File.Exists(imageFile))
            {
                Console.Error.WriteLine($"Error: Dockerfile not found at {imageFile}");
                Environment.ExitCode = ExitCodes.ConfigError;
                return;
            }

            if (!string.IsNullOrWhiteSpace(appSettings) && !File.Exists(appSettings))
            {
                Console.Error.WriteLine($"Error: appsettings file not found: {appSettings}");
                Environment.ExitCode = ExitCodes.ConfigError;
                return;
            }

            var resolvedSourceImage = DockerRunner.ResolveSourceImageReference(sourceImage, sourceTag);

            if (generateDockerfile)
            {
                var outputPath = string.IsNullOrWhiteSpace(dockerfileOutput)
                    ? "./Dockerfile.generated"
                    : dockerfileOutput;
                var writtenPath = DockerRunner.GenerateDockerfile(
                    imageFile,
                    outputPath,
                    imageTag,
                    modules,
                    buildType == "custom" ? resolvedSourceImage : null,
                    appSettings);
                Console.WriteLine($"Dockerfile written to: {writtenPath}");
                if (!string.IsNullOrWhiteSpace(appSettings))
                {
                    Console.WriteLine($"Note: Copy {appSettings} to poshmcp-appsettings.json in your build context before building.");
                }
                Console.WriteLine("To build manually, run:");
                Console.WriteLine($"  docker build -f {writtenPath} -t {imageTag} .");
                Environment.ExitCode = ExitCodes.Success;
                return;
            }

            var dockerPath = DockerRunner.DetectDockerCommand();

            if (dockerPath == null)
            {
                Console.Error.WriteLine("Error: Docker or Podman is not installed or not available in PATH.");
                Console.Error.WriteLine("Please install Docker (https://www.docker.com) or Podman (https://podman.io)");
                Environment.ExitCode = ExitCodes.StartupError;
                return;
            }

            Console.WriteLine($"Building {buildType} PoshMcp image: {imageTag}");

            if (buildType == "custom")
            {
                Console.WriteLine($"Using source image: {resolvedSourceImage}");
            }

            string? stagedAppSettings = null;
            string? tempDockerfilePath = null;
            var effectiveImageFile = imageFile;

            if (!string.IsNullOrWhiteSpace(appSettings) || hasEmbeddedDockerfile)
            {
                stagedAppSettings = !string.IsNullOrWhiteSpace(appSettings)
                    ? DockerRunner.PrepareAppSettingsForBuild(appSettings, ".")
                    : null;
                tempDockerfilePath = ".poshmcp-build.dockerfile";
                DockerRunner.GenerateDockerfile(
                    imageFile,
                    tempDockerfilePath,
                    imageTag,
                    modules,
                    buildType == "custom" ? resolvedSourceImage : null,
                    appSettings);
                effectiveImageFile = tempDockerfilePath;
            }

            try
            {
                var buildArgs = DockerRunner.BuildDockerBuildArgs(
                    effectiveImageFile,
                    imageTag,
                    modules,
                    buildType == "custom" ? resolvedSourceImage : null);

                var result = DockerRunner.ExecuteDockerCommand(dockerPath, buildArgs);
                if (result != ExitCodes.Success)
                {
                    Environment.ExitCode = result;
                    return;
                }

                Console.WriteLine($"Successfully built image: {imageTag}");
                Environment.ExitCode = ExitCodes.Success;
            }
            finally
            {
                DockerRunner.CleanupTempFile(tempDockerfilePath);
                DockerRunner.CleanupTempFile(stagedAppSettings);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Build error: {ex.Message}");
            Environment.ExitCode = ExitCodes.RuntimeError;
        }
    }

    public static void RunRunCommand(string? mode, int? port, string? tag, string? config, string[]? volumes, bool interactive)
    {
        try
        {
            var transportMode = string.IsNullOrWhiteSpace(mode) ? "http" : mode.ToLowerInvariant();
            var portNumber = port ?? 8080;
            var imageTag = string.IsNullOrWhiteSpace(tag) ? "poshmcp:latest" : tag;
            var dockerPath = DockerRunner.DetectDockerCommand();

            if (dockerPath == null)
            {
                Console.Error.WriteLine("Error: Docker or Podman is not installed or not available in PATH.");
                Console.Error.WriteLine("Please install Docker (https://www.docker.com) or Podman (https://podman.io)");
                Environment.ExitCode = ExitCodes.StartupError;
                return;
            }

            if (transportMode != "http" && transportMode != "stdio")
            {
                Console.Error.WriteLine("Error: --mode must be 'http' or 'stdio'");
                Environment.ExitCode = ExitCodes.ConfigError;
                return;
            }

            var runArgs = "run -d";

            if (interactive)
            {
                runArgs = "run -it";
            }

            // Set transport mode environment variable
            var envVar = transportMode == "http" ? "http" : "stdio";
            runArgs += $" -e POSHMCP_TRANSPORT={envVar}";

            // Expose port for HTTP mode
            if (transportMode == "http")
            {
                runArgs += $" -p {portNumber}:8080";
            }

            // Mount config file if provided
            if (!string.IsNullOrWhiteSpace(config))
            {
                var configPath = Path.GetFullPath(config);
                if (!File.Exists(configPath))
                {
                    Console.Error.WriteLine($"Error: Config file not found: {configPath}");
                    Environment.ExitCode = ExitCodes.ConfigError;
                    return;
                }

                runArgs += $" -v {configPath}:/app/appsettings.json:ro";
            }

            // Add volume mounts if provided
            if (volumes != null && volumes.Length > 0)
            {
                foreach (var volume in volumes)
                {
                    runArgs += $" -v {volume}";
                }
            }

            // Add image tag
            runArgs += $" {imageTag}";

            if (interactive)
            {
                Console.WriteLine($"Starting PoshMcp container in interactive mode: {imageTag}");
                var result = DockerRunner.ExecuteDockerCommand(dockerPath, runArgs, interactive: true);
                Environment.ExitCode = result;
            }
            else
            {
                Console.WriteLine($"Starting PoshMcp container in {transportMode} mode on port {portNumber}...");
                var result = DockerRunner.ExecuteDockerCommand(dockerPath, runArgs);
                if (result == ExitCodes.Success)
                {
                    Console.WriteLine($"Container started successfully ({transportMode} mode on port {portNumber})");
                }
                Environment.ExitCode = result;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Run error: {ex.Message}");
            Environment.ExitCode = ExitCodes.RuntimeError;
        }
    }

    public static async Task RunScaffoldCommand(string? projectPath, bool force, string format)
    {
        var outputFormat = ConfigurationFileManager.NormalizeFormat(format);

        try
        {
            var targetProjectPath = string.IsNullOrWhiteSpace(projectPath)
                ? Directory.GetCurrentDirectory()
                : projectPath;

            var result = await InfrastructureScaffolder.ScaffoldAzureInfrastructureAsync(targetProjectPath, force);

            if (outputFormat == "json")
            {
                var payload = new
                {
                    success = true,
                    projectPath = result.ProjectPath,
                    relativePath = result.RelativeInfraPath,
                    filesWritten = result.FilesWritten,
                    filesOverwritten = result.FilesOverwritten,
                    force = result.Force
                };
                Console.WriteLine(JsonSerializer.Serialize(payload));
            }
            else
            {
                Console.WriteLine($"Scaffolded {result.FilesWritten} infra file(s) to {Path.Combine(result.ProjectPath, result.RelativeInfraPath.Replace('/', Path.DirectorySeparatorChar))}");
                if (result.FilesOverwritten > 0)
                {
                    Console.WriteLine($"Overwritten files: {result.FilesOverwritten}");
                }
            }

            Environment.ExitCode = ExitCodes.Success;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Scaffold error: {ex.Message}");
            Environment.ExitCode = ExitCodes.ConfigError;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Scaffold error: {ex.Message}");
            Environment.ExitCode = ExitCodes.RuntimeError;
        }
    }

    // Helper methods for command handlers

    private static void PrintToolEvaluationHeader()
    {
        Console.Error.WriteLine("=== PowerShell MCP Server - Tool Evaluation Mode ===");
        Console.Error.WriteLine();
    }

    private static void LogEvaluationStart(ILogger logger, LogLevel logLevel)
    {
        logger.LogInformation("Starting tool evaluation mode");
        logger.LogDebug($"Log level set to: {logLevel}");
    }

    private static async Task<string> DetermineConfigurationPath(ILogger logger)
    {
        var appDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Directory.GetCurrentDirectory();
        var configPath = Path.Combine(appDirectory, "appsettings.json");

        string finalConfigPath = await ResolveConfigurationPath(configPath);
        logger.LogInformation("Loading configuration from: {ConfigurationPath}", ConfigurationHelpers.DescribeConfigurationPath(finalConfigPath));
        return finalConfigPath;
    }

    internal static async Task<string> ResolveConfigurationPath(string configPath)
    {
        var preferredConfigPath = File.Exists(configPath)
            ? new ResolvedSetting(configPath, SettingsResolver.CliSource)
            : new ResolvedSetting(null, SettingsResolver.DefaultSource);
        var resolvedConfigPath = await SettingsResolver.ResolveConfigurationPathWithSourceAsync(preferredConfigPath);
        return resolvedConfigPath.Value ?? string.Empty;
    }

}
