# Decisions

## Recent Decisions
> Older entries archived to `decisions-archive.md` (entries >7d removed when file >= 50KB).

### 2026-08-05T16:35:00-05:00: #380 APPROVED — Decision B (Isolation-Equivalent Warm Baseline)
**By:** Steven Murawski (User Approval)
**Issue:** #380 (MCP SDK v2 release gate — warm-call p95)
**Decision:** **APPROVE B** — Keep isolation; explicitly retarget warm/throughput comparison to isolation-equivalent baseline; unchanged numeric constants (1.05); re-run enforced gate after methodology fix.
**What:** Farnsworth's proposal B is approved. The 105% warm gate was measuring apples-to-oranges (v1 no-per-call isolation vs v2 mandatory reset). Isolation is the shipped HTTP contract; #389 (native reset) was the correct optimization. Further A-style optimization cannot fit ~0.5ms isolation work into ~0.045ms ratio budget. Methodology amendment: explicit `isolationSemantics` fingerprint field; baseline pairing enforces v2 reset pool vs v1 ephemeral dispose for Stateless warm (same "no cross-call PS state" contract). Stateful warm uses same pool path as Stateless (session affinity is protocol only, not PS stickiness). Numeric thresholds unchanged; sticky v1 mode remains out-of-scope (D framing).
**When:** 2026-08-05 (gate enforced 31046071124 on ba95fa6; soak 31044718081 in progress)
**Companion items:** #389 (native reset) remains landed; #387 (methodology hardening) validated; throughput re-evaluated against fair baseline; sticky PS mode deferred; advisory absolute warm p95 ceiling (2ms) as safety rail (C-lite).

---

### 2026-08-04T18:14:34Z: Hermes #363 PR #384 Revision Decision (Release Docs)
**By:** Hermes (PowerShell Expert)
**What:** PR #384 (`squad/363-release-docs`, b76a2e1) corrected #349 status, protocol scope, and FunctionNames live examples across documentation. #349 gate results documented as completed-FAIL pending investigation #385.
**Why:** Authoritative facts from run 30929198633 (handle-floor gate failed; no root-cause attribution yet). Protocol scope fixed: `2026-07-28` is SDK v2 `server/discover` only; `initialize` negotiates `2025-11-25`/`2024-11-05` unchanged.
**Status:** PR remains DRAFT; 8 files changed, 236 insertions; no reuse of Leela's prior work.

---

### 2026-08-04T12:00:00-05:00: Fry Phase 4 Warm-Call Regression Attribution (PR #378)
**By:** Fry (Tester)
**Issue:** #357 (Phase 4 performance comparison, part of #346)
**PR:** #378 (draft, squad/357-performance-comparison, SHA 6635498)
**Findings:** 3 authoritative same-job paired runs completed. All methodology requirements satisfied.
- **Stateless Warm-Call:** Systematic ALL 3 runs: 108%, 136%, 122% (fail 105% gate). Overhead ~0.16–0.35ms. Phase 4 min (1.05ms) > Phase 0 max (1.17ms) — disjoint distributions.
- **Stateful Warm-Call:** Sporadic spikes in 2/3 runs (15% of calls at 2–9ms); 1/3 clean. Pattern suggests session-tracking contention or GC pause.
- **Cold-start, throughput, memory:** All PASS (no regression; throughput 1.1–2.2× FASTER).
**Fry Recommendation:** Route Stateless regression to production investigation (Option A); accept Stateful as intermittent known behavior, document in Stateful warm gate (Option C).

---

### 2026-08-04T07:26:00-07:00: Bender #352 SessionRunspace* Deprecation Policy
**By:** Bender (Backend Developer)
**Issue:** #352 (remove legacy session-affine code)
**PR:** #381 (squad/352-legacy-cleanup → main)
**Decisions:**
1. Mark `McpServerConfiguration.SessionRunspace*` properties (5 total) as `[Obsolete]`, not deleted — no breaking change; migration guidance to `McpServer:RunspacePool` config section. Removal gate: next major version.
2. Move `opts.IdleTimeout` assignment entirely inside `if (!isStateless)` block. Stateless never touches IdleTimeout; removes MCP9006 pragma suppression.
3. Both HTTP transport modes use `StatelessRunspacePool` (restatement + DI-chain test verification via `HttpLegacyCleanupTests`).

---

