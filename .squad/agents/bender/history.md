# Bender Work History

## Recent Work (2026-04-20 — CURRENT SESSION)

### Issue #170: Azure.Monitor.OpenTelemetry.AspNetCore Package
**Branch:** squad/170-azure-monitor-otel-package  
**Status:** Complete
**PR:** https://github.com/usepowershell/PoshMcp/pull/176

- **Task**: Add Azure.Monitor.OpenTelemetry.AspNetCore NuGet package reference to PoshMcp.Server
- **Implementation**: 
  - `dotnet add` installed v1.4.0 with full transitive dependency tree (Azure.Core, Azure.Monitor.OpenTelemetry.Exporter, OpenTelemetry.Instrumentation.Http, etc.)
  - Updated `PoshMcp.csproj` with new `<PackageReference>` entry
- **Validation**: `dotnet build Release` succeeded with 10 warnings (9 pre-existing CS8602 nullable, 1 pre-existing NU1510)
- **Outcome**: Committed and pushed; PR #176 opened for Spec 008 optional Application Insights telemetry export

**Files modified:**
- `PoshMcp.Server/PoshMcp.csproj` — added Azure.Monitor.OpenTelemetry.AspNetCore v1.4.0

**NOTE:** The csproj filename is `PoshMcp.csproj` NOT `PoshMcp.Server.csproj`. Manifest resource names use assembly name prefix, not namespace.

### Docker Build Arguments Extraction and Testing
**Branch:** background→sync  
**Status:** Complete

- **Task (Bender)**: Extracted `DockerRunner.BuildDockerBuildArgs` static method from `Program.cs` build handler
- **Implementation**: Created `PoshMcp.Server/Infrastructure/DockerRunner.cs` with reusable `BuildDockerBuildArgs(string projectPath)` method
- **Outcome**: Delegated build handler → DockerRunner; build passes without errors
- **Coordination**: Fry created comprehensive 11-test unit suite in `PoshMcp.Tests/Unit/DockerRunnerTests.cs`; all tests passing

**Files modified:**
- `PoshMcp.Server/Program.cs` — build handler simplified
- `PoshMcp.Server/Infrastructure/DockerRunner.cs` — new extraction
- Both agents coordinate on isolated, testable Docker build logic

## Recent Status (2026-07-30, PR #167 Review Nits — COMPLETE)

**Summary:** Addressed 3 Farnsworth review nits on PR #167. 520 tests pass, 0 failures. Pushed commit e440ab2.

## Spec 006 PR #167 Review Nits — commit e440ab2

**What changed:**
- **Fix 1** (`Program.cs`): Removed misleading `--json` flag mention from `get-configuration-troubleshooting` MCP tool description. The `--json` flag is for the CLI `doctor` command; the MCP tool always returns structured text. New description: `"...Always returns structured text output."`
- **Fix 2** (`Program.cs`): Added `POSHMCP_LOG_FILE` to `CollectEnvironmentVariables()` canonical list, positioned after `POSHMCP_LOG_LEVEL`. No column width change needed in `DoctorTextRenderer` (35-char column is sufficient).
- **Fix 3** (`Program.cs`): Corrected `POSHMCP_CONFIG` → `POSHMCP_CONFIGURATION` to match `SettingsResolver.cs` constant `ConfigurationEnvVar`. Also updated unit test assertion in `ProgramDoctorConfigCoverageTests.cs` (renamed method from `WithSevenExpectedKeys` to `WithExpectedKeys`).

**Key pattern:**
- When renaming env var keys, always grep tests for the old key name — they'll have hard-coded string assertions that need updating too.

---

## Recent Status (2026-07-29, Phase 8 — COMPLETE)

**Summary:** Spec 006 Phase 8 complete — dead code removed, `dotnet format` clean, 520 tests pass, PR #167 opened.

## Spec 006 Phase 8: Cleanup and Finalization (T024–T027) — commit ef27ef1

