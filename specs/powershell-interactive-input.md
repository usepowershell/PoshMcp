# PowerShell Interactive Input Handling in PoshMcp

**Author:** Farnsworth, Lead Architect  
**Date:** 2026-04-14  
**Status:** Design Proposal (RFC)

---

## 1. Executive Summary

PoshMcp currently executes PowerShell commands in two modes: **in-process** (via PowerShell SDK in a shared runspace) and **out-of-process** (via a persistent `pwsh` subprocess). Both execution paths have **no mechanism to handle prompts**—commands that call `Read-Host`, `Get-Credential`, confirmation prompts, or mandatory parameters with missing values will block, hang, or fail silently.

This spec proposes three viable approaches to solving this problem and recommends a path forward for implementation.

---

## 2. Research Summary

### 2.1 PowerShell SDK Input/Output Handling

The PowerShell SDK exposes **`PSHostRawUserInterface`** and **`PSHost`** abstractions that applications can override to customize user interaction. Key components:

| Component | Purpose | Relevance |
|-----------|---------|-----------|
| `PSHost.UI.RawUI.ReadLine()` | Reads stdin (with echo) | Captures `Read-Host` input |
| `PSHost.UI.ReadLineAsSecureString()` | Reads stdin without echo | Captures `Get-Credential -AsSecureString` input |
| `PSHost.UI.PromptForChoice()` | Displays choice prompt, reads response | Handles `-Confirm:$true` or menu prompts |
| `PSHost.UI.PromptForCredential()` | Prompts for username/password | Handles `Get-Credential` without args |
| `PSHost.UI.Prompt()` | Custom prompt text + input | Generic prompt mechanism |

**Current State:** PoshMcp uses the default `ConsoleHost` which attempts to read from the actual system console. In a containerized environment or when stdin is redirected (as in OOP subprocess), these reads **fail or hang**.

### 2.2 In-Process Execution Context

- **Runtime:** .NET 8 process with embedded PowerShell SDK
- **Runspace:** Singleton shared runspace (thread-safe via `PowerShellRunspaceHolder`)
- **Command Invocation:** Generated IL code calls `ps.Invoke()` from `PowerShellAssemblyGenerator`
- **User Interface:** Uses default `ConsoleHost` (can be overridden)
- **Threading:** Calls guarded by `lock` in `ExecuteThreadSafe<T>()`

**Implication:** We can inject a custom `PSHost` implementation to intercept prompts per invocation.

### 2.3 Out-of-Process Execution Context

- **Runtime:** Separate `pwsh` subprocess (`-NoProfile -NonInteractive`)
- **Communication:** stdin/stdout ndjson JSON-RPC-like protocol
- **Command Invocation:** `Invoke-InvokeHandler` in `oop-host.ps1` executes `& $commandName @splatParams`
- **Stdin Availability:** `$stdin` in subprocess is connected to parent's stdout/management channel (used for ndjson framing), not user input
- **Environment:** `-NonInteractive` flag prevents interactive prompts (fails fast instead of hanging)

**Implication:** Subprocess stdin is owned by the protocol layer; we cannot trivially inject user input. Prompts cannot be answered via original stdin.

### 2.4 Container & Docker Implications

- **Deployment:** PoshMcp can run as HTTP server or stdio MCP server in containers
- **Stdin Allocation:** Containers default to `tty=false` unless explicitly configured
- **User Interaction:** User input would require explicit stdin forwarding (unusual in container environments)
- **Practical Reality:** Most deployments are **non-interactive by design**—PoshMcp is invoked programmatically via MCP

---

## 3. Problem Statement

### 3.1 Prompt Scenarios

Commands that require interactive input fall into these categories:

1. **`Read-Host`**: Generic text input
   ```powershell
   $name = Read-Host "Enter your name"
   ```

2. **`Get-Credential`**: Username/password prompt
   ```powershell
   $cred = Get-Credential  # Prompts for username and password
   ```

3. **Confirmation Prompts** (`-Confirm:$true`):
   ```powershell
   Remove-Item -Path "C:\temp" -Confirm:$true  # Prompts for confirmation
   ```

4. **`PromptForChoice`**: Menu-style selection
   ```powershell
   $choices = [System.Management.Automation.Host.ChoiceDescription[]]@("Yes", "No")
   $host.UI.PromptForChoice("Title", "Message", $choices, 0)
   ```

5. **Mandatory Parameters with Missing Values**:
   ```powershell
   function Do-Thing { param([Parameter(Mandatory=$true)] [string] $Path) }
   Do-Thing  # Prompts for -Path
   ```

### 3.2 Current Gap

- **In-Process**: Attempts to read from console; blocks if console unavailable
- **Out-of-Process**: Subprocess runs with `-NonInteractive`, causing prompts to fail immediately with error
- **MCP Protocol**: Fundamentally request/response (stateless per call); no mechanism for mid-execution bidirectional communication
- **User Experience**: MCP clients have no way to inject input; commands fail silently or with cryptic "operation timed out" errors

### 3.3 Constraints

| Constraint | Implication |
|-----------|-----------|
| **MCP is stateless request/response** | Cannot maintain state between a prompt and a response; each MCP call is independent |
| **Non-interactive deployment model** | Most PoshMcp deployments are not console-attached; stdin is unavailable |
| **Backwards compatibility** | Changing command execution behavior must not break existing integrations |
| **Timeout handling** | If a command prompts and input is not provided, should fail quickly (not hang forever) |
| **Concurrent execution** | Multiple users may invoke commands simultaneously; prompts must not interfere |

---

## 4. Design Options

### Option A: Pre-Execution Validation & Injection

**Concept:** Inspect the PowerShell command before execution. Detect mandatory parameters and prompts; inject values upfront to prevent prompts from occurring.

#### 4.A.1 Implementation Outline

```
1. Before ps.Invoke():
   - Get command metadata (Get-Command)
   - Identify mandatory parameters
   - Check for known prompt-triggering patterns

2. For each mandatory parameter:
   - If provided by client: use it
   - If not provided: inject a default or fail pre-emptively

3. For confirmation prompts:
   - Add -Confirm:$false to suppress

4. For Read-Host / Get-Credential:
   - Detect in command string; warn or error out
```

#### 4.A.2 Pros
- ✅ **Minimal changes** to execution layer
- ✅ **Fails fast** (no hanging)
- ✅ **Predictable** (same result each time for same inputs)
- ✅ **Works in both in-process and out-of-process** (pre-execution validation applies to both)

#### 4.A.3 Cons
- ❌ **Incomplete solution**: Cannot detect all prompt scenarios (user code calling `Read-Host` within a script)
- ❌ **Brittle**: Pattern matching on command strings is error-prone
- ❌ **Loses interactivity**: Forces non-interactive fallback even if interactivity was intended
- ❌ **Poor UX**: Clients must pre-emptively supply all mandatory params or get errors

#### 4.A.4 Risk: False Negatives
A command might pass validation but still prompt (e.g., a function that calls `Read-Host` internally). This violates the principle of "fail fast and predictably."

---

### Option B: Prompt-as-Error

