# Docker build with pre-installed modules

> **⚠️ Deprecated:** The build-argument approach described here is superseded by the **derived image pattern** in [DOCKER.md](../DOCKER.md). Use that guide for new projects.

This page is kept as a reference for teams still using the legacy `INSTALL_PS_MODULES` build argument. For the recommended approach, create your own Dockerfile that extends `poshmcp:latest` — see [DOCKER.md § Creating your custom image](../DOCKER.md#creating-your-custom-image).

## Legacy build-argument approach

```bash
# Build with modules embedded in the base image (deprecated)
./docker.sh build --modules "Pester PSScriptAnalyzer"

# Or via environment variable
export INSTALL_PS_MODULES="Pester PSScriptAnalyzer"
docker build --build-arg INSTALL_PS_MODULES="$INSTALL_PS_MODULES" -t poshmcp .
```

### Module version syntax

| Syntax | Meaning | Example |
|--------|---------|---------|
| `ModuleName` | Latest version | `Pester` |
| `ModuleName@X.Y.Z` | Exact version | `Pester@5.5.0` |
| `ModuleName@>=X.Y.Z` | Minimum version | `Az.Accounts@>=2.0.0` |
| `ModuleName@<=X.Y.Z` | Maximum version | `Pester@<=5.9.0` |

## Migration to derived image pattern

1. Build the base image without modules: `./docker.sh build`
2. Create your own Dockerfile using `FROM poshmcp:latest`
3. Use `install-modules.ps1` in your derived image

See the [backward compatibility section in DOCKER.md](../DOCKER.md#backward-compatibility-legacy-approach) for a step-by-step migration.

---

## See also

- [DOCKER.md](../DOCKER.md) — full Docker deployment guide (canonical)
- [Environment customization guide](ENVIRONMENT-CUSTOMIZATION.md) — runtime module configuration
- [Examples](../examples/) — Dockerfile templates and Docker Compose files
