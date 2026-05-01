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



# Decision: -GenerateDockerfile switch for docker.ps1

**Date:** 2026-07-28
**By:** Amy (DevOps/Platform/Azure)
**Status:** Applied

## What

Added `-GenerateDockerfile` [switch] and `-OutputPath` [string] parameters to `docker.ps1`.

## Decisions Made

1. **`-OutputPath` has no default in `param()`** — default is computed dynamically inside each command block:
   - Base build: `./Dockerfile.generated`
   - Custom build: `./Dockerfile.<Template>.generated`
   This avoids a static default that would be wrong for the `build-custom` case.

2. **Feature scope** — `-GenerateDockerfile` is only meaningful for `build`/`build-base` and `build-custom`. It is silently ignored for `run`, `stop`, `logs`, and `clean` (the switch is simply not tested in those branches). No warning emitted — the existing command still executes normally.

3. **Header format** — Includes `# Generated by PoshMcp docker.ps1`, the equivalent `docker build` command, an ISO 8601 timestamp, and a copy-paste-ready build command referencing the output file. Azure template appends an env-var note.

4. **Source content** — The generated file is header + verbatim source Dockerfile content. No mutations to the Dockerfile itself.

5. **`Set-Content -NoNewline`** — Used to avoid appending a spurious trailing newline that `Set-Content` adds by default. Content from `Get-Content -Raw` already contains the file's original line endings.

## Why

Provides a documented, archivable snapshot of the exact Dockerfile used for any build invocation, useful for audit trails, CI artifact storage, and debugging build regressions without re-running the full build.

## Files Changed

- `docker.ps1` — new parameters, updated help block, build command logic


# Decision: poshmcp build --generate-dockerfile

**Date:** 2026-07-28
**By:** Amy (DevOps/Platform)
**Status:** Applied

## What

Added `--generate-dockerfile` and `--dockerfile-output` options to `poshmcp build`.

## Decision Points

1. **New CLI options:**
   - `--generate-dockerfile` (bool/switch): when set, write the Dockerfile to disk and exit; do not invoke docker/podman.
   - `--dockerfile-output` (string, optional): destination path; default `./Dockerfile.generated`.

2. **Dockerfile header format:**
   - `# Generated by poshmcp build`
   - `# Equivalent build command: docker build -f <output-path> -t <image-tag> [--build-arg ...] .`
   - `# Generated: <ISO 8601 UTC timestamp>`
   - Blank line separator before the actual Dockerfile content.

3. **Implementation split:**
   - `DockerRunner.GenerateDockerfile(...)` in `Cli/DockerRunner.cs` owns file I/O and header construction.
   - `Program.cs` handler owns option parsing and console output (success message + manual build hint).

4. **Handler pattern change:**
   - Switched the `buildCommand.SetHandler` from the typed-parameter overload to `InvocationContext`-based pattern to accommodate 8 options without hitting System.CommandLine overload limits.

5. **No docker/podman detection when `--generate-dockerfile` is set:**
   - The flag check and early-return happen *before* `DetectDockerCommand()` is called, so the CLI works even in environments without docker/podman installed.

## Rule Going Forward

When adding more than ~6 options to a `System.CommandLine` command handler, use the `InvocationContext`-based `SetHandler` pattern instead of the typed-parameter overload.


# Decision: appsettings bundling uses COPY injection rather than build-arg

**Author:** Bender
**Date:** 2026-05-01

## Decision

`poshmcp build --appsettings` bundles the supplied file into the image by injecting a
`COPY poshmcp-appsettings.json /app/server/appsettings.json` line into the Dockerfile, not via
`--build-arg`.

## Rationale

Using `COPY` is the correct Docker pattern for bundling files into an image:
- `--build-arg` is for scalar configuration values, not file contents.
- Embedding file content in a build-arg would require encoding, hit size limits, and make the
  Dockerfile comment unreadable.
- `COPY` is transparent, auditable, and idiomatic — the resulting Dockerfile is self-documenting.

## Implementation

- **Generate mode:** `GenerateDockerfile()` replaces/injects the `COPY` line in the Dockerfile content.
- **Build mode:** the appsettings file is staged as `poshmcp-appsettings.json` in CWD (the Docker
  build context), a temp Dockerfile (`.poshmcp-build.dockerfile`) is generated with the injected
  `COPY` line, the build runs, and both temp files are cleaned up in a `finally` block.


