# Decisions Log
Canonical record of decisions, actions, and outcomes.

# Decisions Log

Canonical record of decisions, actions, and outcomes.


## 2026-04-08

### Serialization migration coverage should anchor on serializer-level tests

**Author:** Fry (via Copilot)
**Date:** 2026-04-08T00:00:00Z
**Status:** Proposed

Add focused unit coverage for `PowerShellJsonOptions` string serialization, because the current migration regression is shared across stdio and HTTP transport paths while the HTTP integration harness is also vulnerable to unrelated `dotnet run` apphost file locks.

**Rationale:**
- The integration failures prove user-visible breakage across transport paths
- A serializer-level regression test gives the narrowest reliable validation target for the fix
- Web integration runs can be polluted by unrelated `dotnet run` file-lock failures, so unit coverage is the safer regression anchor

**Impact:** Prioritize targeted serializer regression coverage while HTTP transport failures are being investigated. Use broader HTTP integration validation as confirmation, not the sole guardrail.

### Normalize PowerShell results before JSON serialization

**Author:** Steven Murawski (via Copilot/Hermes)
**Date:** 2026-04-08
**Status:** Proposed

Convert PowerShell results into JSON-safe scalars, dictionaries, and arrays before handing them to `System.Text.Json`, with explicit handling for `IDictionary`, `IEnumerable`, pointer-like CLR values, recursive `PSObject` graphs, and inaccessible CLR properties.

**Rationale:**
- Direct serialization of nested CLR objects leaked framework dictionary internals into responses
- Members such as `Encoding.Preamble` and pointer-like CLR values can trip unsupported serialization paths
- Normalizing into JSON-safe shapes protects both live command results and cached outputs

**Impact:** The serialization pipeline should normalize nested PowerShell and CLR objects before JSON output so stdio and HTTP transport responses stay predictable and cache-safe.

### Preserve scalar PowerShell BaseObject values during JSON serialization

**Author:** Hermes (via Copilot)
**Date:** 2026-04-08T00:00:00Z
**Status:** Proposed

Treat scalar `PSObject.BaseObject` values, especially strings, as leaf JSON values before enumerating `PSObject.Properties`.

**Rationale:**
- The `System.Text.Json` migration regressed simple command output by serializing wrapped strings as PowerShell metadata such as `Length`
- Scalar leaf handling is the narrow fix for the user-visible string regression
- Complex PowerShell objects still need property-based serialization after scalar cases are peeled off

**Impact:** String and other scalar PowerShell results should serialize as their actual values instead of adapted metadata objects.

### Tester coverage should pin string serialization through execution and cache paths

**Author:** Fry (via Copilot)
**Date:** 2026-04-08T00:00:00Z
**Status:** Proposed

Keep the serializer unit test as the narrow regression anchor, and add focused execution-plus-cache coverage for a string-returning PowerShell command so `ExecutePowerShellCommandTyped` and `GetLastCommandOutput` are pinned against the `[{"Length":N}]` regression.

**Rationale:**
- HTTP integration tests are useful but indirect for this defect
- Prior functional cache assertions only checked for valid JSON and cache consistency
- A direct execution-plus-cache assertion closes the gap on the public response shape that regressed

**Impact:** Regression coverage should include both serializer-level tests and targeted execution/cache assertions for string outputs.

### Reuse existing test build outputs when launching the in-process HTTP test harness

**Author:** Steven Murawski (via Copilot)
**Date:** 2026-04-08T00:00:00Z
**Status:** Superseded — see "poshmcp as single entry point" (2026-04-10)

The in-process HTTP test harness should infer the active test build configuration and launch `PoshMcp.Server` with `dotnet run --no-build --configuration {Debug|Release} -- serve --transport http` so integration tests reuse existing build outputs.

**Rationale:**
- `dotnet test -c Release` already produces the required binaries
- Triggering a second build during `StartAsync()` caused file-lock conflicts against referenced outputs
- Matching the active test configuration removes accidental Debug/Release mismatches in the harness

**Impact:** HTTP integration startup should avoid redundant builds and reduce file-lock failures during test execution.

### User directive

**By:** Steven Murawski (via Copilot)
**Date:** 2026-04-08T00:00:00Z

Do not run builds or tests from VS Code while the MCP server is running; use static inspection instead unless the server is stopped.


## 2026-07

### Restore explicit resource group creation in deploy.ps1

**Author:** Amy Wong (DevOps/Platform/Azure)
**Date:** 2026-07
**Status:** Implemented

When Bicep was modularized to subscription scope, the `New-ResourceGroupIfNeeded` function in `deploy.ps1` was commented out under the assumption that `main.bicep` would handle resource group creation. However, `Initialize-ContainerRegistry` runs before `Deploy-Infrastructure`, so the resource group must exist before Bicep runs.

**Decision:** Keep explicit `az group create` in the deployment script **and** the declarative resource group in `main.bicep`. Both are idempotent. The script ensures the RG exists for imperative steps (ACR creation); Bicep re-declares it for completeness and drift correction.

**Rationale:**
- Azure resource group creation is idempotent — creating one that already exists is a no-op
- Mixed imperative/declarative pipelines need the RG available before any `--resource-group` flag is used
- Removing either one creates a fragile dependency on execution order

**Impact:** `deploy.ps1` — `New-ResourceGroupIfNeeded` uncommented and restored in workflow. No changes to `main.bicep` or `validate.ps1`. No breaking changes to any parameters or interfaces.


