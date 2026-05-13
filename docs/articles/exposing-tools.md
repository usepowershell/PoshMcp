---
uid: exposing-tools
title: Exposing PowerShell Tools
---

# Exposing PowerShell Tools

PoshMcp discovers PowerShell commands and transforms them into AI-consumable tools automatically.

## How It Works

1. **Discovery** — reads available PowerShell commands via `Get-Command`
2. **Inspection** — extracts metadata: name, synopsis, parameters, output type
3. **Schema generation** — creates JSON schema for AI consumption
4. **Registration** — exposes tool via MCP protocol
5. **Execution** — invokes the command and returns structured results

## Configuration Methods

### Specific Commands (Whitelist)

Expose only the commands you want:

```bash
poshmcp update-config --add-command Get-Service
poshmcp update-config --add-command Restart-Service
poshmcp update-config --add-command Get-Process
```

**Configuration:**

```json
{
  "PowerShellConfiguration": {
    "CommandNames": [
      "Get-Service",
      "Restart-Service",
      "Get-Process"
    ]
  }
}
```

### Include Patterns (Wildcard)

Expose groups of commands matching patterns:

```bash
poshmcp update-config --add-include-pattern "Get-*"
poshmcp update-config --add-include-pattern "Set-Service"
poshmcp update-config --add-include-pattern "Restart-*"
```

**Configuration:**

```json
{
  "PowerShellConfiguration": {
    "IncludePatterns": [
      "Get-*",
      "Set-Service",
      "Restart-*"
    ]
  }
}
```

### Exclude Patterns (Blacklist)

Block dangerous commands:

```bash
poshmcp update-config --add-exclude-pattern "Remove-*"
poshmcp update-config --add-exclude-pattern "*-Credential"
poshmcp update-config --add-exclude-pattern "Invoke-*"
```

**Configuration:**

```json
{
  "PowerShellConfiguration": {
    "ExcludePatterns": [
      "Remove-*",
      "*-Credential",
      "Format-*",
      "ConvertTo-SecureString"
    ]
  }
}
```

## Module Management

### Add Modules

Add module names to discovery configuration:

```bash
poshmcp update-config --add-module Az.Accounts
poshmcp update-config --add-module Microsoft.PowerShell.Management
```

Module install/import behavior and module search paths are configured in `appsettings.json` under `PowerShellConfiguration.Environment`.

## Built-In Utility Tools

PoshMcp provides utility tools for working with command output:

- **`get-last-command-output`** — retrieve cached output from the previous command
- **`sort-last-command-output`** — sort results by property
- **`filter-last-command-output`** — filter using PowerShell expressions
- **`group-last-command-output`** — group results by property

**Example workflow:**

```
1. AI calls: Get-Service
   Result: ~50 services returned

2. AI calls: sort-last-command-output -Property Status
   Result: Sorted by Status

3. AI calls: filter-last-command-output -FilterExpression "$_.Status -eq 'Running'"
   Result: Only running services
```

## Description Precedence

PoshMcp populates two fields in every MCP `tools/list` response:

- **Tool description** — `tools[].description`
- **Parameter description** — `tools[].inputSchema.properties.<name>.description`

Each field is resolved by a fixed precedence chain. PoshMcp tries each step in order and stops at the first that produces a non-empty value. The same chain runs in both the in-process and out-of-process execution paths and produces byte-identical output.

### Tool description chain

| Step | Source | Notes |
| ---- | ------ | ----- |
| 1 | `Get-Help <command>` `.Synopsis` | Used when present, non-empty, and not equal to the command name. |
| 2 | `Get-Help <command>` `.Description` body | Paragraph entries are joined by a blank line (`\n\n`). |
| 3 | `"{CommandName} {ParameterSetSyntax}"` | The syntax line from `CommandParameterSetInfo.ToString()` prefixed by the command name. |
| 4 | The bare command name | Final fallback — guaranteed non-empty. |

The chain is applied **per command**, not per parameter set. All parameter sets of a given command share the same tool description text.

**Authoring example.** A function that supplies only `.SYNOPSIS` lands at step 1:

```powershell
function Get-FixtureSynopsisOnly {
    <#
    .SYNOPSIS
    Returns a fixed sentinel string used by the help parity fixture.
    #>
    [CmdletBinding()]
    param()
    'synopsis-only'
}
```

A function that supplies both `.SYNOPSIS` and `.DESCRIPTION` still lands at step 1 — the synopsis wins because it appears first in the chain. To surface the long-form description, omit the synopsis (or set it equal to the command name, which is filtered out by step 1).

A function with no comment-based help and a single parameter set falls through to step 3:

```
Description: "Get-FixtureBare [[-Anything] <string>] [<CommonParameters>]"
```

A function with no comment-based help and no resolvable syntax falls through to step 4 (bare command name).

### Parameter description chain

| Step | Source | Notes |
| ---- | ------ | ----- |
| 1 | `Get-Help <command>` `.Parameters.parameter[].description` | Matched by parameter name; paragraph join identical to step 2 of the tool chain. |
| 2 | `[Parameter(HelpMessage = "...")]` | Only the `HelpMessage` named argument; ignored when empty. |
| 3 | `[ValidateSet(...)]` allowed values | Singleton: `"One of: A, B, C"`. Array element type: `"Each item is one of: A, B, C"`. Element ordering matches the declaration. |
| 4 | `"Parameter of type <TypeName>"` | Final fallback. `<TypeName>` is the .NET type name (e.g. `System.String`). |

