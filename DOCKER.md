# Docker Deployment for PoshMcp Server

This directory contains Docker configuration files to containerize and deploy both the PoshMcp Web Server (HTTP) and stdio Server.

## Architecture Overview

PoshMcp uses a **base image + derived image** pattern:

1. **Base Image (`poshmcp:latest`)** - Contains the MCP server runtime only, no customizations
2. **User Images** - Your custom Dockerfiles that extend the base with modules, configuration, and scripts

This separation provides:
- ✅ Clear boundaries: PoshMcp = runtime, Your Dockerfile = customization  
- ✅ Reusable module installation with `install-modules.ps1`
- ✅ Version control for your customizations
- ✅ Easy updates: rebuild base, rebuild your image
- ✅ Follows Docker best practices

## Files

- `Dockerfile` - Base image (no modules, just runtime)
- `install-modules.ps1` - Reusable PowerShell module installation script
- `docker-compose.yml` - Orchestration configuration with profiles
- `docker-entrypoint.sh` - Startup script (chooses web or stdio mode)
- `docker.sh` / `docker.ps1` - Convenience scripts for common operations
- `examples/` - Example user Dockerfiles showing customization patterns

## Quick Start

### Method 1: Using the Base Image (No Modules)

```bash
# Build the base image
./docker.sh build

# Run web server
./docker.sh run web

# Run stdio server
./docker.sh run stdio
```

### Method 2: Building Your Custom Image

```bash
# Build your custom image from an example
docker build -f examples/Dockerfile.user -t my-poshmcp .

# Or build with custom modules directly
docker build -f examples/Dockerfile.azure -t poshmcp-azure .

# Run your custom image
docker run -d -p 8080:8080 my-poshmcp
```

## Prerequisites

- Docker Engine 20.10+
- Docker Compose 2.0+ (optional, for orchestration)
- PowerShell 7+ (for local development with install-modules.ps1)

---

## Creating Your Custom Image

The recommended approach is to create your own Dockerfile that extends the PoshMcp base image.

### Step 1: Build the Base Image

```bash
# Build the base PoshMcp image (no modules)
./docker.sh build
# This creates: poshmcp:latest
```

### Step 2: Create Your Dockerfile

Choose an approach based on your needs:

#### Option A: Use Example Templates

```bash
# Basic example (Pester + PSScriptAnalyzer)
docker build -f examples/Dockerfile.user -t my-poshmcp .

# Azure example (Az.* modules)
docker build -f examples/Dockerfile.azure -t poshmcp-azure .

# Advanced example (multi-stage build, custom scripts)
docker build -f examples/Dockerfile.custom -t poshmcp-custom .
```

#### Option B: Write Your Own Dockerfile

```dockerfile
# Your Dockerfile
FROM poshmcp:latest

USER root

# Copy module installer
COPY install-modules.ps1 /tmp/

# Install your modules
ENV INSTALL_PS_MODULES="YourModule1 YourModule2@1.2.3"
RUN pwsh /tmp/install-modules.ps1 && rm /tmp/install-modules.ps1

# Copy your configuration
COPY my-appsettings.json /app/web/appsettings.json
COPY my-appsettings.json /app/server/appsettings.json

USER appuser
```

### Step 3: Build and Run Your Image

```bash
# Build your custom image
docker build -t my-company-poshmcp .

# Run it
docker run -d -p 8080:8080 \
  -e POSHMCP_MODE=web \
  --name my-poshmcp \
  my-company-poshmcp
```

---

## Module Installation Script

The `install-modules.ps1` script provides robust module installation with:
- Version constraint support (`@1.2.3`, `@>=1.0.0`, `@<=2.0.0`)
- Proper error handling (fail-fast on errors)
- Progress reporting and installation summary
- Works in Docker builds or local development

### Usage in Dockerfile

```dockerfile
# Copy script to temp location
COPY install-modules.ps1 /tmp/

# Set modules via environment variable
ENV INSTALL_PS_MODULES="Pester@>=5.0.0 PSScriptAnalyzer"

# Run the script
RUN pwsh /tmp/install-modules.ps1 && rm /tmp/install-modules.ps1
```

