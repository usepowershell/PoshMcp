# Decisions

No active decisions.



### 2026-04-23 10:03: Implemented spec 007 - deploy.ps1 source image support
**By:** Amy
**What:** Added -SourceImage and -UseRegistryCache parameters to deploy.ps1; added Mode A (docker pull + re-tag), Mode B (az acr import), and Mode C (existing build) routing
**Why:** Steven requested feature to deploy pre-built images without local build


### 2026-04-20T21:20:00Z: User directive
**By:** Steven Murawski (via Copilot)
**What:** Release notes must always be added to the docs TOC when a new release notes file is created.
**Why:** User request — captured for team memory after v0.8.0 release notes were not wired up in the TOC


### 2026-07-28: Spec 007 - deploy.ps1 source image support

**By:** Farnsworth

**What:** Created spec 007 for `infrastructure/azure/deploy.ps1` source image and ACR pull-through cache support

**Why:** Steven requested feature to allow pulling pre-built container images instead of always building from Dockerfile locally. Enables faster deployments, artifact promotion workflows, and bandwidth optimization via ACR's pull-through cache for large images.

**Decision Points:**

1. **Parameter names and design**:
   - `-SourceImage` (string): Optional container image reference; when provided, suppresses local build
   - `-UseRegistryCache` (switch): Optional flag; requires `-SourceImage`; enables `az acr import` instead of local pull
   - Validation: `-UseRegistryCache` without `-SourceImage` is a usage error (exit code 2)

2. **Execution modes**:
   - **Mode A** (default when `-SourceImage` provided): Local pull + re-tag + push (`docker pull` → `docker tag` → `docker push`)
   - **Mode B** (when both flags provided): ACR import (`az acr import` directly into ACR, no local pull)
   - **Mode C** (backward compatibility): Build from Dockerfile (no changes to existing behavior)

3. **Retry and error handling**:
   - Reuse existing `Invoke-DockerPushWithRetry` logic for pull failures in Mode A
   - Implement similar retry for `az acr import` in Mode B (exponential backoff, transient error detection)
   - Clear, actionable error messages for each failure scenario

4. **Backward compatibility**:
   - When `-SourceImage` is not provided, script behavior is unchanged
   - All existing parameters and environment variables remain functional
   - No breaking changes to the public contract

5. **Image tagging**:
   - Source image re-tagged to `$RegistryServer/poshmcp:$ImageTag` and `$RegistryServer/poshmcp:latest`
   - Same tagging pattern as current `Build-AndPushImage` for consistency

**Spec location:** `specs/007-deploy-source-image/spec.md`

**Next steps:** Triage into GitHub issues and assign to implementation agent (Bender recommended for Azure/Docker CLI expertise).


### 2026-07-28: Test cases for spec 007
**By:** Fry
**What:** Wrote manual test checklist for deploy.ps1 source image feature
**Why:** Need verification procedures for the three execution modes and error cases