The chain is applied **per parameter**. The same parameter appearing in multiple parameter sets receives the same description text.

**Authoring example.** A `.PARAMETER` block lands at step 1:

```powershell
function Get-FixtureFullHelp {
    <#
    .PARAMETER Message
    The text to echo back to the caller.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )
    $Message
}
```

A `[Parameter(HelpMessage = ...)]` lands at step 2 when there is no `.PARAMETER` block:

```powershell
function Get-FixtureHelpMessageOnly {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, HelpMessage = 'The user identifier to look up.')]
        [string]$UserId
    )
    [pscustomobject]@{ UserId = $UserId }
}
```

A `[ValidateSet]` with no other help lands at step 3:

```powershell
function Get-FixtureValidateSetScalar {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('Red', 'Green', 'Blue')]
        [string]$Color
    )
    $Color
}
```

The resulting parameter description is `"One of: Red, Green, Blue"`. If the parameter were typed `[string[]]` instead of `[string]`, the description would be `"Each item is one of: Red, Green, Blue"`.

A bare parameter with no help, no `HelpMessage`, and no `ValidateSet` falls through to step 4: `"Parameter of type System.String"`.

> **Known issue.** Parameter descriptions resolved through this chain are not yet surfaced into `inputSchema.properties.<name>.description` in every MCP response. The resolver returns the correct value and doctor reports the correct source, but plumbing into the published schema is tracked by [issue #242](https://github.com/usepowershell/poshmcp/issues/242). Module authors who want their parameter help to reach clients today should still add the help — it will appear automatically once #242 ships, with no module change required.

### Sanitization and length caps

All description text from any source is normalized before it leaves PoshMcp:

1. Leading and trailing whitespace are trimmed from the overall string.
2. Non-printable control characters (Unicode category `Cc`) are stripped, except for the paragraph separator `\n\n` produced by step 2 of the tool chain.
3. Within each paragraph, runs of whitespace — spaces, tabs, single `\n`, `\r`, `\r\n` — collapse to a single space. The `\n\n` separators between paragraphs are preserved.
4. Each paragraph is re-trimmed.

Tool descriptions are then capped at **1024 characters** and parameter descriptions at **512 characters**. Truncation lands on a word boundary and appends a single ellipsis (`…`, U+2026).

Sanitization is what makes the in-process path and the out-of-process subprocess produce identical text — `Get-Help` paragraph wrapping varies with `$Host.UI.RawUI.BufferSize`, which differs between an attached console host and a subprocess with redirected stdin/stdout. The collapse step absorbs that difference.

## Inspecting the resolved source

PoshMcp exposes the precedence step that produced each description so operators can verify what their MCP clients will see without connecting one.

### Doctor output

The `poshmcp doctor` command reports the resolved source per command and per parameter via a `descriptionSource` JSON field:

| Scope | JSON path | Allowed values |
| ----- | --------- | -------------- |
| Tool | `tools[].descriptionSource` | `synopsis`, `description`, `syntax`, `name` |
| Parameter | `tools[].parameters[].descriptionSource` | `helpParameter`, `helpMessage`, `validateSet`, `typeFallback` |

Operators looking at impoverished tool metadata can use this field to identify which commands fall through to the syntax or bare-name fallback and add `.SYNOPSIS` blocks to fix it.

### OpenTelemetry counters

PoshMcp emits one OTel counter per chain, incremented once per command (or per parameter) per discovery cycle:

- `poshmcp.tool_description.source` — tag `step` ∈ `{synopsis, description, syntax, name}`
- `poshmcp.parameter_description.source` — tag `step` ∈ `{helpParameter, helpMessage, validateSet, typeFallback}`

Tag values match the `descriptionSource` literals exactly. A dashboard that breaks either counter down by the `step` tag shows at a glance how many tools land on each rung of the chain.

## Worked example

A function with full comment-based help:

```powershell
function Get-WidgetReport {
    <#
    .SYNOPSIS
    Generate a status report for one widget.

    .DESCRIPTION
    Reads the widget identified by -WidgetId and returns a structured
    report containing health, last-seen timestamp, and pending alerts.

    .PARAMETER WidgetId
    The widget identifier. Sourced from the upstream inventory system.

    .PARAMETER Format
    Output format. Defaults to Json.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$WidgetId,

        [ValidateSet('Json', 'Yaml', 'Text')]
        [string]$Format = 'Json'
    )
    # ...
}
```

Resolved fields in the MCP `tools/list` response (after sanitization, before length capping):

- `tools[].description` → `"Generate a status report for one widget."` (tool chain step 1, source `synopsis`)
- `tools[].inputSchema.properties.WidgetId.description` → `"The widget identifier. Sourced from the upstream inventory system."` (parameter chain step 1, source `helpParameter`)
- `tools[].inputSchema.properties.Format.description` → `"Output format. Defaults to Json."` (parameter chain step 1, source `helpParameter`)

Removing the `.PARAMETER Format` block would change the `Format` parameter to step 3 (`"One of: Json, Yaml, Text"`, source `validateSet`). Removing the `[ValidateSet]` as well would land at step 4 (`"Parameter of type System.String"`, source `typeFallback`).

The `HelpParityFixture` module under `PoshMcp.Tests/Fixtures/Modules/HelpParityFixture/` exercises every rung of both chains and is a good reference for additional shapes.
