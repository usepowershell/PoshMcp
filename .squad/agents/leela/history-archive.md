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


