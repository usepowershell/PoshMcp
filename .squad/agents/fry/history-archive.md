# Fry Work History — Archive

Entries summarized and archived from main history on 2026-04-18 when file size exceeded 15 KB threshold.

## Archived Learnings (Pre-2026-04-08)

### Phase 1 Specification-Driven Testing (2026-03-27)

Created 37 comprehensive test scenarios for Phase 1 Quick Wins (health checks and correlation IDs) ahead of Amy's implementation:
- **Health Checks:** 11 tests validating Healthy/Unhealthy/Degraded states, <500ms requirement
- **Correlation IDs:** 28 tests covering generation, async propagation (AsyncLocal<T>), logging, middleware integration, isolation
- Tests serve as both specification and regression guard
- Stub pattern with TODO comments for unimplemented features
- All tests follow PowerShellTestBase/xUnit patterns

**Key outcome:** Amy's implementation passed 13/37 tests initially; architecture validated testability requirements.

### Out-of-Process Execution Unit Tests (2026-04-11)

Created 44 tests for OOP stub types before implementation (Issue #61):
- `RuntimeModeTests` — Enum parsing, case sensitivity
- `RemoteToolSchemaTests` — DTO defaults, JSON round-trips
- `OutOfProcessCommandExecutorTests` — Lifecycle assertions
- `OutOfProcessToolAssemblyGeneratorTests` — Assembly generation interface

Build blocked until Bender delivered stubs; tests designed for easy update when implementations arrived.

### Phase 1 Completion Validation (2026-03-27)

After Amy's implementation:
- 13/37 tests passing (health checks + correlation ID core complete)
- Async propagation tests confirmed AsyncLocal<T> behavior
- Performance tests validated <500ms requirement
- **Key learning:** Stub-based spec approach successful — Amy used tests as requirements directly

### Serialization Migration Investigation (2026-04-08 batch)

Investigated string serialization regression after `ConvertTo-Json` → `System.Text.Json` migration:
- Added `PowerShellJsonSerializationTests.cs` for focused regression coverage
- Found: `PSObject`-wrapped strings serialize as objects with PowerShell properties (e.g., `Length`)
- **Key learning:** Serializer-level tests are narrowest, most stable regression guardrail
- Created unit test to prevent future regressions

### Integration Runtime Analysis (2026-04-09)

Investigated user concern about slow tests and potential process leaks:
- Profiled integration tests: ~16.7s for 7 tests, top durations 3.8-3.9s each (startup + first invoke)
- Added lifecycle tests: `WebServerProcessLifecycleTests`, `McpServerProcessLifecycleTests`
- **Key finding:** Slowdown is startup cost, not leaked processes
- Added `InProcessWebServer.GetServerProcess()` for direct lifecycle assertions

### Out-of-Process Executor Validation (2026-04-10)

Activated real OOP executor tests against `integration/Modules` corpus:
- Tests interact with `poshmcp-host.ps1` for real subprocess execution
- Covers happy-path execution, subprocess error handling, module discovery
- Extended doctor transport-selection coverage
- **Key learning:** Compile-backed focused tests are regression anchors; full MCP server startup tests still scaffolded pending `--runtime-mode` CLI support

## Summary

Fry's testing approach emphasizes specification-first design, focused regression coverage, and cross-agent learning propagation. Key patterns: stub-based specs, lifecycle testing, serialization regression guards, and performance-aware integration test structure.
