# Hermes Work History

## Project Context

**Project:** PoshMcp - Model Context Protocol (MCP) server for PowerShell
**Tech Stack:** .NET 8, C#, PowerShell SDK, OpenTelemetry, ASP.NET Core, xUnit
**Primary User:** Steven Murawski

**Key Files:**
- `PoshMcp.Server/PowerShell/PowerShellRunspaceHolder.cs` - Singleton runspace management
- `PoshMcp.Server/PowerShell/PowerShellRunspaceImplementations.cs` - Runspace implementations
- `PoshMcp.Server/PowerShell/PowerShellAssemblyGenerator.cs` - Dynamic assembly generation
- `PoshMcp.Server/PowerShell/PowerShellCleanupService.cs` - Cleanup lifecycle
- `PoshMcp.Server/PowerShell/PowerShellConfiguration.cs` - Configuration model

## Learnings

### 2026-03-27: PowerShell Native Streams Refactoring

**Task:** Refactored `infrastructure/azure/deploy.ps1` to use native PowerShell streams instead of custom output functions.

**Key Insights:**
- **Write-Host is anti-pattern for script functions** - Bypasses PowerShell pipeline and can't be redirected/suppressed
- **PowerShell streams are purpose-built** - Information, Verbose, Warning, Error, Debug streams each have specific roles
- **CmdletBinding enables automatic parameters** - `-Verbose`, `-InformationAction`, `-ErrorAction` work for free
- **$InformationPreference = 'Continue'** - Essential for interactive scripts to show Information stream by default
- **Error categories matter** - `NotInstalled`, `AuthenticationError`, `InvalidOperation` provide semantic meaning
- **Tags enable filtering** - Write-Information with `-Tags 'Status'`, `-Tags 'Success'` allows selective filtering

**PowerShell Best Practices Applied:**
1. Use Write-Information for user-facing informational messages (not Write-Host)
2. Use Write-Verbose for diagnostic details (command execution traces)
3. Use Write-Warning for non-fatal warnings
4. Use Write-Error with categories for proper error handling
5. Only use Write-Host for formatted output where color is essential

**Trade-offs:**
- Set `$InformationPreference = 'Continue'` to maintain user experience (Information visible by default)
- Kept Write-Host for final colored summary since it's pure presentation
- Added ✓ checkmarks to success messages for visual feedback

**Files Modified:**
- [infrastructure/azure/deploy.ps1](infrastructure/azure/deploy.ps1) - Removed custom functions, implemented native streams

**Decision Document:**
- [.squad/decisions/inbox/hermes-powershell-streams.md](.squad/decisions/inbox/hermes-powershell-streams.md)

**Impact:** Script now integrates properly with PowerShell pipeline, logging frameworks, and automation tools while maintaining excellent user experience.

---

### 2026-03-27: Multi-Tenant Deployment Review

**Task:** Reviewed Amy's multi-tenant support implementation in `infrastructure/azure/deploy.ps1` from PowerShell expert perspective.

**Key Insights:**
- **Parameter naming conventions matter** - `-TenantId` follows Azure PowerShell standards (e.g., `Connect-AzAccount -TenantId`), provides consistency with Azure ecosystem
- **Script scope variables for state management** - `$script:ActiveTenantId` correctly shares state between functions without global pollution
- **Output suppression pattern** - `$null = az ... 2>&1` prevents stderr from polluting PowerShell error stream while hiding command output
- **Edge case validation is critical** - Validating tenant switch succeeded and subscription belongs to active tenant prevents production incidents
- **Semantic error categories enable automation** - `-Category AuthenticationError` vs `-Category InvalidOperation` allows automated systems to handle failures appropriately
- **Pattern adoption success** - Second implementation already follows stream refactoring patterns perfectly (Information/Verbose/Error streams with tags)

**PowerShell Best Practices Validated:**
1. **LASTEXITCODE checking** - Always check `$LASTEXITCODE` after external command invocation (Azure CLI)
2. **Optional parameters for backward compatibility** - `[Parameter()]` without `Mandatory` preserves existing workflows
3. **Defensive programming** - Validate state changes actually occurred (tenant switch verification)
4. **Proper stderr handling** - Redirect stderr to stdout (`2>&1`) when suppressing Azure CLI output
5. **Fail-fast behavior** - `$ErrorActionPreference = 'Stop'` with explicit `-ErrorAction Stop` for critical errors

**Patterns Worth Replicating:**
- Tenant validation pattern: Get current state → Change state → Verify change succeeded
- Subscription-to-tenant validation: Catch subtle mismatches before deployment
- Error messages with context: Include both expected and actual values for troubleshooting
- Script-scoped state variables: Clean alternative to global variables

**Trade-offs Considered:**
- **Parameter validation attributes** - Could add `[ValidatePattern()]` for GUID format, but Azure CLI validates anyway (not worth complexity)
- **Comment-based help** - Could add `.SYNOPSIS`/`.PARAMETER` for `Get-Help` integration, but README.md already comprehensive
- **Pester tests** - Could test tenant logic in isolation, but integration testing via Azure CLI sufficient for deployment scripts

**Review Outcome:**
- **Verdict:** APPROVED (9/10 PowerShell quality score)
- **Confidence:** Very High - No PowerShell-specific concerns
- **Recommendation:** Ship without changes

