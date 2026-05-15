# Feature Specification: Doctor diagnostics for module-imported and pattern-matched tools

**Spec Number**: 011
**Feature Branch**: `011-doctor-module-imports`
**Created**: 2026-05-13
**Author**: Farnsworth (Lead/Architect)
**Status**: Draft
**Issue**: [#263 — doctor: module-imported tools have no validation/visibility section](https://github.com/usepowershell/PoshMcp/issues/263)
**Related Specs**: 006 (Doctor Output Restructure), 010 (Tool Self-Documentation)

---

## Background

`poshmcp doctor` validates the tools that PoshMcp will expose to MCP clients. Today, the validation pass walks **only `CommandNames`** (via `BuildConfiguredFunctionStatus(config.GetEffectiveCommandNames(), …)` at `PoshMcp.Server/Diagnostics/DoctorService.cs#L105`). PowerShell commands brought in via the **other two import sources** — `Modules` and `IncludePatterns` — are invisible to validation. The doctor will say a configuration is `healthy` even when 8 of 9 tools were imported by sources that nobody checked.

### What the three import sources actually do

`McpToolFactoryV2.GetAvailableCommandsWithMetadata` (`PoshMcp.Server/McpToolFactoryV2.cs#L972-L1066`) processes each source independently and merges their results:

| Source | Discovery API | Merge semantics |
|---|---|---|
| `CommandNames` (+ legacy `FunctionNames`) | `Get-Command -Name <each>` | Per-name lookup; missing names log a warning |
| `Modules` | `Get-Command -Module <each>` | All commands from the module; new commands deduped by name against `CommandNames` |
| `IncludePatterns` | If prior commands exist: filter the existing set. If empty: `Get-Command -Name <pattern>` for each pattern as global discovery | When acting as a filter, drops everything that does not match. When acting as global discovery, adds all matches. |
| `ExcludePatterns` | Final wildcard filter over the merged set | Drops matches; never adds |

Each source has independent failure modes (module missing, module loaded but produces zero commands, pattern matched zero, signature discovery threw on a specific command) and independent merge effects. None of these are surfaced today.

### Concrete example (Steven's deployment, 2026-05-13)

```jsonc
"PowerShellConfiguration": {
  "CommandNames":     [ "Get-BamiTenantConfiguration" ],
  "Modules":          [ "AzureCoreDevRel" ],
  "IncludePatterns":  [ "*" ],
  "ExcludePatterns":  []
}
```

Live deployment loads 9 tools. `poshmcp doctor` reports:

```
✓ 1/1 configured functions found
tools discovered: 9
- Get-BamiTenantConfiguration → get_bami_tenant_configuration [✓ FOUND] matched: get_bami_tenant_configuration
[discovered MCP tools (9)]
  - assert_tenant_group_member
  - …
```

The other 8 tools appear in the discovered list but the operator has no way to answer:

- Did `AzureCoreDevRel` load?
- Which 8 of its commands were exposed, and which (if any) were dropped by `Get-Command -Name *` semantics, signature discovery failure, or schema generation failure?
- Did `IncludePatterns: ["*"]` widen the set or narrow it (it acts as a filter here, not discovery, because `Modules` already populated `commands`)?
- If a module typo silently produced zero commands, would the doctor catch it?

Today: no, no, no, no. The doctor's `errors` status is computed from `ConfiguredFunctionsMissing > 0`, and `ConfiguredFunctionsMissing` only counts `CommandNames`. A configuration that imports zero tools from a misnamed module is reported `healthy`.

### Out-of-process parity

The OOP host (`oop-host.ps1` / `oop-host-pool.ps1`) performs its own command discovery from the serialized `modules`, `commandNames`, `includePatterns`, `excludePatterns` payload (see `OutOfProcessCommandExecutor.cs#L139-L142`, `OutOfProcessSubprocessPool.cs#L340-L343`). The same three import sources, with discovery semantics that the in-process path defines but the OOP host re-implements in PowerShell. Spec 010 made tool/parameter descriptions byte-identical across paths; this spec applies the same constraint to module-import diagnostics — the doctor report must reflect the same per-module/per-pattern outcomes regardless of `RuntimeMode`.

---

## User Scenarios & Testing

### Scenario 1 (P1): Operator validates a module loaded and produced the expected tools

**Why this priority**: This is the headline gap. The whole point of `Modules` is "expose everything from this module"; the operator needs to confirm the module loaded and see what was exposed.

**Acceptance**:

- Given `Modules: ["AzureCoreDevRel"]` and the module is installed,
  when the operator runs `poshmcp doctor`,
  then a `Module Imports` section reports:
  - module name, found = true, version, install location (path of the module manifest),
  - count of commands the module contributed to the discovered set,
  - per-command list with each tool's source-of-record (module vs. promoted-from-CommandNames vs. dropped-by-exclude).
- Given `Modules: ["AzureCoreDevRelTypo"]` and the module is **not** installed,
  the report flags it as an error, contributes zero to the tool count, and the overall `summary.status` becomes `errors`.
- Given a module that loads but produces zero commands matching the post-pattern set,
  the report flags it as a warning with a hint pointing at the offending pattern.

### Scenario 2 (P1): Operator validates include/exclude pattern outcomes

**Why this priority**: Patterns silently change tool counts, including the surprising `["*"]`-as-filter vs. `["*"]`-as-discovery behavior. Operators need to see what each pattern actually did.

**Acceptance**:

- Given `IncludePatterns: ["Get-*"]` applied as a filter against a populated set,
  the report shows: pattern, role = `filter`, retained N of M, dropped M-N.
- Given `IncludePatterns: ["Get-*"]` applied as global discovery (no `CommandNames`, no `Modules`),
  the report shows: pattern, role = `discovery`, matched N commands.
- Given an `IncludePatterns` entry that matched zero commands in either role,
  the report flags a warning that the pattern is dead code.
- `ExcludePatterns` get the same treatment (role = `exclude`, dropped N of M).

### Scenario 3 (P2): Doctor surfaces discovery failures (PowerShell errors during `Get-Command`)

**Why this priority**: `GetCommandsByModule` and `GetCommandsByName` already log errors but the doctor does not see them. An `Import-Module` collision, a binary module that fails to bind, or `Get-Command -Module` throwing all silently degrade the tool surface.

**Acceptance**:

- When `Get-Command -Name <pattern>` writes to the error stream,
  the report attributes the error to the source (module/pattern/name), captures the error record's category and message, and raises the section status to `errors`.
- When a per-command signature discovery failure prevents schema generation,
  the report lists the affected command under the originating source with a "discovered but not exposed" status.

### Scenario 4 (P2): OOP and in-process produce identical Module Imports diagnostics

**Why this priority**: Spec 010's parity guarantee for descriptions must extend to module diagnostics. Operators should not need to know which `RuntimeMode` they are in to read the doctor report.

**Acceptance**:

- For a fixed configuration, the `moduleImports` section JSON is byte-identical across `RuntimeMode: InProcess` and `RuntimeMode: OutOfProcess` (modulo serialized timestamps and version strings, which live in other sections).

### Scenario 5 (P3): Existing `configuredFunctionStatus` semantics unchanged

**Why this priority**: The `CommandNames` validation surface (and CI integrations that grep `ConfiguredFunctionsMissing > 0`) must keep working unchanged. The new section is additive.

**Acceptance**:

- A configuration that uses only `CommandNames` produces the same `functionsTools.configuredFunctionStatus` and the same overall `summary.status` it produces today.
- The new `moduleImports` section is present but reports `modules: []`, `patterns: []`, `tools: []` (or is omitted; see Open Question 5).

---

## Functional Requirements

**FR-263-1** — A new `moduleImports` section MUST be added to `DoctorReport` and serialized as the JSON property `moduleImports`. It MUST be additive; existing sections (`functionsTools`, `summary`, etc.) keep their current shape and meaning.

**FR-263-2** — The `moduleImports.modules` array MUST contain one entry per `PowerShellConfiguration.Modules` entry, with:

- `name` (configured string, verbatim)
- `found` (bool — module resolved by `Get-Module -ListAvailable` or equivalent)
- `version` (string or null)
- `path` (module manifest or root path, null if not found)
- `contributedToolCount` (int — commands this module contributed to the final discovered set)
- `contributedToolNames` (string[] — MCP tool names sourced from this module after dedup and pattern filtering)
- `status` (`"ok" | "warning" | "error"`)
- `diagnostic` (string or null — error message when status ≠ ok)

**FR-263-3** — The `moduleImports.patterns` array MUST contain one entry per pattern across `IncludePatterns` and `ExcludePatterns`, with:

- `pattern` (string, verbatim)
- `kind` (`"include" | "exclude"`)
- `role` (`"filter" | "discovery" | "exclude"` — which branch the pattern executed in)
- `matchedCount` (int — commands the pattern affected; meaning depends on role)
- `status` (`"ok" | "warning"`) — `warning` when `matchedCount == 0` (dead pattern)
- `diagnostic` (string or null)

**FR-263-4** — The `moduleImports.tools` array MUST contain one entry per discovered MCP tool, mapping it back to its origin:

- `toolName` (MCP tool name, e.g. `get_az_context`)
- `commandName` (PowerShell command name, e.g. `Get-AzContext`)
- `source` (`"commandName" | "module" | "pattern"`)
- `sourceDetail` (the configured string that produced it — name for `commandName`, module name for `module`, pattern for `pattern`)
- `disposition` (`"exposed" | "filteredOut" | "discoveryFailed"`)
- `diagnostic` (string or null)

**FR-263-5** — `DoctorReport.ComputeStatus` MUST treat `moduleImports.modules[].status == "error"` as `errors` and `moduleImports.patterns[].status == "warning"` (or `modules[].status == "warning"`) as `warnings`. Existing rules (`ConfiguredFunctionsMissing > 0`, etc.) are preserved.

**FR-263-6** — `DoctorTextRenderer` MUST render a `Module Imports` section between `Functions/Tools` and `MCP Definitions`. The renderer MUST be omitted (zero output, no header) when all three arrays are empty.

**FR-263-7** — The new validation pass MUST execute against the same `PowerShellConfiguration` and the same tool list that the existing `BuildConfiguredFunctionStatus` consumes (`tools` from `discoverToolsFunc` in `BuildDoctorReportForCliAsync`). It MUST NOT run a second `Get-Command` invocation; it derives module/pattern attribution from the already-discovered `CommandInfo` set plus the configuration. (Rationale: avoids doubling startup cost and avoids drift between what doctor reports and what the runtime actually exposes.)

**FR-263-8** — Module attribution for each discovered command MUST be sourced from `CommandInfo.ModuleName` / `CommandInfo.Source`. The `tools[].source` field is `"commandName"` when the command name appears in `config.GetEffectiveCommandNames()`, else `"module"` when the command's `ModuleName` matches a configured module, else `"pattern"`.

**FR-263-9** — The `moduleImports.tools[].source` discriminator MUST resolve in this priority order: `commandName` > `module` > `pattern`. (A command listed in both `CommandNames` and exposed by `Modules` is attributed to `commandName`, matching the dedup order in `GetAvailableCommandsWithMetadata` at `McpToolFactoryV2.cs#L1009-L1010`.)

**FR-263-10** — The `moduleImports.modules[].found / version / path` fields MUST be populated by a single `Get-Module -ListAvailable -Name <name>` call per module, executed in the same runspace context as discovery. (One `Get-Module` per module; no per-command lookups.)

**FR-263-11** — When `RuntimeMode == OutOfProcess`, the OOP host MUST surface the same per-module / per-pattern attribution data over the wire so the in-process side can populate `moduleImports`. The serialized payload SHALL extend `RemoteToolSchema` (or a sibling type) with `sourceModule`, `sourcePattern`, and `sourceDetail` fields. (Implementation split: see Follow-up Issues.)

**FR-263-12** — Tests MUST cover, at minimum:

1. `Modules: ["KnownModule"]` produces a populated `moduleImports.modules[0]` with correct `found / version / contributedToolCount`.
2. `Modules: ["NotARealModule"]` produces a `status: "error"` entry and flips overall `summary.status` to `errors`.
3. `IncludePatterns: ["Get-*"]` over a populated set reports `role: "filter"`.
4. `IncludePatterns: ["Get-*"]` with no `CommandNames` and no `Modules` reports `role: "discovery"`.
5. `IncludePatterns: ["Nope-*"]` against any prior set reports `status: "warning"` (dead pattern).
6. `ExcludePatterns: ["*-Service"]` reports `role: "exclude"` with a non-zero `matchedCount`.
7. Mixed configuration (`CommandNames` + `Modules` + `IncludePatterns`) attributes each tool exactly once with the correct source priority.
8. A `CommandNames`-only configuration produces an empty `moduleImports.modules` and `moduleImports.patterns` and the renderer omits the section.
9. All new tests carry `[Trait("Category", ...)]` per Spec 009.

**FR-263-13** — The `moduleImports.modules[].diagnostic` field MUST sanitize PowerShell error records to strip absolute paths outside the module install root, matching the existing log sanitization pattern used in spec 004 / CWE-117 hardening.

---

## Success Criteria

**SC-263-1** — Operator running `poshmcp doctor` against the AdvocacyBami live deployment (1 `CommandName` + `Modules: ["AzureCoreDevRel"]` + `IncludePatterns: ["*"]`) sees: module loaded, version, path, all 8 contributed tool names, and zero warnings. Total doctor runtime increases by ≤ 50 ms versus today's report on the same configuration.

**SC-263-2** — Operator running `poshmcp doctor` against a configuration with a misnamed module sees `summary.status: "errors"` and a clear per-module diagnostic, without needing to set `--log-level Trace`.

**SC-263-3** — `moduleImports` JSON is byte-identical across `RuntimeMode: InProcess` and `RuntimeMode: OutOfProcess` for the same configuration (excluding fields documented as runtime-mode-specific elsewhere in the report).

**SC-263-4** — Existing `functionsTools.configuredFunctionStatus` JSON for any configuration that uses only `CommandNames` is byte-identical to today's output. (Backward compatibility gate.)

---

## Open Questions

**OQ-263-1** — Does `moduleImports` belong in the existing `functionsTools` section or as a top-level sibling? **Proposed**: top-level sibling. Rationale: `functionsTools` already serializes a long `tools` list (Spec 010); nesting another array under it makes the JSON harder to consume. Top-level keeps cardinality flat.

**OQ-263-2** — When `Modules` is empty AND `IncludePatterns` is empty, should the renderer omit the section or print "no module-based imports configured"? **Proposed**: omit. Rationale: the report is already long; an empty section is noise.

**OQ-263-3** — Should `moduleImports.tools` deduplicate against `functionsTools.tools` (Spec 010), or repeat the entries with module attribution added? **Proposed**: do not deduplicate — they are different views. `functionsTools.tools` is description-source attribution; `moduleImports.tools` is import-source attribution. Operators reading either should see the full picture.

**OQ-263-4** — Does the attribution heuristic in FR-263-9 hold when `CommandOverrides` rename a tool? **Proposed**: yes; attribution is by underlying PowerShell command name, not MCP tool name. The renamed tool name flows into `tools[].toolName`; `commandName` keeps the PowerShell identity.

**OQ-263-5** — Is the OOP wire-format extension in FR-263-11 a breaking change for older `oop-host.ps1` versions? **Proposed**: additive fields only; older hosts that omit the new fields cause `moduleImports.tools[].source` to fall back to `"unknown"` with a one-time warning. Hard follow-up: Hermes confirms during implementation.

---

## Out of Scope

- **No new configuration knobs.** This spec adds diagnostic surfacing only; it does not change which tools are exposed or how discovery merges sources.
- **No fix to `IncludePatterns: ["*"]` filter-vs-discovery surprise.** That behavior is a separate ergonomics issue; this spec only makes the current behavior visible. If we want to change it, file a separate issue.
- **No retroactive changes to `functionsTools`.** Existing field names, types, and computation rules stay.
- **No new `--filter` flag for the doctor.** The new section is always present (when applicable); operators who want a slim view use `--format json` and project the fields they want.
- **No PowerShell-side autofix suggestions.** "Did you mean `AzureCoreDevRel`?" Levenshtein-style hints are out of scope; the diagnostic captures the literal error.

---

## Implementation Plan (delegated to follow-up issues)

This spec ships as a design-only PR. Implementation splits cleanly along agent expertise:

1. **C# wiring + JSON contract + tests** — Bender. Adds `ModuleImportsSection` record, wires `BuildModuleImportsSection` into `BuildDoctorReportForCliAsync`, extends `DoctorReport.ComputeStatus`, adds renderer block, lands the eight test cases from FR-263-12. (See Follow-up Issue #1.)
2. **PowerShell module-discovery semantics + OOP wire-format extension** — Hermes. Adds the per-module `Get-Module -ListAvailable` lookup (in-process and OOP), extends `RemoteToolSchema` with attribution fields, ensures byte-parity (SC-263-3). (See Follow-up Issue #2.)
3. **Text renderer formatting + visual snapshot tests** — Amy (optional separable). Format the section with the existing two-space-indent + `→` arrow conventions, add snapshot tests against the AdvocacyBami fixture. Can be folded into Issue #1 if scope is small. (See Follow-up Issue #3.)

---

## Backward-compatibility contract

Anything that today reads:

- `functionsTools.configuredFunctionsMissing` to gate CI: unchanged. `Modules` and `IncludePatterns` failures surface in `moduleImports`, not here.
- `summary.status` to gate health: **may flip from `healthy` to `errors`** for configurations with broken `Modules` or dead `IncludePatterns`. This is the intended fix. Document in CHANGELOG under "Breaking — diagnostics".
- The text renderer output: gains a new section between `Functions/Tools` and `MCP Definitions`. Anyone parsing the text format (please don't) needs to handle it.

---

## References

- Issue #263 — doctor: module-imported tools have no validation/visibility section
- `PoshMcp.Server/Diagnostics/DoctorService.cs#L105` — current `BuildConfiguredFunctionStatus` call site
- `PoshMcp.Server/Diagnostics/DoctorReport.cs#L357` — `FunctionsToolsSection` record
- `PoshMcp.Server/McpToolFactoryV2.cs#L972-L1066` — `GetAvailableCommandsWithMetadata` (the discovery pipeline this spec mirrors for diagnostics)
- `PoshMcp.Server/PowerShell/PowerShellConfiguration.cs#L60-L88` — `CommandNames`, `Modules`, `IncludePatterns`, `ExcludePatterns`
- `PoshMcp.Server/PowerShell/OutOfProcess/OutOfProcessCommandExecutor.cs#L139-L142` — OOP discovery payload
- specs/006-doctor-output-restructure — original `DoctorReport` architecture this spec extends
- specs/010-tool-self-documentation — sets the in-process / OOP byte-parity precedent
- specs/009-test-suite-consistency — `[Trait("Category", ...)]` requirement for new tests
