# Amy Work History

## Recent Status (2026-04-10)

**Summary:** Detailed pre-summary history was archived to `history-archive.md` on 2026-04-10 after the active file crossed the 15 KB Scribe threshold. Amy remains the primary owner for Azure deployment safety, release packaging, and infrastructure-oriented coordination.

**Current Role:** Infrastructure and decision pipeline coordination. Mastery areas: health checks, Azure Container Apps, Bicep deployment safety, release management, and deployment troubleshooting.

## Project Context

**Project:** PoshMcp - Model Context Protocol (MCP) server for PowerShell
**Tech Stack:** .NET 8, C#, PowerShell SDK, OpenTelemetry, ASP.NET Core, xUnit
**Primary User:** Steven Murawski

**Key Files:**
- `PoshMcp.Server/Metrics/McpMetrics.cs`
- `PoshMcp.Server/Health/*.cs`
- `PoshMcp.Server/Observability/*.cs`
- `infrastructure/azure/*.bicep`
- `infrastructure/azure/deploy.ps1`

## Active Learnings

### Platform and observability foundations
- Health checks should enforce timeouts explicitly with `Task.WaitAsync()` rather than relying on ambient cancellation behavior.
- Correlation ID propagation via `AsyncLocal<T>` fits the server's async execution model and should be threaded through logs, metrics, and response surfaces.
- Hot-path logging helpers need clear performance guidance because convenience wrappers can hide real allocation costs.

### Azure infrastructure patterns
- Container Apps remain the preferred deployment target here because they keep operational complexity lower than AKS while still supporting autoscaling, health probes, and managed identity.
- Subscription-scope Bicep entry points must use modules for resource-group deployments; direct cross-scope resource declarations are not valid.
- Mixed imperative/declarative deployment flows still need imperative resource-group creation before commands like `az acr create --resource-group ...` run.

### Release and deployment operations
- Local dotnet tool updates can fail while `poshmcp.exe` is running; stop the tool process before `dotnet tool update -g`.
- Release packaging should be verified with the produced nupkg, the installed tool version, and a smoke check of the published CLI commands.
- ACR auth hardening should keep bounded retries, exponential backoff, and targeted diagnostic snippets because transient EOF/network failures are operationally common.

## Recent Sessions

### 2026-04-09: Version 0.2.2 release cycle
- Bumped `PoshMcp.Server/PoshMcp.csproj` from 0.2.1 to 0.2.2 and packed the release nupkg.
- Verified the global `poshmcp` tool update and confirmed the CLI surface (`serve`, `list-tools`, `validate-config`, `doctor`, `psmodulepath`) was functional after installation.

### 2026-04-08: ACR auth retry hardening
- Hardened the deploy path against transient Azure Container Registry auth failures with bounded retries and clearer diagnostics.

### 2026-04-03: Deployment and documentation corrections
- Restored explicit resource-group creation in `deploy.ps1` before ACR initialization.
- Reviewed the documentation cleanup pass for broken fences, stale references, and incorrect deployment commands.

## Archive Notes
- Full historical detail prior to this summary is preserved in `history-archive.md`.

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

### 2026-04-09: Version 0.2.2 Release Cycle

**Task:** Patch version bump and release deployment.

**Actions:**
1. Bumped version in `PoshMcp.Server/PoshMcp.csproj`: 0.2.1 → 0.2.2
2. Ran `dotnet pack -c Release` → generated `poshmcp.0.2.2.nupkg` (26.4 MB)
3. Updated global tool: `dotnet tool update -g poshmcp --add-source ./PoshMcp.Server/bin/Release`
   - Upgraded from 0.2.0 → 0.2.2 (previous v0.2.1 was not in release bin, only 0.2.0 existed)
4. Verified deployment:
   - `poshmcp --version` → `0.2.2+88dbdbfc09852f4e40f5d9a7e2ced26417d9a12b` ✅
   - `poshmcp --help` → Full CLI help and commands available ✅
   - All commands functional (serve, list-tools, validate-config, doctor, psmodulepath)

**Package Location:** `C:\Users\stmuraws\source\usepowershell\poshmcp\PoshMcp.Server\bin\Release\poshmcp.0.2.2.nupkg`

**Key Learnings:**
- Dotnet tool pack generates nupkg in bin/Release, not specific subdirectory
- `dotnet tool update` with `--add-source` correctly resolves local packages
- Version info embeds git commit hash (useful for tracking production deployments)
- Global tool updates are atomic — no downtime for CLI users
- All release pack warnings were expected (PoshMcp.Web not packable, intentional scope)

---