### Usage Locally

```powershell
# Install modules on your local machine
./install-modules.ps1 -Modules "Pester PSScriptAnalyzer" -Scope CurrentUser

# With version constraints
./install-modules.ps1 -Modules "Az.Accounts@>=2.0.0,Pester@5.5.0"

# From environment variable
$env:INSTALL_PS_MODULES = "Pester PSScriptAnalyzer"
./install-modules.ps1
```

### Module Version Syntax

| Syntax | Meaning | Example |
|--------|---------|---------|
| `ModuleName` | Latest version | `Pester` |
| `ModuleName@1.2.3` | Exact version | `Pester@5.5.0` |
| `ModuleName@>=1.0.0` | Minimum version | `Az.Accounts@>=2.12.0` |
| `ModuleName@<=2.0.0` | Maximum version | `Pester@<=5.9.0` |

---

## Example Dockerfiles

### examples/Dockerfile.user
Basic pattern showing module installation and custom config.
- Modules: Pester, PSScriptAnalyzer
- Single-stage build
- Custom appsettings.json

### examples/Dockerfile.azure
Azure-focused image with Az modules.
- Modules: Az.Accounts, Az.Resources, Az.Storage, Az.KeyVault
- Azure startup script integration
- Managed identity support

### examples/Dockerfile.custom
Advanced multi-stage build pattern.
- Multiple module sets
- Additional system dependencies
- Volume mounts for logs and scripts
- Custom helper scripts

---

## Backward Compatibility (Legacy Approach)

**⚠️ Deprecated:** The old approach of using build arguments directly in the base Dockerfile is still technically possible but not recommended. Use the derived image pattern instead.

<details>
<summary>Click to view legacy build argument approach</summary>

### Legacy: Build Arguments in Base Dockerfile

This approach embeds modules into the base image, which violates the separation of concerns.

```bash
# Using docker.sh (deprecated)
./docker.sh build --modules "Pester PSScriptAnalyzer"

# Using environment variable (deprecated)
export INSTALL_PS_MODULES="Pester PSScriptAnalyzer"
docker build --build-arg INSTALL_PS_MODULES="$INSTALL_PS_MODULES" -t poshmcp .
```

**Why this is deprecated:**
- ❌ Mixes base runtime with customization
- ❌ Every user must rebuild the entire base image
- ❌ Hard to maintain and version control
- ❌ Violates Docker layering best practices

**Migration path:**
1. Build base image without modules: `./docker.sh build`
2. Create your own Dockerfile using `FROM poshmcp:latest`
3. Use `install-modules.ps1` in your derived image

</details>

---

## Server Modes

PoshMcp supports two operational modes controlled by the `POSHMCP_MODE` environment variable.

### Web/HTTP Mode (Default)
Runs the ASP.NET Core web server that exposes MCP over HTTP:
- Accessible at `http://localhost:8080`
- Used by VS Code MCP extension with HTTP transport
- Suitable for development and web-based integrations
- Includes health check endpoints at `/health`

### stdio Mode  
Runs the console application that communicates over stdin/stdout:
- Used for direct stdio communication
- Suitable for command-line tools and stdio-based MCP clients
- Requires direct process interaction
- No HTTP endpoints

---

## Environment Variables

### Mode Selection
- `POSHMCP_MODE` - Controls which server to start:
  - `web` or `http` - Start web server (default)
  - `stdio` or `server` - Start stdio server

### Web Server Configuration
- `ASPNETCORE_ENVIRONMENT` - Set to `Production`, `Development`, or `Staging`
- `ASPNETCORE_URLS` - URLs the server listens on (default: `http://+:8080`)

### Module Installation (for install-modules.ps1)
- `INSTALL_PS_MODULES` - Space or comma-separated list of modules
- `MODULE_INSTALL_SCOPE` - `AllUsers` or `CurrentUser` (default: `AllUsers`)
- `SKIP_PUBLISHER_CHECK` - Skip publisher validation (default: `true`)

