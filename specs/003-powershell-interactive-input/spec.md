# Feature Specification: PowerShell Interactive Input Handling

**Feature Branch**: `003-powershell-interactive-input`
**Created**: 2026-04-17
**Status**: Draft
**Input**: Allow PoshMcp to handle PowerShell commands that require interactive input (prompts, credentials, confirmations) gracefully rather than hanging, blocking, or failing silently

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Safe Failure for Prompt-Triggering Commands (Priority: P1)

A DevOps engineer uses PoshMcp to automate Azure operations. They
accidentally invoke a command that calls `Read-Host` or includes a
mandatory parameter they forgot to supply. Instead of the MCP call
hanging indefinitely (or the server becoming unresponsive), they
immediately receive a clear error explaining that the command requires
interactive input that cannot be provided in this mode. They can then
re-invoke the command with all required inputs supplied.

**Why this priority**: Silent hangs and cryptic timeouts are the current
behavior. Any improvement to this — even a clean fail-fast — is an
unblocking quality improvement that protects server reliability.

**Independent Test**: Can be fully tested by invoking a command that calls
`Read-Host` with no input available and verifying that an error response
is returned promptly (within the configured timeout), rather than the
call hanging.

**Acceptance Scenarios**:

1. **Given** a command that calls `Read-Host` internally, **When** an MCP client invokes it, **Then** the server returns an MCP error response within the configured timeout stating that interactive input is not supported
2. **Given** a function with a mandatory parameter that was not supplied, **When** an MCP client invokes it without that parameter, **Then** the server returns an error describing which parameter is missing rather than prompting interactively
3. **Given** a command with a `-Confirm` prompt, **When** an MCP client invokes it, **Then** the server automatically suppresses the confirmation prompt and proceeds without user interaction
4. **Given** the server is running in a container with no terminal attached, **When** any prompt-triggering command is invoked, **Then** the server fails fast with a descriptive error — it does not hang or crash

---

### User Story 2 — Structured Prompt Response (Stateless Retry) (Priority: P2)

A platform engineer builds an AI-assisted workflow where their agent
invokes `Connect-AzAccount`, which triggers a device-code authentication
flow. The server cannot complete the flow unilaterally — it needs the
user to visit a URL and enter a code. Instead of failing, the server
returns structured prompt metadata (the device code, the verification
URL, the message to display) to the AI agent. The agent presents this to
the user, collects the confirmation, and re-invokes the command with the
user's response included. The server completes the command successfully.

**Why this priority**: Interactive authentication flows (device code, MFA)
are a primary reason PowerShell tools require user input in real-world
automation scenarios. Supporting this enables PoshMcp to work with Azure
authentication, multi-factor prompts, and any command that requires a
single round of user confirmation.

**Independent Test**: Can be tested by invoking a command that triggers a
simulated `Read-Host` prompt, verifying the server returns a
`prompt_required` response with prompt type and message, then re-invoking
with a `_promptResponse` payload and verifying the command completes
successfully with the injected value.

**Acceptance Scenarios**:

1. **Given** a command that calls `Read-Host "Enter your name"`, **When** invoked without pre-supplying a response, **Then** the server returns a structured response with `"type": "prompt_required"`, a `promptId`, the prompt message, and an encoded `sessionSnapshot`
2. **Given** a prior `prompt_required` response was received, **When** the client re-invokes the command with `_promptResponse` containing the `sessionSnapshot` and the user's input, **Then** the server injects the input, completes the command, and returns the final result
3. **Given** a command that triggers a `PromptForChoice` (e.g., a confirmation dialog), **When** invoked and the client responds with the selected choice index, **Then** the command resumes with the chosen option and returns its result
4. **Given** a command triggers a credential prompt (`Get-Credential`), **When** the client responds with username and password in the retry payload, **Then** the command completes with those credentials injected

---

### User Story 3 — Prompt Metadata in Tool Schema (Priority: P2)

A tool-builder configuring PoshMcp wants to know in advance whether a
command might prompt for input. They check the tool's schema and see a
`promptBehavior` field that indicates whether the command is
`"safe-noninteractive"` or `"may-prompt"`. Their AI agent can use this
metadata to decide whether to attempt invocation or warn the user
preemptively.

**Why this priority**: Schema-level guidance prevents errors before they
occur and enables smarter client behavior, but requires the prompt
detection and schema-generation infrastructure to exist first.

**Independent Test**: Can be tested by calling `tools/list` and checking
that tools known to prompt carry `"promptBehavior": "may-prompt"` in
their schema description or annotations, while commands known to be
non-interactive carry `"promptBehavior": "safe-noninteractive"`.

**Acceptance Scenarios**:

1. **Given** a command like `Read-Host` or `Get-Credential` is configured as a tool, **When** `tools/list` is called, **Then** the tool's metadata includes `"promptBehavior": "may-prompt"`
2. **Given** a standard non-interactive command like `Get-Process` is configured, **When** `tools/list` is called, **Then** its metadata includes `"promptBehavior": "safe-noninteractive"`
3. **Given** an MCP client queries the tool list, **When** filtering for `may-prompt` tools, **Then** the client can build a warning UI or pre-flight checklist without attempting invocation first

