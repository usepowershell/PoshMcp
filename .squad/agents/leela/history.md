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

### 2026-05-07: OOP Docs + Samples Audit (PR #210)

**Task:** Audit whether spec 004 OOP changes (default flip to Pool, SubprocessHostMode taxonomy, pool sizing knobs, cancellation contract) reached the configuration docs under `./docs` and the sample `appsettings.json` files.

**Verdict:** Docs — gaps (substantive). Samples — partial.

**What needed updating:**
- `docs/articles/advanced.md` — Out-of-Process section was stale: only mentioned `POSHMCP_RUNTIME_MODE=out-of-process` (wrong casing), no `SubprocessHostMode`, no Pool default, no sizing knobs, no cancellation contract.
- `docs/articles/configuration.md` — full `appsettings.json` reference omitted `RuntimeMode` and `SubprocessHostMode` entirely.
- `docs/articles/azure-integration.md` — described `RuntimeMode` as "sync/async". It's `InProcess` / `OutOfProcess`.
- `examples/appsettings.advanced.json` — no PowerShell runtime tuning, despite loading `Az.*` modules.
- `examples/appsettings.tenant.json` — no PowerShell runtime tuning, despite the multi-tenant trust-boundary use case being exactly what `ProcessPool` exists for.

**What I left alone:**
- `examples/appsettings.basic.json` — purpose is simple/learning. Adding OOP config there changes the sample's purpose.
- `PoshMcp.Server/default.appsettings.json`, `appsettings.modules.json`, `appsettings.azure.json`, `appsettings.environment-example.json` — loaded by dev server / tests in source builds. Out of audit scope; would silently change dev runtime.
- `README.md` / `DOCKER.md` — already updated in #208.
- `docs/release-notes/` — release notes for the default flip belong with the release that ships it.

**Key insight — when to extend an existing doc vs add a new one:**
The task brief offered the option of creating a new "Out-of-Process Execution Modes" article. I extended `advanced.md` instead because (a) the existing OOP section was the natural home for the topic, (b) `configuration.md` already had a full `appsettings.json` reference that needed the `RuntimeMode`/`SubprocessHostMode` row added regardless, and (c) cross-linking from the brief reference in configuration.md into the deep-dive in advanced.md gives readers two natural entry points without duplicating content. Net result: one rewritten section + one new section + one fix, no new TOC entry, no content split across two articles.

**Key insight — sample-rationale belongs in the README, not as JSON comments:**
`appsettings.json` doesn't support comments. To explain why advanced.json picks `Pool` and tenant.json picks `ProcessPool`, I added a "Runtime mode" note to each sample's section in `examples/README.md` rather than trying to encode rationale in key names or duplicate the schema docs. This keeps the samples copy-pasteable as-is.
### 2026-05-01: v0.9.4 Release Notes (Bug Fix — OAuth 2.1 Discovery for VS Code)

**Task:** Write release notes for PoshMcp v0.9.4 fixing OAuth 2.1 discovery for VS Code MCP clients.

**Status:** ✓ Complete. Files created/updated:
- `docs/release-notes/0.9.4.md` — Release notes with detailed RFC 9728 context
- `docs/entra-id-auth-guide.md` — Added specific troubleshooting row for the RFC 9728 header issue
- `docs/toc.yml` — Added missing v0.9.1, v0.9.2, v0.9.3, v0.9.4 to release notes section

**What was documented:**

**Release Notes Structure:**
- **Overview** — Clear statement: VS Code couldn't complete OAuth because PoshMcp's 401 was missing RFC 9728 `resource_metadata` header, causing redirect to PoshMcp's own `/authorize` instead of Entra ID
- **Bug 1 (High): VS Code OAuth Discovery** — Detailed four-step failure chain: (1) missing header, (2) can't discover PRM, (3) assumes PoshMcp is auth server, (4) constructs non-existent `/authorize`. Explained the fix: now emits `WWW-Authenticate: Bearer resource_metadata="{scheme}://{host}/.well-known/oauth-protected-resource"` enabling VS Code to discover PRM → read `authorization_servers` → redirect to correct Entra ID endpoint.
- **Bug 2 (Low): ApiKey Handler Metadata URL** — Invalid `api://` URI scheme now replaced with HTTP URL derived from request context.
- **Compatibility section** — Emphasized RFC 9728 compliance ensures forward compatibility with future MCP clients.

