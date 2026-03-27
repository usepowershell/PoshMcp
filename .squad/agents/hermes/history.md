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
