# Spec: Noun-Derived MCP Resource Mapping

**Status:** Draft  
**Author:** Farnsworth (Lead/Architect)  
**Date:** 2026-05-18  
**Relates to:** `PowerShellConfiguration`, `McpResources`, `McpToolFactoryV2`

---

## 1. Overview

### What the Feature Does

PoshMcp exposes PowerShell commands as MCP tools. PowerShell commands follow a `Verb-Noun` naming convention (e.g., `Get-BamiTenantUser`, `Set-BamiTenantUser`, `Remove-BamiTenantUser`). The nouns in those names identify coherent **entity types** in the domain.

This feature automatically derives MCP **resources** from the nouns present in configured commands, where a resource is backed by the corresponding `Get-{Noun}` command. It also augments tool call results with a **`resourceLinkBlock`** — a well-known JSON annotation that tells the MCP client how to fetch the canonical resource for the entity the tool just acted on.

### Why It Is Valuable

- **Discoverability**: MCP clients that support `resources/list` and `resources/read` gain structured, URI-addressable views of PowerShell-managed entities without requiring manual resource configuration.
- **Context continuity**: After a mutating tool call (e.g., `Assert-BamiTenantUser`), a `resourceLinkBlock` in the result tells the client exactly where to read the updated state — closing the read-your-writes loop within the MCP session.
- **Low operator burden**: Noun-derived resources are registered automatically from the same `CommandNames`/`Modules`/`IncludePatterns` configuration already used for tools. No manual `McpResources.Resources[]` entries are needed.

### Key Terminology

| Term | Definition |
|---|---|
| **Noun** | The part of a PowerShell `Verb-Noun` command name after the first `-`. For `Get-BamiTenantUser`, the noun is `BamiTenantUser`. |
| **Resource name** | The snake_case identifier derived from a noun, used in the MCP resource URI. `BamiTenantUser` → `bami_tenant_user`. |
| **Canonical Get command** | The `Get-{Noun}` command in the configured command set that backs a noun's resource. |
| **Resourceable noun** | A noun for which a canonical `Get-{Noun}` command is present in the discovered command set. |
| **resourceLinkBlock** | A JSON object appended as an extra `TextContent` item in a tool's `CallToolResult`, pointing the MCP client to the resource URI for the affected entity. |

---

## 2. Noun Extraction & Resource Naming

### 2.1 Verb Extraction (Existing)

The existing `McpToolFactoryV2.ExtractVerbFromCommandName(string commandName)` already extracts the verb:

```csharp
return commandName.Contains('-') ? commandName.Split('-')[0] : commandName;
```

### 2.2 Noun Extraction

The **noun** is everything after the first `-`. For commands that have no `-`, there is no verb-noun structure and no noun is derived.

```
"Get-BamiTenantUser"    → verb = "Get",    noun = "BamiTenantUser"
"Assert-BamiTenantUser" → verb = "Assert", noun = "BamiTenantUser"
"Get-Location"          → verb = "Get",    noun = "Location"
"Get-Date"              → verb = "Get",    noun = "Date"
"SomeCommand"           → no noun (no dash)
```

Implementation:

```csharp
internal static string? ExtractNounFromCommandName(string commandName)
{
    var dashIndex = commandName.IndexOf('-');
    if (dashIndex < 0 || dashIndex == commandName.Length - 1)
        return null;
    return commandName[(dashIndex + 1)..];
}
```

### 2.3 Resource Name Derivation (Noun → snake_case)

The **resource name** is the PascalCase noun converted to `snake_case`. The conversion algorithm:

1. Insert `_` before each uppercase letter that is:
   - Preceded by a lowercase letter (`aB` → `a_B`), OR
   - Preceded by an uppercase letter that is itself followed by a lowercase letter (`ABc` → `A_Bc` at the `B`).
2. Lowercase the entire result.

This correctly handles compound nouns with acronym prefixes:

```
BamiTenantUser  → bami_tenant_user
Location        → location
Date            → date
HTMLParser      → html_parser
BamiTenant      → bami_tenant
```