**Documentation Fixes:**
- **Entra ID Auth Guide Update:** Enhanced troubleshooting table with two distinct rows:
  1. New specific row for v0.9.3 and earlier: "VS Code redirects to `/authorize` instead of Entra ID" → "Upgrade to v0.9.4"
  2. Existing row reframed as post-v0.9.4: "PRM is misconfigured or missing (post-v0.9.4)" to distinguish versioning
- **TOC Update:** Added missing release notes entries (v0.9.1, v0.9.2, v0.9.3) so the entire 0.9.x line is discoverable; placed v0.9.4 at top of release notes list (newest first).

**Key Insights:**
- **RFC context is essential for auth fixes** — This bug wasn't just "header missing," it was an RFC 9728 compliance gap. Explaining the standard (and why VS Code depends on it) helps users understand that the fix enables standards-based discovery, not just a workaround.
- **Two-row troubleshooting tables for versioned bugs** — When a bug is fixed in a specific release, the troubleshooting table should have one row for pre-fix versions (with "upgrade" solution) and potentially a separate row for post-fix versions (with deeper diagnostic steps). This prevents users on older versions from getting stuck in post-fix diagnostic loops.
- **TOC maintenance is part of release work** — If release notes files exist but aren't in the TOC, they're invisible to documentation readers. Steven's directive (v0.8.0 reminder) to always wire release notes into TOC should also catch historical gaps — backfill TOC entries whenever you add a new release to avoid fragmenting docs.

---

### 2026-05-01: v0.9.2 Release Notes (Security Patch — Authentication Bypass)

**Build sanity:** `dotnet build PoshMcp.sln -c Debug` — green; only pre-existing nullable warnings.

**PR:** #210 (do not merge — pending Steven's review).

### 2026-05-07: v0.11.0 Release Notes + SECURITY.md support matrix

**Task:** Write release notes for v0.11.0 and bump SECURITY.md supported-versions matrix to the new minor line.

**What I did:**
- Created docs/release-notes/0.11.0.md. Followed the established format (H1 `# PoshMcp v0.11.0 Release Notes`, `## What's New` / `## Bug Fixes` / `## Upgrade Notes` sections, fenced jsonc blocks for config samples, neutral published-docs voice).
- Lead story: OOP execution maturity. `Pool` is now the default `SubprocessHostMode` (was `Single`), backed by the new benchmarks harness data (~4.86x warm-invoke throughput at concurrency 10). New `ProcessPool` topology for trust-boundary / tail-latency workloads. Cancellation now propagates across the OOP boundary in both Pool and ProcessPool modes.
- Other sections: `PoshMcp.Benchmarks` harness, security hardening (LogSanitizer for CWE-117 in OOP host, min workflow permissions, published SECURITY.md), bug fixes (`ConvertTo-Json` Content shadowing, `` clear-before-invoke), spec 004 doc work.
- **Upgrade Notes** explicitly call out the default flip and provide a copy-paste opt-out snippet (`"SubprocessHostMode": "Single"`) plus an opt-in ProcessPool snippet for multi-tenant scenarios. No breaking protocol/CLI/schema changes.
- Updated SECURITY.md supported-versions table: `0.11.x` now supported, `< 0.11` now unsupported. Replaced the prior `0.10.x` line.
- Wrote decision entry to `.squad/decisions/inbox/leela-0.11.0-release-notes.md`.

**What I left alone:** CHANGELOG.md and PoshMcp.csproj (Amy owns the version bump). Other docs untouched — release notes are the right surface for the default-flip narrative; deep config docs were already updated under #210.

**Key insight - upgrade notes for default flips:**
A default-value change is technically non-breaking (no API surface moves) but behaviorally breaking for any user relying on the old default. The release notes have to (a) name the change explicitly under Upgrade Notes, (b) explain *why* it might matter (here: shared runspace state across requests in a pool), and (c) give a copy-paste snippet to restore prior behavior. Burying the change under "What's New" without an opt-out path is what generates GitHub issues a week after release. The opt-in ProcessPool snippet is bonus value: users skimming the upgrade notes also see the better answer for their multi-tenant scenarios.

