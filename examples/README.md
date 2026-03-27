# PoshMcp Environment Customization Examples

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

### Building Custom Images

```bash
# From the repository root:

# 1. Build the base PoshMcp image
docker build -t poshmcp .

# 2. Build a custom image from an example template
docker build -f examples/Dockerfile.user -t my-poshmcp .

# Or use the Azure template
docker build -f examples/Dockerfile.azure -t poshmcp-azure .

# Or the advanced template
docker build -f examples/Dockerfile.custom -t poshmcp-custom .
```

### Running Custom Images

```bash
# Run your custom image in web mode
docker run -d -p 8080:8080 -e POSHMCP_MODE=web my-poshmcp

# Run in stdio mode
docker run -it -e POSHMCP_MODE=stdio my-poshmcp

# Run Azure image with environment variables
docker run -d -p 8080:8080 \
  -e POSHMCP_MODE=web \
  -e AZURE_CLIENT_ID=your-id \
  -e AZURE_TENANT_ID=your-tenant \
  poshmcp-azure
```

### Using Docker Compose Examples

```bash
├── startup.ps1
├── custom-modules/              # Place your custom modules here
│   └── MyModule/
│       ├── MyModule.psd1
│       └── MyModule.psm1
└── config/                      # Additional config files
    ├── startup.ps1              # Startup script (symlink or copy)
    └── modules/                 # Alternative module location
```

## Multi-Tenant Structure

```
examples/
└── tenants/
    ├── tenant-a/
    │   ├── startup.ps1          # Tenant A specific startup
    │   └── modules/             # Tenant A specific modules
    └── tenant-b/
        ├── startup.ps1          # Tenant B specific startup
        └── modules/             # Tenant B specific modules
```

## Common Patterns

### 1. Pre-Install Modules in Dockerfile

**Dockerfile.custom:**
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0

# Install PowerShell Gallery modules at build time
RUN pwsh -Command "Install-Module -Name Az.Accounts -Scope AllUsers -Force -SkipPublisherCheck"

COPY ./app /app
WORKDIR /app
CMD ["dotnet", "PoshMcp.Server.dll"]
```

Then use `ImportModules` instead of `InstallModules` for faster startup.

### 2. Environment-Specific Configuration

Use Docker environment variables to switch configurations:

```yaml
environment:
  - ASPNETCORE_ENVIRONMENT=Production  # Loads appsettings.Production.json
```

### 3. Secrets Management

For sensitive data (credentials, API keys), use Docker secrets:

```yaml
secrets:
  - azure_client_secret

services:
  poshmcp:
    secrets:
      - azure_client_secret
    environment:
      - AZURE_CLIENT_SECRET_FILE=/run/secrets/azure_client_secret
```

Then reference in startup script:
```powershell
$secret = Get-Content /run/secrets/azure_client_secret -Raw
```

## Troubleshooting

### View Logs

```bash
# Follow logs
docker-compose -f docker-compose.environment.yml logs -f poshmcp-basic

# View startup messages
docker-compose -f docker-compose.environment.yml logs poshmcp-basic | grep "environment"
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
dotnet run --project PoshMcp.Server -- \
  --ASPNETCORE_ENVIRONMENT=Development
```

## Performance Tips

1. **Pre-install modules** - Build custom Docker image with modules installed
2. **Use volume caching** - Mount modules read-only from persistent volumes
3. **Keep startup scripts lean** - Complex initialization adds startup time
4. **Set appropriate timeouts** - Balance between reliability and speed

## Security Considerations

1. **Read-only volumes** - Mount configuration and scripts as read-only (`:ro`)
2. **Validate scripts** - Review startup scripts before deployment
3. **Use CurrentUser scope** - Install modules in user scope, not system-wide
4. **Enable publisher checks** - In production, validate module publishers
5. **Secrets management** - Never store credentials in configuration files

## Next Steps

- Read the [full documentation](../docs/ENVIRONMENT-CUSTOMIZATION.md)
- Check the [Docker guide](../DOCKER.md)
- Review the [main README](../README.md)
