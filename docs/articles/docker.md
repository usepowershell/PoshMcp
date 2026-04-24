---
uid: docker
title: Docker Deployment
---

# Docker Deployment

Deploy PoshMcp in Docker containers for consistent environments and cloud deployment.

## Build Images with `poshmcp build`

Use the CLI as the default workflow:

```bash
# Default: build custom image from GHCR base image
poshmcp build --tag myorg/poshmcp:latest

# Custom image with pre-installed modules
poshmcp build --modules "Az.Accounts Az.Resources" --tag myorg/poshmcp:azure

# Local/source base image build from repository Dockerfile
poshmcp build --type base --tag poshmcp:latest

# Pin custom build source image and tag
poshmcp build --source-image ghcr.io/usepowershell/poshmcp/poshmcp --source-tag 0.9.0
```

### Build option semantics

- `poshmcp build` defaults to `--type custom`.
- `--type custom` uses `examples/Dockerfile.user` and layers on a source image.
- `--type base` builds the local runtime/source image from the repo `Dockerfile`.
- Default source image is `ghcr.io/usepowershell/poshmcp/poshmcp:latest`.
- `--source-image` and `--source-tag` apply to custom builds.

## Running the Container

### HTTP Mode

```bash
docker run -p 8080:8080 poshmcp:latest
```

### Stdio Mode

```bash
docker run -it poshmcp:latest poshmcp serve --transport stdio
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
  -p 8080:8080 \
  poshmcp:latest
```

## Direct Docker Build (Advanced)

If needed, you can still call Docker directly:

```bash
docker build -t poshmcp:latest .
docker build -f examples/Dockerfile.user -t myorg/poshmcp:latest .
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