**Concept:** Treat any prompt as an error condition. Update command schema to advertise "may prompt" risk; require clients to supply all inputs upfront or accept failure.

#### 4.B.1 Implementation Outline

```
1. In-process:
   - Custom PSHost that catches prompt calls
   - Immediately throw PromptEncounteredException
   - Return error to client: "Command attempted to prompt; this is not allowed"

2. Out-of-process:
   - Subprocess already runs with -NonInteractive
   - Prompts fail automatically (no change needed)

3. Schema generation:
   - Add "promptBehavior": "may-prompt" | "safe-noninteractive" metadata
   - Document in tool description

4. MCP client handling:
   - For "may-prompt" tools, either:
     a) Supply all mandatory inputs upfront, or
     b) Refuse to invoke and report error to user
```

#### 4.B.2 Pros
- ✅ **Clear semantics**: Prompts are not allowed; fail explicitly
- ✅ **Safe defaults**: No hanging, no cryptic timeouts
- ✅ **Schema-driven**: Clients can query "may-prompt" metadata and decide
- ✅ **Backward compatible**: Existing safe-to-run commands unchanged
- ✅ **Works in both modes**: Applies to in-process and out-of-process consistently

#### 4.B.3 Cons
- ❌ **Loses interactivity**: Cannot support commands that legitimately need user input
- ❌ **User friction**: Clients must re-invoke with all parameters if first attempt prompts
- ❌ **Doesn't solve the problem**: Just rejects it more gracefully
- ❌ **Excludes use cases**: Scripts like `Get-Credential` become unusable

#### 4.B.4 Appropriate For
Commands that are inherently non-interactive (most Azure CLI, automation cmdlets). Not suitable for administrative tools that require user confirmation or sensitive input.

---

### Option C: Structured Prompt Response (Two-Phase Invocation)

**Concept:** Return prompt metadata to the client as a structured response. Client responds with input via a second MCP call, completing the original invocation. Requires session state management.

#### 4.C.1 Architecture

```
MCP Client                              MCP Server (PoshMcp)
    │
    ├─ invoke(command, params)  ────┐
    │                                 │
    │                         ┌──────▼──────────┐
    │                         │ Execute command │
    │                         │ Call Read-Host  │
    │                         │ Prompt detected │
    │                         └──────┬──────────┘
    │                                 │
    │◄──── prompt_required { ... }────┤
    │      session_id: "abc-123"
    │      prompt_id: "read-name"
    │      type: "read_line"
    │      message: "Enter name:"
    │
    ├─ respond_to_prompt           ──┐
    │   { session_id, prompt_id,     │
    │     input_value }              │
    │                                 │
    │                         ┌──────▼──────────┐
    │                         │ Resume execution│
    │                         │ Inject input    │
    │                         │ Complete invoke │
    │                         └──────┬──────────┘
    │                                 │
    │◄──── result { ... }─────────────┤
```

#### 4.C.2 Implementation Outline

**In-Process:**

```
1. Create a custom PSHost that intercepts prompts
2. On prompt:
   - Generate unique (session_id, prompt_id)
   - Serialize prompt metadata (type, message, choices, etc.)
   - Pause the PowerShell pipeline (store continuation state)
   - Return "prompt_required" to MCP client

3. When client calls respond_to_prompt:
   - Look up stored continuation state
   - Inject the input value (simulate keystroke/choice)
   - Resume PowerShell pipeline
   - Collect output and return "result"
```

**Out-of-Process:**

```
1. Modify oop-host.ps1 to handle prompts:
   - Custom PSHost implementation in pwsh subprocess
   - On prompt: send JSON message to parent over stdout
   - Parent (C#) captures it, returns to MCP client
   - On respond_to_prompt: send input back to subprocess stdin
   - Subprocess resumes and completes

2. Protocol enhancement:
   - New request types: "prompt_required", "respond_to_prompt"
   - ndjson framing for prompt/response messages
```

#### 4.C.3 Pros
- ✅ **Enables interactivity**: Supports commands that require user input (Get-Credential, administrative confirmations)
- ✅ **Stateful UX**: Client can present prompt to end-user and collect response naturally
- ✅ **Most powerful**: Handles all prompt scenarios
- ✅ **Fits AI agent workflows**: Agents can be prompted to clarify inputs

#### 4.C.4 Cons
- ❌ **Complex implementation**: Requires session management, continuation context, potential deadlocks
- ❌ **Threading/Runspace challenges**: In-process, must safely pause/resume PowerShell pipeline in thread-safe manner
- ❌ **Timeout handling**: How long do we wait for client response? What if client never responds?
- ❌ **OOP subprocess complexity**: stdin/stdout framing becomes protocol-within-protocol
- ❌ **Testing overhead**: Significant new test surface (async prompt/response flows)
- ❌ **Backward compatibility risk**: New MCP contract requires updated clients; legacy clients unaware of prompt_required messages
- ❌ **Memory/session leaks**: Long-running prompts consume server resources; session cleanup must be robust

#### 4.C.5 Risk: Runspace State Corruption
In-process execution relies on a single shared runspace. Pausing mid-execution and resuming from a different context could corrupt runspace state or cause deadlocks if another thread acquires the lock.

#### 4.C.6 Risk: Subprocess Deadlock (OOP)
If subprocess is waiting for client response and client sends a concurrent request, protocol framing could deadlock or reorder messages.

---

## 5. In-Process Specifics

### 5.1 Current Execution Flow

```csharp
// In PowerShellAssemblyGenerator.ExecutePowerShellCommandTyped()

public static async Task<string> ExecutePowerShellCommandTyped(
    string commandName,
    PowerShellParameterInfo[] parameterInfos,
    object[] parameterValues,
    CancellationToken cancellationToken,
    IPowerShellRunspace runspace,
    ILogger logger)
{
    return await runspace.ExecuteThreadSafeAsync<string>(ps =>
    {
        ps.Commands.Clear();
        ps.AddCommand(commandName);
        
        // Add parameters
        for (int i = 0; i < parameterInfos.Length; i++)
        {
            if (parameterValues[i] != null)
            {
                ps.AddParameter(parameterInfos[i].Name, parameterValues[i]);
            }
        }

        // THIS IS WHERE PROMPTS OCCUR:
        var results = ps.Invoke();  // <-- Blocks if command prompts
        
        // Serialize and return
        var jsonOutput = results | ConvertTo-Json -Depth 4;
        return jsonOutput;
    });
}
```

### 5.2 Approach: Custom PSHost (Option C)

To intercept prompts in-process, we would:

```csharp
// New class: PoshMcpPSHost
public class PoshMcpPSHost : PSHost
{
    private readonly ILogger _logger;
    private readonly Action<PromptMetadata> _onPrompt;
    private PromptResponse? _pendingResponse;
    
    public override PSHostRawUserInterface RawUI => new PoshMcpRawUserInterface(this);
    
    public void WaitForPromptResponse(string promptId, TimeSpan timeout)
    {
        // Block until client responds via respond_to_prompt
        // or timeout expires
    }
}

public class PoshMcpRawUserInterface : PSHostRawUserInterface
{
    public override string ReadLine()
    {
        // Called by Read-Host
        _host.OnPrompt(new ReadLinePrompt { ... });
        return _host.WaitForPromptResponse("read-line-1", TimeSpan.FromSeconds(30));
    }
    
    public override SecureString ReadLineAsSecureString()
    {
        // Called by Get-Credential secure input
        _host.OnPrompt(new SecureReadPrompt { ... });
        var response = _host.WaitForPromptResponse("secure-read-1", TimeSpan.FromSeconds(30));
        // Convert response string to SecureString
        return ConvertToSecureString(response);
    }
    
    public override int PromptForChoice(string caption, string message, Collection<ChoiceDescription> choices, int defaultChoice)
    {
        // Called by -Confirm:$true and menu prompts
        _host.OnPrompt(new ChoicePrompt { ... });
        var response = _host.WaitForPromptResponse("choice-1", TimeSpan.FromSeconds(30));
        return int.Parse(response);
    }
}
```

### 5.3 Thread Safety Concerns

The challenge is that:

1. `ps.Invoke()` runs on the same thread that calls it (or a thread pool thread)
2. `ExecuteThreadSafeAsync` holds a lock during execution
3. If execution pauses (waiting for prompt response), the lock is held, blocking other invocations
4. Deadlock risk: If another thread tries to invoke a command while the first is awaiting a response

**Solution:** Use a separate "prompt response channel" outside the lock:

```csharp
private class PromptWaitHandle
{
    private readonly TaskCompletionSource<string> _tcs = new();
    
    public async Task<string> WaitAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        return await _tcs.Task.ConfigureAwait(false);
    }
    
    public void SetResponse(string value) => _tcs.SetResult(value);
    public void SetTimeout() => _tcs.SetException(new TimeoutException());
}

// In ExecutePowerShellCommandTyped:
var promptWaitHandle = new PromptWaitHandle();
var psHost = new PoshMcpPSHost(_logger, promptWaitHandle);

// Set custom host (if runspace allows; may require runspace recreation)
runspace.Host = psHost;

var results = ps.Invoke();
```

### 5.4 Runspace Host Swapping

The PowerShell SDK runspace's `Host` property is read-only after runspace creation. **Workaround:** Create a new runspace per session (not the singleton model), or proxy through the existing host (less robust).

---

## 6. Out-of-Process Specifics

### 6.1 Current OOP Execution Flow

```
(OutOfProcessCommandExecutor in C#)
    └─ SendRequestAsync("invoke", { command, parameters })
       └─ (via stdin to subprocess)
           └─ (in oop-host.ps1)
               └─ Invoke-InvokeHandler
                   └─ & $commandName @splatParams
                       └─ If prompts: fails with -NonInteractive
                   └─ Write-NdjsonResponse

(Subprocess runs with -NonInteractive, so prompts fail fast)
```

### 6.2 Approach: Prompt Exchange Protocol (Option C)

Modify `oop-host.ps1` to detect and report prompts:

```powershell
# In oop-host.ps1, create custom PSHost
class PromptInterceptor : [System.Management.Automation.Host.PSHost] {
    [void] WritePrompt([string] $message) {
        # Send to parent: prompt_required message
        $promptMessage = @{
            type   = "prompt_required"
            promptId = [guid]::NewGuid().ToString()
            message = $message
            promptType = "read-line"
        }
        Write-Host (ConvertTo-Json $promptMessage)
    }
    
    [string] ReadLine() {
        # Notify parent of prompt
        $this.WritePrompt("Read-Host")
        
        # Read parent's response from ... where?
        # Problem: stdin is used for ndjson protocol framing
    }
}
```

### 6.3 The Stdin Ownership Problem

**Core issue:** The subprocess's `stdin` is already redirected to the parent process for ndjson protocol communication. We cannot multiplex user input over the same stream without breaking the protocol.

**Options:**

1. **Separate communication channel** (named pipe, socket):
   - Complex: adds IPC mechanism
   - Cross-platform: need Windows named pipes + Unix sockets
   - Deployment friction: containers may not support

2. **Protocol enhancement** (use ndjson for prompts):
   - Parent receives `{"type": "prompt_required", "promptId": "...", ...}` message from subprocess
   - Parent exposes it to MCP client via "prompt_required" response
   - Client calls `respond_to_prompt` MCP method
   - Parent sends `{"type": "prompt_response", "promptId": "...", "value": "..."}` to subprocess stdin
   - Subprocess reads and injects input
   - Risk: Protocol framing complexity; difficult to debug

3. **Timeout + fail** (current behavior):
   - If subprocess encounters prompt, fail after timeout
   - Accept this limitation for OOP mode

### 6.4 Recommended: Timeout + Fail for OOP

Given the complexity and risk of protocol changes, **recommend defaulting to Option B (Prompt-as-Error) for out-of-process** execution:

- Subprocess runs with `-NonInteractive`
- If prompt occurs, PowerShell fails with error message
- Server returns error to client: "Command is not supported in non-interactive mode"
- Clients can query schema for "promptBehavior" metadata

If interactive support is needed in OOP, that can be a future enhancement (separate ticket).

---

### Option D: Stateless Prompt-Retry Pattern

**Concept:** On prompt encounter, return structured prompt metadata to the client along with a minimal, immutable state snapshot. Client submits a second request that includes the snapshot plus the user's response. Server re-runs the command with the response pre-injected. All state is client-managed; server holds no session.

#### 4.D.1 Key Insight

**The Difference from Option C:**
- **Option C**: Server maintains session state (`session_id`, continuation context) between prompt and response
- **Option D**: Client manages state (encoded snapshot); each request is independent from server perspective

**Practical Impact:**
- No server-side session management
- No cleanup delays or memory leaks
- Stateless from MCP protocol perspective
- Each "prompt → retry" flow is self-contained (can be safely discarded if client times out)

#### 4.D.2 Architecture Overview

```
Request 1: Client invokes command (no prior context)
    ↓
Server executes command
    ├─ Command triggers prompt (Read-Host, Get-Credential, etc.)
    ├─ Custom PSHost intercepts prompt
    ├─ Capture prompt metadata + minimal execution snapshot
    └─ Encode snapshot as immutable base64 string
    ↓
Response 1: Return "prompt_required" with metadata + snapshot
{
  "type": "prompt_required",
  "promptId": "dev-code-1",
  "sessionSnapshot": "eyJwcm9tcHRUeXBlIjoicmVhZExpbmUiLCJw..."  # immutable, client-managed
  "prompt": {
    "type": "device_code_flow",
    "deviceCode": "ABC123",
    "userCode": "XYZ789",
    "verificationUrl": "https://...",
    "message": "Go to https://... and enter code XYZ789"
  }
}

Client:
    ├─ Display prompt to user
    ├─ Collect response (user enters code, clicks OK, etc.)
    ├─ Store snapshot for retry
    └─ Hold snapshot in memory (or persistence layer)

Request 2: Client re-invokes with response + snapshot
{
  "name": "Connect-AzAccount",
  "arguments": {
    "_promptResponse": {
      "promptId": "dev-code-1",
      "sessionSnapshot": "eyJwcm9tcHRUeXBlIjoicmVhZExpbmUiLCJw...",  # unchanged from Response 1
      "userResponse": {
        "confirmed": true
      }
    }
  }
}

Server:
    ├─ Decode snapshot
    ├─ Re-run original command
    ├─ Detect same prompt scenario
    ├─ Pre-inject user response (inject into PSHost.ReadLine, etc.)
    └─ Command completes successfully
    ↓
Response 2: Return command result
{
  "content": [{
    "type": "text",
    "text": "Account: user@example.com\nTenantId: 00000000-0000-0000-0000-000000000000\n..."
  }]
}
```

