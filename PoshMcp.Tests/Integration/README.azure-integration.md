# Azure deployment integration tests

This directory contains comprehensive integration tests that validate the complete PoshMcp deployment pipeline to Azure Container Apps, including:

1. **Base image build** - Building the core PoshMcp container image
2. **Custom image layering** - Building user-customized images with Azure modules
3. **Container registry** - Pushing images to Azure Container Registry
4. **Azure deployment** - Deploying to Azure Container Apps Environment
5. **Managed identity** - Authenticating with Azure Managed Identity

## Test Structure

### Files

- **`AzureDeploymentIntegrationTests.cs`** - Main integration test class
  - `BuildBaseImage_ShouldSucceed()` - Tests building the base PoshMcp image
  - `BuildCustomAzureImage_FromBaseImage_ShouldSucceed()` - Tests building custom Azure-enabled image
  - `DeployToAzure_CompleteFlow_ShouldSucceed()` - End-to-end deployment test
  
- **`Dockerfile.azure-test`** - Test-specific Dockerfile with:
  - ARG-based base image (injectable for testing)
  - Fixed Az module versions for reproducibility
  - Managed identity startup script
  - Verification checks

### Supporting Files

- **`../examples/azure-managed-identity-startup.ps1`** - Startup script for Azure authentication
- **`../examples/appsettings.advanced.json`** - Configuration with Azure module functions

---

## Prerequisites

### Required Tools

1. **Docker Desktop** - For building and running containers
   ```bash
   docker --version  # Should be >= 20.10
   ```

2. **Azure CLI** - For deploying to Azure
   ```bash
   az --version  # Should be >= 2.50.0
   az login
   ```

3. **.NET SDK 8.0** - For running the tests
   ```bash
   dotnet --version  # Should be >= 8.0
   ```

### Azure Requirements

1. **Azure Subscription** with permissions to:
   - Create resource groups
   - Create container registries
   - Create container app environments
   - Create container apps
   - Assign managed identities

2. **Authentication** - Login to Azure:
   ```bash
   az login
   az account show  # Verify subscription
   ```

### Environment Variables

Configure the following environment variables before running tests:

**Option 1: Using .env Files (Recommended)**

```bash
# 1. Copy the example file
cp .env.example .env.test

# 2. Edit the file and set your Azure subscription ID
# AZURE_SUBSCRIPTION_ID=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
# AZURE_RESOURCE_GROUP=rg-poshmcp-test  # Optional
# AZURE_LOCATION=eastus                  # Optional

# 3. Tests automatically load configuration from .env files
```

The tests check for `.env` files in this priority order:
1. `.env.test` (test-specific, recommended)
2. `.env.local` (local development)
3. `.env` (shared defaults)

System environment variables always take precedence over `.env` files.

**Option 2: Using System Environment Variables**

```bash
# Required
export AZURE_SUBSCRIPTION_ID="your-subscription-id"

# Optional (will be created if not specified)
export AZURE_RESOURCE_GROUP="rg-poshmcp-test"
export AZURE_LOCATION="eastus"
export AZURE_CONTAINER_REGISTRY="acrposhmcptest"
```

**Windows PowerShell:**
```powershell
$env:AZURE_SUBSCRIPTION_ID = "your-subscription-id"
$env:AZURE_RESOURCE_GROUP = "rg-poshmcp-test"
$env:AZURE_LOCATION = "eastus"
$env:AZURE_CONTAINER_REGISTRY = "acrposhmcptest"
```

---

## Running the Tests

### Quick Start (All Tests)

```bash
# From repository root
cd PoshMcp.Tests
dotnet test --filter "FullyQualifiedName~AzureDeploymentIntegrationTests"
```

### Individual Tests