**What changed:**
- **T024**: Removed 5 dead methods/fields from `Program.cs`: `_sensitiveKeyPatterns`, `IsSensitiveKey`, `RedactSensitiveConfigValues`, `LoadFlatConfigSection`, `TryLoadResourcesAndPromptsDefinitions`. These were superseded by `DoctorReport.Build()` in Phase 3 and had zero call sites. `-31 lines`.
- **T025**: `dotnet format` applied, `--verify-no-changes` exits 0.
- **T026**: `dotnet test -c Release` → **520 passed, 0 failed, 7 skipped**.
- **T027**: PR #167 opened: https://github.com/usepowershell/PoshMcp/pull/167

**Key pattern:**
- After refactoring to a new model (e.g., `DoctorReport.Build()`), always grep ALL call sites for helper methods from the old path. Private helpers with zero external references are safe to delete.

## Spec 006 Phase 6: MCP Tool Schema Update (T017–T018) — commit 2ed1546

**What changed:**

### `Program.cs` — `CreateConfigurationTroubleshootingToolInstance` (T017)
- Updated `Description` for the `get-configuration-troubleshooting` MCP tool:
  - Old: `"Returns doctor-style configuration diagnostics for the running server"`
  - New: `"Returns doctor-style configuration diagnostics for the running server. Output includes runtime settings, environment variables, PowerShell info, configured functions, and MCP definitions. Outputs structured text by default; pass argument '--json' for machine-readable JSON."`

### `DoctorReport.cs` — `FunctionsToolsSection` (T018)
- Changed `ConfiguredFunctionsFound` from `List<string>` to `int` to match spec JSON shape (`"configuredFunctionsFound": 5`)
- Changed `ConfiguredFunctionsMissing` from `List<string>` to `int` to match spec JSON shape (`"configuredFunctionsMissing": 0`)
- Updated `ComputeStatus`: `ConfiguredFunctionsMissing.Count > 0` → `ConfiguredFunctionsMissing > 0`
- Updated `DoctorReport.Build`: `ConfiguredFunctionsFound = foundFunctions` → `ConfiguredFunctionsFound = foundFunctions.Count` and same for Missing

### `DoctorTextRenderer.cs`
- Updated `RenderFunctionsTools`: `ConfiguredFunctionsMissing.Count == 0` → `ConfiguredFunctionsMissing == 0`
- Updated count display: `ConfiguredFunctionsFound.Count` → `ConfiguredFunctionsFound`

**Why the schema fix:** The spec.md JSON Output Design shows `configuredFunctionsFound` and `configuredFunctionsMissing` as integer counts (e.g., `5` and `0`), not arrays of names. The full name details are already available in `configuredFunctionStatus` entries. Changed to integers to match the spec contract.

**Build:** 0 errors. Pre-existing warnings (NU1903, CS8602 in McpToolFactoryV2.cs) unchanged.

---

## Recent Status (2026-07-29, Phase 4)

**Summary:** Spec 006 Phase 4 complete — canonical env var list and renderer column width aligned to spec.

## Spec 006 Phase 4: Env Vars Section Population (T013–T014) — commit 2fc1b55

**What changed:**

### `Program.cs` — `CollectEnvironmentVariables()`
- Added 3 missing keys: `POSHMCP_FUNCTION_NAMES`, `POSHMCP_COMMAND_NAMES`, `DOTNET_ENVIRONMENT`
- Reordered to match canonical spec order: TRANSPORT → LOG_LEVEL → SESSION_MODE → RUNTIME_MODE → MCP_PATH → CONFIG → FUNCTION_NAMES → COMMAND_NAMES → ASPNETCORE_ENVIRONMENT → DOTNET_ENVIRONMENT
- All values resolved via `Environment.GetEnvironmentVariable(key)` (null if unset)

### `DoctorTextRenderer.cs` — `RenderEnvironmentVariables()`
- Changed key column width from `{key,-30}` to `{key,-35}` to match spec format

**Build:** 0 errors. All pre-existing warnings (NU1903, CS8602) unchanged.

**Canonical env var list (10 keys):**
```
POSHMCP_TRANSPORT
POSHMCP_LOG_LEVEL
POSHMCP_SESSION_MODE
POSHMCP_RUNTIME_MODE
POSHMCP_MCP_PATH
POSHMCP_CONFIG
POSHMCP_FUNCTION_NAMES
POSHMCP_COMMAND_NAMES
ASPNETCORE_ENVIRONMENT
DOTNET_ENVIRONMENT
```

