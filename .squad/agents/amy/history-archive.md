- Ran 5 sequential iterations of `dotnet clean` + `dotnet test --configuration Release` to diagnose reported flaky tests.
- **Result: STABLE** — All 5 iterations passed with consistent test counts: 387 passed, 1 skipped, 0 failed (total 388).
- Iteration times: 338.9s, 291.9s, 379.9s, 520.9s, 340s (variable duration due to system load, no correlation with failures).
- No failing tests identified across any iteration. One test (`PoshMcp.Tests.Functional.ReturnType.GeneratedMethod.ShouldHandleGetChildItemCorrectly`) consistently skipped.
- Verdict: No evidence of intermittent failures in test suite.

### 2026-04-14: v0.5.4 tool update (local nupkg install)

- Verified latest nupkg in `./nupkg/`: `poshmcp.0.5.4.nupkg`
- Current global tool version: 0.5.3
- PackageId and ToolCommandName both: `poshmcp` (confirmed in .csproj)
- Update command: `dotnet tool update -g poshmcp --add-source ./nupkg --version 0.5.4`
- Verified: `dotnet tool list -g | Select-String poshmcp` → `poshmcp         0.5.4        poshmcp`
- Local .nupkg directory is specified with `--add-source ./nupkg` (relative path from working directory)

### 2026-04-14: v0.5.6 patch release and GitHub Packages publish

- Version source remains `PoshMcp.Server/PoshMcp.csproj` `<Version>`; bumped `0.5.5` → `0.5.6` as patch increment.
- Packaging command: `dotnet pack .\PoshMcp.Server\PoshMcp.csproj -c Release -o .\nupkg`
- Produced artifact: `nupkg/poshmcp.0.5.6.nupkg` (25,843,399 bytes).
- GitHub Packages publish command used existing source alias and gh token: `dotnet nuget push .\nupkg\poshmcp.0.5.6.nupkg --api-key (gh auth token) --source github-poshmcp --skip-duplicate`.
- Publish succeeded to `https://nuget.pkg.github.com/usepowershell`.
- Local update command remains: `dotnet tool update -g poshmcp --version 0.5.6 --add-source .\nupkg --ignore-failed-sources`.
- Verified installs: `dotnet tool list -g` shows `poshmcp 0.5.6`; `poshmcp --version` reports `0.5.6+31fa6372ec4b71d7dd68261ba45266c6c8b93817`.

### 2026-07-18: Issue #131 — OTel stdio suppression and appsettings schema

- Added `isStdioMode = false` parameter to `ConfigureOpenTelemetry(HostApplicationBuilder, bool)` in `Program.cs`.
- Guarded `metricsBuilder.AddConsoleExporter()` behind `if (!isStdioMode)` so no OTel console output occurs in stdio transport mode.
- Updated `ConfigureServerServices` call site (stdio-only path) to pass `isStdioMode: true` to `ConfigureOpenTelemetry`.
- `ConfigureOpenTelemetryForHttp` (HTTP path) is a separate method and remains unchanged — HTTP console exporter unaffected.
- `appsettings.json` already had `Logging.File.Path` added by Bender; added the same `Logging.File.Path: ""` schema key to `appsettings.environment-example.json`, `appsettings.azure.json`, and `appsettings.modules.json`.
- Build: `dotnet build PoshMcp.Server/PoshMcp.csproj` → succeeded, 5 pre-existing warnings, 0 errors.
- Committed and pushed to branch `squad/131-stdio-logging-to-file` (commit `8a10311`).



### 2026-07-18: Issue #133 — docker buildx build missing PATH argument

- **Root cause:** `PoshMcp.Server/Program.cs` line ~692, the `build` CLI command handler constructed `buildArgs` as `"build -f {imageFile} -t {imageTag}"` — missing the required build context PATH argument.
- On modern Docker (buildx-as-default), `docker build` delegates to `docker buildx build` which requires a positional PATH/URL/`-` argument. Without it, Docker fails with `'docker buildx build' requires 1 argument`.
- **Fix:** Changed to `$"build -f {imageFile} -t {imageTag} ."` — appending `.` (current directory) as the build context.
- The CI workflow (`publish-packages.yml`) calls `dotnet run -- build --tag "$IMAGE"` which runs the CLI build handler; the Dockerfile is expected to exist in the working directory (repo root), consistent with using `.` as context.
- **Key files:** `PoshMcp.Server/Program.cs` (handler for `buildCommand`), `.github/workflows/publish-packages.yml` (CI step that triggered the failure).
- Branch: `squad/133-fix-docker-buildx-path`, commit `fadbd4d`, PR #134.
- Build verified: `dotnet build PoshMcp.Server/PoshMcp.csproj -c Release` → 0 errors after fix.

### 2026-07-18: PR #138 follow-up — remove orphaned COPY PoshMcp.sln line

- Farnsworth's nit on PR #138: `COPY PoshMcp.sln ./` in the build stage was dead weight after switching restore/build to target `PoshMcp.Server/PoshMcp.csproj`.
- Removed the line and updated the adjacent comment from "Copy solution and project files first" to "Copy project files first".
- Committed as `fix(#136): remove orphaned COPY PoshMcp.sln line from Dockerfile` with Copilot co-author trailer.
- Pushed to `squad/136-fix-container-image-build`; replied to PR with confirmation comment.
- Key lesson: when switching from solution-level to project-level restore/build in a Dockerfile, audit all COPY lines in the build stage — any files that no longer appear in RUN commands become orphaned layers that add noise without value.

## Learnings

### docker.ps1 -GenerateDockerfile switch

- Added `-GenerateDockerfile` [switch] and `-OutputPath` [string] parameters to `docker.ps1`.
- Works with `build`/`build-base` (reads `./Dockerfile`) and `build-custom` (reads `examples/Dockerfile.$Template`).
- `-OutputPath` has no default in `param()` — computed dynamically: `./Dockerfile.generated` for base, `./Dockerfile.<Template>.generated` for custom. This follows the precomputed-optional-parameter skill pattern.
- Header includes: generated-by comment, equivalent build command, ISO 8601 timestamp, and a reminder `docker build -f <output> -t <tag> .` command.
- Azure template appends an extra env-var note line to the header.
- Existing build paths are fully unchanged — switch is gated, no regressions on `run`, `stop`, `logs`, `clean`.
- Cleaned all pre-existing trailing whitespace from the file while editing (file standard: no trailing whitespace).
- Validated syntax with `[System.Management.Automation.Language.Parser]::ParseFile` — zero errors.

### poshmcp build CLI

- `poshmcp build` is a subcommand of the **poshmcp** dotnet global tool (packaged in `PoshMcp.Server/PoshMcp.csproj` with `<PackAsTool>true</PackAsTool>` and `<ToolCommandName>poshmcp</ToolCommandName>`).
- Accepts `--tag <image:tag>` (single tag only), `--modules`, `--type`, `--docker-file` options.
- Under the hood it calls `DockerRunner.BuildDockerBuildArgs` → `docker/podman build -f Dockerfile -t <tag> .` with auto-detection of docker vs podman.
- Because `poshmcp build` only supports one `--tag`, building both a versioned tag and `latest` requires: call `poshmcp build --tag $VersionedTag` once, then `docker tag $VersionedTag $latestTag` to alias the result — avoiding a double build.
- The deploy script's `Build-AndPushImage` was updated to use this pattern (replaced the direct `docker build -t … -t … -f Dockerfile .` line).

