# Decisions

## Recent Decisions
### 2026-04-29T15:11:29Z: User directive
**By:** Steven Murawski (via Copilot)
**What:** All GitHub posts (issue creation, issue comments, PR creation, PR comments, PR reviews) MUST include the name of the agent posting it. Format: **{emoji} {AgentName} ({Role})**  at the start of the message body.
**Why:** User request - ensures traceability of which AI team member authored each GitHub interaction.

### 2026-05-02T06:39:00-05:00: User directive — progress reporting
**By:** Steven Murawski (via Copilot)
**What:** Report progress at each step of tasks: when starting something, if something significant occurs, and when ending. Applies to all agents and to the Coordinator's task narration.
**Why:** User request — captured for team memory. Improves visibility into multi-step work.

### 2026-05-02: User directive
**By:** stmuraws (via Copilot)
**What:** Never use `git pull`; always run `git fetch` and then `git rebase` from the fetched branch.
**Why:** User request — captured for team memory

# Architect Review: PR #184 — Program.cs Refactoring

**Reviewer:** Farnsworth (Lead Architect)
**Date:** 2026-05-02
**PR:** https://github.com/usepowershell/PoshMcp/pull/184
**Branch:** `squad/program-cs-refactor`

---

## Summary

PR reduces Program.cs from 2,290 → 733 lines by extracting 6 focused classes. The structural intent is correct and the individual classes are well-organized. However, the extraction approach has a critical flaw: **methods were copied into new classes but not removed from Program.cs**, creating active code duplication across 5+ files.

---

## ✅ What's Good

1. **Namespace consistency** — All 6 new classes use `namespace PoshMcp;`, matching the existing pattern from `SettingsResolver`, `ConfigurationLoader`, etc.

2. **Single entry point per class** — Each service has a clean primary method: `RunMcpServerAsync`, `RunHttpTransportServerAsync`, `RunDoctorAsync`, `SetupMcpToolsAsync`. Not a grab-bag of unrelated utilities.

3. **CliDefinition.Build() pattern** — Clean separation between CLI tree declaration and handler wiring. `SetHandler` lambdas in `Main()` are more readable with `CliDefinition` properties than inline `Option<T>` construction.

4. **Delegate injection in DoctorService** — Passing `McpToolSetupService.DiscoverToolsForCliAsync` as a `Func<>` to `DoctorService.RunDoctorAsync` avoids hard static coupling from Diagnostics layer to Server layer. Good layering instinct.

5. **Session memory discipline** — Spec was kept up to date and the worktree boundary was respected throughout.

---

## ⚠️ Concerns

1. **`CliDefinition` nullable static properties are null until `Build()` is called** — All 70+ options/commands are `Option<T>?` initialized to `null`. Callers must use `!` (null-forgiving operator) at every `SetHandler` call site. If `Build()` is ever called more than once (e.g., in tests), the mutable static state is silently replaced. Consider returning a value object from `Build()` rather than side-effecting static fields.

2. **`CliDefinition` and `CommandHandlers` are `public`** — `DoctorService`, `McpToolSetupService`, `StdioServerHost`, `HttpServerHost` are all `internal`. `CliDefinition` and `CommandHandlers` have no documented reason to be `public`. If tests need to call these, that should be via `InternalsVisibleTo`, not by widening their access to the entire assembly surface.

3. **`RegisterCleanupServices` duplication not addressed** — Noted as out of scope but worth tracking: `StdioServerHost` and `HttpServerHost` both have near-identical service registration logic. This should be extracted before the duplication compounds further.

---

## 🔴 Must Fix (blocking)

### 1. `DescribeConfigurationPath` duplicated across 5 files

This private utility method (`string DescribeConfigurationPath(string?)`) now exists independently in:
- `Program.cs`
- `DoctorService.cs`
- `CommandHandlers.cs`
- `StdioServerHost.cs`
- `HttpServerHost.cs`

Same story for `ToToolName`, `GetDiscoveredToolNames`, `GetExpectedToolNames` (exist in both `Program.cs` and `DoctorService.cs`).

**Fix:** Extract these to a shared utility class — `ConfigurationPathHelper` or inline into `ConfigurationLoader` — and delete the duplicates. This must happen before merge, or the codebase will have 5 independent copies of identical logic that will drift.

### 2. `Program.BuildDoctorReportFromConfig` / `Program.BuildDoctorJson` are not removed

The extraction created `DoctorService.BuildDoctorReportFromConfig` and `DoctorService.BuildDoctorJson` correctly. But the originals in `Program.cs` were **not removed**. Program.cs lines 251–440 are entirely duplicated in `DoctorService.cs`. Tests still call `Program.BuildDoctorReportFromConfig` — they should be updated to call `DoctorService.BuildDoctorReportFromConfig`, or Program.cs should forward to DoctorService.

This is not a 68% reduction — it is a 68% reduction in the **entry-point glue**, but the substantive logic is duplicated.

**Fix:** Either:
- (a) Remove the full implementations from `Program.cs`, update tests to call `DoctorService.BuildDoctorReportFromConfig` directly, OR
- (b) Make `Program.BuildDoctorReportFromConfig` a single-line delegation to `DoctorService.BuildDoctorReportFromConfig` (preserving test compatibility while eliminating the duplicate logic)

Option (b) is lower risk for this PR; option (a) is the correct long-term state.

---

## 💡 Recommendations (non-blocking)

1. **Add a shared `ConfigurationHelpers` static class** for `DescribeConfigurationPath`, `ToToolName`, `GetExpectedToolNames`, `GetDiscoveredToolNames`. These are used across CLI, Diagnostics, and Server layers — they need a neutral home.

2. **CliDefinition redesign consideration** — Instead of mutable static properties set during `Build()`, consider having `Build()` return a `CliSetup` record type containing the constructed `RootCommand` and all option/command references. This avoids the null-before-Build problem and makes the contract explicit.

3. **Test class naming** — Tests calling `Program.BuildDoctorReportFromConfig` directly are in `ProgramTests.cs`. Once the method moves to `DoctorService`, rename to `DoctorServiceTests.cs` for clarity.

