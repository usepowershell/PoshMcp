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

### 2026-04-14T00:00:00Z: Patch-release publish workflow confirmation
**By:** Steven Murawski (via Amy/Copilot)
**What:** Bump `PoshMcp.Server/PoshMcp.csproj` `<Version>` by patch (`0.5.5` -> `0.5.6`), package with `dotnet pack -o ./nupkg`, publish `poshmcp.0.5.6.nupkg` to `github-poshmcp` feed via `gh auth token`, and update local global tool from `./nupkg`.
**Why:** This matches current repo release convention and successfully validated GitHub Packages publish plus local install update in one flow.

## References

- MCP Spec Authorization: https://modelcontextprotocol.io/specification/2025-06-18/basic/authorization
- C# MCP SDK v1.2.0 API: https://csharp.sdk.modelcontextprotocol.io/
- Full implementation plan: Session workspace `plan.md`

## 2026-04-14

### Deploy docs to GitHub Pages from prebuilt `docs/_site`

**Author:** Amy
**Date:** 2026-04-14
**Status:** Implemented

Deploy documentation to GitHub Pages from the prebuilt `docs/_site` directory using a dedicated workflow at `.github/workflows/docs-pages.yml`.

**Rationale:**
- Keeps CI simple and low risk by avoiding DocFX installation/build in workflow runtime.
- Matches the current repository state where `docs/_site` is already available.
- Uses official GitHub Pages actions with least-required permissions.
- Restricts deployments to documentation changes with `paths: docs/**`.

**Implementation notes:**
- Trigger: `push` on `main` with `paths: docs/**`, plus `workflow_dispatch`.
- Permissions: `contents: read`, `pages: write`, `id-token: write`.
- Concurrency: `group: pages`, `cancel-in-progress: true`.
- Actions: `actions/configure-pages@v5`, `actions/upload-pages-artifact@v3`, `actions/deploy-pages@v4`.

**Follow-up:** If docs source changes are committed without regenerating `docs/_site`, deployment can publish stale output. Consider adding DocFX build-in-CI later if this occurs.

### Build DocFX in CI before GitHub Pages deploy

**Author:** Amy
**Date:** 2026-04-14
**Status:** Implemented

Update docs deployment workflow (`.github/workflows/docs-pages.yml`) to run a DocFX build in CI before uploading and deploying Pages artifacts.

**Rationale:**
- Ensures deployed docs always match committed source content under `docs/`.
- Removes dependence on prebuilt `docs/_site` being manually regenerated.
- Keeps existing trigger scope, Pages permissions, concurrency, and deploy target unchanged.

**Implementation notes:**
- Keep trigger behavior: `push` on `main` with `paths: docs/**`, plus `workflow_dispatch`.
- Install DocFX via dotnet global tool in workflow runtime.
- Run `docfx build docs/docfx.json` from repository root.
- Upload generated `docs/_site` and deploy via existing GitHub Pages actions.

**Impact:**
- Slightly longer workflow runtime due to tool install/build.
- Lower risk of stale docs publication.

### Fix docs index API links to published API landing URL

**Author:** Leela (via Scribe)
**Date:** 2026-04-14
**Status:** Implemented

Use the published API landing URL `https://usepowershell.github.io/PoshMcp/api/PoshMcp.html` for API reference links in `docs/index.md` instead of `api/index.md`.

**Rationale:**
- Local DocFX builds report `InvalidFileLink` for `api/index.md` because there is no source-side `docs/api/index.md`.
- Published API URL keeps the homepage API link functional for readers.
- Scope stays limited to source docs content and avoids generated output or pipeline changes.

**Verification:**
- `docfx build .\\docs\\docfx.json` no longer reports `docs/index.md` invalid link warnings for the previous API link locations.
- Any remaining build warnings are unrelated to this API link change.



# Merge Session Decisions — PRs #92–#95

**Author:** Amy (DevOps/Platform)
**Date:** 2026-04-12
**Status:** Informational

## Summary

Sequential squash-merge of four approved PRs into main. All passed tests before merging.

