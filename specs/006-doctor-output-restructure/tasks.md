---

description: "Task list for Doctor Output Restructure"
---

# Tasks: Doctor Output Restructure

**Input**: Design documents from `/specs/doctor-output-restructure/`
**Prerequisites**: plan.md (required), spec.md (required for user stories)

**Tests**: Included — the constitution mandates test-driven quality for every change.

**Organization**: Tasks are grouped by implementation phase. Phases 1–2
are foundational (pure additions, no existing code changes). Phase 3 is
the wiring phase that modifies Program.cs. Phases 4–5 add new content.
Phases 6–8 are integration, testing, and cleanup.

## Format: `[ID] [P?] [Phase] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Phase]**: Which implementation phase from plan.md
- Include exact file paths in descriptions

## Path Conventions

- **Server code**: `PoshMcp.Server/`
- **Tests**: `PoshMcp.Tests/`

---

## Phase 1: DoctorReport Record Hierarchy (data model — pure addition)

**Purpose**: Define the structured data model that both JSON serialization and text rendering will consume. No existing code is modified.

- [ ] T001 [P1] Create `PoshMcp.Server/Diagnostics/DoctorReport.cs` with the top-level `DoctorReport` record containing properties: `Summary` (DoctorSummary), `RuntimeSettings` (RuntimeSettingsSection), `EnvironmentVariables` (EnvironmentVariablesSection), `PowerShell` (PowerShellSection), `FunctionsTools` (FunctionsToolsSection), `McpDefinitions` (McpDefinitionsSection), `Warnings` (List\<string\>). Add `[JsonPropertyName]` attributes for camelCase serialization on every property.

- [ ] T002 [P] [P1] In the same file, define the `ResolvedSetting` record with `Value` (string?) and `Source` (string) properties, both with `[JsonPropertyName]` attributes. This is the reusable building block for every runtime setting.

- [ ] T003 [P] [P1] In the same file, define nested section records: `DoctorSummary` (status, generatedAtUtc, configurationPath), `RuntimeSettingsSection` (configurationPath, transport, logLevel, sessionMode, runtimeMode, mcpPath — all `ResolvedSetting`), `EnvironmentVariablesSection` (a `Dictionary<string, string?>` property), `PowerShellSection` (version, modulePathEntries, modulePaths, oopModulePathEntries, oopModulePaths), `FunctionsToolsSection` (configuredFunctionCount, configuredFunctionsFound, configuredFunctionsMissing, toolCount, toolNames, configuredFunctionStatus), `McpDefinitionsSection` (resources and prompts sub-records with configured, valid, errors, warnings).

- [ ] T004 [P1] Add a static method `ComputeStatus(DoctorReport report) → string` that returns `"errors"` if any function is missing OR any resource/prompt errors exist, `"warnings"` if any warnings exist, `"healthy"` otherwise. This can be a static method on `DoctorReport` or a companion static class.

- [ ] T005 [P] [P1] Add a static factory method `DoctorReport.Build(...)` that accepts the same parameters currently passed to `BuildDoctorJson` (settings, config, tools, diagnostics, etc.) and returns a populated `DoctorReport` instance. This centralizes report construction.

**Checkpoint**: `DoctorReport.cs` compiles. No behaviour change. `dotnet build` passes.

---

## Phase 2: DoctorTextRenderer Static Class (text formatting — pure addition)

**Purpose**: Create the text rendering engine. No existing code is modified.

- [ ] T006 [P2] Create `PoshMcp.Server/Diagnostics/DoctorTextRenderer.cs` with a single public static method `Render(DoctorReport report) → string` that delegates to private section render methods and concatenates results.

- [ ] T007 [P] [P2] Implement private helper methods: `RenderSectionHeader(string name) → string` producing `── Name ──────────────────────` (padded to 42 chars with ─), and `StatusSymbol(bool ok) → string` returning `"✓"` or `"✗"`.

- [ ] T008 [P2] Implement `RenderBanner(DoctorSummary summary) → string` producing the three-line Unicode box banner: `╔═══...═══╗`, `║  PoshMcp Doctor  {symbol} {status}  ║`, `╚═══...═══╝`. The symbol is ✓/⚠/✗ based on status.

- [ ] T009 [P2] Implement section render methods: `RenderRuntimeSettings(RuntimeSettingsSection)`, `RenderEnvironmentVariables(EnvironmentVariablesSection)`, `RenderPowerShell(PowerShellSection)`, `RenderFunctionsTools(FunctionsToolsSection)`, `RenderMcpDefinitions(McpDefinitionsSection)`, `RenderWarnings(List<string>, bool hasLegacyFunctionNames)`. Each returns a string block. Use consistent indentation (2 spaces), align colons for readability, prefix items with ✓/✗/⚠ where appropriate.

