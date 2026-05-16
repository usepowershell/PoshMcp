---
uid: tutorial-local-stdio
title: "Tutorial 1: Build a local stdio MCP server"
---

# Tutorial 1: Build a local stdio MCP server

In this tutorial you install PoshMcp as a .NET global tool, expose three built-in PowerShell commands, and connect to the server from an MCP client over stdio. No Docker, no HTTP, no authentication — the goal is to see end-to-end traffic from an AI assistant to your machine in the smallest possible footprint.

**Time:** about 10 minutes
**Audience:** PowerShell users new to MCP

## What you'll build

A PoshMcp server that runs on demand from your local shell and exposes:

- `Get-Date` — returns the current date and time
- `Get-Process` — lists running processes
- `Get-Service` — lists Windows services (or systemd units on Linux)

The server reads requests on stdin and writes responses on stdout. An MCP client launches the server as a child process and talks to it over those pipes.

## Prerequisites

- [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- An MCP client capable of launching stdio servers — for example [VS Code with the MCP extension](../ai-integration.md) or Claude Desktop
- Optional: PowerShell 7 if you want the `OutOfProcess` runtime mode (this tutorial uses the default in-process mode, so PowerShell 7 is not required)

## Step 1 — Install PoshMcp

Install PoshMcp from nuget.org as a .NET global tool:

```bash
dotnet tool install -g poshmcp
```

Verify the install:

```bash
poshmcp --version
```

You should see a version number such as `0.14.0`.

## Step 2 — Create a configuration file

Generate a default `appsettings.json` in the current directory:

```bash
poshmcp create-config
```

Add the three commands you want to expose:

```bash
poshmcp update-config --add-command Get-Date
poshmcp update-config --add-command Get-Process
poshmcp update-config --add-command Get-Service
```

The result is an `appsettings.json` file whose `PowerShellConfiguration` section looks like:

```json
{
  "PowerShellConfiguration": {
    "CommandNames": [
      "Get-Date",
      "Get-Process",
      "Get-Service"
    ]
  }
}
```

> **Tip:** You can also edit `appsettings.json` directly. The CLI is a convenience for the most common edits — there is nothing magic about it.

## Step 3 — Verify the configuration

Run the diagnostic command to confirm the server can load your config and discover the commands:

```bash
poshmcp doctor
```

The output is grouped into sections. Look for:

- **Runtime Settings** — confirms which `appsettings.json` was loaded
- **PowerShell** — lists the commands PoshMcp will expose as MCP tools
- **Authentication** — should report `Enabled: false` (we'll change that in tutorials 3 and 4)

If `doctor` reports any errors, fix them before continuing. Common issues are listed at the end of this tutorial.

## Step 4 — Run the server

Start PoshMcp in stdio mode:

```bash
poshmcp serve --transport stdio
```

The process now waits silently for JSON-RPC frames on stdin. You generally won't run it this way by hand — your MCP client launches it for you.

## Step 5 — Connect from VS Code

Add an MCP server entry that launches PoshMcp. The exact file path varies by extension, but the configuration content is the same. A typical entry looks like:

```json
{
  "servers": {
    "poshmcp-local": {
      "command": "poshmcp",
      "args": ["serve", "--transport", "stdio"]
    }
  }
}
```

Save the configuration, then reload the MCP extension or VS Code. The client should report three available tools: `get_date`, `get_process`, and `get_service`.

> **Tool naming:** PoshMcp converts PowerShell command names to snake_case for MCP. `Get-Date` becomes `get_date`. Both names work when you ask your AI assistant for the tool by name.

## Step 6 — Verify end-to-end

Ask your assistant to run one of the tools. For example:

> "Use the get_date tool to tell me what time it is."

The assistant should call the tool, PoshMcp should execute `Get-Date` in PowerShell, and the result should come back through the same stdio pipe. If you see a structured timestamp object, the loop works.

## Troubleshooting

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| `poshmcp: command not found` after install | The .NET global-tools directory is not on `PATH`. | Add `~/.dotnet/tools` (Linux/macOS) or `%USERPROFILE%\.dotnet\tools` (Windows) to `PATH`, then reopen the shell. |
| `doctor` lists zero commands | `appsettings.json` was not loaded from the expected location. | Run `poshmcp doctor` from the same directory that holds `appsettings.json`, or set `POSHMCP_CONFIGURATION=/full/path/to/appsettings.json`. |
| The client says "server exited" | A startup error happened on stderr. | Run `poshmcp serve --transport stdio` directly in a terminal to see the error. |

For more diagnostics, see [FAQ & Troubleshooting](../troubleshooting.md).

## What you learned

- How to install PoshMcp as a .NET global tool
- How `appsettings.json` and the `update-config` CLI compose into a working server
- How `poshmcp doctor` validates configuration before you connect a client
- How an MCP client launches a stdio server as a child process

---

**Next:** [Tutorial 2 — Run a Docker HTTP server with custom PowerShell functions](02-docker-http-custom-functions.md)
