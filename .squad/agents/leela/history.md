- **20260414T000000Z**: Created conference-ready team introduction content in `docs/articles/talk-team-introductions.md` using project-grounded achievements (dynamic PowerShell-to-MCP tooling, unified `poshmcp` entry point, runspace expertise, observability, test quality, docs education, decisions logging, and queue monitoring) with concise, audience-friendly speaker intros.
- **20260414T000000Z**: ✓ Wired `docs/public/logo.svg` into DocFX build: created `docs/public/` source folder, added `public/logo.svg` to resource files, updated `_appLogoPath` to `public/logo.svg`. Build confirmed `_site/public/logo.svg` present, 0 warnings.
- **20260403T135630Z**: ✓ Docs consistency review (13 files, 2.2K lines deduplicated). Proposal filed & merged into decisions.md.
- **20260414T000000Z**: Updated DocFX branding config to use `poshmcp.svg` via `_appLogoPath` and added SVG to `build.resource.files` so the logo is emitted and referenced correctly in generated docs.
- **20260414T000000Z**: Fixed DocFX homepage `InvalidFileLink` warnings by replacing `api/index.md` references in `docs/index.md` with the published API landing URL `https://usepowershell.github.io/PoshMcp/api/PoshMcp.html`; validated that both index warnings were removed in local build output.
- **20260418T201500Z**: ✓ v0.6.0 Release Notes & Resources/Prompts Documentation — Audited docs for gaps (Resources/Prompts methods and config were undocumented), created comprehensive `docs/articles/resources-and-prompts.md` user guide (4,600 words with configuration, examples, MCP methods, best practices, troubleshooting), added release notes at `docs/release-notes/0.6.0.md`, updated README.md with feature mentions, and added resources-and-prompts to docs/toc.yml. Committed and pushed.
- **20260502T152041Z**: ✓ Entra ID OAuth Implementation Guide (Internal Learning Document) — Documented all OAuth proxy bugs and fixes encountered during Azure Container Apps deployment. Created `docs/entra-id-oauth-implementation-guide.md` (32KB, comprehensive internal reference) covering: (1) OAuth proxy pattern & two-tier auth architecture, (2) complete end-to-end flow with HTTP examples, (3) four critical bugs with root causes (issuer mismatch v0.9.9, scope format config, missing /authorize endpoint v0.9.10, X-Forwarded-Proto reverse proxy scheme), (4) configuration reference with all auth config fields explained, (5) Entra ID app registration checklist, (6) validation checklist with curl examples, (7) debugging tips & manual testing procedures. Used honest first-person "we" voice emphasizing lessons learned. NOT added to toc.yml (internal learning doc only).

- **20260503T061240Z**: ✓ Entra ID OAuth Guide — Bug 5 finalization + documentation (Session) — Integrated MapInboundClaims fix, VS Code scope gotcha, non-refreshable token section, updated checklists and summary. Validated examples against corrected auth implementation.

# Leela — History

**Status:** 35.9 KB (checked 2026-05-03: within 90-day retention, no archival required)



## Project Context (Seeded on Join)

**Project:** poshmcp - Model Context Protocol (MCP) server that dynamically transforms PowerShell scripts, cmdlets, and modules into secure, discoverable AI-consumable tools

**Tech Stack:** .NET 10, C#, PowerShell SDK, OpenTelemetry, ASP.NET Core, xUnit

**Primary User:** Steven Murawski

**Team:** Futurama cast
- Farnsworth (Lead/Architect)
- Bender (Backend Developer)
- Hermes (PowerShell Expert)
- Amy (DevOps/Platform/Azure)
- Fry (Tester)
- Leela (Developer Advocate) ← YOU
- Scribe (Session Logger)
- Ralph (Work Monitor)

**Current Work:** Phase 1 quick wins implemented (health checks, correlation IDs), Azure Container Apps deployment infrastructure created, multi-tenant support added. Ready for Phase 2 (structured error codes, configuration validation, command timeouts) or documentation improvements.



## Learnings

### 2026-05-03: Release-Process Skill — Quality Gate Enforcement

**Task:** Add pre-push quality gates to the release process to prevent shipping with failing tests.

**Background:** A test failure slipped into v0.9.20 release because we skipped running tests before pushing. The skill needed updating to enforce mandatory quality checks.

**What was added:**
- **New step 4: Run quality gates (MANDATORY)** — Added between "Update changelog" and "Leela owns release notes":
  - `dotnet format --verify-no-changes` — verify formatting; fix any issues before proceeding
  - `dotnet test` — all tests must pass; do NOT proceed if any test fails
  - Explicit recovery instruction: if either command fails, fix the issue and restart from step 2
- **Updated YAML description** — Added "quality gates (format+test)" to the release workflow description so the skill summary clearly names this requirement
- **Anti-Pattern entry** — Added "❌ Pushing a release without running `dotnet test` first" to Anti-Patterns list
- **Examples block** — Added the two commands in the correct position (after editing artifacts, before git commit)
- **Step renumbering** — Old steps 4–9 became steps 5–10; post-tag verification now step 10

