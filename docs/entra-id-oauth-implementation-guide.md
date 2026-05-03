# Entra ID OAuth Implementation Guide

## Overview

This is an internal learning document recording everything we learned while implementing and troubleshooting OAuth 2.1 + PKCE authentication for PoshMcp deployed as an Azure Container App. We cover the bugs we hit, why they happened, and how to avoid them next time.

### What PoshMcp's OAuth Proxy Does

PoshMcp doesn't act as an OAuth authorization server itself. Instead, it acts as an **OAuth proxy** that sits in front of Entra ID. This pattern is essential for MCP HTTP servers because:

1. MCP clients expect to find an authorization server at the MCP server's base URL (RFC 9728)
2. Entra ID doesn't live at your server's URL — it lives at `login.microsoftonline.com`
3. The proxy solves this by:
   - Advertising Entra's real endpoints in `/.well-known/oauth-authorization-server`
   - Providing a `/register` endpoint for dynamic client registration (DCR) — Entra doesn't support this for public clients
   - Providing an `/authorize` redirect endpoint that forwards requests to Entra's real authorize URL
   - Providing a `/token` proxy endpoint that strips the legacy `resource` parameter (v1.0) before forwarding to Entra's v2.0 endpoint, preventing AADSTS9010010 errors

### Two-Tier Authentication Architecture

```
┌─────────────┐
│  MCP Client │
└──────┬──────┘
       │
       │ (1) POSTs initialize
       │ (3) Discovers auth server at {base}/.well-known/oauth-*
       │ (5) Uses /authorize proxy → redirects to Entra
       │ (6b) Uses /token proxy to exchange auth code
       │ (8) Retries initialize with token
       │
       ▼
┌─────────────────────────────────┐
│     PoshMcp OAuth Proxy         │  ← This server
│  /.well-known/oauth-*-server    │
│  /register (DCR proxy)          │
│  /authorize (redirect proxy)    │
│  /token (token exchange proxy)  │
│  Token validation & JWT check   │
└──────┬──────────────────────────┘
       │
       │ (6) /authorize redirects to Entra
       │ (6a) /token strips resource param, forwards to Entra
       │ (7) User authenticates, Entra issues token
       │
       ▼
┌─────────────────────────────────┐
│     Azure Entra ID              │
│  login.microsoftonline.com      │
└─────────────────────────────────┘
```

---

## How the OAuth Flow Works End-to-End

### Step-by-Step Flow

#### 1. MCP Client POSTs `initialize`

The MCP client calls `POST /`:

```http
POST / HTTP/1.1
Host: poshmcp.example.com
Content-Type: application/json

{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "initialize",
  "params": {...}
}
```

#### 2. Server Responds 401 with Protected Resource Metadata URL

Since the client is not authenticated, the server responds with a 401 and points the client to the Protected Resource Metadata (PRM):

```http
HTTP/1.1 401 Unauthorized
WWW-Authenticate: Bearer resource_metadata="https://poshmcp.example.com/.well-known/oauth-protected-resource"
Content-Type: application/json

{"error": "authentication_required"}
```

**Critical**: This URL must use `https://`, not `http://`. See [Bug 4: HTTP scheme behind reverse proxy](#bug-4--http-scheme-behind-reverse-proxy).

#### 3. Client Fetches Protected Resource Metadata (PRM)

```http
GET /.well-known/oauth-protected-resource HTTP/1.1
Host: poshmcp.example.com

HTTP/1.1 200 OK
Content-Type: application/json

{
  "resource": "https://poshmcp.example.com",
  "authorization_servers": [
    "https://poshmcp.example.com"
  ],
  "scopes_supported": ["api://poshmcp-prod/user_impersonation"],
  "bearer_methods_supported": ["header"]
}
```

The client finds `authorization_servers[0]` = the proxy base URL.

#### 4. Client Fetches AS (Authorization Server) Metadata

The client calls the OAuth AS metadata endpoint at the proxy base URL:

```http
GET /.well-known/oauth-authorization-server HTTP/1.1
Host: poshmcp.example.com

HTTP/1.1 200 OK
Content-Type: application/json

{
  "issuer": "https://login.microsoftonline.com/{tenantId}/v2.0",
  "authorization_endpoint": "https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize",
  "token_endpoint": "https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token",
  "registration_endpoint": "https://poshmcp.example.com/register",
  "scopes_supported": ["openid", "profile", "email", "offline_access", "api://poshmcp-prod/.default"],
  "response_types_supported": ["code"],
  "grant_types_supported": ["authorization_code", "refresh_token"],
  "code_challenge_methods_supported": ["S256"],
  "token_endpoint_auth_methods_supported": ["none"]
}
```

**Critical fields**:
- `issuer`: Must be Entra's issuer (`https://login.microsoftonline.com/{tenantId}/v2.0`), NOT the proxy URL. See [Bug 1: Wrong issuer in AS metadata](#bug-1--wrong-issuer-in-as-metadata).
- `authorization_endpoint`: Entra's real URL (for reference — clients don't use this directly).

#### 5. Client Performs Dynamic Client Registration (DCR)

Since the client doesn't have a pre-registered `client_id`, it calls the `registration_endpoint`:

```http
POST /register HTTP/1.1
Host: poshmcp.example.com
Content-Type: application/json

{
  "client_name": "VS Code",
  "grant_types": ["authorization_code"]
}
```

The server responds with the pre-configured `client_id`:

