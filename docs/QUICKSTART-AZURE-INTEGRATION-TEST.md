# Azure Integration Test - Quick Reference

## What Was Built

A complete integration test scenario that validates:
1. ✅ **Base image build** - Core PoshMcp container
2. ✅ **Custom image layering** - User customization with Az modules  
3. ✅ **Azure deployment** - Full Container Apps deployment
4. ✅ **Managed Identity** - Azure authentication

## Files Created

```
PoshMcp.Tests/Integration/
├── AzureDeploymentIntegrationTests.cs    ← Main test class
├── Dockerfile.azure-test                 ← Test Dockerfile with ARG support
└── README.azure-integration.md           ← Full documentation

examples/
└── azure-managed-identity-startup.ps1    ← Managed Identity auth script

docs/
└── AZURE-INTEGRATION-TEST-SCENARIO.md    ← Complete overview

run-azure-integration-tests.ps1           ← Helper script (root)
```

## Quick Start

```powershell
# 1. Set your Azure subscription ID
$env:AZURE_SUBSCRIPTION_ID = "your-subscription-id"

# 2. Run the tests
.\run-azure-integration-tests.ps1

# Or run specific tests
.\run-azure-integration-tests.ps1 -TestName Base      # Base image only
.\run-azure-integration-tests.ps1 -TestName Custom    # Custom image only
.\run-azure-integration-tests.ps1 -TestName Deploy    # Full deployment

# With cleanup
.\run-azure-integration-tests.ps1 -Cleanup
```

## Test Methods

| Test | What It Does | Duration | Cost |
|------|--------------|----------|------|
| `BuildBaseImage_ShouldSucceed` | Builds core PoshMcp image | 2-3 min | Free |
| `BuildCustomAzureImage_FromBaseImage_ShouldSucceed` | Builds custom image with Az modules | 1-2 min | Free |
| `DeployToAzure_CompleteFlow_ShouldSucceed` | Complete Azure deployment | 5-10 min | ~$0.25 |

## Architecture Flow

```
Dockerfile (base) 
    → poshmcp:latest
        → examples/Dockerfile.azure (custom layer)
            → poshmcp-azure:{timestamp}
                → Push to ACR
                    → Deploy to Container Apps
                        → Verify deployment
```

## Prerequisites

- Docker Desktop (running)
- Azure CLI (`az login`)
- .NET SDK 8.0
- Azure subscription with permissions

## Environment Variables

```powershell
# Required
$env:AZURE_SUBSCRIPTION_ID = "your-sub-id"

# Optional (auto-generated if not set)
$env:AZURE_RESOURCE_GROUP = "rg-poshmcp-test"
$env:AZURE_LOCATION = "eastus"
$env:AZURE_CONTAINER_REGISTRY = "acrposhmcptest"
```

### Using .env Files (Recommended)

The tests automatically load configuration from `.env` files:

```bash
# 1. Copy the example file
cp .env.example .env.test

# 2. Edit and set your Azure subscription ID
nano .env.test  # or use your favorite editor

# 3. Run tests - configuration loads automatically
dotnet test --filter "AzureDeploymentIntegrationTests"
```

**Priority order:**
1. System environment variables (highest priority)
2. `.env.test` (test-specific)
3. `.env.local` (local development)
4. `.env` (shared defaults)

**.env.test example:**
```bash
# Azure Integration Test Configuration
AZURE_SUBSCRIPTION_ID=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
AZURE_RESOURCE_GROUP=rg-poshmcp-test
AZURE_LOCATION=eastus
```

All `.env*` files (except `.env.example`) are git-ignored for security.

## Manual Test Execution

```bash
# Set environment
export AZURE_SUBSCRIPTION_ID="your-sub-id"

# Run all integration tests
cd PoshMcp.Tests
dotnet test --filter "FullyQualifiedName~AzureDeploymentIntegrationTests"

# Run specific test
dotnet test --filter "DeployToAzure_CompleteFlow_ShouldSucceed"

# Use trait-based filtering (recommended)
dotnet test --filter "Category=Docker"                    # Docker tests only
dotnet test --filter "Category=Integration&Cost!=Expensive"  # Free tests only
dotnet test --filter "Speed!=VerySlow"                    # Exclude very slow tests

# See all trait filtering options
Get-Help ./run-azure-integration-tests.ps1 -Examples
```

**For detailed trait filtering documentation, see [Trait-Based Test Filtering](TRAIT-BASED-TEST-FILTERING.md)**

## Cleanup

```bash
# Automatic cleanup
.\run-azure-integration-tests.ps1 -Cleanup

# Manual cleanup
az group delete --name rg-poshmcp-test --yes --no-wait

# Docker cleanup
docker rmi poshmcp-base poshmcp-azure --force
docker system prune -a
```

## Troubleshooting

**Tests are skipped:**
- Set `AZURE_SUBSCRIPTION_ID` environment variable

**Docker build fails:**
- Ensure Docker Desktop is running
- Check disk space: `docker system df`
- Clean old images: `docker system prune -a`

**Azure deployment fails:**
- Verify `az login` is authenticated
- Check subscription permissions
- Try a different region: `-Location "westus2"`
- Check ACR access (public or managed identity with AcrPull)

**View Container App logs:**
```bash
az containerapp logs show \
  --name app-name \
  --resource-group rg-name \
  --tail 100
```

## Cost Estimation

| Component | Duration | Cost |
|-----------|----------|------|
| Container Registry | 1 day | ~$0.17 |
| Container App | 10 min | ~$0.01 |
| Log Analytics | Per test | ~$0.01 |
| **Total** | **Per run** | **~$0.25** |

Clean up resources promptly to minimize costs.

## Documentation

- **Full Test Docs:** `PoshMcp.Tests/Integration/README.azure-integration.md`
- **Scenario Overview:** `docs/AZURE-INTEGRATION-TEST-SCENARIO.md`
- **Azure Infra Guide:** `infrastructure/azure/README.md`
- **Dockerfile Examples:** `examples/README.md`

## CI/CD Integration

Add to GitHub Actions:

```yaml
name: Azure Integration Tests
on:
  schedule:
    - cron: '0 2 * * *'  # Daily at 2 AM
  workflow_dispatch:

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - uses: azure/login@v1
        with:
          creds: ${{ secrets.AZURE_CREDENTIALS }}
      - run: ./run-azure-integration-tests.ps1 -Cleanup
        env:
          AZURE_SUBSCRIPTION_ID: ${{ secrets.AZURE_SUBSCRIPTION_ID }}
```

---

**Ready to test?**

```powershell
$env:AZURE_SUBSCRIPTION_ID = "your-subscription-id"
.\run-azure-integration-tests.ps1
```
