# Azure Integration Test Scenario - Complete Overview

## What Was Created

This integration test scenario validates the complete PoshMcp deployment pipeline to Azure Container Apps. It demonstrates the two-tier Docker architecture where users build custom images from the PoshMcp base image.

### Files Created

1. **`PoshMcp.Tests/Integration/AzureDeploymentIntegrationTests.cs`**
   - Main test class with three test methods
   - Orchestrates Docker builds and Azure deployments
   - Validates base image → custom image → Azure deployment flow
   - Duration: 5-10 minutes for complete flow

2. **`examples/azure-managed-identity-startup.ps1`**
   - PowerShell startup script for Azure Managed Identity authentication
   - Connects to Azure using system or user-assigned managed identity
   - Sets up Azure context and helper functions
   - Provides troubleshooting guidance

3. **`PoshMcp.Tests/Integration/Dockerfile.azure-test`**
   - Test-specific Dockerfile demonstrating custom image pattern
   - Uses ARG for injectable base image
   - Installs Az modules with fixed versions
   - Includes verification checks

4. **`PoshMcp.Tests/Integration/README.azure-integration.md`**
   - Comprehensive documentation for integration tests
   - Prerequisites and environment setup
   - Troubleshooting guide
   - CI/CD integration examples

5. **`run-azure-integration-tests.ps1`**
   - Helper script to simplify test execution
   - Validates prerequisites automatically
   - Configures environment variables
   - Optional cleanup of Azure resources

### Updated Files

- **`examples/Dockerfile.azure`** - Now references the new `azure-managed-identity-startup.ps1` script

---

## Architecture Flow

```
┌─────────────────────────────────────────────────────────────────┐
│ PHASE 1: BUILD BASE IMAGE                                       │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Dockerfile (root)                                               │
│  ├─ Build Stage (.NET compilation)                              │
│  └─ Runtime Stage (base PoshMcp image)                          │
│      ├─ .NET 8 Runtime                                          │
│      ├─ PowerShell 7.4                                          │
│      ├─ PoshMcp Server & Web                                    │
│      └─ Tag: poshmcp:latest                                     │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│ PHASE 2: BUILD CUSTOM IMAGE                                     │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  examples/Dockerfile.azure (or Dockerfile.azure-test)           │
│  FROM poshmcp:latest                                            │
│  ├─ Copy install-modules.ps1                                    │
│  ├─ Install Az.Accounts, Az.Resources, Az.Storage              │
│  ├─ Copy azure-managed-identity-startup.ps1                     │
│  ├─ Copy custom appsettings.json                                │
│  └─ Tag: poshmcp-azure:test-timestamp                           │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│ PHASE 3: PUSH TO AZURE CONTAINER REGISTRY                       │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  1. Create ACR (if not exists)                                  │
│  2. Login to ACR                                                 │
│  3. Tag image: acrname.azurecr.io/poshmcp-azure:timestamp       │
│  4. Push image to ACR                                            │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│ PHASE 4: DEPLOY TO AZURE CONTAINER APPS                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  infrastructure/azure/main.bicep                                 │
│  ├─ Log Analytics Workspace                                     │
│  ├─ Application Insights                                        │
│  ├─ Container Apps Environment                                  │
│  ├─ Managed Identity (system-assigned)                          │
│  └─ Container App                                                │
│      ├─ Pull image from ACR                                     │
│      ├─ Enable managed identity                                 │
│      ├─ Run startup script: azure-managed-identity-startup.ps1  │
│      └─ Expose health endpoint                                  │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│ PHASE 5: VERIFY DEPLOYMENT                                      │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  1. Check Container App status (should be "Running")            │
│  2. Verify managed identity authenticated successfully          │
│  3. Test health endpoint                                        │
│  4. Validate Az modules are loaded                              │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## Test Methods

### 1. `BuildBaseImage_ShouldSucceed()`

**Purpose:** Validate the base PoshMcp image builds correctly

**Steps:**
1. Run `docker build -t poshmcp-base:{timestamp} .` from repository root
2. Verify exit code is 0
3. Confirm image exists in `docker images`

**Success Criteria:**
- Build completes without errors
- Image is tagged and listed
- No warnings about missing dependencies

**Duration:** ~2-3 minutes (fresh build)

---

### 2. `BuildCustomAzureImage_FromBaseImage_ShouldSucceed()`

**Purpose:** Validate custom image layering pattern

**Steps:**
1. Ensure base image exists (calls BuildBaseImage if needed)
2. Modify Dockerfile.azure to use test base image tag
3. Run `docker build -f Dockerfile.azure.test -t poshmcp-azure:{timestamp} .`
4. Verify Az.Accounts module is installed by running container
5. Check image layers and size

**Success Criteria:**
- Custom image builds from base successfully
- Az modules are present and loadable
- Startup script is copied correctly
- Total image size is reasonable

**Duration:** ~1-2 minutes (base image cached)

---

### 3. `DeployToAzure_CompleteFlow_ShouldSucceed()`

**Purpose:** End-to-end deployment validation

**Steps:**
1. Build base image
2. Build custom Azure image
3. Create resource group (if needed)
4. Create Azure Container Registry
5. Tag image for ACR: `acrname.azurecr.io/poshmcp-azure:{timestamp}`
6. Login to ACR
7. Push image to ACR
8. Deploy using Bicep template (`infrastructure/azure/main.bicep`)
9. Wait for deployment completion
10. Verify Container App status
11. Get application FQDN
12. Optionally test health endpoint

**Success Criteria:**
- All Azure resources created successfully
- Image pushed to ACR without errors
- Container App status is "Running"
- Managed identity is configured
- Application responds to requests (if ingress enabled)

**Duration:** ~5-10 minutes

**Cost:** ~$0.50-1.00 per run (varies by region and duration)

---

## Environment Variables

The tests use the following environment variables:

| Variable | Required | Default | Purpose |
|----------|----------|---------|---------|
| `AZURE_SUBSCRIPTION_ID` | Yes* | None | Azure subscription for deployment |
| `AZURE_RESOURCE_GROUP` | No | `rg-poshmcp-test-{timestamp}` | Resource group name |
| `AZURE_LOCATION` | No | `eastus` | Azure region |
| `AZURE_CONTAINER_REGISTRY` | No | `acr{timestamp}` | ACR name |

*If `AZURE_SUBSCRIPTION_ID` is not set, tests are automatically skipped.

---

## Running the Tests

### Quick Start (Easiest)

```powershell
# Set your subscription ID
$env:AZURE_SUBSCRIPTION_ID = "your-subscription-id"

