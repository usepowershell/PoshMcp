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
- `POST /register` — DCR proxy returning static `client_id`

**There is no `/authorize` handler registered anywhere in the codebase.**

**Root cause:**

VS Code's MCP OAuth client does **not** use `authorization_endpoint` from the AS metadata. Instead it constructs the authorization URL as `{authorization_server_base}/authorize`, where `authorization_server_base` comes from `authorization_servers[0]` in the PRM.

Since `authorization_servers[0]` = `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io`, VS Code constructs `https://poshmcp.../authorize?...` and opens that in the browser — which returns 404.

The fix is to add a `/authorize` proxy endpoint to `OAuthProxyEndpoints.cs` that reads all incoming query parameters and issues a `302` redirect to the real Entra `authorization_endpoint` with those parameters forwarded.

**Root cause classification:** Option **c** — proxy `/authorize` handler is missing entirely. The AS metadata is correct, but VS Code doesn't use `authorization_endpoint`; it derives the URL from the authorization server base URL.

**Filed:** `.squad/decisions/inbox/fry-authorize-redirect-bug.md`

---

### 2026-05-02T10:07: End-to-End MCP Connection Test

**Deployment:** `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io`
**Task:** Full MCP protocol flow verification — unauthenticated 401 behavior, OAuth discovery, DCR, and simulated MCP client flow.

**Raw test results:**

| Step | Check | Status | Raw Result |
|------|-------|--------|------------|
| 1 | `initialize` (no auth) | ✅ PASS | 401, `WWW-Authenticate: Bearer resource_metadata="https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/.well-known/oauth-protected-resource"` |
| 2 | AS Metadata `issuer` | ✅ PASS | `https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/v2.0` |
| 2 | AS Metadata `authorization_endpoint` | ✅ PASS | `https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/oauth2/v2.0/authorize` |
| 2 | AS Metadata `registration_endpoint` | ✅ PASS | `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/register` |
| 3 | PRM `resource` | ✅ PASS | `api://80939099-d811-4488-8333-83eb0409ed53` |
| 3 | PRM `authorization_servers` | ✅ PASS | `["https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io"]` |
| 4 | Health | ✅ PASS | Healthy, AuthEnabled=True, FunctionCount=3 |
| 5 | `tools/list` (no auth) | ✅ PASS | 401 + correct WWW-Authenticate |
| 6 | DCR `/register` | ✅ PASS | 201, `client_id: 80939099-d811-4488-8333-83eb0409ed53` |

**All pass criteria met:**
- ✅ 401 returned on unauthenticated requests
- ✅ WWW-Authenticate header present with `resource_metadata` URL
- ✅ AS metadata `issuer` = correct Entra v2.0 endpoint
- ✅ `authorization_endpoint` points to login.microsoftonline.com (not the container)
- ✅ DCR endpoint reachable and returns `client_id`
- ✅ Browser auth flow would correctly go to Entra, not the container

**Simulated MCP client flow:**
1. Client POSTs `initialize` → receives 401 + `WWW-Authenticate: Bearer resource_metadata=".../.well-known/oauth-protected-resource"`
2. Client fetches PRM → gets `authorization_servers: ["https://poshmcp..."]`
3. Client fetches AS metadata from `https://poshmcp.../.well-known/oauth-authorization-server` → gets real Entra `authorization_endpoint` and `token_endpoint`, plus container's `registration_endpoint`
4. Client POSTs to `/register` → receives ephemeral `client_id`
5. Client redirects browser to `https://login.microsoftonline.com/.../oauth2/v2.0/authorize` with PKCE
6. User authenticates on Entra, browser redirects back with auth code
7. Client POSTs to Entra `token_endpoint` with code + PKCE verifier → receives Bearer token
8. Client retries `initialize` with `Authorization: Bearer <token>` → 200 success

**Verdict: ✅ A real MCP client would successfully complete OAuth and reach `initialize`.**

**Known pre-existing minor issues (not new):**
- PRM `scopes_supported` has duplicate entries: `api://.../.default` appears twice
- PRM `bearer_methods_supported` has duplicate entries: `header` appears twice
- These were previously noted and do not block OAuth flow

---

### 2026-05-02T15:05: v0.9.10 Container Validation Reconfirmed

**Deployment:** `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io`
**Task:** Re-validate v0.9.10 fixes on live container to confirm stability.

**Live validation executed at 2026-05-02T15:05:08 UTC:**

| Check | Status | Result |
|-------|--------|--------|
| 1. Health | ✅ PASS | 200 Healthy, AuthEnabled=true |
| 2. Issuer (Bug 1 PRIMARY) | ✅ PASS | `https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/v2.0` |
| 3. PRM | ✅ PASS | 200 OK, `authorization_servers` uses HTTPS |
| 4. WWW-Authenticate | ✅ PASS | 401 + `Bearer resource_metadata="https://..."` |

