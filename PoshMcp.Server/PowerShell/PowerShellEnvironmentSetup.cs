using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Handles PowerShell environment setup including module installation and startup scripts
/// </summary>
public class PowerShellEnvironmentSetup
{
    private readonly ILogger<PowerShellEnvironmentSetup> _logger;

    public PowerShellEnvironmentSetup(ILogger<PowerShellEnvironmentSetup> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Applies environment configuration to a PowerShell instance
    /// </summary>
    public async Task<EnvironmentSetupResult> ApplyEnvironmentConfiguration(
        PSPowerShell powerShell,
        EnvironmentConfiguration config,
        CancellationToken cancellationToken = default)
    {
        var result = new EnvironmentSetupResult();
        var startTime = DateTime.UtcNow;

        try
        {
            _logger.LogInformation("Starting PowerShell environment setup");

            // Step 1: Configure PSModulePath with additional paths
            if (config.ModulePaths.Any())
            {
                await ConfigureModulePaths(powerShell, config.ModulePaths, result, cancellationToken);
            }

            // Step 2: Trust PSGallery if configured
            if (config.TrustPSGallery)
            {
                await TrustPSGallery(powerShell, result, cancellationToken);
            }

            // Step 3: Install modules from PowerShell Gallery or other repositories
            if (config.InstallModules.Any())
            {
                await InstallModules(powerShell, config.InstallModules, config, result, cancellationToken);
            }

            // Step 4: Import pre-installed modules
            if (config.ImportModules.Any())
            {
                await ImportModules(powerShell, config.ImportModules, config.AllowClobber, result, cancellationToken);
            }

            // Step 5: Execute startup script from file
            if (!string.IsNullOrWhiteSpace(config.StartupScriptPath))
            {
                await ExecuteStartupScriptFile(powerShell, config.StartupScriptPath, result, cancellationToken);
            }

            // Step 6: Execute inline startup script
            if (!string.IsNullOrWhiteSpace(config.StartupScript))
            {
                await ExecuteInlineStartupScript(powerShell, config.StartupScript, result, cancellationToken);
            }

            result.Success = !result.Errors.Any();
            result.Duration = DateTime.UtcNow - startTime;

            if (result.Success)
            {
                _logger.LogInformation(
                    "PowerShell environment setup completed successfully in {Duration}ms. " +
                    "Modules installed: {InstalledCount}, Modules imported: {ImportedCount}",
                    result.Duration.TotalMilliseconds,
                    result.InstalledModules.Count,
                    result.ImportedModules.Count);
            }
            else
            {
                _logger.LogWarning(
                    "PowerShell environment setup completed with errors in {Duration}ms. Errors: {ErrorCount}",
                    result.Duration.TotalMilliseconds,
                    result.Errors.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error during PowerShell environment setup");
            result.Success = false;
            result.Errors.Add($"Fatal error: {ex.Message}");
            result.Duration = DateTime.UtcNow - startTime;
        }

        return result;
    }

    private async Task ConfigureModulePaths(
        PSPowerShell powerShell,
        List<string> modulePaths,
        EnvironmentSetupResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Configuring PSModulePath with {Count} additional paths", modulePaths.Count);

            var validPaths = new List<string>();
            foreach (var path in modulePaths)
            {
                var expandedPath = Environment.ExpandEnvironmentVariables(path);
                if (Directory.Exists(expandedPath))
                {
                    validPaths.Add(expandedPath);
                    _logger.LogDebug("Added module path: {Path}", expandedPath);
                }
                else
                {
                    var warning = $"Module path does not exist: {expandedPath}";
                    _logger.LogWarning(warning);
                    result.Warnings.Add(warning);
                }
            }

            if (validPaths.Any())
            {
                var script = $"$env:PSModulePath = '{string.Join(Path.PathSeparator, validPaths)}' + '{Path.PathSeparator}' + $env:PSModulePath";
                powerShell.Commands.Clear();
                powerShell.AddScript(script);
                await Task.Run(() => powerShell.Invoke(), cancellationToken);

                if (powerShell.HadErrors)
                {
                    var errors = powerShell.Streams.Error.ReadAll();
                    result.Errors.Add($"Error configuring module paths: {string.Join("; ", errors.Select(e => e.ToString()))}");
                }

                result.ConfiguredModulePaths.AddRange(validPaths);
            }
        }
        catch (Exception ex)
        {
            var error = $"Error configuring module paths: {ex.Message}";
            _logger.LogError(ex, error);
            result.Errors.Add(error);
        }
    }

    private async Task TrustPSGallery(
        PSPowerShell powerShell,
        EnvironmentSetupResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Configuring PSGallery as trusted repository");

            powerShell.Commands.Clear();
            powerShell.AddScript(@"
                if (-not (Get-PSRepository -Name PSGallery -ErrorAction SilentlyContinue)) {
                    Register-PSRepository -Default -ErrorAction SilentlyContinue
                }
                Set-PSRepository -Name PSGallery -InstallationPolicy Trusted -ErrorAction Stop
            ");

            await Task.Run(() => powerShell.Invoke(), cancellationToken);

            if (powerShell.HadErrors)
            {
                var errors = powerShell.Streams.Error.ReadAll();
                var warning = $"Warning trusting PSGallery: {string.Join("; ", errors.Select(e => e.ToString()))}";
                _logger.LogWarning(warning);
                result.Warnings.Add(warning);
            }
            else
            {
                _logger.LogDebug("PSGallery configured as trusted");
            }
        }
        catch (Exception ex)
        {
            var warning = $"Failed to trust PSGallery: {ex.Message}";
            _logger.LogWarning(ex, warning);
            result.Warnings.Add(warning);
        }
    }

    private async Task InstallModules(
        PSPowerShell powerShell,
        List<ModuleInstallation> modules,
        EnvironmentConfiguration config,
        EnvironmentSetupResult result,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Installing {Count} modules", modules.Count);

        foreach (var module in modules)
        {
            try
            {
                _logger.LogInformation("Installing module: {Name} from {Repository}", module.Name, module.Repository);

                // Build the Install-Module command
                var scriptBuilder = new StringBuilder();
                scriptBuilder.AppendLine($"$ErrorActionPreference = 'Stop'");
                scriptBuilder.AppendLine($"try {{");

                // Check if module is already installed
                scriptBuilder.AppendLine($"    $existingModule = Get-Module -ListAvailable -Name '{module.Name}' -ErrorAction SilentlyContinue");
                scriptBuilder.AppendLine($"    if ($existingModule -and -not {module.Force.ToString().ToLower()}) {{");
                scriptBuilder.AppendLine($"        Write-Host 'Module {module.Name} is already installed. Skipping.'");
                scriptBuilder.AppendLine($"        return");
                scriptBuilder.AppendLine($"    }}");

                scriptBuilder.Append($"    Install-Module -Name '{module.Name}'");
                scriptBuilder.Append($" -Repository '{module.Repository}'");
                scriptBuilder.Append($" -Scope '{module.Scope}'");

                if (!string.IsNullOrWhiteSpace(module.Version))
                {
                    scriptBuilder.Append($" -RequiredVersion '{module.Version}'");
                }
                else if (!string.IsNullOrWhiteSpace(module.MinimumVersion))
                {
                    scriptBuilder.Append($" -MinimumVersion '{module.MinimumVersion}'");
                    if (!string.IsNullOrWhiteSpace(module.MaximumVersion))
                    {
                        scriptBuilder.Append($" -MaximumVersion '{module.MaximumVersion}'");
                    }
                }

                if (module.Force)
                {
                    scriptBuilder.Append(" -Force");
                }

                if (module.SkipPublisherCheck)
                {
                    scriptBuilder.Append(" -SkipPublisherCheck");
                }

                if (module.AllowPrerelease)
                {
                    scriptBuilder.Append(" -AllowPrerelease");
                }

                scriptBuilder.AppendLine(" -ErrorAction Stop");
                scriptBuilder.AppendLine($"    Write-Host 'Successfully installed module {module.Name}'");
                scriptBuilder.AppendLine($"}} catch {{");
                scriptBuilder.AppendLine($"    Write-Error \"Failed to install module {module.Name}: $_\"");
                scriptBuilder.AppendLine($"    throw");
                scriptBuilder.AppendLine($"}}");

                powerShell.Commands.Clear();
                powerShell.AddScript(scriptBuilder.ToString());

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(config.InstallTimeoutSeconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                await Task.Run(() => powerShell.Invoke(), linkedCts.Token);

                if (powerShell.HadErrors)
                {
                    var errors = powerShell.Streams.Error.ReadAll();
                    var errorMsg = $"Error installing module {module.Name}: {string.Join("; ", errors.Select(e => e.ToString()))}";
                    _logger.LogError(errorMsg);
                    result.Errors.Add(errorMsg);
                }
                else
                {
                    result.InstalledModules.Add(module.Name);
                    _logger.LogInformation("Successfully installed module: {Name}", module.Name);
                }
            }
            catch (OperationCanceledException)
            {
                var error = $"Module installation timeout for {module.Name} after {config.InstallTimeoutSeconds} seconds";
                _logger.LogError(error);
                result.Errors.Add(error);
            }
            catch (Exception ex)
            {
                var error = $"Error installing module {module.Name}: {ex.Message}";
                _logger.LogError(ex, error);
                result.Errors.Add(error);
            }
        }
    }

    private async Task ImportModules(
        PSPowerShell powerShell,
        List<string> modules,
        bool allowClobber,
        EnvironmentSetupResult result,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Importing {Count} modules", modules.Count);

        foreach (var moduleName in modules)
        {
            try
            {
                _logger.LogInformation("Importing module: {Name}", moduleName);

                powerShell.Commands.Clear();
                powerShell.AddCommand("Import-Module")
                    .AddParameter("Name", moduleName)
                    .AddParameter("ErrorAction", "Stop")
                    .AddParameter("PassThru");

                if (allowClobber)
                {
                    powerShell.AddParameter("Force");
                }

                await Task.Run(() => powerShell.Invoke(), cancellationToken);

                if (powerShell.HadErrors)
                {
                    var errors = powerShell.Streams.Error.ReadAll();
                    var error = $"Error importing module {moduleName}: {string.Join("; ", errors.Select(e => e.ToString()))}";
                    _logger.LogError(error);
                    result.Errors.Add(error);
                }
                else
                {
                    result.ImportedModules.Add(moduleName);
                    _logger.LogInformation("Successfully imported module: {Name}", moduleName);
                }
            }
            catch (Exception ex)
            {
                var error = $"Error importing module {moduleName}: {ex.Message}";
                _logger.LogError(ex, error);
                result.Errors.Add(error);
            }
        }
    }

    private async Task ExecuteStartupScriptFile(
        PSPowerShell powerShell,
        string scriptPath,
        EnvironmentSetupResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            var expandedPath = Environment.ExpandEnvironmentVariables(scriptPath);
            _logger.LogInformation("Executing startup script from file: {Path}", expandedPath);

            if (!File.Exists(expandedPath))
            {
                var error = $"Startup script file not found: {expandedPath}";
                _logger.LogError(error);
                result.Errors.Add(error);
                return;
            }

            var scriptContent = await File.ReadAllTextAsync(expandedPath, cancellationToken);

            powerShell.Commands.Clear();
            powerShell.AddScript(scriptContent);

            await Task.Run(() => powerShell.Invoke(), cancellationToken);

            if (powerShell.HadErrors)
            {
                var errors = powerShell.Streams.Error.ReadAll();
                var error = $"Error executing startup script file: {string.Join("; ", errors.Select(e => e.ToString()))}";
                _logger.LogError(error);
                result.Errors.Add(error);
            }
            else
            {
                result.StartupScriptExecuted = true;
                _logger.LogInformation("Successfully executed startup script from file");
            }
        }
        catch (Exception ex)
        {
            var error = $"Error executing startup script file: {ex.Message}";
            _logger.LogError(ex, error);
            result.Errors.Add(error);
        }
    }

    private async Task ExecuteInlineStartupScript(
        PSPowerShell powerShell,
        string script,
        EnvironmentSetupResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Executing inline startup script ({Length} characters)", script.Length);

            powerShell.Commands.Clear();
            powerShell.AddScript(script);

            await Task.Run(() => powerShell.Invoke(), cancellationToken);

            if (powerShell.HadErrors)
            {
                var errors = powerShell.Streams.Error.ReadAll();
                var error = $"Error executing inline startup script: {string.Join("; ", errors.Select(e => e.ToString()))}";
                _logger.LogError(error);
                result.Errors.Add(error);
            }
            else
            {
                result.InlineScriptExecuted = true;
                _logger.LogInformation("Successfully executed inline startup script");
            }
        }
        catch (Exception ex)
        {
            var error = $"Error executing inline startup script: {ex.Message}";
            _logger.LogError(ex, error);
            result.Errors.Add(error);
        }
    }
}

/// <summary>
/// Result of environment setup operation
/// </summary>
public class EnvironmentSetupResult
{
    public bool Success { get; set; }
    public TimeSpan Duration { get; set; }
    public List<string> InstalledModules { get; set; } = new();
    public List<string> ImportedModules { get; set; } = new();
    public List<string> ConfiguredModulePaths { get; set; } = new();
    public bool StartupScriptExecuted { get; set; }
    public bool InlineScriptExecuted { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