4. **Follow-on PR should target ≤400 lines** — The ConfigurationManager extraction (~200 lines) plus cleaning up the remaining doctor helper duplicates will bring Program.cs to a reasonable boundary.

---

## Verdict: CHANGES REQUESTED

The structural direction is correct and the CliDefinition/CommandHandlers/ServerHost split is clean. The blocker is the unfinished extraction: **doctor helper methods still exist in Program.cs in full**, duplicating what's in DoctorService.cs. Fix the duplication (blocking item #2) and the utility method copies (blocking item #1) before merge. Both are addressable within 1–2 small commits.

# Root Cause: VS Code /authorize Redirect Bug

**Date:** 2026-05-02T10:11:52-05:00
**By:** Fry (Tester)
**Requested by:** Steven Murawski

## Summary

VS Code MCP client redirects the browser to:
```
https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/authorize?...
```
instead of `https://login.microsoftonline.com/.../oauth2/v2.0/authorize?...`

The container's `/authorize` returns **404 Not Found**, so the OAuth flow fails immediately.

## Evidence

### 1. AS metadata `authorization_endpoint` — CORRECT

```
GET /.well-known/oauth-authorization-server
```
```json
{
  "authorization_endpoint": "https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/oauth2/v2.0/authorize"
}
```

The AS metadata is correct. `authorization_endpoint` points directly to Entra, not the container.

### 2. Container `GET /authorize` — 404

```
GET /authorize?client_id=...&response_type=code&scope=openid&redirect_uri=...
→ 404 Not Found (no Location header)
```

No `/authorize` endpoint exists on the container.

### 3. Code review — no `/authorize` handler

`PoshMcp.Server/Authentication/OAuthProxyEndpoints.cs` registers only:
- `GET /.well-known/oauth-authorization-server`
- `POST /register`

**No `/authorize` route is registered anywhere in the codebase.**

## Root Cause

**VS Code's MCP OAuth client does not use `authorization_endpoint` from the AS metadata.**

Instead, VS Code constructs the authorization URL as:
```
{authorization_server_base_url}/authorize?<params>
```

The `authorization_server_base_url` comes from `authorization_servers[0]` in the Protected Resource Metadata (PRM):
```json
"authorization_servers": ["https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io"]
```

So VS Code builds `https://poshmcp.../authorize?...` and opens it in the browser → 404.

## Classification

**Root cause c:** The proxy `/authorize` handler is **missing entirely** from the server.

The AS metadata is correct. The bug is that VS Code doesn't read `authorization_endpoint` from the metadata — it derives `/authorize` from the authorization server base URL. Since PoshMcp is the declared authorization server (in the PRM), the container must host a working `/authorize` endpoint that proxies/redirects to Entra.

## Required Fix

Add a `GET /authorize` handler to `OAuthProxyEndpoints.cs` that:
1. Accepts all standard OAuth2 query parameters (`client_id`, `response_type`, `scope`, `redirect_uri`, `state`, `code_challenge`, `code_challenge_method`)
2. Issues a `302 Found` redirect to the real Entra `authorization_endpoint`:
   ```
   https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize?<all-params-forwarded>
   ```
3. The `client_id` in the forwarded request must be the configured Entra `ClientId` (not the DCR-issued ephemeral one), since Entra only knows about the registered app.

## Impact

All MCP clients (VS Code and others) that follow the "construct `/authorize` from authorization server base URL" pattern will fail to complete OAuth until this handler is added. The `/register` DCR flow works correctly — the failure is in step 5 of the OAuth flow (browser redirect to authorization endpoint).

# Diagnosis: MCP `initialize` Timeout — "Waiting for server to respond"

**Filed by:** Fry (Tester)
**Date:** 2026-05-02
**Deployment:** `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io`
**Symptom:** MCP client logs "Waiting for server to respond to initialize request..." every 5 seconds indefinitely after logging "Discovered authorization server metadata."

---

## Evidence Collected

### 1. Health Check — ✅ Healthy

```
GET /health → 200
{
  "status":"Healthy",
  "checks":[
    {"name":"powershell_runspace","status":"Healthy","description":"PowerShell runspace responsive"},
    {"name":"assembly_generation","status":"Healthy","description":"Assembly generation ready"},
    {"name":"configuration","status":"Healthy",
     "data":{"FunctionCount":3,"ModuleCount":1,"AuthEnabled":true,"AuthSchemes":"Bearer"}}
  ]
}
```

Server is fully up.

### 2. Unauthenticated POST to `/` (MCP initialize, no token)

```
POST / → 401 Unauthorized (response in <1ms)
WWW-Authenticate: Bearer resource_metadata="https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/.well-known/oauth-protected-resource"
```

- **HTTPS scheme is correct** — the http:// bug from v0.9.8 is fixed. ✅
- Server is reachable and responds instantly to unauthenticated requests.

### 3. GET `/sse`

```
GET /sse → 404
```

No legacy SSE transport endpoint. Server uses Streamable HTTP only (POST /). This is expected for MCP 2025-03-26+, but legacy clients trying SSE first may behave oddly.

### 4. OAuth AS Metadata — `/.well-known/oauth-authorization-server`

```json
{
  "issuer": "https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io",
  "authorization_endpoint": "https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/oauth2/v2.0/authorize",
  "token_endpoint": "https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/oauth2/v2.0/token",
  "registration_endpoint": "https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/register",
  "scopes_supported": [
    "openid","profile","email","offline_access",
    "api://80939099-d811-4488-8333-83eb0409ed53/.default"
  ],
  ...
}
```

**⚠️ CRITICAL: `issuer` is the PoshMcp URL, not the Entra ID URL.**
- The tokens issued by Entra ID have `iss = "https://login.microsoftonline.com/d91aa5af.../v2.0"`
- The AS metadata says `issuer = "https://poshmcp..."` — these do NOT match
- Some MCP clients/OAuth libraries validate that the `iss` claim in the received token matches the `issuer` in the AS metadata. This would cause the client to reject the token entirely and never send a Bearer-authenticated initialize.

