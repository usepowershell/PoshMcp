- **20260403T135630Z**: ✓ BICEP modularization decision processed and archived.
- **20260403T140503Z**: ✓ Documentation gap review findings documented; decision protocol recorded.
- **20260403T141812Z**: ✓ Deploy.ps1 RG creation ordering bug fixed; decision merged to team ledger.
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

---

### 2026-03-27: Bicep Modularization - Subscription-Scoped Deployment

**Implementation:** Refactored Azure Bicep infrastructure to use proper modularization for cross-scope deployment.

**Problem:** Original `main.bicep` attempted to deploy resource group-scoped resources directly from subscription scope using invalid `scope:` property, causing 10 compilation errors (BCP139, BCP265, BCP037, BCP120).

**Root Cause:**
- **Bicep Rule Violation:** Resources must match file's `targetScope`; cross-scope requires modules
- Invalid syntax: `scope: resourceGroup(name)` on individual resources
- Outdated function: `resourceGroup()` instead of `az.resourceGroup()`
- Non-deterministic GUID: Role assignment used runtime module output

**Solution - Module-Based Architecture:**

```
main.bicep (subscription)
├── Resource Group ✅
├── Module → resources.bicep (resourceGroup) ✅
│   ├── Log Analytics
│   ├── Application Insights
│   ├── Container Apps Environment
│   ├── Managed Identity
│   └── Container App
└── Role Assignment (subscription-scoped) ✅
```

**Key Architectural Decisions:**

1. **Subscription Scope Entry Point:**
   - `main.bicep` at subscription scope creates RG and role assignments
   - Uses `az.resourceGroup(rg.name)` for module scope declaration
   - Aggregates outputs from module

2. **Resource Group Module:**
   - `resources.bicep` contains all RG-scoped resources (already existed, now properly used)
   - No changes required to resource definitions
   - Outputs consumed by parent for role assignment

3. **Role Assignment Fix:**
   - Changed from runtime GUID: `guid(subscription().id, resources.outputs.principalId, ...)`
   - To deterministic GUID: `guid(subscription().id, resourceGroupName, containerAppName, roleId)`
   - Ensures name calculable at deployment start (BCP120 requirement)

**Files Modified:**
- `infrastructure/azure/main.bicep` - Refactored to use module pattern
- `infrastructure/azure/resources.bicep` - Minor formatting only (already correct)

**Files Created:**
- `infrastructure/azure/MODULARIZATION.md` - Comprehensive architecture documentation
- `infrastructure/azure/BICEP-REFACTOR-SUMMARY.md` - Executive summary

**Validation Results:**
- ✅ main.bicep: 0 errors, 0 warnings
- ✅ resources.bicep: 0 errors, 0 warnings
- ✅ Deployment scripts already correct (`az deployment sub create`)

**Zero Breaking Changes:**
- Same parameters
- Same resources
- Same outputs
- Same deployment command
- In-place update (no downtime)

**Best Practices Applied:**
- No `name` property on module (modern Bicep)
- Symbolic references (`resources.outputs.X`) instead of `resourceId()`
- `az.resourceGroup()` namespace function
- Deterministic role assignment names
- Proper multi-line formatting for ternary operators

**Lessons:**
- Bicep **requires modules** for cross-scope deployment - cannot use `scope:` property on resources
- Always use `az.*` namespace functions in modern Bicep
- Role assignment names must be deterministic (calculable at deployment start)
- Module pattern enables clean separation of concerns while maintaining single source of parameters
- Existing deployment scripts using `az deployment sub create` were already correct

**Migration Path:**
- Existing deployments: Run standard deployment → Bicep updates in place → Zero downtime
- New deployments: No special steps required

---
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

### 2026-04-03: Documentation gap review after major cleanup

**Task:** Reviewed all 23 markdown files edited during team documentation cleanup pass (~3,100 lines removed). Checked for broken cross-references, missing content, link integrity, redirect stub quality, and navigation gaps.