**Cubert to review.**
**What was written:**
- **Security Fixes section** — CVE-2026-40894, OpenTelemetry.Api 1.15.1 → 1.15.3, moderate DoS via BaggagePropagator/B3Propagator memory allocation, resolution statement.
- Used a CVE detail table (CVE, Severity, Affected component, Impact, Resolution) for scannability.
- Explicitly noted no code/config changes required — important for ops teams assessing upgrade risk.
- Breaking Changes: None. Upgrade Notes: patch upgrade, no migration.

**Style decisions:**
- Security patch releases get a "Security Fixes" top-level section (not "What's New") so it's immediately visible.
- Omitted the local-sync merge commit (b0a80e4) — not user-facing.
- TOC entry inserted above v0.8.3 (newest first).



### 2026-04-24: v0.8.3 Release Notes

**Task:** Replaced stub `docs/release-notes/0.8.3.md` with proper release notes for PoshMcp v0.8.3.

**What was written:**
- **Deploy Script: Source Image Support** — full coverage of the new `-SourceImage` / `-UseRegistryCache` parameters from spec 007 (Modes A, B, C), user-benefit framing (skip local builds, artifact promotion, ACR import for bandwidth savings), plus parameter table and backward-compatibility callout.
- **Code Quality Improvements** — brief user-facing mention of the refactor commit framed around reliability rather than internal structure.
- **Breaking Changes** — explicitly none.
- **Upgrade Notes** — install instructions and confirmation that no config migration is needed.

**Style decisions:**
- Matched v0.8.0 format exactly (uid frontmatter, `## What's New` subsection headers, Breaking Changes and Upgrade Notes always present, See Also links).
- Omitted squad-internal commits (session-recall skill, scribe logs, decisions.md merges) — not user-facing.
- Used parameter table for quick reference on the two new flags (mirrors what users would scan for first).

### 2026-04-24: update-config CLI Docs Cleanup

**Task:** Reviewed and corrected markdown examples that used obsolete `poshmcp update-config` switches.

**What was updated:**
- Replaced deprecated command flags in examples: `--add-function`/`--remove-function` -> `--add-command`/`--remove-command`.
- Replaced removed module flags in examples: `--add-import-module` and `--add-install-module` -> `--add-module`.
- Removed unsupported `update-config` examples for `--trust-psgallery`, `--skip-publisher-check`, `--install-timeout-seconds`, and `--add-module-path`.
- Updated affected docs to either show valid `update-config` usage or point to `appsettings.json` for `PowerShellConfiguration.Environment` settings.

**Validation:**
- Cross-checked supported options against `PoshMcp.Server/Program.cs` (`updateConfigCommand.AddOption(...)`).
- Searched all markdown for obsolete `update-config` switches and confirmed no remaining matches.

### 2026-04-24: Docker Build Semantics Docs Alignment

**Task:** Updated Docker-related documentation to match current `poshmcp build` semantics after source-image workflow changes.

**Files updated:**
- `README.md`
- `DOCKER.md`
- `docs/articles/docker.md`
- `examples/README.md`

**What was aligned:**
1. Documented that `poshmcp build` defaults to `--type custom` and layers from GHCR base image (`ghcr.io/usepowershell/poshmcp/poshmcp:latest`).
2. Added explicit `--type base` examples for local/source base image builds from repo `Dockerfile`.
3. Added `--source-image` and `--source-tag` usage and clarified tag override behavior for custom builds.
4. Removed/rewrote outdated Docker article patterns that implied old build args (`MODULES`, `POSHMCP_MODULES`) as primary guidance.
5. Kept examples concise and consistent across root docs and examples docs to avoid contradictory defaults.

**Validation:**
- Performed targeted searches across updated docs to verify `poshmcp build` references now consistently reflect custom-by-default behavior and include source/base override options.

### 2026-04-23: Server Configuration Documentation for Azure Deployment

**Task:** Added comprehensive documentation for the new `-ServerAppSettingsFile` feature in `deploy.ps1`.

**What Was Documented:**
- New section: "Server Configuration with `-ServerAppSettingsFile`" in `docs/articles/azure-integration.md`
- Placed after "Scaffold Then Deploy" section, logically connecting scaffold workflow to deployment with custom config
- Covers: how it works, settings translation, practical examples (basic, environment-specific, integration patterns)
- Cross-links to Configuration and Advanced Configuration articles

