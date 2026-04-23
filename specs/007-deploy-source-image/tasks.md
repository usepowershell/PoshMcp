# Test Tasks: Deploy Script Source Image Support (Spec 007)

**Spec**: `specs/007-deploy-source-image/spec.md`
**Script under test**: `infrastructure/azure/deploy.ps1`
**Written by**: Fry (Tester)
**Date**: 2026-07-28

---

## Prerequisites

Before running any tests, confirm the following are available:

- [ ] **Azure CLI** (`az`) installed and `az --version` returns output
- [ ] **Docker CLI** (`docker`) installed and Docker daemon is running (for Mode A tests)
- [ ] **Azure subscription** with at least Contributor access to a resource group
- [ ] **Azure Container Registry** created (or permissions to create one)
- [ ] **ACR import permissions** — your identity needs `AcrPush` role on the target ACR (for Mode B tests)
- [ ] **Azure login** — `az account show` returns your subscription without error
- [ ] **Internet access** to pull public images (mcr.microsoft.com, docker.io)
- [ ] **PowerShell 7+** — `$PSVersionTable.PSVersion.Major` is 7 or higher

### Test Data (safe public images)

Use these image references in test commands:

| Purpose | Image Reference |
|---------|----------------|
| Small, fast pull | `mcr.microsoft.com/powershell:alpine-3.21` |
| Docker Hub (short form) | `alpine:3.20` |
| Docker Hub (full form) | `docker.io/library/alpine:3.20` |
| MCR image (Mode A default) | `mcr.microsoft.com/powershell:latest` |
| Non-existent image | `mcr.microsoft.com/poshmcp/does-not-exist:v999` |

> **Note**: Replace `<RegistryName>`, `<ResourceGroup>`, `<Subscription>`, and `<ImageTag>` throughout
> with your own values. Use a dedicated test resource group to avoid touching production.

---

## Section 1: Acceptance Criteria Checklist

These are the success criteria from the spec. Each must pass before the feature ships.

- [ ] **SC-050** — When `-SourceImage` is given without `-UseRegistryCache`, script calls `docker pull`, `docker tag`, and `docker push`; does **not** call `docker build`
- [ ] **SC-051** — When `-SourceImage` and `-UseRegistryCache` are both given, script calls `az acr import`; does **not** call `docker pull` or `docker build`
- [ ] **SC-052** — When `-SourceImage` is not given, script calls `docker build` as today
- [ ] **SC-053** — When `-UseRegistryCache` is given without `-SourceImage`, script exits with error code 2 and a validation error message containing "requires -SourceImage"
- [ ] **SC-054** — Source image not found → exit code 1 with a descriptive error naming the missing image
- [ ] **SC-055** — Running deploy.ps1 without `-SourceImage` produces identical behavior to pre-change invocations (backward compatibility)
- [ ] **SC-056** — Re-tagged images are pushed with both `$ImageTag` and `latest` tags (Mode A); both tags are imported (Mode B)
- [ ] **SC-057** — Parameter validation (`-UseRegistryCache` without `-SourceImage`) fails fast **before** any Docker or ACR commands run

---

## Section 2: Parameter Validation Tests

These tests require no live Azure deployment and run in under 30 seconds each.

### PV-01: `-UseRegistryCache` without `-SourceImage` fails fast

```powershell
.\infrastructure\azure\deploy.ps1 -RegistryName myacr -UseRegistryCache
```

**Expected**:
- [ ] Script exits immediately without attempting ACR login
- [ ] Error message contains: `Parameter -UseRegistryCache requires -SourceImage to be provided`
- [ ] Exit code is non-zero (`$LASTEXITCODE` is 1 or 2)
- [ ] No `docker` or `az acr` commands were invoked (verify via process monitor or `-WhatIf`-style trace)

**Verify exit code**:
```powershell
.\infrastructure\azure\deploy.ps1 -RegistryName myacr -UseRegistryCache
echo "Exit: $LASTEXITCODE"  # Expect non-zero
```

---

### PV-02: `-SourceImage` with empty string is rejected before deployment

```powershell
.\infrastructure\azure\deploy.ps1 -RegistryName myacr -SourceImage ''
```

**Expected**:
- [ ] Script either skips source image path (falls through to `Build-AndPushImage`) or exits with a meaningful error
- [ ] Does **not** attempt `docker pull` with an empty string

---

### PV-03: `-SourceImage` parameter is optional — no error when omitted

