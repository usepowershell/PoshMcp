# Docker Build with Pre-Installed Modules

This guide explains how to build PoshMcp Docker images with PowerShell modules pre-installed at build time for optimal performance.

## Why Pre-Install Modules?

**Runtime Installation (Default):**
- ❌ Slower startup (2-10 seconds per module)
- ❌ Network dependency during container start
- ❌ Repeated downloads on every container restart
- ✅ Flexible - easy to change module versions

**Build-Time Installation (Optimized):**
- ✅ Faster startup (50-200ms per module)
- ✅ No network dependency at runtime
- ✅ Modules cached in image layers
- ❌ Requires image rebuild to change modules
- ❌ Slightly larger image size

**Recommendation:** Pre-install stable, frequently-used modules at build time. Install optional or frequently-changing modules at runtime.

---

## Quick Start

### Method 1: Command Line

```bash
# Basic build with modules
./docker.sh build --modules "Pester PSScriptAnalyzer"

# Build with version constraints
./docker.sh build --modules "Az.Accounts@2.0.0 Pester@>=5.0.0"

# Build with comma-separated list
./docker.sh build --modules "Pester@5.5.0,Az.Resources,PSScriptAnalyzer"
```

### Method 2: Environment Variable

```bash
# Set environment variable
export INSTALL_PS_MODULES="Pester PSScriptAnalyzer Az.Accounts"

# Build using environment
./docker.sh build

# Or with docker directly
docker build --build-arg INSTALL_PS_MODULES="$INSTALL_PS_MODULES" -t poshmcp .
```

### Method 3: .env File

```bash
# 1. Copy example file
cp .env.example .env

# 2. Edit .env and set INSTALL_PS_MODULES
# INSTALL_PS_MODULES="Pester PSScriptAnalyzer"

# 3. Build (docker-compose will read .env automatically)
docker-compose build
```

---

## Module Syntax

### Basic Syntax

```bash
# Install latest version
INSTALL_PS_MODULES="Pester"

# Multiple modules (space-separated)
INSTALL_PS_MODULES="Pester PSScriptAnalyzer Az.Accounts"

# Multiple modules (comma-separated)  
INSTALL_PS_MODULES="Pester,PSScriptAnalyzer,Az.Accounts"
```

### Version Constraints

```bash
# Specific version
"ModuleName@1.2.3"

# Minimum version
"ModuleName@>=1.0.0"

# Maximum version
"ModuleName@<=2.0.0"

# Mixed example
"Pester@5.5.0 Az.Accounts@>=2.0.0 PSScriptAnalyzer"
```

---

## Build Arguments

| Argument | Description | Default |
|----------|-------------|---------|
| `INSTALL_PS_MODULES` | Space or comma-separated list of modules | `""` (empty) |
| `MODULE_INSTALL_SCOPE` | Installation scope (`AllUsers` or `CurrentUser`) | `AllUsers` |
| `SKIP_PUBLISHER_CHECK` | Skip publisher validation (`true` or `false`) | `true` |

### Using Build Arguments Directly

```bash
docker build \
  --build-arg INSTALL_PS_MODULES="Pester PSScriptAnalyzer" \
  --build-arg MODULE_INSTALL_SCOPE="AllUsers" \
  --build-arg SKIP_PUBLISHER_CHECK="true" \
  -t poshmcp .
```

---

## Common Scenarios

### Scenario 1: Azure Automation

**Build command:**
```bash
./docker.sh build --modules "Az.Accounts@>=2.0.0 Az.Resources Az.Storage"
```

**appsettings.json (runtime):**
```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "ImportModules": [
        "Az.Accounts",
        "Az.Resources", 
        "Az.Storage"
      ],
      "StartupScript": "Connect-AzAccount -Identity"
    }
  }
}
```

### Scenario 2: Testing & Quality

**Build command:**
```bash
./docker.sh build --modules "Pester@>=5.0.0 PSScriptAnalyzer Plaster"
```

**appsettings.json (runtime):**
```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "ImportModules": ["Pester", "PSScriptAnalyzer", "Plaster"]
    }
  }
}
```

### Scenario 3: Minimal Base Image

**Build command:**
```bash
./docker.sh build
# No modules pre-installed
```

**appsettings.json (runtime):**
```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "InstallModules": [
        {"Name": "CustomModule", "Version": "1.0.0"}
      ]
    }
  }
}
```

**Use Case:** When you need maximum flexibility and don't mind slower startup.

### Scenario 4: Hybrid Approach (Recommended)

**Build command:**
```bash
# Pre-install stable, core modules
./docker.sh build --modules "Az.Accounts@2.12.0 PSScriptAnalyzer@1.21.0"
```

**appsettings.json (runtime):**
```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "ImportModules": [
        "Az.Accounts",
        "PSScriptAnalyzer"
      ],
      "InstallModules": [
        {"Name": "ProjectSpecificModule", "Version": "1.0.0"}
      ]
    }
  }
}
```

**Best Practice:** Pre-install stable modules, install project-specific modules at runtime.

---

## Dockerfile Build Process

The Dockerfile uses a multi-line PowerShell script during the `runtime` stage:

```dockerfile
ARG INSTALL_PS_MODULES=""
ARG MODULE_INSTALL_SCOPE="AllUsers"
ARG SKIP_PUBLISHER_CHECK="true"

RUN if [ -n "$INSTALL_PS_MODULES" ]; then \
    pwsh -NoProfile -NonInteractive -Command "
        # Trust PSGallery
        Set-PSRepository -Name PSGallery -InstallationPolicy Trusted;
        
        # Parse and install each module
        \$moduleList = '$INSTALL_PS_MODULES' -replace '[,;]', ' ' -split '\s+';
        foreach (\$moduleSpec in \$moduleList) {
            # Parse version constraints and install
            Install-Module -Name \$moduleName -Scope '$MODULE_INSTALL_SCOPE' ...
        }
    ";
fi
```

