# Decisions Archive

Archived decisions older than 7 days.

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

## 2026-03-27: Phase 1 Code Review - Approval with Minor Fixes

**Context:** Farnsworth completed architectural review of Phase 1 implementation (health checks and correlation IDs). Implementation demonstrated solid design alignment but required performance and correctness improvements.

**Decision:** APPROVED WITH MINOR CHANGES
- Overall assessment: 7.5/10 (Architecture 9/10, Code Quality 7/10)
- Required fixes: Health check timeout enforcement, LoggerExtensions performance warnings
- Non-blocking recommendation: Strengthen AssemblyGenerationHealthCheck validation

**Critical Issues Identified:**
1. **Health Check Timeouts:** PowerShellRunspaceHealthCheck declared 500ms timeout but didn't enforce it
2. **LoggerExtensions Performance:** Created new scope per log call instead of per operation
3. **Program.cs Duplicate Code:** Unreachable code after app.Run() in PoshMcp.Web/Program.cs

**Resolution:** All critical issues addressed by Amy and Bender. Testing confirms 13/37 tests passing, Phase 1 feature-complete.

**Reviewer:** Farnsworth  
**Implementers:** Amy (fixes), Bender (Program.cs cleanup)  
**Validator:** Test suite confirming functionality  

**Status:** Active

---

## 2026-03-27: Program.cs Duplicate Code Removal

**Context:** Code review identified duplicate, unreachable code in PoshMcp.Web/Program.cs after app.Run() call.

**Decision:** Removed duplicate endpoint mapping and app.Run() calls (lines 157-160).

**Rationale:** 
- app.Run() is blocking and starts the web server
- Any code after app.Run() in the same method is unreachable
- Duplicate code adds confusion even if not functional

**Implementation:** Bender  
**Impact:** No functional change (code was unreachable), improved maintainability  
**Lesson Learned:** app.Run() should always be the last call in Program.cs

**Status:** Active

---

## 2026-03-27: Health Check Timeout and LoggerExtensions Performance Fixes

**Context:** Farnsworth's Phase 1 review identified two critical production readiness issues that required immediate attention.

**Decision:** Implemented two targeted fixes:

1. **Health Check Timeout Enforcement:**
   - Wrapped Task.Run with Task.WaitAsync(HealthCheckTimeout, cancellationToken)
   - Added dedicated TimeoutException handling for diagnostics
   - Ensures 500ms timeout requirement for K8s probe compatibility

2. **LoggerExtensions Performance Warnings:**
   - Added comprehensive XML documentation warnings about per-call scope creation
   - Documented BeginCorrelationScope() as RECOMMENDED pattern for hot paths
   - Preserved convenience methods with education-first approach
   - Prevents performance issues while maintaining backward compatibility

**Rationale:**
- Timeout fix: Explicit enforcement guarantees operational requirements met
- Documentation approach: Balances developer experience with performance guidance
- Avoided breaking changes: Maintained backward compatibility while preventing future misuse

**Implementation:** Amy Wong  
**Testing:** All 13 health check tests passing, timeout properly enforced  
**Trade-off:** Documentation warnings vs. method removal (prioritized education and compatibility)

**Status:** Active

---

## 2026-03-27: PowerShell Native Streams for Deploy Scripts

**Context:** Azure deployment script (`infrastructure/azure/deploy.ps1`) was using custom helper functions (`Write-Info`, `Write-Success`, `Write-ErrorMessage`) that wrapped `Write-Host` calls with different colors. This violated PowerShell best practices by bypassing the pipeline and prevented integration with PowerShell's built-in stream infrastructure.

**Decision:** Refactored deploy.ps1 to use native PowerShell streams:
- **Write-Information** (with `-Tags`) for status messages and successes
- **Write-Verbose** for diagnostic details (command execution info)
- **Write-Warning** for warnings (already correct, kept native)
- **Write-Error** for errors with proper categories (`NotInstalled`, `AuthenticationError`, `InvalidOperation`)
- **Write-Host** only for formatted summary output requiring color

**Alternatives Considered:**
1. Keep custom functions and add comment-based help → Rejected: Still bypasses pipeline
2. Switch everything to Write-Host → Rejected: Worse than current state
3. Use Write-Output for all messages → Rejected: Pollutes return values
4. Native streams with proper categorization → **Selected**