## 2026-04-09

### Open question decisions for large-result-performance proposal

**By:** Steven Murawski (via Copilot)
**Date:** 2026-04-09T16:43:00Z
**Status:** Decided

User decisions on open questions from Farnsworth's large-result-performance proposal:

- **Q2 (result limiting):** YES — include `_MaxResults` parameter
- **Q4 (cached results):** Cache the FILTERED object, not the full object
- **Q5 (reset semantics):** Support null or the value "reset" to return to the previously configured setting
- **Q6 (gating):** Do NOT gate `set-result-caching` behind `EnableDynamicReloadTools`
- **Q1 (_AllProperties forcing caching):** Not addressed — keep proposal default (no coupling)
- **Q3 (Format-List vs Format-Table):** Not addressed — keep proposal default (use display set)

**Rationale:** User decisions captured for Phase 2 and Phase 2.5 implementation guidance.

### dotnet tool packaging for PoshMcp.Server

**Author:** Bender (Backend Developer)
**Date:** 2026-04-08
**Status:** Implemented

Dotnet tool properties added to `PoshMcp.Server/PoshMcp.csproj`: `<PackAsTool>true</PackAsTool>`, `<ToolCommandName>poshmcp</ToolCommandName>`, `<PackageId>poshmcp</PackageId>`, `<Version>0.1.0</Version>` with standard metadata. Local tool manifest `.config/dotnet-tools.json` created for per-repo installation. Content files (appsettings.json, etc.) excluded from NuGet package via `<Pack>false</Pack>`. `default.appsettings.json` embedded as resource. Pack succeeded; output: `PoshMcp.Server/bin/Release/poshmcp.0.1.0.nupkg` (~26 MB). Usage: `dotnet tool install -g poshmcp` (global) or `dotnet tool install --local poshmcp` (local with manifest).

**Rationale:** `PoshMcp.Server` is the single application binary; `poshmcp` is the CLI entry point for all transports. Tool command `poshmcp` matches project name and convention.

**Impact:** Users can now install via standard dotnet tooling. Requires .NET 10 Runtime. Configuration via cwd `appsettings.json` or environment variables.

### Large Result Set Performance Improvements: Proposal Filed

**Author:** Farnsworth (Lead / Architect)
**Date:** 2026-04-09
**Status:** Proposed

Proposal filed: `specs/large-result-performance.md`. Two complementary changes recommended:

1. **Optional Tee-Object (opt-in):** Make `Tee-Object -Variable LastCommandOutput` conditional. Default OFF. Saves ~50% memory by eliminating duplicated result cache. Utility tools return error when caching disabled.

2. **Default property filtering via Select-Object:** Inject `Select-Object -Property <props>` using output type's `DefaultDisplayPropertySet`. Reduces JSON payload 95%+ for `Get-Process` (80 properties → 5). Configurable per-function; callers can opt out via `_AllProperties=true` parameter.

**Configuration:** New `Performance` section and `FunctionOverrides` dictionary in `PowerShellConfiguration`. Per-function override → global default → built-in default. Both features independently toggleable.

**Impact:** Breaking change for `get-last-command-output` without config (returns error). Mitigated by clear messaging and docs.

**Rationale:** Addresses three compounding causes of hangs: synchronous blocking in `ExecuteThreadSafeAsync`, `Tee-Object` buffering, and reflection-heavy property enumeration. Result count caps and property shaping reduce payloads from ~2 MB to ~80 KB.

### User directive — dynamic property filtering via tool parameter

**By:** Steven Murawski (via Copilot)
**Date:** 2026-04-09T10:25:38Z

Instead of type-specific property shapers, add a universal `SelectProperties` parameter to every MCP tool invocation. When provided, inject a `Select-Object` step into the PowerShell pipeline to reduce properties returned. Applies to any tool, controlled by the AI caller at call time. More flexible than hardcoded type maps; works for any cmdlet without registry maintenance.

### Get-Process MCP Pipeline Analysis: Large Result Set Hang

**Author:** Bender (Backend Developer)
**Date:** 2026-04-09
**Status:** Analysis / Proposed

Root cause analysis: three compounding causes. **Cause 1** — Synchronous `ps.Invoke()` in `ExecutePowerShellCommandTyped` holds the runspace semaphore for full command duration. For `Get-Process` on busy machines (200–300 processes), this means tens of seconds of blocking. Any second MCP call blocked at `WaitAsync()`. **Cause 2** — `Tee-Object -Variable LastCommandOutput` buffers entire process collection before emitting. Each `System.Diagnostics.Process` holds OS handle; full collection stays live through serialization, doubling peak memory. **Cause 3** — `GetSafeProperties` enumerates all ~50 `Process` properties. Properties like `Modules`, `MainModule`, `Threads` make Win32 API calls that can block indefinitely on protected/system processes without elevated token. 200 processes × 50 properties = hang.

**Concrete patterns:**
- Pattern 1 (1 day): Result count cap — truncate before serialization, signal truncation in JSON response.
- Pattern 2 (2–3 days): Async execution — replace `InvokePowerShellSafe` with `InvokePowerShellSafeAsync`, thread CancellationToken through layers.
- Pattern 3 (2–3 days): Property selection registry — static `ResultShaper` concept mapping Type → filtered dict (Process: Id, Name, CPU, WorkingSet; avoids all dangerous properties).