---

## Running Docker Containers

### Using docker.sh Helper Script

```bash
# Build base image
./docker.sh build

# Run web server
./docker.sh run web

# Run stdio server
./docker.sh run stdio

# # Manual Docker Commands

```bash
# Build base image
docker build -t poshmcp .

# Build your custom image
docker build -f examples/Dockerfile.user -t my-poshmcp .

# Run web server
docker run -d -p 8080:8080 -e POSHMCP_MODE=web --name my-poshmcp-web my-poshmcp

# Run stdio server
docker run -it -e POSHMCP_MODE=stdio --name my-poshmcp-stdio my-poshmcp

# View logs
docker logs -f my-poshmcp-web

# Stop containers
docker stop my-poshmcp-web
docker rm my-poshmcp-web
```

---
./docker.sh logs

# Stop containers
./docker.sh stop

# Clean up
./docker.sh clean
```

## Docker Compose Profiles

## Docker Compose

The `docker-compose.yml` uses profiles to manage different modes:

```bash
# Start web server
docker-compose --profile web up -d

# Start stdio server  
docker-compose --profile stdio up -d

# Stop all
docker-compose --profile web --profile stdio down
```

**Note:** For custom images, create your own `docker-compose.yml` or extend the existing one with your image name.

---

## Custom Configuration

### Mounting Configuration Files

```bash
# Web server with custom config
docker run -d -p 8080:8080 -e POSHMCP_MODE=web \
  -v $(pwd)/my-appsettings.json:/app/web/appsettings.json:ro \
  my-poshmcp

# stdio server with custom config
docker run -it -e POSHMCP_MODE=stdio \
  -v $(pwd)/my-appsettings.json:/app/server/appsettings.json:ro \
  my-poshmcp
```

### Runtime Module Installation

If you prefer runtime module installation (not recommended for production):

```json
{
  "PowerShellConfiguration": {
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

### Pre-Installed Module Configuration

After pre-installing modules in your Dockerfile, use `ImportModules`:

```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "ImportModules": [
        "Pester",
        "PSScriptAnalyzer",
        "Az.Accounts"
      ]
    }
  }
}
```

---

## VS Code MCP Configuration

### HTTP Mode
Configure VS Code to use the web server:
```json
{
  "servers": {
    "my-poshmcp": {
      "type": "http",
      "url": "http://localhost:8080"
    }
  }
}
```

### stdio Mode with Docker
Configure VS Code to use the stdio server via Docker:
```json
{
  "servers": {
    "my-poshmcp": {
      "type": "stdio", 
      "command": "docker",
      "args": [
        "run", "--rm", "-i",
        "-e", "POSHMCP_MODE=stdio",
        "my-poshmcp"
      ]
    }
  }
}
```

**Note:** Replace `my-poshmcp` with your custom image name.

---

## Architecture Details

### Multi-Stage Build

The base Dockerfile uses a multi-stage build pattern:

1. **Build Stage** (`mcr.microsoft.com/dotnet/sdk:8.0`)
   - Restores NuGet dependencies
   - Compiles both PoshMcp.Web and PoshMcp.Server projects
   - Publishes to separate output directories

2. **Runtime Stage** (`mcr.microsoft.com/dotnet/aspnet:8.0`)
   - Installs PowerShell 7.x
   - Copies compiled applications
   - Creates non-root user for security
   - Sets up entrypoint script

### Security Best Practices

- ✅ Runs as non-root user (`appuser`, UID 1001)
- ✅ Minimal attack surface (only runtime dependencies)
- ✅ Read-only filesystem support (mount configs as `:ro`)
- ✅ No unnecessary privileges
- ✅ PowerShell modules installed to system location

### Image Layering Strategy

```
Base layer:     poshmcp:latest (runtime only, ~500MB)
  └─ Your layer: module installation (~50-200MB per layer)
  └─ Your layer: custom configuration (~1-10KB)
  └─ Your layer: custom scripts (varies)
