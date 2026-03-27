# Environment Customization Implementation Guide

This document explains how the environment customization feature is implemented and how to integrate it into the PoshMcp server.

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                     PoshMcp Server Startup                       │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│              PowerShellRunspaceInitializer                       │
│  - Creates runspace                                              │
│  - Sets execution policy                                         │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│           PowerShellEnvironmentSetup (NEW)                       │
│  1. Configure PSModulePath                                       │
│  2. Trust PSGallery                                              │
│  3. Install modules                                              │
│  4. Import modules                                               │
│  5. Execute startup script (file)                                │
│  6. Execute startup script (inline)                              │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│                Ready for MCP Tool Execution                      │
└─────────────────────────────────────────────────────────────────┘
```

## Component Summary

### 1. Configuration Classes

**`EnvironmentConfiguration.cs`** - Main configuration class
- `InstallModules`: List of modules to install from repositories
- `ImportModules`: List of pre-installed modules to import
- `ModulePaths`: Custom directories to add to PSModulePath
- `StartupScript`: Inline PowerShell code
- `StartupScriptPath`: Path to startup script file
- Configuration options (timeouts, trust settings, etc.)

**`ModuleInstallation.cs`** - Module installation descriptor
- Name, version constraints, repository
- Scope, force, prerelease options
- Publisher check settings

**`PowerShellConfiguration.cs`** (Modified) - Added `Environment` property
- Links to the new EnvironmentConfiguration

### 2. Environment Setup Service

**`PowerShellEnvironmentSetup.cs`** - Main setup orchestrator
- `ApplyEnvironmentConfiguration()` - Main entry point
- Executes setup steps in correct order
- Comprehensive error handling and logging
- Returns `EnvironmentSetupResult` with detailed outcome

**Key Methods:**
- `ConfigureModulePaths()` - Add directories to PSModulePath
- `TrustPSGallery()` - Configure PSGallery as trusted
- `InstallModules()` - Install from PowerShell Gallery
- `ImportModules()` - Import available modules
- `ExecuteStartupScriptFile()` - Run external script
- `ExecuteInlineStartupScript()` - Run inline script

### 3. Integration Points

**`PowerShellRunspaceInitializer.cs`** (To be modified)
- Add call to environment setup after runspace creation
- Pass configuration from appsettings.json

**`Program.cs`** or startup (To be modified)
- Register `PowerShellEnvironmentSetup` in DI container
- Ensure configuration is bound properly

## Integration Steps

### Step 1: Register Services

In `Program.cs` (stdio) or `PoshMcp.Web/Program.cs`:

```csharp
// Add after existing PowerShellConfiguration binding
builder.Services.Configure<PowerShellConfiguration>(
    builder.Configuration.GetSection("PowerShellConfiguration"));

// Register the environment setup service
builder.Services.AddSingleton<PowerShellEnvironmentSetup>();
```

### Step 2: Update RunspaceInitializer

Modify `PowerShellRunspaceInitializer.CreateInitializedRunspace()`:

```csharp
public static async Task<PSPowerShell> CreateInitializedRunspaceAsync(
    EnvironmentConfiguration? envConfig = null,
    ILogger? logger = null,
    string? customScript = "")
{
    // Create a new runspace
    var runspace = RunspaceFactory.CreateRunspace();
    runspace.Open();

    // Create PowerShell instance
    var powerShell = PSPowerShell.Create();
    powerShell.Runspace = runspace;

    // Base initialization script
    var baseScript = @"
        if ($PSVersionTable.Platform -eq 'Windows') {
            Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process -Force
        }";

    powerShell.AddScript(baseScript);
    powerShell.Invoke();
    powerShell.Commands.Clear();

    // Apply environment customization if configured
    if (envConfig != null && logger != null)
    {
        var setupService = new PowerShellEnvironmentSetup(logger);
        var result = await setupService.ApplyEnvironmentConfiguration(
            powerShell, 
            envConfig);

        if (!result.Success)
        {
            logger.LogWarning(
                "Environment setup completed with {ErrorCount} errors", 
                result.Errors.Count);
        }
    }

    // Custom script from caller
    if (!string.IsNullOrWhiteSpace(customScript))
    {
        powerShell.AddScript(customScript);
        powerShell.Invoke();
        powerShell.Commands.Clear();
    }

    return powerShell;
}
```

### Step 3: Update PowerShellRunspaceHolder

Modify the singleton initialization:

```csharp
public static class PowerShellRunspaceHolder
{
    private static ILogger<PowerShellRunspaceHolder>? _logger;
    private static EnvironmentConfiguration? _envConfig;

    public static void Initialize(
        ILogger<PowerShellRunspaceHolder> logger,
        EnvironmentConfiguration envConfig)
    {
        _logger = logger;
        _envConfig = envConfig;
    }

    private static readonly Lazy<PSPowerShell> _instance = new(() =>
    {
        var task = PowerShellRunspaceInitializer.CreateInitializedRunspaceAsync(
            _envConfig,
            _logger,
            GetProductionInitializationScript());
        
        return task.GetAwaiter().GetResult();
    });

    // ... rest of the class
}
```

### Step 4: Wire Up in Startup

In the main startup code:

```csharp
// Get configuration
var envConfig = builder.Configuration
    .GetSection("PowerShellConfiguration:Environment")
    .Get<EnvironmentConfiguration>() ?? new EnvironmentConfiguration();

// Initialize the runspace holder with logger and config
var logger = loggerFactory.CreateLogger<PowerShellRunspaceHolder>();
PowerShellRunspaceHolder.Initialize(logger, envConfig);
```

### Step 5: Health Check Integration (Optional)

Add environment setup status to health checks:

```csharp
public class EnvironmentSetupHealthCheck : IHealthCheck
{
    private readonly PowerShellEnvironmentSetup _setup;
    
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Check if environment was set up successfully
        // Return health status based on last setup result
        
