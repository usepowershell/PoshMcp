az# Feature Specification: MCP PowerShell Server

**Feature Branch**: `001-mcp-powershell-server`
**Created**: 2026-04-06
**Status**: Draft
**Input**: MCP server that dynamically transforms PowerShell commands into AI-consumable tools via Model Context Protocol

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Expose PowerShell Commands as MCP Tools (Priority: P1)

A PowerShell expert has existing cmdlets and scripts they use for
automation. They want to make those commands available to AI agents
without writing any wrapper code. They configure a list of function
names in `appsettings.json`, start the MCP server, and an AI agent
can immediately discover and invoke those commands.

**Why this priority**: This is the core value proposition. Without
automatic PowerShell-to-MCP tool transformation, the product has
no reason to exist.

**Independent Test**: Can be fully tested by starting the stdio
server, calling `tools/list`, and verifying that configured
PowerShell commands appear with correct JSON schemas.

**Acceptance Scenarios**:

1. **Given** a PowerShell function `Get-Service` is listed in `FunctionNames`, **When** an MCP client sends `tools/list`, **Then** the response includes a tool named `Get-Service` with its parameters mapped to JSON Schema types
2. **Given** the server has discovered `Get-Process`, **When** an MCP client sends `tools/call` with `{"name": "Get-Process"}`, **Then** the server executes the command and returns structured output
3. **Given** a PowerShell function has mandatory parameters, **When** the tool schema is generated, **Then** those parameters appear as `required` in the JSON Schema

---

### User Story 2 - Persistent PowerShell State Across Calls (Priority: P1)

A DevOps engineer sets a variable in one MCP tool call (e.g.,
`$ConnectionString = "..."`) and expects it to be available in
subsequent calls. They also define helper functions that persist
across the session.

**Why this priority**: State persistence differentiates PoshMcp from
stateless script execution. Without it, complex multi-step
automation workflows are impossible.

**Independent Test**: Can be tested by executing two sequential
`tools/call` requests — the first sets a variable, the second reads
it — and verifying the value is preserved.

**Acceptance Scenarios**:

1. **Given** a variable `$MyVar` is set in call 1, **When** call 2 reads `$MyVar`, **Then** the value from call 1 is returned
2. **Given** a custom function is defined in call 1, **When** call 2 invokes that function, **Then** it executes successfully
3. **Given** modules are imported in the PowerShell session, **When** subsequent calls use those module commands, **Then** they execute without re-importing

---

### User Story 3 - Dynamic Tool Configuration (Priority: P2)

An operator wants to add or remove exposed PowerShell commands
without restarting the server. They update the configuration and
the tool catalog refreshes to reflect the changes.

**Why this priority**: Enables runtime flexibility, but the core
discovery and execution engine must work first.

**Independent Test**: Can be tested by changing configuration at
runtime and verifying `tools/list` returns the updated catalog.

**Acceptance Scenarios**:

1. **Given** `EnableDynamicReloadTools` is true, **When** a new function is added to configuration, **Then** `tools/list` includes the new tool without server restart
2. **Given** a function is removed from configuration, **When** `tools/list` is called, **Then** the removed function no longer appears

---

### User Story 4 - Web Mode with Multi-User Isolation (Priority: P2)

Multiple users access PoshMcp via its HTTP interface simultaneously.
Each user gets an isolated PowerShell session — variables and state
from one user never leak to another.

**Why this priority**: Required for shared deployment scenarios (team
servers, Azure Container Apps) but not needed for local MCP client use.

**Independent Test**: Can be tested by sending concurrent requests
with different session identifiers and verifying state isolation.

**Acceptance Scenarios**:

1. **Given** User A sets `$Secret = "A-secret"` in their session, **When** User B reads `$Secret`, **Then** User B gets `$null` or an error — not User A's value
2. **Given** the web server is running, **When** a client hits `/health`, **Then** it returns a 200 OK with health status
3. **Given** the web server is running, **When** a client hits `/health/ready`, **Then** it returns readiness state including PowerShell runspace health

---

### User Story 5 - Pattern-Based Command Filtering (Priority: P2)

A security-conscious operator wants to expose only `Get-*` commands
and explicitly block `*-Dangerous*` patterns. They configure include
and exclude patterns and trust that only matching commands are
discoverable.

**Why this priority**: Critical for production security, but the
exposure mechanism must exist before filtering it.

**Independent Test**: Can be tested by configuring patterns and
verifying `tools/list` only returns commands matching include
patterns and not matching exclude patterns.

**Acceptance Scenarios**:

1. **Given** `IncludePatterns` contains `"Get-*"`, **When** `tools/list` is called, **Then** only commands starting with `Get-` appear
2. **Given** `ExcludePatterns` contains `"*-Dangerous*"`, **When** `tools/list` is called, **Then** no commands matching that pattern appear regardless of include rules

