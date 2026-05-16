---
uid: tutorials
title: Tutorials
---

# Tutorials

Four end-to-end walkthroughs that take you from a local PoshMcp server with a couple of built-in cmdlets to a containerized HTTP server protected by per-key roles. Each tutorial builds on concepts from the previous one, but every tutorial is self-contained — you can jump straight to the scenario that matches what you're building.

## Choose a tutorial

| # | Tutorial | Transport | Auth | What you'll build |
|---|----------|-----------|------|-------------------|
| 1 | [Build a local stdio MCP server](01-local-stdio-server.md) | stdio | None | A PoshMcp server installed as a .NET global tool that exposes three built-in PowerShell commands to an MCP client. |
| 2 | [Run a Docker HTTP server with custom PowerShell functions](02-docker-http-custom-functions.md) | HTTP | None | A custom Docker image that ships a PowerShell module with two of your own functions and serves them over HTTP. |
| 3 | [Protect specific commands with an API key](03-docker-api-key-per-command.md) | HTTP | API key on selected tools | A Docker server that requires an API key for sensitive tools while leaving a health/status tool open. |
| 4 | [Require an API key for every tool, with two roles](04-docker-api-key-multi-key-roles.md) | HTTP | API key on every tool, two keys with different roles | A Docker server where every call requires a key, with a `reader` key that can run safe queries and an `admin` key that can run privileged commands. |

## How to use this series

- **New to PoshMcp?** Start with [Tutorial 1](01-local-stdio-server.md) — it installs the tool, creates the smallest possible config, and verifies the server end-to-end.
- **Already shipping PoshMcp in a container?** Skip to [Tutorial 2](02-docker-http-custom-functions.md) to see the recommended pattern for layering your own PowerShell functions on top of the base image.
- **Adding security?** Tutorials [3](03-docker-api-key-per-command.md) and [4](04-docker-api-key-multi-key-roles.md) ground the API-key story in the actual `Authentication` configuration schema and `CommandOverrides` model used by the server.

> **Looking for Entra ID / OAuth 2.1?** This series focuses on stdio and API-key flows. For Entra ID setup, app registration, and bearer-token validation, see [Authentication Guide](../authentication.md).

## Prerequisites for the whole series

- **Tutorials 1**: .NET 10 runtime, PowerShell 7 (optional but recommended).
- **Tutorials 2–4**: Docker (or Podman), plus the ability to pull from `ghcr.io/usepowershell/poshmcp/poshmcp:latest`.
- **An MCP client** for verification: VS Code with the MCP extension, Claude Desktop, or `curl` for raw HTTP checks.

---

**Next:** [Tutorial 1 — Build a local stdio MCP server](01-local-stdio-server.md)