**Key Content Added:**
1. **How It Works** — 4-step explanation of the config translation flow
2. **What Gets Translated** — Lists 6 categories (CommandNames, Modules, RuntimeMode, Logging, Auth, Health)
3. **Basic Example** — Copy server config and deploy with `-ServerAppSettingsFile` parameter
4. **Environment-Specific Configuration** — Using different appsettings files for dev/staging/prod
5. **Integration with Scaffold Workflow** — Full end-to-end flow combining scaffold + custom config + deploy

**Documentation Patterns Applied:**
- Consistent tone with existing Azure Integration article (clear, step-by-step, code examples)
- Markdown structure matches doc conventions (numbered steps, code blocks, bullet lists)
- Examples use realistic paths and parameter conventions
- Cross-links point to related docs (Configuration reference, Advanced Configuration)

**Key Note:** The feature enables deployment teams to maintain identical server configuration between local dev and Azure production by passing the server's appsettings.json directly to the deployment script. Reduces configuration drift and simplifies ops workflows.

#

## 2026-03-27: First Assignment - README Revision and Documentation Audit

**Task:** Conducted comprehensive documentation audit and revised root README.md to match GitHub best practices.

**Documentation Findings:**

1. **Tone Inconsistencies Across Project:**
   - DESIGN.md: Aspirational, emoji-heavy (🧠, 🚀, 🔧), vision-focused
   - README.md: Dry, technical, developer-focused but lacked hook/appeal
   - DOCKER.md: Straightforward technical, no embellishment
   - Azure docs: Professional, well-structured, comprehensive
   - Tests README: Uses emojis (📁, ✅), very organized

2. **README.md Gaps Identified:**
   - Missing value proposition/elevator pitch
   - No "wow moment" example at the top
   - Missing badges (build status, version, license)
   - No clear target audience statement
   - Configuration buried deep - hard to find
   - Missing Contributing, License, and Support sections
   - Poor visual hierarchy (wall of text)
   - Generic title didn't convey value

3. **README Revision Approach:**
   - **Structure:** Status → What/Why → Quick Example → Features → Installation → Usage → Docs → Contributing
   - **Voice:** Professional but accessible, developer-focused, benefit-driven
   - **Examples:** Concrete, copy-paste ready, shows immediate value
   - **Links:** Added navigation to deeper documentation
   - **Sections Added:** Contributing, Roadmap, Resources, Support, License, Acknowledgements
   - **Key Principle:** Show value first, details later

**Technical Accuracy Notes:**
- Verified all technical claims against DESIGN.md and existing docs
- Confirmed OpenTelemetry integration from decisions.md
- Verified health check endpoints from Phase 1 implementation
- Confirmed Azure Managed Identity support from azure/README.md
- Validated dual-mode operation (stdio/HTTP) from DOCKER.md

**Documentation Standards Needed:**
- Consistent emoji usage policy (or no emojis)
- Standard README template for sub-projects
- Heading case conventions (sentence vs title case)
- Code block language tag standards
- Link formatting conventions
- Badge/shield standards for status indicators

**Outcome:** Created developer-friendly README with clear value proposition, concrete examples, and comprehensive navigation to detailed docs. Maintained technical accuracy while improving accessibility for new users.



#

## 2026-03-27: Documentation Standards Formalized

**Update:** Documentation standards proposal submitted to decision inbox and merged to decisions.md.

**Standards Established:**
- README structure: Title → Tagline → What/Why → Example → Features → Getting Started → Links → Contributing → License
- Emoji policy: Minimal/none for technical documentation (exception: internal team docs)
- Heading conventions: Title Case for H1, sentence case for H2+
- Code blocks: Always specify language (bash, powershell, json, csharp, text)
- Links: Relative paths for internal, descriptive text for external
- Quality requirements: Verify code examples, validate links, confirm technical accuracy, test commands

**Migration Strategy:**
- Phase 1 (Immediate): All new content follows standards - README.md serves as reference
- Phase 2 (Weeks 2-3): Update critical docs (DESIGN.md, Azure docs, test documentation)
- Phase 3 (As time allows): Comprehensive cleanup of remaining markdown files

**Templates Planned:**
- Feature documentation template
- API documentation template
- Tutorial template
- Deployment guide template

**Impact:** Clear baseline for all future documentation work. README.md revision demonstrates standards in practice. Team now has consistent approach for contributor guidance.