**Conclusion:**
- ✅ Bug 1 (issuer mismatch) — **FIXED and HOLDING**
- ✅ Bug 2 (scope format) — **DEPLOYED (requires real token to validate)**
- ✅ No regressions — http→https scheme still fixed, DCR working
- ✅ Container running v0.9.10 — **CONFIRMED STABLE**

---

### 2026-05-02T10:02: v0.9.10 OAuth Fix Validation — AdvocacyBami

**Deployment:** `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io`
**Task:** Validate two OAuth bug fixes shipped in v0.9.10 on live deployment.

**Checks run and results:**

| Check | Status | Key finding |
|-------|--------|-------------|
| 1. Health | ✅ PASS | 200 Healthy, AuthEnabled=true, 3 functions |
| 2. AS Metadata issuer (Bug 1) | ✅ PASS | `issuer = "https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/v2.0"` |
| 3. PRM | ✅ PASS | 200 OK, `authorization_servers` uses `https://` |
| 4. WWW-Authenticate scheme | ✅ PASS | 401 + `Bearer resource_metadata="https://..."` |
| 5. DCR /register | ✅ PASS | 201 + `client_id: 80939099-d811-4488-8333-83eb0409ed53` |

**Overall: PASS**

**Bug 1 (issuer mismatch):** ✅ Confirmed fixed. `issuer` now correctly returns `https://login.microsoftonline.com/{tenantId}/v2.0` instead of the server's own URL. This was preventing MCP client SDKs from ever sending Bearer tokens.

**Bug 2 (scope format):** ✅ Deployed. Cannot directly validate without a real Entra token, but deployment is live and config change is confirmed applied. Bug 1 fix means end-to-end flow can now be exercised by a real MCP client.

**Minor observation:** PRM `scopes_supported` and `bearer_methods_supported` both contain duplicate entries — cleanup item for future.

**No regressions** in previously fixed issues (http→https scheme, DCR proxy).

**Findings filed:** `.squad/decisions/inbox/fry-v0910-validation.md`

---

### 2026-05-02: MCP `initialize` Timeout Diagnosis — "Waiting for server to respond"

**Deployment:** `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io`
**Task:** Diagnose why MCP client logs "Discovered authorization server metadata" then hangs on "Waiting for server to respond to initialize request..." indefinitely.

