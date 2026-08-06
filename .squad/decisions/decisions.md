# Decision: Use User-Assigned Managed Identity + AcrPull Role for ACR Authentication

**Date:** 2026-07-18
**Author:** Amy (DevOps / Platform / Azure Engineer)
**Status:** Applied
**Triggered by:** Container App deployment failure — UNAUTHORIZED error pulling from `psbamiacr.azurecr.io`

## Context

The Container App was failing to pull its image from ACR with:

```
GET https: UNAUTHORIZED: authentication required for psbamiacr.azurecr.io/poshmcp:...
```

The `registries` array in the Container App was empty (no credentials, no identity reference), so Azure Container Apps had no way to authenticate against the private registry.

## Decision

Use the **existing user-assigned managed identity** (`poshmcp-identity`) on the Container App to authenticate against ACR via the `AcrPull` built-in role. No passwords or admin credentials are used.

**Why user-assigned over system-assigned:**
- A user-assigned identity already existed on the Container App for subscription-level RBAC.
- User-assigned identities persist across Container App recreation; system-assigned identities are tied to the resource lifecycle.
- Consistent with the existing identity architecture in `resources.bicep`.

## Implementation (resources.bicep)

1. **Derive ACR name** from `containerRegistryServer` parameter:
   ```bicep
   var containerRegistryName = !empty(containerRegistryServer) ? split(containerRegistryServer, '.')[0] : 'unused'
   ```

2. **Existing ACR reference** (conditional, same resource group):
   ```bicep
   resource existingAcr 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = if (!empty(containerRegistryServer)) {
     name: containerRegistryName
   }
   ```

3. **AcrPull role assignment** scoped to the ACR resource:
   ```bicep
   resource acrPullRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(containerRegistryServer)) {
     name: guid(containerRegistryServer, managedIdentity.id, acrPullRoleDefinitionId)
     scope: existingAcr
     properties: {
       roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleDefinitionId)
       principalId: managedIdentity.properties.principalId
       principalType: 'ServicePrincipal'
     }
   }
   ```

4. **Registries config** — uses managed identity when no credentials provided:
   ```bicep
   registries: containerRegistryUsername != ''
     ? [{ server: ..., username: ..., passwordSecretRef: 'registry-password' }]
     : !empty(containerRegistryServer)
       ? [{ server: containerRegistryServer, identity: managedIdentity.id }]
       : []
   ```

5. **Container App `dependsOn`** — ensures role assignment is deployed first:
   ```bicep
   dependsOn: [acrPullRoleAssignment]
   ```

## AcrPull Role

- **Role definition ID:** `7f951dda-4ed3-4680-a7ca-43fe172d538d`
- **Scope:** ACR resource (not resource group, not subscription)
- **Principal:** User-assigned managed identity's `principalId`

## Backward Compatibility

- When `containerRegistryUsername` is provided, credential-based auth is used (unchanged).
- When `containerRegistryServer` is empty, no registry config is set (unchanged).
- No changes to `parameters.json`, `main.bicep`, or `deploy.ps1` required.

## Notes

- RBAC propagation in Azure is eventually consistent. On first deployment, there may be a short delay before the role takes effect. Re-running the deployment if the first attempt fails on image pull is a valid workaround.
- The ACR is assumed to be in the **same resource group** as the Container App, consistent with how `deploy.ps1` creates it (`Initialize-ContainerRegistry` uses `--resource-group $ResourceGroup`).

# Decisions Ledger

### 2026-07-15: MCP Resources and Prompts Architecture
**By:** Farnsworth (Architect)
**Status:** Proposed
**Spec:** `specs/002-mcp-resources-and-prompts/spec.md`
**Decision:** Seven architectural decisions for MCP resources/prompts layer:
1. Config placement: `McpResources` and `McpPrompts` are top-level `appsettings.json` sections (not nested under `PowerShellConfiguration`)
2. URI scheme: `poshmcp://resources/{slug}` recommended but not enforced; Doctor emits warning for non-conforming URIs
3. Command execution: shared runspace, read-only by convention (operator responsibility, not server-enforced)
4. Argument injection: pre-assign to runspace as `$argName = value` (not `-ArgumentList`)
5. File-backed prompt arguments: out of scope for v1 — MCP client handles template rendering
6. Resource caching: intentionally absent — operators build caching into PowerShell commands if needed
7. Resource subscriptions: out of scope — `resources/subscribe` and change notifications deferred
8. SDK registration pattern: all four handler types registered on MCP server builder in `Program.cs` via SDK extension methods

### 2026-05-17T08:03:00-05:00: Correlation IDs and auth diagnostics must be scrubbed at log call sites
**By:** Bender (Backend Developer)
**What:** Treat `OperationContext.CorrelationId` plus JWT/config values echoed into diagnostics as untrusted. Scrub them before they enter structured log arguments or logging scopes, not just when writing free-form message text.
**Why:** Correlation IDs can originate from inbound HTTP headers, and auth diagnostics echo claims, decoded token fields, challenge errors, and config-backed strings. CodeQL closes `cs/log-forging` only when the sanitization is applied at the actual sink (`ILogger` arguments, `BeginScope` state, or `Console.Error.WriteLine` interpolation).


