# Cubert — History

## Current Summary (compacted 2026-06-01T00:00:00Z)
Detailed prior fact-check entries were archived to `history-archive.md` because this file exceeded the 15KB Scribe hard gate. Keep this file focused on current verification rules and recent review memory.

## Learnings
- Fact-check executable docs and JSON snippets against actual shipping config keys before approval. Default-flip upgrade snippets are user-facing executable content; wrong top-level keys are blocking defects.
- For PR reviews, verify claims against source, tests, and live output where relevant; do not treat PR prose as authoritative.
- OOP/doctor provenance claims must cover both CLI doctor and runtime report surfaces such as `get_configuration_status` and troubleshooting output.
- For tutorial and auth docs, preserve the `RequiredRoles` any-match / `RequiredScopes` all-match distinction, and verify examples against `AuthorizationHelpers` and real config shapes.
- When a spec is referenced but absent on disk, recover it with `git log --all -- <path>` and `git show <sha>:<path>` rather than guessing.
- GitHub review identity may be blocked from formal approvals on usepowershell-authored PRs; comment-form verdicts should still include agent attribution.