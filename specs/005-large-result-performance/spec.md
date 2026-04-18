# Feature Specification: Large Result Set Performance

**Feature Branch**: `005-large-result-performance`
**Created**: 2026-04-17
**Status**: Draft
**Input**: Reduce JSON payload size and memory pressure when PowerShell commands return large result sets, by making result caching opt-in and limiting output to display-relevant properties by default

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Right-Sized Output for Common Commands (Priority: P1)

A platform engineer uses PoshMcp to ask an AI agent about running
processes on a server. The agent calls `Get-Process` and today receives
a 3–5 MB JSON payload containing all 80+ properties of every process
object — most of which (nested handles, raw memory counters, thread
details) the agent neither needs nor uses. The response is slow to
produce, slow to transmit, and burns large amounts of the agent's
context window. The engineer wants PoshMcp to return only the display-
relevant properties by default (the same five properties PowerShell
itself shows in a terminal), with the option to request the full object
when needed.

**Why this priority**: Payload size directly impacts LLM token costs, agent
reasoning quality, and response latency. A 95%+ reduction for the most
common commands is achievable with minimal configuration. This is the
highest-ROI change in the spec.

**Independent Test**: Can be tested by calling `Get-Process` via MCP,
measuring the JSON response size, and verifying it is under 150 KB
(consistent with returning 5 default display properties across ~250
processes). Then re-calling with `"_AllProperties": true` and verifying
the full payload is returned.

**Acceptance Scenarios**:

1. **Given** `UseDefaultDisplayProperties` is true (the default), **When** `Get-Process` is invoked, **Then** the JSON response includes only the properties in the command's `DefaultDisplayPropertySet` (e.g., `Id`, `Handles`, `CPU`, `SI`, `Name`) — not all 80+ properties
2. **Given** a command is invoked with `"_AllProperties": true`, **When** the response is returned, **Then** it includes every property on the returned objects, overriding the default display filter
3. **Given** a command with no `DefaultDisplayPropertySet` is invoked, **When** the response is returned, **Then** all properties are included (graceful fallback — no filtering applied)
4. **Given** a function has an explicit `DefaultProperties` list in `FunctionOverrides`, **When** that function is invoked, **Then** only the listed properties appear in the response, regardless of the global `UseDefaultDisplayProperties` setting
5. **Given** `UseDefaultDisplayProperties` is set to false for a specific function in `FunctionOverrides`, **When** that function is invoked, **Then** all properties are returned for that function even if the global setting is true

---

### User Story 2 — Opt-In Result Caching for Post-Hoc Analysis (Priority: P2)

A data analyst uses PoshMcp to query service status across a fleet of
servers. After calling `Get-Service`, they want to slice the results —
filtering by status, sorting by name, grouping by service type — without
re-running the expensive command for each query. They enable result
caching for `Get-Service` in their configuration and then use the
`filter-last-command-output`, `sort-last-command-output`, and
`group-last-command-output` tools to explore the cached results
interactively.

**Why this priority**: Result caching provides real value for exploratory
workflows, but it unconditionally doubles memory usage today. Making it
opt-in preserves the value for users who need it while eliminating the
cost for the 90%+ who do not.

**Independent Test**: Can be tested by disabling caching globally (the new
default) and verifying that `get-last-command-output` returns a clear
"caching is disabled" message. Then enabling caching for one function,
invoking it, and verifying that `filter-last-command-output` returns
filtered results from that invocation.

**Acceptance Scenarios**:

1. **Given** `EnableResultCaching` is false (the default), **When** any command is invoked, **Then** the `Tee-Object` pipeline stage is not used and `get-last-command-output` returns a message indicating caching is disabled
2. **Given** `EnableResultCaching` is true (globally or per-function), **When** that command is invoked, **Then** its output is cached and `filter-last-command-output`, `sort-last-command-output`, and `group-last-command-output` return results derived from the cache
3. **Given** `EnableResultCaching` is false globally but true for `Get-Service` in `FunctionOverrides`, **When** `Get-Service` is invoked, **Then** its output is cached and the replay tools work; calling any other command still does not cache
4. **Given** caching is disabled and an MCP client calls `get-last-command-output`, **When** the tool returns, **Then** the response contains a human-readable message explaining that caching is disabled and how to enable it — it does not return an unhandled error

