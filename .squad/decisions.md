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
