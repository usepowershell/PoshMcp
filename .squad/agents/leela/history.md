# Leela — History

## Current Summary (compacted 2026-06-01T00:00:00Z)
Detailed prior documentation entries were archived to `history-archive.md` because this file exceeded the 15KB Scribe hard gate. Keep this file focused on active documentation patterns and release-note guardrails.

## Learnings
- For resolver/precedence chains, use a Step/Source/Notes table plus one small PowerShell example per rung. Match implementation vocabulary exactly (`synopsis`, `description`, `syntax`, `name`; `helpParameter`, `helpMessage`, `validateSet`, `typeFallback`).
- When a feature partially ships, put an honest inline Known Issue callout near the affected content and include what works, what does not, tracking issue, current guidance, and what changes later.
- Tutorial docs should demonstrate `poshmcp doctor` after setup/config steps, especially where `Modules`, `IncludePatterns`, auth, or doctor-visible runtime settings are involved.
- Auth docs must preserve real schema facts: API key dictionary keys are the API key values; command overrides use PowerShell command names; `RequiredRoles` is any-match; `DefaultScheme` selects the active auth handler.
- Docker/docs contract: runtime config path is `/app/server/appsettings.json`, AllUsers module path is `/usr/local/share/powershell/Modules`, and `examples/Dockerfile.user` uses `USER root` for copies then returns to `appuser`.
- Release notes must use actual shipping config keys; Cubert rejected v0.11.0 notes when snippets used `PowerShell` instead of `PowerShellConfiguration`.