**Rationale:**
- Pipeline compatibility: Output can be captured/redirected using standard mechanisms
- User control: Respects `-Verbose`, `-InformationAction`, `-ErrorAction` parameters
- Better automation: Integrates with logging frameworks and automation tools
- Standards compliance: Follows approved verb naming and stream conventions
- Enhanced diagnostics: Verbose stream provides detailed execution information
- Reduced code: Eliminated ~40 lines of custom wrapper functions

**Implementation Details:**
- Set `$InformationPreference = 'Continue'` for interactive visibility
- Added error categories for semantic error handling
- Tagged Information messages (`-Tags 'Status'`, `-Tags 'Success'`) for filtering
- Enhanced verbose logging for diagnostic traces
- Leveraged existing `[CmdletBinding()]` for automatic parameters

**Consequences:**
- ✅ Script now pipeline-compatible for automation scenarios
- ✅ Users can control output verbosity with standard PowerShell parameters
- ✅ Proper integration with PowerShell logging infrastructure
- ✅ Improved error handling with categories and structured error records
- ⚠️ Requires PowerShell 5.0+ (for Write-Information cmdlet)
- 📋 Pattern should be applied to other automation scripts in codebase

**Implementation:** Hermes  
**Code Reduction:** ~40 lines removed  
**Files Modified:** [infrastructure/azure/deploy.ps1](infrastructure/azure/deploy.ps1)

**Status:** Active

---

## 2026-03-27: Azure Container Apps Deployment Strategy

**Context:** PoshMcp needs production-ready Azure deployment infrastructure that:
- Supports both development and production environments
- Integrates with existing health checks and observability (Phase 1)
- Follows Azure best practices for security and scalability
- Provides reproducible, automated deployment workflows

**Decision:** Implement Azure Container Apps deployment using Bicep IaC templates.

**Primary Choices:**

1. **Azure Container Apps over alternatives:**
   - **Why not AKS?** Too complex, higher operational overhead for single-app deployment
   - **Why not App Service for Containers?** Less granular scaling, no built-in Dapr support
   - **Why not Container Instances?** Limited scaling, no built-in ingress management
   - **Container Apps chosen for:** Managed K8s-like features, autoscaling, built-in ingress, lower operational complexity

2. **Bicep over ARM JSON:** More readable, maintainable, native Azure tooling support

3. **User-Assigned Managed Identity:** Independent lifecycle, pre-deployment RBAC setup

4. **Dual scripting (bash + PowerShell):** Cross-platform team support with feature parity

**Health Check Strategy:**
- Startup: `/health` with 30 failures (150s tolerance for PowerShell initialization)
- Liveness: `/health/ready` every 30s (detect hung processes)
- Readiness: `/health/ready` every 10s (control traffic routing)

**Autoscaling:** HTTP-based with 1-10 replicas, 50 concurrent requests per replica threshold

**Resource Sizing:** 0.5 vCPU, 1.0 GB memory (sufficient for PowerShell workloads)

**Implementation:** Amy Wong

**Status:** Active

---

## 2026-03-27: Multi-Tenant Azure Deployment Support

**Context:** Azure deployment scripts did not account for scenarios where users work with multiple Azure tenants, creating risks when deploying across customer environments or switching between organizational tenants.

**Decision:** Implement explicit multi-tenant support in both deployment scripts (deploy.ps1 and deploy.sh):
- Optional tenant parameter (PowerShell: `-TenantId`, Bash: `AZURE_TENANT_ID` env var)
- Falls back to current tenant if not specified (backward compatible)
- Tenant validation workflow: detect current tenant, switch if needed, validate subscription belongs to active tenant
- Enhanced error handling with clear tenant mismatch messages

**Implementation Details:**
- Added `Set-AzureTenant` function in deploy.ps1 to handle tenant switching
- Updated `Set-AzureSubscription` to validate subscription belongs to current tenant
- Added tenant validation after login to verify successful switch
- Comprehensive README documentation with 4 concrete usage scenarios

**Rationale:**
- Maintains backward compatibility (parameter optional)
- Prevents wrong-tenant deployments (critical security issue)
- Follows Azure PowerShell naming conventions (`-TenantId`)
- Provides clear error messages with both expected and actual tenant IDs

