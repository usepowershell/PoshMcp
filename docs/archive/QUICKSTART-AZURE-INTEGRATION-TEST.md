# Azure integration test — quick reference

A quick command reference for running Azure deployment integration tests. For full documentation, see [PoshMcp.Tests/Integration/README.azure-integration.md](../PoshMcp.Tests/Integration/README.azure-integration.md).

## Quick start

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

## Test methods

| Test | Duration | Cost |
|------|----------|------|
| `BuildBaseImage_ShouldSucceed` | 2–3 min | Free |
| `BuildCustomAzureImage_FromBaseImage_ShouldSucceed` | 1–2 min | Free |
| `DeployToAzure_CompleteFlow_ShouldSucceed` | 5–10 min | ~$0.25 |

## Prerequisites

- Docker Desktop (running)
- Azure CLI (`az login`)
- .NET SDK 8.0
- Azure subscription with permissions

## Environment variables

```powershell
# Required
$env:AZURE_SUBSCRIPTION_ID = "your-sub-id"

# Optional (auto-generated if not set)
$env:AZURE_RESOURCE_GROUP = "rg-poshmcp-test"
$env:AZURE_LOCATION = "eastus"
$env:AZURE_CONTAINER_REGISTRY = "acrposhmcptest"
```

### Using .env files (recommended)

```bash
cp .env.example .env.test
# Edit .env.test with your Azure subscription ID
dotnet test --filter "AzureDeploymentIntegrationTests"
```

Priority: system env vars > `.env.test` > `.env.local` > `.env`. All `.env*` files (except `.env.example`) are git-ignored.

## Manual test execution

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
.\run-azure-integration-tests.ps1 -Cleanup

# Or manually:
az group delete --name rg-poshmcp-test --yes --no-wait
```

## Troubleshooting

| Problem | Solution |
|---------|----------|
| Tests skipped | Set `AZURE_SUBSCRIPTION_ID` environment variable |
| Docker build fails | Ensure Docker Desktop is running; check disk space with `docker system df` |
| Azure deployment fails | Verify `az login`; check subscription permissions; try a different region |
| Container App not starting | Check logs: `az containerapp logs show --name app-name --resource-group rg-name --tail 100` |

For detailed troubleshooting, see the [full test README](../PoshMcp.Tests/Integration/README.azure-integration.md#troubleshooting).

---

## See also

- [Azure integration test README](../PoshMcp.Tests/Integration/README.azure-integration.md) — full test documentation (canonical)
- [Azure integration test scenario](AZURE-INTEGRATION-TEST-SCENARIO.md) — architecture overview
- [Trait-based test filtering](TRAIT-BASED-TEST-FILTERING.md) — filter tests by category, speed, or cost
- [Azure infrastructure guide](../infrastructure/azure/README.md) — deployment configuration