#### 4.D.3 In-Process Implementation

**Snapshot Encoding:**

```csharp
// What needs to be captured in the snapshot?
public class PromptSnapshot
{
    // Unique ID for this prompt within the invocation
    public string PromptId { get; set; }
    
    // Enough info to re-run the exact same command path
    public string CommandName { get; set; }
    public Dictionary<string, object> Parameters { get; set; }
    
    // Enough info to detect and skip to the same prompt
    public string PromptType { get; set; }  // "read-line", "credential", "choice", etc.
    public int PromptSequenceNumber { get; set; }  // If multiple prompts: 1st, 2nd, 3rd?
    
    // Timestamp for replay validation
    public long SnapshotEpochTicks { get; set; }
}

// Encode to client
var snapshot = new PromptSnapshot { ... };
var json = JsonConvert.SerializeObject(snapshot);
var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
// e.g., "eyJwcm9tcHRJZCI6ImRldi1jb2RlLTEiLC..."

// Decode from client
var base64 = request.Arguments["_promptResponse"]["sessionSnapshot"];
var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
var snapshot = JsonConvert.DeserializeObject<PromptSnapshot>(json);
```

**Execution Flow with Retry:**

```csharp
// In ExecutePowerShellCommandTyped:
PromptSnapshot? pendingSnapshot = null;
string? pendingUserResponse = null;

// Check if this is a retry request
if (arguments.Contains("_promptResponse"))
{
    var promptResponse = arguments["_promptResponse"];
    pendingSnapshot = DecodeSnapshot(promptResponse["sessionSnapshot"]);
    pendingUserResponse = promptResponse["userResponse"];
}

// Create custom PSHost that knows about the pending response
var psHost = new PoshMcpPSHost(_logger, promptInterceptor: (prompt) =>
{
    // Check if this is the prompt we're retrying
    if (pendingSnapshot != null && MatchesPrompt(prompt, pendingSnapshot))
    {
        // Inject the response into the prompt handler
        return pendingUserResponse;
    }
    
    // First-time prompt: capture and return
    var snapshot = CaptureSnapshot(commandName, parameters, prompt);
    throw new PromptEncounteredException(
        promptId: Guid.NewGuid().ToString(),
        promptMetadata: prompt,
        snapshot: EncodeSnapshot(snapshot)
    );
});

// Execute with custom host
try
{
    var results = ps.Invoke();
    // ... serialize and return success
}
catch (PromptEncounteredException ex)
{
    // First request: return prompt_required
    return new MalformedRequestResponse
    {
        Type = "prompt_required",
        PromptId = ex.PromptId,
        SessionSnapshot = ex.Snapshot,
        Prompt = ex.PromptMetadata
    };
}
```

**Prompt Matching Logic:**

```csharp
private static bool MatchesPrompt(PromptMetadata encountered, PromptSnapshot snapshot)
{
    // Compare prompt type
    if (encountered.Type != snapshot.PromptType)
        return false;
    
    // For credential prompts: check if it's the same prompt (by message?)
    if (encountered.Type == "credential" && 
        encountered.Message != snapshot.CredentialPromptMessage)
        return false;
    
    // For device code: the device code itself is in the snapshot
    if (encountered.Type == "device_code_flow" && 
        encountered.DeviceCode != snapshot.DeviceCode)
        return false;
    
    // For choice/confirmation: check caption/message
    if (encountered.Type == "choice" && 
        (encountered.Caption != snapshot.ChoiceCaption || 
         encountered.Message != snapshot.ChoiceMessage))
        return false;
    
    return true;
}
```

#### 4.D.4 Out-of-Process Implementation

**Challenge:** OOP subprocess has separate stdin/stdout. We need a two-phase protocol in `oop-host.ps1`:

```powershell
# In oop-host.ps1, enhanced prompt handling:

function Invoke-InvokeHandler {
    param(
        [string] $commandName,
        [hashtable] $splatParams,
        [string] $requestId,
        [string] $sessionSnapshot,  # From second request (if retry)
        [object] $promptResponse     # User response (if retry)
    )
    
    # Create custom PSHost for this invocation
    $psHost = New-PoshMcpPSHost -RequestId $requestId
    
    # If this is a retry, inject the response handler
    if ($sessionSnapshot) {
        $snapshot = [System.Text.Encoding]::UTF8.GetString(
            [System.Convert]::FromBase64String($sessionSnapshot)
        ) | ConvertFrom-Json
        
        $psHost.SetPendingResponse($snapshot, $promptResponse)
    }
    
    # Execute command with custom host
    try {
        $results = & $commandName @splatParams 2>&1
        Write-NdjsonResponse @{
            type = "success"
            output = $results
        }
    }
    catch {
        # Check if this was a PromptEncounteredException
        if ($_.Exception -is [PoshMcp.PromptEncounteredException]) {
            Write-NdjsonResponse @{
                type = "prompt_required"
                promptId = $_.Exception.PromptId
                sessionSnapshot = $_.Exception.Snapshot
                prompt = $_.Exception.PromptMetadata
            }
        }
        else {
            Write-NdjsonResponse @{
                type = "error"
                message = $_.Exception.Message
            }
        }
    }
}
```

**Protocol Flow:**

```
Parent (C#)                              Subprocess (pwsh)
    │
    ├─ Send Request 1                ──→│ Invoke-InvokeHandler
    │  { requestId, command, params }   │ Detects prompt
    │                                    │ Sends ndjson: prompt_required + snapshot
    │◄─ Receive prompt_required ────────┤
    │
    ├─ Return prompt to MCP client
    │
    ├─ Client submits response  ────────┐
    │  { promptResponse, sessionSnapshot}│
    │                                    │
    ├─ Send Request 2                ──→│ Invoke-InvokeHandler (retry)
    │  { requestId, command, params,    │ { sessionSnapshot, promptResponse }
    │    sessionSnapshot, promptResponse }│ Injects response
    │                                    │ Command completes
    │                                    │ Sends ndjson: success
    │◄─ Receive success ────────────────┤
```

#### 4.D.5 State Snapshot Security

**Concerns:**
- Snapshot should not be tamper-able by client (injection attack)
- Snapshot should not leak sensitive data (credentials, tokens)
- Snapshot must be immutable and tied to specific command/parameters

**Protections:**

