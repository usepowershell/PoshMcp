# Fry Work History

## Recent Work (2026-05-02)

### 2026-05-02T11:12: OAuth Token Integration Testing — Bearer Token Hang Investigation

**Deployment:** `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io` (v0.9.11)  
**Task:** Investigate why VS Code's `initialize` request with Bearer token hangs instead of completing.

**Tests Executed:**

| Test | Request | Status | Response Time | Finding |
|------|---------|--------|----------------|---------|
| 1. Real token (az cli) | N/A | ⚠️ UNAVAILABLE | N/A | `az account get-access-token --resource "api://80939099..."` returned no token. User must be authenticated to Entra/Azure to test with real token. |
| 2. Invalid JWT signature | Bearer with fake JWT | ✅ **401** | 548ms | Server correctly rejects malformed token **immediately** (not hanging) |
| 3. No auth header | (none) | ✅ **401** | 390ms | Server returns 401 immediately with `WWW-Authenticate: Bearer resource_metadata="https://..."` |
| 4. Verbose request (no auth) | POST initialize (no auth) | ✅ **401** | 369ms | Server responds quickly, not hanging |

**Metadata Endpoints:**

**Authorization Server (AS) metadata:**
- ✅ `issuer`: `https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/v2.0`
- ✅ `authorization_endpoint`: `https://login.microsoftonline.com/...` (correct)
- ✅ `token_endpoint`: `https://login.microsoftonline.com/...` (correct)
- ✅ `scopes_supported`:
  - `openid`
  - `profile`
  - `email`
  - `offline_access`
  - `api://80939099-d811-4488-8333-83eb0409ed53/.default`

**Protected Resource Metadata (PRM):**
- ✅ `resource`: `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io`
- ✅ `scopes_supported`: `["api://80939099-d811-4488-8333-83eb0409ed53/user_impersonation"]`
- ✅ `bearer_methods_supported`: `["header"]`
- ⚠️ **NOTE:** PRM `resource` field changed from v0.9.10 (was `api://...URI`) to v0.9.11 (now `https://...container URL`). This is a semantic change.

**Health Check:**
- ✅ 200 OK, all sub-checks healthy
- ✅ `AuthEnabled=True`
- ✅ PowerShell runspace responsive
- ✅ Assembly generation ready

**Key Findings:**

1. **Server responds IMMEDIATELY to invalid tokens** — not hanging. Auth middleware is working correctly (357–548ms response times).
2. **Token was never rejected with a hang** — 401 comes back quickly whether token is invalid, malformed, or missing.
3. **Both Bug 1 & Bug 2 were already fixed in v0.9.10-11:**
   - ✅ `issuer` now correct (`https://login.microsoftonline.com/...` not container URL)
   - ✅ `scopes_supported` in PRM correctly shows short scope name (`user_impersonation`)
4. **v0.9.11 released successfully** — all health checks green.

**Possible root cause of VS Code hang:**

The issue **may not be on the server side**. Observations:
- Server auth middleware works correctly and responds immediately
- AS metadata and PRM metadata are correctly configured
- Invalid tokens are rejected instantly

**Hypothesis:** The hang is likely on the **VS Code client side**:
- VS Code may not have a valid token to send (stuck in auth flow)
- VS Code may be stuck in metadata discovery loop (issue mentioned in v0.9.10 notes)
- VS Code may be stuck waiting for a different endpoint or format (SSE vs. HTTP streaming)
- Network connectivity / timeout between VS Code and server

**Next steps needed (for Steven):**
1. Obtain a **real token** from Entra for this app registration (requires Azure CLI authentication or device flow)
2. Test `initialize` with real token using curl: `curl -H "Authorization: Bearer $REAL_TOKEN" https://poshmcp...`
3. Check VS Code detailed logs for what metadata endpoint is hanging
4. Verify VS Code can reach all `.well-known` endpoints (metadata discovery)

**Unable to test with real token** due to Azure CLI not being authenticated in this environment. Testing with fake JWT validates server response behavior only.

---

### 2026-05-02T10:34: V0.9.11 Deployment & PRM Circular Reference Investigation

**Task:** Investigate two issues reported by Steven:
1. VS Code client error: "Failed to fetch resource metadata from all attempted URLs"
2. Verify v0.9.11 deployment status and `/authorize` endpoint

**Deployment:** `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io`
**CI Status:** v0.9.11 release completed successfully (~15:27 local time)

#### Issue 1: PRM Endpoint Analysis

**Endpoint:** `/.well-known/oauth-protected-resource`
**Status:** ✅ 200 OK

