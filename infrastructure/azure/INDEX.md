# Azure Container Apps for PoshMcp — file index

Navigation guide for all deployment files in this directory.

## Start here

| Document | Purpose |
|----------|---------|
| [README.md](README.md) | Full deployment guide — prerequisites, configuration, deployment, monitoring, troubleshooting |
| [QUICKSTART.md](QUICKSTART.md) | Quick-reference commands for common operations |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Infrastructure design, component diagrams, and integration details |
| [CHECKLIST.md](CHECKLIST.md) | Step-by-step deployment verification checklist |

## Infrastructure templates

- **[main.bicep](main.bicep)** — Main Bicep template (subscription scope)
- **[resources.bicep](resources.bicep)** — Resource group module (invoked by main.bicep)
- **[parameters.json](parameters.json)** — Production configuration
- **[parameters.local.json.template](parameters.local.json.template)** — Development template (copy to `parameters.local.json`)

## Deployment and validation scripts

- **[deploy.sh](deploy.sh)** / **[deploy.ps1](deploy.ps1)** — Deployment automation (bash / PowerShell)
- **[validate.sh](validate.sh)** / **[validate.ps1](validate.ps1)** — Pre-deployment validation (bash / PowerShell)

## Architecture reference

- **[MODULARIZATION.md](MODULARIZATION.md)** — Bicep module architecture, scope handling, parameter flow, and migration guide
- **[BICEP-REFACTOR-SUMMARY.md](BICEP-REFACTOR-SUMMARY.md)** — Historical record of the 2026-03-27 Bicep refactor

## Recommended reading order

**First-time users:**
1. [README.md](README.md) — Prerequisites section
2. [ARCHITECTURE.md](ARCHITECTURE.md) — Understand the infrastructure design
3. [CHECKLIST.md](CHECKLIST.md) — Walk through deployment steps
4. [QUICKSTART.md](QUICKSTART.md) — Copy-paste deployment commands

**Experienced users:**
1. [QUICKSTART.md](QUICKSTART.md) — Jump straight to commands
2. Deploy directly

**Troubleshooting:**
1. [README.md — Troubleshooting](README.md#troubleshooting)
2. [QUICKSTART.md — Troubleshooting commands](QUICKSTART.md#troubleshooting-commands)

## External links

- [Azure Container Apps documentation](https://learn.microsoft.com/en-us/azure/container-apps/)
- [Bicep documentation](https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/)
- [PoshMcp main README](../../README.md)
