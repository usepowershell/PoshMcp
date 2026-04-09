# Decisions Log

Canonical record of decisions, actions, and outcomes.


## 2026-04-08

### Serialization migration coverage should anchor on serializer-level tests

**Author:** Fry (via Copilot)
**Date:** 2026-04-08T00:00:00Z
**Status:** Proposed

Add focused unit coverage for `PowerShellJsonOptions` string serialization, because the current migration regression is shared across stdio and web paths while the web integration harness is also vulnerable to unrelated `dotnet run` apphost file locks.

**Rationale:**
- The integration failures prove user-visible breakage across transport paths
- A serializer-level regression test gives the narrowest reliable validation target for the fix
- Web integration runs can be polluted by unrelated `dotnet run` file-lock failures, so unit coverage is the safer regression anchor

**Impact:** Prioritize targeted serializer regression coverage while web-path failures are being investigated. Use broader web validation as confirmation, not the sole guardrail.

### Normalize PowerShell results before JSON serialization

**Author:** Steven Murawski (via Copilot/Hermes)
**Date:** 2026-04-08
**Status:** Proposed

Convert PowerShell results into JSON-safe scalars, dictionaries, and arrays before handing them to `System.Text.Json`, with explicit handling for `IDictionary`, `IEnumerable`, pointer-like CLR values, recursive `PSObject` graphs, and inaccessible CLR properties.

**Rationale:**
- Direct serialization of nested CLR objects leaked framework dictionary internals into responses
- Members such as `Encoding.Preamble` and pointer-like CLR values can trip unsupported serialization paths
- Normalizing into JSON-safe shapes protects both live command results and cached outputs

**Impact:** The serialization pipeline should normalize nested PowerShell and CLR objects before JSON output so stdio and web responses stay predictable and cache-safe.

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

### Reuse existing test build outputs when launching the in-process web harness

**Author:** Steven Murawski (via Copilot)
**Date:** 2026-04-08T00:00:00Z
**Status:** Proposed

The in-process web test harness should infer the active test build configuration and launch `PoshMcp.Web` with `dotnet run --no-build --configuration {Debug|Release}` so integration tests reuse existing build outputs.

**Rationale:**
- `dotnet test -c Release` already produces the required binaries
- Triggering a second build during `StartAsync()` caused file-lock conflicts against referenced outputs
- Matching the active test configuration removes accidental Debug/Release mismatches in the harness

**Impact:** Web integration startup should avoid redundant builds and reduce file-lock failures during test execution.

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

**Rationale:** `PoshMcp.Server` is stdio MCP-ready with full CLI; Web project is deployment-focused and not packaged. Tool command `poshmcp` matches project name and convention.

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

### dotnet tool Packaging ADR — PoshMcp

**Author:** Farnsworth (Lead / Architect)
**Date:** 2026-04-09
**Status:** Proposed

Architectural decision record: `PoshMcp.Server` becomes dotnet tool, not Web. Server: stdio MCP transport, complete CLI, fully self-contained. Web: deployment-focused, best via Docker/ACA, not a CLI tool experience.

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

### Runtime Caching Toggle via MCP Tool

**Author:** Farnsworth (Lead / Architect)
**Date:** 2025-07-17
**Status:** Proposed
**Spec:** `specs/large-result-performance.md` (section 3.6)

Add a `set-result-caching` MCP tool that sets runtime overrides for result caching without restarting the server.

**Key design choices:**
1. **Runtime overrides take highest priority** in the resolution chain — above per-function config and global config.
2. **Scope: global + per-function.** The tool accepts a `scope` parameter (`global` or `function`). Per-function runtime overrides take priority over global runtime override.
3. **Ephemeral state.** Runtime overrides do not persist across server restarts. Runtime = session intent; config = operational defaults.
4. **Thread-safe via `ConcurrentDictionary` + `volatile`.** No locks needed for simple flag reads/writes.
5. **Immediate effect on next command.** Toggling caching does not retroactively cache previous output.
6. **Gating.** Recommend gating behind `EnableDynamicReloadTools` for consistency with other runtime configuration tools.

**Rationale:**
- Steven requested runtime toggleability without restart for developer iteration
- Single-client stdio server model makes session-scoped state unnecessary
- Per-function + global hierarchy mirrors static config, reducing cognitive load

**Impact:**
- New file: `RuntimeCachingState.cs`
- New DI registration in `Program.cs`
- New MCP tool registered in `McpToolFactoryV2`
- Updated resolution logic in `ExecutePowerShellCommandTyped`
- New Phase 2.5 in implementation plan (between Phase 2 and Phase 3)
- Additional unit and integration tests


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

