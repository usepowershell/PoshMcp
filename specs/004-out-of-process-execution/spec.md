# Feature Specification: Out-of-Process PowerShell Execution

**Feature Branch**: `004-out-of-process-execution`
**Created**: 2026-04-17
**Status**: Draft
**Input**: Run PowerShell commands in a separate persistent `pwsh` subprocess so that heavy or conflicting modules (Az, Microsoft.Graph) cannot crash or deadlock the MCP server

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Load Heavy Modules Without Crashing the Server (Priority: P1)

A cloud platform engineer wants to expose Azure PowerShell commands
(`Get-AzContext`, `Get-AzResourceGroup`, etc.) through PoshMcp. When
they try this today, loading `Az.*` modules inside the in-process
runspace causes native assembly conflicts, type accelerator corruption,
or memory exhaustion that crashes or hangs the MCP server. They need a
mode where the server stays healthy even if a module behaves badly —
and if the subprocess crashes, it restarts automatically without
requiring them to restart the entire MCP server.

**Why this priority**: This is the blocking problem. The entire out-of-process
feature exists to solve this one failure mode. Without it, PoshMcp is
unusable with the most commonly requested enterprise PowerShell modules.

**Independent Test**: Can be tested by configuring `RuntimeMode:
"OutOfProcess"` with an Az module listed, starting the server, calling
`tools/list`, and verifying Az commands appear. Then killing the pwsh
subprocess and verifying that a subsequent `tools/call` still succeeds
(i.e., the subprocess restarted automatically).

**Acceptance Scenarios**:

1. **Given** `RuntimeMode` is `"OutOfProcess"` and `Az.Accounts` is listed in `Modules`, **When** the server starts, **Then** a `pwsh` subprocess is launched, `Az.Accounts` is imported inside it, and `tools/list` returns the discovered Az commands
2. **Given** the `pwsh` subprocess crashes unexpectedly, **When** an MCP client sends a `tools/call` request, **Then** the server automatically restarts the subprocess, re-imports modules, and fulfills the request — the MCP client does not receive a server error
3. **Given** the subprocess is running, **When** an MCP client invokes `Get-AzContext`, **Then** the command executes inside the subprocess and its JSON output is returned to the client as a normal MCP tool result
4. **Given** a module import fails during subprocess startup, **When** `tools/list` is called, **Then** the server returns whatever commands it could discover (partial schema) and logs a warning — it does not refuse to start

---

### User Story 2 — Subprocess Isolation Protects the Server (Priority: P1)

A systems operator uses PoshMcp to run administrative scripts. One of
those scripts imports a module that registers conflicting type
accelerators and corrupts the PowerShell runspace. In in-process mode,
this would corrupt the entire server and require a restart. In
out-of-process mode, only the subprocess is affected — the MCP server
remains healthy, the bad subprocess is discarded, and a new one is
started for the next request.

**Why this priority**: Isolation is the fundamental contract of the
out-of-process model. Without it, there is no reason to use a subprocess
at all.

**Independent Test**: Can be tested by invoking a tool that intentionally
corrupts its runspace (e.g., removes built-in type accelerators), then
invoking a second unrelated tool and verifying the second call succeeds
on a freshly started subprocess.

**Acceptance Scenarios**:

1. **Given** a command in the subprocess throws a terminating error that kills the subprocess, **When** the next MCP tool call arrives, **Then** the server detects the subprocess exit, starts a new subprocess, re-applies environment setup, and completes the call
2. **Given** the subprocess is running, **When** a command is invoked that hangs for longer than the configured request timeout, **Then** the server kills the subprocess, logs the timeout, starts a fresh subprocess, and returns a timeout error to the client
3. **Given** the MCP server shuts down gracefully, **When** shutdown begins, **Then** the server sends a graceful shutdown signal to the subprocess and waits for it to exit cleanly before the server process ends

---

### User Story 3 — Configure Runtime Mode Declaratively (Priority: P2)

A DevOps team deploys PoshMcp via Docker to run Azure automation
scripts. They need to switch from in-process to out-of-process mode at
deployment time without changing server code. They set an environment
variable (`POSHMCP_RUNTIME_MODE=OutOfProcess`) in their container
definition and the server automatically uses the subprocess model on
startup.

**Why this priority**: Deployment-time configurability is essential for
teams using the same container image across environments (local dev uses
in-process, production uses out-of-process). Baking the mode into the
image would create fragmentation.

**Independent Test**: Can be tested by starting the server with the
environment variable set to `OutOfProcess`, verifying via logs that the
subprocess was launched, then setting it to `InProcess` and verifying no
subprocess is started.

**Acceptance Scenarios**:

1. **Given** `POSHMCP_RUNTIME_MODE=OutOfProcess` is set, **When** the server starts, **Then** it launches a `pwsh` subprocess and uses out-of-process execution for all tool calls
2. **Given** `POSHMCP_RUNTIME_MODE=InProcess` is set (or unset), **When** the server starts, **Then** no subprocess is launched and the embedded PowerShell SDK is used
3. **Given** `RuntimeMode` is set in `appsettings.json`, **When** the environment variable is also set, **Then** the environment variable takes precedence over the config file value
4. **Given** `RuntimeMode` is set to an unrecognized value, **When** the server starts, **Then** it logs an error with the unrecognized value and falls back to in-process mode

---

### Edge Cases