```csharp
public class SecurePromptSnapshot
{
    // Immutable hash of command + parameters (prevents swap attack)
    public string CommandHash { get; set; }
    
    // HMAC of snapshot using server-private key (prevents tampering)
    public string IntegrityHmac { get; set; }
    
    // Expiration: snapshot only valid for 5 minutes (against replay)
    public long ExpirationEpochSeconds { get; set; }
    
    // DO NOT include: passwords, tokens, PII
    // Only: prompt type, device code (public), sequence number
    public string PromptType { get; set; }
    public int PromptSequence { get; set; }
    public Dictionary<string, string> PublicPromptMetadata { get; set; }
}

// Encoding with integrity
public static string EncodeSecureSnapshot(SecurePromptSnapshot snapshot)
{
    var json = JsonConvert.SerializeObject(snapshot);
    var hmac = ComputeHmac(json, _serverPrivateKey);
    snapshot.IntegrityHmac = hmac;
    
    var finalJson = JsonConvert.SerializeObject(snapshot);
    return Convert.ToBase64String(Encoding.UTF8.GetBytes(finalJson));
}

// Decoding with validation
public static SecurePromptSnapshot? DecodeAndValidateSnapshot(string base64)
{
    try
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        var snapshot = JsonConvert.DeserializeObject<SecurePromptSnapshot>(json);
        
        // Validate HMAC
        var expectedHmac = ComputeHmac(json, _serverPrivateKey);
        if (snapshot.IntegrityHmac != expectedHmac)
            throw new InvalidOperationException("Snapshot integrity check failed");
        
        // Validate expiration
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > snapshot.ExpirationEpochSeconds)
            throw new InvalidOperationException("Snapshot has expired");
        
        return snapshot;
    }
    catch
    {
        _logger.LogWarning("Failed to decode or validate snapshot");
        return null;
    }
}
```

#### 4.D.6 Concurrent Request Handling

**Scenario:** Two clients invoke the same command simultaneously; both hit prompts.

**Solution:** Each prompt has a unique `promptId` (GUID). Retry request must specify the exact `promptId` that matches the original prompt response.

```csharp
// Request 1a (Client A): Connect-AzAccount
Response 1a:
{
  "type": "prompt_required",
  "promptId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "sessionSnapshot": "eyJ..."
}

// Request 1b (Client B): Connect-AzAccount (concurrent)
Response 1b:
{
  "type": "prompt_required",
  "promptId": "7b4e2d1c-8a93-4e7f-a2c1-1f5c3e8b9d2a",  # Different promptId!
  "sessionSnapshot": "eyJ..."
}

// Request 2a (Client A): Retry with Response for promptId "3fa85f64..."
{
  "name": "Connect-AzAccount",
  "arguments": {
    "_promptResponse": {
      "promptId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",  # Must match original
      "sessionSnapshot": "eyJ...",
      "userResponse": { "confirmed": true }
    }
  }
}
// ✅ Server matches promptId and processes retry

// Bad Request 2b (Client B): Accidentally uses wrong promptId
{
  "name": "Connect-AzAccount",
  "arguments": {
    "_promptResponse": {
      "promptId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",  # WRONG!
      ...
    }
  }
}
// ❌ Server rejects: "promptId 3fa85f64... not found in current session"
```

#### 4.D.7 Real-World Example: Device Code Flow

```
=== REQUEST 1 (Initial Login) ===
POST /mcp/tools/call
{
  "jsonrpc": "2.0",
  "id": "req-1",
  "method": "tools/call",
  "params": {
    "name": "Connect-AzAccount",
    "arguments": {}
  }
}

=== RESPONSE 1 (Prompt Detected) ===
{
  "jsonrpc": "2.0",
  "id": "req-1",
  "result": {
    "type": "prompt_required",
    "promptId": "dev-code-1",
    "sessionSnapshot": "eyJwcm9tcHRUeXBlIjoiZGV2aWNlQ29kZUZsb3ciLCJjb21tYW5kIjoiQ29ubmVjdC1BekFjY291bnQiLCJwYXJhbWV0ZXJzIjp7fSwiZXhwaXJhdGlvbiI6MTY4NDMyMjAwMH0=",
    "prompt": {
      "type": "device_code_flow",
      "deviceCode": "GF8JWXB2Z",
      "userCode": "QNQ4BMJPT",
      "verificationUrl": "https://microsoft.com/devicelogin",
      "message": "To sign in, use a web browser to open https://microsoft.com/devicelogin and enter code QNQ4BMJPT to authenticate."
    }
  }
}

[User opens browser, navigates to https://microsoft.com/devicelogin, enters code QNQ4BMJPT]
[User successfully authenticates via interactive UI]

=== REQUEST 2 (Resume with Confirmation) ===
POST /mcp/tools/call
{
  "jsonrpc": "2.0",
  "id": "req-2",
  "method": "tools/call",
  "params": {
    "name": "Connect-AzAccount",
    "arguments": {
      "_promptResponse": {
        "promptId": "dev-code-1",
        "sessionSnapshot": "eyJwcm9tdHRUeXBlIjoiZGV2aWNlQ29kZUZsb3ciLCJjb21tYW5kIjoiQ29ubmVjdC1BekFjY291bnQiLCJwYXJhbWV0ZXJzIjp7fSwiZXhwaXJhdGlvbiI6MTY4NDMyMjAwMH0=",
        "userResponse": {
          "confirmed": true,
          "timestamp": 1684318500
        }
      }
    }
  }
}

=== RESPONSE 2 (Success) ===
{
  "jsonrpc": "2.0",
  "id": "req-2",
  "result": {
    "content": [{
      "type": "text",
      "text": "Account             SubscriptionName TenantId                             Environment\n-------             ---------------- --------                             -----------\nuser@example.com    Production       xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx AzureCloud"
    }]
  }
}
```

#### 4.D.8 Pros & Cons Analysis

**Advantages:**

- ✅ **Stateless from MCP perspective:** No server-side session management needed
- ✅ **Client-managed state:** Each client holds its own snapshot; server is completely stateless
- ✅ **Minimal server complexity:** No continuation context, no runspace pausing logic
- ✅ **Backward compatible:** Old clients that don't support retry simply see "prompt_required" as error
- ✅ **Timeout-safe:** If client never retries, request naturally times out; server holds no resources
- ✅ **Works in both in-process and OOP:** Same pattern applies to both execution modes
- ✅ **Enables real interactivity:** Supports device codes, confirmations, credential entry—all real use cases
- ✅ **Concurrent-safe:** Multiple clients with different `promptId`s don't interfere
- ✅ **Audit trail:** Each request is independent; no complex session cleanup logic needed
- ✅ **Simple host implementation:** Custom PSHost intercepts prompt, returns metadata, re-runs with injection

**Disadvantages:**

- ❌ **Two requests required:** Not atomic from client perspective; requires explicit retry logic
- ❌ **State snapshot design complexity:** Must carefully decide what data to encode (not too much, not too little)
- ❌ **Security consideration:** Snapshot must be tamper-proof (requires HMAC/signatures)
- ❌ **Expiration handling:** Snapshots have TTL; expired snapshots must be rejected
- ❌ **Client implementation burden:** Clients must understand retry semantics; not all MCP clients may support this
- ❌ **Debugging complexity:** Multi-request flows harder to trace; need good logging and tracing
- ❌ **Reentrancy concerns:** What if prompt changes on retry (e.g., device code expires)? Need robust error handling
- ❌ **Performance:** Extra network round-trip per prompt (vs. Option C which completes in one logical flow)

