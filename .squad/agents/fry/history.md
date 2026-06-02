# Fry Work History

## Current Summary (compacted 2026-06-01T00:00:00Z)
Detailed prior QA entries were archived to `history-archive.md` because this file exceeded the 15KB Scribe hard gate. Keep this file focused on active test patterns and regression gates.

## Learnings
- Issue #286 added 46 fast unit tests for `NounRegistry`: build/registration, resource-name derivation, noun extraction, conflict resolution, lookup methods, and URI format. Use inline `CapturingLogger` carefully to satisfy nullable logger signatures.
- Doctor JSON shape after the runtime-settings refactor: runtime data lives under `runtimeSettings.{key}.value/source`, tool names under `functionsTools.toolNames`, and OOP module paths under `powerShell.oopModulePaths` / `oopModulePathEntries`.
- Script-level integration tests can mock `az`, Docker/Podman, `poshmcp`, and network calls to validate deployment precedence without live Azure resources.
- For deploy script precedence, capture script-level `$PSBoundParameters` once; nested functions see their own empty hashtable.
- Session-aware runspace release gates should run the focused server-session tests first, then the full Unit tier because singleton/shared runspace behavior is HTTP infrastructure.
- Existing integration/OOP harness sensitivity around `ToolDescriptionParityTests` is a known concern, not necessarily fallout from unrelated runspace fixes.
- 2026-06-01T00:00:00Z: Auth safe-claim logging review covered `PoshMcp.Server/Authentication/AuthClaimDiagnostics.cs`, `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs`, and `PoshMcp.Tests/Unit/AuthenticationServiceExtensionsTests.cs`. Added focused tests for unsupported auth-like claim names and null principals; `git diff --check` and editor diagnostics were clean. Narrow `dotnet test .\PoshMcp.Tests\PoshMcp.Tests.csproj --filter FullyQualifiedName~AuthenticationServiceExtensionsTests` was blocked before execution by missing embedded resource `infrastructure\azure\parameters.json`.
- 2026-06-02: Release-readiness test stabilization included fixing blockers in `ProgramCliScaffoldCommandTests` and `OutOfProcessHostConcurrencyTests`, but those fixes alone were insufficient for a full green gate.