### 2026-08-04T07:26:00-07:00: Bender #385 Soak Handle-Floor Min-Sample Contract
**By:** Bender (Backend Developer)
**Issue:** #385 (handle-floor drift investigation)
**PR:** #386, branch squad/385-handle-drift-investigation
**Run:** 30929198633 (60.57 min, 102,368 requests) — threshold 0.010/s.
**Decisions:**
1. `MinHandleFloorWindowSamples = 5` (contract). Filters terminal bin < 5 samples; does not alter slope threshold. Gate still fails post-fix (12-window slope = 0.04553/s > 0.010/s).
2. Handle-type evidence: ETW (Microsoft-Windows-Kernel-Process) only safe; Process.HandleCount is total only; Sysinternals handle.exe unavailable on GitHub Actions hosted runners. Limitation documented.
3. `server_stability` gate was vacuous (never wrote SERVER_CRASH). Fixed: TakeSampleAsync detects HasExited && !_harnessKilling, writes "SERVER_CRASH exit={code}". DisposeAsync sets _harnessKilling before Kill().
4. Comparison modes (no-op HTTP, direct-PS) deferred — require product changes; documented in analyzer-inputs.json.
5. Burst phase wired (BurstConcurrencyLevel=0 validated), but authoritative run deferred pending baseline drift root-cause characterization.

---

### 2026-08-04T12:00:00-05:00: Fry Performance Gates Benchmark Discoverability Contract
**By:** Fry (Tester)
**Issue:** #336 (benchmark quality gates)
**Decision:** Shared CI verifies HTTP session benchmark scenarios build and are discoverable; uploads case list as artifact. Does NOT make timing assertions. Benchmark suite measures first-session startup, warm-session calls, concurrent warm sessions, bounded-capacity behavior using deterministic config.
**Why:** Benchmark timings are machine-dependent; unsuitable as required checks on shared CI runners. Contract gate prevents accidental scenario removal; controlled-machine BenchmarkDotNet provides reproducible comparison evidence.

---

### 2026-08-04T07:26:00-07:00: Hermes OOP Cancellation and Reload Isolation
**By:** Hermes (PowerShell Expert)
**What:** Interrupted subprocess-pool workers are quarantined and replaced rather than reused. Configuration reload advances pool generation: idle workers configured immediately, active workers complete existing request and retire on return, replacements inherit latest cached config. Prevents setup frames from being sent into worker with active command.

---

### 2026-08-04T07:26:00-07:00: Leela Bounded OOP Cleanup Tracking (2026-07-16)
**By:** Leela (Integration Specialist)
**What:** Out-of-process cleanup retains at most `MaxTrackedCleanupOperations` incomplete tasks (default 16) for shutdown waiting. Every cleanup task gets completion observer, including untracked overflow: completed removed, faults logged, overflow logged explicitly. Prevents repeated stuck-worker replacement from retaining unbounded task/exception collection while preserving bounded disposal waits and failure visibility.

---

### 2026-06-01T20:35:00Z: OAuth authorize proxy forwards only one Entra-compatible prompt value
**By:** Steven Murawski (via Bender)
**What:** `OAuthProxyEndpoints` normalizes `prompt` values before redirecting to Entra v2.0. Unsupported `create` is stripped. If a client sends multiple prompt tokens in one query value, the proxy forwards one supported value in this priority order: `consent`, `select_account`, `login`, `none`. If no supported token remains, `prompt` is omitted.
**Why:** Copilot CLI can send `prompt=consent select_account`, but Entra v2.0 returns `AADSTS90023` for space-separated combined prompt values. Preferring `consent` preserves the strongest client intent while producing an Entra-compatible authorize URL.

---

### 2026-06-01T20:20:00Z: OAuth authorize proxy strips unsupported prompt=create
**By:** Steven Murawski (via Bender)
**What:** The OAuth proxy `/authorize` endpoint strips `prompt=create` before redirecting to Entra v2.0, while preserving supported prompt values such as `select_account` unchanged.
**Why:** Entra v2.0 rejects `prompt=create` with AADSTS90023. Mapping it to `login`, `consent`, or `select_account` would change client intent, so omitting only the unsupported value keeps the existing flow as neutral as possible.

---

### 2026-06-01T19:39:02Z: Malformed config JSON exits as configuration error
**By:** Steven Murawski (via Bender)
**What:** Treat invalid runtime `appsettings.json` content discovered during `serve` settings resolution as a configuration error. The resolver wraps JSON parse failures with the config file path, `serve` catches that as `Configuration error`, and `Program.Main` returns the handler-set exit code when command-line parsing succeeds.
**Why:** Container startup can resolve `/app/appsettings.json` before server startup begins. A malformed mounted or working-directory config previously escaped the `serve` error boundary as an unhandled `JsonReaderException`, obscuring the path and making container failure noisy instead of actionable.

---

