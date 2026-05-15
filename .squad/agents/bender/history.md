# Bender Work History

**Status:** 42.8 KB (checked 2026-05-11: within 90-day retention, no archival required)
**Status:** 37.6 KB (checked 2026-05-03: within 90-day retention, no archival required)

## 2026-05-15: PR #266 - fix(doctor) #261 Pool-mode display

### What I fixed
Doctor report was showing `effectiveProcessPoolSize: 0` and `effectiveMinHealthyForStartup: 0` when `SubprocessHostMode = Pool`. Those knobs are inert in Pool mode (they only apply to ProcessPool), so the value was technically correct but read like a bug to operators. Changed both to render `"n/a (Pool mode)"` outside ProcessPool, mirroring the existing `EffectiveRunspacePoolSize` pattern.

### Files touched
- `PoshMcp.Server/Diagnostics/DoctorReport.cs` - promoted `EffectiveProcessPoolSize` and `EffectiveMinHealthyForStartup` from `int` to `string` (default `string.Empty`).
- `PoshMcp.Server/Diagnostics/DoctorService.cs` - refactored the inline ternaries into an explicit `if (ProcessPool) { compute and ToString } else { "n/a (Pool mode)" }` block. ProcessPool semantics unchanged (clamping + defaults preserved).
- `PoshMcp.Server/Diagnostics/DoctorTextRenderer.cs` - no change. The renderer only emits `process-pool`/`min-healthy` lines when `HostMode == ProcessPool`, so the new strings flow through cleanly.
- `PoshMcp.Tests/Unit/Diagnostics/DoctorOutOfProcessSectionTests.cs` - new, 5 tests: Pool n/a, ProcessPool integer-string, min-healthy clamping, default pool size, not-applicable.

### Test approach
`DoctorService` is internal but the test project has `InternalsVisibleTo`. `OutOfProcessSection` is a public sealed record. Called `DoctorService.BuildOutOfProcessSection` directly with synthesized `PowerShellConfiguration` instances and `NullLoggerFactory.Instance`. No FS, no process spawning - pure Unit tier.

### Gotchas
- `DoctorService` and `DoctorReport` live in the **root `PoshMcp` namespace**, not `PoshMcp.Server.Diagnostics` (despite the folder). My first test file used the folder-shaped namespace and failed to compile. Use `using PoshMcp;` not `using PoshMcp.Server.Diagnostics;`.
- `gh pr create` failed with `Unauthorized: As an Enterprise Managed User` on the `stmuraws_microsoft` account. Had to `gh auth switch -u usepowershell` first. Worth remembering for future PRs to `usepowershell/PoshMcp`.

### Outcome
PR #266 - https://github.com/usepowershell/PoshMcp/pull/266 - marked ready for review, labeled `squad` + `squad:bender`. 54 doctor tests green, full server build clean.
## 2026-05-15: Team update (via Scribe)
**Ralph round 1 — 3 PRs in-flight, may need your review:**
- **PR #266** (Bender, issue #261): Doctor pool display sentinel — EffectiveProcessPoolSize / EffectiveMinHealthyForStartup promoted to `string`, returning `"n/a (<mode> mode)"` when inert. Files: `DoctorService.cs`, `DoctorReport.cs`, `DoctorTextRenderer.cs` + Unit tests.
- **PR #264** (Hermes, issue #262): AAD v2.0 `preferred_username` mapping — added `ClaimsMapping.NameClaim` to `AuthenticationConfiguration`; wires to `JwtBearerOptions.TokenValidationParameters.NameClaimType`. Null preserves default (no behavior change for existing deployments). Files: `AuthenticationConfiguration.cs`, `AuthenticationServiceExtensions.cs`, `docs/entra-id-auth-guide.md`.
- **PR #265 DRAFT** (Farnsworth, issue #263): Spec 011 design-only — `specs/011-doctor-module-imports/spec.md` (13 FRs / 4 SCs / 5 OQs). Implementation split to follow-up issues #267 (Bender) and #268 (Hermes).
