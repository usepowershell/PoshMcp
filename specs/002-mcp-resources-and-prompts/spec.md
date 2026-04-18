# Feature Specification: MCP Resources and MCP Prompts

**Feature Branch**: `002-mcp-resources-and-prompts`
**Created**: 2026-07-15
**Status**: Draft
**Input**: Extend PoshMcp with MCP Resources (static/dynamic content) and MCP Prompts (reusable prompt templates), both backed by files or PowerShell commands, configured declaratively in `appsettings.json`

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Expose Static Files as MCP Resources (Priority: P1)

A DevOps engineer has configuration files, runbooks, and documentation
on disk that they want to share with an AI agent as grounded context.
They configure PoshMcp to expose those files as MCP resources, and an
AI agent can immediately `resources/list` to discover them and
`resources/read` to fetch their content — without writing any code.

**Why this priority**: Files are the simplest, most universally useful
resource source. This is the "hello world" of MCP Resources. If
engineers can't expose a file, the feature has no foothold.

**Independent Test**: Can be fully tested by configuring one
`McpResources` entry with `"Source": "file"`, starting the server,
calling `resources/list` to confirm the resource appears, and calling
`resources/read` to confirm the file contents are returned verbatim.

**Acceptance Scenarios**:

1. **Given** an `McpResources` entry with `"Source": "file"` and a valid `Path`, **When** an MCP client sends `resources/list`, **Then** the response includes the resource with its configured `Uri`, `Name`, `Description`, and `MimeType`
2. **Given** a file-backed resource is configured with a relative `Path`, **When** `resources/read` is called with the resource's URI, **Then** the file is read relative to the `appsettings.json` directory and returned as text content
3. **Given** a file-backed resource is configured with an absolute `Path`, **When** `resources/read` is called, **Then** the file at that absolute path is read and returned
4. **Given** a file-backed resource has `MimeType: "application/json"`, **When** the resource is read, **Then** the response includes `mimeType: "application/json"` in the content entry
5. **Given** two resources are configured with different URIs, **When** `resources/list` is called, **Then** both appear in the response

---

### User Story 2 — Expose PowerShell-Generated Content as MCP Resources (Priority: P1)

A platform engineer wants to expose dynamic system state — running
processes, service topology, Azure resource inventory — to an AI agent
as a readable resource. They configure a PowerShell command as the
resource source. When the AI reads the resource, PoshMcp executes the
command and returns fresh output.

**Why this priority**: Command-backed resources unlock live system data
as first-class AI context. This is the differentiator over a static
file server and directly leverages PoshMcp's core PowerShell execution
engine.

**Independent Test**: Can be tested by configuring a `McpResources`
entry with `"Source": "command"` and a `Command` string, calling
`resources/list` to confirm discovery, and calling `resources/read` to
verify the command is executed and its output returned.

**Acceptance Scenarios**:

1. **Given** a resource with `"Source": "command"` and `"Command": "Get-Process | Select-Object Name, Id | ConvertTo-Json"`, **When** `resources/read` is called, **Then** the server executes the command in the shared runspace and returns its string output as the resource content
2. **Given** a command-backed resource, **When** `resources/read` is called twice in succession, **Then** the command is executed each time (no caching), returning fresh output on each call
3. **Given** a command-backed resource whose command returns non-string output, **When** `resources/read` is called, **Then** the output is serialized to a string (via `ConvertTo-Json` or `ToString()`) before being returned
4. **Given** a command-backed resource whose command throws a terminating error, **When** `resources/read` is called, **Then** the server returns an MCP error response — it does not crash

---

### User Story 3 — Define Reusable Prompt Templates Backed by Files (Priority: P1)

A prompt engineer maintains markdown templates for common AI workflows
(service analysis, incident review, deployment checklists). They store
these as `.md` files and configure PoshMcp to expose them as MCP
prompts with named arguments. An AI agent calls `prompts/get` with
argument values and receives a rendered, ready-to-use prompt message.