**Key insight:**
- Release processes must make quality gates **mandatory and visible** — not optional or buried in CI. By placing quality gates as a numbered step with MANDATORY capitalization and explicit recovery instructions, we ensure no human can accidentally skip them. The anti-pattern callout reinforces this behaviorally.

### 2026-05-03: Entra ID OAuth Guide — Bugs 5–7 (Live Debugging Findings)

**Task:** Integrate three new live-debugging findings into `docs/entra-id-oauth-implementation-guide.md`.

**What was added:**
- **Bug 5 — `MapInboundClaims = false`**: ASP.NET Core JWT Bearer middleware silently remaps short JWT claim names (`scp`, `roles`) to long WS-Federation URI forms by default. This caused `RequireClaim("scp", "user_impersonation")` to always fail even with valid tokens — the claim was present but under the wrong key. Fix: set `options.MapInboundClaims = false` and explicitly set `TokenValidationParameters.RoleClaimType`. Added to bugs section after Bug 4, cross-referenced to Bug 2.
- **VS Code `mcp.json` scope field**: An explicit `scope` in VS Code's `mcp.json` causes silent token acquisition failure — VS Code sends requests with no `Authorization` header. Fix: remove the `scope` field and let Protected Resource Metadata drive scope discovery. Added as a new section "VS Code MCP Client Configuration Gotchas" between Configuration Reference and Entra ID App Registration Requirements.
- **Non-Refreshable Tokens**: Federated/guest accounts may not receive refresh tokens, giving ~88-minute token lifetimes with forced re-auth. Added as "Non-Refreshable Tokens (Federated Accounts)" subsection in Debugging Tips.

**Other changes:**
- Added `MapInboundClaims = false` check to Validation Checklist
- Added lessons 5 and 6 to Summary section
- Added safe-implementation bullet points for both new lessons

**Key insights:**
- The `MapInboundClaims` issue is a .NET-ism that affects all JWT Bearer auth — not Entra-specific. It's silent: token validates fine, claim is present, but authorization check fails because the claim is stored under a different key. The only signal is `scp=[]` in AUTHZ DIAG logs.
- The VS Code `scope` field bug is entirely client-side; server logs showing `HasBearerToken=False` on every request is the diagnostic signal. Removing the field lets VS Code use server-advertised metadata correctly.
- Both Bug 2 (short scope names in config) and Bug 5 (`MapInboundClaims = false`) are required together — fixing only one is insufficient.

### 2026-05-01: Consolidated Entra ID Authentication Documentation

**Task:** Consolidate two separate Entra ID auth documents (`entra-id-mcp-auth.md` and `entra-id-auth-guide.md`) into a single comprehensive guide.

**Status:** ✓ Complete. File consolidated at `docs/entra-id-auth-guide.md`, redundant file deleted.

**What was consolidated:**
- **Scope coverage** — `entra-id-mcp-auth.md` was VS Code MCP-specific; `entra-id-auth-guide.md` was general-purpose with App Registration and Managed Identity paths. Kept the existing guide as the canonical doc and folded VS Code-specific content in as a subsection.
- **Key addition: Step 2b** — Added "Authorize Client Applications" step covering VS Code's pre-registered client ID (`aebc6443-996d-45c2-90f0-388ff96faa56`) in the Expose an API flow. This was critical guidance missing from the original guide.
- **New subsection: VS Code MCP Integration** — Added under Path A Testing, explaining: (1) why pre-registered client ID is needed, (2) how VS Code OAuth + PKCE flow works, (3) VS Code settings.json config, (4) Protected Resource Metadata endpoint, (5) VS Code-specific troubleshooting table with error-to-fix mappings.
- **Integrated references** — Consolidated scope naming and PRM configuration guidance from both docs (new file mentioned App Service `WEBSITE_AUTH_PRM_DEFAULT_WITH_SCOPES` env var for on-demand PRM generation; now reflected in guide).

**Inconsistencies resolved:**
- Scope naming: New file used `user_impersonation`, existing used `access_as_server`. Kept `access_as_server` (more descriptive, already used throughout the guide).
- PRM coverage: New file mentioned App Service EasyAuth auto-generation; existing guide covered manual PRM endpoint for self-hosted. Both approaches now documented in the guide.
- Client authorization: New file emphasized pre-registered client ID; existing guide lacked this critical step. Now fully covered in Step 2b.

**Content preserved:** All unique and valuable content from both documents is in the consolidated guide. No useful information was lost.

**Cross-references checked:** Searched docs/ for links to either file. Only reference was in DOCFX-BUILD-SUMMARY.md (doc index, auto-generated). No manual cross-ref updates needed.

**Key insight:**

---
*Further trimmed to 100 lines on 2026-05-05 by Scribe (15KB gate). Full record in `history-archive.md`.*
