# Fry Work History

## Project Context

**Project:** PoshMcp - Model Context Protocol (MCP) server for PowerShell
**Tech Stack:** .NET 8, C#, PowerShell SDK, OpenTelemetry, ASP.NET Core, xUnit
**Primary User:** Steven Murawski

**Test Structure:**
- 53 total tests across unit, functional, and integration levels
- `PoshMcp.Tests/Unit/` - 3 files
- `PoshMcp.Tests/Functional/` - 6 files
- `PoshMcp.Tests/Integration/` - 4 files
- `PoshMcp.Tests/Shared/` - Test infrastructure (PowerShellTestBase, TestOutputLogger)

**Key Test Files:**
- `IntegrationTests.cs` - Full workflow tests
- `McpServerIntegrationTests.cs` - MCP protocol tests
- `SimpleAssemblyTests.cs`, `ParameterTypeTests.cs`, `OutputTypeTests.cs` - Unit tests

## Learnings

### 2026-03-27: Phase 1 Test Scenarios Created

**Context:** Created comprehensive test structure for Phase 1 Quick Wins (health checks and correlation IDs). Amy will implement these features, so tests serve as specifications and will be updated as implementation progresses.

**Test Files Created:**
1. `PoshMcp.Tests/Unit/HealthChecksTests.cs` - 11 test scenarios for health check implementation
2. `PoshMcp.Tests/Functional/CorrelationId/CorrelationIdGenerationTests.cs` - 5 test scenarios for ID generation
3. `PoshMcp.Tests/Functional/CorrelationId/CorrelationIdPropagationTests.cs` - 6 test scenarios for async propagation
4. `PoshMcp.Tests/Functional/CorrelationId/CorrelationIdLoggingTests.cs` - 6 test scenarios for logging integration
5. `PoshMcp.Tests/Functional/CorrelationId/CorrelationIdMiddlewareTests.cs` - 9 test scenarios for middleware

**Total Test Scenarios:** 37 tests covering happy paths, error conditions, edge cases, and integration points

**Key Testing Strategies:**
- **Stub pattern:** All tests are initially stubs with TODO comments, since implementations don't exist yet
- **Documentation-first:** Each test clearly documents expected behavior, serving as specification
- **Comprehensive coverage:** Tests cover Healthy/Unhealthy/Degraded states for health checks
- **Async propagation:** Extensive tests for AsyncLocal<T> behavior across async boundaries
- **Performance validation:** Tests include <500ms health check requirement for K8s probes
- **Isolation testing:** Validates correlation IDs don't leak between concurrent operations

**Design Decisions:**
- Health check tests validate all three status types: Healthy, Unhealthy, Degraded
- Correlation ID tests emphasize async propagation (critical for async/await codebase)
- Tests follow existing project patterns (extends PowerShellTestBase, uses xUnit)
- Organized functionally: Unit tests for logic, Functional tests for feature scenarios
- Each test includes detailed comments explaining what it validates and why

**Integration with Existing Tests:**
- Follows PowerShellTestBase pattern for consistent logging and infrastructure
- Uses same xUnit testing framework as rest of project
- Organized into Unit/ and Functional/ to match existing structure

**Next Steps:**
- Amy implements health check infrastructure → I update HealthChecksTests.cs to use real implementations
- Amy implements correlation ID infrastructure → I update CorrelationId tests to use real implementations
- Add integration tests once both features are complete
- Performance baseline tests after implementation to validate <500ms requirement

---

### 2026-03-27: Phase 1 Completion - Testing Validation and Learnings

**Test Activation Results:**
- 13/37 tests passing after Amy's Phase 1 implementation
- Core functionality validated: health checks, correlation ID generation and propagation
- Remaining tests awaiting full integration (middleware, advanced scenarios)

**Cross-Team Learnings:**

**From Farnsworth's Architecture:**
- 3-phase plan enabled clear sequencing - tests could target Phase 1 without Phase 2/3 dependencies
- Parallel work strategy (health checks + correlation IDs) validated by independent test files
- Architectural choices (AsyncLocal, IHealthCheck) aligned with testability requirements
- Phase boundaries clear enough to design isolated test scenarios

**From Amy's Implementation:**
- AsyncLocal<T> implementation matches test expectations for async propagation
- Health checks complete in < 500ms (performance test requirement met)
- Correlation ID format `yyyyMMdd-HHmmss-<8-char-guid>` enables sortability testing
- Middleware integration cleaner than anticipated - simplifies middleware test scenarios
- OpenTelemetry integration (correlation_id dimension) provides additional validation surface

**Testing Approach Validation:**
- Stub-based specification approach successful - Amy used tests as requirements
- 37 granular tests better than fewer comprehensive tests (clear failure messages)
- AsyncLocal propagation testing (6 scenarios) critical for async-heavy codebase
- Performance requirements in tests (< 500ms) enforced as constraints, not just documentation

**Identified Gaps:**
- Correlation ID not yet in PowerShell script execution context (test scenario to add)
- Health check timeout handling may need explicit tests (Amy noted as limitation)
- Integration tests for end-to-end scenarios still needed (health → correlation → metrics)

**Application to Phase 2:**
- Continue stub-based specification approach for error codes and timeouts
- Add error code as metric dimension (pattern from correlation_id success)
- Include timeout behavior edge cases from Phase 1 planning
- Design tests for error code propagation through correlation ID infrastructure

