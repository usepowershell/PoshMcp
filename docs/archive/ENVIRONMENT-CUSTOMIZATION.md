# Environment Customization Guide

This guide explains how to customize the PowerShell environment in PoshMcp through startup scripts and module management.

## Before You Start

- **Module conflicts?** If you're experiencing issues with incompatible modules, see [OUT-OF-PROCESS.md](OUT-OF-PROCESS.md) — PoshMcp offers optional out-of-process PowerShell runtime for module isolation.
- **Production modules?** Pre-install PowerShell Gallery modules at container build time to reduce startup time. See [../DOCKER.md](../DOCKER.md) for container examples.

## Overview

PoshMcp supports three types of environment customization:

1. **Startup Scripts** - Execute custom PowerShell code during initialization
2. **Module Installation** - Install modules from PowerShell Gallery or other repositories
3. **Local Module Loading** - Import modules from local paths or mounted volumes

All customization is configured through the `Environment` section in `appsettings.json`.

---

## Configuration Schema

### Basic Structure

```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "InstallModules": [],
      "ImportModules": [],
      "ModulePaths": [],
      "StartupScript": "",
      "StartupScriptPath": "",
      "TrustPSGallery": false,
      "AllowClobber": false,
      "InstallTimeoutSeconds": 300,
      "SetupTimeoutSeconds": 120
    }
  }
}
```

### Configuration Options

| Option | Type | Description | Default |
|--------|------|-------------|---------|
| `InstallModules` | Array | Modules to install from PowerShell Gallery | `[]` |
| `ImportModules` | Array | Pre-installed modules to import | `[]` |
| `ModulePaths` | Array | Additional paths to add to `$env:PSModulePath` | `[]` |
| `StartupScript` | String | Inline PowerShell script to execute | `null` |
| `StartupScriptPath` | String | Path to PowerShell script file | `null` |
| `TrustPSGallery` | Boolean | Automatically trust PSGallery | `false` |
| `AllowClobber` | Boolean | Allow module imports to overwrite existing commands | `false` |
| `InstallTimeoutSeconds` | Integer | Timeout for module installation operations | `300` |
| `SetupTimeoutSeconds` | Integer | Timeout for out-of-process setup request (module imports/startup scripts) | `120` |

---

## Use Cases

### 1. Installing Modules from PowerShell Gallery

Install modules that aren't pre-installed in your container or environment.

```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "InstallModules": [
        {
          "Name": "Az.Accounts",
          "Repository": "PSGallery",
          "Scope": "CurrentUser"
        },
        {
          "Name": "Pester",
          "MinimumVersion": "5.0.0",
          "MaximumVersion": "5.9.9",
          "Repository": "PSGallery"
        }
      ]
    }
  }
}
```

#### Module Installation Options

```json
{
  "Name": "ModuleName",          // Required: Module name
  "Version": "1.2.3",             // Specific version (optional)
  "MinimumVersion": "1.0.0",      // Minimum version (optional)
  "MaximumVersion": "2.0.0",      // Maximum version (optional)
  "Repository": "PSGallery",      // Repository name (default: PSGallery)
  "Scope": "CurrentUser",         // CurrentUser or AllUsers (default: CurrentUser)
  "Force": false,                 // Force reinstall (default: false)
  "SkipPublisherCheck": true,     // Skip publisher validation (default: true)
  "AllowPrerelease": false        // Allow pre-release versions (default: false)
}
```

**Note:** If a module is already installed, it will be skipped unless `Force: true` is set.

---

### 2. Importing Pre-Installed Modules

Import modules that are already available (built-in, in Docker image, or installed by other means).

```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "ImportModules": [
        "Microsoft.PowerShell.Management",
        "Microsoft.PowerShell.Utility",
        "Microsoft.PowerShell.Security",
        "Az.Resources"
      ]
    }
  }
}
```

**Best Practice:** Use `ImportModules` for modules you know are already installed. This is faster than `InstallModules` because it skips installation checks and downloads.

---

### 3. Loading Modules from Custom Paths

Add custom module directories to PowerShell's module path. Useful for:
- Docker volume mounts
- Shared network locations
- Custom module collections

```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "ModulePaths": [
        "/mnt/shared-modules",           // Docker volume mount
        "%USERPROFILE%\\MyModules",      // Windows user profile (expands env vars)
        "./custom-modules",               // Relative to working directory
        "${HOME}/PowerShellModules"       // Unix home directory
      ],
      "ImportModules": [
        "MyCustomModule"                  // Now available from custom path
      ]
    }
  }
}
```

**Environment Variable Expansion:** Module paths support environment variable expansion:
- Windows: `%VARIABLE%`
- Unix: `${VARIABLE}` or `$VARIABLE`

---

### 4. Startup Scripts

#### Inline Startup Script

Execute PowerShell code directly from configuration. Good for simple initialization:

```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "StartupScript": "$Global:Environment = 'Production'\nfunction Get-Environment { return $Global:Environment }\nWrite-Host 'Environment initialized' -ForegroundColor Green"
    }
  }
}
```

**Use Cases:**
- Set global variables
- Define helper functions
- Configure session preferences
- Print initialization messages

#### Startup Script from File

For complex initialization, reference an external file:

```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "StartupScriptPath": "/config/startup.ps1"
    }
  }
}
```

**Example startup.ps1:**
```powershell
# Set up global variables
$Global:CompanyName = "Acme Corp"
$Global:Environment = $env:ENVIRONMENT ?? "Development"

# Define utility functions
function Get-CompanyInfo {
    return @{
        Name = $Global:CompanyName
        Environment = $Global:Environment
        InitializedAt = Get-Date
    }
}

# Configure PowerShell preferences
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# Import company-specific modules
Import-Module CompanyTools -ErrorAction SilentlyContinue

Write-Host "✓ Startup script completed" -ForegroundColor Green
```

---

## Docker Scenarios

### Scenario 1: Pre-Install Modules in Docker Image

**Dockerfile:**
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0

# Install PowerShell Gallery modules at build time
RUN pwsh -Command "Install-Module -Name Az.Accounts -Scope AllUsers -Force -SkipPublisherCheck"
RUN pwsh -Command "Install-Module -Name Pester -Scope AllUsers -Force -SkipPublisherCheck"

COPY ./app /app
WORKDIR /app

CMD ["dotnet", "PoshMcp.Server.dll"]
```

**appsettings.json:**
```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "ImportModules": [
        "Az.Accounts",
        "Pester"
      ]
    }
  }
}
```

**Pros:** Faster startup (no installation), smaller logs
**Cons:** Larger image, modules baked into image

---

### Scenario 2: Install Modules at Runtime

**appsettings.json:**
```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "InstallModules": [
        {
          "Name": "Az.Accounts",
          "Scope": "CurrentUser"
        }
      ]
    }
  }
}
```

**Pros:** Smaller image, flexible module versions
**Cons:** Slower startup, network dependency

---

### Scenario 3: Volume-Mounted Modules

**docker-compose.yml:**
```yaml
version: '3.8'
services:
  poshmcp:
    image: poshmcp:latest
    volumes:
      - ./modules:/mnt/modules:ro
      - ./config:/config:ro
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
```

**appsettings.json:**
```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "ModulePaths": [
        "/mnt/modules"
      ],
      "ImportModules": [
        "MyCompanyModule"
      ],
      "StartupScriptPath": "/config/startup.ps1"
    }
  }
}
```

**Pros:** No image rebuild, easy updates, shared modules
**Cons:** External dependency, requires volume management

---

## Complete Examples

### Example 1: Azure DevOps Automation

```json
{
  "PowerShellConfiguration": {
    "FunctionNames": [],
    "Modules": [],
    "Environment": {
      "InstallModules": [
        {
          "Name": "Az.Accounts",
          "MinimumVersion": "2.0.0"
        },
        {
          "Name": "Az.Resources"
        },
        {
          "Name": "Az.Storage"
        }
      ],
      "StartupScript": "Connect-AzAccount -Identity\n$Global:SubscriptionId = (Get-AzContext).Subscription.Id\nWrite-Host 'Connected to Azure subscription' -ForegroundColor Green"
    }
  }
}
```

### Example 2: Testing & Quality Automation

```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "InstallModules": [
        {
          "Name": "Pester",
          "MinimumVersion": "5.0.0"
        },
        {
          "Name": "PSScriptAnalyzer"
        }
      ],
      "ModulePaths": [
        "./test-modules"
      ],
      "StartupScriptPath": "./config/test-setup.ps1"
    }
  }
}
```

### Example 3: Multi-Tenant SaaS

```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "ModulePaths": [
        "/mnt/tenant-modules"
      ],
      "StartupScript": "$Global:TenantId = $env:TENANT_ID\n$Global:DataPath = \"/data/$TenantId\"\nNew-Item -ItemType Directory -Path $Global:DataPath -Force -ErrorAction SilentlyContinue",
      "ImportModules": [
        "TenantManagement",
        "DataAccess"
      ]
    }
  }
}
```

---

## Execution Order

Environment setup executes in this order:

1. **Module Paths** - Add directories to `$env:PSModulePath`
2. **Trust PSGallery** - Configure PSGallery as trusted repository
3. **Install Modules** - Download and install from repositories
4. **Import Modules** - Import pre-installed modules
5. **Startup Script File** - Execute external script from `StartupScriptPath`
6. **Inline Startup Script** - Execute inline script from `StartupScript`

This order ensures modules are available before startup scripts execute.

---

## Error Handling

### Non-Fatal Errors

The following errors are logged but **don't prevent startup**:
- Module installation failures (individual modules)
- Module import failures (individual modules)
- Warnings from PSGallery trust operations
- Missing module paths (logged as warnings)

The server continues with available functionality.

### Fatal Errors

The following errors **prevent startup**:
- Critical PowerShell runtime errors
- Startup script syntax errors that crash the runspace

---

## Performance Considerations

### Startup Time Impact

| Action | Approximate Time |
|--------|------------------|
| Import pre-installed module | ~50-200ms per module |
| Install module from PSGallery | ~2-10s per module |
| Execute startup script | Varies (typically < 1s) |
| Add module path | ~5ms |

**Recommendation:** For production, pre-install modules in your Docker image and use `ImportModules` instead of `InstallModules`.

### Module Installation Timeout

Default timeout: **300 seconds (5 minutes)**

Adjust for large modules or slow networks:

```json
{
  "Environment": {
    "InstallTimeoutSeconds": 600
  }
}
```

---

## Security Considerations

### Publisher Validation

By default, `SkipPublisherCheck: true` to avoid installation failures. In high-security environments:

```json
{
  "InstallModules": [
    {
      "Name": "Az.Accounts",
      "SkipPublisherCheck": false
    }
  ]
}
```

### Module Sources

Only install modules from trusted repositories. Default `PSGallery` is maintained by Microsoft.

For private repositories:

```json
{
  "InstallModules": [
    {
      "Name": "CompanyModule",
      "Repository": "CompanyPrivateRepo"
    }
  ]
}
```

**Note:** The repository must be registered in PowerShell before installation.

### Startup Script Validation

Startup scripts execute with full PowerShell privileges. **Only use trusted scripts.**

**Best Practices:**
- Store startup scripts in version control
- Review changes before deployment
- Use read-only volume mounts in Docker
- Implement code review for script changes

---

## Troubleshooting

### Enable Verbose Logging

```json
{
  "Logging": {
    "LogLevel": {
      "PoshMcp.Server.PowerShell": "Debug"
    }
  }
}
```

### Common Issues

#### Module Installation Hangs

**Symptom:** Installation never completes
**Solution:** Check network connectivity to PowerShell Gallery, increase timeout

#### Module Not Found After Installation

**Symptom:** Module installed but import fails
**Solution:** Verify installation scope matches execution context (CurrentUser vs AllUsers)

#### Startup Script Fails

**Symptom:** Errors in startup script execution
**Solution:** Test script manually in PowerShell, check syntax and dependencies

#### Module Path Not Found

**Symptom:** Warning about missing module path
**Solution:** Verify path exists, check environment variable expansion

---

## Integration with Existing Configuration

Environment configuration works alongside existing PowerShell configuration:

```json
{
  "PowerShellConfiguration": {
    "FunctionNames": ["Get-CustomData"],
    "Modules": ["Microsoft.PowerShell.Utility"],
    "ExcludePatterns": ["*-Dangerous*"],
    "IncludePatterns": ["Get-*"],
    "EnableDynamicReloadTools": false,
    "Environment": {
      "InstallModules": [...],
      "ImportModules": [...],
      "StartupScript": "..."
    }
  }
}
```

**Note:** The `Modules` property in the root configuration is used for **tool discovery**. The `Environment.ImportModules` property is for **environment setup**. They can overlap or be different based on your needs.

---

## Testing Your Configuration

### Manual Testing

1. Update `appsettings.json` with your configuration
2. Run the server: `dotnet run --project PoshMcp.Server`
3. Check logs for environment setup messages
4. Verify modules are available:

```powershell
# Connect to your MCP server and run:
Get-Module -ListAvailable
```

### Docker Testing

```bash
# Build
docker build -t poshmcp-test .