```powershell
# Should proceed to the build path (will fail later if Docker isn't set up, but should NOT fail due to missing -SourceImage)
.\infrastructure\azure\deploy.ps1 -RegistryName myacr -WhatIf 2>&1 | Select-Object -First 5
```

**Expected**:
- [ ] Script starts without any error about missing `-SourceImage`
- [ ] First output lines are about prerequisites or ACR login (not parameter validation errors)

---

## Section 3: Mode A Tests — Source Image with Local Pull

Mode A is triggered when `-SourceImage` is provided and `-UseRegistryCache` is **not** used.

### MA-01: Happy path — public image pull, re-tag, push (full integration)

```powershell
.\infrastructure\azure\deploy.ps1 `
    -RegistryName <RegistryName> `
    -ResourceGroup <ResourceGroup> `
    -Subscription <Subscription> `
    -SourceImage 'mcr.microsoft.com/powershell:alpine-3.21' `
    -ImageTag 'test-mode-a-001'
```

**Expected output (look for these lines in order)**:
- [ ] `Using source image (no local build): mcr.microsoft.com/powershell:alpine-3.21`
- [ ] `Logging in to Azure Container Registry` (ACR login attempt)
- [ ] `✓ ACR login succeeded`
- [ ] `Pulling image (attempt 1/...): mcr.microsoft.com/powershell:alpine-3.21`
- [ ] `✓ Pull succeeded: mcr.microsoft.com/powershell:alpine-3.21`
- [ ] `Re-tagging ... for ACR...`
- [ ] `Pushing image (attempt 1/...): <RegistryName>.azurecr.io/poshmcp:test-mode-a-001`
- [ ] `Pushing image (attempt 1/...): <RegistryName>.azurecr.io/poshmcp:latest`
- [ ] `✓ Source image pulled, re-tagged, and pushed to: <RegistryName>.azurecr.io`
- [ ] `Deploying infrastructure with Bicep...`
- [ ] `✓ Deployment completed successfully!`

**Negative checks — confirm these are absent**:
- [ ] No line containing `Building container image...`
- [ ] No line containing `docker build`
- [ ] `$LASTEXITCODE` is 0 after the script completes

**Post-run ACR verification**:
```powershell
az acr repository show-tags --name <RegistryName> --repository poshmcp --output table
```
- [ ] Tag `test-mode-a-001` is listed
- [ ] Tag `latest` is listed

---

### MA-02: Re-tag format matches spec (`$RegistryServer/poshmcp:$ImageTag`)

After MA-01, verify the full image name format:

```powershell
az acr repository show --name <RegistryName> --image poshmcp:test-mode-a-001
```

- [ ] Image exists at `<RegistryName>.azurecr.io/poshmcp:test-mode-a-001`
- [ ] Image exists at `<RegistryName>.azurecr.io/poshmcp:latest`
- [ ] The deployed Container App's image field matches `<RegistryName>.azurecr.io/poshmcp:test-mode-a-001`

```powershell
az containerapp show --name poshmcp --resource-group <ResourceGroup> `
    --query "properties.template.containers[0].image" -o tsv
```

---

### MA-03: Docker Hub short-form image reference

```powershell
.\infrastructure\azure\deploy.ps1 `
    -RegistryName <RegistryName> `
    -ResourceGroup <ResourceGroup> `
    -SourceImage 'alpine:3.20' `
    -ImageTag 'test-mode-a-alpine'
```

**Expected**:
- [ ] `docker pull alpine:3.20` succeeds (or the equivalent pull log line)
- [ ] Re-tag and push proceed normally
- [ ] No error about invalid image reference format

---

### MA-04: Mode A does NOT call `docker build`

During MA-01 or MA-03, capture output and confirm absence of build:

```powershell
$output = .\infrastructure\azure\deploy.ps1 `
    -RegistryName <RegistryName> `
    -ResourceGroup <ResourceGroup> `
    -SourceImage 'mcr.microsoft.com/powershell:alpine-3.21' `
    -ImageTag 'test-mode-a-nobuil' 2>&1 | Out-String

$output -match 'docker build'    # Must be: False
$output -match 'Building container image'  # Must be: False
$output -match 'docker pull'     # Must be: True
```

- [ ] `$output -match 'docker build'` returns `False`
- [ ] `$output -match 'docker pull'` returns `True` (or the pull log line appears)

---

### MA-05: Source image not found — clear error, exit code 1

```powershell
.\infrastructure\azure\deploy.ps1 `
    -RegistryName <RegistryName> `
    -ResourceGroup <ResourceGroup> `
    -SourceImage 'mcr.microsoft.com/poshmcp/does-not-exist:v999' `
    -ImageTag 'test-mode-a-404'
$exit = $LASTEXITCODE
```

