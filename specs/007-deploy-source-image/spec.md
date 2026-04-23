# Feature Specification: Deploy Script Source Image Support

**Spec Number**: 007  
**Feature Branch**: `deploy-source-image`  
**Created**: 2026-07-28  
**Status**: Draft  
**Input**: Add optional parameters to `infrastructure/azure/deploy.ps1` to support pulling pre-built container images instead of always building locally, with optional ACR pull-through caching

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Skip Local Build with Pre-built Image (Priority: P1)

A DevOps engineer has a pre-built container image `myregistry.azurecr.io/poshmcp:v1.2.0`
from a CI/CD pipeline. They want to deploy this image to Azure Container Apps without
rebuilding it locally. Today, the deploy script always builds from the Dockerfile, which is
inefficient when a known-good image already exists. The engineer wants to pass
`-SourceImage myregistry.azurecr.io/poshmcp:v1.2.0`, skip the local build entirely,
and deploy directly.

**Why this priority**: Reduces deployment time by 10-15 minutes (no local build); enables
one-image-many-deployments workflows; aligns with GitOps/artifact promotion patterns.

**Independent Test**: Run `deploy.ps1 -SourceImage 'mcr.microsoft.com/powershell:latest'` and
verify: (a) no local Docker build occurs, (b) image is pulled and re-tagged for ACR,
(c) script deploys the re-tagged image successfully.

**Acceptance Scenarios**:

1. **Given** `-SourceImage` is provided and `-UseRegistryCache` is not used, **When** `deploy.ps1` runs, **Then** the script pulls the source image locally, re-tags it for the target ACR, pushes the re-tagged image to ACR, and deploys without calling `docker build`
2. **Given** `-SourceImage` is provided and points to an image in a different registry, **When** the script runs, **Then** the image is successfully pulled (with Docker login if needed), re-tagged, and pushed to the target ACR
3. **Given** `-SourceImage` is provided but the source image cannot be found, **When** the script runs, **Then** a clear error message is shown indicating the image was not found or is unreachable
4. **Given** `-SourceImage` is not provided, **When** the script runs, **Then** the script builds from the Dockerfile as today (backward compatibility maintained)

---

### User Story 2 — ACR Pull-through Cache with Source Image (Priority: P2)

A DevOps engineer has a pre-built image in Docker Hub (`library/ubuntu:latest`) and wants to
use it as the base for PoshMcp deployment. Pulling a multi-GB image from public Docker Hub to
a local machine on every deployment is slow. The engineer wants to pass
`-SourceImage docker.io/library/ubuntu:latest -UseRegistryCache`, causing the script to use
Azure Container Registry's pull-through cache feature (`az acr import`) to pull the image
directly into ACR without downloading it locally. This is faster and does not consume local
bandwidth.

**Why this priority**: Enables efficient large-image import; reduces local bandwidth; useful for
air-gapped or high-latency networks; integrates with ACR's native pull-through cache.

**Independent Test**: Run `deploy.ps1 -SourceImage 'docker.io/library/alpine:latest' -UseRegistryCache`
and verify: (a) `az acr import` is called (not local `docker pull`), (b) source image is imported
into ACR, (c) deployment uses the ACR-cached image.

**Acceptance Scenarios**:

1. **Given** `-SourceImage` and `-UseRegistryCache` are both provided, **When** the script runs, **Then** `az acr import` is called to pull the source image directly into ACR, and no local `docker pull` occurs
2. **Given** ACR import succeeds, **When** deployment continues, **Then** the cached ACR image is used and deployed successfully
3. **Given** `az acr import` fails (e.g., source registry is unreachable), **When** the script runs, **Then** a clear error message indicates the import failure and suggests troubleshooting steps
4. **Given** `-UseRegistryCache` is provided without `-SourceImage`, **When** the script runs, **Then** the script exits with a validation error explaining that `-UseRegistryCache` requires `-SourceImage`

---

### Edge Cases

