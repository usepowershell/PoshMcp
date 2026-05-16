---
uid: tutorial-docker-api-key-multi-key-roles
title: "Tutorial 4: Require an API key for every tool, with two roles"
---

# Tutorial 4: Require an API key for every tool, with two roles

This tutorial extends the API-key model from [Tutorial 3](03-docker-api-key-per-command.md) into a setup that's closer to a real production posture: **every** tool requires an API key, and there are **two** keys with different privilege levels. A `reader` key can call safe, read-only tools. An `admin` key can call those *and* a privileged tool that performs a state-changing operation.

**Time:** about 25 minutes
**Audience:** Teams ready to model coarse privilege tiers (`reader` vs `admin`) inside a single PoshMcp deployment

## What you'll build

A Docker image that exposes three tools:

- `Get-InventorySummary` — read-only. Requires either key.
- `Get-InventoryItem` — read-only. Requires either key.
- `Invoke-InventoryReset` — privileged. Requires the `admin` role.

Globally, authentication is **on**, the default policy **requires** an authenticated caller, and there are **no** anonymous tools. The privileged tool layers an additional `RequiredRoles` check on top.

## Prerequisites

- You have completed [Tutorial 3](03-docker-api-key-per-command.md) or are comfortable with the project-layout and Docker-build pattern from Tutorials 2 and 3
- `curl` or an MCP client for verification

## Step 1 — Create the project layout

```bash
mkdir -p my-poshmcp-roles/InventoryModule
cd my-poshmcp-roles
```

## Step 2 — Write the PowerShell module

Save the following to `InventoryModule/InventoryModule.psm1`:

```powershell
$script:Inventory = @(
    [PSCustomObject]@{ Sku = "A-001"; Name = "Widget";  Stock = 42 }
    [PSCustomObject]@{ Sku = "A-002"; Name = "Gadget";  Stock = 17 }
    [PSCustomObject]@{ Sku = "A-003"; Name = "Gizmo";   Stock = 9  }
)

function Get-InventorySummary {
    <#
    .SYNOPSIS
    Returns a summary of inventory levels. Safe for any authenticated caller.
    #>
    [CmdletBinding()]
    param()

    [PSCustomObject]@{
        TotalSkus     = $script:Inventory.Count
        TotalStock    = ($script:Inventory | Measure-Object -Property Stock -Sum).Sum
        GeneratedAt   = Get-Date
    }
}

function Get-InventoryItem {
    <#
    .SYNOPSIS
    Returns a single inventory item by SKU. Safe for any authenticated caller.
    .PARAMETER Sku
    The SKU to look up.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Sku
    )

    $item = $script:Inventory | Where-Object Sku -eq $Sku
    if (-not $item) {
        throw "Unknown SKU: $Sku"
    }
    $item
}

function Invoke-InventoryReset {
    <#
    .SYNOPSIS
    Resets all inventory stock counts to zero. Privileged — admins only.
    #>
    [CmdletBinding()]
    param()

    foreach ($item in $script:Inventory) {
        $item.Stock = 0
    }

    [PSCustomObject]@{
        ResetAt = Get-Date
        Items   = $script:Inventory.Count
    }
}

Export-ModuleMember -Function Get-InventorySummary, Get-InventoryItem, Invoke-InventoryReset
```

## Step 3 — Write `appsettings.json` with two keys and role-gated overrides

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
          "reader-key-rotate-me": {
            "Scopes": [],
            "Roles": ["reader"]
          },
          "admin-key-rotate-me": {
            "Scopes": [],
            "Roles": ["reader", "admin"]
          }
        }
      }
    }
  },
  "PowerShellConfiguration": {
    "CommandNames": [
      "Get-InventorySummary",
      "Get-InventoryItem",
      "Invoke-InventoryReset"
    ],
    "Modules": [
      "InventoryModule"
    ],
    "Environment": {
      "ImportModules": [
        "InventoryModule"
      ]
    },
    "CommandOverrides": {
      "Invoke-InventoryReset": {
        "RequiredRoles": ["admin"]
      }
    }
  }
}
```

Things worth calling out:

- **There is no `AllowAnonymous` anywhere.** Combined with `DefaultPolicy.RequireAuthentication: true`, this means every tool now requires a valid API key.
- **Each key carries a list of roles.** When PoshMcp authenticates a key, it adds a `ClaimTypes.Role` claim to the caller's identity for each role on that key. That is what `RequiredRoles` checks against.
- **The `admin` key carries both roles.** A common pattern is for higher tiers to be a superset of lower tiers: anything the `reader` can do, the `admin` can also do. The reader cannot do anything the admin can.
- **`Invoke-InventoryReset` is the only command with an override.** Its `RequiredRoles: ["admin"]` raises the bar for that tool only. The two read tools fall through to the default policy, which requires authentication but no specific role.

> **Heads-up on `RequiredRoles` semantics:** The current authorization helper treats `RequiredRoles` as an *any-match* check — a caller passes if they have **at least one** of the listed roles. If you list multiple roles on a single override, any one of them is sufficient. For tutorial 4 we list a single role, so the distinction does not matter; for production schemas where you want "must have all of A and B", you would split into multiple overrides today.

## Step 4 — Write the Dockerfile

Same shape as before:

```dockerfile
ARG BASE_IMAGE=ghcr.io/usepowershell/poshmcp/poshmcp:latest
FROM ${BASE_IMAGE}