---

### User Story 3 — Runtime Caching Toggle for Exploratory Sessions (Priority: P2)

An AI agent is conducting an exploratory session — it does not know in
advance which commands it will run or whether it will need to replay
results. Rather than requiring a server restart to change the caching
setting, the agent calls `set-result-caching` with `enabled: true` at
the start of an exploratory block, uses the filter and sort tools freely,
then calls `set-result-caching` with `enabled: false` when it is done to
release memory. The server honors the runtime toggle immediately on the
next command invocation.

**Why this priority**: Exploratory workflows happen at runtime, not at
configuration time. A runtime toggle makes caching genuinely useful
without requiring operators to anticipate every query pattern at deploy
time.

**Independent Test**: Can be tested by starting the server with caching
disabled, calling `set-result-caching` with `enabled: true`, invoking a
command, calling `filter-last-command-output`, and verifying it returns
filtered results. Then calling `set-result-caching` with `enabled: false`
and verifying subsequent filter calls return the "caching disabled"
message.

**Acceptance Scenarios**:

1. **Given** caching is globally disabled in configuration, **When** `set-result-caching` is called with `enabled: true`, **Then** the next command invocation caches its output and the replay tools return results
2. **Given** caching was enabled via `set-result-caching`, **When** `set-result-caching` is called with `enabled: false`, **Then** the next command invocation does not cache and replay tools return the "caching disabled" message
3. **Given** `set-result-caching` is called with `scope: "function"` and a specific function name, **When** that function is invoked, **Then** only that function's output is cached; other functions are unaffected by the toggle
4. **Given** the MCP server is restarted, **When** the server starts fresh, **Then** any runtime caching toggle is reset — only the static configuration values apply

---

### Edge Cases

- What happens when a command produces output with no `DefaultDisplayPropertySet` and `UseDefaultDisplayProperties` is true? The system falls back to returning all properties without error.
- What happens when `_AllProperties` is true and caching is enabled? The full unfiltered object set is cached and available to replay tools.
- What happens when caching is enabled and the cached result set is very large? The cache doubles memory pressure; this is the expected opt-in cost, and operators should size their environment accordingly.
- What happens when `set-result-caching` is called with `scope: "function"` but no `functionName`? The server returns a validation error identifying the missing parameter.
- What happens when `FunctionOverrides` contains a function name that is not in `FunctionNames`? The override entry is ignored silently and logged as a warning.
- What happens when property discovery (via `DefaultDisplayPropertySet`) fails for a specific command? The system falls back to returning all properties for that command.
- What happens when `_RequestedProperties` lists a property that does not exist on the returned objects? The missing property is omitted from the response (not an error).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-055**: System MUST support a `UseDefaultDisplayProperties` setting (default: `true`) that, when enabled, limits command output to only the properties in each object type's `DefaultDisplayPropertySet`
- **FR-056**: System MUST support an `_AllProperties` framework parameter on every generated tool that, when set to `true`, bypasses property filtering and returns the full object graph
- **FR-057**: System MUST support a `_RequestedProperties` framework parameter on every generated tool that, when provided, returns only the explicitly listed properties
- **FR-058**: System MUST support a `DefaultProperties` explicit list per-function in `FunctionOverrides` that takes priority over the auto-discovered `DefaultDisplayPropertySet`
- **FR-059**: System MUST support an `EnableResultCaching` setting (default: `false`) that, when disabled, removes the `Tee-Object` stage from the command pipeline
- **FR-060**: System MUST keep the four replay tools (`get-last-command-output`, `filter-last-command-output`, `sort-last-command-output`, `group-last-command-output`) in the tool list even when caching is disabled; when caching is disabled, they MUST return a descriptive message rather than an error
- **FR-061**: System MUST expose a `set-result-caching` MCP tool that allows runtime toggling of result caching at either global or per-function scope
- **FR-062**: Runtime caching overrides set via `set-result-caching` MUST take highest priority over static configuration values and MUST be scoped to the current server session (not persisted across restarts)
- **FR-063**: Property filtering via `UseDefaultDisplayProperties` MUST be resolved in priority order: explicit `DefaultProperties` in `FunctionOverrides` > `_RequestedProperties` parameter > auto-discovered `DefaultDisplayPropertySet` > all properties
- **FR-064**: System MUST resolve `EnableResultCaching` in priority order: runtime per-function override > runtime global override > static per-function `FunctionOverrides` value > static global `Performance` value > default (`false`)

