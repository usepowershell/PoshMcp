# Fry Work History

## Recent Work (2026-04-20 — CURRENT SESSION)

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
