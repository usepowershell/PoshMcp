using System;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Utility class for loading PowerShell initialization scripts from configuration
/// </summary>
public static class InitializationScriptLoader
{
    private static readonly object _cacheLock = new object();
    private static string? _cachedScript;
    private static string? _cachedScriptPath;

    /// <summary>
    /// Loads the initialization script specified in configuration, with caching
    /// </summary>
    /// <param name="config">PowerShell configuration containing the script path</param>
    /// <param name="logger">Logger for diagnostic information</param>
    /// <returns>The initialization script content, or default script if none specified</returns>
    public static string LoadInitializationScript(PowerShellConfiguration config, ILogger logger)
    {
        if (config == null)
        {
            logger.LogWarning("PowerShellConfiguration is null, using default initialization script");
            return GetDefaultInitializationScript();
        }

        // If no script path specified, use default
        if (string.IsNullOrWhiteSpace(config.InitializationScriptPath))
        {
            logger.LogDebug("No InitializationScriptPath specified, using default initialization script");
            return GetDefaultInitializationScript();
        }

        // Check cache with thread-safety
        lock (_cacheLock)
        {
            if (_cachedScriptPath == config.InitializationScriptPath && _cachedScript != null)
            {
                logger.LogTrace($"Using cached initialization script from: {config.InitializationScriptPath}");
                return _cachedScript;
            }
        }

        try
        {
            var resolvedPath = ResolveScriptPath(config.InitializationScriptPath);
            logger.LogInformation($"Loading initialization script from: {resolvedPath}");

            if (!File.Exists(resolvedPath))
            {
                logger.LogWarning($"Initialization script file not found: {resolvedPath}, using default script");
                return GetDefaultInitializationScript();
            }

            var scriptContent = File.ReadAllText(resolvedPath);

            if (string.IsNullOrWhiteSpace(scriptContent))
            {
                logger.LogWarning($"Initialization script is empty: {resolvedPath}, using default script");
                return GetDefaultInitializationScript();
            }

            // Cache the script with thread-safety
            lock (_cacheLock)
            {
                _cachedScript = scriptContent;
                _cachedScriptPath = config.InitializationScriptPath;
            }

            logger.LogInformation($"Successfully loaded initialization script ({scriptContent.Length} characters) from: {resolvedPath}");
            return scriptContent;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error loading initialization script from {config.InitializationScriptPath}: {ex.Message}");
            logger.LogWarning("Falling back to default initialization script");
            return GetDefaultInitializationScript();
        }
    }

    /// <summary>
    /// Resolves a script path to an absolute path
    /// </summary>
    /// <param name="scriptPath">The script path from configuration (absolute or relative)</param>
    /// <returns>Resolved absolute path</returns>
    public static string ResolveScriptPath(string scriptPath)
    {
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            throw new ArgumentException("Script path cannot be null or whitespace", nameof(scriptPath));
        }

        // If already absolute, return as-is
        if (Path.IsPathRooted(scriptPath))
        {
            return scriptPath;
        }

        // Resolve relative to application directory
        var appDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                          ?? Directory.GetCurrentDirectory();

        return Path.GetFullPath(Path.Combine(appDirectory, scriptPath));
    }

    /// <summary>
    /// Gets the default initialization script used when no custom script is specified
    /// </summary>
    /// <returns>Default PowerShell initialization script</returns>
    public static string GetDefaultInitializationScript()
    {
        return @"
            # Set up some useful variables
            $McpServerStartTime = Get-Date
            $McpServerVersion = '1.0.0'

            # Create a function to get session info
            function Get-McpSessionInfo {
                return @{
                    StartTime = $McpServerStartTime
                    Version = $McpServerVersion
                    Location = Get-Location
                    Variables = (Get-Variable | Measure-Object).Count
                    Functions = (Get-ChildItem Function: | Measure-Object).Count
                    Modules = (Get-Module | Measure-Object).Count
                }
            }

            Write-Host 'MCP PowerShell session initialized' -ForegroundColor Green
        ";
    }

    /// <summary>
    /// Clears the cached script, forcing a reload on next access
    /// </summary>
    public static void ClearCache()
    {
        lock (_cacheLock)
        {
            _cachedScript = null;
            _cachedScriptPath = null;
        }
    }
}
