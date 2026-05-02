# Work Routing

How to decide who handles what.

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|----------|
| Architecture & design decisions | Farnsworth | System design, API design, patterns, trade-offs |
| C# / .NET implementation | Bender | Error handling, circuit breakers, retry logic, APIs |
| PowerShell integration | Hermes | Runspace management, command execution, PowerShell-specific patterns |
| Observability & monitoring | Amy | OpenTelemetry metrics, health checks, diagnostics, logging |
| Azure cloud & deployment | Amy | Azure resources, Container Apps, App Service, Application Insights, Key Vault |
| Release management | Amy (primary) + Leela (release notes) | "release", "version bump", "prepare release", "tag release", "publish release" |
| Testing & quality | Fry | Unit tests, integration tests, performance baselines |
| Documentation & guides | Leela | Technical writing, tutorials, examples, developer experience |
| Code review | Farnsworth | Review PRs, check quality, architectural alignment |
| Testing & validation | Fry | Verify implementations, edge cases, test coverage |
| Scope & priorities | Farnsworth | What to build next, trade-offs, roadmap decisions |
| Session logging | Scribe | Automatic — never needs routing |
| Work monitoring | Ralph | Automatic — monitors backlog and work queue |

## Domain Overlap

When work crosses domains, spawn relevant agents in parallel:
- **Error handling + PowerShell** → Bender + Hermes
- **Metrics + Health checks** → Amy (primary)
- **New API + Tests** → Bender (implement) + Fry (test cases)
- **Architecture + Implementation** → Farnsworth (design) → Bender (code)
- **New feature + Documentation** → Implementation agent + Leela (docs/examples)

## Issue Routing

| Label | Action | Who |
|-------|--------|-----|
| `squad` | Triage: analyze issue, assign `squad:{member}` label | Farnsworth |
| `squad:farnsworth` | Architecture, design, major decisions | Farnsworth |
| `squad:bender` | C# implementation, backend logic | Bender |
| `squad:hermes` | PowerShell-specific work | Hermes |
| `squad:amy` | Metrics, monitoring, health | Amy |
| `squad:fry` | Testing, quality checks | Fry |
| `squad:leela` | Documentation, tutorials, examples | Leela |

### How Issue Assignment Works

1. When a GitHub issue gets the `squad` label, the **Lead** triages it — analyzing content, assigning the right `squad:{member}` label, and commenting with triage notes.
2. When a `squad:{member}` label is applied, that member picks up the issue in their next session.
3. Members can reassign by removing their label and adding another member's label.
4. The `squad` label is the "inbox" — untriaged issues waiting for Lead review.

## Rules

1. **Eager by default** — spawn all agents who could usefully start work, including anticipatory downstream work.
2. **Scribe always runs** after substantial work, always as `mode: "background"`. Never blocks.
3. **Quick facts → coordinator answers directly.** Don't spawn an agent for "what port does the server run on?"
4. **When two agents could handle it**, pick the one whose domain is the primary concern.
5. **"Team, ..." → fan-out.** Spawn all relevant agents in parallel as `mode: "background"`.
6. **Anticipate downstream work.** If Bender is implementing a feature, spawn Fry to write test cases from requirements simultaneously.
7. **Issue-labeled work** — when a `squad:{member}` label is applied to an issue, route to that member. Farnsworth handles all `squad` (base label) triage.
8. **Release requests require release skill context first.** For "release", "version bump", "prepare release", "tag release", or "publish release" work, the assigned agent must read `.squad/skills/release-process/SKILL.md` before executing tasks. Leela owns release notes and must finalize them before tag/publish steps.