Config schema: `FunctionLimitConfiguration` with `MaxResults` (int, default 50) and `SelectProperties` (list of property names, empty = use type shaper or full serialization).

**Recommendation:** Phase 1: Result count cap (ships immediately, eliminates most hangs). Phase 2: Async execution. Phase 3: Property shaping. Phase 1 alone reduces `Get-Process` payload from ~2 MB to ~80 KB.

### Large Result Set Hang Analysis: Get-Process and Similar Cmdlets

**Author:** Hermes (Observability / Diagnostics)
**Date:** 2026-04-09
**Status:** Analysis / Proposed

Same root causes confirmed independently: (1) Synchronous blocking in `ExecuteThreadSafeAsync` with singleton semaphore; (2) `Tee-Object` buffering; (3) Property reflection on expensive CLR types. Three implementation approaches ranked:

**Approach A** (recommended, medium effort): Property-selected result shaping before serialization. For known-expensive types (Process, FileInfo, Service), emit filtered dict instead of full PSObject graph. Type-to-shaper mappings in `PowerShellObjectSerializer` allow incremental expansion. Produces AI-friendly output, avoids all dangerous properties, O(50) → O(6) per-object serialization.

**Approach B** (low effort, quick win): Result count cap with truncation hint. Cap results to configurable max (default 50), surface truncation flag in JSON. Eliminates large serialization loops. AI caller sees hint and can refine query.

**Approach C** (high effort, high correctness): Async invocation with CancellationToken. Replace `InvokePowerShellSafe` with `InvokePowerShellSafeAsync`, thread token through layers. Allows hung calls to be cancelled; frees thread-pool thread under concurrent requests.

**Recommendation:** Implement B first (2 days, low risk, eliminates user-visible symptom). Then A (makes tool AI-useful). Then C (necessary for correct async behavior and server resilience). A+B together address all three causes; B alone addresses Cause 3 by capping serialization work.

### poshmcp as single entry point — PoshMcp.Web removed

**Author:** Steven Murawski (via Farnsworth/Amy/Leela/Bender)
**Date:** 2026-04-10
**Status:** Implemented

`PoshMcp.Web` has been removed. `PoshMcp.Server` (`poshmcp` CLI tool) is the sole entry point for all transports.

**What changed:**
- `poshmcp serve --transport http` provides full HTTP/web server behavior (health checks, CORS, session-aware runspaces, OpenTelemetry)
- `poshmcp serve --transport stdio` provides MCP stdio transport (unchanged)
- `poshmcp build --tag <image>` delegates to docker/podman to build a container image
- `poshmcp run --mode http|stdio` runs the container
- `docker-entrypoint.sh` simplified to: `exec /app/server/poshmcp serve --transport "$POSHMCP_TRANSPORT"`
- `PoshMcp.Web/` directory deleted; removed from `PoshMcp.sln`
- All tests migrated from `InProcessWebServer` → `InProcessUnifiedHttpServer`
- `POSHMCP_MODE` environment variable replaced by `POSHMCP_TRANSPORT` (values: `http`, `stdio`)

**Rationale:**
- Single binary simplifies installation, deployment, and troubleshooting
- `poshmcp` CLI as Docker entry point is consistent with how professional tools work (same commands locally and in containers)
- PoshMcp.Web carried dead JWT dependencies and no unique functionality

**Impact:** Any reference to `PoshMcp.Web`, `/app/web/`, `POSHMCP_MODE`, or `dotnet run --project PoshMcp.Web` should be updated to use `poshmcp serve --transport <http|stdio>` and `POSHMCP_TRANSPORT`.

---

### dotnet tool Packaging ADR — PoshMcp

**Author:** Farnsworth (Lead / Architect)
**Date:** 2026-04-09
**Status:** Proposed

Architectural decision record: `PoshMcp.Server` becomes dotnet tool. Server: supports both stdio and HTTP transports via `poshmcp serve --transport <stdio|http>`, complete CLI, fully self-contained.

**Key decisions:**
1. **Which project:** `PoshMcp.Server` only (Exe with CLI, stdio-ready).
2. **Tool command:** `poshmcp` (clean, memorable).
3. **csproj changes:** Add tool packaging props. Exclude user configs from NuGet (`<Pack>false</Pack>` on content items). Embed `default.appsettings.json` as resource.
4. **SDK dependency:** Documentation-only. `Microsoft.PowerShell.SDK` ships full runtime; users only need .NET 10 Runtime.
5. **Local manifest:** `.config/dotnet-tools.json` for local install support (`dotnet tool restore`).
6. **NuGet metadata:** PackageId=PoshMcp, Version=0.1.0, Authors=Steven Murawski, Tags=mcp powershell model-context-protocol stdio.
7. **SingleFile/Trim:** Do NOT use. PowerShell SDK and dynamic assembly generation not trim-safe.
8. **Build workflow:** `dotnet pack` + local install via `--add-source ./nupkg`, or publish to NuGet.org.

**User prerequisites:** .NET 10 Runtime. No bundled appsettings.json — users create in cwd. Config layering: embedded defaults → cwd appsettings.json → env vars → CLI flags.

**MCP client wiring example:**
```json
{
  "servers": {
    "poshmcp": {
      "type": "stdio",
      "command": "poshmcp",
      "args": ["serve"]
    }
  }
}
```

## 2026-04-03