#

## 2026-07-18: Issue #131 — Stdio Logging to File Documentation

**Task:** Document the new `--log-file` CLI option, `POSHMCP_LOG_FILE` environment variable, and `Logging.File.Path` appsettings configuration for stdio logging feature (Farnsworth issue #131 architecture decision).

**Documentation updates applied:**

1. **README.md changes:**
   - Added stdio mode note (after MCP client config): "Logging to console is disabled in stdio mode to prevent interference with the MCP JSON-RPC stream. Use `--log-file <path>` or set `POSHMCP_LOG_FILE` to capture diagnostic logs."
   - Created new "CLI Options and Environment Variables" subsection with:
     - `serve` command options: `--transport` and new `--log-file <path>` (stdio mode only, overrides env/appsettings)
     - Environment variables table with `POSHMCP_TRANSPORT`, `POSHMCP_LOG_FILE` (with detailed description of stdio behavior), `POSHMCP_LOG_LEVEL`
   - Added "File-based Configuration (appsettings.json)" subsection showing `Logging.File.Path` schema and note that it's stdio-only
   - Reorganized configuration section for clearer priority: CLI > env > appsettings > silent

2. **DOCKER.md changes:**
   - Added `POSHMCP_LOG_FILE` to "Environment customization" list with note on volume mounting for container persistence
   - Created new subsection "Running in stdio mode with logging" with concrete Docker example: `docker run` with `-v /host/logs:/data` and `-e POSHMCP_LOG_FILE=/data/poshmcp.log` to demonstrate volume mounting pattern

**Key design points captured:**
- Logging is silent in stdio mode when no file is configured (prevents JSON-RPC stream pollution)
- CLI option takes priority over environment variable, which takes priority over appsettings
- Container deployments must use volume mounting for log persistence (logs don't survive container shutdown otherwise)
- Distinction between stdio-only (file-based) vs HTTP console logging behavior

**Outcome:** Issue #131 documentation complete. Users can now discover and understand the three configuration methods for stdio logging, and operators have clear guidance on containerized deployment with persistent logs.



#

## 2026-04-19: Created v0.7.0 and v0.7.1 Release Notes

**Task:** Author release notes for v0.7.0 and v0.7.1 following the established format from 0.6.0.

**v0.7.0 Release Notes (`docs/release-notes/0.7.0.md`):**
- Focused on **stdio logging to file** (issue #131, PR #132) — the primary reliability fix that suppresses diagnostic logs from corrupting the JSON-RPC stream
- Documented **MimeType nullable fix** (PR #130) — safe handling of optional MimeType in resources with `text/plain` fallback
- Included configuration examples for all three log file methods: CLI (`--log-file`), environment (`POSHMCP_LOG_FILE`), and appsettings (`Logging.File.Path`)
- Added Docker example showing volume mounting pattern for persistent logs in containers
- Emphasized backward compatibility and upgrade path for production deployments

**v0.7.1 Release Notes (`docs/release-notes/0.7.1.md`):**
- Documented **Docker build context fix** (PR #134) — resolved build failures in `docker buildx build` command
- Covered **Program.cs refactoring** (PR #135) — extracted five utility classes (LoggingHelpers, DockerRunner, SettingsResolver, ConfigurationFileManager, ConfigurationLoader) for improved maintainability
- Kept release notes concise (maintenance/bugfix release) without inventing features from sparse commit details

**toc.yml Update:**
- Added new "Release Notes" section at the end of navigation
- Listed all three releases: v0.7.1 (latest), v0.7.0, v0.6.0
- Maintains consistent navigation structure with existing sections

**Design Decisions:**
- **Format Consistency:** Matched 0.6.0.md structure exactly (frontmatter with uid/title, release date, What's New, Configuration, Breaking Changes, Upgrade Notes, etc.)
- **Release Focus:** 0.7.0 emphasizes the logging reliability fix as the headline feature (most impactful); 0.7.1 is brief by design (maintenance release)
- **Configuration Examples:** Provided concrete CLI, environment, and appsettings examples for discoverability
- **Cross-linking:** All release notes link to relevant user guides (Transport Modes, Configuration, Resources/Prompts)
- **ToC Placement:** Release Notes added after Support section (alphabetical/logical grouping)

**Outcome:** Both release notes files created and toc.yml updated. Documentation follows established patterns and provides clear upgrade guidance for each release.



#

## 2026-04-19: Fixed DocFX Release Notes Build Content

**Task:** Fix DocFX build configuration to include release-notes in the documentation site build.

**Problem:** Release notes files (`docs/release-notes/0.7.0.md`, `docs/release-notes/0.7.1.md`, `docs/release-notes/0.6.0.md`) were 404ing on the published docs site because `docs/docfx.json` did not include the release-notes directory in the content files glob.

**Solution:** Added `"release-notes/**/*.md"` to the first content entry in `docs/docfx.json` build.content files array. Verified toc.yml already had the Release Notes section properly configured.

**Key Learning:** DocFX content globs must explicitly include all directories containing markdown files intended for the published site. The `build.content[0].files` array is the entry point for content discovery—any .md files outside these patterns will be excluded from the build even if referenced in toc.yml.

**Outcome:** Release notes now included in DocFX build and will be published to the documentation site. Commit: `5a498c8`.



#

## 2026-04-20: Created v0.8.0 Release Notes

**Task:** Author release notes for v0.8.0 highlighting the Docker build deadlock fix and doctor command enhancements.

**v0.8.0 Release Notes (`docs/release-notes/0.8.0.md`):**
- **Primary headline:** Fixed critical stdout/stderr deadlock in Docker builds that caused `poshmcp build` to hang silently even after image built successfully. Explained root cause (sequential `ReadToEnd()` calls with pipe-buffer overflow) and solution (concurrent `Task.Run` readers with `Task.WaitAll`)
- **Secondary feature:** Real-time build output streaming (users now see live progress instead of silence)
- **Infrastructure improvements:** Extracted `BuildDockerBuildArgs` into `DockerRunner` as testable static method; refactored doctor command with hierarchical `DoctorReport` structure and dedicated `DoctorTextRenderer`
- **Doctor command enhancements:** Authentication configuration, logging settings, environment variables, and MCP tool definitions now displayed in diagnostic output
- **Security:** Updated `System.Security.Cryptography.Xml` (10.0.5 → 10.0.6) for CVE mitigation
- **Testing:** Highlighted 11 new unit tests in `DockerRunnerTests.cs` covering Docker build scenarios
- **Format:** Matched 0.7.1.md structure exactly; emphasized user-facing benefits (no hanging, real-time feedback, better diagnostics)

**Design Decisions:**
- **Problem-Solution Format:** Deadlock fix described in plain language with technical explanation for advanced users
- **Highlighted Docker Users:** Added dedicated "Highlights for Docker Users" section since this fix directly impacts a known pain point
- **Hierarchical Information:** What's New → Bug Fixes & Security → Upgrade Notes (less critical for stable release)
- **Cross-links:** Pointed to Docker, Configuration, and doctor documentation for deeper dives

**Outcome:** v0.8.0 release notes created following established format. Docker hang fix prominently featured as significant UX regression fix. Ready for publication without commit (coordinator handles).

## TOC Update for v0.8.0
**Date:** 2026-04-20 16:20:52
**Task:** Add v0.8.0 release notes entry to docs/toc.yml
**Team Directive:** Requested by Steven Murawski; Developer Advocate role as Leela
**Action:** Added v0.8.0 as newest entry (first in list) following semantic versioning convention. Committed with co-author trailer and pushed to origin/main.

## 2026-04-24: Created v0.8.8 Release Notes

**Task:** Draft release notes for patch release v0.8.8 (0.8.7 → 0.8.8).

**v0.8.8 Release Notes (`CHANGELOG.md`):**
- Created new root-level CHANGELOG.md file following standard changelog conventions
- **Primary fix:** `poshmcp build --generate-dockerfile` now emits a user deployment template based on the published `ghcr.io/usepowershell/poshmcp/poshmcp` base image instead of the source build Dockerfile
- **Bug fix:** Generated Dockerfile was incorrectly using the base image's own source Dockerfile as the template
- **Enhancement:** `install-modules.ps1` is now bundled in the base container image at `/app/install-modules.ps1` — generated Dockerfiles no longer require users to have this script locally
- **Documentation:** `examples/Dockerfile.user` updated to reference the bundled script path and use the published base image

**Design Decision:** Patch release focused on Docker deployment workflow improvements. Changes reduce setup friction for users generating custom Dockerfiles by eliminating the need to maintain local `install-modules.ps1` and ensuring generated templates reference the production base image.


## Release Notes for v0.8.9–0.8.11
**Date:** 2026-04-24
**Task:** Write release notes for three patch releases following v0.8.8.

**Learnings:**
Release notes for v0.8.9–0.8.11: PSModule docs in Dockerfile, --appsettings build option, fix for poshmcp build outside repo

## 2026-05-01: v0.9.2 Security Release Notes

**Task:** Write release notes for PoshMcp v0.9.2, a critical security patch addressing authentication configuration bypass.

**Security Issue:** `AddPoshMcpAuthentication()` called `.Get<AuthenticationConfiguration>()` to read configuration but failed to register it with `services.Configure<AuthenticationConfiguration>()`. This caused `IOptions<AuthenticationConfiguration>` to always resolve to its default state (`Enabled = false`), bypassing authentication middleware and token validation even when `Authentication.Enabled: true` was configured.

**Impact:** High severity — any deployment with authentication enabled was accepting all MCP requests without validation. Two security gates were inert: middleware registration and endpoint authorization policy.

**What Was Written:**
- **Security Fix section** — Plain-English explanation of the bug, its impact, and the fix
- **High severity callout** — Made immediately visible that this is a critical security update
- **Root cause and proof:** Explained which two gates were bypassed and why
- **Clear fix code:** Showed the one-line solution with context
- **Upgrade Notes:** Emphasized "redeploy immediately" with clear ops guidance (redeploy required, no config changes, auth now works)
- **Testing:** Documented 3 regression tests covering the fix
- **Breaking Changes:** None (correctness fix, not behavior change)

**Style Decisions:**
- Security patch releases get **"Security Fix"** top-level section (not "What's New"), matching v0.8.4 pattern
- Direct, no-jargon language for operators (most critical audience)
- Impact statement leads with deployment scope ("any deployment with Authentication.Enabled: true")
- Upgrade section emphasizes immediacy ("should be updated immediately") — ops must treat this as urgent
- Structured code block with context, not just snippet
- See Also links point to auth and security docs for deeper reading
- No CVE assigned yet (marked CVE-Pending) — appropriate for first-party security disclosure
- Omitted internal commits — user-facing facts only

**Outcome:** v0.9.2 release notes complete. Clear, direct advisory that meets security patch disclosure standards. Ready for publication and immediate deployment.

### 2026-05-01T16:16:11Z - Release v0.9.4 Documentation - Consolidation & Auth Guides (Leela docs)

- Created docs/release-notes/0.9.4.md (full v0.9.4 release notes)
- Updated docs/entra-id-auth-guide.md: added VS Code troubleshooting rows documenting OAuth redirect fix
- Updated docs/toc.yml: registered new release notes file
- Previously: consolidated Entra ID auth docs (entra-id-mcp-auth.md merged into canonical entra-id-auth-guide.md)
- Coordination: Released Amy's gated v0.9.4, provided QA docs to Fry for regression test scenarios

### 2026-05-12: Authoring MCP-friendly PowerShell — Docs Gap Identified

**Task:** Complementary research alongside Hermes — author-facing guidance for getting good MCP tool metadata out of existing PowerShell.

**Docs audit findings:**
- `docs/articles/exposing-tools.md` covers **discovery/filtering only** (whitelist, include/exclude, modules). No authorship guidance.
- `README.md` mentions "Automatic extraction from Get-Help and Get-Command" once, no follow-through.
- `DESIGN.md` lists "synopsis" as an extracted field but never tells authors what to write.
- **No existing "Authoring MCP-friendly PowerShell" article.** Confirmed docs gap.

**Author-facing recommendations distilled (the ones that matter):**
1. `.SYNOPSIS` is the tool description — treat one sentence as the AI's first impression. Action-led, no marketing prose.
2. Prefer `[Parameter(HelpMessage="...")]` over `.PARAMETER` comment-based help for per-param description (structurally attached, survives module boundaries). Comment-based help is the fallback for richer prose.
3. Verb choice drives safety classification — PoshMcp runs `Get-Verb` and sets `IsReadOnly` / `IsDestructive` / `IsIdempotent` from the verb group (`Common`/`Data`/`Lifecycle`/`Security`/`Diagnostic`). Wrong verb = wrong gating policy.
4. Typed parameters (`[string]`, `[int]`, `[ValidateSet]`, `[Parameter(Mandatory)]`) become the JSON schema. Untyped `[object]` parameters produce unusable schemas.
5. Each parameter set becomes a separate tool — name them clearly or collapse them.

**Current extraction behavior (from McpToolFactoryV2.cs lines ~111–145):**
- Default `metadata.Description = commandInfo.Name`.
- Replaced by `"{commandName} {parameterSet.ToString()}"` — i.e., syntax string, NOT SYNOPSIS/DESCRIPTION from comment-based help, in the path I traced. Hermes' report has the authoritative answer on whether `Get-Help` SYNOPSIS feeds into the description elsewhere; if it doesn't, that itself may be a product gap worth raising separately.

**Recommendation to Brady:** File a GitHub issue for new article `docs/articles/authoring-tools.md`, cross-linked from `exposing-tools.md` and the README "Rich Metadata" bullet. Draft body provided in response, NOT filed pending approval.

**Why this complements Hermes:** Hermes answers "what PoshMcp extracts from a function." This entry answers "what should a module author *write* so PoshMcp has something good to extract." Both halves needed.

## Learnings — 2026-05-13 — issue #234 / PR #247

### Doc pattern: precedence chains as tables + per-step example
- For multi-step resolver chains (FR-500/FR-510), a table with Step/Source/Notes columns is the most scannable surface for module authors. Pair it with one tiny PowerShell example per rung — readers can match their function shape against an example faster than against prose.
- Anchor the chain to the implementation by naming the FR: `synopsis -> description -> syntax -> name` (tool) and `helpParameter -> helpMessage -> validateSet -> typeFallback` (parameter). These literals also match the wire vocabulary in DescriptionSourceVocabulary / IToolMetadataSource.cs and the doctor `descriptionSource` JSON values, so the doc, the metric tag, and the doctor field are one phrase.
- Existing fixtures (HelpParityFixture) make great worked examples — each function targets one rung; lifting them into docs is zero-cost.

### Doc pattern: footnoting an active bug honestly
- When a feature ships partially — resolver works, surfacing doesn't — write a short **Known issue** callout in-line with the affected section (not at the bottom of the doc). Format used here:
  > **Known issue.** {what works} / {what doesn't} / {tracking issue link} / {what authors should do today} / {what changes when the fix ships}.
- The "no module change required when fix ships" line is what made the callout safe — authors who add help today will get the surfacing for free later, so the doc isn't telling them to wait.
- Do NOT pretend the bug is fixed. Future-tense optimism in docs erodes trust faster than honest gaps do.

### Cross-link discipline
- README "Rich Metadata" bullet was the right hook — single inline link to the new section, no rewording. Don't restate the precedence chain in two places; let README defer to the article.

- **20260514T000000Z**: ✓ v0.13.0 release notes drafted at `docs/release-notes/0.13.0.md`. Marquee theme: spec 010 Help-aware tool descriptions — in-process and OOP paths now byte-identical, FR-500/FR-510 precedence + FR-540 sanitization, IToolMetadataSource seam, doctor descriptionSource reporting, OTel resolution counters, parity/regression tests, pre/post-spec010 cold-start gates. Fixes: SwitchParameter round-trip (#222), parameter descriptions wired to inputSchema (#248), HelpAwareToolMetadataSource as default (#250). No breaking changes. Behavior change called out: tools previously showing the raw syntax line now show help-derived descriptions. Matched 0.12.x / 0.11.0 tone and structure (Summary, What's New, Bug Fixes, Documentation, Tests & Benchmarks, Breaking Changes, Upgrade Notes, Affected files & specs).

## 2026-05-14: Spec 009 closed via this session

Spec 009 (Test Suite Consistency and Fast Unit Tier) is functionally complete. Five PRs merged in the closeout wave (#252, #253, #257, #259, #260) and six issues closed (#213, #214, #215, #216, #220, #221). Issue #221 acceptance gate (Fry) measured the Unit tier at 432 passed / 0 failed / 0 skipped across 5 consecutive runs, mean 20.45s wall-clock — well under the <60s FR-419 budget. Your contribution: see your own history entries for this session.