### poshmcp build --generate-dockerfile

- Added `--generate-dockerfile` (bool/switch) and `--dockerfile-output` (string, default `./Dockerfile.generated`) to `poshmcp build`.
- When `--generate-dockerfile` is set, the CLI reads the source Dockerfile, prepends a comment header (generated-by, equivalent build command, ISO 8601 timestamp), writes the result to the output path, prints a success message with the equivalent `docker build` command, and exits 0 — without invoking docker/podman at all.
- Added `DockerRunner.GenerateDockerfile(sourceDockerfilePath, outputPath, imageTag, modules?, sourceImage?)` to `PoshMcp.Server/Cli/DockerRunner.cs`; added `using System.IO;` to that file.
- Switched the build command handler from the typed-parameter `SetHandler` overload to `InvocationContext`-based pattern to cleanly accommodate the two extra options without hitting overload limits.
- Existing `poshmcp build` behavior (without the flag) is fully unchanged — docker detection and build execution path are identical.
- Build verified: `dotnet build PoshMcp.Server/PoshMcp.csproj --configuration Release -v quiet` → 0 errors.


# amy - History Archive (Pre-cleanup)

# Amy Work History

## Recent Status (2026-04-10)

**Summary:** Observability, Azure infrastructure, and deployment-documentation foundations remain complete. Active emphasis is on release hygiene, infrastructure troubleshooting, and keeping the decision pipeline accurate as `.squad` grows.

**Current Role:** Infrastructure and decision coordination. Primary areas: health checks, Azure Container Apps, deployment scripts, documentation verification, and release/version workflows.

## Project Context

**Project:** PoshMcp - Model Context Protocol (MCP) server for PowerShell
**Tech Stack:** .NET 10, C#, PowerShell SDK, OpenTelemetry, ASP.NET Core, xUnit
**Primary User:** Steven Murawski

## Recent Learnings

### 2026-04-14: GitHub Pages docs deployment workflow

- Added `.github/workflows/docs-pages.yml` to deploy the DocFX site from `docs/_site`.
- Triggered on `push` to `main` with `paths: docs/**`, plus optional `workflow_dispatch`.
- Used official GitHub Pages actions: `actions/configure-pages@v5`, `actions/upload-pages-artifact@v3`, and `actions/deploy-pages@v4`.
- Set workflow permissions to `contents: read`, `pages: write`, and `id-token: write`.
- Added workflow-level concurrency guard (`group: pages`, `cancel-in-progress: true`) to avoid overlapping deployments.

### 2026-04-14: GitHub Pages docs now build DocFX in CI

- Updated `.github/workflows/docs-pages.yml` to build docs in CI before Pages upload/deploy.
- Kept trigger scope (`push` to `main` on `docs/**`) and existing Pages deploy actions/permissions/concurrency unchanged.
- Added `actions/setup-dotnet@v4` and installed DocFX as a global dotnet tool, then ran `docfx build docs/docfx.json` from repo root.
- Continued publishing `docs/_site` via `actions/upload-pages-artifact@v3` to preserve existing deployment contract.

### 2026-04-14: v0.5.3 patch release

- Bumped `PoshMcp.Server/PoshMcp.csproj` version from `0.5.2` → `0.5.3` (patch increment).
- Pack command: `dotnet pack .\PoshMcp.Server\PoshMcp.csproj -c Release -o .\artifacts\nupkg` → produces `poshmcp.0.5.3.nupkg` (~25 MB).
- Update command: `dotnet tool update -g poshmcp --version 0.5.3 --add-source .\artifacts\nupkg --ignore-failed-sources`
- Verified: `poshmcp --version` → `0.5.3+1e96a436e71e0872f53a99c98d0a14f46f60fd42`
- Amended git commit: `chore: bump version to 0.5.3` with Copilot co-author trailer.
- Pushed amended commit with `--force-with-lease`.

### 2026-04-14: v0.5.2 patch release

- Bumped `PoshMcp.Server/PoshMcp.csproj` version from `0.5.1` → `0.5.2` (patch increment).
- Pack command: `dotnet pack .\PoshMcp.Server\PoshMcp.csproj -c Release -o .\artifacts\nupkg` → produces `poshmcp.0.5.2.nupkg` (~25 MB).
- Update command: `dotnet tool update -g poshmcp --version 0.5.2 --add-source .\artifacts\nupkg --ignore-failed-sources`
- Verified: `poshmcp --version` → `0.5.2+948d196ecc1cda94e45684e239269c382cce662a`
- No running poshmcp.exe processes present; process-stop guard was a no-op.
- Commit: `chore: bump version to 0.5.2` with Copilot co-author trailer.

### 2026-04-12: v0.5.1 patch release

- Bumped `PoshMcp.Server/PoshMcp.csproj` version from `0.5.0` → `0.5.1` (patch increment following "Bump version to 0.5.0" commit).
- Pack command: `dotnet pack .\PoshMcp.Server\PoshMcp.csproj -c Release -o .\artifacts\nupkg` → produces `poshmcp.0.5.1.nupkg` (~25 MB).
- Update command: `dotnet tool update -g poshmcp --version 0.5.1 --add-source .\artifacts\nupkg --ignore-failed-sources`
- Verified: `poshmcp --version` → `0.5.1+fad23f66007916f0c2145e7c5e0eb8a20925c8dd`
- `dotnet tool update` handles both first-time install and upgrades; no need to uninstall first.
- No running poshmcp.exe processes were present; process-stop guard was a no-op but remains required as a pre-check.

### 2026-04-09 to 2026-04-10: Release and operational hygiene

- `PoshMcp.Server/PoshMcp.csproj` is the source of truth for global tool versioning.
- Stop any running `poshmcp` process before `dotnet tool update -g poshmcp` to avoid access-denied uninstall/update failures.
- Scribe health checks and archive gates matter once `.squad` histories and decisions pass their size thresholds.

### 2026-04-03: Deployment docs and Azure workflow validation

- After large doc cleanups, verify code fences, redirects, command examples, and cross-links explicitly.
- Subscription-scoped Bicep deployment means manual examples must use `az deployment sub create`, not group-scoped commands.
- Deployment scripts with mixed imperative and declarative steps still need the resource group created before ACR and other imperative resource commands.

### 2026-03-27: Platform foundations that remain current

- Health checks and correlation IDs are the baseline observability layer, with explicit `Task.WaitAsync()` timeout enforcement.
- Azure Container Apps plus managed identity, scale-to-zero, and layered docs remain the deployment baseline.
- Multi-tenant deployment safety depends on tenant switching plus subscription-to-tenant validation.

### 2026-04-12: Sequential PR merge session (#92–#95)