# Run the helper script
./run-azure-integration-tests.ps1
```

### Manual Execution

```bash
# Set environment variables
export AZURE_SUBSCRIPTION_ID="your-subscription-id"
export AZURE_RESOURCE_GROUP="rg-poshmcp-test"
export AZURE_LOCATION="eastus"

# Run all integration tests
cd PoshMcp.Tests
dotnet test --filter "FullyQualifiedName~AzureDeploymentIntegrationTests"

# Or run individual tests
dotnet test --filter "BuildBaseImage_ShouldSucceed"
dotnet test --filter "BuildCustomAzureImage_FromBaseImage_ShouldSucceed"
dotnet test --filter "DeployToAzure_CompleteFlow_ShouldSucceed"
```

### Helper Script Options

```powershell
# Run only base image build test
./run-azure-integration-tests.ps1 -TestName Base

# Run with automatic cleanup
./run-azure-integration-tests.ps1 -Cleanup

# Use existing resources
./run-azure-integration-tests.ps1 -ResourceGroup "my-rg" -RegistryName "myacr"

# Dry run (show what would happen)
./run-azure-integration-tests.ps1 -DryRun

# Skip prerequisite checks (faster)
./run-azure-integration-tests.ps1 -SkipPrerequisites
```

---

## What Gets Tested

### Docker Capabilities

✅ Base image builds correctly from Dockerfile
✅ Custom image inherits from base image
✅ Module installation script runs successfully
✅ Az modules are installed and available
✅ Startup scripts are copied and executable
✅ Image layering works as expected
✅ Image sizes are reasonable

### Azure Integration

✅ Resource group creation
✅ Azure Container Registry provisioning
✅ ACR authentication works
✅ Image push to ACR succeeds
✅ Bicep template deploys without errors
✅ Container Apps Environment is created
✅ Container App pulls image from ACR
✅ Managed identity is configured
✅ Container App starts and runs
✅ Application is accessible (if ingress enabled)

### PowerShell Environment

✅ Az.Accounts module loads correctly
✅ Managed identity authentication succeeds
✅ Azure context is established
✅ Helper functions are available
✅ Startup script completes without errors

---

## Troubleshooting

### Tests Are Skipped

**Symptom:** All tests show as "Skipped" in output

**Cause:** `AZURE_SUBSCRIPTION_ID` environment variable is not set

**Solution:**
```powershell
$env:AZURE_SUBSCRIPTION_ID = "your-subscription-id"
./run-azure-integration-tests.ps1
```

### Docker Build Fails

**Common causes:**
1. Docker Desktop not running
2. Insufficient disk space (need ~5GB)
3. Network issues downloading base images
4. Dockerfile syntax errors

**Solutions:**
```bash
# Check Docker is running
docker ps