**Response:**
```json
{
  "resource": "api://80939099-d811-4488-8333-83eb0409ed53",
  "resource_name": "PoshMcp Server",
  "authorization_servers": [
    "https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io"
  ],
  "scopes_supported": [
    "api://80939099-d811-4488-8333-83eb0409ed53/user_impersonation",
    "api://80939099-d811-4488-8333-83eb0409ed53/user_impersonation"
  ],
  "bearer_methods_supported": [
    "header",
    "header"
  ]
}
```

**ROOT CAUSE IDENTIFIED:** ⚠️ **Circular Reference in `authorization_servers`**

The `authorization_servers` array contains the container's own URL (`https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io`), which causes VS Code's OAuth client to:

1. Fetch PRM → gets `authorization_servers: ["https://poshmcp..."]`
2. For each authorization_server, fetch `/.well-known/oauth-protected-resource`
3. This points back to the SAME PRM endpoint → infinite loop

VS Code logs show:
```
Discovered authorization server metadata at https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/.well-known/oauth-authorization-server
Waiting for server to respond to `initialize` request... [repeating]
```

The client gets stuck in metadata discovery loop instead of proceeding to `initialize`.

**Expected behavior:** `authorization_servers` should be EMPTY `[]` or list **only Entra's** Azure AD tenant URL. The container is an OAuth proxy, not an authorization server itself.

#### Issue 2: v0.9.11 Deployment & /authorize Endpoint

**Health Endpoint:** ✅ 200 OK
**Status:** `Healthy`
**Checks:**
- PowerShell runspace: Healthy ✅
- Assembly generation: Healthy ✅
- Configuration: Healthy (AuthEnabled=True, FunctionCount=3) ✅

**Authorize Endpoint Test:**
```
GET /authorize?client_id=test&response_type=code&scope=openid&redirect_uri=http://127.0.0.1:9999/
Status: 302 Found
Location: https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/oauth2/v2.0/authorize?client_id=80939099-d811-4488-8333-83eb0409ed53&response_type=code&scope=openid&redirect_uri=http%3A%2F%2F127.0.0.1%3A9999%2F
```

✅ **v0.9.11 IS deployed** — `/authorize` proxy endpoint working correctly, redirects to Entra as expected.

**Authorization Server Metadata:** ✅ 200 OK
```json
{
  "issuer": "https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/v2.0",
  "authorization_endpoint": "https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/oauth2/v2.0/authorize",
  "token_endpoint": "https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/oauth2/v2.0/token",
  "registration_endpoint": "https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/register",
  "scopes_supported": [...],
  "response_types_supported": ["code"],
  "grant_types_supported": ["authorization_code", "refresh_token"],
  "code_challenge_methods_supported": ["S256"],
  "token_endpoint_auth_methods_supported": ["none"]
}
```

#### Summary

| Issue | Status | Finding |
|-------|--------|---------|
| PRM fetch failure | 🔴 **BLOCKER** | `authorization_servers` contains container URL → creates metadata fetch loop |
| v0.9.11 deployment | ✅ **DEPLOYED** | `/authorize` endpoint working, redirects to Entra correctly |
| CI build | ✅ **SUCCESS** | Release workflow completed successfully |

**Recommendation:** Fix PRM endpoint to return empty `authorization_servers` array or remove the field entirely. The container is a proxy to Entra's OAuth, not an authorization server.

---

### 2026-05-02T10:11: /authorize Proxy Redirect Bug Investigation

**Deployment:** `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io`
**Task:** Diagnose why VS Code MCP client sends browser to container's `/authorize` instead of `login.microsoftonline.com`.

**Step 1 — Live AS metadata:**

| Field | Value |
|-------|-------|
| `issuer` | `https://login.microsoftonline.com/d91aa5af.../v2.0` |
| `authorization_endpoint` | `https://login.microsoftonline.com/d91aa5af.../oauth2/v2.0/authorize` ✅ |
| `token_endpoint` | `https://login.microsoftonline.com/d91aa5af.../oauth2/v2.0/token` ✅ |
| `registration_endpoint` | `https://poshmcp.calmstone.../register` |

The AS metadata `authorization_endpoint` is **correctly pointing to Entra**, not the container.

**Step 2 — GET /authorize on container:**

```
Status: 404 Not Found
Location: (none)
```

The container's `/authorize` endpoint **does not exist** — returns 404.

**Step 3 — Code review (`OAuthProxyEndpoints.cs`):**

`OAuthProxyEndpoints.MapOAuthProxyEndpoints()` registers only two endpoints:
- `GET /.well-known/oauth-authorization-server` — returns correct AS metadata with Entra's `authorization_endpoint`

---
*Older entries (pre-2026-05-05 bulk) moved to `history-archive.md` on 2026-05-05 by Scribe to satisfy 15KB hard gate. See archive for full record.*