#### 4.D.9 Comparison to Option C

| Aspect | Option C (Stateful) | Option D (Stateless) |
|--------|-------------------|----------------------|
| **Server-side state** | Yes, maintains session | No, client manages snapshot |
| **Session cleanup** | Required (timers, leaks) | Automatic (client holds snapshot) |
| **Resource risk** | Medium (long-lived sessions) | Low (requests are independent) |
| **Concurrency risk** | Higher (shared runspace, session collision) | Lower (each request independent) |
| **Snapshot encoding** | N/A | Required (adds complexity) |
| **Tampering risk** | Low (state is server-side) | Higher (need HMAC validation) |
| **Network efficiency** | Higher (logical single flow) | Lower (two requests) |
| **Debugging difficulty** | High (stateful, hard to replay) | Medium (stateless, easier to replay) |
| **Client support** | Requires updated clients | Graceful degradation (old clients see error) |
| **Scaling to many users** | Harder (session mgmt overhead) | Easier (truly stateless) |

#### 4.D.10 Threading & Concurrency Safety

**In-Process:**
- Request 1 acquires lock, executes, hits prompt, returns `PromptEncounteredException`
- Lock is released (no hanging)
- Request 2 from same or different client acquires lock, executes with snapshot + response
- Multiple concurrent requests work independently (each has its own `promptId`)
- **Safety:** ✅ Thread-safe; lock held only during execution (not during prompt wait)

**Out-of-Process:**
- Request 1 sent to subprocess
- Subprocess detects prompt, writes ndjson `prompt_required` message, waits for Request 2
- Request 2 sent to subprocess (could be from different invocation, same command name)
- Subprocess matches `promptId` and processes accordingly
- **Safety:** ⚠️ Needs careful protocol design to avoid message reordering; subprocess must buffer/queue based on `promptId`

#### 4.D.11 Effort & Implementation Timeline

**Phase 1 (Minimal In-Process PoC): ~2-3 weeks**
- Implement `PoshMcpPSHost` with prompt interception
- Snapshot encoding/decoding (base64 + HMAC)
- Integration tests with `Read-Host`, `Get-Credential`
- Schema metadata for retry support
- **Deliverable:** In-process Option D working; OOP deferred

**Phase 2 (Production-Ready): ~3-4 weeks**
- Security review of snapshot encoding
- Load testing (concurrent prompts)
- Error handling (expired snapshots, mismatched promptId)
- Logging and tracing for multi-request flows
- **Deliverable:** Production-ready in-process Option D

**Phase 3 (OOP Support): ~2-3 weeks** (if needed)
- Protocol enhancement for subprocess prompt exchange
- Subprocess message buffering/queue
- End-to-end tests
- **Deliverable:** OOP Option D support

**Total timeline for full support: ~7-10 weeks** (vs. ~12+ weeks for Option C)

---

## 7. Configuration & UX

### 7.1 Per-Function Configuration

Add to `FunctionConfiguration` (in `appsettings.json`):

```json
{
  "functions": [
    {
      "name": "Get-Credential",
      "allowInteractivePrompts": true,
      "promptTimeoutSeconds": 30,
      "promptBehavior": "supported"
    },
    {
      "name": "Remove-Item",
      "allowInteractivePrompts": false,
      "promptBehavior": "not-supported"
    }
  ]
}
```

### 7.2 Global Configuration

```json
{
  "powerShell": {
    "interactivePrompts": {
      "enabled": true,
      "defaultTimeoutSeconds": 30,
      "strategy": "reject" | "inject-defaults" | "respond-to-prompt"
    }
  }
}
```

### 7.3 Schema Metadata

Extend MCP tool schema to advertise prompt behavior:

```json
{
  "name": "Get-Credential",
  "description": "Prompts for username and password",
  "promptBehavior": {
    "mayPrompt": true,
    "supportedPromptTypes": ["credential", "read-line"],
    "requiresInteractivity": true,
    "timeoutSeconds": 30
  },
  "parameters": [...]
}
```

### 7.4 Error Messages

When prompts are encountered:

**Option A (Inject):**
```
"Tool invocation completed successfully. Default values injected for missing mandatory parameters."
```

**Option B (Reject):**
```
"Error: Tool 'Get-Credential' is not supported in non-interactive mode. 
This command requires user input (credentials). 
Please provide credentials via parameters or reconfigure the tool to run in interactive mode."
```

**Option C (Prompt):**
```json
{
  "type": "prompt_required",
  "sessionId": "abc-123",
  "promptId": "cred-1",
  "promptType": "credential",
  "message": "Enter username:",
  "timeoutSeconds": 30
}
```

---

## 8. Recommended Next Steps

### 8.1 Phase 1 Decision: Option A+B vs. Option A+D

Two viable paths forward exist. The choice depends on **whether Phase 1 should include interactive prompt support**.

#### Option A+B: "Reject-and-Notify" (Baseline, Safest)

**Scope:**
1. Pre-execution validation (Option A): Detect mandatory parameters, inject defaults where safe
2. Prompt-as-error (Option B): Treat prompts as errors; return clear error message
3. Schema metadata: Mark commands as "may-prompt" to help clients understand limitations

**Advantages:**
- ✅ Minimal risk (no new architecture needed)
- ✅ Fastest to implement (~3 weeks)
- ✅ Clear semantics: "This command cannot run in this mode"
- ✅ No server-side state
- ✅ Works for ~80% of automation use cases (non-interactive deployments)

**Disadvantages:**
- ❌ Does not support interactive scenarios (device code flows, confirmations)
- ❌ Commands like `Get-Credential`, administrative confirmations are unusable
- ❌ Less useful for human-in-the-loop workflows

**Recommendation if:** You want Phase 1 to be "rock solid, minimal risk, covers automation majority" and defer interactivity to Phase 2.

---

#### Option A+D: "Interactive with Client-Managed State" (New, Better UX)

**Scope:**
1. Pre-execution validation (Option A): Same as above
2. Stateless prompt-retry (Option D): Return prompt metadata; client provides response in second request
3. Snapshot encoding: Base64-encoded, HMAC-signed state (secure, tamper-proof)
4. Schema metadata: Mark commands with prompt support, advertise retry semantics

**Advantages:**
- ✅ Enables real interactivity (device codes, confirmations, credential entry)
- ✅ Stateless from server perspective (no session management complexity)
- ✅ Safer than Option C (no long-lived state, no runspace pausing issues)
- ✅ Faster than Option C (~7-10 weeks vs. ~12-16 weeks)
- ✅ Backward compatible (old clients see error on first request)
- ✅ Concurrent-safe (multiple clients with independent `promptId`s)
- ✅ Supports both in-process and OOP (no major architectural change)

