# Decisions Archive
Entries archived on 2026-04-18 and 2026-05-03.

## Archived Entries (older than 7 days)

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


# Archived 2026-04-20 16:15:42 UTC

# Decisions Log

Canonical record of decisions, actions, and outcomes.


## References


## 2026-07-18

### Issue #131: STDIO logging to file — Architecture decision

Stdio transport must prevent console logging from polluting the JSON-RPC stream. Use Serilog file-backed logging with 3-tier resolution: CLI option > env var > config file. See detailed architecture below.

### 2026-07-18: Architecture decision — Issue #131 STDIO logging

**By:** Farnsworth

**What:**

## Problem

When PoshMcp runs in stdio transport mode, `ConfigureServerLogging` unconditionally calls `builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace)` and `ConfigureOpenTelemetry` unconditionally calls `.AddConsoleExporter()`. Both write to stdout/stderr, which pollutes the stdio pipe that MCP clients use for JSON-RPC communication.

Three affected sites in `Program.cs`:
1. `ConfigureServerLogging` — `AddConsole` (used by both stdio and HTTP paths via `RunMcpServerAsync` and indirectly)
2. `ConfigureOpenTelemetry` (stdio path in `ConfigureServerServices`) — `.AddConsoleExporter()`
3. `CreateLoggerFactory` — `AddConsole` (bootstrap / evaluate-tools path)

## Decision

Use **Serilog** for file logging in stdio mode. It is the industry-standard .NET structured logging library, integrates cleanly with `Microsoft.Extensions.Logging` via `UseSerilog()`, and `Serilog.Sinks.File` is battle-tested. No existing Serilog dependency exists in the project — this is a deliberate new dependency. Alternative (custom file logger) rejected: unnecessary maintenance burden when Serilog solves it idiomatically.

## Configuration

### New `appsettings.json` section

```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft.AspNetCore": "Warning",
    "Microsoft.Hosting.Lifetime": "Information"
  },
  "File": {
    "Path": ""
  }
}
```

`Logging.File.Path` is the appsettings key. Empty string or absent = no file logging.

### New environment variable

`POSHMCP_LOG_FILE` — absolute or relative path to the log file.

### New CLI option

`--log-file <path>` — added to the `serve` subcommand only.

### Resolution order (highest wins)

1. `--log-file` CLI option
2. `POSHMCP_LOG_FILE` environment variable
3. `Logging.File.Path` from appsettings.json
4. No file → silent (NullLogger / suppress all logging in stdio mode)

Add a new constant in `Program.cs`:
```csharp
private const string LogFileEnvVar = "POSHMCP_LOG_FILE";
```

## Implementation — Bender's scope (`Program.cs`, C# changes)

### 1. New CLI option

Add to `serve` command in `Main`:
```csharp
var logFileOption = new Option<string?>(
    aliases: new[] { "--log-file" },
    description: "Path to log file for stdio transport (suppresses console logging)");
serveCommand.AddOption(logFileOption);
```

Pass `logFile` into `ResolveCommandSettingsAsync` and `RunMcpServerAsync` (add parameter).

### 2. Log file resolution helper

```csharp
internal static ResolvedSetting ResolveLogFilePath(string? cliValue)
{
    return ResolveArgumentOrEnvironmentWithSource(cliValue, LogFileEnvVar, null);
    // null default = not configured
}
```

For appsettings resolution, read `Logging:File:Path` from the loaded `IConfiguration` after the config file is resolved. Merge: CLI/env wins over appsettings value.

### 3. New Serilog-backed logging configurator for stdio mode

```csharp
private static void ConfigureStdioLogging(HostApplicationBuilder builder, LogLevel? overrideLogLevel, string? logFilePath)
{
    // Remove default console providers — nothing goes to stdout/stderr in stdio mode
    builder.Logging.ClearProviders();

    if (!string.IsNullOrWhiteSpace(logFilePath))
    {
        var logger = new LoggerConfiguration()
            .MinimumLevel.Is(MapToSerilogLevel(overrideLogLevel ?? LogLevel.Information))
            .WriteTo.File(
                logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        builder.Logging.AddSerilog(logger, dispose: true);
    }
    // else: ClearProviders already installed NullLogger behavior — no output anywhere
}
```

Replace `ConfigureServerLogging(builder, overrideLogLevel)` call in `RunMcpServerAsync` with `ConfigureStdioLogging(builder, overrideLogLevel, resolvedLogFilePath)`.

### 4. Update `CreateLoggerFactory` (bootstrap/evaluate-tools)

Pass `logFilePath` parameter. When in stdio context and log file is configured, use Serilog sink. When no file, return a no-op factory. Keep existing `AddConsole` behavior only for HTTP/evaluate-tools paths that explicitly request it.

### 5. Required NuGet packages

Add to `PoshMcp.csproj`:
```xml
<PackageReference Include="Serilog.Extensions.Hosting" Version="9.0.0" />
<PackageReference Include="Serilog.Extensions.Logging" Version="9.0.0" />
<PackageReference Include="Serilog.Sinks.File" Version="6.0.0" />
```

Check NuGet for latest stable versions compatible with .NET 10 before pinning.

### 6. Tests

- Unit test: `ResolveLogFilePath` resolution priority (CLI > env > null)
- Unit test: `ConfigureStdioLogging` with file path → Serilog file sink registered; without path → `ClearProviders` only
- Integration test: start server in stdio mode with no log file, verify no output on stderr before first MCP message
- Integration test: start server in stdio mode with `--log-file`, verify log file created and messages appear there

## Implementation — Amy's scope (OTel + config schema + docs)

### 1. Suppress OTel console exporter in stdio mode

In `ConfigureOpenTelemetry` (stdio path, called from `ConfigureServerServices`):

```csharp
private static void ConfigureOpenTelemetry(HostApplicationBuilder builder, bool isStdioMode = false)
{
    builder.Services.AddSingleton<McpMetrics>();

    builder.Services.AddOpenTelemetry()
        .WithMetrics(metricsBuilder =>
        {
            metricsBuilder.AddMeter(McpMetrics.MeterName);
            if (!isStdioMode)
            {
                metricsBuilder.AddConsoleExporter();
            }
        });

    // ... rest unchanged
}
```

Pass `isStdioMode: true` from `ConfigureServerServices` when building for stdio transport. `ConfigureOpenTelemetryForHttp` is already HTTP-only and stays unchanged.

### 2. appsettings.json schema

Add `Logging.File.Path` to `appsettings.json`, `default.appsettings.json`, and `appsettings.environment-example.json`:

```json
"Logging": {
  "LogLevel": { ... },
  "File": {
    "Path": ""
  }
}
```

### 3. Documentation updates

**README.md** — Add to the configuration/environment variables section:

| Variable | Description | Default |
|----------|-------------|---------|
| `POSHMCP_LOG_FILE` | Path to log file when running in stdio transport mode. When set, all log output is redirected to this file. When unset in stdio mode, logging is suppressed entirely. | (none) |

Add `--log-file <path>` to the CLI reference for the `serve` subcommand.

Add a note under stdio transport usage: "Logging to console is disabled in stdio mode to prevent interference with the MCP JSON-RPC stream. Use `--log-file` or `POSHMCP_LOG_FILE` to capture logs."

**DOCKER.md** — Add `POSHMCP_LOG_FILE` to the environment variables table. Note that in container deployments, the log file path should point to a volume-mounted directory for persistence (e.g., `/data/poshmcp.log`).

**`appsettings.environment-example.json`** — Update example to include `Logging.File.Path`.

## Default behavior in stdio mode with no log file

**Silent** — `builder.Logging.ClearProviders()` removes all providers. No output to stdout, stderr, or any file. This is correct: the process is intentionally silent to avoid polluting the MCP pipe. If an operator needs diagnostics they must configure a log file path.

Do NOT fail startup or warn to stderr when no log file is configured in stdio mode — that would also pollute the pipe.

## What does NOT change

- HTTP transport logging: `AddConsole` stays, OTel `AddConsoleExporter` stays
- `doctor` command: writes to stdout intentionally (structured output, not logs)
- `list-tools` command: writes to stdout intentionally
- All `Console.Error.WriteLine` calls in pre-startup error handling (before transport is determined) stay — they're correct for CLI error reporting before stdio server starts

**Why:** Issue #131 — STDIO transport must not write logs to stdio. MCP clients (Claude Desktop, VS Code, etc.) communicate with the server exclusively over stdio and any non-JSON-RPC output corrupts the stream.


### 2026-07-18: PR #132 Review — Issue #131
**By:** Farnsworth
**Verdict:** Approved
**What was checked:**
- Logging suppression (ClearProviders first in ConfigureStdioLogging)
- Serilog file sink (rolling daily, 7-day retention, output template)
- Resolution order (CLI > env > appsettings > silent)
- OTel console exporter guarded by isStdioMode
- HTTP path unchanged (AddConsole + AddConsoleExporter still present)
- Null safety (IsNullOrWhiteSpace guards)
- Tests (7 unit + 2+ functional, 10/10 pass, full suite 487/0/1)
- Documentation (README + DOCKER updated with all three config options)
- Build warnings (0 new, 5 pre-existing unrelated)

**Issues found:**
- Non-blocking: `default.appsettings.json` (embedded) missing `Logging.File.Path` — functionally harmless
- Non-blocking: Root handler (bare `poshmcp`) skips `POSHMCP_LOG_FILE` resolution — legacy path, low priority

**Conclusion:** Implementation matches the design spec across all critical areas. ClearProviders unconditionally prevents stdio pollution, Serilog file sink is properly configured, 3-tier resolution works as specified, OTel suppressed in stdio mode, HTTP unaffected. Ship it.


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

### Team intro framing for conference audiences

**Author:** Leela
**Requested by:** Steven Murawski
**Date:** 2026-04-14
**Status:** Implemented

Use a concise role-to-achievement mapping for team introductions, with 1-2 audience-friendly sentences per team member in `docs/articles/talk-team-introductions.md`.

**Rationale:**
- Keeps live delivery short and clear.
- Anchors each intro to verifiable project contributions.

**Impact:** Team-intro content for talk prep is now concise, consistent, and externally legible.

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

## 2026-04-15

### README consistency source and link policy

**Author:** Leela
**Date:** 2026-04-15
**Status:** Proposed

For user-facing guidance in the root `README.md`, treat `docs/articles/*` as canonical. Keep archived materials in `docs/archive/*` explicitly marked as archived, and avoid links to removed root-level `docs/*.md` pages.

**Rationale:**
- Current docs IA centers on `docs/articles/*` for active guides.
- Root README had stale links (`docs/OUT-OF-PROCESS.md`, `docs/ENVIRONMENT-CUSTOMIZATION.md`, `docs/IMPLEMENTATION-GUIDE.md`) that no longer exist.
- Mixed command patterns in README caused drift from current docs (`poshmcp` CLI vs legacy `dotnet run` examples for common workflows).

**Consequences:**
- README remains stable as an onboarding surface while docs evolve.
- Reduced broken-link risk by preferring active docs paths and explicit archive links.
- Fewer support issues caused by outdated command examples.

**Scope:**
- Root `README.md` link and command examples.
- No behavior or code changes.
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

# Decision Proposal: Keep DESIGN.md aligned with implementation boundaries

## Date
2026-04-15

## Proposed By
Farnsworth (Lead/Architect)

## Status
Proposed

## Decision
Adopt a lightweight architecture-doc consistency rule for DESIGN.md:
- Describe AI intent mapping as MCP client responsibility, not server responsibility.
- Describe PoshMcp server responsibilities as tool discovery, schema generation, execution, and transport hosting.
- Keep runtime and transport statements synchronized with implemented modes (`in-process`, `out-of-process`, `stdio`, `http`).
- Use active documentation paths in local links; avoid archived paths unless explicitly labeled archive material.

## Context
The architecture consistency pass found drift in boundary language and at least one stale local link. The implementation and docs now clearly expose dual transport and runtime modes, while intent mapping remains external to the server.

## Rationale
These guardrails preserve architectural clarity for contributors and reviewers, reduce onboarding confusion, and prevent design docs from becoming aspirational in areas that are already concretely implemented.

## Expected Impact
- Fewer architecture misunderstandings in PR reviews.
- Better consistency across DESIGN.md, README, and docs/articles.
- Reduced broken-link churn in design documentation.

## Suggested Follow-up
Add a periodic docs consistency check in release readiness (manual checklist item to start).

# Docker docs consistency guardrails (proposal)

**Author:** Leela (Developer Advocate)  
**Date:** 2026-04-15  
**Status:** Proposed

## Decision
Treat `DOCKER.md` as the canonical root-level container operations guide, and constrain consistency edits to factual, high-confidence alignment with current CLI + container behavior.

## Scope
- Keep `DOCKER.md` CLI-first (`poshmcp build`, `poshmcp run`), while including Docker-native equivalents for parity with docs/articles.
- Use container-accurate paths and entrypoint terminology (`/app/server/poshmcp`, `/app/server/appsettings.json`, `POSHMCP_TRANSPORT`).
- Ensure root-level links only target files that currently exist in-repo.

## Rationale
- Existing docs have mixed generations of guidance (CLI-first and Docker-native); parity examples reduce confusion without broad rewrites.
- Path and entrypoint precision prevents copy/paste failures in derived images and compose usage.
- Small, factual edits lower risk and preserve voice/tone in docs that users already reference.

## Follow-up (non-blocking)
- In a future docs sweep, align `docs/articles/docker.md` examples with the same canonical path and compose environment-variable pattern used in `DOCKER.md`.
# Decision: MimeType default belongs in the handler, not the model

**Date:** 2026-04-15
**Issue:** #129
**Author:** Bender

## Decision

`McpResourceConfiguration.MimeType` is now `string?` with no C# default.
The `"text/plain"` fallback is applied at runtime inside `McpResourceHandler`
(both `HandleListAsync` and `HandleReadAsync`) using `IsNullOrWhiteSpace` coalescing.

## Rationale

A model-level default of `"text/plain"` silenced the validator's null/whitespace check,
so operators who omitted MimeType from config received no warning — violating FR-027.
Moving the default to the handler preserves the runtime contract (FR-030) while
restoring the diagnostic signal.

## Impact

- `McpResourceConfiguration.MimeType` is nullable; callers must handle null.
- `McpResourceHandler` already used null-coalescing — no logic change needed there.
- Test stub `McpResourceDefinition` updated to `string?` to stay in sync.
- Binding tests updated: assert `null` from config binding, not `"text/plain"`.

# Decision: MimeType test was failing, not skipped

**Date:** 2026-04-18
**Author:** Fry
**Issue:** #129

## Finding

`Validate_ResourceWithNoMimeType_ReportsMimeTypeWarning` never had a `[Skip]` attribute. It was simply *failing* because `McpResourceConfiguration.MimeType` had a hardcoded `"text/plain"` default at the model level, preventing `IsNullOrWhiteSpace` from ever being true.

## Resolution

Once Bender made `MimeType` a nullable `string?` with no default (commit `78de3c7`), the validator's existing `IsNullOrWhiteSpace` guard fired correctly and the test passed without any change to test logic.

Fry updated only the inline comment to reference nullable behavior and committed `1419a20`.

## Implication for future

When a test appears to need "unskipping", check first whether it was actually skipped vs failing. A failing `[Fact]` with no Skip attribute just needs the underlying code fixed — no test-attribute surgery needed.



# Decision: Dockerfile COPY hygiene when switching from solution to project-level builds

**Date:** 2026-07-18
**PR:** #138 (fix for issue #136)
**Author:** Amy (DevOps / Platform)

## Decision

When a Dockerfile's build stage is changed from solution-level (`PoshMcp.sln`) to project-level (`PoshMcp.Server/PoshMcp.csproj`) restore and build commands, any `COPY` lines for files that are no longer referenced by any `RUN` command must be removed.

## Rationale

- `COPY PoshMcp.sln ./` was added to support `dotnet restore PoshMcp.sln`, which is no longer the restore target.
- Orphaned `COPY` lines add an unnecessary cache layer without contributing to the build.
- The subsequent `COPY . .` already brings in `PoshMcp.sln` if it were ever needed; the explicit early copy served only to seed the layer cache for restore.
- Keeping dead `COPY` lines is misleading: future maintainers may assume the file is consumed by some RUN step.

## Rule

> In a multi-stage Dockerfile build stage, every explicitly `COPY`-ed file before `COPY . .` must be directly consumed by a subsequent `RUN` command in that stage. Remove any that are not.


### 2026-04-10T00:00:00Z: Configuration troubleshooting tool follows Program.cs special-tool registration
**By:** Bender (via Copilot)
**What:** Register the doctor-style troubleshooting MCP tool through the same Program.cs special-tool path as reload/caching tools, and apply the feature gate via config with an environment-variable override during configuration load.
**Why:** The existing doctor path already lives outside PowerShell command discovery, so mirroring that registration path keeps the change minimal and makes doctor JSON reflect the real runtime tool surface.

# Decision: Redact sensitive config values in doctor output

**Date:** 2026-07-28
**PR:** #139
**Author:** Bender

## Context

`poshmcp doctor` exposes `IConfiguration.GetSection("Authentication")` and `GetSection("Logging")` values in both JSON (`--format json`) and text output. These sections can contain secrets: API keys, client secrets, passwords, connection strings with credentials, etc. Surfacing raw config values in diagnostic output creates a leak vector — logs, CI output, clipboard paste, etc.

## Decision

Apply a **key-pattern redaction pass** to any flat config dictionary before it reaches any output path (text or JSON). Values whose keys match any of the following patterns (case-insensitive substring match) are replaced with `[REDACTED]`:

```
password, secret, key, token, connectionstring, credential, pwd, apikey, clientsecret
```

## Implementation

Three private helpers in `Program.cs`:

```csharp
private static readonly string[] _sensitiveKeyPatterns =
    ["password", "secret", "key", "token", "connectionstring", "credential", "pwd", "apikey", "clientsecret"];

private static bool IsSensitiveKey(string key) =>
    _sensitiveKeyPatterns.Any(pattern => key.Contains(pattern, StringComparison.OrdinalIgnoreCase));

private static Dictionary<string, string?> RedactSensitiveConfigValues(Dictionary<string, string?> config) =>
    config.ToDictionary(kvp => kvp.Key, kvp => IsSensitiveKey(kvp.Key) ? "[REDACTED]" : kvp.Value);
```

`RedactSensitiveConfigValues` is called immediately after `LoadFlatConfigSection` for auth and logging config — before the values are passed to either the text output loop or `BuildDoctorJson`.

## Rationale

- **Substring match** is intentional and safe: keys like `ClientSecret`, `ApiKey`, `ConnectionString`, `PrivateKey`, `Password`, `JwtSecret` all match. False positives (e.g., a key literally named `key`) are acceptable — we prefer over-redaction to under-redaction for diagnostic output.
- **Apply at load time**, not at serialization time: this way both output paths (text loops and JSON serialization) see the same redacted dict, and there is no risk of forgetting to redact in a future output path.
- **Both sections**: Auth config is the obvious vector, but logging config can include connection strings for log sinks (e.g., Serilog.Sinks.MSSqlServer), so it gets the same treatment.

## Alternatives Considered

- **Allowlist approach** (only expose known-safe keys): too fragile; new config keys would be silently exposed.
- **Strip the section entirely**: loses useful diagnostic information (e.g., `Authentication:Enabled`, `Logging:LogLevel:Default`).
- **Apply redaction only at JSON serialization**: leaves text output unprotected and requires two separate redaction call sites.

## Trade-offs

- The `key` pattern is broad (substring match) and will redact keys like `LogLevel` if anyone adds a nested key that contains `key`. In practice, standard `Logging` and `Authentication` config shapes don't hit this; the trade-off favors security.


### 2025-11-26: Recovery fix for out-of-process merge fallout
**By:** Bender
**What:** Restored a shared `Program.BuildDoctorJson(...)` helper so CLI doctor output and MCP troubleshooting tools use the same JSON payload builder, and extended the shared `InProcessMcpServer` test harness to support explicit config arguments and stderr capture expected by out-of-process integration tests.
**Why:** The merge left the server and integration harness in mismatched states: the runtime troubleshooting tool still depended on a removed helper, and the new out-of-process tests depended on harness features that were no longer present. Centralizing the doctor JSON path again and updating the shared harness was the minimal root-cause fix.

### 2026-04-10T10:52:05Z: Doctor MCP tool contract and gating
**By:** Steven Murawski (via Copilot/Farnsworth)
**What:** Reject the current branch for doctor-as-tool because no MCP tool implementation exists yet. If implemented, expose a read-only troubleshooting tool that returns the existing doctor report in structured JSON, and gate it behind both a configuration flag and an explicit environment variable so it is disabled by default.
**Why:** Existing precedent in this repo is that built-in operational tools are feature-gated in `PowerShellConfiguration`, but a troubleshooting/doctor surface is more operationally sensitive than normal tool discovery. Requiring both config and environment opt-in prevents accidental exposure from config drift alone.

**Accepted shape for follow-up implementation:**
- Add `PowerShellConfiguration.EnableDoctorTool` with default `false`.
- Add a dedicated environment variable gate, preferably `POSHMCP_ENABLE_DOCTOR_TOOL=true`.
- Register the tool only when both gates are true.
- Keep the MCP contract read-only and idempotent, returning machine-readable JSON equivalent to `doctor --format json`.
- Do not expose config mutation through this tool.
- Add tests that prove: default disabled, config-only disabled, env-only disabled, both enabled, and tool name appears/disappears in server discovery accordingly.

# Out-of-Process PowerShell Runtime — Architectural Research Brief

**Researcher:** Farnsworth (Lead / Architect)  
**Date:** 2026-04-10  
**Status:** Research Complete  
**Cross-Platform:** ✅ All patterns evaluated for Windows/macOS/Linux compatibility  
**Constraint Mode:** No mixed mode — if started with out-of-process option, entire runtime is out-of-process

---

## 1. Current Architecture Analysis

### In-Process Model (Status Quo)

**How it works today:**

- **Singleton runspace:** `PowerShellRunspaceHolder` creates a single `PSPowerShell` instance per server process using `Lazy<T>` singleton pattern.
- **Thread-safe access:** `SemaphoreSlim` (count=1) + lock guard all runspace operations. `ExecuteThreadSafeAsync` acquires the semaphore before executing any command.
- **Module loading:** `McpToolFactoryV2.GetToolsList()` discovers PowerShell commands. Module imports happen inline during tool discovery via `PowerShell.AddCommand("Import-Module").Invoke()`. Modules are imported into the singleton runspace and persist across all subsequent tool invocations.
- **Command execution:** Dynamically generated IL code (from `PowerShellAssemblyGenerator`) creates tool methods. Each tool method calls `ExecutePowerShellCommandTyped()`, which:
  - Awaits the runspace semaphore
  - Adds command to the singleton runspace's pipeline
  - Invokes `ps.Invoke()`
  - Serializes results to JSON
  - Returns JSON string

**Result:** All modules share one process-wide PowerShell state. State is persistent across tool calls.

### Module Loading Conflicts (The Problem)

**What breaks:**

From repo memory and codebase inspection:
- Some modules depend on exclusive state (e.g., auth tokens, session objects).
- Other modules have initialization side-effects that conflict with each other.
- Example: `Az.Accounts` + `Az.Storage` may have incompatible runspace configurations if loaded in certain orders.
- Current model forces all modules into one runspace — no isolation.
- When a module fails to load in the singleton, the server's tool inventory is permanently degraded.