```http
HTTP/1.1 201 Created
Content-Type: application/json

{
  "client_id": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
  "client_id_issued_at": 1714602000,
  "token_endpoint_auth_method": "none"
}
```

This is an ephemeral `client_id` — it's not stored; the server always returns the same configured one.

#### 6. Client Constructs Authorization URL and Redirects User to Proxy

The client constructs the authorization URL using the proxy base URL (not the `authorization_endpoint` from AS metadata directly):

```
https://poshmcp.example.com/authorize?
  client_id=aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee&
  response_type=code&
  scope=api://poshmcp-prod/.default&
  redirect_uri=http://localhost:1234/callback&
  code_challenge=...&
  code_challenge_method=S256&
  state=...
```

User opens this URL in their browser.

#### 7. Proxy `/authorize` Endpoint Redirects to Entra

The proxy's GET `/authorize` endpoint:
- Receives the client's PKCE parameters
- Replaces the ephemeral `client_id` with the real Entra `client_id`
- Issues a 302 redirect to Entra's real authorize URL

```http
GET /authorize?client_id=aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee&...

HTTP/1.1 302 Found
Location: https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize?
  client_id=aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee&
  response_type=code&
  scope=api://poshmcp-prod/.default&
  redirect_uri=http://localhost:1234/callback&
  code_challenge=...&
  code_challenge_method=S256&
  state=...
```

User's browser follows the redirect to Entra.

#### 8. User Authenticates with Entra

The user enters their credentials at `login.microsoftonline.com`. Entra validates them and issues an authorization code.

#### 9. Authorization Code is Returned to Client's Redirect URI

```http
HTTP/1.1 302 Found
Location: http://localhost:1234/callback?code=M.R3_BAY...&state=...
```

The client's callback endpoint exchanges the code for a token (offline with Entra's token endpoint).

#### 10. Client Retries `initialize` with Bearer Token

The client now has a token and retries the original `initialize` request:

```http
POST / HTTP/1.1
Host: poshmcp.example.com
Authorization: Bearer eyJhbGciOiJSUzI1NiIs...

{"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {...}}
```

#### 11. Server Validates JWT Token

The server:
1. Extracts the JWT from the `Authorization` header
2. Verifies the signature using Entra's public key (fetched from Entra's JWKS endpoint)
3. Validates the `iss` claim matches the configured issuer
4. Validates the `aud` claim matches the configured audience
5. Validates the `scp` claim contains the required scopes

If all checks pass, the token is accepted and `initialize` succeeds.

---

## Bugs We Hit and Why (Critical Lessons)

### Bug 1: Wrong `issuer` in AS Metadata

**Versions**: v0.9.9 and earlier  
**Fixed**: v0.9.10

#### What Happened

The `/.well-known/oauth-authorization-server` endpoint returned the proxy's own URL as the `issuer`:

```json
{
  "issuer": "https://poshmcp.example.com",
  "token_endpoint": "..."
}
```

#### Why It Breaks