- Processed PRs #92, #93, #94, #95 in order — all touching `Program.cs` and related CLI/schema areas.
- Rebase pattern: worktrees start at an already-up-to-date state for the first PR; subsequent PRs require a live rebase after each preceding merge lands on main.
- `dotnet restore` is required before `dotnet test --no-restore` when worktrees haven't been built yet; the `--no-restore` flag fails with `NETSDK1004` on a cold worktree.
- `gh pr merge --delete-branch` produces a non-zero exit code in worktree setups (`fatal: 'main' is already used by worktree`) but the squash merge itself succeeds — the exit code is a false failure from the local branch-delete step, not from the GitHub merge.
- Test counts grew across the session: 343 → 343 → 355 → 388 (PR #94 added 12 tests for update-config flags; PR #95 added 33 tests for unserializable type handling).
- All 4 PRs merged cleanly with zero conflicts. The `Program.cs` changes were additive (new CLI flags, advisory warning) and non-overlapping.
- Force-push must specify the remote branch name explicitly (`git push --force-with-lease origin <branch>`) when the worktree branch has no upstream tracking configured.

### 2026-04-13: Intermittent test failure investigation




## Archived 2026-05-05 (history summarization, lines 201-482 of pre-summarization file)

- Pushed commit to origin main successfully (7 commits ahead including prior session work).
- Created annotated tag 0.8.3 with git tag -a v0.8.3 -m "Release v0.8.3" and pushed it.
- GitHub reported a repository redirect (usepowershell/poshmcp -> usepowershell/PoshMcp) — push succeeded regardless; update remote URL when convenient.
- GitHub also surfaced 1 moderate Dependabot vulnerability — flagged for follow-up.

### 2026-04-24: v0.8.4 release push

- Staged PoshMcp.Server/PoshMcp.csproj, docs/release-notes/0.8.4.md, docs/toc.yml individually.
- Initial push to origin main was rejected due to a merge commit (b0a80e4) in local history (branch was 3 commits ahead, one being a merge).
- Resolution: stashed unstaged changes, rebased --onto origin/main to drop the merge commit, reset main to rebased HEAD (f5583fe), restored stash. Branch became 1 commit ahead with no merge commits.
- Push to origin main succeeded: 6d7a138..f5583fe.
- Created annotated tag v0.8.4 and pushed successfully.
- Commit SHA: f5583feeb3a49c7c8bd22ab7c150414241ca88b9
- GitHub repository redirect (usepowershell/poshmcp -> usepowershell/PoshMcp) present but push succeeds; recommend updating remote URL.
- Key learning: always check local log for merge commits before pushing to protected branch; use rebase --onto to cleanly remove them.

## 2026-04-24: Version 0.8.5 bump and global tool update

**Learnings:**
- Bumped version from 0.8.4 to 0.8.5 in PoshMcp.Server/PoshMcp.csproj.
- Packed with `dotnet pack PoshMcp.Server/PoshMcp.csproj --configuration Release --output ./artifacts`.
- Uninstalled current global tool with dotnet tool uninstall -g poshmcp (0.8.4).
- Reinstalled with dotnet tool install -g poshmcp --add-source ./artifacts --version 0.8.5.
- Verified installation: poshmcp --version returned  .8.5+35c51ce6b51eb8e65ed6af5124741a87490c62da.
- All steps completed successfully; global tool is now active at version 0.8.5.

### 2026-current: Patch release 0.8.6

- Bumped PoshMcp.Server/PoshMcp.csproj version from 0.8.5 to 0.8.6.
- Packed with dotnet pack PoshMcp.Server/PoshMcp.csproj --configuration Release --output ./artifacts.
- Uninstalled current global tool (0.8.5).
- Installed 0.8.6 from local artifacts with dotnet tool install -g poshmcp --add-source ./artifacts --version 0.8.6.
- Verified: poshmcp --version returns  .8.6+35c51ce6b51eb8e65ed6af5124741a87490c62da.
- Version bump to 0.8.6 complete; global tool updated successfully.

### Version bump 0.8.6 → 0.8.7

- Updated PoshMcp.Server/PoshMcp.csproj: changed <Version>0.8.6</Version> to <Version>0.8.7</Version>.
- Ran dotnet pack PoshMcp.Server/PoshMcp.csproj --configuration Release --output ./artifacts → produced poshmcp.0.8.7.nupkg.
- Uninstall cycle: dotnet tool uninstall -g poshmcp → removed version 0.8.6.
- Install new version: dotnet tool install -g poshmcp --add-source ./artifacts --version 0.8.7 → successfully installed.
- Verified: poshmcp --version →  .8.7+35c51ce6b51eb8e65ed6af5124741a87490c62da.

## Learnings
Version bumped to 0.8.8 (phase 1 of coordinated release)
Released v0.8.8: bundled install-modules.ps1 in base image, fixed --generate-dockerfile default, full pipeline: commit→push→tag→pack→global-install

### Version bump 0.8.8 → 0.8.9

- Updated PoshMcp.Server/PoshMcp.csproj: changed <Version>0.8.8</Version> to <Version>0.8.9</Version>.
- Ran dotnet pack PoshMcp.Server/PoshMcp.csproj --configuration Release --output ./artifacts → produced poshmcp.0.8.9.nupkg (27.1 MB).
- Uninstall cycle: dotnet tool uninstall -g poshmcp → removed version 0.8.8.
- Install new version: dotnet tool install -g poshmcp --add-source ./artifacts --version 0.8.9 → successfully installed.
- Verified: poshmcp --version → 0.8.9+216689bc436d8739a4b5a91c1ec75fc56b39221d.
- Bumped to 0.8.9 — added PSModule path docs and local COPY examples to examples/Dockerfile.user

### Version bump 0.8.9 → 0.8.10- Updated PoshMcp.Server/PoshMcp.csproj: changed <Version>0.8.9</Version> to <Version>0.8.10</Version>.
- Ran dotnet pack PoshMcp.Server/PoshMcp.csproj --configuration Release --output ./artifacts → produced poshmcp.0.8.10.nupkg.
- Uninstall cycle: dotnet tool uninstall -g poshmcp → removed version 0.8.9.
- Install new version: dotnet tool install -g poshmcp --add-source ./artifacts --version 0.8.10 → successfully installed.
- Verified: poshmcp --version → 0.8.10+216689bc436d8739a4b5a91c1ec75fc56b39221d.
- Bumped to 0.8.10 — added --appsettings option to poshmcp build

Pushed main + tagged v0.8.11 after Leela added release notes

### 2026-04-25: GitHub milestone #4 and issues #170–#175 creation

- Created GitHub milestone #4: Application Insights Logging (Spec 008)
- Created 6 linked GitHub issues (#170–#175) for spec 008 tasks
- Authentication: switched to usepowershell account via GH_TOKEN environment variable for API calls
- All issues linked to milestone #4 for centralized tracking

### 2026-04-27: Issue #171
Added ApplicationInsights section to appsettings.json and created ApplicationInsightsOptions.cs in worktree poshmcp-171. PR opened. NOTE: csproj filename is PoshMcp.csproj NOT PoshMcp.Server.csproj.

### 2026-05-01: Release v0.9.2 — security fix for authentication bypass

**Session:** Version bump 0.9.1 → 0.9.2, commit, push, wait for CI, tag

**Key Changes:**
- Bumped `PoshMcp.Server/PoshMcp.csproj` version from `0.9.1` to `0.9.2`
- Updated `CHANGELOG.md` with new `## [0.9.2] - 2026-05-01` entry documenting authentication bypass security fix
- Security issue: `AddPoshMcpAuthentication()` was not registering `AuthenticationConfiguration` with .NET options system; `IOptions<AuthenticationConfiguration>` always resolved to default value (`Enabled = false`) regardless of config, allowing unauthenticated requests through when `Authentication.Enabled: true` was set
- Fix: added `services.Configure<AuthenticationConfiguration>()` unconditionally in `AddPoshMcpAuthentication()`
- Added 3 regression tests covering auth-enabled, auth-disabled, and missing-section scenarios
- Committed with message: `release: v0.9.2 — security fix for authentication bypass`
- Pushed to main: `5d55bbf..de02f7d`
- Waited for CI workflows: both "CI" and "Preview Packages" completed with status `success`
- Pre-existing annotated tag `v0.9.2` already created and pushed by prior process; confirmed tag points to correct commit SHA `de02f7d8766b5e4d9b073df33139467a5cfb2d5c`
- Verified remote tag present via `git ls-remote --tags origin | Select-String v0.9.2`

**Learnings:**
- Release workflow fully automated: version bump → commit → push → poll CI workflows via `gh run watch --exit-status` → annotated tag already in place by auto-release process
- GitHub Actions workflow coordination: both CI and Preview Packages workflows trigger on push to main; CI runs first (build, tests) and must complete before tag deployment is safe
- Tag may be pre-created by automation; always verify it points to the correct commit SHA before assuming rebase or retry is needed
- CI pass criteria: `dotnet build` with pre-existing nullable reference warnings (non-blocking), all unit/functional tests passing, no syntax errors

## 2026-05-01: OAuthProxy env vars wired for AdvocacyBami deployment

**Session:** Wire Bender's OAuthProxy config into the AdvocacyBami Container App deployment

**Real deployment layout (corrected from prior audit):**
- Deployment is NOT under `poshmcp/infrastructure/` — it is in a separate repo:
  - `C:\Users\stmuraws\source\emu\gim-home\AdvocacyBami\infra\azure\deploy.ps1`
  - `C:\Users\stmuraws\source\emu\gim-home\AdvocacyBami\appsettings.json`
- The `AdvocacyBami` directory is the canonical deployment root; `infra/azure/` contains deploy.ps1, main.bicep, resources.bicep, and parameters.json.

**Env var wiring mechanism:**
- `ConvertTo-McpServerEnvVars` in `deploy.ps1` reads `appsettings.json` and emits `{ name, value }` arrays passed to Bicep's `serverEnvVars` parameter.
- `resources.bicep` `concat`s `serverEnvVars` onto the fixed env block in the Container App template.
- Before this session, only `Authentication__Enabled` was translated from the `Authentication` section; the `OAuthProxy` sub-section was silently skipped.

**Live probe results (2026-05-01):**
- `/.well-known/oauth-authorization-server` → **404** — Bender's code is in `main` but has NOT been deployed yet (image not rebuilt/redeployed).
- `/.well-known/oauth-protected-resource` → **200** but with duplicate `authorization_servers` and `bearer_methods_supported` entries — pre-Bender state.

**Changes made:**
1. `AdvocacyBami/appsettings.json`:
   - Added `Authentication.OAuthProxy` block: `Enabled=true`, `TenantId` (sourced from Bearer Authority), `ClientId` (App ID GUID from resource URI), `Audience` (App ID URI).
   - Cleared `ProtectedResource.AuthorizationServers` to `[]` — Bender's code will auto-populate with the server's own base URL when OAuthProxy is enabled, eliminating the duplicate/direct-Entra entries.
2. `AdvocacyBami/infra/azure/deploy.ps1` (`ConvertTo-McpServerEnvVars`):
   - Extended `Authentication` block handling to also translate `OAuthProxy.{Enabled,TenantId,ClientId,Audience}` → `Authentication__OAuthProxy__*` env vars.

**What blocks redeploy:**
- A full redeploy (image rebuild + Bicep apply) must be run by Steven to activate Bender's code. Command: from `AdvocacyBami/`, run:
  ```powershell
  ./infra/azure/deploy.ps1 -ServerAppSettingsFile ./appsettings.json -AppSettingsFile ./infra/azure/deploy.appsettings.json
  ```
  (Or pass `-RegistryName` directly if no local deploy.appsettings.json is configured.)

**Key learnings:**
- `ConvertTo-McpServerEnvVars` must be updated any time a new appsettings section is added; it is a curated translator, not a generic JSON-to-env-var converter.
- `ProtectedResource.AuthorizationServers` should be empty (`[]`) when OAuthProxy is enabled — the server auto-fills it dynamically.
- Tenant GUID (`d91aa5af-...`) and App ID / Client ID (`80939099-...`) are already present in the existing appsettings; no new secrets needed.


## 2026-05-01: Team OAuth Authentication Architecture Session

### OAuth Proxy Implementation (Joint Effort)
**Bender + Amy coordinated on comprehensive OAuth fix for deployment:**

- **Bender Role:** Implemented OAuth AS proxy + DCR proxy server-side (RFC 8414 + RFC 7591)
  - Added /.well-known/oauth-authorization-server endpoint
  - Added /register DCR proxy (returns configured ClientId)
  - Dynamic ProtectedResource.AuthorizationServers population
  - PR #135 (items 1-4) merged: LoggingHelpers, DockerRunner, SettingsResolver, ConfigurationFileManager, ConfigurationLoader extracted
  - 32 tests passing

- **Amy Role:** Fixed deployment-side configuration (Container Apps + Bicep)
  - Audited deployed Container App (found OAuth proxy disabled)
  - Located real deployment repo (AdvocacyBami, separate from poshmcp)
  - Patched ppsettings.json with OAuthProxy config (TenantId, ClientId, Audience)
  - Updated deploy.ps1 to translate OAuthProxy env vars
  - Cleared duplicate ProtectedResource.AuthorizationServers entries
  - Changes applied; awaiting redeploy

**Coordination outcome:** Server-side OAuth metadata now advertises Entra endpoints; deployment config now passes OAuth settings to Container App via env vars. MCP clients should complete OAuth 2.0 code grant flow without redirect loops after redeploy.

**Decision files:** bender-mcp-oauth-metadata.md, amy-container-apps-auth-config.md (both merged to decisions.md)

### 2026-05-02: OAuth Proxy Configuration Wiring Investigation

**Issue:** \.well-known/oauth-authorization-server\ returns 404; OAuthProxy not enabled
**Diagnosis Scope:** Dockerfile → PoshMcp.Server appsettings → deployment pipeline

**Key Findings:**
- **Dockerfile:** Does NOT explicitly copy/overlay appsettings files. Uses \dotnet publish\ which includes ./PoshMcp.Server/appsettings.json by default.
- **PoshMcp.Server/appsettings.json:** No OAuthProxy section. Only minimal Authentication config (Enabled=false).
- **Other appsettings files:** No OAuthProxy config in any .json file (checked: appsettings.azure.json, appsettings.modules.json, appsettings.environment-example.json, all examples).
- **Deployment Pipeline (deploy.ps1):** ConvertTo-McpServerEnvVars() function DOES NOT translate OAuthProxy keys to env vars. Only handles PowerShellConfiguration, Authentication.Enabled, Logging.

**Root Cause:** OAuthProxy configuration does NOT exist anywhere in PoshMcp repository. Either: 1) Patch created externally, never merged to PoshMcp.Server/appsettings.json. 2) Image built before patch. 3) Both Dockerfile and pipeline need OAuthProxy extension.