**Key features:**
- ✅ Supports both space and comma separation
- ✅ Parses version constraints (`@`, `>=`, `<=`)
- ✅ Graceful failure (logs warnings, continues)
- ✅ Shows installed modules after completion
- ✅ Runs before user switch (has root privileges)

---

## Verification

After building, verify modules are installed:

```bash
# Build with modules
./docker.sh build --modules "Pester PSScriptAnalyzer"

# Run container
docker run --rm -it poshmcp pwsh

# Inside container, check modules
PS> Get-Module -ListAvailable
PS> Get-Module -ListAvailable -Name Pester
PS> Import-Module Pester; Get-Command -Module Pester
```

---

## Performance Comparison

### Test: Import 3 Azure Modules

| Approach | Startup Time | Notes |
|----------|-------------|-------|
| Runtime install | ~25-30 seconds | Downloads from PSGallery |
| Pre-installed (build) | ~0.5-1 second | Just imports |
| No modules | ~0.1 seconds | Baseline |

**Test command:**
```bash
time docker run --rm poshmcp pwsh -Command "
    Import-Module Az.Accounts, Az.Resources, Az.Storage;
    Write-Host 'Modules loaded'
"
```

---

## Image Size Impact

| Configuration | Image Size | Delta |
|--------------|------------|-------|
| Base (no modules) | ~850 MB | - |
| + Pester | ~860 MB | +10 MB |
| + Azure modules (Az.*) | ~950 MB | +100 MB |
| + All common modules | ~1.2 GB | +350 MB |

**Recommendation:** Only pre-install modules you actually use.

---

## Troubleshooting

### Module Installation Fails

**Symptom:** Build fails with "Module X not found"

**Solution:**
```bash
# Check module name spelling
# Check module is available on PSGallery
pwsh -Command "Find-Module -Name ModuleName"

# Try building with verbose output
docker build --progress=plain \
  --build-arg INSTALL_PS_MODULES="ModuleName" \
  -t poshmcp .
```

### Publisher Check Errors

**Symptom:** Build hangs or fails with publisher validation error

**Solution:**
```bash
# Skip publisher check (faster, less secure)
docker build \
  --build-arg SKIP_PUBLISHER_CHECK="true" \
  --build-arg INSTALL_PS_MODULES="ModuleName" \
  -t poshmcp .
```

### Module Version Conflicts

**Symptom:** "Version X is not available"

**Solution:**
```bash
# Check available versions
pwsh -Command "Find-Module -Name ModuleName -AllVersions"

# Use exact version
./docker.sh build --modules "ModuleName@X.Y.Z"
```

---

## Docker Compose Integration

Update `docker-compose.yml` to use build arguments from `.env`:

```yaml
version: '3.8'
services:
  poshmcp-web:
    build:
      context: .
      args:
        INSTALL_PS_MODULES: ${INSTALL_PS_MODULES}
        MODULE_INSTALL_SCOPE: ${MODULE_INSTALL_SCOPE:-AllUsers}
        SKIP_PUBLISHER_CHECK: ${SKIP_PUBLISHER_CHECK:-true}
    image: poshmcp:latest
    # ... rest of config
```

Then build with:
```bash
# Set in .env file, then:
docker-compose build

# Or inline:
INSTALL_PS_MODULES="Pester PSScriptAnalyzer" docker-compose build
```

---

## CI/CD Integration

### GitHub Actions

```yaml
- name: Build Docker image with modules
  run: |
    docker build \
      --build-arg INSTALL_PS_MODULES="Az.Accounts Az.Resources" \
      -t ${{ secrets.DOCKER_REGISTRY }}/poshmcp:${{ github.sha }} \
      .
```

### Azure DevOps

```yaml
- task: Docker@2
  inputs:
    command: 'build'
    arguments: |
      --build-arg INSTALL_PS_MODULES="Az.Accounts Az.Resources"
      -t $(dockerRegistry)/poshmcp:$(Build.BuildId)
```

---

## Best Practices

1. ✅ **Pre-install stable modules** - Modules that rarely change
2. ✅ **Use version constraints** - Pin versions for reproducibility
3. ✅ **Keep .env.example updated** - Document module choices
4. ✅ **Test locally first** - Verify modules work before CI/CD
5. ✅ **Layer caching** - Order Dockerfile steps to maximize cache hits
6. ❌ **Don't pre-install everything** - Balance image size vs startup time
7. ❌ **Don't skip security** - Use publisher checks in production
8. ❌ **Don't pin to old versions** - Update modules periodically

---

## Examples Directory

See `examples/` for complete Docker Compose configurations:

- `docker-compose.environment.yml` - Three deployment scenarios
- `appsettings.advanced.json` - Configuration with pre-installed modules
- `.env.example` - Template for your .env file

---

## Next Steps

1. Copy `.env.example` to `.env`
2. Customize `INSTALL_PS_MODULES` for your use case
3. Build: `./docker.sh build --modules "Your Module List"`
4. Update `appsettings.json` to use `ImportModules`
5. Test container startup time
6. Deploy to your environment

---

## Related Documentation

- [DOCKER.md](../DOCKER.md) - General Docker documentation
- [ENVIRONMENT-CUSTOMIZATION.md](ENVIRONMENT-CUSTOMIZATION.md) - Runtime customization
- [Dockerfile](../Dockerfile) - Full Dockerfile with comments
- [docker.sh](../docker.sh) - Build script with all options
