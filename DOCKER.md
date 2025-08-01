# Docker Deployment for PoshMcp Server

This directory contains Docker configuration files to containerize and deploy both the PoshMcp Web Server (HTTP) and stdio Server.

## Files

- `Dockerfile` - Multi-stage Docker build configuration for both server modes
- `docker-compose.yml` - Orchestration configuration with profiles for different modes  
- `.dockerignore` - Excludes unnecessary files from Docker build context
- `docker.sh` - Convenience script for common Docker operations
- `docker-entrypoint.sh` - Startup script that chooses server mode based on environment variable

## Quick Start

### Prerequisites

- Docker Engine 20.10+
- Docker Compose 2.0+

### Build and Run

```bash
# Build the Docker image (supports both modes)
./docker.sh build

# Start the web server (HTTP mode)
./docker.sh run web

# OR start the stdio server  
./docker.sh run stdio

# View logs
./docker.sh logs

# Stop the server
./docker.sh stop

# Clean up (remove containers and images)
./docker.sh clean
```

## Server Modes

### Web/HTTP Mode (Default)
Runs the ASP.NET Core web server that exposes MCP over HTTP:
- Accessible at `http://localhost:8080`
- Used by VS Code MCP extension with HTTP transport
- Suitable for development and web-based integrations

### stdio Mode  
Runs the console application that communicates over stdin/stdout:
- Used for direct stdio communication
- Suitable for command-line tools and stdio-based MCP clients
- Requires direct process interaction

## Environment Variables

### Mode Selection
- `POSHMCP_MODE` - Controls which server to start:
  - `web` or `http` - Start web server (default)
  - `stdio` or `server` - Start stdio server

### Web Server Configuration
- `ASPNETCORE_ENVIRONMENT` - Set to `Production`, `Development`, or `Staging`
- `ASPNETCORE_URLS` - URLs the server listens on (default: `http://+:8080`)

## Manual Docker Commands

### Web Server Mode
```bash
# Build the image
docker build -t poshmcp .

# Run web server
docker run -d -p 8080:8080 -e POSHMCP_MODE=web --name poshmcp-web poshmcp

# View logs
docker logs -f poshmcp-web
```

### stdio Server Mode
```bash
# Run stdio server
docker run -it -e POSHMCP_MODE=stdio --name poshmcp-stdio poshmcp

# For background stdio server
docker run -d -e POSHMCP_MODE=stdio --name poshmcp-stdio poshmcp
```

## Docker Compose Profiles

The `docker-compose.yml` uses profiles to manage different modes:

```bash
# Start web server
docker-compose --profile web up -d

# Start stdio server  
docker-compose --profile stdio up -d

# Stop all
docker-compose --profile web --profile stdio down
```

## Custom Configuration

### Web Server Configuration
Mount your `appsettings.json` file:
```bash
docker run -d -p 8080:8080 -e POSHMCP_MODE=web \
  -v /path/to/web-appsettings.json:/app/web/appsettings.json:ro \
  poshmcp
```

### stdio Server Configuration  
Mount your `appsettings.json` file:
```bash
docker run -it -e POSHMCP_MODE=stdio \
  -v /path/to/server-appsettings.json:/app/server/appsettings.json:ro \
  poshmcp
```

## VS Code MCP Configuration

### HTTP Mode
Configure VS Code to use the web server:
```json
{
  "servers": {
    "poshmcp-server": {
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
    "poshmcp-server": {
      "type": "stdio", 
      "command": "docker",
      "args": [
        "run", "--rm", "-i",
        "-e", "POSHMCP_MODE=stdio",
        "poshmcp"
      ]
    }
  }
}
```

## Fixing Multiple appsettings.json Files

When building .NET projects with multiple `appsettings.json` files (like PoshMcp.Web and PoshMcp.Server), you may encounter conflicts during `dotnet publish`. This project implements several solutions:

### 1. Project File Configuration
The `PoshMcp.Server/PoshMcp.csproj` excludes content files from publish operations:
```xml
<Content Include="appsettings*.json">
  <CopyToPublishDirectory>Never</CopyToPublishDirectory>
</Content>
```

### 2. Dockerfile Safeguards
The Dockerfile includes a step to ensure the correct appsettings.json is used:
```dockerfile
# Remove any conflicting appsettings files from referenced projects
RUN if [ -f /app/publish/appsettings.json ] && [ -f /src/PoshMcp.Server/appsettings.json ]; then \
        echo "Multiple appsettings.json detected, ensuring Web project config is used..."; \
        cp /src/PoshMcp.Web/appsettings.json /app/publish/appsettings.json; \
    fi
```

### 3. Alternative Solutions
If you encounter similar issues in other projects, consider:

- **Exclude content files**: Use `/p:CopyOutputSymbolsToPublishDirectory=false` during publish
- **Separate configurations**: Name configuration files differently (e.g., `appsettings.server.json`)
- **Environment-specific configs**: Use `appsettings.{Environment}.json` pattern
- **Runtime configuration**: Load settings from environment variables or external sources

## Architecture

The Dockerfile uses a multi-stage build:

1. **Build Stage** - Uses `mcr.microsoft.com/dotnet/sdk:8.0` to compile the application
2. **Runtime Stage** - Uses `mcr.microsoft.com/dotnet/aspnet:8.0` with PowerShell installed

### Security Features

- Runs as non-root user (`appuser`)
- Only includes runtime dependencies in final image
- Excludes development files and secrets via `.dockerignore`

## Troubleshooting

### Common Issues

1. **Port already in use**: Change the host port in `docker-compose.yml` or use `-p 8081:8080`
2. **PowerShell not found**: The Dockerfile installs PowerShell automatically
3. **Permission denied**: Ensure the `docker.sh` script is executable: `chmod +x docker.sh`
4. **Multiple appsettings.json conflicts**: This is handled automatically by the Dockerfile safeguards

### Health Checks

The container includes a health check that verifies the web server is responding. Check status with:

```bash
docker ps  # Look for "healthy" status
```

### Logs and Debugging

```bash
# View detailed logs
docker-compose logs -f

# Execute commands inside the container
docker exec -it poshmcp-web bash

# Check PowerShell availability
docker exec -it poshmcp-web pwsh --version
```

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