**Resolution Options:**
- **Option A (Recommended):** Add OAuthProxy section to ./PoshMcp.Server/appsettings.json, rebuild image, redeploy.
- **Option B:** Extend deploy.ps1 ConvertTo-McpServerEnvVars() to translate OAuthProxy keys to env vars, use Key Vault.
- **Option C (Best):** Both A + B for baked defaults with runtime override.

**Next Steps:** Clarify with Fry/Steven if patch exists externally. Add to appsettings if found. Extend deploy.ps1 in parallel. Full diagnosis at .squad/decisions/inbox/amy-appsettings-image-wiring.md


### 2026-05-02: Release v0.9.10 (Amy as DevOps lead)

- Verified working tree clean (test output cleaned up).
- Confirmed OAuth issuer fix commit (b81a55d) present on main.
- Confirmed version bump to 0.9.10 in PoshMcp.csproj.
- Pushed main to origin: 2 commits ahead (fix + release notes prep).
- Created annotated tag v0.9.10 and pushed to origin.
- CI triggered immediately upon tag push:
  - Workflow: 'Build and Publish Packages' (Run ID 25254551703) — in_progress
  - Will build container image and publish to GHCR as ghcr.io/usepowershell/poshmcp:0.9.10
- Monitoring URL: https://github.com/usepowershell/PoshMcp/actions/runs/25254551703
- Release process completed successfully. Steven to monitor container build completion and coordinate AdvocacyBami update.