**⚠️ CRITICAL: `scopes_supported` does NOT include the actually required scope.**
- AS metadata advertises: `api://80939099-d811-4488-8333-83eb0409ed53/.default`
- Server requires in `RequiredScopes`: `api://80939099-d811-4488-8333-83eb0409ed53/user_impersonation`
- PRM advertises: `api://80939099-d811-4488-8333-83eb0409ed53/user_impersonation`
- If the client uses AS metadata's `scopes_supported` to decide what scope to request, it will request `.default`, which may or may not include `user_impersonation` depending on app permissions.

### 5. Protected Resource Metadata — `/.well-known/oauth-protected-resource`

```json
{
  "resource": "api://80939099-d811-4488-8333-83eb0409ed53",
  "resource_name": "PoshMcp Server",
  "authorization_servers": ["https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io"],
  "scopes_supported": ["api://80939099-d811-4488-8333-83eb0409ed53/user_impersonation"],
  "bearer_methods_supported": ["header"]
}
```

PRM correctly advertises `user_impersonation` scope. ✅

### 6. JWT Validation Functional — ✅

```
POST / (fake Bearer token) → 401 in 457ms
```

OIDC discovery from container → `login.microsoftonline.com` works. JWT validation is not hanging. This rules out the network-timeout hypothesis.

### 7. Server Auth Logs — NO BEARER TOKENS EVER PRESENTED

From container metrics dump (72 auth attempts):
```
aspnetcore.authentication.result: none   (scheme: Bearer, count: 72)
```

`result: none` means the Bearer middleware ran but found **no token** in any of those 72 requests. There are zero `result: success` or `result: failure` entries. **The MCP client is never sending a Bearer token to the server.** This confirms the OAuth flow is failing client-side before the token is presented to the server.

### 8. Scope Claim Format Mismatch (Code Analysis)

`appsettings.json`:
```json
"RequiredScopes": ["api://80939099-d811-4488-8333-83eb0409ed53/user_impersonation"]
```

`AuthenticationServiceExtensions.cs`:
```csharp
policy.RequireClaim("scp", authConfig.DefaultPolicy.RequiredScopes.ToArray());
```

This check uses **exact match**. But Entra ID v2.0 tokens store the scope as the short name in the `scp` claim:
- **Entra token `scp` claim**: `"user_impersonation"` (just the suffix, not the full URI)
- **Server expects**: `"api://80939099-d811-4488-8333-83eb0409ed53/user_impersonation"` (full URI)

Even if the client successfully obtains a valid Entra token with `user_impersonation` consented, the scope check will **always fail** with 401 because the full URI format does not match what Entra puts in the token.

Additionally, if the token has multiple scopes (`scp = "user_impersonation offline_access"`), ASP.NET Core `RequireClaim` does an exact-value match against the full space-separated string — this would also fail even with the correct format.

---

## Root Cause Analysis

There are **two compound bugs** that together prevent the initialize from ever succeeding:

### Bug 1 (Primary — prevents token from being sent): `issuer` mismatch in AS metadata

**Location:** `OAuthProxyEndpoints.cs` line 64: `var issuer = baseUrl;`

The AS metadata `issuer` is set to the PoshMcp server URL. Entra tokens have `iss = login.microsoftonline.com/{tenantId}/v2.0`. MCP client SDKs that validate `iss == issuer` (per RFC 8414 §2) will reject the token and never send an authenticated initialize request.

This explains the log sequence:
1. Client sends initialize → 401 → discovers AS metadata ✅ ("Discovered authorization server metadata")
2. Client completes OAuth flow and gets Entra token
3. **Client SDK validates: `token.iss` (`login.microsoftonline.com`) ≠ `AS.issuer` (`poshmcp`) → token rejected**
4. Client has no valid token; retries initialize without token → 401 → cycle repeats
5. Log: "Waiting for server to respond to initialize request..." every 5s forever

**The AS metadata `issuer` should be the Entra ID issuer, or the client needs to be informed differently.**

Per RFC 8414, the `issuer` in AS metadata must be the authorization server's own identifier. Since PoshMcp is a **resource server with an OAuth proxy** (not a true AS), the `issuer` should ideally be the Entra ID issuer. However, the PRM's `authorization_servers` points to `https://poshmcp...`, so the client fetches the AS metadata from PoshMcp — creating a proxy relationship where `issuer` must logically be the Entra issuer for token validation to work.

### Bug 2 (Secondary — ensures 401 even if token is presented): Scope format mismatch

**Location:** `appsettings.json` `RequiredScopes` configuration

`RequiredScopes` uses the full scope URI `api://80939099.../user_impersonation`. Entra v2.0 tokens have `scp = "user_impersonation"` (short name only). Even if Bug 1 is fixed and the client sends the Entra token, the scope check will still fail with 401.

**Fix options:**
- Change `RequiredScopes` to `["user_impersonation"]` (short name), OR
- Add custom scope claim parsing that extracts the scope short name from the full URI, OR
- Use Microsoft.Identity.Web's `ScopeAuthorizationRequirement` which handles Entra scope format

---

## What IS Working

