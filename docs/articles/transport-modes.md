---
uid: transport-modes
title: Transport Modes
---

# Transport Modes

PoshMcp supports two transport modes for different deployment scenarios.

## Stdio Mode

**Best for:** Local development, GitHub Copilot integration, single-connection scenarios.

**Characteristics:**
- Single persistent connection
- Stream-based communication
- One runspace per connection
- Minimal overhead

**Start:**

```bash
poshmcp serve --transport stdio
```

**Configure in MCP client:**

```json
{
  "mcpServers": {
    "poshmcp": {
      "command": "poshmcp",
      "args": ["serve", "--transport", "stdio"]
    }
  }
}
```

## HTTP Mode

**Best for:** Multi-user deployments, web integration, cloud infrastructure.

**Characteristics:**
- MCP Streamable HTTP (2025-11-25)
- Per-user isolation
- Horizontal scaling capable
- Built-in health checks

**Start:**

```bash
poshmcp serve --transport http --port 8080
```

### MCP Endpoint and Protocol

The default Streamable HTTP endpoint is `/`; `/mcp` is also available as a
compatibility alias. For a production deployment, set a dedicated endpoint with
`--mcp-path /mcp` (or `POSHMCP_MCP_PATH=/mcp`) and publish only that endpoint
through the reverse proxy or ingress.

Clients initialize with a JSON-RPC `POST` containing
`protocolVersion: "2025-11-25"` and `Accept:
application/json, text/event-stream`. The response provides
`Mcp-Session-Id`; 2025-11-25 clients must send that header and the negotiated
`MCP-Protocol-Version` header on all subsequent `POST`, `GET`, and `DELETE`
requests. Responses may be JSON or Server-Sent Events, according to the
client's `Accept` header.

```bash
# Initialize a Streamable HTTP session.
curl -i https://poshmcp.example/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  --data '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"example","version":"1.0"}}}'

# Open the optional server-to-client event stream with the negotiated headers.
curl -N https://poshmcp.example/mcp \
  -H "Accept: text/event-stream" \
  -H "Mcp-Session-Id: <session-id>" \
  -H "MCP-Protocol-Version: 2025-11-25"

# Explicitly end the session.
curl -X DELETE https://poshmcp.example/mcp \
  -H "Mcp-Session-Id: <session-id>" \
  -H "MCP-Protocol-Version: 2025-11-25"
```

The server returns `400` for a missing, invalid, or unsupported negotiated
protocol version. `DELETE` returns `200` for a live session; requests made
after deletion or idle expiry return `404` and the client must initialize a new
session. `McpServer:IdleSessionTimeoutSeconds` controls expiry (60 seconds by
default); the SDK checks expired sessions in the background approximately every
five seconds.

`2024-11-05` Streamable HTTP clients remain supported for compatibility and
may omit the protocol-version header after initialization. The deprecated
HTTP-with-SSE transport is disabled by default. Enable it only for a required
legacy-client transition:

```json
{
  "McpServer": {
    "EnableLegacySse": true
  }
}
```

### Deployment Security

When an `Origin` header is present on an MCP request, PoshMcp accepts only a
same-origin request or a value in `Authentication:Cors:AllowedOrigins`; other
origins receive `403`. Configure exact HTTPS origins for browser clients rather
than using a wildcard. Non-browser clients normally omit `Origin`.

Enable `Authentication:Enabled` for external deployments. MCP endpoints then
require the configured `McpAccess` policy; `/health` and `/health/ready` remain
anonymous for platform probes. See [Authentication](authentication.md) for
Entra ID and API-key setup.

Health probes remain separate from the MCP endpoint:

```bash
curl https://poshmcp.example/health
curl https://poshmcp.example/health/ready
```

## Override via Environment Variable

```bash
export POSHMCP_TRANSPORT=http
poshmcp serve
```