---

### 2026-04-08: Serialization migration triage for web and MCP responses

**Context:** Investigated the current failures after replacing PowerShell `ConvertTo-Json` output with `System.Text.Json` serialization in the server and web app.

**What failed:**
- `WebServerWithHttpClient.ShouldExecutePowerShellCommand` returns `[{"Length":22}]` instead of the expected string payload.
- `ServerWithExternalClient.ShouldExecutePowerShellCommand` shows the same regression with `[{"Length":49}]`, confirming this is shared serialization behavior and not web-only.
- Two web integration failures are startup noise caused by `dotnet run` rebuilding while `PoshMcp.exe` is file-locked by another running process; that issue is separate from the serialization regression.

**Key learning:**
- `PSObject` values that wrap scalar strings are currently being serialized as objects with PowerShell-adapted properties like `Length` instead of JSON strings.
- Existing coverage only catches this through broader integration tests, so a focused unit test around `PowerShellJsonOptions` is needed to validate Bender's eventual fix without depending on process startup.

**Tester action:**
- Added a narrow unit regression test in `PoshMcp.Tests/Unit/PowerShellJsonSerializationTests.cs` that asserts a `PSObject`-wrapped string serializes as `["FromUnitTest"]`.

---

### 2026-04-08: Serialization migration web-failure validation batch logged

**Context:** Scribe recorded a new batch focused on reproducing and validating targeted web failures after the serialization migration. The spawn manifest assigned Fry to the validation side of the investigation.

**Shared Team Update:**
- Keep targeted validation aligned with serialization-related regressions in `PoshMcp.Web`
- Future test handoffs should include explicit result summaries so Scribe can log validated outcomes, not just assigned scope
- Team directive now requires `dotnet format` and `dotnet test` after code changes

### 2026-04-08: String serialization regression coverage recorded

**Context:** Logged the focused regression coverage decisions that accompanied the serialization fixes.

**Key learnings:**
- Serializer-level tests are the narrowest stable guardrail for the string regression
- Execution-plus-cache assertions are needed to catch public response-shape regressions that pure JSON-validity checks miss
- Web integration tests remain confirmation coverage, but they should not be the only regression anchor while harness issues are being isolated

### 2026-04-09: Integration runtime and process-leak analysis for server harnesses

**Context:** Investigated user concern that slower tests were caused by leaked web server processes.

**Evidence gathered:**
- Focused integration subset (`WebServerWithHttpClient` + `ServerWithExternalClient`) took ~16.7s for 7 tests, with two tests consuming ~3.8-3.9s each due to server startup and first tool invocation overhead.
- Full integration filter run took ~16.9s for 24 tests, with top durations: `ServerWithExternalClient.ShouldExecutePowerShellCommand` (3.93s), `MultiUserIsolationTests.TwoClientsGetSeparatePowerShellRunspaces` (3.85s), `WebServerWithHttpClient.ShouldExecutePowerShellCommand` (3.84s).
- Before/after process snapshot around focused run reported `BEFORE_COUNT=0` and `AFTER_COUNT=0` for processes matching `PoshMcp.Web`/`PoshMcp.Server` command lines.

**Testing changes made:**
- Added `WebServerProcessLifecycleTests.InProcessWebServer_Dispose_ShouldTerminateServerProcess`.
- Added `McpServerProcessLifecycleTests.InProcessMcpServer_Dispose_ShouldTerminateServerProcess`.
- Added `InProcessWebServer.GetServerProcess()` to support direct lifecycle assertion.

**Key learning:**
- Current slowdown is primarily startup + command execution cost in integration harnesses, not observed lingering parent server processes after dispose.

### 2026-04-10: Recovery validation learnings for doctor and out-of-process work

**Key learnings:**
- The public doctor JSON payload is the right narrow contract for gating tests on troubleshooting-tool exposure.
- Startup-order tests are an effective regression guard for module import/discovery sequencing without requiring full server startup.
- Out-of-process end-to-end coverage should remain scaffold-only until the executable and shared harness expose the runtime mode under test.
- Validation on recovery branches should prefer compile-safe, high-signal tests over speculative integration activation.

### 2026-04-10: Out-of-process executor coverage activated against real host script

**Context:** Converted the executor-side out-of-process tests from TODO stubs into real tests that start `poshmcp-host.ps1`, execute commands, and perform module discovery against the checked-in `integration/Modules` corpus.

**What changed:**
- Added real `OutOfProcessCommandExecutorTests` coverage for happy-path execution, subprocess error handling, null-parameter behavior, and Az.Accounts discovery.
- Extended doctor transport-selection coverage to include session mode and MCP path source precedence.
- Confirmed `integration/Modules` is deliberate test input by forwarding `PowerShellConfiguration.Environment.ModulePaths` into out-of-process discovery.

**Validation:**
- Compile-backed focused suite passed: Program tests, transport-selection tests, McpToolFactory tests, host script tests, local module loading tests, out-of-process module tests, and executor tests.

**Remaining gap:**
- Full MCP server startup tests for out-of-process mode are still scaffolded because `Program.cs` and the in-process server harness do not yet expose a `--runtime-mode` integration path.


