# Skill: MCP OAuth + Entra ID Integration

## When to invoke

Use this skill when:
- Adding/debugging OAuth 2.1 authentication to an MCP server
- MCP clients prompt users for a `client_id` that should be automatic
- MCP clients redirect to the wrong `/authorize` endpoint (server instead of Entra)
- Implementing RFC 9728 (Protected Resource Metadata) + RFC 8414 (AS Metadata) for MCP

## Problem pattern

MCP OAuth client discovery follows this chain:

```
Client  →  GET /mcp  →  401 WWW-Authenticate: Bearer resource_metadata="..."
        →  GET /.well-known/oauth-protected-resource
           returns: { authorization_servers: ["..."] }
        →  GET {auth_server}/.well-known/oauth-authorization-server
           (or openid-configuration)
           returns: { authorization_endpoint, token_endpoint, registration_endpoint? }
        →  POST {registration_endpoint}   ← only if client has no pre-registered client_id
           returns: { client_id }
        →  redirect user to authorization_endpoint with client_id + PKCE
```

**Entra ID limitation:** Does not support RFC 7591 Dynamic Client Registration (DCR) for
public clients. VS Code has a hardcoded client_id (`aebc6443-996d-45c2-90f0-388ff96faa56`)
and does not need DCR. Other MCP clients (Claude Desktop, Cline, custom) do not, so they
ask the user to paste a `client_id`.

## Solution pattern: OAuth Proxy

Run PoshMcp itself as a lightweight AS metadata proxy:

1. `/.well-known/oauth-protected-resource` → `authorization_servers: [this_server]`
2. `/.well-known/oauth-authorization-server` on this server → wraps Entra's real endpoints,
   adds `registration_endpoint = {this_server}/register`
3. `POST /register` → returns the statically-configured client_id

This way ALL MCP clients work without user intervention.

## Implementation checklist

- [ ] Add `OAuthProxyConfiguration` (TenantId, ClientId, Audience, Enabled)
- [ ] Endpoint: `GET /.well-known/oauth-authorization-server`
  - `issuer` = server base URL (honoring `X-Forwarded-Proto`/`X-Forwarded-Host`)
  - `authorization_endpoint` = `https://login.microsoftonline.com/{tenant}/oauth2/v2.0/authorize`
  - `token_endpoint` = `https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token`
  - `registration_endpoint` = `{server_base}/register`
  - `scopes_supported` = `[openid, profile, email, offline_access, {audience}/.default]`
  - `response_types_supported` = `["code"]`
  - `grant_types_supported` = `["authorization_code", "refresh_token"]`
  - `code_challenge_methods_supported` = `["S256"]`  ← required by OAuth 2.1
  - `token_endpoint_auth_methods_supported` = `["none"]`  ← public client
- [ ] Endpoint: `POST /register`
  - Returns `{ client_id, client_id_issued_at, token_endpoint_auth_method: "none" }`
  - HTTP 201 Created
- [ ] PRM endpoint: when `OAuthProxy.Enabled` and `AuthorizationServers` is empty,
  auto-populate with server base URL at request time
- [ ] Both endpoints must be `.AllowAnonymous()` — metadata is always public

## Config contract

```json
{
  "Authentication": {
    "OAuthProxy": {
      "Enabled": true,
      "TenantId": "12345678-...",
      "ClientId": "the-client-id-to-return",
      "Audience": "api://poshmcp-prod"
    }
  }
}
```

Env var equivalents (ASP.NET Core double-underscore separator):
```
Authentication__OAuthProxy__Enabled=true
Authentication__OAuthProxy__TenantId=...
Authentication__OAuthProxy__ClientId=...
Authentication__OAuthProxy__Audience=...
```

## Gotchas

1. **`X-Forwarded-*` headers** — Azure Container Apps sets `X-Forwarded-Proto=https` and
   `X-Forwarded-Host={fqdn}`. Always use these when building absolute URLs in endpoints.
   Otherwise the `issuer` and `registration_endpoint` will show `http://` instead of `https://`.

2. **Entra tenant URL format** — Use `https://login.microsoftonline.com/{tenant}/v2.0` as
   the Entra issuer, NOT `https://login.microsoftonline.com/{tenant}` (the `/v2.0` suffix is
   required for modern tokens and the correct `/.well-known/openid-configuration` endpoint).

3. **Entra client authorization** — Any client_id returned by `/register` must be pre-authorized
   in the Entra app registration under **Expose an API → Authorized client applications**.
   Without this, Entra returns AADSTS65001 ("User or administrator has not consented").

4. **VS Code built-in client_id** = `aebc6443-996d-45c2-90f0-388ff96faa56`.
   This must be added to **Authorized client applications** in the Entra app registration.

5. **OAuth proxy vs pointing directly to Entra** — Pointing `AuthorizationServers` directly
   to Entra works for VS Code but not for generic MCP clients (no DCR). The proxy approach
   handles both via the `/register` fallback.

6. **`StringValues.FirstOrDefault()`** — In ASP.NET Core, `Request.Headers["X-Forwarded-Proto"]`
   returns `StringValues`, not `string`. Call `(string?)req.Headers["X-Forwarded-Proto"]`
   or add `using System.Linq` and use `.FirstOrDefault()`.

## References

- RFC 9728: OAuth 2.0 Protected Resource Metadata
- RFC 8414: OAuth 2.0 Authorization Server Metadata
- RFC 7591: OAuth 2.0 Dynamic Client Registration
- MCP Auth Spec: https://modelcontextprotocol.io/specification/2025-03-26/basic/authorization
- Entra ID OpenID Connect: https://login.microsoftonline.com/{tenant}/v2.0/.well-known/openid-configuration