**Key insight from `.squad/decisions.md` (module-discovery-import-order):**
- Modules **must** be imported before any `Get-Command` or discovery query.
- `PowerShellEnvironmentSetup` exists but is not currently wired into startup.
- No regression tests exist for `PSModuleAutoLoadingPreference='None'`.

### Runspace Model (Current)

- **Singleton per server process:** One runspace, one state, one module inventory.
- **No isolation:** All tool executions share the same memory, variables, functions, and module state.
- **Session-aware variant exists:** `SessionAwarePowerShellRunspace` exists to create per-session runspaces for HTTP/web contexts, but test coverage is incomplete; it is not used for stdio MCP servers.
- **Synchronization:** Coarse-grained semaphore at the runspace holder level. Blocks all tool calls while one runs.

**Performance impact:** Large cmdlets like `Get-Process` hold the semaphore for seconds, blocking all other tool calls.

---

## 2. Cross-Platform Out-of-Process Hosting Patterns

### Pattern A: TCP Localhost with Persistent Subprocess

**Concept:** Start a separate PowerShell process (`pwsh.exe`, `pwsh` on Linux/macOS) as a subprocess. Main .NET server communicates with subprocess via localhost TCP on an ephemeral port. Subprocess maintains a persistent runspace and reads JSON commands from stdin, writes JSON results to stdout.

**Platforms:** 
- Windows ✅ (pwsh.exe or powershell.exe)
- Linux ✅ (pwsh binary from PowerShell Core)
- macOS ✅ (pwsh from Homebrew or direct install)

**Pros:**
- Works uniformly across all platforms — no conditional code.
- Proven pattern (VS Code debuggers, language servers use similar models).
- Subprocess runspace is completely isolated from main process.
- Easy to test (localhost guaranteed available, ports auto-assigned by OS).
- Subprocess can be killed and restarted without affecting main server.
- Module loading conflicts isolated to subprocess — doesn't crash main server.
- Simple wire protocol (JSON stdin/stdout).

**Cons:**
- Startup latency (~200–500ms for pwsh process creation).
- Memory overhead (+80–120 MB per subprocess for a pwsh instance).
- TCP port allocation/cleanup (minor: OS releases ports quickly, ephemeral ports are abundant).
- Firewall edge cases (localhost should always work; non-localhost bindings require care).
- Slightly higher IPC latency than Unix sockets (but acceptable, <1ms).

**Implementation Sketch:**

1. **Main process (.NET):**
   - Spawns subprocess: `pwsh -NoProfile -Command { Read-Host ... | ... | Write-Output ... }`
   - Subprocess binds to localhost:0 (ephemeral port), reports port number on startup.
   - Main process read port from subprocess stdout, establishes TCP client.
   - Main process creates JSON request: `{ "command": "Get-Process", "args": { "Name": "powershell" } }`
   - Writes JSON + newline to subprocess TCP socket.
   - Reads JSON response from subprocess.
   - Synchronous sends/receives (or async with buffering).

2. **Subprocess (PowerShell):**
   - Initialize: import modules, set up state.
   - Enter loop: read JSON from stdin, parse command + args, execute, serialize results to JSON, write to stdout.
   - Persistent runspace across multiple commands.

**Trade-offs vs. in-process:**

| Aspect | In-Process | Pattern A |
|--------|-----------|----------|
| Module isolation | ❌ None — conflicts crash or degrade | ✅ Yes — processes independent |
| Startup latency | ~100ms | ~300ms (pwsh overhead) |
| Per-call latency | <5ms (direct invoke) | ~10–20ms (TCP + JSON roundtrip) |
| Memory per server instance | ~150 MB | ~150 + 80–120 (subprocess) = ~230–270 MB |
| Testability | Moderate (shared state) | High (isolate subprocess) |
| Cross-platform complexity | Low (native .NET) | Low (TCP everywhere) |
| Error isolation | ❌ Subprocess crash kills server | ✅ Subprocess crash doesn't kill main server |
| State persistence across calls | Yes (shared runspace) | Yes (persistent subprocess runspace) |
| Dynamic module loading | Single inventory | Multiple independent inventories (one per subprocess) |

---

### Pattern B: Socket + TCP Hybrid (Unix domain sockets on *nix, TCP on Windows)

**Concept:** Native Unix domain sockets on Linux/macOS (better performance), TCP on Windows. Unified abstraction layer hides platform differences.

**Platforms:**
- Windows ✅ (TCP fallback)
- Linux ✅ (Unix domain socket, `AF_UNIX`)
- macOS ✅ (Unix domain socket, `AF_UNIX`)

**Pros:**
- Best performance on Unix platforms: Unix sockets have zero kernel-space overhead vs. TCP (no loopback stack).
- Better security posture on Unix (file permissions on socket file).
- Native OS patterns (Linux/macOS developers expect Unix sockets).

**Cons:**
- Platform-specific code paths (need conditional imports, different socket creation logic).
- Test coverage must run on all three platforms (more complex CI/CD).
- Additional abstraction layer (socket factory, platform detection).
- Total implementation complexity ≈ 1.5x vs. Pattern A.
- Port cleanup on Windows identical to Pattern A.
- Socket file cleanup on Unix (if subprocess crashes, socket file may remain; cleanup logic required).

**Implementation Sketch:**

```
IPC Transport (abstraction)
  ├─ Windows: TcpTransport (localhost:ephemeral)
  └─ Unix: UnixDomainSocketTransport (/tmp/poshmcp-GUID.sock)
```

Subprocess reports its listening endpoint (port or socket path) on startup. Main process establishes connection using platform-appropriate transport.

---

### Pattern C: Remote PowerShell (Cluster/Multi-Machine)

**Concept:** Use PowerShell Remoting (`Enter-PSSession`, `Invoke-Command` over WinRM/SSH). Connects to localhost PowerShell service or remote machine.

**Platforms:**
- Windows ✅ (WinRM)
- Linux ⚠️ (SSH remoting, requires OpenSSH + PowerShell Core)
- macOS ⚠️ (SSH remoting, requires OpenSSH + PowerShell Core)

**Pros:**
- Native PowerShell feature (existing tooling, familiar patterns).
- Credential/auth model well-understood.
- Can target remote machines (future feature: distributed execution).

**Cons:**
- WinRM adds daemon/service management complexity on Windows (must run WinRM service).
- SSH setup on Unix is non-trivial (requires OpenSSH server, firewall rules).
- Authentication overhead for localhost connections (overkill).
- Significantly slower than TCP or socket IPC (handles auth, encryption, marshalling).
- **Not recommended for MVP:** Too much operational overhead for localhost-only use case.

**Verdict:** Defer to Phase 2 if remote execution is needed.

---

### Pattern D: Named Pipes (Windows-only)

**Concept:** Use Windows named pipes (`\\.\pipe\PoshMcp-{guid}`) for IPC.

**Platforms:**
- Windows ✅ (native support)
- Linux ❌ (not available)
- macOS ❌ (not available)

**Verdict:** **Disqualified.** Cross-platform constraint requires all patterns to work on Windows/Linux/macOS. Named pipes are Windows-only.

---

## 3. Trade-off Matrix (Cross-Platform Focus)

| Factor | Pattern A (TCP) | Pattern B (Socket+TCP) | Pattern C (Remoting) |
|--------|---------|--------|---------|
| **Startup latency** | ~300ms | ~300ms | ~1–2s |
| **Per-call latency** | ~10–20ms | ~5–10ms (socket) / ~15–20ms (TCP) | ~100–200ms |
| **Memory footprint per instance** | +80–120 MB | +80–120 MB | +50–80 MB (reuses service) |
| **Cross-platform (W/L/M)** | ✅ Uniform | ✅ Best performance varies | ⚠️ Operationally heavy |
| **Windows startup latency** | ~300ms | ~300ms | ~1–2s |
| **Linux startup latency** | ~300ms | ~300ms | ~800ms |
| **macOS startup latency** | ~300ms | ~300ms | ~800ms |
| **Implementation complexity** | Low (TCP everywhere) | Medium (platform detection) | High (credential handling) |
| **Test coverage burden** | Low (same code path everywhere) | Medium (platform-specific tests) | High (auth mocking, service config) |
| **Module isolation** | ✅ Yes | ✅ Yes | ✅ Yes |
| **Error resilience** | ✅ Subprocess crash independent | ✅ Subprocess crash independent | ⚠️ Service crash affects all clients |
| **Dynamic reloading** | Per-subprocess | Per-subprocess | Service-wide |
| **State isolation per-user** | No (one subprocess per config) | No (one subprocess per config) | Yes (per-session isolation) |
| **Operational simplicity** | Low (just spawn process) | Low (just connect to socket/port) | Medium (manage service) |

**Recommendation:** **Pattern A (TCP Localhost)** strikes the best balance for MVP:
- Works uniformly on all platforms.
- Simplest implementation.
- Acceptable latency (10–20ms per call is negligible for AI workloads).
- Easiest to test (no platform-specific logic).
- Defers Pattern B optimization to Phase 2 if needed.

---

## 4. Integration Points & Ripple Analysis

### High-Impact Changes

**1. Runtime Mode Selection (Program.cs)**
- Add new CLI flag: `--runtime-mode [in-process|out-of-process]`
- Or environment variable: `POSHMCP_RUNTIME_MODE=[in-process|out-of-process]`
- **Impact:** Startup logic diverges:
  - **In-process:** Use `SingletonPowerShellRunspace` (status quo).
  - **Out-of-process:** Start subprocess, establish TCP connection, inject `OutOfProcessPowerShellRunspace` service.
- No mixed mode: all operations use selected mode for entire server lifetime.

**2. IPowerShellRunspace Abstraction**
- **Current:** Two implementations: `SingletonPowerShellRunspace` (production) and `IsolatedPowerShellRunspace` (test).
- **Add:** `OutOfProcessPowerShellRunspace` implementation:
  - Opens TCP connection to subprocess.
  - `ExecuteThreadSafeAsync(Func<PSPowerShell, Task<T>> operation)` serializes the operation to JSON, sends to subprocess, waits for response.
  - Returns `PSPowerShell`-compatible interface (but subprocess-backed).

**Problem:** `IPowerShellRunspace.ExecuteThreadSafeAsync` expects a `Func<PSPowerShell, Task<T>>` — a delegate that runs in-process. Out-of-process can't execute a .NET delegate in a PowerShell process.

**Solution:** Refactor the abstraction:
- Split `IPowerShellRunspace` into two:
  - `ILocalPowerShellRunspace` (current): For in-process execution with `PSPowerShell` instance.
  - `IPowerShellExecutor` (new): For any execution mode (in-process or out-of-process):
    ```csharp
    interface IPowerShellExecutor
    {
        Task<string> ExecuteCommandAsync(
            string commandName, 
            PowerShellParameterInfo[] parameters, 
            object[] values, 
            CancellationToken ct);
    }
    ```
- `LocalPowerShellExecutor` (wraps `ILocalPowerShellRunspace`, current behavior).
- `RemotePowerShellExecutor` (uses TCP to off-process subprocess).

**3. McpToolFactoryV2 Execution Path**
- **Current:** Generated IL code calls `ExecutePowerShellCommandTyped()`, which acquires runspace, executes.
- **New:** Generated IL code calls `IPowerShellExecutor.ExecuteCommandAsync()`:
  - If in-process: delegates to `ExecutePowerShellCommandTyped()` (no change).
  - If out-of-process: sends JSON over TCP, waits for response.
- **Impact:** Tool methods remain identical; execution path branches at the executor level.

**4. Subprocess Lifecycle Management**
- **New component:** `OutOfProcessPowerShellHost`:
  - Spawns subprocess: `pwsh -NoProfile -Command [initialization script]`
  - Waits for subprocess to report listening port/socket.
  - Stores subprocess handle for cleanup on shutdown.
  - On server shutdown: `process.Kill()` + `process.WaitForExit()`.
- **Health:** Should subprocess crash, how does main server know?
  - Wrapper class monitors subprocess: `IsAlive` property, restarts on crash (Phase 2).
  - MVP: Just detect crash on next command, return error to caller.

**5. Configuration**
- New `PowerShellConfiguration` section:
  ```json
  {
    "PowerShell": {
      "RuntimeMode": "in-process",  // or "out-of-process"
      "OutOfProcessOptions": {
        "ConnectionType": "tcp",     // or "socket" (Phase 2)
        "InitializationScript": "path/to/init.ps1",
        "RestartPolicy": "none"      // or "auto-restart" (Phase 2)
      }
    }
  }
  ```

### Medium-Impact Changes

**1. Tool Discovery**
- **Artifact:** How does tool discovery work when modules are in subprocess?
- **Current:** `McpToolFactoryV2.GetToolsList()` imports modules, discovers commands, generates assembly.
- **Out-of-process:** Module discovery must run in subprocess, results serialized back to main process.
- **Implementation:** New MCP tool in subprocess: `$tools = Get-AvailableCommands` (internal command).
  - Called once at startup, caches result.
  - Main process caches discovered tools locally (same as current behavior).
- **Impact:** Tool discovery adds 1–2s to startup (subprocess module import time).

**2. Result Serialization**
- **Artifact:** PowerShell objects serialized to JSON differ between in-process and out-of-process.
- **Current:** `ExecutePowerShellCommandTyped()` uses `PowerShellObjectSerializer` to normalize results.
- **Out-of-process:** Subprocess serializes results, sends JSON. Main process receives JSON (double-serialized).
- **Risk:** If subprocess uses different serialization logic, results differ.
- **Mitigation:** Subprocess and main process must use same `PowerShellObjectSerializer` logic. Encode serializer version as part of protocol.
- **Impact:** Low if serializer remains consistent.

**3. Error Handling**
- **Current:** Errors during command execution caught in `ExecutePowerShellCommandTyped()`, wrapped in MCP error response.
- **Out-of-process:** Errors in subprocess caught by subprocess serialization, included in JSON response. Main process unwraps error.
- **Wire format:** JSON response includes:
  ```json
  {
    "success": true/false,
    "result": "...",
    "error": { "code": "...", "message": "..." }
  }
  ```
- **Impact:** Medium — requires new response schema.

### Low-Impact Changes

**1. Logging & Observability**
- **Artifact:** How do we correlate subprocess logs with main server logs?
- **Current:** `OperationContext.CorrelationId` passed through logging scopes.
- **Out-of-process:** Include correlation ID in JSON request to subprocess. Subprocess includes it in its logs.
- **Subprocess log destination:** Same as main server (shared log file or centralized logging).
- **Impact:** Low if logging infrastructure is already centralized (OpenTelemetry already in place).

**2. Health Checks**
- **Current:** `PowerShellRunspaceHealthCheck` pings singleton runspace.
- **Out-of-process:** Special health tool in subprocess: `Test-McpHealth` returns `{ "healthy": true }`.
- **Main server health:** Reports out-of-process subprocess health as part of `/health` endpoint.
- **Impact:** Low — health check becomes TCP call instead of direct invoke.

**3. Configuration Reload**
- **Current:** `PowerShellConfigurationReloadService` reloads function list, clears tool cache.
- **Out-of-process:** Reload service sends signal to subprocess to reload its module list (new internal tool: `Reload-McpConfiguration`).
- **Impact:** Low if reload protocol is simple (command + response).

---

## 5. Recommended Approach: Pattern A (TCP Localhost)

### Rationale

Pattern A (TCP Localhost) is the recommended primary pattern for MVP because:

1. **Uniform cross-platform behavior:** Identical code path on Windows/Linux/macOS. No conditional imports or feature detection.
2. **Simplest implementation:** TCP is fully supported in .NET, no platform-specific APIs needed. Standard socket library everywhere.
3. **Testability:** Same protocol on all platforms = same test suite. No platform-specific test logic.
4. **Operational simplicity:** Spawn subprocess, read port, connect. No daemon management, no service config.
5. **Acceptable performance:** 10–20ms per-call latency is negligible for AI assistant workloads (human perception is ~100ms).
6. **Phase 2 optimization:** Pattern B (socket + TCP hybrid) can be added after MVP; abstraction supports both.
7. **Error resilience:** Subprocess crash doesn't crash main server; can restart independently.

### Key Load-Bearing Decisions

**Decision 1: Mode Selection is Early + Permanent**
- CLI flag or environment variable at startup.
- Once selected, all tool execution uses that mode.
- No switching during runtime (no mixed mode).
- Rationale: Simplifies resource management, prevents mode-switching bugs.

**Decision 2: Subprocess is a Long-Lived Singleton**
- One subprocess per server instance (not per-request).
- Persistent runspace maintains state across tool calls.
- Rationale: Reduces startup overhead, allows stateful module interactions (auth tokens, session objects).

**Decision 3: Module Loading is Out-of-Process Responsibility**
- Configured per-server in `appsettings.json` (same as current).
- Subprocess imports modules at startup.
- Main process doesn't need to know module details.
- Rationale: Isolation — module conflicts don't affect main process.

**Decision 4: Command Execution is Serialized (JSON over TCP)**
- No marshalling of .NET objects or PowerShell objects.
- All parameters serialized as JSON strings/primitives.
- All results serialized as JSON.
- Rationale: Simplifies protocol, prevents serialization mismatches.

**Decision 5: Abstraction: IPowerShellExecutor (Not IPowerShellRunspace)**
- Split from current `IPowerShellRunspace` to avoid exposing `PSPowerShell` in out-of-process mode.
- `IPowerShellExecutor` is mode-agnostic (in-process or out-of-process).
- Current in-process code path wrapped by `LocalPowerShellExecutor`.
- Out-of-process code path implemented by `RemotePowerShellExecutor`.
- Rationale: Clear separation of concerns, no forced abstraction leaks.

---

## 6. MVP Scope & Phasing (Cross-Platform)

### MVP (Phase 1): In-Process + Out-of-Process TCP (Windows + Linux)

**Scope:** Implement Pattern A (TCP localhost) for both platforms. Enable **selective per-server deployment**: operators choose in-process or out-of-process at startup.

**Goals:**
- Solve module loading conflicts by isolating problematic modules to subprocess.
- Maintain compatibility with existing in-process mode.
- Prove TCP protocol works across Windows and Linux (CI/CD).

**What ships:**
1. **New abstraction layer:** `IPowerShellExecutor` (replaces delegation path in `McpToolFactoryV2`).
2. **Implementation:** `LocalPowerShellExecutor` (wraps current logic) + `RemotePowerShellExecutor` (TCP).
3. **Subprocess:** PowerShell script (`pwsh-mcp-host.ps1`):
   - Initializes runspace.
   - Binds to localhost:0, reads port from OS.
   - Enters command loop: read JSON, execute, respond.
   - Persistent runspace.
4. **Subprocess launcher:** `OutOfProcessPowerShellHost` (.NET):
   - Spawns subprocess.
   - Waits for port number.
   - Manages lifecycle.
5. **CLI flag:** `--runtime-mode [in-process|out-of-process]`.
6. **Configuration:** `PowerShellConfiguration.RuntimeMode` in `appsettings.json`.
7. **Tests:**
   - Functional tests for TCP protocol (JSON serialization, error handling).
   - Integration tests for both modes (in-process + out-of-process).
   - Cross-platform CI: Windows + Linux containers.
8. **Documentation:** Deployment guide for selecting mode, module configuration per mode.

**Effort estimate:** 8–12 engineering days (one developer, with testing).

**Success criteria:**
- AI assistant can invoke tools in both modes.
- Results identical (or equivalent after normalization).
- Subprocess crash doesn't crash main server.
- TCP implementation passes cross-platform tests.

**macOS support:** Identical to Linux (pwsh binary available); included in MVP.

### Phase 2: Subprocess Lifecycle Management + Restart Policy

**Scope:** Handle subprocess crashes gracefully. Optional auto-restart.

**What ships:**
- Enhanced `OutOfProcessPowerShellHost`: Monitors subprocess health, auto-restarts on crash (configurable).
- Health endpoint includes subprocess status.
- Logging of subprocess crashes with recovery attempts.

**Effort estimate:** 3–4 days.

### Phase 3: Socket + TCP Hybrid (Performance Optimization)

**Scope:** Add Pattern B (Unix domain sockets on Linux/macOS) for performance-sensitive deployments.

**What ships:**
- Platform-agnostic `IPcTransport` abstraction.
- `TcpTransport` (Windows + fallback).
- `UnixSocketTransport` (Linux/macOS).
- `OutOfProcessPowerShellHost` uses abstraction; subprocess reports endpoint type.
- Benchmarks: socket vs. TCP latency.

**Effort estimate:** 5–7 days.

**Decision point:** Proceed to Phase 3 only if MVP telemetry shows TCP latency is user-visible issue (unlikely).

### Phase 4: Per-User Session Isolation (HTTP/Web Mode)

**Scope:** Use `SessionAwarePowerShellRunspace` for HTTP mode with out-of-process.

**What ships:**
- Subprocess pool: one subprocess per user session (or shared pool with session-scoped state).
- Session ID passed in JSON requests.
- Subprocess maintains per-session module imports.

**Effort estimate:** 8–12 days.

**Prerequisite:** Completion of Phase 1.

### Phase 5: Distributed Execution (Future)

**Scope:** Deploy subprocess on different machines, communicate via SSH or HTTP.

**What ships:** TBD (research phase).

---

## 7. Open Questions & Next Steps

### Questions Requiring Implementation Phase Assessment

**Q1: Subprocess restart strategy for MVP**
- Auto-restart on crash, or fail loudly?
- **Recommendation:** Fail loudly in MVP. Operator restarts server. Phase 2 adds auto-restart.

**Q2: Result caching with out-of-process**
- Feature `set-result-caching` (`.squad/decisions.md`) caches results in main process. Does this work for out-of-process?
- **Answer:** Yes — cache lives in main process, even if execution is out-of-process. No change to caching logic.

**Q3: Module versioning**
- What if main process and subprocess use different module versions?
- **Recommendation:** Both use same `appsettings.json` module list. Version mismatch detected at setup time (error if subprocess import fails).

**Q4: Debugging out-of-process failures**
- How do developers debug subprocess crashes?
- **Recommendation:** Log subprocess stderr/stdout to main process logs. Capture stack traces from PowerShell errors. Phase 2: structured logging query tool.

**Q5: Port allocation race conditions**
- What if ephemeral port is reused between server instances?
- **Answer:** OS handles this; ports released immediately. No race condition.

**Q6: IPv6 vs. IPv4**
- Should we explicitly bind to `127.0.0.1` (IPv4) or support IPv6?
- **Recommendation:** MVP: Explicit `127.0.0.1` (IPv4). All platforms support. IPv6 deferred to Phase 2 if needed.

---

## 8. Implementation Roadmap (Next Steps for Architect)

### Before Implementation Starts

1. **Approve this brief** — confirm Pattern A recommendation and MVP scope.
2. **Define subprocess wire protocol** — JSON schema for requests/responses (request for tech spike).
3. **Identify first problematic module pair** — use this as MVP validation case.
4. **Estimate CI/CD changes** — ensure cross-platform test infrastructure can launch out-of-process servers.

### Assign Work

1. **Core abstraction layer** (IPowerShellExecutor, LocalPowerShellExecutor):
  - Assign to Backend Developer.
  - 2–3 days.
  - Unblocks remaining work.

2. **RemotePowerShellExecutor + TCP transport:**
  - Assign to Backend Developer.
  - 4–5 days.