**Expected**:
- [ ] Script exits without completing deployment
- [ ] Error message contains the image name: `does-not-exist:v999`
- [ ] Error message contains words like `not found`, `failed`, or `accessible`
- [ ] `$exit` is 1 (runtime error)
- [ ] No Bicep deployment was attempted (no `Deploying infrastructure` log line)

---

### MA-06: Retry behavior on transient pull failure

> **Semi-automated** — requires simulating a network error. If a direct network simulation is not available, skip to the retry logic code review instead.

**Option A (manual network simulation)**: Block `mcr.microsoft.com` in the hosts file temporarily, run the script, then restore.

**Option B (code review)**: Verify `Invoke-DockerPullWithRetry` in deploy.ps1:
- [ ] Function exists at approximately line 208
- [ ] Loop runs up to `$script:PushMaxAttempts` times (default: 4)
- [ ] `Test-IsTransientNetworkError` is called on pull failure output
- [ ] On transient error: `Write-Warning "Docker pull failed with a transient network error..."` appears
- [ ] On transient error: `Start-Sleep` is called with exponential backoff
- [ ] On permanent error: script stops after first non-transient failure

---

## Section 4: Mode B Tests — ACR Import (Pull-through Cache)

Mode B is triggered when **both** `-SourceImage` **and** `-UseRegistryCache` are provided.

### MB-01: Happy path — ACR import, no local docker pull

```powershell
.\infrastructure\azure\deploy.ps1 `
    -RegistryName <RegistryName> `
    -ResourceGroup <ResourceGroup> `
    -Subscription <Subscription> `
    -SourceImage 'mcr.microsoft.com/powershell:alpine-3.21' `
    -UseRegistryCache `
    -ImageTag 'test-mode-b-001'
```

**Expected output**:
- [ ] `Using ACR import (pull-through cache) for source image: mcr.microsoft.com/powershell:alpine-3.21`
- [ ] `Logging in to Azure Container Registry` (ACR login attempt)
- [ ] `✓ ACR login succeeded`
- [ ] `Importing image into ACR (attempt 1/...): mcr.microsoft.com/powershell:alpine-3.21 -> poshmcp:test-mode-b-001`
- [ ] `✓ ACR import succeeded: poshmcp:test-mode-b-001`
- [ ] `Importing image into ACR (attempt 1/...): mcr.microsoft.com/powershell:alpine-3.21 -> poshmcp:latest`
- [ ] `✓ ACR import succeeded: poshmcp:latest`
- [ ] `✓ Source image imported into ACR: <RegistryName>.azurecr.io/poshmcp:test-mode-b-001`
- [ ] `Deploying infrastructure with Bicep...`
- [ ] `✓ Deployment completed successfully!`

**Negative checks**:
- [ ] No line containing `docker pull` or `Pulling image`
- [ ] No line containing `docker build` or `Building container image`
- [ ] `$LASTEXITCODE` is 0

---

### MB-02: Mode B calls `az acr import`, not `docker pull`

```powershell
$output = .\infrastructure\azure\deploy.ps1 `
    -RegistryName <RegistryName> `
    -ResourceGroup <ResourceGroup> `
    -SourceImage 'docker.io/library/alpine:3.20' `
    -UseRegistryCache `
    -ImageTag 'test-mode-b-import' 2>&1 | Out-String

$output -match 'az acr import'     # Must be: True
$output -match 'docker pull'       # Must be: False
$output -match 'docker build'      # Must be: False
```

- [ ] `az acr import` appears in output
- [ ] `docker pull` does NOT appear in output
- [ ] `docker build` does NOT appear in output

---

### MB-03: Both `$ImageTag` and `latest` are imported

After MB-01, verify both tags exist in ACR:

```powershell
az acr repository show-tags --name <RegistryName> --repository poshmcp --output table
```

- [ ] Tag `test-mode-b-001` is listed
- [ ] Tag `latest` is listed

---

### MB-04: `$script:FullImageName` is set correctly for deployment

After MB-01, verify the Container App uses the ACR image, not the source image:

```powershell
az containerapp show --name poshmcp --resource-group <ResourceGroup> `
    --query "properties.template.containers[0].image" -o tsv
```

- [ ] Returns `<RegistryName>.azurecr.io/poshmcp:test-mode-b-001`
- [ ] Does NOT return `mcr.microsoft.com/powershell:alpine-3.21` (the source image)