### 2026-06-01T19:12:44Z: OAuth DCR proxy echoes client metadata for Copilot CLI
**By:** Steven Murawski (via Bender)
**What:** The OAuth proxy `/register` endpoint should treat GitHub Copilot CLI registration requests as dynamic client registration metadata and return requested client metadata fields, especially `redirect_uris`, alongside the configured static `client_id`. The response also preserves `token_endpoint_auth_method: none` and may echo supplied `client_name`, `grant_types`, `response_types`, and `scope`.
**Why:** Copilot CLI validates the `/register` response body and fails authentication when `redirect_uris` is absent, even when the server returns HTTP 201. Echoing requested redirect URIs keeps the static-client proxy compatible with clients expecting an RFC 7591-shaped response while preserving Entra's configured client ID and public-client authentication method.

---

### 2026-06-01T00:00:00Z: v0.16.3 release remains blocked until test gate completes cleanly
**By:** Steven Murawski (via Amy)
**What:** Do not push `main`, create a release commit, or create/push tag `v0.16.3` yet. The version bump is present in `PoshMcp.Server/PoshMcp.csproj`, Leela prepared `CHANGELOG.md` and `docs/release-notes/0.16.3.md`, and the format gate completed cleanly, but the full `dotnet test PoshMcp.sln --no-restore` run stalled during `PoshMcp.Tests` execution and was stopped before a completion summary.
**Why:** The release process requires committed release artifacts plus green readiness gates before tag publication. Publishing now would bypass the test gate and leave the release notes/version bump uncommitted.

---

### 2026-06-01T00:00:00Z: Auth diagnostics use safe claim allowlist
**By:** Steven Murawski (via Bender)
**What:** Validated JWT diagnostic logging should report only auth-relevant safe fields: audience, scope/scp, roles/role, and issuer. The previous all-claims diagnostic is removed so arbitrary token claim names or values are not written to logs.
**Why:** Operators still need enough context to diagnose audience, scope, role, and issuer mismatches, but arbitrary claim logging can disclose token, user, tenant, or other sensitive details.

---

### 2026-06-01T00:00:00Z: PoshMcp scenario Container Apps Terraform uses prebuilt images
**By:** Steven Murawski (via Amy)
**What:** The generated `infra/` Terraform provisions Azure Container Apps scenario infrastructure but does not build or push container images during Terraform apply. Image references are configurable globally and per scenario, with optional ACR creation and managed identity-based ACR pull.
**Why:** Keeping image build/push outside Terraform makes the scenario environment easier to use from local, CI, and release workflows without coupling `terraform apply` to Docker/Podman availability or registry admin credentials.

---

---

### 2026-06-01T00:00:00Z: Plan-first Terraform for multi-scenario Container Apps
**By:** Steven Murawski (via Amy)
**What:** Prepare Azure Container Apps Terraform through `.azure/deployment-plan.md` first, with `infra/` generation blocked until user approval. The proposed shape uses shared core resources, optional identity/auth, and a scenario map to create multiple PoshMcp Container Apps for basic, advanced, Azure, and auth testing.
**Why:** The Azure prepare workflow requires an approved plan before infrastructure artifacts are generated, and scenario testing needs repeatable multi-instance deployment without hard-coding one app per Terraform file.

---

---

### 2026-06-01T00:00:00Z: App Insights suppresses metrics console exporter
**By:** Steven Murawski (via Farnsworth)
**What:** When Application Insights is enabled and has a resolved connection string, HTTP OpenTelemetry metrics should export through Azure Monitor and must not also register the console exporter. Console metric export remains the fallback when App Insights is disabled or not fully configured.
**Why:** Duplicate console metric output makes operator logs harder to read in App Insights-backed deployments, while preserving console metrics keeps local/default observability behavior intact.

---

### 2026-05-29T11:46:53.2558064-05:00: HTTP non-MCP requests use the default PowerShell runspace key
**By:** Hermes (PowerShell Expert)
**What:** `SessionAwarePowerShellRunspace` should create distinct isolated PowerShell runspaces only when the request includes a non-empty `Mcp-Session-Id`. Requests without an MCP session header, including `/health` probes and other non-MCP HTTP traffic, use the stable `default` runspace key.
**Why:** Connection ID and trace ID are not MCP session identities. Using them as fallback runspace keys lets health probes and unaffiliated HTTP requests create unbounded runspaces that are never cleaned up by MCP session lifecycle, which can starve or stall later initialization/tool calls while health still appears to work.

---

---

### 2026-05-28: MCP prompts/get enforces required args and renders templates post-source
**By:** Steven (via Bender)

**Decision:** For `prompts/get`, validate required prompt arguments from prompt metadata (`Arguments[].Required`) before file or command source execution. Missing or empty required args return MCP `InvalidParams` with the missing argument names. Render template placeholders only after source text is produced for both file-backed and command-backed prompts, supporting `{{argName}}` and backward-compatible `{argName}` replacement.

**Why:** Align prompt behavior with expected templating semantics while preserving command argument variable injection into PowerShell execution.