- **Source image with digest**: If `-SourceImage` is `registry.io/image:tag@sha256:abc...`, both tag and digest are preserved through re-tagging.
- **Source image in the same ACR**: If `-SourceImage` points to the target ACR already, the script recognizes this and copies/re-tags without re-pulling.
- **Very large images**: With `-UseRegistryCache`, import time may be several minutes; progress is shown and timeouts are generous.
- **Authentication to source registry**: If the source image requires authentication (e.g., private Docker Hub image), the script attempts to use existing Docker credentials. If login fails, a clear message suggests running `docker login` first.
- **Concurrent deployments with same `-SourceImage`**: Multiple runs with the same source image may result in multiple re-tags to the same ACR. Azure handles this gracefully (tags are updated in-place).
- **Source image tag does not exist**: `docker pull` fails with a clear "not found" message; deployment exits early.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-200**: Script MUST accept an optional `-SourceImage` parameter (string, not mandatory) specifying a container image reference to use instead of building from Dockerfile
- **FR-201**: Script MUST accept an optional `-UseRegistryCache` switch (boolean, default `$false`) that, when combined with `-SourceImage`, uses `az acr import` to pull the source image into ACR instead of pulling locally
- **FR-202**: When `-SourceImage` is provided and `-UseRegistryCache` is `$false` (or not provided), the script MUST pull the source image using `docker pull`, re-tag it for the target ACR, push it using `docker push`, and then deploy (Mode A)
- **FR-203**: When both `-SourceImage` and `-UseRegistryCache` are provided, the script MUST call `az acr import` to pull the source image directly into ACR, and then deploy using the imported image (Mode B)
- **FR-204**: When `-SourceImage` is not provided, the script MUST build the image from the Dockerfile as today, unchanged (Mode C — backward compatibility)
- **FR-205**: Script MUST validate that `-UseRegistryCache` is only provided together with `-SourceImage`; if `-UseRegistryCache` is used without `-SourceImage`, script exits with error code and clear message
- **FR-206**: Script MUST re-tag the source image to the format `$RegistryServer/poshmcp:$ImageTag` and `$RegistryServer/poshmcp:latest` before pushing or using
- **FR-207**: Script MUST handle image references with and without registry hostname (e.g., `ubuntu:latest` vs `docker.io/library/ubuntu:latest`)
- **FR-208**: Script MUST skip the `Build-AndPushImage` function when `-SourceImage` is provided and substitute the pull/re-tag/push flow instead
- **FR-209**: Script MUST log each step of the pull/re-tag/push flow (or ACR import flow) with informational messages so the operator can follow progress
- **FR-210**: Script MUST set `$script:FullImageName` to the final re-tagged ACR image reference so downstream functions (e.g., `Deploy-Infrastructure`) use the correct image
- **FR-211**: Script MUST validate that the source image can be pulled (or imported) before proceeding to deployment; if the source image is not found, exit with error and clear message
- **FR-212**: Script MUST use the same retry logic for `docker pull` (from Mode A) as currently exists for `docker push` (transient error detection, exponential backoff)
- **FR-213**: In Mode B (ACR import), the script MUST use `az acr import --source <source-image> --image <target-tag>` to perform the import; source and target registries may be different
- **FR-214**: Script MUST remain backward compatible: all existing behavior when `-SourceImage` is not provided is unchanged

### Parameter Design

#### `-SourceImage` Parameter

- **Type**: `[string]` (optional)
- **Default**: `$null` (not provided)
- **Validation**:
  - Must be a valid container image reference (e.g., `registry.io/image:tag`, `image:tag`, `registry.io/image@sha256:...`)
  - Operators are responsible for ensuring the image exists; the script validates by attempting to pull or import
  - No length limit enforced by the script (relies on registry limits)
- **Mutual Dependencies**:
  - When provided, `-SourceImage` suppresses the `Build-AndPushImage` call
  - When not provided, the script falls back to Mode C (build from Dockerfile) regardless of other parameters
- **Behavior with `-UseRegistryCache`**:
  - If `-SourceImage` is provided and `-UseRegistryCache` is `$false` or not provided → Mode A (pull locally)
  - If `-SourceImage` is provided and `-UseRegistryCache` is `$true` → Mode B (ACR import)
  - If `-SourceImage` is not provided and `-UseRegistryCache` is `$true` → **VALIDATION ERROR**: exit with code 2 and message "Parameter -UseRegistryCache requires -SourceImage to be provided"

