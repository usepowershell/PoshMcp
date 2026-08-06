# Bender Work History

## Current Summary (compacted 2026-06-01T00:00:00Z)
Detailed prior entries were archived to `history-archive.md` because this file exceeded the 15KB Scribe hard gate. Keep this file focused on active patterns and recent handoff context.

## Learnings
- Spec 012 resource work: static resources win URI collisions with noun-derived resources; OOP noun resources use the executor path while in-process resources use runspace handlers; `ResourceLinkInjector` wraps tools at registration time and appends `EmbeddedResourceBlock` content after successful tool calls.
- OOP execution patterns: `ICommandExecutor.InvokeAsync` returns pre-serialized JSON; constructor guards should enforce exactly one execution backend where applicable; materialize executor leases before branching to avoid double evaluation.
- Doctor/report patterns: provenance seams must thread through CLI and runtime report builders; tracker state belongs to the discovery cycle and should reset at discovery start.
- Auth/OAuth patterns: Entra v2.0 flows require v2.0 issuer/authority, short `scp` scope names, `MapInboundClaims=false`, and proxy endpoints that honor forwarded scheme/host headers.
- Auth diagnostic logging pattern: `PoshMcp.Server/Authentication/AuthClaimDiagnostics.cs` is the allowlist formatter for validated token diagnostics; only audience, scope/scp, roles/role, and issuer values should flow into auth logs, with arbitrary claim names/values excluded. Tests live in `PoshMcp.Tests/Unit/AuthenticationServiceExtensionsTests.cs`.
- OAuth DCR proxy compatibility: Copilot CLI validates `/register` responses as RFC 7591 client metadata; echo requested `redirect_uris` (and related metadata) in the 201 body, with snake_case JSON binding via `JsonPropertyName`.
- OAuth authorize proxy compatibility: normalize `prompt` before redirecting to Entra v2.0; strip unsupported `create`, collapse combined values like `consent select_account` to one supported value preferring `consent`, and preserve supported single values such as `select_account` unchanged.
- Config startup handling: `serve` must include settings resolution and config upgrade inside its error boundary; malformed runtime JSON should surface as `Configuration error` with exit code 2 and include the config path, not as an unhandled `JsonReaderException`.
- Docker/build patterns: embedded Dockerfile templates make `poshmcp build --generate-dockerfile` work after tool install; user-facing generate defaults to the custom/user Dockerfile unless `--type base` is explicit.
- GitHub comments must start with the required agent attribution format from the decision ledger.
- 2026-06-02: Additional release-track test fixes in `ToolDescriptionParity` and `GetChildItem` functional coverage reduced noise, but AppInsights integration port conflicts can still prevent a clean full-suite release gate.
## 2026-08-05 — #389 dual APPROVE_WITH_NITS revision
- Blocking: removed autoclose of #380 (title `perf:`; no Fixes/Closes); rebased onto main post-#388 `ba95fa6`; CTS dispose stays main-owned; fixed gate narrative (warm_call + throughput, not solely warm).
- Farnsworth: fail-closed Variable:/Drive/Function:/Alias: enumeration; stopTimeout docs; remarks refreshed.
- Head `6ad0732`; closingIssuesReferences=[]; PR marked ready; CI green incl. Phase 4 3/3; comments on #389 + #380. Issue 380 remains open.

## 2026-08-05 — table-based reset residual cut (#380)
- Enforced gate post-#389 still RED warm p95 1.44–1.62×; in-proc reset median ~0.77ms (Alias: provider enum ~0.39ms alone).
- Microbench: provider full clean ~0.50ms → SessionStateInternal tables ~0.05ms (~10×); unsafe no-enum ~0.008ms.
- Implemented `SessionStateInternalAccessor` + table hot path in `RunspaceResetProtocol` with provider fallback; cached exclude sets on worker.
- Isolation tests extended (combined pollution, clean Get-Date baseline, accessor availability); pool functional 25/25 + unit Pool 174/174.
- Does not close #380; draft PR for residual progress.

---

### 2026-08-05: PR 391 R2 reject-fix (Cubert autoclose + Farnsworth nits)
- Rebased isolation-equivalent baseline onto main (93d922 / #390).
- Isolation methodology preflight: exit **2**; missing isolation fail-closed (no silent ephemeral default).
- Scrubbed PR title/body so closingIssuesReferences is empty; issue 380 stays open; PR stays draft.
- Learning: GitHub GraphQL autoclose triggers on body phrases like `Close #N` even in procedural steps — never put closing keywords + `#N` in PR prose; verify with `closingIssuesReferences`.
- Learning: methodology failures must use exit 2 consistently end-to-end (workflow preflight + gate script); threshold RED is exit 1 only.

## 2026-08-06 — Milestone 10 cont.: Farnsworth Fix 3 + Fix 4 (activity guard + BeginInvoke)
- Branch: `squad/activity-scope-handles` from origin/main (58ee724)
- **Fix 3 (HasListeners guard):** Guarded `ToolActivitySource.StartActivity("tool.invoke")` with `ToolActivitySource.HasListeners()`. When no OTel listener registered, skips Activity creation AND the unconditional LINQ/string.Join for `paramNames` (was running even when `activity == null`). `if (activity != null)` gates all tag computation.
- **Fix 4 (BeginInvoke + WaitHandle disposal):** Replaced `ps.Invoke()` in `InvokePowerShellSafe` with `BeginInvoke<PSObject,PSObject>(null, output)` + `EndInvoke` + `asyncResult.AsyncWaitHandle?.Dispose()` in `finally`. Removed dead code (`string firstCommand = ...` in unreachable else branch). This is the confirmed handle-floor root cause: per-call `ManualResetEvent` from `LocalPipeline` was only GC'd, not explicitly disposed.
- Tests: 6 new unit tests in `ActivitySourceGuardTests.cs`; full suite 1359/1359 green.
- PR drafted; closingIssuesReferences=[].
- Learning: `ps.Invoke()` wraps `BeginInvoke`/`EndInvoke` but discards the `IAsyncResult` without disposing the `AsyncWaitHandle` → per-call kernel WaitHandle leak. Always use explicit `BeginInvoke`/`EndInvoke` with `asyncResult.AsyncWaitHandle?.Dispose()` in hot paths.