---

**[Earlier history before 2026-04-21 archived to history-archive.md per Scribe threshold policy. Preserving last 90 days in main history.]**

## Recent Work (2026-04-23)

### CLI infra scaffolding with embedded deployment assets
**Status:** Complete

- Added a new `scaffold` CLI command in `Program.cs` with `--project-path|--path|-p` (default current directory), `--force`, and `--format text|json`.
- Implemented `InfrastructureScaffolder.ScaffoldAzureInfrastructureAsync` to extract embedded infrastructure assets into `infra/azure` under the target project.
- Embedded Azure deployment artifacts in `PoshMcp.csproj` (`deploy.ps1`, `validate.ps1`, `main.bicep`, `resources.bicep`, `parameters.json`, `parameters.local.json.template`) so scaffolding works from packaged tool output.
- Added `ProgramCliScaffoldCommandTests` covering successful scaffold and existing-file behavior without force.

**Key pattern:**
- For tool packaging scenarios, embed source artifacts in the server assembly and resolve resource names by suffix to avoid brittle fully-qualified manifest names.

## 2026-04-23 17:21 — appsettings → env var mapping (with Amy)

- Added \ConvertTo-McpServerEnvVars\ to deploy.ps1: walks known PowerShellConfiguration/Authentication keys,
  applies canonical POSHMCP_* names for RuntimeMode/SessionMode, falls through to __-separated names for the rest.
- Added \Resolve-McpAppSettingsFile\: CLI override first, then auto-discovers poshmcp.appsettings.json / appsettings.json in script dir.
- Added \-McpAppSettingsFile\ parameter to deploy.ps1 param block; distinct from \-AppSettingsFile\ (deploy-level settings).
- Skips: Logging, McpResources, secrets, file paths.
- Injects \xtraEnvVars\ into Bicep parameters JSON at deploy time.
- Key file: infrastructure/azure/deploy.ps1

## 2026-04-24 — Build flow defaults to remote GHCR base image

- Changed `poshmcp build` default behavior from local source-image build assumptions to custom-image layering with published base image.
- Added `--source-image` and `--source-tag` build options and defaulted source resolution to `ghcr.io/usepowershell/poshmcp/poshmcp:latest`.
- Updated default Dockerfile selection to `examples/Dockerfile.user` for `--type custom` (now default), while preserving `--type base` for local `Dockerfile` source builds.
- Updated `examples/Dockerfile.user` to support `BASE_IMAGE` and `INSTALL_PS_MODULES` build args so `--modules` remains effective in the new default flow.
- Added/updated tests in `PoshMcp.Tests/Unit/DockerRunnerTests.cs` and `PoshMcp.Tests/Unit/ProgramCliBuildCommandTests.cs` for build arg construction and option/help coverage.

## 2026-04-24 — Issue #169: update-config adds obsolete FunctionNames block

- Reproduced issue locally: running `update-config --runtime-mode out-of-process` against config with only `CommandNames` added an empty legacy `FunctionNames` array.
- Root cause in `ConfigurationFileManager.UpdateConfigurationFileAsync`: legacy function array was always created via `GetOrCreateArray(powerShellConfiguration, "FunctionNames")` even when no `--add-function/--remove-function` flags were used.
- Fix: only create/update `FunctionNames` when legacy function updates are explicitly requested or the property already exists.
- Added regression test in `ProgramCliConfigCommandsTests` to ensure runtime-mode updates do not introduce `FunctionNames` when absent.
- Validation:
  - `dotnet test PoshMcp.Tests/PoshMcp.Tests.csproj --filter "FullyQualifiedName~ProgramCliConfigCommandsTests"` => 16 passed.
  - `dotnet build PoshMcp.Server/PoshMcp.csproj` => build succeeded (existing warnings unchanged).

## 2026-04-24 — CommandOverrides rename with FunctionOverrides compatibility

