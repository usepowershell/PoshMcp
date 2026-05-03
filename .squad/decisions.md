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

# Diagnosis: OAuthProxy Configuration Not Reachable in Deployed Image

**Date:** 2026-05-02  
**Investigator:** Amy (DevOps/Platform/Azure)  
**Issue:** `/.well-known/oauth-authorization-server` returns 404 → `Authentication__OAuthProxy__Enabled` is false or missing  
**Root Cause:** OAuthProxy configuration is not present in ANY appsettings.json file in the PoshMcp repository, and the deployment pipeline does not support it.

---

## Findings

### 1. Dockerfile Analysis
**File:** `./Dockerfile` (lines 1-88)

- **Build Stage:** Compiles PoshMcp.Server project with `dotnet publish`
- **Publish Location:** `/app/publish/server` → copied to container image as `/app/server`
- **Appsettings Handling:** The Dockerfile does NOT explicitly copy any appsettings.json file
- **Config Source:** The `dotnet publish` command includes `./PoshMcp.Server/appsettings.json` in the published output by default
- **No Overlay:** The Dockerfile does not layer additional appsettings files on top of the published application

**Implication:** The appsettings.json bundled in the container image is EXACTLY what exists in `./PoshMcp.Server/appsettings.json` at build time.

### 2. PoshMcp.Server appsettings.json Status
**File:** `./PoshMcp.Server/appsettings.json`

- **OAuthProxy Configuration:** ❌ NOT PRESENT
- **Current Authentication Section:**
  ```json
  "Authentication": {
    "Enabled": false,
    "DefaultScheme": "Bearer",
    "DefaultPolicy": {
      "RequireAuthentication": true,
      "RequiredScopes": [],
      "RequiredRoles": []
    },
    "Schemes": {}
  }
  ```
- **No `OAuthProxy` subsection:** The file contains no `OAuthProxy` configuration at all

### 3. Other Appsettings Files Checked
- `./PoshMcp.Server/appsettings.azure.json` — PowerShell config only, no auth
- `./PoshMcp.Server/appsettings.modules.json` — PowerShell module config only
- `./PoshMcp.Server/appsettings.environment-example.json` — Example template, no OAuthProxy
- `./PoshMcp.Server/default.appsettings.json` — Not present or empty
- `./examples/appsettings*.json` — All templates/examples only

**None of these files contain OAuthProxy configuration.**

### 4. Deployment Pipeline Analysis
**File:** `./infrastructure/azure/deploy.ps1` (lines 302–407)

The `ConvertTo-McpServerEnvVars()` function translates appsettings.json keys to Container App environment variables.

**Supported Translations:**
- `PowerShellConfiguration.*` → `PowerShellConfiguration__*`
- `Authentication.Enabled` → `Authentication__Enabled`
- `Logging.LogLevel.Default` → `Logging__LogLevel__Default`

**Missing:** ❌ No handling for `Authentication.OAuthProxy.*` or any nested auth properties  
**Missing:** ❌ No support for `ProtectedResource.*` or `IdentityProvider.*`

**Impact:** Even if OAuthProxy were added to an appsettings.json file, the deployment script would silently ignore it and NOT convert it to an env var.

---

## Root Cause Chain

1. **OAuthProxy is not defined in PoshMcp.Server/appsettings.json**
   - The base/default appsettings has only minimal Authentication config
   
2. **The Dockerfile does not overlay a custom appsettings file**
   - There is no `COPY` instruction for any alternate appsettings files
   - The bundled config is what's published by `dotnet publish`

3. **The ASP.NET Core configuration system loads appsettings.json from the working directory**
   - Container working directory: `/app` (line 33 of Dockerfile)
   - The published binary is in `/app/server/`
   - ASP.NET Core loads `./appsettings.json` relative to the executable, which is `/app/server/appsettings.json`
   - This matches what was published during build

4. **No environment variables are setting OAuthProxy values**
   - The Container App revisions have no `Authentication__OAuthProxy__*` env vars
   - Even if they did, the Bicep/deploy.ps1 doesn't have logic to inject them

5. **Result:** The server runs with `Authentication.Enabled = false`, no OAuthProxy section → returns 404 for `/.well-known/oauth-authorization-server`

---

## What Steven Meant (and What's Wrong)

**Steven's Statement:**  
> "The appsettings.json with OAuthProxy settings IS bundled into the container image — env vars shouldn't be needed."

**Reality:**  
- ❌ There is NO appsettings.json in the repo with OAuthProxy settings
- ❌ The Dockerfile does not perform any custom bundling of appsettings files
- ✅ In theory, IF an appsettings.json with OAuthProxy were in `./PoshMcp.Server/`, it WOULD be bundled by `dotnet publish`
- ❌ But that file does not exist yet

**Likely Scenario:**  
Either Fry or another team member:
1. Created a patched appsettings.json (e.g., in a separate branch or external to this repo)
2. Assumed it would be included in the image
3. Did NOT merge the patch into `./PoshMcp.Server/appsettings.json`
4. Did NOT rebuild/redeploy the image with the patched config

---

## Resolution Path

### **Option A: Add OAuthProxy to PoshMcp.Server/appsettings.json (Recommended)**

1. Update `./PoshMcp.Server/appsettings.json` with a complete `Authentication.OAuthProxy` section
2. Include `TenantId`, `ClientId`, `Audience`, `Scopes`, etc.
3. Rebuild the image: `docker build -t poshmcp:latest .`
4. Deploy the new image to Azure Container Apps
5. OAuthProxy config will be bundled in the image and used by ASP.NET Core's config system

**Pros:**  
- Config is immutable, baked into the image
- No env var surprises or shadowing
- Follows `.NET standard` (appsettings.json is the config source)

**Cons:**  
- Credentials/secrets should use Azure Key Vault, not hardcoded JSON
- Requires rebuild + redeploy for config changes

### **Option B: Update Deployment Pipeline to Support OAuthProxy Env Vars**

1. Extend `ConvertTo-McpServerEnvVars()` in `deploy.ps1` to handle `Authentication.OAuthProxy.*` keys
2. Ensure Bicep resource template accepts and injects these env vars
3. Deploy OAuthProxy config as Container App environment variables (with Key Vault secrets for sensitive values)

**Pros:**  
- Config can be updated without image rebuild
- Secrets managed via Key Vault
- Flexible for multi-environment deployments

**Cons:**  
- More complex deployment logic
- Requires changes to Bicep, deploy.ps1, and possible env var structure

### **Option C: Both (Recommended for Production)**

1. Add a minimal OAuthProxy template to `./PoshMcp.Server/appsettings.json` with placeholder values
2. Extend `deploy.ps1` and Bicep to support overriding OAuthProxy values via env vars
3. At deploy time, inject actual credentials as Container App env vars
4. This gives flexibility + security

---

## Next Steps

1. **Clarify with Fry/Steven:** Where is the patch to appsettings.json that adds OAuthProxy? Is it in a separate branch, external repo, or lost?

2. **For Bender (Development/Build):**  
   - If the OAuthProxy config exists externally, add it to `./PoshMcp.Server/appsettings.json`
   - Update `deploy.ps1` `ConvertTo-McpServerEnvVars()` function to translate OAuthProxy keys
   - Add integration test to verify OAuthProxy settings are passed as env vars

3. **For Amy (Deployment):**  
   - Once config is in place, rebuild image: `./docker.ps1 build -ImageTag latest`
   - Push image to registry
   - Deploy with `infrastructure/azure/deploy.ps1`
   - Verify `/.well-known/oauth-authorization-server` returns 200

---

## References

- **Dockerfile:** Publishes from `./PoshMcp.Server/` with `dotnet publish`
- **PoshMcp.Server/appsettings.json:** Currently has minimal Authentication config, no OAuthProxy
- **deploy.ps1 (ConvertTo-McpServerEnvVars):** Does not translate OAuthProxy keys to env vars
- **ASP.NET Core Config Loading:** Reads `appsettings.json` from app working directory

# Release v0.9.10

**By:** Amy (DevOps / Platform / Azure Engineer)  
**Date:** 2026-05-02  
**Status:** Applied  

