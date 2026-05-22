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