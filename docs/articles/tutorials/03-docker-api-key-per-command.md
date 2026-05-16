---
uid: tutorial-docker-api-key-per-command
title: "Tutorial 3: Protect specific commands with an API key"
---

# Tutorial 3: Protect specific commands with an API key

In this tutorial you take the Docker HTTP server from [Tutorial 2](02-docker-http-custom-functions.md) and add API-key authentication — but only on the tool that needs protection. A separate health-check tool stays callable without any credentials so monitoring systems and humans can poke at the server without distributing the key.

**Time:** about 20 minutes
**Audience:** Teams that want a single shared secret on sensitive tools without turning every endpoint into an authenticated call

## What you'll build

The same image shape as Tutorial 2, but with a slightly different module and a richer `appsettings.json`:

- `Get-PublicStatus` — returns server health and version. **No API key required.**
- `Get-PrivateData` — returns a sample sensitive payload. **API key required.**

Globally, authentication is **on** and the default policy **requires** an authenticated caller. The public tool opts out using a per-command override.

## Prerequisites

- You have completed [Tutorial 2](02-docker-http-custom-functions.md) or are comfortable repeating the project-layout and Docker-build steps from it
- `curl` or an MCP client for verification

## Step 1 — Create the project layout

```bash
mkdir -p my-poshmcp-apikey/PrivateModule
cd my-poshmcp-apikey
```

## Step 2 — Write the PowerShell module

Save the following to `PrivateModule/PrivateModule.psm1`:

```powershell
function Get-PublicStatus {
    <#
    .SYNOPSIS
    Returns a small public health and version object. No authentication required.
    #>
    [CmdletBinding()]
    param()

    [PSCustomObject]@{
        Status    = "ok"
        Version   = "1.0.0"
        Timestamp = Get-Date
    }
}

function Get-PrivateData {
    <#
    .SYNOPSIS
    Returns a sample sensitive payload. Requires an API key.
    #>
    [CmdletBinding()]
    param()

    [PSCustomObject]@{
        Secret      = "this came from a protected tool"
        RequestedAt = Get-Date
    }
}

Export-ModuleMember -Function Get-PublicStatus, Get-PrivateData
```

## Step 3 — Write `appsettings.json` with API-key auth

Save the following to `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "Authentication": {
    "Enabled": true,
    "DefaultScheme": "ApiKey",
    "DefaultPolicy": {
      "RequireAuthentication": true
    },
    "Schemes": {
      "ApiKey": {
        "Type": "ApiKey",
        "HeaderName": "X-API-Key",
        "Keys": {
          "tutorial-secret-key-please-rotate": {
            "Scopes": [],
            "Roles": []
          }
        }
      }
    }
  },
  "PowerShellConfiguration": {
    "CommandNames": [
      "Get-PublicStatus",
      "Get-PrivateData"
    ],
    "Modules": [
      "PrivateModule"
    ],
    "Environment": {
      "ImportModules": [
        "PrivateModule"
      ]
    },
    "CommandOverrides": {
      "Get-PublicStatus": {
        "AllowAnonymous": true
      }
    }
  }
}
```

A few things to notice:

- `Authentication.Enabled` is `true` and `DefaultScheme` is `ApiKey`. The default authorization policy requires an authenticated caller for every tool unless an override says otherwise.
- The `Keys` dictionary uses the **key value itself** as the property name. The string `"tutorial-secret-key-please-rotate"` is literally what the client sends in the header. Pick something long and random for real deployments.
- The empty `Scopes` and `Roles` lists are intentional — this tutorial uses presence of a valid key alone as the authorization signal. Tutorial 4 introduces roles.
- `PowerShellConfiguration.CommandOverrides.Get-PublicStatus.AllowAnonymous = true` exempts that one tool from the global policy. The server's per-tool filter consults this override before challenging the caller, and the tool-list filter also uses it so the public tool appears in `tools/list` even to unauthenticated callers.

> **Heads-up on header naming:** `HeaderName` is configurable per scheme. The default `X-API-Key` is what `ApiKeyAuthenticationOptions` ships with. If you change it here, every client must change too.

## Step 4 — Write the Dockerfile

Save the following as `Dockerfile`. It is the same shape as Tutorial 2:

```dockerfile
ARG BASE_IMAGE=ghcr.io/usepowershell/poshmcp/poshmcp:latest
FROM ${BASE_IMAGE}

USER root

COPY PrivateModule/ /usr/local/share/powershell/Modules/PrivateModule/
COPY appsettings.json /app/server/appsettings.json

USER appuser
```

## Step 5 — Build and run

```bash
docker build -t my-poshmcp-apikey:latest .
docker run -d --name poshmcp-apikey -p 8080:8080 my-poshmcp-apikey:latest
```

Tail the logs to confirm authentication is enabled:

```bash
docker logs poshmcp-apikey | grep -i "Authentication"
```

## Step 6 — Confirm per-command overrides with `poshmcp doctor`

Before exercising the tools from a client, run the diagnostic inside the container and grab the `moduleImports.tools` block — it shows exactly which configuration source produced each tool, so you can confirm the per-command override is wired to the right one:

```bash
docker exec poshmcp-apikey poshmcp doctor --json | jq '.moduleImports.tools'
```

For this tutorial's configuration you should see something close to:

```json
[
  {
    "toolName": "get_public_status",
    "commandName": "Get-PublicStatus",
    "source": "commandName",
    "sourceDetail": "Get-PublicStatus",
    "disposition": "exposed"
  },
  {
    "toolName": "get_private_data",
    "commandName": "Get-PrivateData",
    "source": "commandName",
    "sourceDetail": "Get-PrivateData",
    "disposition": "exposed"
  }
]
```

Each entry's `source` field tells you whether the tool came from `commandName`, `module`, or a `pattern` (see spec 011, FR-263-4). `CommandOverrides` keys are matched against the `commandName` field, so confirming that the override's spelling (`Get-PublicStatus`) lines up with the `commandName` value here is the cheapest way to catch an attribution mismatch *before* a client gets a confusing 401.

## Step 7 — Verify the public tool requires no key

The health/status tool should be callable without any credentials. From an MCP client, listing tools without sending a key should still show `get_public_status` (the tool-list filter hides authenticated-only tools from unauthenticated callers, but `AllowAnonymous` tools stay visible). Calling `get_public_status` should succeed and return the small status object.

## Step 8 — Verify the protected tool refuses anonymous calls

From the same anonymous client, calling `get_private_data` should fail with an authorization error. The server's per-tool authorization filter returns an error result with a message similar to:

> Authentication required to call tool 'get_private_data'

## Step 9 — Verify the protected tool accepts the API key

Now configure the MCP client to send the `X-API-Key` header. In VS Code's MCP configuration, the entry looks like:

```json
{
  "servers": {
    "poshmcp-apikey": {
      "url": "http://localhost:8080",
      "headers": {
        "X-API-Key": "tutorial-secret-key-please-rotate"
      }
    }
  }
}
```

After reconnecting, `get_private_data` should now succeed and return the sample payload.

## Troubleshooting

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| The protected tool refuses even with the correct header | The configured `HeaderName` and the client's header name don't match. | Both default to `X-API-Key`. If you changed one, change both. |
| The public tool also asks for a key | The `CommandOverrides` key spelling doesn't match the PowerShell command name. | The key must be the PowerShell name (`Get-PublicStatus`), not the snake-case MCP tool name. The server normalizes between the two, but the configured side must be a real command. |
| Every call returns a 401, even with a valid key | The container is running an older image that doesn't include your config. | Rebuild without cache: `docker build --no-cache -t my-poshmcp-apikey:latest .` |

## What you learned

- The `Authentication` schema for an API-key-only deployment: `Enabled`, `DefaultScheme`, `Schemes.{name}` with `Type: "ApiKey"`, `HeaderName`, and a `Keys` dictionary
- How a single API key is represented: the key value is the dictionary property name and carries an `ApiKeyDefinition` with optional `Scopes` and `Roles` lists
- How `PowerShellConfiguration.CommandOverrides.{Command}.AllowAnonymous` exempts an individual tool from the global authentication policy
- How the same override is consulted by both the per-tool authorization filter (on `tools/call`) and the tool-list filter (on `tools/list`)

---

**Next:** [Tutorial 4 — Require an API key for every tool, with two roles](04-docker-api-key-multi-key-roles.md)