### Key Entities

- **Default Display Property Set**: The subset of object properties that PowerShell's own formatting system uses when displaying a type in a terminal; typically 5–8 properties defined in type data
- **Result Cache**: The in-memory copy of a command's full output, held in the PowerShell runspace variable `$LastCommandOutput`, available to replay tools until overwritten by the next cached command
- **Property Filter**: The resolved set of property names that will be included in a command's JSON output for a specific invocation — derived from configuration, type data, or caller-supplied parameters
- **Runtime Caching State**: An ephemeral, session-scoped record of runtime caching overrides set via `set-result-caching`, taking priority over all static configuration

### Configuration Schema (if applicable)

```jsonc
{
  "PowerShellConfiguration": {
    "FunctionNames": ["Get-Process", "Get-Service"],

    "Performance": {
      // Limit output to DefaultDisplayPropertySet properties (default: true)
      "UseDefaultDisplayProperties": true,

      // Cache command output for replay tools (default: false)
      "EnableResultCaching": false
    },

    "FunctionOverrides": {
      "Get-Process": {
        // Explicit property list — overrides DefaultDisplayPropertySet
        "DefaultProperties": ["Id", "ProcessName", "CPU", "WorkingSet64", "StartTime"],
        // Enable caching for this command only
        "EnableResultCaching": true
      },
      "Get-Service": {
        // Disable property filtering for this command
        "UseDefaultDisplayProperties": false
      }
    }
  }
}
```

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-026**: `Get-Process` invoked with default settings returns a JSON payload under 150 KB (versus the current ~3 MB baseline), representing a 95%+ reduction in payload size
- **SC-027**: `Get-Process` serialization time with default settings is under 100 ms (versus the current 500 ms–2 s baseline)
- **SC-028**: With `EnableResultCaching: false` (the default), no `Tee-Object` stage is present in any command pipeline — confirmed by verifying `$LastCommandOutput` is not set after invocation
- **SC-029**: The `set-result-caching` tool takes effect on the next command invocation after the call completes — there is no restart required and no race condition under concurrent access
- **SC-030**: All four replay tools (`get-last-command-output`, `filter-last-command-output`, `sort-last-command-output`, `group-last-command-output`) return a human-readable "caching is disabled" message (not an exception or empty result) when caching is off

## Assumptions

- `DefaultDisplayPropertySet` is defined in PowerShell type data for all commonly used object types (`System.Diagnostics.Process`, `System.ServiceProcess.ServiceController`, `System.IO.FileInfo`, etc.); for types without it, the fallback is to return all properties
- Property discovery (discovering `DefaultDisplayPropertySet` for a type) should not require executing the command — it should use PowerShell type metadata queries to avoid side effects
- The default change from "caching always on" to "caching off by default" is a breaking change for any MCP client relying on `get-last-command-output`; this must be documented in release notes and the utility tools must provide actionable error messages
- Runtime caching state is tied to the server process lifetime and is not shared between sessions or clients in web mode
- Framework parameters (`_AllProperties`, `_RequestedProperties`) use an underscore prefix to distinguish them from PowerShell command parameters and must not be forwarded to the PowerShell runtime
- Property filtering is applied server-side (in the MCP server process) so the implementation is consistent across both in-process and out-of-process runtime modes