        return HealthCheckResult.Healthy("Environment configured");
    }
}
```

## Configuration Examples

### Minimal Configuration

```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "ImportModules": ["Microsoft.PowerShell.Utility"]
    }
  }
}
```

### Full Configuration

```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "InstallModules": [
        {
          "Name": "Pester",
          "MinimumVersion": "5.0.0"
        }
      ],
      "ImportModules": [
        "Microsoft.PowerShell.Management"
      ],
      "ModulePaths": ["/custom/modules"],
      "StartupScript": "Write-Host 'Ready'",
      "StartupScriptPath": "/config/startup.ps1",
      "TrustPSGallery": true,
      "InstallTimeoutSeconds": 300
    }
  }
}
```

## Testing

### Unit Tests

```csharp
[Fact]
public async Task ApplyEnvironmentConfiguration_InstallsModules()
{
    var logger = new Mock<ILogger<PowerShellEnvironmentSetup>>();
    var setup = new PowerShellEnvironmentSetup(logger.Object);
    
    var config = new EnvironmentConfiguration
    {
        InstallModules = new List<ModuleInstallation>
        {
            new() { Name = "Pester" }
        }
    };

    using var ps = PowerShell.Create();
    var result = await setup.ApplyEnvironmentConfiguration(ps, config);

    Assert.True(result.Success);
    Assert.Contains("Pester", result.InstalledModules);
}
```

### Integration Tests

```csharp
[Fact]
public async Task ServerStartup_WithEnvironmentConfig_InitializesCorrectly()
{
    // Arrange
    var config = new ConfigurationBuilder()
        .AddJsonFile("appsettings.test.json")
        .Build();

    // Act
    var host = CreateHostBuilder(config).Build();
    await host.StartAsync();

    // Assert
    // Verify modules are loaded
    // Verify startup script executed
}
```

## Logging

The environment setup produces structured logs:

```
[Information] Starting PowerShell environment setup
[Information] Configuring PSModulePath with 2 additional paths
[Debug] Added module path: /mnt/modules
[Information] Configuring PSGallery as trusted repository
[Information] Installing 3 modules
[Information] Installing module: Az.Accounts from PSGallery
[Information] Successfully installed module: Az.Accounts
[Information] Importing 2 modules
[Information] Importing module: Microsoft.PowerShell.Management
[Information] Successfully imported module: Microsoft.PowerShell.Management
[Information] Executing startup script from file: /config/startup.ps1
[Information] Successfully executed startup script from file
[Information] PowerShell environment setup completed successfully in 15234ms. Modules installed: 3, Modules imported: 2
```

## Error Handling

Errors are categorized as:

**Non-Fatal** (logged, but startup continues):
- Individual module installation failures
- Individual module import failures
- Missing custom module paths
- PSGallery trust warnings

**Fatal** (startup fails):
- PowerShell runspace creation failure
- Critical script syntax errors

Result object contains:
- `Success` - Overall success status
- `Errors` - List of error messages
- `Warnings` - List of warning messages
- `Duration` - Total setup time

## Performance Considerations

**Module Installation:**
- Each module: 2-10 seconds
- Timeout configurable per installation
- Skips already-installed modules (unless Force=true)

**Module Import:**
- Each module: 50-200ms
- No network access, quick operation

**Startup Script:**
- Varies based on script complexity
- Executed after module setup

**Optimization Strategies:**
1. Pre-install modules in Docker image (fastest)
2. Use ImportModules over InstallModules
3. Keep startup scripts focused
4. Use volume-mounted modules for sharing

## Upgrade Path

### For Existing Deployments

1. **No Breaking Changes** - Feature is opt-in via configuration
2. **Default Behavior** - Without `Environment` config, works as before
3. **Gradual Adoption** - Can add features incrementally:
   - Start with ImportModules
   - Add startup scripts
   - Later add InstallModules if needed

### Migration Example

**Before:**
```json
{
  "PowerShellConfiguration": {
    "Modules": ["Microsoft.PowerShell.Utility"]
  }
}
```

**After (equivalent):**
```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "ImportModules": ["Microsoft.PowerShell.Utility"]
    }
  }
}
```

## Security Checklist

- [ ] Validate startup scripts before deployment
- [ ] Use read-only volume mounts for scripts
- [ ] Enable publisher checks in production (when possible)
- [ ] Don't store credentials in configuration
- [ ] Use minimal scope (CurrentUser) for installations
- [ ] Set appropriate timeouts to prevent hangs
- [ ] Log all environment setup actions
- [ ] Review module sources (use trusted repositories)

## Troubleshooting

### Issue: Module installation hangs

**Solution:**
```json
{
  "Environment": {
    "InstallTimeoutSeconds": 600  // Increase timeout
  }
}
```

### Issue: Startup script fails

**Debug:**
1. Test script manually: `pwsh -File startup.ps1`
2. Check logs for error details
3. Verify file paths and permissions
4. Ensure script doesn't require interaction

### Issue: Modules not found

**Check:**
1. Module path is in PSModulePath
2. Module is installed in correct scope
3. Module name is correct (case-sensitive on Linux)
4. Import happens after installation

## Next Steps

1. Implement integration steps outlined above
2. Add unit tests for `PowerShellEnvironmentSetup`
3. Add integration tests with sample configurations
4. Update documentation with examples
5. Add health check for environment setup status
6. Consider adding metrics for setup duration

## Related Files

- `/docs/ENVIRONMENT-CUSTOMIZATION.md` - User documentation
- `/examples/startup.ps1` - Sample startup script
- `/examples/docker-compose.environment.yml` - Docker examples
- `/examples/appsettings.*.json` - Configuration examples
