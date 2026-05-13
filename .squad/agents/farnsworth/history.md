# Farnsworth — Lead/Architect — Work History

## Project Context
**Project:** PoshMcp — Model Context Protocol (MCP) server for PowerShell
**Tech Stack:** .NET 10, C#, PowerShell SDK, OpenTelemetry, ASP.NET Core, xUnit
**Primary User:** Steven Murawski

## Pre-2026-05-12 Summary (archived to history-archive.md on 2026-05-13)
**Pre-2026-05-02:** see history-archive.md (Spec 003/004/005 restructure; PR #130 nullable MimeType; Spec 006 milestone #3 with 27 issues; PR #167 Doctor Output Restructure approved).
- 2026-05-02: Reviewed PR #184 (Program.cs refactor / spec 002 prompts+resources).
- 2026-05-06: Authored SECURITY.md (private vuln reporting + supported-versions). Reviewed Spec 004 wave: PR #187 (experiment plan), PR #200 (Bender Option B / ProcessPool), PR #201 (Hermes Option A / Pool — chosen as enum baseline), PR #202 (Fry harness wiring; surfaced ConvertTo-Json shadowed-member bug → #203/#204), PR #204 (Bender fix), PR #205 (Hermes findings). Recommended Pool as default with cancellation propagation as a hard gate. Security alerts triage: 25 log-forging + 1 missing workflow perms — defined LogSanitizer pattern at call-site (not enricher) to clear CodeQL.
- 2026-05-06: PR #207 (Bender — cancellation across Single/Pool/ProcessPool) approved; both #196 hard gates (custom PSHost from #201, cancellation from #207) closed.
- 2026-05-07: PR #210 (Leela — OOP docs + samples audit) reviewed. v0.11.0 release shipped (Pool default flip).
**Patterns to remember:**
- EMU gh pr review blocked from this account; use gh pr comment (not a formal approval — Steven must convert if branch protection requires).
- When a default flip is questioned, audit ALL direct construction sites of the affected type (grep for 
ew TypeName).
- Benchmark harnesses surfacing concurrency races on day one are doing their job — land harness, file race separately, don't hold harness PR hostage.
- PowerShell ConvertTo-Json failures with ArgumentException: ... Key: <name> → suspect CLR member shadowing on the input type before suspecting a race.
- Surfacing hardcoded values (e.g. 30s timeout) in doctor reports — even before they're config knobs — signposts the eventual configuration surface.
- Spot-check 2-3 headline numbers in docs+data PRs against source tables (catches arithmetic + rounding inversions).

- 2026-05-12: Authored specs/009-test-suite-consistency/spec.md. Full suite flake (~668 tests, 6min) traced to OS-level resource contention (port reuse, pwsh handle leak, temp-dir collisions) — parallelization is already off. Recommended trait-based phasing (Option 1) + per-test resource hygiene audit (Option 3) as first step; deferred project split (Option 2) and drain fixtures (Option 4) until measured. Hard user requirement: unit tier must run in <60s, no subprocesses, no ports.

### 2026-05-12 — Spec 009 accepted, milestone + 10 issues filed
- Resolved 7 open questions on specs/009-test-suite-consistency/spec.md; status flipped Proposed → Accepted (2026-05-12).
- New FRs encoding resolutions: FR-416 (Functional→Integration rule, OQ-3), FR-417 (untagged → default bucket, not Unit, OQ-2), FR-418 (dedicated CI flake-rate step N=5, OQ-7), FR-419 (reference machine = maintainer's primary dev machine, OQ-1).
- New Non-Goals: Azure category in CI (OQ-4 deferred), Option 4 cooldown duration (OQ-6 blocked on OQ-4), analyzer for Trait presence (OQ-5 dropped).
- Milestone: "Spec 009: Test Suite Consistency" — creation BLOCKED by EMU policy (gh api POST → HTTP 404, same pattern as gh issue create blocked since 2026-05-06). Staged at C:\Users\stmuraws\AppData\Local\Temp\poshmcp-spec009 with create-all.ps1 for manual run from non-EMU context.
- 10 issues drafted (bodies in staging dir):
  1. Add Category traits to all tests (FR-400, FR-406, FR-417)
  2. Reclassify misfiled Unit/* (FR-401/402/403/414)
  3. Document per-category local commands TESTING.md (FR-408)
  4. CI: split into category-scoped phases (FR-409)
  5. CI: dedicated flake-rate step (FR-418 / OQ-7)
  6. Hygiene: dynamic ports (FR-411)
  7. Hygiene: pwsh subprocess teardown (FR-412)
  8. Hygiene: unique temp dirs (FR-403/410)
  9. Functional→Integration rule applied (FR-416 / OQ-3)
  10. Unit-tier acceptance gate <60s 5x clean (SC-100/101, FR-404/405) — blocked by #1, #2, #6, #7, #8.
- Trade-offs accepted: permissive default bucket (no strict analyzer); hard Functional rule over case-by-case; Azure CI deferred; drain fixture deferred behind hygiene-audit results.
- Pattern: EMU blocks all repo-modifying gh API calls (issues, milestones, PR reviews). For multi-resource setup, draft all bodies to a temp dir + create-all.ps1 script so user can fire them from a non-EMU shell in one shot.