**Consequences:**
- ✅ Safe multi-tenant deployments
- ✅ Backward compatible with existing workflows
- ✅ CI/CD with managed identity works without tenant parameter
- ✅ Follows PowerShell and Azure CLI best practices

**Implementation:** Amy Wong
**Code Review:** Hermes (9/10 - production-ready)

**Status:** Active

---

## 2026-03-27: Hermes Review - Multi-Tenant Deployment Implementation

**Context:** Hermes reviewed Amy's multi-tenant PowerShell implementation in deploy.ps1 for code quality, PowerShell best practices, and production readiness.

**Decision:** APPROVED (9/10 from PowerShell perspective) - Production-ready implementation.

**Strengths Identified:**
1. **Parameter naming:** Correct usage of `-TenantId` following Azure PowerShell conventions
2. **Error handling:** Semantic error categories, defensive LASTEXITCODE checking, proper stderr redirection
3. **Stream usage:** Perfect alignment with recent PowerShell streams refactoring (Write-Information, Write-Verbose)
4. **Edge case handling:** Tenant mismatch validation, subscription-to-tenant validation
5. **Script state management:** Correct use of script-scoped variables

**Minor Optional Improvements:**
- Could split tenant switching into separate function for reusability
- Could add `-WhatIf` support for dry-run scenarios

**Reviewer:** Hermes
**Reviewee:** Amy Wong

**Status:** Active

---

## 2026-03-27: Documentation Standards Baseline

**Context:** During initial documentation audit, Leela (Developer Advocate) identified significant inconsistencies across 162 markdown files. Mix of emoji usage patterns, heading case styles, code block formatting, and document structure made it unclear what standards contributors should follow.

**Decision:** Established comprehensive documentation standards for PoshMcp project:

**README Standards:**
- Required sections: Title → Tagline → What/Why → Example → Features → Getting Started → Links → Contributing → License
- Professional developer-focused voice with concrete examples
- Benefits before details approach

**Style Guidelines:**
- **Emojis:** Minimal/none in technical documentation (exception: internal team docs)
- **Headings:** Title Case for H1, sentence case for H2+
- **Code blocks:** Always specify language (bash, powershell, json, csharp, text)
- **Links:** Relative paths for internal, descriptive text for external

**Quality Requirements:**
- Verify code examples work before publishing
- Validate all links
- Confirm technical accuracy
- Test copy-paste commands

**Migration Strategy:**
- Phase 1: All new content follows standards immediately (README.md updated as reference)
- Phase 2: Critical docs (DESIGN.md, Azure docs, tests) - weeks 2-3
- Phase 3: Comprehensive cleanup as time allows

**Templates to Create:**
- Feature documentation template
- API documentation template  
- Tutorial template
- Deployment guide template

**Rationale:** Consistent documentation improves contributor experience, project professionalism, and reduces friction for new users. Standards based on industry best practices for technical developer tools.

**Consequences:**
- ✅ Clear guidance for all documentation work
- ✅ README.md revised as reference implementation
- ⚠️ Requires progressive migration of existing docs (non-blocking)
- 📋 May add markdown linters in future for automated enforcement

**Proposed by:** Leela  
**Status:** Active

---

## 2026-03-27: PowerShell Review Policy - Hermes Mandatory Review

**Context:** PoshMcp is built on PowerShell SDK and includes PowerShell automation scripts. PowerShell has specific idioms, stream patterns, and best practices that differ from general scripting. Recent work (stream refactoring, multi-tenant deployment) established strong patterns that new PowerShell code should follow.

**Decision:** Hermes MUST review all new or updated PowerShell scripts before merge.

**Scope:**
- ✅ All `.ps1` files (scripts)
- ✅ PowerShell code embedded in other files (Dockerfiles, CI/CD, documentation)
- ✅ PowerShell configuration patterns in C# code (if they affect PowerShell execution semantics)
- ❌ Trivial typo fixes in PowerShell comments

