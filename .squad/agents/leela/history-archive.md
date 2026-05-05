## 2025-07-24: Documentation consistency and deduplication review

**Task:** Reviewed all 13 project markdown files (excluding .squad/ and .copilot/ internal files) for consistency, duplication, and cross-referencing.

**Duplication found and resolved:**

1. **Docker docs (3 files → 1 canonical + 2 redirects):**
   - `DOCKER.md` is the canonical comprehensive Docker guide
   - `docs/DOCKER-BUILD-QUICK-REF.md` → replaced with redirect to DOCKER.md (content was subset)
   - `docs/DOCKER-BUILD-MODULES.md` → replaced with redirect to DOCKER.md (covered deprecated build-arg approach)

2. **Environment customization (3 files → 1 canonical + 1 summary + 1 checklist):**
   - `docs/ENVIRONMENT-CUSTOMIZATION.md` is the canonical user guide
   - `docs/ENVIRONMENT-CUSTOMIZATION-SUMMARY.md` → replaced with brief changelog summary linking to canonical
   - `docs/INTEGRATION-CHECKLIST.md` → trimmed to essential checklist items, links to IMPLEMENTATION-GUIDE.md

3. **Azure test docs (3 files → 1 canonical + 2 lean references):**
   - `PoshMcp.Tests/Integration/README.azure-integration.md` is the canonical test documentation
   - `docs/AZURE-INTEGRATION-TEST-SCENARIO.md` → replaced with brief overview + links
   - `docs/QUICKSTART-AZURE-INTEGRATION-TEST.md` → trimmed to true quick-reference, removed duplicate troubleshooting/CI/CD sections

**Consistency fixes applied across all 13 files:**
- Sentence-case headings (per docs-standards skill): "Getting started" not "Getting Started"
- Removed emoji from headings in DESIGN.md (🧠, 🚀, 🔧, etc.)
- Added "See also" cross-reference sections to all docs that reference other docs
- Consistent em-dash separators in link descriptions
- Fixed broken markdown in DESIGN.md (missing `##` on several H2 headings)

**Cross-reference links added to:**
- DESIGN.md → README, DOCKER, Environment customization, Azure
- DOCKER.md → Environment customization, Examples (removed deprecated doc link)
- ENVIRONMENT-CUSTOMIZATION.md → Implementation guide, Integration checklist, DOCKER, Examples
- IMPLEMENTATION-GUIDE.md → Environment customization, Integration checklist, Examples
- TRAIT-BASED-TEST-FILTERING.md → Azure test README, Quickstart, Test organization
- PoshMcp.Tests/README.md → Azure test README, Trait filtering, Main README
- PoshMcp.Tests/Integration/README.azure-integration.md → Azure infra, Examples, DESIGN, Trait filtering, Quickstart

**Net effect:** ~2,250 lines of duplicated content removed, replaced with clear cross-reference links. Every doc now has a distinct purpose with no overlapping content.

- **20260414T000000Z**: Local DocFX and published HTML diverged on navbar logo path (`poshmcp.svg` vs `logo.svg`); standardized docs source to `logo.svg` in `docs/docfx.json` and added `docs/logo.svg` so local builds consistently emit `<img id="logo" class="svg" src="logo.svg" alt="">`.
- **20260414T000000Z**: Fixed DocFX `InvalidFileLink` warnings in environment docs by replacing links that pointed outside the DocFX content graph with in-site article links or stable GitHub links; also corrected `docs/articles/environment.md` relative path to `../archive/ENVIRONMENT-CUSTOMIZATION.md`.
- **20260414T000000Z**: DocFX content boundaries matter for link validation; archive-only pages in `docs/archive` should link to included `docs/articles/*` pages (or external URLs), not sibling archive files or repo-root folders excluded by `docs/docfx.json`.
- **20260414T000000Z**: Navbar logo placement in DocFX should be changed in source template/style assets (`docs/templates/poshmcp/public/main.css` and matching source CSS), then validated by rebuilding and checking generated `_site/public/main.css` rather than editing `_site` directly.
- **20260414T000000Z**: Tool authorization docs should show complete API key examples with `DefaultPolicy`, per-key `Keys` role/scope claims, and `PowerShellConfiguration.FunctionOverrides` precedence so readers can reason about default vs per-tool access quickly.
- **20260414T000000Z**: Updated docs/authentication.md, docs/articles/security.md, and docs/articles/configuration.md to explicitly present both Entra ID (`JwtBearer`) and API key (`ApiKey`) authentication with concise "when to use which" guidance and cross-links between sections.
- **20260414T000000Z**: v0.5.6 release notes should anchor on three concrete commits: `31fa637` (authorization override matching + new `AuthorizationHelpersTests`), `0d26be6` (auth/security/config docs alignment), and `df8fcff` (package version bump to 0.5.6 in `PoshMcp.Server/PoshMcp.csproj`).
- **20260415T000000Z**: README consistency pass should use docs under `docs/articles/*` as the canonical user-facing source; keep root README examples aligned to `poshmcp` CLI usage (`create-config`, `update-config`, `serve --transport ...`), use `PowerShellConfiguration.CommandNames` (not `FunctionNames`), and retarget legacy `docs/*.md` links to existing `docs/articles/*` or `docs/archive/*` paths.
- **20260415T195657Z**: Scribe merged the README consistency proposal from inbox into canonical `.squad/decisions.md` and retained the scope as docs-only guidance (no code behavior changes), so future README/doc alignment work can cite a single decision record.
- **20260415T000000Z**: DOCKER.md consistency passes should prioritize high-confidence fixes only: keep CLI-first guidance, add Docker-native equivalents, ensure container config paths use `/app/server/appsettings.json`, and avoid introducing links outside the current docs graph.