### 2026-05-17T08:10:41-05:00: User directive
**By:** Steven Murawski (via Copilot)
**What:** When you need to shell out to run a command, prefer `pwsh` to `powershell` unless you need a PowerShell command or module that is not supported under pwsh (PowerShell 7).
**Why:** User request — captured for team memory

### 2026-05-17T08:15:00-05:00: Hermes — log-forging revision
**By:** Hermes
**Status:** Proposed
**Related:** #277, PR #278
**Decision:** In `PoshMcp.Server\PowerShell\PowerShellAssemblyGenerator.cs`, every log sink that can receive user-controlled or environment-controlled string data must sanitize that value with `LogSanitizer.Scrub()` at the `ILogger` call site, and prefer structured logging over interpolated log strings.
**Applied in this revision:**
- generation-time command failure/skip logs
- `_MaxResults` validation warning
- cached output sort/filter/group helper diagnostics
- invalid filter-script warning with scrubbed script and scrubbed exception message

---

### 2026-08-06T17:05:00Z: User directive
**By:** Steven Murawski (via Copilot / Squad)
**What:** Accept documented residual SDK overhead and redefine warm baseline/threshold with a written decision (Decision C). Do not keep chasing the 1.05× cross-SDK warm bar as the sole release gate without redefining the comparison.
**Why:** User request after Phase 4 enforce remained RED (~1.6× warm p95) post #392/#393; Farnsworth RCA showed structural v1→v2 Streamable HTTP cost.

---

### 2026-08-06: Cubert Verification — Decision C (Warm Gate Redefinition)
**By:** Cubert (Fact Checker)
**Status:** APPROVED — Amy may implement as written
**Decision under review:** `Decision C: Redefine Warm/Throughput Gate for SDK v2 Migration` (Farnsworth)
**Cross-checked against:** Post-merge warm-call RCA, `PerformanceComparator.cs`, `Phase4Models.cs`, `MethodologyContract.cs`, `Phase4ComparisonTests.cs`

**Findings:**
- ✅ Arithmetic verified: v1 = 0.778 ms, v2 = 1.271 ms, ratio = 1.634× (confirmed)
- ✅ Best-case after all fixes: ~1.126 ms (ratio ~1.45×, still above 1.05×)
- ⚠️ Minor: ResetCore range stated as 50–130 µs; itemized sub-items sum to 30–90 µs (gap ~20–40 µs likely `ResetWorkingLocation`). No impact on Decision C validity.
- ✅ All code-state claims verified; all version constants and field names match current code
- ℹ️ Note for Amy: `AllPassed` update requires `checks.All(c => !c.IsBlocking || c.Passed)` logic

**Verdict:** APPROVE. Farnsworth may optionally add parenthetical note to 1.58–1.83× range ("single run at 1.634×; range estimated from expected run-to-run variance") for auditability, but this does not block implementation.

---

### 2026-08-06: Decision C — Redefine Warm/Throughput Gate for SDK v2 Migration
**By:** Farnsworth (Lead Architect)
**Status:** APPROVED for implementation (Cubert verified; Steven must sign off after green CI run)
**Supersedes:** Decision B warm/throughput threshold constants only (isolation semantics and all other gates unchanged)
**Implements:** Steven Murawski directive 2026-08-06 — accept documented residual SDK overhead; redefine gate
**RCA evidence:** `.squad/decisions/inbox/farnsworth-post-merge-warm-rca.md` (summary: Phase 4 enforce gate at commit 0082d50 produced RED EXIT 1; measured warm p95 ratio v1→v2 = 1.63×; RCA identifies structural v1.4.1→v2.0.0 SDK transport overhead ~100–200µs/call, irreducible at application layer; achievable product fixes total ~70–220µs savings → best-case ratio ≈1.45×, still above 1.05× gate)
**Enforce run:** https://github.com/usepowershell/PoshMcp/actions/runs/31116482880 (commit `0082d50`)

**Problem:** Decision B gated cross-SDK warm p95 at ≤1.05× (v1-ephemeral vs v2-pool-reset). This comparison conflates two independent costs:
1. **Isolation overhead** — cost of `pool_reset` vs `ephemeral_create_dispose` — our code, controllable
2. **SDK migration overhead** — cost of v2 Streamable HTTP vs v1 transport — not our code, structural

A threshold that cannot be met even with perfect application code is not a product regression detector. It measures the SDK, not our isolation design.

**Decision C Redefinition:**

| Item | Status | Threshold |
|------|--------|-----------|
| Cross-SDK warm/throughput (v1 vs v2) | Informational only (`IsBlocking = false`) | Ratio still captured and reported; no EXIT 1 |
| Same-SDK warm/throughput (v2 vs v2, pool vs ephemeral) | **Blocking → NEW PRIMARY GATE** | ≤ 1.10× (product regression detector) |
| Cold-start p95 | Blocking (unchanged) | ≤ 1.10× |
| Memory peak mean | Blocking (unchanged) | ≤ 1.10× |
| Soak handle floor | Blocking (unchanged) | ≤ 0.010 /s |
| SDK/methodology validation | Blocking (unchanged) | EXIT 2 on violation |