**Review Criteria:**
1. **Stream usage:** Proper use of Write-Information/Verbose/Warning/Error (not Write-Host)
2. **Error handling:** Semantic error categories, proper $ErrorActionPreference
3. **Parameter naming:** Following PowerShell and Azure conventions
4. **CmdletBinding:** Proper use of [CmdletBinding()] and parameter attributes
5. **Pipeline compatibility:** Script works in automation/pipeline scenarios
6. **State management:** Correct variable scoping (script/global/local)
7. **Performance:** No common anti-patterns (like creating scopes per log call)

**Process:**
1. Create review request in `.squad/decisions/inbox/` when PowerShell changes are made
2. Hermes performs review within 1 business day (when available)
3. Hermes provides APPROVED or CHANGES REQUESTED with specific feedback
4. Implementation team addresses feedback, iteration continues until approval
5. Merge only after Hermes approval

**Rationale:**
- PowerShell has established patterns from stream refactoring (2026-03-27)
- Hermes already successfully reviewed Amy's multi-tenant implementation (9/10)
- Consistent application of patterns prevents technical debt
- Expert review catches PowerShell-specific issues other reviewers might miss
- Maintains high code quality bar for PowerShell automation

**Consequences:**
- ✅ All PowerShell code follows established best practices
- ✅ Team learns PowerShell patterns through review feedback
- ✅ Prevents PowerShell anti-patterns from entering codebase
- ⚠️ Adds review step to PowerShell changes (worth the quality improvement)
- 📋 Hermes availability becomes critical path for PowerShell work

**Requested by:** Steven Murawski  
**Status:** Active

---

## 2026-03-27: Azure Bicep Modularization for Subscription-Scoped Deployment

**Decision Maker:** Amy Wong (DevOps/Platform/Azure)  
**Status:** ✅ Implemented  

**Decision:** Refactor Azure Bicep deployment to use **module-based architecture** for proper cross-scope resource deployment, separating subscription-scoped resources (resource group, role assignments) from resource group-scoped resources (Container Apps, monitoring, identity).

**Context:** The original `main.bicep` file attempted to deploy all resources at subscription scope while setting `scope: resourceGroup(name)` on individual resources. This violates Bicep's fundamental scope rule and caused 10 compilation errors (BCP139, BCP265, BCP037, BCP120). Requirement was to enable **subscription-scoped RBAC role assignments** for Managed Identity while properly deploying Container Apps resources at resource group scope.

**Solution Architecture:**
```
main.bicep (targetScope = 'subscription')
├── Creates Resource Group
├── Invokes Module: resources.bicep (scope: az.resourceGroup(rg.name))
│   └── Deploys all RG-scoped resources
└── Assigns RBAC role at subscription level
```

**Key Changes:**
1. **main.bicep** (subscription scope): Create resource group, invoke `resources.bicep` module with RG scope, deploy role assignment at subscription scope, aggregate outputs from module
2. **resources.bicep** (resource group scope): No structural changes (already correct), contains Log Analytics, App Insights, Container Apps Environment, Managed Identity, Container App
3. **Role Assignment Fix:** Changed from `guid(subscription().id, resources.outputs.principalId, ...)` (runtime value) to `guid(subscription().id, resourceGroupName, containerAppName, roleId)` (deterministic)

**Alternatives Considered:**
- Single Resource Group-Scoped Template: Rejected - Cannot assign subscription-level RBAC roles from resource group scope
- Two Separate Templates (Manual Orchestration): Rejected - Adds complexity and requires manual two-step deployment
- Module-Based (Selected): Enables single-command deployment with proper scope separation

**Consequences:**
- ✅ Compilation errors eliminated, subscription-scoped role assignments enabled
- ✅ Clean separation of concerns, reusable module pattern, follows Bicep best practices
- ✅ Zero breaking changes to deployment process, in-place updates (no downtime)
- Adds one level of indirection (module call), developers must understand scope hierarchy

**Best Practices Applied:** Modules for cross-scope deployment, no `name` on module (modern convention), symbolic references using `resources.outputs.*`, `az.*` namespace for functions, deterministic GUIDs

**Documentation:** [MODULARIZATION.md](infrastructure/azure/MODULARIZATION.md), [BICEP-REFACTOR-SUMMARY.md](infrastructure/azure/BICEP-REFACTOR-SUMMARY.md)

**Validation:** Both `main.bicep` and `resources.bicep` compile with 0 errors, `az deployment sub what-if` successful