---

### Edge Cases

- What happens when a command prompts multiple times in sequence? Each prompt in the retry chain must be resolved before the next is reached.
- What happens if the client never responds to a `prompt_required` response? The server must time out and release any held state.
- What happens when the same command is invoked concurrently by two clients, both waiting on prompts? Each invocation must be independently isolated.
- What happens when a command triggers a `Get-Credential` prompt in out-of-process mode? The subprocess runs non-interactively; the error should be surfaced cleanly without crashing the subprocess.
- What happens when a `sessionSnapshot` is tampered with or expired? The server must detect and reject invalid snapshots with a clear error.
- What if a command produces output before prompting — does the partial output get discarded or returned?
- What happens when confirmation suppression (`-Confirm:$false`) causes unintended side effects (e.g., deletes a file without warning)?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-035**: System MUST return an MCP error response within the configured timeout when a command triggers an interactive prompt and no input is available, rather than blocking indefinitely
- **FR-036**: System MUST automatically suppress `-Confirm` prompts by default; the suppression behavior MUST be configurable
- **FR-037**: System MUST validate that all mandatory parameters are supplied before executing a command and return a pre-flight error if any are missing
- **FR-038**: System MUST return a structured `prompt_required` response when a command triggers an interceptable prompt, including the prompt type, display message, and an encoded stateless session snapshot
- **FR-039**: System MUST accept a `_promptResponse` framework parameter on any tool call, decode the session snapshot, inject the user's response into the command's prompt handler, and complete the command
- **FR-040**: System MUST support the following prompt types in the structured response flow: `read-line` (text input), `secure-string` (password/credential input), `choice` (confirmation or menu selection), and `credential` (username + password pair)
- **FR-041**: System MUST annotate generated tool schemas with `promptBehavior` metadata indicating whether each command is `safe-noninteractive` or `may-prompt`
- **FR-042**: System MUST impose a configurable timeout on prompt responses; if no response arrives within the timeout, the invocation MUST fail with a timeout error and all associated state MUST be released
- **FR-043**: System MUST support the prompt response flow in in-process execution mode; out-of-process mode MUST fail fast with a clear error message when a prompt is encountered

### Key Entities

- **Prompt**: An interactive input request generated by a PowerShell command during execution — may be a text read, a credential request, a confirmation, or a choice selection
- **Prompt Metadata**: Structured description of a prompt: its type, display message, available choices (if any), and a unique `promptId`
- **Session Snapshot**: An immutable, client-managed token encoding the command name, parameters, and prompt sequence number needed to reproduce and inject a response on retry — contains no sensitive user data
- **Prompt Response**: The user-supplied value provided by the MCP client in a retry call, paired with the `sessionSnapshot` from the prior `prompt_required` response
- **Prompt Behavior**: Schema-level annotation (`safe-noninteractive` or `may-prompt`) indicating whether a tool is expected to require interactive input

### Configuration Schema (if applicable)

```jsonc
{
  "PowerShellConfiguration": {
    // Suppress -Confirm prompts automatically (default: true)
    "SuppressConfirmation": true,

    // Timeout in seconds for prompt responses (default: 30)
    "PromptTimeoutSeconds": 30,

    // Behavior when a prompt is encountered and no response is possible
    // "fail-fast": return error immediately
    // "structured-response": return prompt_required and await retry
    "PromptBehavior": "fail-fast"
  }
}
```

### SDK Integration Notes (if applicable)

The prompt interception mechanism operates through the PowerShell Host abstraction. The server must be able to register a custom host implementation that intercepts prompt calls and either fails fast or suspends execution pending a client response. The specific mechanism used must not break the existing singleton runspace model.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-016**: A command that calls `Read-Host` returns an MCP error response within `PromptTimeoutSeconds` — the server does not hang, block other requests, or crash
- **SC-017**: An AI agent that invokes a command triggering a structured prompt can complete the workflow in exactly two MCP calls: one that receives `prompt_required` and one retry that receives the final result
- **SC-018**: All mandatory-parameter validation errors are returned as pre-flight errors before command execution begins, with the missing parameter name(s) identified in the error message
- **SC-019**: Tool schemas for prompt-capable commands include `promptBehavior: "may-prompt"` metadata, enabling clients to build preemptive UI without invoking the command first
- **SC-020**: Prompt response timeout fires reliably within `PromptTimeoutSeconds + 2 seconds` of the prompt being surfaced, with all associated state cleaned up

## Assumptions

- The MCP protocol remains request/response; there is no persistent bidirectional channel between client and server within a single call
- Most PoshMcp deployments are non-interactive by nature (containers, CI/CD pipelines, headless agents) — the dominant use case is fail-fast, not structured prompt exchange
- The stateless retry pattern (Option D from the design) is the appropriate architecture for structured prompt support: the server holds no session state between the prompt response and the retry call
- Session snapshots do not contain secrets; credentials are provided fresh in each retry payload
- In out-of-process mode, interactive prompt support is out of scope for the initial implementation; fail-fast is sufficient
- The `-Confirm` suppression default (`SuppressConfirmation: true`) is safe for automation contexts and is documented prominently as a behavior change from interactive PowerShell sessions