```bash
# Build base image only
dotnet test --filter "BuildBaseImage_ShouldSucceed"

# Build custom Azure image
dotnet test --filter "BuildCustomAzureImage_FromBaseImage_ShouldSucceed"

# Complete deployment flow (SLOW - ~5-10 minutes)
dotnet test --filter "DeployToAzure_CompleteFlow_ShouldSucceed"
```

### Skip Tests (Default)

By default, these tests are **skipped** with `[Fact(Skip = "...")]` attribute because they:
- Require Docker (slow builds)
- Require Azure credentials
- Create billable Azure resources
- Take 5-10 minutes to run

To **enable** the tests, remove the `Skip` parameter from the `[Fact]` attributes in `AzureDeploymentIntegrationTests.cs`.

### Running Manually

If you prefer to run the deployment manually (without xUnit):

```bash
# 1. Build base image
docker build -t poshmcp:latest .

# 2. Build custom Azure image
docker build -f examples/Dockerfile.azure -t poshmcp-azure .

# 3. Tag for ACR
docker tag poshmcp-azure myacr.azurecr.io/poshmcp-azure:v1

# 4. Login to ACR
az acr login --name myacr

# 5. Push image
docker push myacr.azurecr.io/poshmcp-azure:v1

# 6. Deploy with Bicep (subscription-scoped deployment)
az deployment sub create \
  --location eastus \
  --template-file infrastructure/azure/main.bicep \
  --parameters @infrastructure/azure/parameters.json \
  --parameters containerImage=myacr.azurecr.io/poshmcp-azure:v1 \
  --parameters containerRegistryServer=myacr.azurecr.io
```

---

## Test Scenarios

### Scenario 1: Base Image Build

**What it tests:**
- Dockerfile syntax and build process
- Base image contains all required components
- Image can be tagged and listed

**Success criteria:**
- `docker build` exits with code 0
- Image appears in `docker images`
- No build errors or warnings

**Typical duration:** 2-3 minutes

---

### Scenario 2: Custom Image Layering

**What it tests:**
- FROM base image works correctly
- Module installation script runs successfully
- Az modules are installed and available
- Startup script is copied correctly

**Success criteria:**
- Custom image builds without errors
- `Get-Module -ListAvailable Az.Accounts` returns results
- Image size is reasonable (< 2GB total)

**Typical duration:** 1-2 minutes (base image cached)

---

### Scenario 3: Complete Azure Deployment

**What it tests:**
- Resource group creation
- Azure Container Registry provisioning
- ACR authentication and image push
- Container App Environment creation
- Container App deployment with custom image
- Managed identity configuration
- Application health and readiness

**Success criteria:**
- All Azure resources created successfully
- Image pushed to ACR
- Container App shows "Running" status
- Application responds to health checks
- No deployment errors

**Typical duration:** 5-10 minutes

**Azure resources created:**
- Resource Group (if not exists)
- Container Registry
- Log Analytics Workspace
- Application Insights
- Container App Environment
- Container App
- Managed Identity

**Estimated cost:** $0.10-0.50 per test run (varies by region)

---

## Troubleshooting

### Docker Build Fails

**Problem:** `docker build` returns non-zero exit code

**Solutions:**
1. Ensure Docker Desktop is running
2. Check disk space (need ~5GB for images)
3. Try cleaning Docker: `docker system prune -a`
4. Check Dockerfile syntax

### Azure CLI Not Authenticated

**Problem:** `az` commands fail with authentication error

**Solution:**
```bash
az login
az account set --subscription "your-subscription-id"
```

### ACR Push Permission Denied

**Problem:** `docker push` fails with 401 or 403

**Solution:**
```bash
# Re-login to ACR
az acr login --name your-acr-name

# Or enable admin and use credentials
az acr update --name your-acr-name --admin-enabled true
az acr credential show --name your-acr-name
```

### Container App Deployment Fails

**Problem:** Bicep deployment fails or Container App won't start

**Common causes:**
1. **Image not found** - Verify image exists in ACR
2. **ACR authentication** - Check managed identity has AcrPull role
3. **Resource quotas** - Check subscription limits
4. **Invalid configuration** - Review appsettings.json

