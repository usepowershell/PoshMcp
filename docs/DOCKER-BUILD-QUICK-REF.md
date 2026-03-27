# Docker Build Quick Reference

Quick reference for building PoshMcp Docker images with pre-installed PowerShell modules.

## Basic Commands

### Linux/Mac (Bash)

```bash
# Build without modules
./docker.sh build

# Build with modules
./docker.sh build --modules "Pester PSScriptAnalyzer"

# Build with version constraints
./docker.sh build --modules "Az.Accounts@2.0.0 Pester@>=5.0.0"

# Build with environment variable
export INSTALL_PS_MODULES="Pester PSScriptAnalyzer"
./docker.sh build

# Build with .env file
cp .env.example .env
# Edit .env, then:
docker-compose build
```

### Windows (PowerShell)

```powershell
# Build without modules
.\docker.ps1 build

# Build with modules
.\docker.ps1 build -Modules "Pester PSScriptAnalyzer"

# Build with version constraints
.\docker.ps1 build -Modules "Az.Accounts@2.0.0 Pester@>=5.0.0"

# Build with environment variable
$env:INSTALL_PS_MODULES = "Pester PSScriptAnalyzer"
.\docker.ps1 build

# Build with .env file
Copy-Item .env.example .env
# Edit .env, then:
docker-compose build
```

## Module Version Syntax

| Syntax | Meaning | Example |
|--------|---------|---------|
| `ModuleName` | Latest version | `Pester` |
| `ModuleName@X.Y.Z` | Exact version | `Pester@5.5.0` |
| `ModuleName@>=X.Y.Z` | Minimum version | `Az.Accounts@>=2.0.0` |
| `ModuleName@<=X.Y.Z` | Maximum version | `Pester@<=5.9.0` |

## Common Scenarios

### Azure Automation
```bash
./docker.sh build --modules "Az.Accounts@>=2.0.0 Az.Resources Az.Storage"
```

### Testing & QA
```bash
./docker.sh build --modules "Pester@>=5.0.0 PSScriptAnalyzer Plaster"
```

### Development
```bash
# No pre-installed modules - everything at runtime
./docker.sh build
```

### Production (Hybrid)
```bash
# Pre-install stable modules at build time
./docker.sh build --modules "Az.Accounts@2.12.0 PSScriptAnalyzer@1.21.0"

# Install project-specific modules at runtime (appsettings.json)
```

## Direct Docker Commands

```bash
# Build with modules
docker build \
  --build-arg INSTALL_PS_MODULES="Pester PSScriptAnalyzer" \
  -t poshmcp .

# Build with all options
docker build \
  --build-arg INSTALL_PS_MODULES="Pester@5.5.0 Az.Accounts" \
  --build-arg MODULE_INSTALL_SCOPE="AllUsers" \
  --build-arg SKIP_PUBLISHER_CHECK="true" \
  -t poshmcp .
```

## Verify Installation

```bash
# Run container and check modules
docker run --rm -it poshmcp pwsh

# Inside container:
PS> Get-Module -ListAvailable
PS> Get-Module -ListAvailable -Name Pester
PS> Import-Module Pester; Get-Command -Module Pester
```

## Performance Tips

✅ **DO:**
- Pre-install stable, frequently-used modules
- Use exact version numbers for production
- Test locally before deploying

❌ **DON'T:**
- Pre-install everything (increases image size)
- Skip publisher checks in production
- Pin to very old module versions

## Build Arguments Reference

| Argument | Type | Default | Description |
|----------|------|---------|-------------|
| `INSTALL_PS_MODULES` | string | `""` | Space/comma-separated module list |
| `MODULE_INSTALL_SCOPE` | string | `AllUsers` | Installation scope |
| `SKIP_PUBLISHER_CHECK` | string | `true` | Skip publisher validation |

## Environment Variables

Set in shell or `.env` file:

```bash
# Module list
INSTALL_PS_MODULES="Pester PSScriptAnalyzer"

# Installation scope
MODULE_INSTALL_SCOPE="AllUsers"

# Skip publisher check
SKIP_PUBLISHER_CHECK="true"

# Image name
IMAGE_NAME="poshmcp"
```

## Example .env File

```bash
# Copy this to .env and customize
INSTALL_PS_MODULES="Pester@>=5.0.0 PSScriptAnalyzer"
MODULE_INSTALL_SCOPE="AllUsers"
SKIP_PUBLISHER_CHECK="true"
IMAGE_NAME="poshmcp"
```

## After Build

Update `appsettings.json` to import pre-installed modules:

```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "ImportModules": ["Pester", "PSScriptAnalyzer"]
    }
  }
}
```

## Troubleshooting

### Module not found
```bash
# Check module exists in PSGallery
pwsh -Command "Find-Module -Name ModuleName"
```

### Build fails
```bash
# Build with verbose output
docker build --progress=plain \
  --build-arg INSTALL_PS_MODULES="ModuleName" \
  -t poshmcp .
```

### Publisher check error
```bash
# Skip publisher validation
docker build \
  --build-arg SKIP_PUBLISHER_CHECK="true" \
  --build-arg INSTALL_PS_MODULES="ModuleName" \
  -t poshmcp .
```

## Help

```bash
# Bash
./docker.sh --help

# PowerShell
.\docker.ps1 -Help
```

## Documentation

- **Full Guide**: [docs/DOCKER-BUILD-MODULES.md](DOCKER-BUILD-MODULES.md)
- **Runtime Config**: [docs/ENVIRONMENT-CUSTOMIZATION.md](ENVIRONMENT-CUSTOMIZATION.md)
- **Examples**: [examples/](../examples/)
- **Docker Guide**: [DOCKER.md](../DOCKER.md)
