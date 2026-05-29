
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

### 2026-05-29T11:46:53.2558064-05:00: v0.16.2 release prep blocked by pre-existing format drift
- Built a clean release worktree from `origin/main` to avoid including local non-release changes, then copied only the intended v0.16.2 release files into it.
- Full `dotnet format --verify-no-changes` failed on existing whitespace drift in `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs` and `PoshMcp.Benchmarks/Scenarios/PayloadSizeSerializationBenchmark.cs`, both outside the approved release file set.
- Release rule: do not stage unrelated format files into a scoped release commit without explicit approval; stop before commit/push/tag when the mandatory format gate fails.

### 2026-05-29T11:46:53.2558064-05:00: Azure Container App MCP initialize diagnostics
- Live ACA `ca-poshmcp` health/readiness can be healthy while MCP initialize appears stuck because auth/path negotiation happens before tool execution. Current HTTP transport maps MCP at `/` when `POSHMCP_MCP_PATH`/`--mcp-path` is unset; `/mcp` returns 404.
- For authenticated ACA deployments, verify `.well-known/oauth-protected-resource`, `.well-known/oauth-authorization-server`, token consent for the advertised `api://.../user_impersonation` scope, and CORS preflight headers for browser/WebView clients before suspecting port or cold-start issues.
- Generic ARM `az resource show --resource-type Microsoft.App/containerApps --api-version 2023-05-01` was more reliable than `az containerapp show` when the Container Apps CLI extension hit management-plane connection issues.
- When ACA ARM commands fail intermittently with local socket errors (`WinError 10051` unreachable network or `WinError 10048` socket exhaustion), use Resource Graph to confirm resource existence and resolve workspace/customer IDs, then retry Log Analytics with the workspace ID directly. This distinguished CLI/network failure from resource/RBAC mismatch for `ca-poshmcp`.
- Post-auth-removal validation pattern: confirm auth state from `/health` configuration data (`AuthEnabled:false`, `AuthSchemes:none`), then prove MCP root behavior with unauthenticated `initialize` over both HTTP/1.1 and .NET HTTP/2. For `ca-poshmcp--0000009`, `/` returned SSE initialize success while `/mcp` returned 404; system logs showed only rollout/startup probe connection-refused events before traffic settled.
- If VS Code reports `TypeError: fetch failed` while AppRequests show successful `POST /` at the same timestamp, treat it as client/connection handling after ingress unless logs show a matching app exception. A local live probe can also fail before ACA with Windows socket exhaustion (`Only one usage of each socket address...`), producing no new POST rows.

### 2026-05-17T08:25:00-05:00: PR #278 final log-forging cleanup
- Fixed the remaining unsanitized structured log field in `PoshMcp.Server/PowerShell/PowerShellAssemblyGenerator.cs` by wrapping `convertedValue?.GetType().Name` for the `ValueType` field with `LogSanitizer.Scrub()`.
- Pattern: every dynamic string flowing into `LogInformation`, `LogWarning`, `LogError`, `LogDebug`, or `LogTrace` must be scrubbed at the call site, even when it looks low-risk (like a runtime type name).

### 2026-05-16: Release v0.14.1
- Version file: PoshMcp.Server/PoshMcp.csproj `<Version>` element
- Release notes path: docs/release-notes/{version}.md
- Release notes must be committed before tag push (charter gate)
- Used `gh release create` with --notes-file for release body

## 2026-05-17T13:12:00Z: Cross-team update — Log-forging fix #277

Bender completed remediation of 24 CodeQL cs/log-forging alerts across PowerShellAssemblyGenerator.cs, AuthenticationServiceExtensions.cs, and LoggerExtensions.cs. Pattern: LogSanitizer.Scrub() applied to all untrusted sources (correlation IDs, JWT claims, config values) at structured log call sites. Build + tests pass. PR #278 open.

## 2026-05-19: Release v0.15.0 prep pushed, awaiting final CI gate
- Cut release prep from local `main` after Leela's release-notes gate work.
- Added the final version bump in `PoshMcp.Server/PoshMcp.csproj` (`0.14.2` -> `0.15.0`) and finalized `docs/release-notes/0.15.0.md` release date.
- Kept the release-prep commit scoped to release artifacts only: `PoshMcp.Server/PoshMcp.csproj`, `CHANGELOG.md`, `docs/release-notes/0.15.0.md`, `docs/toc.yml`.
- Explicitly excluded unrelated local changes from the release commit: `.squad/agents/leela/history.md`, `docs/release-notes/0.14.1.md`, and untracked `docs/logging-and-metrics.md`.
- Local quality gates passed before push: `dotnet format PoshMcp.sln --verify-no-changes` and `dotnet test PoshMcp.sln` (`914` passed, `0` failed).
- Release-prep commit: `a9f262a` (`chore(release): v0.15.0`), pushed to `origin/main`.
- Remote checks observed green for `submit-nuget`, CodeQL analyzers, `release`, preview package publish, and docs deploy. Tag creation was intentionally withheld while the main `CI` workflow's `build` job remained in progress on GitHub (integration stage still running at handoff).

## 2026-05-19: Release v0.15.0 shipped
- Verified release-prep commit `a9f262a` was already on `origin/main` and that the release artifact set included `docs/release-notes/0.15.0.md`, `CHANGELOG.md`, `PoshMcp.Server/PoshMcp.csproj`, and `docs/toc.yml` before tag publication.
- Waited for the final required GitHub Actions gate on commit `a9f262a891afb50e977e132f003588e0f2ef4758`. Final check set: `submit-nuget`, `Analyze (csharp)`, `Analyze (python)`, `Analyze (actions)`, `deploy`, `build`, `release`, and `Build & Publish Preview NuGet Package` all completed with `success`.
- Created annotated tag `v0.15.0` on commit `a9f262a891afb50e977e132f003588e0f2ef4758` and pushed it to `origin`.
- Verified the remote annotated tag object exists at `refs/tags/v0.15.0` and dereferences via `refs/tags/v0.15.0^{}` to the intended release commit `a9f262a891afb50e977e132f003588e0f2ef4758`.
- Observed a non-blocking GitHub Actions annotation during CI completion: Node.js 20 deprecation warning for marketplace actions (`actions/checkout@v4`, `actions/setup-dotnet@v4`, `actions/upload-artifact@v4`). Release still shipped successfully; workflow maintenance follow-up is separate from the release cut.

## 2026-05-29T11:46:53.2558064-05:00: v0.16.1 release tag and CI phase split
- Verified release commit `b37f4857a42651d908eafe86dd98b026fbec0279` had green GitHub checks and committed release notes before creating tag `v0.16.1`.
- For CI phase splitting, keep job id `build` unchanged so existing required-check configuration is preserved; put Unit in that job and fan out longer category tests with `needs: build`.
- When Azure credentials are absent, condition the Azure artifact upload on `HAS_AZURE_CREDS == 'true'` so a skipped Azure test does not produce a missing-artifact warning.