## What

Completed release of PoshMcp v0.9.10 by:
1. Verifying fix commit (b81a55d: OAuth issuer in Entra metadata)
2. Confirming version bump to 0.9.10 in `PoshMcp.Server/PoshMcp.csproj`
3. Pushing main branch to origin (2 commits)
4. Creating and pushing annotated tag `v0.9.10`
5. Verifying CI triggered automatically

## Why

Release implements security/configuration fix for OAuth Entra issuer metadata propagation. Bender had prepared all necessary changes (fix commit, version bump, release notes); Amy executed the final push and tagging steps to trigger the container build pipeline.

## Technical Details

- **Fix commit:** b81a55d on main — sets Entra issuer in AS metadata for OAuth compliance
- **Release notes:** Updated `docs/release-notes/0.9.10.md` and `docs/toc.yml`
- **CI Triggered:** GitHub Actions workflow "Build and Publish Packages" (Run 25254551703)
  - Builds Dockerfile → pushes container to `ghcr.io/usepowershell/poshmcp:0.9.10`
  - Expected completion: ~5-10 minutes

## Next Steps (Steven)

1. Monitor workflow at https://github.com/usepowershell/PoshMcp/actions/runs/25254551703
2. Confirm container image published to GHCR with tag `0.9.10`
3. Coordinate AdvocacyBami update with new base image reference (do NOT modify AdvocacyBami files in this release)

## Context

This is a .NET/Docker project using tag-based release model (not npm). Release notes and version were already prepared by Bender. Amy's role was infrastructure/operations — pushing to origin and triggering CI.

## Rule Going Forward

Tag push automatically triggers GitHub Actions workflows. No additional manual steps needed — just monitor the container build completion and publish status.

# Decision: v0.9.11 Release — OAuth /authorize Proxy Endpoint

**Date:** 2026-05-02T10:11:52-05:00
**Agent:** Amy (DevOps / Platform Engineer)
**Status:** COMPLETED

## Context

Bender committed `feat(auth): add /authorize proxy redirect endpoint for VS Code OAuth` and bumped the version to 0.9.11 in PoshMcp.Server/PoshMcp.csproj.

The commit introduced a critical OAuth fix: VS Code MCP clients were constructing auth URLs as `{proxy_base}/authorize` and receiving 404 errors. The new endpoint acts as a proxy that:
- Accepts all OAuth2 PKCE parameters
- Issues a 302 redirect to Entra's authorize endpoint
- Replaces the ephemeral DCR client_id with the real Entra client_id from config

## Decision

Release v0.9.11 with the following artifacts:

### 1. Release Notes (`docs/release-notes/0.9.11.md`)

Created following the established pattern from v0.9.10:
- **Title:** PoshMcp v0.9.11 Release Notes
- **What's New:** OAuth /authorize proxy redirect endpoint feature
- **Bug Fixes:** OAuth flow now completes for VS Code MCP clients
- **Upgrade Notes:** Configuration guidance for Authentication.ClientId and TenantId

### 2. Table of Contents (`docs/toc.yml`)

Updated Release Notes section to include:
```yaml
- name: v0.9.11
  href: release-notes/0.9.11.md
```

Added at the top of the release notes list, maintaining reverse chronological order.

### 3. Git Workflow

- **Commit:** `docs: add v0.9.11 release notes` (7e67ac9)
  - Included Copilot co-author trailer as per project standards
  - Modified: docs/release-notes/0.9.11.md, docs/toc.yml
- **Push:** Pushed commit to origin main (b81a55d → 7e67ac9)
- **Tag:** Created lightweight tag `v0.9.11` on commit 7e67ac9
- **Push Tag:** Pushed tag to origin (new tag on remote)

## Rationale

### Release Timing

The OAuth /authorize endpoint is a critical bug fix for VS Code MCP client compatibility. Without this fix, VS Code clients cannot complete the OAuth flow, making the MCP server unusable with that client. This warrants an immediate patch release.

### Release Notes Format

Followed the established release notes pattern:
- Single-file release notes document per version
- Listed in toc.yml in reverse chronological order
- Clear sections: Features, Bug Fixes, Upgrade Notes
- Concise descriptions tied to user impact

### Version Numbering

No decision needed — Bender already bumped to 0.9.11 in the csproj. This is a patch release (0.9.10 → 0.9.11) appropriate for a bug fix + proxy endpoint.

## Implementation Checklist

- ✅ Version confirmed: 0.9.11 in PoshMcp.Server/PoshMcp.csproj
- ✅ Release notes created with feature and bug fix details
- ✅ Table of contents updated
- ✅ Commit created with proper trailer
- ✅ Changes pushed to origin main
- ✅ Git tag created and pushed
- ✅ Release is now discoverable by CI/CD (publish-packages.yml listens for v* tags)

## Artifacts Created

- `docs/release-notes/0.9.11.md` — Release notes document
- `docs/toc.yml` — Updated table of contents
- Git commit: `7e67ac9` — Release notes commit on main
- Git tag: `v0.9.11` — Release tag pointing to commit 7e67ac9

## Next Steps (Async — CI Handles Automatically)

The publish-packages.yml GitHub Actions workflow will automatically:
1. Detect the `v0.9.11` tag push
2. Build and test the release
3. Create a GitHub Release with the release notes
4. Publish poshmcp v0.9.11 to NuGet.org

No manual intervention required unless CI reports failures.

## Related Documents

- `.squad/agents/amy/history.md` — Updated with session entry
- `.copilot/skills/release-process/SKILL.md` — Release process guidelines (reviewed)
- `docs/release-notes/0.9.11.md` — This release's notes
- `docs/toc.yml` — Navigation updated

# Decision: Advertise explicit delegated scope in AS metadata

**Date:** 2026-05-02
**Author:** Bender (Backend Developer)
**Status:** Implemented

## Problem

After the `/authorize` redirect succeeded and the user authenticated, the token exchange returned a JWT that failed with `SecurityTokenInvalidIssuerException`.

**Root cause:** `OAuthProxyEndpoints.cs` advertised `api://{audience}/.default` in `scopes_supported` of the AS metadata document. When VS Code requested the `.default` scope, Entra issued a **v1.0 token** whose issuer claim is `https://sts.windows.net/{tenant}/`. The configured `ValidIssuers` expected the v2.0 issuer `https://login.microsoftonline.com/{tenant}/v2.0`, causing validation failure.

## Decision

Change `scopes_supported` to advertise an explicit delegated scope instead of `.default`.

**Resolution order:**
1. Look up `config.DefaultPolicy.RequiredScopes` for an entry that starts with the configured audience URI.
2. If found, use it — keeps AS metadata aligned with what token validators require.
3. Otherwise, fall back to `{audience}/user_impersonation`.

## Implementation

**File:** `PoshMcp.Server/Authentication/OAuthProxyEndpoints.cs`

```csharp
// Before
scopesSupported.Add($"{proxy.Audience.TrimEnd('/')}/.default");

// After
var audienceBase = proxy.Audience.TrimEnd('/');
var explicitScope = config.DefaultPolicy?.RequiredScopes
    .FirstOrDefault(s => s.StartsWith(audienceBase, StringComparison.OrdinalIgnoreCase));
scopesSupported.Add(explicitScope ?? $"{audienceBase}/user_impersonation");
```

**Commit:** `fix: advertise explicit user_impersonation scope in AS metadata to prevent v1.0 token issuance`

## Why `.default` is wrong here

The `.default` scope instructs Entra to grant all statically-declared permissions for the app. When the app registration is a v1.0 registration (or has no explicit v2.0 access token configuration), Entra issues a v1.0 token signed by `sts.windows.net`. Our middleware validates against `login.microsoftonline.com/.../v2.0` — these are different issuers. Explicit delegated scopes (e.g. `user_impersonation`) force v2.0 token issuance regardless of app registration version.

## Impact

- VS Code now requests `api://{audience}/user_impersonation` instead of `.default`.
- Entra issues v2.0 tokens; issuer validation passes.
- No changes required to `ValidIssuers` or token validation configuration.

# Decision: Fix Auth Challenge Not Firing for No-Token Requests