**Why this priority**: File-backed prompts are the most predictable
and version-controllable prompt source. They integrate naturally with
git-managed prompt libraries.

**Independent Test**: Can be tested by configuring a `McpPrompts` entry
with `"Source": "file"`, calling `prompts/list` to confirm discovery,
and calling `prompts/get` with argument values to verify the file
content is returned as prompt messages.

**Acceptance Scenarios**:

1. **Given** a `McpPrompts` entry with `"Source": "file"`, **When** an MCP client sends `prompts/list`, **Then** the response includes the prompt with its configured `Name`, `Description`, and `Arguments` list
2. **Given** a file-backed prompt has a `Required: true` argument `serviceName`, **When** `prompts/list` is called, **Then** the argument appears with `required: true` in the arguments array
3. **Given** a file-backed prompt with `Path: "./prompts/analyze-service.md"`, **When** `prompts/get` is called, **Then** the file is read and returned as a user-role message in the prompt messages array
4. **Given** a file-backed prompt is requested with no arguments, **When** `prompts/get` is called, **Then** the raw file content is returned without substitution errors

---

### User Story 4 — Define Prompts Backed by PowerShell Commands (Priority: P2)

A toolmaker writes a PowerShell function that constructs context-rich
prompts from live system state — incorporating current environment
variables, recent event log entries, or Azure resource details. They
configure PoshMcp to expose that function as a prompt source. When an
AI calls `prompts/get`, the command runs and its output becomes the
prompt.

**Why this priority**: Command-backed prompts enable dynamic prompt
construction. Valuable but higher complexity; file-backed prompts must
work first.

**Independent Test**: Can be tested by configuring a `McpPrompts` entry
with `"Source": "command"`, calling `prompts/list` to confirm it
appears, and calling `prompts/get` to verify the command runs and its
string output is returned as prompt message content.

**Acceptance Scenarios**:

1. **Given** a `McpPrompts` entry with `"Source": "command"` and `"Command": "Get-SystemSummaryPrompt"`, **When** `prompts/get` is called, **Then** the command is executed in the shared runspace and its string output is returned as a user-role message
2. **Given** a command-backed prompt has configured `Arguments`, **When** `prompts/get` is called with argument values, **Then** the argument values are injected into the PowerShell command via `$args` positional parameters or by pre-setting `$argumentName` variables before invocation (see FR-024)
3. **Given** a command-backed prompt's command throws a terminating error, **When** `prompts/get` is called, **Then** the server returns an MCP error — it does not crash

---

### User Story 5 — `poshmcp doctor` Validates Resource and Prompt Configuration (Priority: P2)

An operator deploys PoshMcp with a new `McpResources` and `McpPrompts`
configuration block. Before connecting an AI agent, they run
`poshmcp doctor` to verify that all file paths resolve, all commands
are non-empty, URIs are valid and unique, and argument definitions are
well-formed. Doctor reports any problems with actionable messages so
the operator can fix config before the first client request.

**Why this priority**: Configuration errors in resources/prompts are
silent until a client hits them. Doctor validation surfaces issues at
deploy time — consistent with the existing doctor pattern for tool
configuration.

**Independent Test**: Can be tested by configuring a resource with a
non-existent file path, running `poshmcp doctor`, and verifying the
output includes a validation failure identifying that resource by URI
and explaining the missing path.

**Acceptance Scenarios**:

1. **Given** a file-backed resource with a non-existent `Path`, **When** `poshmcp doctor` runs, **Then** the output includes a failure entry naming the resource URI and stating the file was not found
2. **Given** a command-backed resource with an empty `Command` string, **When** `poshmcp doctor` runs, **Then** the output includes a failure entry naming the resource URI and stating the command is empty
3. **Given** two resources share the same `Uri`, **When** `poshmcp doctor` runs, **Then** the output reports a duplicate URI conflict
4. **Given** a prompt argument has `Required: true` but an empty `Name`, **When** `poshmcp doctor` runs, **Then** the output reports a malformed argument definition
5. **Given** all resources and prompts are valid, **When** `poshmcp doctor` runs, **Then** the resources and prompts section shows a passing status with counts
6. **Given** `poshmcp validate-config` is run, **Then** resource and prompt validation runs identically to the doctor checks

