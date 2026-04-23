- **20260414T000000Z**: Created conference-ready team introduction content in `docs/articles/talk-team-introductions.md` using project-grounded achievements (dynamic PowerShell-to-MCP tooling, unified `poshmcp` entry point, runspace expertise, observability, test quality, docs education, decisions logging, and queue monitoring) with concise, audience-friendly speaker intros.
- **20260414T000000Z**: ✓ Wired `docs/public/logo.svg` into DocFX build: created `docs/public/` source folder, added `public/logo.svg` to resource files, updated `_appLogoPath` to `public/logo.svg`. Build confirmed `_site/public/logo.svg` present, 0 warnings.
- **20260403T135630Z**: ✓ Docs consistency review (13 files, 2.2K lines deduplicated). Proposal filed & merged into decisions.md.
- **20260414T000000Z**: Updated DocFX branding config to use `poshmcp.svg` via `_appLogoPath` and added SVG to `build.resource.files` so the logo is emitted and referenced correctly in generated docs.
- **20260414T000000Z**: Fixed DocFX homepage `InvalidFileLink` warnings by replacing `api/index.md` references in `docs/index.md` with the published API landing URL `https://usepowershell.github.io/PoshMcp/api/PoshMcp.html`; validated that both index warnings were removed in local build output.
- **20260418T201500Z**: ✓ v0.6.0 Release Notes & Resources/Prompts Documentation — Audited docs for gaps (Resources/Prompts methods and config were undocumented), created comprehensive `docs/articles/resources-and-prompts.md` user guide (4,600 words with configuration, examples, MCP methods, best practices, troubleshooting), added release notes at `docs/release-notes/0.6.0.md`, updated README.md with feature mentions, and added resources-and-prompts to docs/toc.yml. Committed and pushed.
# Leela — History



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