3. **Subprocess + PowerShell host script:**
  - Assign to Backend Developer or DevOps.
  - 3–4 days.

4. **Functional + integration tests:**
  - Assign to QA / Backend Developer.
  - 2–3 days cross-platform validation.

5. **Configuration + CLI integration:**
  - Assign to Backend Developer.
  - 1–2 days.

### Validation Gate

Before Phase 2:
- [ ] Subprocess can load conflicting modules (e.g., two versions of same module) without main server impact.
- [ ] Tool results identical (or provably equivalent) in both modes.
- [ ] CI/CD green for Windows + Linux.
- [ ] No performance regression in in-process mode.

---

## 9. Risk Mitigation

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| TCP timeout hangs caller | Medium | High | Implement per-call timeout (configurable, default 30s). Return error if subprocess unresponsive. |
| Subprocess OOM crashes silently | Low | High | Monitor subprocess memory in health checks. Restart before OOM if possible (Phase 2). |
| Module discovery results out-of-sync | Medium | Medium | Tool discovery runs once at startup; cache results. Reload via dedicated tool. |
| Firewall blocks localhost TCP | Low | High | Localhost (127.0.0.1) is exempt from firewall on all platforms. Document this. |
| Cross-platform serialization bugs | Medium | High | Shared `PowerShellObjectSerializer` on both sides. Protocol version negotiation at startup. |
| Operator confusion (in-proc vs. out-proc) | High | Low | Clear documentation, distinct log messages, clear CLI help. Web UI mode indicator. |

---

## 10. Conclusion

**Recommended pattern:** **Pattern A (TCP Localhost)** for MVP.

**Why:** Simplest implementation, uniform cross-platform behavior, solves module isolation problem, acceptable performance, unblocks Phase 2 optimizations.

**Key insight:** Out-of-process execution doesn't need to be a permanent architecture change. It's an **opt-in deployment mode**. Operators choose at startup. In-process mode (current) remains the default, suitable for most users. Out-of-process mode available for users with module conflicts or isolation requirements.

**Next milestone:** Tech spike on subprocess wire protocol definition (~1 day). Then architecture review → implementation.



### 2026-04-10T00:00:00Z: Out-of-process recovery work should stay split between runtime product work and integration corpus maintenance
**By:** Steven Murawski (via Copilot/Farnsworth)
**What:** Treat out-of-process recovery as two separate streams. Stream 1 is product/runtime implementation in `PoshMcp.Server/` plus supporting docs, examples, and tests. Stream 2 is the `integration/Modules` corpus used only to validate module isolation and discovery scenarios. The corpus is a test fixture, not shipped runtime content.
**Why:** The repository already contains executable out-of-process plumbing, but the product entry point still lacks a runtime-mode contract while the vendored module corpus is large and operationally distinct. Keeping them separate prevents fixture maintenance from being mistaken for product completion and keeps runtime approvals tied to actual server wiring.

# PR Review: #138 and #139

**Reviewer:** Farnsworth
**Date:** 2025-07-18

## PR #138 — APPROVED ✅