**Debugging:**
```bash
# Check Container App logs
az containerapp logs show --name your-app --resource-group your-rg --tail 50

# Check deployment status
az containerapp show --name your-app --resource-group your-rg --query properties.provisioningState

# Check Container App events
az containerapp revision list --name your-app --resource-group your-rg
```

### Tests Take Too Long

**Problem:** Integration tests exceed reasonable time limits

**Solutions:**
1. Use an existing resource group (don't create new one each time)
2. Use an existing ACR (don't create new one each time)
3. Pre-build base image: `docker build -t poshmcp:latest .`
4. Run tests individually, not all at once
5. Use Azure regions close to you for faster deployments

### Cleanup Resources

**Problem:** Test resources are left behind and incurring costs

**Solution:**
```bash
# Delete entire resource group (WARNING: deletes everything)
az group delete --name rg-poshmcp-test --yes --no-wait

# Or delete individual resources
az containerapp delete --name app-name --resource-group rg-name --yes
az containerappenv delete --name env-name --resource-group rg-name --yes
az acr delete --name acrname --resource-group rg-name --yes
```

---

## CI/CD Integration

These integration tests can be integrated into CI/CD pipelines:

### GitHub Actions Example

```yaml
name: Azure Integration Tests

on:
  schedule:
    - cron: '0 2 * * *'  # Run nightly
  workflow_dispatch:      # Allow manual trigger

jobs:
  integration-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      
      - name: Azure Login
        uses: azure/login@v1
        with:
          creds: ${{ secrets.AZURE_CREDENTIALS }}
      
      - name: Run Integration Tests
        env:
          AZURE_SUBSCRIPTION_ID: ${{ secrets.AZURE_SUBSCRIPTION_ID }}
          AZURE_RESOURCE_GROUP: rg-poshmcp-ci-test
          AZURE_LOCATION: eastus
        run: |
          cd PoshMcp.Tests
          dotnet test --filter "AzureDeploymentIntegrationTests"
      
      - name: Cleanup
        if: always()
        run: |
          az group delete --name rg-poshmcp-ci-test --yes --no-wait
```

### Azure DevOps Example

```yaml
trigger:
  - main

pool:
  vmImage: 'ubuntu-latest'

steps:
- task: Docker@2
  inputs:
    command: 'build'
    Dockerfile: '**/Dockerfile'
    tags: 'poshmcp:latest'

- task: AzureCLI@2
  inputs:
    azureSubscription: 'Azure-Service-Connection'
    scriptType: 'bash'
    scriptLocation: 'inlineScript'
    inlineScript: |
      cd PoshMcp.Tests
      dotnet test --filter "AzureDeploymentIntegrationTests"
  env:
    AZURE_SUBSCRIPTION_ID: $(AZURE_SUBSCRIPTION_ID)
```

---

## Best Practices

1. **Run locally first** - Validate tests work before adding to CI/CD
2. **Use dedicated test subscription** - Isolate test resources from production
3. **Set budget alerts** - Monitor costs of test resources
4. **Automate cleanup** - Use tags and scheduled cleanup jobs
5. **Cache base images** - Don't rebuild base image unnecessarily
6. **Parallelize when possible** - Run independent tests concurrently
7. **Monitor test duration** - Track and optimize slow tests

---

## See also

- [Azure infrastructure documentation](../../infrastructure/azure/README.md) — deployment architecture and configuration
- [Dockerfile examples](../../examples/README.md) — custom image templates
- [PoshMcp architecture](../../DESIGN.md) — design philosophy
- [Trait-based test filtering](../../docs/TRAIT-BASED-TEST-FILTERING.md) — filter tests by category, speed, or cost
- [Quick start reference](../../docs/QUICKSTART-AZURE-INTEGRATION-TEST.md) — quick command reference
