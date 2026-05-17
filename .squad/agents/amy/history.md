
- Created `integration/spec-002-mcp-resources-and-prompts` from `main` and merged all 4 feature branches in order.
- `feature/002-resources` merged clean. `feature/002-prompts` conflicted on `Program.cs` — resolved by merging `ConfigureServerServices`/`RegisterMcpServerServices` signatures to accept both handlers, and chaining all 4 `With*Handler` calls in HTTP and stdio paths.
- `feature/002-doctor` had add/add conflicts on all 5 config model files (it defined its own nullable-property versions). Kept HEAD (implementation branch) non-nullable versions; validator `IsNullOrWhiteSpace` checks are compatible with both.
- `feature/002-tests` merged clean.
- Build: `dotnet build PoshMcp.sln --no-incremental` → **succeeded**, 5 pre-existing warnings in `McpToolFactoryV2.cs` (unrelated to Spec 002).
- Branch pushed to `origin`.
- Key lesson: when 3+ branches all modify `Program.cs` service registration, the standard pattern is to merge signatures by adding parameters for each feature's handler/config, then chain all handlers together.

### 2026-05-07 - v0.11.0 minor release cut
- Bumped PoshMcp.Server/PoshMcp.csproj Version 0.10.0 -> 0.11.0.
- Added [0.11.0] CHANGELOG entry above 0.10.0. Marquee: out-of-process subprocess pool (Pool now default SubprocessHostMode, #196). Also: ProcessPool mode, OutOfProcessHost extraction, OOP cancellation propagation (#188), PoshMcp.Benchmarks harness, ConvertTo-Json wrap (#203), $Error clear (#189), CWE-117 log scrubbing in OOP host, CI minimum permissions + SECURITY.md, docs (#210).
- Did NOT tag/push - Steven runs the tag after Cubert reviews release notes.
- Did NOT touch SECURITY.md or docs/release-notes/ - Leela has those in flight.
- Build verified: dotnet build PoshMcp.sln -c Debug -> 0 errors, 19 pre-existing nullable warnings.

### 2026-05-07 - Lockout-revision: fix config key in 0.11.0 release notes (Cubert rejection)
- Took over from Leela (locked out per Reviewer Rejection Protocol).
- Cubert flagged: both jsonc snippets in 'Upgrade Notes' used "PowerShell" as top-level key; correct binding section is "PowerShellConfiguration" (verified in PoshMcp.Server/appsettings.json).
- Replaced "PowerShell" -> "PowerShellConfiguration" in both snippets (opt-out Single example + opt-in ProcessPool example). No other changes.

### 2026-05-11 - v0.12.0 release: Doctor resilience + proxy cmdlet support
- **Version bump:** 0.11.0 → 0.12.0 (minor). Doctor resilience + WinPSCompat proxy support both qualify as feature-level additions; no breaking changes.
- **Release notes:** Created `docs/release-notes/0.12.0.md` covering (1) resilient Doctor command with error handling, (2) proxy cmdlet discovery via cached-delegate generation for >16-parameter methods, (3) integration test coverage, (4) no upgrade notes required.
- **Release workflow:** (1) Created release notes file, committed before tag (`git commit -m "docs: Release notes for v0.12.0"`), (2) bumped version in PoshMcp.Server/PoshMcp.csproj `<Version>` tag, committed (`git commit -am "chore: Bump version to 0.12.0"`), (3) created annotated tag (`git tag -a v0.12.0 -m "Release v0.12.0"`), (4) pushed main branch then tag separately (`git push origin main` → `git push origin v0.12.0`). **Critical:** Release notes must be committed BEFORE tag creation per charter constraint.
- **Key learnings:** (a) Release notes commit is a gate; tag creation depends on it. (b) Version bumps and release notes are independent commits (enables cherry-pick/revert granularity). (c) Git push of branch and tag are separate operations. (d) PR #211 (proxy support) + doctor branch (resilience) merged cleanly; no conflicts. (e) Semantic versioning decision: feature-level work → minor bump (vs. patch).
- **Commits shipped:** Release notes (ff8997f), version bump (40e4a56); tag v0.12.0 created and pushed.

### 2026-05-11 - v0.12.1 patch release: Code formatting cleanup
- **Hotfix patch:** 0.12.0 → 0.12.1 (formatting/maintenance only).
- **What happened:** dotnet format discovered and fixed trailing whitespace and spacing inconsistencies (single change: collection expression spacing in DoctorService.cs).
- **Release notes:** Created lightweight docs/release-notes/0.12.1.md documenting patch as 'maintenance' with no functional changes.
- **Release workflow:** (1) Amended HEAD commit message to 'chore: Code formatting cleanup', (2) bumped version in PoshMcp.Server/PoshMcp.csproj 0.12.0 → 0.12.1, committed ('chore: Bump version to 0.12.1'), (3) created release notes, committed ('docs: Release notes for v0.12.1'), (4) created annotated tag (git tag -a v0.12.1 -m 'Release v0.12.1 — patch release'), (5) pushed main and tag.
- **Key learnings:** (a) Formatting-only releases are valid patch cycles — tooling (dotnet format) finds regressions/inconsistencies that are worth capturing in version history. (b) Amend HEAD if formatting commit landed with incorrect message (no need to rebase or force-push if only local). (c) Patch releases don't require detailed upgrade notes — single-line 'maintenance/cleanup' suffices. (d) Hotfix workflow is fast: one or two functional commits + lightweight release notes + tag + push.
- **Commits shipped:** Formatting cleanup (c9e67b2), version bump (ef50162), release notes (554d9ce); tag v0.12.1 created and pushed.

## 2026-05-14: Spec 009 closed via this session

Spec 009 (Test Suite Consistency and Fast Unit Tier) is functionally complete. Five PRs merged in the closeout wave (#252, #253, #257, #259, #260) and six issues closed (#213, #214, #215, #216, #220, #221). Issue #221 acceptance gate (Fry) measured the Unit tier at 432 passed / 0 failed / 0 skipped across 5 consecutive runs, mean 20.45s wall-clock — well under the <60s FR-419 budget. Your contribution: see your own history entries for this session.


## 2026-05-15: Team update (via Scribe)
**Ralph round 1 — 3 PRs in-flight, may need your review:**
- **PR #266** (Bender, issue #261): Doctor pool display sentinel — EffectiveProcessPoolSize / EffectiveMinHealthyForStartup promoted to `string`, returning `"n/a (<mode> mode)"` when inert. Files: `DoctorService.cs`, `DoctorReport.cs`, `DoctorTextRenderer.cs` + Unit tests.
- **PR #264** (Hermes, issue #262): AAD v2.0 `preferred_username` mapping — added `ClaimsMapping.NameClaim` to `AuthenticationConfiguration`; wires to `JwtBearerOptions.TokenValidationParameters.NameClaimType`. Null preserves default (no behavior change for existing deployments). Files: `AuthenticationConfiguration.cs`, `AuthenticationServiceExtensions.cs`, `docs/entra-id-auth-guide.md`.
- **PR #265 DRAFT** (Farnsworth, issue #263): Spec 011 design-only — `specs/011-doctor-module-imports/spec.md` (13 FRs / 4 SCs / 5 OQs). Implementation split to follow-up issues #267 (Bender) and #268 (Hermes).

## Learnings

### 2026-05-16: Release v0.14.1
- Version file: PoshMcp.Server/PoshMcp.csproj `<Version>` element
- Release notes path: docs/release-notes/{version}.md
- Release notes must be committed before tag push (charter gate)
- Used `gh release create` with --notes-file for release body

## 2026-05-17T13:12:00Z: Cross-team update — Log-forging fix #277

Bender completed remediation of 24 CodeQL cs/log-forging alerts across PowerShellAssemblyGenerator.cs, AuthenticationServiceExtensions.cs, and LoggerExtensions.cs. Pattern: LogSanitizer.Scrub() applied to all untrusted sources (correlation IDs, JWT claims, config values) at structured log call sites. Build + tests pass. PR #278 open.