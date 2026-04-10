# PoshMcp environment customization examples

This directory contains examples demonstrating how to customize the PowerShell environment in PoshMcp, including Dockerfile templates for creating custom images.

## Files

### Dockerfile Templates

- **`Dockerfile.user`** - Basic pattern for extending the base image
  - Install PowerShell modules at build time
  - Include custom appsettings.json
  - Simple single-stage build
  
- **`Dockerfile.azure`** - Azure-enabled image with Az modules
  - Pre-installed Azure PowerShell modules (Az.Accounts, Az.Resources, etc.)
  - Azure Managed Identity integration
  - Startup script for Azure connection
  
- **`Dockerfile.custom`** - Advanced customization pattern
  - Multi-stage build for smaller images
  - Multiple module sets (testing, Azure, custom)
  - Additional system dependencies
  - Custom volumes and helper scripts

### Configuration Examples

- **`appsettings.basic.json`** - Simple configuration with module installation
- **`appsettings.advanced.json`** - Advanced setup with Azure modules and custom paths
- **`appsettings.tenant.json`** - Multi-tenant configuration template

### Startup Scripts

- **`startup.ps1`** - Comprehensive startup script example with:
  - Company-specific initialization
  - Azure Managed Identity integration
  - Custom helper functions
  - Environment detection

### Docker Compose

- **`docker-compose.environment.yml`** - Three deployment scenarios:
  1. **Basic** - Simple runtime module installation
  2. **Advanced** - Custom modules, startup scripts, Azure integration
  3. **Multi-tenant** - Isolated tenant configurations with separate data volumes

---

## Quick Start

### Building the Base Image

The base PoshMcp image must be built first:

```bash
# From the repository root
docker build -t poshmcp:latest .
```

### Building Custom Images

Use `poshmcp build` for standard containerization:

```bash
# Build base image
poshmcp build

# Build with pre-installed modules
poshmcp build --modules "Pester PSScriptAnalyzer"
poshmcp build --modules "Az.Accounts Az.Resources Az.Storage Az.Compute"

# Build with a custom tag
poshmcp build --tag myorg/poshmcp:latest

# See all options
poshmcp build --help
```

These are reference patterns. Use `poshmcp build` for standard containerization. For custom Dockerfile scenarios, see the example files in this directory.

### Running Custom Images

```bash
# Web mode (HTTP on port 8080)
docker run -d -p 8080:8080 my-poshmcp

# Stdio mode (for MCP clients)
docker run -it --rm my-poshmcp

# With Azure authentication
docker run -d -p 8080:8080 \
  -e POSHMCP_MODE=web \
  -e AZURE_CLIENT_ID=your-client-id \
  -e AZURE_TENANT_ID=your-tenant-id \
  poshmcp-azure

# With custom configuration
docker run -d -p 8080:8080 \
  -v /path/to/appsettings.json:/app/appsettings.json \
  my-poshmcp
```

### Using Docker Compose

To use docker-compose, add an entry that runs `poshmcp serve --transport http` in the service definition:

```yaml
services:
  poshmcp:
    image: poshmcp:latest
    command: ["poshmcp", "serve", "--transport", "http"]
    ports:
      - "8080:8080"
```

See `docker-compose.environment.yml` in this directory for a full multi-scenario example.

## Dockerfile Examples Explained

### Dockerfile.user - Basic Pattern

**Use case:** Simple customization with additional PowerShell modules

```dockerfile
FROM poshmcp:latest
USER root
COPY install-modules.ps1 /tmp/
ENV INSTALL_PS_MODULES="Pester PSScriptAnalyzer"
RUN pwsh /tmp/install-modules.ps1 && rm /tmp/install-modules.ps1
COPY appsettings.basic.json /app/appsettings.json
USER appuser
```

**What it does:**
- Installs Pester and PSScriptAnalyzer (common testing/analysis tools)
- Replaces appsettings.json with basic configuration
- Uses install-modules.ps1 helper script for consistency
- Runs as non-root `appuser` for security

