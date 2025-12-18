# 🧠 MCP Server for PowerShell Tool Transformation

## Overview

The **Model Protocol (MCP) Server** is a platform that empowers PowerShell experts to become AI toolmakers. It dynamically transforms PowerShell scripts, cmdlets, and modules into secure, discoverable, and governable tools that can be invoked by AI agents or exposed as dedicated administrative endpoints.

This project bridges traditional scripting with modern AI interfaces—extending the reach of PowerShell automation to non-scripters and operational AI consumers.

---

## 🚀 Goals

- **Empower PowerShell Experts**: Publish scripts as AI-consumable tools with minimal friction.
- **Secure Endpoint Exposure**: Create endpoints with support for Azure Managed Identities.
- **AI Integration**: Enable natural language invocation of PowerShell tools via AI agents.
- **Operational Reach**: Democratize access to automation across teams and platforms.

---

## 🔧 Key Features

### Tool Registration & Metadata
- Auto-discovery of PowerShell commands via introspection (`Get-Command`, `Get-Help`)
- Metadata extraction: name, synopsis, parameters, output types, examples
- Programatic manifest generation for tool registration

### AI Intent Mapping (provided by the MCP client)
- Natural language interface for users
- AI parses intent and maps to registered tools
- Parameter extraction and prompting for missing values

### Execution Engine
- Isolated PowerShell runspace per session for resource isolation
- Input/output translation for AI consumption
- Azure Managed Identity support when deployed as a container in Azure (no code changes required)

### Endpoint Exposure
- Comprehensive logging for traceability and debugging

### Observability & Metrics (Implemented)
- OpenTelemetry integration for monitoring
- Tool execution metrics and performance tracking
- Session-level metrics in web mode

---

## 🧱 Architecture

```text
+------------------+     +------------------+     +------------------+
|  User Interface  | --> |  AI Intent Layer | --> | PowerShell Runner|
| (Chat, Web, CLI) |     | (Intent Mapping) |     | (Sandboxed Exec) |
+------------------+     +------------------+     +------------------+
        ^                        ^                        ^
        |                        |                        |
+------------------+     +------------------+     +-------------------+
| Tool Catalog     | <-- | Tool Metadata    | <-- | PowerShell Modules|
| & Documentation  |     | & Registration   |     | (Auto Discovery)  |
+------------------+     +------------------+     +-------------------+

## 🔐 Security & Governance

- Isolated PowerShell runspaces per session (multi-user isolation in web mode)
- Comprehensive logging for audit trails
- Azure Managed Identity support when running as containers in Azure
- Configurable command filtering via include/exclude patterns


## 🧪 Example Workflow

- Toolmaker writes a PowerShell script to reset user passwords.
- MCP server auto-discovers and registers the tool.
- AI agent receives input: “Reset John Doe’s password.”
- AI maps intent, extracts parameters, and invokes the tool.
- Execution result is returned; user provides feedback.


## 🎯 Benefits

- Democratized Automation: Non-scripters can use advanced tools.
- Amplified Reach: Toolmakers reach broader audiences.
- Operational Efficiency: AI agents streamline repetitive tasks.


## 📍 Next Steps

- Finalize manifest schema and auto-registration logic
- Build MVP with AI intent mapping and secure execution
- Pilot with PowerShell experts and AI agent consumers
- Integrate with Azure Copilot and GitHub Copilot for enhanced DevOps workflows