**Date:** 2026-05-02
**Author:** Bender (Backend Developer)
**Status:** Implemented

## Problem

When VS Code's MCP client connects with no pre-existing auth credentials, it was not being redirected to sign in — the connection hung at `initialize`. The container log showed `aspnetcore.authentication.result: none` (expected — no token was presented), but the OAuth browser redirect never happened.

## Root Cause

Two related defects in the auth challenge path:

### 1. `OnChallenge` condition too narrow (`AuthenticationServiceExtensions.cs`)

The `OnChallenge` handler that injects `WWW-Authenticate: Bearer resource_metadata="..."` was gated on:

```csharp
if (cfg.Value.ProtectedResource?.Resource is not null)
```

The `AuthenticationConfigurationValidator` does **not** require `ProtectedResource.Resource` to be set — it is optional. When `Resource` is null (a valid configuration), the condition is `false`. The handler fell through to the default JWT Bearer challenge, which emits only `WWW-Authenticate: Bearer` with no `resource_metadata` parameter.

VS Code's MCP client reads `resource_metadata` to discover the OAuth Authorization Server. Without it, no browser redirect is triggered and the connection hangs waiting for the MCP `initialize` response.

This affects the "no token" case (`authentication.result: none`) specifically because a valid token would have bypassed the challenge entirely.

### 2. RFC 9728 `resource` field could be `null` in PRM response (`ProtectedResourceMetadataEndpoint.cs`)

The Protected Resource Metadata endpoint only substituted non-HTTPS URIs with `serverBase`. If `Resource` was `null` or empty, the `resource` field in the PRM JSON was `null`. RFC 9728 requires `resource` to be an absolute HTTPS URI — a null value breaks the VS Code OAuth discovery chain even if the challenge had fired correctly.

## Fix

**`AuthenticationServiceExtensions.cs`:** Changed condition from `ProtectedResource?.Resource is not null` to `ProtectedResource is not null`. This aligns with `MapProtectedResourceMetadata`'s own gate (also `ProtectedResource is not null`), ensuring `resource_metadata` is always sent in the challenge whenever the PRM endpoint is available.

**`ProtectedResourceMetadataEndpoint.cs`:** Added a null/empty fallback so `resource` is always computed as `serverBase` when `Resource` is not configured, before applying the existing non-HTTPS substitution. This ensures the PRM `resource` field always satisfies RFC 9728.

## Expected Flow After Fix

1. VS Code sends `POST /` with no token
2. Server returns `401` + `WWW-Authenticate: Bearer resource_metadata="https://host/.well-known/oauth-protected-resource"` (now fires even when `Resource` is null)
3. VS Code fetches the PRM; `resource` is now always a valid HTTPS URI
4. VS Code reads `authorization_servers`, fetches AS metadata
5. VS Code opens browser → user signs in → token obtained → retry succeeds

# Decision: Use Entra v2.0 Authority URL for JWT Bearer authentication

**Date:** 2026-05-02
**Author:** Bender (Backend Developer)
**Status:** Accepted and implemented

## Context

AdvocacyBami was logging `SecurityTokenSignatureKeyNotFoundException` followed by 401 responses. The JWT Bearer middleware was configured with `Authority = https://login.microsoftonline.com/{tenant}` (no `/v2.0` suffix), which resolves to the Entra **v1.0** OIDC discovery document. The v1.0 JWKS (`/common/discovery/keys`) does not contain the signing keys used for tokens issued via the v2.0 token endpoint (`/oauth2/v2.0/token`), which is what VS Code uses.

## Decision

1. **Always append `/v2.0` to the Entra Authority URL** when the token flow uses the v2.0 endpoint. Specifically: `https://login.microsoftonline.com/{tenant}/v2.0`.

2. **PoshMcp server should warn at startup** when it detects the dangerous mismatch: Authority is a v1.0 Entra URL but `ValidIssuers` contains a v2.0 issuer. This is implemented via `Console.Error.WriteLine` in `AuthenticationServiceExtensions.cs`.

## Rationale

- Entra v1.0 and v2.0 endpoints use different JWKS URIs and issue tokens with different signing keys.
- A v1.0 Authority with a v2.0 token always fails signature validation silently — no obvious configuration error is reported until runtime failure.
- The startup warning gives operators an actionable message before the first request fails.

## Consequences

- **AdvocacyBami**: `appsettings.json` Authority updated. JWT validation now succeeds for v2.0 tokens.
- **PoshMcp**: Any deployment with this misconfiguration will log a clear warning on startup.
- **No breaking changes**: The v2.0 OIDC discovery doc is a superset of v1.0 for validation purposes.

## Affected Files

- `C:\Users\stmuraws\source\emu\gim-home\AdvocacyBami\appsettings.json`
- `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs`

# Decision: Add /authorize proxy redirect endpoint

**Date:** 2026-05-02
**Author:** Bender (Backend Developer)
**Status:** Implemented — v0.9.11

## Context

VS Code's MCP OAuth client does not use `authorization_endpoint` from the Authorization Server metadata document directly. Instead it constructs the auth URL as `{authorization_server_base}/authorize`, where `authorization_server_base` comes from `authorization_servers[0]` in the Protected Resource Metadata. Since PoshMcp is the authorization server base, VS Code was issuing `GET /authorize?...` → **404**.

Root cause diagnosed by Fry.

## Decision

Add a `GET /authorize` endpoint to `OAuthProxyEndpoints.cs` that acts as a redirect proxy to Entra's real authorize endpoint.

## Implementation

**File:** `PoshMcp.Server/Authentication/OAuthProxyEndpoints.cs`

The endpoint:
1. Accepts all incoming query parameters via `HttpContext.Request.Query`
2. Iterates params with `SelectMany` to handle multi-value params; replaces `client_id` (case-insensitive) with `proxy.ClientId` from config
3. Ensures `client_id` is always present even if the caller omits it
4. Builds the redirect URL: `https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize` + `QueryString.Create(params)`
5. Returns `Results.Redirect(url, permanent: false)` — HTTP 302
6. Logs at Debug level: tenant ID only (no code_challenge, state, or other sensitive values)

All other params (`scope`, `response_type`, `code_challenge`, `code_challenge_method`, `redirect_uri`, `state`) pass through unchanged. Scope transformation is deliberately omitted — Entra handles `api://.../.default` scopes natively.

## Alternatives Considered

- **Rewrite `authorization_endpoint` in AS metadata to point at Entra directly**: Would fix VS Code but break other clients that rely on the proxy for `client_id` substitution. Rejected.
- **Update Protected Resource Metadata to point at Entra as authorization server**: Would require clients to handle the real Entra authorize URL directly, losing the DCR proxy benefit. Rejected.

## Guard rails

- Endpoint is only registered when `proxy.Enabled == true` and `proxy.TenantId` is non-empty (same guards as existing proxy endpoints)
- Returns `501 Not Implemented` if `proxy.ClientId` is unconfigured
- Marked `.AllowAnonymous()` — auth challenge must not intercept the OAuth handshake itself

## Version

`PoshMcp.csproj` bumped from `0.9.10` → `0.9.11`

# Decision: Honor X-Forwarded-Proto in All Public-URL Construction

**Date:** 2026-05-02  
**Author:** Bender (Backend Developer)  
**Status:** Implemented

## Context

Fry's v0.9.8 functional check found that the `WWW-Authenticate: Bearer resource_metadata=` URL
returned by the server used `http://` instead of `https://` when deployed to Azure Container Apps.
Azure Container Apps (and similar reverse-proxy platforms) terminate TLS and forward requests
internally over HTTP, setting `X-Forwarded-Proto: https` to indicate the original public scheme.

`HttpContext.Request.Scheme` returns `http` in this configuration, producing incorrect URLs.

## Decision

**Any code that constructs a public-facing URL from the current request MUST read
`X-Forwarded-Proto` (and optionally `X-Forwarded-Host`) before falling back to the raw request
values.**

Canonical pattern:

```csharp
var scheme = req.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? req.Scheme;
var host   = req.Headers["X-Forwarded-Host"].FirstOrDefault() ?? req.Host.ToUriComponent();
var url    = $"{scheme}://{host}{path}";
```