### Documentation gap review protocol

**Author:** Amy Wong (DevOps/Platform/Azure)
**Status:** Proposed

After any bulk documentation edit (deduplication, restructuring, redirect creation), run a verification pass:

1. **Code block fences** — every opening ` has a matching close
2. **Link targets** — every [text](path) points to a file that exists and contains the expected content
3. **Redirect stubs** — any file converted to a stub must have its old inbound links verified
4. **Command correctness** — deployment/build commands in docs must match current infrastructure

Prevents broken rendering and incorrect instructions from reaching users after large-scale documentation changes.

### User directive

**By:** Steven Murawski (via Copilot)
**Date:** 2026-04-03T14:07:03Z

When asked for tenant IDs or domain names, use `C:\Users\stmuraws\source\gim-home\AdvocacyBami\data\tenants.psd1` as the lookup source, plus the hardcoded entry `72f988bf-86f1-41af-91ab-2d7cd011db47` for `microsoft.onmicrosoft.com`. User request — captured for team memory.

### User directive

**By:** Steven Murawski (via Copilot)
**Date:** 2026-04-03T18:42:46Z

After any code change, `dotnet format` and `dotnet test` should be run to verify formatting and tests pass.


### 2026-04-09T17:18Z: User directive — aggressive commit strategy
**By:** Steven Murawski (via Copilot)
**What:** "Continue to cache status aggressively" — commit after every logical chunk of work. Do not batch commits. Crash recovery protection.
**Why:** User request after losing in-flight work from Bender and Hermes during a crash.
**Context:** Spawned as crash recovery workflow. Teams spawned: Bender (Select-Object pipeline injection), Hermes (DefaultDisplayPropertySet), Scribe (logging/recovery).


# Decision: PropertySetDiscovery uses temporary runspace and two-step type lookup

**Author:** Hermes (PowerShell Expert)
**Date:** 2026-07
**Status:** Implemented

## Decision

`PropertySetDiscovery` uses a temporary `Runspace` (not the singleton `PowerShellRunspaceHolder`) and a two-step lookup pattern: Get-Command → OutputType → Get-TypeData → DefaultDisplayPropertySet.

## Rationale

1. **Temporary runspace:** Discovery runs at assembly generation time, before the singleton is initialized and before any MCP client connects. Using the singleton would create a startup ordering dependency and could deadlock if the semaphore is already held.

2. **Two-step lookup (no command execution):** Some commands have side effects (Set-*, Remove-*, Stop-*). We never execute the actual command. Instead we read the `OutputType` metadata from `Get-Command`, then query `Get-TypeData` for the type's display property set. This is purely metadata inspection.

3. **Best-effort with null:** If any step fails (no OutputType, no TypeData, no DefaultDisplayPropertySet), we return null. The caller interprets null as "use all properties." This keeps the system working for commands that don't declare output types.

4. **ConcurrentDictionary cache:** Discovery only needs to run once per command name. The cache is process-lifetime.

## Related Decision: IDictionary recursive normalization in serializer

Split `IDictionary` and `IEnumerable` handling in `NormalizePSPropertyValue`:
- **IDictionary** → recursively normalize entries (bounded key-value maps, safe to walk, `.ToString()` is useless on Hashtable)
- **IEnumerable** → keep `.ToString()` (unbounded, may trigger expensive OS calls like `ProcessModuleCollection`)

## Impact

- New file: `PoshMcp.Server/PowerShell/PropertySetDiscovery.cs`
- Modified: `PoshMcp.Server/PowerShell/PowerShellObjectSerializer.cs` (IDictionary handling)
- Consumers: Pipeline construction in `PowerShellAssemblyGenerator` will use `PropertySetDiscovery.DiscoverAll()` at startup to determine which properties to `Select-Object` for each command.

### Integration fixture process cleanup hardening

**By:** Bender (via Copilot)
**Date:** 2026-04-09T00:00:00Z
**Status:** Proposed

In integration fixtures that launch dotnet child processes, use explicit process-tree termination and a centralized teardown helper, and invoke cleanup on startup-failure paths before rethrowing.

**Rationale:**
- Parent-only Kill can leave orphaned child processes
- Startup exceptions can bypass deterministic process cleanup
- Orphaned processes cause longer test sessions and flaky follow-on runs

**Impact:** Integration fixture lifecycle management should always use deterministic process-tree cleanup in both normal teardown and startup-failure paths.

### Integration runtime analysis and leak-guard coverage

**By:** Fry (via Copilot)
**Date:** 2026-04-09T00:00:00Z
**Status:** Proposed

Added dedicated integration lifecycle tests asserting `InProcessWebServer` and `InProcessMcpServer` terminate spawned server processes during `Dispose()`, with focused before/after process snapshots to check for server-process leakage.

**Rationale:**
- User reported slower tests and suspected lingering web/server processes
- Evidence points to startup and first command execution runtime concentration
- Focused lifecycle tests create explicit guardrails against process-leak regressions

**Impact:** Integration coverage now includes explicit disposal/leak checks for in-process server fixtures, reducing risk of unnoticed process-lifecycle regressions.

### 2026-04-09T00:00:00Z: Unified transport selector foundation in server executable

**By:** Bender (via Copilot)
**Status:** Implemented

Added explicit transport mode selection in server startup with default stdio behavior and a dedicated HTTP placeholder branch in the same executable.

**Rationale:** Establishes a compile-safe unified transport foundation without regressing stdio workflows while creating a clear seam for full HTTP transport enablement.

### User directive

**By:** Steven Murawski (via Copilot)
**Date:** 2026-04-09T22:46:44.2180935Z

Use one unified executable for stdio and HTTP transport; ship HTTP transport immediately once green; defer container configuration revamp until after consolidation work.

### HTTP transport phase 1 kickoff sequencing

**Author:** Farnsworth (Lead / Architect)
**Date:** 2026-04-09
**Status:** Proposed

Phase 1 prioritizes shared startup composition and tool wiring first, keeps explicit transport selection per host during refactor, and defers container configuration changes until after coding/validation.

**Rationale:** Preserves low-risk incremental delivery and keeps HTTP shipment path unblocked while convergence work proceeds.

### Unified HTTP transport implementation in server executable

**Author:** Bender (Backend Developer)
**Date:** 2026-04-09
**Status:** Implemented

Implemented real HTTP serve path in `PoshMcp.Server` for `serve --transport http`, reusing web-host patterns where practical (CORS exposing `Mcp-Session-Id`, session-aware runspace wiring, correlation-id middleware, health endpoints, MCP HTTP endpoint mapping, and MCP path normalization).

**Scope controls:** No container changes, no broad cross-project refactor, minimal package additions required for server-hosted HTTP transport and metrics instrumentation.

**Validation:** Release build for server/tests and focused Program + unified HTTP integration tests passed.

### Unified HTTP runspace parity in server host

**Author:** Hermes (Observability / Diagnostics)
**Date:** 2026-04-09
**Status:** Implemented

Implemented session-aware runspace behavior in `PoshMcp.Server` HTTP mode to mirror established web-host semantics, and ensured the same session-aware instance is used for both DI and generated MCP tool execution.

**Rationale:** Preserves proven HTTP session behavior while keeping stdio semantics and dynamic tool behavior unchanged.

### Graceful schema degradation for unsupported CLR overload types

**Author:** Bender (Backend Developer)
**Date:** 2026-04-09
**Status:** Implemented

When MCP JSON schema generation encounters unsupported CLR parameter types (pointer/ref-struct cases), skip only that overload instead of failing server bootstrap.

**Rationale:** Localized schema incompatibilities are recoverable; preserving server startup and remaining overload availability is preferable to full startup failure.

### Version 0.2.2 release completed

**Author:** Amy (via Steven Murawski)
**Date:** 2026-04-09T17:36:00Z
**Status:** Completed

Bumped global tool package version from 0.2.1 to 0.2.2, built release nupkg, updated global install, and verified CLI reports `0.2.2+88dbdbfc09852f4e40f5d9a7e2ced26417d9a12b`.

**Impact:** Release package `PoshMcp.Server/bin/Release/poshmcp.0.2.2.nupkg` is available and deployed for tool users.

### CLI config lifecycle commands in server executable

**Author:** Bender (Backend Developer)
**Date:** 2026-04-09
**Status:** Implemented

Added CLI configuration management commands to `PoshMcp.Server`:
- `create-config` creates a default `appsettings.json` in the current directory (with `--force` support)
- `update-config` updates the active config file using the same resolution precedence as `doctor`

`update-config` defaults to interactive prompts for newly added functions, including advanced per-function overrides (`EnableResultCaching`, `UseDefaultDisplayProperties`, and `DefaultProperties`). A `--non-interactive` switch supports CI/automation workflows.

**Rationale:** Keep configuration lifecycle actions in one executable, avoid manual JSON edits for common scenarios, and preserve predictable file targeting by reusing doctor-style config resolution.

**Impact:** CLI help surface expanded; targeted unit coverage added for create/update flows and advanced prompt behavior; README/TODO updates recorded by the implementation agent.

### Local dotnet tool versioning workflow for global poshmcp updates

**Author:** Amy (DevOps / Platform)
**Date:** 2026-04-09
**Status:** Implemented

Use `PoshMcp.Server/PoshMcp.csproj` as the package source of truth, bump `Version` with a patch increment for small releases, pack to the local feed folder, and update the global tool from that feed.

**Rationale:** `PoshMcp.Server/PoshMcp.csproj` is the correct tool package source because it defines `PackAsTool=true`, `ToolCommandName=poshmcp`, and `PackageId=poshmcp`.

**Operational guardrail:** Before `dotnet tool update -g poshmcp`, stop any running `poshmcp.exe` process to avoid uninstall/update lock failures on `.dotnet/tools/.store/poshmcp/<version>`.

**Standard command sequence:**
1. `dotnet pack .\PoshMcp.Server\PoshMcp.csproj -c Release -o .\artifacts\nupkg`
2. `Get-Process poshmcp -ErrorAction SilentlyContinue | Stop-Process -Force`
3. `dotnet tool update -g poshmcp --version <newVersion> --add-source .\artifacts\nupkg --ignore-failed-sources`
4. `dotnet tool list -g`

**Outcome:** Version bumped from 0.3.0 to 0.3.1, package built, and global tool updated to 0.3.1.

## 2026-04-10

### Doctor troubleshooting MCP surface stays read-only, shared, and explicitly gated

**Author:** Steven Murawski (via Farnsworth/Bender/Fry)
**Date:** 2026-04-10
**Status:** Implemented

Keep configuration troubleshooting exposed only through the existing special-tool registration path in `Program.cs`, have it return the same structured payload as `poshmcp doctor --format json`, and require both runtime configuration enablement and an explicit environment opt-in before the tool appears.

**Rationale:**
- Reusing the shared doctor JSON builder keeps CLI and MCP diagnostics aligned
- The troubleshooting surface is operationally sensitive and should remain disabled by default
- Focused tests can assert the public JSON contract without coupling to internal registration details

**Impact:** The doctor/configuration troubleshooting flow now has one canonical payload builder, one registration seam, and default-hidden exposure semantics.

### Import configured modules before discovery and pin startup ordering with focused tests

**Author:** Steven Murawski (via Hermes/Fry)
**Date:** 2026-04-10
**Status:** Implemented

Explicitly import modules from configuration before any by-name `Get-Command` or tool discovery pass, and keep regression tests that prove discovery fails before startup setup and succeeds after module import or startup script execution.

**Rationale:**
- Discovery-before-import silently drops module-exported functions when autoloading is disabled or constrained
- Configuration semantics already promise that listed modules are part of the tool surface
- Narrow startup-order tests are faster and less brittle than full server boot validation

**Impact:** Startup ordering around module import is now a pinned contract rather than an incidental implementation detail.

### Out-of-process runtime work remains scaffolded until startup and harness support exist

**Author:** Steven Murawski (via Farnsworth/Bender/Fry)
**Date:** 2026-04-10
**Status:** Decided

Treat the current out-of-process MCP path as incomplete until `Program.cs` and the shared `InProcessMcpServer` harness expose a supported `--runtime-mode` startup path with matching config/stderr handling. Keep subprocess and module-isolation tests, but leave end-to-end server tests as documented stubs until that runtime surface is real.

**Rationale:**
- The branch had tests that advanced ahead of the implemented CLI/runtime surface
- Shared harness parity is a prerequisite for trustworthy end-to-end coverage
- Compile-safe stubs preserve intent without letting speculative wiring break the solution build

**Impact:** Recovery work should prioritize supported startup/harness seams before reactivating live out-of-process end-to-end tests.

### Split vendored module layout and `POSHMCP_TRANSPORT` are the recovery baselines

**Author:** Steven Murawski (via Farnsworth/Hermes)
**Date:** 2026-04-10
**Status:** Implemented

Treat the split `integration/Modules/*` layout as canonical, remove partial merge-fallout vendored content such as `integration/Modules/Az.AppConfiguration/2.0.1`, and normalize live helpers, infrastructure, and docs from `POSHMCP_MODE` to `POSHMCP_TRANSPORT` to match the single-entry-point `poshmcp serve --transport ...` architecture.

**Rationale:**
- The partial Az.AppConfiguration tree was incomplete and not importable
- Current integration assets are organized by concrete module name, not umbrella paths
- Old transport environment-variable names encode a retired startup contract

**Impact:** Recovery and future test work should assume split module paths and `POSHMCP_TRANSPORT` as the only supported transport selector.

### Preferred MVP direction for out-of-process hosting is a persistent `pwsh` subprocess over localhost TCP

**Author:** Farnsworth and Hermes
**Date:** 2026-04-10
**Status:** Proposed

If out-of-process hosting proceeds, prefer a single persistent `pwsh` child process that speaks a JSON request/response protocol over localhost TCP, with host-script safeguards that resolve commands through `Get-Command` plus `& $CommandInfo`, keep diagnostics on stderr only, filter null parameters from the splat, and expand umbrella modules into child-module discovery.

**Rationale:**
- Localhost TCP gives one cross-platform transport and the lowest implementation complexity for an isolated subprocess model
- Persistent subprocess state preserves module/session behavior while isolating assembly and module conflicts from the main server
- Protocol-safe host-script rules prevent stdout corruption and reduce injection risk

**Impact:** Future out-of-process implementation work should treat persistent subprocess hosting and protocol-safe host-script behavior as the default design direction.

## 2026-04-11

### Out-of-process execution architecture plan

**Author:** Farnsworth (Lead / Architect)
**Date:** 2026-04-11
**Status:** Proposed

Comprehensive plan for out-of-process PowerShell execution written to `specs/out-of-process-execution.md`. The feature enables modules that crash the in-process runtime (Az.*, Microsoft.Graph.*) to run in a separate `pwsh` subprocess.

**Key architectural decisions:**

1. **Communication protocol: stdin/stdout ndjson** (not TCP). Lower complexity than the localhost TCP direction noted on 2026-04-10 — no port conflicts, no firewall, no connection handshake, works identically cross-platform. TCP remains a future option for multi-client scenarios.

2. **6-phase implementation plan** starting with stub types to fix 13 build errors (Phase 1), then subprocess lifecycle (Phase 2), command discovery (Phase 3), command invocation (Phase 4), IL assembly generation (Phase 5), and integration testing with Az/Graph modules (Phase 6).

3. **`oop-host.ps1` subprocess host** — a PowerShell script running inside the persistent `pwsh` process that handles discover/invoke/ping/shutdown requests via ndjson protocol.

4. **Crash recovery** — automatic subprocess restart with exponential backoff (3 retries in 5 minutes), re-discovery after restart.

5. **No mixed mode** in v1 — RuntimeMode is server-wide (InProcess or OutOfProcess). Per-function routing deferred.

**Impact:** Unblocks `dotnet build` (Phase 1 is immediate priority) and enables PoshMcp to serve modules from the `integration/Modules/` test corpus.

**Full spec:** `specs/out-of-process-execution.md`




# Decision: PoshMcp Release Packaging Workflow (v0.5.1)

**Date:** 2026-04-12
**Author:** Amy (DevOps / Platform Engineer)
**Status:** Confirmed

## Context

Steven requested a new release of PoshMcp be packaged and installed globally. This is the first release from the `main` branch after the `0.5.0` bump commit.

## Decision

Use a **patch version increment** (`0.5.0` → `0.5.1`) for this release. The change set since `0.5.0` is purely operational (embedded oop-host.ps1 resource, CLI wiring) with no breaking changes or new features warranting a minor bump.

## Established Workflow (canonical steps)

1. **Stop any running poshmcp process** — prevents update lock failures on Windows.
2. **Bump version** in `PoshMcp.Server/PoshMcp.csproj` (`<Version>` property).
3. **Pack:** `dotnet pack .\PoshMcp.Server\PoshMcp.csproj -c Release -o .\artifacts\nupkg`
4. **Install/Update:** `dotnet tool update -g poshmcp --version <newVersion> --add-source .\artifacts\nupkg --ignore-failed-sources`
5. **Verify:** `poshmcp --version` should report `<newVersion>+<git-sha>`

## Outcomes

- Package: `artifacts/nupkg/poshmcp.0.5.1.nupkg` (~25 MB)
- Global tool verified: `poshmcp --version` → `0.5.1+fad23f66007916f0c2145e7c5e0eb8a20925c8dd`
- `dotnet tool update` works for both initial install and upgrade; no need to uninstall first.

## Notes

- `artifacts/nupkg/` is the local feed folder; not committed to source control.
- Version bump commit should be made after packaging to keep git history clean.
- NU1510 warnings (redundant explicit package refs) are pre-existing and non-blocking.



### 2026-04-11: CLIXML vs ndjson for OOP subprocess communication

**Author:** Farnsworth (Lead / Architect)
**Date:** 2026-04-11
**Status:** Proposed
**Requested by:** Steven Murawski

## Decision

**Stick with ndjson (newline-delimited JSON) for OOP subprocess communication. Do not adopt CLIXML.**

## Context

Steven asked whether CLIXML (`Export-Clixml`/`Import-Clixml`, PowerShell's native serialization format used by PS Remoting) should replace or complement the ndjson protocol defined in `specs/out-of-process-execution.md`. The current spec has `oop-host.ps1` returning results via `ConvertTo-Json -Depth 4 -Compress` over stdin/stdout.

## Analysis

### The fundamental constraint: MCP output is JSON

The entire pipeline terminates at a JSON string delivered to the MCP client. Any type fidelity gained from CLIXML is necessarily lost when the server converts results back to JSON for MCP transport. This makes CLIXML a detour, not a shortcut.

### Can the server deserialize CLIXML?

Yes — the server already loads `Microsoft.PowerShell.SDK`. The OOP architecture isolates **heavy module loading** (Az.\*, Microsoft.Graph.\*), not the SDK itself. `[System.Management.Automation.PSSerializer]::Deserialize()` would work fine on the server side. This is not a technical blocker — but it's also not a free benefit, because the resulting `PSObject` still needs to go through `PowerShellObjectSerializer.FlattenPSObject()` and then `System.Text.Json` serialization to reach MCP-compatible JSON.

### CLIXML advantages (real but insufficient)

| Advantage | Severity | Assessment |
|-----------|----------|------------|
| Round-trip type fidelity (DateTime Kind, TimeSpan, enums) | Medium | Lost at the JSON output boundary. MCP clients receive JSON strings regardless of internal transport format. |
| ErrorRecord preservation (full exception chain, InvocationInfo) | Medium | Already solved differently: the spec uses a separate `error` response field for PowerShell errors, not serialized ErrorRecord objects in the result set. |
| PSTypeName metadata | Low | The in-process path already strips this via `PowerShellObjectSerializer`. MCP tool schemas, not runtime type names, drive client behavior. |
| SecureString/PSCredential handling | Low | These should never transit the subprocess boundary — security-sensitive types should be handled in-process or via environment variables. |
| Battle-tested by PS Remoting | Neutral | PS Remoting sends CLIXML between two PowerShell endpoints. Our architecture is PowerShell→C#→JSON — different shape. |

### CLIXML disadvantages (concrete)

| Disadvantage | Impact |
|-------------|--------|
| **Triple conversion pipeline:** CLIXML serialize (pwsh) → CLIXML deserialize to PSObject (server) → FlattenPSObject → JSON serialize (server). Current path: JSON serialize (pwsh) → pass-through or parse (server). | Significant complexity and CPU overhead. |
| **Wire size inflation:** CLIXML/XML is 5-10x larger than compressed JSON for equivalent data. The spec already flags large result serialization as Risk #4. CLIXML makes this worse. | Direct performance regression for large result sets. |
| **Parsing cost:** XML parsing is heavier than JSON parsing. `PSSerializer.Deserialize()` reconstructs full PSObject graphs with type metadata — work we then discard. | Unnecessary CPU/memory for throwaway fidelity. |
| **ConvertTo-Json is the simpler, tested path:** `ConvertTo-Json -Depth 4 -Compress` produces output close to what MCP needs. Minimal transformation required on the server side. | Current approach is already nearly optimal. |
| **Subprocess simplicity:** `oop-host.ps1` stays lean with `ConvertTo-Json`. Adding `Export-Clixml` requires temp files or `[PSSerializer]::Serialize()` (string API) and careful stream handling. | Adding CLIXML complicates the host script for no user-visible benefit. |

### Hybrid approach evaluation

A hybrid (JSON for commands, CLIXML for results) was considered. The command direction (server→pwsh) is simple JSON — no type fidelity needed. The result direction (pwsh→server) is where CLIXML would theoretically help. But:

- The server must still produce JSON for MCP clients, so CLIXML→PSObject→JSON adds steps
- The existing `PowerShellObjectSerializer` normalizes PSObjects to JSON-safe shapes — this pipeline already exists and works
- Net effect: CLIXML adds serialization + deserialization + normalization, replacing a direct JSON-to-JSON path

### Where CLIXML genuinely helps (and what to do instead)

The real types that `ConvertTo-Json` handles poorly: deeply nested objects, circular references, types without public properties. The spec already mitigates these:

- **Depth:** `-Depth 4` matches the in-process `MaxDepth = 4` in `PowerShellObjectSerializer`
- **Circular refs:** `ConvertTo-Json` handles this with depth limits; the host script can add `-WarningAction SilentlyContinue`
- **Large result sets:** `_MaxResults` framework parameter and `Select-Object` injection (from the performance spec) cap output before serialization

If specific types prove problematic during implementation, the `oop-host.ps1` script can add targeted handling (e.g., custom serialization for ErrorRecord) without switching the entire transport to CLIXML.

## Recommendation

1. **Keep ndjson/JSON as specified.** The current `ConvertTo-Json -Depth 4 -Compress` approach matches the MCP output format, minimizes conversion steps, and keeps the subprocess host script simple.

2. **Do not add CLIXML support in OOP v1.** The complexity-to-benefit ratio is unfavorable when the pipeline ends at JSON regardless.

3. **If type fidelity becomes a real problem during implementation** (not theoretical), address it surgically in `oop-host.ps1` with per-type handlers rather than switching transport format.

4. **Document this decision** so the team doesn't revisit it without new evidence.

## Impact

No changes to `specs/out-of-process-execution.md`. The existing ndjson protocol remains the correct approach.



# Decision: MCP Authentication Architecture

**Author:** Farnsworth (Lead/Architect)
**Date:** 2026-07-14
**Status:** Proposed

## Context

PoshMcp has no authentication or authorization. For HTTP deployments, any client can connect and invoke any tool — including destructive ones like `Stop-Process`. The MCP protocol spec (2025-06-18) defines an optional OAuth 2.1-based authorization model for HTTP transports.

## Decision

Implement a **two-layer authentication architecture** for PoshMcp's HTTP transport:

1. **ASP.NET Core authentication middleware** validates caller identity (JWT Bearer tokens and API keys), populating `HttpContext.User` which the MCP SDK automatically propagates to `RequestContext.User`.

2. **MCP SDK `CallToolFilters`** enforce per-tool authorization by matching tool names against configuration-driven scope and role requirements in `FunctionOverrides`.

Authentication is **disabled by default** (`Authentication.Enabled = false`) to preserve backward compatibility.

## Key Architectural Choices

- **`McpRequestFilters.CallToolFilters`** for per-tool auth — not `DelegatingMcpServerTool` wrappers. Filters are cross-cutting, have direct access to `User` and tool name, and pair with `ListToolsFilters` for consistent tool visibility.
- **Standard ASP.NET Core auth stack** for token validation — not custom MCP-layer parsing. The SDK's `MessageContext.User` (`ClaimsPrincipal`) proves this is the intended integration point.
- **Per-tool overrides via `FunctionOverrides`** — extends the existing pattern (`RequiredScopes`, `RequiredRoles`, `AllowAnonymous`) rather than creating a parallel config section.
- **Multi-scheme support** (JWT Bearer + API Key) — JWT for spec compliance and enterprise, API Key for simplicity.
- **Stdio transport**: Skips HTTP auth per MCP spec, but `CallToolFilters` still run for tool-level policy enforcement.

## Consequences

- Existing deployments are unaffected (auth off by default)
- New `Authentication` config section in `appsettings.json` with `Enabled`, `Schemes`, `DefaultPolicy`
- `FunctionOverride` class gets three new properties
- `Program.cs` HTTP pipeline gains auth middleware conditionally
- RFC 9728 Protected Resource Metadata endpoint at `/.well-known/oauth-protected-resource`
- New NuGet dependency: `Microsoft.AspNetCore.Authentication.JwtBearer`

## Alternatives Considered

1. **Auth at MCP filter layer only** (parse tokens in `CallToolFilters`): Rejected — reinvents ASP.NET Core's auth stack, misses session-init protection, fragile JWT handling.
2. **`DelegatingMcpServerTool` per-tool wrappers**: Rejected — requires wrapping every tool individually, no cleaner than a single filter, and doesn't pair with tool-list filtering.
3. **Auth enabled by default**: Rejected — would break all existing deployments immediately.

## References

- MCP Spec Authorization: https://modelcontextprotocol.io/specification/2025-06-18/basic/authorization
- C# MCP SDK v1.2.0 API: https://csharp.sdk.modelcontextprotocol.io/
- Full implementation plan: Session workspace `plan.md`