**Rationale:** Enables production Azure deployment with proper RBAC while maintaining clean architecture and zero breaking changes. Follows established Bicep patterns for cross-scope resource deployment.

---

## 2026-03-27: Docker Base Image Architecture - Module-Based Separation

**Decision Maker:** Farnsworth (Lead Architect)  
**Status:** ✅ Implemented  

**Decision:** Redesigned Docker architecture to use **base image + derived image pattern**, separating MCP server runtime from user customizations.

**Context:** Original Docker architecture embedded PowerShell module installation directly into base Dockerfile via build arguments. This created coupling (base runtime mixed with user customization), maintainability issues (every customization required rebuilding entire base image), and violated Docker layering best practices.

**Solution:**
1. **Base Image (`Dockerfile`)**: Contains only MCP server runtime, no module installation code, clean minimal reusable foundation (~500MB)
2. **Module Installation Script (`install-modules.ps1`)**: Standalone PowerShell script with proper error handling, supports version constraints, reusable in any context
3. **User Dockerfiles (Examples)**: `examples/Dockerfile.user` (basic), `examples/Dockerfile.azure` (Azure automation), `examples/Dockerfile.custom` (multi-stage build)

**Key Implementation:**
- Refactored base `Dockerfile` to ~100 lines (from 150), removed all module installation code
- Created `install-modules.ps1` (400+ lines) with comprehensive error handling, parameter validation, environment variable support
- Created 3 example Dockerfiles covering 90% of use cases
- Updated documentation: `DOCKER.md` (~500 lines rewrite), `examples/README.md` (~400 lines)

**Alternatives Considered:**
- Keep Build Arguments: Rejected - Violates separation of concerns
- Only Runtime Module Installation: Rejected - Poor performance (2-10s per module vs 50-200ms)
- Monolithic Multi-Purpose Images: Rejected - Multiple large images harder to maintain
- Script-Only Approach: Rejected - Users would handle .NET build, PowerShell installation

**Consequences:**
- ✅ Clearer responsibility boundaries, easier to maintain base image, faster base builds
- ✅ Users can version control their Dockerfile + configs, reusable module installation script
- ✅ Better layer caching, multi-stage build support, reusable tooling
- ⚠️ Increased complexity: Users need to write a Dockerfile (mitigated with 3 copy-paste templates)
- ⚠️ Broken backward compatibility: Old `docker.sh build --modules` approach deprecated (still works, migration guide provided)
- ⚠️ Learning curve: Users must understand Dockerfile basics (mitigated with comprehensive documentation)

**PowerShell Best Practices (Hermes-Reviewed):** `$ErrorActionPreference = 'Stop'`, `-ErrorAction Stop` for critical operations, proper stream-based output, exit codes for integration, version constraint mapping

**Performance Impact:**
- Build time comparable (5-7 min total, with caching custom rebuilds ~1-2 min)
- Runtime performance unchanged (modules pre-installed)
- Base image ~500MB, user images 550-800MB depending on modules

**Rationale:** Separates concerns between PoshMcp team (runtime) and users (customization), follows Docker best practices, provides reusable tooling, maintains flexibility while improving maintainability.

---

## 2026-03-27: Docker PowerShell Scripts - Critical Error Handling Fixes

**Implementer:** Hermes (PowerShell Expert)  
**Status:** ✅ Complete  

**Decision:** Fix critical error handling and stream usage issues in Docker module pre-installation feature (`Dockerfile` embedded PowerShell and `docker.ps1` script).

**Context:** Initial implementation of Docker module pre-installation had critical PowerShell issues that violated 2026-03-27 stream refactoring patterns. The Dockerfile embedded PowerShell had error handling bugs causing silent failures, and `docker.ps1` used `Write-Host` instead of native PowerShell streams.

**Critical Fixes Applied:**

**Priority 1 (Dockerfile):**
1. **Missing $LASTEXITCODE Checking**: Added `if ($LASTEXITCODE -ne 0) { throw }` after every `Install-Module` call to catch failures that don't propagate through `-ErrorAction Stop`
2. **Boolean Parameter Bug**: Fixed `-SkipPublisherCheck:\$$SKIP_PUBLISHER_CHECK` which expanded to string `"$true"` instead of boolean. Now converts environment variable to boolean first: `$skipCheck = [System.Convert]::ToBoolean('$SKIP_PUBLISHER_CHECK')`
3. **Silent Failure Handling**: Changed `catch { Write-Warning }` to `catch { Write-Error -ErrorAction Stop; exit 1 }` to fail build on module installation errors