## Rationale

- `OAuthProxyEndpoints.GetServerBaseUrl` and `ProtectedResourceMetadataEndpoint` already
  implemented this pattern correctly.
- `AuthenticationServiceExtensions.OnChallenge` was the only location that did not; this
  inconsistency caused the bug reported by Fry.
- Using the forwarded headers is the standard ASP.NET Core approach for hosted-behind-proxy
  scenarios (cf. `UseForwardedHeaders` middleware, `ForwardedHeadersOptions`).

## Scope of Change

- `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs` — fixed `OnChallenge` handler
- `PoshMcp.Server/PoshMcp.csproj` — version bumped to 0.9.9

## Affected Deployments

All deployments behind a reverse proxy that terminates TLS (Azure Container Apps, nginx, etc.).
Local/stdio deployments are unaffected (fallback to `req.Scheme` = `https` or `http` as
configured).

# Decision: OAuth Issuer and Scope Fix (v0.9.10)

**By:** Bender (Backend Developer)
**Date:** 2026-05-02
**Status:** Applied

## What

Fixed two bugs causing MCP `initialize` timeout when using Entra ID authentication:

1. **Issuer mismatch** — `OAuthProxyEndpoints.cs` returned the container's own URL as `issuer` in the `/.well-known/oauth-authorization-server` metadata. Changed to `https://login.microsoftonline.com/{tenantId}/v2.0`.

2. **Scope format mismatch** — `RequiredScopes` in `AdvocacyBami/appsettings.json` used the full URI form (`api://.../{clientId}/user_impersonation`). Entra v2.0 tokens carry `scp` as the short name only (`user_impersonation`). Changed to `["user_impersonation"]`.

## Why

RFC 8414 requires MCP clients to validate `token.iss == AS.issuer`. With the issuer set to the container URL, Entra tokens were always rejected, triggering infinite `initialize` retries. The scope mismatch caused every authenticated request to return 401.

## Rules Going Forward

- The `issuer` field in `/.well-known/oauth-authorization-server` MUST always be the Entra v2.0 issuer URL, not the server's base URL.
- `RequiredScopes` in configuration MUST use the short scope name (`user_impersonation`), not the full application URI form — Entra v2.0 tokens never include the full URI in the `scp` claim.
- When configuring `RequiredScopes`, test against a real token's decoded `scp` claim to confirm the exact format.

# Decision: Token Diagnostics and Configurable IdleTimeout

**By:** Bender (Backend Developer)
**Date:** 2026-05-02
**Status:** Applied

## What

1. **Token diagnostics**: Enhanced `/token` proxy in `OAuthProxyEndpoints.cs` to log HTTP status, Content-Type, and response body (on error) from Entra. On success, logs status+content-type only (no token body). Request field names are logged at Debug (no values to avoid leaking secrets).

2. **Configurable IdleTimeout**: Added `McpServerConfiguration` class and `McpServer.IdleSessionTimeoutSeconds` appsettings key. `HttpServerHost` reads this and passes it to `WithHttpTransport(opts => opts.IdleTimeout = ...)`.

## Why

- `/token` proxy failures were invisible — no logging on Entra errors made auth debugging very hard.
- VS Code's ~5s initialize timeout causes double auth redirect loops when server startup takes time. `IdleSessionTimeoutSeconds` lets operators tune the session idle timeout without code changes.

## Rule Going Forward

- Never log token values, auth codes, or client secrets — log field names and HTTP metadata only.
- `WithHttpTransport` in MCP SDK 1.2.0 accepts `Action<HttpServerTransportOptions>` — use this overload for transport configuration rather than `builder.Services.Configure<HttpServerTransportOptions>()` separately.

### 2026-05-02T06:39:00-05:00: User directive — progress reporting
**By:** Steven Murawski (via Copilot)
**What:** Report progress at each step of tasks: when starting something, if something significant occurs, and when ending. Applies to all agents and to the Coordinator's task narration.
**Why:** User request — captured for team memory. Improves visibility into multi-step work.

### 2026-05-02: User directive
**By:** stmuraws (via Copilot)
**What:** Never use `git pull`; always run `git fetch` and then `git rebase` from the fetched branch.
**Why:** User request — captured for team memory

# Architect Review: PR #184 — Program.cs Refactoring

**Reviewer:** Farnsworth (Lead Architect)
**Date:** 2026-05-02
**PR:** https://github.com/usepowershell/PoshMcp/pull/184
**Branch:** `squad/program-cs-refactor`

---

## Summary

PR reduces Program.cs from 2,290 → 733 lines by extracting 6 focused classes. The structural intent is correct and the individual classes are well-organized. However, the extraction approach has a critical flaw: **methods were copied into new classes but not removed from Program.cs**, creating active code duplication across 5+ files.

---

## ✅ What's Good

1. **Namespace consistency** — All 6 new classes use `namespace PoshMcp;`, matching the existing pattern from `SettingsResolver`, `ConfigurationLoader`, etc.

2. **Single entry point per class** — Each service has a clean primary method: `RunMcpServerAsync`, `RunHttpTransportServerAsync`, `RunDoctorAsync`, `SetupMcpToolsAsync`. Not a grab-bag of unrelated utilities.

3. **CliDefinition.Build() pattern** — Clean separation between CLI tree declaration and handler wiring. `SetHandler` lambdas in `Main()` are more readable with `CliDefinition` properties than inline `Option<T>` construction.

4. **Delegate injection in DoctorService** — Passing `McpToolSetupService.DiscoverToolsForCliAsync` as a `Func<>` to `DoctorService.RunDoctorAsync` avoids hard static coupling from Diagnostics layer to Server layer. Good layering instinct.

5. **Session memory discipline** — Spec was kept up to date and the worktree boundary was respected throughout.

---

## ⚠️ Concerns

1. **`CliDefinition` nullable static properties are null until `Build()` is called** — All 70+ options/commands are `Option<T>?` initialized to `null`. Callers must use `!` (null-forgiving operator) at every `SetHandler` call site. If `Build()` is ever called more than once (e.g., in tests), the mutable static state is silently replaced. Consider returning a value object from `Build()` rather than side-effecting static fields.

2. **`CliDefinition` and `CommandHandlers` are `public`** — `DoctorService`, `McpToolSetupService`, `StdioServerHost`, `HttpServerHost` are all `internal`. `CliDefinition` and `CommandHandlers` have no documented reason to be `public`. If tests need to call these, that should be via `InternalsVisibleTo`, not by widening their access to the entire assembly surface.

3. **`RegisterCleanupServices` duplication not addressed** — Noted as out of scope but worth tracking: `StdioServerHost` and `HttpServerHost` both have near-identical service registration logic. This should be extracted before the duplication compounds further.

---

## 🔴 Must Fix (blocking)

### 1. `DescribeConfigurationPath` duplicated across 5 files

This private utility method (`string DescribeConfigurationPath(string?)`) now exists independently in:
- `Program.cs`
- `DoctorService.cs`
- `CommandHandlers.cs`
- `StdioServerHost.cs`
- `HttpServerHost.cs`

Same story for `ToToolName`, `GetDiscoveredToolNames`, `GetExpectedToolNames` (exist in both `Program.cs` and `DoctorService.cs`).

**Fix:** Extract these to a shared utility class — `ConfigurationPathHelper` or inline into `ConfigurationLoader` — and delete the duplicates. This must happen before merge, or the codebase will have 5 independent copies of identical logic that will drift.

### 2. `Program.BuildDoctorReportFromConfig` / `Program.BuildDoctorJson` are not removed

The extraction created `DoctorService.BuildDoctorReportFromConfig` and `DoctorService.BuildDoctorJson` correctly. But the originals in `Program.cs` were **not removed**. Program.cs lines 251–440 are entirely duplicated in `DoctorService.cs`. Tests still call `Program.BuildDoctorReportFromConfig` — they should be updated to call `DoctorService.BuildDoctorReportFromConfig`, or Program.cs should forward to DoctorService.

This is not a 68% reduction — it is a 68% reduction in the **entry-point glue**, but the substantive logic is duplicated.

