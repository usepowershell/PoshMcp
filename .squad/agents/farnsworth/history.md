# Farnsworth — Lead/Architect — Work History

## Current Summary (compacted 2026-06-01T00:00:00Z)
Detailed prior architecture and review entries were archived to `history-archive.md` because this file exceeded the 15KB Scribe hard gate. Keep this file focused on active architectural decisions and review patterns.

## Learnings
- Spec 012 noun-resource architecture: resource config types live under `McpResources`; noun resource overrides key by default snake_case resource name; noun-resource validation runs during PowerShell config load because it needs a logger.
- Noun-derived resources require a parameterless `Get-*` backing command; static/custom resources take precedence on URI collision; explicit `AssociatedResourceUri` is a command override resolved against the exposed resource surface at registration time.
- Report/provenance seams must reach every production report builder, not just CLI doctor. Runtime status/troubleshooting surfaces are first-class doctor consumers.
- AsyncLocal capture is acceptable for one-shot CLI discovery flows when reset at flow start, set before disposal, and documented; avoid it for long-running multi-flow state.
- Reviewer rejection lockout remains strict: original artifact authors do not revise rejected artifacts.
- Before presenting Farnsworth-authored plans/specs/proposals to Steven, Cubert must fact-check them.