| Check | Status |
|-------|--------|
| Server health | ✅ Healthy |
| WWW-Authenticate scheme (https://) | ✅ Fixed vs v0.9.8 |
| JWT OIDC discovery reachability | ✅ Working (457ms) |
| PRM scopes_supported format | ✅ Has correct `user_impersonation` |
| AS metadata auth/token endpoints | ✅ Correct Entra endpoints |

---

## Recommended Fixes (for the team to implement)

### Fix 1 — AS metadata `issuer` (High Priority)

In `OAuthProxyEndpoints.cs`, change `issuer` from the PoshMcp base URL to the Entra ID issuer:

```csharp
// Before:
var issuer = baseUrl;

// After:
var entraBase = string.Format(EntraV2BaseTemplate, proxy.TenantId);
var issuer = $"{entraBase}";  // e.g., "https://login.microsoftonline.com/{tenantId}/oauth2/v2.0"
// Or more precisely:
var issuer = $"https://login.microsoftonline.com/{proxy.TenantId}/v2.0";
```

This makes the `issuer` in AS metadata match the `iss` claim in Entra-issued tokens.

### Fix 2 — Scope format in RequiredScopes (High Priority)

Change `appsettings.json` (and documentation) so `RequiredScopes` uses the short scope name:

```json
"DefaultPolicy": {
  "RequireAuthentication": true,
  "RequiredScopes": ["user_impersonation"]
}
```

Or alternatively, add scope claim splitting logic so `RequireClaim("scp", "user_impersonation")` works when `scp = "user_impersonation offline_access"`.

### Fix 3 — Add `user_impersonation` to AS metadata `scopes_supported` (Medium Priority)

The AS metadata `scopes_supported` should advertise the scopes the client needs to request. Currently it only has `.default`. Add the delegated scope explicitly, or populate from `ProtectedResource.ScopesSupported`.

---

## Files to Investigate

- `PoshMcp.Server/Authentication/OAuthProxyEndpoints.cs` — issuer generation (line 64)
- `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs` — RequireClaim scope check
- `appsettings.json` — RequiredScopes value format

# Decision: OAuth Redirect Validation — Live Endpoint Diagnosis

**Date:** 2026-05-02
**Author:** Fry (Tester)
**Reviewers:** Amy (deploy/env vars), Bender (code), Farnsworth (oversight)
**Status:** OPEN — awaiting fix assignment

---

## Context

v0.9.5 shipped OAuth AS proxy + DCR proxy (`OAuthProxyEndpoints.cs`) to enable VS Code MCP clients to authenticate without manual client_id entry. Steven reports that connecting to the live Container App still does NOT redirect to `login.microsoftonline.com`.

Live endpoint: `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io`

---

## Findings Summary

### What IS working
- `/health` → 200, all checks healthy, `AuthEnabled: true`
- `/.well-known/oauth-protected-resource` → 200 (returns data)
- Auth enforcement → 401 with `WWW-Authenticate: Bearer resource_metadata=...`
- Image deployed today (`psbamiacr.azurecr.io/advocacybami:20260502-061835`, revision `poshmcp--0000019`, active)

### What is BROKEN

**Primary failure:** `/.well-known/oauth-authorization-server` → **404**

The OAuth proxy endpoint is not registered because `OAuthProxy.Enabled = false` in the running container. The code in `OAuthProxyEndpoints.MapOAuthProxyEndpoints` returns early when `proxy.Enabled == false`.

**Root cause:** None of the 4 required env vars are set on the Container App:
```
❌ Authentication__OAuthProxy__Enabled    (not set)
❌ Authentication__OAuthProxy__TenantId   (not set)
❌ Authentication__OAuthProxy__ClientId   (not set)
❌ Authentication__OAuthProxy__Audience   (not set)
```

Confirmed via: `az containerapp revision show -n poshmcp -g rg-poshmcp --revision poshmcp--0000019`

**Secondary failure:** PRM (`/.well-known/oauth-protected-resource`) advertises Entra directly

Because `OAuthProxy.Enabled = false`, the PRM does NOT inject the proxy URL as the authorization server. Instead, it returns a hardcoded Entra URL from `ProtectedResource.AuthorizationServers`. VS Code then tries `https://login.microsoftonline.com/{tenant}/.well-known/oauth-authorization-server` → **404** (Entra serves OIDC metadata, not RFC 8414 AS metadata). No `registration_endpoint` is available → VS Code cannot do DCR → no `client_id` → no OAuth redirect → **login.microsoftonline.com never triggered**.

**Tertiary defect (Bender):** `WWW-Authenticate` header uses `http://` instead of `https://`

`AuthenticationServiceExtensions.cs:60` builds `metadataUrl` from `req.Scheme` without honoring `X-Forwarded-Proto`. Azure Container Apps terminates TLS at the ingress, so the app sees `http`. The correct pattern (already used in `OAuthProxyEndpoints.cs::GetServerBaseUrl`) is:
```csharp
var scheme = req.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? req.Scheme;
var host = req.Headers["X-Forwarded-Host"].FirstOrDefault() ?? req.Host.ToUriComponent();
var metadataUrl = $"{scheme}://{host}/.well-known/oauth-protected-resource";
```

**Quaternary defect (investigate):** PRM arrays are duplicated

`authorization_servers`, `scopes_supported` each appear twice; `bearer_methods_supported` appears 3×. Likely caused by non-empty `ProtectedResource.AuthorizationServers` in the baked-in appsettings PLUS another config source (appsettings.Production.json or old env vars). Needs investigation to confirm source; clearing extra config sources should fix.

---

## VS Code Client Flow (Simulated)

```
GET /.well-known/oauth-protected-resource
  → authorization_servers[0] = https://login.microsoftonline.com/{tenant}

GET https://login.microsoftonline.com/{tenant}/.well-known/oauth-authorization-server
  → 404 (Entra does not serve RFC 8414 AS metadata here)

GET https://login.microsoftonline.com/{tenant}/.well-known/openid-configuration
  → 200, registration_endpoint = null (Entra doesn't support DCR)

⛔ No registration_endpoint → no DCR → no client_id → no OAuth flow → no redirect
```

---

## Recommended Actions

### 🔴 Amy — IMMEDIATE (no redeploy needed)

Set the 4 missing env vars on the Container App:

```bash
az containerapp update -n poshmcp -g rg-poshmcp \
  --set-env-vars \
    "Authentication__OAuthProxy__Enabled=true" \
    "Authentication__OAuthProxy__TenantId=d91aa5af-8c1e-442c-b77c-0b92988b387b" \
    "Authentication__OAuthProxy__ClientId=80939099-d811-4488-8333-83eb0409ed53" \
    "Authentication__OAuthProxy__Audience=api://80939099-d811-4488-8333-83eb0409ed53"
```

Also investigate/remove any `Authentication__ProtectedResource__AuthorizationServers__*` env vars that may be contributing to array duplication.

**Expected result after fix:**
- `/.well-known/oauth-authorization-server` → 200 (proxy metadata)
- `/.well-known/oauth-protected-resource` `authorization_servers[0]` → `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io` (the proxy)
- VS Code fetches AS metadata from the proxy → gets `authorization_endpoint`, `token_endpoint`, `registration_endpoint`
- VS Code POSTs `/register` → gets `client_id = 80939099-d811-4488-8333-83eb0409ed53`
- VS Code redirects to `https://login.microsoftonline.com/{tenant}/oauth2/v2.0/authorize` → login prompt appears ✅

**Deploy process fix:** Update the deployment process to invoke `deploy.ps1 -ServerAppSettingsFile ./appsettings.json` rather than a bare `az containerapp update --image ...`. The deploy.ps1 `ConvertTo-McpServerEnvVars` function correctly translates the appsettings into Container App env vars.

### 🟡 Bender — CODE FIX (low urgency, no user-visible impact until proxy works)

Fix `AuthenticationServiceExtensions.cs:60` `OnChallenge` handler to use `X-Forwarded-Proto`:

```csharp
// Before:
var metadataUrl = $"{req.Scheme}://{req.Host}/.well-known/oauth-protected-resource";

// After:
var scheme = req.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? req.Scheme;
var host = req.Headers["X-Forwarded-Host"].FirstOrDefault()
           ?? req.Host.ToUriComponent();
var metadataUrl = $"{scheme}://{host}/.well-known/oauth-protected-resource";
```

### 🟡 Bender — INVESTIGATE array duplication

Determine why `authorization_servers`, `scopes_supported`, and `bearer_methods_supported` appear 2–3× in the PRM response. Check for stale env vars or `appsettings.Production.json` in the image. Fix to ensure exactly one copy of each value.

---

## Decision

**Root cause is configuration, not code.** v0.9.5 code is deployed and correct. The fix is Amy setting 4 env vars on the Container App — no rebuild or code change required for the primary issue.

Bender should address the secondary `http://` and array-duplication bugs in a follow-up commit.

# v0.9.10 OAuth Fix Validation — AdvocacyBami Deployment

**Date:** 2026-05-02T10:02:31-05:00
**Tester:** Fry
**Deployment:** `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io`
**Release:** v0.9.10

## Summary

```
✅ Check 1: Health — 200 Healthy
✅ Check 2: issuer field — https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/v2.0 (expected: https://login.microsoftonline.com/d91aa5af-.../v2.0)
✅ Check 3: PRM — 200 OK, authorization_servers uses https://
✅ Check 4: WWW-Authenticate scheme — https://
✅ Check 5: DCR /register — client_id: 80939099-d811-4488-8333-83eb0409ed53

Overall: PASS
Root cause bugs fixed: Yes (Bug 1 confirmed; Bug 2 deployed, not directly observable without real Entra token)
```

---

## Check 1: Health

**Request:** `GET /health`
**HTTP Status:** 200
**Body:**
```json
{
  "status": "Healthy",
  "checks": [
    {"name": "powershell_runspace", "status": "Healthy"},
    {"name": "assembly_generation", "status": "Healthy"},
    {"name": "configuration", "status": "Healthy",
     "data": {"FunctionCount": 3, "ModuleCount": 1, "AuthEnabled": true, "AuthSchemes": "Bearer"}}
  ]
}
```
**Result:** ✅ PASS — Server healthy, auth enabled, 3 functions registered.

---

## Check 2: OAuth AS Metadata — issuer field (PRIMARY FIX VALIDATION)

**Request:** `GET /.well-known/oauth-authorization-server`
**HTTP Status:** 200

**Key fields:**
| Field | Value |
|-------|-------|
| `issuer` | `https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/v2.0` |
| `authorization_endpoint` | `https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/oauth2/v2.0/authorize` |
| `token_endpoint` | `https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/oauth2/v2.0/token` |
| `registration_endpoint` | `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/register` |

**Result:** ✅ PASS — `issuer` is now `https://login.microsoftonline.com/{tenantId}/v2.0` (Bug 1 fix confirmed). Previously the issuer was the server's own URL, which caused MCP client SDK to reject tokens (iss ≠ issuer). All endpoints point to `login.microsoftonline.com`. `registration_endpoint` is present.

---

## Check 3: Protected Resource Metadata

**Request:** `GET /.well-known/oauth-protected-resource`
**HTTP Status:** 200
**Body:**
```json
{
  "resource": "api://80939099-d811-4488-8333-83eb0409ed53",
  "resource_name": "PoshMcp Server",
  "authorization_servers": ["https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io"],
  "scopes_supported": ["api://80939099-d811-4488-8333-83eb0409ed53/user_impersonation", "api://80939099-d811-4488-8333-83eb0409ed53/user_impersonation"],
  "bearer_methods_supported": ["header", "header"]
}
```

**Result:** ✅ PASS — `authorization_servers` uses `https://` (not `http://`). The `http://` scheme bug (v0.9.8) is still fixed.

**⚠️ Minor observation:** `scopes_supported` and `bearer_methods_supported` both contain duplicate entries. Not a blocking issue but worth noting as a future cleanup item.

---

## Check 4: WWW-Authenticate Header

**Request:** `GET /` (unauthenticated)
**HTTP Status:** 401
**WWW-Authenticate header:**
```
Bearer resource_metadata="https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/.well-known/oauth-protected-resource"
```

**Result:** ✅ PASS — Returns 401 (not redirect), scheme is `https://` (not `http://`). MCP clients following this URL will get the PRM over HTTPS.

---

## Check 5: DCR /register Endpoint

**Request:** `POST /register` with `Content-Type: application/json` body `{}`
**HTTP Status:** 201
**Body:**
```json
{
  "client_id": "80939099-d811-4488-8333-83eb0409ed53",
  "client_id_issued_at": 1777734205,
  "token_endpoint_auth_method": "none"
}
```

**Result:** ✅ PASS — Returns 201 with correct Entra `client_id` `80939099-d811-4488-8333-83eb0409ed53`.

---

## Bug Fix Validation Assessment

### Bug 1: issuer mismatch (OAuthProxyEndpoints.cs)
**Status: ✅ CONFIRMED FIXED**
The `issuer` field now returns `https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/v2.0` exactly as required. MCP client SDKs that validate `iss == issuer` will now accept Entra tokens and proceed to send Bearer tokens in subsequent requests.

### Bug 2: scope format (AdvocacyBami/appsettings.json)
**Status: ✅ DEPLOYED (indirect confirmation)**
The `RequiredScopes` change from `["api://80939099.../user_impersonation"]` to `["user_impersonation"]` cannot be directly validated without a real Entra Bearer token. The deployment is live and Bug 1 is fixed, so the full end-to-end flow (token acquisition + scope check) can now be tested with a real MCP client. The health check confirms `AuthEnabled: true` with correct configuration.

---

## Regression Check

- HTTP → HTTPS scheme fix (v0.9.8): ✅ Still holding (Checks 3 and 4)
- DCR proxy: ✅ Still working (Check 5)
- Server health: ✅ Healthy with auth enabled (Check 1)

No regressions observed.

# Fry — v0.9.8 Deployment Verification Findings
**Date:** 2026-05-02
**Deployment:** https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io
**Image:** PoshMcp v0.9.8 (AdvocacyBami rebuild)

## Summary

| Check | Result | Notes |
|-------|--------|-------|
| 1. Health | ✅ PASS | All 3 sub-checks Healthy |
| 2. OAuth AS Metadata | ✅ PASS | Both endpoints → login.microsoftonline.com |
| 3. Protected Resource Metadata | ⚠️ PARTIAL | `resource` is `api://` URI, not container URL; rest is correct |
| 4. Dynamic Client Registration | ✅ PASS | 201 with correct client_id |
| 5. MCP Endpoint Reachability | ⚠️ ISSUE | `resource_metadata` URL uses `http://` instead of `https://` |

## Detailed Findings

### CHECK 1: Health — ✅ PASS
- **Status:** 200 OK
- **All checks healthy:** `powershell_runspace`, `assembly_generation`, `configuration`
- Configuration: 3 functions, 1 module, Auth enabled (Bearer)

### CHECK 2: OAuth Authorization Server Metadata (RFC 8414) — ✅ PASS
- **Status:** 200 OK
- **issuer:** `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io` ✅
- **authorization_endpoint:** `https://login.microsoftonline.com/d91aa5af-.../oauth2/v2.0/authorize` ✅ (NOT the container URL)
- **token_endpoint:** `https://login.microsoftonline.com/d91aa5af-.../oauth2/v2.0/token` ✅
- **registration_endpoint:** `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/register` ✅
- Scopes, grant types, PKCE all populated correctly.

### CHECK 3: Protected Resource Metadata (RFC 9728) — ⚠️ PARTIAL PASS
- **Status:** 200 OK
- **authorization_servers:** 1 entry (no duplicates) ✅
- **bearer_methods_supported:** `["header"]` (exactly 1, no duplicates) ✅
- **scopes_supported:** 1 entry, no duplicates ✅
- **⚠️ ISSUE — `resource` field:**
  - **Actual:** `"api://80939099-d811-4488-8333-83eb0409ed53"` (Entra app ID URI)
  - **Expected per task spec:** the container URL (`https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io`)
  - RFC 9728 allows either form; the app ID URI is valid for Entra-protected resources. Not a hard failure, but worth noting if MCP clients resolve `resource` to discover the server URL.

### CHECK 4: Dynamic Client Registration — ✅ PASS
- **Status:** 201 Created ✅ (task accepts 200 or 201)
- **client_id:** `80939099-d811-4488-8333-83eb0409ed53` ✅ (configured Entra app client ID)
- Response also includes `client_id_issued_at` and `token_endpoint_auth_method: none`.

### CHECK 5: MCP Endpoint Reachability — ⚠️ ISSUE
- **Status:** 401 Unauthorized ✅ (NOT a redirect to /authorize — the core OAuth fix is working)
- **WWW-Authenticate header present:** ✅
- **⚠️ ISSUE — `http://` in resource_metadata:**
  - **Actual:** `WWW-Authenticate: Bearer resource_metadata="http://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/.well-known/oauth-protected-resource"`
  - **Expected:** `https://` (the container serves HTTPS; `http://` reference in the header will cause MCP clients to attempt an insecure fetch, which will either fail or be redirected)
  - This is a configuration/code bug — the server is generating the `resource_metadata` URL with the wrong scheme.

## Recommended Actions

1. **Check 5 (`http://` in resource_metadata)** — **HIGH PRIORITY:** The `WWW-Authenticate: Bearer resource_metadata` URL must use `https://`. MCP clients (e.g., Claude Desktop, VS Code extension) follow this URL to discover OAuth metadata; an `http://` reference may fail TLS validation or get rejected. Investigate how the resource metadata URL is constructed — likely the app is reading `HttpContext.Request.Scheme` or a configured base URL that is resolving as `http` behind the Azure Container Apps reverse proxy. Fix: ensure `X-Forwarded-Proto` is honored, or hardcode the scheme from configuration.

2. **Check 3 (`resource` URI)** — **LOW PRIORITY / INFORMATIONAL:** `resource` = `api://80939099-...` is valid per RFC 9728 for Entra-protected APIs. No action required unless client tooling specifically expects the container HTTPS URL here.

# Decision: `MapInboundClaims = false` is Required; No `scope` in VS Code mcp.json

**By:** Leela (Developer Advocate)
**Date:** 2026-05-03
**Status:** Proposed

## Summary

Two requirements are now documented in `docs/entra-id-oauth-implementation-guide.md` as a result of live debugging sessions:

### 1. `MapInboundClaims = false` is a documented requirement

ASP.NET Core's JWT Bearer middleware remaps short JWT claim names (`scp`, `roles`) to long WS-Federation URI forms by default. This causes authorization policies that check for `scp` or `roles` by short name to silently fail — the token is valid, the claim is present, but it is stored under the wrong key in `ClaimsPrincipal`.

**Rule:** `options.MapInboundClaims = false` must always be set when configuring JWT Bearer authentication in PoshMcp. `TokenValidationParameters.RoleClaimType` must be set explicitly to the configured role claim short name so that `IsInRole()` continues to work.

This is implemented in `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs` and is now validated in the deployment checklist.

### 2. VS Code `mcp.json` must not include a `scope` field

An explicit `scope` field in VS Code's `mcp.json` causes VS Code's MCP auth provider to silently fail token acquisition — no `Authorization` header is sent, every request hits `DenyAnonymousAuthorizationRequirement`, and no useful error is surfaced to the user.

**Rule:** Do not set `scope` in VS Code's `mcp.json` for PoshMcp connections. Let VS Code read `scopes_supported` from the server's Protected Resource Metadata at `/.well-known/oauth-protected-resource` and handle scope selection automatically.

## Documentation

Both findings are documented in `docs/entra-id-oauth-implementation-guide.md`:
- Bug 5: `MapInboundClaims = false` — in the "Bugs We Hit and Why" section
- VS Code client gotcha — in the new "VS Code MCP Client Configuration Gotchas" section
- Validation Checklist updated with `MapInboundClaims` check
- Summary updated with lessons 5 and 6
### 2026-05-03: Release v0.9.20 — Authentication Fixes
**By:** Amy (DevOps / Platform / Azure Engineer)
**Status:** Completed
**What:** Cut patch release v0.9.20 (commit b87ca27) capturing three auth fixes and a diagnostics consistency improvement: (1) HasRequiredRoles uses .Any() (OR semantics) for Entra app roles; (2) MapInboundClaims=false on JWT bearer to preserve short claim names; (3) RequiredScopes now uses short scope name (user_impersonation) matching JWT scp claim; (4) DoctorReport.cs uses FindAll(`"roles"`) consistent with MapInboundClaims=false. Bumped PoshMcp.csproj 0.9.19→0.9.20, prepended CHANGELOG, committed with Copilot co-author trailer, lightweight tag v0.9.20.
**Why:** Production Entra OAuth flows were failing due to claim-mapping and role-semantics mismatches. CI/CD auto-publishes to NuGet + GHCR on tag push.

### 2026-05-03: RequiredRoles Uses OR Semantics
**By:** Bender (Backend Developer)
**Status:** Accepted
**What:** Changed AuthorizationHelpers.HasRequiredRoles from .All() (AND) to .Any() (OR). User satisfies the check if they hold any one of the listed roles. ToolAuthorizationFilter and ToolListAuthorizationFilter inherit the corrected semantics automatically.
**Why:** Aligns with (1) Entra app roles being granted one-at-a-time, (2) ASP.NET Core's policy.RequireRole(string[]) which uses OR, (3) explicit product intent. AND semantics is no longer achievable via RequiredRoles — would need nested policies or custom claims.

### 2026-05-03: Fix DoctorReportTests role claim type for MapInboundClaims=false
**By:** Fry (Tester)
**Status:** Accepted
**Commit:** e64b800
**What:** In DoctorReportTests.Build_WithAuthenticatedIdentity_PopulatesIdentitySection, changed `new Claim(ClaimTypes.Role, "admin")` to `new Claim("roles", "admin")`. Single occurrence; no other tests affected.
**Why:** DoctorReport.cs (commit 8c8e4ad) switched to FindAll(`"roles"`) to match MapInboundClaims=false behavior. Test fixtures must mirror the production claim-name form. Result: failing test now passes; full unit suite 582 passed / 1 skipped / 0 failed.
**Rule Going Forward:** Future tests building role claims for DoctorReport validation must use `"roles"` as the claim type, not ClaimTypes.Role.

### 2026-05-03: Release v0.9.21 — Test Fix for DoctorReport Role Claim
**By:** Amy (DevOps)
**Status:** Released
**What:** Patch release v0.9.21 capturing the DoctorReportTests fix from commit e64b800. Verified PoshMcp.csproj already at 0.9.21, CHANGELOG entry present, commit 2ad3739 with Copilot co-author trailer, tag v0.9.21 pushed to origin/main. Pre-release quality gates (dotnet format --verify-no-changes, dotnet test --filter "Category!=Integration" --no-build) both PASS.
**Why:** Ship the DoctorReportTests claim-name fix that was broken by v0.9.20's MapInboundClaims=false change. CI/CD auto-publishes NuGet + GHCR on tag push.

### 2026-05-03: Release-Process Skill — Mandatory Quality Gates
**By:** Leela (Developer Advocate)
**Status:** Approved
**What:** Updated .squad/skills/release-process/SKILL.md to make `dotnet format --verify-no-changes` and `dotnet test` MANDATORY pre-commit steps. Inserted as Step 4 between "Update changelog" and "Leela owns release notes." Renumbered subsequent steps (old 4–9 → new 5–10). Updated YAML description, added anti-pattern `"❌ Pushing a release without running dotnet test first"`, added recovery instructions ("If either command fails, fix the issue first and restart from step 2.").
**Why:** v0.9.20 was pushed and tagged without running dotnet test locally; a failing test was discovered post-release, forcing a v0.9.21 hotfix. Local quality gates shift-left testing, catch failures faster than CI, and become part of the human-executable checklist instead of being buried in CI docs.
**Rule Going Forward:** Release process must run format+test gates locally before commit/tag/push. No exceptions.

### 2026-05-05: Systemic future-dated entries across squad artifacts
**By:** Cubert (Fact Checker)
**Requested by:** Steven Murawski
**Status:** Flagged
**What:** Multiple squad artifacts contain entries dated 2026-07-15, 2026-07-18, 2026-07-28, 2026-07-30 — 2–3 months in the future relative to current date 2026-05-05. Affected: docs/articles/squad-work-log.md (Hermes 2026-07-15/18, Fry 2026-07-15/18, Bender 2026-07-30); .squad/decisions.md (multiple 2026-07-18 and 2026-07-28 entries); .squad/agents/farnsworth/history-archive.md (references to 2026-07-29); story article sample timestamp "2026-07-30T12:34:56Z".
**Why:** Either clerical errors or the project has been silently writing future dates for months. Either way, the integrity of the dated decision ledger is compromised — readers cannot trust chronology. Blocks publication of squad-story.md and squad-work-log.md.
**Recommendation:** (1) Audit git commit dates against `### YYYY-MM-DD` headers in .squad/decisions.md and agent histories; correct headers where they disagree with commit dates. (2) Re-affirm rule already in squad.agent.md: agents must use the CURRENT_DATETIME injected by the Coordinator, never an inferred or guessed date. (3) Document corrected dates in a follow-up decision once audit is complete.
### 2026-05-05: User directive — Cubert pre-reviews Farnsworth plans
**By:** Steven Murawski (via Copilot)
**What:** Cubert (Fact Checker) must review any plans, specs, or proposals Farnsworth creates before they are presented to the user for review. Cubert verifies accuracy, internal consistency, and any verifiable claims; only after Cubert's review does the plan reach the user.
**Why:** User request — captured for team memory. Inserts a fact-checking gate into the architecture proposal workflow.


### 2026-05-06: Hermes — Runspace pool vs multi-process experiment plan (Issue #65)

**By:** Hermes (PowerShell Expert)
**What:** Filed R&D plan at `specs/004-out-of-process-execution/runspace-pool-experiment-plan.md` covering two prototype paths for OOP parallelism: (Option A) a runspace pool inside one pwsh subprocess with a synchronized stdout writer and ISS-based pre-warm; (Option B) a pool of N subprocesses dispatched via a `Channel<OutOfProcessHost>` queue. Plan includes a benchmark harness design (BenchmarkDotNet + custom crash/recovery harness) with scenarios for CPU-light, CPU-bound, I/O-bound, network-shaped, heavy serialization, cold start, crash recovery, and isolation. Recommended phasing into 6 follow-up issues, starting with extracting `OutOfProcessHost` as shared infrastructure.
**Why:** Issue #65 asks us to compare in-process runspace pooling vs multiple processes for OOP execution. A written plan is needed before either prototype is built so the trade-offs (parallelism vs isolation, memory cost, startup cost, complexity) are explicit and the benchmark methodology is fixed in advance. The single biggest open trade-off is failure containment: Option A loses the strong isolation that motivated OOP in the first place, so the benchmark harness explicitly measures isolation as a pass/fail criterion.


### 2026-05-05: Squad story / work-log fact-check corrections
**By:** Leela (via Cubert verification)
**What:** Updated `docs/articles/squad-story.md` and `docs/articles/squad-work-log.md` with verified counts:
- Team size: 8 → 9 (Cubert added)
- NuGet downloads: 700+ → 1,600+
- PRs merged (window 2026-03-27..2026-04-25): "10+" → 34 (verified via `gh pr list --repo usepowershell/PoshMcp --state merged`)
- Issues closed (same window): 27 → 83 (verified via `gh issue list --state closed`)
- Commits to main (same window): "40+" → 183 (verified via `git log main`)
- Documentation: "8 articles, 12,000+ words" → "19 articles" (word count unverified, dropped)
- "0 broken builds" → "0 reverts" (matches what was actually verified)

**Why:** Story metrics were significantly understated and the team-size/article counts were stale after Cubert joined and docs grew. Numbers now match reproducible `gh`/`git` queries.


### 2026-05-06: Spec 004 milestone + 8 follow-up issues filed

**By:** Hermes (PowerShell Expert), at Steven Murawski's request
**What:** Merged PR #187 (runspace pool vs multi-process experiment plan) into `main` (squash merge, branch deleted, issue #65 referenced via `Refs #65` so it stays open). Created milestone **#5 — `Spec 004 - Out-of-Process PowerShell Execution`** (https://github.com/usepowershell/PoshMcp/milestone/5) and filed 8 follow-up issues from the plan's §5 phasing, all in the milestone with proper `Blocked by` cross-references and `squad:*` routing labels.

**Issues created:**

| # | Title | Plan ref | Owner label | Blocked by |
|---|-------|----------|-------------|------------|
| #189 | OOP: Bug-fix — clear `$Error` before invoke in single-runspace host | §5 #0 | squad:hermes | — |
| #190 | OOP: Extract `OutOfProcessHost` (with lifecycle unit tests) | §5 #1 | squad:bender | — |
| #191 | OOP: Option A prototype — runspace pool host (`SubprocessHostMode: "Pool"`) | §5 #2 | squad:hermes | #190 |
| #192 | OOP: Option B prototype — process pool executor (`SubprocessHostMode: "ProcessPool"`) | §5 #3 | squad:bender | #190 |
| #193 | OOP: Benchmark harness infrastructure (`PoshMcp.Benchmarks` project) | §5 #4a | squad:fry | — |
| #194 | OOP: Wire benchmark harness to executors | §5 #4b | squad:fry | #191, #192, #193 |
| #195 | OOP: Run benchmarks and write findings | §5 #5 | squad:hermes | #194 |
| #196 | OOP: Adopt the winner — make recommended mode default | §5 #6 | squad:farnsworth | #195 |

**Why:** Land the experiment plan, set up actionable follow-ups under a single milestone so the runspace-pool vs multi-process work can proceed without losing the dependency ordering. Issue #65 stays open as the umbrella tracker through prototype work; commented there with the milestone and issue list.

**Side effects:**
- Created two missing labels: `refactor` (`#D4E5F7`), `testing` (`#BFD4F2`).
- Auth workaround used throughout: `gh auth switch --user usepowershell` for write ops, switched back to `stmuraws_microsoft` after.


### 2026-05-06: Security policy
**By:** Farnsworth (requested by Steven Murawski)
**What:** Added `SECURITY.md` at repo root. Supported versions: only latest 0.x minor (currently 0.10.x); older minors unsupported. Reporting channel: GitHub private vulnerability reporting via Security tab — no security email address invented. Documented SLA (ack 3 business days, triage 7), coordinated disclosure, and reporter credit via GHSA.
**Why:** Establish a clear, standard security disclosure process before 1.0; align with GitHub's recommended private vuln reporting flow rather than ad-hoc email.
