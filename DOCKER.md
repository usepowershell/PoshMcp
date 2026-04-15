# Container Architecture & Operations

## Overview

PoshMcp containerization is CLI-first (`poshmcp build` / `poshmcp run`) with equivalent Docker/Podman workflows also supported. The same patterns work locally or in CI/CD pipelines.

## Quick Start

```bash
# Build
poshmcp build --modules "Az.Accounts" --tag myorg/poshmcp:latest

# Run
poshmcp run --mode http --port 8080 --tag myorg/poshmcp:latest

# Help (complete option reference)
poshmcp build --help
poshmcp run --help
```

Docker-native equivalent:

```bash
docker build -t myorg/poshmcp:latest .
docker run -d -p 8080:8080 -e POSHMCP_TRANSPORT=http myorg/poshmcp:latest
```

## Architecture

Single-application containerization using the PoshMcp.Server binary. HTTP transport is integrated into the CLI's `serve` command---no separate web app binary is needed in containers.

### What's in the container
- .NET 10 runtime
- PowerShell environment
- `poshmcp` binary (CLI tool)

### Entry point
Container entrypoint is `docker-entrypoint.sh`, which launches:
`/app/server/poshmcp serve --transport "$POSHMCP_TRANSPORT"`

---

## Azure Deployment

Run the same image in Azure Container Apps or App Service:

```bash
# Build locally and push to Azure Container Registry
poshmcp build --tag myregistry.azurecr.io/poshmcp:latest
docker push myregistry.azurecr.io/poshmcp:latest

# Create container app
az containerapp create \
    --image myregistry.azurecr.io/poshmcp:latest \
    --environment-variables POSHMCP_TRANSPORT=http \
    --ingress external --target-port 8080
```

See [infrastructure/azure/README.md](infrastructure/azure/README.md) for the full Azure walkthrough including Managed Identity, monitoring, and scaling.

---

## Advanced Scenarios

### Pre-installing modules
Modules can be installed at build time (faster startup) or at runtime.

**Build-time:**
```bash
poshmcp build --modules "Az.Accounts Az.KeyVault" --tag myorg/poshmcp:prod
```

**Runtime:** Mount a config file with a modules list, or use `poshmcp update-config --add-module ...` inside the container.

### Environment customization
- `POSHMCP_CONFIGURATION` --- path to appsettings.json
- `POSHMCP_TRANSPORT` --- `http` or `stdio` (default: `http`)
- `POSHMCP_LOG_LEVEL` --- `trace|debug|info|warn|error`

Run `poshmcp doctor --help` for all diagnostic options.

### Custom images & multi-stage builds
For advanced scenarios (multiple build stages, custom init), use a custom Dockerfile and invoke directly via docker/podman, or create a derived image:

```dockerfile
FROM poshmcp:latest
USER root
COPY install-modules.ps1 /tmp/
ENV INSTALL_PS_MODULES="YourModule1 YourModule2"
RUN pwsh /tmp/install-modules.ps1 && rm /tmp/install-modules.ps1
COPY my-appsettings.json /app/server/appsettings.json
USER appuser
```

See [examples/](examples/) for reference Dockerfile patterns.

### Using docker-compose (legacy approach)
If you prefer docker-compose, set `POSHMCP_TRANSPORT` per service:

```yaml
services:
  poshmcp:
    image: poshmcp:latest
    environment:
      POSHMCP_TRANSPORT: http
    ports:
      - "8080:8080"
```

See [examples/docker-compose.environment.yml](examples/docker-compose.environment.yml) for a full example.

### Image layering strategy

```
Base layer:     poshmcp:latest (runtime only)
  └─ Your layer: module installation
  └─ Your layer: custom configuration
  └─ Your layer: custom scripts
```

Base layer is cached and reused---only rebuild custom layers when needed.

### VS Code MCP configuration

**HTTP mode:**
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

**stdio mode via docker:**
```json
{
  "servers": {
    "my-poshmcp": {
      "type": "stdio",
      "command": "docker",
      "args": ["run", "--rm", "-i", "my-poshmcp"]
    }
  }
}
```

---

## Troubleshooting

### `poshmcp build` not found
Ensure PoshMcp is installed: `dotnet tool install -g PoshMcp` or build locally: `dotnet build` then `dotnet run -- build --help`.

### Container exits with "configuration error"
Run `poshmcp doctor` locally to debug config issues. The same diagnostics apply inside the container.

### `poshmcp run` says docker not available
Ensure docker or podman is installed and in PATH.

### Module installation fails at build time
Check PowerShell Gallery connectivity or add verbose logging:
```bash
docker run --rm poshmcp:latest pwsh -c "Test-NetConnection -ComputerName www.powershellgallery.com -Port 443"
```

### Health checks (HTTP mode)
```bash
curl http://localhost:8080/health
curl http://localhost:8080/health/ready
```

---

## See Also
- `poshmcp --help` --- Full CLI reference
- `poshmcp serve --help` --- Server transport modes
- [infrastructure/azure/](infrastructure/azure/) --- Azure deployment guide
- [examples/](examples/) --- Sample Dockerfiles and configurations
- [docs/articles/environment.md](docs/articles/environment.md) --- PowerShell environment setup