---

### Edge Cases

- What happens when a file-backed resource's file is deleted between server startup and a `resources/read` call?
- What happens when a command-backed resource's command produces no output (void/empty)?
- What happens when a command-backed resource returns binary content that cannot be represented as UTF-8 text?
- What happens when two prompts share the same `Name`?
- What happens when `prompts/get` is called with extra arguments not declared in `Arguments`?
- What happens when a required prompt argument is not supplied in `prompts/get`?
- What happens when a file path contains environment variables (e.g., `%APPDATA%` or `$HOME`)?
- What happens when a command-backed prompt's command output is very large (> 1 MB)?
- What happens when `McpResources` or `McpPrompts` sections are absent from `appsettings.json` entirely?
- What happens when a file resource's `MimeType` is omitted?
- What happens when a command produces structured objects rather than strings?

## Requirements *(mandatory)*

### Functional Requirements

*(Continuing from FR-017 in spec 001)*

- **FR-018**: System MUST implement the `resources/list` MCP method and return the configured list of resources with `uri`, `name`, `description`, and `mimeType` fields
- **FR-019**: System MUST implement the `resources/read` MCP method and return resource content as text or blob entries, keyed by URI
- **FR-020**: System MUST support a **file source** for resources — reading a file from disk at request time using the configured `Path` (absolute or relative to `appsettings.json`)
- **FR-021**: System MUST support a **command source** for resources — executing a PowerShell command string in the shared runspace at request time and returning its string output
- **FR-022**: System MUST implement the `prompts/list` MCP method and return the configured list of prompts with `name`, `description`, and `arguments` fields
- **FR-023**: System MUST implement the `prompts/get` MCP method and return a rendered prompt as a list of messages (role + content)
- **FR-024**: System MUST support a **file source** for prompts — reading a file from disk at request time and returning its content as a user-role message
- **FR-025**: System MUST support a **command source** for prompts — executing a PowerShell command string with argument values injected as PowerShell variables and returning string output as a user-role message
- **FR-026**: System MUST validate resource and prompt configuration in `poshmcp doctor`: missing file paths, empty commands, duplicate URIs/names, malformed argument definitions
- **FR-027**: System MUST validate resource and prompt configuration in `poshmcp validate-config` using the same checks as FR-026
- **FR-028**: System MUST register resources via the MCP SDK's `WithListResourcesHandler` and `WithReadResourceHandler` extension methods on the MCP server builder
- **FR-029**: System MUST register prompts via the MCP SDK's `WithListPromptsHandler` and `WithGetPromptHandler` extension methods on the MCP server builder
- **FR-030**: System MUST default `MimeType` to `"text/plain"` when not specified for a resource
- **FR-031**: System MUST serialize command output to string when it is not already a string — using `ConvertTo-Json -Depth 4 -Compress` for structured objects, or `ToString()` for scalars, consistent with the existing serialization pipeline
- **FR-032**: System MUST inject prompt argument values into command-backed prompts by setting PowerShell variables (`$argumentName = value`) in the runspace before executing the command string
- **FR-033**: System MUST return an MCP error response (not a server crash) when a file-backed source cannot be read or a command-backed source throws a terminating error
- **FR-034**: System MUST resolve relative `Path` values relative to the directory containing the active `appsettings.json` file

### Key Entities

