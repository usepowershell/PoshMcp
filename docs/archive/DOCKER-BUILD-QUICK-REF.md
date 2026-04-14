# Docker build quick reference

> **⚠️ Advanced Docker Patterns**
> For most use cases, use `poshmcp build` and `poshmcp run` commands (see [DOCKER.md](../DOCKER.md)).
> This document covers advanced scenarios and manual Docker workflows.

This page previously contained a quick reference for building Docker images with pre-installed PowerShell modules using build arguments. That approach is now **deprecated** in favor of the derived image pattern documented in [DOCKER.md](../DOCKER.md).

## Quick commands

```bash
# Build the base image
./docker.sh build

# Build a custom image with your modules
docker build -f examples/Dockerfile.user -t my-poshmcp .

# Run the web server
docker run -d -p 8080:8080 my-poshmcp
```

For module version syntax, custom image patterns, and troubleshooting, see [DOCKER.md](../DOCKER.md).

---

## See also

- [DOCKER.md](../DOCKER.md) — full Docker deployment guide (canonical)
- [Environment customization guide](ENVIRONMENT-CUSTOMIZATION.md) — runtime module configuration
- [Examples](../examples/) — Dockerfile templates and Docker Compose files
