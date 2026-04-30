# Fry Work History

## Learnings

### 2026-04-28: Issue #175 — Integration tests for AppInsights graceful degradation

**Branch:** `squad/175-integration-test-appinsights` (worktree `poshmcp-175`)

**New test file:** `PoshMcp.Tests/Integration/ApplicationInsightsIntegrationTests.cs`
- 4 integration tests verifying SC-102, SC-104, SC-105 from Spec 008
- `Server_StartsSuccessfully_WithAppInsightsEnabled_NoConnectionString` — health endpoints respond 200
- `Server_LogsWarning_WhenAppInsightsEnabled_NoConnectionString` — warning about missing connection string appears in captured stderr
- `Server_McpToolsStillRespond_WithAppInsightsEnabled` — MCP initialize + tools/list still works
- `Server_StartsNormally_WithAppInsightsDisabled` — control test, no warning when disabled

**Bug found & fixed:** `appsettings.json` had a duplicate `ApplicationInsights` key (lines 12 and 61), causing `SettingsResolver.MergeMissingProperties` to crash with `ArgumentException: An item with the same key has already been added`. Removed the duplicate at line 61.

**Key patterns:**
- Created `AppInsightsTestHttpServer` helper class (similar to `InProcessUnifiedHttpServer` but accepts `Dictionary<string, string> environmentOverrides`) for passing env vars like `ApplicationInsights__Enabled=true`
- Server warning message to check: `"Application Insights is enabled but no connection string was found"` (from `Program.cs` `ConfigureApplicationInsights`)
- Environment variable `APPLICATIONINSIGHTS_CONNECTION_STRING` must be explicitly removed from the process environment to test the "missing" path
- Tests tagged `[Trait("Category", "Integration")]` for filtering

**Result:** 4/4 passing. Committed `ee3bdf5`, pushed to origin. PR creation blocked by EMU auth — user needs to open PR manually.

## Recent Work (2026-04-20 — CURRENT SESSION)

### 2026-04-28: Unit Tests for ConfigureApplicationInsights (#174)

**Branch:** `squad/174-unit-tests-appinsights` (worktree `poshmcp-174`)
**Status:** Complete — pushed, PR creation blocked by EMU auth

**Test file created:** `PoshMcp.Tests/Unit/ConfigureApplicationInsightsTests.cs`

**6 test cases covering SC-103 to SC-106 from Spec 008:**
1. `Enabled_False_DoesNotRegisterAzureMonitor` — verifies no OTel/AzureMonitor service descriptors when Enabled=false
2. `Enabled_True_WithConnectionString_RegistersAzureMonitor` — verifies OpenTelemetry services registered
3. `Enabled_True_NoConnectionString_LogsWarning_NoCrash` — captures stderr, verifies warning message, no throw, no OTel services
4. `SamplingPercentage_50_SetsSamplingRatio` — verifies stderr info message reports 50% sampling
5. `Enabled_True_ConnectionString_FromEnvVar_RegistersAzureMonitor` — sets/restores env var, verifies OTel registered
6. `Enabled_True_WithConnectionString_AddsLoggerFilterRule` — verifies LoggerFilterOptions configured for OTel suppression

**Result:** 396 total unit tests — 396 passed, 0 failed ✅

**Key patterns:**
- `ConfigureApplicationInsights` is `private static` in `PoshMcp.Program` (namespace `PoshMcp`, not `PoshMcp.Server`)
- Accessed via reflection (`BindingFlags.NonPublic | BindingFlags.Static`)
- `InternalsVisibleTo("PoshMcp.Tests")` is set in csproj but doesn't help with private — reflection required
- Tests use `ServiceCollection` inspection to verify registrations (checking `ServiceType.FullName` for "OpenTelemetry"/"AzureMonitor")
- Env var tests save/restore original value in try/finally blocks
- Stderr capture uses `Console.SetError` with `StringWriter`, restored in finally

### Docker Build Arguments Unit Tests
**Branch:** background→sync  
**Status:** Complete