```

**Benefits:**
- Base layer cached and reused
- Only rebuild custom layers when needed
- Smaller incremental builds
- Easy to update base runtime

---

## Troubleshooting

### Common Issues

**Port already in use**
```bash
# Change the host port
docker run -d -p 8081:8080 my-poshmcp
```

**Module installation fails in Dockerfile**
```dockerfile
# Check PowerShell Gallery connectivity
RUN pwsh -c "Test-NetConnection -ComputerName www.powershellgallery.com -Port 443"

# Add verbose logging to install-modules.ps1
RUN pwsh /tmp/install-modules.ps1 -Verbose
```

**Permission denied errors**
```bash
# Ensure non-root user has access
USER root
RUN chown -R appuser:appuser /app
USER appuser
```

**Custom appsettings.json not loading**
```bash
# Verify file is copied correctly
docker exec my-poshmcp ls -la /app/web/appsettings.json
docker exec my-poshmcp cat /app/web/appsettings.json
```

### Health Checks (Web Mode Only)

```bash
# Check container health status
docker ps  # Look for "healthy" status

# Manual health check
curl http://localhost:8080/health

# Detailed health information
curl http://localhost:8080/health | jq
```

### Logs and Debugging

```bash
# View container logs
docker logs -f my-poshmcp-web

# Execute commands inside container
docker exec -it my-poshmcp-web bash

# Check PowerShell is available
docker exec -it my-poshmcp-web pwsh --version

# List installed modules
docker exec -it my-poshmcp-web pwsh -c "Get-Module -ListAvailable"

# Test module installation script
docker exec -it my-poshmcp-web pwsh -c "./install-modules.ps1 -Modules Pester -Verbose"
```

---

## Migration Guide

### Migrating from Build Argument Approach

If you were using the old `INSTALL_PS_MODULES` build argument:

**Old approach:**
```bash
docker build --build-arg INSTALL_PS_MODULES="Pester PSScriptAnalyzer" -t poshmcp .
```

**New approach:**
1. Create `Dockerfile.mycompany`:
   ```dockerfile
   FROM poshmcp:latest
   USER root
   COPY install-modules.ps1 /tmp/
   ENV INSTALL_PS_MODULES="Pester PSScriptAnalyzer"
   RUN pwsh /tmp/install-modules.ps1 && rm /tmp/install-modules.ps1
   COPY my-appsettings.json /app/web/appsettings.json
   USER appuser
   ```

2. Build:
   ```bash
   # Build base first
   docker build -t poshmcp .
   
   # Build your custom image
   docker build -f Dockerfile.mycompany -t mycompany-poshmcp .
   ```

**Benefits of migration:**
- Separation of concerns (base vs customization)
- Easier to update base image
- Your Dockerfile can be version controlled
- Reusable across team

---

## Additional Resources

- [Environment Customization Guide](docs/ENVIRONMENT-CUSTOMIZATION.md) - Comprehensive guide to PowerShell environment setup
- [Docker Build Modules Documentation](docs/DOCKER-BUILD-MODULES.md) - Legacy documentation (deprecated)
- [Examples README](examples/README.md) - More examples and use cases
- [Base Dockerfile](Dockerfile) - Base image source
- [install-modules.ps1](install-modules.ps1) - Module installation script source

## Production Deployment

For production environments, consider:

1. **Reverse Proxy**: Use nginx or Traefik in front of the container
2. **SSL/TLS**: Terminate SSL at the reverse proxy level
3. **Resource Limits**: Set memory and CPU limits in `docker-compose.yml`
4. **Monitoring**: Add health check endpoints and monitoring tools
5. **Secrets Management**: Use Docker secrets or external secret management

Example production docker-compose additions:

```yaml
services:
  poshmcp-web:
    # ... existing configuration
    deploy:
      resources:
        limits:
          memory: 1G
          cpus: '0.5'
        reservations:
          memory: 512M
          cpus: '0.25'
    restart: unless-stopped
```