- Updated configuration nomenclature from `FunctionOverrides` to `CommandOverrides` across runtime access, update-config advanced prompt writes, appsettings templates/examples, and user-facing docs.
- Added compatibility path in `PowerShellConfiguration`: legacy `FunctionOverrides` still binds and is merged via `GetEffectiveCommandOverrides()` while `CommandOverrides` takes precedence.
- Updated runtime consumers (`AuthorizationHelpers`, `PowerShellAssemblyGenerator`, `ConfigurationHealthCheck`) to resolve overrides through command-first helpers.
- Enhanced `update-config` advanced prompts to write `CommandOverrides` and migrate existing `FunctionOverrides` in-place when the command touches overrides.
- Added/updated focused tests:
  - `ProgramCliConfigCommandsTests`: assert `CommandOverrides` output and migration from legacy key.
  - `PerformanceConfigurationTests`: binding compatibility coverage for legacy and precedence behavior.
  - `ProgramTests` + `AuthorizationHelpersTests`: primary usage now points to `CommandOverrides`.
- Validation:
  - `dotnet build PoshMcp.Server/PoshMcp.csproj -p:UseSharedCompilation=false` => succeeded.
  - Targeted unit tests (`ProgramCliConfigCommandsTests`, `PerformanceConfigurationTests`, `AuthorizationHelpersTests`, `ProgramTests`) => 73 passed.

## Learnings

### Version in doctor output (2026-05-01)

- `AssemblyInformationalVersionAttribute` preserves the full semver string (including `+{commit-hash}` suffix added by the .NET SDK). Strip the suffix with `raw[..raw.IndexOf('+')]` to expose a clean `0.9.2` string.
- `.NET SDK` sets `InformationalVersion` from `<Version>` in the csproj — no manual attribute needed.
- `GetEntryAssembly()` can return null in test contexts; `typeof(DoctorReport).Assembly` is safer and always resolves the correct assembly.
- `DoctorSummary.Version` defaults to `string.Empty` — tests that build minimal reports without setting `Version` still pass; banner renders `PoshMcp v  ✓ healthy` in test but the substring checks (`✓ healthy` etc.) still match.
- **Files modified:** `PoshMcp.Server/Diagnostics/DoctorReport.cs`, `PoshMcp.Server/Diagnostics/DoctorTextRenderer.cs`

### Authentication IOptions bypass fix (2026-05-01)

- **Root cause pattern:** Calling `.Get<T>()` on a config section for local decision-making does NOT register `IOptions<T>` in DI. These are two independent operations. Always pair with `services.Configure<T>(section)` when any downstream consumer uses `IOptions<T>`.
- **Security implication:** If an early-return guard sits before `services.Configure<>()`, the DI options object always resolves to the default value — in this case `Enabled = false` — regardless of appsettings. Middleware and authorization policy gates that read `IOptions<AuthenticationConfiguration>.Value.Enabled` will always see `false`.
- **Rule:** Register `services.Configure<T>()` unconditionally (before any feature-enabled guard) so the real configured value is always available to downstream consumers via DI.
- **Files modified:** `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs`

- `install-modules.ps1` is now bundled in the base image at `/app/install-modules.ps1`; `examples/Dockerfile.user` updated to use it directly.
- Added PSModule path documentation to examples/Dockerfile.user — AllUsers=/usr/local/share/powershell/Modules, built-in=/opt/microsoft/powershell/7/Modules, CurrentUser(runtime)=/home/appuser/.local/share/powershell/Modules
- Added commented COPY directive examples to examples/Dockerfile.user for local module installation (single module + bulk copy patterns)

### ConfigureApplicationInsights pattern (2026-04-27)

- `ApplicationInsightsOptions` must be in `PoshMcp.Server` namespace; Program.cs is in `PoshMcp` namespace — use fully-qualified `PoshMcp.Server.ApplicationInsightsOptions` in the method or add a using.
- `ConfigureApplicationInsights(IServiceCollection, IConfiguration, bool)` must be called AFTER `ConfigureOpenTelemetry` / `ConfigureOpenTelemetryForHttp` in both paths (stdio and HTTP), so OpenTelemetry is already wired before Azure Monitor enriches it.
- `UseAzureMonitor` chaining with `.ConfigureResource(...)` works cleanly on the same `OpenTelemetryBuilder` returned by `services.AddOpenTelemetry()`.
- `SamplingRatio` is a float 0–1; divide `SamplingPercentage` by 100.0f — don't forget the float suffix.
- `Math.Clamp(value, 1, 100)` guards the percentage before converting to ratio, preventing 0% or >100% from reaching Azure Monitor SDK.
- When `Enabled: false` (the default), zero code runs — the guard at the top of the method is all that's needed for zero overhead.


