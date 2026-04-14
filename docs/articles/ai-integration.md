---
uid: ai-integration
title: AI Assistant Integration
---

# AI Assistant Integration

Integrate PoshMcp with GitHub Copilot and other MCP-compatible AI assistants.

## GitHub Copilot in VS Code

Configure PoshMcp as an MCP server for GitHub Copilot.

### 1. Install PoshMcp

```bash
dotnet tool install -g poshmcp
```

For a specific version:

```bash
dotnet tool install -g poshmcp --version 0.5.5
```

### 2. Create MCP Configuration

Edit `.vscode/cline_mcp_settings.json`:

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

With custom config:

```json
{
  "mcpServers": {
    "poshmcp": {
      "command": "poshmcp",
      "args": [
        "serve",
        "--transport", "stdio",
        "--config", "/path/to/appsettings.json"
      ]
    }
  }
}
```

### 3. Open Copilot Chat

- Press `Ctrl+Shift+I` (or `Cmd+Shift+I` on macOS)
- Ask a question: "What services are running on this computer?"
- Copilot now has access to PoshMcp tools

### For Developers Building from Source

If you're working in the poshmcp repository and want to test changes:

```json
{
  "mcpServers": {
    "poshmcp": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:\\Users\\YourUsername\\source\\poshmcp\\PoshMcp.Server",
        "--",
        "serve",
        "--transport", "stdio"
      ],
      "env": {
        "DOTNET_ENVIRONMENT": "Development"
      }
    }
  }
}
```

## Other MCP-Compatible Clients

PoshMcp works with any MCP-compatible client. Configure the client to connect to the stdio server:

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

Or if building from source:

```json
{
  "mcpServers": {
    "poshmcp": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/path/to/poshmcp/PoshMcp.Server",
        "--",
        "serve",
        "--transport", "stdio"
      ]
    }
  }
}
```

## Web Integration

Integrate PoshMcp's HTTP API into your web application:

```javascript
// Fetch available tools
const tools = await fetch('http://localhost:8080/tools')
  .then(r => r.json());

// Call a tool
const result = await fetch('http://localhost:8080/call', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({
    tool: 'Get-Process',
    arguments: { Name: 'explorer' }
  })
}).then(r => r.json());
```

---

**See also:** [Getting Started](getting-started.md) | [Transport Modes](transport-modes.md)
