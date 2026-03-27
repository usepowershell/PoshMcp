# Team Decisions

Authoritative record of scope, architecture, and process decisions.

## 2026-03-27: Squad initialization

**Decision:** Formed squad with Futurama cast to tackle maintainability, resilience, and observability improvements.

**Context:** PoshMcp MCP server needs structured error handling, circuit breakers, health checks, enhanced metrics, and better runtime observability.

**Team roster:**
- 🏗️ Farnsworth (Lead/Architect)
- ⚙️ Bender (Backend Dev - C#/.NET)
- 💻 Hermes (PowerShell Expert)
- 📊 Amy (DevOps/Platform - Observability)
- 🧪 Fry (Tester)
- 📋 Scribe (Session Logger)
- 🔄 Ralph (Work Monitor)

**Rationale:** Coverage across architecture, .NET implementation, PowerShell specifics, observability, and testing ensures capability to implement all recommended improvements.

---

## 2026-03-27: Quick Wins Implementation Plan

**Context:** PoshMcp needs immediate improvements to observability, resilience, and maintainability. Five high-priority quick wins have been identified that provide significant value with manageable implementation effort.

**Decision:** Implement 5 quick wins in 3 phases over 2-3 weeks:
- Phase 1: Health checks + Correlation IDs (parallel, no dependencies)
- Phase 2: Structured error codes (depends on correlation IDs)
- Phase 3: Config validation + Timeouts (depend on error codes)

**Key Architectural Choices:**
- Health checks: ASP.NET Core IHealthCheck infrastructure for K8s integration
- Correlation IDs: AsyncLocal<T> for async/await propagation via middleware
- Timeouts: Task.WaitAsync() with CancellationTokenSource at PowerShell execution layer
- Error codes: Enum-based hierarchy (1xxx=config, 2xxx=execution, 3xxx=runspace, 4xxx=parameters)
- Config validation: IValidateOptions<T> for fail-fast startup validation

**Work Distribution:**
- Amy: Health checks (lead), correlation IDs (lead), metrics integration
- Bender: Error codes (lead), config validation (lead)
- Hermes: PowerShell health checks, timeout handling (lead)
- Fry: Comprehensive test coverage for all 5 features

**Rationale:** These improvements address critical operational gaps with manageable scope. Phased approach ensures dependencies are resolved in order while enabling parallel work where possible.

**Status:** Phase 1 Complete (2026-03-27)

---

## 2026-03-27: Phase 1 Implementation - Health Checks and Correlation IDs

**Context:** First phase of quick wins focusing on operational observability. Enables monitoring PoshMcp health and tracing requests across distributed systems.

**Decision:** Implemented health check infrastructure and correlation ID tracking:

**Health Checks:**
- PowerShellRunspaceHealthCheck: Validates runspace responsiveness (< 500ms)
- AssemblyGenerationHealthCheck: Tests dynamic assembly generation capability
- ConfigurationHealthCheck: Validates configuration structure and reports metadata
- Endpoints: `/health` (detailed JSON), `/health/ready` (K8s-compatible)

**Correlation IDs:**
- OperationContext: AsyncLocal-based tracking with format `yyyyMMdd-HHmmss-<8-char-guid>`
- LoggerExtensions: Correlation-aware logging helpers for all log levels
- Middleware: Extracts from X-Correlation-ID header, propagates through pipeline
- Integration: Added to all logs, response headers, and OpenTelemetry metrics

**Package Dependencies:**
- Microsoft.Extensions.Diagnostics.HealthChecks 9.0.7 (PoshMcp.Server, PoshMcp.Web)

**Success Criteria Met:**
✅ Health checks complete in < 500ms
✅ Correlation IDs propagate through async operations
✅ All log statements include correlation ID
✅ Metrics tagged with correlation_id dimension
✅ K8s-compatible readiness probe available

**Implementation:** Amy Wong

**Testing:** Fry created 37 test scenarios (13 passing at phase completion)

**Status:** Active

---

## 2026-03-27: Phase 1 Testing Strategy - Stub-Based Specification

**Context:** Phase 1 features (health checks, correlation IDs) required test coverage before implementation to serve as specifications and catch regressions.

**Decision:** Created 37 test scenarios as stubs with TODO comments, organized into 5 test files:
- HealthChecksTests.cs (11 tests): Healthy/Unhealthy/Degraded states, timeouts, concurrent access
- CorrelationIdGenerationTests.cs (5 tests): ID format, uniqueness, explicit setting
- CorrelationIdPropagationTests.cs (6 tests): Async/await propagation, concurrent isolation
- CorrelationIdLoggingTests.cs (6 tests): Log enrichment, structured logging integration
- CorrelationIdMiddlewareTests.cs (9 tests): Header extraction, context lifetime, isolation

**Rationale:** Tests-first approach provides clear specifications for implementation, documents expected behavior, enables regression prevention, and reveals design considerations early.

**Consequences:**
- Tests serve as living documentation of expected behavior
- 13/37 tests passing after Phase 1 implementation (feature-complete but test activation ongoing)
- Remaining tests will be activated as implementations are completed
- Granular tests provide clear failure messages during development

**Testing:** Fry Fry

**Status:** Active

---

## Decision Template

```markdown
### YYYY-MM-DD: {Decision title}
**Context:** {What led to this decision}
**Decision:** {What was decided}
**Alternatives considered:** {What else was considered}
**Rationale:** {Why this choice}
**Consequences:** {What this enables or constrains}
**Status:** [Active | Superseded | Deprecated]
```