- **MCP Resource**: A discoverable, readable piece of content exposed to AI agents — has a URI, name, description, MIME type, and a source (file or command) that produces its content on demand
- **MCP Prompt**: A discoverable, renderable prompt template exposed to AI agents — has a name, description, optional arguments, and a source (file or command) that produces its message content
- **Resource Source**: The mechanism that produces a resource's content — either a file read from disk or a PowerShell command executed in the shared runspace
- **Prompt Source**: The mechanism that produces a prompt's content — either a file read from disk or a PowerShell command executed in the shared runspace with argument variables pre-set
- **Prompt Argument**: A named, optionally required parameter declared on a prompt — argument values are supplied by MCP clients at `prompts/get` time and injected into command-backed prompts as PowerShell variables
- **McpResources Configuration**: A top-level `appsettings.json` section (sibling to `PowerShellConfiguration`) declaring the list of resource definitions
- **McpPrompts Configuration**: A top-level `appsettings.json` section (sibling to `PowerShellConfiguration`) declaring the list of prompt definitions

### Configuration Schema

`McpResources` and `McpPrompts` are **top-level siblings** to `PowerShellConfiguration` in `appsettings.json`. They are not nested under `PowerShellConfiguration` because resources and prompts are MCP-layer concerns, not PowerShell-execution concerns.

```json
{
  "PowerShellConfiguration": { },

  "McpResources": {
    "Resources": [
      {
        "Uri": "poshmcp://resources/server-config",
        "Name": "Server Configuration",
        "Description": "The active appsettings.json contents",
        "MimeType": "application/json",
        "Source": "file",
        "Path": "./appsettings.json"
      },
      {
        "Uri": "poshmcp://resources/process-list",
        "Name": "Running Processes",
        "Description": "Current process list as JSON",
        "MimeType": "application/json",
        "Source": "command",
        "Command": "Get-Process | Select-Object Name, Id, CPU | ConvertTo-Json"
      }
    ]
  },

  "McpPrompts": {
    "Prompts": [
      {
        "Name": "analyze-service",
        "Description": "Prompt to analyze a named Windows service",
        "Source": "file",
        "Path": "./prompts/analyze-service.md",
        "Arguments": [
          { "Name": "serviceName", "Description": "The service to analyze", "Required": true }
        ]
      },
      {
        "Name": "system-summary",
        "Description": "Generate a system summary prompt from live state",
        "Source": "command",
        "Command": "Get-SystemSummaryPrompt",
        "Arguments": []
      }
    ]
  }
}
```

**Resource definition fields:**

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `Uri` | string | Yes | Must be unique across all resources. Recommend `poshmcp://resources/{slug}` scheme. |
| `Name` | string | Yes | Human-readable display name. |
| `Description` | string | No | Shown in `resources/list` response. |
| `MimeType` | string | No | Defaults to `"text/plain"` if omitted. |
| `Source` | string | Yes | `"file"` or `"command"`. |
| `Path` | string | Conditional | Required when `Source = "file"`. Absolute or relative to `appsettings.json`. |
| `Command` | string | Conditional | Required when `Source = "command"`. Executed in shared runspace. |

**Prompt definition fields:**

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `Name` | string | Yes | Must be unique across all prompts. Used in `prompts/get`. |
| `Description` | string | No | Shown in `prompts/list` response. |
| `Source` | string | Yes | `"file"` or `"command"`. |
| `Path` | string | Conditional | Required when `Source = "file"`. Absolute or relative to `appsettings.json`. |
| `Command` | string | Conditional | Required when `Source = "command"`. Executed in shared runspace with args pre-set. |
| `Arguments` | array | No | List of argument definitions (may be empty). |

**Prompt argument fields:**

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `Name` | string | Yes | Must be a valid PowerShell variable name (no `$` prefix in config). |
| `Description` | string | No | Shown in `prompts/list` arguments. |
| `Required` | bool | No | Defaults to `false`. Doctor warns if a required argument has no name. |

### SDK Integration Notes

The MCP SDK (ModelContextProtocol 1.2.0) provides four extension methods on the server builder for registering these handlers. Implementers (Bender/Hermes) should use these and not attempt custom JSON-RPC routing:

- `WithListResourcesHandler` — registers the `resources/list` handler
- `WithReadResourceHandler` — registers the `resources/read` handler
- `WithListPromptsHandler` — registers the `prompts/list` handler
- `WithGetPromptHandler` — registers the `prompts/get` handler

