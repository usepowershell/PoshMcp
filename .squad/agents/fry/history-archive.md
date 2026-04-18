# Fry History Archive

Entries archived on 2026-04-18. Preserved for reference.


Detailed prior history (2026-03-27 through 2026-04-07) archived to `history-archive.md` when this file exceeded 15 KB threshold on 2026-04-18.

### 2026-04-15: Cross-agent verification pattern for auth behavior

- Bender's resolver improvements should be validated with both precedence-focused tests and docs wording review in the same handoff.
- For configuration behavior, lock exact-match precedence with regression tests and keep docs explicit about recommended key style versus currently accepted key styles.

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

### 2026-04-11: OOP Phase 6 Unit Tests Created (Issue #61)

**Context:** Created unit tests for out-of-process execution stub types. Written ahead of the stubs (Bender's task) — tests won't compile until `PoshMcp.Server/PowerShell/OutOfProcess/` types exist.

**Test Files Created:**
1. `PoshMcp.Tests/Unit/OutOfProcess/RuntimeModeTests.cs` — 10 tests: enum values exist, Parse round-trips, TryParse valid/invalid, case sensitivity, value count
2. `PoshMcp.Tests/Unit/OutOfProcess/RemoteToolSchemaTests.cs` — 14 tests: DTO defaults (Name, Description, Parameters, ParameterSetName), property setting, RemoteParameterSchema defaults (TypeName="System.String", IsMandatory=false, Position=int.MaxValue), JSON serialization round-trips via Newtonsoft
3. `PoshMcp.Tests/Unit/OutOfProcess/OutOfProcessCommandExecutorTests.cs` — 12 tests: constructor with logger, StartAsync/DiscoverCommandsAsync/InvokeAsync all throw NotImplementedException, DisposeAsync doesn't throw (called once and twice), implements ICommandExecutor and IAsyncDisposable
4. `PoshMcp.Tests/Unit/OutOfProcess/OutOfProcessToolAssemblyGeneratorTests.cs` — 8 tests: constructor with mock ICommandExecutor, GenerateAssembly/GetGeneratedInstance/GetGeneratedMethods throw NotImplementedException, ClearCache doesn't throw (called once and twice)

**Total new test count:** 44 tests

**Key Patterns:**
- Used `NullLogger<T>.Instance` from Microsoft.Extensions.Logging.Abstractions for logger params
- Used Moq for `ICommandExecutor` mock (already in test .csproj)
- Namespace: `PoshMcp.Tests.Unit.OutOfProcess`
- Tests are designed for stubs — will need updates when real implementations arrive
- Build blocked by missing server stubs (13 CS0234/CS0246 errors in PoshMcp.Server, not in test code)

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

### 2026-04-14: Auth override key matching docs must reflect actual candidate order

**Context:** Reviewed the auth resolution change for `FunctionOverrides` mapping and verified docs against implementation and tests.

**Key learning:**
- `AuthorizationHelpers.GetToolOverride` checks candidates in this order: exact tool name, normalized hyphen form, configured command-name matches from snake_case parsing, then truncated fallbacks.
- Docs that say generated MCP tool names are not valid keys are inaccurate; generated tool-name keys are currently supported and can take precedence when both generated and command-name keys are present.
- Recommended docs wording: command-name keys are preferred for durable configuration, while exact generated names are still honored by the current resolver.

**Coverage update:**
- Added `GetToolOverride_PrefersExactToolNameOverride_BeforeCommandNameResolution` in `PoshMcp.Tests/Unit/AuthorizationHelpersTests.cs` to lock current precedence behavior.

---

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

---

### 2026-04-18: Issue #129 MimeType Fix Validation

- Verified that `Validate_ResourceWithNoMimeType_ReportsMimeTypeWarning` was never skipped — it was failing.
- Root cause: `McpResourceConfiguration.MimeType` had C# default `"text/plain"`, so `IsNullOrWhiteSpace` check never fired.
- Once Bender made property nullable (commit `6a93c3d`), validator logic fired correctly and test passed.
- Updated inline comment in test to document nullable behavior; test logic required no changes.
- All 9 validator tests pass; finding drives key learning: failing tests with no Skip attribute often need implementation fixes, not test harness changes.
- **Commit:** `1419a20` on `squad/129-fix-mimetype-nullable` (Coordinator rebased)
- **PR #130** ready for review.