RFC 8414 requires that the `iss` claim in JWT tokens matches the `issuer` field in the Authorization Server metadata. Strict clients (including VS Code's MCP SDK and some security-conscious implementations) validate this:

```python
if token.iss != as_metadata.issuer:
    raise "Token rejected: issuer mismatch"
```

Entra ID v2.0 **always** issues JWTs with `iss = "https://login.microsoftonline.com/{tenantId}/v2.0"`. When the metadata reported the proxy URL as the issuer, valid Entra tokens were silently rejected.

**Symptom**: MCP client would retry `initialize` infinitely, unable to authenticate. The token was valid, but the issuer check failed silently.

#### The Fix

Update the AS metadata to report Entra's issuer, not the proxy's:

```json
{
  "issuer": "https://login.microsoftonline.com/{tenantId}/v2.0",
  "authorization_endpoint": "https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize",
  "token_endpoint": "https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token",
  ...
}
```

**Code location**: `PoshMcp.Server/Authentication/OAuthProxyEndpoints.cs`, line 65:

```csharp
var issuer = $"https://login.microsoftonline.com/{proxy.TenantId}/v2.0";
```

#### Why We Got It Wrong

We initially thought the proxy should advertise itself as the AS (since clients connect to it). RFC 8414 allows proxies to do this — but only if the proxy actually issues tokens. Since we're not issuing tokens (Entra is), we must advertise Entra's issuer so token validation checks pass.

---

### Bug 2: Wrong Scope Format in `RequiredScopes`

**Versions**: All (configuration issue)  
**Identified**: While troubleshooting v0.9.10

#### What Happened

The `Authentication.DefaultPolicy.RequiredScopes` configuration used the **full URI form**:

```json
{
  "Authentication": {
    "DefaultPolicy": {
      "RequiredScopes": [
        "api://poshmcp-prod-abc123/user_impersonation"
      ]
    }
  }
}
```

#### Why It Breaks

Entra ID v2.0 encodes scopes in JWT tokens differently depending on the flow:

- **In `scope` request parameter**: Full URI form `"api://poshmcp-prod/user_impersonation"`
- **In `scp` claim of JWT token**: Short name only `"user_impersonation"`

The server's JWT validation policy checks `token.scp` against `RequiredScopes`. When `RequiredScopes` was the full URI form, the comparison failed:

```csharp
// Token has: scp = "user_impersonation"
// Config has: RequiredScopes = ["api://poshmcp-prod/user_impersonation"]
// Result: NO MATCH → 401 Unauthorized
```

Even valid, correctly-scoped tokens were rejected.

**Symptom**: Every authenticated user receives a 403 Forbidden, even though their token contains the correct scope.

#### The Fix

Use the **short scope name** in `RequiredScopes`:

```json
{
  "Authentication": {
    "DefaultPolicy": {
      "RequiredScopes": [
        "user_impersonation"
      ]
    }
  }
}
```

**Code location**: Token validation happens in `AuthenticationServiceExtensions.cs`, line 109:

```csharp
policy.RequireClaim(scopeClaim, authConfig.DefaultPolicy.RequiredScopes.ToArray());
```

The `scopeClaim` variable defaults to `"scp"` (the JWT claim name), and we compare against the configured `RequiredScopes`.

#### Why We Got It Wrong

The full URI form is what you see in Entra's "Expose an API" UI and what you request in OAuth flows. It's natural to think that's what should be in the token. Entra's design here is unintuitive — it truncates to the short name in the JWT without warning.

#### How to Verify Entra's Behavior

Once you have a valid token, decode it (use `jwt.io` or `jq`):

```bash
# Extract and decode the JWT
TOKEN="eyJhbGciOiJSUzI1NiIs..."
echo $TOKEN | cut -d. -f2 | base64 -d | jq .

# Look for "scp" claim:
{
  "iss": "https://login.microsoftonline.com/.../v2.0",
  "scp": "user_impersonation",    # ← SHORT NAME, not full URI
  "aud": "api://poshmcp-prod",
  ...
}
```

---

### Bug 3: Missing `/authorize` Proxy Endpoint

**Versions**: v0.9.10 and earlier  
**Fixed**: v0.9.11

#### What Happened

The GET `/authorize` endpoint returned 404. VS Code's MCP client was constructing the authorization URL as `{proxy_base}/authorize?...` and hitting a non-existent route.

#### Why It Happens

VS Code's MCP SDK and other clients construct the authorization URL directly as `{authorization_server_base}/authorize` rather than reading the `authorization_endpoint` from AS metadata. The code looks something like this:

```javascript
const authUrl = `${authServerBase}/authorize?client_id=${clientId}&...`;
```

If the `/authorize` endpoint doesn't exist, users see a browser "page not found" error when they try to authenticate.

**Symptom**: User clicks "Authenticate" in VS Code → browser opens `https://poshmcp.example.com/authorize?...` → 404 page. OAuth flow never starts.

#### The Fix

Implement a GET `/authorize` endpoint that:
1. Receives the client's OAuth2 PKCE parameters (all query params)
2. Replaces the ephemeral DCR `client_id` with the real Entra `client_id`
3. Issues a 302 redirect to Entra's real authorize URL

**Code location**: `PoshMcp.Server/Authentication/OAuthProxyEndpoints.cs`, lines 106–143:

```csharp
app.MapGet("/authorize", (HttpContext httpContext, ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("PoshMcp.Server.Authentication.OAuthProxyEndpoints");

    if (string.IsNullOrWhiteSpace(proxy.ClientId))
        return Results.Problem(...);

    // Build new query params: forward everything as-is, replace client_id.
    var queryParams = httpContext.Request.Query
        .SelectMany(kvp => kvp.Value.Select(v =>
            new KeyValuePair<string, string?>(
                kvp.Key,
                kvp.Key.Equals("client_id", StringComparison.OrdinalIgnoreCase)
                    ? proxy.ClientId
                    : v)))
        .ToList();

    // Ensure client_id is always present
    if (!queryParams.Any(kv => kv.Key.Equals("client_id", StringComparison.OrdinalIgnoreCase)))
        queryParams.Add(new KeyValuePair<string, string?>("client_id", proxy.ClientId));

    var redirectUrl = $"{authEndpoint}{QueryString.Create(queryParams)}";
    return Results.Redirect(redirectUrl, permanent: false);
}).AllowAnonymous();
```

#### Why We Got It Wrong

We assumed clients would be RFC 8414-compliant and read `authorization_endpoint` from the metadata. VS Code's implementation (and some others) hardcode the path as `/authorize` for simplicity. The lesson: test with real MCP clients early, not just RFC-compliant theory.

---

### Bug 4: HTTP Scheme Behind Reverse Proxy

**Versions**: v0.9.7 / v0.9.8 (intermittent depending on deployment)  
**Identified**: Early testing with Azure Container Apps

#### What Happened

The `WWW-Authenticate` header returned by the JWT challenge handler used `http://` URLs instead of `https://`:

```http
HTTP/1.1 401 Unauthorized
WWW-Authenticate: Bearer resource_metadata="http://poshmcp.example.com/.well-known/oauth-protected-resource"
```

#### Why It Happens

Azure Container Apps (and many reverse proxies) terminate TLS externally and forward traffic to the container as HTTP. When your ASP.NET Core app calls `HttpContext.Request.Scheme`, it returns `"http"` because the **internal** request is HTTP.

If you naively build URLs using `HttpContext.Request.Scheme`, you get `http://` when you need `https://`.

**Symptom**: MCP client receives `http://` URL in `WWW-Authenticate` header. Clients may reject it as insecure or try to fetch metadata over HTTP and get rejected by cert validation.

#### The Fix

Check `X-Forwarded-Proto` header before falling back to `HttpContext.Request.Scheme`. Azure Container Apps (and most cloud load balancers) set this header automatically.

**Code location**: `PoshMcp.Server/Authentication/OAuthProxyEndpoints.cs`, lines 148–156:

```csharp
private static string GetServerBaseUrl(HttpContext ctx)
{
    var req = ctx.Request;
    // Honour X-Forwarded-* headers that Azure Container Apps sets
    var scheme = req.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? req.Scheme;
    var host = req.Headers["X-Forwarded-Host"].FirstOrDefault()
               ?? req.Host.ToUriComponent();
    return $"{scheme}://{host}{req.PathBase}".TrimEnd('/');
}
```

This same helper is used in `AuthenticationServiceExtensions.cs` for the JWT challenge handler (lines 64–66).

#### Why We Got It Wrong

Locally, `HttpContext.Request.Scheme` works because you're connecting directly over HTTPS. Azure Container Apps' behavior is different — it's easy to forget if you haven't deployed there before.

#### How to Debug

```bash
# Inside container, check the headers the app sees
curl -v http://localhost:80 -H "X-Forwarded-Proto: https" -H "X-Forwarded-Host: poshmcp.example.com"

# Expected response should include:
# WWW-Authenticate: Bearer resource_metadata="https://poshmcp.example.com/..."
```

---

### Bug 5: `MapInboundClaims = false` (ASP.NET Core JWT Claim Type Mapping)

**Versions**: v0.9.12 and earlier  
**Fixed**: v0.9.13

#### What Happened

ASP.NET Core's JWT Bearer middleware maps short JWT claim names to long WS-Federation URI forms by default:

- `scp` → `http://schemas.microsoft.com/identity/claims/scope`
- `roles` → `http://schemas.microsoft.com/ws/2008/06/identity/claims/role`

So even with correct scope config and valid tokens, `RequireClaim("scp", "user_impersonation")` matched nothing. The claim was stored in `ClaimsPrincipal` under the long URI key, not `"scp"`.

#### Why It Breaks

The authorization policy calls `ClaimsPrincipal.FindAll("scp")` — which returns empty when the middleware has already remapped that claim to `http://schemas.microsoft.com/identity/claims/scope`. The token validation succeeds (JWT signature and issuer are fine), but the authorization check silently fails:

```
// Token validated successfully — but in ClaimsPrincipal:
// "http://schemas.microsoft.com/identity/claims/scope" = "user_impersonation"   ← stored here
// "scp" = (not found)                                                            ← policy checks this
```

**Symptom**: Every authenticated user receives 403 Forbidden despite the token containing the correct `scp` claim. The AUTHZ DIAG log line shows `scp=[]` even though a full claim dump shows the scope present under the long URI name.

Evidence from container logs:
```
JWT OnTokenValidated fired — all claims present
JWT AUTHZ DIAG: aud=[api://...] scp=[] roles=[]   ← scp empty despite valid token
```

#### The Fix

Set `MapInboundClaims = false` when configuring JWT Bearer, and set `RoleClaimType` explicitly so `IsInRole()` still works:

**Code location**: `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs`

```csharp
// Disable default claim-type mapping so JWT claim names (e.g. "scp",
// "roles", "aud") remain as-is in ClaimsPrincipal instead of being
// transformed to long WS-Fed URIs.
options.MapInboundClaims = false;
// Tell the token validator which short claim name carries roles so that
// ClaimsPrincipal.IsInRole() (used by RequireRole policy) resolves correctly
// now that inbound claim mapping is disabled.
options.TokenValidationParameters.RoleClaimType = scheme.ClaimsMapping.RoleClaim;
```

Also updated `DoctorReport.cs` to use `FindAll("roles")` instead of `FindAll(ClaimTypes.Role)` since the inbound mapping is disabled.

#### Why We Got It Wrong

The default behavior is documented but easy to overlook when you're focused on Entra-specific configuration. It's a .NET-ism that affects all JWT Bearer auth, not just Entra. The claim remapping happens silently — no warning, no log line, the token validates fine and the claim is present, just under a different key than you expect.

Note: this interacts with [Bug 2](#bug-2--wrong-scope-format-in-requiredscopess) — both are needed. Bug 2 ensures `RequiredScopes` uses short names (`user_impersonation`); Bug 5 ensures the claim is actually queryable under the short name `scp`.

#### How to Verify

After the fix, AUTHZ DIAG should show:

```
JWT AUTHZ DIAG: aud=[api://poshmcp-prod] scp=[user_impersonation] roles=[mcp.user]
```

Not `scp=[]`.

---

### Bug 6: Resource Parameter Causes AADSTS9010010 (v1.0 vs v2.0 Incompatibility)

**Versions**: v0.9.10 and earlier  
**Fixed**: v0.9.11

#### What Happened

Clients performing OAuth flows against Entra would receive the error:

```
error=invalid_scope
error_description=AADSTS9010010: The resource parameter must not be provided when the requested scopes are specified.
```

#### Why It Happens

The OAuth proxy was forwarding all client query parameters directly to Entra's `/token` endpoint without modification. When an older client or library included the legacy `resource` parameter (common in v1.0 flows), Entra v2.0's `/token` endpoint rejected the request because it doesn't accept both `resource` (v1.0 style) and `scope` (v2.0 style) in the same request.

**Root cause**: v1.0 and v2.0 endpoints have incompatible parameter schemas:
- **v1.0** uses `resource` to specify what API the token is for
- **v2.0** uses `scope` and doesn't understand `resource`
- If both are present, v2.0 rejects with AADSTS9010010

#### The Fix

Implement a **`/token` proxy endpoint** that:
1. Accepts token exchange requests from the client
2. **Strips the `resource` parameter** (v1.0 only)
3. Forwards the request to Entra's real `/token` endpoint
4. Returns the token response to the client

This allows PoshMcp to bridge v1.0-style clients with the v2.0 endpoint.

**Code location**: `PoshMcp.Server/Authentication/OAuthProxyEndpoints.cs`, lines 150–200:

```csharp
// ── /token (token exchange proxy) ─────────────────────────────────────
// Clients obtain tokens by POSTing to this endpoint with an authorization code.
// We forward the request to Entra's real /token endpoint, but strip the legacy
// `resource` parameter which causes AADSTS9010010 on Entra v2.0 when both
// `resource` and `scope` are present.
app.MapPost("/token", async (HttpRequest request, IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("PoshMcp.Server.Authentication.OAuthProxyEndpoints");

    // Read the entire request body
    var body = await new StreamReader(request.Body).ReadToEndAsync();
    var parameters = QueryHelpers.ParseQuery(body);

    // Strip the `resource` parameter (v1.0 only; v2.0 uses `scope`)
    var filteredParams = parameters
        .Where(kvp => !kvp.Key.Equals("resource", StringComparison.OrdinalIgnoreCase))
        .SelectMany(kvp => kvp.Value.Select(v => new KeyValuePair<string, string?>(kvp.Key, v)))
        .ToList();

    // Forward to Entra's real token endpoint
    using var httpClient = httpClientFactory.CreateClient();
    var content = new FormUrlEncodedContent(filteredParams.ToDictionary(kv => kv.Key, kv => kv.Value ?? ""));
    var response = await httpClient.PostAsync(tokenEndpoint, content);

    return Results.StatusCode((int)response.StatusCode);
}).AllowAnonymous();
```

#### Why We Got It Wrong

We initially assumed that strict RFC 8414 compliance meant forwarding all parameters unchanged. In practice, the proxy sits between clients with different expectations (some using v1.0, some using v2.0) and needs to translate between them.

#### How to Debug

Check server logs for errors with the `/token` endpoint. If clients send `resource=...`, it will be stripped before reaching Entra:

```
[Debug] POST /token: Forwarding to Entra with stripped resource parameter
```

If you still see AADSTS9010010, verify that `scope` is present in the request:

```bash
# Test the token endpoint manually
curl -X POST https://poshmcp.example.com/token \
  -d "grant_type=authorization_code" \
  -d "code=M.R3_BAY..." \
  -d "redirect_uri=http://localhost:1234/callback" \
  -d "client_id=xxx" \
  -d "scope=api://poshmcp-prod/user_impersonation" \
  # Note: Do NOT include resource=... parameter
```

#### Impact on Configuration

The `/token` proxy also respects `ScopesSupported` configuration. It advertises the explicit delegated scope (e.g., `user_impersonation`) rather than `.default` to prevent Entra from issuing v1.0 tokens when the app registration is configured for v1.0.

In `appsettings.json`:

```json
{
  "Authentication": {
    "OAuthProxy": {
      "Enabled": true,
      "Audience": "api://poshmcp-prod"
    },
    "DefaultPolicy": {
      "RequiredScopes": ["user_impersonation"]
    }
  }
}
```

The proxy automatically advertises `api://poshmcp-prod/user_impersonation` instead of `api://poshmcp-prod/.default`, which instructs clients to request the correct scope during token exchange.

---

## Configuration Reference

### appsettings.json Structure

Here's the complete authentication configuration with all required fields:

```json
{
  "Authentication": {
    "Enabled": true,
    "DefaultScheme": "Bearer",
    
    "DefaultPolicy": {
      "RequireAuthentication": true,
      "RequiredScopes": [
        "user_impersonation"
      ],
      "RequiredRoles": []
    },
    
    "Schemes": {
      "Bearer": {
        "Type": "JwtBearer",
        "Authority": "https://login.microsoftonline.com/{tenantId}/v2.0",
        "Audience": "api://poshmcp-prod",
        "ValidIssuers": [
          "https://login.microsoftonline.com/{tenantId}/v2.0"
        ],
        "RequireHttpsMetadata": true,
        "ClaimsMapping": {
          "ScopeClaim": "scp",
          "RoleClaim": "roles"
        }
      }
    },
    
    "ProtectedResource": {
      "Resource": "https://poshmcp.example.com",
      "ResourceName": "PoshMcp",
      "AuthorizationServers": [],
      "ScopesSupported": [
        "api://poshmcp-prod/user_impersonation"
      ],
      "BearerMethodsSupported": [
        "header"
      ]
    },
    
    "OAuthProxy": {
      "Enabled": true,
      "TenantId": "12345678-1234-1234-1234-123456789012",
      "ClientId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
      "Audience": "api://poshmcp-prod"
    }
  }
}
```

### Field Explanations

#### `Authentication.Enabled`
- **Type**: `boolean`
- **Default**: `false`
- **Purpose**: Master switch for all authentication. When `false`, all endpoints are accessible without credentials.

#### `Authentication.DefaultScheme`
- **Type**: `string`
- **Default**: `"Bearer"`
- **Purpose**: Which authentication scheme to use by default. Must match a key in `Schemes`.

#### `Authentication.DefaultPolicy.RequireAuthentication`
- **Type**: `boolean`
- **Default**: `true`
- **Purpose**: If `true`, all endpoints require a valid token. If `false`, endpoints are accessible with or without a token.

#### `Authentication.DefaultPolicy.RequiredScopes`
- **Type**: `List<string>`
- **Default**: `[]` (empty)
- **Purpose**: Scopes that every token must contain. Use **short scope names** (`"user_impersonation"`), NOT full URIs.
- **Example**: `["user_impersonation"]`

#### `Authentication.Schemes["Bearer"].Authority`
- **Type**: `string`
- **Purpose**: URL to Entra's OIDC discovery endpoint. Used to fetch Entra's public signing keys.
- **Example**: `"https://login.microsoftonline.com/{tenantId}/v2.0"`

#### `Authentication.Schemes["Bearer"].Audience`
- **Type**: `string`
- **Purpose**: Expected `aud` claim in JWT tokens. Must match what Entra issues for your app registration.
- **Example**: `"api://poshmcp-prod"`

#### `Authentication.Schemes["Bearer"].ValidIssuers`
- **Type**: `List<string>`
- **Purpose**: Acceptable `iss` claims in JWT tokens. Entra v2.0 always uses a specific issuer format.
- **Example**: `["https://login.microsoftonline.com/{tenantId}/v2.0"]`

#### `Authentication.ProtectedResource.Resource`
- **Type**: `string`
- **Purpose**: Your server's public resource URI (shown in PRM response).
- **Example**: `"https://poshmcp.example.com"`

#### `Authentication.ProtectedResource.AuthorizationServers`
- **Type**: `List<string>`
- **Default**: `[]` (empty)
- **Purpose**: List of authorization server URLs. If empty and `OAuthProxy.Enabled: true`, auto-populated with the proxy base URL.
- **Leave empty** — let the OAuth proxy populate it automatically.

#### `Authentication.ProtectedResource.ScopesSupported`
- **Type**: `List<string>`
- **Purpose**: Scopes this resource supports (advertised in PRM). Use **full URI form** here (unlike `RequiredScopes`).
- **Example**: `["api://poshmcp-prod/user_impersonation"]`

#### `Authentication.OAuthProxy.Enabled`
- **Type**: `boolean`
- **Default**: `false`
- **Purpose**: Enable the OAuth proxy endpoints (`/.well-known/oauth-*`, `/register`, `/authorize`).

#### `Authentication.OAuthProxy.TenantId`
- **Type**: `string` (GUID or tenant name)
- **Purpose**: Azure Entra ID tenant identifier. Used to construct Entra URLs.
- **Example**: `"12345678-1234-1234-1234-123456789012"`

#### `Authentication.OAuthProxy.ClientId`
- **Type**: `string` (GUID)
- **Purpose**: The client ID returned by the `/register` endpoint and used in the `/authorize` redirect. Must be pre-authorized in your Entra ID app registration.
- **Example**: `"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"`

#### `Authentication.OAuthProxy.Audience`
- **Type**: `string`
- **Purpose**: Application ID URI for your app registration. Exposed in AS metadata scopes and used in token validation.
- **Example**: `"api://poshmcp-prod"`

### Environment Variables

All configuration can also be set via environment variables using the hierarchical format with `__` as separator:

```bash
Authentication__Enabled=true
Authentication__OAuthProxy__Enabled=true
Authentication__OAuthProxy__TenantId=12345678-1234-1234-1234-123456789012
Authentication__OAuthProxy__ClientId=aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee
Authentication__OAuthProxy__Audience=api://poshmcp-prod
Authentication__DefaultPolicy__RequiredScopes__0=user_impersonation
```

---

## VS Code MCP Client Configuration Gotchas

### Don't Specify `scope` in `mcp.json`

If your VS Code `mcp.json` has an explicit `scope` field, VS Code may silently fail to acquire a token — sending requests with no `Authorization` header at all.

**Broken config:**

```jsonc
"poshmcp": {
  "url": "https://poshmcp.example.com",
  "type": "http",
  "scope": "api://80939099-d811-4488-8333-83eb0409ed53/user_impersonation"  // ❌ DO NOT ADD THIS
}
```

**Working config:**

```jsonc
"poshmcp": {
  "url": "https://poshmcp.example.com",
  "type": "http"
  // No scope field — let VS Code discover it from the server's OAuth metadata
}
```

**Why it breaks:** VS Code's MCP auth provider caches sessions per scope string. If the specified scope doesn't match a cached session exactly, VS Code silently fails token acquisition without surfacing an error. Removing the explicit scope lets VS Code read `scopes_supported` from the server's Protected Resource Metadata and handle scope selection automatically, triggering a proper browser OAuth flow.

**Symptom:** Server logs show `HasBearerToken=False` on every request despite the user being "logged in" in VS Code. No 401 challenge is successfully triggered. Every request fails with `DenyAnonymousAuthorizationRequirement`.

**How to debug:** Check server logs for `JWT OnMessageReceived: HasBearerToken=False`. If every request shows `False`, the client is not sending a token — it's a client-side acquisition issue, not a server-side validation issue. Fix: remove the `scope` field from `mcp.json` and let the server drive scope discovery.

---

## Entra ID App Registration Requirements

### Creating an App Registration

1. Open [Azure Portal](https://portal.azure.com) → Entra ID → App registrations
2. Click **New registration**
3. **Name**: PoshMcp (or your server name)
4. **Supported account types**: "Accounts in this organizational directory only"
5. **Redirect URI**: Leave blank for now (we'll add it in a moment)
6. Click **Register**

### Add Redirect URIs

1. Go to **Authentication**
2. Under **Web**, click **Add URI**
3. Add: `http://localhost:1234/callback` (for local testing)
4. Add: `https://poshmcp.example.com/callback` (for production)
5. Check **Access tokens** and **ID tokens** under **Implicit grant**
6. Click **Save**

### Expose an API

1. Go to **Expose an API**
2. Click **Set** next to "Application ID URI"
3. Set it to: `api://poshmcp-prod` (use a unique identifier for your environment)
4. Click **Save**
5. Click **Add a scope**
   - **Scope name**: `user_impersonation`
   - **Admin consent display name**: "Impersonate the signed-in user"
   - **Admin consent description**: "Allows PoshMcp to call Entra-secured Web APIs on behalf of the user"
   - Click **Add scope**

### Pre-Authorize Client Applications

This allows specific clients (VS Code, Claude Desktop, etc.) to bypass consent prompts.

1. Go to **Expose an API** → **Authorized client applications**
2. Click **Add a client application**
3. Add VS Code's client ID: `aebc6443-996d-45c2-90f0-388ff96faa56`
4. Check the `user_impersonation` scope
5. Click **Add application** again
6. Add your MCP client's client ID (from `/register` response or your custom client)
7. Check the `user_impersonation` scope
8. Click **Save**

### Get Your Client ID and Tenant ID

1. Go to **Overview**
2. Copy **Application (client) ID** — this is your `ClientId`
3. Copy **Directory (tenant) ID** — this is your `TenantId`

### Example Configuration

```json
{
  "Authentication": {
    "OAuthProxy": {
      "TenantId": "12345678-1234-1234-1234-123456789012",
      "ClientId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
      "Audience": "api://poshmcp-prod"
    }
  }
}
```

---

## Validation Checklist

Use this checklist before deploying to verify OAuth is working correctly:

### Metadata Endpoints

- [ ] **AS metadata issuer is Entra's, not proxy's**
  ```bash
  curl https://poshmcp.example.com/.well-known/oauth-authorization-server | jq .issuer
  # Expected: "https://login.microsoftonline.com/{tenantId}/v2.0"
  # NOT: "https://poshmcp.example.com"
  ```

- [ ] **AS metadata has correct authorization_endpoint**
  ```bash
  curl https://poshmcp.example.com/.well-known/oauth-authorization-server | jq .authorization_endpoint
  # Expected: "https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize"
  ```

- [ ] **Protected Resource Metadata points to proxy as authorization server**
  ```bash
  curl https://poshmcp.example.com/.well-known/oauth-protected-resource | jq .authorization_servers
  # Expected: ["https://poshmcp.example.com"]
  ```

### Unauthenticated Access

- [ ] **401 response includes https:// in WWW-Authenticate**
  ```bash
  curl -i https://poshmcp.example.com/
  # Expected header: WWW-Authenticate: Bearer resource_metadata="https://poshmcp.example.com/..."
  # NOT: WWW-Authenticate: Bearer resource_metadata="http://..."
  ```

### OAuth Endpoints

- [ ] **GET /authorize returns 302, not 404**
  ```bash
  curl -i "https://poshmcp.example.com/authorize?client_id=test&response_type=code&scope=openid"
  # Expected: HTTP/1.1 302 Found
  # NOT: HTTP/1.1 404 Not Found
  ```

- [ ] **POST /register returns client_id**
  ```bash
  curl -X POST https://poshmcp.example.com/register
  # Expected response:
  # {
  #   "client_id": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
  #   "client_id_issued_at": 1714602000,
  #   "token_endpoint_auth_method": "none"
  # }
  ```

### Configuration

- [ ] **RequiredScopes uses short names, not full URIs**
  ```json
  {
    "Authentication": {
      "DefaultPolicy": {
        "RequiredScopes": ["user_impersonation"]
      }
    }
  }
  ```
  NOT `["api://poshmcp-prod/user_impersonation"]`

- [ ] **`MapInboundClaims = false` is set** so `scp` / `roles` claims are queryable by short name (not remapped to long WS-Fed URIs)

- [ ] **ValidIssuers includes Entra's issuer**
  ```json
  {
    "Schemes": {
      "Bearer": {
        "ValidIssuers": [
          "https://login.microsoftonline.com/{tenantId}/v2.0"
        ]
      }
    }
  }
  ```

### Token Validation (Manual Test)

1. Get a valid token from your MCP client or manually via Entra
2. Decode the JWT: `echo $TOKEN | cut -d. -f2 | base64 -d | jq .`
3. Verify the JWT has the expected claims:
   ```json
   {
     "iss": "https://login.microsoftonline.com/{tenantId}/v2.0",
     "aud": "api://poshmcp-prod",
     "scp": "user_impersonation",
     ...
   }
   ```
4. Send it to PoshMcp:
   ```bash
   curl https://poshmcp.example.com/ \
     -H "Authorization: Bearer $TOKEN" \
     -X POST -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}'
   # Expected: 200 OK with initialize response (NOT 401)
   ```

---

## Debugging Tips

### The Infinite `initialize` Loop

**Symptom**: MCP client keeps retrying `initialize` indefinitely.

**Most likely cause**: Token rejection due to issuer or scope mismatch.

**How to debug**:

1. **Check the issuer first**:
   ```bash
   curl https://poshmcp.example.com/.well-known/oauth-authorization-server | jq .issuer
   # Must be: https://login.microsoftonline.com/{tenantId}/v2.0
   ```

2. **Check the scope format**:
   ```bash
   # Get a token and decode it
   echo $TOKEN | cut -d. -f2 | base64 -d | jq .scp
   # Will show: "user_impersonation" (short name)
   
   # Check your config
   cat appsettings.json | jq '.Authentication.DefaultPolicy.RequiredScopes'
   # Must be: ["user_impersonation"]
   # NOT: ["api://poshmcp-prod/user_impersonation"]
   ```

3. **Enable debug logging**:
   ```json
   {
     "Logging": {
       "LogLevel": {
         "PoshMcp.Server.Authentication": "Debug"
       }
     }
   }
   ```
   Then check logs for JWT validation errors.

### The `/authorize` 404 Error

**Symptom**: User clicks "Authenticate" in VS Code → browser shows 404 at `https://poshmcp.example.com/authorize?...`

**Cause**: `/authorize` endpoint is not implemented or not reachable.

**How to debug**:

1. **Test the endpoint directly**:
   ```bash
   curl -i "https://poshmcp.example.com/authorize?client_id=test&response_type=code"
   # Expected: 302 Found (redirect to Entra)
   # If: 404 Not Found → endpoint not registered
   ```

2. **Check if OAuth proxy is enabled**:
   ```json
   {
     "Authentication": {
       "OAuthProxy": {
         "Enabled": true
       }
    }
   }
   ```

3. **Check container logs**:
   ```bash
   podman logs <container-id> | grep -i authorize
   # Should see debug log about redirect
   ```

### HTTP vs HTTPS Scheme Issues

**Symptom**: `WWW-Authenticate` header has `http://` instead of `https://`.

**Cause**: Running behind a reverse proxy (Azure Container Apps, load balancer, etc.) that doesn't set `X-Forwarded-Proto`.

**How to debug**:

1. **Check headers the app sees**:
   ```bash
   # Inside container, make a request and inspect what the app received
   curl -v http://localhost:80 -H "X-Forwarded-Proto: https" \
     -H "X-Forwarded-Host: poshmcp.example.com"
   
   # Response should include: WWW-Authenticate: Bearer resource_metadata="https://..."
   ```

2. **Verify reverse proxy is setting headers**:
   ```bash
   # From outside, check the response header
   curl -i https://poshmcp.example.com/ | grep WWW-Authenticate
   # Should have https:// not http://
   ```

3. **If headers are not set, add forwarding rule** to your reverse proxy / load balancer.

### Using `poshmcp doctor`

PoshMcp includes a built-in diagnostic helper:

```bash
# Inside the container
pwsh -Command 'Import-Module PoshMcp; PoshMcp-Doctor'
```

This will verify:
- Configuration is loaded correctly
- Required environment variables are set
- Entra ID connectivity
- Token validation configuration

### Testing with Manual Token

If you want to test token validation without going through the full OAuth flow:

```bash
# Get a token directly from Entra (using Client Credentials flow or interactive auth)
# Then test it against your server
curl -X POST https://poshmcp.example.com/ \
  -H "Authorization: Bearer eyJhbGc..." \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}'

# If 401: Check token validity, issuer, audience, scopes
# If 200: Token validation is working
```

### Non-Refreshable Tokens (Federated Accounts)

When authenticating through VS Code with `user_impersonation` delegated scope from a federated Microsoft identity (e.g., a Microsoft employee account where the IdP is `sts.windows.net/72f988bf...`), the resulting token may be non-refreshable:

- Token lifetime: ~88 minutes
- No refresh token issued
- After expiry, MCP requests fail with 401
- User must re-authenticate manually

**Why this happens:** Federated/guest accounts in organizational tenants may have conditional access or token lifetime policies that restrict refresh token issuance for certain delegated scopes.

**Mitigation:**
- Ensure the Entra app registration declares `offline_access` in its scopes
- If refresh tokens are required, the app registration may need additional policy exemptions
- MCP clients typically restart the OAuth flow when they receive a 401, so the user experience is a prompted re-login, not a silent failure

---

## Summary

The key lessons from our OAuth implementation:

1. **The proxy must advertise Entra's issuer, not its own** — RFC 8414 clients validate `token.iss == AS.issuer`.

2. **Scope names in tokens are short forms** — Use `"user_impersonation"` in `RequiredScopes`, not the full URI.

3. **VS Code expects `/authorize` at the proxy base** — Even though RFC 8414 says to use `authorization_endpoint`, VS Code hardcodes `/authorize`.

4. **Always handle `X-Forwarded-Proto` in cloud deployments** — Reverse proxies strip TLS and forward as HTTP; you must reconstruct https:// from headers.

5. **Disable `MapInboundClaims`** — ASP.NET Core remaps short JWT claim names (`scp`, `roles`) to long WS-Fed URIs by default. Set `MapInboundClaims = false` or your authorization policies will silently fail even with valid tokens.

6. **Don't put `scope` in VS Code's `mcp.json`** — Let the server's Protected Resource Metadata drive scope discovery. An explicit `scope` field causes VS Code to silently fail token acquisition.

7. **Test early with real MCP clients** — Theory (RFC compliance) doesn't always match implementation (VS Code SDK).

Safe implementations:
- Get the issuer from the tenant: `https://login.microsoftonline.com/{tenantId}/v2.0`
- Use short scope names in config: `["user_impersonation"]`
- Implement `/authorize` as a redirect proxy, even if it's not in the RFC
- Always check `X-Forwarded-Proto` before using `HttpContext.Request.Scheme`
- Set `MapInboundClaims = false` on JWT Bearer options; set `RoleClaimType` explicitly
- Keep VS Code's `mcp.json` free of `scope` — let metadata drive it

We hope this saves the next person a few hours of debugging!