**Fix:** Either:
- (a) Remove the full implementations from `Program.cs`, update tests to call `DoctorService.BuildDoctorReportFromConfig` directly, OR
- (b) Make `Program.BuildDoctorReportFromConfig` a single-line delegation to `DoctorService.BuildDoctorReportFromConfig` (preserving test compatibility while eliminating the duplicate logic)

Option (b) is lower risk for this PR; option (a) is the correct long-term state.

---

## 💡 Recommendations (non-blocking)

1. **Add a shared `ConfigurationHelpers` static class** for `DescribeConfigurationPath`, `ToToolName`, `GetExpectedToolNames`, `GetDiscoveredToolNames`. These are used across CLI, Diagnostics, and Server layers — they need a neutral home.

2. **CliDefinition redesign consideration** — Instead of mutable static properties set during `Build()`, consider having `Build()` return a `CliSetup` record type containing the constructed `RootCommand` and all option/command references. This avoids the null-before-Build problem and makes the contract explicit.

3. **Test class naming** — Tests calling `Program.BuildDoctorReportFromConfig` directly are in `ProgramTests.cs`. Once the method moves to `DoctorService`, rename to `DoctorServiceTests.cs` for clarity.

4. **Follow-on PR should target ≤400 lines** — The ConfigurationManager extraction (~200 lines) plus cleaning up the remaining doctor helper duplicates will bring Program.cs to a reasonable boundary.

---

## Verdict: CHANGES REQUESTED

The structural direction is correct and the CliDefinition/CommandHandlers/ServerHost split is clean. The blocker is the unfinished extraction: **doctor helper methods still exist in Program.cs in full**, duplicating what's in DoctorService.cs. Fix the duplication (blocking item #2) and the utility method copies (blocking item #1) before merge. Both are addressable within 1–2 small commits.

# Root Cause: VS Code /authorize Redirect Bug

**Date:** 2026-05-02T10:11:52-05:00
**By:** Fry (Tester)
**Requested by:** Steven Murawski

## Summary

VS Code MCP client redirects the browser to:
```
https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/authorize?...
```
instead of `https://login.microsoftonline.com/.../oauth2/v2.0/authorize?...`

The container's `/authorize` returns **404 Not Found**, so the OAuth flow fails immediately.

## Evidence

### 1. AS metadata `authorization_endpoint` — CORRECT

```
GET /.well-known/oauth-authorization-server
```
```json
{
  "authorization_endpoint": "https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/oauth2/v2.0/authorize"
}
```

The AS metadata is correct. `authorization_endpoint` points directly to Entra, not the container.

### 2. Container `GET /authorize` — 404

```
GET /authorize?client_id=...&response_type=code&scope=openid&redirect_uri=...
→ 404 Not Found (no Location header)
```

No `/authorize` endpoint exists on the container.

### 3. Code review — no `/authorize` handler

`PoshMcp.Server/Authentication/OAuthProxyEndpoints.cs` registers only:
- `GET /.well-known/oauth-authorization-server`
- `POST /register`

**No `/authorize` route is registered anywhere in the codebase.**

## Root Cause

**VS Code's MCP OAuth client does not use `authorization_endpoint` from the AS metadata.**

Instead, VS Code constructs the authorization URL as:
```
{authorization_server_base_url}/authorize?<params>
```

The `authorization_server_base_url` comes from `authorization_servers[0]` in the Protected Resource Metadata (PRM):
```json
"authorization_servers": ["https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io"]
```

So VS Code builds `https://poshmcp.../authorize?...` and opens it in the browser → 404.

## Classification

**Root cause c:** The proxy `/authorize` handler is **missing entirely** from the server.

The AS metadata is correct. The bug is that VS Code doesn't read `authorization_endpoint` from the metadata — it derives `/authorize` from the authorization server base URL. Since PoshMcp is the declared authorization server (in the PRM), the container must host a working `/authorize` endpoint that proxies/redirects to Entra.

## Required Fix

Add a `GET /authorize` handler to `OAuthProxyEndpoints.cs` that:
1. Accepts all standard OAuth2 query parameters (`client_id`, `response_type`, `scope`, `redirect_uri`, `state`, `code_challenge`, `code_challenge_method`)
2. Issues a `302 Found` redirect to the real Entra `authorization_endpoint`:
   ```
   https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize?<all-params-forwarded>
   ```
3. The `client_id` in the forwarded request must be the configured Entra `ClientId` (not the DCR-issued ephemeral one), since Entra only knows about the registered app.

## Impact

All MCP clients (VS Code and others) that follow the "construct `/authorize` from authorization server base URL" pattern will fail to complete OAuth until this handler is added. The `/register` DCR flow works correctly — the failure is in step 5 of the OAuth flow (browser redirect to authorization endpoint).

# Diagnosis: MCP `initialize` Timeout — "Waiting for server to respond"

**Filed by:** Fry (Tester)
**Date:** 2026-05-02
**Deployment:** `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io`
**Symptom:** MCP client logs "Waiting for server to respond to initialize request..." every 5 seconds indefinitely after logging "Discovered authorization server metadata."

---

## Evidence Collected

### 1. Health Check — ✅ Healthy

```
GET /health → 200
{
  "status":"Healthy",
  "checks":[
    {"name":"powershell_runspace","status":"Healthy","description":"PowerShell runspace responsive"},
    {"name":"assembly_generation","status":"Healthy","description":"Assembly generation ready"},
    {"name":"configuration","status":"Healthy",
     "data":{"FunctionCount":3,"ModuleCount":1,"AuthEnabled":true,"AuthSchemes":"Bearer"}}
  ]
}
```

Server is fully up.

### 2. Unauthenticated POST to `/` (MCP initialize, no token)

```
POST / → 401 Unauthorized (response in <1ms)
WWW-Authenticate: Bearer resource_metadata="https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/.well-known/oauth-protected-resource"
```

- **HTTPS scheme is correct** — the http:// bug from v0.9.8 is fixed. ✅
- Server is reachable and responds instantly to unauthenticated requests.

### 3. GET `/sse`

```
GET /sse → 404
```

No legacy SSE transport endpoint. Server uses Streamable HTTP only (POST /). This is expected for MCP 2025-03-26+, but legacy clients trying SSE first may behave oddly.

### 4. OAuth AS Metadata — `/.well-known/oauth-authorization-server`

```json
{
  "issuer": "https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io",
  "authorization_endpoint": "https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/oauth2/v2.0/authorize",
  "token_endpoint": "https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/oauth2/v2.0/token",
  "registration_endpoint": "https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/register",
  "scopes_supported": [
    "openid","profile","email","offline_access",
    "api://80939099-d811-4488-8333-83eb0409ed53/.default"
  ],
  ...
}
```

**⚠️ CRITICAL: `issuer` is the PoshMcp URL, not the Entra ID URL.**
- The tokens issued by Entra ID have `iss = "https://login.microsoftonline.com/d91aa5af.../v2.0"`
- The AS metadata says `issuer = "https://poshmcp..."` — these do NOT match
- Some MCP clients/OAuth libraries validate that the `iss` claim in the received token matches the `issuer` in the AS metadata. This would cause the client to reject the token entirely and never send a Bearer-authenticated initialize.

**⚠️ CRITICAL: `scopes_supported` does NOT include the actually required scope.**
- AS metadata advertises: `api://80939099-d811-4488-8333-83eb0409ed53/.default`
- Server requires in `RequiredScopes`: `api://80939099-d811-4488-8333-83eb0409ed53/user_impersonation`
- PRM advertises: `api://80939099-d811-4488-8333-83eb0409ed53/user_impersonation`
- If the client uses AS metadata's `scopes_supported` to decide what scope to request, it will request `.default`, which may or may not include `user_impersonation` depending on app permissions.

### 5. Protected Resource Metadata — `/.well-known/oauth-protected-resource`

```json
{
  "resource": "api://80939099-d811-4488-8333-83eb0409ed53",
  "resource_name": "PoshMcp Server",
  "authorization_servers": ["https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io"],
  "scopes_supported": ["api://80939099-d811-4488-8333-83eb0409ed53/user_impersonation"],
  "bearer_methods_supported": ["header"]
}
```

