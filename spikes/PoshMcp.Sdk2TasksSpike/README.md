# MCP SDK 2 Tasks compatibility spike

This isolated `net10.0` console project evaluates `ModelContextProtocol` 2.0.0-preview.3 and
`ModelContextProtocol.Extensions.Tasks` 2.0.0-preview.3. It is intentionally not in
`PoshMcp.sln`; production is on SDK **2.0.0** and default protocol **2026-07-28** (Tasks
extension remains deferred — see Findings below).

Run:

```powershell
dotnet run --project spikes\PoshMcp.Sdk2TasksSpike\PoshMcp.Sdk2TasksSpike.csproj
```

The executable creates task-enabled tools with the same `McpServerTool.Create` mechanism used
by `McpToolFactoryV2`, then validates:

- automatic polling returns a normal `CallToolResult`;
- manual polling receives a task handle, reaches `Completed`, and preserves the tool result;
- `tasks/cancel` cancels the tool's `CancellationToken` and reaches `Cancelled`.

## Findings (2026-07-16)

SDK 2 preview Tasks work with the dynamic tool shape, but only when client and server negotiate
the July 2026 draft protocol (`2026-07-28`) and the client opts into the extension per tool
call. `WithTasks` adds `tasks/get`, `tasks/update`, and `tasks/cancel`, wraps calls in a
background task, and supplies a task-specific cancellation token.

Validation on .NET SDK 10.0.302:

```text
Build succeeded. 0 Warning(s), 0 Error(s)
PASS auto-poll: completed task returned tool result
PASS manual-poll: created task was completed and preserved CallToolResult
PASS cancellation: tasks/cancel cancelled the tool CancellationToken and task state
```

The preview package set resolves together (`ModelContextProtocol`,
`ModelContextProtocol.AspNetCore`, and `ModelContextProtocol.Extensions.Tasks`, all
`2.0.0-preview.3`). The standalone stream transport setup must construct the server with
`McpServer.Create(...)`; `AddMcpServer()` alone does not register an `McpServer` service. This
is a source-level hosting difference to account for in any isolated transport migration.

The in-memory store is appropriate only for this executable: it is process-local and loses task
state on restart. An HTTP deployment needs a durable, session-isolated `IMcpTaskStore`, plus task
retention, cleanup, ownership, authorization, and observability policies. The preview uses
experimental (`MCPEXP001`, `MCPEXP002`, `MCPEXP004`) extensibility seams and moves Tasks into a
new package. SDK 2 also changes Streamable HTTP behavior (including standalone GET-stream
configuration), so its transport upgrade needs separate HTTP/session lifecycle validation. It
cannot be safely introduced without a separate HTTP/session-lifecycle validation
and durable-store design against the production 2.0.0/2026-07-28 server.

**Recommendation: defer adoption.** Revisit after the Tasks protocol and SDK 2 APIs stabilize,
with a separate durable-store and HTTP/session-lifecycle design.
