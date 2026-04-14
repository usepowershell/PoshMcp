using System.Collections.Generic;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Configuration for PowerShell environment customization
/// </summary>
public class EnvironmentConfiguration
{
    /// <summary>
    /// Modules to install from PowerShell Gallery at startup
    /// </summary>
    public List<ModuleInstallation> InstallModules { get; set; } = new();

    /// <summary>
    /// Modules to import that are already available (built-in or pre-installed)
    /// </summary>
    public List<string> ImportModules { get; set; } = new();

    /// <summary>
    /// Local module paths to add to PSModulePath
    /// </summary>
    public List<string> ModulePaths { get; set; } = new();

    /// <summary>
    /// Startup script to execute during initialization
    /// Can be inline PowerShell code
    /// </summary>
    public string? StartupScript { get; set; }

    /// <summary>
    /// Path to a PowerShell script file to execute at startup
    /// </summary>
    public string? StartupScriptPath { get; set; }

    /// <summary>
    /// Whether to trust PowerShell Gallery repository automatically.
    /// Defaults to false.
    /// </summary>
    public bool TrustPSGallery { get; set; } = false;

    /// <summary>
    /// Whether to skip module installation if already installed
    /// </summary>
    public bool SkipPublisherCheck { get; set; } = true;

    /// <summary>
    /// Whether to allow clobber when importing modules
    /// </summary>
    public bool AllowClobber { get; set; } = false;

    /// <summary>
    /// Timeout in seconds for module installation operations
    /// </summary>
    public int InstallTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Timeout in seconds for the out-of-process setup request.
    /// This only applies when RuntimeMode is OutOfProcess and controls the setup call
    /// (module imports, startup scripts, and related environment initialization).
    /// Separate from <see cref="InstallTimeoutSeconds"/>, which only controls module install operations.
    /// Defaults to 120 seconds.
    /// </summary>
    public int SetupTimeoutSeconds { get; set; } = 120;
}

/// <summary>
/// Configuration for installing a module from PowerShell Gallery or other repository
/// </summary>
public class ModuleInstallation
{
    /// <summary>
    /// Name of the module to install
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Specific version to install (optional)
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Minimum version required (optional)
    /// </summary>
    public string? MinimumVersion { get; set; }

    /// <summary>
    /// Maximum version allowed (optional)
    /// </summary>
    public string? MaximumVersion { get; set; }

    /// <summary>
    /// Repository to install from (defaults to PSGallery)
    /// </summary>
    public string Repository { get; set; } = "PSGallery";

    /// <summary>
    /// Scope for installation (CurrentUser or AllUsers)
    /// </summary>
    public string Scope { get; set; } = "CurrentUser";

    /// <summary>
    /// Whether to force installation even if already exists
    /// </summary>
    public bool Force { get; set; } = false;

    /// <summary>
    /// Whether to skip publisher validation
    /// </summary>
    public bool SkipPublisherCheck { get; set; } = true;

    /// <summary>
    /// Whether to allow installing pre-release versions
    /// </summary>
    public bool AllowPrerelease { get; set; } = false;
}