**Build time:** ~2-5 minutes (depends on module sizes)  
**Container size:** ~850 MB

### Dockerfile.azure - Azure Integration

**Use case:** Azure PowerShell modules with Managed Identity support

```dockerfile
FROM poshmcp:latest
USER root
COPY install-modules.ps1 /tmp/
COPY azure-managed-identity-startup.ps1 /app/startup.ps1
ENV INSTALL_PS_MODULES="Az.Accounts Az.Resources Az.Storage Az.Compute"
RUN pwsh /tmp/install-modules.ps1 && rm /tmp/install-modules.ps1
COPY appsettings.advanced.json /app/appsettings.json
USER appuser
```

**What it does:**
- Pre-installs Azure PowerShell modules (faster startup)
- Adds Managed Identity startup script
- Configures for Azure Container Apps deployment
- Enables Azure resource access via `Connect-AzAccount -Identity`

**Build time:** ~5-10 minutes (Az modules are large)  
**Container size:** ~1.2 GB

**Environment variables:**
```bash
AZURE_CLIENT_ID=<managed-identity-client-id>
AZURE_TENANT_ID=<tenant-id>
ASPNETCORE_ENVIRONMENT=Production
```

### Dockerfile.custom - Advanced Multi-Stage

**Use case:** Production deployments with minimal container size

```dockerfile
FROM poshmcp:latest as builder
USER root
COPY install-modules.ps1 /tmp/
ENV INSTALL_PS_MODULES="Az.Accounts Pester PSScriptAnalyzer"
RUN pwsh /tmp/install-modules.ps1

FROM poshmcp:latest
# Copy pre-installed modules from builder
COPY --from=builder /root/.local/share/powershell/Modules /root/.local/share/powershell/Modules
COPY startup.ps1 /app/startup.ps1
COPY appsettings.advanced.json /app/appsettings.json
```

**What it does:**
- Multi-stage build reduces final image size (only modules, not build tools)
- Caches modules in layers for faster rebuilds
- Includes comprehensive startup script
- Best for production deployments

**Build time:** ~5-10 minutes  
**Container size:** ~900 MB (smaller than full install)

## Configuration Files Explained

### appsettings.basic.json

Simple configuration for basic usage:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "PowerShellConfiguration": {
    "FunctionNames": [
      "Get-Process",
      "Get-Service",
      "Get-ChildItem"
    ],
    "Environment": {
      "InstallModules": [
        {
          "Name": "Pester",
          "MinimumVersion": "5.0.0"
        }
      ]
    }
  }
}
```

**Good for:**
- Learning and development
- Simple PowerShell function exposure
- Testing MCP server functionality
- Lightweight deployments

### appsettings.advanced.json

Full-featured configuration with Azure integration:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "PowerShellConfiguration": {
    "FunctionNames": [
      "Get-AzResource",
      "Get-AzVM",
      "New-AzResourceGroup"
    ],
    "IncludePatterns": [
      "Get-Az*",
      "Set-Az*"
    ],
    "ExcludePatterns": [
      "*-Dangerous*"
    ],
    "Environment": {
      "StartupScriptPath": "/app/startup.ps1",
      "ImportModules": [
        "Az.Accounts",
        "Az.Resources",
        "Az.Compute"
      ],
      "AllowClobber": true
    }
  }
}
```

**Good for:**
- Azure resource management
- Production deployments
- Advanced module usage
- Pattern-based function filtering

### appsettings.tenant.json

Multi-tenant configuration:

```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "StartupScript": "
        $Global:TenantId = $env:TENANT_ID;
        $Global:TenantName = $env:TENANT_NAME;
        Write-Output 'Tenant initialized: $Global:TenantName'
      "
    }
  }
}
```

**Good for:**
- SaaS deployments with multiple customers
- Tenant-specific initialization
- Per-tenant module management
- Isolated PowerShell runspaces per tenant

## Startup Scripts Explained

### startup.ps1 - Comprehensive Example

