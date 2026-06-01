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