**Checkpoint**: `DoctorTextRenderer.cs` compiles. No behaviour change. `dotnet build` passes.

---

## Phase 3: Wire into RunDoctorAsync and BuildDoctorJson (behaviour change)

**Purpose**: Replace the inline Console.WriteLine calls and anonymous JSON object with the new DoctorReport and DoctorTextRenderer.

**⚠️ CRITICAL**: This phase modifies Program.cs. Phases 1 and 2 must be complete.

- [ ] T010 [P3] Refactor `RunDoctorAsync` in `PoshMcp.Server/Program.cs`: after collecting all diagnostic data, call `DoctorReport.Build(...)` to create a report instance. For `format == "json"`, serialize the report with `JsonSerializer.Serialize(report)` and write to console. For text format, call `DoctorTextRenderer.Render(report)` and write to console. Remove all existing inline `Console.WriteLine` calls in the text branch.

- [ ] T011 [P3] Replace the `BuildDoctorJson` method body: instead of constructing an anonymous object, call `DoctorReport.Build(...)` and serialize it. Keep the method signature temporarily for backward compatibility with `CreateConfigurationTroubleshootingToolInstance`, but have it delegate to the new code path.

- [ ] T012 [P3] Verify `dotnet build` compiles cleanly and `dotnet test` passes with the wired-up code. Fix any serialization differences (e.g., property ordering, null handling) that cause test failures.

**Checkpoint**: `poshmcp doctor` produces the new structured text output. `poshmcp doctor --format json` produces the new nested JSON. All tests pass.

---

## Phase 4: Environment Variables Section (new content)

**Purpose**: Add the Environment Variables section that reads the 8 POSHMCP_* and ASPNETCORE_* environment variables.

- [ ] T013 [P4] In `DoctorReport.Build(...)`, populate `EnvironmentVariablesSection` by reading `Environment.GetEnvironmentVariable()` for each of: `POSHMCP_TRANSPORT`, `POSHMCP_CONFIGURATION`, `POSHMCP_MCP_PATH`, `POSHMCP_SESSION_MODE`, `POSHMCP_RUNTIME_MODE`, `POSHMCP_LOG_LEVEL`, `POSHMCP_LOG_FILE`, `ASPNETCORE_ENVIRONMENT`. Store as `Dictionary<string, string?>`.

- [ ] T014 [P4] Verify the environment variables section appears in both text and JSON output by running `poshmcp doctor` manually and inspecting.

**Checkpoint**: Environment variables section is populated in both output formats.

---

## Phase 5: Summary Banner with Health Computation (new content)

**Purpose**: Implement the overall health status computation and wire it into the banner.

- [ ] T015 [P5] In `DoctorReport.Build(...)`, call `ComputeStatus` after all sections are populated and set `Summary.Status` accordingly. Set `Summary.GeneratedAtUtc` to `DateTime.UtcNow` and `Summary.ConfigurationPath` from the resolved settings.

- [ ] T016 [P5] Verify the banner renders correctly for each status: run `poshmcp doctor` with a healthy config (expect ✓), with a missing function (expect ✗ or ⚠), and with a deprecation warning (expect ⚠).

**Checkpoint**: Banner shows correct health status. JSON `summary.status` matches.

---

## Phase 6: Update get-configuration-troubleshooting MCP Tool (integration)

**Purpose**: Ensure the MCP tool returns the same nested JSON as the CLI.

- [ ] T017 [P6] Update `CreateConfigurationTroubleshootingToolInstance` in `PoshMcp.Server/Program.cs` to use `DoctorReport.Build(...)` and serialize the result, replacing the call to `BuildDoctorJson`. Ensure the MCP tool's JSON output structure matches `poshmcp doctor --format json` exactly.

- [ ] T018 [P6] If `BuildDoctorJson` is now only called from this one place and has been fully replaced, remove the old `BuildDoctorJson` method entirely.

**Checkpoint**: `get-configuration-troubleshooting` MCP tool returns nested JSON. Existing integration tests for this tool pass.

---

## Phase 7: Tests (verification)

**Purpose**: Add unit tests for new types and update integration tests for new output format.

### Unit Tests

- [ ] T019 [P] [P7] Create `PoshMcp.Tests/Unit/DoctorReportTests.cs`: test `ComputeStatus` returns `"healthy"` when no issues, `"warnings"` when warnings present, `"errors"` when missing functions or resource errors. Test JSON serialization produces expected top-level keys and camelCase property names.

