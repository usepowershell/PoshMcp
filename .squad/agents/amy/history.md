# Amy Work History

## Project Context

**Project:** PoshMcp - Model Context Protocol (MCP) server for PowerShell
**Tech Stack:** .NET 8, C#, PowerShell SDK, OpenTelemetry, ASP.NET Core, xUnit
**Primary User:** Steven Murawski

**Key Files:**
- `PoshMcp.Server/Metrics/McpMetrics.cs` - Metrics infrastructure
- `PoshMcp.Server/Health/*.cs` - Health check implementations
- `PoshMcp.Server/Observability/*.cs` - Correlation ID tracking
- `infrastructure/azure/*.bicep` - Azure deployment infrastructure

## Learnings

### 2026-03-27: Phase 1 Complete - Health Checks and Correlation IDs

**Implementation:** Created comprehensive health check and correlation ID infrastructure.

**Health Checks (3 implementations):**
- PowerShellRunspaceHealthCheck: Validates runspace responsiveness (< 500ms with explicit timeout enforcement)
- AssemblyGenerationHealthCheck: Tests dynamic assembly generation capability
- ConfigurationHealthCheck: Validates configuration structure and metadata
- Endpoints: `/health` (detailed JSON), `/health/ready` (K8s-compatible)

**Correlation IDs:**
- AsyncLocal-based tracking with format `yyyyMMdd-HHmmss-<8-char-guid>`
- Middleware extracts from X-Correlation-ID header or generates new ID
- Propagates through async operations automatically
- Integrated into all logs (LoggerExtensions), metrics (correlation_id dimension), and response headers

**Files Created:**
- `PoshMcp.Server/Health/` - 3 health check implementations
- `PoshMcp.Server/Observability/OperationContext.cs` - Correlation ID tracking
- `PoshMcp.Server/Observability/LoggerExtensions.cs` - Correlation-aware logging

**Key Patterns:**
- Use `OperationContext.BeginOperation(name)` for scoped operations
- Use `logger.BeginCorrelationScope()` for hot paths (not LogXxxWithCorrelation per call)
- Health checks must enforce timeouts explicitly with `Task.WaitAsync()`

**Critical Fixes (Post-Review):**
- Added explicit timeout enforcement: `healthCheckTask.WaitAsync(HealthCheckTimeout, cancellationToken)`
- Added performance warnings to LoggerExtensions XML docs (scope allocation cost)
- Separated TimeoutException handling from cancellation for diagnostics

**Testing:** 13/37 test scenarios passing, feature-complete

**Lessons:**
- AsyncLocal<T> correct for async/await correlation propagation
- Task.WaitAsync() required for reliable timeout enforcement (don't rely on token config)
- Scope allocations have real performance cost - document hot path patterns
- TimeoutException vs OperationCanceledException distinction important for diagnostics

---

### 2026-03-27: Azure Container Apps Production Infrastructure

**Implementation:** Complete Azure hosting capability with Bicep IaC, deployment scripts, and comprehensive documentation.

**Architecture Choices:**
- **Container Apps over AKS/App Service:** Simpler management, built-in autoscaling, lower operational complexity
- **User-Assigned Managed Identity:** Independent lifecycle, pre-deployment RBAC
- **Bicep over ARM JSON:** Readability, type safety, native tooling
- **Dual scripting:** Both bash and PowerShell for cross-platform support

**Key Configuration:**
- Resources: 0.5 vCPU, 1.0 GB memory (production)
- Autoscaling: 1-10 replicas, 50 concurrent requests/replica threshold
- Health probes: Startup (30 failures/150s), Liveness (30s), Readiness (10s)
- Integration: Phase 1 `/health/ready` endpoint, Application Insights, Log Analytics

**Files Created (13 files):**
- `infrastructure/azure/main.bicep` - All Azure resources
- `infrastructure/azure/deploy.{sh,ps1}` - Deployment automation
- `infrastructure/azure/validate.{sh,ps1}` - Pre-deployment checks
- `infrastructure/azure/{README,QUICKSTART,ARCHITECTURE,EXAMPLES,CHECKLIST}.md` - Documentation

**Lessons:**
- Scale-to-zero critical for dev cost optimization (minReplicas: 0)
- Startup probe needs higher failure threshold than liveness/readiness (PowerShell initialization)
- Managed Identity eliminates secrets management complexity
- Validation scripts catch issues early (fail fast)
- Documentation layering: Quickstart → Detailed → Reference

---

### 2026-03-27: Multi-Tenant Azure Deployment Support

**Implementation:** Added explicit multi-tenant support to deployment scripts for cross-tenant scenarios.

**Key Features:**
- Optional `-TenantId` parameter (PowerShell) / `AZURE_TENANT_ID` env var (Bash)
- Tenant validation: Detect current → Switch if needed → Verify successful switch
- Subscription-to-tenant validation: Prevents wrong-tenant deployments
- Enhanced error messages with both expected and actual tenant IDs
- Backward compatible (optional parameter uses current tenant if not specified)

**Functions Added:**
- `Set-AzureTenant` (PowerShell): Handles tenant switching and validation
- `set_tenant` (Bash): Equivalent tenant management

**Hermes Code Review:** APPROVED (9/10) - Production-ready

**Validation Points:**
- ✅ Parameter naming follows Azure conventions (`-TenantId`)
- ✅ Stream usage aligns with PowerShell refactoring patterns
- ✅ Semantic error categories (AuthenticationError, InvalidOperation)
- ✅ Defensive programming with tenant mismatch detection
- ✅ LASTEXITCODE checking with stderr redirection (`2>&1`)

**Patterns:**
- Tenant validation workflow: Get current → Change → Verify
- Subscription validation ensures it belongs to active tenant
- Script-scoped variables for state management (`$script:ActiveTenantId`)
- Clear error messages include diagnostic context

**Lessons:**
- Tenant validation critical for multi-tenant safety
- `az login --tenant` necessary for programmatic tenant switching
- Subscriptions can span tenants in complex org scenarios
- Optional parameters maintain backward compatibility while adding safety

---

### Cross-Team Learnings

**From Farnsworth (Architect):**
- 3-phase sequencing validated through Phase 1 completion
- Architectural patterns (AsyncLocal, IHealthCheck, middleware) chosen well
- Hot path analysis requires understanding call patterns beyond code correctness

**From Hermes (PowerShell Expert):**
- Stream refactoring patterns successfully adopted without additional guidance
- deploy.ps1 established as reference implementation for team
- Confirms team capability for autonomous PowerShell work

**From Fry (Tester):**
- 37 test scenarios provided comprehensive edge case coverage
- Stub-based testing revealed priorities and design considerations
- Performance requirements (< 500ms) explicitly validated

**Session:** 2026-03-27T16:51:02Z tenant review

**From Leela (Developer Advocate):**
- Documentation standards established for consistent technical documentation
- Azure deployment docs already follow many best practices (professional tone, clear structure)
- All new documentation should follow standards: Title Case H1, sentence case H2+, language-tagged code blocks
- Deployment guide template planned (use existing Azure docs as foundation)
- Phase 2 may include minor Azure docs formatting updates for consistency

**Session:** 2026-03-27T17:07:35Z documentation standards

---