**Disadvantages:**
- ❌ Requires two requests per prompt (not atomic)
- ❌ Adds snapshot encoding complexity (security review needed)
- ❌ Client must implement retry logic (burden on integrators)
- ❌ Implementation time longer than A+B (~7-10 weeks vs. ~3 weeks)
- ❌ More testing surface (multi-request flows)

**Recommendation if:** You want Phase 1 to include some interactivity, and you're willing to trade additional complexity for statelessness (safer than C).

---

### 8.1a Phase 1 Recommendation: **Option A+D (Stateless Prompt-Retry)**

**Rationale:**

After weighing trade-offs, recommend **Option A+D** as Phase 1 for these reasons:

1. **Safety over complexity:** Option D provides interactivity **without server-side session state**. This is a semantic win over Option C.

2. **Timeline feasibility:** ~7-10 weeks is reasonable Phase 1 scope (vs. ~12-16 for Option C).

3. **Deployment flexibility:** With Option D, teams can defer OOP support and ship in-process first (2-3 weeks), then add OOP (2-3 weeks later).

4. **Future-proofs architecture:** Stateless design means we can later add Option B (fail-fast) as a configuration toggle, or even add Option C later without rearchitecting.

5. **Real use cases unblocked:** Teams can now use `Connect-AzAccount` with device code, get confirmations, request credentials—modern interactive patterns.

6. **Lower risk than C:** No runspace pausing, no complex state cleanup, no deadlock risks in OOP subprocess.

**Alternative: Phased Approach**

If you want to be more conservative, consider:

- **Phase 1a (Weeks 1-3):** Option A+B (reject-and-notify, minimal risk)
- **Phase 1b (Weeks 4-10):** Option A+D (stateless retry, safer than C)

This lets you ship minimal Phase 1a fast, then add Option D as a Phase 1b enhancement.

---

### 8.2 Phase 2: Feasibility & Refinement

Regardless of Phase 1 choice:

1. **Gather user feedback** on Phase 1 implementation
2. **Monitor incident reports:** Are clients hitting prompt-related errors often?
3. **Assess interactivity demand:** If Option A+B was Phase 1, do customers want Option D?
4. **Consider Option C later:** Only if Option D proves insufficient (rare)

---

### 8.3 Go/No-Go Assessment for Option D in Phase 1

**Key question:** Is Option D viable as Phase 1, or should we stick with A+B?

**Answer: Yes, Option D is viable.**

**Conditions:**
1. ✅ Security review of snapshot encoding/HMAC approach (straightforward)
2. ✅ In-process implementation has no blockers (custom PSHost is well-understood pattern)
3. ✅ OOP implementation can be deferred (start with in-process only)
4. ✅ Client integration is manageable (new MCP clients can understand `_promptResponse` convention)
5. ✅ Timeline is realistic (~7-10 weeks)

**Red flags that would cause "No-Go":**
- ❌ If snapshot encoding requires encryption (vs. just HMAC)—adds crypto complexity
- ❌ If in-process runspace threading proves unsafe during prototyping—pivot to Option C
- ❌ If OOP protocol redesign is deemed too risky—stay with A+B
- ❌ If timeline pressure requires <4 week Phase 1—revert to A+B

**Recommendation: Proceed with Option D as Phase 1, with a 1-week security review spike.**

---

### Previous Section: 8.2 Medium-term (Phase 2: Feasibility Study)

**Explore Option C for in-process only:**

1. **Research & PoC:**
   - Prototype custom PSHost for in-process execution
   - Test thread safety with concurrent invocations
   - Measure runspace state impact

2. **Prototype deliverables:**
   - Custom `PSHost` + `PSHostRawUserInterface` implementation
   - Session state manager for prompt/response pairing
   - Integration tests with `Read-Host` and `Get-Credential`

3. **Go/no-go decision:**
   - If complex/risky: archive and stay with Options A+B
   - If feasible: plan full implementation for Phase 3

**Effort:** ~1-2 sprints (research + prototype, not shipping)

### Previous Section: 8.3 Future (Phase 3+: Full Interactive Mode)

If Phase 2 PoC succeeds:

1. **Full Option C implementation** (in-process):
   - Production-ready custom PSHost
   - Comprehensive error handling (timeouts, invalid responses, etc.)
   - Session cleanup & lifecycle management
   - Load testing under concurrent load

2. **Extended to out-of-process** (if demand warrants):
   - Protocol enhancement for prompt exchange
   - Subprocess custom PSHost
   - End-to-end testing

**Effort:** ~4-6 sprints (full feature)

---

## 9. Risk Assessment

| Risk | Option | Likelihood | Impact | Mitigation |
|------|--------|-----------|--------|-----------|
| **Runspace state corruption** | C | Medium | High | Extensive testing; consider per-session runspaces |
| **Protocol deadlock** | C (OOP) | High | High | Defer OOP interactive support to future |
| **Long-running prompts exhaust memory** | C | Low | Medium | Implement aggressive cleanup; per-session timeouts |
| **Snapshot tampering attack** | D | Low | Medium | Require HMAC validation of all snapshots |
| **Snapshot expiration edge case** | D | Low | Low | Reject expired snapshots; clear error message |
| **PromptId collision (concurrent)** | D | Very Low | Medium | Use UUIDs for promptId; very low collision risk |
| **Client never responds to prompt** | D | Medium | Low | Timeout after 30s; fail gracefully |
| **False negatives** (command prompts despite validation) | A | Low | Medium | Comprehensive logging; alert operators |
| **Breaking existing workflows** (config changes) | A, B | Low | Low | Default to backward-compatible behavior |

---

## 10. Open Questions

1. **Phase 1 decision:** Should Phase 1 include Option D (stateless retry), or stay with A+B (reject-and-notify) for safety?

2. **In-process runspace model:** Should we move away from singleton runspace if Option D (or later, Option C) is implemented? Per-session runspaces would isolate state but increase memory/startup time.

3. **Session lifetime:** For Option D, how long should a snapshot remain valid? 5 minutes? 1 hour? Configurable per-command?

4. **Multiple prompts per command:** If a single command triggers multiple prompts (e.g., `Get-Credential` followed by confirmation), does the protocol scale? Do we need sequential or simultaneous prompt handling?

5. **Audit trail:** Should we log all prompt/response interactions for compliance? Especially for sensitive inputs like credentials. Should snapshot HMAC include timestamp?

6. **AI agent behavior:** How should AI agents (e.g., Claude, GPT) handle `prompt_required` responses? Should PoshMcp provide guidance on best practices?

7. **OOP mode decision:** For Option D, should Phase 1 include OOP support, or start in-process only? Starting in-process is lower risk; OOP can be Phase 1b or Phase 2.

8. **Snapshot versioning:** If schema changes in the future, how do we handle old snapshots? Version field in snapshot?

9. **Snapshot size limits:** Should snapshots have a max size? What if command has many parameters?

---

## 11. Related Decisions & Context

1. **In-process runspace model:** Should we move away from singleton runspace if Option C is implemented? Per-session runspaces would isolate state but increase memory/startup time.

