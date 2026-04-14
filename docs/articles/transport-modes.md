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
- RESTful API (JSON request/response)
- Per-user isolation
- Horizontal scaling capable
- Built-in health checks

**Start:**

```bash
poshmcp serve --transport http --port 8080
```

**API endpoints:**

```bash
# List available tools
curl http://localhost:8080/tools

# Call a tool
curl -X POST http://localhost:8080/call \
  -H "Content-Type: application/json" \
  -d '{"tool": "Get-Service", "arguments": {"Name": "wuauserv"}}'

# Health check
curl http://localhost:8080/health
```

## Override via Environment Variable

```bash
export POSHMCP_TRANSPORT=http
poshmcp serve
```