# Run with custom config
docker run -v $(pwd)/test-config.json:/app/appsettings.json poshmcp-test

# Check logs
docker logs <container-id>
```

---

## Best Practices Summary

1. ✅ **Pre-install modules** in Docker images for production
2. ✅ **Use environment variables** in paths for portability
3. ✅ **Keep startup scripts** in version control
4. ✅ **Test configuration changes** in development first
5. ✅ **Use `ImportModules`** for available modules (faster)
6. ✅ **Set appropriate timeouts** based on your network
7. ✅ **Enable verbose logging** when troubleshooting
8. ❌ **Don't install large modules** at every startup
9. ❌ **Don't skip publisher checks** in production (when possible)
10. ❌ **Don't execute untrusted** startup scripts

---

## Additional resources

- [PowerShell Gallery](https://www.powershellgallery.com/)
- [Docker Volumes Documentation](https://docs.docker.com/storage/volumes/)
- [PowerShell Module Management](https://docs.microsoft.com/powershell/module/powershellget/)
- [PoshMcp Project README](../README.md)

---

## See also

- [Implementation guide](IMPLEMENTATION-GUIDE.md) — developer integration steps for this feature
- [Integration checklist](INTEGRATION-CHECKLIST.md) — step-by-step checklist for wiring up the feature
- [DOCKER.md](../DOCKER.md) — Docker deployment guide with module installation patterns
- [Examples](../examples/) — sample configurations and Docker Compose files