### Doctor AppInsights validation (2026-04-28)

- `BuildConfigurationWarnings` now returns `(List<string> Warnings, List<string> Errors)` tuple and takes `string configPath` to load `ApplicationInsights` settings offline.
- Added `ConfigurationErrors` property to `DoctorReport` at the top level — errors are separate from warnings so `ComputeStatus` can return `"errors"` when config problems are hard blockers (e.g., missing connection string).
- Connection string validation: must start with `InstrumentationKey=` or `https://` — matches the patterns accepted by Azure Monitor SDK.
- `SamplingPercentage` outside 1–100 is a warning (not error) because the runtime already `Math.Clamp`s it.
- When `Enabled: false`, ALL App Insights validation is skipped entirely — no warnings, no errors.
- `DoctorTextRenderer` renders `ConfigurationErrors` with `✖` prefix (same as MCP definition errors).
- Key files: `Program.cs` (`BuildConfigurationWarnings`), `DoctorReport.cs` (`ConfigurationErrors`, `ComputeStatus`), `DoctorTextRenderer.cs`.

### Embedding Dockerfiles in the assembly (2026-07-30)

**Pattern:** To ship static files (Dockerfiles, templates) inside a dotnet global tool so they work without disk presence:

1. Add `<EmbeddedResource>` entries in `.csproj` with `Link` paths using backslash separators to control the manifest resource name:
   ```xml
   <EmbeddedResource Include="..\Dockerfile" Link="Dockerfiles\Dockerfile" />
   ```

2. The manifest name is: `{AssemblyName}.{Link path with backslashes replaced by dots}`.  
   **Important:** The prefix is the *assembly name* (`<AssemblyName>` or project name), not the namespace. For this project, the assembly is `PoshMcp`, so the resource is `PoshMcp.Dockerfiles.Dockerfile` — NOT `PoshMcp.Server.Dockerfiles.Dockerfile`.

3. Read via `Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)`.

4. When the resource isn't found (e.g., file wasn't embedded, or path was custom), fall back to `File.ReadAllText()` so local dev still works.

5. Skip disk-existence checks (`File.Exists`) for paths that are satisfied by embedded resources — in this case the `--generate-dockerfile` flow.

### `--generate-dockerfile` default corrected to "custom" (fixed current session)

**What was wrong:** The `build` command handler had:

```csharp
var buildType = string.IsNullOrWhiteSpace(type)
    ? (generateDockerfile ? "base" : "custom")
    : type.ToLowerInvariant();
```

This meant `poshmcp build --generate-dockerfile` defaulted to `buildType = "base"`, which maps
to the repo root `Dockerfile` — the file for building PoshMcp from source. That is the wrong
template for users; they want `examples/Dockerfile.user`, which extends the published base image.

**How it was fixed:** Both paths (with and without `--generate-dockerfile`) now default to `"custom"`:

```csharp
var buildType = string.IsNullOrWhiteSpace(type)
    ? "custom"
    : type.ToLowerInvariant();
```

Users who explicitly want the source-build Dockerfile can still pass `--type base`.

**Also updated:** `examples/Dockerfile.user` — clarified that `install-modules.ps1` must be
downloaded from the repo, and that the `COPY appsettings.json` line is a placeholder the user
should update to their own path (removed the repo-internal `examples/appsettings.basic.json` path).

- Added --appsettings to poshmcp build: injects COPY line into generated Dockerfile; for build mode stages file to CWD as poshmcp-appsettings.json, uses temp Dockerfile (.poshmcp-build.dockerfile), cleans up both temp files after build
- Fixed poshmcp build 'Dockerfile not found' — embedded resources bypass the disk check; always generate temp dockerfile from embedded resource so build works outside the poshmcp repo
