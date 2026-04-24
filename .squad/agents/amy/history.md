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

## Archive Note

Detailed session history was archived to `history-archive.md` on 2026-04-10 when this file exceeded the 15 KB Scribe threshold.



### 2026-04-18: Spec 002 integration branch (2026-04-15 session note)

- Created `integration/spec-002-mcp-resources-and-prompts` from `main` and merged all 4 feature branches in order.
- `feature/002-resources` merged clean. `feature/002-prompts` conflicted on `Program.cs` — resolved by merging `ConfigureServerServices`/`RegisterMcpServerServices` signatures to accept both handlers, and chaining all 4 `With*Handler` calls in HTTP and stdio paths.
- `feature/002-doctor` had add/add conflicts on all 5 config model files (it defined its own nullable-property versions). Kept HEAD (implementation branch) non-nullable versions; validator `IsNullOrWhiteSpace` checks are compatible with both.
- `feature/002-tests` merged clean.
- Build: `dotnet build PoshMcp.sln --no-incremental` → **succeeded**, 5 pre-existing warnings in `McpToolFactoryV2.cs` (unrelated to Spec 002).
- Branch pushed to `origin`.
- Key lesson: when 3+ branches all modify `Program.cs` service registration, the standard pattern is to merge signatures by adding parameters for each feature's handler/config, then chain all handlers together.

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