# Clean up old images
docker system prune -a

# Check disk space
docker system df
```

### Azure Deployment Timeout

**Symptom:** Deployment takes >15 minutes or times out

**Possible causes:**
1. Azure region is busy or throttling
2. Resource quotas exceeded
3. Network connectivity issues to Azure
4. Container App can't pull image from ACR

**Solutions:**
- Use a different Azure region (`-Location "westus2"`)
- Check subscription quotas
- Verify ACR has public access enabled (or managed identity has AcrPull role)

### Image Won't Start in Azure

**Symptom:** Container App created but status is not "Running"

**Debugging:**
```bash
# Check Container App logs
az containerapp logs show \
  --name app-name \
  --resource-group rg-name \
  --tail 100

# Check revision status
az containerapp revision list \
  --name app-name \
  --resource-group rg-name \
  --output table

# Check system/console logs
az containerapp logs show \
  --name app-name \
  --resource-group rg-name \
  --type console
```

---

## CI/CD Integration

These tests can be automated in CI/CD pipelines for continuous validation.

### When to Run

- **On Pull Requests:** Run base and custom image build tests (skip Azure deployment)
- **On Main Branch:** Run complete flow including Azure deployment
- **Scheduled (Nightly):** Run complete flow to validate Azure integration
- **Manual Trigger:** Allow developers to run on-demand

### Sample GitHub Actions

```yaml
name: Azure Integration Tests

on:
  push:
    branches: [main]
  schedule:
    - cron: '0 2 * * *'  # 2 AM daily
  workflow_dispatch:

jobs:
  integration:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0.x'
      
      - name: Azure Login
        uses: azure/login@v1
        with:
          creds: ${{ secrets.AZURE_CREDENTIALS }}
      
      - name: Run Integration Tests
        env:
          AZURE_SUBSCRIPTION_ID: ${{ secrets.AZURE_SUBSCRIPTION_ID }}
        run: |
          ./run-azure-integration-tests.ps1 -Cleanup
```

---

## Cleanup

### Automatic Cleanup

```powershell
./run-azure-integration-tests.ps1 -Cleanup
```

### Manual Cleanup

```bash
# Delete resource group (deletes all resources)
az group delete --name rg-poshmcp-test --yes --no-wait

# Or delete individual resources
az containerapp delete --name app-name --resource-group rg-name --yes
az acr delete --name acrname --resource-group rg-name --yes

# Clean up Docker images
docker rmi poshmcp-base:latest poshmcp-azure:latest --force
docker system prune -a
```

---

## Cost Estimation

Typical costs for running the complete integration test:

| Resource | Duration | Cost |
|----------|----------|------|
| Container App | ~10 min | ~$0.01 |
| Container App Environment | ~10 min | ~$0.05 |
| Container Registry | 1 day | ~$0.17 |
| Log Analytics | Data ingested | ~$0.01 |
| Application Insights | Telemetry | ~$0.01 |
| **Total per test run** | | **~$0.25** |

*Prices are approximate and vary by region. Clean up promptly to minimize costs.*

---

## See Also

- [Integration Test Documentation](PoshMcp.Tests/Integration/README.azure-integration.md)
- [Azure Infrastructure Guide](infrastructure/azure/README.md)
- [Dockerfile Examples](examples/README.md)
- [PoshMcp Design Document](DESIGN.md)