---

### MB-05: ACR import failure — clear error message

Use a non-existent source image:

```powershell
.\infrastructure\azure\deploy.ps1 `
    -RegistryName <RegistryName> `
    -ResourceGroup <ResourceGroup> `
    -SourceImage 'mcr.microsoft.com/poshmcp/does-not-exist:v999' `
    -UseRegistryCache `
    -ImageTag 'test-mode-b-404'
$exit = $LASTEXITCODE
```

**Expected**:
- [ ] Script exits without completing deployment
- [ ] Error message contains source image name
- [ ] Error message contains words like `ACR import failed`, `failed`, or `not found`
- [ ] `$exit` is 1 (runtime error)
- [ ] No Bicep deployment was attempted

---

### MB-06: Mode B retry on transient error (code review)

Verify `Invoke-AcrImportWithRetry` in deploy.ps1:
- [ ] Function exists at approximately line 246
- [ ] Loop runs up to `$script:PushMaxAttempts` times (default: 4)
- [ ] `Test-IsTransientNetworkError` is called on import failure output
- [ ] On transient error: `Write-Warning "ACR import failed with a transient network error..."` is emitted
- [ ] On transient error: `Start-Sleep` is called with exponential backoff delay
- [ ] After exhausting retries: error message says `after $attempt attempt(s)` and mentions verifying source registry

---

## Section 5: Mode C Regression Tests — Existing Build Behavior

Mode C is the pre-existing behavior: when `-SourceImage` is **not** provided, the script builds from the Dockerfile.

### MC-01: No `-SourceImage` → `Build-AndPushImage` is called

```powershell
$output = .\infrastructure\azure\deploy.ps1 `
    -RegistryName <RegistryName> `
    -ResourceGroup <ResourceGroup> `
    -Subscription <Subscription> `
    -ImageTag 'test-mode-c-001' 2>&1 | Out-String

$output -match 'Building container image'  # Must be: True
$output -match 'docker build'              # Must be: True
$output -match 'Using source image'        # Must be: False
$output -match 'ACR import'               # Must be: False
```

- [ ] `Building container image...` appears in output
- [ ] `Using source image (no local build)` does **not** appear
- [ ] `ACR import` does **not** appear
- [ ] `$LASTEXITCODE` is 0 on success

---

### MC-02: Existing parameters unchanged — no new required parameters

```powershell
# Should fail with prerequisite error, NOT with "missing parameter" error
.\infrastructure\azure\deploy.ps1 -RegistryName <RegistryName> 2>&1 | Select-Object -First 3
```

- [ ] First error (if any) is about Azure login or Docker, not about missing `-SourceImage`
- [ ] Script does not require `-SourceImage` to run

---

### MC-03: Environment variable fallbacks still work

```powershell
$env:RESOURCE_GROUP = '<ResourceGroup>'
$env:LOCATION = 'eastus'
.\infrastructure\azure\deploy.ps1 -RegistryName <RegistryName> 2>&1 | Select-Object -First 5
Remove-Item Env:\RESOURCE_GROUP
Remove-Item Env:\LOCATION
```

- [ ] Script reads `$env:RESOURCE_GROUP` and `$env:LOCATION` without error
- [ ] No regression in environment variable handling

---

### MC-04: All pre-existing parameters still accepted

```powershell
# Verify parameter names haven't changed or been removed
Get-Help .\infrastructure\azure\deploy.ps1 -Parameter * | Select-Object Name
```

- [ ] `-ResourceGroup` listed
- [ ] `-Location` listed
- [ ] `-ContainerAppName` listed
- [ ] `-RegistryName` listed
- [ ] `-ImageTag` listed
- [ ] `-Subscription` listed
- [ ] `-TenantId` listed
- [ ] **New**: `-SourceImage` listed
- [ ] **New**: `-UseRegistryCache` listed

---

## Section 6: Edge Case Tests

### EC-01: Source image in the same target ACR (Mode A)

```powershell
# First push any image to ACR manually, then use it as source:
$acrImage = "<RegistryName>.azurecr.io/poshmcp:latest"

.\infrastructure\azure\deploy.ps1 `
    -RegistryName <RegistryName> `
    -ResourceGroup <ResourceGroup> `
    -SourceImage $acrImage `
    -ImageTag 'test-ec-same-acr'
