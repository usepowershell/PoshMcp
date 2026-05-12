# Feature Specification: Improve MCP Tool Self-Documentation from PowerShell Help/Metadata

**Spec Number**: 010
**Feature Branch**: `010-tool-self-documentation`
**Created**: 2026-05-12
**Revised**: 2026-05-12 (Hermes; addresses Cubert pre-review and Steven's Open Question resolutions)
**Status**: Accepted (2026-05-12)
**Input**: Improve MCP tool self-documentation by reading more of what `Get-Help`, `Get-Command`, and the .NET `CommandInfo` / `ParameterMetadata` surface area already expose, so MCP clients see useful tool and parameter descriptions instead of raw syntax lines or "Parameter of type X" placeholders.

---

## Background

PoshMcp ships two execution paths for PowerShell tools, and **each derives MCP tool metadata differently**. The platform has already normalized whatever help mechanism the author chose (comment-based help, MAML, external XML) into a uniform `Get-Help` / `Get-Command` / `CommandInfo` surface. PoshMcp reads almost none of it.

### Current behavior — in-process path

In `PoshMcp.Server/McpToolFactoryV2.cs#L123-L145` (`SetParameterSetDescription`):

```csharp
var parameterSetSyntax = parameterSet.ToString();
if (!string.IsNullOrWhiteSpace(parameterSetSyntax))
{
    metadata.Description = $"{commandInfo.Name} {parameterSetSyntax}";
    ...
}
else
{
    metadata.Description = commandInfo.Name;
    ...
}
```

`Get-Help` is **never invoked**. The MCP tool description is the raw `CommandParameterSetInfo.ToString()` syntax line prefixed with the command name — for example, `Get-Process [[-Name] <string[]>] [-Module] [-FileVersionInfo]`. It looks like a man-page header, not a description.

### Current behavior — out-of-process path

In `PoshMcp.Server/PowerShell/OutOfProcess/oop-host.ps1#L763-L771` and the equivalent in `oop-host-pool.ps1#L824-L832`:

```powershell
$helpInfo = Get-Help -Name $cmd.Name -ErrorAction SilentlyContinue
if ($null -ne $helpInfo -and $null -ne $helpInfo.Synopsis) {
    $synopsis = "$($helpInfo.Synopsis)".Trim()
    if ($synopsis -and $synopsis -ne $cmd.Name) {
        $description = $synopsis
    }
}
```

Only `.Synopsis` is read, and only when it differs from the command name. Empty otherwise. The C# fallback at `PoshMcp.Server/McpToolFactoryV2.cs#L442` (`CreateRemoteCommandMetadataMapping`) then turns an empty string into the bare command name:

```csharp
Description = string.IsNullOrWhiteSpace(schema.Description) ? schema.Name : schema.Description,
```

### Current behavior — parameter descriptions (both paths)

`PoshMcp.Server/PowerShell/PowerShellSchemaGenerator.cs#L98`:

```csharp
schema["description"] = $"Parameter of type {parameterType.Name}";
```

This is **the** parameter description. Both code paths produce it. Nothing from `Get-Help`'s `parameter` blocks, `ParameterMetadata.Aliases`, `ParameterAttribute.HelpMessage`, or any other already-normalized platform metadata is consulted.

### What authors expect vs. what they get

Authors who write PowerShell with help (whatever form) reasonably expect MCP clients to see something like:

> **Get-AzContext** — Gets the metadata used to authenticate Azure Resource Manager requests.
> Parameter `-Name`: The name of the context.

What they actually get from PoshMcp depends on which execution mode the operator picked:

| Path | Tool description for `Get-AzContext` | `-Name` parameter description |
|------|--------------------------------------|-------------------------------|
| In-process (today) | `Get-AzContext [[-Name] <string>] [-DefaultProfile <IAzureContextContainer>]` (raw syntax) | `Parameter of type String` |
| Out-of-process (today) | `Gets the metadata used to authenticate Azure Resource Manager requests.` (Synopsis only) | `Parameter of type String` |
| Both paths (post-spec 010) | `Gets the metadata used to authenticate Azure Resource Manager requests.` (Synopsis; same byte sequence in both modes) | `The name of the context.` (sourced from `Get-Help` `.Parameters.parameter[name='Name'].description`) |

The same command, fronted by the same MCP server, exposes structurally different descriptions to clients today depending on a configuration flag the author cannot see. Parameter descriptions are uniformly useless in both paths. Spec 010 closes both gaps and makes the two paths byte-identical.

### Misleading documentation

`PoshMcp.Server/PowerShell/OutOfProcess/RemoteToolSchema.cs#L17`:

```csharp
/// <summary>
/// Human-readable description (from Get-Help or parameter set syntax).
/// </summary>
public string Description { get; set; } = string.Empty;
```

This XML doc is wrong on both counts: only `Synopsis` is used, never the long Description, and the OOP path never falls back to parameter set syntax — it falls back to the empty string, which downstream becomes the bare command name. Anyone reading the type to understand the contract will be misled.

### What the platform exposes that PoshMcp does not read

`Get-Help` returns a `PSCustomObject` with at minimum:

- `Name`, `Synopsis`
- `Description` — array of `MamlParaText` (long description body)
- `Parameters.parameter[]` — each entry has `name`, `description`, `type`, `required`, `position`, `aliases`, `defaultValue`
- `Examples.example[]`, `inputTypes`, `returnValues`, `relatedLinks`

`Get-Command` returns `CommandInfo` whose `.Parameters` collection is `ParameterMetadata` — each entry has `.Aliases` and `.Attributes` (including `ParameterAttribute.HelpMessage` and `ValidateSetAttribute`).

PoshMcp consumes `Synopsis` (OOP path only). Everything else is ignored.

---

## User Scenarios & Testing

### Scenario 1 (P1): Tool author sees their `.SYNOPSIS` and parameter help reach the client

**Why this priority**: This is the headline gap. Authors write help. PoshMcp throws most of it away. Today, the in-process path throws away **all** of it; the OOP path throws away everything except one line. Closing this gap is the spec's reason to exist.

**Independent Test**: Configure PoshMcp with a function whose `Get-Help` returns a non-empty `Synopsis`, a non-empty `Description`, and per-parameter descriptions. Connect an MCP client, list tools, and inspect the resulting tool definition.

**Acceptance Scenarios**:

- SC-200: Given a function whose `Get-Help` returns a non-empty `Synopsis` — the MCP tool description is the synopsis (not the syntax line, not the bare command name) regardless of execution path.
- SC-201: Given a function with `Get-Help` parameter descriptions on every parameter — every MCP parameter description is the matching help text (not `Parameter of type X`).
- SC-202: Given a function with `[Parameter(HelpMessage="...")]` on a parameter and no `Get-Help` parameter description for it — the MCP parameter description is the `HelpMessage`.
- SC-203: Given a function with `[ValidateSet("A","B","C")]` on a parameter — the MCP parameter schema includes the enum constraint AND a description that names the allowed values when no other description is available.
- SC-204: Given a function with neither `Get-Help` text nor `HelpMessage` nor `ValidateSet` for a parameter — the description falls back to a deterministic placeholder no worse than today's `Parameter of type X`.

### Scenario 2 (P2): Operator gets identical tool metadata regardless of execution mode

**Why this priority**: Operator-visible behavior must not depend on a flag the author cannot see. Two installations of PoshMcp running the same module configured the same way must expose the same MCP descriptions to clients. Any drift is a bug, not a feature.

**Independent Test**: Run the same module configuration through both `RuntimeMode: InProcess` and `RuntimeMode: OutOfProcess`. Capture the MCP `tools/list` response from each. Diff the descriptions and parameter schemas.

**Acceptance Scenarios**:

- SC-205: Given identical configuration and identical PowerShell source — the MCP tool description for any given command is byte-identical between in-process and OOP modes.
- SC-206: Given identical configuration and identical PowerShell source — the MCP parameter descriptions for any given parameter are byte-identical between in-process and OOP modes.
- SC-207: Given the doctor command runs against either mode — the resolved description-source precedence is reported per command and per parameter via the `descriptionSource` field defined in FR-583 (string literal values: `synopsis | description | syntax | name` for tools; `helpParameter | helpMessage | validateSet | typeFallback` for parameters), so operators can verify what their clients will see without connecting one.

### Scenario 3 (P3): Per-parameter description consistency across parameter sets

**Why this priority**: A single command can expose the same logical parameter (`-Path`, say) across multiple parameter sets. The platform tracks parameters per-set, but the parameter has one canonical meaning and therefore one canonical description. Clients that introspect tool variants for the same command must see the same parameter description in each variant, otherwise the tool surface looks self-contradictory. Lower priority than Scenarios 1 and 2 because it surfaces only when a command has multiple parameter sets that share parameters.

**Independent Test**: Configure a function with two parameter sets that share a parameter, give that parameter a single `.PARAMETER` block in comment-based help, list MCP tools, and inspect every tool variant generated for the command.

**Acceptance Scenarios**:

- SC-208: Given a function with a parameter present in two or more parameter sets of the same command — the MCP `inputSchema.properties.<paramName>.description` is identical across every tool variant generated for that command (covers FR-511; the same parameter MUST NOT receive different description text in different parameter sets).

---

## Edge Cases

- `Get-Help` returns `$null` because the command is in a module that has not loaded help (lazy MAML loading). Both paths must continue without throwing and fall through the precedence chain.
- `Get-Help` for a command returns auto-generated help (`Synopsis` equals command name) — the OOP path already detects this; the in-process path must apply the same rule.
- A command's `Synopsis` is multi-paragraph or contains embedded newlines; the result must be sanitized to a single line for the MCP description and length-capped.
- A command's `Description` body is megabytes long (rare but possible with auto-generated docs) — must be capped before serialization.
- A parameter has no PowerShell type (`[object]` or untyped) — the existing schema fallback applies; this spec does not change schema generation.
- A parameter set is `__AllParameterSets` and contains a parameter that is *also* present in a named set — the description used for that parameter must not differ across sets for the same command (deterministic per-parameter, not per-set).
- `Get-Help` is slow on first call per command (loads MAML on demand). Calling it once per command at discovery is acceptable; calling it per parameter is not.
- The configured PowerShell session has been stripped of `Get-Help` (e.g., `Microsoft.PowerShell.Utility` excluded). Discovery must not fail; both paths must fall through to the syntax / command-name fallback.
- `ParameterAttribute.HelpMessage` is set to a localized resource lookup string — used verbatim (PoshMcp does not perform resource resolution).
- A module is loaded into the OOP runspace but not into the in-process runspace (or vice versa) — descriptions must still be derived from the same precedence rules; differences across modes must be limited to "command not present at all," never "same command, different description."

---

## Requirements

### Functional Requirements

#### Tool description sourcing

- **FR-500**: PoshMcp MUST derive each MCP tool description from the platform-normalized PowerShell metadata using a single, documented precedence chain applied identically in both execution paths:
  1. `Get-Help <command>` `.Synopsis`, when present, non-empty, and not equal to the command name.
  2. `Get-Help <command>` `.Description` body, with `MamlParaText[]` entries joined by `"\n\n"` (paragraph separator preserved), then sanitized per FR-540 and length-capped per FR-541, when present and non-empty.
  3. `CommandParameterSetInfo.ToString()` syntax line prefixed by the command name (current in-process behavior).
  4. The bare command name.
- **FR-501**: The precedence chain in FR-500 MUST be applied per command, not per parameter set. All parameter sets of a given command MUST share the same tool description text. (Tool *names* still vary per parameter set as today; only the description body is shared.)
- **FR-502**: `Get-Help` failures (`$null`, exception, lazy-load miss) MUST be caught and treated as "no value at this precedence step." The chain MUST proceed to the next step without surfacing the failure to the MCP client.

#### Parameter description sourcing

- **FR-510**: PoshMcp MUST derive each MCP parameter description from the platform-normalized PowerShell metadata using a single, documented precedence chain applied identically in both execution paths:
  1. `Get-Help <command>` `.Parameters.parameter[]` matching by `name`, field `description` (joined per FR-500 step 2 paragraph rules and length-capped per FR-542), when present and non-empty.
  2. `ParameterMetadata.Attributes.OfType<ParameterAttribute>().HelpMessage`, when present and non-empty.
  3. A description derived from `ValidateSetAttribute` allowed values, when present. Phrasing depends on parameter shape:
     - Singleton (scalar) parameter whose type is the validated set: `"One of: A, B, C"`.
     - Array parameter whose element type is the validated set (each element is constrained to the enum): `"Each item is one of: A, B, C"`.
     - Element ordering follows the declaration order in `ValidateSetAttribute.ValidValues`.
  4. `Parameter of type <TypeName>` (current behavior, preserved as the final fallback).
- **FR-511**: The precedence chain in FR-510 MUST be applied per parameter, not per parameter set. The same parameter appearing in multiple parameter sets of the same command MUST receive the same description text.

#### Path parity

- **FR-520**: For any given command, the MCP tool description and parameter descriptions produced by the in-process path MUST be byte-identical (after FR-540 sanitization) to those produced by the out-of-process path, given identical PowerShell source loaded identically. Sanitization (FR-540) is what makes the comparison robust across the in-process console host and the OOP subprocess host with redirected I/O.
- **FR-521**: Path parity (FR-520) MUST be verified by an automated test in `PoshMcp.Tests` with the following concrete shape:
  - **Test class:** `PoshMcp.Tests/Integration/ToolDescriptionParityTests.cs` (one xUnit `[Theory]` per fixture command, plus a top-level `[Fact]` asserting fixture command count parity between modes).
  - **Fixture corpus:** `PoshMcp.Tests/Fixtures/Modules/HelpParityFixture/HelpParityFixture.psm1` — a small in-tree PowerShell module exporting at minimum 5 deterministic functions covering the precedence chain:
    1. one with `.SYNOPSIS` only (exercises FR-500 step 1),
    2. one with `.SYNOPSIS` + `.DESCRIPTION` + per-parameter `.PARAMETER` blocks (exercises FR-500 step 2 and FR-510 step 1),
    3. one with `[Parameter(HelpMessage="...")]` and no comment-based help (exercises FR-510 step 2),
    4. one with `[ValidateSet("A","B","C")]` on a scalar param and no other help (exercises FR-510 step 3 singleton),
    5. one bare function with no help, no `HelpMessage`, no `ValidateSet` (exercises FR-500 step 3/4 and FR-510 step 4 fallbacks).
    The fixture module SHOULD also include a function with `[ValidateSet]` on an array-typed parameter to exercise the FR-510 step 3 array phrasing.
  - **Equality scope:** Per-tool, scoped to the MCP `tools/list` response fields `description` (tool-level) and `inputSchema.properties.<paramName>.description` (per-parameter). The test does NOT compare the entire tool object — `name`, type, enum, mandatory, and array shape are out of scope per FR-551 and the schema-generation Non-Goal. Equality is exact string equality after FR-540 sanitization is applied to both sides.
  - **Modes within a single test session:** The test loads the fixture module through both `RuntimeMode: InProcess` and `RuntimeMode: OutOfProcess` within the same xUnit collection, so Get-Help MAML is warmed in both runspaces before assertion.
  - **Flake bound:** Before assertion, the test calls `Get-Help <commandName>` once per fixture command in each mode (pre-warm) so MAML lazy-load latency does not race the assertion.

#### Sanitization and length caps

- **FR-540**: Description text from any source MUST be normalized using the following sequence, applied identically in both execution paths:
  1. Trim leading and trailing whitespace from the overall string.
  2. Strip non-printable control characters (Unicode category `Cc`) other than the paragraph separator `\n\n` produced by FR-500 step 2.
  3. Within each paragraph (text between `\n\n` separators), collapse all runs of whitespace — spaces, tabs, single `\n`, `\r`, and `\r\n` — to a single space. Preserve the `\n\n` separators between paragraphs.
  4. Re-trim each paragraph after collapse.
  This aggressive intra-paragraph collapse is what absorbs host-specific formatting differences (`Get-Help` paragraph wrapping varies with `$Host.UI.RawUI.BufferSize`, which differs between an attached console host in the in-process path and a subprocess with redirected stdin/stdout in the OOP path) and is what makes the byte-identical guarantee in FR-520 / SC-205 / SC-206 deliverable across modes and platforms.
- **FR-541**: Tool descriptions MUST be capped at 1024 characters before being sent to the MCP client; truncation MUST end at a word boundary and append a single ellipsis (`…`, U+2026).
- **FR-542**: Parameter descriptions MUST be capped at 512 characters using the same word-boundary truncation rule as FR-541.

#### Backward compatibility

- **FR-550**: Every command whose pre-change MCP description (in either execution path) is a non-empty `Get-Help` `.Synopsis` MUST surface that exact synopsis or a strict superset (i.e., the synopsis followed by additional text from FR-500 step 2) post-change. This is verified by a snapshot mechanism:
  1. Before implementation begins, capture the current `tools/list` response for a reference module set (at minimum `Microsoft.PowerShell.Management` and the `HelpParityFixture` module from FR-521) in both `InProcess` and `OutOfProcess` modes. Store the captured JSON under `specs/010-tool-self-documentation/baseline/{mode}-tools-list.json`.
  2. After implementation, an automated assertion in `PoshMcp.Tests/Integration/ToolDescriptionParityTests.cs` (or a sibling class `ToolDescriptionRegressionTests.cs`) MUST load each baseline file and assert: for every tool whose baseline `description` is non-empty AND was sourced from `.Synopsis` (verified by re-running Get-Help against the same fixture and comparing), the post-change `description` either equals the baseline or starts with the baseline string followed by the FR-500 step 2 paragraph separator `\n\n`.
  Tools whose baseline description is the syntax-line fallback or the bare command name are NOT covered by this guarantee — they are expected to improve.
- **FR-551**: Tool *names* (the MCP tool identifier derived from command name + parameter set) MUST NOT change as part of this spec. Renaming tools is a breaking change for clients and is out of scope.

#### Documentation correctness

- **FR-560**: The XML doc on `RemoteToolSchema.Description` (`PoshMcp.Server/PowerShell/OutOfProcess/RemoteToolSchema.cs#L17`) MUST be corrected as part of this work to accurately describe the new sourcing rule. The current text ("from Get-Help or parameter set syntax") is misleading regardless of which approach is chosen.

#### Performance

- **FR-570**: `Get-Help` MUST be invoked at most once per command during a single discovery cycle. Per-parameter `Get-Help` calls are forbidden (the parameter help must be read from the per-command result).
- **FR-571**: The result of `Get-Help` for a command MAY be cached and reused across discovery cycles, keyed by the same fingerprint already used for command discovery (modules + setup hash). Cache lifetime is path-specific:
  - **In-process path:** The cache lives for the lifetime of the in-process runspace held by `PowerShellRunspaceHolder`. When the runspace is disposed (e.g., on host shutdown or explicit refresh), the cache is discarded with it.
  - **Out-of-process path:** The cache lives in the subprocess host (`oop-host.ps1` / `oop-host-pool.ps1`). When a subprocess recycles (per spec 004 lifecycle), the in-subprocess cache is lost with the process. The .NET-side executor MAY ALSO maintain a cache keyed by setup-hash that survives subprocess recycles within the same setup; if it does, it MUST invalidate when the setup-hash changes (i.e., when `setup` is called with a different module/configuration set).
- **FR-572**: Discovery throughput regression introduced by these changes MUST be measurable and bounded.
  1. Before implementation begins, the `PoshMcp.Benchmarks` cold-start scenario MUST be captured and committed to the repository at `bench-runs/run-N-pre-spec010/` (where N is the next available run number; do not overwrite `bench-runs/run-3-artifacts/` or `bench-runs/run-4-artifacts/`).
  2. After implementation, the same scenario MUST be re-run and the results committed alongside the implementation PR at `bench-runs/run-N-post-spec010/`.
  3. The 50% regression threshold is computed as `(post.coldStart.mean - pre.coldStart.mean) / pre.coldStart.mean` against the `run-N-pre-spec010` baseline specifically — NOT against any earlier bench run. A regression > 50% triggers a redesign before the spec can be marked Implemented.

#### Failure modes

- **FR-580**: `Get-Help` returning `$null`, throwing, or returning a synthetic/auto-generated help record MUST be treated as "no value." The precedence chain MUST proceed silently.
- **FR-581**: A command whose help is in a module that fails to load MUST still produce a tool description via the syntax / command-name fallback. Discovery MUST NOT fail because help cannot be resolved.
- **FR-582**: When the precedence chain for a description falls through to any step beyond the first, this MUST be observable via doctor output (per FR-583 and SC-207) so operators can identify which tools have impoverished metadata.
- **FR-583**: The doctor command MUST report the resolved description-source step per command and per parameter using a JSON field named `descriptionSource` whose value is one of the following string literals:
  - For tool descriptions: `"synopsis"` (FR-500 step 1), `"description"` (FR-500 step 2), `"syntax"` (FR-500 step 3), `"name"` (FR-500 step 4).
  - For parameter descriptions: `"helpParameter"` (FR-510 step 1), `"helpMessage"` (FR-510 step 2), `"validateSet"` (FR-510 step 3), `"typeFallback"` (FR-510 step 4).
  The field appears alongside the existing per-command and per-parameter doctor entries. The exact JSON path is: `tools[].descriptionSource` for tool-level and `tools[].parameters[].descriptionSource` for parameter-level. This naming is independent of any restructuring proposed in spec 006 (Doctor Output Restructure); if spec 006 lands first and renames the parent containers, FR-583 reuses the field name `descriptionSource` under the new container path.

#### Telemetry

- **FR-590**: PoshMcp MUST emit a metric counting how often each precedence step in FR-500 and FR-510 fires during discovery. The metric is published through the existing `PoshMcp.Server/Metrics` and `PoshMcp.Server/Observability` layer (OpenTelemetry counter), with one counter per chain:
  - Counter `poshmcp.tool_description.source` with tag `step` ∈ {`synopsis`, `description`, `syntax`, `name`} — incremented once per command per discovery cycle.
  - Counter `poshmcp.parameter_description.source` with tag `step` ∈ {`helpParameter`, `helpMessage`, `validateSet`, `typeFallback`} — incremented once per parameter per discovery cycle.
  Tag values match the `descriptionSource` literals in FR-583 exactly. Operators reading the metrics see at a glance how many tools land on the impoverished fallback without inspecting individual doctor output.

---

## Approach Options

### Option A — Read `Get-Help` in both paths, single sourcing function

Implement the FR-500 / FR-510 precedence chains once, in a shared component used by both paths. The in-process path invokes `Get-Help` against the in-process runspace; the OOP path invokes it inside the subprocess and ships the result over the existing ndjson protocol as part of `RemoteToolSchema`.

**Pros**:
- One precedence implementation, identical results by construction (FR-520 is automatic).
- Reuses what authors already write — no new conventions to teach.
- The richest data source: `Get-Help` exposes both per-command and per-parameter prose.

**Cons**:
- `Get-Help` cost on cold discovery: loads MAML on demand, can add hundreds of ms per command on first touch. Mitigated by FR-570 / FR-571 caching but real.
- `RemoteToolSchema` must be extended to carry per-parameter descriptions. This is a wire-format change in the OOP protocol — additive and backward-compatible if old fields are preserved, but real.
- Modules that ship with stripped or absent help produce the same fallback text as today; the change is invisible for them.

### Option B — Read only `CommandInfo` / `ParameterMetadata`, skip `Get-Help` entirely

Use only what `Get-Command` and the .NET reflection surface return: `CommandInfo.Definition` for command-level text where available, `ParameterAttribute.HelpMessage` for parameters, `ValidateSetAttribute` for enum-shaped descriptions, `ParameterMetadata.Aliases` for aliases. Never call `Get-Help`.

**Pros**:
- Faster: no MAML loading, no help-file resolution.
- No protocol-level changes for the OOP path beyond adding the `HelpMessage`-sourced parameter description field.
- Resilient to misconfigured help (stripped modules, lazy-load failures).

**Cons**:
- `[Parameter(HelpMessage="...")]` is rarely populated in practice — most authors put help in comment-based help, which the platform exposes through `Get-Help`. Skipping `Get-Help` means most existing modules see no per-parameter improvement.
- Tool-level descriptions get nothing better than command name + verb-noun analysis. The author's `.SYNOPSIS` is invisible to the in-process path under this option, which contradicts the user expectation captured in Scenario 1.
- Materially smaller win for the same engineering cost.

### Option C — Hybrid: `CommandInfo` for parameters, `Get-Help` for command synopsis only

Read `Get-Help` once per command for the tool description (FR-500 steps 1–2). Read parameter descriptions only from `ParameterAttribute.HelpMessage` and `ValidateSetAttribute` (FR-510 steps 2–3). Skip `Get-Help` parameter blocks entirely.

**Pros**:
- Fastest path that still respects `.SYNOPSIS`.
- Smaller protocol change for OOP (no per-parameter description shipped from the help system).
- Predictable cost: at most one `Get-Help` per command per discovery, and only the lightweight `Synopsis`/`Description` fields are read.

**Cons**:
- `HelpMessage` adoption is low. Most existing modules will continue to show `Parameter of type X` for parameters, defeating the headline reason for the spec.
- Splits the precedence story into two unrelated mechanisms (command-level reads `Get-Help`, parameter-level does not). Harder to explain, harder to debug.

### Option D — Add a `[PoshMcp.ToolDescription("...")]` attribute escape hatch

Extend Option A or C with a new attribute that authors apply to functions or parameters when they want explicit MCP-only descriptions independent of their PowerShell help. Attribute-supplied text wins over any other source in the precedence chain.

**Pros**:
- Lets authors who maintain MCP-targeted modules say exactly what they want without rewriting comment-based help.
- Useful for modules whose `Get-Help` text is auto-generated, generic, or in a language other than the MCP client's expected language.
- Optional and additive — modules that don't use it see no change.

**Cons**:
- New convention to teach; competes with comment-based help and `HelpMessage` for author attention.
- An attribute defined in PoshMcp's namespace creates a soft coupling between author modules and the PoshMcp package — modules become harder to ship independently.
- Could mask underlying help quality issues by encouraging "just paste a description into the attribute" instead of fixing the module's help.

---

## Recommendation

**Adopt Option A (read `Get-Help` in both paths via a shared sourcing function), and treat Option D as an opt-in follow-up considered only after Option A has shipped and operators report a real need.** Reject Options B and C.

**Rationale**:

1. **Option A is the only option that delivers Scenario 1 across both paths.** The user expectation is "I wrote help, my client should see it." Options B and C deliver this only for narrow subsets of authors who pre-adopted PoshMcp-specific conventions. Option A meets authors where they already are.

2. **Path parity (FR-520) falls out for free under Option A.** A single precedence implementation eliminates the entire class of "same command, different description across modes" bugs that motivated the spec in the first place.

3. **Cost is real but bounded.** `Get-Help` cold-call cost is the legitimate concern. FR-570 (one call per command per discovery) and FR-571 (caching by setup-hash, the same key already in use for OOP discovery) put a hard ceiling on it. The benchmarks harness from spec 004 already exists and FR-572 mandates re-running the cold-start scenario as a gate.

4. **Option D is the right escape hatch but the wrong starting point.** Shipping a new attribute before observing that authors want one would be premature. After Option A ships, if operators report cases where they want descriptions different from what `Get-Help` returns (localization mismatches, generated boilerplate, MCP-specific phrasing), Option D becomes the targeted answer.

5. **Option B is a strictly smaller win for the same engineering cost.** The protocol change to ship per-parameter descriptions and `HelpMessage` is required either way; skipping `Get-Help` only avoids the per-discovery cost, which FR-571 caching addresses.

6. **The misleading XML doc (FR-560) gets fixed regardless of which option is chosen.** Calling it out as a requirement, not a side effect, is intentional — it is the kind of stale doc that compounds into wrong assumptions during future code reads.

**Sequencing** (high-level — detailed step-by-step ordering belongs in `tasks.md` when this spec promotes to Accepted):

1. Capture the FR-572 pre-change `PoshMcp.Benchmarks` baseline at `bench-runs/run-N-pre-spec010/` and commit it before any implementation work begins.
2. Capture the FR-550 pre-change `tools/list` snapshots at `specs/010-tool-self-documentation/baseline/{mode}-tools-list.json` for both modes.
3. Extract a shared `IToolMetadataSource` (or equivalent) seam in C#; both `McpToolFactoryV2.SetParameterSetDescription` and the OOP fallback at `McpToolFactoryV2.CreateRemoteCommandMetadataMapping#L442` call into it.
4. Implement the FR-500 / FR-510 precedence chains and FR-540 sanitization in the in-process path; verify Scenario 1 against an in-process configuration.
5. Extend the OOP `RemoteToolSchema` and the host scripts (`oop-host.ps1`, `oop-host-pool.ps1`) to ship the additional fields (full description, per-parameter description). Keep the existing `Description` field populated for backward compatibility.
6. Wire the OOP path through the same `IToolMetadataSource`, sourcing input from the new schema fields.
7. Add the parity test (FR-521) and the regression-snapshot test (FR-550) and the per-parameter-set consistency test (SC-208).
8. Add doctor reporting of the resolved precedence step per command and per parameter (FR-582 / FR-583 / SC-207). Add the FR-590 telemetry counters in the same change.
9. Re-run `PoshMcp.Benchmarks` cold-start scenario, commit the post-change run at `bench-runs/run-N-post-spec010/`, and gate on FR-572.
10. Fix `RemoteToolSchema.cs#L17` XML doc (FR-560) with text describing the new precedence.
11. Update `docs/articles/exposing-tools.md` to document the precedence chains so authors know which platform-normalized fields PoshMcp will read.

---

## Resolved Questions

> All Open Questions raised in the Draft were resolved during revision (2026-05-12, Hermes). Recorded here for traceability.

1. **OQ-1 — Aliases.** **Resolved: out of scope.** Steven directed that aliases are not exposed in MCP tool metadata for this spec. FR-530 and FR-531 were removed; a Non-Goal entry was added. Future spec may revisit.
2. **OQ-2 — Length caps.** **Resolved: 1024 chars for tool descriptions, 512 chars for parameter descriptions, not configurable in v1.** FR-541 / FR-542 fixed at these values. (Note for Steven: I interpreted "512 is reasonable" as the parameter cap and kept 1024 for tools as the original draft proposed; if you intended 512 for both, flag and I'll re-revise.)
3. **OQ-3 — Description body assembly.** **Resolved: join `MamlParaText[]` with `"\n\n"` (paragraph separator preserved).** FR-500 step 2 and FR-540 updated. Sanitization collapses whitespace runs *within* paragraphs but preserves the `\n\n` separators between them.
4. **OQ-4 — Cache invalidation.** **Resolved per execution path (FR-571).** In-process: cache lives for the lifetime of the runspace held by `PowerShellRunspaceHolder`. Out-of-process: in-subprocess cache lives until the process recycles; an optional .NET-side cache keyed by setup-hash invalidates when the setup-hash changes.
5. **OQ-5 — Doctor field naming.** **Resolved (Hermes proposal): field name `descriptionSource`** with string-literal values per FR-583. Tool-level values: `synopsis | description | syntax | name`. Parameter-level values: `helpParameter | helpMessage | validateSet | typeFallback`. Independent of spec 006 restructuring; field name stays the same under any new container path.
6. **OQ-6 — `ValidateSet` phrasing.** **Resolved per parameter shape (FR-510 step 3).** Singleton scalar parameter constrained to the set: `"One of: A, B, C"`. Array parameter whose elements are constrained to the set: `"Each item is one of: A, B, C"`. Element ordering follows `ValidateSetAttribute.ValidValues` declaration order.
7. **OQ-7 — Telemetry on fallback frequency.** **Resolved: yes (FR-590).** Two OpenTelemetry counters added — `poshmcp.tool_description.source` and `poshmcp.parameter_description.source` — each tagged with the resolved precedence step. Tag vocabulary matches the FR-583 `descriptionSource` literals exactly so doctor output and metrics use the same names.

---

## Non-Goals

- **Aliases.** Command and parameter aliases are NOT exposed in MCP tool metadata as part of this spec. `CommandInfo` aliases (`Get-ChildItem` → `gci`, `dir`, `ls`) and `ParameterMetadata.Aliases` (`-Path` → `-PSPath`, `-LP`) are intentionally not surfaced. Adding them is a candidate for a follow-up spec; this spec's wire-format and metadata model do not reserve fields for them.
- **Authoring help.** This spec does NOT prescribe whether authors should write comment-based help, MAML, external XML, or any other help mechanism. The platform normalizes all of them through `Get-Help` / `Get-Command`; the spec is about reading more of that already-normalized surface.
- **New help formats.** Not introducing a PoshMcp-specific help DSL, frontmatter convention, or sidecar file format.
- **MCP protocol changes.** Not proposing any change to the MCP specification itself or to the `ModelContextProtocol.Server` SDK. All work fits within existing tool/parameter description and schema fields.
- **New authoring attributes (default).** Option D (`[PoshMcp.ToolDescription]`) is not part of the recommended scope. It is named as a future option only; this spec does not introduce it.
- **Schema generation rework.** `PowerShellSchemaGenerator` continues to generate JSON Schema as it does today. Only the `description` field on parameters changes; type, enum, mandatory, and array shape are out of scope.
- **Tool naming changes.** Tool identifiers (command + parameter set name) do not change. Renaming tools breaks clients and is out of scope (FR-551).
- **Examples / `inputTypes` / `relatedLinks`.** `Get-Help` exposes much more than synopsis and parameter descriptions. Surfacing examples or related links into MCP is a follow-up spec, not this one.
- **Localization / language detection.** PoshMcp uses whatever language `Get-Help` returns. Translating, detecting, or matching to client locale is out of scope.

---

## Success Criteria

- Both execution paths produce byte-identical MCP tool descriptions and parameter descriptions (after FR-540 sanitization) for the same configured command (FR-520, verified by the FR-521 parity test in `PoshMcp.Tests/Integration/ToolDescriptionParityTests.cs` against the `HelpParityFixture` corpus).
- For at least one well-known module configured against PoshMcp (e.g., `Microsoft.PowerShell.Management`), MCP `tools/list` shows real `Get-Help` synopses as tool descriptions and real per-parameter help text as parameter descriptions, in both modes.
- The XML doc on `RemoteToolSchema.Description` accurately describes the new precedence (FR-560).
- `PoshMcp.Benchmarks` cold-start scenario shows < 50% regression vs. the `bench-runs/run-N-pre-spec010/` baseline (FR-572), with the post-change run committed at `bench-runs/run-N-post-spec010/`.
- Doctor output identifies the resolved description-source step per command and per parameter via the `descriptionSource` field (FR-582 / FR-583 / SC-207).
- OpenTelemetry counters `poshmcp.tool_description.source` and `poshmcp.parameter_description.source` are emitted with `step` tags matching the FR-583 vocabulary (FR-590).
- Every command whose pre-change description was a non-empty `Get-Help` `.Synopsis` continues to surface that exact synopsis or a strict superset (FR-550, verified by snapshot assertion against `specs/010-tool-self-documentation/baseline/`).
- A parameter shared by multiple parameter sets of the same command receives identical description text in every generated tool variant (SC-208 / FR-511).
- No tool identifier changes (FR-551).