- **20260419T000000Z**: ✓ Preview build install instructions added to README and user-guide. README.md updated with pointer to preview guide; user-guide.md now includes complete "Installing Preview Builds" subsection covering: why previews exist (latest features before stable release), GitHub Packages URL and authentication requirements (PAT with `read:packages` scope or GitHub CLI token), setup options (gh CLI recommended with bash/PowerShell examples, manual PAT fallback), install/update/downgrade commands with `--prerelease` and `--source` flags, preview version naming (`0.6.0-preview.{run_number}`), and link to browse packages at GitHub UI. Committed and pushed.

- **20260419T201500Z**: ✓ Reconciled orphaned `docs/user-guide.md` into articles. Migrated "Installing Preview Builds" section (87 lines, complete GitHub Packages workflow) into `docs/articles/getting-started.md` after "Building from Source" as new subsection. Verified no other unique content in user-guide.md warranted migration (configuration, basic setup, etc. already covered in articles). Updated README.md link from `docs/user-guide.md#installing-preview-builds` to `docs/articles/getting-started.md#installing-preview-builds`. Deleted orphaned user-guide.md (1,590 lines removed). Committed and pushed. ToC remains wired to articles/ only—no conflicts.



#




## Archived 2026-05-05 (history summarization, lines 201-509 of pre-summarization file)


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


### 2026-05-05: Cubert flagged docs for revision
**By:** Scribe (cross-agent update)
Cubert (newly-hired Fact Checker) flagged `docs/articles/squad-story.md` and `docs/articles/squad-work-log.md` as needing revision before publication. Issues: systemic future-dated entries (2026-07-XX), fabricated health JSON sample, wrong file paths, stale counts, Cubert missing from story roster. Cubert recommended Leela for the revision pass per Reviewer Rejection Protocol (original author locked out). See `.squad/decisions.md` entry `2026-05-05: Systemic future-dated entries across squad artifacts`.


## Archived 2026-05-05 (second-pass summarization)

- When consolidating docs with overlapping scope, preserve the more comprehensive/structured file as the canonical source and fold specialized guidance (e.g., VS Code) in as subsections. This creates a single, authoritative reference without fragmenting readers across multiple files. Critical integration points (e.g., client app authorization) must be called out explicitly if they were buried or missing in the original guide.

### 2026-05-01: Verified VS Code Scope Naming — `access_as_server` is Correct

**Task:** Investigate whether `user_impersonation` is required for VS Code OAuth flows, or if `access_as_server` is acceptable.

**Status:** ✓ RESOLVED — No documentation changes needed.

**Investigation findings:**
- **VS Code scope discovery:** VS Code's MCP client does NOT hardcode scope names. It uses RFC 9728 Protected Resource Metadata to dynamically discover available scopes from the server.
- **Scope naming conventions:** 
  - `user_impersonation` = Microsoft's built-in convention for Azure service permissions (e.g., `AzureServiceManagement/user_impersonation`). Owned by Microsoft.
  - `access_as_server` = Custom scope owned by PoshMcp, following pattern `api://poshmcp-prod/access_as_server`. Fully configurable.
- **VS Code compatibility:** VS Code works with any scope name, as long as:
  1. The scope is listed in `ScopesSupported` in the Protected Resource Metadata endpoint
  2. The scope is authorized in "Authorized client applications" for the VS Code client ID
  3. The token includes the scope in its `scp` claim
- **Decision:** Keep `access_as_server`. It is more descriptive (indicates delegated server access), owned by PoshMcp (not confusing with Microsoft conventions), and fully compatible with VS Code's dynamic scope discovery.