**fix(#136): Fix Dockerfile restore/build**

**Summary:** Switches `dotnet restore` and `dotnet build` from `PoshMcp.sln` to `PoshMcp.Server/PoshMcp.csproj`, fixing the container build failure caused by missing test/client project files.

**Verdict reasoning:** Minimal, correct fix. Layer caching preserved. Runtime stage unchanged. No trailing whitespace. Non-blocking nit: `COPY PoshMcp.sln ./` is now unreferenced in the build stage — candidate for cleanup.

## PR #139 — APPROVED ✅

**feat(#137): Add auth, logging, env vars, MCP definitions to doctor**

**Summary:** Adds 4 new diagnostic sections (environment variables, authentication config, logging config, MCP resource/prompt definitions) to both text and JSON doctor output, with 12 new tests.

**Verdict reasoning:** All 7 env vars present. All 4 sections in both output formats. `BuildDoctorJson` parameters default to null with null-coalescing fallback — zero impact on existing callers. Tests are comprehensive with well-designed disposable helpers. Correct `[Collection("TransportSelectionTests")]` for parallel safety.

**Non-blocking nits:**
1. `TryLoadResourcesAndPromptsDefinitions` called unconditionally in `BuildDoctorJson` line 1166 even when both values pre-supplied — should be guarded like auth/logging 3 lines above.
2. `POSHMCP_LOG_FILE` (from PR #132) absent from the 7 env vars — follow-up candidate.


### 2026-04-10T00:00:00Z: Recovery review
**By:** Steven Murawski (via Copilot/Farnsworth)
**What:** Treat the current out-of-process MCP end-to-end path as incomplete and non-authoritative until `Program.cs` and the `InProcessMcpServer` test harness expose a supported `--runtime-mode` startup path. Keep subprocess/module-isolation tests, but do not let speculative end-to-end tests break the solution build. Also normalize all live deployment helpers from `POSHMCP_MODE` to `POSHMCP_TRANSPORT` to match the single-entry-point `poshmcp serve --transport ...` architecture.
**Why:** The repo was failing at build time because tests advanced past the implemented server surface, and deployment helpers still encoded a retired transport contract.

### 2026-04-10T00:00:00Z: Doctor tool gating coverage anchored on doctor JSON output
**By:** Steven Murawski (via Copilot/Fry)
**What:** Added focused doctor-command tests that treat the JSON payload from `poshmcp doctor --format json` as the public contract for configuration-troubleshooting tool gating, including default-hidden, config-enabled, and environment-override-disabled cases.
**Why:** This keeps the test surface small and user-visible while allowing internal tool registration details to move without rewriting the entire harness.

# Decision: Add startup-ordering regression tests for module import and function discovery

- Author: Fry
- Date: 2026-04-10
- Status: Proposed

## Decision
Add focused unit tests that exercise a shared isolated runspace and prove discovery outcomes differ before vs after environment setup steps.

## Why
- Existing tests covered command discovery by name/module and configuration parsing, but did not validate ordering between environment setup (`ImportModules` / startup script execution) and tool discovery.
- Discovery-before-import regressions can silently remove expected tools at startup.

## What was added
- `ModuleDiscoveryStartupOrderingTests` with two deterministic scenarios:
  - Module import then discovery discovers the module-exported function.
  - Startup script execution then discovery discovers the script-defined function.
- Each test asserts the negative case first (before setup => no tool), then positive case after setup (function discoverable and tool generated).

## Impact
- Provides a fast unit-level guardrail against startup ordering regressions without depending on full server startup.
- Keeps assertions tied to externally visible discovery behavior instead of private implementation details.


### 2026-04-10T00:00:00Z: Out-of-process discovery must honor configured module paths
**By:** Fry (via Copilot)
**What:** Forward `PowerShellConfiguration.Environment.ModulePaths` into the out-of-process discover request so the checked-in `integration/Modules` corpus participates in tool discovery and validation.
**Why:** The subprocess host already supports `modulePaths`, but the executor was not sending them. Without that handoff, out-of-process discovery ignored the repo's intentional module fixtures and left the highest-value validation path uncovered.

### 2026-04-10T00:00:00Z: Restore doctor troubleshooting flag and tool registration
**By:** Fry (via Copilot)
**What:** Restored `EnableConfigurationTroubleshootingTool`, its `POSHMCP_ENABLE_CONFIGURATION_TROUBLESHOOTING_TOOL` override, and `get-configuration-troubleshooting` registration after merge fallout removed the live source path while tests still expected the feature.
**Why:** The repository was failing at compile time on a missing doctor helper and then failing unit coverage because the troubleshooting feature had been dropped from active source without corresponding test or behavior changes.

# Test Plan: Out-of-Process PowerShell Runtime Mode

**Author:** Fry (Tester)
**Date:** 2026-04-10
**Branch:** managing_troublesome_modules
**Status:** Scaffolding complete — stubs + functional tests written

---

## Summary

Test scaffolding is written and compiles cleanly. Five test files cover all categories
in the task brief. Two categories are fully implemented (functional host-script tests,
integration module tests). Three categories are stubs awaiting Bender's implementation.

---

## Files Created

| File | Category | Status |
|------|----------|--------|
| `PoshMcp.Tests/Shared/OutOfProcessTestCollection.cs` | Collection def | Complete |
| `PoshMcp.Tests/Unit/OutOfProcess/SubprocessManagerTests.cs` | Unit — manager | Stubs |
| `PoshMcp.Tests/Unit/OutOfProcess/OutOfProcessCommandExecutorTests.cs` | Unit — executor | Stubs |
| `PoshMcp.Tests/Functional/OutOfProcess/SubprocessHostScriptTests.cs` | Functional | **Fully implemented** |
| `PoshMcp.Tests/Integration/OutOfProcess/OutOfProcessModuleTests.cs` | Integration | **Fully implemented** |
| `PoshMcp.Tests/Integration/OutOfProcess/OutOfProcessMcpServerTests.cs` | Integration e2e | Stubs |

---

## What Is Fully Implemented Now

### Category 3: SubprocessHostScriptTests (6 tests)

Located at `PoshMcp.Tests/Functional/OutOfProcess/SubprocessHostScriptTests.cs`.

These tests launch `poshmcp-host.ps1` directly via `Process.Start("pwsh", ...)`,
communicate via the stdin/stdout JSON wire protocol, and assert on response structure.
They can run the moment `PoshMcp.Server/poshmcp-host.ps1` exists.

All 6 tests skip automatically with a clear message if the host script is not found.

Tests:
- `HostScript_StartupMessage_WritesToStderr` — proves startup noise goes to stderr, not stdout
- `HostScript_ExecuteGetProcess_ReturnsJson` — proves happy-path JSON roundtrip
- `HostScript_ExecuteWithNullParams_FiltersNulls` — proves null params are stripped before execution
- `HostScript_ShutdownRequest_ExitsCleanly` — proves `{"type":"shutdown"}` exits with code 0
- `HostScript_UnknownRequestType_ReturnsError` — proves loop survives unknown type
- `HostScript_InvalidJson_ReturnsError` — proves loop survives malformed stdin

### Category 4: OutOfProcessModuleTests (7 tests)

Located at `PoshMcp.Tests/Integration/OutOfProcess/OutOfProcessModuleTests.cs`.

These tests run real Az and Microsoft.Graph modules in child pwsh processes using
`Process.Start("pwsh", "-Command -")`. They extend the pattern from `LocalModuleLoadingTests`.

Tests skip automatically if `integration/Modules/Az/15.5.0/` or
`integration/Modules/Microsoft.Graph/2.34.0/` are not present.
Use `[Trait("Category", "RequiresIntegrationModules")]` for selective filtering.

**The key test:** `OutOfProcess_AzAndGraph_LoadTogether_NoConflict` — loads
Az.Accounts and Microsoft.Graph.Authentication simultaneously in a single subprocess
and asserts both `Get-AzContext` and `Connect-Graph` are discoverable. This is the
primary proof that these MSAL-conflicting modules coexist in an isolated subprocess.

One test, `InProcess_AzAndGraph_ConfirmConflict`, is permanently skipped
(`[Fact(Skip = "...")]`) to avoid AppDomain pollution in the test host. Run manually
to document the conflict being solved.

---

## What Is Stubbed (Awaiting Bender)

### Category 1: SubprocessManagerTests (8 stubs)

Awaiting: `PowerShellSubprocessManager` class in `PoshMcp.Server.PowerShell` (or equivalent).

Each stub has detailed comments with:
- The full setup/act/assert code that will replace the stub
- The specific behavior being validated
- Mock patterns (using Moq, which is already in the test project)

### Category 2: OutOfProcessCommandExecutorTests (5 stubs)

Awaiting: `IPowerShellExecutor` interface + `OutOfProcessCommandExecutor` implementation.

One open question documented in stub `ExecuteCommandAsync_SubprocessError_ThrowsOrReturnsError`:
> **Decision needed:** Does error behavior throw an exception or return error-shape JSON?
> Must match `LocalPowerShellExecutor` behavior so `McpToolFactoryV2` doesn't branch on executor type.

### Category 5: OutOfProcessMcpServerTests (3 stubs)

Awaiting: `--runtime-mode out-of-process` flag wired into `Program.cs`.

These also need `InProcessMcpServer` to support passing extra command-line arguments
(currently it just calls `dotnet run ... PoshMcp.csproj`). A thin wrapper or overload
will be needed.

---

## Module Inventory (integration/Modules/)

**Az:** Present at `integration/Modules/Az/15.5.0/` ✅
- Also: individual Az.* sub-modules are present in `integration/Modules/`
- Tests use `Az.Accounts` (not the full `Az` umbrella) to avoid 100+ sub-module import latency
- `Az.Accounts` is the correct test target: it ships MSAL and conflicts with Microsoft.Graph.Authentication

**Microsoft.Graph:** Present at `integration/Modules/Microsoft.Graph/2.34.0/` ✅
- Also: `2.20.0` version present (tests use `2.34.0` as the latest)
- `Microsoft.Graph.Authentication/2.34.0/` is also present for the auth-specific skip guard
- Tests use `Microsoft.Graph.Authentication` as the primary Graph module for conflict testing

**Key conflict pair:** Az.Accounts + Microsoft.Graph.Authentication (both bundle MSAL)

---

## Test Execution Notes

**Run only the implemented tests:**
```
dotnet test --filter "OutOfProcess" --configuration Release
```

**Run only integration module tests (requires modules):**
```
dotnet test --filter "Category=RequiresIntegrationModules" --configuration Release
```

**Run only host script tests (requires poshmcp-host.ps1):**
```
dotnet test --filter "FullyQualifiedName~Functional.OutOfProcess" --configuration Release
```

**Timeout notes:**
- Host script tests: 30s per test (generous for pwsh startup)
- Module integration tests: 120s per test (Az.Accounts import can be slow first-run)
- Unit stubs: <1ms each (Assert.True(true, ...))

---

## Decisions / Questions for the Team

1. **Wire protocol finalized?**
   The tests assume:
   ```json
   Request:  {"type":"execute","command":"...","parameters":{...},"id":"..."}
   Response: {"type":"result","id":"...","data":[...],"errors":[]}
   Shutdown: {"type":"shutdown"}
   Error:    {"type":"error","id":"...","code":"...","message":"..."}
   ```
   If Bender changes the protocol, `SubprocessHostScriptTests` needs updating.

2. **Error behavior (throw vs. return)?**
   `OutOfProcessCommandExecutorTests.ExecuteCommandAsync_SubprocessError_ThrowsOrReturnsError`
   is deliberately open-ended. Bender needs to decide: throw exception or return error JSON?
   Must match `LocalPowerShellExecutor` behavior for `McpToolFactoryV2` compatibility.

3. **Host script path?**
   Tests expect `PoshMcp.Server/poshmcp-host.ps1`. If the script lives elsewhere,
   update `HostScriptPath` in `SubprocessHostScriptTests.cs`.

4. **InProcessMcpServer extra args?**
   The e2e tests need `InProcessMcpServer` to accept `--runtime-mode out-of-process`.
   Either extend the constructor or create a thin subclass.

5. **Az full umbrella import?**
   Tests use `Az.Accounts` as a proxy for the full `Az` module. If we need to test the
   full umbrella, add `OutOfProcess_AzUmbrella_LoadsCleanly` with a 5-minute timeout.
   Currently deferred due to latency concerns.


# Hermes — Host Script Design Decisions

**Author:** Hermes (PowerShell Expert)
**Date:** 2026-04-10
**Status:** Proposed
**Task:** Implementation of `PoshMcp.Server/PowerShell/OutOfProcess/poshmcp-host.ps1`

---

## Decisions Made

### 1. No `Set-StrictMode` — Use explicit null-guards instead

`Set-StrictMode -Version Latest` throws `PropertyNotFoundException` when code accesses
a property that does not exist on a `PSCustomObject` (the type returned by
`ConvertFrom-Json`).  Since the wire protocol intentionally omits optional fields
(e.g. a `discover` request may omit `commands`, `includePatterns`, etc.), strict-mode
property access would throw on every such omission.

`$ErrorActionPreference = 'Stop'` is retained because it converts non-terminating
PowerShell errors into terminating ones, which the try/catch blocks can handle.
Strict-mode added no safety beyond what explicit null-guards provide in this script.

**Rejected:** `Set-StrictMode -Version 1` — still enforces uninitialized variable
strictness but not object-property strictness, providing only partial benefit.

---

### 2. `Get-Command` + `& $cmdInfo @params` — not `Invoke-Expression`

Commands are resolved by name with `Get-Command` before execution.  The actual
invocation uses the resolved `CommandInfo` object: `& $cmdInfo @boundParams`.

This prevents command-injection via a crafted `"command"` field value.  An attacker
who controls the request JSON cannot execute arbitrary script via this path; they can
only invoke a command that already exists in the runspace.

**Rejected:** `Invoke-Expression "$commandName @params"` — arbitrary code execution.

---

### 3. Null parameter values are filtered out of the splat

Parameters whose JSON value is `$null` are excluded from `$boundParams`.  This
matches caller intent: `null` means "do not pass this parameter, use the default".

Implemented via `Get-Member -MemberType NoteProperty` iteration over the
`$Request.parameters` PSCustomObject, which is portable across PS 7 versions and
does not require `Set-StrictMode` workarounds.

---

### 4. Sub-module discovery for umbrella modules (Az, Microsoft.Graph)

`Get-Command -Module Az` returns zero results because the Az umbrella module itself
exports no commands — it only imports sub-modules (`Az.Compute`, `Az.Network`, etc.),
and the commands live there.

After importing a module, the handler also queries:
```powershell
Get-Module | Where-Object { $_.Name -like "$moduleName.*" }
```
and runs `Get-Command -Module` against each detected child module.  This produces the
full command inventory without requiring callers to enumerate every sub-module name.

**Impact on team:** The `discover` request `"modules"` field should list the top-level
umbrella name (`"Az"`); the script handles sub-module expansion automatically.

---

### 5. Module import errors are non-fatal in discover

If `Import-Module` fails for one module, the error is appended to the response
`"errors"` array and discovery continues with the remaining modules and named
functions.  A partial tool inventory is better than a total failure, consistent with
the project's stated goal of isolating bad modules from the rest of the tool set.

---

### 6. Include patterns evaluated before exclude patterns

`includePatterns` is an allow-list (empty = include all).  `excludePatterns` is a
deny-list applied after the allow-list.  Evaluation order: include → exclude.

Both use PowerShell `-like` wildcard matching (e.g. `*-AzBilling*`), which matches
the existing `appsettings.json` `ExcludePatterns` convention in the server.

---

### 7. `results` field in execute response is a JSON-encoded string

Per the wire protocol spec, `"results"` is a `string` field whose value is the JSON
serialization of the command output:

```json
{ "id": "req-1", "success": true, "results": "[{\"Name\":\"pwsh\"}]", "errors": [] }
```

The script serializes command output with `ConvertTo-Json -Depth 5 -Compress` and
stores the resulting string as `$response.results`.  When the outer response is
serialized by `Write-Response`, the string value is JSON-escaped as a quoted string.
No manual escaping is needed — PowerShell's serializer handles it.

---

### 8. UTF-8 no-BOM output encoding

On Windows, `pwsh` can emit a UTF-8 BOM on stdout unless suppressed.  The C# reader
in the parent process uses `new UTF8Encoding(false)` (no BOM).  To guarantee
consistency, the script sets:

```powershell
if ($IsWindows) {
    [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false
}
```

Linux and macOS do not emit BOMs; the conditional avoids a redundant object creation
on those platforms.

---

### 9. Stderr for all diagnostics — stdout reserved for protocol

`[poshmcp-host] ready` and `[poshmcp-host] entering request loop` go to stderr.  The
parent process drains stderr asynchronously (documented pattern in
`ValidateModuleInChildProcess` in `McpToolFactoryV2.cs`).

Nothing is written to stdout before the main loop starts and nothing diagnostic is
written to stdout during the loop.  Violating this invariant would corrupt the JSON
protocol stream.

---

### 10. `$script:CommonParameters` built once at script scope

The `HashSet<string>` for common-parameter exclusion is created once with
`$script:` scope and reused by every `Get-ToolDescriptor` call.  This avoids
reallocating the set for every command during a large `discover` response.

---

### 11. Loop-level catch-all with best-effort id recovery

The main request loop wraps every iteration in `try/catch`.  On failure, the handler
attempts to read `$request.id` to correlate the error response to the caller's
original request.  Since `$request` may be `$null` (e.g. JSON parse failure), this
inner read is itself wrapped in a nested `try/catch`.

---

## Open Questions for Team

1. **Per-command timeouts:** Should `poshmcp-host.ps1` enforce a timeout per
   `execute` call (e.g. `Start-Job` + `Wait-Job -Timeout`)?  Current implementation
   has no timeout — the C# host (Bender) is expected to kill the process via
   `Process.WaitForExit(timeout)` if needed.  Confirm ownership.

2. **Module-path side-effects across requests:** `modulePaths` prepended during a
   `discover` call persist in `$env:PSModulePath` for all subsequent `execute` calls
   in the same subprocess lifetime.  This may be intentional (modules discoverable =
   modules usable) or may need resetting.  Confirm design intent with Farnsworth.

3. **Runspace state shared between discover and execute:** Modules imported during
   `discover` are available in subsequent `execute` calls.  This is the expected
   "persistent runspace" design, but means module state (variables, aliases) leaks
   between the two phases.  Acceptable?

4. **`#Requires -Version 7.0`:** Included to fail fast on old `powershell.exe`
   invocations.  Confirm minimum supported pwsh version for out-of-process mode.

5. **Result size limits:** No `maxResults` cap is applied in this script.  If a
   command returns 50,000 objects, the JSON payload could be many MB.  Should the
   script honor an optional `"maxResults"` field in the execute request, or is
   result capping the caller's responsibility via command-native parameters
   (`-First`, `-Top`, etc.)?


# Hermes decision: module import before command discovery

- Date: 2026-04-10
- Author: Hermes
- Status: Proposed

## Decision
Import modules listed in `PowerShellConfiguration.Modules` before any function or module command discovery in `McpToolFactoryV2.GetAvailableCommandsWithMetadata`.

## Why
- Discovery previously queried `Get-Command -Name ...` before any import attempt.
- If module auto-loading is disabled or constrained, commands provided by configured modules are invisible to by-name discovery.
- The configuration model describes `Modules` as modules to import all commands from, so explicit import aligns runtime behavior with configuration intent.

## Implementation
- Added `ImportConfiguredModules(...)` in `PoshMcp.Server/McpToolFactoryV2.cs`.
- Called it at the start of `GetAvailableCommandsWithMetadata(...)` when configured modules are present.
- Import failures are logged as warnings and discovery continues (best-effort resilience).

## Validation
- Added regression test `GetToolsList_WithConfiguredModuleAndAutoloadDisabled_ImportsModuleBeforeNameDiscovery` in `PoshMcp.Tests/Unit/McpToolFactoryV2Tests.cs`.
- Test creates an ephemeral script module, disables auto-loading, and verifies tool generation still finds module command by name.


### 2026-04-10: Remove partial Az.AppConfiguration vendored module and align tests to split-module layout
**By:** Steven Murawski (via Copilot/Hermes)
**What:** Treat the untracked `integration/Modules/Az.AppConfiguration/2.0.1` subtree as partial merge fallout and remove it. Update out-of-process integration tests to validate the current split-module layout (`Az.Accounts`, `Microsoft.Graph.Authentication`) instead of old umbrella-module paths.
**Why:** The added Az.AppConfiguration subtree is incomplete: both `AppConfiguration.Autorest/bin` and `AppConfigurationdata.Autorest/bin` are missing, and `Import-Module` fails with a missing `Az.AppConfiguration.private` assembly. The repo's current vendored module layout is split by module name at `integration/Modules/*`, so tests gating on `integration/Modules/Az/15.5.0` and `integration/Modules/Microsoft.Graph/2.34.0` no longer match the actual structure.

### 2026-04-10T00:00:00Z: Out-of-process PowerShell host must gate on readiness and honor framework execution options

**By:** Steven Murawski (via Copilot/Hermes)
**What:** The out-of-process PowerShell host now waits for an explicit readiness signal before the server uses it, propagates configured module paths/imports/startup hooks into discovery, and applies `requestedProperties`, `maxResults`, and result caching inside the subprocess so remote execution matches in-process semantics more closely.
**Why:** Returning from startup before `pwsh` is actually ready creates race conditions and silent startup failures. Ignoring protocol fields like module paths and result shaping makes out-of-process behavior diverge from the plan and breaks split-module layouts such as `integration/Modules/*`.

# Out-of-Process PowerShell Hosting — PowerShell Patterns Research

**Researcher:** Hermes  
**Date:** 2026-04-10  
**Status:** Research Complete  
**Cross-Platform:** ✅ Windows / Linux / macOS considerations throughout

---

## 1. PowerShell Process Hosting Constraints

### Subprocess Lifecycle (Windows, Linux, macOS)

**Finding: pwsh is uniformly available and reliable as subprocess across all three platforms.**

- **Windows:** `pwsh.exe` from published Microsoft.PowerShell.SDK or PowerShell MSI
- **Linux:** `pwsh` available via package managers (apt, yum, snap)
- **macOS:** `pwsh` available via Homebrew or direct package

**Reliability pattern:** `ProcessStartInfo` with `FileName="pwsh"` and `-NonInteractive -NoProfile` flags spawns reliably. Exit codes and signals behave consistently:
- `exit 0` = success
- Non-zero exit codes = command or module failure
- `0xC0000005` (Windows) = native memory access violation (likely module crash or corruption)
- `143` (Linux/macOS) = SIGTERM received (timeout kill)

**Cross-platform subprocess mechanics:**
- **Windows:** Uses process handles; `Process.Kill(true)` tree-kills with grace period
- **Linux/macOS:** Uses POSIX signals; `Process.Kill(true)` sends SIGTERM then SIGKILL
- Timeout behavior: `WaitForExit(milliseconds)` works uniformly; `Process.WaitForExit()` without timeout hangs if subprocess blocks

**Example from codebase:** `ValidateModuleInChildProcess` in McpToolFactoryV2.cs already demonstrates 30-second timeout with tree-kill fallback — this pattern is production-validated.

---

### Stdin/Stdout Redirection: Reliability for JSON Serialization

**Finding: Stdin/stdout via redirection is safe for JSON-serialized data on all platforms, with UTF-8 encoding as the cross-platform constant.**

**Encoding handling:**
- **Default behavior:** `ProcessStartInfo` with no explicit CodePage uses UTF-8 on .NET 5+ uniformly across Windows, Linux, macOS
- **Line endings:** PowerShell normalizes CRLF → LF automatically in some contexts; safer to explicitly set `$PSDefaultParameterValues['Out-File:Encoding']='UTF8'` in subprocess initialization
- **BOM (Byte Order Mark):** UTF-8 BOM can appear on Windows-only. Mitigation: use `UTF8Encoding(false)` (no BOM) in C# when reading subprocess output
- **Buffer sizes:** Default 4096-byte buffer is sufficient for most MCP results; large payloads (>10 MB) benefit from explicit StreamReader buffer tuning

**Stream handling gotchas:**
1. **Deadlock risk if not drained:** Subprocess stderr blocking while parent waits on stdout → deadlock
   - **Fix:** Drain both stdout and stderr asynchronously (example in codebase uses tasks)
   - Pattern: `stderrTask = process.StandardError.ReadToEndAsync(); stderrContent = stderrTask.Result;`
2. **Closed stream exception:** If subprocess closes unexpectedly, reading returns empty rather than throwing (safe behavior)
3. **Pipe error (EPIPE on Linux):** If parent closes stdin before subprocess writes to stdout, subprocess may crash. Mitigation: keep stdin open or suppress SIGPIPE

**JSON-specific findings:**
- **PowerShell's `ConvertTo-Json`:** Works uniformly; output is valid UTF-8. No platform-specific JSON variants
- **Serialization of complex objects:** PSObject properties serialize identically across platforms (no Windows/Linux property differences for `Get-Service`, `Get-Process`, etc. core cmdlets)
- **Large results:** `[int] $MaxResults` parameter can cap results before serialization; proven in functional tests to work across all platforms

---

### Context Setup (Module Paths, Policies, Profiles)

**Finding: Module path discovery and policy setup differs by platform; profiles must be skipped for deterministic subprocess execution.**

**Module path configuration across platforms:**

| Aspect | Windows | Linux | macOS | Cross-Platform Pattern |
|--------|---------|-------|-------|------------------------|
| **$PSModulePath** | `$PSHOME\Modules; $PROFILE\..\Modules; $env:PSModulePath` | `/opt/powershell/Modules; ~/.local/share/powershell/Modules; /usr/local/share/powershell/Modules` | `/opt/powershell/Modules; ~/.local/share/powershell/Modules; /usr/local/share/powershell/Modules` | **Explicit path passing via env var or CLI arg** |
| **Registry (Windows only)** | HKLM/HKCU for module paths | N/A | N/A | **Skip registry-based discovery; assume cmdline + env var only** |
| **Home directory** | `%USERPROFILE%` | `$HOME` | `$HOME` | **Use `$env:HOME` uniformly** |

**Recommended subprocess initialization script (cross-platform):**

```powershell
# Executed in spawned subprocess with -NonInteractive -NoProfile
# 1. Skip all profiles (deterministic execution)
# 2. Set execution policy to Bypass for Process scope (no persistence)
if ($PSVersionTable.Platform -ne 'Linux') {
    # Windows only - no-op on Linux/macOS
    Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process -Force -ErrorAction SilentlyContinue
}

# 3. Configure PSModulePath if custom paths provided
if (-not [string]::IsNullOrWhiteSpace($env:POSHMCP_MODULE_PATH)) {
    $env:PSModulePath = "$env:POSHMCP_MODULE_PATH" + [System.IO.Path]::PathSeparator + $env:PSModulePath
}

# 4. Set ErrorActionPreference to stop-on-first-error (safer for validation)
$ErrorActionPreference = 'Stop'

# Command execution follows
```

**Cross-platform module path passing via environment:**
- **Option A (env var):** Export `$env:POSHMCP_MODULE_PATH = "/path1:/path2"` before spawning subprocess (Unix-style separator works in PowerShell 7+ on all platforms)
- **Option B (CLI arg):** Pass `-Command "& { $env:PSModulePath = '...' ; Import-Module 'ModuleName' }"`
- **Recommended:** Option A with escaped colons on Windows if needed; PowerShell handles path separator normalization

**Profile executon gotchas:**
- `-NoProfile` flag prevents `$PROFILE` execution (safe, recommended)
- Without `-NoProfile`, Windows reads HKLM and HKCU profiles; Linux/macOS read `~/.config/powershell/profile.ps1`
- **Issue:** If user profile has `Import-Module Az.Accounts`, it may run before subprocess module validation, causing conflicts
- **Solution:** Always use `-NoProfile` for deterministic isolation

---

### Cleanup & Resource Management

**Finding: Process cleanup is reliable across all platforms; file handles and module state are properly freed on process exit. Watch for zombie processes on Linux/macOS if parent crashes.**

**Cleanup patterns:**

1. **Normal exit (process.Exit(0)):**
   - Runspace disposed
   - All handles closed by OS
   - Modules unloaded
   - Works identically on Windows, Linux, macOS

2. **Timeout kill (30s default):**
   - Windows: `Process.Kill(true)` → `TerminateProcess()`
   - Linux/macOS: `Process.Kill(true)` → SIGTERM + SIGKILL
   - Both close handles and free memory immediately

3. **Zombie processes (Linux/macOS specific):**
   - If parent crashes without calling `Dispose()` on Process, OS zombie remains until parent is reaped
   - **Mitigation in PoshMcp:** `TestProcessRegistry` pattern (already in codebase) tracks all spawned processes and kills on `ProcessExit` or unhandled exception
   - Operational cleanup: kill any `pwsh` processes older than 5 minutes (stale validation processes)

4. **File handle leaks:**
   - **Symptom:** 2nd subprocess validation fails with "file in use" on Windows
   - **Root cause:** Parent process keeps stdout/stderr streams open after read
   - **Fix:** `process.StandardOutput.Dispose()` and `process.StandardError.Dispose()` explicitly after `WaitForExit()`
   - Cross-platform: same mitigation works on all three

5. **Module reload contamination (in-process AppDomain, not subprocess):**
   - Subprocesses are isolated; no contamination into parent runspace
   - Parent runspace module state unaffected by subprocess crashes
   - **This is the entire point of out-of-process validation**

---

## 2. Module Isolation & Loading (Cross-Platform)

### Common Module Conflicts (In-Process)

**Finding: Certain modules fail in-process due to AppDomain pollution, type conflicts, and registry assumptions. Out-of-process isolation solves most.**

**Known conflict patterns in PoshMcp (documented in team memory):**

1. **Type definition conflicts:**
   - **Example:** `GroupPolicy` module on Windows defines `Microsoft.GroupPolicy.WmiObject`
   - If loaded alongside other modules using similar strongly-typed COM objects, CLR AppDomain type resolution fails
   - **Error manifest:** `PSArgumentException: Cannot find a matching overload for method 'XyzMethod'` (misleading; actually type mismatch in AppDomain)
   - **Out-of-process fix:** Each subprocess has fresh AppDomain; no accumulated type pollution
   - **Cross-platform:** Linux doesn't have WMI, so GroupPolicy never loads; Windows-specific conflict

2. **Reflection-heavy module initialization:**
   - **Example:** `Azure.PowerShell.Cmdlets.Billing` (part of Az.Billing) scans all executing types on module import
   - If PoshMcp has already imported `Az.Accounts`, the Billing module may detect it and try to augment it
   - This works in-process by design (sharing is the goal), but if modules have **version conflicts** or **dependency cycles**, the second import fails silently or hangs
   - **Error manifest:** `Import-Module` appears to hang for 30+ seconds; actually spinning on lock contention
   - **Out-of-process fix:** Billing module runs in clean AppDomain; no prior Az.Accounts state
   - **Cross-platform:** This is common on Windows/Linux (Azure modules); mostly Windows Azure Stack issues on macOS

3. **Registry-based assumptions (Windows-only):**
   - **Example:** `WMI` cmdlets on Windows assume registry keys exist in `HKLM\Software\Microsoft\Windows\CurrentVersion\policies\system`
   - If running PowerShell with restricted registry access, import silently fails or cmdlets behave unexpectedly
   - **Error manifest:** `Get-WmiObject` returns no results, but no error (Windows behavior: fall back to empty)
   - **Out-of-process fix:** Subprocess runs in same process context (Windows only); no fix
   - **Cross-platform:** N/A on Linux/macOS (no WMI)
   - **Practical mitigation:** For cross-platform tools, avoid WMI; use CIM/WinRM instead

4. **Dependent assembly version conflicts:**
   - **Example:** `Newtonsoft.Json 11.0` + `Newtonsoft.Json 13.0` both loaded in same AppDomain
   - Some PowerShell modules (especially older community modules or vendor-specific tools) bundle JSON libraries
   - If PoshMcp loads a newer version first, the older module's bundled DLL is ignored, causing method-not-found errors
   - **Error manifest:** `MethodAccessException: Method 'JsonConvert.DeserializeObject<T>' not found on type 'Newtonsoft.Json.JsonConvert'`
   - **Out-of-process fix:** Subprocess loads module's bundled DLL in isolation; no conflict with parent
   - **Cross-platform:** Common on Windows/Linux for enterprise tools; Linux distro modules often bundle libraries

---

### Isolation Boundaries (Out-of-Process Model)

**Finding: Out-of-process provides complete isolation at AppDomain and filesystem levels. Cost is process latency and IPC overhead.**

**Isolation model:**

```
┌─────────────────────────────────────────┐
│  PoshMcp Server Process (Parent)        │
│  - Loaded modules: Az.Accounts, Pester  │
│  - Type registry: Pester.*.Types        │
│  - Variables: $ServerState = {...}      │
│  - Runspace: singleton shared session   │
└─────────────────┬───────────────────────┘
                  │ (Process spawn via pwsh)
                  │ Stdin/Stdout pipe
                  │ JSON wire protocol
                  ▼
┌─────────────────────────────────────────┐
│  Subprocess (Child)                     │
│  - Fresh AppDomain                      │
│  - No loaded modules (except cmdre)     │
│  - Isolated type registry               │
│  - No access to parent $ServerState     │
│  - Module import x crashes child        │
│  - Parent runspace inaffected           │
└─────────────────────────────────────────┘
```

**Isolation coverage:**

| Isolation Aspect | In-Process | Out-of-Process | Notes |
|---|---|---|---|
| **AppDomain type registry** | Shared across modules | Isolated per process | Solves type conflicts |
| **CLR assembly binding** | Shared; version conflicts visible | Isolated per process | Solves DLL binding issues |
| **Filesystem access** | Shared; modules can modify cwd | Isolated per process | Module side-effects confined |
| **Registry (Windows)** | Shared; elevation context matters | Shared subprocess context | No fix for Windows-specific issues |
| **Environment variables** | Inherited by subprocess | Can be overridden | See section 1.3 for passing |
| **Loaded modules** | Accumulate in parent | Subprocess starts clean | Core isolation benefit |
| **Process crash** | Terminates parent → MCP down | Terminates child → try next subprocess | Resilience gain |
| **Memory (MB)** | All modules in one heap | Each subprocess separate heap | Per-module cost ~5-50 MB |

**Cost analysis:**

- **Per-module subprocess spawn:** 500-800ms startup (pwsh init + module import)
- **Recurring calls same module:** Can reuse subprocess, amortize cost
- **Memory:** Parent + N subprocesses = (parent baseline) + N * (pwsh baseline ~50 MB + module ~5-30 MB each)
- **Stateful commands:** Lose session state across subprocess boundaries (design choice needed)

---

### Module Discovery & Import Strategy

**Finding: Recommended pattern for cross-platform support is explicit subprocess-per-module-group with pre-validated module list.**

**Strategy (pseudo-implementation):**

```
1. At startup, PoshMcp loads PowerShellConfiguration.Modules
2. For each module:
   a. Validate in isolation subprocess (existing ValidateModuleInChildProcess)
   b. If validation fails:
      - Log warning
      - Exclude module from MCP tools
      - Continue to next module
   c. If validation succeeds:
      - Mark module as "safe"
      - Store in subprocess pool or "import-on-demand" list
3. During tool invocation:
   a. Determine which module(s) the tool needs
   b. If in-process safe (pre-validated, no conflicts): import to parent runspace
   c. If out-of-process needed (high-risk module or requested isolation):
      - Spawn subprocess with module
      - Execute command in subprocess
      - Return result via JSON
      - Keep subprocess alive for 30s (reuse for repeated calls)
   d. After 30s idle, kill subprocess and return pool
4. Cross-platform consideration:
   - Windows-specific modules (GroupPolicy, WMI) marked as Windows-only
   - Call filtered from Linux/macOS clients
```

**Module discovery ordering (cross-platform):**

1. `$PSModulePath` from environment (passed by parent)
2. Builtin cmdlet-only modules (no imports needed)
3. User-provided module paths (from `EnvironmentConfiguration.ModulePaths`)
4. PowerShell Gallery (if `Install-Module` configured)

**Team memory note:** "McpToolFactoryV2 discovery must import PowerShellConfiguration.Modules before any Get-Command name/module queries." — This is currently done, but should be validated with explicit test for cross-platform behavior.

---

### Platform-Specific Module Availability

**Finding: Module ecosystems differ by platform; Windows Azure/Group Policy modules not available on Linux/macOS. Docs must flag this.**

**Module availability table:**

| Module | Windows | Linux | macOS | Notes |
|--------|---------|-------|-------|-------|
| **Az.Accounts** | ✅ | ✅ | ✅ | Core Azure auth; cross-platform |
| **Az.Billing, Az.AnalysisServices** | ✅ | ✅ | ✅ | REST-based; cross-platform |
| **GroupPolicy** | ✅ | ❌ | ❌ | WMI-based; Windows-only |
| **DnsClient** | ✅ | ❌ | ❌ | Win32 API; Windows-only |
| **ScheduledTasks** | ✅ | ❌ | ❌ | Windows Task Scheduler; no equivalent on Unix |
| **Pester** | ✅ | ✅ | ✅ | Test framework; cross-platform |
| **PSReadLine** | ✅ | ✅ | ✅ | Interactive editing; cross-platform |
| **ImportExcel** | ✅ | ✅ | ✅ | .NET-based; cross-platform |

**Cross-platform discovery strategy:**

```csharp
// In PowerShellConfiguration or startup
var osSpecificModules = new Dictionary<string, string[]>
{
    { "Windows", new[] { "GroupPolicy", "DnsClient", "ScheduledTasks" } },
    { "Linux", new[] { } }, // No OS-specific modules
    { "Darwin", new[] { } }  // No OS-specific modules
};

var currentPlatform = Environment.OSVersion.Platform switch
{
    PlatformID.Win32NT => "Windows",
    PlatformID.Unix when RuntimeInformation.IsOSPlatform(OSPlatform.Linux) => "Linux",
    PlatformID.Unix when RuntimeInformation.IsOSPlatform(OSPlatform.OSX) => "Darwin",
    _ => "Unknown"
};

// Filter modules based on platform
var availableModules = config.Modules
    .Where(m => !osSpecificModules.Any(kv => kv.Key != currentPlatform && kv.Value.Contains(m)))
    .ToList();
```

---

## 3. Command Execution Models

### Stateful Runspace (Per-Session Retention)

**Finding: Stateful runspace is viable cross-platform; current SessionAwarePowerShellRunspace proves pattern. Out-of-process makes statefulness harder.**

**Current in-process stateful model (production, cross-platform):**

```csharp
// PoshMcp.Server/PowerShell/SessionAwarePowerShellRunspace.cs
// Creates isolated runspace per Mcp-Session-Id header
// Variables persist across calls in same session
// Example:
//   Call 1: Set-Variable -Name foo -Value "bar"
//   Call 2: Get-Variable -Name foo  (returns "bar")
```

**Characteristics:**

| Aspect | Behavior | Cross-Platform |
|--------|----------|-----------------|
| **Variables** | Persist across calls | ✅ Identical on Windows/Linux/macOS |
| **Functions** | Can define custom functions | ✅ Works everywhere |
| **Module imports** | Shared within session | ✅ Works, but risks module conflicts |
| **Working directory** | Changes affect next call | ✅ Works, but use absolute paths to avoid surprises |
| **Session timeout** | After 30 min idle, runspace can be disposed | ✅ Timer logic is platform-agnostic |

**Why statefulness matters in out-of-process scenario:**

- **Problem:** Out-of-process subprocess dies after process exit → state lost
- **Mitigation A:** Keep subprocess alive (subprocess pool)
  - Reuse subprocess for multiple commands from same session
  - 30s idle timeout before recycle
  - Cost: ~100 MB memory per active module subprocess
- **Mitigation B:** Accept statelessness
  - Each command gets fresh subprocess
  - No state retention
  - Simpler, but limits use cases

**Recommended pattern:** Use in-process stateful for common paths (Az.Accounts, Pester); out-of-process stateless for high-risk modules.

---

### Stateless (Module-Per-Call)

**Finding: Stateless execution (spawn subprocess, run command, exit) is cheapest and safest for out-of-process isolation. Works uniformly across platforms.**

**Stateless model:**

```
User calls: Invoke-MCP-Tool("Get-Service", ["dhcp"])

PoshMcp server:
  1. Check if "Get-Service" is marked high-risk
  2. If no:  Use in-process singleton runspace
     If yes: Spawn subprocess
  3. Subprocess:
     a. pwsh -NonInteractive -NoProfile -Command "Import-Module <deps>; Get-Service dhcp"
     b. Capture stdout (JSON)
     c. Exit(0)
  4. Parent parses JSON, returns result
  5. Subprocess memory freed
```

**Characteristics:**

| Aspect | Behavior | Trade-off |
|--------|----------|-----------|
| **Startup cost** | 500-800ms per call | Significant; only worthwhile for truly problematic modules |
| **State retention** | None (fresh process) | Acceptable if documented |
| **Memory** | Recycled per call | No accumulation |
| **Cross-platform cost** | Identical on all platforms | pwsh startup time ~500ms default everywhere |
| **Error isolation** | Module crash → subprocess exit | Excellent; parent unaffected |
| **Concurrent calls** | Can spawn multiple subprocesses | N processes × startup cost; need rate limiting |

**Cross-platform performance notes:**

- Windows: PowerShell startup ~500ms (includes TPM/security checks on some systems)
- Linux: pwsh startup ~250-400ms (depends on distro, filesystem type)
- macOS: pwsh startup ~300-500ms (depends on code signing, Gatekeeper delays)

**Optimization:** Use subprocess pool (keep alive 30-60s) instead of true stateless; amortizes startup cost.

---

### Hybrid Approaches

**Finding: Recommended hybrid is in-process for safe modules + subprocess pool for risky ones. Session affinity helps with statefulness.**

**Recommended architecture:**

```
┌─ PoshMcp Server (Parent)
│  ├─ SingletonPowerShellRunspace (shared across sessions)
│  │  ├─ Loaded: Az.Accounts (safe, validated)
│  │  ├─ Loaded: Pester (safe, validated)
│  │  └─ Runspace: Variables/functions persist
│  │
│  ├─ ModuleSubprocessPool (keyed by module name)
│  │  ├─ Pool["GroupPolicy"] → [subprocess 1, subprocess 2] (if alive)
│  │  ├─ Pool["VendorTool"] → [subprocess] (if alive)
│  │  └─ Subprocesses auto-recycled after 30s idle
│  │
│  └─ SessionAwarePowerShellRunspace (per-HTTP-session isolation)
│     ├─ Session 123 → IsolatedRunspace_123 (separate variables)
│     └─ Session 456 → IsolatedRunspace_456 (separate variables)
```

**Decision logic per tool invocation:**

```
Tool = "Get-Service" (from Microsoft.PowerShell.Management)
Module = null (builtin)
  → Use SingletonPowerShellRunspace (execute synchronously)

Tool = "New-AzResourceGroup" (from Az.Resources)
Module = "Az.Resources" (depends on Az.Accounts)
Module validation = "safe"
  → Use SingletonPowerShellRunspace (but ensure Az.Accounts imported first)

Tool = "Import-GPO" (from GroupPolicy)
Module = "GroupPolicy"
Module validation = "Windows-only"
Host = Linux
  → Return error "Module 'GroupPolicy' not available on Linux"

Tool = "Invoke-VendorTool" (from VendorModule)
Module = "VendorModule"
Module validation = "FAIL: subprocess crashed"
  → Get subprocess from ModuleSubprocessPool["VendorModule"]
  → If no subprocess alive: spawn new one, validate, add to pool
  → Execute command in subprocess via JSON RPC
  → Return result
```

**Cross-platform implications:**

- Windows-only modules skip singleton, use subprocess isolation (prevents crashes)
- Linux/macOS filter out Windows-only modules early (no subprocess cost)
- Subprocess pool size tuned per platform (Windows: smaller due to heavier processes)

---

## 4. Serialization & Data Passing (Cross-Platform)

### Current PoshMcp Serialization (PSObjectJsonConverter)

**Finding: Current serialization uses PowerShellObjectSerializer.FlattenPSObject -> System.Text.Json. Tested cross-platform; output identical.**

**Current pipeline:**

```csharp
// PowerShell execution returns PSObject[]
PSObject[] results = ps.Invoke();

// PSObjectJsonConverter normalizes each PSObject
foreach (var psObj in results) {
    var normalized = PowerShellObjectSerializer.FlattenPSObject(psObj);
    // normalized is now: scalars, Dictionary<>, List<>, null — no PSObject wrappers
}

// System.Text.Json serializes normalized objects
string json = JsonSerializer.Serialize(normalized, PowerShellJsonOptions.Options);
```

**Key serializer behaviors (cross-platform identical):**

| Behavior | Example | Cross-Platform Test |
|----------|---------|---------------------|
| **Scalar handling** | `"hello"` string → JSON `"hello"` | ✅ Identical output on all platforms |
| **Hashtable** | PowerShell `@{x=1;y=2}` → JSON `{"x":1,"y":2}` | ✅ Works (converted to Dictionary) |
| **PSCustomObject** | `[PSCustomObject]@{a=1}` → JSON `{"a":1}` | ✅ Works (unwrapped properties) |
| **Complex types** | `System.Diagnostics.Process` → JSON `{...process props...}` | ✅ Flattened, but slow if recursive |
| **Null values** | PowerShell `$null` → JSON `null` | ✅ Works |
| **Collections** | `array`, `List<>`, `Collection<>` → JSON `[...]` | ✅ Works |

**Current limitations (documented in team memory):**

1. **Live object performance:** If you serialize a Process object with nested .Modules property (is IEnumerable), the serializer walks the entire tree, which triggers Win32 API calls → hangs. **Fix:** Shallow serialization for expensive properties.
2. **Pointer types:** `System.ReadOnlySpan<byte>` (pointer-like) cannot be serialized. Logged warning; method skipped. **This is acceptable; rare edge case.**
3. **CLR property leaking:** Direct System.Text.Json can leak internal Hashtable properties. **Current fix:** Normalize to Dictionary first.

---

### Bidirectional Viability (Across Process Boundary)

**Finding: Serialization is one-way (PowerShell → JSON) in current design. Deserialization (JSON → PowerShell PSObject) is NOT implemented. Bidirectional across process boundary needs design.**

**Current one-way design:**

```
Parent runspace
  ↓ (ps.Invoke() returns PSObject[])
Server
  ↓ (PowerShellObjectSerializer.FlattenPSObject)
Normalized objects (Dictionary, List, scalars)
  ↓ (System.Text.Json.Serialize)
JSON over HTTP or stdio
  ↓ (Client deserialization)
Client application (e.g., Claude)
```

**For out-of-process subprocess communication, reverse path needed:**

```
Parent MCP call with arguments {name: "GetService", args: {name: "dhcp"}}
  ↓ (JSON)
Subprocess receives JSON
  ↓ (Deserialize JSON → PowerShell parameters??)
PowerShell command: Get-Service -Name "dhcp"
  ↓ (ps.Invoke())
Subprocess result: PSObject[]
  ↓ (Flatten + JSON)
Return JSON to parent
```

**Deserialization challenges:**

1. **Type mapping:** JSON `"dhcp"` → PowerShell string. Simple.
   - But JSON might be `{"_module": "Az.Resources", "_type": "ResourceGroup", "name": "mygroup"}`
   - Deserialize to what PowerShell type? `[ResourceGroup]`? Need type registry.

2. **Parameter transformation:** MCP tool parameter schema says `"type": "array"`.
   - JSON `["a", "b", "c"]`
   - PowerShell command expects `-Name string[]`
   - Deserialization: convert JSON array → PowerShell array (works)
   - But if command wants custom type (e.g., `[PSCredential]`), JSON deserialization can't construct it

3. **Bidirectional design options:**

   **Option A (Recommended): Stateless subprocess, client-side parameter binding**
   ```
   Parent:
     Parameters from MCP call (already in C# objects)
       ↓ Convert C# object → PowerShell string form
       ↓ Pass to subprocess as command-line argument
   Subprocess:
     PowerShell parses command-line → parameter values
     → No JSON deserialization in subprocess
   ```
   
   **Option B: JSON schema + PowerShellParameterUtils**
   ```
   Subprocess receives JSON
   Uses PowerShellParameterUtils.ConvertParameterValue to transform
   Maps JSON to PowerShell parameter types
   Higher effort; more flexible if subprocess needs full PSObject state
   ```

**Cross-platform implications:** Option A is simpler and platform-agnostic. Option B works but requires more testing (different type handling on Windows vs. Linux vs. macOS).

---

### Complex Type Handling, Round-Trip Survival

**Finding: Complex types (custom classes, module-specific types) do NOT survive round-trip across process boundary in current design. This is acceptable; out-of-process is stateless by definition.**

**Round-trip analysis:**

| Type | Parent → Child Serialization | Survival | Notes |
|------|------|---|---|
| Built-in scalar (int, string, bool) | ✅ JSON | ✅ 100% | No issues |
| System.Collections.Hashtable | ✅ JSON → {key: value} | ✅ 100% | Works; becomes PSCustomObject in subprocess |
| System.Management.Automation.PSCredential | ✅ JSON ??? | ❌ 0% | Cannot serialize SecureString; credentials don't round-trip |
| Custom class MyModule.ResourceType | ✅ JSON → {prop: value} | ❌ 0% | Subprocess has no `MyModule.ResourceType` class; becomes PSCustomObject |
| System.Diagnostics.Process | ✅ JSON (flattened) | ⚠️ 5% | Subprocess can't reconstruct Process handle; mostly serialized data only |

**Practical implication:** If a parent command returns a custom object, you can't pass it back to the same module in a subprocess. This is endemic to process boundaries; not a PoshMcp limitation.

---

### Error Propagation ($Error, ExceptionRecord)

**Finding: PowerShell error propagation across process boundary requires explicit capture in subprocess and serialization to JSON. Current design captures but doesn't expose in MCP response.**

**Current error handling (in-process):**

```csharp
// McpToolFactoryV2 generated methods invoke command
ps.Invoke();
if (ps.HadErrors)
{
    // Errors are in ps.Streams.Error (Collection<ErrorRecord>)
    var errorRecords = ps.Streams.Error;
    // Currently: logged but not returned to MCP client
}
```

**For out-of-process (recommended):**

```powershell
# In subprocess
try {
    Get-Service -Name "invalid" -ErrorAction Stop
} catch {
    # Capture exception
    $errorInfo = @{
        Exception = $_.Exception.Message
        ErrorRecord = $_.FullyQualifiedErrorId
        ScriptStackTrace = $_.ScriptStackTrace
        StackTrace = $_.Exception.StackTrace
    }
    # Return as JSON
    ConvertTo-Json $errorInfo
}
```

**Error serialization schema (cross-platform):**

```json
{
  "success": false,
  "error": {
    "message": "Cannot find a matching parameter set for the specified parameters",
    "category": "InvalidArgument",
    "fullyQualifiedId": "System.Management.Automation.ParameterBindingException",
    "scriptStackTrace": "at <ScriptBlock>, <No file>: line 1"
  },
  "stderr": "(optional stderr output)"
}
```

**Cross-platform error behavior:**

| Error Type | Windows | Linux | macOS | Handling |
|---|---|---|---|---|
| **Module not found** | Identical message | Identical message | Identical message | ✅ Same error text |
| **Permission denied** | Access is denied | Permission denied | Permission denied | ⚠️ Different text; normalize |
| **Timeout** | (subprocess killed; no PowerShell error) | SIGTERM (same) | SIGTERM (same) | ✅ Handled uniformly |
| **Type not found** | Identical exception | Identical exception | Identical exception | ✅ Same error |

**Recommended change:** Extend MCP response schema to include optional `error` block with full `ExceptionRecord` details. This is out of scope for current research but feasible.

---

### Line Endings & Encoding Gotchas

**Finding: UTF-8 encoding is safe cross-platform with explicit BOM handling. Line endings (CRLF vs LF) are platform-dependent but PowerShell 7+ normalizes transparently.**

**Encoding security:**

1. **UTF-8 default:** ProcessStartInfo uses UTF-8 on all platforms by default (.NET 5+)
   - No special handling needed
   - BOM (Byte Order Mark) presence: Windows subprocess may emit UTF-8 BOM (`EF BB BF`); Linux/macOS typically don't
   - **Mitigation:** Use `Encoding utf8 = new UTF8Encoding(false);` to ensure no BOM in JSON output

2. **Line ending normalization:**

   ```powershell
   # Parent (Windows)
   $result = "line1`r`nline2" # CRLF
   # Subprocess (Linux) via pipe receives UTF-8 bytes representing CRLF
   # PowerShell normalizes: when reading from stdin, CRLF → automatic handling
   # When writing to stdout with ConvertTo-Json: PowerShell normalizes to LF (Unix style) on pwsh 7+
   ```

   | Platform | Default output | Visible as | PowerShell 7+ behavior |
   |---|---|---|---|
   | Windows | CRLF | `^M^J` in hex | Uses LF in modern pwsh.exe |
   | Linux | LF | newline | Uses LF |
   | macOS | LF | newline | Uses LF |

3. **JSON-specific encoding:**
   - `ConvertTo-Json` always outputs valid JSON (RFC 7159)
   - Newlines within JSON strings are escaped (`\n`)
   - CRLF within strings serializes as `\r\n` (safe)
   - No platform-specific JSON variants

4. **Real-world subprocess command (cross-platform safe):**

   ```csharp
   using var process = Process.Start(new ProcessStartInfo
   {
       FileName = "pwsh",
       Arguments = "-NoProfile -Command \"Get-Service dhcp | ConvertTo-Json\"",
       RedirectStandardOutput = true,
       StandardOutputEncoding = new UTF8Encoding(false), // No BOM
       UseShellExecute = false
   });
   
   string json = process.StandardOutput.ReadToEnd(); // Works identically on all platforms
   ```

---

## 5. Cross-Platform Considerations (Windows vs. Linux vs. macOS)

### Windows-Specific Concerns

**Registry assumptions:**
- Many Windows cmdlets assume registry keys + ACLs exist (e.g., WMI consumer modules read HKLM)
- Out-of-process subprocess runs in same process context → registry access identical to parent
- **Not solved by out-of-process.** Mitigation: use CIM/REST APIs instead of WMI where possible.

**Code signing & Gatekeeper (not Windows, but relevant for comparison):**
- Windows has Authenticode signing; PowerShell respects `-ExecutionPolicy RemoteSigned`
- Add `COPY *.ps1 C:\app\scripts` to Dockerfile → scripts marked with ZoneId=3 (downloaded)
- Bypass via `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass`

**PowerShell editions:**
- Windows: PowerShell 5.1 (built-in, deprecated) vs. pwsh 7+ (modern, recommended)
- PoshMcp uses pwsh CLI exclusively (via Process.Start); compatible with 7.x

**Specific modules affected by Windows platform code:**
- `ActiveDirectory` — LDAP protocol; cross-platform via pwsh 7+, but better as WinRM call
- `DnsClient` — Win32 API; Windows-only
- `GroupPolicy` — ADSI/WMI; Windows-only
- `Hyper-V` — Windows-only
- `ScheduledTasks` — Windows Task Scheduler; no Unix equivalent

---

### Linux-Specific Concerns

**File permissions & sudo elevation:**
- PowerShell runs in user context; if subprocess needs to modify `/etc`, it requires sudo
- Subprocess will prompt for password → blocks waiting on stdin → timeout
- **Mitigation:** Use `sudo` with `-n` (non-interactive) or configure `/etc/sudoers` with `NOPASSWD`
- PoshMcp subprocess uses `-NonInteractive` flag, which blocks password prompts (safe behavior, fails loudly)

**Module availability:**
- Most PowerShell Gallery modules work on Linux
- Fewer modules pre-installed in linux container images
- Recommend: explicitly list required modules in `appsettings.json` or Dockerfile `ENV INSTALL_PS_MODULES`

**Container runtime:**
- If running PoshMcp in container (`docker run`), subprocess inherits container isolation
- No `pwsh` in lightweight scratch images; use `powershell:latest` base image

**Package management differences:**
- Fedora/RHEL: `dnf`, `rpm`
- Debian/Ubuntu: `apt`, `dpkg`
- Alpine: `apk` (often no pwsh available; use Ubuntu base for PoshMcp)

---

### macOS-Specific Concerns

**Code signing & Gatekeeper:**
- Downloaded pwsh binary must be code-signed (Microsoft signs official releases)
- Gatekeeper may quarantine on first run; `xattr -d com.apple.quarantine $(which pwsh)` to remove
- PoshMcp subprocess startup may hang 5-10s on first subprocess spawn if Gatekeeper is involved
- **Mitigation:** Pre-run `pwsh -NoProfile -Command "exit"` in Dockerfile or startup
- **Cross-platform note:** This is macOS-only; no equivalent on Windows/Linux

**Homebrew installation:**
- Standard path: `/usr/local/bin/pwsh` or `/opt/homebrew/bin/pwsh` (Apple Silicon)
- PATH must include Homebrew bin; container base image might not
- PoshMcp subprocess uses `FileName="pwsh"`, relies on PATH; may not find it
- **Mitigation:** `export PATH="/opt/homebrew/bin:$PATH"` in container startup

**M1/M2 (Apple Silicon) vs. Intel:**
- pwsh now has native ARM64 builds
- Container architectures matter: `docker buildx build --platform linux/arm64` for M1
- x86_64 binaries work via Rosetta 2, but slower; native ARM64 preferred

---

### Unified Strategy (Windows, Linux, macOS)

**Recommended approach for cross-platform subprocess support:**

```csharp
public class CrossPlatformPowerShellSubprocess
{
    public static ProcessStartInfo CreateValidationProcessInfo(string moduleName)
    {
        var psi = new ProcessStartInfo
        {
            FileName = GetPowerShellExecutable(),
            Arguments = $"-NonInteractive -NoProfile -Command \"Import-Module '{EscapeForShell(moduleName)}' -ErrorAction Stop; exit 0\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };

        // Add cross-platform environment setup
        // Remove any profile-loading env vars
        psi.EnvironmentVariables.Remove("POWERSHELL_TELEMETRY_OPTOUT");
        
        // Pass module paths if configured
        if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("POSHMCP_MODULE_PATH")))
        {
            psi.EnvironmentVariables["POSHMCP_MODULE_PATH"] =
                System.Environment.GetEnvironmentVariable("POSHMCP_MODULE_PATH")!;
        }

        return psi;
    }

    private static string GetPowerShellExecutable()
    {
        // Always use pwsh (PowerShell Core 7+)
        // Platform-agnostic: pwsh on all platforms
        return "pwsh";
    }

    private static string EscapeForShell(string text)
    {
        // PowerShell escaping: single quote → two single quotes
        return text.Replace("'", "''");
    }
}
```

**Container base image strategy:**

```dockerfile
# Dockerfile.multi-platform
FROM mcr.microsoft.com/powershell:latest

WORKDIR /app

# Install .NET 10 SDK/runtime (for PoshMcp)
# (Already in above base image, but could be explicit)

# On macOS (Apple Silicon): explicitly opt for ARM64
# docker buildx build --platform linux/arm64 -t poshmcp .

# On Linux: any platform works; prefer lightweight distro
# Already using powershell:latest base (Ubuntu-based)

COPY install-modules.ps1 /tmp/
ENV INSTALL_PS_MODULES="Az.Accounts Pester"
RUN pwsh /tmp/install-modules.ps1

COPY PoshMcp.Server/bin/Release/net10.0 /app/server
ENTRYPOINT ["/app/server/poshmcp", "serve", "--transport", "stdio"]
```

---

## 6. Real Module Examples

### Modules Known to Fail In-Process

**Investigation summary:**

PoshMcp's existing `ValidateModuleInChildProcess` function already handles this by **preventing import if subprocess validation fails**. No documented "known bad modules" list in the repo, but the pattern allows discovery.

**When a module would fail in-process (pattern recognition):**

1. **Module that spawns subprocesses that hang:**
   - Example: Some Azure AD module versions fork child processes during initialization
   - **Fail mode:** In-process import hangs parent runspace (no timeout)
   - **In subprocess:** Timeout (30s) kills the child process group → clean exit
   - **Example module:** `AzureAD` (older versions, Microsoft now recommends Graph API instead)

2. **Module with unresolved Windows 7 deprecated APIs:**
   - Example: Older `WebAdministration` module on newer Windows
   - **Fail mode:** Import raises MissingMethodException (API removed)
   - **Platform:** Windows-only (not relevant on Linux/macOS)
   - **In subprocess:** Subprocess crashes; parent unaffected

3. **Module with bundled DLL conflicts:**
   - Example: Vendor module ships Newtonsoft.Json 11.0; PoshMcp has 13.0
   - **Fail mode:** Calls to newer API fail; method not found
   - **In subprocess:** Subprocess loads module's DLL in isolation
   - **Real-world:** AWS Tools for PowerShell (Older versions had this)

**Failing module discovery method (programmatic):**

```powershell
# This is what ValidateModuleInChildProcess does
# Run in a subprocess with 30s timeout
pwsh -NonInteractive -NoProfile -Command @"
    try {
        Import-Module 'ProblematicModule' -ErrorAction Stop
        exit 0
    } catch {
        Write-Error $_
        exit 1
    }
"@
```

**Exit codes tell the story:**
- `0`: Module loaded successfully
- `1`: PowerShell error (normal; module didn't load)
- `0xC0000005`: Access violation (module or dependency corrupt)
- `143` (SIGTERM on Linux/macOS): Timeout kill (module initialization hung)

---

### How Out-of-Process Solves Them

**For each failure mode:**

| Failure Mode | In-Process Result | Out-of-Process Result | Mitigation |
|---|---|---|---|
| **Module hangs parent runspace** | MCP server unresponsive | Subprocess killed after 30s; parent continues | Mark module "do not load in-process"; use subprocess pool |
| **Unresolved API** | Tool creation fails; cascading failures | Subprocess exits with error; parent stable | Exclude module from MCP tools; document platform limitation |
| **DLL conflict** | Module method fails at runtime | Subprocess loads module in clean AppDomain | Use subprocess pool for module; pay startup cost |
| **Subprocess fork deadlock** | (No issue; in-process only) | Subprocess deadlocked child reaped on WaitForExit timeout | Works; timeout protection handles it |

---

### Modules That Won't Benefit

**Out-of-process doesn't solve:**

1. **Registry-based Windows issues**
   - Subprocess runs in same registry context as parent
   - Windows-only; no out-of-process fix
   - **Solution:** Use REST/CIM APIs instead of WMI

2. **Host-level elevation requirements**
   - If a command needs admin privileges, subprocess also needs them
   - Parent process elevation doesn't transfer to child on Windows
   - **Solution:** Run entire PoshMcp server as admin (not recommended); use `runas` subprocess (complex)

3. **Network/authentication timeouts**
   - If `Get-AzResource` hangs waiting for Azure to respond, out-of-process doesn't fix it
   - Subprocess would hang just as much
   - **Solution:** Configure `-TimeoutSec` parameter (if cmdlet supports it)

---

## 7. PowerShell-Specific Recommendations

### Best Practice IPC Mechanism (Cross-Platform)

**Finding: Stdin/stdout JSON framing is simplest; TCP localhost is alternative. Sockets are not recommended (less portable across Windows/Linux/macOS).**

**Option A: Stdin/Stdout (Recommended)**
- **Used by:** Model Context Protocol (MCP) stdio standard
- **Mechanism:** Process.StandardInput/StandardOutput with UTF-8 encoding
- **MCP framing:** `Content-Length: N\r\n\r\n` header followed by JSON
- **Pros:** No extra ports; works in containers; matches MCP spec
- **Cons:** Requires careful buffer management (no partial reads)
- **Cross-platform:** Identical on Windows/Linux/macOS (stdio is universal)

**Option B: TCP Localhost**
- **Mechanism:** Process starts listening on 127.0.0.1:random_port; parent connects
- **Pros:** Built-in TCP libraries; easier async/streaming
- **Cons:** Port conflict possible; requires firewall args; more complex setup
- **Cross-platform:** Identical on Windows/Linux/macOS
- **Use case:** If you need bidirectional streaming or persistent subprocess connection

**Option C: Unix Domain Sockets (Linux/macOS only)**
- **Mechanism:** Subprocess creates socket at `/tmp/poshmcp-XXXXX.sock`
- **Pros:** Efficient; no port binding
- **Cons:** Windows doesn't support; adds platform-specific code
- **Recommendation:** Skip for cross-platform; stick with Option A or B

**For PoshMcp out-of-process addition:**

Use **Stdin/Stdout + MCP framing** (already used for HTTP transport). Minimal changes:
1. Parent spawns `pwsh -NonInteractive -Command <script>`
2. Parent writes JSON request to stdin
3. Subprocess reads stdin, executes command, writes JSON response to stdout
4. Parent reads stdout, parses JSON
5. Process exits; cleanup

```csharp
var psi = new ProcessStartInfo
{
    FileName = "pwsh",
    Arguments = "-NonInteractive -NoProfile -Command -",
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
    UseShellExecute = false,
    StandardOutputEncoding = new UTF8Encoding(false)
};

using var process = Process.Start(psi);
string script = $@"
    Import-Module '{module}' -ErrorAction Stop
    {command} | ConvertTo-Json
";

process.StandardInput.WriteLine(script);
process.StandardInput.Close(); // Signal EOF

string json = process.StandardOutput.ReadToEnd();
process.WaitForExit(30_000);
```

---

### Module Configuration Strategy

**Finding: Configuration should explicitly enumerate which modules are "safe in-process" vs. "require subprocess isolation". This is a per-organization decision.**

**Recommended configuration schema:**

```json
{
  "PowerShellConfiguration": {
    "Modules": [
      {
        "Name": "Az.Accounts",
        "IsolationLevel": "InProcess",
        "ValidationRequired": false,
        "Reason": "Safe; no conflicting types"
      },
      {
        "Name": "GroupPolicy",
        "IsolationLevel": "OutOfProcessOnly",
        "ValidationRequired": false,
        "PlatformFilter": "Windows",
        "Reason": "Windows-only; WMI-based"
      },
      {
        "Name": "MyVendorModule",
        "IsolationLevel": "OutOfProcessPool",
        "ValidationRequired": true,
        "SubprocessTTL": 300,
        "Reason": "Untrusted; crashes child on import; reuse subprocess for 5 min"
      }
    ],
    "OutOfProcessSubprocessPoolConfig": {
      "MaxPoolSize": 10,
      "IdleTimeout": 300,
      "SpawnTimeout": 30000,
      "ValidationTimeout": 30000,
      "MaxConcurrentSpawns": 3
    }
  }
}
```

**Decision criteria per module:**

```
1. Is the module Windows-only?
   → Mark "OutOfProcessOnly" (for Windows users); exclude from Linux/macOS
2. Does subprocess validation fail (timeout, crash)?
   → Mark "OutOfProcessPool"; use subprocess isolation
3. Is the module from trusted source (Microsoft, established vendor)?
   → Mark "InProcess"; load normally
4. Is the module custom or third-party with unknown quality?
   → Mark "OutOfProcessPool"; safer approach
```

---

### Recommended Execution Model (Cross-Platform)

**Finding: Hybrid model recommended — in-process for safe modules, subprocess pool for risky. Addresses safety without prohibitive startup cost.**

**Execution flow (pseudocode):**

```csharp
public async Task<McpToolResponse> ExecuteToolAsync(McpToolCall call)
{
    var toolName = call.ToolName;
    var moduleName = GetModuleForTool(toolName);
    
    var moduleConfig = _config.Modules.FirstOrDefault(m => m.Name == moduleName);
    
    if (moduleConfig?.IsolationLevel == "OutOfProcessOnly")
    {
        return await ExecuteInSubprocessPool(toolName, call.Arguments, moduleName);
    }
    else if (moduleConfig?.IsolationLevel == "OutOfProcessPool")
    {
        return await ExecuteInSubprocessPool(toolName, call.Arguments, moduleName);
    }
    else // InProcess
    {
        // Current behavior: execute in singleton runspace
        return ExecuteInRunspace(toolName, call.Arguments, moduleName);
    }
}

private async Task<McpToolResponse> ExecuteInSubprocessPool(string toolName, Dictionary<string, object> args, string module)
{
    // Reuse subprocess if available (< 5 min idle)
    var subprocess = _subprocessPool.GetOrCreate(module);
    
    // Send request via stdin
    var request = new { tool = toolName, args };
    subprocess.StandardInput.WriteLine(JsonConvert.SerializeObject(request));
    
    // Receive response from stdout
    string jsonResponse = subprocess.StandardOutput.ReadLine();
    
    // Parse and return
    return JsonConvert.DeserializeObject<McpToolResponse>(jsonResponse);
}
```

**Platform tuning:**

- **Windows:** Subprocess pool size = 2-3 (heavier process); startup cost high
- **Linux:** Subprocess pool size = 5-10 (lighter); startup faster
- **macOS:** Subprocess pool size = 2-3 (Gatekeeper adds delay on launch)

---

### Data Passing Protocol (Recommended Wire Format)

**Finding: MCP JSON-RPC over stdin/stdout already proven; use it. For subprocess IPC, minimal wrapper needed.**

**MCP-compliant request format:**

```json
{
  "jsonrpc": "2.0",
  "id": "1",
  "method": "tools/call",
  "params": {
    "name": "get_service",
    "arguments": {
      "Name": "dhcp"
    }
  }
}
```

**MCP-compliant response format:**

```json
{
  "jsonrpc": "2.0",
  "id": "1",
  "result": {
    "content": [
      {
        "type": "text",
        "text": "[{\"name\":\"dhcp\",\"state\":\"Running\"}]"
      }
    ]
  }
}
```

**For subprocess-to-parent IPC (simplified, not full MCP):**

```json
{
  "status": "ok|error",
  "result": [{ "name": "dhcp", "state": "Running" }],
  "error": null
}
```

**Encoding:** UTF-8, no BOM. Framing: `Content-Length: N\r\n\r\n` (MCP standard).

---

## 8. Known Limitations & Gotchas (Cross-Platform)

### What Can Still Go Wrong Even in Out-of-Process Mode

**Finding: Out-of-process solves module isolation but introduces new challenges. Plan accordingly.**

1. **Network timeouts masked by subprocess timeout:**
   - **Scenario:** `Get-AzResource` makes HTTP call to Azure (times out after 60s); parent subprocess timeout is 30s
   - **Result:** Subprocess killed before Azure times out; parent sees process timeout, not Azure timeout
   - **Mitigation:** Set subprocess timeout > command timeout; document expected duration per tool
   - **Cross-platform:** Identical issue on all platforms

2. **Zombie processes on Linux/macOS if parent crashes:**
   - **Scenario:** PoshMcp server crashes; subprocess processes not reaped
   - **Result:** `pwsh` processes remain until systemd reaps them (system-dependent)
   - **Mitigation:** Use PoshMcp's TestProcessRegistry pattern; register all spawned processes on startup
   - **Cross-platform:** Linux/macOS specific (Windows auto-reaps on parent exit)

3. **Subprocess module state leaks between invocations:**
   - **Scenario:** Module A modifies `$env:PSModulePath`; next command in same subprocess sees modified path
   - **Result:** Unexpected behavior if not documented
   - **Mitigation:** Accept this as stateful subprocess behavior; document or reset environment per call
   - **Cross-platform:** Affects all platforms equally

4. **Encoding/BOM surprises if not careful:**
   - **Scenario:** Subprocess outputs UTF-8 with BOM; parent parser chokes
   - **Result:** `"Unexpected character encountered while parsing value: EF BB BF"`
   - **Mitigation:** Explicit UTF8Encoding(false) on parent reader; explicit output encoding in subprocess
   - **Cross-platform:** More common on Windows (BOM output); test all platforms

5. **Working directory inherited by subprocess:**
   - **Scenario:** Parent is in `/app/server`; subprocess starts in same directory
   - **Result:** Relative paths in subprocess commands behave unexpectedly
   - **Mitigation:** Always use absolute paths in MCP tool arguments; document this in tooling guide
   - **Cross-platform:** Path separator difference (\ vs /); PowerShell 7+ normalizes, but test

6. **Module import order matters if sharing state:**
   - **Scenario:** Module A depends on Module B being imported first; if subprocess imports A before B, it fails
   - **Result:** Out-of-process execution fails, but in-process works (B already loaded)
   - **Mitigation:** Explicit dependency ordering in subprocess initialization; document module dependencies
   - **Cross-platform:** Same on all platforms

7. **Credential passing across subprocess boundary:**
   - **Scenario:** Parent has `$cred = Get-Credential`; wants to pass to subprocess
   - **Result:** Cannot serialize PSCredential (SecureString not JSON-serializable)
   - **Mitigation:** Pass credentials separately (e.g., env vars for token; separate auth mechanism); document limitation
   - **Cross-platform:** Affects all platforms equally

8. **Platform-specific cmdlet availability causes silent failures:**
   - **Scenario:** Linux user tries to call `Get-GPOStatus` tool; tool is unavailable
   - **Result:** Tool not exposed in MCP list → AI doesn't attempt call (correct)
   - **Gotcha:** If you dynamically load modules per-session, Linux user might see different tool set than team documented
   - **Mitigation:** Document which tools are Windows-only in all MCP metadata and docs
   - **Cross-platform:** Critical for multi-platform deployments

---

## Summary & Recommendations

### Key Findings

1. **Subprocess hosting is feasible and proven:** PoshMcp already uses `ValidateModuleInChildProcess` successfully. Scaling to command execution is straightforward.

2. **Cross-platform support is achievable:** pwsh is uniformly available; stdin/stdout is platform-agnostic; only module availability differs by OS.

3. **Serialization is safe:** Current PowerShellObjectSerializer + System.Text.Json works cross-platform with minor UTF-8 encoding care.

4. **Hybrid strategy is optimal:** In-process for safe modules; subprocess pool for risky ones. Balances performance with resilience.

5. **No platform-specific IPC needed:** Use MCP stdin/stdout framing; works on Windows, Linux, macOS identically.

### Recommended Next Steps (Phase 2)

1. **Configuration schema extension** (1 day)
   - Add `IsolationLevel` field to module config
   - Add subprocess pool configuration

2. **Subprocess pool implementation** (3-5 days)
   - Extend ProcessStartInfo to support module execution
   - Implement subprocess reuse + TTL-based recycling
   - Add logging and metrics

3. **End-to-end cross-platform testing** (2-3 days)
   - Windows: Test with problematic modules (GroupPolicy, Azure AD)
   - Linux: Test with Az modules, verify no module-not-found errors
   - macOS: Test with native M1 subprocess spawn

4. **Documentation** (1 day)
   - Document which modules require out-of-process isolation
   - Update deployment guides for multi-platform
   - Add runbook for diagnosing subprocess failures

### Risk Mitigation

- **Start with validation only:** Don't change execution yet; extend existing `ValidateModuleInChildProcess` to gather data on which modules actually fail
- **Opt-in per module:** Don't force all modules out-of-process; let operators decide
- **Gradual rollout:** Pilot with known-safe modules (Az.Accounts); upgrade to risky modules only after validation



# Leela Documentation Decision: Out-of-Process Runtime Documentation

**Author:** Leela (Developer Advocate)
**Date:** 2026-04-10
**Status:** Implemented

## Decision

Document out-of-process PowerShell hosting as a supported, optional feature with clear guidance on when to use it, how to configure it, and what the trade-offs are. Explicitly clarify that `integration/Modules` is a local test-asset corpus, not production content.

## Rationale

The out-of-process runtime capability is implemented and wired in Program.cs with CLI and environment variable support, but was not clearly documented for end users. This creates:

- **Discovery gap:** Users don't know out-of-process mode exists or when to use it
- **Configuration confusion:** How to enable it is unclear (CLI flag? env var? appsettings.json?)
- **Test corpus confusion:** `integration/Modules` looks like it could be a feature (it's in examples/) but should only be used locally

Creating comprehensive, focused documentation addresses these gaps and enables:
- Users to self-serve when they have module compatibility issues
- Clear configuration patterns for different deployment scenarios (local dev, containers, Azure)
- Explicit boundaries between test assets and production content

## Implementation

### New Documentation

**`docs/OUT-OF-PROCESS.md`** — Comprehensive 500+ line guide covering:
- When to use out-of-process (module conflicts, type pollution, platform-specific issues)
- Architecture comparison (in-process vs out-of-process)
- Configuration methods (appsettings.json, CLI flag, environment variable, priority order)
- Usage patterns (local dev, containers, Azure Container Apps)
- Troubleshooting (subprocess failures, module loading, latency, memory)
- Performance characteristics with benchmarks
- Integration test workflow with local corpus
- Limitations and known issues
- Best practices

### Updated Documentation

1. **`README.md`**
   - Added reference to OUT-OF-PROCESS.md in documentation section
   - Added "Out-of-Process PowerShell Runtime (Advanced)" quick-reference section with quick start, trade-offs table, and link to full documentation

2. **`examples/README.md`**
   - Clarified `appsettings.outofprocess.integration-modules.json` is **local testing only**
   - Added prominent warning about integration test corpus scope
   - Moved appsettings example documentation above Dockerfile section for clarity

3. **`integration/README.md`**
   - Completely restructured with explicit scope boundaries
   - Clear distinction between what `integration/Modules` IS and is NOT
   - When/when-not-to-use guidance
   - Local development workflow examples
   - Maintenance and refactoring guidelines
   - References to related documentation

4. **`docs/ENVIRONMENT-CUSTOMIZATION.md`**
   - Added "Before You Start" section with references to OUT-OF-PROCESS.md and production deployment patterns
   - Links to help users make informed choices about runtime mode and module installation

## Trade-Offs

**Accuracy vs Brevity:**
- OUT-OF-PROCESS.md is comprehensive (500+ lines) but focused only on that feature
- Keeps a clear one-concern-per-file pattern
- Users can reference the README.md quick start, then dive into OUT-OF-PROCESS.md for details

**Discovery:**
- Out-of-process is an "advanced" feature (noted in README section header)
- Appropriate because most users won't need it; available documentation links for those who do

**Examples:**
- `appsettings.outofprocess.integration-modules.json` remains in examples/ for backward compatibility
- Now clearly marked as local-testing-only to prevent misuse
- Production configurations should use PowerShell Gallery or pre-built container images

## Verification

Documentation is:
- ✅ Consistent with OUT_OF_PROCESS_PLAN.md implementation roadmap
- ✅ Accurate to Program.cs CLI wiring (--runtime-mode, env vars, appsettings)
- ✅ Clear about unsupported behavior (mixed modes, per-module selection)
- ✅ Explicit about test vs product boundaries (integration/Modules)
- ✅ Includes actionable configuration examples
- ✅ References all related docs (ENVIRONMENT-CUSTOMIZATION, DOCKER, examples)

## User Impact

### Before
- Users with module conflicts had no guidance
- Out-of-process capability was undiscoverable
- integration/Modules appeared to be a feature

### After
- Users know out-of-process exists and when to use it
- Configuration is clear (three methods documented with examples)
- Test/product boundaries are explicit
- Clear path from README quick start → detailed OUT-OF-PROCESS.md → troubleshooting

## Next Steps

None. Documentation fully addresses out-of-process hosting scope and is ready for user consumption.

## Related

- `OUT_OF_PROCESS_PLAN.md` — Implementation design
- `Program.cs` — CLI wiring and runtime mode selection
- `.squad/agents/leela/history.md` — Learnings from this documentation work



## 2025-07-17

### Default build type for --generate-dockerfile

**Date:** 2025-07-17
**Author:** Bender (Backend Developer)
**Status:** Implemented

The poshmcp build command supports two image types: ase (builds from ./Dockerfile) and custom (uses xamples/Dockerfile.user). When --generate-dockerfile is used without an explicit --type, default to ase. When actual Docker build without --type, default to custom (existing behavior).

**Decision:** 
- --generate-dockerfile without --type → default to ase
- Actual build without --type → default to custom (unchanged)

**Result:** poshmcp build --generate-dockerfile works without errors. Primary workflow unaffected. Users wanting to generate custom Dockerfile must pass --type custom.

## Archived 2026-05-05 (older than 7 days)


### 2026-04-24: Release v0.8.4 Pushed
**By:** Amy (DevOps/Platform Engineer)
**Status:** Applied
**What:** Bumped version to 0.8.4, built poshmcp.0.8.4.nupkg, updated global install, committed (f5583fe), created and pushed annotated tag v0.8.4. Rebase required to resolve merge commit rejected by branch protection.
**Why:** Security patch release fixing CVE-2026-40894.
**Rule Going Forward:** Rebase onto origin/main before pushing to avoid merge commit rejections on protected branches.

### 2026-07-18: Canonical Infrastructure Defaults for PoshMcp Azure Deployment
**By:** Amy (DevOps / Platform / Azure Engineer)
**Status:** Applied
**What:** Aligned all deploy scripts to match canonical defaults defined in `main.bicep` and `parameters.json`. Updated `deploy.ps1`, `deploy.sh`, and `validate.ps1` to use `rg-poshmcp` instead of `poshmcp-rg` for resource group default.
**Why:** Bicep/parameters.json is source of truth; scripts using different defaults would deploy to wrong resource group. Running with script defaults would create duplicate resource group.
**Rule Going Forward:** Bicep and parameters.json are the authoritative sources for infrastructure defaults. Deploy scripts are wrappers — their defaults must mirror Bicep/parameters, not diverge.



### 2026-04-23 10:03: Implemented spec 007 - deploy.ps1 source image support
**By:** Amy
**What:** Added -SourceImage and -UseRegistryCache parameters to deploy.ps1; added Mode A (docker pull + re-tag), Mode B (az acr import), and Mode C (existing build) routing
**Why:** Steven requested feature to deploy pre-built images without local build

### 2026-04-23: Add session-recall skill
**By:** Steven Murawski (via Farnsworth)
**What:** session-recall CLI wired into coordinator startup behavior via .squad/skills/session-recall/SKILL.md
**Why:** Provides progressive session recall after crashes/compaction using installed CLI tool; preferred over raw SQL patterns

### 2026-07-28: Spec 007 - deploy.ps1 source image support

**By:** Farnsworth

**What:** Created spec 007 for `infrastructure/azure/deploy.ps1` source image and ACR pull-through cache support

**Why:** Steven requested feature to allow pulling pre-built container images instead of always building from Dockerfile locally. Enables faster deployments, artifact promotion workflows, and bandwidth optimization via ACR's pull-through cache for large images.

**Decision Points:**

1. **Parameter names and design**:
   - `-SourceImage` (string): Optional container image reference; when provided, suppresses local build
   - `-UseRegistryCache` (switch): Optional flag; requires `-SourceImage`; enables `az acr import` instead of local pull
   - Validation: `-UseRegistryCache` without `-SourceImage` is a usage error (exit code 2)

2. **Execution modes**:
   - **Mode A** (default when `-SourceImage` provided): Local pull + re-tag + push (`docker pull` → `docker tag` → `docker push`)
   - **Mode B** (when both flags provided): ACR import (`az acr import` directly into ACR, no local pull)
   - **Mode C** (backward compatibility): Build from Dockerfile (no changes to existing behavior)

3. **Retry and error handling**:
   - Reuse existing `Invoke-DockerPushWithRetry` logic for pull failures in Mode A
   - Implement similar retry for `az acr import` in Mode B (exponential backoff, transient error detection)
   - Clear, actionable error messages for each failure scenario

4. **Backward compatibility**:
   - When `-SourceImage` is not provided, script behavior is unchanged
   - All existing parameters and environment variables remain functional
   - No breaking changes to the public contract

5. **Image tagging**:
   - Source image re-tagged to `$RegistryServer/poshmcp:$ImageTag` and `$RegistryServer/poshmcp:latest`
   - Same tagging pattern as current `Build-AndPushImage` for consistency

**Spec location:** `specs/007-deploy-source-image/spec.md`

**Next steps:** Triage into GitHub issues and assign to implementation agent (Bender recommended for Azure/Docker CLI expertise).

### 2026-07-28: Test cases for spec 007
**By:** Fry
**What:** Wrote manual test checklist for deploy.ps1 source image feature
**Why:** Need verification procedures for the three execution modes and error cases


### 2026-04-20T21:20:00Z: User directive
**By:** Steven Murawski (via Copilot)
**What:** Release notes must always be added to the docs TOC when a new release notes file is created.
**Why:** User request — captured for team memory after v0.8.0 release notes were not wired up in the TOC


### 2026-07-28: Spec 007 - deploy.ps1 source image support

**By:** Farnsworth

**What:** Created spec 007 for `infrastructure/azure/deploy.ps1` source image and ACR pull-through cache support

**Why:** Steven requested feature to allow pulling pre-built container images instead of always building from Dockerfile locally. Enables faster deployments, artifact promotion workflows, and bandwidth optimization via ACR's pull-through cache for large images.

**Decision Points:**

1. **Parameter names and design**:
   - `-SourceImage` (string): Optional container image reference; when provided, suppresses local build
   - `-UseRegistryCache` (switch): Optional flag; requires `-SourceImage`; enables `az acr import` instead of local pull
   - Validation: `-UseRegistryCache` without `-SourceImage` is a usage error (exit code 2)

2. **Execution modes**:
   - **Mode A** (default when `-SourceImage` provided): Local pull + re-tag + push (`docker pull` → `docker tag` → `docker push`)
   - **Mode B** (when both flags provided): ACR import (`az acr import` directly into ACR, no local pull)
   - **Mode C** (backward compatibility): Build from Dockerfile (no changes to existing behavior)

3. **Retry and error handling**:
   - Reuse existing `Invoke-DockerPushWithRetry` logic for pull failures in Mode A
   - Implement similar retry for `az acr import` in Mode B (exponential backoff, transient error detection)
   - Clear, actionable error messages for each failure scenario

4. **Backward compatibility**:
   - When `-SourceImage` is not provided, script behavior is unchanged
   - All existing parameters and environment variables remain functional
   - No breaking changes to the public contract

5. **Image tagging**:
   - Source image re-tagged to `$RegistryServer/poshmcp:$ImageTag` and `$RegistryServer/poshmcp:latest`
   - Same tagging pattern as current `Build-AndPushImage` for consistency

**Spec location:** `specs/007-deploy-source-image/spec.md`

**Next steps:** Triage into GitHub issues and assign to implementation agent (Bender recommended for Azure/Docker CLI expertise).


### 2026-07-28: Test cases for spec 007
**By:** Fry
**What:** Wrote manual test checklist for deploy.ps1 source image feature
**Why:** Need verification procedures for the three execution modes and error cases


# Decision: Use `poshmcp build` in deploy.ps1 instead of `docker build`

**Date:** 2026-07-18
**Author:** Amy (DevOps/Platform)
**Context:** `infrastructure/azure/deploy.ps1` — `Build-AndPushImage` function

## Decision

The `Build-AndPushImage` function in the Azure deploy script now uses `poshmcp build --tag <image>` instead of calling `docker build` directly.

## Rationale

Steven requested that any image-building step in the deploy pipeline go through the `poshmcp build` CLI, which:
- Auto-detects docker vs podman
- Is the canonical build interface for this project
- Ensures consistent build behavior with the rest of the toolchain

## Implementation Detail

`poshmcp build` only supports a single `--tag` argument per invocation. The original `docker build` call applied both the versioned tag and `latest` in one pass (`-t $FullImageName -t $latestImage`). To keep a single build, we call `poshmcp build --tag $FullImageName` for the build step, then `docker tag $FullImageName $latestImage` to alias the result. The push logic is unchanged.

## Impact

- `Build-AndPushImage` no longer calls `docker build` directly.
- `docker tag` (not `docker build`) is used to apply the `latest` alias — this is acceptable because the restriction was specifically on the build operation.
- `poshmcp` must be installed as a dotnet global tool on the machine or agent running the deploy script.

### 2026-04-23T15:56:32-05:00: Deploy script config precedence and appsettings contract
**By:** Amy
**What:** Added appsettings-sourced deployment configuration to `infrastructure/azure/deploy.ps1` via `-AppSettingsFile` / `DEPLOY_APPSETTINGS_FILE`, with explicit precedence `CLI > env > appsettings > defaults`. Introduced `AzureDeployment` appsettings section (also supports `Deployment.Azure`) and added `infrastructure/azure/deploy.appsettings.json.template` as scaffold-ready template.
**Why:** Preserve existing deploy workflow while enabling repeatable environment-specific deployment configuration from file, especially for CI/bootstrap scaffolds.

### 2026-04-23T16:05:12Z: Add CLI scaffold command backed by embedded infra artifacts
**By:** Bender
**What:** Added `poshmcp scaffold` to materialize an `infra/azure` folder from assembly-embedded deployment files (`deploy.ps1`, bicep files, and parameters) into a target project directory with optional `--force` overwrite behavior.
**Why:** Ensures scaffold works both from source and packaged tool installations without depending on repository-relative filesystem paths.

### 2026-04-23T17:28:05Z: server appsettings to Container App env vars
**By:** Amy (DevOps/Platform) - requested by Steven Murawski
**What:** Translate MCP server appsettings.json into Container App environment variables via deploy.ps1.
**Decisions:**
- Removed `powerShellFunctions` Bicep param from `resources.bicep` and `main.bicep`. Covered by translated env var array.
- Removed `enableDynamicReloadTools` Bicep param; its env var is now emitted from server appsettings translation.
- Renamed Bicep `extraEnvVars` param to `serverEnvVars`.
- Renamed `-McpAppSettingsFile` param in `deploy.ps1` to `-ServerAppSettingsFile`; added `POSHMCP_APPSETTINGS_FILE` env var support; kept auto-discovery.
- Added translations for IncludePatterns, ExcludePatterns, EnableConfigurationTroubleshootingTool, and Logging.LogLevel.Default.
- Fixed RuntimeMode normalization: server expects "InProcess"/"OutOfProcess". deploy.ps1 previously emitted "in-process"/"out-of-process" - corrected.
**Why:** Single source of truth - container configured identically to local server from one appsettings file.

### 2026-04-24: Version bump to 0.8.3 with release metadata alignment
**By:** Amy
**What:** Chose a patch release bump from `0.8.2` to `0.8.3`, aligned version-bearing artifacts by updating `PoshMcp.Server/PoshMcp.csproj`, and ensured release notes index coverage by adding `docs/release-notes/0.8.3.md` into `docs/toc.yml`.
**Why:** Patch bump is the safest default when no target version is specified, and team convention requires release-notes TOC alignment whenever a new release notes file is added.
**Operational Outcome:** Build and pack completed successfully and produced `artifacts/nupkg/poshmcp.0.8.3.nupkg`.
**Merged from inbox:** `.squad/decisions/inbox/amy-version-bump-pack-update.md`



# Decision: -GenerateDockerfile switch for docker.ps1

**Date:** 2026-07-28
**By:** Amy (DevOps/Platform/Azure)
**Status:** Applied

## What

Added `-GenerateDockerfile` [switch] and `-OutputPath` [string] parameters to `docker.ps1`.

## Decisions Made

1. **`-OutputPath` has no default in `param()`** — default is computed dynamically inside each command block:
   - Base build: `./Dockerfile.generated`
   - Custom build: `./Dockerfile.<Template>.generated`
   This avoids a static default that would be wrong for the `build-custom` case.

2. **Feature scope** — `-GenerateDockerfile` is only meaningful for `build`/`build-base` and `build-custom`. It is silently ignored for `run`, `stop`, `logs`, and `clean` (the switch is simply not tested in those branches). No warning emitted — the existing command still executes normally.

3. **Header format** — Includes `# Generated by PoshMcp docker.ps1`, the equivalent `docker build` command, an ISO 8601 timestamp, and a copy-paste-ready build command referencing the output file. Azure template appends an env-var note.

4. **Source content** — The generated file is header + verbatim source Dockerfile content. No mutations to the Dockerfile itself.

5. **`Set-Content -NoNewline`** — Used to avoid appending a spurious trailing newline that `Set-Content` adds by default. Content from `Get-Content -Raw` already contains the file's original line endings.

## Why

Provides a documented, archivable snapshot of the exact Dockerfile used for any build invocation, useful for audit trails, CI artifact storage, and debugging build regressions without re-running the full build.

## Files Changed

- `docker.ps1` — new parameters, updated help block, build command logic


# Decision: poshmcp build --generate-dockerfile

**Date:** 2026-07-28
**By:** Amy (DevOps/Platform)
**Status:** Applied

## What

Added `--generate-dockerfile` and `--dockerfile-output` options to `poshmcp build`.

## Decision Points

1. **New CLI options:**
   - `--generate-dockerfile` (bool/switch): when set, write the Dockerfile to disk and exit; do not invoke docker/podman.
   - `--dockerfile-output` (string, optional): destination path; default `./Dockerfile.generated`.

2. **Dockerfile header format:**
   - `# Generated by poshmcp build`
   - `# Equivalent build command: docker build -f <output-path> -t <image-tag> [--build-arg ...] .`
   - `# Generated: <ISO 8601 UTC timestamp>`
   - Blank line separator before the actual Dockerfile content.

3. **Implementation split:**
   - `DockerRunner.GenerateDockerfile(...)` in `Cli/DockerRunner.cs` owns file I/O and header construction.
   - `Program.cs` handler owns option parsing and console output (success message + manual build hint).

4. **Handler pattern change:**
   - Switched the `buildCommand.SetHandler` from the typed-parameter overload to `InvocationContext`-based pattern to accommodate 8 options without hitting System.CommandLine overload limits.

5. **No docker/podman detection when `--generate-dockerfile` is set:**
   - The flag check and early-return happen *before* `DetectDockerCommand()` is called, so the CLI works even in environments without docker/podman installed.

## Rule Going Forward

When adding more than ~6 options to a `System.CommandLine` command handler, use the `InvocationContext`-based `SetHandler` pattern instead of the typed-parameter overload.


# Decision: appsettings bundling uses COPY injection rather than build-arg

**Author:** Bender
**Date:** 2026-05-01

## Decision

`poshmcp build --appsettings` bundles the supplied file into the image by injecting a
`COPY poshmcp-appsettings.json /app/server/appsettings.json` line into the Dockerfile, not via
`--build-arg`.

## Rationale

Using `COPY` is the correct Docker pattern for bundling files into an image:
- `--build-arg` is for scalar configuration values, not file contents.
- Embedding file content in a build-arg would require encoding, hit size limits, and make the
  Dockerfile comment unreadable.
- `COPY` is transparent, auditable, and idiomatic — the resulting Dockerfile is self-documenting.

## Implementation

- **Generate mode:** `GenerateDockerfile()` replaces/injects the `COPY` line in the Dockerfile content.
- **Build mode:** the appsettings file is staged as `poshmcp-appsettings.json` in CWD (the Docker
  build context), a temp Dockerfile (`.poshmcp-build.dockerfile`) is generated with the injected
  `COPY` line, the build runs, and both temp files are cleaned up in a `finally` block.


### 2026-04-24: Bundle install-modules.ps1 in base image
**Decision:** Copy install-modules.ps1 into the base container image at /app/install-modules.ps1
**Why:** Generated Dockerfiles (poshmcp build --generate-dockerfile) are used in repos that don't have this script locally. Bundling it eliminates the COPY dependency.


# Decision: Embed Dockerfiles in PoshMcp Assembly

**Date:** 2026-07-30
**Author:** Bender (Backend Developer)
**Requested by:** Steven Murawski

## Context

`poshmcp build --generate-dockerfile` reads Dockerfile templates from disk at runtime.
When the CLI is installed as a global dotnet tool via `dotnet tool install`, those files
do not exist on the user's machine — only the packed NuGet `.nupkg` payload is present.
This caused `Error: Dockerfile not found at examples/Dockerfile.user` for tool users.

## Decision

Embed the four Dockerfile templates directly in the `PoshMcp` assembly as `EmbeddedResource`
items in `PoshMcp.Server/PoshMcp.csproj`:

- `Dockerfile` (root) → manifest name `PoshMcp.Dockerfiles.Dockerfile`
- `examples/Dockerfile.user` → `PoshMcp.Dockerfiles.Dockerfile.user`
- `examples/Dockerfile.azure` → `PoshMcp.Dockerfiles.Dockerfile.azure`
- `examples/Dockerfile.custom` → `PoshMcp.Dockerfiles.Dockerfile.custom`

`DockerRunner.ReadEmbeddedDockerfile(name)` reads from the assembly manifest stream.
`DockerRunner.GenerateDockerfile(...)` tries embedded first, falls back to disk so local
dev workflows are unaffected.

`Program.cs` build handler: the `File.Exists(imageFile)` guard is now skipped when
`--generate-dockerfile` is active (the source doesn't need to be on disk).

## Consequences

- `poshmcp build --generate-dockerfile` works correctly after `dotnet tool install`.
- Local development (running from source) continues to work via the disk fallback.
- Dockerfile content stays in sync with the assembly version — no runtime drift.
- Four Dockerfiles add negligible size to the assembly (~4 KB total).


# Decision: `--generate-dockerfile` always defaults to `buildType = "custom"`

**Date:** current session
**Author:** Bender (Backend Dev)
**Requested by:** Steven Murawski

## Context

`poshmcp build --generate-dockerfile` is a user-facing command for generating a starter Dockerfile
that the user can customize and use to build their own container on top of the published PoshMcp
base image (`ghcr.io/usepowershell/poshmcp/poshmcp:latest`).

The previous logic branched the default `buildType` on whether `--generate-dockerfile` was active:

```csharp
var buildType = string.IsNullOrWhiteSpace(type)
    ? (generateDockerfile ? "base" : "custom")
    : type.ToLowerInvariant();
```

This caused `--generate-dockerfile` (with no `--type`) to default to `"base"`, which maps to the
root `Dockerfile` — the file for building PoshMcp itself from source. That is wrong for users.

## Decision

Always default to `"custom"` when `--type` is not supplied:

```csharp
var buildType = string.IsNullOrWhiteSpace(type)
    ? "custom"
    : type.ToLowerInvariant();
```

`"custom"` maps to `examples/Dockerfile.user`, which is the correct user-deployment template.
Users who need the source-build Dockerfile can explicitly pass `--type base`.

## Consequences

- `poshmcp build --generate-dockerfile` now emits `examples/Dockerfile.user` content by default ✅
- `poshmcp build` (no flags) is unchanged — still defaults to `"custom"` / `examples/Dockerfile.user` ✅
- `poshmcp build --type base --generate-dockerfile` still works for maintainers who want the source Dockerfile ✅


### 2026-04-24: User directive — git fetch/rebase workflow
**By:** Steven Murawski (via Copilot)
**What:** Always use `git fetch origin main` followed by `git rebase origin/main` to sync with remote before pushing. Never use merge pulls (`git pull` without `--rebase`).
**Why:** User preference — avoids stray merge commits that can trigger branch protection rejections.


### 2026-04-25: Application Insights logging spec created
**By:** Farnsworth (via Steven Murawski)
**What:** Spec at specs/application-insights-logging.md. Proposes opt-in App Insights via appsettings using Azure.Monitor.OpenTelemetry.AspNetCore. Targets post-0.8.11.
**Why:** Users running PoshMcp in Azure need logs/traces in App Insights without breaking existing logging.



# Decision: ConfigureApplicationInsights Implementation Choices

**Author**: Bender
**Date**: 2026-04-27
**Issue**: #172

## Decision 1: Use `Console.Error.WriteLine` for startup logs

The spec says "use the existing logging infrastructure" but the method signature `(IServiceCollection, IConfiguration, bool)` provides no `ILogger`. Rather than widening the signature (which would deviate from FR-307), startup messages are written to `Console.Error` — consistent with other early-startup log sites in `Program.cs` that precede host construction.

## Decision 2: Call site placement — after existing OpenTelemetry wiring

`ConfigureApplicationInsights` is called immediately after `ConfigureOpenTelemetryForHttp` (HTTP) and `ConfigureOpenTelemetry` (stdio). This ensures the existing OTel pipeline (McpMetrics, console exporter) is already registered before Azure Monitor is layered on, so FR-317 (McpMetrics flow through Azure Monitor) is satisfied without special ordering logic.

## Decision 3: Transport mode as OpenTelemetry resource attribute

FR-309 requires transport mode as a custom dimension. Implemented via `.ConfigureResource(resource => resource.AddAttributes(...))` on the `OpenTelemetryBuilder`. Resource attributes appear as custom dimensions in Azure Monitor and are set once at startup — zero per-request overhead.

## Decision 4: Clamp `SamplingPercentage` at runtime

`Math.Clamp(options.SamplingPercentage, 1, 100)` is applied before converting to ratio. This makes runtime behaviour predictable even with out-of-range config values. Doctor validation (future issue) will surface the out-of-range warning to users at config time.


---
## Archived 2026-05-06 (entries older than 2026-04-29)
### 2026-04-28: Doctor AppInsights validation architecture
**By:** Bender (Backend Dev)
**What:** Added a `ConfigurationErrors` list to `DoctorReport` (separate from `Warnings`) so `ComputeStatus` can distinguish error-level config issues from warnings. `BuildConfigurationWarnings` now returns a `(Warnings, Errors)` tuple and accepts the config path to load `ApplicationInsights` settings via `BuildRootConfiguration`. This keeps validation offline (no network calls per FR-315).
**Why:** Empty connection string with `Enabled: true` is a hard error (blocks telemetry), while a malformed format or out-of-range sampling is a softer warning. Keeping these separate preserves the existing `ComputeStatus` severity model.

### 2026-04-27: User directive
**By:** Steven Murawski (via Copilot)
**What:** Never merge main back into a branch. Feature branches must stay clean — no back-merges from main. Rebase if needed.
**Why:** User request — captured for team memory

### 2026-04-27T14:50:29Z: User directive
**By:** Steven Murawski (via Copilot)
**What:** Never merge main back into a branch. Feature branches should never have main merged into them.
**Why:** User request — captured for team memory

### 2026-04-27: User directive
**By:** Steven Murawski (via Copilot)
**What:** Always use rebase. When updating feature branches with upstream changes, use git rebase, never git merge.
**Why:** User request — captured for team memory

# PR #180 Review: ConfigureApplicationInsights() — REQUEST CHANGES

**Reviewer:** Farnsworth (Lead/Architect)
**Date:** 2026-04-28
**Branch:** squad/172-configure-app-insights
**Verdict:** REQUEST CHANGES

---

## Spec Compliance Summary

| FR | Status | Notes |
|----|--------|-------|
| FR-303 | ✅ PASS | Early return when `!options.Enabled` — no SDK wiring |
| FR-304 | ✅ PASS | Env var fallback via `Environment.GetEnvironmentVariable` |
| FR-305 | ✅ PASS | `Console.Error.WriteLine` warning + return |
| FR-306 | ✅ PASS | Package is `Azure.Monitor.OpenTelemetry.AspNetCore` v1.4.0 |
| FR-307 | ✅ PASS | Exact method signature match |
| FR-308 | ✅ PASS | `services.AddOpenTelemetry().UseAzureMonitor(...)` |
| FR-309 | ✅ PASS | `transport.mode` resource attribute (becomes global dimension) |
| FR-310 | ❌ FAIL | **Not implemented** — no telemetry enrichment adds parameter names |
| FR-311 | ⚠️ GAP | No active suppression; Debug-level logs include parameter values |
| FR-312 | ⚠️ GAP | No active suppression for PowerShell output |
| FR-316 | ✅ PASS | Serilog untouched |
| FR-317 | ✅ PASS | McpMetrics meter flows through shared OTel pipeline |
| FR-318 | ✅ PASS | appsettings section present with `Enabled: false` |

---

## Required Changes

### 1. FR-310: Tool parameter names as custom properties (BLOCKING)

The spec requires tool parameter **names** to appear in custom properties on telemetry. The current implementation only wires the Azure Monitor exporter but adds no telemetry enrichment. This requires one of:

- An `ITelemetryInitializer` that inspects incoming telemetry and adds parameter name tags
- Activity tag additions in the tool execution path (in `PowerShellAssemblyGenerator.cs`)
- A custom `ActivitySource` span wrapping tool invocations that includes `param.name.*` tags

**Recommendation:** Add Activity tags at the point where tool parameters are resolved (around line 692-730 in `PowerShellAssemblyGenerator.cs`). Add tags like `tool.param.names = "Name,Id,Module"` (comma-separated list). This keeps VALUES out but exposes the schema.

### 2. FR-311/FR-312: Active suppression of parameter values and output (BLOCKING)

`UseAzureMonitor()` enables the OpenTelemetry **log exporter** by default. The existing code at `PowerShellAssemblyGenerator.cs:731-738` logs:

```csharp
logger.LogDebug("Tool parameter detail: ... Value={ParameterValue}", ..., paramValue);
```

And at line 801-808:
```csharp
logger.LogDebug("Bound parameter: ... Value={Value}", ..., convertedValue);
```

While these are at `Debug` level (suppressed by default `Information` filter), the spec says **MUST NOT** — meaning defensive suppression is required regardless of log level configuration. Options:

**Option A (preferred):** Configure `UseAzureMonitor` to disable log export entirely — only export traces + metrics:
```csharp
services.AddOpenTelemetry()
    .UseAzureMonitor(opts => { ... })
    .WithLogging(logBuilder => logBuilder.AddFilter("*", LogLevel.None)); // suppress all OTel log export
```

**Option B:** Add a log filter category that excludes the `PowerShellAssemblyGenerator` category from OTel export.

**Option C:** Strip the `Value=` fields from those log templates (replace with `HasValue={HasValue}` bool). This is the most invasive but cleanest long-term.

**I recommend Option A** for this PR — it's additive, non-invasive, and satisfies FR-311/FR-312 definitively. FR-316 (Serilog continues unchanged) is also preserved since Serilog operates at the ILogger provider level, independent of OTel log export.

---

## Non-Blocking Observations

1. **Double `AddOpenTelemetry()` call ordering:** The implementation correctly relies on `AddOpenTelemetry()` idempotency. Add a brief comment at the call site noting that this builds on the metrics registration from `ConfigureOpenTelemetry`/`ConfigureOpenTelemetryForHttp`.

2. **`SamplingRatio` type:** The SDK's `SamplingRatio` is a `float`. The division `samplingPercentage / 100.0f` is correct but note that `Math.Clamp` returns `int`, so this always produces a clean float division. Fine.

3. **Missing `SectionName` constant:** PR #177 (previously approved) included `public const string SectionName = "ApplicationInsights"` on the options class. This PR uses inline `"ApplicationInsights"` string. Minor inconsistency — prefer the constant.

---

## Architectural Assessment

The plumbing is correct. The method placement (after `ConfigureOpenTelemetry*`), the early-return guard, the connection string resolution chain, and the `ConfigureResource` approach for global dimensions are all architecturally sound. The gaps are in telemetry enrichment and defensive security filtering — both required by spec.

---

## Assignment

Return to original author for fixes. The changes are well-scoped additions to the existing method — no architectural rework needed.

# Wave 1 Review — Spec 008 Application Insights Logging

**Reviewer:** Farnsworth (Lead Architect)
**Date:** 2026-04-27
**Spec:** 008-application-insights-logging

---

## PR #176 — feat: add Azure.Monitor.OpenTelemetry.AspNetCore package reference

**Branch:** squad/170-azure-monitor-otel-package
**Verdict:** ✅ APPROVED

### Findings

| Check | Status | Notes |
|-------|--------|-------|
| Package name | ✅ Pass | Azure.Monitor.OpenTelemetry.AspNetCore (correct, not legacy) |
| Package version | ✅ Pass | 1.4.0 |
| FR-306 compliance | ✅ Pass | Uses modern OpenTelemetry-based SDK |
| Build | ✅ Pass | 0 errors, 9 warnings (pre-existing) |

### Diff Summary

```diff
+    <PackageReference Include="Azure.Monitor.OpenTelemetry.AspNetCore" Version="1.4.0" />
```

---

## PR #177 — feat: add ApplicationInsights config section and binding model

**Branch:** squad/171-app-insights-config-section
**Verdict:** ✅ APPROVED

### Findings

| Check | Status | Notes |
|-------|--------|-------|
| Enabled default | ✅ Pass | false (zero overhead) |
| ConnectionString default | ✅ Pass | empty string |
| SamplingPercentage default | ✅ Pass | 100 |
| SectionName constant | ✅ Pass | "ApplicationInsights" |
| XML documentation | ✅ Pass | All public members documented |
| appsettings.json section | ✅ Pass | Present with Enabled: false |
| FR-300 compliance | ✅ Pass | |
| FR-301 compliance | ✅ Pass | |
| FR-302 compliance | ✅ Pass | |
| FR-318 compliance | ✅ Pass | |
| Build | ✅ Pass | 0 errors, 9 warnings (pre-existing) |

### Diff Summary — appsettings.json

```diff
+  "ApplicationInsights": {
+    "Enabled": false,
+    "ConnectionString": "",
+    "SamplingPercentage": 100
+  },
```

### Diff Summary — ApplicationInsightsOptions.cs

New file with correct structure:
- `SectionName` constant
- `Enabled` property (default: false)
- `ConnectionString` property (default: empty)
- `SamplingPercentage` property (default: 100)
- XML docs on class and all properties

---

## Recommendation

Both PRs are ready to merge. Wave 1 infrastructure for spec 008 is complete.

# Diagnosis: OAuthProxy Configuration Not Reachable in Deployed Image

**Date:** 2026-05-02  
**Investigator:** Amy (DevOps/Platform/Azure)  
**Issue:** `/.well-known/oauth-authorization-server` returns 404 → `Authentication__OAuthProxy__Enabled` is false or missing  
**Root Cause:** OAuthProxy configuration is not present in ANY appsettings.json file in the PoshMcp repository, and the deployment pipeline does not support it.

---

## Findings

### 1. Dockerfile Analysis
**File:** `./Dockerfile` (lines 1-88)

- **Build Stage:** Compiles PoshMcp.Server project with `dotnet publish`
- **Publish Location:** `/app/publish/server` → copied to container image as `/app/server`
- **Appsettings Handling:** The Dockerfile does NOT explicitly copy any appsettings.json file
- **Config Source:** The `dotnet publish` command includes `./PoshMcp.Server/appsettings.json` in the published output by default
- **No Overlay:** The Dockerfile does not layer additional appsettings files on top of the published application

**Implication:** The appsettings.json bundled in the container image is EXACTLY what exists in `./PoshMcp.Server/appsettings.json` at build time.

### 2. PoshMcp.Server appsettings.json Status
**File:** `./PoshMcp.Server/appsettings.json`

- **OAuthProxy Configuration:** ❌ NOT PRESENT
- **Current Authentication Section:**
  ```json
  "Authentication": {
    "Enabled": false,
    "DefaultScheme": "Bearer",
    "DefaultPolicy": {
      "RequireAuthentication": true,
      "RequiredScopes": [],
      "RequiredRoles": []
    },
    "Schemes": {}
  }
  ```
- **No `OAuthProxy` subsection:** The file contains no `OAuthProxy` configuration at all

### 3. Other Appsettings Files Checked
- `./PoshMcp.Server/appsettings.azure.json` — PowerShell config only, no auth
- `./PoshMcp.Server/appsettings.modules.json` — PowerShell module config only
- `./PoshMcp.Server/appsettings.environment-example.json` — Example template, no OAuthProxy
- `./PoshMcp.Server/default.appsettings.json` — Not present or empty
- `./examples/appsettings*.json` — All templates/examples only

**None of these files contain OAuthProxy configuration.**

### 4. Deployment Pipeline Analysis
**File:** `./infrastructure/azure/deploy.ps1` (lines 302–407)

The `ConvertTo-McpServerEnvVars()` function translates appsettings.json keys to Container App environment variables.

**Supported Translations:**
- `PowerShellConfiguration.*` → `PowerShellConfiguration__*`
- `Authentication.Enabled` → `Authentication__Enabled`
- `Logging.LogLevel.Default` → `Logging__LogLevel__Default`

**Missing:** ❌ No handling for `Authentication.OAuthProxy.*` or any nested auth properties  
**Missing:** ❌ No support for `ProtectedResource.*` or `IdentityProvider.*`

**Impact:** Even if OAuthProxy were added to an appsettings.json file, the deployment script would silently ignore it and NOT convert it to an env var.

---

## Root Cause Chain

1. **OAuthProxy is not defined in PoshMcp.Server/appsettings.json**
   - The base/default appsettings has only minimal Authentication config
   
2. **The Dockerfile does not overlay a custom appsettings file**
   - There is no `COPY` instruction for any alternate appsettings files
   - The bundled config is what's published by `dotnet publish`

3. **The ASP.NET Core configuration system loads appsettings.json from the working directory**
   - Container working directory: `/app` (line 33 of Dockerfile)
   - The published binary is in `/app/server/`
   - ASP.NET Core loads `./appsettings.json` relative to the executable, which is `/app/server/appsettings.json`
   - This matches what was published during build

4. **No environment variables are setting OAuthProxy values**
   - The Container App revisions have no `Authentication__OAuthProxy__*` env vars
   - Even if they did, the Bicep/deploy.ps1 doesn't have logic to inject them

5. **Result:** The server runs with `Authentication.Enabled = false`, no OAuthProxy section → returns 404 for `/.well-known/oauth-authorization-server`

---

## What Steven Meant (and What's Wrong)

**Steven's Statement:**  
> "The appsettings.json with OAuthProxy settings IS bundled into the container image — env vars shouldn't be needed."

**Reality:**  
- ❌ There is NO appsettings.json in the repo with OAuthProxy settings
- ❌ The Dockerfile does not perform any custom bundling of appsettings files
- ✅ In theory, IF an appsettings.json with OAuthProxy were in `./PoshMcp.Server/`, it WOULD be bundled by `dotnet publish`
- ❌ But that file does not exist yet

**Likely Scenario:**  
Either Fry or another team member:
1. Created a patched appsettings.json (e.g., in a separate branch or external to this repo)
2. Assumed it would be included in the image
3. Did NOT merge the patch into `./PoshMcp.Server/appsettings.json`
4. Did NOT rebuild/redeploy the image with the patched config

---

## Resolution Path

### **Option A: Add OAuthProxy to PoshMcp.Server/appsettings.json (Recommended)**

1. Update `./PoshMcp.Server/appsettings.json` with a complete `Authentication.OAuthProxy` section
2. Include `TenantId`, `ClientId`, `Audience`, `Scopes`, etc.
3. Rebuild the image: `docker build -t poshmcp:latest .`
4. Deploy the new image to Azure Container Apps
5. OAuthProxy config will be bundled in the image and used by ASP.NET Core's config system

**Pros:**  
- Config is immutable, baked into the image
- No env var surprises or shadowing
- Follows `.NET standard` (appsettings.json is the config source)

**Cons:**  
- Credentials/secrets should use Azure Key Vault, not hardcoded JSON
- Requires rebuild + redeploy for config changes

### **Option B: Update Deployment Pipeline to Support OAuthProxy Env Vars**

1. Extend `ConvertTo-McpServerEnvVars()` in `deploy.ps1` to handle `Authentication.OAuthProxy.*` keys
2. Ensure Bicep resource template accepts and injects these env vars
3. Deploy OAuthProxy config as Container App environment variables (with Key Vault secrets for sensitive values)

**Pros:**  
- Config can be updated without image rebuild
- Secrets managed via Key Vault
- Flexible for multi-environment deployments

**Cons:**  
- More complex deployment logic
- Requires changes to Bicep, deploy.ps1, and possible env var structure

### **Option C: Both (Recommended for Production)**

1. Add a minimal OAuthProxy template to `./PoshMcp.Server/appsettings.json` with placeholder values
2. Extend `deploy.ps1` and Bicep to support overriding OAuthProxy values via env vars
3. At deploy time, inject actual credentials as Container App env vars
4. This gives flexibility + security

---

## Next Steps

1. **Clarify with Fry/Steven:** Where is the patch to appsettings.json that adds OAuthProxy? Is it in a separate branch, external repo, or lost?

2. **For Bender (Development/Build):**  
   - If the OAuthProxy config exists externally, add it to `./PoshMcp.Server/appsettings.json`
   - Update `deploy.ps1` `ConvertTo-McpServerEnvVars()` function to translate OAuthProxy keys
   - Add integration test to verify OAuthProxy settings are passed as env vars

3. **For Amy (Deployment):**  
   - Once config is in place, rebuild image: `./docker.ps1 build -ImageTag latest`
   - Push image to registry
   - Deploy with `infrastructure/azure/deploy.ps1`
   - Verify `/.well-known/oauth-authorization-server` returns 200

---

## References

- **Dockerfile:** Publishes from `./PoshMcp.Server/` with `dotnet publish`
- **PoshMcp.Server/appsettings.json:** Currently has minimal Authentication config, no OAuthProxy
- **deploy.ps1 (ConvertTo-McpServerEnvVars):** Does not translate OAuthProxy keys to env vars
- **ASP.NET Core Config Loading:** Reads `appsettings.json` from app working directory

# Release v0.9.10

**By:** Amy (DevOps / Platform / Azure Engineer)  
**Date:** 2026-05-02  
**Status:** Applied  

## What

Completed release of PoshMcp v0.9.10 by:
1. Verifying fix commit (b81a55d: OAuth issuer in Entra metadata)
2. Confirming version bump to 0.9.10 in `PoshMcp.Server/PoshMcp.csproj`
3. Pushing main branch to origin (2 commits)
4. Creating and pushing annotated tag `v0.9.10`
5. Verifying CI triggered automatically

## Why

Release implements security/configuration fix for OAuth Entra issuer metadata propagation. Bender had prepared all necessary changes (fix commit, version bump, release notes); Amy executed the final push and tagging steps to trigger the container build pipeline.

## Technical Details

- **Fix commit:** b81a55d on main — sets Entra issuer in AS metadata for OAuth compliance
- **Release notes:** Updated `docs/release-notes/0.9.10.md` and `docs/toc.yml`
- **CI Triggered:** GitHub Actions workflow "Build and Publish Packages" (Run 25254551703)
  - Builds Dockerfile → pushes container to `ghcr.io/usepowershell/poshmcp:0.9.10`
  - Expected completion: ~5-10 minutes

## Next Steps (Steven)

1. Monitor workflow at https://github.com/usepowershell/PoshMcp/actions/runs/25254551703
2. Confirm container image published to GHCR with tag `0.9.10`
3. Coordinate AdvocacyBami update with new base image reference (do NOT modify AdvocacyBami files in this release)

## Context

This is a .NET/Docker project using tag-based release model (not npm). Release notes and version were already prepared by Bender. Amy's role was infrastructure/operations — pushing to origin and triggering CI.

## Rule Going Forward

Tag push automatically triggers GitHub Actions workflows. No additional manual steps needed — just monitor the container build completion and publish status.

# Decision: v0.9.11 Release — OAuth /authorize Proxy Endpoint

**Date:** 2026-05-02T10:11:52-05:00
**Agent:** Amy (DevOps / Platform Engineer)
**Status:** COMPLETED

## Context

Bender committed `feat(auth): add /authorize proxy redirect endpoint for VS Code OAuth` and bumped the version to 0.9.11 in PoshMcp.Server/PoshMcp.csproj.

The commit introduced a critical OAuth fix: VS Code MCP clients were constructing auth URLs as `{proxy_base}/authorize` and receiving 404 errors. The new endpoint acts as a proxy that:
- Accepts all OAuth2 PKCE parameters
- Issues a 302 redirect to Entra's authorize endpoint
- Replaces the ephemeral DCR client_id with the real Entra client_id from config

## Decision

Release v0.9.11 with the following artifacts:

### 1. Release Notes (`docs/release-notes/0.9.11.md`)

Created following the established pattern from v0.9.10:
- **Title:** PoshMcp v0.9.11 Release Notes
- **What's New:** OAuth /authorize proxy redirect endpoint feature
- **Bug Fixes:** OAuth flow now completes for VS Code MCP clients
- **Upgrade Notes:** Configuration guidance for Authentication.ClientId and TenantId

### 2. Table of Contents (`docs/toc.yml`)

Updated Release Notes section to include:
```yaml
- name: v0.9.11
  href: release-notes/0.9.11.md
```

Added at the top of the release notes list, maintaining reverse chronological order.

### 3. Git Workflow

- **Commit:** `docs: add v0.9.11 release notes` (7e67ac9)
  - Included Copilot co-author trailer as per project standards
  - Modified: docs/release-notes/0.9.11.md, docs/toc.yml
- **Push:** Pushed commit to origin main (b81a55d → 7e67ac9)
- **Tag:** Created lightweight tag `v0.9.11` on commit 7e67ac9
- **Push Tag:** Pushed tag to origin (new tag on remote)

## Rationale

### Release Timing

The OAuth /authorize endpoint is a critical bug fix for VS Code MCP client compatibility. Without this fix, VS Code clients cannot complete the OAuth flow, making the MCP server unusable with that client. This warrants an immediate patch release.

### Release Notes Format

Followed the established release notes pattern:
- Single-file release notes document per version
- Listed in toc.yml in reverse chronological order
- Clear sections: Features, Bug Fixes, Upgrade Notes
- Concise descriptions tied to user impact

### Version Numbering

No decision needed — Bender already bumped to 0.9.11 in the csproj. This is a patch release (0.9.10 → 0.9.11) appropriate for a bug fix + proxy endpoint.

## Implementation Checklist

- ✅ Version confirmed: 0.9.11 in PoshMcp.Server/PoshMcp.csproj
- ✅ Release notes created with feature and bug fix details
- ✅ Table of contents updated
- ✅ Commit created with proper trailer
- ✅ Changes pushed to origin main
- ✅ Git tag created and pushed
- ✅ Release is now discoverable by CI/CD (publish-packages.yml listens for v* tags)

## Artifacts Created

- `docs/release-notes/0.9.11.md` — Release notes document
- `docs/toc.yml` — Updated table of contents
- Git commit: `7e67ac9` — Release notes commit on main
- Git tag: `v0.9.11` — Release tag pointing to commit 7e67ac9

## Next Steps (Async — CI Handles Automatically)

The publish-packages.yml GitHub Actions workflow will automatically:
1. Detect the `v0.9.11` tag push
2. Build and test the release
3. Create a GitHub Release with the release notes
4. Publish poshmcp v0.9.11 to NuGet.org

No manual intervention required unless CI reports failures.

## Related Documents

- `.squad/agents/amy/history.md` — Updated with session entry
- `.copilot/skills/release-process/SKILL.md` — Release process guidelines (reviewed)
- `docs/release-notes/0.9.11.md` — This release's notes
- `docs/toc.yml` — Navigation updated

# Decision: Advertise explicit delegated scope in AS metadata

**Date:** 2026-05-02
**Author:** Bender (Backend Developer)
**Status:** Implemented

## Problem

After the `/authorize` redirect succeeded and the user authenticated, the token exchange returned a JWT that failed with `SecurityTokenInvalidIssuerException`.

**Root cause:** `OAuthProxyEndpoints.cs` advertised `api://{audience}/.default` in `scopes_supported` of the AS metadata document. When VS Code requested the `.default` scope, Entra issued a **v1.0 token** whose issuer claim is `https://sts.windows.net/{tenant}/`. The configured `ValidIssuers` expected the v2.0 issuer `https://login.microsoftonline.com/{tenant}/v2.0`, causing validation failure.

## Decision

Change `scopes_supported` to advertise an explicit delegated scope instead of `.default`.

**Resolution order:**
1. Look up `config.DefaultPolicy.RequiredScopes` for an entry that starts with the configured audience URI.
2. If found, use it — keeps AS metadata aligned with what token validators require.
3. Otherwise, fall back to `{audience}/user_impersonation`.

## Implementation

**File:** `PoshMcp.Server/Authentication/OAuthProxyEndpoints.cs`

```csharp
// Before
scopesSupported.Add($"{proxy.Audience.TrimEnd('/')}/.default");

// After
var audienceBase = proxy.Audience.TrimEnd('/');
var explicitScope = config.DefaultPolicy?.RequiredScopes
    .FirstOrDefault(s => s.StartsWith(audienceBase, StringComparison.OrdinalIgnoreCase));
scopesSupported.Add(explicitScope ?? $"{audienceBase}/user_impersonation");
```

**Commit:** `fix: advertise explicit user_impersonation scope in AS metadata to prevent v1.0 token issuance`

## Why `.default` is wrong here

The `.default` scope instructs Entra to grant all statically-declared permissions for the app. When the app registration is a v1.0 registration (or has no explicit v2.0 access token configuration), Entra issues a v1.0 token signed by `sts.windows.net`. Our middleware validates against `login.microsoftonline.com/.../v2.0` — these are different issuers. Explicit delegated scopes (e.g. `user_impersonation`) force v2.0 token issuance regardless of app registration version.

## Impact

- VS Code now requests `api://{audience}/user_impersonation` instead of `.default`.
- Entra issues v2.0 tokens; issuer validation passes.
- No changes required to `ValidIssuers` or token validation configuration.

# Decision: Fix Auth Challenge Not Firing for No-Token Requests

**Date:** 2026-05-02
**Author:** Bender (Backend Developer)
**Status:** Implemented

## Problem

When VS Code's MCP client connects with no pre-existing auth credentials, it was not being redirected to sign in — the connection hung at `initialize`. The container log showed `aspnetcore.authentication.result: none` (expected — no token was presented), but the OAuth browser redirect never happened.

## Root Cause

Two related defects in the auth challenge path:

### 1. `OnChallenge` condition too narrow (`AuthenticationServiceExtensions.cs`)

The `OnChallenge` handler that injects `WWW-Authenticate: Bearer resource_metadata="..."` was gated on:

```csharp
if (cfg.Value.ProtectedResource?.Resource is not null)
```

The `AuthenticationConfigurationValidator` does **not** require `ProtectedResource.Resource` to be set — it is optional. When `Resource` is null (a valid configuration), the condition is `false`. The handler fell through to the default JWT Bearer challenge, which emits only `WWW-Authenticate: Bearer` with no `resource_metadata` parameter.

VS Code's MCP client reads `resource_metadata` to discover the OAuth Authorization Server. Without it, no browser redirect is triggered and the connection hangs waiting for the MCP `initialize` response.

This affects the "no token" case (`authentication.result: none`) specifically because a valid token would have bypassed the challenge entirely.

### 2. RFC 9728 `resource` field could be `null` in PRM response (`ProtectedResourceMetadataEndpoint.cs`)

The Protected Resource Metadata endpoint only substituted non-HTTPS URIs with `serverBase`. If `Resource` was `null` or empty, the `resource` field in the PRM JSON was `null`. RFC 9728 requires `resource` to be an absolute HTTPS URI — a null value breaks the VS Code OAuth discovery chain even if the challenge had fired correctly.

## Fix

**`AuthenticationServiceExtensions.cs`:** Changed condition from `ProtectedResource?.Resource is not null` to `ProtectedResource is not null`. This aligns with `MapProtectedResourceMetadata`'s own gate (also `ProtectedResource is not null`), ensuring `resource_metadata` is always sent in the challenge whenever the PRM endpoint is available.

**`ProtectedResourceMetadataEndpoint.cs`:** Added a null/empty fallback so `resource` is always computed as `serverBase` when `Resource` is not configured, before applying the existing non-HTTPS substitution. This ensures the PRM `resource` field always satisfies RFC 9728.

## Expected Flow After Fix

1. VS Code sends `POST /` with no token
2. Server returns `401` + `WWW-Authenticate: Bearer resource_metadata="https://host/.well-known/oauth-protected-resource"` (now fires even when `Resource` is null)
3. VS Code fetches the PRM; `resource` is now always a valid HTTPS URI
4. VS Code reads `authorization_servers`, fetches AS metadata
5. VS Code opens browser → user signs in → token obtained → retry succeeds

# Decision: Use Entra v2.0 Authority URL for JWT Bearer authentication

**Date:** 2026-05-02
**Author:** Bender (Backend Developer)
**Status:** Accepted and implemented

## Context

AdvocacyBami was logging `SecurityTokenSignatureKeyNotFoundException` followed by 401 responses. The JWT Bearer middleware was configured with `Authority = https://login.microsoftonline.com/{tenant}` (no `/v2.0` suffix), which resolves to the Entra **v1.0** OIDC discovery document. The v1.0 JWKS (`/common/discovery/keys`) does not contain the signing keys used for tokens issued via the v2.0 token endpoint (`/oauth2/v2.0/token`), which is what VS Code uses.

## Decision

1. **Always append `/v2.0` to the Entra Authority URL** when the token flow uses the v2.0 endpoint. Specifically: `https://login.microsoftonline.com/{tenant}/v2.0`.

2. **PoshMcp server should warn at startup** when it detects the dangerous mismatch: Authority is a v1.0 Entra URL but `ValidIssuers` contains a v2.0 issuer. This is implemented via `Console.Error.WriteLine` in `AuthenticationServiceExtensions.cs`.

## Rationale

- Entra v1.0 and v2.0 endpoints use different JWKS URIs and issue tokens with different signing keys.
- A v1.0 Authority with a v2.0 token always fails signature validation silently — no obvious configuration error is reported until runtime failure.
- The startup warning gives operators an actionable message before the first request fails.

## Consequences

- **AdvocacyBami**: `appsettings.json` Authority updated. JWT validation now succeeds for v2.0 tokens.
- **PoshMcp**: Any deployment with this misconfiguration will log a clear warning on startup.
- **No breaking changes**: The v2.0 OIDC discovery doc is a superset of v1.0 for validation purposes.

## Affected Files

- `C:\Users\stmuraws\source\emu\gim-home\AdvocacyBami\appsettings.json`
- `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs`

# Decision: Add /authorize proxy redirect endpoint

**Date:** 2026-05-02
**Author:** Bender (Backend Developer)
**Status:** Implemented — v0.9.11

## Context

VS Code's MCP OAuth client does not use `authorization_endpoint` from the Authorization Server metadata document directly. Instead it constructs the auth URL as `{authorization_server_base}/authorize`, where `authorization_server_base` comes from `authorization_servers[0]` in the Protected Resource Metadata. Since PoshMcp is the authorization server base, VS Code was issuing `GET /authorize?...` → **404**.

Root cause diagnosed by Fry.

## Decision

Add a `GET /authorize` endpoint to `OAuthProxyEndpoints.cs` that acts as a redirect proxy to Entra's real authorize endpoint.

## Implementation

**File:** `PoshMcp.Server/Authentication/OAuthProxyEndpoints.cs`

The endpoint:
1. Accepts all incoming query parameters via `HttpContext.Request.Query`
2. Iterates params with `SelectMany` to handle multi-value params; replaces `client_id` (case-insensitive) with `proxy.ClientId` from config
3. Ensures `client_id` is always present even if the caller omits it
4. Builds the redirect URL: `https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize` + `QueryString.Create(params)`
5. Returns `Results.Redirect(url, permanent: false)` — HTTP 302
6. Logs at Debug level: tenant ID only (no code_challenge, state, or other sensitive values)

All other params (`scope`, `response_type`, `code_challenge`, `code_challenge_method`, `redirect_uri`, `state`) pass through unchanged. Scope transformation is deliberately omitted — Entra handles `api://.../.default` scopes natively.

## Alternatives Considered

- **Rewrite `authorization_endpoint` in AS metadata to point at Entra directly**: Would fix VS Code but break other clients that rely on the proxy for `client_id` substitution. Rejected.
- **Update Protected Resource Metadata to point at Entra as authorization server**: Would require clients to handle the real Entra authorize URL directly, losing the DCR proxy benefit. Rejected.

## Guard rails

- Endpoint is only registered when `proxy.Enabled == true` and `proxy.TenantId` is non-empty (same guards as existing proxy endpoints)
- Returns `501 Not Implemented` if `proxy.ClientId` is unconfigured
- Marked `.AllowAnonymous()` — auth challenge must not intercept the OAuth handshake itself

## Version

`PoshMcp.csproj` bumped from `0.9.10` → `0.9.11`

# Decision: Honor X-Forwarded-Proto in All Public-URL Construction

**Date:** 2026-05-02  
**Author:** Bender (Backend Developer)  
**Status:** Implemented

## Context

Fry's v0.9.8 functional check found that the `WWW-Authenticate: Bearer resource_metadata=` URL
returned by the server used `http://` instead of `https://` when deployed to Azure Container Apps.
Azure Container Apps (and similar reverse-proxy platforms) terminate TLS and forward requests
internally over HTTP, setting `X-Forwarded-Proto: https` to indicate the original public scheme.

`HttpContext.Request.Scheme` returns `http` in this configuration, producing incorrect URLs.

## Decision

**Any code that constructs a public-facing URL from the current request MUST read
`X-Forwarded-Proto` (and optionally `X-Forwarded-Host`) before falling back to the raw request
values.**

Canonical pattern:

```csharp
var scheme = req.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? req.Scheme;
var host   = req.Headers["X-Forwarded-Host"].FirstOrDefault() ?? req.Host.ToUriComponent();
var url    = $"{scheme}://{host}{path}";
```

## Rationale

- `OAuthProxyEndpoints.GetServerBaseUrl` and `ProtectedResourceMetadataEndpoint` already
  implemented this pattern correctly.
- `AuthenticationServiceExtensions.OnChallenge` was the only location that did not; this
  inconsistency caused the bug reported by Fry.
- Using the forwarded headers is the standard ASP.NET Core approach for hosted-behind-proxy
  scenarios (cf. `UseForwardedHeaders` middleware, `ForwardedHeadersOptions`).

## Scope of Change

- `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs` — fixed `OnChallenge` handler
- `PoshMcp.Server/PoshMcp.csproj` — version bumped to 0.9.9

## Affected Deployments

All deployments behind a reverse proxy that terminates TLS (Azure Container Apps, nginx, etc.).
Local/stdio deployments are unaffected (fallback to `req.Scheme` = `https` or `http` as
configured).

# Decision: OAuth Issuer and Scope Fix (v0.9.10)

**By:** Bender (Backend Developer)
**Date:** 2026-05-02
**Status:** Applied

## What

Fixed two bugs causing MCP `initialize` timeout when using Entra ID authentication:

1. **Issuer mismatch** — `OAuthProxyEndpoints.cs` returned the container's own URL as `issuer` in the `/.well-known/oauth-authorization-server` metadata. Changed to `https://login.microsoftonline.com/{tenantId}/v2.0`.

2. **Scope format mismatch** — `RequiredScopes` in `AdvocacyBami/appsettings.json` used the full URI form (`api://.../{clientId}/user_impersonation`). Entra v2.0 tokens carry `scp` as the short name only (`user_impersonation`). Changed to `["user_impersonation"]`.

## Why

RFC 8414 requires MCP clients to validate `token.iss == AS.issuer`. With the issuer set to the container URL, Entra tokens were always rejected, triggering infinite `initialize` retries. The scope mismatch caused every authenticated request to return 401.

## Rules Going Forward

- The `issuer` field in `/.well-known/oauth-authorization-server` MUST always be the Entra v2.0 issuer URL, not the server's base URL.
- `RequiredScopes` in configuration MUST use the short scope name (`user_impersonation`), not the full application URI form — Entra v2.0 tokens never include the full URI in the `scp` claim.
- When configuring `RequiredScopes`, test against a real token's decoded `scp` claim to confirm the exact format.

# Decision: Token Diagnostics and Configurable IdleTimeout

**By:** Bender (Backend Developer)
**Date:** 2026-05-02
**Status:** Applied

## What

1. **Token diagnostics**: Enhanced `/token` proxy in `OAuthProxyEndpoints.cs` to log HTTP status, Content-Type, and response body (on error) from Entra. On success, logs status+content-type only (no token body). Request field names are logged at Debug (no values to avoid leaking secrets).

2. **Configurable IdleTimeout**: Added `McpServerConfiguration` class and `McpServer.IdleSessionTimeoutSeconds` appsettings key. `HttpServerHost` reads this and passes it to `WithHttpTransport(opts => opts.IdleTimeout = ...)`.

## Why

- `/token` proxy failures were invisible — no logging on Entra errors made auth debugging very hard.
- VS Code's ~5s initialize timeout causes double auth redirect loops when server startup takes time. `IdleSessionTimeoutSeconds` lets operators tune the session idle timeout without code changes.

## Rule Going Forward

- Never log token values, auth codes, or client secrets — log field names and HTTP metadata only.
- `WithHttpTransport` in MCP SDK 1.2.0 accepts `Action<HttpServerTransportOptions>` — use this overload for transport configuration rather than `builder.Services.Configure<HttpServerTransportOptions>()` separately.

