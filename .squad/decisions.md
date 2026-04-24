# Decisions

## Recent Decisions

### 2026-04-24: Release v0.8.4 Pushed
**By:** Amy (DevOps/Platform Engineer)
**Status:** Applied
**What:** Bumped version to 0.8.4, built poshmcp.0.8.4.nupkg, updated global install, committed (f5583fe), created and pushed annotated tag v0.8.4. Rebase required to resolve merge commit rejected by branch protection.
**Why:** Security patch release fixing CVE-2026-40894.
**Rule Going Forward:** Rebase onto origin/main before pushing to avoid merge commit rejections on protected branches.

### 2026-07-18: Canonical Infrastructure Defaults for PoshMcp Azure Deployment
**By:** Amy (DevOps / Platform / Azure Engineer)
**Status:** Applied
**What:** Aligned all deploy scripts to match canonical defaults defined in `main.bicep` and `parameters.json`. Updated `deploy.ps1`, `deploy.sh`, and `validate.ps1` to use `rg-poshmcp` instead of `poshmcp-rg` for resource group default.
**Why:** Bicep/parameters.json is source of truth; scripts using different defaults would deploy to wrong resource group. Running with script defaults would create duplicate resource group.
**Rule Going Forward:** Bicep and parameters.json are the authoritative sources for infrastructure defaults. Deploy scripts are wrappers — their defaults must mirror Bicep/parameters, not diverge.



### 2026-04-23 10:03: Implemented spec 007 - deploy.ps1 source image support
**By:** Amy
**What:** Added -SourceImage and -UseRegistryCache parameters to deploy.ps1; added Mode A (docker pull + re-tag), Mode B (az acr import), and Mode C (existing build) routing
**Why:** Steven requested feature to deploy pre-built images without local build

### 2026-04-23: Add session-recall skill
**By:** Steven Murawski (via Farnsworth)
**What:** session-recall CLI wired into coordinator startup behavior via .squad/skills/session-recall/SKILL.md
**Why:** Provides progressive session recall after crashes/compaction using installed CLI tool; preferred over raw SQL patterns

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


# Decision: Use `poshmcp build` in deploy.ps1 instead of `docker build`

**Date:** 2026-07-18
**Author:** Amy (DevOps/Platform)
**Context:** `infrastructure/azure/deploy.ps1` — `Build-AndPushImage` function

## Decision

The `Build-AndPushImage` function in the Azure deploy script now uses `poshmcp build --tag <image>` instead of calling `docker build` directly.

## Rationale

Steven requested that any image-building step in the deploy pipeline go through the `poshmcp build` CLI, which:
- Auto-detects docker vs podman
- Is the canonical build interface for this project
- Ensures consistent build behavior with the rest of the toolchain

## Implementation Detail

`poshmcp build` only supports a single `--tag` argument per invocation. The original `docker build` call applied both the versioned tag and `latest` in one pass (`-t $FullImageName -t $latestImage`). To keep a single build, we call `poshmcp build --tag $FullImageName` for the build step, then `docker tag $FullImageName $latestImage` to alias the result. The push logic is unchanged.

## Impact

- `Build-AndPushImage` no longer calls `docker build` directly.
- `docker tag` (not `docker build`) is used to apply the `latest` alias — this is acceptable because the restriction was specifically on the build operation.
- `poshmcp` must be installed as a dotnet global tool on the machine or agent running the deploy script.

### 2026-04-23T15:56:32-05:00: Deploy script config precedence and appsettings contract
**By:** Amy
**What:** Added appsettings-sourced deployment configuration to `infrastructure/azure/deploy.ps1` via `-AppSettingsFile` / `DEPLOY_APPSETTINGS_FILE`, with explicit precedence `CLI > env > appsettings > defaults`. Introduced `AzureDeployment` appsettings section (also supports `Deployment.Azure`) and added `infrastructure/azure/deploy.appsettings.json.template` as scaffold-ready template.
**Why:** Preserve existing deploy workflow while enabling repeatable environment-specific deployment configuration from file, especially for CI/bootstrap scaffolds.

### 2026-04-23T16:05:12Z: Add CLI scaffold command backed by embedded infra artifacts
**By:** Bender
**What:** Added `poshmcp scaffold` to materialize an `infra/azure` folder from assembly-embedded deployment files (`deploy.ps1`, bicep files, and parameters) into a target project directory with optional `--force` overwrite behavior.
**Why:** Ensures scaffold works both from source and packaged tool installations without depending on repository-relative filesystem paths.

### 2026-04-23T17:28:05Z: server appsettings to Container App env vars
**By:** Amy (DevOps/Platform) - requested by Steven Murawski
**What:** Translate MCP server appsettings.json into Container App environment variables via deploy.ps1.
**Decisions:**
- Removed `powerShellFunctions` Bicep param from `resources.bicep` and `main.bicep`. Covered by translated env var array.
- Removed `enableDynamicReloadTools` Bicep param; its env var is now emitted from server appsettings translation.
- Renamed Bicep `extraEnvVars` param to `serverEnvVars`.
- Renamed `-McpAppSettingsFile` param in `deploy.ps1` to `-ServerAppSettingsFile`; added `POSHMCP_APPSETTINGS_FILE` env var support; kept auto-discovery.
- Added translations for IncludePatterns, ExcludePatterns, EnableConfigurationTroubleshootingTool, and Logging.LogLevel.Default.
- Fixed RuntimeMode normalization: server expects "InProcess"/"OutOfProcess". deploy.ps1 previously emitted "in-process"/"out-of-process" - corrected.
**Why:** Single source of truth - container configured identically to local server from one appsettings file.

### 2026-04-24: Version bump to 0.8.3 with release metadata alignment
**By:** Amy
**What:** Chose a patch release bump from `0.8.2` to `0.8.3`, aligned version-bearing artifacts by updating `PoshMcp.Server/PoshMcp.csproj`, and ensured release notes index coverage by adding `docs/release-notes/0.8.3.md` into `docs/toc.yml`.
**Why:** Patch bump is the safest default when no target version is specified, and team convention requires release-notes TOC alignment whenever a new release notes file is added.
**Operational Outcome:** Build and pack completed successfully and produced `artifacts/nupkg/poshmcp.0.8.3.nupkg`.
**Merged from inbox:** `.squad/decisions/inbox/amy-version-bump-pack-update.md`