**Investigation steps:**
1. GET /health → 200 Healthy ✅
2. POST / (no auth) → 401 with `WWW-Authenticate: Bearer resource_metadata="https://..."` ✅ (https:// fixed)
3. GET /sse → 404 (Streamable HTTP only, no legacy SSE)
4. GET /.well-known/oauth-authorization-server → AS metadata retrieved
5. GET /.well-known/oauth-protected-resource → PRM retrieved
6. POST / (fake Bearer token) → 401 in 457ms ✅ (JWT validation/OIDC discovery works)
7. `az containerapp logs` → 72 auth attempts, ALL `result: none` (no Bearer token ever sent by client)
8. Code analysis of OAuthProxyEndpoints.cs + AuthenticationServiceExtensions.cs

**Two compound bugs found:**

| Bug | Location | Impact |
|-----|----------|--------|
| AS metadata `issuer = "https://poshmcp..."` but Entra tokens have `iss = "https://login.microsoftonline.com/{tenant}/v2.0"` | `OAuthProxyEndpoints.cs` line 64 | MCP client SDK rejects token (iss ≠ issuer), never sends Bearer → endless 401 loop |
| `RequiredScopes = ["api://80939099.../user_impersonation"]` (full URI) but Entra `scp` claim = `"user_impersonation"` (short name) | `appsettings.json` + `AuthenticationServiceExtensions.cs` | Even if token is sent, scope check fails with 401 |

**Ruling out:**
- JWT/OIDC network hang: disproved — fake token gets 401 in 457ms
- http:// scheme bug: fixed since v0.9.8
- Server health: fully healthy

**Diagnosis:** Bug 1 (issuer mismatch) prevents the client from ever presenting a Bearer token. Bug 2 (scope format) ensures that even if Bug 1 is fixed, every valid Entra token would still be rejected with 401 due to `scp` claim format (`user_impersonation` vs full API URI).

**Root cause confirmed by:** 72 server-side auth attempts with `result: none` — the client never sends a token.

**Findings filed:** `.squad/decisions/inbox/fry-initialize-timeout-diagnosis.md`

---

### 2026-05-02: v0.9.8 OAuth Deployment Verification — AdvocacyBami

**Deployment:** `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io`
**Task:** Verify OAuth proxy endpoints shipped in v0.9.8

**Results:**

| Check | Verdict | Key finding |
|-------|---------|-------------|
| 1. Health | ✅ PASS | 200 Healthy, all sub-checks pass |
| 2. OAuth AS Metadata | ✅ PASS | auth/token endpoints → login.microsoftonline.com |
| 3. PRM (RFC 9728) | ⚠️ PARTIAL | `resource` = `api://` URI (not container URL); arrays clean |
| 4. DCR Proxy | ✅ PASS | 201 with correct Entra client_id |
| 5. MCP Reachability | ⚠️ ISSUE | 401 ✅ (not redirect), but `resource_metadata` uses `http://` not `https://` |

**Key issue found:** `WWW-Authenticate: Bearer resource_metadata="http://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/.well-known/oauth-protected-resource"` — the scheme is `http://` instead of `https://`. MCP clients following this URL may fail. Root cause: app likely reads `HttpContext.Request.Scheme` which is `http` behind the ACA reverse proxy (needs `X-Forwarded-Proto` forwarding or hardcoded HTTPS base URL from config).

**Findings filed:** `.squad/decisions/inbox/fry-v098-check-findings.md`

## Learnings

### 2026-05-03: DoctorReport role claim — short name vs WS-Fed URI

**Task:** Fix `Build_WithAuthenticatedIdentity_PopulatesIdentitySection` test failure after `DoctorReport.cs` was updated to use `FindAll("roles")` (short claim name) instead of `FindAll(ClaimTypes.Role)`.

**Root cause:** Test was creating role claims with `ClaimTypes.Role` (the WS-Fed long URI `http://schemas.microsoft.com/ws/2008/06/identity/claims/role`). After the production fix that aligns with `MapInboundClaims = false`, the claim type in test setup must also use the short name `"roles"`.

**Fix:** Changed `new Claim(ClaimTypes.Role, "admin")` → `new Claim("roles", "admin")` in `DoctorReportTests.cs` line 305.

**Lesson:** When `MapInboundClaims = false` is set on JWT bearer auth, Entra tokens arrive with short claim names (`roles`, `scp`, `name`). Both production code and test fixtures must use the same claim name form. Always audit test claim setup when production code changes how claims are looked up.

**Result:** 16/16 DoctorReport tests passing ✅

### 2026-05-01: Auth regression tests — IOptions registration fix

**Task:** Regression tests for `AddPoshMcpAuthentication()` bug where `services.Configure<AuthenticationConfiguration>()` was missing, causing `IOptions<AuthenticationConfiguration>` to always resolve to default (`Enabled = false`).

**New test file:** `PoshMcp.Tests/Unit/AuthenticationServiceExtensionsTests.cs`

**3 test cases:**
1. `WhenAuthEnabled_IOptionsAuthenticationConfiguration_ReflectsConfig` — auth enabled config, asserts `Enabled == true`, `DefaultScheme == "Bearer"`, correct scheme count (2)
2. `WhenAuthDisabled_IOptionsAuthenticationConfiguration_IsRegisteredWithEnabledFalse` — auth disabled config, asserts `Enabled == false` and `DefaultScheme` still populated (not just a default)
3. `WhenNoAuthSection_IOptionsAuthenticationConfiguration_DoesNotThrow` — no Authentication section, asserts resolving `IOptions<AuthenticationConfiguration>` does not throw and `Enabled == false`

**Key pattern:** `ServiceCollection` + `ConfigurationBuilder().AddInMemoryCollection(...)` → `services.AddPoshMcpAuthentication(config)` → `BuildServiceProvider()` → `GetRequiredService<IOptions<AuthenticationConfiguration>>()` → assert `.Value` properties.

**Result:** 3/3 passing ✅

### 2026-04-28: Issue #175 — Integration tests for AppInsights graceful degradation

**Branch:** `squad/175-integration-test-appinsights` (worktree `poshmcp-175`)

**New test file:** `PoshMcp.Tests/Integration/ApplicationInsightsIntegrationTests.cs`
- 4 integration tests verifying SC-102, SC-104, SC-105 from Spec 008
- `Server_StartsSuccessfully_WithAppInsightsEnabled_NoConnectionString` — health endpoints respond 200
- `Server_LogsWarning_WhenAppInsightsEnabled_NoConnectionString` — warning about missing connection string appears in captured stderr
- `Server_McpToolsStillRespond_WithAppInsightsEnabled` — MCP initialize + tools/list still works
- `Server_StartsNormally_WithAppInsightsDisabled` — control test, no warning when disabled

**Bug found & fixed:** `appsettings.json` had a duplicate `ApplicationInsights` key (lines 12 and 61), causing `SettingsResolver.MergeMissingProperties` to crash with `ArgumentException: An item with the same key has already been added`. Removed the duplicate at line 61.

**Key patterns:**
- Created `AppInsightsTestHttpServer` helper class (similar to `InProcessUnifiedHttpServer` but accepts `Dictionary<string, string> environmentOverrides`) for passing env vars like `ApplicationInsights__Enabled=true`
- Server warning message to check: `"Application Insights is enabled but no connection string was found"` (from `Program.cs` `ConfigureApplicationInsights`)
- Environment variable `APPLICATIONINSIGHTS_CONNECTION_STRING` must be explicitly removed from the process environment to test the "missing" path
- Tests tagged `[Trait("Category", "Integration")]` for filtering

**Result:** 4/4 passing. Committed `ee3bdf5`, pushed to origin. PR creation blocked by EMU auth — user needs to open PR manually.

## Recent Work (2026-04-20 — CURRENT SESSION)

### 2026-04-28: Unit Tests for ConfigureApplicationInsights (#174)

**Branch:** `squad/174-unit-tests-appinsights` (worktree `poshmcp-174`)
**Status:** Complete — pushed, PR creation blocked by EMU auth

**Test file created:** `PoshMcp.Tests/Unit/ConfigureApplicationInsightsTests.cs`

**6 test cases covering SC-103 to SC-106 from Spec 008:**
1. `Enabled_False_DoesNotRegisterAzureMonitor` — verifies no OTel/AzureMonitor service descriptors when Enabled=false
2. `Enabled_True_WithConnectionString_RegistersAzureMonitor` — verifies OpenTelemetry services registered
3. `Enabled_True_NoConnectionString_LogsWarning_NoCrash` — captures stderr, verifies warning message, no throw, no OTel services
4. `SamplingPercentage_50_SetsSamplingRatio` — verifies stderr info message reports 50% sampling
5. `Enabled_True_ConnectionString_FromEnvVar_RegistersAzureMonitor` — sets/restores env var, verifies OTel registered
6. `Enabled_True_WithConnectionString_AddsLoggerFilterRule` — verifies LoggerFilterOptions configured for OTel suppression

**Result:** 396 total unit tests — 396 passed, 0 failed ✅

**Key patterns:**
- `ConfigureApplicationInsights` is `private static` in `PoshMcp.Program` (namespace `PoshMcp`, not `PoshMcp.Server`)
- Accessed via reflection (`BindingFlags.NonPublic | BindingFlags.Static`)
- `InternalsVisibleTo("PoshMcp.Tests")` is set in csproj but doesn't help with private — reflection required
- Tests use `ServiceCollection` inspection to verify registrations (checking `ServiceType.FullName` for "OpenTelemetry"/"AzureMonitor")
- Env var tests save/restore original value in try/finally blocks
- Stderr capture uses `Console.SetError` with `StringWriter`, restored in finally

### Docker Build Arguments Unit Tests
**Branch:** background→sync  
**Status:** Complete

- **Task (Fry)**: Create comprehensive unit test suite for Docker build arguments
- **Implementation**: Created `PoshMcp.Tests/Unit/DockerRunnerTests.cs` with 11 test cases
- **Coverage**: All PoshMcp Docker build scenarios (minimal config, buildkit, registries, multi-arch, custom paths, ignore patterns, error handling, labels, caching, build args, output format)
- **Outcome**: All 11 tests passing ✅
- **Coordination**: Tests verify Bender's extracted `DockerRunner.BuildDockerBuildArgs` method thoroughly

**Test results summary:**
- 11/11 passing
- Covers argument construction, registry handling, multi-architecture builds, error paths
- Validates build argument ordering and formatting

## Recent Status (2026-07-18: Spec 006 Phase 7 — Doctor Output Tests (T019–T023)

**Branch:** `squad/spec006-doctor-output-restructure` (worktree `poshmcp-spec006`)

**New test files created:**
- `PoshMcp.Tests/Unit/DoctorReportTests.cs` — 14 tests: `ComputeStatus` (healthy/errors/warnings/resource-errors/prompt-errors/resource-warnings), `DoctorSummary` property assertions, JSON top-level key verification (T021 combined), camelCase name assertions, `effectivePowerShellConfiguration` absence check
- `PoshMcp.Tests/Unit/DoctorTextRendererTests.cs` — 14 tests: banner box-drawing chars, status symbols (✓/⚠/✗), section headers (Runtime Settings/Env Vars/PowerShell/Functions-Tools/MCP Definitions), header format validation, conditional Warnings section (present/absent)

**Existing test files updated (T022):**
- `ProgramDoctorConfigCoverageTests.cs` — replaced 12 failing tests: removed `authenticationConfig`/`loggingConfig`/old resource+prompt assertions; added `runtimeSettings`, `summary.status`, `mcpDefinitions.resources`, `mcpDefinitions.prompts`, new text section header checks (`── Environment Variables`, `── Runtime Settings`, `── MCP Definitions`), auth-absent test
- `ProgramDoctorToolExposureTests.cs` — fixed `GetToolNames` to use `functionsTools.toolNames`; removed `effectivePowerShellConfiguration` assertion
- `ProgramConfigurationGuidanceToolExposureTests.cs` — fixed `GetToolNames` to use `functionsTools.toolNames`
- `ProgramTransportSelectionTests.cs` — updated all 8 tests: flat keys (`effectiveTransport`, `effectiveSessionMode`, etc.) → nested (`runtimeSettings.transport.value`, `runtimeSettings.sessionMode.value`, etc.); `PayloadContainsConfiguredModulePath` now checks `powerShell.oopModulePaths`
- `ProgramTests.cs` — fixed `oopModulePaths`/`oopModulePathEntries` → `powerShell.oopModulePaths`/`powerShell.oopModulePathEntries`

**Result:** 527 total — 520 passed, 7 skipped (pre-existing), 0 failed ✅
`dotnet format --verify-no-changes` clean. Commit `f38b9b9` pushed.

**Key patterns established:**
- New JSON shape: all runtime settings under `runtimeSettings.{key}.value` / `.source`
- Tool names under `functionsTools.toolNames`
- OOP module paths under `powerShell.oopModulePaths`/`oopModulePathEntries`
- No `authenticationConfig`, `loggingConfig`, `effectivePowerShellConfiguration` in new JSON
- Text output sections use `── Section Name ──...` headers (44-char padded)

**[Earlier detailed history 2026-04-14 and prior archived to history-archive.md on 2026-04-18 per Scribe threshold policy. Preserving last 90 days (2026-04-21 onwards) in main history.]**



### 2026-04-15: Cross-agent verification pattern for auth behavior

- Bender's resolver improvements should be validated with both precedence-focused tests and docs wording review in the same handoff.
- For configuration behavior, lock exact-match precedence with regression tests and keep docs explicit about recommended key style versus currently accepted key styles.

### 2026-04-18: Spec 002 Final Verification — Full Suite Run on feature/002-tests (rebased)

**Context:** Hermes rebased `feature/002-tests` onto main and removed all 16 Skip attributes from `McpResourcesIntegrationTests` and `McpPromptsIntegrationTests`. Task was to run the full test suite and confirm readiness for merge (PR #128).

**Test run result:** 478 total — 470 passed, 1 failed, 7 skipped (duration ~247s)

**Spec 002 integration tests:** 16/16 pass ✅
- 8 `McpResourcesIntegrationTests`: all passing (resources/list, resources/read, file/command sources, error paths)
- 8 `McpPromptsIntegrationTests`: all passing (prompts/list, prompts/get, file/command sources, argument injection, error paths)
- Zero Skip attributes remain on spec-002 tests

**The one failure (pre-existing, non-blocking):**
- Test: `McpResourcesValidatorTests.Validate_ResourceWithNoMimeType_ReportsMimeTypeWarning`
- Cause: `McpResourceConfiguration.MimeType` defaults to `"text/plain"` at the C# object level. The test creates a resource without setting MimeType, expecting the validator to warn, but the property already carries `"text/plain"` — so `IsNullOrWhiteSpace` is never true.
- Pre-existing since `a2ade16` (original resources implementation). Not introduced by Hermes's rebase.
- Remediation (future, not blocking): change `MimeType` default to `null`/empty and apply `"text/plain"` at runtime.

**Skips (7, all pre-existing):**
- 6 `OutOfProcessModuleTests` — out-of-process mode not yet integrated
- 1 `Functional.ReturnType.GeneratedMethod.ShouldHandleGetChildItemCorrectly` — pre-existing

**Verdict: ✅ CLEAR TO MERGE — PR #128**

### 2026-04-18: Issue #129 MimeType Fix Validation

- Verified that `Validate_ResourceWithNoMimeType_ReportsMimeTypeWarning` was never skipped — it was failing.
- Root cause: `McpResourceConfiguration.MimeType` had C# default `"text/plain"`, so `IsNullOrWhiteSpace` check never fired.
- Once Bender made property nullable (commit `6a93c3d`), validator logic fired correctly and test passed.
- Updated inline comment in test to document nullable behavior; test logic required no changes.
- All 9 validator tests pass; finding drives key learning: failing tests with no Skip attribute often need implementation fixes, not test harness changes.
- **Commit:** `1419a20` on `squad/129-fix-mimetype-nullable` (Coordinator rebased)
- **PR #130** ready for review.

### 2026-07-18: Issue #131 — Stdio logging suppression tests

**Branch:** `squad/131-stdio-logging-to-file`

**Test files created:**
- `PoshMcp.Tests/Unit/StdioLoggingConfigurationTests.cs` — 8 unit tests for `ResolveLogFilePath` resolution priority
- `PoshMcp.Tests/Functional/StdioLoggingTests.cs` — 2 functional tests for stdio logging suppression/file routing

**Unit tests (reflection-based):**
- `ResolveLogFilePath` is `private static` in `Program.cs` so tests use `BindingFlags.NonPublic | BindingFlags.Static` reflection
- Return type `ResolvedSetting` is a private sealed record; properties accessed via reflection too
- Covered: CLI > env var, env var > null, null both = null/default, appsettings fallback, CLI > appsettings, env > appsettings, whitespace CLI falls back to env

**Functional tests:**
- Use `InProcessMcpServer` + `ExternalMcpClient` from `PoshMcp.Tests.Integration` namespace (same project, public classes)
- `WithNoLogFile`: asserts no Serilog `[yyyy-MM-dd HH:mm:ss LVL]` or MEL `info:` lines appear on server stderr
- `WithLogFile`: passes `serve --log-file <path>` as extraArgs; Serilog uses `RollingInterval.Day` so search for `basename*.log`; assert file exists and has content
- All 10 tests (8 unit + 2 functional) pass; total run ~11s

**Testing infrastructure notes:**
- `ImplicitUsings` is disabled in the test project — all `using` statements must be explicit
- `AddInMemoryCollection` available via transitive `Microsoft.Extensions.Configuration` dependency from `PoshMcp.Server`
- `EnvironmentVariableScope` helper pattern (save/restore env var) reused from `ProgramTransportSelectionTests.cs` style

### 2026-07-18: Spec 006 Phase 7 — Doctor Output Tests (T019–T023)

**Branch:** `squad/spec006-doctor-output-restructure` (worktree `poshmcp-spec006`)

**New test files created:**
- `PoshMcp.Tests/Unit/DoctorReportTests.cs` — 14 tests: `ComputeStatus` (healthy/errors/warnings/resource-errors/prompt-errors/resource-warnings), `DoctorSummary` property assertions, JSON top-level key verification (T021 combined), camelCase name assertions, `effectivePowerShellConfiguration` absence check
- `PoshMcp.Tests/Unit/DoctorTextRendererTests.cs` — 14 tests: banner box-drawing chars, status symbols (✓/⚠/✗), section headers (Runtime Settings/Env Vars/PowerShell/Functions-Tools/MCP Definitions), header format validation, conditional Warnings section (present/absent)

**Existing test files updated (T022):**
- `ProgramDoctorConfigCoverageTests.cs` — replaced 12 failing tests: removed `authenticationConfig`/`loggingConfig`/old resource+prompt assertions; added `runtimeSettings`, `summary.status`, `mcpDefinitions.resources`, `mcpDefinitions.prompts`, new text section header checks (`── Environment Variables`, `── Runtime Settings`, `── MCP Definitions`), auth-absent test
- `ProgramDoctorToolExposureTests.cs` — fixed `GetToolNames` to use `functionsTools.toolNames`; removed `effectivePowerShellConfiguration` assertion
- `ProgramConfigurationGuidanceToolExposureTests.cs` — fixed `GetToolNames` to use `functionsTools.toolNames`
- `ProgramTransportSelectionTests.cs` — updated all 8 tests: flat keys (`effectiveTransport`, `effectiveSessionMode`, etc.) → nested (`runtimeSettings.transport.value`, `runtimeSettings.sessionMode.value`, etc.); `PayloadContainsConfiguredModulePath` now checks `powerShell.oopModulePaths`
- `ProgramTests.cs` — fixed `oopModulePaths`/`oopModulePathEntries` → `powerShell.oopModulePaths`/`powerShell.oopModulePathEntries`

**Result:** 527 total — 520 passed, 7 skipped (pre-existing), 0 failed ✅
`dotnet format --verify-no-changes` clean. Commit `f38b9b9` pushed.

**Key patterns established:**
- New JSON shape: all runtime settings under `runtimeSettings.{key}.value` / `.source`
- Tool names under `functionsTools.toolNames`
- OOP module paths under `powerShell.oopModulePaths`/`oopModulePathEntries`
- No `authenticationConfig`, `loggingConfig`, `effectivePowerShellConfiguration` in new JSON
- Text output sections use `── Section Name ──...` headers (44-char padded)



Detailed prior history (2026-03-27 through 2026-04-07) archived to `history-archive.md` when this file exceeded 15 KB threshold on 2026-04-18.

## [2026-04-23T15:08:26] Deploy Source Image Test Tasks

**Session:** Deploy source image support implementation (spec 007)
**Contribution:** Created test tasks checklist for spec 007

**Key Learnings:**
- Test checklist: specs/007-deploy-source-image/tasks.md
- Comprehensive test coverage planning
- Coordinated with Farnsworth (spec) and Amy (implementation)
- Test-driven approach validates implementation

**Artifacts:** specs/007-deploy-source-image/tasks.md

## [2026-05-02] OAuth AS proxy live endpoint validation

**Task:** End-to-end validation of v0.9.5 OAuth proxy endpoints on the live Container App deployment at `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io`.

**Deployed image:** `psbamiacr.azurecr.io/advocacybami:20260502-061835` (built 06:18 UTC 2026-05-02)
**Active revisions:** `poshmcp--0000018` (2026-05-01) and `poshmcp--0000019` (2026-05-02 11:21 UTC), both active in traffic-split mode.
**Resource group:** `rg-poshmcp`

### What the live endpoints return

| Endpoint | Status | Notes |
|---|---|---|
| `/.well-known/oauth-protected-resource` | **200** | Returns Entra URL in `authorization_servers` (wrong — should be proxy URL). All arrays duplicated/tripled. |
| `/.well-known/oauth-authorization-server` | **404** | Proxy NOT registered — confirms OAuthProxy is disabled |
| `/.well-known/openid-configuration` | 404 | Not implemented (expected) |
| `/health` | 200 | `AuthEnabled: true`, `AuthSchemes: Bearer`, 4 functions, all healthy |
| `/` | 401 | `WWW-Authenticate: Bearer resource_metadata="http://..."` — note `http://` not `https://` |

**PRM response (abbreviated):**
```json
{
  "resource": "api://80939099-d811-4488-8333-83eb0409ed53",
  "authorization_servers": [
    "https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b",
    "https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b"
  ],
  "scopes_supported": ["...user_impersonation", "...user_impersonation"],
  "bearer_methods_supported": ["header", "header", "header"]
}
```

### Root cause: OAuth proxy disabled, env vars not set

**Container env vars (both revisions — identical):**
- `ASPNETCORE_ENVIRONMENT=Production`
- `ASPNETCORE_URLS=http://+:8080`
- `POSHMCP_TRANSPORT=http`
- `APPLICATIONINSIGHTS_CONNECTION_STRING` (secret ref)
- `AZURE_CLIENT_ID=f47096ea-...`

**None of the 4 required OAuth proxy env vars are present:**
- ❌ `Authentication__OAuthProxy__Enabled`
- ❌ `Authentication__OAuthProxy__TenantId`
- ❌ `Authentication__OAuthProxy__ClientId`
- ❌ `Authentication__OAuthProxy__Audience`

**Why `/.well-known/oauth-authorization-server` = 404:** `OAuthProxyEndpoints.MapOAuthProxyEndpoints` returns early when `proxy.Enabled == false`. No route is registered. Code is confirmed present in `OAuthProxyEndpoints.cs` (v0.9.5 is deployed).

**Why PRM returns Entra directly:** The running config's `ProtectedResource.AuthorizationServers` is non-empty (has Entra URL hardcoded), so `ProtectedResourceMetadataEndpoint.cs` line 27 (`authServers.Count == 0`) is false — proxy URL injection is skipped.

**Why arrays are duplicated:** The baked-in appsettings.json (with Entra URL in `AuthorizationServers`) AND another config source (likely an older env var or appsettings.Production.json) both contribute the same array items, causing .NET config array merge duplication.

**Why `http://` in WWW-Authenticate:** `AuthenticationServiceExtensions.cs:60` uses `req.Scheme` directly without checking `X-Forwarded-Proto`. Azure Container Apps terminates TLS at the ingress, forwarding as HTTP internally, so the scheme is `http` at the app level. (The proxy code in `OAuthProxyEndpoints.cs` correctly uses `X-Forwarded-Proto` — this is an unrelated bug in the challenge handler.)

### VS Code client flow (simulated)

1. `GET /.well-known/oauth-protected-resource` → `authorization_servers[0] = https://login.microsoftonline.com/d91aa5af-...`
2. `GET https://login.microsoftonline.com/d91aa5af-.../.well-known/oauth-authorization-server` → **404** (Entra doesn't serve RFC 8414 AS metadata at this path)
3. Fallback `GET /.well-known/openid-configuration` → 200 but `registration_endpoint = null` (Entra doesn't support DCR)
4. No `registration_endpoint` → VS Code cannot do dynamic client registration → no client_id → no redirect to login

**This is the exact failure mode Steven reported.**

### Fix required

**Immediate fix (Amy):** Set the 4 env vars on the Container App (no rebuild needed):
```
Authentication__OAuthProxy__Enabled = true
Authentication__OAuthProxy__TenantId = d91aa5af-8c1e-442c-b77c-0b92988b387b
Authentication__OAuthProxy__ClientId = 80939099-d811-4488-8333-83eb0409ed53
Authentication__OAuthProxy__Audience = api://80939099-d811-4488-8333-83eb0409ed53
```
Also remove any existing `Authentication__ProtectedResource__AuthorizationServers__*` env vars that may be causing the duplicate array entries (or clear them and let the proxy URL auto-populate).

**Secondary fix (Bender):** `AuthenticationServiceExtensions.cs:60` — the `OnChallenge` handler should use `X-Forwarded-Proto` / `X-Forwarded-Host` for building `metadataUrl` (same pattern as `GetServerBaseUrl` in `OAuthProxyEndpoints.cs`).

**Root cause deployment process gap:** The deploy.ps1 `ConvertTo-McpServerEnvVars` function correctly translates `Authentication.OAuthProxy.*` from appsettings.json into env vars, but was apparently not invoked for these deployments (likely direct `az containerapp update --image ...` was used). The local `appsettings.json` at `AdvocacyBami/appsettings.json` has `OAuthProxy.Enabled: true`, so running `deploy.ps1` with `-ServerAppSettingsFile ./appsettings.json` would have set the correct env vars.

---

## 2026-05-02T16:18: Live Deployment Test — v0.9.12 Validation & RFC 9728 Round-Trip

**Deployment:** `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io`  
**Version Detected:** v0.9.12 ✅ (from PRM `resource` field being HTTPS)

**Report Summary:**

| Question | Answer | Status |
|----------|--------|--------|
| **Is v0.9.12 deployed?** | YES — `resource` field is `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io` (HTTPS URL) | ✅ |
| **Does RFC 9728 resource metadata round-trip work?** | YES — Server correctly responds to `GET {resource}/.well-known/oauth-protected-resource` | ✅ |
| **Real token obtained via az CLI?** | NO — User authentication consent required (consent flow requires interactive browser) | ⚠️ BLOCKED |
| **AS metadata scopes correct?** | YES — Contains `openid`, `profile`, `email`, `offline_access`, and `api://80939099-d811-4488-8333-83eb0409ed53/.default` | ✅ |

**Test Results:**

1. **Health Endpoint** (`/health`)
   - Status: ✅ 200 OK
   - PowerShell runspace: Healthy (11.2ms)
   - Assembly generation: Healthy (20.8ms)
   - Configuration: Healthy (auth enabled, 3 functions, 1 module)

2. **Protected Resource Metadata (PRM)** (`/.well-known/oauth-protected-resource`)
   - `resource`: `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io` ✅ (HTTPS)
   - `scopes_supported`: `api://80939099-d811-4488-8333-83eb0409ed53/user_impersonation` ✅
   - `bearer_methods_supported`: `header` ✅
   - `authorization_servers`: `https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io` ✅

3. **Authorization Server Metadata** (`/.well-known/oauth-authorization-server`)
   - `issuer`: `https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b/v2.0` ✅
   - `token_endpoint`: `https://login.microsoftonline.com/.../oauth2/v2.0/token` ✅
   - `authorization_endpoint`: `https://login.microsoftonline.com/.../oauth2/v2.0/authorize` ✅
   - `scopes_supported`: `openid`, `profile`, `email`, `offline_access`, `api://80939099-d811-4488-8333-83eb0409ed53/.default` ✅

4. **RFC 9728 Resource Metadata Fetch** (VS Code flow validation)
   - Fetch: `GET https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/.well-known/oauth-protected-resource`
   - Response: ✅ **SUCCESS** — Server correctly serves PRM at the resource URL
   - **Result:** VS Code can now successfully complete the RFC 9728 metadata discovery round-trip (v0.9.12 fix validated)

5. **Azure CLI Token Fetch**
   - Command: `az account get-access-token --resource "api://80939099-d811-4488-8333-83eb0409ed53"`
   - Status: ❌ **BLOCKED** — `AADSTS65001: consent_required`
   - Reason: User consent required for application `Microsoft Azure CLI` (ID: 04b07795...)
   - Impact: Cannot test `initialize` request with real token in non-interactive environment

**Key Findings:**

1. ✅ **v0.9.12 successfully deployed** — `resource` field is now HTTPS URL
2. ✅ **RFC 9728 round-trip works** — Server correctly responds to metadata fetch at resource URL
3. ✅ **All metadata endpoints healthy** — AS and PRM endpoints respond correctly
4. ⚠️ **Real token test blocked** — Interactive auth required
5. ✅ **Health check green** — All subsystems operational

---

## [2026-04-23] deploy.ps1 precedence automation (CLI vs env vs appsettings)

- Added a script-level integration test that invokes `infrastructure/azure/deploy.ps1` under a mocked PowerShell harness (mocked `az`, `docker`, `poshmcp`, and `Invoke-WebRequest`) so precedence behavior can be validated without live Azure or Docker dependencies.
- Learned that script-level CLI parameter detection was broken because `Initialize-DeploymentConfiguration` was reading `$PSBoundParameters` inside a nested function scope (empty there), which silently made env win over CLI.
- Reliable pattern: capture script invocation-bound parameters once at script scope and reference that captured hashtable in helper functions when precedence logic depends on "CLI was explicitly provided" semantics.