| PR | Branch | Description | Tests Before | Tests After |
|----|--------|-------------|-------------|-------------|
| #92 | squad/86-use-default-display-properties-flag | `--use-default-display-properties` CLI flag | 343 passed | 343 passed |
| #93 | squad/87-warn-set-auth-enabled-no-schemes | Advisory warning when auth enabled with no schemes | 343 passed | 343 passed |
| #94 | squad/88-unit-tests-update-config-flags | 12 new unit tests for update-config CLI flags | 348 passed | 355 passed |
| #95 | squad/89-unserializable-parameter-types | Skip unserializable param types in MCP schema gen | 381 passed | 388 passed |

## Notable Operational Decisions

### `gh pr merge --delete-branch` exit code in worktrees
The `--delete-branch` flag on `gh pr merge` exits non-zero in a worktree environment because the local branch-delete step fails (`fatal: 'main' is already used by worktree`). The GitHub-side squash merge **succeeds**. This is expected behavior in a git worktree setup — the remote branch is deleted by GitHub; the local worktree ref cleanup fails harmlessly. No action needed; treat exit code 1 as a false failure when the merge confirmation line is present in stdout.

### `dotnet restore` required for cold worktrees
Worktrees that have not been previously built do not have `project.assets.json` present. `dotnet test --no-restore` fails with `NETSDK1004`. Always run `dotnet restore` first when testing a worktree that hasn't been built in the current session.

### Force-push requires explicit remote branch when upstream is not configured
`git push --force-with-lease` fails without an upstream tracking ref. Use `git push --force-with-lease origin <branch-name>` explicitly in worktrees.


# Decision: --use-default-display-properties CLI flag pattern

**Date:** 2026-04-14
**Author:** Amy
**Issue:** #86
**PR:** #92 (https://github.com/usepowershell/PoshMcp/pull/92)

## Decision