Boots the PowerShell environment with custom initialization:

```powershell
# 1. Set tenant context
$tenantId = $env:TENANT_ID ?? "default"
$Global:TenantContext = @{
    Id = $tenantId
    Initialized = (Get-Date)
}

# 2. Azure Managed Identity connection (if available)
if ($env:AZURE_CLIENT_ID) {
    Write-Host "Connecting to Azure with Managed Identity..."
    Connect-AzAccount -Identity -AccountId $env:AZURE_CLIENT_ID
}

# 3. Load custom functions
if (Test-Path "/app/custom-functions.ps1") {
    . "/app/custom-functions.ps1"
}

# 4. Output init status
Write-Host "PoshMcp environment ready for tenant: $tenantId"
```

**Called by:** appsettings.json `StartupScriptPath` setting  
**Executed:** During PowerShel runspace initialization (once per container lifecycle)

### azure-managed-identity-startup.ps1

Minimal startup for Azure Managed Identity:

```powershell
# Connect using system-assigned or user-assigned managed identity
$clientId = $env:AZURE_CLIENT_ID

if ($clientId) {
    Write-Output "Authenticating with Managed Identity: $clientId"
    Connect-AzAccount -Identity -AccountId $clientId | Out-Null
} else {
    Write-Output "Authenticating with system-assigned Managed Identity"
    Connect-AzAccount -Identity | Out-Null
}

Write-Output "Azure authentication successful"
```

## Directory Structure

```
examples/
├── Dockerfile.user              # Basic customization template
├── Dockerfile.azure             # Azure integration template
├── Dockerfile.custom            # Advanced multi-stage template
│
├── appsettings.basic.json       # Simple configuration
├── appsettings.advanced.json    # Full-featured configuration
├── appsettings.tenant.json      # Multi-tenant template
│
├── startup.ps1                  # Comprehensive startup script
├── azure-managed-identity-startup.ps1  # Azure auth startup
│
├── docker-compose.environment.yml      # Docker Compose examples
└── README.md                    # This file
```

### Using startup scripts with Docker Compose

```yaml
services:
  poshmcp-basic:
    image: poshmcp:latest
    environment:
      POSHMCP_MODE: web
    volumes:
      - ./startup.ps1:/app/startup.ps1:ro
      - ./appsettings.basic.json:/app/appsettings.json:ro
```

## Security Considerations

1. **Read-only volumes** - Mount configuration and scripts as read-only (`:ro`)
2. **Validate scripts** - Review startup scripts before deployment
3. **Use CurrentUser scope** - Install modules in user scope, not system-wide
4. **Enable publisher checks** - In production, validate module publishers
5. **Secrets management** - Never store credentials in configuration files

## Troubleshooting

### View Logs

```bash
# Follow logs
docker-compose -f docker-compose.environment.yml logs -f poshmcp-basic

# View startup messages
docker-compose -f docker-compose.environment.yml logs poshmcp-basic | grep -i "environment"
```

### Debug Inside Container

```bash
# Execute PowerShell in running container
docker exec -it poshmcp-basic pwsh

# Then inside container:
Get-Module
Get-EnvironmentInfo
```

### Test Configuration Without Docker

```bash
# From repository root
dotnet run --project PoshMcp.Server

# Or with custom config
dotnet run --project PoshMcp.Server -- --ASPNETCORE_ENVIRONMENT=Development
```

## Performance Tips

1. **Pre-install modules** - Build custom Docker image with modules installed
2. **Use volume caching** - Mount modules read-only from persistent volumes
3. **Keep startup scripts lean** - Complex initialization adds startup time
4. **Set appropriate timeouts** - Balance between reliability and speed

## Next steps

- Read the [environment customization guide](../docs/ENVIRONMENT-CUSTOMIZATION.md)
- Check the [Docker deployment guide](../DOCKER.md)
- Review the [main README](../README.md)
- Explore [infrastructure/azure/](../infrastructure/azure/) for cloud deployment

