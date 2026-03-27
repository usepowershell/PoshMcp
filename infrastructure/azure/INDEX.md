# Azure Container Apps for PoshMcp - File Index

Quick navigation guide for all deployment files.

## 📋 Start Here

1. **[README.md](README.md)** - 📖 Comprehensive deployment guide (600+ lines)
   - Prerequisites, configuration, deployment, troubleshooting
   - **Read this first** for full understanding

2. **[QUICKSTART.md](QUICKSTART.md)** - ⚡ Quick reference commands
   - Common operations, deployment scenarios, monitoring queries
   - **Use this** for quick lookups and copy-paste commands

3. **[ARCHITECTURE.md](ARCHITECTURE.md)** - 🏗️ Architecture overview
   - Component diagram, integration details, security features
   - **Read this** to understand the infrastructure design

## 🚀 Deployment Files

### Infrastructure Templates
- **[main.bicep](main.bicep)** - Main Bicep template for Azure resources
- **[parameters.json](parameters.json)** - Production configuration
- **[parameters.local.json.template](parameters.local.json.template)** - Development template (copy to `parameters.local.json`)

### Deployment Scripts
- **[deploy.sh](deploy.sh)** - Bash deployment automation
- **[deploy.ps1](deploy.ps1)** - PowerShell deployment automation

### Validation Scripts
- **[validate.sh](validate.sh)** - Bash pre-deployment validation
- **[validate.ps1](validate.ps1)** - PowerShell pre-deployment validation

## 📚 Reference

- **[EXAMPLES.md](EXAMPLES.md)** - Real-world examples and snippets
  - Environment-specific configurations
  - Update and rollback procedures
  - CI/CD pipeline examples
  - Troubleshooting commands

## 🎯 Quick Actions

### First-Time Deployment
```bash
# 1. Validate prerequisites
./validate.sh  # or .\validate.ps1

# 2. Set environment variables
export REGISTRY_NAME="myregistry"
export RESOURCE_GROUP="poshmcp-rg"

# 3. Deploy
./deploy.sh    # or .\deploy.ps1
```

### Update Deployment
See [QUICKSTART.md - Update Deployment](QUICKSTART.md#update-deployment)

### Troubleshooting
See [README.md - Troubleshooting](README.md#troubleshooting)

## 📁 Recommended Reading Order

**For first-time users:**
1. README.md (Prerequisites section)
2. ARCHITECTURE.md (understand what you're deploying)
3. QUICKSTART.md (see quick deploy steps)
4. Run validate script
5. Run deploy script
6. README.md (Post-deployment sections)

**For experienced users:**
1. QUICKSTART.md (quick commands)
2. EXAMPLES.md (copy relevant examples)
3. Deploy directly

**For troubleshooting:**
1. README.md (Troubleshooting section)
2. QUICKSTART.md (Troubleshooting commands)
3. Check logs with commands from examples

## 🔗 External Links

- [Azure Container Apps Docs](https://learn.microsoft.com/en-us/azure/container-apps/)
- [Bicep Documentation](https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/)
- [PoshMcp Main README](../../README.md)

## 📝 File Summary

| File | Lines | Purpose |
|------|-------|---------|
| README.md | 600+ | Comprehensive guide |
| ARCHITECTURE.md | 300+ | Architecture overview |
| QUICKSTART.md | 300+ | Quick reference |
| EXAMPLES.md | 250+ | Real-world examples |
| main.bicep | 270 | Infrastructure template |
| deploy.sh | 180 | Bash deployment |
| deploy.ps1 | 200 | PowerShell deployment |
| validate.sh | 100 | Bash validation |
| validate.ps1 | 120 | PowerShell validation |
| parameters.json | 35 | Production config |
| parameters.local.json.template | 35 | Dev config template |

**Total:** ~2,400 lines of documentation and infrastructure code

---

**Created:** 2026-03-27  
**Author:** Amy Wong (DevOps/Platform/Azure Engineer)  
**Status:** Production-ready ✅
