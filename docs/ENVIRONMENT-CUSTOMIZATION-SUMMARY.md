# Environment Customization — Feature Summary

> **📍 This is a feature overview.** For the full user guide, see [Environment Customization Guide](ENVIRONMENT-CUSTOMIZATION.md). For developer integration steps, see [Implementation Guide](IMPLEMENTATION-GUIDE.md).

## What Was Added

PoshMcp supports comprehensive environment customization through `appsettings.json`, enabling:

1. **Startup Scripts** — execute custom PowerShell code during initialization (inline or from file)
2. **Module Installation** — install modules from PowerShell Gallery or custom repositories  
3. **Local Module Loading** — import modules from custom paths (volumes, network shares)
4. **Module Path Configuration** — add custom directories to `$env:PSModulePath`

All configuration is declarative and happens during PowerShell runspace initialization.

## Feature Files Overview

| Category | Files | Description |
|----------|-------|-------------|
| **Core Implementation** | `PowerShell/EnvironmentConfiguration.cs`, `PowerShell/PowerShellEnvironmentSetup.cs` | Configuration model classes and setup service |
| **Configuration Examples** | `appsettings.environment-example.json`, `appsettings.modules.json` | Sample configurations for reference |
| **Documentation** | [ENVIRONMENT-CUSTOMIZATION.md](ENVIRONMENT-CUSTOMIZATION.md), [IMPLEMENTATION-GUIDE.md](IMPLEMENTATION-GUIDE.md) | User guide and developer integration guide |
| **Examples** | `examples/startup.ps1`, `examples/azure-managed-identity-startup.ps1`, `examples/docker-compose.environment.yml` | Working examples with Docker Compose |
| **Docker** | `examples/Dockerfile.user`, `examples/Dockerfile.azure`, `examples/Dockerfile.custom` | Dockerfile templates demonstrating customization |

## Implementation Status

### ✅ Complete
- Configuration classes (`EnvironmentConfiguration.cs`, `ModuleInstallation.cs`)
- Environment setup service (`PowerShellEnvironmentSetup.cs`) with:
  - Module path configuration
  - PowerShell Gallery trust setup
  - Module installation from repositories
  - Module importing from installed packages
  - Startup script file execution
  - Inline startup script execution
- User documentation and examples
- Docker examples with working configurations
- Docker Compose orchestration examples

### ✅ Integrated (As of Recent Updates)
- ✅ Service registration in `PoshMcp.Server/Program.cs`
- ✅ Service registration in `PoshMcp.Web/Program.cs`
- ✅ PowerShellRunspaceInitializer updated with environment setup
- ✅ PowerShellRunspaceHolder integration

### Available in Production
- ✅ Full environment customization functionality
- ✅ All configuration options working
- ✅ Docker and Docker Compose examples working
- ✅ Comprehensive documentation and examples

## Usage Example

Configure in `appsettings.json`:

```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "InstallModules": [
        {
          "Name": "Az.Accounts",
          "MinimumVersion": "2.15.0"
        },
        {
          "Name": "Pester",
          "MinimumVersion": "5.0.0"
        }
      ],
      "ImportModules": [
        "Az.Accounts",
        "Az.Resources"
      ],
      "ModulePaths": [
        "/mnt/shared-modules",
        "./custom-modules"
      ],
      "StartupScriptPath": "/app/startup.ps1",
      "TrustPSGallery": true
    },
    "FunctionNames": [
      "Get-AzResource",
      "Get-AzVM"
    ]
  }
}
```

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                 Application Startup                              │
│              (Program.cs / Startup)                              │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│         PowerShell Runspace Initialization                      │
│  1. Create runspace                                              │
│  2. Set execution policy                                         │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│    PowerShellEnvironmentSetup.ApplyEnvironmentConfiguration()   │
│  1. Configure PSModulePath                                       │
│  2. Trust PSGallery                                              │
│  3. Install modules from repositories                            │
│  4. Import pre-installed modules                                 │
│  5. Execute startup script (file)                                │
│  6. Execute startup script (inline)                              │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│          PowerShell Runspace Ready                               │
│  - All modules installed and imported                            │
│  - Custom initialization complete                                │
│  - Ready for MCP tool execution                                  │
└─────────────────────────────────────────────────────────────────┘
```

## Quick Reference

### Install modules from PowerShell Gallery

```json
{
  "InstallModules": [
    {
      "Name": "ModuleName",
      "MinimumVersion": "1.0.0",
      "Repository": "PSGallery"
    }
  ]
}
```

### Run custom startup script

```json
{
  "StartupScriptPath": "/path/to/startup.ps1"
}
```

Or inline:

```json
{
  "StartupScript": "$Global:CompanyName = 'Acme'; Write-Host 'Ready!'"
}
```

### Add custom module paths

```json
{
  "ModulePaths": [
    "/mnt/shared-modules",
    "./custom-modules"
  ]
}
```

## Docker Integration

### Pre-install modules at build time (faster startup)

```dockerfile
FROM poshmcp:latest
ENV INSTALL_PS_MODULES="Az.Accounts Pester"
COPY install-modules.ps1 /tmp/
RUN pwsh /tmp/install-modules.ps1
```

Then in `appsettings.json`, use `ImportModules` instead of `InstallModules`:

```json
{
  "Environment": {
    "ImportModules": ["Az.Accounts", "Pester"]
  }
}
```

### Docker Compose example

See `examples/docker-compose.environment.yml` for:
- **Basic deployment** — simple runtime module installation
- **Advanced deployment** — custom modules, startup scripts, Azure integration
- **Multi-tenant deployment** — isolated configurations per tenant

## Testing

### Manual verification

```bash
# Build and run with custom configuration
docker build -f examples/Dockerfile.user -t my-poshmcp .
docker run -it --rm my-poshmcp

# Inside container: verify modules are loaded
# (Modules are automatically imported on startup)
```

### View logs during startup

```bash
# See environment setup in logs
docker logs <container-id> | grep -i "environment"
```

## See Also

- **[Environment Customization Guide](ENVIRONMENT-CUSTOMIZATION.md)** — comprehensive user guide with use cases
- **[Implementation Guide](IMPLEMENTATION-GUIDE.md)** — developer integration details
- **[examples/](../examples/)** — Dockerfile templates and sample configurations
- **[DOCKER.md](../DOCKER.md)** — containerization guide
- **[README.md](../README.md)** — project overview

## Troubleshooting

### Modules not installing

- Check that repository is accessible: `Search-Module -Name ModuleName`
- Verify module name is correct
- Check internet connectivity in container
- View logs for install errors: `docker logs <container-id> | grep -i "module"`

### Startup script not running

- Verify `StartupScriptPath` or `StartupScript` is set in config
- Check script syntax: run locally first
- View logs for execution errors

### Startup timeout exceeds container readiness probe

- Reduce `InstallTimeoutSeconds` in configuration (default: 300s)
- Pre-install modules at build time instead of runtime
- Optimize startup script (remove unnecessary operations)