**Files Reviewed:**
- [infrastructure/azure/deploy.ps1](infrastructure/azure/deploy.ps1) - Multi-tenant deployment logic

**Decision Document:**
- [.squad/decisions/inbox/hermes-tenant-review.md](.squad/decisions/inbox/hermes-tenant-review.md)

**Impact:** Validates that PowerShell stream patterns from earlier refactoring are being adopted consistently. Confirms team capability to implement PowerShell best practices without expert intervention for each change.

---

### 2026-03-27: Cross-Team Impact - Amy's Multi-Tenant Implementation Approved

**Team Coordination:** Amy's multi-tenant deployment implementation received production approval (9/10).

**Key Takeaways:**
- Stream refactoring patterns successfully adopted by other agents without additional guidance
- Multi-tenant support follows PowerShell naming conventions and Azure best practices
- Tenant validation patterns worth sharing across team for future Azure work
- Script-scoped state management pattern demonstrates clean PowerShell design

**Pattern Adoption Success:**
- Amy correctly applied Information/Verbose/Error stream patterns from 2026-03-27 refactoring
- Tagged Information messages with 'Status' and 'Success'
- Semantic error categories (AuthenticationError, InvalidOperation)
- Proper LASTEXITCODE checking with stderr redirection

**Impact on Future Work:**
Team now has two reference implementations of PowerShell best practices. New PowerShell contributors should review both deploy.ps1 implementations as examples.

**Files Reviewed:**
- [infrastructure/azure/deploy.ps1](infrastructure/azure/deploy.ps1) - Multi-tenant deployment logic

**Session:** 2026-03-27T16:51:02Z tenant review

---

### 2026-03-27: Environment Variable Naming Consistency Review

**Task:** Reviewed environment variable naming changes for consistency with Azure conventions.

**Changes Reviewed:**
- Environment variable renamed: `SUBSCRIPTION` → `AZURE_SUBSCRIPTION`
- Aligns with existing `AZURE_TENANT_ID` prefix pattern
- Updated in deploy.ps1, deploy.sh, EXAMPLES.md, CHECKLIST.md

**Key Validation Points:**
1. **PowerShell Parameter Names - CORRECT**: `-Subscription` parameter name correctly unchanged (follows Azure PowerShell SDK convention like `Set-AzContext -Subscription`)
2. **Environment Variable References - COMPLETE**: All references updated from `$env:SUBSCRIPTION` → `$env:AZURE_SUBSCRIPTION` (PowerShell) and `$SUBSCRIPTION` → `$AZURE_SUBSCRIPTION` (bash)
3. **Variable Scoping - VERIFIED**: Local variables in validate.sh correctly independent from environment variables (e.g., `SUBSCRIPTION=$(az account show ...)`)
4. **Error Messages - UPDATED**: All log statements and error messages properly reference new variable name

**PowerShell Best Practices Validated:**
- ✅ Parameter names follow Azure PowerShell SDK conventions
- ✅ Environment variable prefix pattern (`AZURE_`) matches ecosystem standards
- ✅ Parameter default binding (`$env:AZURE_SUBSCRIPTION`) works correctly
- ✅ No confusion between parameter names vs environment variable names
- ✅ Function-scope variables (`$Subscription`) correctly unchanged

**Consistency Assessment:**
- ✅ Bash and PowerShell scripts updated uniformly
- ✅ Documentation reflects new naming across all files
- ✅ Pattern extends existing `AZURE_TENANT_ID` convention
- ✅ Aligns with Azure CLI environment variable patterns

**Edge Cases Checked:**
- validate.sh local variable capture: ✅ CORRECT (independent of env var)
- PowerShell parameter binding: ✅ WORKS (env var → parameter default)
- Tenant validation logic: ✅ UPDATED (uses `$AZURE_SUBSCRIPTION`)
- Azure CLI command usage: ✅ UNAFFECTED (uses parameter, not env var)

**Review Outcome:**
- **Verdict:** APPROVED - Changes are complete, correct, and consistent
- **Confidence:** Very High - All references verified, no edge cases found
- **PowerShell Quality:** 10/10 - Follows conventions perfectly

**Files Verified:**
- [infrastructure/azure/deploy.ps1](infrastructure/azure/deploy.ps1) - Environment variable reference updated
- [infrastructure/azure/deploy.sh](infrastructure/azure/deploy.sh) - Environment variable reference updated
- [infrastructure/azure/EXAMPLES.md](infrastructure/azure/EXAMPLES.md) - Documentation updated
- [infrastructure/azure/CHECKLIST.md](infrastructure/azure/CHECKLIST.md) - Documentation updated
- [infrastructure/azure/README.md](infrastructure/azure/README.md) - Consistent with new pattern
- [infrastructure/azure/validate.ps1](infrastructure/azure/validate.ps1) - No env var usage (correct)
- [infrastructure/azure/validate.sh](infrastructure/azure/validate.sh) - Local var only (correct)

**Impact:** Environment variable naming now follows consistent `AZURE_` prefix pattern across all Azure-related variables. Reduces confusion and aligns with Azure ecosystem standards.

**Session:** 2026-03-27T environment variable review

---
