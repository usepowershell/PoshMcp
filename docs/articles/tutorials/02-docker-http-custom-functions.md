---
uid: tutorial-docker-http-custom-functions
title: "Tutorial 2: Run a Docker HTTP server with custom PowerShell functions"
---

# Tutorial 2: Run a Docker HTTP server with custom PowerShell functions

In this tutorial you build a custom PoshMcp Docker image that ships a small PowerShell module of your own and serves it over HTTP. By the end you will have a container that exposes two custom functions on `http://localhost:8080` with no authentication — a clean baseline for the auth tutorials that follow.

**Time:** about 20 minutes
**Audience:** Anyone who already has Docker and wants to expose their own PowerShell logic, not just built-in cmdlets

## What you'll build

A directory layout like this:

```
my-poshmcp/
├── Dockerfile
├── appsettings.json
└── HelloModule/
    └── HelloModule.psm1
```

`HelloModule.psm1` defines two functions:

- `Get-Greeting` — returns a friendly greeting for a given name
- `Get-SystemSummary` — returns a small object describing the host PowerShell environment

The Docker image extends `ghcr.io/usepowershell/poshmcp/poshmcp:latest`, installs your module into the container's `AllUsers` module path, and starts PoshMcp in HTTP mode on port 8080.

## Prerequisites

- Docker (or Podman) installed and able to pull from `ghcr.io`
- A working `curl`, or an MCP client that supports HTTP transport, for verification

## Step 1 — Create the project layout

Create a fresh directory and the `HelloModule` subdirectory:

```bash
mkdir -p my-poshmcp/HelloModule
cd my-poshmcp
```

## Step 2 — Write the PowerShell module

Save the following to `HelloModule/HelloModule.psm1`:

```powershell
function Get-Greeting {
    <#
    .SYNOPSIS
    Returns a friendly greeting for a given name.
    .PARAMETER Name
    The name to greet. Defaults to "world".
    #>
    [CmdletBinding()]
    param(
        [Parameter(Position = 0)]
        [string]$Name = "world"
    )

    [PSCustomObject]@{
        Message   = "Hello, $Name!"
        Timestamp = Get-Date
    }
}

function Get-SystemSummary {
    <#
    .SYNOPSIS
    Returns a small object describing the current PowerShell host.
    #>
    [CmdletBinding()]
    param()

    [PSCustomObject]@{
        PSVersion    = $PSVersionTable.PSVersion.ToString()
        OS           = $PSVersionTable.OS
        MachineName  = [System.Environment]::MachineName
        ProcessCount = (Get-Process).Count
    }
}

Export-ModuleMember -Function Get-Greeting, Get-SystemSummary
```

PoshMcp uses the synopsis and parameter blocks to build the MCP tool description and JSON schema, so it's worth filling in the comment-based help.

## Step 3 — Write `appsettings.json`

Save the following to `appsettings.json` in the project root. This configuration tells PoshMcp to import `HelloModule` at startup and expose exactly those two functions as MCP tools.

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "PowerShellConfiguration": {
    "CommandNames": [
      "Get-Greeting",
      "Get-SystemSummary"
    ],
    "Modules": [
      "HelloModule"
    ],
    "Environment": {
      "ImportModules": [
        "HelloModule"
      ]
    }
  }
}
```

The `Modules` list scopes command discovery to the module; the `Environment.ImportModules` list makes sure the module is loaded into the PowerShell session at startup.

## Step 4 — Write the Dockerfile

Save the following as `Dockerfile`. It follows the pattern from `examples/Dockerfile.user` in the PoshMcp repo: start from the published base image, drop into `root` to install your module under the container's `AllUsers` module path, copy your config in, then switch back to the non-root runtime user.

```dockerfile
ARG BASE_IMAGE=ghcr.io/usepowershell/poshmcp/poshmcp:latest
FROM ${BASE_IMAGE}

USER root

# Install the custom module into the AllUsers module path so PoshMcp can find it.
COPY HelloModule/ /usr/local/share/powershell/Modules/HelloModule/

# Use our custom appsettings.json instead of the image default.
COPY appsettings.json /app/server/appsettings.json

USER appuser
```

> **Where modules live in the image:** The base image's `AllUsers` module path is `/usr/local/share/powershell/Modules`. Anything you copy here is discoverable by `Get-Module -ListAvailable` and importable by name. The base image's runtime config lives at `/app/server/appsettings.json`. Both paths are stable contracts of the published image.

## Step 5 — Build the image

```bash
docker build -t my-poshmcp:latest .
```

If you prefer the `poshmcp` CLI, the same build is available as:

```bash
poshmcp build --type custom --tag my-poshmcp:latest
```

The CLI form expects your project layout to match `examples/Dockerfile.user`; for the bespoke layout we built here, the plain `docker build` is the most predictable choice.

## Step 6 — Run the container

```bash
docker run -d --name poshmcp -p 8080:8080 my-poshmcp:latest
```

The base image starts PoshMcp in HTTP mode by default. Confirm it is up:

```bash
curl http://localhost:8080/health
```

You should get a small JSON object reporting the server's health.

## Step 7 — Verify your custom tools

Tail the logs to confirm `HelloModule` was imported and the two commands were registered:

```bash
docker logs poshmcp | grep -E "HelloModule|Get-Greeting|Get-SystemSummary"
```

Now connect from an MCP client. A typical VS Code MCP entry for an HTTP server looks like:

```json
{
  "servers": {
    "poshmcp-local-http": {
      "url": "http://localhost:8080"
    }
  }
}
```

When the client lists tools you should see `get_greeting` and `get_system_summary`. Call `get_greeting` with `Name=Steven` and confirm the response contains `"Hello, Steven!"`.

## Troubleshooting

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| `HelloModule` does not appear in `docker logs` | The `COPY` path is wrong, or the module file is not named `HelloModule.psm1`. | The module folder name, the `.psm1` file name, and the value in `ImportModules` must all match. |
| The client lists zero tools | `CommandNames` does not match the exported function names. | Confirm `Export-ModuleMember -Function` names match the entries in `CommandNames` exactly. |
| `curl http://localhost:8080/health` connects but returns nothing useful | The container is running stdio mode instead of HTTP. | Confirm `POSHMCP_TRANSPORT` is not overridden to `stdio`; the base image defaults to HTTP. |

## What you learned

- The base-image contract: where to put modules (`/usr/local/share/powershell/Modules`) and where to put config (`/app/server/appsettings.json`)
- How `Modules`, `CommandNames`, and `Environment.ImportModules` work together to expose your own PowerShell functions as MCP tools
- How to run PoshMcp as a Docker HTTP service and connect to it from an MCP client

---

**Next:** [Tutorial 3 — Protect specific commands with an API key](03-docker-api-key-per-command.md)
