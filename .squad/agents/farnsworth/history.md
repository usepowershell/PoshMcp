# Farnsworth — Lead/Architect — Work History

## Project Context
**Project:** PoshMcp — Model Context Protocol (MCP) server for PowerShell
**Tech Stack:** .NET 10, C#, PowerShell SDK, OpenTelemetry, ASP.NET Core, xUnit
**Primary User:** Steven Murawski

## Pre-2026-05-12 Summary (archived to history-archive.md on 2026-05-13)
**Pre-2026-05-02:** see history-archive.md (Spec 003/004/005 restructure; PR #130 nullable MimeType; Spec 006 milestone #3 with 27 issues; PR #167 Doctor Output Restructure approved).
- 2026-05-02: Reviewed PR #184 (Program.cs refactor / spec 002 prompts+resources).
- 2026-05-06: Authored SECURITY.md (private vuln reporting + supported-versions). Reviewed Spec 004 wave: PR #187 (experiment plan), PR #200 (Bender Option B / ProcessPool), PR #201 (Hermes Option A / Pool — chosen as enum baseline), PR #202 (Fry harness wiring; surfaced ConvertTo-Json shadowed-member bug → #203/#204), PR #204 (Bender fix), PR #205 (Hermes findings). Recommended Pool as default with cancellation propagation as a hard gate. Security alerts triage: 25 log-forging + 1 missing workflow perms — defined LogSanitizer pattern at call-site (not enricher) to clear CodeQL.
- 2026-05-06: PR #207 (Bender — cancellation across Single/Pool/ProcessPool) approved; both #196 hard gates (custom PSHost from #201, cancellation from #207) closed.
- 2026-05-07: PR #210 (Leela — OOP docs + samples audit) reviewed. v0.11.0 release shipped (Pool default flip).
**Patterns to remember:**
- EMU gh pr review blocked from this account; use gh pr comment (not a formal approval — Steven must convert if branch protection requires).
- When a default flip is questioned, audit ALL direct construction sites of the affected type (grep for 
ew TypeName).
- Benchmark harnesses surfacing concurrency races on day one are doing their job — land harness, file race separately, don't hold harness PR hostage.
- PowerShell ConvertTo-Json failures with ArgumentException: ... Key: <name> → suspect CLR member shadowing on the input type before suspecting a race.
- Surfacing hardcoded values (e.g. 30s timeout) in doctor reports — even before they're config knobs — signposts the eventual configuration surface.
- Spot-check 2-3 headline numbers in docs+data PRs against source tables (catches arithmetic + rounding inversions).

## Pre-2026-05-02 Summary (archived to history-archive.md on 2026-05-06)
- 2026-04-17: Restructured loose specs (003 prompts, 004 OOP, 005 large-result) into speckit format; FR-035..FR-064, SC-016..SC-030.
- 2026-04-18: Approved PR #130 (MimeType nullable fix) — pattern: model nullable + handler-applied default preserves validator signal.
- 2026-04-20: Filed Spec 006 (Doctor Output Restructure) milestone #3 with 27 issues T001-T027 (#140-#166) split Bender/Fry.
- 2026-07-15: MCP Resources/Prompts spec (002) authored; 4 team skills extracted from PRs #92-#96.
- 2026-07-18: Triaged Issue #131 (stdio logging to file) — Serilog file sink, ClearProviders unconditional in stdio mode, 3-tier resolution (CLI > env > config). Approved PRs #132 (stdio logging), #134 (docker buildx context fix).
- 2026-07-28: Approved PR #167 (Spec 006 Doctor Output Restructure) — DoctorReport records + DoctorTextRenderer architecture.
- See history-archive.md for full entries.

### 2026-05-07 — Reviewed PR #210 (Leela — OOP docs + samples audit, branch squad/oop-docs-samples-audit)

**Verdict:** APPROVE with one non-blocking framing nit. Posted via gh pr comment (#issuecomment-4396923714) — gh pr comment now works after switching back to usepowershell account. Architectural angle only; Cubert handled fact-checking in parallel.

**Mental-model assessment:** Two-entry-point split (brief in configuration.md, deep-dive in advanced.md) is the right structure — avoids duplication, lets operators land on either article and discover the other. Three-mode taxonomy table in advanced.md delivers explicit "when to use" + sizing + per-mode cancellation contract + doctor pointer in the right order. Decision narrative (Pool wins warm throughput ~4.86×, ProcessPool opt-in for trust/tail, Single legacy/bisect) matches spec 004 study and #208 default-flip rationale exactly.

**Sample-pick judgment — both correct, both well-justified in examples/README.md:**
- advanced.json → Pool with SubprocessRunspacePoolSize:0 (auto-tune): correct for heavy-Az + concurrent throughput case. Auto-tune is the right default for a copy-paste sample.
- tenant.json → ProcessPool (size 4, min healthy 2): correct for trust-boundary case. README rationale names the tradeoff explicitly ("trust boundaries between callers matter more than peak throughput") — multi-tenant is exactly where peak throughput is the wrong optimization target. Getting this right in the SAMPLE (not just the docs) is what made #210 more than a documentation update.

**Coherence with #208 — clean.** RuntimeMode correctly described as InProcess/OutOfProcess (the azure-integration.md "sync/async" description was a real bug; fixed). SubprocessHostMode is presented as a primary configuration concept rather than a tuning knob — correct framing for post-default-flip docs. Cancellation documented as a contract per mode, not a footnote — correct framing because cancellation is what made the flip safe.

**Operator completeness:** poshmcp doctor surfaced in advanced.md ("reports the resolved host mode, effective pool sizes, host-script path, clamp warnings"). Adequate — answers the verify-my-config question without burying or over-emphasizing.

**One framing gap (non-blocking):** advanced.md Cancellation section says of Single mode: *"the historical timeout-and-restart behavior applies."* This UNDERSELLS what Single does post-#207 — the SingleDispatcher worker-thread pattern (BlockingCollection + ConcurrentDictionary registry, mirroring PoolDispatcher shape) supports the same cooperative soft-cancel contract as Pool/ProcessPool with per-request timeout as backstop. As written, an operator could read this as "Single mode does not support cooperative cancellation," which would be inaccurate AND would undersell why the default flip became safe across all three modes simultaneously. Suggested follow-up phrasing: *"Single: cooperative cancellation via the dispatcher worker; the per-request timeout acts as the backstop and recycles the host on timeout."* One line, follow-up PR.

**Pattern noted:** When a docs PR ships alongside an engineering decision PR, the per-mode contract narrative is where framing drift hides. Cancellation contract was the strongest place to look because it's the gate that made the default flip safe — any underselling there undersells the whole flip rationale. Sample-pick rationale was the second strongest, because the wrong tradeoff narrative in a sample propagates to operators who copy the sample without reading the docs.

**EMU note:** gh pr comment from usepowershell account works (now properly switched). Coordinator's task setup pre-switched the account so no friction this time. Comments still do NOT count as formal GitHub approvals for branch protection.

### 2026-05-07: v0.11.0 release shipped (cross-agent note from Scribe)
Your work landed in v0.11.0 (csproj 0.10.0 → 0.11.0, CHANGELOG entry, release notes at docs/release-notes/0.11.0.md). The release narrative credits the OOP maturity wave: Pool default flip (#196/#208), cancellation propagation across all modes (#207), benchmarks harness + findings (#193/#194/#195/#205), OOP host extraction (#190/#198), bug fixes (#203/#189), CWE-117 log-injection hardening, minimum workflow permissions, and SECURITY.md. Tag/push deferred to Steven.

## Learnings
- 2026-05-12: Authored specs/009-test-suite-consistency/spec.md. Full suite flake (~668 tests, 6min) traced to OS-level resource contention (port reuse, pwsh handle leak, temp-dir collisions) — parallelization is already off. Recommended trait-based phasing (Option 1) + per-test resource hygiene audit (Option 3) as first step; deferred project split (Option 2) and drain fixtures (Option 4) until measured. Hard user requirement: unit tier must run in <60s, no subprocesses, no ports.

### 2026-05-12 — Spec 009 accepted, milestone + 10 issues filed
- Resolved 7 open questions on specs/009-test-suite-consistency/spec.md; status flipped Proposed → Accepted (2026-05-12).
- New FRs encoding resolutions: FR-416 (Functional→Integration rule, OQ-3), FR-417 (untagged → default bucket, not Unit, OQ-2), FR-418 (dedicated CI flake-rate step N=5, OQ-7), FR-419 (reference machine = maintainer's primary dev machine, OQ-1).
- New Non-Goals: Azure category in CI (OQ-4 deferred), Option 4 cooldown duration (OQ-6 blocked on OQ-4), analyzer for Trait presence (OQ-5 dropped).
- Milestone: "Spec 009: Test Suite Consistency" — creation BLOCKED by EMU policy (gh api POST → HTTP 404, same pattern as gh issue create blocked since 2026-05-06). Staged at C:\Users\stmuraws\AppData\Local\Temp\poshmcp-spec009 with create-all.ps1 for manual run from non-EMU context.
- 10 issues drafted (bodies in staging dir):
  1. Add Category traits to all tests (FR-400, FR-406, FR-417)
  2. Reclassify misfiled Unit/* (FR-401/402/403/414)
  3. Document per-category local commands TESTING.md (FR-408)
  4. CI: split into category-scoped phases (FR-409)
  5. CI: dedicated flake-rate step (FR-418 / OQ-7)
  6. Hygiene: dynamic ports (FR-411)
  7. Hygiene: pwsh subprocess teardown (FR-412)
  8. Hygiene: unique temp dirs (FR-403/410)
  9. Functional→Integration rule applied (FR-416 / OQ-3)
  10. Unit-tier acceptance gate <60s 5x clean (SC-100/101, FR-404/405) — blocked by #1, #2, #6, #7, #8.
- Trade-offs accepted: permissive default bucket (no strict analyzer); hard Functional rule over case-by-case; Azure CI deferred; drain fixture deferred behind hygiene-audit results.
- Pattern: EMU blocks all repo-modifying gh API calls (issues, milestones, PR reviews). For multi-resource setup, draft all bodies to a temp dir + create-all.ps1 script so user can fire them from a non-EMU shell in one shot.

### 2026-05-12: Authored Spec 010 — Improve MCP tool self-documentation

**Path:** specs/010-tool-self-documentation/spec.md (Status: Draft)
**Co-author:** Hermes (technical baseline in his history.md, 2026-05-12 entry)
**Scope per Brady:** Read more of what `Get-Help`/`Get-Command`/CommandInfo already expose. NOT about comment-based vs MAML vs XML — platform normalizes all of those.

**Grounding facts verified before drafting:**
- McpToolFactoryV2.cs#L123-145: in-process description = `"{name} {paramSetSyntax}"`, never calls Get-Help.
- McpToolFactoryV2.cs#L442: OOP fallback = bare command name when remote schema description is empty.
- PowerShellSchemaGenerator.cs#L98: parameter description literal `"Parameter of type {Name}"` for both paths.
- oop-host.ps1#L763-771 and oop-host-pool.ps1#L824-832: only Synopsis read from Get-Help, fallback empty string.
- RemoteToolSchema.cs#L17 XML doc claims "from Get-Help or parameter set syntax" — wrong on both counts (called out as FR-560).

**Key spec elements:**
- FR-500 / FR-510: precedence chains for tool desc (Synopsis → Description → syntax → name) and param desc (Get-Help param block → ParameterAttribute.HelpMessage → ValidateSet hint → "Parameter of type X").
- FR-520 / FR-521: byte-identical descriptions across in-process and OOP modes, automated parity test.
- FR-530 / FR-531: command and parameter aliases exposed.
- FR-540..542: sanitization and length caps (suggested 1024/512).
- FR-550 / FR-551: no description regression, no tool identifier rename.
- FR-560: fix the misleading XML doc.
- FR-570..572: one Get-Help call per command per discovery, cache by setup-hash, gate on <50% cold-start regression in PoshMcp.Benchmarks.
- FR-580..582: silent fallback on Get-Help failures, doctor reports resolved precedence step.

**Approach options:** A (Get-Help in both paths via shared sourcing function — RECOMMENDED), B (CommandInfo only, no Get-Help — rejected: misses Scenario 1 for most modules), C (hybrid — rejected: splits the precedence story), D (PoshMcp.ToolDescription attribute — escape hatch, deferred until A ships and need is observed).

**Why Option A wins:** only option that delivers Scenario 1 across both paths; FR-520 parity falls out for free; cost concern addressed by FR-570/571 caching keyed by the same setup-hash already in use for OOP discovery.

**Sequencing recommended:** extract IToolMetadataSource shared seam → in-process precedence first (no protocol change) → extend RemoteToolSchema additively → wire OOP through same source → parity test → doctor reporting → benchmark re-run → fix XML doc → docs update.

**Open Questions left for Brady:** alias placement (description tail vs dedicated array), length cap defaults, MamlParaText join style, cache invalidation across runspace recycling, doctor field naming (coordinate w/ spec 006), ValidateSet phrasing, telemetry on fallback frequency.

**Pattern for MCP-author-facing specs:** when the technical baseline comes from a co-author, restate the cited file/line evidence inside Background so readers can verify without leaving the document, and treat path-divergence (in-process vs OOP producing different descriptions for the same command) as a first-class bug, not a quirk.


### 2026-05-12 — Spec 010 Wave 1 PR reviews (#235 / #236 / #237)

**Verdicts (all DRAFT; posted as `gh pr comment`, would-approve on undraft):**

- **PR #235 — Bender — RemoteToolSchema XML doc fix (FR-560):** APPROVE. https://github.com/usepowershell/PoshMcp/pull/235#issuecomment-4435719787
- **PR #236 — Fry — pre-spec010 tools/list snapshots + HelpParityFixture (FR-550 step 1, FR-521 fixture):** APPROVE; KEEP fixture in this PR (not scope creep). https://github.com/usepowershell/PoshMcp/pull/236#issuecomment-4435719899
- **PR #237 — Amy — pre-spec010 cold-start baseline (FR-572 step 1):** APPROVE. https://github.com/usepowershell/PoshMcp/pull/237#issuecomment-4435719995

**Scope-creep call on #236:** KEEP HelpParityFixture in #236. FR-550 step 1 explicitly names the fixture as a required input to the baseline capture (`"at minimum Microsoft.PowerShell.Management and the HelpParityFixture module from FR-521"`). The fixture is a dependency of #224's stated work, not a separable concern. Splitting would force a 3-PR chain (fixture → snapshots → tests) and block snapshot reproducibility. #229 (parity test class ToolDescriptionParityTests.cs) consumes the fixture but does not own it.

**Architectural notes recorded for spec 010 implementation work (non-blocking on Wave 1 but worth carrying forward):**

1. **OOP host coverage gap.** Description-sourcing block exists in both `oop-host.ps1` (Single) and `oop-host-pool.ps1` (Pool). #236 baseline captures only Pool. Spec 010 implementation should consolidate the description-sourcing logic into a shared helper sourced by both host scripts (preferred), OR capture a third snapshot for `SubprocessHostMode = 'Single'`. Recommendation: consolidate during implementation PR.

2. **In-process `Environment.ImportModules` is unwired.** #236 capture script works around this by setting `PSModulePath` on the spawned process. Worth a separate follow-up issue (in-process should honour `Environment.ImportModules` for parity with OOP) — orthogonal to spec 010 but the asymmetry is real and surfaces every time a spec adds a runtime-mode-parameterized test.

3. **Bench gate granularity.** #237 `--job short` (3-10 iterations) is sufficient for the 50% threshold given current noise envelope (~±2% on Mean). If post-spec010 run lands near the threshold (say 30-40% regression), rerun with `--job medium` before final merge of the implementation PR. Compare per-mode Mean; do not gate on P99 with n=3 (degenerate).

4. **XML doc precedence.** When spec 010 implementation lands, `RemoteToolSchema.Description` XML doc (just corrected in #235) will need a second update to describe the new sourced-from-Get-Help-precedence-chain rule. Bender's #235 doc text was accurate for pre-implementation behavior only. Flag for the implementation PR.

**Cubert review:** Per the user directive (2026-05-05 decision), Cubert pre-reviews Farnsworth plans/proposals before they reach the user. These were PR reviews, not plans/proposals, so the directive did not gate this work — Cubert reviewed in parallel with me, focusing on claim-by-claim fact-checking while I focused on architecture and spec alignment.

**EMU pattern (reconfirmed):** `gh pr comment` works only from the `usepowershell` account; `stmuraws_microsoft` is blocked. Switch-account-comment-switch-back remains the working channel. `gh pr review` remains blocked for all paths.

## Learnings (2026-05-12)
- When a baseline-capture PR depends on a fixture module that the spec explicitly names as a required input, the fixture is part of the baseline PR's scope. Do not split fixture-as-dependency from baseline-as-deliverable — it produces a serialized 3-PR chain with the fixture-only middle PR adding zero verifiable value on its own.
- Reproducibility checklist for benchmark-baseline PRs (used to evaluate #237): folder naming follows spec numbering convention; exact CLI captured; BDN version + OS + .NET + JIT + machine specs recorded; gating rule stated in README; ApplicationInsights/telemetry isolation called out. All five are non-negotiable for FR-572-style gates.
- Reproducibility checklist for snapshot-capture PRs (used to evaluate #236): temp config file (repo config not mutated); full JSON-RPC envelope captured (not just `result`); pretty-print + LF endings + UTF-8 no BOM for diff stability; deterministic-by-construction discovery config (`EnableDynamicReloadTools=false`, `EnableConfigurationTroubleshootingTool=false`); manual fallback documented in README; regeneration policy explicit. All six are non-negotiable for FR-550-style baselines.
- Pre-implementation baseline PRs are inherently low-architectural-risk but high-process-risk. The architectural review surface is small (does the deliverable match what the spec gates against?); the process risk is that a sloppy baseline silently allows post-change regressions to slip the gate. The reviewer's job is mostly verifying that the gate is well-defined and reproducible, not critiquing the implementation.

### 2026-05-12 — Reviewed PR #238 (Bender — IToolMetadataSource seam, #225, spec 010 step 3, Option A)

**Verdict:** APPROVE (would-approve comment posted via `gh pr comment`; Steven owns formal approval). Backup at C:\Users\stmuraws\AppData\Local\Temp\farnsworth-pr238-review.md. Terminal output capture intermittent this session — could not visually confirm comment URL but `gh pr comment` returned non-error.

**Seam contract (the architecture decision):**
- `IToolMetadataSource` exposes two methods (`ResolveToolDescription`, `ResolveParameterDescription`) keyed off readonly record struct request types. Result types carry both the resolved string and a `Source` enum.
- `ToolDescriptionSource` enum {Synopsis, Description, Syntax, Name} maps 1:1 with FR-583 string literals; `ParameterDescriptionSource` enum {HelpParameter, HelpMessage, ValidateSet, TypeFallback} same. Enum-to-literal conversion deferred to #228 (doctor) — correct placement.
- `ParameterDescriptionRequest` carries every input the FR-510 chain needs (HelpParameterDescription, HelpMessage, ValidateSetValues, ValidateSetAppliesToArrayElement for the singleton vs array phrasing in step 3, ParameterTypeName for step 4).
- **Caller-side resolution** is the design decision: Get-Help is invoked by `McpToolFactoryV2`/`PowerShellSchemaGenerator` and resolved fields are PASSED INTO the seam as request fields. The seam owns precedence rules; callers own data acquisition. Right separation — keeps OOP wire-format independent (subprocess can resolve and ship pre-resolved fields without the seam knowing about runspaces).

**Verification:**
1. **Both call sites wired:** in-process `SetParameterSetDescription` and OOP `CreateRemoteCommandMetadataMapping` both construct `ToolDescriptionRequest` and call `ResolveToolDescription`. `ResolveParameterDescription` exists but isn't invoked yet (deferred to #226 with `PowerShellSchemaGenerator` wiring) — coherent scope, not a gap.
2. **Behavior preservation:** identical output bytes for realistic inputs in both paths. Two non-blocking subtleties: `DefaultToolMetadataSource` calls `Synopsis.Trim()` (PowerShell's Get-Help output is already trimmed; FR-540 step 1 will mandate trim anyway — strictly closer to spec target); Synopsis suppressed when equal to CommandName (FR-500 step 1 explicitly excludes that case; output bytes identical, only Source tag differs).
3. **DI lifetime:** `TryAddSingleton<IToolMetadataSource, DefaultToolMetadataSource>()` in BOTH `StdioServerHost.ConfigureOpenTelemetry` and `HttpServerHost.ConfigureOpenTelemetryForHttp`. `TryAdd` is the right choice — lets #226/#227 register a replacement before host configuration without conflict. Singleton lifetime correct (impl is documented stateless/thread-safe).
4. **Forward-compat for #226/#227/#228:** all plug in WITHOUT touching the interface. #226 populates Synopsis+LongDescription+HelpParameterDescription on the request records. #227 extends `RemoteToolSchema` with per-parameter help fields and the OOP caller passes them through the same request shape. #228 reads `result.Source` for doctor `descriptionSource` literal and FR-590 metric tag.

**Non-blocking observations:** `McpToolSetupService.CreateToolFactory` accepts metadata source as optional parameter with `null` default — reasonable for seam-only scope, but #226 should switch to constructor-injected (resolved from DI) so the `null` branch can't accidentally bypass a registered replacement. `McpToolFactoryV2` now has six constructors (3 × {with, without metadata source}) — acceptable backward-compat during transition; collapse in follow-up once all callers route through DI.

**Pattern noted (architecture):** When designing a seam intended to absorb a future precedence chain, putting data-acquisition responsibility on the CALLER (not inside the seam) produces a wire-format-independent contract. The seam is pure logic; call sites supply pre-resolved inputs from whatever source they have access to (in-process runspace, subprocess over ndjson, cached helpData). This makes OOP wire-through (#227) trivially additive — extend RemoteToolSchema fields, populate the request, no seam change.

**Pattern noted (process):** Six-constructor proliferation is acceptable during a backward-compat transition. Don't block a seam-only PR over it; file a collapse-after-DI follow-up.

**EMU caveat:** `gh pr review` remains blocked from this account; `gh pr comment` is the working channel. Comment is NOT a formal GitHub approval.


### 2026-05-12 — Spec 010 Wave 3 PR reviews (#240 / #239)

**Verdicts (both posted via gh pr comment; gh pr review remains EMU-blocked):**

- **PR #240 — Bender — eat(metadata): in-process Help-aware precedence + sanitization (#226):** WOULD APPROVE. https://github.com/usepowershell/PoshMcp/pull/240#issuecomment-4436060222
- **PR #239 — Hermes — eat(oop): extend RemoteToolSchema with full description + per-parameter metadata (#227):** WOULD APPROVE. https://github.com/usepowershell/PoshMcp/pull/239#issuecomment-4436060298

**PR #240 architecture findings:**
- DI registration is a clean swap (TryAddSingleton<IToolMetadataSource, HelpAwareToolMetadataSource>) in both StdioServerHost and HttpServerHost. Not a fallback chain.
- Get-Help cost contained per FR-570/FR-571: PowerShellHelpResolver is a per-factory ConcurrentDictionary keyed case-insensitively on command name; Get-Help -Name <cmd> -Full invoked exactly once per unique command per discovery via GetOrAdd. No per-parameter Get-Help calls — parameter help read from the per-command result.
- DescriptionSanitizer is pure-static, comprehensively unit-tested (17 tests covering nulls, control chars, paragraph preservation, word-boundary truncation, ellipsis edges).
- Caller-side data acquisition matches the seam contract from #238: BuildParameterDescriptionMap collects every input the FR-510 chain needs (HelpParameter, HelpMessage first non-empty, ValidateSetValues, ValidateSetAppliesToArrayElement when array-typed).
- FR-500 step 1 auto-generated synopsis filter (synopsis == commandName → null) implemented in BuildFromHelpObject, matches OOP host behavior.
- Sanitize → check non-empty → cap order applied consistently in both ResolveToolDescription and ResolveParameterDescription. Caps match FR-541/FR-542 exactly.
- [Description] attached only to PowerShell-derived parameters; framework params (_AllProperties, _MaxResults, _RequestedProperties) and CancellationToken correctly skipped.

**PR #240 non-blocking observations:**
- Scenario 1 demo deferred to #229 (FR-521 parity test + FR-550 regression test) — coherent with spec sequencing but flag for #229 review.
- In-process baseline snapshot (inprocess-tools-list.json from #236) WILL diverge after this PR. FR-550 regression test in #229 must encode "starts-with baseline + paragraph separator OR equals baseline" rule, not naive equality.
- PowerShellSchemaGenerator.CreateParameterSchema legacy two-arg overload allocates a DefaultToolMetadataSource per call when source not supplied — negligible; collapse after callers migrate.
- OOP path correctly untouched in this PR; OOP wiring is #228.

**PR #239 architecture findings:**
- Strictly additive: Description field preserved verbatim (same name, type, default, semantics); FullDescription and three new per-parameter fields all string? / string[]?. Older payloads deserialize with nulls and consumers fall through precedence per FR-510 contract.
- JSON ↔ C# DTO shape parity verified for both layers (tool: 5 fields; parameter: 7 fields). Both oop-host.ps1 (Single) and oop-host-pool.ps1 (Pool) emit identical shape — consumer needs no per-host-mode branching.
- Defensive PowerShell at every layer: outer Get-Help wrapped in try/catch with -ErrorAction SilentlyContinue (FR-581 — discovery cannot fail because help is missing); Get-RemoteCommandHelpMetadata guards if ( -ne ); inner property reads in nested try/catch; MAML projection handles both string-array and MamlParaText[] shapes; Get-RemoteParameterAttributeMetadata checks every level for null; HelpMessage only set when non-whitespace; ValidateSetValues only emitted when non-empty.
- Closes the spec-010-wave-1 OOP host coverage gap (Single host now updated alongside Pool host).
- No precedence resolution in the host — emits raw source data only; FR-540 sanitization happens on the .NET consumer side per the seam contract from #238. Right separation.

**PR #239 non-blocking observations:**
- Description-sourcing logic duplicated across two host scripts (script-scope helpers in oop-host.ps1 vs nested local functions in oop-host-pool.ps1's Invoke-DiscoverHandler). Intentional and matches the established pool-host pattern from PR #204 / #207. Future consolidation into oop-host-shared.ps1 is a candidate refactor, not blocking.
- oop-tools-list.json baseline (#236) was Pool-only — capturing a Single snapshot in #229 closes the OOP coverage gap I flagged on #236.
- HelpMessage "first non-empty wins" across multiple parameter sets matches FR-511 intent (single value per parameter); XML doc flags the edge case explicitly.
- ValidateSetValues ordering preserved per declaration order (FR-510 step 3 requirement).
- Consumer wiring lands in #228 — confirmed CreateRemoteCommandMetadataMapping unchanged here. After both #240 and #239 land, #228 wires the OOP consumer through IToolMetadataSource and FR-520 byte-parity becomes verifiable.

**XML doc precedence (FR-560) carryforward:** RemoteToolSchema.Description doc was corrected for pre-spec-010 in #235; updated in this PR to point at FullDescription. Will need a third update in #228 once consumers source descriptions through the precedence chain. Flag for #228.

**Pattern noted (architecture):** When a multi-PR feature splits "wire format extension" from "consumer wiring", the wire-format PR's job is to be additive, defensive, and exhaustively null-safe at the source — and to NOT pre-resolve precedence on the producer side. PR #239 nailed this: every new field is nullable, every read is guarded, and the host emits raw data only. The consumer (#228) then owns the precedence chain via the seam from #238. This separation is what makes gh pr review of the wire-format PR boil down to "does the JSON shape match the DTO and is every read defensive" — both verifiable in isolation without staging the consumer.

**Pattern noted (review process):** When reviewing two coordinated PRs against the same spec wave, sequence the architecture review around the seam contract: verify the implementation PR (#240) wires the seam into the path it owns, verify the wire-format PR (#239) doesn't violate the seam by pre-resolving on the wrong side, and explicitly call out the cross-PR coordination point (#228) so the implementation review of the consumer PR knows what to look for. Reduces the "I forgot why we did X this way" risk in wave 4.

**EMU caveat (reconfirmed):** gh pr review blocked on usepowershell/PoshMcp; gh pr comment --body-file <tempfile-outside-repo> is the working channel. Comments do NOT count as formal GitHub Approves for branch protection — Steven owns formal approval.

### 2026-05-13: PR #222 — SwitchParameter MCP round-trip review
- Root cause: SwitchParameter is a struct with getter-only IsPresent; STJ reflection binds to `default(SwitchParameter)` (always `IsPresent=false`). Schema also exposed `{isPresent}` envelope, which most clients reject when the model emits a plain bool.
- Fix shape: `JsonConverter<SwitchParameter>` + `AIJsonSchemaCreateOptions.TransformSchemaNode` rewrite to `anyOf [boolean | {isPresent} | null]`, registered via `McpServerToolCreateOptions.SerializerOptions` and `SchemaCreateOptions` in `CreateMcpToolOptions`.
- Placement is correct: new file under `PoshMcp.Server/PowerShell/` next to `PowerShellParameterUtils.cs` (which already cross-references it). Single chokepoint — clean.
- Watch-out: `SerializerOptions` is now applied to **every** tool, not only those with switch params. `DefaultIgnoreCondition = WhenWritingNull` will silently change response shapes for tools that emit explicit nulls. Cloning the SDK defaults + adding only the converter would be safer; current PR is acceptable but worth a follow-up audit.
- Converter token-cursor caveat: object-valued `isPresent` (e.g. `{"isPresent": {"x":1}}`) hits the switch's `_ => present` default WITHOUT consuming the inner StartObject, so the outer `while (reader.Read())` then descends INTO the nested object. Practically unreachable, but a one-line `reader.Skip()` in the default branch would harden it.
- Test coverage is genuinely strong: 12 converter inlines, schema transform applied to a real `Get-ChildItem -Recurse`, sanity check that `-Path` is untouched, and an e2e probe that defines a PS function with `[switch]` and asserts `.IsPresent` inside the runspace.
- Pattern to remember: SDK reflection-binding gaps for opaque CLR types → small focused converter + `TransformSchemaNode` + register through `CreateMcpToolOptions`. Don't add per-parameter type substitution in `EffectiveParameterType` for these.


## Learnings (2026-05-13)

### PR #222 review — global JsonSerializerOptions blast radius

Reviewed PR #222 at Steven's request. **Verdict: Approve with suggestions** (UI-only — not posted to GitHub).

**Architectural lesson — globals on shared serializer options.** PR #222 set `JsonSerializerOptions.DefaultIgnoreCondition = WhenWritingNull` on what looks like a shared/default options instance. That mutation has the wrong blast radius: every unrelated serialization path that touches the same options object silently changes behavior, and the change isn't visible at the call sites that actually need it. Pattern to apply going forward:

- If null-skip is needed for a specific contract, scope it to that contract's converter (`[JsonIgnore(Condition = ...)]` on the property or a custom converter).
- If null-skip is needed for one call, build a per-call `JsonSerializerOptions` (or clone) instead of mutating a shared one.
- Reserve mutation of shared options to startup-time configuration where the team has audited every consumer.

**Minor items flagged:** two converter/schema cleanups + several nits (recorded in the chat review; not formalized as a decision because no team-level architectural commitment was made beyond the approve-with-suggestions verdict).

**Process note:** Review was performed in chat; verdict was not posted to the PR. If Steven wants this on the PR record, that's a follow-up action — not done in this session.

## Learnings (2026-05-13)

### PR #222 review — global JsonSerializerOptions blast radius

Reviewed PR #222 at Steven's request. **Verdict: Approve with suggestions** (UI-only — not posted to GitHub).

**Architectural lesson — globals on shared serializer options.** PR #222 set `JsonSerializerOptions.DefaultIgnoreCondition = WhenWritingNull` on what looks like a shared/default options instance. That mutation has the wrong blast radius: every unrelated serialization path that touches the same options object silently changes behavior, and the change isn't visible at the call sites that actually need it. Pattern to apply going forward:

- If null-skip is needed for a specific contract, scope it to that contract's converter (`[JsonIgnore(Condition = ...)]` on the property or a custom converter).
- If null-skip is needed for one call, build a per-call `JsonSerializerOptions` (or clone) instead of mutating a shared one.
- Reserve mutation of shared options to startup-time configuration where the team has audited every consumer.

**Minor items flagged:** two converter/schema cleanups + several nits (recorded in the chat review; not formalized as a decision because no team-level architectural commitment was made beyond the approve-with-suggestions verdict).

**Process note:** Review was performed in chat; verdict was not posted to the PR. If Steven wants this on the PR record, that's a follow-up action — not done in this session.
### 2026-05-13: PR #243 review (issue #229, spec 010 wave 5) — APPROVED
- Fry's test trio (HelpParityFixtureSession + ToolDescriptionParityTests + ToolDescriptionRegressionTests + ParameterSetConsistencyTests) is architecturally clean: one shared fixture, three consumers, correct unit-vs-integration split.
- Skip strategy on the 10 `ParameterDescription_IsNonEmpty_*` variants pinned to issue #242 is the right call. Tests-before-fix sequencing with explicit regression gate; failing them would block unrelated CI for an already-triaged finding.
- Issue #242 (FR-510 wiring gap: resolver returns correct strings but they don't reach `inputSchema.properties.<name>.description` JSON) is well-scoped — names the seam, points at the fixture for repro, lists the 10 test methods that un-skip green on fix.
- One non-blocking nit: `ToolDescriptionRegressionTests.IsEqualOrSuperset` uses `Contains(baseline)` as a third acceptance branch, which is broader than FR-550's "equal OR baseline + paragraph separator + additional text". Suggested tightening to `EndsWith(separator + baseline)` or dropping the fallback once confirmed unneeded. Filed as a review nit, not a gate.
- Lesson: when a test PR exposes a real bug, file a separate issue + skip-with-tracking-comment is the right pattern. Don't conflate measurement and remediation in one PR — different domains, different reviewers.

### 2026-05-13: PR #244 review (issue #230, spec 010 step 8) — APPROVE
**Requested by:** Steven (via Ralph)
**Author:** Bender
**Verdict:** APPROVE with one non-blocking follow-up.

**Reviewed:** descriptionSource per command and per parameter in doctor JSON.

**Architectural takeaways:**
- Parallel IToolDescriptionSourceTracker (NOT extending IToolMetadataSource) — right call. Preserves the OOP seam from #228; adding methods to the interface would have rippled into every external implementer. Tracker is additive, optional, full back-compat across all McpToolFactoryV2 constructor overloads (chained through with descriptionSourceTracker: null).
- OOP coverage verified at all four Resolve* sites: SetParameterSetDescription, BuildParameterDescriptionMap (in-proc) + CreateRemoteCommandMetadataMapping, BuildRemoteParameterDescriptionMap (OOP).
- Vocabulary centralized in DescriptionSourceVocabulary.ToWireValue(...) — both JSON converters route exclusively through it. No duplicated switches anywhere.
- CLI doctor now wires `new HelpAwareToolMetadataSource()` via the new DiscoverToolsForCliAsync overload — matches production DI at HttpServerHost.cs:287 and StdioServerHost.cs:148 (both TryAddSingleton<IToolMetadataSource, HelpAwareToolMetadataSource>()).
- Field placement (functionsTools.tools[]) is consistent with spec 006 FunctionsToolsSection.

**Non-blocking follow-up (file as separate issue):**
Runtime doctor (BuildDoctorReportFromConfig — used by McpToolSetupService.cs:411 and ConfigurationReloadTools.cs:198) does NOT receive a tracker. Live-server doctor resource emits empty tools[]. Seam is in place; wiring is small additive change. CLI doctor is the canonical diagnostic surface, so SC-207 is materially served — but full coverage requires runtime path too.

**#231 handoff:** Decision drop is unusually well-prepared. Names canonical APIs (DescriptionSourceVocabulary.ToWireValue, IToolDescriptionSourceTracker), proposes observer-fan-out pattern so OTel attaches at the same call sites without coupling to the doctor tracker. Amy can land FR-590 cleanly.

**#242:** Correct to leave out of scope. Different code path (parameter desc → JSON Schema, not tracker → doctor). Would have muddied the spec-010-step-8 landing.

521 unit tests passing (12 new). All 8 enum values exercised through real HelpAwareToolMetadataSource resolver.

## 2026-05-13 — PR #245 (issue #231, FR-590) — APPROVE

OTel counters for description-source resolution. Amy chose Option B (direct emission in McpToolFactoryV2) over Option A (decorator over IToolDescriptionSourceTracker).

**Verdict: APPROVE.**

Key architectural points:
- Tracker is IToolDescriptionSourceTracker? — null by default, only wired by doctor command path. Decorator over a null-by-default service cannot satisfy FR-590 (emit on every resolution). Option B is structurally correct, not a workaround.
- All four `Resolve*` sites covered (in-proc tool ~L268, in-proc param ~L625, OOP tool ~L671, OOP param ~L750). Counter call sits immediately after each tracker call — same lexical line of sight, drift-resistant.
- Tag values flow exclusively through `DescriptionSourceVocabulary.ToWireValue(...)`. Doctor JSON `descriptionSource` and OTel `step` tag are byte-identical by construction. Zero string duplication.
- Failure isolation: null-check + try/catch swallowing all exceptions including vocabulary's `ArgumentOutOfRangeException` for unknown enums. Test `Counter_emission_swallows_unknown_enum_values` exercises the failure path.
- Performance: cold path (no MeterListener) is near-no-op; warm path is one `KeyValuePair` allocation per resolution. Negligible on discovery cycle.

Future-proofing flag (non-blocking): if someone later wires the tracker in production AND adds a tracker-side decorator for these counters, you'd get 2x counts. Mitigation already baked in: helpers live in McpToolFactoryV2 not in any tracker impl, so wiring the tracker is a no-op for these counters. Optional one-line "do not duplicate in tracker decorators" comment on each helper would be belt-and-suspenders.

OOP flake (`Lifecycle_Start_Ping_Setup_Shutdown_Restart` STATUS_ACCESS_VIOLATION) confirmed unrelated — counter emission is parent-host code with no IPC contribution to OOP child address space, and the failing test is lifecycle/restart not discovery.

MeterListener test pattern is the right shape (filter on MeterName + instrument name, copy tags into owned dict because ReadOnlySpan doesn't outlive the callback, cache MethodInfo for reflection). Sets a clean precedent for future McpMetrics instrument tests.

## 2026-05-13 — PR #245 (issue #231, FR-590) — APPROVE

OTel counters for description-source resolution. Amy chose Option B (direct emission in McpToolFactoryV2) over Option A (decorator over IToolDescriptionSourceTracker).

**Verdict: APPROVE.**

Key architectural points:
- Tracker is IToolDescriptionSourceTracker? — null by default, only wired by doctor command path. Decorator over a null-by-default service cannot satisfy FR-590 (emit on every resolution). Option B is structurally correct, not a workaround.
- All four `Resolve*` sites covered (in-proc tool ~L268, in-proc param ~L625, OOP tool ~L671, OOP param ~L750). Counter call sits immediately after each tracker call — same lexical line of sight, drift-resistant.
- Tag values flow exclusively through `DescriptionSourceVocabulary.ToWireValue(...)`. Doctor JSON `descriptionSource` and OTel `step` tag are byte-identical by construction. Zero string duplication.
- Failure isolation: null-check + try/catch swallowing all exceptions including vocabulary's `ArgumentOutOfRangeException` for unknown enums. Test `Counter_emission_swallows_unknown_enum_values` exercises the failure path.
- Performance: cold path (no MeterListener) is near-no-op; warm path is one `KeyValuePair` allocation per resolution. Negligible on discovery cycle.

Future-proofing flag (non-blocking): if someone later wires the tracker in production AND adds a tracker-side decorator for these counters, you'd get 2x counts. Mitigation already baked in: helpers live in McpToolFactoryV2 not in any tracker impl, so wiring the tracker is a no-op for these counters. Optional one-line "do not duplicate in tracker decorators" comment on each helper would be belt-and-suspenders.

OOP flake (`Lifecycle_Start_Ping_Setup_Shutdown_Restart` STATUS_ACCESS_VIOLATION) confirmed unrelated — counter emission is parent-host code with no IPC contribution to OOP child address space, and the failing test is lifecycle/restart not discovery.

MeterListener test pattern is the right shape (filter on MeterName + instrument name, copy tags into owned dict because ReadOnlySpan doesn't outlive the callback, cache MethodInfo for reflection). Sets a clean precedent for future McpMetrics instrument tests.
- 2026-05-13: PR #246 (Amy — issue #232, FR-572 spec-010 cold-start regression gate). Methodology parity confirmed: same BDN config (InvocationCount=1, MaxIterationCount=10), same filter, same machine, same scenario class. Direct .exe launch in run-6 is functionally equivalent to `dotnet run -c Release`. Toolchain drift minor (SDK 10.0.107 → 10.0.108). Regression ~11–12% Mean across all three modes (Single +11.74% / Pool +12.08% / ProcessPool +10.66%); P95 and P99 track Mean within ±2pp — uniform overhead, not mode-specific. Spec 010 attribution clean: `git log 16878b8..48db59a -- PoshMcp.Server PoshMcp.Benchmarks` returns exactly the 7 spec-010 wave merges (#235, #238, #239, #240, #241, #244, #245), zero unrelated changes. COMPARISON.md is solid template material — keep the paired `run-N` + COMPARISON.md pattern for future regression gates. Verdict: ✅ APPROVE (comment posted; EMU restriction prevents formal review).

## Learnings - 2026-05-13 - PR #247 review (issue #234, spec 010 step 11)

### Verdict
APPROVE. Documentation for FR-500/FR-510 description precedence chains in docs/articles/exposing-tools.md plus README cross-link.

### What I cross-checked and what passed
- **Vocabulary parity**: Doc literals (synopsis/description/syntax/name and helpParameter/helpMessage/validateSet/typeFallback) match IToolDescriptionSourceTracker.cs ToString() lines 152-165 verbatim. This is the property we want — doctor JSON, OTel `step` tag, and prose are all one phrase.
- **Resolver behavior**: HelpAwareToolMetadataSource.cs ResolveToolDescription early-returns at each rung; the doc's "tries each step in order, stops at first non-empty" framing is structurally accurate.
- **Step 3 string format**: Code emits `$"{CommandName} {ParameterSetSyntax}"`. Doc shows `"Get-FixtureBare [[-Anything] <string>] [<CommonParameters>]"` — matches.
- **ValidateSet prefixes**: Code line 111 uses `"Each item is one of: "` vs `"One of: "` based on `ValidateSetAppliesToArrayElement`. Doc strings match verbatim, including punctuation/spacing.
- **TypeFallback**: `$"Parameter of type {typeName}"` matches doc's `"Parameter of type System.String"`.
- **Synopsis-equals-name filter**: The StringComparison.Ordinal guard in the code is correctly described as "not equal to the command name".
- **#242 callout**: Honest framing — what works (resolver + doctor), what doesn't (inputSchema plumbing), tracking link, what authors should do today, what changes when fix ships. NO fix date promised.
- **Cross-link**: README anchor `#description-precedence` resolves to `## Description Precedence` heading in both GitHub and DocFX renderers.
- **HelpParityFixture mapping**: Every fixture function the doc references exists in HelpParityFixture.psm1 with the matching shape.

### Doc review patterns worth keeping
- For a multi-step resolver, **table + tiny per-step example** beats prose. Anchoring step names to the literal wire vocabulary makes the doc dual-purpose: human reading + searchability across doctor output and metrics.
- **In-line "Known issue" callout** under the affected section (not at bottom) for partially-shipped features. Format: what works / what doesn't / link / today's guidance / what changes on fix. The "no module change required" line is what makes the callout safe to ship — authors aren't asked to wait.
- **README defers to article**, doesn't restate the chain. Single-link cross-link from a one-line bullet is the right hook.
- For doc PRs touching behavior I've reviewed code-side, cross-checking literal strings (prefixes, format strings, enum-to-wire names) catches drift faster than re-reading the prose.

### Process note
Worktree-local strategy meant I could `gh pr view` and read implementation files in the same session without cross-branch confusion. Resolved everything from c:\Users\stmuraws\source\github\usepowershell\poshmcp-234.