# Amy Work History

## Project Context

**Project:** PoshMcp - Model Context Protocol (MCP) server for PowerShell
**Tech Stack:** .NET 8, C#, PowerShell SDK, OpenTelemetry, ASP.NET Core, xUnit
**Primary User:** Steven Murawski

**Key Files:**
- `PoshMcp.Server/Metrics/McpMetrics.cs` - Current metrics infrastructure
- `PoshMcp.Server/Metrics/MetricsConfigurationService.cs` - Metrics configuration
- `PoshMcp.Server/Program.cs` - Metrics and telemetry setup

**Current Observability:**
- OpenTelemetry integration exists
- Basic tool execution metrics (invocation count, duration, errors)
- Intent resolution placeholders
- Tool registration lifecycle metrics

## Learnings

### 2026-03-27: Phase 1 - Health Checks and Correlation IDs

**Implementation Summary:**
- Created health check infrastructure for PowerShell runspace, assembly generation, and configuration
- Implemented correlation ID tracking using AsyncLocal<T> for async flow propagation
- Added logging extension methods for consistent correlation ID inclusion
- Integrated correlation IDs into key logging points and metrics
- Added health check endpoints to PoshMcp.Web with JSON response formatting

**Key Technical Decisions:**
- Used AsyncLocal<T> for correlation ID storage to ensure proper async/await flow
- Created IHealthCheck implementations in PoshMcp.Server for reusability
- Added Microsoft.Extensions.Diagnostics.HealthChecks package to both Server and Web projects
- Implemented correlation ID middleware in Web app to extract/generate IDs from headers
- Added correlation_id as a dimension to OpenTelemetry metrics

**Files Created:**
- `PoshMcp.Server/Health/PowerShellRunspaceHealthCheck.cs` - Tests PowerShell runspace responsiveness
- `PoshMcp.Server/Health/AssemblyGenerationHealthCheck.cs` - Validates assembly generation capability
- `PoshMcp.Server/Health/ConfigurationHealthCheck.cs` - Checks configuration validity
- `PoshMcp.Server/Observability/OperationContext.cs` - AsyncLocal-based correlation ID tracking
- `PoshMcp.Server/Observability/LoggerExtensions.cs` - Logging helpers with correlation support

**Files Modified:**
- `PoshMcp.Web/Program.cs` - Added health checks registration and endpoints, correlation ID middleware
- `PoshMcp.Web/PoshMcp.Web.csproj` - Added health checks package
- `PoshMcp.Server/PoshMcp.csproj` - Added health checks package
- `PoshMcp.Server/McpToolFactoryV2.cs` - Added correlation ID support to tool generation logging
- `PoshMcp.Server/PowerShell/PowerShellAssemblyGenerator.cs` - Added correlation IDs to execution logging and metrics

**Health Check Design:**
- All health checks complete in < 500ms to avoid blocking
- PowerShell runspace check executes simple "1 + 1" command to verify responsiveness
- Assembly generation check validates command introspection capability
- Configuration check validates structure and reports counts as metadata
- Health endpoints: `/health` (detailed JSON), `/health/ready` (simple OK/unhealthy for K8s)

**Correlation ID Design:**
- Format: `yyyyMMdd-HHmmss-<8-char-guid>` (sortable and unique)
- Propagates through AsyncLocal across async operations
- Middleware extracts from X-Correlation-ID header or generates new ID
- Response includes X-Correlation-ID header for client tracking
- Included in all structured logs via BeginCorrelationScope()
- Added as dimension to metrics (tool_name, status, correlation_id)

**Patterns Established:**
- Use `OperationContext.BeginOperation(name)` for scoped operations
- Use `logger.LogXXXWithCorrelation()` extension methods for key events
- Health checks should be non-intrusive and fast (< 500ms target)
- Metrics should include correlation_id for request tracing

**Lessons Learned:**
- AsyncLocal<T> is the correct choice for correlation ID propagation in async/await scenarios
- Health checks need the package in the project where they're defined (Server), not just where they're registered (Web)
- dotnet restore may be needed after package changes for IDE to recognize types
- Correlation IDs should be added to metrics as well as logs for full observability
- Health check JSON responses should include duration metadata for performance monitoring

**Next Steps for Phase 2:**
- Add structured error codes enum and exception hierarchy
- Implement timeout handling for PowerShell command execution
- Add configuration validation on startup
- Expand metrics to include more granular dimensions
- Create diagnostics snapshot API

*Learnings from work will be recorded here automatically*

---

### 2026-03-27: Cross-Team Learnings from Phase 1

**From Farnsworth's Architecture:**
- 3-phase sequencing strategy validated through Phase 1 completion
- Parallel work approach (health checks + correlation IDs) enabled fast delivery
- Dependency analysis accurate - no blocking issues encountered
- Architectural patterns (AsyncLocal, IHealthCheck, middleware) chosen well

**From Fry's Testing:**
- Stub-based testing provided clear specifications before implementation
- 37 test scenarios comprehensive - covered edge cases I hadn't considered (concurrent access, timeout handling)
- AsyncLocal propagation testing critical - 6 dedicated test scenarios revealed importance
- Performance requirements (< 500ms) explicitly captured in tests - good constraint
- Test activation approach (13/37 passing) provides incremental validation feedback

**Application to Future Work:**
- Test scenarios should guide implementation order (Fry's tests revealed priorities)
- Performance constraints should be tested explicitly, not just documented
- Correlation IDs should extend to PowerShell script execution context (identified gap)
- Health check timeout handling may need explicit implementation (noted limitation)
- Consider error codes as metric dimensions in Phase 2 (pattern from correlation_id success)

**Team Coordination Insights:**
- Specification-first approach (Farnsworth → Fry → Amy) worked well
- Parallel work effective when dependencies clearly identified
- Documentation (decisions, orchestration logs) helps maintain shared context
- Cross-agent history updates valuable for learning propagation