---

### User Story 6 - Docker and Azure Deployment (Priority: P3)

An operations team deploys PoshMcp as a Docker container to Azure
Container Apps. They pre-install PowerShell modules at build time
for fast startup and rely on Azure Managed Identity for secure
resource access.

**Why this priority**: Deployment is important but depends on the
core server working correctly first.

**Independent Test**: Can be tested by building the Docker image,
running it, and verifying health endpoints respond and tools are
discoverable.

**Acceptance Scenarios**:

1. **Given** a Dockerfile with pre-installed modules, **When** the container starts, **Then** startup completes in under 5 seconds
2. **Given** the container is deployed with Azure Managed Identity, **When** a PowerShell command calls `Connect-AzAccount -Identity`, **Then** authentication succeeds without stored credentials

---

### Edge Cases

- What happens when a configured PowerShell function does not exist in the runspace?
- How does the server handle a PowerShell command that throws a terminating error?
- What happens when a command returns no output (void)?
- How does the server handle a command that produces extremely large output?
- What happens when two MCP clients connect to the same stdio server simultaneously?
- How does the server handle malformed JSON-RPC requests?
- What happens when a PowerShell module fails to import at startup?
- How does the server respond when a configured include pattern matches zero commands?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST automatically discover PowerShell command signatures using introspection (`Get-Command`, `Get-Help`)
- **FR-002**: System MUST generate valid JSON Schema for each discovered command's parameters, mapping PowerShell types to JSON types
- **FR-003**: System MUST expose discovered commands as MCP tools via the `tools/list` method
- **FR-004**: System MUST execute PowerShell commands when invoked via the `tools/call` method and return structured results
- **FR-005**: System MUST maintain a persistent PowerShell runspace so variables, functions, and imported modules survive across calls
- **FR-006**: System MUST support stdio transport for direct MCP client integration
- **FR-007**: System MUST support HTTP transport for web-based integration with multi-user session isolation
- **FR-008**: System MUST support configurable command filtering via include patterns, exclude patterns, and explicit function name lists
- **FR-009**: System MUST handle the standard MCP lifecycle: `initialize`, `tools/list`, `tools/call`
- **FR-010**: System MUST return proper MCP error responses with appropriate error codes when commands fail
- **FR-011**: System MUST provide health check endpoints (`/health`, `/health/ready`) in web mode
- **FR-012**: System MUST support correlation IDs for request tracing across distributed systems
- **FR-013**: System MUST emit OpenTelemetry metrics for tool executions and performance
- **FR-014**: System MUST support environment customization: startup scripts, module installation, custom module paths
- **FR-015**: System MUST support Azure Managed Identity when deployed as a container in Azure
- **FR-016**: System MUST cache dynamic assembly generation and function metadata to avoid repeated reflection
- **FR-017**: System MUST provide built-in utility tools for working with command output (get, sort, filter, group last command output)

### Key Entities

- **MCP Tool**: A discoverable, invocable capability exposed to AI agents — represents a PowerShell command with its parameter schema, metadata, and execution context
- **PowerShell Runspace**: An isolated execution environment that maintains state (variables, functions, modules) across multiple command invocations
- **Tool Schema**: JSON Schema representation of a PowerShell command's parameters, generated dynamically from command metadata
- **PowerShell Configuration**: The declarative configuration that controls which commands are exposed, which patterns to include/exclude, and environment setup
- **Operation Context**: A correlation tracking entity that links requests across distributed components for observability

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A PowerShell expert can expose a new command to AI agents by adding one line to a JSON configuration file — no code changes required
- **SC-002**: All three test tiers (unit, functional, integration) pass with zero failures before any release
- **SC-003**: The server responds to `tools/list` within 2 seconds of startup, even with 50+ configured commands
- **SC-004**: State set in one `tools/call` invocation is retrievable in the next invocation within the same session 100% of the time
- **SC-005**: Docker container with pre-installed modules starts and passes health checks in under 5 seconds
- **SC-006**: In web mode, concurrent sessions MUST maintain complete state isolation — zero cross-session data leakage
- **SC-007**: Every tool execution is traceable via correlation IDs and OpenTelemetry metrics
- **SC-008**: Exclude patterns MUST prevent command exposure with zero bypass scenarios

## Assumptions

- Target users are PowerShell experts (DevOps engineers, sysadmins, toolmakers) comfortable with PowerShell and JSON configuration
- MCP clients (AI agents, VS Code, Claude Desktop, etc.) implement the MCP specification correctly
- .NET 8 runtime is available in the deployment environment
- For Azure deployments, Azure Managed Identity is configured at the infrastructure level
- PowerShell modules installed from PSGallery are trusted and compatible with PowerShell 7.4.x
- The stdio transport serves a single client at a time (multiplexing is not in scope)
- Web mode operates behind a reverse proxy or load balancer in production environments