## [2026-05-02] v0.9.11 Release — OAuth /authorize Proxy Endpoint

**Session:** Release v0.9.11 (Amy as DevOps lead)
**Contribution:** Release management and publication workflow

**What was released:**
- OAuth /authorize proxy endpoint that redirects to Entra's authorize URL with PKCE params
- Fixes VS Code MCP client OAuth flow (was getting 404 on auth endpoint)
- Replaces ephemeral DCR client_id with real Entra client_id from config

**Release steps executed:**
1. ✅ Confirmed version in PoshMcp.Server/PoshMcp.csproj: 0.9.11
2. ✅ Created release notes at docs/release-notes/0.9.11.md
3. ✅ Updated docs/toc.yml with v0.9.11 entry
4. ✅ Committed release notes: docs: add v0.9.11 release notes (7e67ac9)
5. ✅ Pushed to origin main (b81a55d → 7e67ac9)
6. ✅ Tagged v0.9.11
7. ✅ Pushed tag to origin

**Artifacts:**
- docs/release-notes/0.9.11.md (release notes with feature and bug fix details)
- docs/toc.yml (updated table of contents)
- Git commit: 7e67ac9
- Git tag: v0.9.11

**Key Learnings:**
- Release process from Bender's prior commit flows cleanly through Amy's release management
- No obstacles or issues encountered
- Release notes format follows consistent pattern from prior versions (0.9.10 template)

### 2026-05-02: v0.9.12 release

- Tagged commit on main as v0.9.12
- Pushed tag to origin
- CI builds started (Build and Publish workflow triggered)

### 2026-05-02: v0.9.13 release — Entra v2.0 authorize proxy fix (AADSTS9010010)

- Bender's fix `0f5e2bf` (strip `resource` param from Entra v2.0 authorize proxy) was 2 commits ahead of v0.9.12.
- Bumped `PoshMcp.Server/PoshMcp.csproj` version from `0.9.12` → `0.9.13`.
- Committed: `chore: bump version to 0.9.13`, tagged `v0.9.13`, pushed main + tag to origin.
- GitHub Actions `Build and Publish Packages` (run 25258358757) triggered on `v0.9.13` tag push — completed ✓ in ~3 min.
  - Published NuGet package `poshmcp.0.9.13` to GitHub Packages and NuGet.org.
  - Built and pushed container image `ghcr.io/usepowershell/poshmcp/poshmcp:0.9.13` and `:latest` to GHCR.
- Deployed AdvocacyBami Container App via `.\infra\azure\deploy.ps1 -AppSettingsFile .\infra\azure\deploy.appsettings.json` — completed ✓.
  - Image built from `Dockerfile.generated` (pulls `ghcr.io/usepowershell/poshmcp/poshmcp:latest`), pushed to `psbamiacr.azurecr.io/poshmcp:latest`.
  - Bicep infrastructure deployed, health check passed.
- Post-deploy verification:
  - `GET /health` → `{"status":"Healthy"}` — all 3 checks (powershell_runspace, assembly_generation, configuration) green.
  - `GET /.well-known/oauth-authorization-server` → returns Entra v2.0 metadata with correct `authorization_endpoint` (no `resource` param will be sent on redirects).
- Note to Steven: cached VS Code token may still cause issues. Sign out from VS Code Accounts → poshmcp MCP auth entry, then reconnect to get fresh auth.

## 2026-05-03: v0.9.21 release — Test fix for DoctorReport role claim short name

**Release workflow executed:**
- Pre-release quality gate: `dotnet test PoshMcp.Tests\PoshMcp.Tests.csproj --no-restore`
  - Result: **590 passed, 1 skipped, 0 failed** (exit code 0)
  - No process locks, clean build
