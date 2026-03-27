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

---

### 2026-03-27: Phase 1 Code Review - Post-Fix Validation

**Context:** Conducted comprehensive architectural review of Phase 1 deliverables, identified 3 issues (2 critical, 1 minor), all resolved by team.

**Review Verdict:** APPROVE WITH MINOR CHANGES → All fixes applied → Phase 1 COMPLETE
- Overall Assessment: 7.5/10 (Architecture 9/10, Code Quality 7/10)
- Critical issues: Health check timeout enforcement, LoggerExtensions performance
- Minor issue: Program.cs duplicate code

**Cross-Team Learnings:**

**From Amy's Fix Implementations:**
- Task.WaitAsync() pattern validated for explicit timeout control (doesn't rely on token config)
- Documentation-first approach effective for performance guidance (preserved API compatibility)
- TimeoutException handling provides better diagnostics than generic catches
- XML documentation warnings show in IDE tooltips - valuable for preventing misuse
- Trade-off decisions (convenience vs. performance) are legitimate design choices

**From Bender's Code Cleanup:**
- Even unreachable code matters for maintainability and team clarity
- Thorough code review catches issues that static analysis might miss
- app.Run() blocking behavior is easy to forget - worth documenting pattern
- Quick fixes demonstrate value of lightweight review process

**Code Review Patterns Validated:**
- Scoring rubric (Architecture/Quality/Standards/Integration) provides clear quality signal
- Separating critical vs. non-blocking issues enables prioritization
- Specific file/line references with explanation accelerates fix process
- Non-blocking recommendations preserve team velocity while capturing technical debt

**Learnings for Future Reviews:**
- Performance issues require hot path analysis (not just correctness testing)
- API design choices need documentation (convenience methods with warnings)
- Timeout enforcement needs explicit validation (can't assume framework defaults)
- Code duplication matters even if unreachable (maintainability impact)

**Phase 2 Readiness Confirmed:**
- All blocking issues resolved (health checks, performance, duplicate code)
- Test suite validates fixes (13/13 passing)
- Correlation ID foundation ready for error code integration
- Patterns established: Task.WaitAsync for timeouts, BeginCorrelationScope for operations
- Team coordination effective: Review → Assign → Fix → Validate cycle worked smoothly

---

### 2026-03-27: Phase 1 Code Review - Health Checks & Correlation IDs

**Task:** Architectural review of Phase 1 implementation by Amy (health checks and correlation infrastructure)

**Review Findings:**

**Architecture Validated:**
- AsyncLocal<T> choice for correlation IDs confirmed correct for async/await propagation
- ASP.NET Core IHealthCheck infrastructure integrates cleanly
- Three-health-check design (Runspace, Assembly, Configuration) provides good coverage
- Middleware pattern for correlation ID extraction/generation is simple and effective
- Metrics integration with correlation_id tags works as designed

**Code Quality Issues Found:**

1. **Critical Performance Issue - LoggerExtensions Pattern:**
   - `LogXxxWithCorrelation()` methods create new scope per log call
   - Used in hot path (PowerShellAssemblyGenerator - 4 calls per tool execution)
   - Correct pattern: Create scope once at operation start, use standard logging within
   - **Anti-pattern to avoid:** Scope-per-log creates unnecessary allocations

2. **Health Check Timeout Not Enforced:**
   - PowerShellRunspaceHealthCheck declares `HealthCheckTimeout` constant but doesn't use it
   - Only relies on cancellationToken, ASP.NET may not configure timeouts
   - Could violate < 500ms requirement for K8s probes
   - **Fix:** Wrap execution in `Task.WaitAsync(timeout, cancellationToken)`

3. **Duplicate Code Bug:**
   - Program.cs has duplicate `app.MapMcp(); app.Run();` block (lines 157-160)
   - Second block never reached (Run() blocks)
   - Simple copy-paste error

4. **Weak Validation in AssemblyGenerationHealthCheck:**
   - Only tests `Get-Command` introspection, not actual assembly generation
   - Could report healthy when assembly generation broken
   - Consider testing PowerShellAssemblyGenerator instantiation more thoroughly

**Integration Review:**
- Health checks properly registered in Web project
- Correlation IDs flow through McpToolFactoryV2 and PowerShellAssemblyGenerator
- Middleware implementation correct (extract from header, generate if missing, write to response)
- Manual tagging of metrics with correlation_id works but could be more ergonomic

**Testing Observations:**
- Test-first approach validated (37 stubs created before implementation)
- 13/37 passing at phase completion - expected for parallel implementation
- Propagation tests critical for validating AsyncLocal behavior
- Timeout tests should validate < 500ms requirement

**Architectural Patterns for Future Use:**

✅ **DO:**
- Use AsyncLocal<T> with disposable scopes for context propagation
- Create logging scopes at operation entry, not per log statement
- Enforce timeouts at lowest layer (e.g., Task.WaitAsync)
- Write tests before implementation to serve as specifications
- Add correlation IDs as OpenTelemetry metric tags for traceability

❌ **DON'T:**
- Create logging scopes per log call (performance overhead)
- Declare timeout constants without enforcing them
- Rely solely on cancellationToken for timeout enforcement (may not be configured)
- Forget to validate that declarative timeouts are actually enforced

**Verdict:** APPROVE WITH MINOR CHANGES
- Required fixes: Duplicate code removal, timeout enforcement, LoggerExtensions pattern documentation
- Estimated fix time: 45 minutes
- Phase 2 can proceed after fixes validated

**Key Learnings for Team:**

1. **Scope Management Pattern:**
   ```csharp
   // ✅ Correct - scope at operation start
   using (logger.BeginCorrelationScope())
   {
       logger.LogInformation("Starting");
       await DoWork();
       logger.LogInformation("Complete");
   }
   
   // ❌ Incorrect - scope per log call
   logger.LogInformationWithCorrelation("Message");  // Creates scope internally
   ```

2. **Timeout Enforcement Pattern:**
   ```csharp
   // ✅ Explicit timeout enforcement
   await Task.Run(() => Work(), cancellationToken)
       .WaitAsync(timeout, cancellationToken);
   
   // ❌ Assumes timeout configured elsewhere
   await Task.Run(() => Work(), cancellationToken);
   ```

3. **Health Check Design:**
   - Keep checks simple and fast (< 500ms total for all checks)
   - Test actual capabilities, not proxies (e.g., test assembly generation, not just introspection)
   - Return diagnostic data in HealthCheckResult for troubleshooting

**Phase 2 Readiness:**
- Correlation ID infrastructure validated and production-ready
- OperationContext.BeginOperation() pattern established for scoping
- Metrics tagging pattern works for adding error dimensions
- After minor fixes, foundation is solid for error code integration

**Decision Record:** Created detailed review at `.squad/decisions/inbox/farnsworth-phase1-review.md`

---

### 2026-03-27: Documentation Standards Established

**Cross-Team Learning from Leela (Developer Advocate):**

**Context:** Leela completed comprehensive documentation audit (162 markdown files) and revised README.md to match GitHub best practices.

**Documentation Standards Formalized:**
- README structure: Title → Tagline → What/Why → Example → Features → Getting Started → Links → Contributing → License
- Emoji policy: Minimal/none for technical documentation (internal team docs excepted)
- Heading conventions: Title Case for H1, sentence case for H2+
- Code blocks: Always specify language (bash, powershell, json, csharp, text)
- Links: Relative paths for internal, descriptive text for external
- Quality requirements: Verify code examples, validate links, confirm technical accuracy

**Migration Strategy:**
- Phase 1: All new content follows standards (README.md as reference)
- Phase 2: Critical docs (DESIGN.md, Azure docs, tests)
- Phase 3: Comprehensive cleanup

**Architectural Impact:**
- DESIGN.md may need emoji reduction and formatting updates
- Technical specifications should follow consistent structure
- Architecture Decision Records (ADRs) if created should use deployment guide template pattern

**Templates Planned:**
- Feature documentation template
- API documentation template
- Tutorial template
- Deployment guide template