#### `-UseRegistryCache` Parameter

- **Type**: `[switch]` (optional, boolean)
- **Default**: `$false`
- **Validation**:
  - Must only be provided together with `-SourceImage` (see mutual dependency above)
  - No value is required (it is a flag)
- **Effect**:
  - When `$true`, changes the pull strategy from local `docker pull` to ACR's `az acr import` command
  - When `$false` or not provided, uses local `docker pull` + `docker push` workflow

### Execution Flow

#### Mode A: Source Image + Local Pull (when `-SourceImage` is provided, `-UseRegistryCache` is false or omitted)

1. Skip prerequisite check for Docker build tooling (local Docker is still needed for pull/push)
2. Call `Invoke-AcrLoginWithRetry` (already exists)
3. Pull source image: `docker pull $SourceImage`
   - On failure (transient): retry with exponential backoff (same retry logic as `Invoke-DockerPushWithRetry`)
   - On failure (permanent): exit with error message showing the failure reason
4. Re-tag for target ACR:
   ```
   docker tag $SourceImage $script:FullImageName
   docker tag $SourceImage "$RegistryServer/poshmcp:latest"
   ```
5. Push re-tagged images: `docker push $script:FullImageName` and `docker push "$RegistryServer/poshmcp:latest"` (use `Invoke-DockerPushWithRetry`)
6. Continue to `Deploy-Infrastructure` (unchanged)

#### Mode B: Source Image + ACR Import (when `-SourceImage` and `-UseRegistryCache` are both provided)

1. Skip prerequisite check for Docker build tooling (Docker is not needed; only `az acr` CLI)
2. Call `Invoke-AcrLoginWithRetry` (already exists) — needed for ACR credentials
3. Determine the target image reference within ACR:
   ```
   $targetImage = "poshmcp:$ImageTag"
   ```
4. Call `az acr import` to pull source image into ACR:
   ```
   az acr import --registry $RegistryName --source $SourceImage --image $targetImage
   ```
   - On failure: exit with error message showing the import failure
   - Include retry logic with exponential backoff for transient errors (e.g., network timeouts)
5. Set `$script:FullImageName = "$RegistryServer/poshmcp:$ImageTag"` (image now exists in ACR)
6. Also tag as `:latest` in ACR (optional: use `az acr repository update` or `docker tag` + push if Docker is available)
7. Continue to `Deploy-Infrastructure` (unchanged)

#### Mode C: Build from Dockerfile (when `-SourceImage` is not provided)

- Call `Build-AndPushImage` as today (no changes)

### Backward Compatibility

- When `-SourceImage` is not provided, the script behaves identically to today: build from Dockerfile, push to ACR, deploy
- All existing parameters (`-ResourceGroup`, `-Location`, `-ContainerAppName`, `-RegistryName`, `-ImageTag`, `-Subscription`, `-TenantId`) remain unchanged
- All existing environment variable support (e.g., `$env:RESOURCE_GROUP`, `$env:LOCATION`) remains unchanged
- Calling the script without `-SourceImage` MUST produce identical behavior to the current version

## Error Handling

### Error Cases and Recovery

1. **Invalid `-SourceImage` reference**:
   - Error message: "Invalid source image reference: 'xyz'. Expected format: 'registry.io/image:tag' or 'image:tag'"
   - Exit code: 2 (usage error)
   - Recovery: Operator corrects the image reference and re-runs

2. **Source image not found (docker pull)**:
   - Error message: "Docker pull failed for source image 'registry.io/image:tag'. Error: [Docker error output]. Verify the image exists and is accessible."
   - Exit code: 1 (runtime error)
   - Recovery: Operator verifies image exists in source registry, checks authentication, and re-runs

3. **Source image not found (az acr import)**:
   - Error message: "ACR import failed for source image 'docker.io/image:tag'. Error: [az output]. Verify the source registry is accessible and the image exists."
   - Exit code: 1 (runtime error)
   - Recovery: Operator verifies source registry reachability, checks permissions, and re-runs

4. **ACR login fails before pulling source image**:
   - Existing `Invoke-AcrLoginWithRetry` handles this with retries and clear error messages
   - Exit code: 1 (runtime error)

