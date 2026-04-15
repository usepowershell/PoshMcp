- **20260414T000000Z**: Created conference-ready team introduction content in `docs/articles/talk-team-introductions.md` using project-grounded achievements (dynamic PowerShell-to-MCP tooling, unified `poshmcp` entry point, runspace expertise, observability, test quality, docs education, decisions logging, and queue monitoring) with concise, audience-friendly speaker intros.
- **20260414T000000Z**: ✓ Wired `docs/public/logo.svg` into DocFX build: created `docs/public/` source folder, added `public/logo.svg` to resource files, updated `_appLogoPath` to `public/logo.svg`. Build confirmed `_site/public/logo.svg` present, 0 warnings.
- **20260403T135630Z**: ✓ Docs consistency review (13 files, 2.2K lines deduplicated). Proposal filed & merged into decisions.md.
- **20260414T000000Z**: Updated DocFX branding config to use `poshmcp.svg` via `_appLogoPath` and added SVG to `build.resource.files` so the logo is emitted and referenced correctly in generated docs.
- **20260414T000000Z**: Fixed DocFX homepage `InvalidFileLink` warnings by replacing `api/index.md` references in `docs/index.md` with the published API landing URL `https://usepowershell.github.io/PoshMcp/api/PoshMcp.html`; validated that both index warnings were removed in local build output.
# Leela — History

## Project Context (Seeded on Join)

**Project:** poshmcp - Model Context Protocol (MCP) server that dynamically transforms PowerShell scripts, cmdlets, and modules into secure, discoverable AI-consumable tools

**Tech Stack:** .NET 8, C#, PowerShell SDK, OpenTelemetry, ASP.NET Core, xUnit

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

### 2026-03-27: First Assignment - README Revision and Documentation Audit

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

### 2026-03-27: Documentation Standards Formalized

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

### 2025-07-24: Documentation consistency and deduplication review

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