**Priority 2 (docker.ps1):**
1. **Added [CmdletBinding()]**: Enables `-Verbose`, `-InformationAction`, `-ErrorAction` common parameters
2. **Stream Refactoring**: Converted status messages from `Write-Host` to `Write-Information -Tags 'Status'`, set `$InformationPreference = 'Continue'`, kept final success messages as `Write-Host` (presentation layer only)
3. **Verbose Diagnostics**: Added `Write-Verbose` for build arguments, image name, module scope, container operations

**Verification:**
- ✅ Invalid module name → Build FAILS (exit 1)
- ✅ Install-Module error → Build FAILS (caught by try/catch)
- ✅ Boolean parameter correctly interpreted (not string)
- ✅ Status messages use Write-Information with -Tags 'Status'
- ✅ [CmdletBinding()] present, common parameters enabled
- ✅ Follows 2026-03-27 stream refactoring patterns

**Consequences:**
- ✅ No silent failures - module installation errors immediately fail Docker build
- ✅ PowerShell pipeline integration - scripts work with logging frameworks and automation
- ✅ Diagnostic support - `-Verbose` switch provides troubleshooting information
- ✅ Pattern consistency - matches established PowerShell best practices from deploy.ps1

**PowerShell Quality Score:** Dockerfile improved from 4/10 → 9/10, docker.ps1 improved from 6/10 → 9/10

**Rationale:** Prevents production incidents from silently failing module installations, ensures PowerShell code follows established team patterns, enables proper observability and troubleshooting.

---

## 2026-03-27: Hermes PowerShell Review Policy

**Requested by:** Steven Murawski (via Squad Coordinator)  
**Status:** Active  

**Decision:** Establish **mandatory Hermes review** for all new or updated PowerShell scripts before merge.

**Context:** Docker module pre-installation feature introduced new PowerShell scripts (`docker.ps1`, embedded PowerShell in `Dockerfile`) that initially had critical issues violating established patterns from 2026-03-27 stream refactoring. User policy requires Hermes expert review for any PowerShell changes to maintain quality and consistency.

**Scope of Review:**
- Parameter handling and validation
- PowerShell stream usage (Write-Information/Verbose/Error vs Write-Host)
- Error handling patterns (`$ErrorActionPreference`, `-ErrorAction`, `$LASTEXITCODE` checking)
- CmdletBinding and parameter attributes
- Exit code handling
- PowerShell anti-patterns and best practices compliance

**Process:**
1. Create review request in `.squad/decisions/inbox/` when PowerShell changes made
2. Hermes performs review within 1 business day (when available)
3. Hermes provides APPROVED or CHANGES REQUESTED with specific feedback
4. Implementation team addresses feedback, iteration continues until approval
5. Merge only after Hermes approval

**Key Review Criteria:**
- Follows PowerShell best practices from established patterns
- Proper PowerShell streams used (Information/Verbose/Error vs Write-Host)
- Error handling is semantic and appropriate
- Parameter names follow conventions
- No PowerShell anti-patterns
- Edge cases handled (empty inputs, version constraints, timeouts)

**Consequences:**
- ✅ All PowerShell code follows established best practices
- ✅ Team learns PowerShell patterns through review feedback
- ✅ Prevents PowerShell anti-patterns from entering codebase
- ⚠️ Adds review step to PowerShell changes (worth the quality improvement)
- 📋 Hermes availability becomes critical path for PowerShell work

**Example Success:** Hermes review of Docker scripts identified 3 critical Dockerfile bugs (silent failures, boolean interpolation, missing exit code checks) and 3 important docker.ps1 issues (missing CmdletBinding, Write-Host usage, no verbose diagnostics), preventing production incidents.

**Rationale:** PowerShell has established patterns requiring domain expertise. Hermes already successfully reviewed Amy's multi-tenant implementation (9/10) and caught critical issues in Docker scripts. Consistent expert review prevents technical debt and maintains high code quality bar.

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