- What happens when `pwsh` is not installed or not on `PATH`? The server must fail to start (or fail to enable OOP mode) with a clear actionable error.
- What happens when module installation in the subprocess takes longer than the configured timeout? The setup must time out and report which module failed.
- What happens when the subprocess produces extremely large output? The ndjson framing must handle large payloads without truncating or deadlocking.
- What happens when two tool calls arrive simultaneously in out-of-process mode? Calls must be serialized or the protocol must support concurrent in-flight requests.
- What happens when a command in out-of-process mode tries to prompt for interactive input? The subprocess runs non-interactively; the prompt must be surfaced as a clean error (not a hang).
- What happens when the subprocess restarts while a prior call's result is in flight? The response must be discarded cleanly and the client must receive an error.
- What happens when the module list changes in configuration without a server restart? A re-discover must be triggered to pick up the new modules in the subprocess.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-044**: System MUST support a configurable `RuntimeMode` setting with values `InProcess` (default) and `OutOfProcess`
- **FR-045**: When `RuntimeMode` is `OutOfProcess`, the system MUST launch a persistent external `pwsh` subprocess at startup and communicate with it over stdin/stdout using newline-delimited JSON
- **FR-046**: System MUST send a health-check ping to the subprocess after launch and before any tool discovery or invocation, to confirm the subprocess is responsive
- **FR-047**: System MUST apply environment configuration (module path additions, module installation, module imports, startup scripts) to the subprocess before tool discovery
- **FR-048**: System MUST discover available commands by requesting the subprocess to import configured modules and return command schemas including parameter names, types, and mandatory status
- **FR-049**: System MUST detect subprocess exit events and automatically restart the subprocess, re-apply environment configuration, and re-discover commands
- **FR-050**: System MUST enforce a per-request timeout for out-of-process invocations; if the subprocess does not respond within the timeout, the subprocess MUST be killed and restarted
- **FR-051**: System MUST serialize out-of-process tool invocations to prevent concurrent in-flight requests from corrupting the subprocess communication channel
- **FR-052**: System MUST resolve `RuntimeMode` with the following priority: CLI argument > environment variable > `appsettings.json` > default (`InProcess`)
- **FR-053**: System MUST gracefully shut down the subprocess when the MCP server shuts down, waiting for in-flight requests to complete or timing out and force-killing the subprocess
- **FR-054**: System MUST log subprocess stderr output as diagnostic information without treating it as a protocol error

### Key Entities

- **Runtime Mode**: The execution strategy for PowerShell commands — `InProcess` uses the embedded SDK runspace; `OutOfProcess` delegates to a persistent subprocess
- **PowerShell Subprocess**: A persistent `pwsh` process launched by the MCP server, running a host script that handles discovery and invocation requests over stdin/stdout
- **Subprocess Protocol**: The newline-delimited JSON request/response contract between the server and the subprocess, including methods `ping`, `setup`, `discover`, `invoke`, and `shutdown`
- **Command Schema**: The parameter metadata returned by the subprocess for each discovered command, including parameter names, .NET type names, mandatory status, and position
- **Environment Setup**: The ordered sequence of operations applied to the subprocess before tool discovery: module path configuration, module installation, module import, and startup script execution

### Configuration Schema (if applicable)

```jsonc
{
  "PowerShellConfiguration": {
    // "InProcess" (default) or "OutOfProcess"
    "RuntimeMode": "OutOfProcess",

    // Modules to import in the subprocess before discovery
    "Modules": ["Az.Accounts", "Az.Compute"],

    // Request timeout for subprocess invocations (seconds, default: 30)
    "SubprocessTimeoutSeconds": 30,

    // Maximum subprocess restart attempts before giving up (default: 3)
    "SubprocessMaxRestarts": 3
  }
}
```

Environment variable override:
```
POSHMCP_RUNTIME_MODE=OutOfProcess
```

### Doctor Validation Contract (if applicable)

The server's doctor/health tooling MUST verify the following when `RuntimeMode` is `OutOfProcess`:
- `pwsh` is available and on `PATH` (or at the configured path)
- The subprocess can be launched and responds to a `ping` within 5 seconds
- All configured modules can be imported without error
- At least one command is discoverable after module import

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-021**: The MCP server starts successfully and exposes Az or Microsoft.Graph commands via `tools/list` when `RuntimeMode: OutOfProcess` is configured — with no crashes, hangs, or assembly conflicts in the server process
- **SC-022**: After a subprocess crash, the next `tools/call` succeeds (with automatic restart) within `SubprocessTimeoutSeconds + startup time` — the MCP client receives a result, not a server error
- **SC-023**: `RuntimeMode` set via environment variable overrides the value in `appsettings.json` with 100% reliability across all deployment environments
- **SC-024**: Subprocess requests are serialized — zero protocol corruption or interleaved responses are observed under concurrent load
- **SC-025**: Graceful server shutdown completes within 10 seconds even when a subprocess is in-flight, with no orphaned `pwsh` processes left behind

## Assumptions

- `pwsh` (PowerShell 7+) must be installed and available on the system where out-of-process mode is used; the server does not bundle a separate pwsh binary
- The subprocess communicates exclusively over stdin/stdout; no port allocation, named pipes, or sockets are required
- One subprocess instance is created per `OutOfProcessCommandExecutor` instance — there is no process pool; concurrency is handled by serializing requests
- Module installation in the subprocess (`Install-Module`) requires internet access or a configured repository; the server does not manage offline module caches
- In out-of-process mode, interactive prompt support is out of scope (see spec 003); all commands must be non-interactive
- The subprocess host script is shipped alongside the server binary and does not require a separate installation step