USER root

COPY InventoryModule/ /usr/local/share/powershell/Modules/InventoryModule/
COPY appsettings.json /app/server/appsettings.json

USER appuser
```

## Step 5 — Build and run

```bash
docker build -t my-poshmcp-roles:latest .
docker run -d --name poshmcp-roles -p 8080:8080 my-poshmcp-roles:latest
```

## Step 6 — Verify the reader key

Configure the MCP client with the reader key:

```json
{
  "servers": {
    "poshmcp-reader": {
      "url": "http://localhost:8080",
      "headers": {
        "X-API-Key": "reader-key-rotate-me"
      }
    }
  }
}
```

Expected behavior:

- The tool list returned by the server includes `get_inventory_summary` and `get_inventory_item`, but **not** `invoke_inventory_reset`. The tool-list filter hides tools the caller cannot invoke.
- Calling `get_inventory_summary` succeeds and returns a small summary object.
- Calling `get_inventory_item` with `Sku=A-001` succeeds and returns the matching item.
- Trying to call `invoke_inventory_reset` (e.g., by asking the model to "use the inventory reset tool") fails with an authorization error similar to: *"Insufficient permissions to call tool 'invoke_inventory_reset'"*.

## Step 7 — Verify the admin key

Add or switch to a second client entry using the admin key:

```json
{
  "servers": {
    "poshmcp-admin": {
      "url": "http://localhost:8080",
      "headers": {
        "X-API-Key": "admin-key-rotate-me"
      }
    }
  }
}
```

Expected behavior:

- The tool list now includes all three tools.
- `get_inventory_summary` and `get_inventory_item` still work.
- `invoke_inventory_reset` now succeeds and returns the reset confirmation object.

## Step 8 — Inspect the auth posture with `doctor`

From inside the container, run the diagnostic:

```bash
docker exec poshmcp-roles poshmcp doctor
```

The **Authentication** section should report `Enabled: true`, the `ApiKey` scheme, the key count (2), and the default policy. No secret material is included in the output.

## Troubleshooting

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| Reader can call `invoke_inventory_reset` | The override is missing or the command name is misspelled. | The `CommandOverrides` key must exactly match the PowerShell command name (`Invoke-InventoryReset`), not the MCP snake_case name. |
| Both keys are rejected | The header name in `appsettings.json` and in the client don't match. | They both default to `X-API-Key`. If you changed one, change both. |
| Admin key works but reader is also accepted on the privileged tool | The reader key was accidentally given the `admin` role. | Confirm the `Roles` list on the reader-key entry contains only `"reader"`. |

## What you learned

- How `ApiKeyDefinition.Roles` becomes a `ClaimTypes.Role` claim on the authenticated caller's identity
- How `CommandOverrides.{Command}.RequiredRoles` enforces a per-tool role check on top of the default policy
- The current behavior of `RequiredRoles` as an any-match check
- How the tool-list filter and the per-tool authorization filter combine to both *hide* and *reject* unauthorized tools

## Where to go next

- For Entra ID / OAuth 2.1 instead of API keys, see [Authentication Guide](../authentication.md). The same `CommandOverrides` machinery accepts `RequiredScopes` to gate tools on OAuth scope claims.
- For deploying these images to Azure with managed identity for outbound calls, see [Azure Integration](../azure-integration.md).
- For more configuration knobs (caching, performance, runtime mode), see [Advanced Configuration](../advanced.md).

---

**Series complete.** You've taken PoshMcp from a local stdio process all the way to a Dockerized HTTP server with per-key role enforcement. The same `Authentication` and `CommandOverrides` shapes scale to richer policies — additional schemes, JWT bearer tokens, scope-based gating — without rewriting the surface your AI clients see.
