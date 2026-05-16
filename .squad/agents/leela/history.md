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


## Learnings
- **2026-05-14**: Authored 4-part tutorial series under `docs/articles/tutorials/` (PR #273, branch `squad/docs-tutorial-series`). Layout: `index.md` + `01-local-stdio-server.md` + `02-docker-http-custom-functions.md` + `03-docker-api-key-per-command.md` + `04-docker-api-key-multi-key-roles.md`. All auth configs grounded in actual code under `PoshMcp.Server/Authentication/` (no fabrication).
- **2026-05-14**: Confirmed auth schema facts for future docs work:
  - `Authentication.Schemes.ApiKey.Keys` is a dictionary where the **property name IS the API key value** (per `ApiKeyAuthenticationHandler`).
  - `ApiKeyDefinition` has `Scopes: List<string>` and `Roles: List<string>`; roles become `ClaimTypes.Role` claims.
  - `PowerShellConfiguration.CommandOverrides` is `Dict<string, FunctionOverride>`; key = PowerShell command name (e.g., `Get-PublicStatus`), NOT the snake_case MCP tool name.
  - `FunctionOverride` nullable fields: `AllowAnonymous`, `RequiredScopes`, `RequiredRoles`. Null = fall through to `DefaultPolicy`.
  - `RequiredRoles` is currently an **any-match** check (per `AuthorizationHelpers`) — caller passes if they have at least one listed role. Documented this in tutorial 4.
  - `ToolListAuthorizationFilter` hides unauthorized tools from `tools/list` (nice UX detail to demonstrate).
- **2026-05-14**: Confirmed Docker image contract:
  - Base image: `ghcr.io/usepowershell/poshmcp/poshmcp:latest`
  - AllUsers PowerShell module path: `/usr/local/share/powershell/Modules` (drop custom modules here)
  - Runtime config path: `/app/server/appsettings.json` (override with COPY in custom Dockerfile)
  - `examples/Dockerfile.user` pattern: `USER root` → COPY modules + config → `USER appuser`
- **2026-05-14**: DocFX confirmation — `docs/docfx.json` uses `articles/**/*.md` glob, so new subdirectories under `articles/` are picked up automatically. Only `docs/toc.yml` needs an explicit nav entry. Avoids touching docfx config for content-only additions.
- **2026-05-14**: CLI surface verified against actual docs: `poshmcp create-config`, `poshmcp update-config --add-command|--add-module|--add-include-pattern|--add-exclude-pattern`, `poshmcp serve --transport stdio|http`, `poshmcp doctor`, `poshmcp build --type custom --tag NAME`.

## Learnings — 2026-05-16 — PR #273 polish landing

### doctor moduleImports JSON shape (grounded against `DoctorReport.cs`)
- `ModuleImportsSection` carries three arrays: `Modules` (one per configured module), `Patterns` (one per glob pattern), and `Tools` (one per discovered tool, post-filter).
- `ToolImportEntry.Source` is one of `"commandName" | "module" | "pattern" | "unknown"`; `Disposition` is one of `"exposed" | "filteredOut" | "discoveryFailed"`. These are the surfaces tutorials should teach against — they're the audit trail for `CommandOverrides`.
- Per spec 011 FR-263-9 the source-attribution priority is `commandName > module > pattern`, so a command reachable through both `Modules` and `CommandNames` still appears once and is attributed to `commandName`. This is what makes the `Modules` + `CommandNames` redundancy in tutorial 4 harmless.

### Authentication.DefaultScheme semantics
- `AuthenticationConfiguration.DefaultScheme` (defaults to `"Bearer"`) is consumed at `AuthenticationServiceExtensions.cs` line 37 — `services.AddAuthentication(authConfig.DefaultScheme)` — and again at line 249 to resolve the scope claim against the default scheme handler. So when `Schemes` has more than one entry, `DefaultScheme` is what binds the global authentication/authorization middleware to a specific handler. Worth flagging in any multi-scheme tutorial.

### Tutorial polish workflow
- Inserting a doctor-demo step mid-tutorial needs three small rituals: insert the step, renumber every later step, and update `What you learned` to mention any new field surface. Easy to forget the last one.
- Two focused commits per polish landing reads cleaner in PR history than one omnibus commit — reviewers can scan each commit's scope from the subject line.

## 2026-05-16 — PR #273 merged

4-part tutorial series + polish pass squash-merged to ``main`` today. Three decisions captured in ``decisions.md`` (tutorial location, doctor-after-setup pattern, ``RequiredRoles`` any-match asymmetry).