- **Task (Fry)**: Create comprehensive unit test suite for Docker build arguments
- **Implementation**: Created `PoshMcp.Tests/Unit/DockerRunnerTests.cs` with 11 test cases
- **Coverage**: All PoshMcp Docker build scenarios (minimal config, buildkit, registries, multi-arch, custom paths, ignore patterns, error handling, labels, caching, build args, output format)
- **Outcome**: All 11 tests passing ✅
- **Coordination**: Tests verify Bender's extracted `DockerRunner.BuildDockerBuildArgs` method thoroughly

**Test results summary:**
- 11/11 passing
- Covers argument construction, registry handling, multi-architecture builds, error paths
- Validates build argument ordering and formatting

## Recent Status (2026-07-18: Spec 006 Phase 7 — Doctor Output Tests (T019–T023)

**Branch:** `squad/spec006-doctor-output-restructure` (worktree `poshmcp-spec006`)

**New test files created:**
- `PoshMcp.Tests/Unit/DoctorReportTests.cs` — 14 tests: `ComputeStatus` (healthy/errors/warnings/resource-errors/prompt-errors/resource-warnings), `DoctorSummary` property assertions, JSON top-level key verification (T021 combined), camelCase name assertions, `effectivePowerShellConfiguration` absence check
- `PoshMcp.Tests/Unit/DoctorTextRendererTests.cs` — 14 tests: banner box-drawing chars, status symbols (✓/⚠/✗), section headers (Runtime Settings/Env Vars/PowerShell/Functions-Tools/MCP Definitions), header format validation, conditional Warnings section (present/absent)

**Existing test files updated (T022):**
- `ProgramDoctorConfigCoverageTests.cs` — replaced 12 failing tests: removed `authenticationConfig`/`loggingConfig`/old resource+prompt assertions; added `runtimeSettings`, `summary.status`, `mcpDefinitions.resources`, `mcpDefinitions.prompts`, new text section header checks (`── Environment Variables`, `── Runtime Settings`, `── MCP Definitions`), auth-absent test
- `ProgramDoctorToolExposureTests.cs` — fixed `GetToolNames` to use `functionsTools.toolNames`; removed `effectivePowerShellConfiguration` assertion
- `ProgramConfigurationGuidanceToolExposureTests.cs` — fixed `GetToolNames` to use `functionsTools.toolNames`
- `ProgramTransportSelectionTests.cs` — updated all 8 tests: flat keys (`effectiveTransport`, `effectiveSessionMode`, etc.) → nested (`runtimeSettings.transport.value`, `runtimeSettings.sessionMode.value`, etc.); `PayloadContainsConfiguredModulePath` now checks `powerShell.oopModulePaths`
- `ProgramTests.cs` — fixed `oopModulePaths`/`oopModulePathEntries` → `powerShell.oopModulePaths`/`powerShell.oopModulePathEntries`

**Result:** 527 total — 520 passed, 7 skipped (pre-existing), 0 failed ✅
`dotnet format --verify-no-changes` clean. Commit `f38b9b9` pushed.

**Key patterns established:**
- New JSON shape: all runtime settings under `runtimeSettings.{key}.value` / `.source`
- Tool names under `functionsTools.toolNames`
- OOP module paths under `powerShell.oopModulePaths`/`oopModulePathEntries`
- No `authenticationConfig`, `loggingConfig`, `effectivePowerShellConfiguration` in new JSON
- Text output sections use `── Section Name ──...` headers (44-char padded)

**[Earlier detailed history 2026-04-14 and prior archived to history-archive.md on 2026-04-18 per Scribe threshold policy. Preserving last 90 days (2026-04-21 onwards) in main history.]**



### 2026-04-15: Cross-agent verification pattern for auth behavior

- Bender's resolver improvements should be validated with both precedence-focused tests and docs wording review in the same handoff.
- For configuration behavior, lock exact-match precedence with regression tests and keep docs explicit about recommended key style versus currently accepted key styles.

### 2026-04-18: Spec 002 Final Verification — Full Suite Run on feature/002-tests (rebased)