2. **Session lifetime:** For Option C, how long should a session remain open after the initial command invocation? Until client closes it? Timeout after inactivity?

3. **Multiple prompts per command:** If a single command triggers multiple prompts (e.g., `Get-Credential` followed by confirmation), how does the protocol scale? Sequential or simultaneous?

4. **Audit trail:** Should we log all prompt/response interactions for compliance? Especially for sensitive inputs like credentials.

5. **AI agent behavior:** How should AI agents (e.g., Claude, GPT) handle `prompt_required` responses? Should PoshMcp provide guidance on best practices?

6. **OOP mode decision:** Is out-of-process mode intended for high-security, non-interactive deployments? Or should it eventually support interactivity?

---

## 11. Related Decisions & Context

- **Decision**: OOP execution was chosen to isolate heavy modules (Az.*, Microsoft.Graph.*) from the .NET host process.
- **Constraint**: MCP protocol is stateless request/response; not designed for multi-turn interactions.
- **Deployment Model**: Most PoshMcp instances are non-interactive (no console), running in containers or as background services.
- **User Base**: Mix of automation engineers (non-interactive preferred) and administrators (interactive desired).

---

## 12. Implementation Examples

### 12.1 Option A: Pre-Execution Validation

```csharp
// Pseudo-code
private static void ValidateCommandCanExecuteNonInteractively(
    CommandInfo commandInfo, 
    PowerShellParameterInfo[] parameterInfos, 
    object[] parameterValues)
{
    // Find all mandatory parameters in the command
    var mandatoryParams = commandInfo.Parameters
        .Where(p => p.Value.Attributes.OfType<ParameterAttribute>()
            .Any(attr => attr.Mandatory))
        .ToList();
    
    // Check that all mandatory parameters are provided
    foreach (var mandatoryParam in mandatoryParams)
    {
        var providedValue = parameterValues
            .FirstOrDefault(pv => /* matches mandatoryParam */);
        
        if (providedValue == null)
        {
            throw new InvalidOperationException(
                $"Command '{commandInfo.Name}' requires mandatory parameter '{mandatoryParam.Key}'. " +
                $"This command may prompt for input, which is not allowed in non-interactive mode. " +
                $"Please supply all mandatory parameters before invoking.");
        }
    }
    
    // Check for known prompt-triggering cmdlets
    if (commandInfo.Name is "Read-Host" or "Get-Credential" or "Out-Host -Paging")
    {
        throw new InvalidOperationException(
            $"Command '{commandInfo.Name}' is not supported in non-interactive mode. " +
            $"It requires user input.");
    }
}
```

### 12.2 Option B: Prompt-as-Error (Schema)

```csharp
// In schema generation
public class McpToolSchema
{
    public string Name { get; set; }
    public string Description { get; set; }
    
    // NEW FIELD:
    public PromptBehaviorMetadata PromptBehavior { get; set; }
    
    public List<McpToolParameter> InputSchema { get; set; }
}

public class PromptBehaviorMetadata
{
    /// <summary>
    /// If true, this tool may prompt for user input and cannot be used without providing all parameters.
    /// </summary>
    public bool MayPrompt { get; set; }
    
    /// <summary>
    /// Types of prompts this tool may display (e.g., "credential", "confirmation", "read-line").
    /// </summary>
    public List<string> SupportedPromptTypes { get; set; }
    
    /// <summary>
    /// Recommended timeout (in seconds) for prompt response.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
    
    /// <summary>
    /// Human-readable message for clients.
    /// </summary>
    public string Note { get; set; }
}
```

---

## 13. Success Metrics

After implementation, measure:

1. **Error rate for prompt-related failures:** Decrease from "unexpected timeout" to "clear error message"
2. **Tool adoption:** Track which tools are invoked; identify if prompt-heavy tools are avoided
3. **Client feedback:** Gather input on whether error messages are helpful
4. **Incident response time:** Reduced debug time for "tool hangs" issues

---

## 14. References

- **PowerShell SDK Documentation:** https://learn.microsoft.com/en-us/dotnet/api/system.management.automation.host.pshost
- **MCP Specification:** https://modelcontextprotocol.io
- **PoshMcp Architecture:** See `specs/out-of-process-execution.md` and `DESIGN.md`
- **Current OOP Implementation:** `PoshMcp.Server/PowerShell/OutOfProcess/oop-host.ps1`
- **In-Process Assembly Generation:** `PoshMcp.Server/PowerShell/PowerShellAssemblyGenerator.cs`

---

## Appendix A: Glossary

- **PSHost:** PowerShell abstraction for user interaction (read input, write output)
- **PSHostRawUserInterface:** Low-level API for reading keystrokes, mouse input, etc.
- **In-process:** PowerShell command executed within the MCP server's .NET process
- **Out-of-process (OOP):** PowerShell command executed in a separate `pwsh` subprocess
- **ndjson:** Newline-delimited JSON (one JSON object per line)
- **Prompt:** Any request for user input (Read-Host, Get-Credential, confirmation, etc.)
- **Session:** Long-lived context for state management across multiple MCP calls
- **Continuation:** Paused execution state that can be resumed with additional input

---

## Appendix B: Decision Matrix

| Criteria | Option A (Inject) | Option B (Reject) | Option C (Stateful) | Option D (Stateless Retry) |
|----------|-------------------|------------------|-------------------|--------------------------|
| **Complexity** | Low | Low | High | Medium-High |
| **Risk** | Low | Low | High | Medium |
| **Interactivity** | ❌ No | ❌ No | ✅ Yes | ✅ Yes |
| **Fail-fast** | ✅ Fast (first req) | ✅ Fast | ⚠️ Depends | ⚠️ Fast initially, retry needed |
| **Backward-compatible** | ✅ Yes | ✅ Yes | ⚠️ Needs new clients | ✅ Graceful degradation |
| **Server-side state** | None | None | Extensive | None (client-managed) |
| **Session cleanup** | N/A | N/A | Required | Automatic |
| **Implementation time** | ~2 weeks | ~1 week | ~12-16 weeks | ~7-10 weeks |
| **Testing complexity** | Low | Low | High | Medium |
| **Deployment safety** | ✅ Safe | ✅ Safe | ⚠️ Stateful (risky) | ✅ Safe (stateless) |
| **Concurrent requests** | ✅ Independent | ✅ Independent | ⚠️ Session collision risk | ✅ PromptId-isolated |
| **OOP support** | ✅ Trivial | ✅ Built-in | ⚠️ Protocol redesign | ✅ Protocol enhancement only |
| **Production-ready** | ✅ Q2 2026 | ✅ Q2 2026 | ⏳ Q3-Q4 2026 | ✅ Q3 2026 |

**Legend:**
- ✅ = Fully supported / Advantage
- ⚠️ = Partial / Tradeoff
- ❌ = Not supported / Disadvantage

**Key Observations:**
- **Option A+B:** Lowest risk, minimal interactivity, fastest to ship
- **Option D:** Middle ground—enables interactivity with stateless architecture (safer than C)
- **Option C:** Most capable but highest complexity and risk; defer unless Option D proves insufficient