Reference regex (C#): `Regex.Replace(noun, @"(?<=[a-z])([A-Z])|(?<=[A-Z])([A-Z][a-z])", "_$1$2").ToLowerInvariant()`

The resource name is the **unique identifier** for the noun within a server instance. It is stable across restarts for a given noun string.

### 2.4 Canonical Get Command

A noun `N` is **resourceable** if and only if `Get-{N}` (exact, case-insensitive) appears in the set of discovered commands. Commands discovered through `CommandNames`, `Modules`, or `IncludePatterns` all qualify — the source does not matter.

When multiple commands share a noun (e.g., `Get-BamiTenantUser`, `Set-BamiTenantUser`, `Assert-BamiTenantUser`), all map to the same resource name (`bami_tenant_user`), backed by `Get-BamiTenantUser`.

### 2.5 Noun Conflict Rules

A **noun conflict** occurs when two commands from different modules produce the same resource name (e.g., `ModuleA\Get-User` and `ModuleB\Get-User` both produce `user`). Resolution rules:

1. **First-writer-wins**: the command discovered first (in the order `CommandNames` → `Modules` → `IncludePatterns`, within each, in declaration order) claims the resource name.
2. The second command's noun is marked as conflicted and does **not** generate a resource. A warning is logged at discovery time.
3. The conflict is surfaced in the doctor report (see §8.3).

### 2.6 Singular/Plural and Compound Nouns

No singularization/pluralization is applied. The resource name is derived purely from the noun string. `Get-Users` and `Get-User` are **different** nouns and produce separate resources (`users` vs. `user`). Operators are responsible for using consistent naming in their PowerShell modules.

---

## 3. Resource Discovery

### 3.1 Discovering Resourceable Nouns

Resource discovery happens **during tool discovery**, immediately after `McpToolFactoryV2.GetToolsListAsync` completes (before resources are registered with the MCP server). A new `NounRegistry` class (§6.1) performs a single pass over the discovered command set.

Algorithm:
1. For each discovered command, extract its noun.
2. Group commands by resource name.
3. For each group, check whether `Get-{Noun}` (case-insensitive on both verb and noun) is in the group.
4. Groups with a canonical Get command → **resourceable** (added to registry).
5. Groups without a canonical Get command → **non-resourceable** (no resource created; tools in this group still receive `resourceLinkBlock` suppressed — see §5.2).

### 3.2 Nouns Without a Get Command

If a noun has only `Set-Foo`, `Remove-Foo`, `Assert-Foo`, etc. but no `Get-Foo`:

- **No resource is created** for that noun.
- Tool results for those commands do **not** receive a `resourceLinkBlock`.
- This is by design: a resourceLinkBlock without a backing `resources/read` endpoint would confuse clients.

If a `Get-Foo` command is later added (e.g., via configuration reload), the noun becomes resourceable on the next reload cycle.

### 3.3 Configuration

Noun resource derivation is **opt-in** via a global toggle in `PowerShellConfiguration`:

```json
"PowerShellConfiguration": {
  "EnableNounResources": false
}
```

Default: `false`. When `false`, no noun-derived resources are created and no `resourceLinkBlock` is injected into tool results.

Per-noun opt-out is supported via `NounResourceOverrides` (see §7).

---

## 4. Resource Schema

### 4.1 URI Scheme

All noun-derived resources use the existing `poshmcp://resources/` prefix:

```
poshmcp://resources/{resource_name}
```

Examples:

| Command | Noun | Resource Name | URI |
|---|---|---|---|
| `Get-BamiTenantUser` | `BamiTenantUser` | `bami_tenant_user` | `poshmcp://resources/bami_tenant_user` |
| `Get-Location` | `Location` | `location` | `poshmcp://resources/location` |
| `Get-BamiTenantContext` | `BamiTenantContext` | `bami_tenant_context` | `poshmcp://resources/bami_tenant_context` |

Noun-derived resources are **parameterless** at the URI level (no `/{id}` segment). The backing `Get-{Noun}` command is invoked with no arguments. This is the "list/singleton" read pattern: it returns whatever `Get-{Noun}` returns without parameters.

**Rationale for no `/{id}` segment**: PowerShell `Get-*` commands vary wildly in their parameter signatures. Encoding parameter values into URI segments creates an unbounded schema surface. Parameterized reads (e.g., `Get-BamiTenantUser -Alias foo`) are better served by the existing MCP tool call path. This spec does not preclude a future parameterized resource URI extension.

### 4.2 Resource Response Structure

A `resources/read` response for a noun-derived resource has:

```json
{
  "contents": [
    {
      "uri": "poshmcp://resources/bami_tenant_user",
      "mimeType": "application/json",
      "text": "<JSON-serialized output of Get-BamiTenantUser>"
    }
  ]
}
```

The `text` field contains the JSON-serialized output of the canonical `Get-{Noun}` command, using the same `PowerShellObjectSerializer.FlattenPSObject` → `JsonSerializer.Serialize` pipeline already used by `McpResourceHandler.SerializeCommandOutput`. This ensures the wire format is consistent with command-backed static resources.

### 4.3 Content Type

Noun-derived resources always use `application/json` as the MIME type, regardless of whether the underlying Get command returns scalar strings or objects. This differs from the static resource default of `text/plain`. The rationale: noun resources are expected to return structured entity data, and `application/json` signals to clients that they can parse and navigate the response.

If a `Get-{Noun}` command returns a single plain string, the serialized JSON will be `"the string value"` (a JSON string literal) — still valid `application/json`.

---

## 5. ResourceLinkBlock Augmentation

### 5.1 Block Structure

When a tool whose noun is resourceable completes execution, an additional `TextContent` item is appended to the `CallToolResult.Content` array:

```json
{
  "type": "text",
  "mimeType": "application/json+mcp-resource-link",
  "text": "{\"resourceLink\":{\"uri\":\"poshmcp://resources/bami_tenant_user\",\"resourceName\":\"bami_tenant_user\",\"noun\":\"BamiTenantUser\",\"relationship\":\"subject\",\"description\":\"Read the current state of BamiTenantUser via Get-BamiTenantUser\"}}"
}
```

Fields in the `resourceLink` object:

| Field | Type | Description |
|---|---|---|
| `uri` | string | The `poshmcp://resources/{resource_name}` URI for the noun's resource. |
| `resourceName` | string | The snake_case resource name (e.g., `bami_tenant_user`). |
| `noun` | string | The original PascalCase noun (e.g., `BamiTenantUser`). |
| `relationship` | string | Always `"subject"` in this spec. Identifies this as the entity the tool operated on. |
| `description` | string | Human-readable hint: `"Read the current state of {Noun} via Get-{Noun}"`. |

The `mimeType` value `application/json+mcp-resource-link` is a PoshMcp convention that allows clients to detect and selectively parse this block without ambiguity.

### 5.2 Which Tools Are Augmented

A tool result is augmented if and only if:
1. `EnableNounResources` is `true`.
2. The tool's command has a noun (verb-noun structure with a `-`).
3. That noun is **resourceable** (a `Get-{Noun}` command exists in the discovered set).
4. The noun's resource is **not disabled** via `NounResourceOverrides` (see §7).

Tools whose nouns have no canonical Get command (§3.2) do **not** receive a `resourceLinkBlock`.

### 5.3 Injection Point

The `resourceLinkBlock` is the **last** item in the `CallToolResult.Content` array. Tools that return errors (i.e., `IsError = true`) do **not** receive a `resourceLinkBlock` — injecting a resource link into an error result is misleading.

### 5.4 Injection Mechanism

The injection is implemented via a `ResourceLinkInjectorWrapper` that wraps each `McpServerTool` at registration time (§6.3). The wrapper:

1. Calls the underlying tool's handler.
2. Inspects the returned `CallToolResult`.
3. If `IsError == false` and the noun is resourceable, appends the `resourceLinkBlock` content item.
4. Returns the augmented result.

This is a **post-execution decoration** pattern, analogous to how `EnableDynamicReloadTools` wraps/appends tools after the primary list is built.

---

## 6. Implementation Approach

### 6.1 New Component: `NounRegistry`

**File:** `PoshMcp.Server/McpResources/NounRegistry.cs`  
**Namespace:** `PoshMcp.Server.McpResources`

```csharp
public sealed class NounRegistry
{
    // Keyed by resource name (snake_case). Thread-safe; immutable after Build().
    private IReadOnlyDictionary<string, NounEntry> _entries;

    public static NounRegistry Build(IEnumerable<string> discoveredCommandNames, ILogger logger);

    // Returns null if the noun is not resourceable.
    public NounEntry? GetEntry(string noun);

    // Returns null if the resource name has no entry.
    public NounEntry? GetEntryByResourceName(string resourceName);

    public IReadOnlyList<NounEntry> AllEntries { get; }
}

public sealed record NounEntry(
    string Noun,                  // e.g. "BamiTenantUser"
    string ResourceName,          // e.g. "bami_tenant_user"
    string Uri,                   // e.g. "poshmcp://resources/bami_tenant_user"
    string CanonicalGetCommand,   // e.g. "Get-BamiTenantUser"
    bool IsConflicted             // true if resource name was claimed by a different module
);
```

`NounRegistry.Build` accepts the full list of discovered command names (both in-process and OOP), applies the noun extraction and conflict resolution rules, and returns an immutable registry. It is called once per discovery cycle, immediately after `GetToolsListAsync` returns.

### 6.2 New Component: `McpNounResourceHandler`

**File:** `PoshMcp.Server/McpResources/McpNounResourceHandler.cs`  
**Namespace:** `PoshMcp.Server.McpResources`

Handles `resources/list` and `resources/read` for noun-derived resources. It composes with the existing `McpResourceHandler` (for statically configured resources) via a **composite handler** pattern.

The composite:
1. `resources/list` → merge static resources + noun-derived resources.
2. `resources/read` → try static handler first; if `ResourceNotFound`, try noun handler.

The `McpNounResourceHandler.HandleReadAsync` implementation:
1. Look up the URI in the `NounRegistry`.
2. Execute `Get-{Noun}` with no arguments on the shared `IPowerShellRunspace`.
3. Serialize output via `PowerShellObjectSerializer`.
4. Return `TextResourceContents` with `mimeType = "application/json"`.

For the OOP runtime mode, the read executes via the same `ICommandExecutor` used for tool execution.

### 6.3 New Component: `ResourceLinkInjectorWrapper`

**File:** `PoshMcp.Server/McpResources/ResourceLinkInjectorWrapper.cs`  
**Namespace:** `PoshMcp.Server.McpResources`

```csharp
public static class ResourceLinkInjector
{
    // Wraps a tool if its noun is resourceable; returns the original tool otherwise.
    public static McpServerTool MaybeWrap(McpServerTool tool, NounRegistry registry, ILogger logger);
}
```

The wrapper checks whether the `McpServerTool`'s underlying command name has a resourceable noun. If yes, it produces a new `McpServerTool`-compatible instance that delegates to the original and appends the `resourceLinkBlock` content item on success.

**Open question (OQ-1):** The `McpServerTool` class in the SDK may be sealed or have factory-only construction. If direct subclassing is not possible, the wrapper will use a decorator that constructs a new `McpServerTool` via `McpServerTool.Create(...)` with a lambda that wraps the original invocation. Implementer must verify the SDK surface.

### 6.4 Changes to Existing Components

**`McpToolSetupService.SetupMcpToolsAsync` and `SetupHttpMcpToolsAsync`:**
- After `GetToolsListAsync`, if `EnableNounResources == true`:
  1. Build a `NounRegistry` from the discovered tool command names.
  2. Wrap each tool via `ResourceLinkInjector.MaybeWrap`.
  3. Stash the `NounRegistry` for use by resource handler registration.
- Pass the `NounRegistry` (or null if disabled) to `StdioServerHost.RegisterMcpServerServices` and `HttpServerHost.RegisterMcpServerServices`.

**`StdioServerHost.RegisterMcpServerServices` and HTTP equivalent:**
- Accept an optional `NounRegistry? nounRegistry` parameter.
- If non-null, build a `McpNounResourceHandler` and compose it with the existing `McpResourceHandler` in a composite.
- Wire the composite into `WithListResourcesHandler` / `WithReadResourceHandler`.

**`ConfigurationLoader`:**
- Bind the new `EnableNounResources` and `NounResourceOverrides` fields from configuration.

### 6.5 Sequence at Startup

```
1. LoadPowerShellConfiguration → read EnableNounResources flag
2. McpToolSetupService.SetupMcpToolsAsync
   a. GetToolsListAsync → returns List<McpServerTool> + populates command name set
   b. if EnableNounResources:
      i.  NounRegistry.Build(commandNames)      → NounRegistry
      ii. ResourceLinkInjector.MaybeWrap(tools) → augmented tool list
3. RegisterMcpServerServices(tools, nounRegistry)
   a. new McpNounResourceHandler(nounRegistry, runspace/executor)
   b. CompositeResourceHandler(staticHandler, nounHandler)
   c. .WithListResourcesHandler(composite.HandleListAsync)
   d. .WithReadResourceHandler(composite.HandleReadAsync)
```

### 6.6 Configuration Reload

On `configuration reload` (triggered by `ConfigurationReloadTools`), the discovery cycle repeats from step 2. A new `NounRegistry` is built from the refreshed command set, and the wrapped tools and composite handler are rebuilt. The server's resource handler references are updated via the same reload mechanism used for tools.

### 6.7 Threading / Runspace Considerations

- `NounRegistry` is immutable after `Build()` and freely shareable across threads.
- `McpNounResourceHandler.HandleReadAsync` executes `Get-{Noun}` via `IPowerShellRunspace.ExecuteThreadSafe` (in-process) or `ICommandExecutor` (OOP). Both are already thread-safe for concurrent resource reads.
- The composite handler has no mutable state; composition is safe.

---

## 7. Configuration

### 7.1 New Keys in `PowerShellConfiguration`

```json
"PowerShellConfiguration": {
  "EnableNounResources": false,
  "NounResourceOverrides": {
    "location": {
      "Disabled": true
    },
    "bami_tenant_user": {
      "ResourceName": "tenant_user",
      "Uri": "poshmcp://resources/tenant_user",
      "Description": "Current tenant users",
      "DisableResourceLinkBlock": false
    }
  }
}
```

**`EnableNounResources`** (bool, default `false`): Global opt-in toggle. When `false`, the entire feature is inert.

**`NounResourceOverrides`** (dictionary, keyed by **default resource name**): Per-noun configuration overrides. Any noun not present in this dictionary uses defaults.

Each override entry supports:

| Field | Type | Default | Description |
|---|---|---|---|
| `Disabled` | bool | `false` | If `true`, no resource is created for this noun and no `resourceLinkBlock` is injected for its tools. |
| `ResourceName` | string? | null (use derived) | Override the snake_case resource name. Must be unique across all resources. |
| `Uri` | string? | null (use derived) | Override the full resource URI. Must be unique. |
| `Description` | string? | null (use generated) | Override the human-readable resource description. |
| `DisableResourceLinkBlock` | bool | `false` | If `true`, the resource is created (visible in `resources/list`) but tools for this noun do not receive a `resourceLinkBlock`. |

### 7.2 Example: Minimal Opt-In

```json
"PowerShellConfiguration": {
  "CommandNames": [
    "Get-BamiTenantUser",
    "Assert-BamiTenantUser",
    "Get-BamiTenantContext"
  ],
  "EnableNounResources": true
}
```

This configuration will:
- Create resource `poshmcp://resources/bami_tenant_user` (backed by `Get-BamiTenantUser`)
- Create resource `poshmcp://resources/bami_tenant_context` (backed by `Get-BamiTenantContext`)
- Augment `Assert-BamiTenantUser` results with a `resourceLinkBlock` pointing to `poshmcp://resources/bami_tenant_user`
- Not augment `Get-BamiTenantUser` results (Get commands are not excluded, but there is no practical benefit — see OQ-3)

### 7.3 Example: Suppress One Noun

```json
"PowerShellConfiguration": {
  "EnableNounResources": true,
  "NounResourceOverrides": {
    "location": { "Disabled": true }
  }
}
```

`Get-Location` will not generate a resource and `resourceLinkBlock` will not be injected for location-related commands.

---

## 8. Edge Cases & Error Handling

### 8.1 Noun Has No Get Command

No resource is created. Tools for that noun return results without a `resourceLinkBlock`. No error is raised; a debug-level log entry is emitted during noun registry construction.

### 8.2 Get Command Fails at Resource Read Time

The `McpNounResourceHandler.HandleReadAsync` implementation wraps `Get-{Noun}` execution in a try/catch matching the existing `McpResourceHandler` pattern:

- PowerShell errors → log at Warning level → throw `McpProtocolException` with `McpErrorCode.InternalError` and message: `"Failed to read resource '{uri}': {innerMessage}"`.
- The MCP client receives a proper error response; the server does not crash.

### 8.3 Conflicting Resource Names (Same Noun, Different Modules)

First-writer-wins (§2.5). The losing command's noun is not resourceable. A warning-level log entry is emitted: `"Noun resource conflict: resource name '{resourceName}' claimed by '{winnerCommand}'; '{loserCommand}' will not generate a resource."` The doctor report (§6 of spec 011) should surface this warning in a future extension.

### 8.4 Same Noun, Different Module Prefixes

`Get-BamiUser` and `Get-AdvocacyUser` have different full nouns (`BamiUser` vs. `AdvocacyUser`) and therefore different resource names (`bami_user` vs. `advocacy_user`). No conflict.

If two modules both export `Get-User` (identical command names with module qualification), the conflict resolution in §2.5 applies: the first-discovered wins.

### 8.5 Command Name Without a Dash

`SomeCommand` has no noun. Silently skipped. No resource, no `resourceLinkBlock`.

### 8.6 NounResourceOverrides Specifies a Custom ResourceName That Conflicts

If two nouns are overridden to the same `ResourceName`, validation (analogous to `McpResourcesValidator`) should raise an error at startup and skip creating both conflicting entries, logging an error: `"NounResourceOverrides conflict: resource name '{name}' assigned to both '{noun1}' and '{noun2}'."`.

### 8.7 OOP Mode

In OOP mode, `McpNounResourceHandler` executes the `Get-{Noun}` command through `ICommandExecutor` (same as tools). The noun registry is built from the schemas returned by `DiscoverCommandsAsync`, using the `Name` field of each `RemoteToolSchema`. The full pipeline is symmetric with in-process mode.

### 8.8 Configuration Reload Race

If configuration is reloaded while a `resources/read` request is in flight using the previous `NounRegistry`, the in-flight request completes against the old registry. The new registry takes effect for subsequent requests. This is safe because `NounRegistry` is immutable.

---

## 9. Open Questions

**OQ-1 — McpServerTool wrapping surface:**  
The `McpServerTool` type from `ModelContextProtocol.Server` may be sealed or factory-constructed. The `ResourceLinkInjectorWrapper` design (§6.3) depends on being able to produce a new `McpServerTool` that delegates to an existing one. Implementer must confirm whether `McpServerTool.Create(string name, Func<...> handler)` or a subclass approach is viable before coding the wrapper.

**OQ-2 — Parameterized URI future:**  
Should a future extension allow `poshmcp://resources/bami_tenant_user/{alias}` with the alias forwarded as a `-{Alias} <value>` argument to `Get-BamiTenantUser`? The URI template RFC 6570 approach would require parameter mapping metadata. This spec defers the question; no parameterized URIs in this iteration.

**OQ-3 — Should Get commands receive a resourceLinkBlock?**  
`Get-BamiTenantUser`'s result already *is* the resource content. Injecting a `resourceLinkBlock` pointing back to itself is technically redundant, but some clients use it for caching hints. Team should decide: (a) suppress for `Get` verbs, (b) inject always when resourceable, (c) make it configurable. Default assumption in this spec: inject for **all** commands with a resourceable noun, including `Get-*`.

**OQ-4 — Integration with doctor report:**  
Should `poshmcp doctor` include a `nounResources` section listing discovered noun resources, conflicts, and suppressed nouns? This would be a natural extension of spec 011's `moduleImports` pattern. Deferred to a follow-up spec.

**OQ-5 — Should resourceLinkBlock be a separate content item or embedded in the primary JSON?**  
This spec proposes a separate `TextContent` item with a custom MIME type. An alternative is embedding `"_resourceLink": {...}` into the primary JSON object when the result is a JSON object. The separate content item approach is more composable (it doesn't break scalar or array results) but requires clients to scan the content array. Team should confirm the preferred wire shape before implementation.

---

## 10. Acceptance Criteria

These criteria are written to be directly testable by Fry.

### FR-NR-01 — Noun extraction
Given command name `Get-BamiTenantUser`, `ExtractNounFromCommandName` returns `"BamiTenantUser"`.  
Given command name `GetLocation` (no dash), `ExtractNounFromCommandName` returns `null`.  
Given command name `Get-`, `ExtractNounFromCommandName` returns `null` (empty noun after dash).

### FR-NR-02 — Resource name derivation
Given noun `BamiTenantUser`, resource name is `bami_tenant_user`.  
Given noun `Location`, resource name is `location`.  
Given noun `HTMLParser`, resource name is `html_parser`.  
Given noun `BamiTenant`, resource name is `bami_tenant`.

### FR-NR-03 — NounRegistry: only resourceable nouns registered
Given commands `["Get-BamiTenantUser", "Assert-BamiTenantUser", "Set-Foo"]` (no `Get-Foo`),  
`NounRegistry.Build` produces one entry (`bami_tenant_user`) backed by `Get-BamiTenantUser`.  
`Set-Foo`'s noun `Foo` has no entry because no `Get-Foo` is present.

### FR-NR-04 — NounRegistry: conflict detection
Given commands `["ModuleA\Get-User", "ModuleB\Get-User"]` (or two separate unqualified `Get-User` entries),  
`NounRegistry.Build` registers `user` for the first-discovered command and marks the second as conflicted.  
A warning is logged.

### FR-NR-05 — Resource list includes noun-derived resources
With `EnableNounResources = true` and commands including `Get-BamiTenantUser`,  
`resources/list` includes a resource with URI `poshmcp://resources/bami_tenant_user` and `mimeType = "application/json"`.

### FR-NR-06 — Resource read executes Get command
With `EnableNounResources = true`, a `resources/read` for `poshmcp://resources/bami_tenant_user`  
executes `Get-BamiTenantUser` with no arguments and returns its JSON-serialized output as `TextResourceContents`.

### FR-NR-07 — Resource read for unknown URI returns ResourceNotFound
A `resources/read` for `poshmcp://resources/does_not_exist` returns `McpErrorCode.ResourceNotFound`.

### FR-NR-08 — resourceLinkBlock appended to non-Get tool result
With `EnableNounResources = true`, a call to `Assert-BamiTenantUser` that succeeds  
returns a `CallToolResult` whose `Content` array includes a final item with  
`mimeType = "application/json+mcp-resource-link"` and a `text` field containing valid JSON with  
`resourceLink.uri = "poshmcp://resources/bami_tenant_user"`.

### FR-NR-09 — No resourceLinkBlock on error results
When a tool call returns `IsError = true`, no `resourceLinkBlock` is appended.

### FR-NR-10 — No resourceLinkBlock for non-resourceable nouns
When `EnableNounResources = true` and a command's noun has no `Get-{Noun}` counterpart,  
the tool result contains no `resourceLinkBlock` content item.

### FR-NR-11 — Feature is inert when disabled
When `EnableNounResources = false` (the default),  
`resources/list` returns only statically configured resources,  
tool results contain no `resourceLinkBlock` items, and no `NounRegistry` is built.

### FR-NR-12 — NounResourceOverrides: Disabled suppresses resource and block
Given `NounResourceOverrides: { "location": { "Disabled": true } }`,  
`poshmcp://resources/location` does not appear in `resources/list`  
and `Get-Location` results include no `resourceLinkBlock`.

### FR-NR-13 — NounResourceOverrides: custom URI
Given `NounResourceOverrides: { "bami_tenant_user": { "Uri": "poshmcp://resources/tenant_user" } }`,  
`resources/list` includes `poshmcp://resources/tenant_user` (not the default URI)  
and the `resourceLinkBlock` on `Assert-BamiTenantUser` contains `uri = "poshmcp://resources/tenant_user"`.

### FR-NR-14 — Static and noun-derived resources coexist
When both `McpResources.Resources[]` and `EnableNounResources = true` are configured,  
`resources/list` returns the union of both sets.  
`resources/read` resolves from the combined set without conflict.

### FR-NR-15 — OOP mode parity
With `RuntimeMode = OutOfProcess` and `EnableNounResources = true`,  
`NounRegistry.Build` uses command names from `RemoteToolSchema.Name` entries (OOP discovery output).  
`resources/read` executes `Get-{Noun}` through `ICommandExecutor`.  
FR-NR-05 through FR-NR-10 hold in OOP mode.