- [ ] T020 [P] [P7] Create `PoshMcp.Tests/Unit/DoctorTextRendererTests.cs`: test `RenderBanner` produces box-drawing output with correct status symbol. Test `RenderSectionHeader` produces `── Name ──` format. Test `Render` produces output containing all section headers.

- [ ] T021 [P] [P7] Add a test that constructs a `DoctorReport` with all sections populated, serializes to JSON, and verifies the top-level keys are exactly: `summary`, `runtimeSettings`, `environmentVariables`, `powerShell`, `functionsTools`, `mcpDefinitions`, `warnings`.

### Integration Test Updates

- [ ] T022 [P7] Update `PoshMcp.Tests/Unit/ProgramDoctorToolExposureTests.cs` to expect the new nested JSON structure from `BuildDoctorJson` or its replacement. Verify `summary.status` exists and is a valid enum value.

- [ ] T023 [P7] If any existing integration tests in `PoshMcp.Tests/Integration/` assert on doctor text output format (e.g., checking for specific strings), update them to match the new section-header format.

**Checkpoint**: `dotnet test` passes for all three test tiers. No test is skipped or disabled.

---

## Phase 8: Cleanup and Validation (polish)

**Purpose**: Remove dead code, verify formatting, run full validation.

- [ ] T024 [P8] Remove the old inline `Console.WriteLine`-based text rendering from `RunDoctorAsync` if any remnants remain. Remove the old anonymous-object-based `BuildDoctorJson` if not already removed in T018.

- [ ] T025 [P] [P8] Run `dotnet format --verify-no-changes` on the solution. Fix any trailing whitespace or formatting violations introduced by the new files.

- [ ] T026 [P8] Run `dotnet test` one final time from solution root. Verify all tests pass across Unit, Functional, and Integration tiers.

- [ ] T027 [P8] Manually run `poshmcp doctor` and `poshmcp doctor --format json` to visually verify the output matches the spec's design mockups. Confirm no information loss compared to the pre-restructure output.

**Checkpoint**: Feature complete. All tests pass. Code is formatted. Output matches spec.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1** (DoctorReport): No dependencies — can start immediately
- **Phase 2** (DoctorTextRenderer): Depends on Phase 1 (renderer consumes DoctorReport records)
- **Phase 3** (Wiring): Depends on Phase 1 + Phase 2 — BLOCKS all subsequent phases
- **Phase 4** (Env Vars): Depends on Phase 3
- **Phase 5** (Banner): Depends on Phase 3
- **Phase 6** (MCP Tool): Depends on Phase 3
- **Phase 7** (Tests): Depends on Phases 3–6 (tests verify the wired-up behavior)
- **Phase 8** (Cleanup): Depends on Phase 7

### Parallel Opportunities

- T002 and T003 can run in parallel with T001 (different sections of the same file, but logically independent records)
- T005 can run in parallel with T004 (factory method vs. status computation)
- T007 can run in parallel with T006 (helper methods vs. main entry point)
- T019, T020, T021 can all run in parallel (different test files)
- Phases 4, 5, and 6 can all run in parallel after Phase 3 completes
- T025 can run in parallel with T024

### Within Each Phase

- Build verification after each phase (T012, T014, T016, etc.)
- Commit after each phase or logical group of tasks
- Stop at any checkpoint to validate independently

---

## Implementation Strategy

### Recommended Approach: Incremental Delivery

1. **Phase 1 + 2**: Pure additions, zero risk — commit as "Add DoctorReport and DoctorTextRenderer"
2. **Phase 3**: The big swap — commit as "Wire DoctorReport into doctor command"
3. **Phases 4–6**: Independent enhancements — one commit each
4. **Phase 7**: Tests — commit as "Add tests for doctor output restructure"
5. **Phase 8**: Cleanup — commit as "Remove legacy doctor output code"

Each phase can be a separate PR if desired, but Phases 1–3 are most
naturally a single PR since Phase 3 is the behaviour change that depends
on 1 and 2.

---

## Notes

- [P] tasks = different files, no dependencies
- [Phase] label maps task to implementation phase from plan.md
- The `effectivePowerShellConfiguration` blob is intentionally omitted from the new JSON structure (see spec Assumptions) — if needed later, it can be added as a new section without breaking the schema
- Authentication and Logging sections are structurally defined but not populated in this spec — they will be filled by the parallel #137 work using the same `DoctorReport` extension pattern
- Commit after each phase or logical group
- Run `dotnet format` before every commit