### 2026-04-24: Bundle install-modules.ps1 in base image
**Decision:** Copy install-modules.ps1 into the base container image at /app/install-modules.ps1
**Why:** Generated Dockerfiles (poshmcp build --generate-dockerfile) are used in repos that don't have this script locally. Bundling it eliminates the COPY dependency.


# Decision: Embed Dockerfiles in PoshMcp Assembly

**Date:** 2026-07-30
**Author:** Bender (Backend Developer)
**Requested by:** Steven Murawski

## Context

`poshmcp build --generate-dockerfile` reads Dockerfile templates from disk at runtime.
When the CLI is installed as a global dotnet tool via `dotnet tool install`, those files
do not exist on the user's machine — only the packed NuGet `.nupkg` payload is present.
This caused `Error: Dockerfile not found at examples/Dockerfile.user` for tool users.

## Decision

Embed the four Dockerfile templates directly in the `PoshMcp` assembly as `EmbeddedResource`
items in `PoshMcp.Server/PoshMcp.csproj`:

- `Dockerfile` (root) → manifest name `PoshMcp.Dockerfiles.Dockerfile`
- `examples/Dockerfile.user` → `PoshMcp.Dockerfiles.Dockerfile.user`
- `examples/Dockerfile.azure` → `PoshMcp.Dockerfiles.Dockerfile.azure`
- `examples/Dockerfile.custom` → `PoshMcp.Dockerfiles.Dockerfile.custom`

`DockerRunner.ReadEmbeddedDockerfile(name)` reads from the assembly manifest stream.
`DockerRunner.GenerateDockerfile(...)` tries embedded first, falls back to disk so local
dev workflows are unaffected.