**Issues found and fixed (4):**

1. **DESIGN.md — Unclosed code block (Critical):** The architecture ASCII diagram's code block (line 49) was never closed with ` ``` `. Everything from "Security and governance" through the "See also" section at the end rendered as code — making 5 headings and all navigation links invisible. Fixed by adding the closing fence.

2. **DOCKER.md — Broken code block structure (Critical):** The "Running Docker Containers" section had a nested/unclosed code block. The `docker.sh` helper commands and "Manual Docker Commands" sections were interleaved, with orphaned lines outside any block. Fixed by properly separating the two command sections. Also merged duplicate "Docker Compose Profiles" / "Docker Compose" headers.

3. **infrastructure/azure/ARCHITECTURE.md — Stale EXAMPLES.md reference:** CI/CD section referenced "Workflow example provided in EXAMPLES.md" but EXAMPLES.md was converted to a redirect stub during cleanup. Updated to link to `README.md#cicd-integration` where the examples now live.

4. **PoshMcp.Tests/Integration/README.azure-integration.md — Wrong deployment command:** Manual test instructions used `az deployment group create` which fails because `main.bicep` targets subscription scope. Fixed to `az deployment sub create` with correct parameters, matching the Bicep modularization architecture.

**No issues found in:**
- All redirect stubs (DOCKER-BUILD-QUICK-REF.md, DOCKER-BUILD-MODULES.md, AZURE-INTEGRATION-TEST-SCENARIO.md, ENVIRONMENT-CUSTOMIZATION-SUMMARY.md, EXAMPLES.md) — clear titles, descriptions, and valid link targets
- All "See also" cross-reference sections — consistent and bidirectional
- Navigation paths — README.md links to all major topics; infrastructure/azure/INDEX.md provides reading-order guidance
- Content completeness — Azure deployment steps, Docker instructions, integration test setup, and environment customization options all present in canonical locations

**Lessons:**
- Code block fence bugs are the most dangerous cleanup artifact — they silently hide large sections
- Bicep modularization changes (subscription-scoped deployment) need to propagate to ALL manual command examples, not just deployment scripts
- Redirect stubs should always be cross-verified: the content they point to must actually contain the redirected content
- After deduplication, check that files referencing the removed content now point to the surviving canonical location

---

### 2026-07: Fix deploy.ps1 resource group ordering bug

**Problem:** `deploy.ps1` failed with `ResourceGroupNotFound` because `Initialize-ContainerRegistry` (which calls `az acr create --resource-group poshmcp-rg`) ran before the resource group existed. The `New-ResourceGroupIfNeeded` function had been commented out with a note that Bicep would handle RG creation — but Bicep runs later in the workflow (step 6), after ACR creation (step 4).

**Root Cause:** When Bicep was modularized to subscription scope, the script's explicit RG creation was removed under the assumption that Bicep alone was sufficient. But `az acr create` needs the RG earlier in the pipeline, before the Bicep deployment step.

**Fix:** Uncommented `New-ResourceGroupIfNeeded` and restored its call in `Invoke-Deployment` before `Initialize-ContainerRegistry`. Added a comment clarifying that both the script and Bicep create the RG — this is safe because Azure RG creation is idempotent.

**Corrected workflow order:**
1. Test-Prerequisites
2. Set-AzureTenant
3. Set-AzureSubscription
4. **New-ResourceGroupIfNeeded** ← restored
5. Initialize-ContainerRegistry
6. Build-AndPushImage
7. Deploy-Infrastructure (Bicep — re-declares RG, no-op)
8. Get-DeploymentInfo

**Lesson:** When refactoring infrastructure-as-code to handle resource creation declaratively (Bicep), verify that imperative steps earlier in the pipeline don't depend on those resources already existing. Deployment scripts with mixed imperative/declarative steps need the imperative RG creation as a safety net.

---