```

**Expected**:
- [ ] Script does not fail with "already exists" or circular reference error
- [ ] Pull of the ACR image succeeds (requires `docker login` to ACR first)
- [ ] Re-tag and push complete normally

---

### EC-02: Source image reference with digest (Mode A)

```powershell
# Get a digest from a known image
$digest = (docker inspect --format='{{index .RepoDigests 0}}' alpine:3.20 2>$null)
# Example: docker.io/library/alpine@sha256:1234abcd...

.\infrastructure\azure\deploy.ps1 `
    -RegistryName <RegistryName> `
    -ResourceGroup <ResourceGroup> `
    -SourceImage $digest `
    -ImageTag 'test-ec-digest'
```

**Expected**:
- [ ] Script does not crash on digest-format image reference
- [ ] Pull attempt is made with the digest reference
- [ ] If pull succeeds, re-tag and push proceed

---

### EC-03: Authentication error when source registry requires login

```powershell
# Use a private image reference without logging in first
docker logout myregistry.example.com 2>$null

.\infrastructure\azure\deploy.ps1 `
    -RegistryName <RegistryName> `
    -ResourceGroup <ResourceGroup> `
    -SourceImage 'myregistry.example.com/private/image:latest' `
    -ImageTag 'test-ec-auth'
$exit = $LASTEXITCODE
```

**Expected**:
- [ ] Error message contains `authentication` or `authorization` and mentions running `docker login`
- [ ] Error is specific — not a generic "deployment failed" message
- [ ] `$exit` is 1

---

### EC-04: Large image import via Mode B — timeout and retry behavior

> **Code review** (full test requires a multi-GB image):

- [ ] `Invoke-AcrImportWithRetry` retry loop counts up to `$script:PushMaxAttempts`
- [ ] Transient patterns in `Test-IsTransientNetworkError` include `timed out`, `timeout`, `context deadline exceeded`
- [ ] Error message after retries exhausted references the source image by name

---

### EC-05: Concurrent runs with same `-SourceImage` and same `-ImageTag`

> **Manual / observation only** — do not run in parallel in production:

- [ ] Running two deploys with the same `-ImageTag` does not cause script failure
- [ ] ACR handles duplicate tag push gracefully (latest push wins)

---

### EC-06: Docker Hub image without registry prefix (Mode B)

```powershell
.\infrastructure\azure\deploy.ps1 `
    -RegistryName <RegistryName> `
    -ResourceGroup <ResourceGroup> `
    -SourceImage 'alpine:3.20' `
    -UseRegistryCache `
    -ImageTag 'test-ec-norefix'
```

**Expected**:
- [ ] `az acr import` is called with `--source alpine:3.20`
- [ ] ACR resolves the short-form reference (Docker Hub) correctly
- [ ] Import succeeds or fails with a descriptive error (not a crash)

---

## Section 7: End-to-End Health Verification

After any successful deployment (Mode A or Mode B), verify the application is healthy:

### EE-01: Container App health endpoint responds

```powershell
$fqdn = az containerapp show `
    --name poshmcp `
    --resource-group <ResourceGroup> `
    --query "properties.configuration.ingress.fqdn" -o tsv

Invoke-WebRequest -Uri "https://$fqdn/health/ready" -UseBasicParsing
```

- [ ] HTTP status 200 returned
- [ ] Application is running the image from ACR (not a stale image)

---

### EE-02: Deployed image tag matches the `-ImageTag` parameter

```powershell
az containerapp show --name poshmcp --resource-group <ResourceGroup> `
    --query "properties.template.containers[0].image" -o tsv
# Expected: <RegistryName>.azurecr.io/poshmcp:<ImageTag>
```

- [ ] Image tag in Container App matches the `-ImageTag` value you passed
- [ ] Image registry is `<RegistryName>.azurecr.io`, not the source registry

---

## Notes for Testers

- **ACR import may be slow** for large images (minutes). Mode B tests should use small images like `alpine:3.20` (~3 MB).
- **Exit codes**: The spec requires exit code 2 for parameter validation errors (SC-053) and exit code 1 for runtime errors (SC-054). Verify both with `$LASTEXITCODE` immediately after the script exits.
- **Verbose output**: Add `-Verbose` to any command to see the exact `docker` and `az acr` commands being executed. This makes it easy to verify Mode A vs. Mode B command dispatch.
- **Cleanup**: After testing, remove test tags from ACR to avoid clutter:
  ```powershell
  az acr repository delete --name <RegistryName> --image poshmcp:test-mode-a-001 --yes
  ```
- **FR-206 / SC-056**: Both `$ImageTag` and `latest` tags must always be pushed/imported. Check for both in ACR after every Mode A and Mode B test.
