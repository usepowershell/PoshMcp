# Farnsworth — Lead/Architect — Work History

## Recent Work Index (2026-05-16)

- **2026-05-16:** PR #276 re-review (import source tracker wiring across all doctor builders) — APPROVE
- **2026-05-16:** PR #273 review + merge (Leela — tutorials series) — merged
- **2026-05-15–16:** Cross-agent PR #276 cycle (Hermes execution, Cubert verification) — track parity achieved

## Prior Work (2026-05-13 to 2026-05-15)

Detailed entries archived to history-archive.md: Spec 009 review wave (6 PRs, closed), PR #269–#271 (Hermes spec 011 work), architectural learnings from import-tracker-gap discovery.

---

### 2026-05-16 — PR #276 re-review (issue #272)

**Verdict:** APPROVE. The revised chain now threads IToolImportSourceTracker through both runtime report entry points: ConfigurationReloadTools.GetConfigurationStatus() and McpToolSetupService.BuildConfigurationTroubleshootingJson() pass the shared discovery tracker into DoctorService.BuildDoctorReportFromConfig(...), which forwards it into BuildModuleImportsSection(...). That closes the prior runtime 	ools[].source = "unknown" gap.

**Architectural lesson:** for provenance/report seams, the clean shape is a shared per-discovery snapshot owned by discovery (McpToolFactoryV2) and injected into read-only report builders. This keeps DoctorService pure, avoids re-running command discovery for attribution, and lets runtime + CLI surfaces share one authoritative contract.

**Lifecycle note:** McpToolFactoryV2.GetToolsListAsync() now resets the tracker at discovery start, so reload-driven rediscovery can safely reuse the same tracker instance without stale attribution leaking across cycles.

### 2026-05-16 — Squad Scribe cross-pollinate (PR #276 multi-agent cycle)

**Agent collaboration on import tracker fix (issue #272):**
- **Hermes (executor):** Wired tracker through all runtime doctor paths (ConfigurationReloadTools, McpToolSetupService → DoctorService), reset tracker per discovery cycle, added parity tests. 849 tests green.
- **Farnsworth (architect):** Identified architectural gap v1 (CLI-only wiring), recorded decision (all doctor builders must share provenance seam), approved revised wiring.
- **Cubert (fact-check):** Verified tracker design, caught wiring gap v1, confirmed all claims in v2 (parity tests cover commitments, no stale refs).

**Architectural lesson captured in decisions.md:** doctor provenance upgrades must thread through all report builders (BuildDoctorReportForCliAsync AND BuildDoctorReportFromConfig), not just CLI path. Shared per-discovery tracker owned by discovery layer, injected into report builders as read-only snapshot. Reset-at-cycle-start prevents stale attribution on reloads.

**Process note:** User directive recorded (Steven request) — all squad agents must include their name when posting GitHub comments (e.g., "— Farnsworth" or "[Bender]").

## Learnings

### 2026-05-17T08:12:00-05:00 — PR #278 review (issue #277)

**Verdict:** REJECT.

**What I reviewed:** `main...squad/277-log-forging-fixes` for `AuthenticationServiceExtensions.cs`, `PowerShellAssemblyGenerator.cs`, and `LoggerExtensions.cs`, focusing on whether `LogSanitizer.Scrub()` was applied at user-controlled log sinks without needless spread into internal-only values.

**Key observations:** The new scrubbing added in JWT diagnostics and correlation scope handling is directionally correct, and the call-site pattern matches the `LogSanitizer` contract. But `PowerShellAssemblyGenerator.cs` still leaves user-controlled values unsanitized at several log sinks, including `_MaxResults` validation (`commandName`), cache-output helpers (`property`, `filterScript`), and generation-time command-name/error logging. That means the fix is not yet a complete or sustainable logging-hardening pass for the touched file.

**Review comment posted:** REJECT on PR #278 with the specific missed sinks called out for follow-up.

## 2026-05-17T13:12:00Z: Cross-team update — Log-forging fix #277

Bender completed remediation of 24 CodeQL cs/log-forging alerts across PowerShellAssemblyGenerator.cs, AuthenticationServiceExtensions.cs, and LoggerExtensions.cs. Pattern: LogSanitizer.Scrub() applied to all untrusted sources (correlation IDs, JWT claims, config values) at structured log call sites. Build + tests pass. PR #278 open.
## 2026-05-16 — v0.14.1 Release (via Scribe)

Release v0.14.1 shipped successfully. Version bump, release notes, and GitHub release creation completed by Amy. Commit a2a89b3, tag v0.14.1 pushed to origin, release published.
