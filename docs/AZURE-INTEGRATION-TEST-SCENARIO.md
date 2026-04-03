# Azure integration test scenario

> **📍 This is an overview page.** For the full test documentation, see [PoshMcp.Tests/Integration/README.azure-integration.md](../PoshMcp.Tests/Integration/README.azure-integration.md). For a quick-start command reference, see [Quick reference](QUICKSTART-AZURE-INTEGRATION-TEST.md).

## What this tests

The integration test scenario validates the complete PoshMcp deployment pipeline to Azure Container Apps:

1. **Base image build** — core PoshMcp container
2. **Custom image layering** — user customization with Az modules
3. **Azure deployment** — full Container Apps deployment via Bicep
4. **Managed identity** — Azure authentication

## Architecture flow

```
Dockerfile (base)
    → poshmcp:latest
        → examples/Dockerfile.azure (custom layer)
            → poshmcp-azure:{timestamp}
                → Push to ACR
                    → Deploy to Container Apps
                        → Verify deployment
```

## Test methods

| Test | Duration | Cost |
|------|----------|------|
| `BuildBaseImage_ShouldSucceed` | 2–3 min | Free |
| `BuildCustomAzureImage_FromBaseImage_ShouldSucceed` | 1–2 min | Free |
| `DeployToAzure_CompleteFlow_ShouldSucceed` | 5–10 min | ~$0.25 |

## Quick start

```powershell
$env:AZURE_SUBSCRIPTION_ID = "your-subscription-id"
.\run-azure-integration-tests.ps1
```

---

## See also

- [Azure integration test README](../PoshMcp.Tests/Integration/README.azure-integration.md) — full test documentation (canonical)
- [Quick reference](QUICKSTART-AZURE-INTEGRATION-TEST.md) — quick-start commands and environment setup
- [Trait-based test filtering](TRAIT-BASED-TEST-FILTERING.md) — filter tests by category, speed, or cost
- [Azure infrastructure guide](../infrastructure/azure/README.md) — deployment architecture and configuration