- Version bump: `PoshMcp.Server/PoshMcp.csproj` `0.9.20` → `0.9.21`
- CHANGELOG.md entry prepended with fix summary (updated `DoctorReportTests` to use `"roles"` short claim name after v0.9.20's `MapInboundClaims = false`)
- Commit: `chore: bump version to 0.9.21` with Copilot co-author trailer
- Tagged: `v0.9.21`
- Push: `git push origin main && git push origin v0.9.21` — both succeeded
- Log verification: `2ad3739 (HEAD -> main, tag: v0.9.21, origin/main, origin/HEAD) chore: bump version to 0.9.21`
- CI will auto-trigger on tag push (Build and Publish Packages workflow)

**Learnings:**
- Process lock on `PoshMcp.exe` (from prior interactive session) blocked build retry. Solution: `Stop-Process -Id <PID> -Force` before test rerun.
- Quality gate (full test suite) is mandatory before every release — it prevents shipping regressions.
- Release commit message must include Copilot co-author trailer for proper attribution.

## 2026-05-03: v0.9.21 release verification

**Release workflow verification (already completed by prior session):**
- Pre-release quality gates re-confirmed:
  - `dotnet format --verify-no-changes` → **PASS** (exit code 0, no formatting changes needed)
  - `dotnet test --filter "Category!=Integration" --no-build` → **PASS** (all tests passed cleanly)
- Version: `PoshMcp.Server/PoshMcp.csproj` confirmed as `0.9.21`
- CHANGELOG.md: v0.9.21 entry present with test fix summary
- Commit: `2ad3739` (`chore: bump version to 0.9.21`) with Copilot co-author trailer
- Tag: `v0.9.21` pointing to `2ad3739`
- Push: ✅ both commit and tag successfully pushed to `origin/main` and `origin/tags`

**Key learnings:**
- Release workflow is idempotent — running quality gates a second time on shipped commits still passes, confirming code quality remains intact after merge/push.
- GitHub Actions auto-triggers on tag push (`v*` pattern) — no manual publish steps needed. NuGet package and container image will be built, tested, and published to registries automatically.
- Release commit/tag structure proved solid across multiple releases (v0.9.20, v0.9.21) — pattern is stable and repeatable.



## Archived 2026-05-05 (second-pass summarization)

- Three files contained the stale value `poshmcp-rg`:
  - `infrastructure/azure/deploy.ps1` — `$ResourceGroup` default fixed to `'rg-poshmcp'`
  - `infrastructure/azure/deploy.sh` — `RESOURCE_GROUP` default fixed to `rg-poshmcp`
  - `infrastructure/azure/validate.ps1` — help text updated to `rg-poshmcp`
- Other defaults (`location = eastus`, `containerAppName = poshmcp`) were already consistent across all files.
- Rule: Bicep + parameters.json are the source of truth for infrastructure defaults. Deploy scripts must follow, not define.

**Artifacts:** infrastructure/azure/deploy.ps1, infrastructure/azure/deploy.sh, infrastructure/azure/validate.ps1

## [2026-07-18] ACR Pull — Managed Identity Auth for Container App

**Session:** Fix Container App UNAUTHORIZED error pulling from ACR
**Contribution:** Wired user-assigned managed identity to AcrPull role on ACR; updated registries config to use identity instead of credentials

**Key Learnings:**
- See Learnings section below for the complete ACR → Container App auth pattern.

**Artifacts:** infrastructure/azure/resources.bicep

- **ACR -> Container App auth (managed identity pattern):** When a Container App needs to pull from ACR without credentials, the correct pattern is: (1) declare a conditional `existing` reference to the ACR resource in the same resource group, (2) add a `Microsoft.Authorization/roleAssignments` scoped to the ACR granting AcrPull (`7f951dda-4ed3-4680-a7ca-43fe172d538d`) to the managed identity's `principalId`, and (3) set `registries[].identity` to the managed identity's resource ID (user-assigned) — no `passwordSecretRef` needed. Add `dependsOn: [acrPullRoleAssignment]` on the Container App so ARM sequences the role before the app revision is created. Both the existing ACR ref and role assignment should be conditional on `!empty(containerRegistryServer)` for backward compatibility. The ACR registry name is derived via `split(containerRegistryServer, '.')[0]`. No changes to deploy.ps1 needed — Bicep handles the role assignment entirely at resource group scope.

## [2026-04-23T15:56:32-05:00] Deploy Script AppSettings Parameter Sourcing

**Session:** Extend infrastructure deployment script to source values from appsettings-style JSON while keeping existing workflow compatibility.
**Contribution:** Added `-AppSettingsFile` and `DEPLOY_APPSETTINGS_FILE` support with explicit precedence (`CLI > env > appsettings > defaults`) in `infrastructure/azure/deploy.ps1`.

**Key Learnings:**
- PowerShell parameter defaults that directly read env vars make precedence opaque and harder to extend. Moving resolution into a dedicated initialization function enables transparent and testable precedence handling.
- For deploy-specific configuration, a dedicated `AzureDeployment` section in an appsettings file is clear and avoids coupling to runtime server appsettings schemas.
- Supporting both `AzureDeployment` and `Deployment.Azure` shapes provides backward-friendly flexibility for future scaffold/output conventions.
- Boolean settings in mixed sources (switch/env/json) need explicit normalization; accepted values now include `true/false`, `1/0`, `yes/no`, and `on/off`.
- Deploy script now logs source provenance per resolved setting, which improves debugging in CI and multi-tenant deployments.

**Artifacts:**
- `infrastructure/azure/deploy.ps1`
- `infrastructure/azure/deploy.appsettings.json.template`
- `infrastructure/azure/QUICKSTART.md`

### 2026-04-23: Local release mechanics (0.8.1)

- Bumped tool/package version in `PoshMcp.Server/PoshMcp.csproj` from `0.8.0` to `0.8.1` and packed with `dotnet pack -c Release -o .\artifacts\nupkg`.
- Global update from local source initially failed with access denied uninstalling `C:\Users\stmuraws\.dotnet\tools\.store\poshmcp\0.8.0` because running `poshmcp` processes held the lock.
- Safe recovery pattern: stop `poshmcp`/`PoshMcp` processes, then rerun `dotnet tool update -g poshmcp --add-source .\artifacts\nupkg --version 0.8.1 --ignore-failed-sources`.
- Verification: `dotnet tool list -g` shows `poshmcp 0.8.1`; `poshmcp --version` reports `0.8.1+acf034bc2eb5d848c8c4e854c69abb587eb0a691`.


## 2026-04-23 17:21 — appsettings → env var mapping (with Bender)

- Added \xtraEnvVars array\ param to \
esources.bicep\ (default = empty); concat into Container App env alongside hardcoded vars.
- Added \xtraEnvVars\ passthrough param in \main.bicep\, wired into module call.
- Both Bicep files re-embedded on next build (no csproj changes needed).
- Key file: infrastructure/azure/resources.bicep, infrastructure/azure/main.bicep

## 2026-04-23 — Server appsettings to Container App env vars

**Task:** Wire deploy.ps1 to read PoshMcp.Server/appsettings.json and translate runtime
settings into Container App environment variables.

**Changes made:**
- `deploy.ps1`: renamed `-McpAppSettingsFile` -> `-ServerAppSettingsFile`, added `POSHMCP_APPSETTINGS_FILE` env var support, added translations for `IncludePatterns`, `ExcludePatterns`, `EnableConfigurationTroubleshootingTool`, `Logging.LogLevel.Default`, fixed RuntimeMode values to emit "InProcess"/"OutOfProcess" (matching server enum `.ToString()`), renamed `ExtraEnvVars` -> `ServerEnvVars`, passes `serverEnvVars` to Bicep unconditionally.
- `resources.bicep`: removed `powerShellFunctions` param + derived vars, removed `enableDynamicReloadTools` param + static env var entry, renamed `extraEnvVars` -> `serverEnvVars`.
- `main.bicep`: removed `powerShellFunctions` and `enableDynamicReloadTools` params, renamed `extraEnvVars` -> `serverEnvVars` in module call.
- `parameters.json`: removed `powerShellFunctions` and `enableDynamicReloadTools` entries.
- `deploy.appsettings.json.template`: added clarifying header comment.

**Key learnings:**
- The server normalizes POSHMCP_RUNTIME_MODE via `NormalizeRuntimeModeValue()` in `Cli/SettingsResolver.cs` — strips `-`/`_`, lowercases, maps "inprocess" -> `RuntimeMode.InProcess.ToString()` = "InProcess". Always use PascalCase enum values for this env var.
- `resources.bicep` uses `concat([...fixed vars], serverEnvVars)` — the fixed vars block always includes ASPNETCORE_ENVIRONMENT, ASPNETCORE_URLS, POSHMCP_TRANSPORT, APPLICATIONINSIGHTS_CONNECTION_STRING, AZURE_CLIENT_ID.
- deploy.ps1 always passes `serverEnvVars` (empty array if no appsettings file) — no conditional injection.
- Test filter: `FullyQualifiedName~DeployScript` — 1 test passes.

## 2026-04-24: poshmcp build flow alignment for source-image publishing

- Audited script/workflow code paths that execute `poshmcp build` under `.github/workflows/**`, `docker.ps1`, `docker.sh`, and repository scripts.
- Confirmed only two executable call sites in this scope: `.github/workflows/publish-packages.yml` and `infrastructure/azure/deploy.ps1`.
- Updated both source-image build paths to use explicit base build flow:
  - `dotnet run ... -- build --type base --tag "$IMAGE"` in publish workflow.
  - `poshmcp build --type base --tag $FullImageName` in Azure deploy script.
- Rationale: `poshmcp build` defaults to `custom`; publishing/building this repo image from local source must explicitly set `--type base` to use `Dockerfile` runtime source build.
- Quick validation completed: PowerShell parser reports no syntax errors for `infrastructure/azure/deploy.ps1`; grep verification confirms corrected command usage.

## 2026-04-24: Release bump, pack, and consistency update (v0.8.3)

- Bumped `PoshMcp.Server/PoshMcp.csproj` version from `0.8.2` to `0.8.3` (patch release).
- Added `docs/release-notes/0.8.3.md` and wired it into `docs/toc.yml` under Release Notes.
- Packed with `dotnet pack .\PoshMcp.Server\PoshMcp.csproj -c Release -o .\artifacts\nupkg`.
- Produced artifact: `artifacts/nupkg/poshmcp.0.8.3.nupkg`.
- Verified build with `dotnet build .\PoshMcp.Server\PoshMcp.csproj -c Release`.

**Key Learnings:**
- Current package/version source of truth remains `<Version>` in `PoshMcp.Server/PoshMcp.csproj`.
- Release notes continuity requires both a new notes file and a matching entry in `docs/toc.yml`.

## 2026-04-24: Release v0.8.3 pushed to origin

**Learnings:**
- Staged release files individually (csproj, release notes, toc.yml, squad state files) using explicit paths — never git add ..
- Committed with message: chore: bump version to 0.8.3 and add release notes.
- Release commit SHA: 492e3b.

---
*Older entries (pre-2026-05-05 bulk) moved to `history-archive.md` on 2026-05-05 by Scribe to satisfy 15KB hard gate. See archive for full record.*

## Archived 2026-05-14T11:34Z

### 2026-04-18: Spec 002 PR creation and merge session

- Created 4 PRs targeting main: #125 (resources), #126 (prompts), #127 (doctor), #128 (tests).
- PR #125 squash-merged cleanly (no conflicts on origin).
- PR #126 required rebase in worktree `poshmcp-002-prompts` (`Program.cs` conflict resolved using integration branch version with both handlers chained). Squash-merged.
- PR #127 required rebase in worktree `poshmcp-002-doctor` (5 add/add conflicts on McpPrompts/McpResources config files — kept HEAD/main versions; `Program.cs` resolved from integration branch). Squash-merged.
- PR #128 (tests) created but NOT merged — pending rebase onto merged main.
- **Encoding bug encountered and fixed:** `git show | Out-File -Encoding UTF8` in PowerShell 5 converts UTF-8 BOM bytes (0xEF 0xBB 0xBF) through CP850 console encoding into literal characters ∩╗┐ (U+2229 U+2557 U+2510), causing `CS1056` C# build errors.
- **Fix:** Use `cmd /c "git show <ref>:path > outfile"` for binary-safe file extraction. Applied as fix commit `c17cdf8` on main.
- Final build: `dotnet build PoshMcp.sln --no-incremental` → **Build succeeded, 0 errors**.

### 2026-04-18: Spec 002 final merge — PR #128 and worktree cleanup

- Squash-merged PR #128 (`feature/002-tests` → `main`) via `gh pr merge 128 --squash --delete-branch`. GitHub confirmed merge to `b6a268c`.
- Pulled `main` (fast-forward): 10 new test files, 2,267 lines added.
- Final `dotnet test PoshMcp.sln` on main: **476 passed, 1 failed, 1 skipped — total 478**.
  - Failing: `McpResourcesValidatorTests.cs(250) Assert.NotEmpty()` — pre-existing, non-blocking.
  - Skipped: `ShouldHandleGetChildItemCorrectly` — pre-existing, non-blocking.
- Removed all four spec-002 feature worktrees: `poshmcp-002-resources`, `poshmcp-002-prompts`, `poshmcp-002-doctor`, `poshmcp-002-tests`.
- Deleted local branches: `feature/002-resources`, `feature/002-prompts`, `feature/002-doctor`, `feature/002-tests`, `integration/spec-002-mcp-resources-and-prompts`.
- Deleted remote branches: all four `feature/002-*` and `integration/spec-002-mcp-resources-and-prompts`.
- Spec review worktrees (`poshmcp-spec-001` through `poshmcp-spec-005`) are separate infrastructure — left intact.
- Note: `gh pr merge --delete-branch` produces a non-zero exit but the merge itself succeeds when GitHub auto-deletes the remote branch (same false-failure pattern as #92–#95 session). Squash-merge is the required strategy (merge commits blocked on this repo).
- Spec 002 is fully closed. No residual branches or worktrees remain.

### 2026-04-18: Issue #131 STDIO logging infrastructure (Amy as DevOps lead)

- Suppressed OTel console exporter in stdio mode via isStdioMode parameter in ConfigureOpenTelemetry.
- Updated all appsettings files with Logging.File.Path schema (appsettings.json, default.appsettings.json, environment-example, azure, modules).
- Infrastructure changes complete and merged to squad/131-stdio-logging-to-file branch.

### 2026-04-18: v0.6.0 minor release

- Minor version bump: `PoshMcp.Server/PoshMcp.csproj` `0.5.6` → `0.6.0` (reflects merged feature PRs #125–#128 for Spec 002).
- Pulled latest main: branch already up-to-date (10 spec-002 test commits already present from previous session).
- Pack command: `dotnet pack PoshMcp.Server/PoshMcp.csproj -c Release -o ./nupkg` → produced `poshmcp.0.6.0.nupkg` (25.8 MB).
- Uninstall/reinstall cycle: removed `poshmcp.0.5.6`, installed `0.6.0` from local nupkg source.
- Verified: `poshmcp --version` → `0.6.0+3ed89f5946ba89be53ebb9f85238ab1a3143015b` (commit hash from main).
- Commit: `chore: bump version to 0.6.0` with Copilot co-author trailer; pushed to main.

### 2026-04-18: CI/CD pipeline improvements — preview builds, NuGet.org release, README in package

- Added `<PackageReadmeFile>README.md</PackageReadmeFile>` to `PoshMcp.Server/PoshMcp.csproj` PropertyGroup.
- Added `<None Include="..\README.md" Pack="true" PackagePath="\" />` so README.md from the repo root is embedded in the NuGet package.
- Created `.github/workflows/preview-packages.yml`: triggers on push to main (same paths as ci.yml), skips on `[skip ci]` or `[no preview]` in commit message, versions as `{base-version}-preview.{GITHUB_RUN_NUMBER}`, runs unit + functional tests, packs and publishes to GitHub Packages, uploads artifact (14-day retention), writes a job summary with version and link.
- Reworked `.github/workflows/publish-packages.yml`: replaced `release: published` trigger with `push: tags: ['v*']`; updated version logic to strip `v` prefix from `github.ref_name` on tag push; added "Publish to NuGet.org" step (using `NUGET_API_KEY` secret, `if: github.event_name == 'push'`); added "Create or update GitHub Release with notes" step that uses `docs/release-notes/{version}.md` if present or auto-generates notes; updated `contents` permission from `read` to `write` (required for `gh release`); updated container job's "Tag image as latest" and "Push latest tag" `if:` conditions from `release` to `push`.
- All changes committed and pushed to main: `0037c66`.



- Package artifact: `nupkg/poshmcp.0.6.0.nupkg` (verified present, 25.8 MB).
- GitHub Packages source was already registered as `github-poshmcp` → `https://nuget.pkg.github.com/usepowershell/index.json`.
- Publish command: `dotnet nuget push ./nupkg/poshmcp.0.6.0.nupkg --source https://nuget.pkg.github.com/usepowershell/index.json --api-key (gh auth token)`.
- Result: **Successfully published** to GitHub Packages NuGet registry.
- Verified via `gh api "/users/usepowershell/packages/nuget/poshmcp/versions"` → confirmed `0.6.0` is the latest published version (alongside 0.5.6 and 0.5.5).
- Repository owner: `usepowershell` (user account, not organization).


## Cross-Agent: PR #139 Also Approved (2026-04-20)

- Farnsworth approved both PRs #138 and #139
- Bender added config secrets redaction to #139
- 334 tests now passing across suite

## Learnings

- **Version management:** Project version is maintained solely in PoshMcp.Server/PoshMcp.csproj under the <Version> element. No distributed version configuration across multiple files (e.g., Directory.Build.props). Bumped  .7.1 →  .8.0.- **Tool update access denied:** `dotnet tool update -g poshmcp` can fail with "Access to the path ... is denied" if the poshmcp process is currently running (e.g., as an MCP server in VS Code). Stop all poshmcp processes first (`Get-Process poshmcp | Stop-Process -Force`), then retry the update. This applies to 0.8.3 → 0.8.4 and any future in-place updates while the tool is active.
- **Container Apps OAuth Configuration (2026-05-01):** Audited deployed Container Apps OAuth setup. Key findings: (1) OAuth Proxy not enabled on deployment — OAuthProxy.Enabled defaults to false, so /.well-known/oauth-authorization-server returns 404. (2) /.well-known/oauth-protected-resource returns 200 with valid metadata pointing to Entra (tenant: d91aa5af-8c1e-442c-b77c-0b92988b387b), but has duplicate entries. (3) Easy Auth disabled (correct). (4) Bicep infrastructure sound; serverEnvVars parameter exists but deploy.ps1 only translates Authentication__Enabled. (5) Managed Identity properly configured. Next: await Bender's OAuth metadata fix, then deploy with full auth config template (see .squad/decisions/inbox/amy-container-apps-auth-config.md).
- **Release process (.NET projects):** Cut v0.9.15 release on 2026-05-02. For .NET projects, version is in `PoshMcp.Server/PoshMcp.csproj` `<Version>` element. Release steps: (1) bump version in csproj, (2) commit with "chore: bump version to X.Y.Z" and Copilot co-author trailer, (3) create annotated tag `git tag -a vX.Y.Z -m "vX.Y.Z - release description"`, (4) push with `git push origin main --tags`. Differs from npm-based workflow: no draft releases, no workspace-scoped publish, no pre-flight dependency scans — just semantic versioning in single file + git tag + push.
- **Release v0.9.20 (2026-05-03):** Patch release capturing three authentication fixes (OR semantics for RequiredRoles, JWT claim-type remapping disabled, RequiredScopes format correction, and DoctorReport role claim lookup consistency). Version bumped 0.9.19 → 0.9.20 in PoshMcp.csproj, CHANGELOG.md prepended with 4-bullet release notes documenting each fix. Committed as `b87ca27` with Copilot co-author trailer; tagged `v0.9.20`. Build committed and tagged successfully; 3 commits captured since v0.9.19 (fe1b1bc, fd6d115, 8c8e4ad).
## [2026-04-23T15:08:26] Source Image Implementation

**Session:** Deploy source image support implementation (spec 007)
**Contribution:** Implemented -SourceImage and -UseRegistryCache parameters

**Key Learnings:**
- Parameters added to infrastructure/azure/deploy.ps1
- -SourceImage: specify container source image
- -UseRegistryCache: control registry caching behavior
- Implements parameter validation and integration
- Coordinated with Farnsworth (spec) and Fry (testing)

**Artifacts:** infrastructure/azure/deploy.ps1

## [2026-07-18] Resource Group Default Alignment

**Session:** Fix mismatched `$ResourceGroup` default between deploy scripts and Bicep
**Contribution:** Aligned all three deploy-side files to the canonical value defined in Bicep/parameters

**Key Learnings:**
- **Canonical resource group name is `rg-poshmcp`** — Azure naming convention uses type-prefix-first (e.g., `rg-`, `ca-`, `acr-`). The authoritative source is `infrastructure/azure/main.bicep` and `parameters.json`.

---
*Further trimmed to 100 lines on 2026-05-05 by Scribe (15KB gate). Full record in `history-archive.md`.*

## 2026-05-06 — CodeQL workflow permissions + secret scanning docs
- Added top-level `permissions: contents: read` to `.github/workflows/ci.yml` to resolve CodeQL `actions/missing-workflow-permissions` alert. Build job only restores/builds/tests and uploads artifacts via `actions/upload-artifact@v4` — none of which require write scopes on GITHUB_TOKEN. Future jobs needing more (check runs, PR comments, package publish) should request scopes at job level, not widen the top-level grant.
- Added `Repository Security Controls` section to `SECURITY.md` documenting expected GitHub-native controls: secret scanning, push protection, Dependabot alerts, CodeQL. Cannot toggle these settings from a workflow — admin UI required.



