---
uid: transport-modes
title: Transport Modes
---

# Transport Modes

PoshMcp supports two transport modes for different deployment scenarios.
The production implementation uses `ModelContextProtocol` and
`ModelContextProtocol.AspNetCore` **2.0.0** and defaults to MCP protocol **2026-07-28** (Stateless mode).
Protocol `2025-11-25` remains available for legacy clients via Stateful mode.

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
- MCP Streamable HTTP (**2026-07-28**, Stateless default)
- Per-**call** isolation via reset-before-reuse pool
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

In **Stateless** (default) mode, clients initialize with a JSON-RPC `POST`
containing `protocolVersion: "2026-07-28"` and `Accept:
application/json, text/event-stream`. No `Mcp-Session-Id` is issued and none is
required; each tool call is served by a pooled runspace that is reset before
reuse — PowerShell state does **not** persist between calls. In **Stateful**
mode (opt-in), the server issues a `Mcp-Session-Id` for MCP protocol session
continuity; even then, `Mcp-Session-Id` does **not** bind a caller to a
specific PowerShell runspace or preserve variables across calls. Responses may
be JSON or Server-Sent Events, according to the client's `Accept` header.

```bash
# Initialize a Stateless HTTP session (default).
curl -i https://poshmcp.example/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  --data '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2026-07-28","capabilities":{},"clientInfo":{"name":"example","version":"1.0"}}}'

# Open the optional server-to-client event stream.
curl -N https://poshmcp.example/mcp \
  -H "Accept: text/event-stream" \
  -H "MCP-Protocol-Version: 2026-07-28"

# Stateful mode only — explicitly end the MCP session.
curl -X DELETE https://poshmcp.example/mcp \
  -H "Mcp-Session-Id: <session-id>" \
  -H "MCP-Protocol-Version: 2026-07-28"
```

The server returns `400` for a missing, invalid, or unsupported negotiated
protocol version. In **Stateful** mode, `DELETE` returns `200` for a live session;
requests made after deletion or idle expiry return `404` and the client must
initialize a new session. `McpServer:IdleSessionTimeoutSeconds` applies
**only in Stateful mode** and governs MCP session idle expiry — not a dedicated
runspace. Pool worker lifetime is governed by `McpServer:RunspacePool:IdleTtl`
(default `00:05:00`); the pool replenishment sweep runs at
`McpServer:RunspacePool:SweepInterval` (default `00:00:30`).

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
