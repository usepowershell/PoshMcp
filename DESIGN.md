# MCP server for PowerShell tool transformation

## Overview

The **Model Context Protocol (MCP) Server** is a platform that empowers PowerShell experts to become AI toolmakers. It dynamically transforms PowerShell scripts, cmdlets, and modules into secure, discoverable, and governable tools that can be invoked by AI agents or exposed via MCP-compatible transports.

This project bridges traditional scripting with modern AI interfaces, extending the reach of PowerShell automation to operational AI consumers. Intent understanding and natural-language mapping are provided by MCP clients; PoshMcp focuses on tool discovery, schema generation, and execution.

---

## Goals

- **Empower PowerShell Experts**: Publish scripts as AI-consumable tools with minimal friction.
- **Secure Endpoint Exposure**: Create endpoints with support for Azure Managed Identities.
- **AI Integration**: Enable natural language invocation of PowerShell tools via AI agents.
- **Operational Reach**: Democratize access to automation across teams and platforms.

---

## Key features

### Tool registration and metadata
- Auto-discovery of PowerShell commands via introspection (`Get-Command`, `Get-Help`)
- Metadata extraction: name, synopsis, parameters, output types, examples
- JSON schema generation for MCP tool registration (dynamic tool factory)

### AI intent mapping (provided by the MCP client)
- Natural language interface for users
- AI parses intent and maps to registered tools
- Parameter extraction and prompting for missing values

### Execution engine
- Isolated PowerShell runspace per session for resource isolation
- Runtime mode support: in-process (default) and out-of-process PowerShell execution
- Input/output translation for AI consumption
- Azure Managed Identity support when deployed as a container in Azure (no code changes required)

### Endpoint exposure
- Transport modes: `stdio` (MCP client integration) and `http` (hosted/web integration)
- Health endpoints in HTTP mode (`/health`, `/health/ready`)
- Optional HTTP authentication/authorization configuration
- Comprehensive logging for traceability and debugging

### Observability and metrics (implemented)
- OpenTelemetry integration for monitoring
- Tool execution metrics and performance tracking
- Session-level metrics for hosted HTTP usage

---

## Architecture

```text
+------------------------------+      +-------------------------------------------+      +----------------------+
| MCP Client / AI Assistant    | ---> | PoshMcp Server                            | ---> | PowerShell Runtime   |
| (intent mapping in client)   |      | - Tool discovery + schema generation      |      | (runspaces/sessions) |
+------------------------------+      | - Command execution + serialization        |      +----------------------+
                                      | - Transport: stdio or http                |
                                      | - Auth (http), health checks, observability|
                                      +-------------------------------------------+
```

## Security and governance

- Isolated PowerShell runspaces per session (multi-user isolation in web mode)
- Comprehensive logging for audit trails
- Azure Managed Identity support when running as containers in Azure
- Configurable command filtering via include/exclude patterns
- OAuth 2.1 authentication via Entra ID (JWT Bearer) with RFC 9728 Protected Resource Metadata
- Built-in OAuth proxy: `/.well-known/oauth-authorization-server` + `/register` DCR endpoint so generic MCP clients (non-VS Code) obtain a client_id automatically without prompting the user


## Example workflow

- Toolmaker writes a PowerShell script to reset user passwords.
- MCP server auto-discovers and registers the tool.
- AI agent receives input: “Reset John Doe’s password.”
- AI maps intent, extracts parameters, and invokes the tool.
- Execution result is returned; user provides feedback.


## Benefits

- Democratized Automation: Non-scripters can use advanced tools.
- Amplified Reach: Toolmakers reach broader audiences.
- Operational Efficiency: AI agents streamline repetitive tasks.


## Next steps

- Continue hardening out-of-process execution and large-result performance
- Expand transport/authentication operational guidance and validation tooling
- Keep docs and examples aligned with current CLI and configuration surface

---

## See also

- [README.md](README.md) — project overview, getting started, and configuration
- [DOCKER.md](DOCKER.md) — Docker deployment guide
- [Environment customization guide](docs/articles/environment.md) — runtime environment setup
- [Azure deployment](infrastructure/azure/README.md) — Azure Container Apps deployment