`Program.cs` build handler: the `File.Exists(imageFile)` guard is now skipped when
`--generate-dockerfile` is active (the source doesn't need to be on disk).

## Consequences

- `poshmcp build --generate-dockerfile` works correctly after `dotnet tool install`.
- Local development (running from source) continues to work via the disk fallback.
- Dockerfile content stays in sync with the assembly version — no runtime drift.
- Four Dockerfiles add negligible size to the assembly (~4 KB total).


# Decision: `--generate-dockerfile` always defaults to `buildType = "custom"`

**Date:** current session
**Author:** Bender (Backend Dev)
**Requested by:** Steven Murawski

## Context

`poshmcp build --generate-dockerfile` is a user-facing command for generating a starter Dockerfile
that the user can customize and use to build their own container on top of the published PoshMcp
base image (`ghcr.io/usepowershell/poshmcp/poshmcp:latest`).

The previous logic branched the default `buildType` on whether `--generate-dockerfile` was active:

```csharp
var buildType = string.IsNullOrWhiteSpace(type)
    ? (generateDockerfile ? "base" : "custom")
    : type.ToLowerInvariant();
```

This caused `--generate-dockerfile` (with no `--type`) to default to `"base"`, which maps to the
root `Dockerfile` — the file for building PoshMcp itself from source. That is wrong for users.

## Decision

Always default to `"custom"` when `--type` is not supplied:

```csharp
var buildType = string.IsNullOrWhiteSpace(type)
    ? "custom"
    : type.ToLowerInvariant();
```

`"custom"` maps to `examples/Dockerfile.user`, which is the correct user-deployment template.
Users who need the source-build Dockerfile can explicitly pass `--type base`.

## Consequences

- `poshmcp build --generate-dockerfile` now emits `examples/Dockerfile.user` content by default ✅
- `poshmcp build` (no flags) is unchanged — still defaults to `"custom"` / `examples/Dockerfile.user` ✅
- `poshmcp build --type base --generate-dockerfile` still works for maintainers who want the source Dockerfile ✅


# Decision: Default build type for `--generate-dockerfile`

**Date:** 2025-07-17
**Author:** Bender (Backend Developer)
**Status:** Implemented

## Context

The `poshmcp build` command supports two image types: `base` (builds the runtime from local source using `./Dockerfile`) and `custom` (builds a derived image using `examples/Dockerfile.user`). The default when `--type` is omitted was `custom`, which makes sense for actual Docker builds because the primary user workflow is building a custom derived image from the published GHCR base.

However, `--generate-dockerfile` is a different operation — it dumps the resolved Dockerfile to disk so the user can inspect or customize it. When no `--type` is specified alongside `--generate-dockerfile`, there is no obvious "custom" Dockerfile to generate (the user hasn't specified modules or a source image), so defaulting to `base` (the plain `./Dockerfile`) is the correct zero-configuration behavior.

## Decision

When `--generate-dockerfile` is used without an explicit `--type`:
- Default `buildType` to `"base"` → uses `./Dockerfile`

When `--generate-dockerfile` is **not** used (actual Docker build) without an explicit `--type`:
- Default `buildType` to `"custom"` → uses `examples/Dockerfile.user` (existing behavior preserved)

## Consequences

- `poshmcp build --generate-dockerfile` now works out of the box without errors.
- The non-generate-dockerfile default remains `custom`, preserving the primary build workflow.
- Users who want to generate the custom Dockerfile must pass `--type custom` explicitly.


### 2026-04-24: User directive — git fetch/rebase workflow
**By:** Steven Murawski (via Copilot)
**What:** Always use `git fetch origin main` followed by `git rebase origin/main` to sync with remote before pushing. Never use merge pulls (`git pull` without `--rebase`).
**Why:** User preference — avoids stray merge commits that can trigger branch protection rejections.


### 2026-04-25: Application Insights logging spec created
**By:** Farnsworth (via Steven Murawski)
**What:** Spec at specs/application-insights-logging.md. Proposes opt-in App Insights via appsettings using Azure.Monitor.OpenTelemetry.AspNetCore. Targets post-0.8.11.
**Why:** Users running PoshMcp in Azure need logs/traces in App Insights without breaking existing logging.



# Decision: ConfigureApplicationInsights Implementation Choices

**Author**: Bender
**Date**: 2026-04-27
**Issue**: #172

## Decision 1: Use `Console.Error.WriteLine` for startup logs

The spec says "use the existing logging infrastructure" but the method signature `(IServiceCollection, IConfiguration, bool)` provides no `ILogger`. Rather than widening the signature (which would deviate from FR-307), startup messages are written to `Console.Error` — consistent with other early-startup log sites in `Program.cs` that precede host construction.

## Decision 2: Call site placement — after existing OpenTelemetry wiring

`ConfigureApplicationInsights` is called immediately after `ConfigureOpenTelemetryForHttp` (HTTP) and `ConfigureOpenTelemetry` (stdio). This ensures the existing OTel pipeline (McpMetrics, console exporter) is already registered before Azure Monitor is layered on, so FR-317 (McpMetrics flow through Azure Monitor) is satisfied without special ordering logic.

## Decision 3: Transport mode as OpenTelemetry resource attribute

FR-309 requires transport mode as a custom dimension. Implemented via `.ConfigureResource(resource => resource.AddAttributes(...))` on the `OpenTelemetryBuilder`. Resource attributes appear as custom dimensions in Azure Monitor and are set once at startup — zero per-request overhead.

## Decision 4: Clamp `SamplingPercentage` at runtime

`Math.Clamp(options.SamplingPercentage, 1, 100)` is applied before converting to ratio. This makes runtime behaviour predictable even with out-of-range config values. Doctor validation (future issue) will surface the out-of-range warning to users at config time.

### 2026-04-28: Doctor AppInsights validation architecture
**By:** Bender (Backend Dev)
**What:** Added a `ConfigurationErrors` list to `DoctorReport` (separate from `Warnings`) so `ComputeStatus` can distinguish error-level config issues from warnings. `BuildConfigurationWarnings` now returns a `(Warnings, Errors)` tuple and accepts the config path to load `ApplicationInsights` settings via `BuildRootConfiguration`. This keeps validation offline (no network calls per FR-315).
**Why:** Empty connection string with `Enabled: true` is a hard error (blocks telemetry), while a malformed format or out-of-range sampling is a softer warning. Keeping these separate preserves the existing `ComputeStatus` severity model.

### 2026-04-27: User directive
**By:** Steven Murawski (via Copilot)
**What:** Never merge main back into a branch. Feature branches must stay clean — no back-merges from main. Rebase if needed.
**Why:** User request — captured for team memory

### 2026-04-29T15:11:29Z: User directive
**By:** Steven Murawski (via Copilot)
**What:** All GitHub posts (issue creation, issue comments, PR creation, PR comments, PR reviews) MUST include the name of the agent posting it. Format: **{emoji} {AgentName} ({Role})**  at the start of the message body.
**Why:** User request - ensures traceability of which AI team member authored each GitHub interaction.

### 2026-04-27T14:50:29Z: User directive
**By:** Steven Murawski (via Copilot)
**What:** Never merge main back into a branch. Feature branches should never have main merged into them.
**Why:** User request — captured for team memory

### 2026-04-27: User directive
**By:** Steven Murawski (via Copilot)
**What:** Always use rebase. When updating feature branches with upstream changes, use git rebase, never git merge.
**Why:** User request — captured for team memory

# PR #180 Review: ConfigureApplicationInsights() — REQUEST CHANGES

**Reviewer:** Farnsworth (Lead/Architect)
**Date:** 2026-04-28
**Branch:** squad/172-configure-app-insights
**Verdict:** REQUEST CHANGES

---

## Spec Compliance Summary

| FR | Status | Notes |
|----|--------|-------|
| FR-303 | ✅ PASS | Early return when `!options.Enabled` — no SDK wiring |
| FR-304 | ✅ PASS | Env var fallback via `Environment.GetEnvironmentVariable` |
| FR-305 | ✅ PASS | `Console.Error.WriteLine` warning + return |
| FR-306 | ✅ PASS | Package is `Azure.Monitor.OpenTelemetry.AspNetCore` v1.4.0 |
| FR-307 | ✅ PASS | Exact method signature match |
| FR-308 | ✅ PASS | `services.AddOpenTelemetry().UseAzureMonitor(...)` |
| FR-309 | ✅ PASS | `transport.mode` resource attribute (becomes global dimension) |
| FR-310 | ❌ FAIL | **Not implemented** — no telemetry enrichment adds parameter names |
| FR-311 | ⚠️ GAP | No active suppression; Debug-level logs include parameter values |
| FR-312 | ⚠️ GAP | No active suppression for PowerShell output |
| FR-316 | ✅ PASS | Serilog untouched |
| FR-317 | ✅ PASS | McpMetrics meter flows through shared OTel pipeline |
| FR-318 | ✅ PASS | appsettings section present with `Enabled: false` |

---

## Required Changes

### 1. FR-310: Tool parameter names as custom properties (BLOCKING)

The spec requires tool parameter **names** to appear in custom properties on telemetry. The current implementation only wires the Azure Monitor exporter but adds no telemetry enrichment. This requires one of:

- An `ITelemetryInitializer` that inspects incoming telemetry and adds parameter name tags
- Activity tag additions in the tool execution path (in `PowerShellAssemblyGenerator.cs`)
- A custom `ActivitySource` span wrapping tool invocations that includes `param.name.*` tags

**Recommendation:** Add Activity tags at the point where tool parameters are resolved (around line 692-730 in `PowerShellAssemblyGenerator.cs`). Add tags like `tool.param.names = "Name,Id,Module"` (comma-separated list). This keeps VALUES out but exposes the schema.

### 2. FR-311/FR-312: Active suppression of parameter values and output (BLOCKING)

`UseAzureMonitor()` enables the OpenTelemetry **log exporter** by default. The existing code at `PowerShellAssemblyGenerator.cs:731-738` logs:

```csharp
logger.LogDebug("Tool parameter detail: ... Value={ParameterValue}", ..., paramValue);
```

And at line 801-808:
```csharp
logger.LogDebug("Bound parameter: ... Value={Value}", ..., convertedValue);
```

While these are at `Debug` level (suppressed by default `Information` filter), the spec says **MUST NOT** — meaning defensive suppression is required regardless of log level configuration. Options:

**Option A (preferred):** Configure `UseAzureMonitor` to disable log export entirely — only export traces + metrics:
```csharp
services.AddOpenTelemetry()
    .UseAzureMonitor(opts => { ... })
    .WithLogging(logBuilder => logBuilder.AddFilter("*", LogLevel.None)); // suppress all OTel log export
```

**Option B:** Add a log filter category that excludes the `PowerShellAssemblyGenerator` category from OTel export.

**Option C:** Strip the `Value=` fields from those log templates (replace with `HasValue={HasValue}` bool). This is the most invasive but cleanest long-term.

**I recommend Option A** for this PR — it's additive, non-invasive, and satisfies FR-311/FR-312 definitively. FR-316 (Serilog continues unchanged) is also preserved since Serilog operates at the ILogger provider level, independent of OTel log export.

---

## Non-Blocking Observations

1. **Double `AddOpenTelemetry()` call ordering:** The implementation correctly relies on `AddOpenTelemetry()` idempotency. Add a brief comment at the call site noting that this builds on the metrics registration from `ConfigureOpenTelemetry`/`ConfigureOpenTelemetryForHttp`.

2. **`SamplingRatio` type:** The SDK's `SamplingRatio` is a `float`. The division `samplingPercentage / 100.0f` is correct but note that `Math.Clamp` returns `int`, so this always produces a clean float division. Fine.

3. **Missing `SectionName` constant:** PR #177 (previously approved) included `public const string SectionName = "ApplicationInsights"` on the options class. This PR uses inline `"ApplicationInsights"` string. Minor inconsistency — prefer the constant.

---

## Architectural Assessment

The plumbing is correct. The method placement (after `ConfigureOpenTelemetry*`), the early-return guard, the connection string resolution chain, and the `ConfigureResource` approach for global dimensions are all architecturally sound. The gaps are in telemetry enrichment and defensive security filtering — both required by spec.

---

## Assignment

Return to original author for fixes. The changes are well-scoped additions to the existing method — no architectural rework needed.

# Wave 1 Review — Spec 008 Application Insights Logging

**Reviewer:** Farnsworth (Lead Architect)
**Date:** 2026-04-27
**Spec:** 008-application-insights-logging

---

## PR #176 — feat: add Azure.Monitor.OpenTelemetry.AspNetCore package reference

**Branch:** squad/170-azure-monitor-otel-package
**Verdict:** ✅ APPROVED

### Findings

| Check | Status | Notes |
|-------|--------|-------|
| Package name | ✅ Pass | Azure.Monitor.OpenTelemetry.AspNetCore (correct, not legacy) |
| Package version | ✅ Pass | 1.4.0 |
| FR-306 compliance | ✅ Pass | Uses modern OpenTelemetry-based SDK |
| Build | ✅ Pass | 0 errors, 9 warnings (pre-existing) |

### Diff Summary

```diff
+    <PackageReference Include="Azure.Monitor.OpenTelemetry.AspNetCore" Version="1.4.0" />
```

---

## PR #177 — feat: add ApplicationInsights config section and binding model

**Branch:** squad/171-app-insights-config-section
**Verdict:** ✅ APPROVED

### Findings

| Check | Status | Notes |
|-------|--------|-------|
| Enabled default | ✅ Pass | false (zero overhead) |
| ConnectionString default | ✅ Pass | empty string |
| SamplingPercentage default | ✅ Pass | 100 |
| SectionName constant | ✅ Pass | "ApplicationInsights" |
| XML documentation | ✅ Pass | All public members documented |
| appsettings.json section | ✅ Pass | Present with Enabled: false |
| FR-300 compliance | ✅ Pass | |
| FR-301 compliance | ✅ Pass | |
| FR-302 compliance | ✅ Pass | |
| FR-318 compliance | ✅ Pass | |
| Build | ✅ Pass | 0 errors, 9 warnings (pre-existing) |

### Diff Summary — appsettings.json

```diff
+  "ApplicationInsights": {
+    "Enabled": false,
+    "ConnectionString": "",
+    "SamplingPercentage": 100
+  },
```

### Diff Summary — ApplicationInsightsOptions.cs

New file with correct structure:
- `SectionName` constant
- `Enabled` property (default: false)
- `ConnectionString` property (default: empty)
- `SamplingPercentage` property (default: 100)
- XML docs on class and all properties

---

## Recommendation

Both PRs are ready to merge. Wave 1 infrastructure for spec 008 is complete.
