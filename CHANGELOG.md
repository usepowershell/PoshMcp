# Changelog

All notable changes to this project will be documented here.

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