PRM correctly advertises `user_impersonation` scope. ✅

### 6. JWT Validation Functional — ✅

```
POST / (fake Bearer token) → 401 in 457ms
```

OIDC discovery from container → `login.microsoftonline.com` works. JWT validation is not hanging. This rules out the network-timeout hypothesis.

### 7. Server Auth Logs — NO BEARER TOKENS EVER PRESENTED

From container metrics dump (72 auth attempts):
```
aspnetcore.authentication.result: none   (scheme: Bearer, count: 72)
```

`result: none` means the Bearer middleware ran but found **no token** in any of those 72 requests. There are zero `result: success` or `result: failure` entries. **The MCP client is never sending a Bearer token to the server.** This confirms the OAuth flow is failing client-side before the token is presented to the server.

### 8. Scope Claim Format Mismatch (Code Analysis)

`appsettings.json`:
```json
"RequiredScopes": ["api://80939099-d811-4488-8333-83eb0409ed53/user_impersonation"]
```

`AuthenticationServiceExtensions.cs`:
```csharp
policy.RequireClaim("scp", authConfig.DefaultPolicy.RequiredScopes.ToArray());
```

This check uses **exact match**. But Entra ID v2.0 tokens store the scope as the short name in the `scp` claim:
- **Entra token `scp` claim**: `"user_impersonation"` (just the suffix, not the full URI)
- **Server expects**: `"api://80939099-d811-4488-8333-83eb0409ed53/user_impersonation"` (full URI)

Even if the client successfully obtains a valid Entra token with `user_impersonation` consented, the scope check will **always fail** with 401 because the full URI format does not match what Entra puts in the token.

Additionally, if the token has multiple scopes (`scp = "user_impersonation offline_access"`), ASP.NET Core `RequireClaim` does an exact-value match against the full space-separated string — this would also fail even with the correct format.

---

## Root Cause Analysis

There are **two compound bugs** that together prevent the initialize from ever succeeding:

### Bug 1 (Primary — prevents token from being sent): `issuer` mismatch in AS metadata

**Location:** `OAuthProxyEndpoints.cs` line 64: `var issuer = baseUrl;`

The AS metadata `issuer` is set to the PoshMcp server URL. Entra tokens have `iss = login.microsoftonline.com/{tenantId}/v2.0`. MCP client SDKs that validate `iss == issuer` (per RFC 8414 §2) will reject the token and never send an authenticated initialize request.

This explains the log sequence:
1. Client sends initialize → 401 → discovers AS metadata ✅ ("Discovered authorization server metadata")
2. Client completes OAuth flow and gets Entra token
3. **Client SDK validates: `token.iss` (`login.microsoftonline.com`) ≠ `AS.issuer` (`poshmcp`) → token rejected**
4. Client has no valid token; retries initialize without token → 401 → cycle repeats
5. Log: "Waiting for server to respond to initialize request..." every 5s forever

**The AS metadata `issuer` should be the Entra ID issuer, or the client needs to be informed differently.**

Per RFC 8414, the `issuer` in AS metadata must be the authorization server's own identifier. Since PoshMcp is a **resource server with an OAuth proxy** (not a true AS), the `issuer` should ideally be the Entra ID issuer. However, the PRM's `authorization_servers` points to `https://poshmcp...`, so the client fetches the AS metadata from PoshMcp — creating a proxy relationship where `issuer` must logically be the Entra issuer for token validation to work.

### Bug 2 (Secondary — ensures 401 even if token is presented): Scope format mismatch

**Location:** `appsettings.json` `RequiredScopes` configuration

`RequiredScopes` uses the full scope URI `api://80939099.../user_impersonation`. Entra v2.0 tokens have `scp = "user_impersonation"` (short name only). Even if Bug 1 is fixed and the client sends the Entra token, the scope check will still fail with 401.

**Fix options:**
- Change `RequiredScopes` to `["user_impersonation"]` (short name), OR
- Add custom scope claim parsing that extracts the scope short name from the full URI, OR
- Use Microsoft.Identity.Web's `ScopeAuthorizationRequirement` which handles Entra scope format

---

## What IS Working

