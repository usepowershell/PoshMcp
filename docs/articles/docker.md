---
uid: docker
title: Docker Deployment
---

# Docker Deployment

Deploy PoshMcp in Docker containers for consistent environments and cloud deployment.

## Building a Docker Image

Clone and build:

```bash
git clone https://github.com/microsoft/poshmcp.git
cd poshmcp

docker build -t poshmcp:latest .
```

## Running the Container

### HTTP Mode

```bash
docker run -p 8080:8080 poshmcp:latest
```

### Stdio Mode

```bash
docker run -it poshmcp:latest poshmcp serve --transport stdio
```

## Pre-installing PowerShell Modules

Build a custom image with modules pre-installed:

```bash
docker build \
  --build-arg MODULES="Az.Accounts Az.Resources Az.Storage" \
  -t poshmcp:azure .
```

Or configure at runtime:

```bash
docker run \
  -e POSHMCP_MODULES="Az.Accounts Az.Resources" \
  -p 8080:8080 \
  poshmcp:latest
```

## Custom Configuration

Mount your `appsettings.json`:

```bash
docker run -v $(pwd)/appsettings.json:/app/appsettings.json \
  -p 8080:8080 \
  poshmcp:latest
```

## Environment Variables

```bash
docker run \
  -e POSHMCP_TRANSPORT=http \
  -e POSHMCP_LOG_LEVEL=debug \
  -e POSHMCP_MODULES="Az.Accounts" \
  -p 8080:8080 \
  poshmcp:latest
```

## Azure Container Apps Deployment

See [Azure Integration](azure-integration.md) for full deployment guide with Managed Identity setup, monitoring, and scaling.

Quick deploy:

```bash
cd infrastructure/azure
./deploy.sh
```

---

**Next:** [Environment Customization](environment.md) | [Azure Integration](azure-integration.md)