**What We Accept:** SDK v1.4.1 → v2.0.0 Streamable HTTP warm call overhead of ~100–200 µs per call as structural, non-blocking baseline for v2. Cross-SDK warm ratio (1.58–1.83× observed; single measured point 1.634×) is now informational, not blocking.

**What We Preserve:** Isolation semantics from Decision B unchanged. Same-SDK (v2 ephemeral vs pool) is now the blocking gate at 1.10×, measuring ONLY our reset protocol cost. Cross-SDK comparison remains audit-quality but non-blocking.

**Implementation (Amy's scope):**
- Add `SameSdkWarmCallP95MaxRatio = 1.10` and `SameSdkThroughputMeanMaxRatio = 1.10` constants to `PerformanceComparator.cs`
- Add `CompareSameSdkIsolation()` method for v2-ephemeral vs v2-pool comparison
- Add `IsBlocking` field to `Phase4ThresholdCheck` (default `true`)
- Add `SameSdkIsolationChecks` list to `Phase4ModeComparison`
- Mark cross-SDK warm/throughput checks as `IsBlocking = false` in existing `Compare()` method
- Update `AllPassed` to filter on blocking checks: `checks.All(c => !c.IsBlocking || c.Passed)` AND all `SameSdkIsolationChecks` passed
- Bump `Phase4ComparisonArtifact.SchemaVersion` to `"poshmcp/v4-comparison/1.1"`
- Bump `MethodologyContract.ContractVersion` to `"poshmcp/methodology-contract/1.1"`
- Add `V2EphemeralIsolationMode` field to `MethodologyContract`
- Add appsettings for v2-ephemeral modes: `phase4-stateless-v2ephemeral.appsettings.json`, `phase4-stateful-v2ephemeral.appsettings.json`
- In `Phase4ComparisonTests.cs`: add v2-ephemeral measurement step; call `CompareSameSdkIsolation()`; assert all `SameSdkIsolationChecks` pass; log cross-SDK checks as informational without asserting

**Close #380 criteria:**
1. Same-SDK isolation gate (v2-pool vs v2-ephemeral) is GREEN: warm p95 ≤ 1.10× and throughput mean ≤ 1.10× on same-job paired run
2. Cold/memory gates still GREEN ≤ 1.10×
3. Soak handle floor ≤ 0.010/s
4. Decision C gate code merged via Amy's PR
5. Cross-SDK warm ratio documented in artifact with `IsBlocking = false`
6. Steven approves close after seeing green CI run artifact

**Do NOT:**
- Hide the cross-SDK warm ratio; it must appear in every Phase 4 artifact run
- Disable Phase 4 tests or skip `[Trait("Category", "PerformanceComparison")]`
- Use soft-bounded cross-SDK warm ceiling as blocking gate without new SDK measurement data
- Close #380 before a green CI run under Decision C gate

---

### 2026-08-06: Post-Merge Warm-Call RCA Summary
**By:** Farnsworth (Lead Architect)
**Date:** 2026-08-06
**Full RCA:** `.squad/decisions/inbox/farnsworth-post-merge-warm-rca.md`

**Summary:** Phase 4 enforce at commit `0082d50` measured warm_call_latency_ms stateless p95 = 1.271ms vs v1 baseline 0.778ms = 163% (gate required ≤105%). All #392/#393 product fixes verified merged. RCA ranked remaining costs:

| Item | Cost | Owner | Fix difficulty |
|------|------|-------|---|
| **SDK v2 HTTP transport overhead** | ~100–200µs | (none — structural) | Irreducible at app layer |
| Middleware body peek (stateless) | 15–40µs | Hermes | Low |
| OperationContext.BeginOperation | 20–80µs | Bender | Medium |
| TryGetTables reflection per call | 5–35µs | Hermes | Low |
| Drive GetAll() no count guard | 15–25µs | Hermes | Low |
| ResetPreferenceVariables (always) | 10–30µs | Hermes | Medium |
| BeginCorrelationScope unconditional | 5–10µs | Bender | Low |
| **Total achievable savings** | **~70–220µs** | | |
| **Best-case new warm call** | **~1.126ms** (still 1.45× ratio, above 1.05×) | | |

**Structural floor (PS pipeline + HTTP + SDK v2 overhead):** ~500–750µs → ratio ≈0.64×–0.96×. Reaching 1.05× requires characterizing and controlling SDK overhead (beyond app layer).

**Recommended follow-ups:**
- **Hermes PR:** Middleware stateless fast-path skip, cached table references, drive count guard
- **Bender PR:** Replace `GenerateCorrelationId()` with `context.TraceIdentifier`; guard `BeginCorrelationScope` with log-level check
- **Steven decision:** Approve Decision C gate redefinition to accept SDK tax; pursue same-SDK isolation gate as primary blocker