| Check | Status |
|-------|--------|
| Server health | ✅ Healthy |
| WWW-Authenticate scheme (https://) | ✅ Fixed vs v0.9.8 |
| JWT OIDC discovery reachability | ✅ Working (457ms) |
| PRM scopes_supported format | ✅ Has correct `user_impersonation` |
| AS metadata auth/token endpoints | ✅ Correct Entra endpoints |

---

## Recommended Fixes (for the team to implement)

### Fix 1 — AS metadata `issuer` (High Priority)

In `OAuthProxyEndpoints.cs`, change `issuer` from the PoshMcp base URL to the Entra ID issuer:

```csharp
// Before:
var issuer = baseUrl;

// After:
var entraBase = string.Format(EntraV2BaseTemplate, proxy.TenantId);
var issuer = $"{entraBase}";  // e.g., "https://login.microsoftonline.com/{tenantId}/oauth2/v2.0"
// Or more precisely:
var issuer = $"https://login.microsoftonline.com/{proxy.TenantId}/v2.0";
```

This makes the `issuer` in AS metadata match the `iss` claim in Entra-issued tokens.

### Fix 2 — Scope format in RequiredScopes (High Priority)

Change `appsettings.json` (and documentation) so `RequiredScopes` uses the short scope name:

```json
"DefaultPolicy": {
  "RequireAuthentication": true,
  "RequiredScopes": ["user_impersonation"]
}
```

Or alternatively, add scope claim splitting logic so `RequireClaim("scp", "user_impersonation")` works when `scp = "user_impersonation offline_access"`.

### Fix 3 — Add `user_impersonation` to AS metadata `scopes_supported` (Medium Priority)

The AS metadata `scopes_supported` should advertise the scopes the client needs to request. Currently it only has `.default`. Add the delegated scope explicitly, or populate from `ProtectedResource.ScopesSupported`.

---

## Files to Investigate

- `PoshMcp.Server/Authentication/OAuthProxyEndpoints.cs` — issuer generation (line 64)
- `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs` — RequireClaim scope check
- `appsettings.json` — RequiredScopes value format

# Decision: OAuth Redirect Validation — Live Endpoint Diagnosis

**Date:** 2026-05-02
**Author:** Fry (Tester)
**Reviewers:** Amy (deploy/env vars), Bender (code), Farnsworth (oversight)
**Status:** OPEN — awaiting fix assignment

---

## Context

v0.9.5 shipped OAuth AS proxy + DCR proxy (`OAuthProxyEndpoints.cs`) to enable VS Code MCP clients to authenticate without manual client_id entry. Steven reports that connecting to the live Container App still does NOT redirect to `login.microsoftonline.com`.

Live endpoint: `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io`

---

## Findings Summary

### What IS working
- `/health` → 200, all checks healthy, `AuthEnabled: true`
- `/.well-known/oauth-protected-resource` → 200 (returns data)
- Auth enforcement → 401 with `WWW-Authenticate: Bearer resource_metadata=...`
- Image deployed today (`psbamiacr.azurecr.io/advocacybami:20260502-061835`, revision `poshmcp--0000019`, active)

### What is BROKEN

**Primary failure:** `/.well-known/oauth-authorization-server` → **404**

The OAuth proxy endpoint is not registered because `OAuthProxy.Enabled = false` in the running container. The code in `OAuthProxyEndpoints.MapOAuthProxyEndpoints` returns early when `proxy.Enabled == false`.

**Root cause:** None of the 4 required env vars are set on the Container App:
```
❌ Authentication__OAuthProxy__Enabled    (not set)
❌ Authentication__OAuthProxy__TenantId   (not set)
❌ Authentication__OAuthProxy__ClientId   (not set)
❌ Authentication__OAuthProxy__Audience   (not set)
```

Confirmed via: `az containerapp revision show -n poshmcp -g rg-poshmcp --revision poshmcp--0000019`

**Secondary failure:** PRM (`/.well-known/oauth-protected-resource`) advertises Entra directly

Because `OAuthProxy.Enabled = false`, the PRM does NOT inject the proxy URL as the authorization server. Instead, it returns a hardcoded Entra URL from `ProtectedResource.AuthorizationServers`. VS Code then tries `https://login.microsoftonline.com/{tenant}/.well-known/oauth-authorization-server` → **404** (Entra serves OIDC metadata, not RFC 8414 AS metadata). No `registration_endpoint` is available → VS Code cannot do DCR → no `client_id` → no OAuth redirect → **login.microsoftonline.com never triggered**.

**Tertiary defect (Bender):** `WWW-Authenticate` header uses `http://` instead of `https://`

`AuthenticationServiceExtensions.cs:60` builds `metadataUrl` from `req.Scheme` without honoring `X-Forwarded-Proto`. Azure Container Apps terminates TLS at the ingress, so the app sees `http`. The correct pattern (already used in `OAuthProxyEndpoints.cs::GetServerBaseUrl`) is:
```csharp
var scheme = req.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? req.Scheme;
var host = req.Headers["X-Forwarded-Host"].FirstOrDefault() ?? req.Host.ToUriComponent();
var metadataUrl = $"{scheme}://{host}/.well-known/oauth-protected-resource";
```

**Quaternary defect (investigate):** PRM arrays are duplicated

`authorization_servers`, `scopes_supported` each appear twice; `bearer_methods_supported` appears 3×. Likely caused by non-empty `ProtectedResource.AuthorizationServers` in the baked-in appsettings PLUS another config source (appsettings.Production.json or old env vars). Needs investigation to confirm source; clearing extra config sources should fix.

---

## VS Code Client Flow (Simulated)

```
GET /.well-known/oauth-protected-resource
  → authorization_servers[0] = https://login.microsoftonline.com/{tenant}

GET https://login.microsoftonline.com/{tenant}/.well-known/oauth-authorization-server
  → 404 (Entra does not serve RFC 8414 AS metadata here)

GET https://login.microsoftonline.com/{tenant}/.well-known/openid-configuration
  → 200, registration_endpoint = null (Entra doesn't support DCR)

⛔ No registration_endpoint → no DCR → no client_id → no OAuth flow → no redirect
```

---

## Recommended Actions

### 🔴 Amy — IMMEDIATE (no redeploy needed)

Set the 4 missing env vars on the Container App:

```bash
az containerapp update -n poshmcp -g rg-poshmcp \
  --set-env-vars \
    "Authentication__OAuthProxy__Enabled=true" \
    "Authentication__OAuthProxy__TenantId=d91aa5af-8c1e-442c-b77c-0b92988b387b" \
    "Authentication__OAuthProxy__ClientId=80939099-d811-4488-8333-83eb0409ed53" \
    "Authentication__OAuthProxy__Audience=api://80939099-d811-4488-8333-83eb0409ed53"
```

Also investigate/remove any `Authentication__ProtectedResource__AuthorizationServers__*` env vars that may be contributing to array duplication.

**Expected result after fix:**
- `/.well-known/oauth-authorization-server` → 200 (proxy metadata)
- `/.well-known/oauth-protected-resource` `authorization_servers[0]` → `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io` (the proxy)
- VS Code fetches AS metadata from the proxy → gets `authorization_endpoint`, `token_endpoint`, `registration_endpoint`
- VS Code POSTs `/register` → gets `client_id = 80939099-d811-4488-8333-83eb0409ed53`
- VS Code redirects to `https://login.microsoftonline.com/{tenant}/oauth2/v2.0/authorize` → login prompt appears ✅

**Deploy process fix:** Update the deployment process to invoke `deploy.ps1 -ServerAppSettingsFile ./appsettings.json` rather than a bare `az containerapp update --image ...`. The deploy.ps1 `ConvertTo-McpServerEnvVars` function correctly translates the appsettings into Container App env vars.

### 🟡 Bender — CODE FIX (low urgency, no user-visible impact until proxy works)

Fix `AuthenticationServiceExtensions.cs:60` `OnChallenge` handler to use `X-Forwarded-Proto`:

```csharp
// Before:
var metadataUrl = $"{req.Scheme}://{req.Host}/.well-known/oauth-protected-resource";

// After:
var scheme = req.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? req.Scheme;
var host = req.Headers["X-Forwarded-Host"].FirstOrDefault()
           ?? req.Host.ToUriComponent();
var metadataUrl = $"{scheme}://{host}/.well-known/oauth-protected-resource";
```

### 🟡 Bender — INVESTIGATE array duplication

Determine why `authorization_servers`, `scopes_supported`, and `bearer_methods_supported` appear 2–3× in the PRM response. Check for stale env vars or `appsettings.Production.json` in the image. Fix to ensure exactly one copy of each value.

---

## Decision

**Root cause is configuration, not code.** v0.9.5 code is deployed and correct. The fix is Amy setting 4 env vars on the Container App — no rebuild or code change required for the primary issue.

Bender should address the secondary `http://` and array-duplication bugs in a follow-up commit.

# v0.9.10 OAuth Fix Validation — AdvocacyBami Deployment

**Date:** 2026-05-02T10:02:31-05:00
**Tester:** Fry
**Deployment:** `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io`
**Release:** v0.9.10

## Summary

```
✅ Check 1: Health — 200 Healthy
✅ Check 2: issuer field — https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/v2.0 (expected: https://login.microsoftonline.com/d91aa5af-.../v2.0)
✅ Check 3: PRM — 200 OK, authorization_servers uses https://
✅ Check 4: WWW-Authenticate scheme — https://
✅ Check 5: DCR /register — client_id: 80939099-d811-4488-8333-83eb0409ed53

Overall: PASS
Root cause bugs fixed: Yes (Bug 1 confirmed; Bug 2 deployed, not directly observable without real Entra token)
```

---

## Check 1: Health

**Request:** `GET /health`
**HTTP Status:** 200
**Body:**
```json
{
  "status": "Healthy",
  "checks": [
    {"name": "powershell_runspace", "status": "Healthy"},
    {"name": "assembly_generation", "status": "Healthy"},
    {"name": "configuration", "status": "Healthy",
     "data": {"FunctionCount": 3, "ModuleCount": 1, "AuthEnabled": true, "AuthSchemes": "Bearer"}}
  ]
}
```
**Result:** ✅ PASS — Server healthy, auth enabled, 3 functions registered.

---

## Check 2: OAuth AS Metadata — issuer field (PRIMARY FIX VALIDATION)

**Request:** `GET /.well-known/oauth-authorization-server`
**HTTP Status:** 200

**Key fields:**
| Field | Value |
|-------|-------|
| `issuer` | `https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/v2.0` |
| `authorization_endpoint` | `https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/oauth2/v2.0/authorize` |
| `token_endpoint` | `https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/oauth2/v2.0/token` |
| `registration_endpoint` | `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/register` |

**Result:** ✅ PASS — `issuer` is now `https://login.microsoftonline.com/{tenantId}/v2.0` (Bug 1 fix confirmed). Previously the issuer was the server's own URL, which caused MCP client SDK to reject tokens (iss ≠ issuer). All endpoints point to `login.microsoftonline.com`. `registration_endpoint` is present.

---

## Check 3: Protected Resource Metadata

**Request:** `GET /.well-known/oauth-protected-resource`
**HTTP Status:** 200
**Body:**
```json
{
  "resource": "api://80939099-d811-4488-8333-83eb0409ed53",
  "resource_name": "PoshMcp Server",
  "authorization_servers": ["https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io"],
  "scopes_supported": ["api://80939099-d811-4488-8333-83eb0409ed53/user_impersonation", "api://80939099-d811-4488-8333-83eb0409ed53/user_impersonation"],
  "bearer_methods_supported": ["header", "header"]
}
```

**Result:** ✅ PASS — `authorization_servers` uses `https://` (not `http://`). The `http://` scheme bug (v0.9.8) is still fixed.

**⚠️ Minor observation:** `scopes_supported` and `bearer_methods_supported` both contain duplicate entries. Not a blocking issue but worth noting as a future cleanup item.

---

## Check 4: WWW-Authenticate Header

**Request:** `GET /` (unauthenticated)
**HTTP Status:** 401
**WWW-Authenticate header:**
```
Bearer resource_metadata="https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/.well-known/oauth-protected-resource"
```

**Result:** ✅ PASS — Returns 401 (not redirect), scheme is `https://` (not `http://`). MCP clients following this URL will get the PRM over HTTPS.

---

## Check 5: DCR /register Endpoint

**Request:** `POST /register` with `Content-Type: application/json` body `{}`
**HTTP Status:** 201
**Body:**
```json
{
  "client_id": "80939099-d811-4488-8333-83eb0409ed53",
  "client_id_issued_at": 1777734205,
  "token_endpoint_auth_method": "none"
}
```

**Result:** ✅ PASS — Returns 201 with correct Entra `client_id` `80939099-d811-4488-8333-83eb0409ed53`.

---

## Bug Fix Validation Assessment

### Bug 1: issuer mismatch (OAuthProxyEndpoints.cs)
**Status: ✅ CONFIRMED FIXED**
The `issuer` field now returns `https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/v2.0` exactly as required. MCP client SDKs that validate `iss == issuer` will now accept Entra tokens and proceed to send Bearer tokens in subsequent requests.

### Bug 2: scope format (AdvocacyBami/appsettings.json)
**Status: ✅ DEPLOYED (indirect confirmation)**
The `RequiredScopes` change from `["api://80939099.../user_impersonation"]` to `["user_impersonation"]` cannot be directly validated without a real Entra Bearer token. The deployment is live and Bug 1 is fixed, so the full end-to-end flow (token acquisition + scope check) can now be tested with a real MCP client. The health check confirms `AuthEnabled: true` with correct configuration.

---

## Regression Check

- HTTP → HTTPS scheme fix (v0.9.8): ✅ Still holding (Checks 3 and 4)
- DCR proxy: ✅ Still working (Check 5)
- Server health: ✅ Healthy with auth enabled (Check 1)

No regressions observed.

# Fry — v0.9.8 Deployment Verification Findings
**Date:** 2026-05-02
**Deployment:** https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io
**Image:** PoshMcp v0.9.8 (AdvocacyBami rebuild)

## Summary

| Check | Result | Notes |
|-------|--------|-------|
| 1. Health | ✅ PASS | All 3 sub-checks Healthy |
| 2. OAuth AS Metadata | ✅ PASS | Both endpoints → login.microsoftonline.com |
| 3. Protected Resource Metadata | ⚠️ PARTIAL | `resource` is `api://` URI, not container URL; rest is correct |
| 4. Dynamic Client Registration | ✅ PASS | 201 with correct client_id |
| 5. MCP Endpoint Reachability | ⚠️ ISSUE | `resource_metadata` URL uses `http://` instead of `https://` |

## Detailed Findings

### CHECK 1: Health — ✅ PASS
- **Status:** 200 OK
- **All checks healthy:** `powershell_runspace`, `assembly_generation`, `configuration`
- Configuration: 3 functions, 1 module, Auth enabled (Bearer)

### CHECK 2: OAuth Authorization Server Metadata (RFC 8414) — ✅ PASS
- **Status:** 200 OK
- **issuer:** `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io` ✅
- **authorization_endpoint:** `https://login.microsoftonline.com/d91aa5af-.../oauth2/v2.0/authorize` ✅ (NOT the container URL)
- **token_endpoint:** `https://login.microsoftonline.com/d91aa5af-.../oauth2/v2.0/token` ✅
- **registration_endpoint:** `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/register` ✅
- Scopes, grant types, PKCE all populated correctly.

### CHECK 3: Protected Resource Metadata (RFC 9728) — ⚠️ PARTIAL PASS
- **Status:** 200 OK
- **authorization_servers:** 1 entry (no duplicates) ✅
- **bearer_methods_supported:** `["header"]` (exactly 1, no duplicates) ✅
- **scopes_supported:** 1 entry, no duplicates ✅
- **⚠️ ISSUE — `resource` field:**
  - **Actual:** `"api://80939099-d811-4488-8333-83eb0409ed53"` (Entra app ID URI)
  - **Expected per task spec:** the container URL (`https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io`)
  - RFC 9728 allows either form; the app ID URI is valid for Entra-protected resources. Not a hard failure, but worth noting if MCP clients resolve `resource` to discover the server URL.

### CHECK 4: Dynamic Client Registration — ✅ PASS
- **Status:** 201 Created ✅ (task accepts 200 or 201)
- **client_id:** `80939099-d811-4488-8333-83eb0409ed53` ✅ (configured Entra app client ID)
- Response also includes `client_id_issued_at` and `token_endpoint_auth_method: none`.

### CHECK 5: MCP Endpoint Reachability — ⚠️ ISSUE
- **Status:** 401 Unauthorized ✅ (NOT a redirect to /authorize — the core OAuth fix is working)
- **WWW-Authenticate header present:** ✅
- **⚠️ ISSUE — `http://` in resource_metadata:**
  - **Actual:** `WWW-Authenticate: Bearer resource_metadata="http://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/.well-known/oauth-protected-resource"`
  - **Expected:** `https://` (the container serves HTTPS; `http://` reference in the header will cause MCP clients to attempt an insecure fetch, which will either fail or be redirected)
  - This is a configuration/code bug — the server is generating the `resource_metadata` URL with the wrong scheme.

## Recommended Actions

1. **Check 5 (`http://` in resource_metadata)** — **HIGH PRIORITY:** The `WWW-Authenticate: Bearer resource_metadata` URL must use `https://`. MCP clients (e.g., Claude Desktop, VS Code extension) follow this URL to discover OAuth metadata; an `http://` reference may fail TLS validation or get rejected. Investigate how the resource metadata URL is constructed — likely the app is reading `HttpContext.Request.Scheme` or a configured base URL that is resolving as `http` behind the Azure Container Apps reverse proxy. Fix: ensure `X-Forwarded-Proto` is honored, or hardcode the scheme from configuration.

2. **Check 3 (`resource` URI)** — **LOW PRIORITY / INFORMATIONAL:** `resource` = `api://80939099-...` is valid per RFC 9728 for Entra-protected APIs. No action required unless client tooling specifically expects the container HTTPS URL here.

# Decision: `MapInboundClaims = false` is Required; No `scope` in VS Code mcp.json

**By:** Leela (Developer Advocate)
**Date:** 2026-05-03
**Status:** Proposed

## Summary

Two requirements are now documented in `docs/entra-id-oauth-implementation-guide.md` as a result of live debugging sessions:

### 1. `MapInboundClaims = false` is a documented requirement

ASP.NET Core's JWT Bearer middleware remaps short JWT claim names (`scp`, `roles`) to long WS-Federation URI forms by default. This causes authorization policies that check for `scp` or `roles` by short name to silently fail — the token is valid, the claim is present, but it is stored under the wrong key in `ClaimsPrincipal`.

**Rule:** `options.MapInboundClaims = false` must always be set when configuring JWT Bearer authentication in PoshMcp. `TokenValidationParameters.RoleClaimType` must be set explicitly to the configured role claim short name so that `IsInRole()` continues to work.

This is implemented in `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs` and is now validated in the deployment checklist.

### 2. VS Code `mcp.json` must not include a `scope` field

An explicit `scope` field in VS Code's `mcp.json` causes VS Code's MCP auth provider to silently fail token acquisition — no `Authorization` header is sent, every request hits `DenyAnonymousAuthorizationRequirement`, and no useful error is surfaced to the user.

**Rule:** Do not set `scope` in VS Code's `mcp.json` for PoshMcp connections. Let VS Code read `scopes_supported` from the server's Protected Resource Metadata at `/.well-known/oauth-protected-resource` and handle scope selection automatically.

## Documentation

Both findings are documented in `docs/entra-id-oauth-implementation-guide.md`:
- Bug 5: `MapInboundClaims = false` — in the "Bugs We Hit and Why" section
- VS Code client gotcha — in the new "VS Code MCP Client Configuration Gotchas" section
- Validation Checklist updated with `MapInboundClaims` check
- Summary updated with lessons 5 and 6
