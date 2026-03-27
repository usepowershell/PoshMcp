# Farnsworth Work History

## Project Context

**Project:** PoshMcp - Model Context Protocol (MCP) server for PowerShell
**Tech Stack:** .NET 8, C#, PowerShell SDK, OpenTelemetry, ASP.NET Core, xUnit
**Primary User:** Steven Murawski

**Project Description:**
PoshMcp dynamically transforms PowerShell scripts, cmdlets, and modules into secure, discoverable, AI-consumable tools via the Model Context Protocol. It features persistent PowerShell runspaces, dynamic tool discovery, multi-user isolation (web mode), and OpenTelemetry metrics.

**Current Priorities:**
- Improve maintainability (structured errors, config validation)
- Enhance resilience (circuit breakers, timeouts, retry logic)
- Boost observability (metrics, health checks, diagnostics)

## Learnings

### 2026-03-27: Quick Wins Implementation Plan

**Task:** Created implementation plan for 5 high-priority observability and resilience improvements

**Architectural Decisions Made:**

1. **Health Checks:** Use ASP.NET Core built-in health check infrastructure rather than custom protocol. Provides Kubernetes integration and battle-tested patterns.

2. **Timeouts:** Implement at PowerShell execution layer using `Task.WaitAsync()` with CancellationTokenSource. Configuration-driven with defaults: 5min default, 30min max.

3. **Correlation IDs:** Use `AsyncLocal<T>` for propagation across async boundaries. Generated at request entry (middleware), included in all logs and response headers.

4. **Error Codes:** Define enum-based error code hierarchy with categories (1xxx=config, 2xxx=execution, 3xxx=runspace, 4xxx=parameters). All exceptions inherit from `McpException` base.

5. **Configuration Validation:** Use `IValidateOptions<T>` for fail-fast startup validation. Validates timeouts, regex patterns, and logical consistency.

**Implementation Sequencing:**
- **Phase 1 (Week 1):** Health checks + Correlation IDs (parallel, no dependencies)
- **Phase 2 (Week 2):** Structured error codes (depends on correlation IDs)
- **Phase 3 (Week 2-3):** Config validation + Timeouts (depend on error codes)

**Work Distribution:**
- **Amy:** Health checks (lead), correlation IDs (lead), metrics for all features
- **Bender:** Error codes (lead), config validation (lead), integration support
- **Hermes:** PowerShell health checks, timeout handling (lead), PowerShell error mapping
- **Fry:** Comprehensive test coverage for all 5 quick wins

**Key Insights:**
- Correlation IDs must come before error codes to enable proper error tracking
- Health checks are Web-only, but other improvements apply to both stdio and HTTP modes
- Some features already partially exist (CancellationToken usage, metrics infrastructure) - leverage existing patterns
- Timeline: 2-3 weeks with parallel work where possible

**Cross-Cutting Patterns Established:**
- All exceptions include correlation IDs
- All metrics tagged with operation type
- Structured logging throughout (no string interpolation)
- Fail-fast principle for configuration errors

**Decision Record:** Created at `.squad/decisions/inbox/farnsworth-quick-wins-plan.md`

---

### 2026-03-27: Phase 1 Completion - Validation of Architectural Decisions

**Cross-Team Learning:**

**From Amy's Implementation:**
- AsyncLocal<T> choice validated - proper async/await propagation confirmed
- Health check package needs to be in both Server (definition) and Web (registration) projects
- Correlation IDs valuable in metrics as well as logs (added as OpenTelemetry dimension)
- Middleware integration cleanly separates concerns (extraction, generation, propagation)
- < 500ms health check target achieved with simple validation commands

**From Fry's Testing:**
- Stub-based testing approach effective - tests served as specifications
- 37 granular tests easier to debug than fewer comprehensive tests
- AsyncLocal propagation testing critical (6 dedicated scenarios)
- Test activation ongoing (13/37 passing) - provides phased validation feedback
- Performance requirements captured in tests (< 500ms health check constraint)

**Architectural Validation:**
- Phase 1 parallellization successful (Amy + Fry worked independently)
- No blocking dependencies encountered
- Existing OpenTelemetry infrastructure integrated smoothly
- ASP.NET Core patterns (IHealthCheck, middleware) fit PoshMcp architecture well

**Plan Adjustments for Future Phases:**
- Consider adding correlation ID to PowerShell script execution context (not just .NET layer)
- Health check timeout handling may need explicit implementation (Amy noted as limitation)
- Metrics dimensions strategy should extend to Phase 2 (error codes as dimensions)

**Phase 1 Success Metrics:**
- Timeline: Completed in single session (planned for Week 1)
- Test coverage: 13/37 passing validates core functionality
- Performance: < 500ms health check requirement met
- Integration: Clean fit with existing infrastructure

**Readiness for Phase 2:**
- Correlation ID foundation ready for error code integration
- Patterns established for adding new observability features
- Team coordination validated through Phase 1 success