**Context:** Hermes rebased `feature/002-tests` onto main and removed all 16 Skip attributes from `McpResourcesIntegrationTests` and `McpPromptsIntegrationTests`. Task was to run the full test suite and confirm readiness for merge (PR #128).

**Test run result:** 478 total — 470 passed, 1 failed, 7 skipped (duration ~247s)

**Spec 002 integration tests:** 16/16 pass ✅
- 8 `McpResourcesIntegrationTests`: all passing (resources/list, resources/read, file/command sources, error paths)
- 8 `McpPromptsIntegrationTests`: all passing (prompts/list, prompts/get, file/command sources, argument injection, error paths)
- Zero Skip attributes remain on spec-002 tests

**The one failure (pre-existing, non-blocking):**
- Test: `McpResourcesValidatorTests.Validate_ResourceWithNoMimeType_ReportsMimeTypeWarning`
- Cause: `McpResourceConfiguration.MimeType` defaults to `"text/plain"` at the C# object level. The test creates a resource without setting MimeType, expecting the validator to warn, but the property already carries `"text/plain"` — so `IsNullOrWhiteSpace` is never true.
- Pre-existing since `a2ade16` (original resources implementation). Not introduced by Hermes's rebase.
- Remediation (future, not blocking): change `MimeType` default to `null`/empty and apply `"text/plain"` at runtime.

**Skips (7, all pre-existing):**
- 6 `OutOfProcessModuleTests` — out-of-process mode not yet integrated
- 1 `Functional.ReturnType.GeneratedMethod.ShouldHandleGetChildItemCorrectly` — pre-existing

**Verdict: ✅ CLEAR TO MERGE — PR #128**

### 2026-04-18: Issue #129 MimeType Fix Validation

- Verified that `Validate_ResourceWithNoMimeType_ReportsMimeTypeWarning` was never skipped — it was failing.
- Root cause: `McpResourceConfiguration.MimeType` had C# default `"text/plain"`, so `IsNullOrWhiteSpace` check never fired.
- Once Bender made property nullable (commit `6a93c3d`), validator logic fired correctly and test passed.
- Updated inline comment in test to document nullable behavior; test logic required no changes.
- All 9 validator tests pass; finding drives key learning: failing tests with no Skip attribute often need implementation fixes, not test harness changes.
- **Commit:** `1419a20` on `squad/129-fix-mimetype-nullable` (Coordinator rebased)
- **PR #130** ready for review.

### 2026-07-18: Issue #131 — Stdio logging suppression tests

**Branch:** `squad/131-stdio-logging-to-file`

**Test files created:**
- `PoshMcp.Tests/Unit/StdioLoggingConfigurationTests.cs` — 8 unit tests for `ResolveLogFilePath` resolution priority
- `PoshMcp.Tests/Functional/StdioLoggingTests.cs` — 2 functional tests for stdio logging suppression/file routing

**Unit tests (reflection-based):**
- `ResolveLogFilePath` is `private static` in `Program.cs` so tests use `BindingFlags.NonPublic | BindingFlags.Static` reflection
- Return type `ResolvedSetting` is a private sealed record; properties accessed via reflection too
- Covered: CLI > env var, env var > null, null both = null/default, appsettings fallback, CLI > appsettings, env > appsettings, whitespace CLI falls back to env

**Functional tests:**
- Use `InProcessMcpServer` + `ExternalMcpClient` from `PoshMcp.Tests.Integration` namespace (same project, public classes)
- `WithNoLogFile`: asserts no Serilog `[yyyy-MM-dd HH:mm:ss LVL]` or MEL `info:` lines appear on server stderr
- `WithLogFile`: passes `serve --log-file <path>` as extraArgs; Serilog uses `RollingInterval.Day` so search for `basename*.log`; assert file exists and has content
- All 10 tests (8 unit + 2 functional) pass; total run ~11s

**Testing infrastructure notes:**
- `ImplicitUsings` is disabled in the test project — all `using` statements must be explicit
- `AddInMemoryCollection` available via transitive `Microsoft.Extensions.Configuration` dependency from `PoshMcp.Server`
- `EnvironmentVariableScope` helper pattern (save/restore env var) reused from `ProgramTransportSelectionTests.cs` style

### 2026-07-18: Spec 006 Phase 7 — Doctor Output Tests (T019–T023)

**Branch:** `squad/spec006-doctor-output-restructure` (worktree `poshmcp-spec006`)

**New test files created:**
- `PoshMcp.Tests/Unit/DoctorReportTests.cs` — 14 tests: `ComputeStatus` (healthy/errors/warnings/resource-errors/prompt-errors/resource-warnings), `DoctorSummary` property assertions, JSON top-level key verification (T021 combined), camelCase name assertions, `effectivePowerShellConfiguration` absence check
- `PoshMcp.Tests/Unit/DoctorTextRendererTests.cs` — 14 tests: banner box-drawing chars, status symbols (✓/⚠/✗), section headers (Runtime Settings/Env Vars/PowerShell/Functions-Tools/MCP Definitions), header format validation, conditional Warnings section (present/absent)

**Existing test files updated (T022):**
- `ProgramDoctorConfigCoverageTests.cs` — replaced 12 failing tests: removed `authenticationConfig`/`loggingConfig`/old resource+prompt assertions; added `runtimeSettings`, `summary.status`, `mcpDefinitions.resources`, `mcpDefinitions.prompts`, new text section header checks (`── Environment Variables`, `── Runtime Settings`, `── MCP Definitions`), auth-absent test
- `ProgramDoctorToolExposureTests.cs` — fixed `GetToolNames` to use `functionsTools.toolNames`; removed `effectivePowerShellConfiguration` assertion
- `ProgramConfigurationGuidanceToolExposureTests.cs` — fixed `GetToolNames` to use `functionsTools.toolNames`
- `ProgramTransportSelectionTests.cs` — updated all 8 tests: flat keys (`effectiveTransport`, `effectiveSessionMode`, etc.) → nested (`runtimeSettings.transport.value`, `runtimeSettings.sessionMode.value`, etc.); `PayloadContainsConfiguredModulePath` now checks `powerShell.oopModulePaths`
- `ProgramTests.cs` — fixed `oopModulePaths`/`oopModulePathEntries` → `powerShell.oopModulePaths`/`powerShell.oopModulePathEntries`

**Result:** 527 total — 520 passed, 7 skipped (pre-existing), 0 failed ✅
`dotnet format --verify-no-changes` clean. Commit `f38b9b9` pushed.

**Key patterns established:**
- New JSON shape: all runtime settings under `runtimeSettings.{key}.value` / `.source`
- Tool names under `functionsTools.toolNames`
- OOP module paths under `powerShell.oopModulePaths`/`oopModulePathEntries`
- No `authenticationConfig`, `loggingConfig`, `effectivePowerShellConfiguration` in new JSON
- Text output sections use `── Section Name ──...` headers (44-char padded)



Detailed prior history (2026-03-27 through 2026-04-07) archived to `history-archive.md` when this file exceeded 15 KB threshold on 2026-04-18.

## [2026-04-23T15:08:26] Deploy Source Image Test Tasks

**Session:** Deploy source image support implementation (spec 007)
**Contribution:** Created test tasks checklist for spec 007

**Key Learnings:**
- Test checklist: specs/007-deploy-source-image/tasks.md
- Comprehensive test coverage planning
- Coordinated with Farnsworth (spec) and Amy (implementation)
- Test-driven approach validates implementation

**Artifacts:** specs/007-deploy-source-image/tasks.md

## [2026-04-23] deploy.ps1 precedence automation (CLI vs env vs appsettings)

- Added a script-level integration test that invokes `infrastructure/azure/deploy.ps1` under a mocked PowerShell harness (mocked `az`, `docker`, `poshmcp`, and `Invoke-WebRequest`) so precedence behavior can be validated without live Azure or Docker dependencies.
- Learned that script-level CLI parameter detection was broken because `Initialize-DeploymentConfiguration` was reading `$PSBoundParameters` inside a nested function scope (empty there), which silently made env win over CLI.
- Reliable pattern: capture script invocation-bound parameters once at script scope and reference that captured hashtable in helper functions when precedence logic depends on "CLI was explicitly provided" semantics.