5. **Docker push fails (Mode A)**:
   - Existing `Invoke-DockerPushWithRetry` handles this with retries and clear error messages
   - Exit code: 1 (runtime error)

6. **Network timeout during import (Mode B)**:
   - Implement retry logic in the ACR import step (exponential backoff, up to 3 attempts)
   - Error message after retries exhausted: "ACR import timed out after 3 attempts. The source image may be very large or the network connection is unstable."
   - Exit code: 1 (runtime error)

7. **`-UseRegistryCache` without `-SourceImage`**:
   - Error message: "Parameter -UseRegistryCache requires -SourceImage to be provided"
   - Exit code: 2 (usage error)
   - Recovery: Operator adds `-SourceImage` parameter

## Testing Strategy

### Functional Tests

- **Test Case 1**: Mode A with public Docker Hub image (`library/alpine:latest`)
  - Verify: script runs, image is pulled, re-tagged, pushed to ACR, deployment succeeds
- **Test Case 2**: Mode A with private registry image (mock/skip actual pull if not in test environment)
  - Verify: authentication is attempted, clear error if credentials are missing
- **Test Case 3**: Mode B with ACR import (`az acr import` is called, no local `docker pull` occurs)
  - Verify: image is imported, script proceeds to deployment
- **Test Case 4**: Mode C without `-SourceImage` (backward compatibility)
  - Verify: script builds from Dockerfile, behavior identical to today
- **Test Case 5**: Error case — source image not found
  - Verify: script exits with error code 1 and clear message
- **Test Case 6**: Error case — `-UseRegistryCache` without `-SourceImage`
  - Verify: script exits with error code 2 and clear message

### Manual Testing

- Deploy with `-SourceImage mcr.microsoft.com/powershell:latest` on a local machine or CI/CD pipeline
- Deploy with `-SourceImage docker.io/library/alpine:latest -UseRegistryCache` in an environment with Azure CLI access
- Verify deployment FQDN and health checks pass in both cases

## Assumptions

- The script runs on a machine with Azure CLI (`az`) installed (pre-existing requirement)
- For Mode A, Docker CLI (`docker`) must be installed and the user must be logged in to both source and target registries via `docker login` (pre-existing requirement)
- For Mode B, only Azure CLI is required; Docker is optional (but if available, can be used for the `:latest` tag push)
- The retry logic from `Invoke-DockerPushWithRetry` and `Invoke-AcrLoginWithRetry` is reusable for the new pull and import operations
- ACR is the target registry (already a hard requirement in this script)
- The source image reference can be any valid container image reference that resolves in Docker Hub, registries, or public registries; private registries require the operator to have pre-authenticated with `docker login` or Azure credentials
- The `ImageTag` parameter format (default: `yyyyMMdd-HHmmss`) remains unchanged and is applied consistently to re-tagged images

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-050**: When `-SourceImage` is provided without `-UseRegistryCache`, the script calls `docker pull`, `docker tag`, and `docker push` in sequence and does not call `docker build`
- **SC-051**: When `-SourceImage` and `-UseRegistryCache` are provided, the script calls `az acr import` and does not call `docker pull` or `docker build`
- **SC-052**: When `-SourceImage` is not provided, the script calls `docker build` as today
- **SC-053**: When `-UseRegistryCache` is provided without `-SourceImage`, the script exits with error code 2 and a validation error message
- **SC-054**: Source image not found errors result in exit code 1 and a descriptive error message (not generic failure)
- **SC-055**: All existing deploy.ps1 behavior is preserved when `-SourceImage` is not provided (backward compatibility verified via regression testing)
- **SC-056**: Re-tagged images are pushed to ACR with both `$ImageTag` and `latest` tags (same as current `Build-AndPushImage`)
- **SC-057**: Parameter validation happens before any Docker/ACR commands are executed; invalid parameter combinations fail fast with exit code 2

## Dependencies

- Existing script functions: `Invoke-AcrLoginWithRetry`, `Invoke-DockerPushWithRetry`, `Deploy-Infrastructure`, `Get-DeploymentInfo`
- Azure CLI (`az`) with `acr import` support
- Docker CLI (`docker`) for Mode A
- PowerShell 7+ (already required by this script)