All four are registered during the server builder phase in `Program.cs`, alongside the existing `WithToolsFromProvider` and filter registrations. The handlers receive a `RequestContext` providing access to `IConfiguration` and `IServiceProvider` for loading configuration and resolving the shared runspace.

### Doctor Validation Contract

`poshmcp doctor` and `poshmcp validate-config` MUST report the following resource/prompt checks in their output:

| Check | Severity | Condition |
|-------|----------|-----------|
| File path exists | Error | `Source = "file"` and `Path` does not resolve to an existing file |
| Command non-empty | Error | `Source = "command"` and `Command` is null, empty, or whitespace |
| URI unique | Error | Two or more resources share the same `Uri` |
| Name unique | Error | Two or more prompts share the same `Name` |
| Source valid | Error | `Source` is not `"file"` or `"command"` |
| URI format | Warning | `Uri` does not follow the recommended `poshmcp://resources/{slug}` scheme |
| Argument name valid | Error | An argument `Name` is null or empty |
| Required arg named | Error | An argument has `Required: true` and empty `Name` |
| MimeType present | Warning | Resource `MimeType` is omitted (will default to `text/plain`) |

Doctor JSON output adds a `"resources"` and `"prompts"` key to the existing diagnostics structure:

```json
{
  "resources": {
    "configured": 2,
    "valid": 2,
    "errors": [],
    "warnings": []
  },
  "prompts": {
    "configured": 2,
    "valid": 1,
    "errors": ["Prompt 'analyze-service': Path './prompts/analyze-service.md' not found"],
    "warnings": []
  }
}
```

## Success Criteria *(mandatory)*

### Measurable Outcomes

*(Continuing from SC-008 in spec 001)*

- **SC-009**: An operator can expose a file as an MCP resource by adding one JSON object to `McpResources.Resources` — no code changes required
- **SC-010**: `resources/list` and `resources/read` respond correctly for both file-backed and command-backed resources with zero code-level resource routing logic needed per resource
- **SC-011**: `prompts/list` and `prompts/get` respond correctly for both file-backed and command-backed prompts; argument values reach the command execution context
- **SC-012**: `poshmcp doctor` surfaces every misconfigured resource and prompt (missing file, empty command, duplicate URI/name) before any MCP client connects — zero silent runtime failures for validatable errors
- **SC-013**: All three test tiers (unit, functional, integration) include coverage for `resources/list`, `resources/read`, `prompts/list`, and `prompts/get` with both source types
- **SC-014**: Removing `McpResources` or `McpPrompts` sections entirely from `appsettings.json` causes zero errors — the server starts normally with empty resource and prompt catalogs
- **SC-015**: Command-backed resource and prompt execution reuses the shared PowerShell runspace — no new runspace is created per request

## Assumptions

- The MCP SDK (ModelContextProtocol 1.2.0) handler extension methods (`WithListResourcesHandler`, etc.) are sufficient for registration; no custom JSON-RPC dispatch is required
- File-backed sources do not need caching — freshness on every read is the correct default; operators who need caching can use command-backed sources with explicit caching in their PowerShell command
- Command-backed resources and prompts are read-only by convention — the spec does not enforce this at runtime, but operators are expected to configure only non-mutating commands as resource/prompt sources
- Argument substitution for file-backed prompts is out of scope for the initial implementation — the file is returned verbatim; the MCP client is responsible for applying argument values to the template text
- Resource subscription (`resources/subscribe`, `resources/unsubscribe`) and change notifications are out of scope for this feature; the MCP SDK handlers registered do not need to support push notifications
- `poshmcp://resources/{slug}` is the recommended URI scheme but is not enforced — any valid URI string is accepted
- PowerShell variable injection for prompt arguments uses pre-assignment (`$argName = value` before command execution), not `-ArgumentList`, to keep the command string simple and readable
- Relative paths are resolved against the directory of the active `appsettings.json` at request time, not at server startup, so hot-reloaded config changes are reflected immediately