Added `--use-default-display-properties <true|false>` to `update-config`, following the exact same pattern as `--enable-result-caching` (PR #85). No new patterns were introduced.

## Rationale

Consistency: every scalar `Performance.*` setting in `PowerShellConfiguration` should be directly settable as a top-level CLI flag without requiring interactive prompts. `UseDefaultDisplayProperties` was the only one missing this treatment.

## Pattern Confirmed

All scalar boolean flags in `update-config` follow this four-step pattern in `Program.cs`:
1. `Option<string?>` declaration near line 180
2. `updateConfigCommand.AddOption(...)` near line 255
3. `GetValueForOption` + `TryParseRequiredBoolean` in handler, passed positionally to `ConfigUpdateRequest`
4. `if (request.X.HasValue)` block in `UpdateConfigurationFileAsync`, using `GetOrCreateObject` for the correct parent object and incrementing `boolUpdateApplied`

## Scope

Single file change: `PoshMcp.Server/Program.cs`, 15 lines added, 0 deleted.


# Decision: Advisory warnings in CLI commands go to stderr

**Date:** 2026-04-14
**Author:** Bender
**Issue:** #87

## Context

When `--set-auth-enabled true` is passed to `update-config` without any `Authentication.Schemes` configured, the server would fail at startup with `AuthenticationConfigurationValidator` but the user received no signal at config-write time.

## Decision

CLI advisory warnings that do not block an operation should be written to `Console.Error` (stderr), **not** stdout. This keeps stdout clean for structured output (e.g., `--output json`) while still surfacing important information to interactive users and CI pipelines that capture stderr separately.

## Pattern

```csharp
Console.Error.WriteLine("WARNING: <message>. Run 'poshmcp validate-config' to verify your configuration.");
```

Always prefix with `WARNING:` for easy grepping/filtering.

## Rationale

- Stdout may be parsed programmatically (`--output json`); mixing warnings there breaks parsers.
- Stderr is the conventional channel for diagnostic/advisory output in CLI tools.
- The write must not be blocked — the advisory is informational only.


# Decision: Cache DiagnoseMissingCommands Results in Doctor Output

**Author:** Bender  
**Date:** 2025-07-15  
**PR:** #96 (Issue #91 — doctor command resolution)

## Decision

`DiagnoseMissingCommands` must be executed at most once per doctor invocation. Its results must be cached and shared between the text output path and the JSON output path in `Program.cs`.

## Context

`RunDoctorAsync` and `BuildDoctorJson` both previously called `DiagnoseMissingCommands` independently. Each call creates an `IsolatedPowerShellRunspace` and runs `Get-Command`/`Import-Module` for every missing command. When `format == "json"`, both calls executed — doubling the cost with no benefit.

## Fix Applied

- `BuildDoctorJson` now accepts `List<ConfiguredFunctionStatus>? precomputedFunctionStatus = null`
- A guard in `BuildDoctorJson` skips `DiagnoseMissingCommands` if `ResolutionReason` is already populated
- `RunDoctorAsync` passes its resolved `configuredFunctionStatus` to `BuildDoctorJson` for the JSON path
- `ConfiguredFunctionStatus` accessibility changed from `private` to `internal` to satisfy C# accessibility rules

## Outcome

- 336 tests pass, 0 failures
- Build succeeds with no new warnings
- PR #96 re-reviewed and ready for Farnsworth's approval


### 2026-04-13T08:50:30Z: User directive
**By:** Steven Murawski (via Copilot)
**What:** Whenever an agent creates a comment, issue, or PR on GitHub, sign it at the end with the agent's name (e.g., — Bender, — Farnsworth).
**Why:** Without signatures, GitHub activity looks like the repo owner talking to themselves. Agent attribution makes conversations legible.


# Decision: Guard against duplicate DiagnoseMissingCommands calls

**Author:** Farnsworth
**Date:** 2026-07-15
**Status:** Required (PR #96 rejection condition)

## Context

PR #96 adds `DiagnoseMissingCommands` for doctor command resolution diagnosis. The method creates an `IsolatedPowerShellRunspace` and runs `Get-Command`/`Import-Module` for each missing command — expensive operations.

## Problem

Both `RunDoctorAsync` and `BuildDoctorJson` independently call `DiagnoseMissingCommands`. When doctor runs in JSON format, introspection executes twice per missing command.

## Decision

`BuildDoctorJson` must guard the call: only invoke `DiagnoseMissingCommands` when `configuredFunctionStatus` entries with `Found=false` have `ResolutionReason is null`. This preserves standalone correctness (tests calling `BuildDoctorJson` directly) while avoiding double work from `RunDoctorAsync`.

## Impact

- PR #96 must be revised before merge
- Assigned to Bender (rejection lockout on Hermes)
- Pattern applies to any future expensive diagnostic that appears in both runtime and builder paths


# PR #84 Action Required — Rebase onto main

**Date:** 2026-07-15
**Author:** Farnsworth
**PR:** [#84 — fix: handle warning stream content during OOP server startup](https://github.com/usepowershell/PoshMcp/pull/84)

---

## Status

GitHub reports `mergeable: false / dirty`. **This is almost certainly a transient compute-lag, not a real conflict.**

`git merge-tree origin/main origin/squad/78-fix-oop-warning-stream` exits 0 with a clean tree — no conflicts.

---

## Files Changed in PR #84

| File | What PR #84 Does |
|---|---|
| `PoshMcp.Server/PowerShell/OutOfProcess/oop-host.ps1` | Adds `-WarningAction SilentlyContinue -WarningVariable` to all `Install-Module` and `Import-Module` calls; forwards captured warnings to `Write-Diag` (stderr) |
| `PoshMcp.Server/PowerShell/OutOfProcess/OutOfProcessCommandExecutor.cs` | Adds `IsNonJsonPowerShellStreamLine()` fast-path helper; skips non-JSON PowerShell stream lines at Debug level; demotes `catch(JsonException)` from LogWarning to LogDebug |
| `.squad/agents/farnsworth/history.md` | Appends PR #83 review note |

---

## Overlap With Already-Merged Work

All three files were also touched by commit `728b108` (#90 "Fixing tests") which landed on main after the PR branch was last synced.

| File | What #90 Changed | Conflict? |
|---|---|---|
| `OutOfProcessCommandExecutor.cs` | Line 62: added `-ExecutionPolicy Bypass` to `ProcessStartInfo.Arguments` | **None** — PR #84 edits lines 424-550 (ReadLoopAsync + helper method) |
| `oop-host.ps1` | Lines ~411+: added global include-pattern discovery block inside `Invoke-DiscoverHandler` | **None** — PR #84 edits lines 223, 247-264, 339-345 (Install/Import-Module params) |
| `history.md` | Appended PR #85 merge note | **None** — PR #84 appends different entry (PR #83 review) |

The PR's `PassThru = $true` (ImportModules success detection, already on main) is correctly reflected in the PR diff context — no duplication issue.

---

## Required Action

1. **Author** (`usepowershell` / Steven Murawski): update the PR branch to include main's latest commits:
   ```bash
   git checkout squad/78-fix-oop-warning-stream
   git merge origin/main   # or: git rebase origin/main
   git push origin squad/78-fix-oop-warning-stream
   ```
2. GitHub will recompute mergeability — it should flip to `true`.
3. **No code changes are needed** — the PR changes are correct, non-overlapping, and CI passes.
4. **Safe to merge immediately after the branch update.**

---

## Review Assessment

The fix is sound and the approach is appropriate for the current scope:
- Primary fix is at the source (oop-host.ps1 suppresses warnings before they hit stdout).
- Defensive C# fix (`IsNonJsonPowerShellStreamLine`) is a cheap fast-path guard against third-party modules that bypass WarningAction.
- Demoting `JsonException` catch from LogWarning to LogDebug eliminates alarm fatigue without hiding real errors.
- CLIXML and in-process-runspace alternatives acknowledged and deferred appropriately (tracked issue open if needed).

**Verdict: Approve and merge after rebase.**


# Decision: Approve and merge PR #85 — extend update-config all settings

**Date:** 2026-04-13
**Decision maker:** Farnsworth (Lead / Architect)
**PR:** https://github.com/usepowershell/PoshMcp/pull/85
**Author:** Amy
**Fixes:** Issue #76

## Approval Decision

**APPROVED and MERGED** (squash merge to `main`).

## Summary of Changes

PR #85 extends the `poshmcp update-config` CLI command to expose all remaining scalar configuration settings as top-level flags:

| Flag | Config Path |
|------|-------------|
| `--runtime-mode <in-process\|out-of-process>` | `PowerShellConfiguration.RuntimeMode` |
| `--enable-result-caching <true\|false>` | `PowerShellConfiguration.Performance.EnableResultCaching` |
| `--enable-configuration-troubleshooting-tool <true\|false>` | `PowerShellConfiguration.EnableConfigurationTroubleshootingTool` |
| `--set-auth-enabled <true\|false>` | `Authentication.Enabled` |

Additionally:
- Interactive per-function prompts extended with `AllowAnonymous`, `RequiredScopes`, `RequiredRoles`
- Interactive prompts now correctly cover `--add-command` entries (was functions-only bug)
- `boolUpdateApplied` counter upgraded bool → int; `SettingsChanged` exposed in text and JSON output

## Notable Patterns

### Correct JSON nesting
`Performance.EnableResultCaching` is nested under `powerShellConfiguration` (correct), while `Authentication.Enabled` is at the config root (correct). The `GetOrCreateObject` helper handles both levels cleanly.

### `NormalizeRuntimeMode` validation
New helper follows the same defensive pattern as `NormalizeFormat` and `TryParseRequiredBoolean` — normalizes casing variants (`in-process`, `inprocess` → `InProcess`) and throws `ArgumentException` for invalid input. Good pattern to continue.

### Complex auth config stays as direct JSON editing
JWT authorities, API keys, CORS — these deeply nested settings are intentionally NOT exposed as CLI flags. Direct JSON editing via `--config-path` is the right call. This is the correct long-term design: CLI flags for scalar toggles, direct JSON for structured config.

### Counter vs bool for settings-changed tracking
Upgrading `boolUpdateApplied` from `bool` to `int` is a strictly better design — it allows `settingsChanged: 3` in JSON output rather than a boolean, which is more informative and composable with future audit/logging.

## Non-blocking Observations (filed as issues)

- **#86** — Add `--use-default-display-properties` global flag for `Performance.UseDefaultDisplayProperties` (consistency)
- **#87** — Warn when `--set-auth-enabled true` used with empty `Authentication.Schemes` (UX improvement, not blocking)
- **#88** — Add unit tests for all 4 new flags in `ProgramCliConfigCommandsTests` (test coverage gap, Fry's queue)


# Decision: update-config flag test patterns (Issue #88)

**Author:** Fry  
**Date:** 2026-04-14  
**PR:** #94

## Summary

Closed the test coverage gap for the four CLI flags and interactive prompt extensions added in PR #85.

## Decisions Made

### 1. Structural assertions over raw file comparison
When asserting that a config file was NOT modified after an error, parse it as JSON and check specific keys rather than comparing raw strings. `UpgradeConfigWithMissingDefaultsAsync` normalizes line endings (`\n` → `\r\n`) as a side effect of config resolution on Windows, making raw string comparison brittle.

### 2. Assert stderr content for error paths
For `--runtime-mode invalid-value`, assert that `capture.StandardError` contains the invalid value string. This is more direct than checking `Environment.ExitCode` vs the `InvokeAsync` return value (which always returns 0 for Task handlers).

### 3. Authentication.Enabled placement assertion
The `--set-auth-enabled` test explicitly asserts both that `Authentication.Enabled` is set at the JSON root AND that `PowerShellConfiguration["Authentication"]` is null. This prevents accidental wrong-level placement by future refactors.

### 4. Existing interactive test extended, not duplicated
Rather than a separate test for AllowAnonymous/RequiredScopes/RequiredRoles, the new test `UpdateConfigCommand_WhenAddingFunction_InteractivePromptsCanSetAllowAnonymousRequiredScopesAndRoles` uses `Get-Service` (different function) with a full stdin sequence. The original `Get-Process` test was updated to supply blank-skip lines for the new prompts to avoid hanging on the extra `Console.ReadLine()` calls.

### 5. settingsChanged = boolUpdateApplied
The `settingsChanged` JSON field increments once per flag that writes a value (`boolUpdateApplied` in `UpdateConfigurationFileAsync`). It does NOT count function add/remove operations — those appear in separate fields (`addedFunctions`, `removedFunctions`).


# Decision: Doctor command resolution diagnostics pattern

**Author:** Hermes  
**Issue:** #91  
**PR:** https://github.com/usepowershell/PoshMcp/pull/96  
**Date:** 2026-07

## Decision

When `poshmcp doctor` reports a configured command as [MISSING], it now runs PowerShell introspection via `IsolatedPowerShellRunspace` and surfaces a human-readable reason explaining why the command was not resolved.

## Rationale

The doctor command exists for troubleshooting. Reporting [MISSING] with no context forces users to manually investigate PSModulePath, module exports, and parameter type issues. The fix surfaces actionable diagnostics directly.

## Pattern established

- Use `IsolatedPowerShellRunspace` (never the singleton) for any diagnostic introspection that runs outside the normal tool execution path
- Share ONE isolated runspace across all diagnostics in a single doctor call
- Use local functions inside `ExecuteThreadSafe` lambdas to avoid needing `System.Management.Automation.PowerShell` type references in Program.cs
- Diagnostic enrichment is additive: the `ConfiguredFunctionStatus` record gets a nullable `ResolutionReason` field, null when found or not diagnosed

## Diagnostic resolution order

1. `Get-Command <name>` in isolated session → found = unserializable param types skipped tool generation
2. Per configured module: `Get-Module -ListAvailable` → missing = not in PSModulePath
3. Per configured module: `Import-Module; Get-Command -Module <module> -Name <name>` → missing = module doesn't export command
4. Command in module → import order / discovery timing issue
5. No modules + not found → command not installed

## Scope

This pattern applies to any future doctor/diagnostic subcommands that need to explain why something is missing. Keep introspection in `IsolatedPowerShellRunspace`, keep it best-effort (catch and report errors), and surface reasons in both text and JSON output.


# Decision: Unserializable Parameter Type Filtering

**Author:** Hermes
**Date:** 2026-07
**Issue:** #89
**Status:** Implemented — PR #95

## Decision

When a PowerShell parameter type cannot be meaningfully represented as a JSON schema value, the MCP tool schema generator should filter it out rather than exposing a broken or misleading parameter entry.

### Rules

| Scenario | Action |
|---|---|
| Optional parameter with unserializable type | Drop from schema silently |
| Mandatory parameter with unserializable type (in a specific parameter set) | Skip that entire parameter set |
| All parameter sets skipped for a command | No MCP tool emitted; warning logged |

### Unserializable Type Criteria

A type is considered unserializable if it belongs to any of these categories:

- **Pointer/by-ref** — `IntPtr`, `UIntPtr`, `T*`, `T&`
- **Opaque PS types** — `PSObject`, `ScriptBlock`
- **Too generic** — `System.Object`
- **Delegate-derived** — `Delegate`, `Action`, `Func<>`, …
- **Binary streams** — `Stream` and any derived type
- **OS sync primitives** — `WaitHandle` and derived
- **Reflection handles** — `System.Reflection.Assembly`
- **PS runtime handles** — `System.Management.Automation.PowerShell`
- **Runspace types** — any type in `System.Management.Automation.Runspaces.*`
- **Arrays** — when the element type is itself unserializable

## Rationale

- JSON has no representation for OS handles, streams, callbacks, or opaque object wrappers.
- Including such parameters in the MCP schema would mislead callers about what values are acceptable.
- Skipping only the affected parameter sets (rather than the whole command) preserves reachability of overloads that use only serializable types.

## Implementation Location

- `PowerShellParameterUtils.IsUnserializableType(Type)` — predicate, can be reused anywhere parameter types are evaluated
- `PowerShellAssemblyGenerator.GenerateMethodForCommand` — filtering applied before IL generation
- `PowerShellAssemblyGenerator.GenerateAssembly` — per-command tracking + warning log when all parameter sets are skipped

### 2026-04-14: DocFX docs branding and Mermaid template baseline (consolidated)
**By:** Leela, Amy
**Status:** Accepted

**What:**
- Set DocFX global metadata `_appLogoPath` to `poshmcp.svg`.
- Ensure `poshmcp.svg` is explicitly included in `build.resource.files` so it is copied to `docs/_site`.
- Enable DocFX Mermaid rendering by using `build.template: ["default", "modern"]`.

**Why:**
- Keeps branding and navbar logo behavior source-driven in `docs/docfx.json` instead of patching generated files.
- Guarantees consistent logo asset availability in generated output for both root and nested docs pages.
- Enables Mermaid diagram rendering without introducing Node.js or `mermaid-cli` dependencies in CI.

**Validation:**
- `docfx docs/docfx.json` completed successfully.
- Generated docs output uses `poshmcp.svg` for navbar branding.

### 2026-04-14: Standardize DocFX navbar logo path to logo.svg
**By:** Steven Murawski (via Leela/Scribe)
**Status:** Implemented

**Decision:**
Use `logo.svg` as the canonical DocFX navbar logo path in source configuration.

**Rationale:**
- Align source configuration with published navbar contract (`<img id="logo" class="svg" src="logo.svg" alt="">`).
- Remove ambiguity between `poshmcp.svg` and `logo.svg` naming.
- Keep fixes targeted to docs source/config rather than generated output edits.

**Impact:**
- `docs/docfx.json` should use `build.globalMetadata._appLogoPath = "logo.svg"`.
- `docs/docfx.json` should include `logo.svg` under `build.resource.files`.
- `docs/logo.svg` is the canonical source asset for navbar branding.

**Verification:**
- `docfx build .\\docs\\docfx.json` succeeds.
- Generated `docs/_site/index.html` contains `<img id="logo" class="svg" src="logo.svg" alt="">`.
- Generated article pages contain `<img id="logo" class="svg" src="../logo.svg" alt="">`.

### 2026-04-14: Resolve DocFX environment link warnings within content boundaries
**By:** Steven Murawski (via Leela/Scribe)
**Status:** Implemented

**Decision:**
When a markdown page is intentionally included as a singleton from a larger folder, links to files outside the DocFX content graph should be converted to either in-scope docs links or stable external repository URLs.

**Rationale:**
- Keeps markdown valid under the current `docs/docfx.json` content graph.
- Minimizes edits while preserving reader intent for cross-references.
- Avoids widening DocFX content boundaries to solve warning-only issues.

**Impact:**
- In `docs/archive/ENVIRONMENT-CUSTOMIZATION.md`, out-of-scope local links should be replaced by in-scope docs links when equivalents exist.
- Repository-root/archive references without in-scope equivalents should use stable GitHub URLs.
- In `docs/articles/environment.md`, relative links should point to `../archive/ENVIRONMENT-CUSTOMIZATION.md`.

**Verification:**
- The six originally reported `InvalidFileLink` warnings are resolved.
- A follow-up pass resolved two remaining warnings.
- Final `docfx build .\\docs\\docfx.json` result is 0 warnings / 0 errors.
- `docs/_site/poshmcp.svg` exists after build.

### 2026-04-14: Route logo.svg through docs/public/ for DocFX build output
**By:** Steven Murawski (via Leela/Scribe)
**Status:** Implemented

**Decision:**
Move the canonical logo source to `docs/public/logo.svg` and route it through DocFX's `build.resource` mechanism so that `logo.svg` is emitted to `docs/_site/public/` during every build.

**Changes:**
- Created `docs/public/logo.svg` (canonical logo source location).
- `docs/docfx.json` `build.resource.files`: added `"public/logo.svg"`.
- `docs/docfx.json` `globalMetadata._appLogoPath`: changed from `"logo.svg"` to `"public/logo.svg"`.
- `docs/logo.svg` retained at root for backward compatibility.

**Rationale:**
- Deployment tooling expects the logo at `public/logo.svg` relative to the site root.
- All other static template assets (JS, CSS) land in `_site/public/` via the modern DocFX template; the logo should follow the same path.
- Template mechanism (`templates/poshmcp/public/logo.svg`) rejected to avoid conflating content asset with template asset.
- Post-build copy script rejected per task constraints.

**Verification:**
- `docfx build` completed with 0 warnings, 0 errors.
- `Test-Path docs/_site/public/logo.svg` returns `True`.

## 2026-04-15

### Authorization override matching for generated tool names
**By:** Steven Murawski (via Copilot/Bender)
**Status:** Implemented

**Decision:**
Resolve per-tool authorization overrides by command-name candidates derived from generated MCP tool names, preferring configured `CommandNames`/`FunctionNames` matches.

**Rationale:**
- Previous lookup behavior checked exact tool names and simple normalization but could miss command-name override keys when generated tool names included parameter-set suffixes.
- Matching generated tool names back to command names keeps per-command `FunctionOverrides` authorization policies effective.

**Impact:**
- Command-level authorization overrides now apply consistently to tools generated from parameter-set-specific method names.
- Existing command-name override configuration remains valid and predictable.

### Align auth docs with real FunctionOverrides matching behavior
**By:** Steven Murawski (via Fry/Copilot)
**Status:** Implemented

**Decision:**
Update docs to reflect actual `FunctionOverrides` resolver order: exact tool-name match first, then normalized command-name candidates.

**Rationale:**
- Prior docs implied generated MCP tool names were not valid override keys, which contradicted runtime behavior.
- Accurate docs reduce operator confusion and align guidance with implementation and tests.

**Impact:**
- Documentation now recommends command-name keys for durable configuration while acknowledging that generated tool-name keys are currently honored.
- Regression coverage includes precedence behavior so docs and implementation remain aligned.

