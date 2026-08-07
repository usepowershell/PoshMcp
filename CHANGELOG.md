# Changelog

All notable changes to this project will be documented here.

## [Unreleased]

> **Release gate status.** Release-blocking soak non-handle gates passed. The SDK v2
> warm-call/throughput gate ([#380](https://github.com/usepowershell/PoshMcp/issues/380))
> is **GREEN** in enforce mode on `d5d715c`
> (run [31126540107](https://github.com/usepowershell/PoshMcp/actions/runs/31126540107));
> [#380](https://github.com/usepowershell/PoshMcp/issues/380) and
> [#349](https://github.com/usepowershell/PoshMcp/issues/349) are closed.
> **Known follow-up:** residual Windows FullMix handle-floor slope is deferred to
> [#396](https://github.com/usepowershell/PoshMcp/issues/396) — not a migration blocker.
> Final release verification: [#360](https://github.com/usepowershell/PoshMcp/issues/360).

### Added
- **Stateless HTTP transport (default)** — HTTP now defaults to `Stateless` mode, aligning with MCP SDK v2 semantics. Each tool call leases a clean worker from the shared warm `StatelessRunspacePool`, resets it before use, and returns it after use. No cross-call PowerShell state is preserved.
- **Shared warm runspace pool** — HTTP tool calls are served from a single `StatelessRunspacePool` shared across all sessions. Pool behaviour is controlled by the `McpServer:RunspacePool:*` configuration keys (see Configuration Reference).
- **Health and readiness endpoints** — `/health` reports `runspace_pool` status; `/health/ready` tags pool readiness. Metrics expose pool depth, lease latency, and eviction counts.
- **Explicit Stateful HTTP mode (compatibility option)** — Setting `McpServer:HttpTransportMode` to `Stateful` retains MCP protocol/session lifecycle for clients that require `Mcp-Session-Id` continuity. This mode does **not** select or retain a PowerShell worker per session; all calls still lease from the shared pool and PowerShell variables, modules, and working directory are not preserved across calls.
- **Migration and startup-script documentation** — Added `docs/articles/migration-v1-v2.md` and `docs/articles/startup-scripts.md`.

### Changed
- **MCP SDK upgrade** — `ModelContextProtocol` and `ModelContextProtocol.AspNetCore` updated to **2.0.0**. SDK v2 introduces the `server/discover` capability using the `2026-07-28` MCP protocol spec. Standard `initialize` negotiates `2025-11-25`; `2024-11-05` remains supported as a compatibility fallback.
- **Startup script scope** — Startup scripts run once per warm worker during pool initialisation. Scripts must be idempotent and thread-safe; they do not run once per process or per tool call.
- **`CommandNames` replaces `FunctionNames`** — The canonical configuration key is now `CommandNames`. The legacy `FunctionNames` key is accepted with a deprecation warning and will be removed in a future major version.

### Deprecated
- **`SessionRunspace*` configuration keys** — `SessionRunspaceCapacity`, `SessionRunspaceWarmStandbyCount`, `SessionRunspaceIdleTtlSeconds`, `SessionRunspaceSweepIntervalSeconds`, and `SessionRunspaceAcquisitionTimeoutSeconds` are translated to their `McpServer:RunspacePool:*` equivalents at startup with a `LogWarning` for each key used. These keys will be removed in a future major version. Migrate to the current keys (see configuration reference).
- **`SessionAwarePowerShellRunspace` and `SessionRunspaceOptions` public types** — Deprecated compatibility surfaces; retained until next major version.
- **HTTP-with-SSE transport** — Available only via `McpServer:EnableLegacySse`; not recommended for new deployments.
- **Tasks extension** — Deferred; not shipped in this release.

### Breaking
- **HTTP cross-call PowerShell state is no longer preserved** — Stateless HTTP does not retain PowerShell variables, modules, or working directory between tool calls. Workflows that relied on per-session runspace affinity must migrate to explicit request arguments, external state keyed by authenticated identity, or stdio process-scoped mode. Restoring per-session runspace affinity requires reverting to the previous code and package versions; no configuration switch re-enables it.
- **Worker affinity removed** — `Mcp-Session-Id` is no longer used to route requests to a specific PowerShell worker.

### Upgrade Notes
- Run `McpServer:HttpTransportMode=Stateful` only to preserve MCP protocol/session lifecycle for clients that require it. It does not preserve PowerShell execution state.
- Migrate `SessionRunspace*` keys to `McpServer:RunspacePool:*` to silence deprecation warnings. See [migration guide](docs/articles/migration-v1-v2.md).
- Update startup scripts to assume once-per-warm-worker execution. See [startup-script guide](docs/articles/startup-scripts.md).
- See the [SDK v2 pre-release notes](docs/release-notes/sdk-v2-upgrade.md) for the full scope and rollback instructions.

## [0.18.0] - 2026-07-17

### Added
- **HTTP session runspace lifecycle controls** - Added configurable session-runspace capacity, idle retention, sweep interval, warm-standby count, and acquisition timeout settings under `McpServer`. Runspaces are released on session expiry, client `DELETE`, dynamic tool reload, and host shutdown.
- **MCP endpoint origin validation** - Requests that carry an `Origin` header are now accepted only when they are same-origin or match `Authentication:Cors:AllowedOrigins`.
- **HTTP-session benchmark contract** - Added benchmark scenarios for startup and first-session work, warm-session latency, concurrent session throughput, and bounded-capacity rejection. CI verifies scenario discovery and the benchmark contract without shared-runner timing thresholds.

### Changed
- **Streamable HTTP protocol negotiation** - `2025-11-25` clients must retain and send the negotiated `MCP-Protocol-Version` and `Mcp-Session-Id` headers after initialization; `2024-11-05` clients remain compatible without the protocol-version header after initialization. The legacy HTTP-with-SSE transport is now opt-in through `McpServer:EnableLegacySse`.
- **HTTP runspace isolation** - Each HTTP MCP session now has a dedicated initialized PowerShell runspace. Headerless requests use isolated one-shot runspaces and no longer retain state between requests; warm standbys are initialized separately from assigned or leased capacity.
- **MCP schema and error semantics** - Tools without user parameters now advertise a strict empty-object input schema, and PowerShell command failures surface through MCP error handling rather than serialized JSON error strings.
- **MCP SDK upgrade** - Updated `ModelContextProtocol` and `ModelContextProtocol.AspNetCore` to 1.4.1.

### Fixed
- **Out-of-process execution resilience** - Hardened cancellation, configuration reload, worker cleanup, and worker tracking for out-of-process PowerShell execution.
- **HTTP runspace shutdown cleanup** - HTTP session runspaces are disposed during host shutdown, including releases requested while a tool invocation is active.

### Documentation
- Updated transport, configuration, session-management, advanced, logging, Docker, and README guidance for Streamable HTTP protocol migration, origin validation, and session-runspace lifecycle behavior.

### Tests
- Added Streamable HTTP conformance, origin-validation, session-lifecycle, tool-schema, error-handling, and out-of-process cleanup coverage.

### Breaking
- Cross-origin browser clients that send an `Origin` header receive `403` unless the origin is same-origin or listed in `Authentication:Cors:AllowedOrigins`.
- `2025-11-25` Streamable HTTP clients must send a valid negotiated `MCP-Protocol-Version` header after initialization; missing, invalid, or unsupported versions receive `400`.
- Headerless HTTP calls no longer preserve PowerShell state between requests.
- Consumers that interpreted serialized `{"error": ...}` tool results must handle MCP error responses.

### Upgrade Notes
- Add each required exact HTTPS browser origin to `Authentication:Cors:AllowedOrigins`; do not use wildcard origins.
- Ensure `2025-11-25` clients retain and send both `Mcp-Session-Id` and `MCP-Protocol-Version` after initialization. Use an HTTP session for PowerShell state that must persist between calls.
- Enable `McpServer:EnableLegacySse` only for required legacy clients that cannot use Streamable HTTP.

## [0.17.0] - 2026-06-03T00:00:00Z

### Added
- **HTTP `/mcp` route alias** - The HTTP transport now responds on both `/` and `/mcp` endpoints, enabling compatibility with Microsoft Scout (clawpilot) clients that expect MCP on the `/mcp` path. The alias endpoint applies the same authorization and MCP protocol handling as the root endpoint.

### Tests
- Added integration test coverage for `/mcp` route alias functionality.

### Breaking
- None.

### Upgrade Notes
- **Drop-in upgrade.** Existing clients using the `/` endpoint are unaffected. Microsoft Scout and other clients can now connect to `/mcp` without configuration changes.

## [0.16.4] - 2026-06-02T00:00:00Z

### Fixed
- **OAuth authorize prompt compatibility** - The OAuth `/authorize` proxy now normalizes `prompt` forwarding for Microsoft Entra by stripping unsupported values and forwarding at most one supported prompt token (`consent`, `select_account`, `login`, or `none`) to avoid `AADSTS90023` failures.
- **Malformed configuration error handling** - Startup configuration parsing failures now surface as configuration errors with file-path context and stable command exit behavior instead of unhandled JSON exceptions.

### Changed
- **OAuth dynamic client registration response shape** - `/register` now echoes expected client metadata fields (including `redirect_uris`) while preserving configured static client identity behavior for Copilot CLI compatibility.
- **Infrastructure docs update** - Updated Azure infrastructure guidance for the current OAuth and deployment behavior.

### Tests
- Added unit coverage for OAuth prompt normalization and OAuth registration response compatibility.
- Added unit coverage for transport selection and configuration error propagation paths.

### Breaking
- None.

### Upgrade Notes
- **Drop-in upgrade.** No configuration schema migration is required.
- If you use Entra-backed OAuth flows, this release improves compatibility with clients that send combined or unsupported `prompt` values.

## [0.16.3] - 2026-06-01T00:00:00Z

### Added
- **Azure Container Apps scenario Terraform** - Added `infra/` Terraform for repeatable PoshMcp scenario deployments on Azure Container Apps, including shared Log Analytics, Application Insights, optional ACR, user-assigned managed identity, optional Azure Files storage, and scenario-driven Container Apps.
- **Logging and metrics guide** - Added `docs/logging-and-metrics.md` covering logging providers, stdio file logging, tool invocation logs, health-check logs, log sanitization, correlation IDs, OpenTelemetry metrics, Application Insights behavior, and troubleshooting patterns.

### Changed
- **Application Insights metrics export** - Centralized Application Insights configuration and suppresses the HTTP console metrics exporter when Application Insights is enabled with a resolved connection string. Console metrics remain the fallback when Application Insights is disabled or incomplete.
- **Authentication diagnostics** - JWT validation diagnostics now log only sanitized auth-relevant claim values for audience, scope, roles, and issuer instead of arbitrary token claims.
- **Repository maintenance** - Removed obsolete squad automation workflows from `.github/workflows`.

### Tests
- Added unit coverage for Application Insights configuration, connection string fallback, Azure Monitor registration, console exporter suppression, safe authentication claim summaries, and doctor diagnostic sections.

### Breaking
- None.

### Upgrade Notes
- **Drop-in upgrade.** No runtime configuration migration is required.
- For Azure Container Apps scenario testing, build and push a PoshMcp image before applying the Terraform deployment, then configure the global or per-scenario image settings.

## [0.16.2] - 2026-05-29

### Fixed
- **Configured command MCP resources use the active command executor when available** — Static/configured `McpResources` entries with `Source: "command"` and a simple command name now execute through the active `ICommandExecutor` when one is available. This lets `resources/read` see commands imported through the same out-of-process/module-aware execution path as tools, including module-imported commands such as `Get-BamiTenantConfiguration`. Script and pipeline command resources still use the existing runspace fallback.

### Tests
- Added unit coverage proving simple command-backed resources use the executor and script/pipeline command resources continue to use the runspace fallback.

### Breaking
- None.

### Upgrade Notes
- **Drop-in upgrade.** No configuration changes required. Recommended for deployments that expose configured command resources backed by module-imported PowerShell commands, especially when using out-of-process execution.

## [0.16.1] - 2026-05-29

### Fixed
- **Stable default PowerShell runspace for sessionless HTTP requests** — HTTP requests without `Mcp-Session-Id` now reuse the shared `default` runspace instead of being keyed by per-request connection or trace identifiers. This prevents health probes, readiness checks, and other non-MCP HTTP requests from creating unbounded isolated runspaces that can cause later MCP initialization or tool work to stall.

### Tests
- Added unit coverage proving sessionless HTTP requests reuse the default runspace while MCP session IDs still preserve per-session isolation.

### Breaking
- None.

### Upgrade Notes
- **Drop-in upgrade.** No configuration changes required. Recommended for Streamable HTTP deployments behind health probes or platform readiness checks.

## [0.16.0] - 2026-05-28

### Added
- **Prompt template rendering for both prompt sources** — `prompts/get` now renders template placeholders in content returned from both `Source: "file"` and `Source: "command"`, with support for preferred `{{argName}}` syntax and backward-compatible `{argName}` syntax.

### Fixed
- **Required prompt argument enforcement at runtime** — `prompts/get` now returns `InvalidParams` when required prompt arguments are missing, null, or whitespace.
- **Prompt metadata stability across retrieval** — prompt retrieval no longer mutates prompt metadata surfaced by `prompts/list`.

### Documentation
- Updated prompt documentation in `README.md` and the resources/prompts guide to reflect source-aware prompt behavior, runtime required-argument enforcement, and supported placeholder syntax.

### Tests
- Added integration coverage for file and command prompt template rendering, required-argument validation behavior, and `prompts/list` metadata stability.

## [0.15.1] - 2026-05-20

### Added
- **Explicit `AssociatedResourceUri` command override links** — `PowerShellConfiguration.CommandOverrides` can now set `AssociatedResourceUri` to attach a successful tool result to an explicitly chosen MCP resource instead of relying only on the implicit noun-derived link. Resolution happens against the exposed resource surface during tool registration, prefers static/custom resources before noun-derived resources, preserves legacy `FunctionOverrides` binding, and falls back to the implicit noun-derived link when the configured URI does not resolve. (#292)

### Fixed
- **Noun-derived resource eligibility tightening** — Noun-derived resources are now registered only when the matching `Get-{Noun}` command has at least one parameter set that can run without required user parameters. This keeps `resources/list`, `resources/read`, and tool-result link injection aligned with the actual readable runtime surface. (#292)
- **Doctor and configuration-status noun-resource parity** — Diagnostic surfaces now reuse the same eligibility-aware noun registry captured during discovery, so doctor and runtime configuration status match the effective runtime noun-resource registry, including overrides and suppressed nouns. (#292)

### Documentation
- Updated the configuration guide and resources guide to document explicit associated-resource links, resolution precedence, and noun-resource fallback behavior. (#292)

### Tests
- Added coverage for explicit associated-resource links, eligibility-aware noun-resource registration, and doctor/config-status parity. (#292)

### Breaking
- None.

### Upgrade Notes
- **Drop-in upgrade.** No configuration changes are required. If noun-derived resources were enabled in `0.15.0`, resources backed only by `Get-*` commands with required parameters will no longer be advertised.

## [0.15.0] - 2026-05-19

### Added
- **Noun-derived MCP resources (spec 012)** — PoshMcp can now derive MCP resources from discovered PowerShell nouns when `PowerShellConfiguration.EnableNounResources` is enabled. Any noun with a matching `Get-{Noun}` command is exposed through `resources/list` and `resources/read` as `poshmcp://resources/{resource_name}` with `application/json` content. Added `NounRegistry`, `EffectiveNounResourceRegistry`, and `McpNounResourceHandler` to build, resolve, and serve the derived resources in both stdio and HTTP hosts. (#281, #283, #288, #290)
- **Per-noun resource overrides** — `PowerShellConfiguration` now supports `NounResourceOverrides`, keyed by the default derived snake_case resource name. Overrides can disable a derived resource, rename the resource, replace its URI or description, or suppress tool-result link injection via `DisableResourceLinkBlock`. Startup validation now checks override conflicts during PowerShell configuration load. (#279, #280)
- **Tool-result resource link blocks** — Successful tool results can now append an MCP resource link block with `application/json+mcp-resource-link`, pointing clients at the readable noun-derived resource for the same noun. This behavior is opt-in with noun resources and respects per-noun override suppression. (#281)
- **Doctor: `nounResources` section** — `poshmcp doctor` now reports noun-resource enablement, registered resources, conflicts, and suppressed nouns, with a matching text-rendered `Noun Resources` section. This gives operators a grounded verification surface for spec 012 configuration and runtime behavior. (#288, #290)

### Fixed
- **Combined `resources/list` de-duplication for static and noun-derived collisions** — When a static MCP resource and a noun-derived resource resolve to the same URI, the combined list is now de-duplicated instead of surfacing duplicate entries. (#283)
- **OOP noun-resource execution backend guard** — Noun-derived resource reads now enforce the intended exactly-one-backend rule in out-of-process mode instead of allowing ambiguous execution wiring. (#289)

### Documentation
- **Resources and prompts guide** — Documented noun-derived resource behavior, the `application/json+mcp-resource-link` payload shape, override behavior, suppression cases, and the opt-in enablement model in the existing resources guide. (#291)
- **Configuration guide** — Added configuration coverage for `EnableNounResources` and `NounResourceOverrides`, keeping behavior and enablement guidance aligned. (#291)

### Tests
- Added integration coverage for noun-derived `resources/list`, `resources/read`, link-injection behavior, override handling, suppression, collisions, and doctor reporting.
- Added unit coverage for `NounRegistry` and doctor noun-resource rendering.

### Breaking
- None.

### Upgrade Notes
- **Opt-in feature.** Existing deployments are unchanged unless `PowerShellConfiguration.EnableNounResources` is set to `true`.
- After enabling the feature, run `poshmcp doctor` and inspect the `nounResources` / `Noun Resources` section to verify the effective registry, conflicts, and suppressed nouns.

## [0.14.0] - 2026-05-15

### Added
- **Doctor: `moduleImports` section (spec 011 — Issue #263)** — `poshmcp doctor` now reports per-module, per-pattern, and per-tool import diagnostics under a new `moduleImports` JSON section (and a corresponding text-rendered "Module Imports" block). Each module entry surfaces resolved version/path, contributed-tool count and names, and an `ok`/`warning`/`error` status; pattern entries distinguish `filter`, `discovery`, and `exclude` roles per FR-263-9; tool entries attribute every exposed tool to a `commandName`, `module`, `pattern`, or `unknown` source. The section is omitted entirely for `CommandNames`-only configurations (FR-263-6, SC-263-4). (#265, #267, #269, #270)
- **OOP wire-format parity for `moduleImports`** — Out-of-process hosts (`oop-host.ps1`, `oop-host-pool.ps1`) now emit a parallel `RemoteModuleImportsPayload` and per-tool source attribution via three new additive nullable fields on `RemoteToolSchema` (`SourceModule`, `SourcePattern`, `SourceDetail`) per FR-263-11. The doctor consumer skips the in-process `Get-Module` probe entirely when an OOP payload is present, ensuring InProcess and OutOfProcess produce byte-identical `moduleImports` JSON (SC-263-3). Older OOP hosts that omit the new fields fall back to `tools[].source = "unknown"` with a one-time warning in `DoctorReport.Warnings`, preserving backward compatibility. (#268, #271)

### Breaking
- **Doctor: `summary.status` flips `healthy → errors` when configured modules fail to resolve, and `healthy → warnings` when modules resolve without contributing tools or include patterns match nothing.** Existing `CommandNames`-only configurations are unaffected — the new `moduleImports` section is omitted and `summary.status` is computed as before (SC-263-4). Operators relying on `summary.status === "healthy"` as a proxy for "everything is fine" should now also tolerate `warnings`/`errors` driven by misconfigured `Modules`/`IncludePatterns`/`ExcludePatterns`. (#267, #270)

## [0.14.2] - 2026-05-17

### Fixed
- **Security: log forging hardening across remaining sinks** — Applied `LogSanitizer.Scrub()` to the remaining user-controlled and environment-controlled log sink inputs across the server, including `PowerShellAssemblyGenerator.cs`, `AuthenticationServiceExtensions.cs`, and logger helper paths. The hardening pass also scrubbed adjacent diagnostic sinks that followed the same CWE-117 pattern even when they were not part of the original CodeQL alert set. (#277, #278)

### Upgrade Notes
- **Drop-in upgrade.** No configuration changes required. Sanitized log output now escapes control characters instead of emitting raw user-controlled values.

## [0.14.1] - 2026-05-16

### Added
- **Per-tool import source tracking across runtime diagnostics** — Added `IToolImportSourceTracker` threading so runtime diagnostics surfaces preserve the same `commandName | module | pattern | unknown` import-source attribution already used during discovery. Runtime `get_configuration_status` and troubleshooting output now stay aligned with doctor/report generation instead of falling back to `unknown`. (#272, #276)

### Tests
- Added coverage to verify import-source attribution is preserved across CLI doctor, runtime status, and configuration-troubleshooting paths.

### Documentation
- Published the tutorial series covering local stdio, Docker HTTP, and API key authorization scenarios. (#273, #274, #275)

### Upgrade Notes
- **Drop-in upgrade.** No configuration changes required.

## [0.13.1] - 2026-05-15

### Added
- **Configurable `NameClaim` for AAD authentication.** `AuthenticationSchemes[*].ClaimsMapping` now accepts an optional `NameClaim` setting that maps `ClaimsIdentity.Name` from a non-default claim (e.g. `preferred_username` for AAD v2.0 access tokens). Fixes the doctor report showing `Identity name is null despite being authenticated` when the configured token authority emits `preferred_username` instead of `name`. Backwards compatible — when unset, the JWT bearer default (`name`) is used. (#262, #264)

### Fixed
- **Doctor: `effectiveProcessPoolSize` / `effectiveMinHealthyForStartup` displayed `0` in non-ProcessPool host modes.** These fields are now reported as the string sentinel `"n/a (Pool mode)"` (or `"n/a (Subprocess mode)"`, etc.) when ProcessPool sizing does not apply, eliminating the misleading `0` that conflicted with the configured pool size. The fields' JSON type is now `string` rather than `int`; consumers that parsed these as integers must update accordingly. (#261, #266)

## [0.13.0] - 2026-05-14

### Added
- **Help-aware tool descriptions (spec 010 — marquee)** — Tool and parameter descriptions exposed to MCP clients are now sourced from `Get-Help` output rather than the raw PowerShell syntax line. Implements FR-500/FR-510 description precedence chains and FR-540 sanitization. In-process and out-of-process paths produce byte-identical schemas. (#226, #240, #234, #247)
- **`IToolMetadataSource` seam** — Extracted metadata resolution into a dedicated abstraction so in-process and OOP hosts share a single source of truth for tool descriptions and parameter metadata. `HelpAwareToolMetadataSource` is now the default. (#225, #228, #238, #241, #249, #250)
- **OOP `RemoteToolSchema` extended** — Out-of-process schema now carries the full description and per-parameter metadata across the OOP boundary, eliminating description loss in subprocess and pool host modes. (#227, #239)
- **Doctor: `descriptionSource` reporting** — `poshmcp doctor` and the `get-configuration-troubleshooting` MCP tool now report the resolved description source (e.g. `help`, `attribute`, `syntax`) per command and per parameter, making schema provenance auditable. (#230, #244)
- **OTel counters for description-source resolution** — New metrics emitted under the description-source resolution pipeline so operators can monitor which sources are being hit at runtime. (#231, #245)

### Fixed
- **`SwitchParameter` round-trips through MCP JSON arguments** — Switch parameters now correctly survive the MCP JSON → PowerShell argument bind, fixing tools whose switches were silently dropped. (#222)
- **Parameter descriptions wired through to MCP `inputSchema`** — Per-parameter descriptions resolved by the metadata source are now emitted on the tool's `inputSchema.properties[*].description`, where MCP clients actually read them. (#242, #248)
- **`HelpAwareToolMetadataSource` wired as default in `PowerShellSchemaGenerator`** — Closes the gap where the new metadata source was registered but not selected by the schema generator's default code path. (#249, #250)
- **Misleading `RemoteToolSchema` XML doc** corrected. (#233, #235)

### Tests
- **Tool description parity, regression, and parameter-set consistency tests** — New parity suite asserts that in-process and OOP paths produce identical tool descriptions and parameter metadata; regression tests lock the FR-500/FR-510/FR-540 behavior. (#229, #243)
- **Pre-spec010 `tools/list` snapshots captured** to anchor regression comparisons. (#224, #236)

### Documentation
- **Exposing-tools doc** — Documents the FR-500/FR-510 description precedence chains and FR-540 sanitization rules so module authors can reason about which description wins. (#234, #247)
- **Spec 010 promoted to Accepted.**

### Benchmarks
- **Pre-spec010 cold-start baseline** captured. (#223, #237)
- **Post-spec010 cold-start gate** added: cold-start regression is now gated at <50% vs the run-5 baseline. (#232, #246)

### Behavior notes
- Tools whose descriptions were previously the raw PowerShell syntax line now show help-derived descriptions to MCP clients. This is the intended behavior change for spec 010 — no API surface change, but the strings clients see will look meaningfully different (and better) for any command with comment-based help or `MAML` help available.

## [0.12.3] - 2026-05-12

### Fixed
- **OOP executor: stale-looking output returned on non-terminating errors.** When a PowerShell command emitted pipeline output and then wrote a non-terminating error (`Write-Error`, parameter validation failure on a nested call, etc.), the OOP executor logged `hadErrors=true` but still returned the partial pipeline output as a successful tool result. MCP clients saw what looked like cached or stale output from a prior tool invocation. The executor now throws `InvalidOperationException` (prefixed `"OOP error:"`) when `hadErrors && !cancelled`, so MCP surfaces a proper tool error to the client. Cancellation path is unchanged. Applies to both the single-host executor and the subprocess pool.
- **OOP host script: defensive per-invoke variable scope.** `oop-host.ps1` and `oop-host-pool.ps1` now pass `useLocalScope=$true` to `PowerShell.AddScript`, so the per-invoke working variable lives in a child scope discarded at pipeline return rather than the runspace's default scope. Defense in depth against any future regression where a pooled runspace's state could be observed across invokes.

### Behavior notes
- This is a behavior change for callers that previously consumed partial output from a tool which also wrote a non-terminating error: those calls now return a tool error instead of a misleading success payload. If your client depends on partial output, switch the tool to suppress errors (`-ErrorAction SilentlyContinue`) or return a structured result.

### Tests
- New regression tests covering cross-invoke output isolation for both single-host and pool-host paths, including a production-shape pool configuration (`runspacePoolSize=10`, alternating different scripts across 50 iterations). All 47 `Category=OutOfProcess` tests pass.

## [0.12.2] - 2026-05-12

### Fixed
- **OOP single-host: auto-recovery when subprocess dies mid-invoke.** When a user command terminated the `pwsh` subprocess (e.g. native crash, `[Environment]::Exit`, or a misbehaving cmdlet), the single-host executor would surface `InvalidOperationException: OOP subprocess is not running` for every subsequent tool call until the server was restarted. The executor now restarts the subprocess, replays the cached `SetupAsync` environment configuration (module paths, imports, startup scripts), and retries the failing invoke once. Restart is serialized via an internal lock so concurrent failing invokes only trigger a single recovery. The pool-host path was already self-healing via its reconciler — this brings the single-host path to parity.

### Tests
- New integration tests for the single-host executor:
  - `SubprocessCrash_NextInvokeAutoRecovers` — kills the live pwsh process and asserts the next invoke succeeds against a fresh subprocess (different PID).
  - `UserCommandKillsHost_NextInvokeAutoRecovers` — simulates a real-world misbehaving cmdlet by importing a module whose function calls `[System.Environment]::Exit`, then asserts that a subsequent benign invoke recovers against a fresh subprocess.

## [0.11.0] - 2026-05-07

### Added
- **Out-of-process subprocess pool (marquee)** — New `Pool` host mode runs PowerShell in a pool of warm out-of-process `pwsh` hosts, dramatically reducing per-invoke latency vs. cold-start while keeping each host isolated. `Pool` is now the default `SubprocessHostMode`. (#196)
  - `ProcessPool` mode: process-per-invoke pool executor for workloads requiring full process isolation. (#175 series)
  - Extracted `OutOfProcessHost` abstraction so host strategies (single, pool, process-pool) share a common contract.
  - Cancellation propagation across the OOP boundary — client-cancelled invokes now reliably terminate the underlying host work. (#188)
- **PoshMcp.Benchmarks harness** — New BenchmarkDotNet-based project covering cold-start, warm-invoke throughput, and payload-size serialization scenarios, with a custom P99 statistic column for tail-latency reporting.

### Improved
- **Documentation** — README and DESIGN updated to reflect `Pool` as the default host mode and to document the full `SubprocessHostMode` taxonomy (InProcess / Subprocess / Pool / ProcessPool). (#210)
- **Spec 004** — Runspace pool experiment plan published under `specs/004-out-of-process-execution/`. (#187)

### Fixed
- **OOP host: `ConvertTo-Json` wrapping** — Result serialization for OOP hosts now wraps output through `ConvertTo-Json` consistently, fixing payload shape regressions for complex objects. (#203)
- **OOP host: `$Error` cleared before invoke** — Prevents stale errors from a previous invocation leaking into the next caller's diagnostic output. (#189)

### Security
- **CWE-117 log-injection hardening in OOP host** — All log call sites consuming attacker-controllable values (MCP method names, subprocess stdout/stderr, echoed request ids) are now scrubbed via `LogSanitizer.Scrub()` before logging.
- **CI hardening** — All GitHub Actions workflows pinned to minimum required permissions; `SECURITY.md` published with vulnerability reporting policy.

## [0.10.0] - 2026-05-03

### Added
- **Program.cs maintainability refactor** — Extracted major concerns into dedicated classes: `SettingsResolver`, `ConfigurationFileManager`, `CommandHandlers`, `DoctorService`, `McpToolSetupService`, `StdioServerHost`, `HttpServerHost`, `CliDefinition`, and `LoggingHelpers`. Achieves 73% reduction in `Program.cs` lines (from ~800 to ~210), improving maintainability and testability.

### Improved
- **Authentication/OAuth reliability wins**
  - RequiredRoles: Now uses OR semantics - users need any one configured role instead of all.
  - MapInboundClaims: Disabled to preserve short JWT claim names (`scp`, `roles`) for consistent policy enforcement.
  - RequiredScopes: Standardized to short names (e.g., `user_impersonation`) matching JWT claim format.
  - RFC 9728 headers: 401 challenge now includes `WWW-Authenticate` `resource_metadata` header and `/token` proxy strips legacy `resource` parameter.
- **Documentation improvements** — Updated Entra ID authentication guides with scope naming clarifications and improved OAuth configuration guidance.
- **Tests: 590 passed, 0 failed, 1 skipped ✅ | Format verification passed ✅**

## [0.9.21] - 2026-05-03

### Fixed
- **Tests: DoctorReport role claim lookup** — Updated `DoctorReportTests` to use `"roles"` as the claim type instead of `ClaimTypes.Role` (WS-Federation long URI). Required after `MapInboundClaims = false` was enabled in v0.9.20, which caused `DoctorReport.cs` to look up roles by their short JWT claim name. All 590 tests now pass.

## [0.9.20] - 2026-05-03

### Fixed
- **Authentication: OR semantics for RequiredRoles checks** — `HasRequiredRoles()` now uses `.Any()` instead of `.All()`, so a user needs any one of the configured roles rather than all of them. This matches ASP.NET Core's built-in `policy.RequireRole(string[])` behavior and correctly handles Entra app roles, which are granted individually.
- **Authentication: JWT claim-type remapping disabled** — Added `MapInboundClaims = false` to JWT Bearer options to prevent ASP.NET Core from remapping short JWT claim names (`scp`, `roles`) to WS-Federation long URIs. This fixes policy checks that were silently failing because `FindAll("scp")` returned empty after remapping.
- **Authentication: RequiredScopes format corrected** — `RequiredScopes` config value now uses the short scope name (`user_impersonation`) as it appears in the JWT `scp` claim, not the full URI form (`api://{appid}/user_impersonation`) that Entra strips during token issuance.
- **Diagnostics: Role claim lookup in DoctorReport** — Updated `DoctorReport.cs` to use `FindAll("roles")` (short name) instead of `FindAll(ClaimTypes.Role)` (WS-Fed URI), consistent with `MapInboundClaims = false`.

## [0.9.4] - 2026-05-01

### Fixed
- **OAuth discovery for VS Code MCP clients:** JwtBearer 401 challenge now includes `WWW-Authenticate: Bearer resource_metadata="{url}"` per RFC 9728, enabling VS Code to discover the Protected Resource Metadata endpoint and correctly redirect OAuth flows to Entra ID instead of to PoshMcp's own base URL.
- **ApiKey handler metadata URL:** Fixed invalid `api://` URI being used for PRM URL construction; now correctly uses `{scheme}://{host}/.well-known/oauth-protected-resource`.

## [0.9.3] - 2026-05-01

### Fixed
- **Security: Authentication bypass (second instance)** — `WebApplicationBuilder` loads the container's baked-in `appsettings.json` (which has `Authentication.Enabled: false` as a default) before the custom configuration file is added. The custom file's `Enabled: true` was silently overridden by the base file. All auth middleware gates checked this overridden value, resulting in `UseAuthentication()`, `UseAuthorization()`, and `RequireAuthorization("McpAccess")` being skipped despite correct configuration. Fixed by building a dedicated `authRootConfig` via `ConfigurationLoader.BuildRootConfiguration()` using only the resolved custom config path — the same source the `poshmcp doctor` and `get-configuration-troubleshooting` tools already use. All three auth call sites now read from this consistent source, ensuring what doctor reports matches what the runtime enforces.

## [0.9.2] - 2026-05-01

### Fixed
- **Security: Authentication bypass** — `AddPoshMcpAuthentication()` was not registering `AuthenticationConfiguration` with the .NET options system (`services.Configure<>()`). As a result, `IOptions<AuthenticationConfiguration>` always resolved to its default value (`Enabled = false`) regardless of `appsettings.json` settings. The middleware gates (`UseAuthentication`, `UseAuthorization`) and endpoint authorization (`RequireAuthorization("McpAccess")`) were silently skipped, allowing unauthenticated requests through even when `Authentication.Enabled: true` was configured. Fixed by adding `services.Configure<AuthenticationConfiguration>()` unconditionally in `AddPoshMcpAuthentication()`.
- Adds 3 regression tests in `AuthenticationServiceExtensionsTests` covering auth-enabled, auth-disabled, and missing-section scenarios.

## [0.9.1] - 2026-05-01

### Added
- **Authentication and identity diagnostics in doctor/troubleshooting tool** — Both `poshmcp doctor` and the `get-configuration-troubleshooting` MCP tool now include:
  - `authentication` section: enabled state, configured scheme types (JWT Bearer / API Key), authority/audience presence, key count (no secrets), default policy scopes and roles, protected resource URI, and CORS origins
  - `identity` section: caller identity when available in HTTP context — authenticated state, authentication scheme, name, scopes, and roles; `available: false` in CLI/stdio contexts where no HTTP context exists

### Removed
- `ConfigurationTroubleshootingTools.cs` dead code class — was never instantiated; real implementation lives in `CreateConfigurationTroubleshootingToolInstance` in `Program.cs`

## [0.9.0] - 2026-04-29

### Added
- **Application Insights integration** — Optional Azure Application Insights telemetry via OpenTelemetry. Enable with `ApplicationInsights.Enabled: true` in `appsettings.json`.
- `ApplicationInsightsOptions` configuration model with `Enabled`, `ConnectionString`, and `SamplingPercentage` properties.
- `ConfigureApplicationInsights()` method registers Azure Monitor OpenTelemetry when enabled, with support for connection string from config or `APPLICATIONINSIGHTS_CONNECTION_STRING` environment variable.
- Configurable sampling percentage (1–100) to control telemetry volume.
- `poshmcp doctor` validation for Application Insights configuration — validates connection string format and sampling range without network calls.
- 6 unit tests for `ConfigureApplicationInsights` covering enabled/disabled states, env var fallback, and sampling config.
- 4 integration tests for HTTP server with Application Insights enabled covering startup, logging, health endpoints, and tool discovery.
- `Azure.Monitor.OpenTelemetry.AspNetCore 1.4.0` package reference.

## [0.8.11] - 2026-04-24

### Fixed
- `poshmcp build` now works correctly when run outside the PoshMcp repository directory. The embedded Dockerfile is now materialized to a temporary file before invoking docker, preventing "Dockerfile not found" errors.

## [0.8.10] - 2026-04-24

### Added
- `--appsettings` option for `poshmcp build` command allows users to bundle a local `appsettings.json` file into their container image at build time, simplifying configuration management in containerized deployments.

## [0.8.9] - 2026-04-24

### Added
- `examples/Dockerfile.user` now includes documented PSModule paths showing where PowerShell modules are available in the container (`/usr/local/share/powershell/Modules` for AllUsers, `/opt/microsoft/powershell/7/Modules` for built-in, `/home/appuser/.local/share/powershell/Modules` for CurrentUser).
- Commented `COPY` directive examples in `Dockerfile.user` demonstrating how to easily copy local modules and startup scripts into the container.

## [0.8.8] - 2026-04-24

### Changed
- `poshmcp build --generate-dockerfile` now emits a user deployment template based on the published `ghcr.io/usepowershell/poshmcp/poshmcp` base image instead of the source build Dockerfile
- `install-modules.ps1` is now bundled in the base container image at `/app/install-modules.ps1` — generated Dockerfiles no longer require users to have this script locally
- `examples/Dockerfile.user` updated to reference the bundled script path and use the published base image

### Fixed
- Generated Dockerfile was incorrectly using the base image's own source Dockerfile as the template
