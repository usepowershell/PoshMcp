# Amy Work History

## Project Context

**Project:** PoshMcp - Model Context Protocol server for PowerShell
**Role:** Release readiness, DevOps, Azure deployment planning, and operational gates.

## Compacted Summary

### 2026-06-01T00:00:00Z: Active History Summarized

Amy's active history was compacted after crossing the squad history threshold. The retained working knowledge is below; older detailed notes remain in Amy's history archive.

## Release Readiness Patterns

- Release version source is `PoshMcp.Server/PoshMcp.csproj` `<Version>`.
- Release notes path is `docs/release-notes/{version}.md`.
- `CHANGELOG.md`, release notes, and the version bump should be committed before tag publication.
- Do not push or tag when release artifacts are uncommitted or quality gates are incomplete.
- Keep release commits scoped to release artifacts and avoid pulling unrelated dirty files into the release commit.
- Required release gates include formatting verification and a clean CI-aligned test run.
- For `0.16.3`, the format gate was green, focused auth unit tests passed, and full test execution stalled before completion; release remains blocked.

## Azure And Container Apps Patterns

- Use plan-first Azure preparation: write `.azure/deployment-plan.md` and wait for approval before generating infrastructure artifacts.
- PoshMcp Container Apps scenario infrastructure uses a shared Container Apps Environment with scenario-driven apps for basic, advanced, Azure, and auth configurations.
- Terraform should provision scenario infrastructure and reference prebuilt images rather than building or pushing container images during `terraform apply`.
- Container Apps runtime conventions include port `8080`, `/health` startup checks, `/health/ready` readiness/liveness checks, Application Insights configuration, Log Analytics, managed identity, optional ACR Pull, and optional Azure Files mounts.
- Generic ARM/resource graph queries can be more reliable than specialized Container Apps CLI commands during local network or CLI extension issues.

## Auth And Diagnostics Patterns

- Auth failures can be caused by issuer-shape mismatches between tokens and accepted issuer configuration; verify token version and accepted issuer shape before assuming app runtime failure.
- Auth diagnostics should avoid arbitrary JWT claim logging and stick to a safe allowlist for audience, scope, roles, and issuer.
- Authenticated ACA MCP troubleshooting should verify metadata endpoints, token consent scope, CORS preflight behavior, and MCP root path behavior before suspecting port or cold-start issues.

## Build, Packaging, And Tooling Patterns

- `poshmcp build` is the dotnet global tool build subcommand and delegates to Docker or Podman build using a single tag and the current directory as build context.
- For both versioned and `latest` image tags, build once with the versioned tag and then add an image alias rather than building twice.
- Dockerfile build-stage changes should audit stale `COPY` lines when restore/build targets move from solution-level to project-level commands.
- Generated Dockerfile flows should produce an output file and equivalent build command without invoking Docker or Podman.

## Test And Gate Patterns

- Long or stalled full-suite runs should be treated as blocked unless they produce a clear completion summary.
- Focused passing tests are useful evidence but do not replace the full release gate.
- When format drift is outside the approved release file set, do not include unrelated formatting changes in a scoped release commit without explicit approval.

## Learnings

- 2026-06-01: Copilot CLI rejects PoshMcp OAuth DCR responses when `/register` omits `redirect_uris`; a local fix now echoes requested DCR metadata, but deployed ACA images must be rebuilt/redeployed before the live URL reflects it.
- 2026-06-01: ACA startup `JsonReaderException` during config resolution usually means the active runtime config is not the checked-in root `appsettings.json`; inspect `POSHMCP_CONFIGURATION`, `--config`, volume mounts, and the deployed image-bundled `/app/server/appsettings.json` before changing app code.
- 2026-06-02: Patch release orchestration should stop before commit/tag when full-suite validation is red; this run halted after version/changelog/release-note updates because tests remained failing.