**Documentation status:** ✓ Current docs are accurate. No changes needed. The guide correctly explains how VS Code discovers scopes via Protected Resource Metadata and authorizes them.

**Key insight:**
- OAuth scope naming is flexible when using dynamic scope discovery (RFC 9728). Hardcoded conventions like `user_impersonation` are only required when implementing built-in Azure service APIs; custom applications can use any descriptive name they own. Documenting the *discovery mechanism* (PRM endpoint) is more important than prescribing specific scope names.

---

### 2026-05-01: v0.9.3 Release Notes (Security Patch — Configuration Precedence Override)

**Task:** Create release notes for PoshMcp v0.9.3 security patch release.

**Status:** ✓ File created at `docs/release-notes/0.9.3.md` with comprehensive security fix documentation.

**What was documented:**
- **Root cause deep-dive** — ASP.NET Core's `WebApplicationBuilder` loads base `appsettings.json` first, then adds custom config via `AddJsonFile()`. The base file has `Authentication.Enabled: false` as a default. Configuration precedence makes base file win, silently overriding custom `Enabled: true`.
- **Relationship to v0.9.2** — Two *separate* auth bypass bugs: v0.9.2 fixed DI binding (config not registered), v0.9.3 fixes loading order (config bound but wrong precedence). Both independently caused auth bypass.
- **Diagnostic parity problem** — Tools like `doctor` use `ConfigurationLoader.BuildRootConfiguration(finalConfigPath)` (custom only + env vars), never loading base file. They reported correct value (`enabled: true`) but runtime enforced wrong value (`enabled: false`). The fix unifies all auth gates to use the same source.
- **Impact clearly scoped** — Container deployments with `Authentication.Enabled: true` in custom config affected; Stdio/CLI unaffected (no HTTP middleware).
- **Technical fix** — All three auth gates in `Program.cs` now use `authRootConfig` built via `ConfigurationLoader.BuildRootConfiguration(finalConfigPath)`.
- **Diagnostic parity assurance** — Highlighted that after v0.9.3, what `doctor` reports matches what runtime enforces.

**Style observations (consistent with v0.9.2 pattern):**
- Security patch = "Security Fix" top-level section (high visibility)
- Frontmatter: uid, title, released date
- Breaking Changes: None
- Upgrade Notes: Action items (rebuild + redeploy), no config changes, verification steps, diagnostic guidance
- See Also: Links to related guides

**Key insight:**
- When a security bug involves *configuration precedence* or *diagnostic divergence*, the root cause explanation must include "why did diagnostics work but runtime didn't?" This builds confidence that the fix is complete and diagnostic tools can now be trusted again.

### 2026-05-01: v0.9.2 Release Notes (Security Patch — Authentication Bypass)

**Task:** Create release notes for PoshMcp v0.9.2 security patch release.

**Status:** File already existed at `docs/release-notes/0.9.2.md` with comprehensive content. Verified and confirmed complete.

**What was documented:**
- **Root cause deep-dive** — Configuration binding bug: `AddPoshMcpAuthentication()` called `.Get<>()` to read config but never registered with `services.Configure<>()`, causing `IOptions<AuthenticationConfiguration>` to always resolve to `Enabled = false` default regardless of appsettings.json.
- **Impact clearly scoped** — HTTP transport with `Authentication.Enabled: true` affected; Stdio/CLI unaffected (no HTTP middleware in those paths).
- **Technical fix** — One-line addition: `services.Configure<AuthenticationConfiguration>(configuration.GetSection("Authentication"))` placed unconditionally before early-return guards.
- **Regression tests** — 3 new tests: auth-enabled (IOptions correct), auth-disabled (registered, not default), no-auth-section (no throw).
- **Upgrade action explicit** — "Rebuild and redeploy your container image. No configuration changes required."

**Style observations (consistent with v0.8.4 pattern):**
- Security patch = "Security Fix" top-level section (high visibility)
- Frontmatter: uid, title, released date
- Breaking Changes: None
- Upgrade Notes: Action items, scoped impact analysis, no config changes needed
- Testing: Regression test coverage documented
- See Also: Links to related guides (Authentication, Security, Configuration)

**Key insight:**
- Security releases must explain *why* the bug happened (not just what), so ops teams understand the fix and why redeployment is mandatory. The "two security gates in Program.cs were bypassed" framing helped readers understand the severity.

### 2026-04-24: v0.8.4 Release Notes (Security Patch)

**Task:** Created release notes for PoshMcp v0.8.4 and added TOC entry.

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

---
*Older entries (pre-2026-05-05 bulk) moved to `history-archive.md` on 2026-05-05 by Scribe to satisfy 15KB hard gate. See archive for full record.*
